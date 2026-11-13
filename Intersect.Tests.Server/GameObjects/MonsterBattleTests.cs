using Intersect.Config;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Network.Packets.Client;
using Intersect.Network.Packets.Server;
using Intersect.Server.Monsters;
using Intersect.Server.Monsters.Battles;
using Intersect.Server.Monsters.Calculations;
using NUnit.Framework;

namespace Intersect.Tests.Server.GameObjects;

[TestFixture, NonParallelizable]
public class MonsterBattleTests
{
    private sealed class LastRandom : IMonsterRandom { public long NextInt64(long bound) => bound - 1; }
    private sealed class ZeroRandom : IMonsterRandom { public long NextInt64(long bound) => 0; }
    private sealed class BrokenRandom : IMonsterRandom { public long NextInt64(long bound) => bound; }
    private static readonly Guid Element = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static MonsterMoveDescriptor Move(MonsterMoveCategory category = MonsterMoveCategory.Physical, int pp = 20, int priority = 0) =>
        new(Guid.NewGuid()) { Name = "Test hit", TypeId = Element, BasePower = 40, Category = category, AlwaysHits = true, MaximumPP = pp, Priority = priority };
    private static MonsterBattleCombatant Actor(MonsterMoveDescriptor move, long hp = 200, long speed = 20, MonsterTypeDescriptor? type = null, Guid[]? defendingTypes = null) =>
        new(new(Guid.NewGuid(), 50, new(hp, 50, 50, 100, 50, speed), defendingTypes ?? [Element]), "Test monster", "",
            [new(move, type ?? new MonsterTypeDescriptor(Element)), null, null, null]);
    private static MonsterBattleSession Session(Guid owner, MonsterBattleCombatant player, MonsterBattleCombatant opponent, IMonsterRandom? random = null) =>
        new(new(owner, [player]), new(null, [opponent]), MonsterBattleRules.Generation4, random ?? new LastRandom());
    private static MonsterBattleActionPacket Action(MonsterBattleSession session, int slot = 0) => new()
    { BattleId = session.Id, Turn = session.Turn, Revision = session.Revision, MoveSlot = slot };
    private static void Play(MonsterBattleSession session, int slot = 0) => session.Submit(session.OwnerId, session.Id, session.Turn, session.Revision, slot, false);

    [Test]
    public void ConsoleCommandsRequireExplicitOnlineCharacterMonsterOpponentAndLevel()
    {
        var parser = new Intersect.Server.Core.CommandParsing.CommandParser();
        parser.Register<Intersect.Server.Core.Commands.StartMonsterBattleCommand>();
        parser.Register<Intersect.Server.Core.Commands.CancelMonsterBattleCommand>();
        Assert.That(parser.Parse($"startmonsterbattle \"Practice Trainer\" {Guid.NewGuid()} {Guid.NewGuid()} 10").Missing, Is.Empty);
        Assert.That(parser.Parse("startmonsterbattle").Missing.Count, Is.EqualTo(4));
        Assert.That(parser.Parse($"cancelmonsterbattle {Guid.NewGuid()}").Missing, Is.Empty);
    }

    [Test]
    public void LearnsetSelectionUsesLatestFourDistinctEligibleMovesAndStableTieOrdering()
    {
        var ids = Enumerable.Range(1, 8).Select(i => new Guid(i, 0, 0, new byte[8])).ToArray();
        var species = new MonsterSpeciesDescriptor(Guid.NewGuid()) { Learnset = ids.Select((id, i) => new MonsterLearnsetEntry { MoveId = id, Level = i + 1 }).ToList() };
        species.Learnset.Add(new() { MoveId = ids[4], Level = 5 });
        Assert.That(MonsterMoveAssignment.Initial(species, 6, _ => true), Is.EqualTo(new Guid?[] { ids[2], ids[3], ids[4], ids[5] }));
        Assert.That(MonsterMoveAssignment.Initial(species, 1, _ => true), Is.EqualTo(new Guid?[] { ids[0], null, null, null }));
        Assert.That(MonsterMoveAssignment.Initial(species, 6, _ => false), Is.EqualTo(new Guid?[4]));
        species.Learnset = ids.Reverse().Select(id => new MonsterLearnsetEntry { MoveId = id, Level = 1 }).ToList();
        Assert.That(MonsterMoveAssignment.Initial(species, 1, _ => true), Is.EqualTo(ids.Take(4).Cast<Guid?>().ToArray()));
        Assert.Throws<InvalidOperationException>(() => MonsterMoveAssignment.Validate([ids[0], ids[0], null, null]));
        Assert.Throws<InvalidOperationException>(() => MonsterMoveAssignment.Validate([ids[0]]));
    }
    [Test]
    public void PpDefinitionDefaultsAndLegacyReloadAreValidated()
    {
        var move = Move(); Assert.That(move.MaximumPP, Is.EqualTo(20));
        move.MaximumPP = 0; Assert.That(move.IsValid, Is.False);
        move.MaximumPP = 1000; Assert.That(move.IsValid, Is.False);
        move.MaximumPP = 999; Assert.That(move.IsValid, Is.True);
        move.Load("{\"Name\":\"Old move\"}"); Assert.That(move.MaximumPP, Is.EqualTo(20));
    }
    [Test]
    public void PhysicalSpecialAndCriticalUseReferenceDamageAndClampHp()
    {
        var owner = Guid.NewGuid();
        var physical = Session(owner, Actor(Move()), Actor(Move(), speed: 10)); Play(physical);
        Assert.That(physical.Snapshot(1).Opponent.HP, Is.EqualTo(172)); // 19 core, then STAB floor(19*1.5)=28.
        var special = Session(owner, Actor(Move(MonsterMoveCategory.Special)), Actor(Move(), speed: 10)); Play(special);
        Assert.That(special.Snapshot(1).Opponent.HP, Is.EqualTo(145));
        var critical = Session(owner, Actor(Move()), Actor(Move(), speed: 10), new ZeroRandom()); Play(critical);
        Assert.That(critical.Snapshot(1).Opponent.HP, Is.EqualTo(152)); // floor(19*2*85/100)*1.5=48.
        Assert.That(critical.Snapshot(1).Messages, Does.Contain("Critical hit!"));
        var ko = Session(owner, Actor(Move()), Actor(Move(), hp: 1, speed: 10)); Play(ko);
        Assert.That(ko.Snapshot(1).Opponent.HP, Is.Zero);
        Assert.That(ko.Snapshot(1).Opponent.HP, Is.LessThanOrEqualTo(ko.Snapshot(1).Opponent.MaximumHP));
        Assert.That(ko.Snapshot(1).Messages, Does.Contain("Test monster lost 1 HP."));
    }
    [Test]
    public void MissesSpendPpWithoutDamagingAndStatusOrOtherTargetsCannotBeSelected()
    {
        var move = Move(); move.AlwaysHits = false; move.Accuracy = 1;
        var session = Session(Guid.NewGuid(), Actor(move), Actor(Move(), speed: 10)); Play(session);
        Assert.That(session.Snapshot(1).Opponent.HP, Is.EqualTo(200));
        Assert.That(session.Snapshot(1).Moves[0].PP, Is.EqualTo(19));
        Assert.That(session.Snapshot(1).Messages, Does.Contain("The attack missed!"));
        Assert.Throws<InvalidOperationException>(() => Session(Guid.NewGuid(), Actor(Move(MonsterMoveCategory.Status)), Actor(Move())));
        var multi = Move(); multi.Target = MonsterMoveTarget.AllOpponents;
        Assert.Throws<InvalidOperationException>(() => Session(Guid.NewGuid(), Actor(multi), Actor(Move())));
    }
    [Test]
    public void TypeEffectivenessImmunityDualTypesAndDamageBoundariesFlowThroughSession()
    {
        var defender = Guid.NewGuid(); var second = Guid.NewGuid();
        foreach (var (factor, expected) in new[] { (0d, 0L), (.5, 14L), (1d, 28L), (2d, 56L) })
        {
            var type = new MonsterTypeDescriptor(Element); type.Effectiveness[defender] = factor;
            var session = Session(Guid.NewGuid(), Actor(Move(), type: type), Actor(Move(), speed: 10, defendingTypes: [defender])); Play(session);
            Assert.That(200 - session.Snapshot(1).Opponent.HP, Is.EqualTo(expected));
        }
        var dual = new MonsterTypeDescriptor(Element); dual.Effectiveness[defender] = .5; dual.Effectiveness[second] = 2;
        var combined = Session(Guid.NewGuid(), Actor(Move(), type: dual), Actor(Move(), speed: 10, defendingTypes: [defender, second])); Play(combined);
        Assert.That(combined.Snapshot(1).Opponent.HP, Is.EqualTo(172));
        var low = Session(Guid.NewGuid(), Actor(Move()), Actor(Move(), speed: 10), new ZeroRandom()); Play(low);
        Assert.That(low.Snapshot(1).Opponent.HP, Is.InRange(0, 200));
        var variance = Session(Guid.NewGuid(), Actor(Move()), Actor(Move(), speed: 10), new ControlledMonsterRandom(0, 15, 0, 15, 0)); Play(variance);
        Assert.That(variance.Snapshot(1).Opponent.HP, Is.EqualTo(176));
        var noStab = Session(Guid.NewGuid(), Actor(Move(), defendingTypes: [Guid.NewGuid()]), Actor(Move(), speed: 10)); Play(noStab);
        Assert.That(noStab.Snapshot(1).Opponent.HP, Is.EqualTo(181));
    }
    [Test]
    public void PrioritySpeedAndTieDecisionsPreventFaintedActorsFromActing()
    {
        var priority = Session(Guid.NewGuid(), Actor(Move(priority: 2), hp: 10, speed: 1), Actor(Move(), hp: 10, speed: 100)); Play(priority);
        Assert.That(priority.Outcome, Is.EqualTo(MonsterBattleOutcome.Victory));
        Assert.That(priority.Snapshot(1).Moves[0].PP, Is.EqualTo(19));
        Assert.That(priority.Snapshot(1).Player.HP, Is.EqualTo(10));
        var speed = Session(Guid.NewGuid(), Actor(Move(), hp: 10, speed: 1), Actor(Move(), hp: 10, speed: 100)); Play(speed);
        Assert.That(speed.Outcome, Is.EqualTo(MonsterBattleOutcome.Defeat));
        Assert.That(speed.Snapshot(1).Moves[0].PP, Is.EqualTo(20));
        var tiePlayer = Session(Guid.NewGuid(), Actor(Move(), hp: 10), Actor(Move(), hp: 10)); Play(tiePlayer);
        Assert.That(tiePlayer.Outcome, Is.EqualTo(MonsterBattleOutcome.Victory));
        var tieOpponent = Session(Guid.NewGuid(), Actor(Move(), hp: 10), Actor(Move(), hp: 10), new ZeroRandom()); Play(tieOpponent);
        Assert.That(tieOpponent.Outcome, Is.EqualTo(MonsterBattleOutcome.Defeat));
    }
    [Test]
    public void ExhaustionEndsDrawAndRejectsFurtherActions()
    {
        var session = Session(Guid.NewGuid(), Actor(Move(pp: 1), hp: 1000), Actor(Move(pp: 1), hp: 1000, speed: 10)); Play(session);
        Assert.That(session.Outcome, Is.EqualTo(MonsterBattleOutcome.Draw));
        Assert.That(session.Snapshot(1).Moves[0].PP, Is.Zero);
        Assert.Throws<InvalidOperationException>(() => Play(session));
    }
    [Test]
    public void InvalidForeignDuplicateAndStaleActionsDoNotResolveTwice()
    {
        var session = Session(Guid.NewGuid(), Actor(Move()), Actor(Move(), speed: 10));
        var action = Action(session);
        Assert.Throws<InvalidOperationException>(() => session.Submit(Guid.NewGuid(), session.Id, session.Turn, session.Revision, 0, false));
        Assert.Throws<InvalidOperationException>(() => Play(session, 1));
        Assert.Throws<InvalidOperationException>(() => Play(session, 4));
        Assert.That(session.Snapshot(1).Moves[0].PP, Is.EqualTo(20));
        Play(session);
        Assert.Throws<InvalidOperationException>(() => session.Submit(session.OwnerId, action.BattleId, action.Turn, action.Revision, action.MoveSlot, false));
        Assert.That(session.Snapshot(1).Moves[0].PP, Is.EqualTo(19));
    }
    [Test]
    public void DefinitionsCombatantsAndOtherSessionsRemainIsolated()
    {
        var move = Move(); var type = new MonsterTypeDescriptor(Element);
        var actor = Actor(move, type: type); var opponent = Actor(Move(), speed: 10);
        var one = Session(Guid.NewGuid(), actor, opponent); var two = Session(Guid.NewGuid(), actor, opponent);
        move.BasePower = int.MaxValue; type.Effectiveness[Element] = 0;
        actor.Damage(150); Play(one);
        Assert.That(one.Snapshot(1).Opponent.HP, Is.EqualTo(172));
        Assert.That(two.Snapshot(1).Player.HP, Is.EqualTo(200));
        Assert.That(two.Snapshot(1).Moves[0].PP, Is.EqualTo(20));
    }
    [Test]
    public void RegistryLifecyclePrivacyAndDuplicateStartAreValidated()
    {
        var published = new List<(Guid, MonsterBattleStatePacket)>(); var registry = new MonsterBattleRegistry((owner, state) => published.Add((owner, state)));
        var owner = Guid.NewGuid(); var session = Session(owner, Actor(Move()), Actor(Move(), speed: 10));
        registry.Start(owner, () => session);
        Assert.Throws<InvalidOperationException>(() => registry.Start(owner, () => session));
        Assert.Throws<InvalidOperationException>(() => registry.Submit(Guid.NewGuid(), Action(session)));
        Assert.That(published.Count, Is.EqualTo(1));
        registry.Submit(owner, Action(session));
        Assert.That(published.All(item => item.Item1 == owner && item.Item2.PlayerId == owner), Is.True);
        var bytes = MessagePack.MessagePackSerializer.Serialize(published.Last().Item2);
        var packet = MessagePack.MessagePackSerializer.Deserialize<MonsterBattleStatePacket>(bytes);
        Assert.That(packet.Opponent.HP, Is.EqualTo(172));
        var packetTypes = new Intersect.Network.PacketTypeRegistry(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, typeof(Intersect.Network.IntersectPacket).Assembly);
        Assert.That(packetTypes.TryRegisterBuiltIn(), Is.True);
        Intersect.Network.PackedIntersectPacket.AddKnownTypes(packetTypes.Types);
        Assert.That(packetTypes.Types.Any(type => type.Name.Contains("StartMonsterBattle")), Is.False);
        var wireState = Intersect.Network.MessagePacker.Instance.Deserialize<MonsterBattleStatePacket>(Intersect.Network.MessagePacker.Instance.Serialize(packet))!;
        Assert.That(wireState.PlayerId, Is.EqualTo(owner));
        Assert.That(wireState.Moves[0].PP, Is.EqualTo(19));
        var wireAction = Intersect.Network.MessagePacker.Instance.Deserialize<MonsterBattleActionPacket>(Intersect.Network.MessagePacker.Instance.Serialize(Action(session)))!;
        Assert.That(wireAction.Revision, Is.EqualTo(session.Revision));
        registry.Cancel(owner, "Disconnected", true); Assert.That(registry.HasActive(owner), Is.False);
        Assert.That(published.Last().Item2.Outcome, Is.EqualTo(MonsterBattleOutcome.Forfeit));
        registry.Start(owner, () => Session(owner, Actor(Move()), Actor(Move(), speed: 10)));
        registry.CancelAll(); Assert.That(registry.HasActive(owner), Is.False);
    }
    [Test]
    public void ProviderFailureCancelsAndReleasesTheOwner()
    {
        var owner = Guid.NewGuid(); var registry = new MonsterBattleRegistry((_, _) => { });
        var session = Session(owner, Actor(Move()), Actor(Move(), speed: 10), new BrokenRandom());
        registry.Start(owner, () => session);
        Assert.Throws<InvalidOperationException>(() => registry.Submit(owner, Action(session)));
        Assert.That(registry.HasActive(owner), Is.False);
        Assert.That(session.Outcome, Is.EqualTo(MonsterBattleOutcome.Cancelled));
    }
    [Test]
    public void FailedPublicationDoesNotLeaveAnActiveReservation()
    {
        var owner = Guid.NewGuid(); var reported = false;
        var registry = new MonsterBattleRegistry((_, _) => throw new IOException("Test connection failed"), _ => reported = true);
        Assert.Throws<InvalidOperationException>(() => registry.Start(owner, () => Session(owner, Actor(Move()), Actor(Move(), speed: 10))));
        Assert.That(registry.HasActive(owner), Is.False);
        Assert.That(reported, Is.True);
    }
    [Test]
    public void MutualImmunityHasFiniteTurnLimitAndConcurrentRetryResolvesOnlyOnce()
    {
        var type = new MonsterTypeDescriptor(Element); type.Effectiveness[Element] = 0;
        var session = Session(Guid.NewGuid(), Actor(Move(pp: 999), type: type), Actor(Move(pp: 999), speed: 10, type: type));
        for (var i = 0; i < 200; i++) Play(session);
        Assert.That(session.Outcome, Is.EqualTo(MonsterBattleOutcome.Draw));
        Assert.That(session.Snapshot(1).Player.HP, Is.EqualTo(200));
        var owner = Guid.NewGuid(); var registry = new MonsterBattleRegistry((_, _) => { });
        var concurrent = Session(owner, Actor(Move()), Actor(Move(), speed: 10)); registry.Start(owner, () => concurrent);
        var action = Action(concurrent); var accepted = 0;
        Parallel.For(0, 2, _ => { try { registry.Submit(owner, action); Interlocked.Increment(ref accepted); } catch (InvalidOperationException) { } });
        Assert.That(accepted, Is.EqualTo(1));
        Assert.That(concurrent.Snapshot(1).Moves[0].PP, Is.EqualTo(19));
        registry.Cancel(owner, "Test cleanup");
    }

    [Test]
    public void TypedPresentationFollowsSpeedAndCarriesAuthoritativeHpAndRevision()
    {
        var session = Session(Guid.NewGuid(), Actor(Move(), speed: 10), Actor(Move(), speed: 100));
        var initial = session.Snapshot(1); Assert.That(initial.Events, Is.Empty); Play(session);
        var resolved = session.Snapshot(2);
        Assert.That(resolved.PreviousRevision, Is.EqualTo(initial.Revision)); Assert.That(resolved.Revision, Is.Not.EqualTo(initial.Revision));
        Assert.That(resolved.Events.First(e => e.Kind == MonsterBattleEventKind.MoveUsed).ActorId, Is.EqualTo(initial.Opponent.Id));
        Assert.That(resolved.Events.Select(e => e.Order), Is.EqualTo(Enumerable.Range(0, resolved.Events.Length)));
        var losses = resolved.Events.Where(e => e.Kind == MonsterBattleEventKind.DamageApplied).ToArray();
        Assert.That(losses.Length, Is.EqualTo(2)); Assert.That(losses[0].TargetId, Is.EqualTo(initial.Player.Id));
        Assert.That(losses[0].HPBefore, Is.EqualTo(initial.Player.HP)); Assert.That(losses[0].HPAfter, Is.EqualTo(resolved.Player.HP));
        Assert.That(losses[1].HPAfter, Is.EqualTo(resolved.Opponent.HP));
        Assert.That(losses.All(e => e.MoveId != Guid.Empty), Is.True);
        var rejected = session.Snapshot(3, "Stale action"); Assert.That(rejected.IsCorrection, Is.True); Assert.That(rejected.Events, Is.Empty);
    }
    [Test]
    public void TypedPriorityPrecedesSpeedAndKnockoutOmitsTheFaintedAction()
    {
        var attack = Move(priority: 1); attack.AnimationId = Guid.NewGuid();
        var session = Session(Guid.NewGuid(), Actor(attack, speed: 1), Actor(Move(), hp: 1, speed: 100));
        var initial = session.Snapshot(1); Play(session); var result = session.Snapshot(2);
        var actions = result.Events.Where(e => e.Kind == MonsterBattleEventKind.MoveUsed).ToArray();
        Assert.That(actions.Length, Is.EqualTo(1)); Assert.That(actions[0].ActorId, Is.EqualTo(initial.Player.Id));
        Assert.That(actions[0].AnimationId, Is.EqualTo(attack.AnimationId));
        Assert.That(result.Events.Single(e => e.Kind == MonsterBattleEventKind.DamageApplied).HPAfter, Is.Zero);
        Assert.That(Array.IndexOf(result.Events, result.Events.Single(e => e.Kind == MonsterBattleEventKind.MonsterFainted)), Is.LessThan(result.Events.Length - 1));
        Assert.That(result.Events.Last().Kind, Is.EqualTo(MonsterBattleEventKind.BattleFinished)); Assert.That(result.Outcome, Is.EqualTo(MonsterBattleOutcome.Victory));
    }
    [Test]
    public void MissAndImmunityFactsDoNotInventDamage()
    {
        var miss = Move(); miss.AlwaysHits = false; miss.Accuracy = 1;
        var session = Session(Guid.NewGuid(), Actor(miss), Actor(Move(), speed: 10)); Play(session); var state = session.Snapshot(1);
        Assert.That(state.Events.Any(e => e.Kind == MonsterBattleEventKind.MoveMissed && e.ActorId == state.Player.Id), Is.True);
        Assert.That(state.Events.Any(e => e.Kind == MonsterBattleEventKind.DamageApplied && e.ActorId == state.Player.Id), Is.False);
        var immune = new MonsterTypeDescriptor(Element); immune.Effectiveness[Element] = 0;
        session = Session(Guid.NewGuid(), Actor(Move(), type: immune), Actor(Move(), speed: 10, type: immune)); Play(session);
        Assert.That(session.Snapshot(1).Events.Where(e => e.Kind == MonsterBattleEventKind.DamageApplied).All(e => e.HPBefore == e.HPAfter), Is.True);
    }
    [Test]
    public void PresentationIsBoundedPrivateAndCopiedAndWireEventsRoundTrip()
    {
        var actor = Actor(Move(), hp: 10000); var session = Session(Guid.NewGuid(), actor, Actor(Move(), hp: 10000, speed: 10));
        for (var i = 0; i < 4; i++) { Play(session); Assert.That(session.Snapshot(i).Events.Length, Is.LessThanOrEqualTo(15)); }
        var state = session.Snapshot(5); var original = state.Events[0].Text; state.Events[0].Text = "caller mutation";
        Assert.That(session.Snapshot(6).Events[0].Text, Is.EqualTo(original));
        var wire = MessagePack.MessagePackSerializer.Deserialize<MonsterBattleStatePacket>(MessagePack.MessagePackSerializer.Serialize(session.Snapshot(7)));
        Assert.That(wire.Events, Is.EqualTo(session.Snapshot(7).Events)); Assert.That(wire.PreviousRevision, Is.EqualTo(state.PreviousRevision));
        Assert.That(wire.Events.All(e => e.ActorId == wire.Player.Id || e.ActorId == wire.Opponent.Id), Is.True);
        session.Cancel("operator cancelled"); Assert.That(session.Snapshot(8).Events.Single().Kind, Is.EqualTo(MonsterBattleEventKind.BattleFinished));
    }
    [Test]
    public void CombatantArtReferencesAreCopiedWithoutChangingStatsOrOverworldSprite()
    {
        var original = Actor(Move());
        var actor = new MonsterBattleCombatant(original.Calculation, "Custom", "sheet.png", original.Moves, "front.png", "back.png");
        var copy = actor.Copy().View(); Assert.That(copy.Sprite, Is.EqualTo("sheet.png")); Assert.That(copy.FrontBattleSprite, Is.EqualTo("front.png"));
        Assert.That(copy.BackBattleSprite, Is.EqualTo("back.png")); Assert.That(copy.MaximumHP, Is.EqualTo(original.MaximumHP));
    }

    [Test]
    public void BattleCompatibilityMarkerFitsTheProductionRsaHandshake()
    {
        using var outer = System.Security.Cryptography.RSA.Create(4096);
        using var peer = System.Security.Cryptography.RSA.Create(2048);
        var expected = SharedConstants.VersionData.ToArray();
        var hail = new Intersect.Network.Packets.HailPacket(outer, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), expected, peer.ExportParameters(false));
        Assert.That(hail.Encrypt(), Is.True); Assert.That(hail.Decrypt(outer), Is.True);
        Assert.That(hail.VersionData, Is.EqualTo(expected));
        Assert.That(System.Text.Encoding.UTF8.GetString(expected), Does.EndWith("|monster-battles-v7"));
    }


    [Test]
    public void VoluntarySwitchRetargetsAttackAndCannotSpendPlayerPpOrRepeat()
    {
        var owner = Guid.NewGuid();
        var a = Actor(Move());
        var b = Actor(Move());
        var opponent = Actor(Move(priority: 5));
        var session = new MonsterBattleSession(new(owner, [a, b]), new(null, [opponent]), MonsterBattleRules.Generation4, new LastRandom());
        MonsterBattleStatePacket? state = null;
        var registry = new MonsterBattleRegistry((recipient, packet) =>
        {
            Assert.That(recipient, Is.EqualTo(owner));
            state = packet;
        });
        registry.Start(owner, () => session);
        Assert.That(state!.Party.Select(member => member.Actor.Id), Is.EqualTo(new[] { a.Id, b.Id }));
        Assert.That(registry.ReservesMonster(b.Id), Is.True);
        var action = Action(session);
        action.Kind = MonsterBattleActionKind.Switch;
        action.MonsterId = b.Id;
        var packer = Intersect.Network.MessagePacker.Instance;
        var decoded = packer.Deserialize<MonsterBattleActionPacket>(packer.Serialize(action))!;
        Assert.That(decoded.MonsterId, Is.EqualTo(b.Id));
        Assert.That(decoded.Kind, Is.EqualTo(MonsterBattleActionKind.Switch));
        registry.Submit(owner, decoded);
        Assert.That(state!.Player.Id, Is.EqualTo(b.Id));
        Assert.That(state.Turn, Is.EqualTo(2));
        Assert.That(state.Events.Take(3).Select(entry => entry.Kind), Is.EqualTo(new[]
            { MonsterBattleEventKind.Withdraw, MonsterBattleEventKind.SendOut, MonsterBattleEventKind.MoveUsed }));
        Assert.That(state.Events.Single(entry => entry.Kind == MonsterBattleEventKind.MoveUsed).ActorId, Is.EqualTo(opponent.Id));
        Assert.That(state.Events.Single(entry => entry.Kind == MonsterBattleEventKind.DamageApplied).TargetId, Is.EqualTo(b.Id));
        Assert.That(state.Party[0].Actor.HP, Is.EqualTo(a.HP));
        Assert.That(state.Player.HP, Is.LessThan(b.HP));
        Assert.That(state.Moves[0].PP, Is.EqualTo(20));
        var revision = state.Revision;
        var hp = state.Player.HP;
        Assert.Throws<InvalidOperationException>(() => registry.Submit(owner, decoded));
        Assert.That(state.Revision, Is.EqualTo(revision));
        Assert.That(state.Player.HP, Is.EqualTo(hp));
        Assert.That(state.Events, Is.Empty);
        registry.Cancel(owner, "Disconnected.", true);
        Assert.That(registry.ReservesMonster(a.Id), Is.False);
        Assert.That(registry.ReservesMonster(b.Id), Is.False);
    }

    [Test]
    public void ForcedReplacementHasNoAttackOrPpCostAndDefeatWaitsForLastMonster()
    {
        var owner = Guid.NewGuid();
        var a = Actor(Move(), hp: 1);
        var b = Actor(Move(), hp: 1);
        var fainted = Actor(Move());
        fainted.Damage(fainted.HP);
        var opponent = Actor(Move(priority: 5));
        var provider = new CountingProvider();
        var session = new MonsterBattleSession(new(owner, [a, b, fainted]), new(null, [opponent]),
            MonsterBattleRules.Generation4, new LastRandom(), provider);
        var unknown = Action(session);
        unknown.Kind = MonsterBattleActionKind.Switch;
        foreach (var invalid in new[] { Guid.NewGuid(), a.Id, fainted.Id })
        {
            unknown.MonsterId = invalid;
            Assert.Throws<InvalidOperationException>(() => session.Submit(owner, unknown));
        }
        unknown.Kind = MonsterBattleActionKind.Replace;
        unknown.MonsterId = b.Id;
        Assert.Throws<InvalidOperationException>(() => session.Submit(owner, unknown));
        Assert.That(provider.Calls, Is.Zero);

        Play(session);
        var afterFaint = session.Snapshot(2);
        Assert.That(afterFaint.Phase, Is.EqualTo(MonsterBattlePhase.Replacement));
        Assert.That(afterFaint.Outcome, Is.EqualTo(MonsterBattleOutcome.Active));
        Assert.That(afterFaint.Player.HP, Is.Zero);
        Assert.That(afterFaint.Moves[0].PP, Is.EqualTo(20));
        Assert.That(afterFaint.Events.Count(entry => entry.Kind == MonsterBattleEventKind.MoveUsed), Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => Play(session));
        var replacement = Action(session);
        replacement.Kind = MonsterBattleActionKind.Replace;
        replacement.MonsterId = b.Id;
        Assert.Throws<InvalidOperationException>(() => session.Submit(Guid.NewGuid(), replacement));
        session.Submit(owner, replacement);
        var afterReplace = session.Snapshot(3);
        Assert.That(afterReplace.Player.Id, Is.EqualTo(b.Id));
        Assert.That(afterReplace.Player.HP, Is.EqualTo(1));
        Assert.That(afterReplace.Turn, Is.EqualTo(afterFaint.Turn));
        Assert.That(afterReplace.Phase, Is.EqualTo(MonsterBattlePhase.Commands));
        Assert.That(afterReplace.Events.Select(entry => entry.Kind), Is.EqualTo(new[] { MonsterBattleEventKind.SendOut }));
        Assert.That(provider.Calls, Is.EqualTo(1));
        var wire = MessagePack.MessagePackSerializer.Deserialize<MonsterBattleStatePacket>(MessagePack.MessagePackSerializer.Serialize(afterReplace));
        Assert.That(wire.Party.Select(member => member.Actor.HP), Is.EqualTo(new long[] { 0, 1, 0 }));
        Assert.That(wire.Events.Single().Incoming!.Id, Is.EqualTo(b.Id));
        Play(session);
        Assert.That(session.Outcome, Is.EqualTo(MonsterBattleOutcome.Defeat));
    }

    [Test]
    public void DisconnectWhileChoosingReplacementReleasesEntireParty()
    {
        var owner = Guid.NewGuid();
        var first = Actor(Move(), hp: 1);
        var second = Actor(Move());
        var session = new MonsterBattleSession(new(owner, [first, second]), new(null, [Actor(Move(priority: 5))]),
            MonsterBattleRules.Generation4, new LastRandom());
        var registry = new MonsterBattleRegistry((_, _) => { });
        registry.Start(owner, () => session);
        registry.Submit(owner, Action(session));
        Assert.That(session.Phase, Is.EqualTo(MonsterBattlePhase.Replacement));
        registry.Cancel(owner, "Disconnected.", true);
        Assert.That(session.Outcome, Is.EqualTo(MonsterBattleOutcome.Forfeit));
        Assert.That(registry.HasActive(owner), Is.False);
        Assert.That(registry.ReservesMonster(first.Id) || registry.ReservesMonster(second.Id), Is.False);
    }

    [Test]
    public void SimultaneousKnockoutsUseWholePartyBeforeRequestingReplacement()
    {
        var a = Actor(Move(), hp: 1);
        var b = Actor(Move(), hp: 1);
        var opponent = Actor(Move(), hp: 1);
        var player = new MonsterBattleSide(Guid.NewGuid(), [a, b]);
        var enemy = new MonsterBattleSide(null, [opponent]);
        a.Damage(1);
        Assert.That(MonsterBattleSession.KnockoutOutcome(player, enemy), Is.Null);
        opponent.Damage(1);
        Assert.That(MonsterBattleSession.KnockoutOutcome(player, enemy), Is.EqualTo(MonsterBattleOutcome.Victory));
        b.Damage(1);
        Assert.That(MonsterBattleSession.KnockoutOutcome(player, enemy), Is.EqualTo(MonsterBattleOutcome.Draw));
    }

    private sealed class CountingProvider : IMonsterBattleDecisionProvider
    {
        public int Calls { get; private set; }
        public int ChooseMove(MonsterBattleDecision view, IMonsterRandom random)
        {
            Calls++;
            return view.LegalMoveSlots[0];
        }
    }

    [Test]
    public void MoveUsedPublishesTheFrozenAuthoredAnimationAndPosition()
    {
        var owner = Guid.NewGuid();
        var move = Move();
        var animationId = Guid.NewGuid();
        move.AnimationId = animationId;
        move.AnimationPosition = BattleMoveAnimationPosition.Both;
        var player = Actor(move);
        var opponent = Actor(Move(priority: 5));
        var session = Session(owner, player, opponent);
        move.AnimationId = null;
        move.AnimationPosition = BattleMoveAnimationPosition.Attacker;
        Play(session);
        var state = session.Snapshot(1);
        var wire = MessagePack.MessagePackSerializer.Deserialize<MonsterBattleStatePacket>(MessagePack.MessagePackSerializer.Serialize(state));
        var used = wire.Events.Single(entry => entry.Kind == MonsterBattleEventKind.MoveUsed && entry.ActorId == player.Id);
        Assert.That(used.AnimationId, Is.EqualTo(animationId));
        Assert.That(used.AnimationPosition, Is.EqualTo(BattleMoveAnimationPosition.Both));
        Assert.That(used.TargetId, Is.EqualTo(opponent.Id));
    }

    private static MonsterMoveDescriptor Status(params MonsterMoveEffect[] effects)
    {
        var move = Move(MonsterMoveCategory.Status);
        move.BasePower = 0;
        move.Target = effects.Any(effect => effect.Target == MonsterMoveEffectTarget.Opponent)
            ? MonsterMoveTarget.OneOpponent : MonsterMoveTarget.Self;
        move.Effects = effects.ToList();
        return move;
    }

    private static MonsterBattleCombatant EffectActor(long maximum, long hp, long speed, params MonsterMoveDescriptor[] moves)
    {
        var slots = new MonsterBattleMove?[4];
        for (var index = 0; index < moves.Length; index++)
            slots[index] = new(moves[index], new MonsterTypeDescriptor(Element));
        return new(new(Guid.NewGuid(), 50, new(maximum, 50, 50, 50, 50, speed), [Element]),
            "Effect tester", "", slots, currentHP: hp);
    }

    [Test]
    public void StatusMovesClampEveryStageAndUseActualDeltaWithoutDamage()
    {
        var idle = Status(new MonsterMoveEffect { Stat = MonsterStat.Speed, Amount = 1 });
        foreach (var stat in Enum.GetValues<MonsterStat>().Where(MonsterMoveEffect.SupportsStage))
        foreach (var direction in new[] { -1, 1 })
        {
            var move = Status(new MonsterMoveEffect { Stat = stat, Amount = direction * 5 },
                new MonsterMoveEffect { Stat = stat, Amount = direction * 4 },
                new MonsterMoveEffect { Stat = stat, Amount = direction * 2 });
            move.AlwaysHits = false;
            move.Accuracy = 1;
            var session = Session(Guid.NewGuid(), Actor(move), Actor(idle));
            Play(session);
            var state = session.Snapshot(1);
            Assert.That(state.Player.StatStages[(int)stat - (int)MonsterStat.Attack], Is.EqualTo(direction * 6));
            var ownEvents = state.Events.Where(e => e.ActorId == state.Player.Id).ToArray();
            Assert.That(ownEvents.Select(e => e.Kind), Is.EqualTo(new[] { MonsterBattleEventKind.MoveUsed,
                MonsterBattleEventKind.StatStageChanged, MonsterBattleEventKind.StatStageChanged, MonsterBattleEventKind.EffectFailed }));
            Assert.That(ownEvents[2].Text, Does.Not.Contain("sharply").And.Not.Contain("drastically"));
            Assert.That(state.Player.HP, Is.EqualTo(200));
            Assert.That(state.Moves[0].PP, Is.EqualTo(19));
        }

        Assert.That(Enumerable.Range(-6, 13).Select(stage => MonsterBattleCalculator.ApplyStatStage(840, stage)),
            Is.EqualTo(new long[] { 210, 240, 280, 336, 420, 560, 840, 1260, 1680, 2100, 2520, 2940, 3360 }));
    }

    [Test]
    public void PhysicalAndSpecialDamageUseTheirOwnStages()
    {
        foreach (var category in new[] { MonsterMoveCategory.Physical, MonsterMoveCategory.Special })
        {
            var attack = category == MonsterMoveCategory.Physical ? MonsterStat.Attack : MonsterStat.SpecialAttack;
            var defense = category == MonsterMoveCategory.Physical ? MonsterStat.Defense : MonsterStat.SpecialDefense;
            long DamageAfter(MonsterStat? stat, MonsterMoveEffectTarget target)
            {
                var setup = Status(new MonsterMoveEffect
                {
                    Stat = stat ?? MonsterStat.Speed, Amount = 2, Target = target,
                });
                var idle = Status(new MonsterMoveEffect { Stat = MonsterStat.Speed });
                var session = Session(Guid.NewGuid(), EffectActor(500, 500, 20, setup, Move(category)), Actor(idle, hp: 500));
                Play(session);
                Play(session, 1);
                var damage = session.Snapshot(2).Events.Single(e => e.Kind == MonsterBattleEventKind.DamageApplied);
                return damage.HPBefore - damage.HPAfter;
            }

            var baseline = DamageAfter(null, MonsterMoveEffectTarget.Self);
            Assert.That(DamageAfter(attack, MonsterMoveEffectTarget.Self), Is.GreaterThan(baseline));
            Assert.That(DamageAfter(defense, MonsterMoveEffectTarget.Opponent), Is.LessThan(baseline));
        }
    }

    [Test]
    public void SpeedChangesApplyNextTurnAndSwitchingClearsStages()
    {
        var setup = Status(new MonsterMoveEffect { Stat = MonsterStat.Speed, Amount = 2 });
        var a = EffectActor(1000, 1000, 20, setup, Move());
        var b = EffectActor(1000, 1000, 10, Move());
        var opponent = Actor(Move(), hp: 1000, speed: 30);
        var owner = Guid.NewGuid();
        var session = new MonsterBattleSession(new(owner, [a, b]), new(null, [opponent]), MonsterBattleRules.Generation4, new LastRandom());
        Play(session);
        Assert.That(session.Snapshot(1).Events.First().ActorId, Is.EqualTo(opponent.Id));
        Play(session, 1);
        Assert.That(session.Snapshot(2).Events.First().ActorId, Is.EqualTo(a.Id));

        var toB = Action(session);
        toB.Kind = MonsterBattleActionKind.Switch;
        toB.MonsterId = b.Id;
        session.Submit(owner, toB);
        Assert.That(session.Snapshot(3).Party[0].Actor.StatStages, Is.All.Zero);
        var back = Action(session);
        back.Kind = MonsterBattleActionKind.Switch;
        back.MonsterId = a.Id;
        session.Submit(owner, back);
        Play(session, 1);
        Assert.That(session.Snapshot(4).Events.First().ActorId, Is.EqualTo(opponent.Id));

        Play(session);
        Assert.That(session.Snapshot(5).Player.StatStages[4], Is.EqualTo(2));
        session.Cancel("Disconnected.", true);
        Assert.That(session.Snapshot(6).Player.StatStages, Is.All.Zero);
    }

    [Test]
    public void OpponentDebuffChecksAccuracyAndDuplicateActionCannotReapplyIt()
    {
        var move = Status(new MonsterMoveEffect { Target = MonsterMoveEffectTarget.Opponent, Stat = MonsterStat.Attack, Amount = -2 });
        var opponent = Actor(Move(), hp: 1000);
        move.AlwaysHits = false;
        move.Accuracy = 1;
        var owner = Guid.NewGuid();
        var miss = Session(owner, Actor(move, hp: 1000), opponent);
        Play(miss);
        Assert.That(miss.Snapshot(1).Events.Any(e => e.Kind == MonsterBattleEventKind.MoveMissed), Is.True);
        Assert.That(miss.Snapshot(1).Opponent.StatStages[0], Is.Zero);
        Assert.That(miss.Snapshot(1).Moves[0].PP, Is.EqualTo(19));

        move.AlwaysHits = true;
        var hit = Session(owner, Actor(move, hp: 1000), opponent);
        var registry = new MonsterBattleRegistry((_, _) => { });
        registry.Start(owner, () => hit);
        var action = Action(hit);
        registry.Submit(owner, action);
        Assert.Throws<InvalidOperationException>(() => registry.Submit(owner, action));
        Assert.That(hit.Snapshot(2).Opponent.StatStages[0], Is.EqualTo(-2));
        Assert.That(hit.Snapshot(2).Moves[0].PP, Is.EqualTo(19));
        registry.Cancel(owner, "Disconnected.", true);
        Assert.That(registry.HasActive(owner), Is.False);
        var next = Session(owner, Actor(move, hp: 1000), opponent);
        registry.Start(owner, () => next);
        Assert.That(next.Snapshot(3).Opponent.StatStages, Is.All.Zero);
    }

    [Test]
    public void HealingRoundsDownWithMinimumOneClampsAndReportsFullHP()
    {
        var idle = Status(new MonsterMoveEffect { Stat = MonsterStat.Speed });
        foreach (var (method, amount, starting, expected) in new[]
        {
            (MonsterHealingMethod.Fixed, 20, 11L, 31L),
            (MonsterHealingMethod.Fixed, 20, 97L, 101L),
            (MonsterHealingMethod.Percentage, 50, 11L, 61L),
            (MonsterHealingMethod.Percentage, 1, 100L, 101L),
            (MonsterHealingMethod.Percentage, 100, 101L, 101L),
        })
        {
            var heal = Status(new MonsterMoveEffect { Kind = MonsterMoveEffectKind.RestoreHP, HealingMethod = method, Amount = amount });
            heal.AnimationId = Guid.NewGuid();
            heal.AlwaysHits = false;
            heal.Accuracy = 1;
            var session = Session(Guid.NewGuid(), EffectActor(101, starting, 20, heal), Actor(idle));
            Play(session);
            var state = session.Snapshot(1);
            Assert.That(state.Player.HP, Is.EqualTo(expected));
            Assert.That(state.Moves[0].PP, Is.EqualTo(19));
            var ownEvents = state.Events.Where(e => e.ActorId == state.Player.Id).ToArray();
            Assert.That(ownEvents[0].AnimationId, Is.EqualTo(heal.AnimationId));
            Assert.That(ownEvents[0].TargetId, Is.EqualTo(state.Player.Id));
            Assert.That(ownEvents[1].Kind, Is.EqualTo(starting == expected ? MonsterBattleEventKind.EffectFailed : MonsterBattleEventKind.HPRecovered));
            Assert.That(ownEvents.All(e => e.Kind != MonsterBattleEventKind.DamageApplied), Is.True);
        }
    }

    [Test]
    public void KnockoutBeforeHealingSpendsNoPpAndCannotRevive()
    {
        var heal = Status(new MonsterMoveEffect { Kind = MonsterMoveEffectKind.RestoreHP, Amount = 100 });
        var session = Session(Guid.NewGuid(), EffectActor(101, 1, 20, heal), Actor(Move(priority: 1)));
        Play(session);
        var state = session.Snapshot(1);
        Assert.That(state.Outcome, Is.EqualTo(MonsterBattleOutcome.Defeat));
        Assert.That(state.Moves[0].PP, Is.EqualTo(20));
        Assert.That(state.Events.Any(e => e.Kind == MonsterBattleEventKind.HPRecovered), Is.False);
    }

    [Test]
    public void PriorityGuardReducesIncomingDamageAndDamagingEffectsRequireALandedHit()
    {
        long DamageWithGuard(MonsterStat stat)
        {
            var setup = Status(new MonsterMoveEffect { Stat = stat, Amount = 2 });
            setup.Priority = 1;
            var session = Session(Guid.NewGuid(), Actor(setup, hp: 500), Actor(Move(), hp: 500, speed: 30));
            Play(session);
            var events = session.Snapshot(1).Events;
            Assert.That(events[1].Kind, Is.EqualTo(MonsterBattleEventKind.StatStageChanged));
            var damage = events.Single(entry => entry.Kind == MonsterBattleEventKind.DamageApplied);
            return damage.HPBefore - damage.HPAfter;
        }

        Assert.That(DamageWithGuard(MonsterStat.Defense), Is.LessThan(DamageWithGuard(MonsterStat.Speed)));
        var move = Move(priority: 1);
        move.Effects = [new() { Stat = MonsterStat.Defense, Amount = -1, Target = MonsterMoveEffectTarget.Opponent }];
        var opponent = Actor(Move(), hp: 500);
        var hit = Session(Guid.NewGuid(), Actor(move, hp: 500), opponent);
        Play(hit);
        var hitEvents = hit.Snapshot(1).Events;
        var damageEvent = hitEvents.First(entry => entry.Kind == MonsterBattleEventKind.DamageApplied);
        var statEvent = hitEvents.Single(entry => entry.Kind == MonsterBattleEventKind.StatStageChanged);
        Assert.That(statEvent.Order, Is.GreaterThan(damageEvent.Order));
        Assert.That(hit.Snapshot(2).Opponent.StatStages[1], Is.EqualTo(-1));

        move.AlwaysHits = false;
        move.Accuracy = 1;
        var miss = Session(Guid.NewGuid(), Actor(move, hp: 500), opponent);
        Play(miss);
        Assert.That(miss.Snapshot(1).Opponent.StatStages[1], Is.Zero);
        move.AlwaysHits = true;
        var immune = Session(Guid.NewGuid(), Actor(move, hp: 500,
            type: new MonsterTypeDescriptor(Element) { Effectiveness = new() { [Element] = 0 } }), opponent);
        Play(immune);
        Assert.That(immune.Snapshot(1).Opponent.StatStages[1], Is.Zero);
    }

    private static MonsterMoveDescriptor StatusMove(MonsterStatusDescriptor status, bool cure = false,
        MonsterMoveEffectTarget target = MonsterMoveEffectTarget.Self, int chance = 100) =>
        Status(new MonsterMoveEffect
        {
            Kind = cure ? MonsterMoveEffectKind.CureStatus : MonsterMoveEffectKind.ApplyStatus,
            StatusId = status.Id, Target = target, Chance = chance,
        });

    [Test]
    public void IdenticalDamageCanPersistOrEndWithBattleAndSwitchingIsIndependent()
    {
        foreach (var persist in new[] { false, true })
        foreach (var removeOnSwitch in new[] { false, true })
        {
            var definition = new MonsterStatusDescriptor(Guid.NewGuid())
            {
                Name = "Custom condition", Damage = MonsterStatusDamage.Percent, DamageAmount = 12.5m,
                PersistOutsideBattle = persist, RemoveWhenSwitching = removeOnSwitch,
            };
            var owner = Guid.NewGuid();
            var first = EffectActor(160, 160, 20, StatusMove(definition), Move());
            var second = Actor(Move(), hp: 1000);
            var idle = Status(new MonsterMoveEffect { Stat = MonsterStat.Speed });
            var session = new MonsterBattleSession(new(owner, [first, second]), new(null, [Actor(idle)]),
                MonsterBattleRules.Generation4, new LastRandom(), findStatus: id => id == definition.Id ? definition : null);
            definition.DamageAmount = 99; // Content edits do not mutate an active battle.
            Play(session);
            var initial = session.Snapshot(1);
            Assert.That(initial.Player.HP, Is.EqualTo(140));
            Assert.That(initial.Events.Single(e => e.Kind == MonsterBattleEventKind.StatusDamage).HPAfter, Is.EqualTo(140));

            var action = Action(session);
            action.Kind = MonsterBattleActionKind.Switch;
            action.MonsterId = second.Id;
            session.Submit(owner, action);
            Assert.That(session.Snapshot(2).Party[0].Actor.HP, Is.EqualTo(140), "Benched statuses do not tick.");
            var back = Action(session);
            back.Kind = MonsterBattleActionKind.Switch;
            back.MonsterId = first.Id;
            session.Submit(owner, back);
            Assert.That(session.Snapshot(3).Player.StatusName, Is.EqualTo(removeOnSwitch ? "" : "Custom condition"));
            Assert.That(session.Snapshot(3).Player.HP, Is.EqualTo(removeOnSwitch ? 140 : 120));
            session.Cancel("Disconnected.", true);
            Assert.That(session.Snapshot(4).Player.StatusName, Is.EqualTo(persist && !removeOnSwitch ? "Custom condition" : ""));
        }
    }

    [Test]
    public void StatusPreventionConsumesNoPpAndRecoveryPrecedesPrevention()
    {
        foreach (var recovery in new[] { 0, 100 })
        {
            var status = new MonsterStatusDescriptor(Guid.NewGuid())
            {
                Name = "Drowsy", PreventActionChance = 100, RecoveryChance = recovery,
                Duration = MonsterStatusDuration.Fixed, MinimumTurns = 2, MaximumTurns = 2,
            };
            var own = Actor(Move(), hp: 1000);
            own.Status = new(status, 2);
            var session = Session(Guid.NewGuid(), own, Actor(Move(), hp: 1000));
            Play(session);
            var first = session.Snapshot(1);
            Assert.That(first.Moves[0].PP, Is.EqualTo(recovery == 0 ? 20 : 19));
            Assert.That(first.Events.Any(e => e.Kind == MonsterBattleEventKind.StatusBlocked), Is.EqualTo(recovery == 0));
            if (recovery == 0)
            {
                Play(session);
                Assert.That(session.Snapshot(2).Player.StatusName, Is.Empty);
                Play(session);
                Assert.That(session.Snapshot(3).Moves[0].PP, Is.EqualTo(19));
            }
        }
    }

    [Test]
    public void ApplicationChecksAccuracyChanceExclusivityAndCure()
    {
        var status = new MonsterStatusDescriptor(Guid.NewGuid()) { Name = "Example" };
        var idle = Status(new MonsterMoveEffect { Stat = MonsterStat.Speed });
        var miss = StatusMove(status, target: MonsterMoveEffectTarget.Opponent);
        miss.Accuracy = 1;
        miss.AlwaysHits = false;
        var unlikely = StatusMove(status, target: MonsterMoveEffectTarget.Opponent, chance: 1);
        var owner = Guid.NewGuid();
        var session = new MonsterBattleSession(new(owner, [EffectActor(500, 500, 20, miss, unlikely, StatusMove(status), StatusMove(status, cure: true))]),
            new(null, [Actor(idle)]), MonsterBattleRules.Generation4, new LastRandom(), findStatus: _ => status);
        Play(session);
        Assert.That(session.Snapshot(1).Events.Any(e => e.Kind == MonsterBattleEventKind.MoveMissed), Is.True);
        Play(session, 1);
        Assert.That(session.Snapshot(2).Opponent.StatusName, Is.Empty);
        Play(session, 2);
        Play(session, 2);
        Assert.That(session.Snapshot(4).Events.Any(e => e.Text.Contains("already has")), Is.True);
        Assert.That(session.Snapshot(4).Moves[2].PP, Is.EqualTo(18));
        Play(session, 3);
        Assert.That(session.Snapshot(5).Player.StatusName, Is.Empty);
        Assert.That(session.Snapshot(5).Events.Any(e => e.Kind == MonsterBattleEventKind.StatusRemoved), Is.True);
    }

    [Test]
    public void FixedDamageCanFaintBothSidesBeforeOutcomeAndPercentPenaltiesStack()
    {
        var status = new MonsterStatusDescriptor(Guid.NewGuid())
        {
            Damage = MonsterStatusDamage.Fixed, DamageAmount = 2000, AttackPercent = 50, SpeedPercent = 25,
        };
        var idle = Status(new MonsterMoveEffect { Stat = MonsterStat.Speed });
        var a = Actor(idle, hp: 200);
        var b = Actor(idle, hp: 200);
        a.Status = new(status, null);
        b.Status = new(status, null);
        Assert.That(a.EffectiveSpeed, Is.EqualTo(5));
        a.ChangeStage(MonsterStat.Speed, 2);
        Assert.That(a.EffectiveSpeed, Is.EqualTo(10));
        var session = Session(Guid.NewGuid(), a, b);
        Play(session);
        var state = session.Snapshot(1);
        Assert.That(state.Outcome, Is.EqualTo(MonsterBattleOutcome.Draw));
        Assert.That(state.Events.Count(e => e.Kind == MonsterBattleEventKind.MonsterFainted), Is.EqualTo(2));

        var type = new MonsterTypeDescriptor(Element);
        long Damage(int percent) => MonsterBattleCalculator.Damage(a.Calculation, b.Calculation, Move(), type,
            MonsterBattleRules.Generation4, new() { AttackStage = 2, AttackPercent = percent }, false, new LastRandom());
        Assert.That(Damage(50), Is.LessThan(Damage(100)));
    }

    [Test]
    public void RandomStatusDurationIncludesBothEndpointsAndBlockedAttemptsDoNotSpendPp()
    {
        foreach (var (random, expected) in new (IMonsterRandom, int)[] { (new ZeroRandom(), 2), (new LastRandom(), 4) })
        {
            var status = new MonsterStatusDescriptor(Guid.NewGuid())
            {
                Duration = MonsterStatusDuration.Random, MinimumTurns = 2, MaximumTurns = 4, PreventActionChance = 100,
            };
            var player = EffectActor(1000, 1000, 30, StatusMove(status, target: MonsterMoveEffectTarget.Opponent), Move());
            var idle = Status(new MonsterMoveEffect { Stat = MonsterStat.Attack });
            var rival = EffectActor(1000, 1000, 20, idle);
            var session = new MonsterBattleSession(new(Guid.NewGuid(), [player]), new(null, [rival]),
                MonsterBattleRules.Generation4, random, findStatus: _ => status);
            Play(session);
            for (var turn = 1; turn < expected; turn++)
            {
                Assert.That(session.Snapshot(turn).Opponent.StatusName, Is.EqualTo(status.Name));
                Play(session, 1);
            }
            var final = session.Snapshot(expected);
            Assert.That(final.Opponent.StatusName, Is.Empty);
            Assert.That(final.Events.Any(entry => entry.Kind == MonsterBattleEventKind.StatusBlocked), Is.True);
            Play(session, 1);
            Assert.That(session.Snapshot(expected + 1).Events.Any(entry =>
                entry.Kind == MonsterBattleEventKind.MoveUsed && entry.ActorId == rival.Id), Is.True);
        }
    }
}
