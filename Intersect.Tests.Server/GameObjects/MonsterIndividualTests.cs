using System.Reflection;
using Intersect.Config;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Server.Core.CommandParsing;
using Intersect.Server.Core.Commands;
using Intersect.Server.Database;
using Intersect.Server.Database.GameData;
using Intersect.Server.Database.PlayerData;
using Intersect.Server.Database.PlayerData.Security;
using Intersect.Server.Database.PlayerData.Players;
using Intersect.Server.Entities;
using Intersect.Server.Monsters;
using Intersect.Server.Monsters.Calculations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Intersect.Tests.Server.GameObjects;

[TestFixture, NonParallelizable]
public class MonsterIndividualTests
{
    private string _directory = null!;
    private DatabaseContextOptions _options = null!;
    private Guid _owner, _species;
    private PlayerContext Context() => new SqlitePlayerContext(_options);
    private MonsterOwnershipService Service(IMonsterRandom random, IEnumerable<MonsterNatureDescriptor>? natures = null, ILogger? logger = null,
        MonsterBattleRules? rules = null) => new(Context, id => id == _species, 6, random, () => natures ?? Array.Empty<MonsterNatureDescriptor>(), rules, logger);
    [SetUp]
    public void Setup()
    {
        Options.EnsureCreated();
        _directory = Path.Combine(Path.GetTempPath(), "Intersect-IndividualStats-" + Guid.NewGuid());
        Directory.CreateDirectory(_directory);
        _options = new DatabaseContextOptions
        {
            DatabaseType = DatabaseType.Sqlite, AutoDetectChanges = true,
            ConnectionStringBuilder = new SqliteConnectionStringBuilder { DataSource = Path.Combine(_directory, "player.db"), Pooling = false },
        };
        _species = Guid.NewGuid();
        using var db = Context();
        db.Database.Migrate();
        var player = new Player { Name = "Individual Tester" };
        db.Users.Add(new User { Name = "individual-test", Email = "test@example.invalid", Power = UserRights.None, Players = [player] });
        db.SaveChanges();
        _owner = player.Id;
    }
    [TearDown]
    public void Cleanup()
    {
        if (Path.GetDirectoryName(Path.GetFullPath(_directory)) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())))
            throw new InvalidOperationException("Not a disposable test directory.");
        Directory.Delete(_directory, true);
    }
    [Test]
    public void GeneratedAttributesSurviveRetryReloadPlacementAndNicknameChangesWithoutRerolling()
    {
        var nature = new MonsterNatureDescriptor(Guid.NewGuid());
        var random = new ControlledMonsterRandom(0, 31, 1, 2, 3, 4, 0);
        var request = Guid.NewGuid();
        var summary = Service(random, new[] { nature }).Grant(_owner, _species, request);
        Guid revision;
        using (var db = Context())
        {
            var monster = db.OwnedMonsters.Single();
            Assert.That(monster.IVs, Is.EqualTo(new MonsterStatValues(0, 31, 1, 2, 3, 4)));
            Assert.That(monster.EVs, Is.EqualTo(new MonsterStatValues()));
            Assert.That(monster.NatureId, Is.EqualTo(nature.Id));
            revision = monster.Revision;
            db.Entry(monster).Property("Nickname").CurrentValue = "Unchanged attributes";
            db.SaveChanges();
        }
        var noDraw = new ControlledMonsterRandom();
        var service = Service(noDraw, new[] { nature });
        Assert.That(service.Grant(_owner, _species, request).Id, Is.EqualTo(summary.Id));
        service.Move(_owner, summary.Id, revision, MonsterLocation.Storage);
        var stored = service.GetOwned(_owner).Single();
        service.Move(_owner, summary.Id, stored.Revision, MonsterLocation.Party);
        service.Grant(_owner, _species, request);
        using (var db = Context())
        {
            var monster = db.OwnedMonsters.Single();
            Assert.That(monster.Id, Is.EqualTo(summary.Id));
            Assert.That(monster.IVs, Is.EqualTo(new MonsterStatValues(0, 31, 1, 2, 3, 4)));
            Assert.That(monster.NatureId, Is.EqualTo(nature.Id));
            Assert.That(monster.Nickname, Is.EqualTo("Unchanged attributes"));
            Assert.That(monster.Location, Is.EqualTo(MonsterLocation.Party));
        }
        Assert.That(random.Bounds.Count, Is.EqualTo(7));
        Assert.That(noDraw.Bounds, Is.Empty);
    }
    private sealed class WarningLogger : ILogger
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (level == LogLevel.Warning) Messages.Add(formatter(state, exception)); }
    }
    [Test]
    public void EmptyAndZeroWeightCatalogsLogAndPreserveNeutralUnassignedIndividuals()
    {
        foreach (var natures in new[] { Array.Empty<MonsterNatureDescriptor>(), new[] { new MonsterNatureDescriptor { SelectionWeight = 0 } } })
        {
            var logger = new WarningLogger();
            var service = Service(new ControlledMonsterRandom(1, 2, 3, 4, 5, 6), natures, logger);
            var request = Guid.NewGuid();
            var result = service.Grant(_owner, _species, request);
            Assert.That(service.Grant(_owner, _species, request).Id, Is.EqualTo(result.Id));
            Assert.That(logger.Messages.Count, Is.EqualTo(1));
            Assert.That(logger.Messages.Single(), Does.Contain("neutral/unassigned"));
            using var db = Context();
            var monster = db.OwnedMonsters.Single(item => item.Id == result.Id);
            Assert.That(monster.NatureId, Is.Null);
            Assert.That(monster.IVs, Is.EqualTo(new MonsterStatValues(1, 2, 3, 4, 5, 6)));
        }
    }
    [Test]
    public void EvAdjustmentEnforcesPerStatTotalOwnershipAndRevisionAndPersists()
    {
        var service = Service(new ControlledMonsterRandom(0, 0, 0, 0, 0, 0));
        var monster = service.Grant(_owner, _species, Guid.NewGuid());
        Assert.Throws<ArgumentOutOfRangeException>(() => service.SetEVs(_owner, monster.Id, monster.Revision, new(Attack: 256)));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.SetEVs(_owner, monster.Id, monster.Revision, new(HP: 255, Attack: 255, Speed: 1)));
        Assert.Throws<InvalidOperationException>(() => service.SetEVs(Guid.NewGuid(), monster.Id, monster.Revision, new(Attack: 4)));
        service.SetEVs(_owner, monster.Id, monster.Revision, new(HP: 255, Attack: 255));
        Assert.Throws<InvalidOperationException>(() => service.SetEVs(_owner, monster.Id, monster.Revision, new()));
        using var db = Context();
        var loaded = db.OwnedMonsters.Single();
        Assert.That(loaded.EVs, Is.EqualTo(new MonsterStatValues(HP: 255, Attack: 255)));
        Assert.That(loaded.IVs, Is.EqualTo(new MonsterStatValues()));
    }
    [Test]
    public void CustomEvRulesDoNotTruncateProfilesOrBlockPlacementWhenCapsAreLaterLowered()
    {
        var custom = Service(new ControlledMonsterRandom(0, 0, 0, 0, 0, 0), rules: MonsterBattleRules.Generation4 with { MaxEvTotal = 1000 });
        var created = custom.Grant(_owner, _species, Guid.NewGuid());
        custom.SetEVs(_owner, created.Id, created.Revision, new(HP: 255, Attack: 255, Defense: 255));
        var current = custom.GetOwned(_owner).Single();
        Service(new ControlledMonsterRandom()).Move(_owner, current.Id, current.Revision, MonsterLocation.Storage);
        using var db = Context();
        var loaded = db.OwnedMonsters.Single();
        Assert.That(loaded.EVs, Is.EqualTo(new MonsterStatValues(255, 255, 255)));
        Assert.That(loaded.Location, Is.EqualTo(MonsterLocation.Storage));
    }

    [Test]
    public void PersistenceBoundaryRejectsRerollingInitializedIvAndNatureFields()
    {
        var result = Service(new ControlledMonsterRandom(1, 2, 3, 4, 5, 6)).Grant(_owner, _species, Guid.NewGuid());
        using var db = Context();
        var monster = db.OwnedMonsters.Single();
        db.Entry(monster).Property("IvHP").CurrentValue = 7;
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.ThrowsAsync<InvalidOperationException>(async () => await db.SaveChangesAsync());
        db.Entry(monster).Property("IvHP").CurrentValue = 1;
        db.Entry(monster).Property("NatureId").CurrentValue = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        using var verify = Context();
        Assert.That(verify.OwnedMonsters.Single(item => item.Id == result.Id).IVs.HP, Is.EqualTo(1));
        Assert.That(verify.OwnedMonsters.Single().NatureId, Is.Null);
    }
    [Test]
    public void NatureDeletionChecksOfflinePersistentOwnershipAndCurrentDefinitionsChangeDerivedStats()
    {
        var nature = new MonsterNatureDescriptor(Guid.NewGuid())
        { Modifiers = new() { Increase = new() { Stat = MonsterStat.Attack, Percentage = 10 } } };
        var result = Service(new ControlledMonsterRandom(31, 31, 31, 31, 31, 31, 0), new[] { nature }).Grant(_owner, _species, Guid.NewGuid());
        Assert.That(MonsterNatureReferences.IsAssigned(nature.Id, Context), Is.True);
        Assert.Throws<InvalidOperationException>(() => MonsterNatureReferences.EnsureUnreferenced(nature.Id, Context));
        using var game = new SqliteGameContext(new DatabaseContextOptions
        {
            DatabaseType = DatabaseType.Sqlite, AutoDetectChanges = true,
            ConnectionStringBuilder = new SqliteConnectionStringBuilder { DataSource = Path.Combine(_directory, "game.db"), Pooling = false },
        });
        game.Database.EnsureCreated();
        game.MonsterNatures.Add(nature); game.SaveChanges();
        game.NatureReferenceExists = id => MonsterNatureReferences.IsAssigned(id, Context);
        game.MonsterNatures.Remove(nature);
        Assert.Throws<InvalidOperationException>(() => game.SaveChanges());
        game.Entry(nature).State = EntityState.Unchanged;
        var species = new MonsterSpeciesDescriptor(_species) { BaseStats = new() { HP = 45, Attack = 45 } };
        using var players = Context();
        var monster = players.OwnedMonsters.Single(item => item.Id == result.Id);
        var before = MonsterStatCalculator.Calculate(species.BaseStats, 1, monster.IVs, monster.EVs, nature, MonsterBattleRules.Generation4);
        nature.Modifiers.Increase.Percentage = 50;
        game.SaveChanges();
        var after = MonsterStatCalculator.Calculate(species.BaseStats, 1, monster.IVs, monster.EVs, nature, MonsterBattleRules.Generation4);
        Assert.That(after.Attack, Is.GreaterThan(before.Attack));
        Assert.That(monster.NatureId, Is.EqualTo(nature.Id));
    }
    [Test]
    public void LegacyMigrationAddsStableZerosWithoutChangingIdentityOwnerOrPlacement()
    {
        var id = Guid.NewGuid(); var request = Guid.NewGuid(); var revision = Guid.NewGuid();
        using (var db = Context())
        {
            db.GetService<IMigrator>().Migrate("20261108000000_AddOwnedMonsters");
            db.Database.ExecuteSqlInterpolated($"INSERT INTO OwnedMonsters (Id, SpeciesId, OwnerPlayerId, Level, Experience, Nickname, Location, Slot, CreatedAtUtcMilliseconds, GrantRequestId, Revision) VALUES ({id}, {_species}, {_owner}, {37}, {345L}, {"Legacy Buddy"}, {1}, {2}, {12345L}, {request}, {revision})");
            db.Database.Migrate();
            Assert.That(db.Database.HasPendingModelChanges(), Is.False);
        }
        using (var db = Context())
        {
            var monster = db.OwnedMonsters.Single();
            Assert.That((monster.Id, monster.OwnerPlayerId, monster.SpeciesId, monster.Level, monster.Experience, monster.Nickname, monster.Location, monster.Slot, monster.CreatedAtUtcMilliseconds, monster.GrantRequestId, monster.Revision),
                Is.EqualTo((id, _owner, _species, 37, 345L, "Legacy Buddy", MonsterLocation.Storage, 2, 12345L, request, revision)));
            Assert.That(monster.IVs, Is.EqualTo(new MonsterStatValues()));
            Assert.That(monster.EVs, Is.EqualTo(new MonsterStatValues()));
            Assert.That(monster.NatureId, Is.Null);
        }
        Assert.That(Service(new ControlledMonsterRandom()).Grant(_owner, _species, request).Id, Is.EqualTo(id));
    }
    [Test]
    public void ReadOnlyInspectionShowsAttributesAndComputedStatsWithoutChangingRevisionOrCount()
    {
        var result = Service(new ControlledMonsterRandom(31, 0, 1, 2, 3, 4)).Grant(_owner, _species, Guid.NewGuid());
        using var db = Context();
        var monster = db.OwnedMonsters.AsNoTracking().Single();
        var revision = monster.Revision;
        var output = MonsterDiagnostics.Describe(monster, new MonsterSpeciesDescriptor(_species) { Name = "Diagnostic species" }, null, MonsterBattleRules.Generation4);
        Assert.That(output, Does.Contain("neutral/unassigned"));
        Assert.That(output, Does.Contain("IVs: 31 / 0 / 1 / 2 / 3 / 4"));
        Assert.That(output, Does.Contain("Calculated stats:"));
        Assert.That(output, Does.Not.Contain(_owner.ToString()));
        Assert.That(db.ChangeTracker.Entries(), Is.Empty);
        Assert.That(db.OwnedMonsters.Single().Revision, Is.EqualTo(revision));
        var parser = new CommandParser(); parser.Register<InspectMonsterCommand>();
        Assert.That(parser.Parse("inspectmonster " + result.Id).Missing, Is.Empty);
        Assert.That(parser.Parse("inspectmonster").Missing.Count, Is.EqualTo(1));
    }
    [Test]
    public void MySqlAttributeMigrationMatchesModelWithoutConnecting()
    {
        using var db = new OfflineMySql(_options with
        {
            DatabaseType = DatabaseType.MySql,
            ConnectionStringBuilder = new MySqlConnector.MySqlConnectionStringBuilder { Server = "127.0.0.1", Database = "unused_attrs", UserID = "unused" },
        });
        var migration = new Intersect.Server.Migrations.MySql.Player.AddMonsterAttributes();
        var target = db.GetService<IModelRuntimeInitializer>().Initialize(new Intersect.Server.Migrations.MySql.Player.AddMonsterMoveSlots().TargetModel, designTime: true);
        var current = db.GetService<IDesignTimeModel>().Model;
        Assert.That(db.GetService<IMigrationsModelDiffer>().GetDifferences(target.GetRelationalModel(), current.GetRelationalModel()), Is.Empty);
        var sql = string.Join("\n", db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, current).Select(command => command.CommandText));
        Assert.That(sql, Does.Contain("ADD `NatureId` char(36)"));
        Assert.That(sql, Does.Contain("ADD `IvHP` int NOT NULL DEFAULT 0"));
        Assert.That(sql, Does.Not.Contain("DROP TABLE"));
    }
    [Test]
    public void RosterDetailsAreOwnerScopedPersistedAndMatchInspectionCalculations()
    {
        var nature = new MonsterNatureDescriptor(Guid.NewGuid())
        { Name = "Custom 15.5%", Modifiers = new() { Increase = new() { Stat = MonsterStat.Attack, Percentage = 15.5m } } };
        var service = Service(new ControlledMonsterRandom(0, 31, 1, 2, 3, 4, 0), [nature]);
        var created = service.Grant(_owner, _species, Guid.NewGuid());
        var species = new MonsterSpeciesDescriptor(_species) { Name = "Roster species" };
        var calls = 0;
        OwnedMonsterDetails Describe(MonsterInstance monster)
        {
            calls++;
            return MonsterRosterDetails.Create(monster, species, nature, MonsterBattleRules.Generation4);
        }
        Assert.That(service.GetOwned(Guid.NewGuid(), Describe), Is.Empty);
        Assert.That(calls, Is.Zero, "Foreign rows must never reach the detail mapper.");
        var summary = service.GetOwned(_owner, Describe).Single();
        Assert.That(summary.Details!.NatureId, Is.EqualTo(nature.Id));
        Assert.That(summary.Details.NatureName, Is.EqualTo(nature.Name));
        Assert.That(summary.Details.IVs, Is.EqualTo(new long[] { 0, 31, 1, 2, 3, 4 }));
        Assert.That(summary.Details.EVs, Is.EqualTo(new long[6]));
        using var db = Context();
        var loaded = db.OwnedMonsters.Single();
        var expected = MonsterStatCalculator.Calculate(species.BaseStats, loaded.Level, loaded.IVs, loaded.EVs, nature, MonsterBattleRules.Generation4).ToArray();
        Assert.That(summary.Details.CalculatedStats, Is.EqualTo(expected));
        Assert.That(MonsterDiagnostics.Describe(loaded, species, nature, MonsterBattleRules.Generation4), Does.Contain(string.Join(" / ", expected)));
        var packet = new Intersect.Network.Packets.Server.OwnedMonstersPacket { PlayerId = _owner, Monsters = [summary] };
        var bytes = MessagePack.MessagePackSerializer.Serialize(packet);
        var roundTrip = MessagePack.MessagePackSerializer.Deserialize<Intersect.Network.Packets.Server.OwnedMonstersPacket>(bytes);
        Assert.That(roundTrip.Monsters.Single().Details!.CalculatedStats, Is.EqualTo(expected));
        service.Move(_owner, created.Id, created.Revision, MonsterLocation.Storage);
        var reconnected = Service(new ControlledMonsterRandom()).GetOwned(_owner, Describe).Single();
        Assert.That(reconnected.Location, Is.EqualTo(MonsterLocation.Storage));
        Assert.That(reconnected.Details!.IVs, Is.EqualTo(summary.Details.IVs));
        Assert.That(reconnected.Details.CalculatedStats, Is.EqualTo(expected));
        var missing = MonsterRosterDetails.Create(loaded, null, nature, MonsterBattleRules.Generation4);
        Assert.That(missing.CalculatedStats, Is.Null);
        Assert.That(missing.CalculationError, Is.Not.Empty);
    }

    [Test]
    public void NeutralRosterDetailsDoNotInventAnAssignedNature()
    {
        Service(new ControlledMonsterRandom(1, 2, 3, 4, 5, 6)).Grant(_owner, _species, Guid.NewGuid());
        using var db = Context();
        var details = MonsterRosterDetails.Create(db.OwnedMonsters.Single(), new MonsterSpeciesDescriptor(_species), null, MonsterBattleRules.Generation4);
        Assert.That(details.NatureId, Is.Null);
        Assert.That(details.NatureName, Is.Null);
        Assert.That(details.CalculatedStats, Has.Length.EqualTo(6));
        Assert.That(details.CalculationError, Is.Null);
    }

    [Test]
    public void MoveSlotsPersistInitializeLegacyOnceAndProtectOfflineAssignments()
    {
        var moveIds = Enumerable.Range(1, 5).Select(i => new Guid(i, 0, 0, new byte[8])).ToArray();
        var species = new MonsterSpeciesDescriptor(_species) { Learnset = moveIds.Select(id => new MonsterLearnsetEntry { MoveId = id, Level = 1 }).ToList() };
        MonsterOwnershipService WithMoves() => new(Context, id => id == _species, 6, new ControlledMonsterRandom(0, 0, 0, 0, 0, 0),
            species: _ => species, moveExists: moveIds.Contains);
        var grant = WithMoves().Grant(_owner, _species, Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => WithMoves().GetForBattle(Guid.NewGuid(), grant.Id));
        using (var db = Context())
        {
            var owned = db.OwnedMonsters.Single();
            Assert.That(owned.MovesInitialized, Is.True);
            Intersect.Server.Monsters.Battles.MonsterPracticeBattles.ValidateOwnedParty(_owner, owned);
            Assert.Throws<InvalidOperationException>(() => Intersect.Server.Monsters.Battles.MonsterPracticeBattles.ValidateOwnedParty(Guid.NewGuid(), owned));
            Assert.That(owned.MoveIds, Is.EqualTo(moveIds.Take(4).Cast<Guid?>().ToArray()));
            // Simulate a future authorized saved choice, including deliberately empty slots.
            db.MonsterMoveExists = moveIds.Contains;
            db.Entry(owned).Property("Move1Id").CurrentValue = moveIds[4];
            for (var i = 2; i <= 4; i++) db.Entry(owned).Property("Move" + i + "Id").CurrentValue = null;
            db.SaveChanges();
        }
        species.Learnset.Clear();
        var service = WithMoves(); service.GetOwned(_owner);
        service.Move(_owner, grant.Id, grant.Revision, MonsterLocation.Storage);
        using (var db = Context())
        {
            var owned = db.OwnedMonsters.Single();
            Assert.That(owned.MoveIds, Is.EqualTo(new Guid?[] { moveIds[4], null, null, null }));
            Assert.That(owned.Location, Is.EqualTo(MonsterLocation.Storage));
            Assert.Throws<InvalidOperationException>(() => Intersect.Server.Monsters.Battles.MonsterPracticeBattles.ValidateOwnedParty(_owner, owned));
            Assert.That(MonsterMoveAssignment.IsAssigned(moveIds[4], Context), Is.True);
            Assert.Throws<InvalidOperationException>(() => MonsterMoveAssignment.EnsureUnreferenced(moveIds[4], Context));
            db.MonsterMoveExists = moveIds.Contains;
            db.Entry(owned).Property("Move2Id").CurrentValue = moveIds[4];
            Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
            db.Entry(owned).Property("Move2Id").CurrentValue = Guid.NewGuid();
            Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        }
        using (var db = Context())
        {
            db.GetService<IMigrator>().Migrate("20261108040000_AddMonsterAttributes");
            db.Database.Migrate();
            Assert.That(db.Database.HasPendingModelChanges(), Is.False);
            Assert.That(db.OwnedMonsters.Single().MovesInitialized, Is.False);
        }
        species.Learnset.Add(new() { MoveId = moveIds[0], Level = 1 });
        WithMoves().GetOwned(_owner);
        species.Learnset.Clear();
        WithMoves().GetOwned(_owner);
        using (var db = Context())
        {
            var owned = db.OwnedMonsters.Single();
            Assert.That(owned.MoveIds, Is.EqualTo(new Guid?[] { moveIds[0], null, null, null }));
            Assert.That(owned.IVs, Is.EqualTo(new MonsterStatValues()));
            Assert.That(owned.Id, Is.EqualTo(grant.Id));
        }
        using var game = new SqliteGameContext(new DatabaseContextOptions
        {
            DatabaseType = DatabaseType.Sqlite, AutoDetectChanges = true,
            ConnectionStringBuilder = new SqliteConnectionStringBuilder { DataSource = Path.Combine(_directory, "moves-game.db"), Pooling = false },
        });
        game.Database.Migrate();
        Assert.That(game.Database.HasPendingModelChanges(), Is.False);
        var type = new MonsterTypeDescriptor(Guid.NewGuid());
        var move = new MonsterMoveDescriptor(moveIds[0]) { TypeId = type.Id, MaximumPP = 35 };
        game.MonsterTypes.Add(type); game.MonsterMoves.Add(move); game.SaveChanges();
        game.MoveReferenceExists = id => MonsterMoveAssignment.IsAssigned(id, Context);
        game.MonsterMoves.Remove(move);
        Assert.Throws<InvalidOperationException>(() => game.SaveChanges());
        game.Entry(move).State = EntityState.Unchanged;
        Assert.That(game.MonsterMoves.AsNoTracking().Single().MaximumPP, Is.EqualTo(35));
    }

    private sealed class OfflineMySql(DatabaseContextOptions options) : PlayerContext(options)
    {
        public override DatabaseType DatabaseType => DatabaseType.MySql;
        protected override void OnConfiguringProvider(DbContextOptionsBuilder builder, string connectionString) =>
            builder.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36)));
    }
}
