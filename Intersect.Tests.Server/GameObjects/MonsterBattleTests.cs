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
        Assert.That(System.Text.Encoding.UTF8.GetString(expected), Does.EndWith("|monster-battles-v2"));
    }

}
