using Intersect.Network.Packets.Server;

namespace Intersect.Client.Interface.Game.Monsters;

public enum MonsterBattlePresentationPhase { Waiting, CommandSelection, AwaitingServer, PlayingSequence, ShowingResult }

/// <summary>UI-clock playback of resolved facts. Never calculates damage, order, PP or opponent choices.</summary>
public sealed class MonsterBattlePlayback
{
    public MonsterBattleStatePacket? State { get; private set; }
    public MonsterBattlePresentationPhase Phase { get; private set; }
    public MonsterBattlePresentationEvent? CurrentEvent => Phase == MonsterBattlePresentationPhase.PlayingSequence ? State?.Events.ElementAtOrDefault(_index) : null;
    public string Text { get; private set; } = string.Empty;
    public double PlayerHP { get; private set; }
    public double OpponentHP { get; private set; }
    public bool PlayerFainted { get; private set; }
    public bool OpponentFainted { get; private set; }
    public double Elapsed => _elapsed;
    public double Progress => Math.Clamp(_elapsed / Duration, 0, 1);
    public Func<MonsterBattlePresentationEvent, double>? EffectDuration { get; set; }
    private long _lastSequence;
    private int _index;
    private double _elapsed;
    private double Duration => Math.Clamp((CurrentEvent is { } current ? EffectDuration?.Invoke(current) : null) ?? (CurrentEvent?.Kind switch
    {
        MonsterBattleEventKind.MoveUsed => .35, MonsterBattleEventKind.DamageApplied => .5,
        MonsterBattleEventKind.MonsterFainted => .55, _ => .45,
    }), .1, 2);
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
        State = state; _index = 0; _elapsed = 0;
        var knownActors = new[] { state.Player.Id, state.Opponent.Id };
        var valid = state.Events.Length is > 0 and <= 32 && state.Events.Select((e, i) => e.Order == i && Enum.IsDefined(e.Kind) &&
            (e.Kind == MonsterBattleEventKind.BattleFinished || knownActors.Contains(e.ActorId) && knownActors.Contains(e.TargetId)) &&
            (e.Kind != MonsterBattleEventKind.DamageApplied || e.HPAfter >= 0 && e.HPBefore >= e.HPAfter &&
                e.HPBefore <= (e.TargetId == state.Player.Id ? state.Player.MaximumHP : state.Opponent.MaximumHP))).All(ok => ok);
        if (!valid || state.IsCorrection || previous == null || previous.BattleId != state.BattleId || previous.Revision != state.PreviousRevision ||
            state.Outcome is MonsterBattleOutcome.Cancelled or MonsterBattleOutcome.Forfeit)
        { Synchronize(); return true; }
        Phase = MonsterBattlePresentationPhase.PlayingSequence;
        Text = CurrentEvent!.Text;
        return true;
    }
    private void Synchronize()
    {
        if (State is not { } state) { Phase = MonsterBattlePresentationPhase.Waiting; return; }
        PlayerHP = Math.Clamp(state.Player.HP, 0, Math.Max(0, state.Player.MaximumHP));
        OpponentHP = Math.Clamp(state.Opponent.HP, 0, Math.Max(0, state.Opponent.MaximumHP));
        PlayerFainted = PlayerHP == 0; OpponentFainted = OpponentHP == 0;
        Phase = state.Outcome == MonsterBattleOutcome.Active ? MonsterBattlePresentationPhase.CommandSelection : MonsterBattlePresentationPhase.ShowingResult;
        Text = state.Messages.LastOrDefault() ?? string.Empty;
    }
    public bool BeginAwaiting()
    {
        if (Phase != MonsterBattlePresentationPhase.CommandSelection) return false;
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
                if (damage.TargetId == State!.Player.Id) PlayerHP = hp; else OpponentHP = hp;
            }
            if (_elapsed < duration) break;
            if (CurrentEvent is { Kind: MonsterBattleEventKind.MonsterFainted } faint)
            { if (faint.TargetId == State!.Player.Id) PlayerFainted = true; else OpponentFainted = true; }
            _index++; _elapsed = 0;
            if (_index >= State!.Events.Length) Synchronize();
            else Text = CurrentEvent!.Text;
        }
    }
    public void Advance() { if (Phase == MonsterBattlePresentationPhase.PlayingSequence) Tick(Duration - _elapsed + .00001); }
    public void Skip() { if (Phase == MonsterBattlePresentationPhase.PlayingSequence) Synchronize(); }
    public void Reset()
    {
        State = null; _lastSequence = 0; Phase = MonsterBattlePresentationPhase.Waiting; _index = 0; _elapsed = 0; Text = string.Empty;
        PlayerHP = OpponentHP = 0; PlayerFainted = OpponentFainted = false;
    }
}
