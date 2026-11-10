using Intersect.Config;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Network;
using Intersect.Network.Packets.Client;
using Intersect.Network.Packets.Server;
using Intersect.Server.Database;
using Intersect.Server.Database.PlayerData;
using Intersect.Server.Database.PlayerData.Security;
using Intersect.Server.Entities;
using Intersect.Server.Monsters;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Intersect.Tests.Server.GameObjects;

[TestFixture, NonParallelizable]
public class MonsterOwnershipTests
{
    private string _directory = null!;
    private DatabaseContextOptions _options = null!;
    private Guid _first;
    private Guid _second;
    private Guid _species;
    private Guid _item;
    private bool _speciesAvailable;
    private MonsterOwnershipService Service(int capacity = 6) => new(CreateContext, id => _speciesAvailable && id == _species, capacity);
    private PlayerContext CreateContext() => new SqlitePlayerContext(_options);

    [SetUp]
    public void Setup()
    {
        Options.EnsureCreated();
        _directory = Path.Combine(Path.GetTempPath(), "Intersect-MonsterOwnership-" + Guid.NewGuid());
        Directory.CreateDirectory(_directory);
        _options = new DatabaseContextOptions
        {
            DatabaseType = DatabaseType.Sqlite, AutoDetectChanges = true,
            ConnectionStringBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_directory, "playerdata.db"), Pooling = false,
            },
        };
        _species = Guid.NewGuid();
        _speciesAvailable = true;
        _item = Guid.NewGuid();
        using var db = CreateContext();
        // Seed a pre-milestone database, then upgrade it. Never use application configuration.
        var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, "20261108000000_AddOwnedMonsters") < 0);
        db.GetService<IMigrator>().Migrate(previous);
        var first = new Player { Name = "Trainer One" };
        var second = new Player { Name = "Trainer Two" };
        first.Items[0].ItemId = _item;
        first.Items[0].Quantity = 7;
        var account = new User
        {
            Name = "ownership-test", Email = "test@example.invalid", Power = UserRights.None,
            Players = [first, second],
        };
        db.Users.Add(account);
        db.SaveChanges();
        _first = first.Id;
        _second = second.Id;
        db.Database.Migrate();
    }

    [TearDown]
    public void Cleanup()
    {
        var directory = Path.GetFullPath(_directory);
        if (Path.GetDirectoryName(directory) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())))
            throw new InvalidOperationException("Refusing to delete outside the disposable test directory.");
        Directory.Delete(directory, recursive: true);
    }

    [Test]
    public void NormallyCreatedCharacterCanSaveLazySlotsAndMonstersAcrossReconnects()
    {
        // Exercise the real character-add and account-save paths against this fixture's database.
        // Never use the server's configured resource directory or a development database.
        var previousDirectory = Intersect.Server.Core.ServerContext.ResourceDirectory;
        var previousType = Options.Instance.PlayerDatabase.Type;
        try
        {
            Intersect.Server.Core.ServerContext.ResourceDirectory = _directory;
            Options.Instance.PlayerDatabase.Type = DatabaseType.SQLite;
            var account = new User { Name = "normal-creation", Email = "normal@example.invalid" };
            using (var db = CreateContext()) { db.Users.Add(account); db.SaveChanges(); }
            var character = new Player { Id = Guid.NewGuid(), Name = "NormalTrainer" };
            character.ValidateLists();
            // CreateCharacterPacket explicitly marks all equipment slots unequipped.
            Array.Fill(character.Equipment, -1);
            Assert.That(account.TryAddCharacter(character), Is.True);

            // Join-game packets materialize these lazy collections after character creation.
            for (var i = 0; i < character.Items.Capacity; i++) _ = character.Items[i];
            for (var i = 0; i < character.Spells.Capacity; i++) _ = character.Spells[i];
            for (var i = 0; i < character.Hotbar.Capacity; i++) _ = character.Hotbar[i];
            var monster = Service().Grant(character.Id, _species, Guid.NewGuid());
            using (var db = CreateContext())
                Assert.That(account.Save(db, force: true), Is.EqualTo(UserSaveResult.Completed));

            for (var reconnect = 0; reconnect < 2; reconnect++)
            {
                using (var db = CreateContext())
                    account = db.Users.Include(user => user.Players).Single(user => user.Id == account.Id);
                character = account.Players.Single();
                // Character list and selection each load relationships using short-lived contexts.
                using (var db = CreateContext()) character.LoadRelationships(db);
                using (var db = CreateContext()) character.LoadRelationships(db);
                character.Bank.FillToCapacity();
                character.Items[0].Quantity = 7;
                using (var db = CreateContext())
                    Assert.That(account.Save(db, force: true), Is.EqualTo(UserSaveResult.Completed));
                using var reopened = CreateContext();
                Assert.That(reopened.Player_Items.Count(slot => slot.PlayerId == character.Id), Is.EqualTo(character.Items.Capacity));
                Assert.That(reopened.Player_Spells.Count(slot => slot.PlayerId == character.Id), Is.EqualTo(character.Spells.Capacity));
                Assert.That(reopened.Player_Hotbar.Count(slot => slot.PlayerId == character.Id), Is.EqualTo(character.Hotbar.Capacity));
                Assert.That(reopened.Player_Bank.Count(slot => slot.PlayerId == character.Id), Is.EqualTo(character.Bank.Capacity));
                Assert.That(reopened.Player_Items.Single(slot => slot.PlayerId == character.Id && slot.Slot == 0).Quantity, Is.EqualTo(7));
                var owned = Service().GetOwned(character.Id).Single();
                Assert.That(owned.Id, Is.EqualTo(monster.Id));
                Assert.That(owned.Location, Is.EqualTo(MonsterLocation.Party));
            }
        }
        finally
        {
            Intersect.Server.Core.ServerContext.ResourceDirectory = previousDirectory;
            Options.Instance.PlayerDatabase.Type = previousType;
            // The production context factory enables SQLite pooling; release its test-file handles.
            SqliteConnection.ClearAllPools();
        }
    }

    [Test]
    public void GrantCreatesIndependentIndividualsOwnedByCharactersOnSameAccount()
    {
        var first = Service().Grant(_first, _species, Guid.NewGuid());
        var second = Service().Grant(_second, _species, Guid.NewGuid());
        Assert.That(first.Id, Is.Not.EqualTo(second.Id));
        Assert.That(first.SpeciesId, Is.EqualTo(second.SpeciesId));
        Assert.That(first.Level, Is.EqualTo(1));
        Assert.That(first.Experience, Is.Zero);
        Assert.That(Service().GetOwned(_first).Select(item => item.Id), Is.EqualTo(new[] { first.Id }));
        Assert.That(Service().GetOwned(_second).Select(item => item.Id), Is.EqualTo(new[] { second.Id }));
        using var db = CreateContext();
        Assert.That(db.OwnedMonsters.Single(item => item.Id == first.Id).CreatedAtUtcMilliseconds, Is.GreaterThan(0));
    }

    [Test]
    public void OwnerCannotMoveAnotherCharactersMonster()
    {
        var monster = Service().Grant(_first, _species, Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => Service().Move(_second, monster.Id, monster.Revision, MonsterLocation.Storage));
        Assert.That(Service().GetOwned(_second), Is.Empty);
        Assert.That(Service().GetOwned(_first).Single().Location, Is.EqualTo(MonsterLocation.Party));
    }

    [Test]
    public void GrantsFillSixPartySlotsThenStorageAndReuseEmptySlots()
    {
        var granted = Enumerable.Range(0, 8).Select(_ => Service().Grant(_first, _species, Guid.NewGuid())).ToArray();
        Assert.That(granted.Take(6).Select(item => item.Slot), Is.EqualTo(Enumerable.Range(0, 6)));
        Assert.That(granted.Skip(6).Select(item => item.Location), Is.All.EqualTo(MonsterLocation.Storage));
        Assert.That(granted.Skip(6).Select(item => item.Slot), Is.EqualTo(new[] { 0, 1 }));
        Assert.Throws<InvalidOperationException>(() => Service().Move(_first, granted[6].Id, granted[6].Revision, MonsterLocation.Party));
        Service().Move(_first, granted[2].Id, granted[2].Revision, MonsterLocation.Storage);
        Service().Move(_first, granted[6].Id, granted[6].Revision, MonsterLocation.Party);
        Assert.That(Service().GetOwned(_first).Single(item => item.Id == granted[6].Id).Slot, Is.EqualTo(2));
    }

    [Test]
    public void LowerPartyCapacityMovesOverflowToStorageWithoutChangingIdentity()
    {
        var ids = Enumerable.Range(0, 6).Select(_ => Service().Grant(_first, _species, Guid.NewGuid()).Id).ToArray();
        var owned = Service(capacity: 2).GetOwned(_first);
        Assert.That(owned.Select(item => item.Id), Is.EquivalentTo(ids));
        Assert.That(owned.Count(item => item.Location == MonsterLocation.Party), Is.EqualTo(2));
        Assert.That(owned.Count(item => item.Location == MonsterLocation.Storage), Is.EqualTo(4));
    }

    [Test]
    public void DuplicateGrantIsIdempotentAndCannotBeRetargeted()
    {
        var request = Guid.NewGuid();
        var first = Service().Grant(_first, _species, request);
        Assert.That(Service().Grant(_first, _species, request).Id, Is.EqualTo(first.Id));
        Assert.Throws<InvalidOperationException>(() => Service().Grant(_second, _species, request));
        Assert.That(Service().GetOwned(_first), Has.Length.EqualTo(1));
    }

    [Test]
    public void IdenticalGrantRetryPreservesOwnerPlacementRevisionAndCount()
    {
        var request = Guid.Parse("48929c8f-2bd4-4cd0-9016-49c429279753");
        var service = Service(capacity: 1);
        var first = service.Grant(_first, _species, request, out var created);
        Assert.That(created, Is.True);
        Assert.That(first.Location, Is.EqualTo(MonsterLocation.Party));
        Assert.That(first.Slot, Is.Zero);
        // Match the console path, including sending/reading the roster between grants.
        service.GetOwned(_first);
        var retry = Service(capacity: 1).Grant(_first, _species, request, out created);
        Assert.That(created, Is.False);
        Assert.That((retry.Id, retry.Location, retry.Slot, retry.Revision),
            Is.EqualTo((first.Id, first.Location, first.Slot, first.Revision)));
        using (var db = CreateContext())
        {
            var saved = db.OwnedMonsters.Single();
            Assert.That(saved.OwnerPlayerId, Is.EqualTo(_first));
            Assert.That((saved.Id, saved.Location, saved.Slot, saved.Revision),
                Is.EqualTo((first.Id, first.Location, first.Slot, first.Revision)));
        }
        // A legitimate Store action must survive a later grant retry as well.
        service.Move(_first, first.Id, first.Revision, MonsterLocation.Storage);
        var stored = service.GetOwned(_first).Single();
        var storedRetry = service.Grant(_first, _species, request);
        Assert.That((storedRetry.Id, storedRetry.Location, storedRetry.Slot, storedRetry.Revision),
            Is.EqualTo((stored.Id, stored.Location, stored.Slot, stored.Revision)));
        Assert.That(service.GetOwned(_first), Has.Length.EqualTo(1));
    }

    [Test]
    public void ConcurrentRetriesAndGrantsDoNotDuplicateIndividualsOrSlots()
    {
        var request = Guid.NewGuid();
        var retries = Enumerable.Range(0, 8).Select(_ => Task.Run(() => Service().Grant(_first, _species, request))).ToArray();
        Task.WaitAll(retries);
        Assert.That(retries.Select(task => task.Result.Id).Distinct().Count(), Is.EqualTo(1));
        var grants = Enumerable.Range(0, 8).Select(_ => Task.Run(() => Service().Grant(_first, _species, Guid.NewGuid()))).ToArray();
        Task.WaitAll(grants);
        var owned = Service().GetOwned(_first);
        Assert.That(owned, Has.Length.EqualTo(9));
        Assert.That(owned.Select(item => (item.Location, item.Slot)).Distinct().Count(), Is.EqualTo(9));
    }

    [Test]
    public void RepeatedAndStaleMoveRequestsCannotOverwriteNewPlacement()
    {
        var monster = Service().Grant(_first, _species, Guid.NewGuid());
        Service().Move(_first, monster.Id, monster.Revision, MonsterLocation.Storage);
        Assert.Throws<InvalidOperationException>(() => Service().Move(_first, monster.Id, monster.Revision, MonsterLocation.Storage));
        Assert.Throws<InvalidOperationException>(() => Service().Move(_first, monster.Id, monster.Revision, (MonsterLocation)9));
        Assert.That(Service().GetOwned(_first).Single().Location, Is.EqualTo(MonsterLocation.Storage));
    }

    [Test]
    public void InvalidGrantsDoNotInsertAndMissingSpeciesDoesNotBreakExistingOwnership()
    {
        Assert.Throws<InvalidOperationException>(() => Service().Grant(_first, Guid.NewGuid(), Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => Service().Grant(Guid.NewGuid(), _species, Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => Service().Grant(_first, _species, Guid.Empty));
        Assert.That(Service().GetOwned(_first), Is.Empty);
        var monster = Service().Grant(_first, _species, Guid.NewGuid());
        _speciesAvailable = false;
        Assert.That(Service().GetOwned(_first).Single().Id, Is.EqualTo(monster.Id));
        Service().Move(_first, monster.Id, monster.Revision, MonsterLocation.Storage);
        Assert.Throws<InvalidOperationException>(() => Service().Grant(_first, _species, Guid.NewGuid()));
    }

    [Test]
    public void ReopenedContextsAndStaleAccountAutosavePreserveCommittedMonstersAndInventory()
    {
        User stale;
        using (var db = CreateContext()) stale = db.Users.Include(user => user.Players).Single();
        var monster = Service().Grant(_first, _species, Guid.NewGuid());
        using (var db = CreateContext()) Assert.That(stale.Save(db, force: true), Is.EqualTo(UserSaveResult.Completed));
        using (var reopened = CreateContext())
        {
            Assert.That(reopened.OwnedMonsters.Single().Id, Is.EqualTo(monster.Id));
            Assert.That(reopened.Player_Items.Single(item => item.PlayerId == _first && item.Slot == 0).ItemId, Is.EqualTo(_item));
            Assert.That(reopened.Player_Items.Single(item => item.PlayerId == _first && item.Slot == 0).Quantity, Is.EqualTo(7));
            Assert.That(reopened.Players.Count(), Is.EqualTo(2));
            Assert.That(reopened.Database.HasPendingModelChanges(), Is.False);
        }
        Assert.That(Service().GetOwned(_first).Single().Id, Is.EqualTo(monster.Id));
    }

    [Test]
    public void DatabaseEnforcesOwnerForeignKeyAndUniquePlacements()
    {
        var first = Service().Grant(_first, _species, Guid.NewGuid());
        var second = Service().Grant(_first, _species, Guid.NewGuid());
        using var db = CreateContext();
        Assert.Throws<SqliteException>(() => db.Database.ExecuteSqlInterpolated($"UPDATE OwnedMonsters SET Slot = {first.Slot} WHERE Id = {second.Id}"));
        var nonexistent = Guid.NewGuid();
        Assert.Throws<SqliteException>(() => db.Database.ExecuteSqlInterpolated($"UPDATE OwnedMonsters SET OwnerPlayerId = {nonexistent} WHERE Id = {first.Id}"));
    }

    [Test]
    public void MySqlMigrationMatchesModelAndGeneratesOnlyAdditiveSql()
    {
        using var db = new OfflineMySqlPlayerContext(_options with
        {
            DatabaseType = DatabaseType.MySql,
            ConnectionStringBuilder = new MySqlConnector.MySqlConnectionStringBuilder { Server = "127.0.0.1", Database = "unused", UserID = "unused" },
        });
        var migration = new Intersect.Server.Migrations.MySql.Player.AddOwnedMonsters();
        var snapshot = db.GetService<IModelRuntimeInitializer>().Initialize(new Intersect.Server.Migrations.MySql.Player.AddMonsterMoveSlots().TargetModel, designTime: true);
        var current = db.GetService<IDesignTimeModel>().Model;
        Assert.That(db.GetService<IMigrationsModelDiffer>().GetDifferences(snapshot.GetRelationalModel(), current.GetRelationalModel()), Is.Empty);
        var sql = string.Join("\n", db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, current).Select(command => command.CommandText));
        Assert.That(sql, Does.Contain("CREATE TABLE `OwnedMonsters`"));
        Assert.That(sql, Does.Contain("UNIQUE INDEX"));
        Assert.That(sql, Does.Contain("FOREIGN KEY"));
        Assert.That(sql, Does.Not.Contain("ALTER TABLE"));
        Assert.That(sql, Does.Not.Contain("DROP TABLE"));
    }

    private sealed class OfflineMySqlPlayerContext(DatabaseContextOptions options) : PlayerContext(options)
    {
        public override DatabaseType DatabaseType => DatabaseType.MySql;
        protected override void OnConfiguringProvider(DbContextOptionsBuilder builder, string connectionString) =>
            builder.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36)));
    }

    [Test]
    public void RosterAndPlacementPacketsRoundTripWithoutAddingGrantPacket()
    {
        var registry = new PacketTypeRegistry(NullLogger.Instance, typeof(IntersectPacket).Assembly);
        Assert.That(registry.TryRegisterBuiltIn(), Is.True);
        PackedIntersectPacket.AddKnownTypes(registry.Types);
        Assert.That(registry.Types.Any(type => type.Name.Contains("GrantMonster")), Is.False);
        var monster = Service().Grant(_first, _species, Guid.NewGuid());
        var packet = new OwnedMonstersPacket { PlayerId = _first, PartyCapacity = 6, Monsters = [monster] };
        var loaded = MessagePacker.Instance.Deserialize<OwnedMonstersPacket>(MessagePacker.Instance.Serialize(packet))!;
        Assert.That(loaded.PlayerId, Is.EqualTo(_first));
        Assert.That(loaded.Monsters.Single().Id, Is.EqualTo(monster.Id));
        var move = new MoveOwnedMonsterPacket { MonsterId = monster.Id, ExpectedRevision = monster.Revision, Destination = MonsterLocation.Storage };
        Assert.That(MessagePacker.Instance.Deserialize<MoveOwnedMonsterPacket>(MessagePacker.Instance.Serialize(move))!.ExpectedRevision, Is.EqualTo(monster.Revision));
        Assert.That(MessagePacker.Instance.Deserialize<RequestOwnedMonstersPacket>(MessagePacker.Instance.Serialize(new RequestOwnedMonstersPacket())), Is.Not.Null);
        Assert.That(System.Text.Encoding.UTF8.GetString(SharedConstants.VersionData), Does.EndWith("|monster-ownership-v1|monster-types-v1|monster-moves-v1|monster-natures-v1|monster-battles-v1"));
    }
}
