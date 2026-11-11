using Intersect.Network.Packets.Server;

namespace Intersect.Client.Interface.Game.Monsters;

public enum MonsterBattlePresentationPhase { Waiting, CommandSelection, AwaitingServer, PlayingSequence, ShowingResult, Replacement }

/// <summary>UI-clock playback of resolved facts. Never calculates damage, order, PP or opponent choices.</summary>
public sealed class MonsterBattlePlayback
{
    public MonsterBattleStatePacket? State { get; private set; }
    public MonsterBattlePresentationPhase Phase { get; private set; }
    public MonsterBattlePresentationEvent? CurrentEvent => Phase == MonsterBattlePresentationPhase.PlayingSequence ? State?.Events.ElementAtOrDefault(_index) : null;
    public string Text { get; private set; } = string.Empty;
    public MonsterBattleActorView? DisplayedPlayer { get; private set; }
    public double PlayerHP { get; private set; }
    public double OpponentHP { get; private set; }
    public bool PlayerFainted { get; private set; }
    public bool OpponentFainted { get; private set; }
    public double Elapsed => _elapsed;
    public double Progress => Math.Clamp(_elapsed / Duration, 0, 1);
    public Func<MonsterBattlePresentationEvent, double>? EffectDuration { get; set; }
    internal Action<MonsterBattlePresentationEvent>? EventStarted { get; set; }
    internal Action<bool>? EventEnded { get; set; }
    internal Action? Interrupted { get; set; }
    private long _lastSequence;
    private int _index;
    private double _elapsed;
    private double Duration => Math.Clamp((CurrentEvent is { } current ? EffectDuration?.Invoke(current) : null) ?? (CurrentEvent?.Kind switch
    {
        MonsterBattleEventKind.MoveUsed => .35, MonsterBattleEventKind.DamageApplied => .5,
        MonsterBattleEventKind.MonsterFainted => .55, _ => .45,
    }), .1, MonsterBattleAnimation.MaximumSeconds);
    public bool Accept(MonsterBattleStatePacket state)
    {
        if (state.Sequence <= _lastSequence) return false;
        _lastSequence = state.Sequence;
        var previous = State;
        if (previous != null && previous.BattleId == state.BattleId && state.Turn < previous.Turn) return false;
        if (previous?.BattleId == state.BattleId && previous.Revision == state.Revision)
        {
            // Rejections/duplicate revisions correct an awaiting command without replaying a resolved turn.
            if (Phase == MonsterBattlePresentationPhase.AwaitingServer) { State = state; Synchronize(); }
            return false;
        }
        Interrupted?.Invoke();
        State = state; _index = 0; _elapsed = 0;
        var valid = previous != null && ValidEvents(previous, state);
        if (!valid || state.IsCorrection || previous == null || previous.BattleId != state.BattleId || previous.Revision != state.PreviousRevision ||
            state.Outcome is MonsterBattleOutcome.Cancelled or MonsterBattleOutcome.Forfeit)
        { Synchronize(); return true; }
        Phase = MonsterBattlePresentationPhase.PlayingSequence;
        BeginEvent();
        return true;
    }
    private static bool ValidEvents(MonsterBattleStatePacket previous, MonsterBattleStatePacket state)
    {
        if (state.Events.Length is < 1 or > 32 || previous.Opponent.Id != state.Opponent.Id) return false;
        var player = previous.Player;
        for (var index = 0; index < state.Events.Length; index++)
        {
            var entry = state.Events[index];
            if (entry.Order != index || !Enum.IsDefined(entry.Kind)) return false;
            if (entry.Kind == MonsterBattleEventKind.BattleFinished) continue;
            if (entry.Kind == MonsterBattleEventKind.SendOut)
            {
                if (entry.Incoming is not { } incoming || incoming.Id == player.Id || incoming.Id == state.Opponent.Id ||
                    incoming.Id != entry.ActorId || incoming.Id != entry.TargetId ||
                    incoming.HP <= 0 || incoming.HP > incoming.MaximumHP ||
                    !state.Party.Any(member => member.Actor.Id == incoming.Id)) return false;
                player = incoming;
                continue;
            }
            if ((entry.ActorId != player.Id && entry.ActorId != state.Opponent.Id) ||
                (entry.TargetId != player.Id && entry.TargetId != state.Opponent.Id)) return false;
            if (entry.Kind == MonsterBattleEventKind.Withdraw && (entry.ActorId != player.Id || entry.TargetId != player.Id)) return false;
            if (entry.Kind == MonsterBattleEventKind.DamageApplied &&
                (entry.HPAfter < 0 || entry.HPBefore < entry.HPAfter ||
                entry.HPBefore > (entry.TargetId == player.Id ? player.MaximumHP : state.Opponent.MaximumHP))) return false;
        }
        return player.Id == state.Player.Id;
    }

    private void BeginEvent()
    {
        var entry = CurrentEvent!;
        Text = entry.Text;
        EventStarted?.Invoke(entry);
        if (entry.Kind != MonsterBattleEventKind.SendOut || entry.Incoming is not { } incoming) return;
        DisplayedPlayer = incoming;
        PlayerHP = incoming.HP;
        PlayerFainted = false;
    }

    private void Synchronize()
    {
        if (State is not { } state) { Phase = MonsterBattlePresentationPhase.Waiting; return; }
        DisplayedPlayer = state.Player;
        PlayerHP = Math.Clamp(state.Player.HP, 0, Math.Max(0, state.Player.MaximumHP));
        OpponentHP = Math.Clamp(state.Opponent.HP, 0, Math.Max(0, state.Opponent.MaximumHP));
        PlayerFainted = PlayerHP == 0; OpponentFainted = OpponentHP == 0;
        Phase = state.Outcome != MonsterBattleOutcome.Active ? MonsterBattlePresentationPhase.ShowingResult
            : state.Phase == MonsterBattlePhase.Replacement ? MonsterBattlePresentationPhase.Replacement
            : MonsterBattlePresentationPhase.CommandSelection;
        Text = state.Messages.LastOrDefault() ?? string.Empty;
    }
    public bool BeginAwaiting()
    {
        if (Phase is not (MonsterBattlePresentationPhase.CommandSelection or MonsterBattlePresentationPhase.Replacement)) return false;
        Phase = MonsterBattlePresentationPhase.AwaitingServer; return true;
    }
    public void Tick(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        while (Phase == MonsterBattlePresentationPhase.PlayingSequence && seconds > 0)
        {
            var duration = Duration; var step = Math.Min(seconds, Math.Max(0, duration - _elapsed));
            _elapsed += step; seconds -= step;
            if (CurrentEvent is { Kind: MonsterBattleEventKind.DamageApplied } damage)
            {
                var hp = damage.HPBefore + (damage.HPAfter - (double)damage.HPBefore) * Math.Clamp(_elapsed / duration, 0, 1);
                if (damage.TargetId == DisplayedPlayer!.Id) PlayerHP = hp; else OpponentHP = hp;
            }
            if (_elapsed < duration) break;
            FinishEvent(completed: true);
        }
    }
    private void FinishEvent(bool completed)
    {
        if (CurrentEvent is { Kind: MonsterBattleEventKind.MonsterFainted } faint)
        {
            if (faint.TargetId == DisplayedPlayer!.Id) PlayerFainted = true;
            else OpponentFainted = true;
        }
        EventEnded?.Invoke(completed);
        _index++;
        _elapsed = 0;
        if (_index >= State!.Events.Length) Synchronize();
        else BeginEvent();
    }

    public void Advance()
    {
        if (Phase != MonsterBattlePresentationPhase.PlayingSequence) return;
        if (CurrentEvent is { Kind: MonsterBattleEventKind.DamageApplied } damage)
        {
            if (damage.TargetId == DisplayedPlayer!.Id) PlayerHP = damage.HPAfter;
            else OpponentHP = damage.HPAfter;
        }
        FinishEvent(completed: false);
    }
    public void Skip()
    {
        if (Phase != MonsterBattlePresentationPhase.PlayingSequence) return;
        Interrupted?.Invoke();
        Synchronize();
    }
    public void Reset()
    {
        Interrupted?.Invoke();
        State = null; DisplayedPlayer = null; _lastSequence = 0; Phase = MonsterBattlePresentationPhase.Waiting; _index = 0; _elapsed = 0; Text = string.Empty;
        PlayerHP = OpponentHP = 0; PlayerFainted = OpponentFainted = false;
    }
}
