using Intersect.Config;
using Intersect.Network.Packets.Client;
using Intersect.Network.Packets.Server;
using Intersect.Server.Monsters.Calculations;

namespace Intersect.Server.Monsters.Battles;

/// <summary>One active combatant per side, with independent condition for each party member.</summary>
public sealed class MonsterBattleSession
{
    private readonly object _gate = new();
    private readonly MonsterBattleRules _rules;
    private readonly IMonsterRandom _random;
    private readonly IMonsterBattleDecisionProvider _opponentProvider;
    private readonly MonsterBattleSide _playerSide;
    private readonly MonsterBattleSide _opponentSide;
    private MonsterBattleCombatant Player => _playerSide.Active;
    private MonsterBattleCombatant Opponent => _opponentSide.Active;
    private string[] _messages = ["Practice battle started. Select a command."];
    private MonsterBattlePresentationEvent[] _events = [];
    private Guid _previousRevision;
    private readonly string _background;
    private readonly Action<IReadOnlyList<MonsterBattleCombatant>>? _persist;
    private MonsterBattleCombatant[] _changed = [];

    public bool Reserves(Guid monster) => _playerSide.Party.Any(member => member.Id == monster);
    public Guid Id { get; } = Guid.NewGuid();
    public Guid OwnerId => _playerSide.ControllerId!.Value;
    public int Turn { get; private set; } = 1;
    public Guid Revision { get; private set; } = Guid.NewGuid();
    public MonsterBattleOutcome Outcome { get; private set; }
    public MonsterBattlePhase Phase { get; private set; }

    public MonsterBattleSession(MonsterBattleSide player, MonsterBattleSide opponent, MonsterBattleRules rules,
        IMonsterRandom random, IMonsterBattleDecisionProvider? provider = null, string background = "",
        Action<MonsterBattleCombatant>? persist = null, Action<IReadOnlyList<MonsterBattleCombatant>>? persistParty = null)
    {
        rules.Validate();
        var members = player.Party.Concat(opponent.Party).ToArray();
        if (player.ControllerId is not { } owner || owner == Guid.Empty || opponent.ControllerId != null ||
            opponent.Party.Count != 1 || members.Select(member => member.Id).Distinct().Count() != members.Length ||
            members.Any(member => member.Calculation.Level > rules.MaxLevel) ||
            player.Active.HP == 0 || opponent.Active.HP == 0 ||
            player.Active.LegalSlots().Length == 0 || opponent.Active.LegalSlots().Length == 0 ||
            (persist != null && (player.Party.Count != 1 || persistParty != null)))
            throw new InvalidOperationException("Practice requires a healthy active monster and opponent with usable damaging moves.");

        _background = background;
        _persist = persistParty ?? (persist == null ? null : changed => persist(changed.Single()));
        _playerSide = player.Copy();
        _opponentSide = opponent.Copy();
        _rules = rules with { };
        _random = random;
        _opponentProvider = provider ?? new RandomPracticeDecisionProvider();
    }

    internal void PersistResolvedTurn()
    {
        if (_changed.Length > 0) _persist?.Invoke(_changed);
    }

    private static int[] PP(MonsterBattleCombatant actor) => actor.Moves.Select(move => move?.PP ?? 0).ToArray();

    internal Action CaptureCheckpoint()
    {
        lock (_gate)
        {
            var resources = _playerSide.Party.Concat(_opponentSide.Party).Select(actor => (Actor: actor, actor.HP, PP: PP(actor))).ToArray();
            var active = Player.Id;
            var turn = Turn;
            var revision = Revision;
            var previous = _previousRevision;
            var outcome = Outcome;
            var phase = Phase;
            var messages = _messages;
            var events = _events;
            return () =>
            {
                lock (_gate)
                {
                    foreach (var saved in resources) saved.Actor.RestoreResources(saved.HP, saved.PP);
                    _playerSide.Select(active);
                    Turn = turn;
                    Revision = revision;
                    _previousRevision = previous;
                    Outcome = outcome;
                    Phase = phase;
                    _messages = messages;
                    _events = events;
                    _changed = [];
                }
            };
        }
    }

    public void Submit(Guid owner, Guid battle, int turn, Guid revision, int slot, bool forfeit) =>
        Submit(owner, new MonsterBattleActionPacket { BattleId = battle, Turn = turn, Revision = revision, MoveSlot = slot, Forfeit = forfeit });

    public void Submit(Guid owner, MonsterBattleActionPacket action)
    {
        lock (_gate)
        {
            var kind = ValidateAction(owner, action);
            _changed = [];
            if (kind == MonsterBattleActionKind.Forfeit)
            {
                Finish(MonsterBattleOutcome.Forfeit, "You forfeited the practice battle.");
                return;
            }

            var before = _playerSide.Party.ToDictionary(actor => actor.Id, actor => (actor.HP, PP: PP(actor)));
            var messages = new List<string>();
            var events = new List<MonsterBattlePresentationEvent>();
            _previousRevision = Revision;
            Phase = MonsterBattlePhase.Resolving;
            try
            {
                if (kind == MonsterBattleActionKind.Replace)
                {
                    Switch(action.MonsterId, false, messages, events);
                    Phase = MonsterBattlePhase.Commands;
                }
                else
                {
                    // The AI chooses against the pre-action view, without knowing the player's choice.
                    var opponentSlot = _opponentProvider.ChooseMove(new(Opponent.LegalSlots(), Opponent.View(), [Player.View()]), _random);
                    if (opponentSlot is < 0 or > 3 || Opponent.Moves[opponentSlot]?.Usable != true)
                        throw new InvalidOperationException("Practice provider selected an illegal action.");
                    var opponentMove = Opponent.Moves[opponentSlot]!;
                    if (kind == MonsterBattleActionKind.Switch)
                    {
                        Switch(action.MonsterId, true, messages, events);
                        Attack(Opponent, Player, opponentMove, messages, events);
                    }
                    else
                    {
                        var playerMove = Player.Moves[action.MoveSlot]!;
                        var order = MonsterBattleCalculator.OrderActions([
                            new(Player.Id, playerMove.Definition.Priority, Player.Calculation.Stats.Speed),
                            new(Opponent.Id, opponentMove.Definition.Priority, Opponent.Calculation.Stats.Speed)], _random);
                        foreach (var entry in order)
                        {
                            if (entry.ActorId == Player.Id) Attack(Player, Opponent, playerMove, messages, events);
                            else Attack(Opponent, Player, opponentMove, messages, events);
                        }
                    }
                    CompleteTurn(messages, events);
                }
                Revision = Guid.NewGuid();
                _events = events.ToArray();
                _messages = messages.ToArray();
                _changed = _playerSide.Party.Where(actor => actor.HP != before[actor.Id].HP ||
                    !PP(actor).SequenceEqual(before[actor.Id].PP)).ToArray();
            }
            catch
            {
                Finish(MonsterBattleOutcome.Cancelled, "Practice battle cancelled because turn resolution failed.");
                throw;
            }
        }
    }

    private void Switch(Guid target, bool withdraw, List<string> messages, List<MonsterBattlePresentationEvent> events)
    {
        if (withdraw)
        {
            messages.Add($"{Player.Name} withdrew.");
            events.Add(new() { Kind = MonsterBattleEventKind.Withdraw, Order = events.Count, ActorId = Player.Id, TargetId = Player.Id, Text = messages[^1] });
        }
        _playerSide.Select(target);
        messages.Add($"{Player.Name} entered the battle!");
        events.Add(new() { Kind = MonsterBattleEventKind.SendOut, Order = events.Count, ActorId = Player.Id, TargetId = Player.Id, Incoming = Player.View(), Text = messages[^1] });
    }

    private void Attack(MonsterBattleCombatant attacker, MonsterBattleCombatant defender, MonsterBattleMove move,
        List<string> messages, List<MonsterBattlePresentationEvent> events)
    {
        if (attacker.HP == 0 || defender.HP == 0) return;
        move.Consume(); // An executed attempt spends PP even when it misses or meets immunity.
        void Emit(MonsterBattleEventKind kind, string text, long before = 0, long after = 0) => events.Add(new()
        {
            Kind = kind, Order = events.Count, ActorId = attacker.Id, TargetId = defender.Id,
            MoveId = move.Definition.Id, AnimationId = move.Definition.AnimationId, HPBefore = before, HPAfter = after, Text = text,
        });
        messages.Add($"{attacker.Name} used {move.Definition.Name}!");
        Emit(MonsterBattleEventKind.MoveUsed, messages[^1]);
        if (!MonsterBattleCalculator.Hits(move.Definition, 0, 0, _random)) { messages.Add("The attack missed!"); Emit(MonsterBattleEventKind.MoveMissed, messages[^1]); return; }
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

    internal static MonsterBattleOutcome? KnockoutOutcome(MonsterBattleSide player, MonsterBattleSide opponent)
    {
        var playerHasLiving = player.Party.Any(actor => actor.HP > 0);
        var opponentHasLiving = opponent.Party.Any(actor => actor.HP > 0);
        if (playerHasLiving && opponentHasLiving) return null;
        if (playerHasLiving) return MonsterBattleOutcome.Victory;
        return opponentHasLiving ? MonsterBattleOutcome.Defeat : MonsterBattleOutcome.Draw;
    }

    private void CompleteTurn(List<string> messages, List<MonsterBattlePresentationEvent> events)
    {
        if (KnockoutOutcome(_playerSide, _opponentSide) is { } knockout)
        {
            Outcome = knockout;
            messages.Add(knockout switch
            {
                MonsterBattleOutcome.Victory => "Battle won!",
                MonsterBattleOutcome.Defeat => "Battle lost.",
                _ => "Both parties fainted.",
            });
        }
        else if (Opponent.LegalSlots().Length == 0 || !_playerSide.Party.Any(actor => actor.HP > 0 && actor.LegalSlots().Length > 0))
        {
            Outcome = MonsterBattleOutcome.Draw;
            messages.Add("Practice ended in a draw: a side has no executable PP remaining.");
        }
        else if (Turn >= 200)
        {
            Outcome = MonsterBattleOutcome.Draw;
            messages.Add("Practice ended in a draw at the 200-turn safety limit.");
        }

        if (Outcome != MonsterBattleOutcome.Active)
        {
            Phase = MonsterBattlePhase.Completed;
            events.Add(new() { Kind = MonsterBattleEventKind.BattleFinished, Order = events.Count, Text = messages[^1] });
            return;
        }
        Turn++;
        Phase = Player.HP == 0 ? MonsterBattlePhase.Replacement : MonsterBattlePhase.Commands;
        if (Phase == MonsterBattlePhase.Replacement) messages.Add("Choose a replacement monster.");
    }

    public MonsterBattleActionKind ValidateAction(Guid owner, MonsterBattleActionPacket action)
    {
        lock (_gate)
        {
            if (owner != OwnerId || action.BattleId != Id) throw new InvalidOperationException("You do not control this battle.");
            if (Outcome != MonsterBattleOutcome.Active || action.Turn != Turn || action.Revision != Revision ||
                Phase is not (MonsterBattlePhase.Commands or MonsterBattlePhase.Replacement))
                throw new InvalidOperationException("Stale or completed battle action.");

            var kind = action.Forfeit && action.Kind == MonsterBattleActionKind.Move ? MonsterBattleActionKind.Forfeit : action.Kind;
            switch (kind)
            {
                case MonsterBattleActionKind.Forfeit:
                    break;
                case MonsterBattleActionKind.Move when Phase == MonsterBattlePhase.Commands &&
                    action.MoveSlot is >= 0 and <= 3 && Player.Moves[action.MoveSlot]?.Usable == true:
                    break;
                case MonsterBattleActionKind.Switch when Phase == MonsterBattlePhase.Commands:
                case MonsterBattleActionKind.Replace when Phase == MonsterBattlePhase.Replacement:
                    if (!_playerSide.Party.Any(actor => actor.Id == action.MonsterId && actor.Id != Player.Id && actor.HP > 0))
                        throw new InvalidOperationException("Choose a healthy inactive member of your battle party.");
                    break;
                default:
                    throw new InvalidOperationException("This command is not available in the current battle phase.");
            }
            return kind;
        }
    }

    public void Cancel(string reason, bool disconnect = false)
    {
        lock (_gate) if (Outcome == MonsterBattleOutcome.Active) Finish(disconnect ? MonsterBattleOutcome.Forfeit : MonsterBattleOutcome.Cancelled, reason);
    }

    private void Finish(MonsterBattleOutcome result, string message)
    {
        _previousRevision = Revision;
        Outcome = result;
        Phase = MonsterBattlePhase.Ended;
        Revision = Guid.NewGuid();
        _messages = [message];
        _events = [new() { Kind = MonsterBattleEventKind.BattleFinished, Text = message }];
    }

    public MonsterBattleStatePacket Snapshot(long sequence, string? rejection = null)
    {
        lock (_gate) return new()
        {
            PlayerId = OwnerId, BattleId = Id, Turn = Turn, Revision = Revision, Outcome = Outcome, Phase = Phase,
            Player = Player.View(), Opponent = Opponent.View(), Sequence = sequence,
            Party = _playerSide.Party.Select((actor, index) => new MonsterBattlePartyMember
                { Slot = actor.PartySlot < 0 ? index : actor.PartySlot, Actor = actor.View() }).ToArray(),
            Moves = Enumerable.Range(0, 4).Select(slot => Player.Moves[slot]?.View(slot) ?? new MonsterBattleMoveView { Slot = slot, Name = "Empty slot", UnavailableReason = "Empty slot" }).ToArray(),
            Messages = rejection == null ? _messages.ToArray() : [rejection],
            PreviousRevision = _previousRevision, IsCorrection = rejection != null, Background = _background,
            Events = rejection == null ? _events.Select(item => item with { }).ToArray() : [],
        };
    }
}
