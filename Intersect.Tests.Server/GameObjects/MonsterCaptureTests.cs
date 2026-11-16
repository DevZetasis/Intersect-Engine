using Intersect.Config;
using Intersect.Framework.Core.GameObjects.Conditions.ConditionMetadata;
using Intersect.Framework.Core.GameObjects.Events;
using Intersect.Framework.Core.GameObjects.Events.Commands;
using Intersect.Framework.Core.GameObjects.Items;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Framework.Core.GameObjects.Variables;
using Intersect.Network.Packets.Client;
using Intersect.Network.Packets.Server;
using Intersect.Server.Database;
using Intersect.Server.Database.GameData;
using Intersect.Server.Database.PlayerData;
using Intersect.Server.Database.PlayerData.Players;
using Intersect.Server.Entities;
using Intersect.Server.Entities.Events;
using Intersect.Server.Monsters;
using Intersect.Server.Monsters.Battles;
using Intersect.Server.Monsters.Calculations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Intersect.Tests.Server.GameObjects;

[TestFixture, NonParallelizable]
public sealed class MonsterCaptureTests
{
    [Test]
    public void CalculatesHpAndConfiguredMultipliersBeforeApplyingTheCap()
    {
        foreach (var (hp, expected) in new[] { (100, 40m), (75, 45m), (50, 50m), (25, 55m), (1, 59.8m) })
            Assert.That(MonsterCaptureChance.Calculate(true, 40, hp, 100, [], 1m, false), Is.EqualTo(expected));
        foreach (var (modifier, expected) in new[] { (2m, 90m), (1.5m, 67.5m), (.58m, 26.1m), (0m, 0m) })
            Assert.That(MonsterCaptureChance.Calculate(true, 40, 75, 100, [modifier], 1m, false), Is.EqualTo(expected));
        Assert.That(MonsterCaptureChance.Calculate(true, 40, 75, 100, [.2m, 2m, 1.5m], 1m, false), Is.EqualTo(90m));
        Assert.That(MonsterCaptureChance.Calculate(true, 40, 75, 100, [0m, .58m], 1m, false), Is.EqualTo(26.1m));
        Assert.That(MonsterCaptureChance.Calculate(true, 40, 75, 100, [2m], 1.5m, false), Is.EqualTo(95m));
        Assert.That(MonsterCaptureChance.Calculate(true, 99, 1, 100, [5m], 10m, false), Is.EqualTo(95m));
    }

    [Test]
    public void EligibilityPrecedesGuaranteedAndSpeciesExtremesAndInvalidValuesAreRejected()
    {
        Assert.That(MonsterCaptureChance.Calculate(false, 0, 1, 100, [], 1m, true), Is.Null);
        foreach (var (hp, max) in new[] { (0, 100), (-1, 100), (1, 0), (101, 100), (1, -1) })
            Assert.That(MonsterCaptureChance.Calculate(true, 100, hp, max, [], 1m, true), Is.Null);
        foreach (var multiplier in new[] { -1m, 0m, 10.01m, .019m })
            Assert.That(MonsterCaptureChance.Calculate(true, 40, 75, 100, [], multiplier, true), Is.Null);
        Assert.That(MonsterCaptureChance.Calculate(true, 40, 75, 100, [5.01m], 1m, false), Is.Null);
        Assert.That(MonsterCaptureChance.Calculate(true, 40, 75, 100, [-.01m], 1m, false), Is.Null);
        Assert.That(MonsterCaptureChance.Calculate(true, 0, 75, 100, [5m], 10m, false), Is.Zero);
        Assert.That(MonsterCaptureChance.Calculate(true, 0, 75, 100, [0m], 1m, true), Is.EqualTo(100m));
        Assert.That(MonsterCaptureChance.Calculate(true, 100, 75, 100, [0m], 1m, false), Is.EqualTo(100m));
    }

    [Test]
    public void ContentMigrationAndJsonPreserveDefaultsAndExplicitZeroes()
    {
        Options.EnsureCreated();
        using var fixture = new DatabaseFixture();
        var item = new ItemDescriptor(Guid.NewGuid())
        {
            Name = "Orbe — 雪", Description = "Précision 1.50×", ItemType = ItemType.MonsterCaptureDevice, CaptureMultiplier = 1.5m, GuaranteedCapture = true,
        };
        var status = new MonsterStatusDescriptor(Guid.NewGuid()) { CaptureMultiplier = 0m };
        var species = new MonsterSpeciesDescriptor(Guid.NewGuid())
        {
            BaseCaptureChance = 0, Name = "Nivéole — 雪", Description = "Un esprit d'hiver.",
        };
        var gender = new MonsterGenderDescriptor(Guid.NewGuid()) { Name = "Lunaire", Symbol = "\u263E" };
        var evt = new EventDescriptor(Guid.NewGuid(), isCommon: true) { Name = "Halte \u2014 caf\u00E9" };
        var dialogue = new ShowTextCommand { Text = "Bienvenue \u2014 \u96EA!" };
        var page = new EventPage();
        page.CommandLists.Values.Single().Add(dialogue);
        evt.Pages = [page];
        var legacyItem = new ItemDescriptor(item.Id);
        var oldItemJson = JObject.Parse(item.JsonData);
        oldItemJson.Remove(nameof(ItemDescriptor.CaptureMultiplier));
        oldItemJson.Remove(nameof(ItemDescriptor.GuaranteedCapture));
        legacyItem.Load(oldItemJson.ToString());
        Assert.That(legacyItem.CaptureMultiplier, Is.EqualTo(1m));
        Assert.That(legacyItem.GuaranteedCapture, Is.False);
        var copy = new ItemDescriptor(item.Id);
        copy.Load(item.JsonData);
        Assert.That(copy.Name, Is.EqualTo(item.Name));
        Assert.That(copy.Description, Is.EqualTo(item.Description));
        Assert.That(copy.ItemType, Is.EqualTo(ItemType.MonsterCaptureDevice));
        Assert.That(copy.CaptureMultiplier, Is.EqualTo(1.5m));
        Assert.That(copy.GuaranteedCapture, Is.True);

        using (var db = fixture.Game())
        {
            db.GetService<IMigrator>().Migrate("20261114000000_AddMonsterEncounters");
            var script = db.GetService<IMigrator>().GenerateScript("20261114000000_AddMonsterEncounters");
            Assert.That(script, Does.Contain("DEFAULT 40"));
            db.Database.Migrate();
            Assert.That(db.Database.HasPendingModelChanges(), Is.False);
            db.Items.Add(item);
            db.MonsterSpecies.Add(species);
            db.MonsterStatuses.Add(status);
            db.MonsterGenders.Add(gender);
            db.Events.Add(evt);
            db.SaveChanges();
        }
        using var read = fixture.Game();
        var savedItem = read.Items.Single();
        Assert.That(savedItem.Name, Is.EqualTo(item.Name));
        Assert.That(savedItem.Description, Is.EqualTo(item.Description));
        Assert.That(savedItem.CaptureMultiplier, Is.EqualTo(1.5m));
        var savedSpecies = read.MonsterSpecies.Single();
        Assert.That(savedSpecies.Name, Is.EqualTo(species.Name));
        Assert.That(savedSpecies.Description, Is.EqualTo(species.Description));
        Assert.That(read.MonsterGenders.Single(row => row.Id == gender.Id).Symbol, Is.EqualTo(gender.Symbol));
        var savedEvent = read.Events.Single(row => row.Id == evt.Id);
        Assert.That(savedEvent.Name, Is.EqualTo(evt.Name));
        Assert.That(savedEvent.Pages.Single().CommandLists.Values.Single().OfType<ShowTextCommand>().Single().Text,
            Is.EqualTo(dialogue.Text));
        Assert.That(read.Items.Single().GuaranteedCapture, Is.True);
        Assert.That(read.MonsterSpecies.Single().BaseCaptureChance, Is.Zero);
        Assert.That(read.MonsterStatuses.Single().CaptureMultiplier, Is.Zero);
    }

    private sealed class LastRandom : IMonsterRandom
    {
        public List<long> Bounds { get; } = [];
        public long NextInt64(long bound) { Bounds.Add(bound); return bound - 1; }
    }

    private static MonsterBattleActionPacket Action(MonsterBattleSession session, MonsterBattleActionKind kind, Guid item = default) =>
        new() { BattleId = session.Id, Turn = session.Turn, Revision = session.Revision, Kind = kind, ItemId = item };

    private static MonsterBattleSession Session(Guid owner, Guid item, int rate, bool guaranteed,
        Func<bool> hasItem, Func<MonsterBattleSession.CaptureTurn, IReadOnlyList<MonsterBattleCombatant>, string?> commit,
        bool wild = true, bool enabled = true, LastRandom? random = null)
    {
        var type = new MonsterTypeDescriptor(Guid.NewGuid());
        var move = new MonsterMoveDescriptor(Guid.NewGuid()) { TypeId = type.Id, BasePower = 20, AlwaysHits = true, MaximumPP = 20 };
        MonsterBattleCombatant Actor(string name) => new(new(Guid.NewGuid(), 10, new(100, 10, 10, 10, 10, 10), [type.Id]),
            name, "", [new(move, type), null, null, null]);
        var opponent = Actor("Wild monster");
        var generated = new GeneratedWildMonster(opponent.Id, Guid.NewGuid(), 10, new(), null, null, [move.Id, null, null, null]);
        return new(new(owner, [Actor("Owned monster")]), new(null, [opponent]), MonsterBattleRules.Generation4, random ?? new(),
            wild: wild ? generated : null, baseCaptureChance: rate,
            captureDevices: () => [new() { Id = item, Name = "Basic device", Quantity = 1 }],
            captureDevice: enabled ? id => id == item && hasItem() ? new("Basic device", 1m, guaranteed) : null : null,
            persistCapture: commit);
    }

    [Test]
    public void FailedAttemptUsesTheTurnAndOneDeviceWhileStaleOrMissingInventoryUsesNeither()
    {
        var owner = Guid.NewGuid();
        var item = Guid.NewGuid();
        var quantity = 1;
        var random = new LastRandom();
        var session = Session(owner, item, 40, false, () => quantity > 0, (attempt, changes) =>
        {
            Assert.That(attempt.Target, Is.Null);
            Assert.That(changes.Single().HP, Is.LessThan(100));
            quantity--;
            return null;
        }, random: random);
        var snapshots = new List<MonsterBattleStatePacket>();
        var registry = new MonsterBattleRegistry((_, packet) => snapshots.Add(packet));
        registry.Start(owner, () => session);
        Assert.That(snapshots.Last().CaptureDevices.Single().Id, Is.EqualTo(item));
        Assert.Throws<InvalidOperationException>(() => registry.Submit(owner, Action(session, MonsterBattleActionKind.Capture, Guid.NewGuid())));
        Assert.That(quantity, Is.EqualTo(1));
        Assert.That(session.Turn, Is.EqualTo(1));

        var action = Action(session, MonsterBattleActionKind.Capture, item);
        registry.Submit(owner, action);
        Assert.That(random.Bounds.First(), Is.EqualTo(100_000_000));
        Assert.That(quantity, Is.Zero);
        Assert.That(session.Turn, Is.EqualTo(2));
        Assert.That(session.Outcome, Is.EqualTo(MonsterBattleOutcome.Active));
        Assert.That(snapshots.Last().Events.Take(3).Select(entry => entry.Kind), Is.EqualTo(new[]
        {
            MonsterBattleEventKind.CaptureDeviceUsed, MonsterBattleEventKind.CaptureFailed, MonsterBattleEventKind.MoveUsed,
        }));
        Assert.That(snapshots.Last().Events[2].ActorId, Is.EqualTo(session.Wild!.Id));
        Assert.Throws<InvalidOperationException>(() => registry.Submit(owner, action));
        Assert.Throws<InvalidOperationException>(() => registry.Submit(owner, Action(session, MonsterBattleActionKind.Capture, item)));
        Assert.That(quantity, Is.Zero);
        Assert.That(session.Turn, Is.EqualTo(2));
    }

    [Test]
    public void PracticeAndCaptureDisabledEventRejectEvenGuaranteedDevices()
    {
        var owner = Guid.NewGuid();
        var item = Guid.NewGuid();
        foreach (var (wild, enabled) in new[] { (false, true), (true, false) })
        {
            var session = Session(owner, item, 100, true, () => true, (_, _) => throw new AssertionException("Rejected device committed."),
                wild, enabled);
            Assert.That(session.Snapshot(1).AllowsCapture, Is.False);
            Assert.That(session.Snapshot(1).CaptureDevices, Is.Empty);
            Assert.Throws<InvalidOperationException>(() => session.Submit(owner, Action(session, MonsterBattleActionKind.Capture, item)));
            Assert.That(session.Turn, Is.EqualTo(1));
        }
    }

    [Test]
    public void CapturedEventWaitsForAcknowledgmentAndOnlyResumesTheCorrectFrame()
    {
        var owner = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();
        var item = Guid.NewGuid();
        var calls = 0;
        var session = Session(owner, item, 0, true, () => true, (attempt, changes) =>
        {
            Assert.That(attempt.Target!.Id, Is.EqualTo(attempt.Wild!.Id));
            Assert.That(changes, Is.Empty);
            calls++;
            return "party slot 2";
        });
        var other = Session(otherOwner, item, 100, false, () => true, (_, _) => "party slot 3");
        var snapshots = new Dictionary<Guid, MonsterBattleStatePacket>();
        var registry = new MonsterBattleRegistry((id, packet) => snapshots[id] = packet);
        var page = new EventPage();
        var command = new StartMonsterBattleCommand(page.CommandLists) { SpeciesId = Guid.NewGuid(), AllowCapture = true };
        page.CommandLists[command.Capture].Add(new ShowTextCommand { Text = "Captured branch" });
        var legacy = new StartMonsterBattleCommand { Victory = command.Victory, Defeat = command.Defeat, Escape = command.Escape };
        Assert.That(legacy.AllowCapture, Is.False);
        Assert.That(legacy.Branches(), Has.Length.EqualTo(3));
        legacy.EnsureCaptureBranch(page.CommandLists);
        Assert.That(legacy.Branches(), Has.Length.EqualTo(4));
        var ownFrame = new CommandInstance(page);
        var otherFrame = new CommandInstance(page);
        var ownResult = new TaskCompletionSource<MonsterBattleOutcome>();
        var otherResult = new TaskCompletionSource<MonsterBattleOutcome>();
        Assert.That(ownFrame.TryBeginMonsterBattle(command, () => (registry.Start(owner, () => session, ownResult), ownResult.Task)), Is.True);
        Assert.That(otherFrame.TryBeginMonsterBattle(command, () => (registry.Start(otherOwner, () => other, otherResult), otherResult.Task)), Is.True);
        var capture = Action(session, MonsterBattleActionKind.Capture, item);
        registry.Submit(owner, capture);
        Assert.That(calls, Is.EqualTo(1));
        var terminal = snapshots[owner];
        Assert.That(terminal.Outcome, Is.EqualTo(MonsterBattleOutcome.Captured));
        Assert.That(terminal.AwaitingPresentationClose, Is.True);
        Assert.That(terminal.Events.Select(entry => entry.Kind), Is.EqualTo(new[]
        {
            MonsterBattleEventKind.CaptureDeviceUsed, MonsterBattleEventKind.BattleFinished,
        }));
        Assert.That(terminal.Events.Last().Text, Does.Contain("party slot 2"));
        Assert.That(ownFrame.TryConsumeMonsterBattle(out _), Is.False);
        Assert.Throws<InvalidOperationException>(() => registry.Submit(owner, capture));
        var ack = Action(session, MonsterBattleActionKind.PresentationClosed);
        registry.Submit(otherOwner, ack);
        Assert.That(otherFrame.TryConsumeMonsterBattle(out _), Is.False);
        registry.Submit(owner, ack);
        registry.Submit(owner, ack);
        Assert.That(ownFrame.TryConsumeMonsterBattle(out var branch), Is.True);
        Assert.That(branch!.CommandList, Is.SameAs(page.CommandLists[command.Capture]));
        Assert.That(ownFrame.TryConsumeMonsterBattle(out _), Is.False);
        Assert.That(calls, Is.EqualTo(1));

        registry.Submit(otherOwner, Action(other, MonsterBattleActionKind.Capture, item));
        registry.Cancel(otherOwner, "Disconnected", disconnect: true);
        Assert.That(otherFrame.TryConsumeMonsterBattle(out var cancelled), Is.True);
        Assert.That(cancelled, Is.Null);
    }

    [Test]
    public void CapturedEventPageCanBeHiddenForOnlyTheCapturingPlayer()
    {
        Options.EnsureCreated();
        var flag = new PlayerVariableDescriptor(Guid.NewGuid()) { Name = "Caught this encounter" };
        PlayerVariableDescriptor.Lookup.Set(flag.Id, flag);
        try
        {
            var first = new Player { Name = "Caught" };
            var second = new Player { Name = "Not caught" };
            var page = new EventPage
            {
                ConditionLists = new()
                {
                    Lists = [new() { Conditions = [new VariableIsCondition
                    {
                        VariableId = flag.Id,
                        Comparison = new BooleanVariableComparison { ComparingEqual = true, Value = false },
                    }] }],
                },
            };
            Assert.That(Conditions.CanSpawnPage(page, first, null!), Is.True);
            Assert.That(Conditions.CanSpawnPage(page, second, null!), Is.True);
            first.GetVariableValue(flag.Id).Boolean = true;
            Assert.That(Conditions.CanSpawnPage(page, first, null!), Is.False);
            Assert.That(Conditions.CanSpawnPage(page, second, null!), Is.True);
        }
        finally { PlayerVariableDescriptor.Lookup.DeleteAt(flag.Id); }
    }

    [Test]
    public void AtomicCommitPreservesTheExactMonsterAndUsesStorageWhenPartyIsFull()
    {
        Options.EnsureCreated();
        using var fixture = new DatabaseFixture();
        var (account, player) = fixture.AddPlayer();
        var owner = player.Id;
        var item = Guid.NewGuid();
        var slot = player.Items[0];
        slot.ItemId = item;
        slot.Quantity = 3;
        var species = new MonsterSpeciesDescriptor(Guid.NewGuid());
        var move = Guid.NewGuid();
        var service = new MonsterOwnershipService(fixture.Player, id => id == species.Id, 1,
            species: _ => species, moveExists: id => id == move);
        var status = Guid.NewGuid();
        var ivs = new MonsterStatValues(3, 4, 5, 6, 7, 8);
        var evs = new MonsterStatValues(1, 2, 3, 4, 5, 6);
        var wild = new GeneratedWildMonster(Guid.NewGuid(), species.Id, 13, ivs, Guid.NewGuid(), Guid.NewGuid(), [move, null, null, null])
            { Experience = 22, EVs = evs };
        var condition = new MonsterOwnershipService.BattleCondition(wild.Id, Guid.Empty, 15, 20,
            [new(move, 3, 9), new(null, 0, 0), new(null, 0, 0), new(null, 0, 0)], status, 2);
        var receipt = Guid.NewGuid();

        fixture.BeforeCommitFailure = true;
        Assert.Throws<IOException>(() => service.CommitCapture(owner, slot, item, receipt, [], wild, condition));
        Assert.That(slot.Quantity, Is.EqualTo(3));
        using (var before = fixture.Player())
        {
            Assert.That(before.OwnedMonsters, Is.Empty);
            Assert.That(before.Player_Items, Is.Empty);
        }

        fixture.BeforeCommitFailure = false;
        fixture.AfterCommitFailure = true;
        var caught = service.CommitCapture(owner, slot, item, receipt, [], wild, condition);
        fixture.AfterCommitFailure = false;
        Assert.That(caught!.Id, Is.EqualTo(wild.Id));
        Assert.That(caught.Location, Is.EqualTo(MonsterLocation.Party));
        Assert.That(caught.Slot, Is.Zero);
        Assert.That(slot.Quantity, Is.EqualTo(2));
        Assert.Throws<InvalidOperationException>(() => service.CommitCapture(owner, slot, item, receipt, [], wild, condition));
        Assert.That(slot.Quantity, Is.EqualTo(2));

        // The captured UUID is also unique when a caller supplies another receipt.
        Assert.Throws<DbUpdateException>(() => service.CommitCapture(owner, slot, item, Guid.NewGuid(), [], wild, condition));
        Assert.That(slot.Quantity, Is.EqualTo(2));

        using var staleSave = fixture.Player();
        var staleSlot = staleSave.Player_Items.Single();
        var next = wild with { Id = Guid.NewGuid() };
        var second = service.CommitCapture(owner, slot, item, Guid.NewGuid(), [], next, condition with { Id = next.Id });
        Assert.That(second!.Location, Is.EqualTo(MonsterLocation.Storage));
        Assert.That(second.Slot, Is.Zero);
        Assert.That(slot.Quantity, Is.EqualTo(1));
        staleSlot.Quantity = 10;
        // Intersect's context reports an EF concurrency rejection as -1.
        Assert.That(staleSave.SaveChanges(), Is.EqualTo(-1));
        var retaliated = condition with { ExpectedRevision = receipt, HP = condition.HP - 1 };
        fixture.BeforeCommitFailure = true;
        Assert.Throws<IOException>(() => service.CommitCapture(owner, slot, item, Guid.NewGuid(), [retaliated], null, null));
        Assert.That(slot.Quantity, Is.EqualTo(1));
        using (var beforeRetaliation = fixture.Player())
            Assert.That(beforeRetaliation.OwnedMonsters.Single(monster => monster.Id == wild.Id).CurrentHP, Is.EqualTo(condition.HP));
        fixture.BeforeCommitFailure = false;
        fixture.AfterCommitFailure = true;
        Assert.That(service.CommitCapture(owner, slot, item, Guid.NewGuid(), [retaliated], null, null), Is.Null);
        Assert.That(slot.ItemId, Is.EqualTo(Guid.Empty));
        Assert.That(slot.Quantity, Is.Zero);
        fixture.AfterCommitFailure = false;
        using (var autosave = fixture.Player())
            Assert.That(account.Save(autosave, force: true), Is.EqualTo(UserSaveResult.Completed));

        using var reopened = fixture.Player();
        var saved = reopened.OwnedMonsters.AsNoTracking().Single(monster => monster.Id == wild.Id);
        Assert.That(reopened.OwnedMonsters.Count(), Is.EqualTo(2));
        Assert.That(saved.OwnerPlayerId, Is.EqualTo(owner));
        Assert.That(saved.SpeciesId, Is.EqualTo(wild.SpeciesId));
        Assert.That(saved.Level, Is.EqualTo(wild.Level));
        Assert.That(saved.Experience, Is.EqualTo(wild.Experience));
        Assert.That(saved.IVs, Is.EqualTo(wild.IVs));
        Assert.That(saved.EVs, Is.EqualTo(wild.EVs));
        Assert.That(saved.NatureId, Is.EqualTo(wild.NatureId));
        Assert.That(saved.GenderId, Is.EqualTo(wild.GenderId));
        Assert.That(saved.CurrentHP, Is.EqualTo(retaliated.HP));
        var savedStorage = reopened.OwnedMonsters.AsNoTracking().Single(monster => monster.Id == next.Id);
        Assert.That(savedStorage.CurrentHP, Is.EqualTo(condition.HP));
        Assert.That(savedStorage.MovePP, Is.EqualTo(condition.PP));
        Assert.That(savedStorage.StatusId, Is.EqualTo(status));
        Assert.That(saved.MovePP, Is.EqualTo(condition.PP));
        Assert.That(saved.StatusId, Is.EqualTo(status));
        Assert.That(saved.StatusTurns, Is.EqualTo(2));
        Assert.That(reopened.Player_Items.Single().Quantity, Is.Zero);
        Assert.That(reopened.Player_Items.Single().Id, Is.EqualTo(slot.Id));
    }

    private sealed class DatabaseFixture : IDisposable
    {
        private readonly string _prefix = Path.Combine(Path.GetTempPath(), $"intersect-capture-{Guid.NewGuid():N}");
        public bool BeforeCommitFailure { get; set; }
        public bool AfterCommitFailure { get; set; }

        private DatabaseContextOptions OptionsFor(string purpose) => new()
        {
            DatabaseType = DatabaseType.Sqlite, AutoDetectChanges = true, KillServerOnConcurrencyException = false,
            ConnectionStringBuilder = new SqliteConnectionStringBuilder { DataSource = _prefix + purpose + ".db", Pooling = false },
        };

        public SqliteGameContext Game() => new(OptionsFor("-game"));

        public SqlitePlayerContext Player()
        {
            var db = new SqlitePlayerContext(OptionsFor("-player"));
            db.SavingChanges += (_, _) => { if (BeforeCommitFailure) throw new IOException("Before commit"); };
            db.SavedChanges += (_, _) => { if (AfterCommitFailure) throw new IOException("Ambiguous commit"); };
            return db;
        }

        public (User Account, Player Character) AddPlayer()
        {
            using var db = Player();
            db.Database.Migrate();
            Assert.That(db.Database.HasPendingModelChanges(), Is.False);
            var player = new Player { Name = "Capture Trainer" };
            Array.Fill(player.Equipment, -1);
            var account = new User { Name = "capture-test", Email = "capture@example.invalid", Players = [player] };
            db.Users.Add(account);
            db.SaveChanges();
            return (account, player);
        }

        public void Dispose()
        {
            File.Delete(_prefix + "-player.db");
            File.Delete(_prefix + "-game.db");
        }
    }
}
