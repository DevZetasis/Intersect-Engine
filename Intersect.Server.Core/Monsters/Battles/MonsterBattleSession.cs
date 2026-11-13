using Intersect.Config;
using Intersect.Framework.Core.GameObjects.Monsters;
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
            throw new InvalidOperationException("Practice requires a healthy active monster and opponent with usable moves.");

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
            var resources = _playerSide.Party.Concat(_opponentSide.Party).Select(actor => (Actor: actor, actor.HP, PP: PP(actor), actor.Stages)).ToArray();
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
                    foreach (var saved in resources)
                    {
                        saved.Actor.RestoreResources(saved.HP, saved.PP);
                        saved.Actor.Stages = saved.Stages;
                    }
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
                            new(Player.Id, playerMove.Definition.Priority, Player.EffectiveSpeed),
                            new(Opponent.Id, opponentMove.Definition.Priority, Opponent.EffectiveSpeed)], _random);
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
        Player.Stages = new();
        _playerSide.Select(target);
        messages.Add($"{Player.Name} entered the battle!");
        events.Add(new() { Kind = MonsterBattleEventKind.SendOut, Order = events.Count, ActorId = Player.Id, TargetId = Player.Id, Incoming = Player.View(), Text = messages[^1] });
    }

    private void Attack(MonsterBattleCombatant attacker, MonsterBattleCombatant defender, MonsterBattleMove move,
        List<string> messages, List<MonsterBattlePresentationEvent> events)
    {
        if (attacker.HP == 0 || defender.HP == 0) return;
        move.Consume(); // An executed attempt spends PP even when it misses or cannot change a value.
        var definition = move.Definition;
        var status = definition.Category == MonsterMoveCategory.Status;
        var targetsOpponent = !status || definition.Effects.Any(effect => effect.Target == MonsterMoveEffectTarget.Opponent);
        var primary = targetsOpponent ? defender : attacker;

        void Emit(MonsterBattleEventKind kind, MonsterBattleCombatant target, string text, long before = 0, long after = 0,
            MonsterStat stat = MonsterStat.None, int stage = 0, bool summarize = true)
        {
            if (summarize) messages.Add(text);
            events.Add(new()
            {
                Kind = kind, Order = events.Count, ActorId = attacker.Id, TargetId = target.Id,
                MoveId = definition.Id, AnimationId = definition.AnimationId, AnimationPosition = definition.AnimationPosition,
                HPBefore = before, HPAfter = after, Stat = stat, StageAfter = stage,
                Text = text,
            });
        }

        Emit(MonsterBattleEventKind.MoveUsed, primary, $"{attacker.Name} used {definition.Name}!");
        if (targetsOpponent && !MonsterBattleCalculator.Hits(definition, 0, 0, _random))
        {
            Emit(MonsterBattleEventKind.MoveMissed, primary, "The attack missed!");
            return;
        }

        if (!status)
        {
            var critical = MonsterBattleCalculator.CriticalHit(0, _random);
            var modifiers = new MonsterBattleModifiers
            {
                AttackStage = attacker.Stage(MonsterStat.Attack),
                DefenseStage = defender.Stage(MonsterStat.Defense),
                SpecialAttackStage = attacker.Stage(MonsterStat.SpecialAttack),
                SpecialDefenseStage = defender.Stage(MonsterStat.SpecialDefense),
            };
            var damage = MonsterBattleCalculator.Damage(attacker.Calculation, defender.Calculation, definition, move.Type,
                _rules, modifiers, critical, _random);
            var before = defender.HP;
            defender.Damage(damage);
            if (damage == 0)
            {
                Emit(MonsterBattleEventKind.DamageApplied, defender, "It had no effect.", before, defender.HP);
                return;
            }
            var damageMessage = $"{defender.Name} lost {before - defender.HP} HP.";
            // Show damage first during playback while preserving the established summary order.
            Emit(MonsterBattleEventKind.DamageApplied, defender, damageMessage, before, defender.HP, summarize: false);
            if (critical) Emit(MonsterBattleEventKind.CriticalHit, defender, "Critical hit!");
            var effectiveness = defender.Calculation.TypeIds.Aggregate(1d, (value, type) => value * move.Type.Effectiveness.GetValueOrDefault(type, 1));
            if (effectiveness > 1) Emit(MonsterBattleEventKind.TypeEffectiveness, defender, "It's super effective!");
            else if (effectiveness < 1) Emit(MonsterBattleEventKind.TypeEffectiveness, defender, "It's not very effective.");
            messages.Add(damageMessage);
            if (defender.HP == 0) Emit(MonsterBattleEventKind.MonsterFainted, defender, $"{defender.Name} fainted!");
        }

        foreach (var effect in definition.Effects)
        {
            var target = effect.Target == MonsterMoveEffectTarget.Self ? attacker : defender;
            if (target.HP == 0)
            {
                Emit(MonsterBattleEventKind.EffectFailed, target, $"{target.Name} has fainted.");
                continue;
            }

            if (effect.Kind == MonsterMoveEffectKind.RestoreHP)
            {
                var before = target.HP;
                // Divide first to avoid overflowing even for a very large maximum HP.
                var amount = effect.HealingMethod == MonsterHealingMethod.Fixed ? effect.Amount :
                    Math.Max(1, target.MaximumHP / 100 * effect.Amount + target.MaximumHP % 100 * effect.Amount / 100);
                var restored = target.RecoverHP(amount);
                if (restored == 0) Emit(MonsterBattleEventKind.EffectFailed, target, $"{target.Name} is already at full HP!");
                else Emit(MonsterBattleEventKind.HPRecovered, target, $"{target.Name} restored {restored} HP!", before, target.HP);
                continue;
            }

            var change = target.ChangeStage(effect.Stat, effect.Amount);
            var stat = effect.Stat switch
            {
                MonsterStat.SpecialAttack => "Special Attack",
                MonsterStat.SpecialDefense => "Special Defense",
                _ => effect.Stat.ToString(),
            };
            if (change == 0)
            {
                var limit = effect.Amount > 0 ? "higher" : "lower";
                Emit(MonsterBattleEventKind.EffectFailed, target, $"{target.Name}'s {stat} won't go any {limit}!");
                continue;
            }
            var direction = change > 0 ? "rose" : "fell";
            var magnitude = Math.Abs(change) switch { 1 => "", 2 => " sharply", _ => " drastically" };
            Emit(MonsterBattleEventKind.StatStageChanged, target, $"{target.Name}'s {stat}{magnitude} {direction}!",
                stat: effect.Stat, stage: target.Stage(effect.Stat));
        }
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
            ClearStages();
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

    private void ClearStages()
    {
        foreach (var actor in _playerSide.Party.Concat(_opponentSide.Party)) actor.Stages = new();
    }

    private void Finish(MonsterBattleOutcome result, string message)
    {
        _previousRevision = Revision;
        Outcome = result;
        ClearStages();
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
