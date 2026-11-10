using Intersect.Config;
using Intersect.Enums;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Network;
using Intersect.Network.Packets.Editor;
using Intersect.Network.Packets.Server;
using Intersect.Server.Database;
using Intersect.Server.Database.GameData;
using Intersect.Server.Monsters;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Intersect.Tests.Server.GameObjects;

[TestFixture, NonParallelizable]
public class MonsterNatureTests
{
    private static MonsterNatureDescriptor Custom() => new(Guid.NewGuid())
    {
        Name = "Original nature", Description = "Custom content", Folder = "Rare", SelectionWeight = 5,
        Modifiers = new()
        {
            Increase = new() { Stat = MonsterStat.Attack, Percentage = 15.5m },
            Decrease = new() { Stat = MonsterStat.SpecialDefense, Percentage = 10m },
        },
    };
    [Test]
    public void DefaultsAreNeutralWithTenPercentMagnitudesAndWeightOneHundred()
    {
        Assert.That((int)GameObjectType.MonsterMove, Is.EqualTo(21));
        Assert.That((int)GameObjectType.MonsterNature, Is.EqualTo(22));
        Assert.That(GameObjectType.MonsterNature.CreateNew(), Is.TypeOf<MonsterNatureDescriptor>());
        var nature = new MonsterNatureDescriptor();
        Assert.That(nature.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(nature.IsValid, Is.True);
        Assert.That(nature.SelectionWeight, Is.EqualTo(100));
        Assert.That(nature.Modifiers.Increase.Stat, Is.EqualTo(MonsterStat.None));
        Assert.That(nature.Modifiers.Decrease.Stat, Is.EqualTo(MonsterStat.None));
        Assert.That(nature.Modifiers.Increase.Percentage, Is.EqualTo(10m));
        Assert.That(nature.Modifiers.Decrease.Percentage, Is.EqualTo(10m));
        nature.ModifiersJson = "{}";
        Assert.That(nature.IsValid, Is.True);
        Assert.That(nature.Modifiers.Increase.Percentage, Is.EqualTo(10m));
    }
    [Test]
    public void FiveNonHpStatsCustomMagnitudesAndNeutralConfigurationsAreValidated()
    {
        var nature = new MonsterNatureDescriptor();
        foreach (var stat in new[] { MonsterStat.Attack, MonsterStat.Defense, MonsterStat.SpecialAttack, MonsterStat.SpecialDefense, MonsterStat.Speed })
        {
            nature.Modifiers.Increase.Stat = stat;
            nature.Modifiers.Decrease.Stat = MonsterStat.None;
            foreach (var percentage in new[] { 0m, 10m, 15.5m, 50m })
            {
                nature.Modifiers.Increase.Percentage = percentage;
                Assert.That(nature.IsValid, Is.True);
            }
            nature.Modifiers.Increase.Stat = MonsterStat.None;
            nature.Modifiers.Decrease.Stat = stat;
            Assert.That(nature.IsValid, Is.True);
            nature.Modifiers.Increase.Stat = stat;
            Assert.That(nature.IsValid, Is.False);
            Assert.That(Assert.Throws<InvalidOperationException>(() => MonsterNatureRules.Validate(nature))!.Message,
                Does.Contain("same stat"));
        }
        foreach (var invalid in new[] { MonsterStat.HP, (MonsterStat)99, (MonsterStat)(-1) })
        {
            nature.Modifiers = new();
            nature.Modifiers.Increase.Stat = invalid;
            Assert.That(nature.IsValid, Is.False);
            nature.Modifiers.Increase.Stat = MonsterStat.None;
            nature.Modifiers.Decrease.Stat = invalid;
            Assert.That(nature.IsValid, Is.False);
        }
        foreach (var percentage in new[] { -0.01m, 50.01m })
        {
            nature.Modifiers = new();
            nature.Modifiers.Increase.Percentage = percentage;
            Assert.That(nature.IsValid, Is.False);
            nature.Modifiers = new();
            nature.Modifiers.Decrease.Percentage = percentage;
            Assert.That(nature.IsValid, Is.False);
        }
        nature.ModifiersJson = "{\"Increase\":{\"Stat\":\"HP\",\"Percentage\":10}}";
        Assert.That(nature.IsValid, Is.False, "HP from untrusted JSON is rejected as well.");
    }
    [Test]
    public void SelectionWeightsAllowZeroAndBoundsAndRejectFractionalOrInvalidValues()
    {
        var nature = Custom();
        foreach (var weight in new[] { 0, 5, 25, 100, 100000 })
        {
            nature.SelectionWeight = weight;
            Assert.That(nature.IsValid, Is.True);
        }
        foreach (var weight in new[] { -1, 100001, int.MaxValue })
        {
            nature.SelectionWeight = weight;
            Assert.That(nature.IsValid, Is.False);
            Assert.Throws<InvalidOperationException>(() => MonsterNatureRules.Validate(nature));
        }
        foreach (var invalidJson in new[] { "1.5", "\"100\"", "true", "null" })
            Assert.Throws<JsonSerializationException>(() => nature.Load("{\"SelectionWeight\":" + invalidJson + "}"));
        Assert.Throws<JsonSerializationException>(() => nature.Load("{\"SelectionWeight\":100,\"selectionweight\":1.5}"));
        nature.Load("{\"SelectionWeight\":0}");
        Assert.That(nature.IsValid, Is.True);
        Assert.That(nature.SelectionWeight, Is.Zero);
    }
    [Test]
    public void JsonCopiesUndoAndDraftEditsPreserveUuidAndIndependentNestedModifiers()
    {
        var source = Custom();
        var target = new MonsterNatureDescriptor(Guid.NewGuid());
        var targetId = target.Id;
        var created = target.TimeCreated;
        var before = target.JsonData;
        target.MakeBackup();
        target.Load(source.JsonData, keepCreationTime: true);
        Assert.That(target.Id, Is.EqualTo(targetId));
        Assert.That(target.TimeCreated, Is.EqualTo(created));
        Assert.That(target.ModifiersJson, Is.EqualTo(source.ModifiersJson));
        Assert.That(target.SelectionWeight, Is.EqualTo(5));
        Assert.That(target.Folder, Is.EqualTo("Rare"));
        target.Modifiers.Increase.Percentage = 42m;
        Assert.That(source.Modifiers.Increase.Percentage, Is.EqualTo(15.5m));
        target.RestoreBackup();
        Assert.That(target.JsonData, Is.EqualTo(before));
    }
    [Test]
    public void ExistingPacketsAndServerContentListSynchronizeNatureDefinitions()
    {
        var registry = new PacketTypeRegistry(NullLogger.Instance, typeof(IntersectPacket).Assembly);
        Assert.That(registry.TryRegisterBuiltIn(), Is.True);
        PackedIntersectPacket.AddKnownTypes(registry.Types);
        var nature = Custom();
        var save = new SaveGameObjectPacket(nature.Type, nature.Id, nature.JsonData);
        var restored = MessagePacker.Instance.Deserialize<SaveGameObjectPacket>(MessagePacker.Instance.Serialize(save))!;
        var content = new GameObjectPacket(restored.Id, restored.Type, restored.Data, false, false);
        var packet = MessagePacker.Instance.Deserialize<GameObjectPacket>(MessagePacker.Instance.Serialize(content))!;
        var loaded = new MonsterNatureDescriptor(packet.Id);
        loaded.Load(packet.Data);
        Assert.That(loaded.JsonData, Is.EqualTo(nature.JsonData));
        MonsterNatureDescriptor.Lookup.Set(nature.Id, nature);
        try
        {
            var packets = new List<GameObjectPacket>();
            Intersect.Server.Networking.PacketSender.SendGameObjects(null!, GameObjectType.MonsterNature, packets);
            Assert.That(packets.Single(item => item.Id == nature.Id).Data, Is.EqualTo(nature.JsonData));
        }
        finally { MonsterNatureDescriptor.Lookup.DeleteAt(nature.Id); }
    }
    private static void WithDatabase(Action<DatabaseContextOptions> test)
    {
        Options.EnsureCreated();
        var directory = Path.Combine(Path.GetTempPath(), "Intersect-MonsterNatures-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            test(new DatabaseContextOptions
            {
                DatabaseType = DatabaseType.Sqlite, AutoDetectChanges = true,
                ConnectionStringBuilder = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "game.db"), Pooling = false },
            });
        }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(directory)) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())))
                throw new InvalidOperationException("Not a disposable test directory.");
            Directory.Delete(directory, true);
        }
    }
    [Test]
    public void SqliteUpgradeNatureCrudAndReopenedContextsPreserveAllExistingContent()
    {
        WithDatabase(options =>
        {
            var type = new MonsterTypeDescriptor(Guid.NewGuid()) { Name = "Existing type" };
            var move = new MonsterMoveDescriptor(Guid.NewGuid()) { Name = "Existing move", TypeId = type.Id };
            var species = new MonsterSpeciesDescriptor(Guid.NewGuid())
            {
                Name = "Existing species", TypeIds = [type.Id], Learnset = [new() { MoveId = move.Id, Level = 1 }],
            };
            var speciesBefore = species.JsonData;
            var moveBefore = move.JsonData;
            var typeBefore = type.JsonData;
            var rare = Custom();
            var excluded = new MonsterNatureDescriptor(Guid.NewGuid()) { Name = "Excluded neutral", SelectionWeight = 0 };
            using (var db = new SqliteGameContext(options))
            {
                db.GetService<IMigrator>().Migrate("20261108020000_AddMonsterMoves");
                // Seed the historical schema directly: the current EF model includes later PP columns.
                db.Database.ExecuteSqlInterpolated($"INSERT INTO MonsterTypes (Id, Name, Description, Folder, DisplayColor, TimeCreated, Effectiveness) VALUES ({type.Id}, {type.Name}, {type.Description}, {type.Folder}, {type.DisplayColor}, {type.TimeCreated}, {type.EffectivenessJson})");
                db.Database.ExecuteSqlInterpolated($"INSERT INTO MonsterMoves (Id, Name, Description, Folder, TimeCreated, TypeId, Category, BasePower, Accuracy, AlwaysHits, Priority, Target, AnimationId) VALUES ({move.Id}, {move.Name}, {move.Description}, {move.Folder}, {move.TimeCreated}, {move.TypeId}, {(int)move.Category}, {move.BasePower}, {move.Accuracy}, {move.AlwaysHits}, {move.Priority}, {(int)move.Target}, {move.AnimationId})");
                db.Database.ExecuteSqlInterpolated($"INSERT INTO MonsterSpecies (Id, Name, Description, Sprite, Folder, TimeCreated, BaseStats, Types, Learnset) VALUES ({species.Id}, {species.Name}, {species.Description}, {species.Sprite}, {species.Folder}, {species.TimeCreated}, {species.BaseStatsJson}, {species.TypeIdsJson}, {species.LearnsetJson})");
                db.Database.Migrate();
                Assert.That(db.Database.HasPendingModelChanges(), Is.False);
                Assert.That(db.MonsterNatures.Count(), Is.Zero, "No predefined natures are seeded.");
                db.MonsterNatures.AddRange(rare, excluded);
                db.SaveChanges();
            }
            using (var db = new SqliteGameContext(options))
            {
                var loaded = db.MonsterNatures.Single(nature => nature.Id == rare.Id);
                Assert.That(loaded.JsonData, Is.EqualTo(rare.JsonData));
                Assert.That(db.MonsterNatures.Single(nature => nature.Id == excluded.Id).SelectionWeight, Is.Zero);
                loaded.Name = "Edited nature";
                loaded.Modifiers.Increase.Stat = MonsterStat.SpecialAttack;
                loaded.Modifiers.Increase.Percentage = 25.5m;
                loaded.SelectionWeight = 100000;
                db.SaveChanges();
            }
            using (var db = new SqliteGameContext(options))
            {
                var loaded = db.MonsterNatures.Single(nature => nature.Id == rare.Id);
                Assert.That((loaded.Name, loaded.Modifiers.Increase.Stat, loaded.Modifiers.Increase.Percentage, loaded.SelectionWeight),
                    Is.EqualTo(("Edited nature", MonsterStat.SpecialAttack, 25.5m, 100000)));
                Assert.That(loaded.Folder, Is.EqualTo("Rare"));
                Assert.That(db.MonsterSpecies.Single().JsonData, Is.EqualTo(speciesBefore));
                Assert.That(db.MonsterMoves.Single().JsonData, Is.EqualTo(moveBefore));
                Assert.That(db.MonsterTypes.Single().JsonData, Is.EqualTo(typeBefore));
                db.MonsterNatures.Remove(db.MonsterNatures.Single(nature => nature.Id == excluded.Id));
                db.SaveChanges();
            }
            using (var db = new SqliteGameContext(options))
                Assert.That(db.MonsterNatures.Single().Id, Is.EqualTo(rare.Id));
        });
    }
    [Test]
    public void DatabaseSaveRejectsHpConflictsAndOutOfRangeModifiersAndWeights()
    {
        WithDatabase(options =>
        {
            using var db = new SqliteGameContext(options);
            db.Database.EnsureCreated();
            var invalid = new List<MonsterNatureDescriptor>();
            var hp = Custom(); hp.Modifiers.Increase.Stat = MonsterStat.HP; invalid.Add(hp);
            var conflict = Custom(); conflict.Modifiers.Decrease.Stat = conflict.Modifiers.Increase.Stat; invalid.Add(conflict);
            var negative = Custom(); negative.Modifiers.Decrease.Percentage = -1m; invalid.Add(negative);
            var high = Custom(); high.Modifiers.Increase.Percentage = 51m; invalid.Add(high);
            var weight = Custom(); weight.SelectionWeight = 100001; invalid.Add(weight);
            foreach (var nature in invalid)
            {
                db.MonsterNatures.Add(nature);
                Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
                db.ChangeTracker.Clear();
            }
            Assert.That(db.MonsterNatures.Count(), Is.Zero);
        });
    }
    [Test]
    public void MySqlNatureMigrationMatchesModelAndOnlyCreatesTheNatureTable()
    {
        Options.EnsureCreated();
        using var db = new OfflineMySql(new DatabaseContextOptions
        {
            DatabaseType = DatabaseType.MySql,
            ConnectionStringBuilder = new MySqlConnector.MySqlConnectionStringBuilder { Server = "127.0.0.1", Database = "unused_natures", UserID = "unused" },
        });
        var migration = new Intersect.Server.Migrations.MySql.Game.AddMonsterNatures();
        var target = db.GetService<IModelRuntimeInitializer>().Initialize(new Intersect.Server.Migrations.MySql.Game.AddMonsterMovePP().TargetModel, designTime: true);
        var current = db.GetService<IDesignTimeModel>().Model;
        Assert.That(db.GetService<IMigrationsModelDiffer>().GetDifferences(target.GetRelationalModel(), current.GetRelationalModel()), Is.Empty);
        Assert.That(migration.UpOperations.Count, Is.EqualTo(1));
        Assert.That(((CreateTableOperation)migration.UpOperations.Single()).Name, Is.EqualTo("MonsterNatures"));
        var sql = string.Join("\n", db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, current).Select(command => command.CommandText));
        Assert.That(sql, Does.Contain("CREATE TABLE `MonsterNatures`"));
        Assert.That(sql, Does.Contain("`Modifiers` longtext"));
        Assert.That(sql, Does.Not.Contain("ALTER TABLE"));
    }
    private sealed class OfflineMySql(DatabaseContextOptions options) : GameContext(options)
    {
        public override DatabaseType DatabaseType => DatabaseType.MySql;
        protected override void OnConfiguringProvider(DbContextOptionsBuilder builder, string connectionString) =>
            builder.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36)));
    }
}
