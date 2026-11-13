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
        BaseStats = new MonsterBaseStats { HP = 30, Attack = 12, Defense = 9, SpecialAttack = 27, SpecialDefense = 18, Speed = 15 },
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
        draft.BaseStats.SpecialAttack = 888;
        draft.BaseStats.SpecialDefense = 777;
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
        destination.BaseStats.SpecialAttack++;
        destination.BaseStats.SpecialDefense++;
        Assert.That(source.BaseStats.HP, Is.EqualTo(30));
        Assert.That(source.BaseStats.SpecialAttack, Is.EqualTo(27));
        Assert.That(source.BaseStats.SpecialDefense, Is.EqualTo(18));
        destination.BaseStatsJson = "{\"HP\":20,\"Attack\":5,\"Defense\":6}";
        Assert.That(destination.BaseStats.Speed, Is.EqualTo(1), "New/missing stat fields have defaults.");
    }

    [Test]
    public void SixStatsHavePositiveDefaultsAndRejectNonPositiveValues()
    {
        var stats = new MonsterSpeciesDescriptor().BaseStats;
        var names = new[] { "HP", "Attack", "Defense", "SpecialAttack", "SpecialDefense", "Speed" };
        Assert.That(typeof(MonsterBaseStats).GetProperties().Where(p => p.PropertyType == typeof(int)).Select(p => p.Name),
            Is.EquivalentTo(names));
        foreach (var name in names)
        {
            var property = typeof(MonsterBaseStats).GetProperty(name)!;
            Assert.That(property.GetValue(stats), Is.EqualTo(1));
            foreach (var invalid in new[] { 0, -1 })
            {
                property.SetValue(stats, invalid);
                Assert.That(stats.IsValid, Is.False, name);
            }
            property.SetValue(stats, int.MaxValue);
            Assert.That(stats.IsValid, Is.True, name);
        }
    }

    [Test]
    public void LegacyFourStatJsonPreservesIdentityAndValuesWithSpecialStatDefaults()
    {
        const string legacyStats = "{\"HP\":73,\"Attack\":31,\"Defense\":29,\"Speed\":47}";
        var species = Species();
        var id = species.Id;
        species.BaseStatsJson = legacyStats;
        Assert.That((species.BaseStats.HP, species.BaseStats.Attack, species.BaseStats.Defense, species.BaseStats.Speed),
            Is.EqualTo((73, 31, 29, 47)));
        Assert.That((species.BaseStats.SpecialAttack, species.BaseStats.SpecialDefense), Is.EqualTo((1, 1)));
        // Content packets and editor paste use Load/PopulateObject, rather than the DB string setter.
        species.BaseStats.SpecialAttack = 99;
        species.BaseStats.SpecialDefense = 98;
        species.Load("{\"BaseStats\":" + legacyStats + "}");
        Assert.That(species.Id, Is.EqualTo(id));
        Assert.That((species.BaseStats.HP, species.BaseStats.Attack, species.BaseStats.Defense, species.BaseStats.Speed),
            Is.EqualTo((73, 31, 29, 47)));
        Assert.That((species.BaseStats.SpecialAttack, species.BaseStats.SpecialDefense), Is.EqualTo((1, 1)));
        Assert.That(species.IsValid, Is.True);
        species.BaseStats.SpecialAttack = 52;
        species.BaseStats.SpecialDefense = 54;
        var reloaded = new MonsterSpeciesDescriptor(id);
        reloaded.Load(species.JsonData);
        Assert.That(reloaded.Id, Is.EqualTo(id));
        Assert.That(reloaded.BaseStatsJson, Is.EqualTo(species.BaseStatsJson));
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
        var snapshot = db.GetService<IModelRuntimeInitializer>().Initialize(new Intersect.Server.Migrations.MySql.Game.AddMonsterStatuses().TargetModel, designTime: true);
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
    public void LegacySqliteSpeciesIsNotRewrittenOnReadAndCanSaveSixStats()
    {
        Options.EnsureCreated();
        var directory = Path.Combine(Path.GetTempPath(), "Intersect-SixStats-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var options = new DatabaseContextOptions
        {
            DatabaseType = DatabaseType.Sqlite, AutoDetectChanges = true,
            ConnectionStringBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(directory, "gamedata.db"), Pooling = false,
            },
        };
        const string legacy = "{\"HP\":73,\"Attack\":31,\"Defense\":29,\"Speed\":47}";
        var species = Species();
        try
        {
            using (var db = new SqliteGameContext(options))
            {
                db.Database.EnsureCreated();
                db.MonsterSpecies.Add(species);
                db.SaveChanges();
                // Store the exact original four-field payload, bypassing current serialization.
                db.Database.ExecuteSqlInterpolated($"UPDATE MonsterSpecies SET BaseStats = {legacy}");
            }
            using (var db = new SqliteGameContext(options))
            {
                var loaded = db.MonsterSpecies.Single();
                Assert.That(loaded.Id, Is.EqualTo(species.Id));
                Assert.That((loaded.BaseStats.HP, loaded.BaseStats.Attack, loaded.BaseStats.Defense, loaded.BaseStats.Speed),
                    Is.EqualTo((73, 31, 29, 47)));
                Assert.That((loaded.BaseStats.SpecialAttack, loaded.BaseStats.SpecialDefense), Is.EqualTo((1, 1)));
                db.Database.OpenConnection();
                using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "SELECT BaseStats FROM MonsterSpecies";
                Assert.That(command.ExecuteScalar(), Is.EqualTo(legacy), "Reading does not rewrite stored content.");
                loaded.BaseStats.SpecialAttack = 81;
                loaded.BaseStats.SpecialDefense = 82;
                db.SaveChanges();
            }
            using (var db = new SqliteGameContext(options))
            {
                var loaded = db.MonsterSpecies.Single();
                Assert.That(loaded.Id, Is.EqualTo(species.Id));
                Assert.That((loaded.BaseStats.HP, loaded.BaseStats.Attack, loaded.BaseStats.Defense,
                        loaded.BaseStats.SpecialAttack, loaded.BaseStats.SpecialDefense, loaded.BaseStats.Speed),
                    Is.EqualTo((73, 31, 29, 81, 82, 47)));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
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
                var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, MigrationId) < 0);
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
                loaded.BaseStats.SpecialAttack = 64;
                loaded.BaseStats.SpecialDefense = 72;
                db.SaveChanges();
            }
            using (var db = new SqliteGameContext(options))
            {
                var loaded = db.MonsterSpecies.Single();
                Assert.That(loaded.Id, Is.EqualTo(species.Id));
                Assert.That(loaded.Name, Is.EqualTo("Edited Species"));
                Assert.That(loaded.BaseStats.Attack, Is.EqualTo(42));
                Assert.That(loaded.BaseStats.SpecialAttack, Is.EqualTo(64));
                Assert.That(loaded.BaseStats.SpecialDefense, Is.EqualTo(72));
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
    [Test]
    public void LegacyBattleAppearanceDefaultsAndJsonIdentityRemainCompatible()
    {
        var id = Guid.NewGuid(); var species = new MonsterSpeciesDescriptor(id)
        { Sprite = "sheet.png", FrontBattleSprite = "front.png", BackBattleSprite = "back.png", BattleBackground = "scene.png" };
        var copy = new MonsterSpeciesDescriptor(Guid.NewGuid()); var destination = copy.Id; copy.Load(species.JsonData);
        Assert.That(copy.Id, Is.EqualTo(destination)); Assert.That(copy.FrontBattleSprite, Is.EqualTo("front.png")); Assert.That(copy.BackBattleSprite, Is.EqualTo("back.png"));
        Assert.That(copy.BattleBackground, Is.EqualTo("scene.png")); Assert.That(copy.Sprite, Is.EqualTo("sheet.png"));
        copy.Load("{\"Name\":\"Legacy\",\"Sprite\":\"old.png\"}");
        Assert.That(copy.IsValid, Is.True); Assert.That(copy.FrontBattleSprite, Is.Empty); Assert.That(copy.BackBattleSprite, Is.Empty); Assert.That(copy.BattleBackground, Is.Empty);
    }
    [Test]
    public void BothAppearanceMigrationsAddOnlyDefaultedContentColumns()
    {
        foreach (var migration in new Migration[] { new Intersect.Server.Migrations.Sqlite.Game.AddMonsterBattleAppearance(), new Intersect.Server.Migrations.MySql.Game.AddMonsterBattleAppearance() })
        {
            var columns = migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation>().ToArray();
            Assert.That(migration.UpOperations.Count, Is.EqualTo(3)); Assert.That(columns.Select(c => c.Name), Is.EquivalentTo(new[] { "FrontBattleSprite", "BackBattleSprite", "BattleBackground" }));
            Assert.That(columns.All(c => c.Table == "MonsterSpecies" && !c.IsNullable && Equals(c.DefaultValue, "")), Is.True);
            Assert.That(migration.DownOperations.Count, Is.EqualTo(3));
        }
    }

    [Test]
    public void AppearanceUpgradePreservesLegacyContentAndPersistsDedicatedArt()
    {
        Options.EnsureCreated(); var directory = Path.Combine(Path.GetTempPath(), "Intersect-M6D-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        var options = new DatabaseContextOptions { DatabaseType = DatabaseType.Sqlite, AutoDetectChanges = true,
            ConnectionStringBuilder = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "gamedata.db"), Pooling = false } };
        var original = Species();
        try
        {
            using (var db = new SqliteGameContext(options))
            {
                db.GetService<IMigrator>().Migrate("20261109000100_AddMonsterMovePP");
                db.Database.ExecuteSqlInterpolated($"INSERT INTO MonsterSpecies (Id, Name, Description, Sprite, Folder, TimeCreated, BaseStats, Types, Learnset) VALUES ({original.Id}, {original.Name}, {original.Description}, {original.Sprite}, {original.Folder}, {original.TimeCreated}, {original.BaseStatsJson}, {original.TypeIdsJson}, {original.LearnsetJson})");
                db.Database.Migrate(); var loaded = db.MonsterSpecies.Single();
                Assert.That(loaded.Id, Is.EqualTo(original.Id)); Assert.That(loaded.Sprite, Is.EqualTo(original.Sprite)); Assert.That(loaded.BaseStatsJson, Is.EqualTo(original.BaseStatsJson));
                Assert.That(loaded.FrontBattleSprite, Is.Empty); Assert.That(loaded.BackBattleSprite, Is.Empty); Assert.That(loaded.BattleBackground, Is.Empty);
                loaded.FrontBattleSprite = "front.png"; loaded.BackBattleSprite = "back.png"; loaded.BattleBackground = "scene.png"; db.SaveChanges();
            }
            using var reopened = new SqliteGameContext(options); var saved = reopened.MonsterSpecies.Single();
            Assert.That(saved.Id, Is.EqualTo(original.Id)); Assert.That(saved.FrontBattleSprite, Is.EqualTo("front.png"));
            Assert.That(saved.BackBattleSprite, Is.EqualTo("back.png")); Assert.That(saved.BattleBackground, Is.EqualTo("scene.png"));
        }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(directory)) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))) throw new InvalidOperationException();
            Directory.Delete(directory, recursive: true);
        }
    }

}
