using Intersect.Config;
using Intersect.Framework.Core.GameObjects.Events;
using Intersect.Framework.Core.GameObjects.Events.Commands;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Network.Packets.Client;
using Intersect.Network.Packets.Server;
using Intersect.Server.Entities.Events;
using Intersect.Server.Monsters.Battles;
using Intersect.Server.Monsters.Calculations;
using NUnit.Framework;

namespace Intersect.Tests.Server.GameObjects;

[TestFixture, NonParallelizable]
public sealed class MonsterEventBattleTests
{
    private sealed class ZeroRandom : IMonsterRandom
    {
        public long NextInt64(long exclusiveUpperBound) => 0;
    }

    private static (EventPage Page, StartMonsterBattleCommand Command) CreatePage()
    {
        var page = new EventPage();
        var command = new StartMonsterBattleCommand(page.CommandLists)
        {
            SpeciesId = Guid.NewGuid(), Level = 10,
        };
        page.CommandLists.Values.First().Add(command);
        page.CommandLists[command.Victory].Add(new ShowTextCommand { Text = "Victory" });
        page.CommandLists[command.Defeat].Add(new ShowTextCommand { Text = "Defeat" });
        page.CommandLists[command.Escape].Add(new ShowTextCommand { Text = "Escape" });
        return (page, command);
    }

    private static MonsterBattleSession CreateSession(Guid owner, int playerHp, int opponentHp,
        Action<IReadOnlyList<MonsterBattleCombatant>>? persist = null)
    {
        var type = new MonsterTypeDescriptor(Guid.NewGuid());
        var move = new MonsterMoveDescriptor(Guid.NewGuid())
        {
            TypeId = type.Id, BasePower = 100, AlwaysHits = true,
        };
        MonsterBattleCombatant Actor(int hp, int speed) => new(
            new(Guid.NewGuid(), 50, new(hp, 90, 10, 90, 10, speed), [type.Id]),
            "Mossling", "", [new(move, type), null, null, null]);
        var player = Actor(playerHp, playerHp == 1 ? 10 : 50);
        var opponent = Actor(opponentHp, playerHp == 1 ? 50 : 10);
        var wild = new GeneratedWildMonster(opponent.Id, Guid.NewGuid(), 50, new(), null, null, [move.Id]);
        return new(new(owner, [player]), new(null, [opponent]), MonsterBattleRules.Generation4, new ZeroRandom(),
            persistParty: persist, wild: wild);
    }

    private static MonsterBattleActionPacket Move(MonsterBattleSession session, MonsterBattleActionKind kind = MonsterBattleActionKind.Move) =>
        new() { BattleId = session.Id, Revision = session.Revision, Turn = session.Turn, Kind = kind, MoveSlot = 0 };

    private static TaskCompletionSource<MonsterBattleOutcome> NewResult() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Test]
    public void EventPagesPersistCommandAndBranchesAfterSerializationAndCopy()
    {
        var (page, command) = CreatePage();
        var descriptor = new EventDescriptor(Guid.NewGuid(), Guid.NewGuid(), 4, 3) { Pages = [page] };
        var loaded = new EventDescriptor(descriptor.Id, descriptor.JsonData);
        var loadedPage = loaded.Pages.Single();
        var loadedCommand = (StartMonsterBattleCommand)loadedPage.CommandLists.Values.First().Single();
        Assert.That(loadedCommand.Type, Is.EqualTo(EventCommandType.StartMonsterBattle));
        Assert.That(loadedCommand.SpeciesId, Is.EqualTo(command.SpeciesId));
        Assert.That(loadedCommand.Level, Is.EqualTo(10));
        Assert.That(loadedCommand.Branches(), Is.EqualTo(command.Branches()));
        Assert.That(((ShowTextCommand)loadedPage.CommandLists[loadedCommand.Escape].Single()).Text, Is.EqualTo("Escape"));
        var copy = new Dictionary<Guid, List<EventCommand>>();
        var nested = new StartMonsterBattleCommand(loadedPage.CommandLists);
        loadedPage.CommandLists[loadedCommand.Escape].Add(nested);
        var data = loadedCommand.GetCopyData(loadedPage.CommandLists, copy);
        Assert.That(copy.Count, Is.EqualTo(6));
        Assert.That(data, Does.Contain(nameof(StartMonsterBattleCommand)));
        var replacements = copy.Keys.ToDictionary(id => id, _ => Guid.NewGuid());
        var old = loadedCommand.Branches();
        loadedCommand.FixBranchIds(replacements);
        Assert.That(loadedCommand.Branches(), Is.EqualTo(old.Select(id => replacements[id])));
    }

    [Test]
    public void ValidatesSpeciesLevelAndBranchTargetsBeforeStarting()
    {
        var (page, command) = CreatePage();
        Assert.That(command.IsValid(10, id => id == command.SpeciesId), Is.True);
        Assert.That(command.IsValid(9, _ => true), Is.False);
        Assert.That(command.IsValid(100, _ => false), Is.False);
        command.Level = 0;
        Assert.That(command.IsValid(100, _ => true), Is.False);
        command.Level = 1;
        command.SpeciesId = Guid.Empty;
        Assert.That(command.IsValid(100, _ => true), Is.False);
        var frame = new CommandInstance(page);
        page.CommandLists.Remove(command.Victory);
        Assert.That(frame.TryBeginMonsterBattle(command, () => throw new AssertionException("Invalid branch started a battle")), Is.False);
    }

    [Test]
    public void StartsAndWaitsThenSelectsExactlyOneBranchForEachAuthoritativeResult()
    {
        var (page, command) = CreatePage();
        var cases = new[]
        {
            (PlayerHp: 200, OpponentHp: 1, Kind: MonsterBattleActionKind.Move, Text: "Victory"),
            (PlayerHp: 1, OpponentHp: 200, Kind: MonsterBattleActionKind.Move, Text: "Defeat"),
            (PlayerHp: 200, OpponentHp: 200, Kind: MonsterBattleActionKind.Run, Text: "Escape"),
        };
        var registry = new MonsterBattleRegistry((_, _) => { });
        foreach (var test in cases)
        {
            var owner = Guid.NewGuid();
            var frame = new CommandInstance(page);
            var result = NewResult();
            var session = CreateSession(owner, test.PlayerHp, test.OpponentHp);
            Assert.That(frame.TryBeginMonsterBattle(command, () => (registry.Start(owner, () => session, result), result.Task)), Is.True);
            Assert.That(frame.WaitingForResponse, Is.EqualTo(CommandInstance.EventResponse.MonsterBattle));
            Assert.That(frame.TryBeginMonsterBattle(command, () => throw new AssertionException("Duplicate start")), Is.False);
            Assert.That(frame.TryConsumeMonsterBattle(out _), Is.False);
            var action = Move(session, test.Kind);
            registry.Submit(owner, action);
            Assert.That(frame.TryConsumeMonsterBattle(out var branch), Is.True);
            Assert.That(((ShowTextCommand)branch!.Command).Text, Is.EqualTo(test.Text));
            Assert.That(frame.TryConsumeMonsterBattle(out _), Is.False);
            Assert.Throws<InvalidOperationException>(() => registry.Submit(owner, action));
            Assert.That(registry.HasActive(owner), Is.False);
        }
    }

    [Test]
    public void SharedPageCanWaitForTwoPlayersAndAnOldEventCannotCancelANewerBattle()
    {
        var (page, command) = CreatePage();
        var registry = new MonsterBattleRegistry((_, _) => { });
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var firstFrame = new CommandInstance(page);
        var otherFrame = new CommandInstance(page);
        var firstResult = NewResult();
        var secondResult = NewResult();
        var firstBattle = CreateSession(first, 200, 1);
        var otherBattle = CreateSession(second, 200, 1);
        Assert.That(firstFrame.TryBeginMonsterBattle(command, () => (registry.Start(first, () => firstBattle, firstResult), firstResult.Task)), Is.True);
        Assert.That(otherFrame.TryBeginMonsterBattle(command, () => (registry.Start(second, () => otherBattle, secondResult), secondResult.Task)), Is.True);
        Assert.Throws<InvalidOperationException>(() => registry.Start(first, () => CreateSession(first, 200, 1)));
        registry.Submit(first, Move(firstBattle));
        Assert.That(firstFrame.TryConsumeMonsterBattle(out var victory), Is.True);
        Assert.That(victory!.CommandList, Is.SameAs(page.CommandLists[command.Victory]));
        Assert.That(otherFrame.TryConsumeMonsterBattle(out _), Is.False);
        var next = NewResult();
        registry.Start(first, () => CreateSession(first, 200, 1), next);
        registry.CancelEventBattle(first, firstBattle.Id);
        registry.CancelEventBattle(first, otherBattle.Id);
        Assert.That(registry.HasActive(first), Is.True);
        Assert.That(registry.HasActive(second), Is.True);
        registry.Cancel(second, "Disconnect", disconnect: true);
        Assert.That(otherFrame.TryConsumeMonsterBattle(out var cancelled), Is.True);
        Assert.That(cancelled, Is.Null);
        Assert.That(registry.HasActive(second), Is.False);
        registry.CancelAll();
        Assert.That(next.Task.Result, Is.EqualTo(MonsterBattleOutcome.Cancelled));
    }

    [Test]
    public void UnconfirmedVictoryAndForfeitProduceNoBranch()
    {
        var (page, command) = CreatePage();
        var owner = Guid.NewGuid();
        void Verify(MonsterBattleRegistry registry, MonsterBattleSession session, MonsterBattleActionKind kind, bool throws)
        {
            var frame = new CommandInstance(page);
            var result = NewResult();
            Assert.That(frame.TryBeginMonsterBattle(command, () => (registry.Start(owner, () => session, result), result.Task)), Is.True);
            if (throws) Assert.Throws<InvalidOperationException>(() => registry.Submit(owner, Move(session, kind)));
            else registry.Submit(owner, Move(session, kind));
            Assert.That(frame.TryConsumeMonsterBattle(out var branch), Is.True);
            Assert.That(branch, Is.Null);
            Assert.That(registry.HasActive(owner), Is.False);
        }

        var normal = new MonsterBattleRegistry((_, _) => { });
        Verify(normal, CreateSession(owner, 200, 1, _ => throw new InvalidOperationException("Persistence failure")),
            MonsterBattleActionKind.Move, throws: true);
        Verify(normal, CreateSession(owner, 200, 1), MonsterBattleActionKind.Forfeit, throws: false);
        var deliveryFailure = new MonsterBattleRegistry((_, state) =>
        {
            if (state.Outcome == MonsterBattleOutcome.Victory) throw new InvalidOperationException("Delivery failure");
        });
        Verify(deliveryFailure, CreateSession(owner, 200, 1), MonsterBattleActionKind.Move, throws: false);
    }
}
