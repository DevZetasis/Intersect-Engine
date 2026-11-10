using Intersect.Config;
using Intersect.Network.Packets.Server;
using Intersect.Server.Monsters.Calculations;

namespace Intersect.Server.Monsters.Battles;

/// <summary>In-memory practice session. No reference to a tracked database entity or client-supplied statistics.</summary>
public sealed class MonsterBattleSession
{
    private readonly object _gate = new();
    private readonly MonsterBattleRules _rules;
    private readonly IMonsterRandom _random;
    private readonly IMonsterBattleDecisionProvider _opponentProvider;
    private readonly MonsterBattleSide _playerSide, _opponentSide;
    private MonsterBattleCombatant Player => _playerSide.Active[0];
    private MonsterBattleCombatant Opponent => _opponentSide.Active[0];
    private string[] _messages = ["Practice battle started. Select a move."];
    private MonsterBattlePresentationEvent[] _events = [];
    private Guid _previousRevision;
    private readonly string _background;
    public Guid Id { get; } = Guid.NewGuid();
    public Guid OwnerId => _playerSide.ControllerId!.Value;
    public int Turn { get; private set; } = 1;
    public Guid Revision { get; private set; } = Guid.NewGuid();
    public MonsterBattleOutcome Outcome { get; private set; }
    public MonsterBattleSession(MonsterBattleSide player, MonsterBattleSide opponent, MonsterBattleRules rules,
        IMonsterRandom random, IMonsterBattleDecisionProvider? provider = null, string background = "")
    {
        _background = background;
        rules.Validate();
        if (player.ControllerId is not { } owner || owner == Guid.Empty || opponent.ControllerId != null ||
            player.Active.Count != 1 || opponent.Active.Count != 1 || player.Active[0].Id == opponent.Active[0].Id ||
            player.Active[0].LegalSlots().Length == 0 || opponent.Active[0].LegalSlots().Length == 0 ||
            player.Active[0].Calculation.Level > rules.MaxLevel || opponent.Active[0].Calculation.Level > rules.MaxLevel)
            throw new InvalidOperationException("Practice requires an owned monster and opponent with usable damaging moves, one active monster per side.");
        _playerSide = new(player.ControllerId, player.Active.Select(actor => actor.Copy()).ToArray());
        _opponentSide = new(null, opponent.Active.Select(actor => actor.Copy()).ToArray());
        _rules = rules with { }; _random = random; _opponentProvider = provider ?? new RandomPracticeDecisionProvider();
    }
    public void Submit(Guid owner, Guid battle, int turn, Guid revision, int slot, bool forfeit)
    {
        lock (_gate)
        {
            ValidateAction(owner, battle, turn, revision, slot, forfeit);
            if (forfeit) { Finish(MonsterBattleOutcome.Forfeit, "You forfeited the practice battle."); return; }
            if (slot is < 0 or > 3 || Player.Moves[slot]?.Usable != true) throw new InvalidOperationException("This move slot is not usable.");
            try
            {
                // Provider receives public HP/identity and its own legal slots, not the player's selected action.
                var opponentSlot = _opponentProvider.ChooseMove(new(Opponent.LegalSlots(), Opponent.View(), [Player.View()]), _random);
                if (opponentSlot is < 0 or > 3 || Opponent.Moves[opponentSlot]?.Usable != true) throw new InvalidOperationException("Practice provider selected an illegal action.");
                var playerMove = Player.Moves[slot]!;
                var opponentMove = Opponent.Moves[opponentSlot]!;
                var actions = MonsterBattleCalculator.OrderActions([
                    new(Player.Id, playerMove.Definition.Priority, Player.Calculation.Stats.Speed),
                    new(Opponent.Id, opponentMove.Definition.Priority, Opponent.Calculation.Stats.Speed)], _random);
                var messages = new List<string>();
                var events = new List<MonsterBattlePresentationEvent>();
                _previousRevision = Revision;
                foreach (var action in actions)
                {
                    var attacker = action.ActorId == Player.Id ? Player : Opponent;
                    var defender = action.ActorId == Player.Id ? Opponent : Player;
                    var move = action.ActorId == Player.Id ? playerMove : opponentMove;
                    if (attacker.HP == 0 || defender.HP == 0) continue;
                    move.Consume(); // An executed attempt spends PP even when it misses or meets immunity.
                    void Emit(MonsterBattleEventKind kind, string text, long before = 0, long after = 0) => events.Add(new()
                    {
                        Kind = kind, Order = events.Count, ActorId = attacker.Id, TargetId = defender.Id,
                        MoveId = move.Definition.Id, AnimationId = move.Definition.AnimationId, HPBefore = before, HPAfter = after, Text = text,
                    });
                    messages.Add($"{attacker.Name} used {move.Definition.Name}!");
                    Emit(MonsterBattleEventKind.MoveUsed, messages[^1]);
                    if (!MonsterBattleCalculator.Hits(move.Definition, 0, 0, _random)) { messages.Add("The attack missed!"); Emit(MonsterBattleEventKind.MoveMissed, messages[^1]); continue; }
                    var critical = MonsterBattleCalculator.CriticalHit(0, _random);
                    var damage = MonsterBattleCalculator.Damage(attacker.Calculation, defender.Calculation, move.Definition, move.Type,
                        _rules, new(), critical, _random);
                    var appliedDamage = Math.Min(damage, defender.HP);
                    var hpBefore = defender.HP;
                    defender.Damage(damage);
                    Emit(MonsterBattleEventKind.DamageApplied, damage == 0 ? "It had no effect." : $"{defender.Name} lost {appliedDamage} HP.", hpBefore, defender.HP);
                    if (damage == 0) messages.Add("It had no effect.");
                    else
                    {
                        if (critical) { messages.Add("Critical hit!"); Emit(MonsterBattleEventKind.CriticalHit, messages[^1]); }
                        var effectiveness = defender.Calculation.TypeIds.Aggregate(1d, (value, type) => value * move.Type.Effectiveness.GetValueOrDefault(type, 1));
                        if (effectiveness > 1) { messages.Add("It's super effective!"); Emit(MonsterBattleEventKind.TypeEffectiveness, messages[^1]); }
                        else if (effectiveness < 1) { messages.Add("It's not very effective."); Emit(MonsterBattleEventKind.TypeEffectiveness, messages[^1]); }
                        messages.Add($"{defender.Name} lost {appliedDamage} HP.");
                    }
                    if (defender.HP == 0) { messages.Add($"{defender.Name} fainted!"); Emit(MonsterBattleEventKind.MonsterFainted, messages[^1]); }
                }
                Revision = Guid.NewGuid();
                if (Opponent.HP == 0) { Outcome = MonsterBattleOutcome.Victory; messages.Add("Battle won!"); }
                else if (Player.HP == 0) { Outcome = MonsterBattleOutcome.Defeat; messages.Add("Battle lost."); }
                else if (Player.LegalSlots().Length == 0 || Opponent.LegalSlots().Length == 0)
                { Outcome = MonsterBattleOutcome.Draw; messages.Add("Practice ended in a draw: a side has no executable PP remaining."); }
                else if (Turn >= 200) { Outcome = MonsterBattleOutcome.Draw; messages.Add("Practice ended in a draw at the 200-turn safety limit."); }
                else Turn++;
                if (Outcome != MonsterBattleOutcome.Active) events.Add(new() { Kind = MonsterBattleEventKind.BattleFinished, Order = events.Count, Text = messages[^1] });
                _events = events.ToArray();
                _messages = messages.ToArray();
            }
            catch
            {
                Finish(MonsterBattleOutcome.Cancelled, "Practice battle cancelled because turn resolution failed.");
                throw;
            }
        }
    }
    public void ValidateAction(Guid owner, Guid battle, int turn, Guid revision, int slot, bool forfeit)
    {
        lock (_gate)
        {
            if (owner != OwnerId || battle != Id) throw new InvalidOperationException("You do not control this battle.");
            if (Outcome != MonsterBattleOutcome.Active || turn != Turn || revision != Revision) throw new InvalidOperationException("Stale or completed battle action.");
            if (!forfeit && (slot is < 0 or > 3 || Player.Moves[slot]?.Usable != true)) throw new InvalidOperationException("This move slot is not usable.");
        }
    }
    public void Cancel(string reason, bool disconnect = false)
    {
        lock (_gate) if (Outcome == MonsterBattleOutcome.Active) Finish(disconnect ? MonsterBattleOutcome.Forfeit : MonsterBattleOutcome.Cancelled, reason);
    }
    private void Finish(MonsterBattleOutcome result, string message)
    {
        _previousRevision = Revision; Outcome = result; Revision = Guid.NewGuid(); _messages = [message];
        _events = [new() { Kind = MonsterBattleEventKind.BattleFinished, Text = message }];
    }
    public MonsterBattleStatePacket Snapshot(long sequence, string? rejection = null)
    {
        lock (_gate) return new()
        {
            PlayerId = OwnerId, BattleId = Id, Turn = Turn, Revision = Revision, Outcome = Outcome,
            Player = Player.View(), Opponent = Opponent.View(), Sequence = sequence,
            Moves = Enumerable.Range(0, 4).Select(slot => Player.Moves[slot]?.View(slot) ?? new MonsterBattleMoveView { Slot = slot, Name = "Empty slot", UnavailableReason = "Empty slot" }).ToArray(),
            Messages = rejection == null ? _messages.ToArray() : [rejection],
            PreviousRevision = _previousRevision, IsCorrection = rejection != null, Background = _background,
            Events = rejection == null ? _events.Select(item => item with { }).ToArray() : [],
        };
    }
}
