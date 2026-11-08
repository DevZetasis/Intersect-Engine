using Intersect.Config;
using Intersect.Enums;
using Intersect.Framework.Core.GameObjects.Items;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Framework.Core.GameObjects.NPCs;
using Intersect.GameObjects;
using Intersect.Network;
using Intersect.Network.Packets.Editor;
using Intersect.Network.Packets.Server;
using Intersect.Server.Database;
using Intersect.Server.Database.GameData;
using Intersect.Server.Maps;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Intersect.Tests.Server.GameObjects;

[TestFixture, NonParallelizable]
public class MonsterSpeciesTests
{
    private const string MigrationId = "20261107000000_AddMonsterSpecies";

    private static MonsterSpeciesDescriptor Species(Guid? id = null) => new(id ?? Guid.NewGuid())
    {
        Name = "Test Monster",
        Description = "A configurable species.",
        Sprite = "test.png",
        Folder = "Test Species",
        BaseStats = new MonsterBaseStats { HP = 30, Attack = 12, Defense = 9, Speed = 15 },
    };

    [Test]
    public void ContentTypeAppendsWithoutRenumberingExistingObjects()
    {
        var existing = new[] { "Animation", "Class", "Item", "Npc", "Projectile", "Quest", "Resource", "Shop",
            "Spell", "CraftTables", "Crafts", "Map", "Event", "PlayerVariable", "ServerVariable", "Tileset",
            "Time", "GuildVariable", "UserVariable" };
        for (var index = 0; index < existing.Length; index++)
            Assert.That((int)Enum.Parse<GameObjectType>(existing[index]), Is.EqualTo(index));
        Assert.That((int)GameObjectType.MonsterSpecies, Is.EqualTo(19));
        Assert.That(GameObjectType.MonsterSpecies.CreateNew(), Is.TypeOf<MonsterSpeciesDescriptor>());
        Assert.That(Enum.GetNames<Stat>(), Is.EqualTo(new[] { "Attack", "AbilityPower", "Defense", "MagicResist", "Speed" }));
    }

    [Test]
    public void DraftEditingAndCancelLeaveSynchronizedSpeciesUnchanged()
    {
        var source = Species();
        var before = source.JsonData;
        var draft = new MonsterSpeciesDescriptor(source.Id);
        draft.Load(before);
        draft.MakeBackup();
        draft.Name = "Unsaved";
        draft.BaseStats.HP = 999;
        Assert.That(source.JsonData, Is.EqualTo(before));
        draft.RestoreBackup();
        Assert.That(draft.JsonData, Is.EqualTo(before));
    }

    [Test]
    public void JsonCopyPreservesDestinationIdentityAndIndependentStats()
    {
        var source = Species();
        var destination = new MonsterSpeciesDescriptor(Guid.NewGuid());
        var destinationId = destination.Id;
        var creationTime = destination.TimeCreated;
        destination.Load(source.JsonData, keepCreationTime: true);
        Assert.That(destination.Id, Is.EqualTo(destinationId));
        Assert.That(destination.TimeCreated, Is.EqualTo(creationTime));
        Assert.That(destination.BaseStatsJson, Is.EqualTo(source.BaseStatsJson));
        destination.BaseStats.HP++;
        Assert.That(source.BaseStats.HP, Is.EqualTo(30));
        destination.BaseStatsJson = "{\"HP\":20,\"Attack\":5,\"Defense\":6}";
        Assert.That(destination.BaseStats.Speed, Is.EqualTo(1), "New/missing stat fields have defaults.");
    }

    [Test]
    public void SpeciesRejectsEmptyNamesAndNonPositiveStats()
    {
        var species = Species();
        Assert.That(species.IsValid, Is.True);
        species.Name = " ";
        Assert.That(species.IsValid, Is.False);
        species.Name = "Valid";
        species.BaseStats.HP = 0;
        Assert.That(species.IsValid, Is.False);
        species.BaseStats.HP = 1;
        species.BaseStats.Speed = -1;
        Assert.That(species.IsValid, Is.False);
    }

    [Test]
    public void ExistingEditorAndContentPacketsRoundTripSpecies()
    {
        var registry = new PacketTypeRegistry(NullLogger.Instance, typeof(IntersectPacket).Assembly);
        Assert.That(registry.TryRegisterBuiltIn(), Is.True);
        PackedIntersectPacket.AddKnownTypes(registry.Types);
        var species = Species();
        var save = new SaveGameObjectPacket(GameObjectType.MonsterSpecies, species.Id, species.JsonData);
        var saved = MessagePacker.Instance.Deserialize<SaveGameObjectPacket>(MessagePacker.Instance.Serialize(save));
        Assert.That(saved!.Id, Is.EqualTo(species.Id));
        Assert.That(saved.Type, Is.EqualTo(GameObjectType.MonsterSpecies));
        var content = new GameObjectPacket(species.Id, species.Type, saved.Data, false, false);
        var received = MessagePacker.Instance.Deserialize<GameObjectPacket>(MessagePacker.Instance.Serialize(content));
        var loaded = new MonsterSpeciesDescriptor(received!.Id);
        loaded.Load(received.Data);
        Assert.That(loaded.JsonData, Is.EqualTo(species.JsonData));
        var create = new CreateGameObjectPacket(species.Type);
        Assert.That(MessagePacker.Instance.Deserialize<CreateGameObjectPacket>(MessagePacker.Instance.Serialize(create))!.Type,
            Is.EqualTo(species.Type));
        var delete = new DeleteGameObjectPacket(species.Type, species.Id);
        Assert.That(MessagePacker.Instance.Deserialize<DeleteGameObjectPacket>(MessagePacker.Instance.Serialize(delete))!.Id,
            Is.EqualTo(species.Id));
    }

    [Test]
    public void BothProviderMigrationsOnlyCreateAndDropSpeciesTable()
    {
        Migration[] migrations = [new Intersect.Server.Migrations.Sqlite.Game.AddMonsterSpecies(), new Intersect.Server.Migrations.MySql.Game.AddMonsterSpecies()];
        foreach (var migration in migrations)
        {
            Assert.That(migration.UpOperations, Has.Count.EqualTo(1));
            var table = (CreateTableOperation)migration.UpOperations.Single();
            Assert.That(table.Name, Is.EqualTo("MonsterSpecies"));
            Assert.That(table.Columns.Select(column => column.Name), Is.EquivalentTo(
                new[] { "Id", "Name", "TimeCreated", "Description", "Sprite", "Folder", "BaseStats" }));
            Assert.That(migration.DownOperations.Single(), Is.TypeOf<DropTableOperation>());
            Assert.That(((DropTableOperation)migration.DownOperations.Single()).Name, Is.EqualTo("MonsterSpecies"));
        }
    }

    [Test]
    public void MySqlMigrationMatchesModelAndGeneratesSqlWithoutConnecting()
    {
        Options.EnsureCreated();
        using var db = new OfflineMySqlGameContext(new DatabaseContextOptions
        {
            DatabaseType = DatabaseType.MySql,
            ConnectionStringBuilder = new MySqlConnector.MySqlConnectionStringBuilder
            {
                Server = "127.0.0.1", Database = "unused_species_test", UserID = "unused",
            },
        });
        var migration = new Intersect.Server.Migrations.MySql.Game.AddMonsterSpecies();
        var snapshot = db.GetService<IModelRuntimeInitializer>().Initialize(migration.TargetModel, designTime: true);
        var current = db.GetService<IDesignTimeModel>().Model;
        var differences = db.GetService<IMigrationsModelDiffer>().GetDifferences(snapshot.GetRelationalModel(), current.GetRelationalModel());
        Assert.That(differences, Is.Empty, "MySQL migration target matches the runtime model.");
        var sql = string.Join("\n", db.GetService<IMigrationsSqlGenerator>()
            .Generate(migration.UpOperations, current).Select(command => command.CommandText));
        Assert.That(sql, Does.Contain("CREATE TABLE `MonsterSpecies`"));
        Assert.That(sql, Does.Contain("char(36)"));
        Assert.That(sql, Does.Contain("`BaseStats` longtext"));
        Assert.That(sql, Does.Not.Contain("ALTER TABLE"));
    }

    private sealed class OfflineMySqlGameContext(DatabaseContextOptions options) : GameContext(options)
    {
        public override DatabaseType DatabaseType => DatabaseType.MySql;

        // Supply the version explicitly: production AutoDetect would connect to a server.
        protected override void OnConfiguringProvider(DbContextOptionsBuilder builder, string connectionString) =>
            builder.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36)));
    }

    [Test]
    public void SqliteUpgradeCrudAndReloadPreserveSpeciesAndLegacyContent()
    {
        Options.EnsureCreated();
        var directory = Path.Combine(Path.GetTempPath(), "Intersect-MonsterSpecies-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var options = new DatabaseContextOptions
        {
            DatabaseType = DatabaseType.Sqlite,
            ConnectionStringBuilder = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "gamedata.db"), Pooling = false },
            AutoDetectChanges = true,
        };
        try
        {
            var species = Species();
            var npcId = Guid.NewGuid();
            var itemId = Guid.NewGuid();
            var spellId = Guid.NewGuid();
            var mapId = Guid.NewGuid();
            using (var db = new SqliteGameContext(options))
            {
                var previous = db.Database.GetMigrations().Last(id => id != MigrationId);
                db.GetService<IMigrator>().Migrate(previous);
                db.Npcs.Add(new NPCDescriptor(npcId) { Name = "Existing NPC", Level = 7 });
                db.Items.Add(new ItemDescriptor(itemId) { Name = "Existing Item" });
                db.Spells.Add(new SpellDescriptor(spellId) { Name = "Existing Spell" });
                db.Maps.Add(new MapController(mapId) { Name = "Existing Map" });
                db.SaveChanges();
                db.Database.Migrate();
                Assert.That(db.Database.HasPendingModelChanges(), Is.False, "SQLite snapshot matches the runtime model.");
                db.MonsterSpecies.Add(species);
                db.SaveChanges();
            }
            using (var db = new SqliteGameContext(options))
            {
                var loaded = db.MonsterSpecies.Single();
                Assert.That(loaded.JsonData, Is.EqualTo(species.JsonData));
                loaded.Name = "Edited Species";
                loaded.BaseStats.Attack = 42;
                db.SaveChanges();
            }
            using (var db = new SqliteGameContext(options))
            {
                var loaded = db.MonsterSpecies.Single();
                Assert.That(loaded.Id, Is.EqualTo(species.Id));
                Assert.That(loaded.Name, Is.EqualTo("Edited Species"));
                Assert.That(loaded.BaseStats.Attack, Is.EqualTo(42));
                Assert.That(db.Npcs.Single(n => n.Id == npcId).Level, Is.EqualTo(7));
                Assert.That(db.Items.Single(i => i.Id == itemId).Name, Is.EqualTo("Existing Item"));
                Assert.That(db.Spells.Single(s => s.Id == spellId).Name, Is.EqualTo("Existing Spell"));
                Assert.That(db.Maps.Single(m => m.Id == mapId).Name, Is.EqualTo("Existing Map"));
                db.MonsterSpecies.Remove(loaded);
                db.SaveChanges();
            }
            using (var db = new SqliteGameContext(options))
                Assert.That(db.MonsterSpecies.Count(), Is.Zero);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
