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
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Intersect.Tests.Server.GameObjects;

[TestFixture, NonParallelizable]
public class MonsterTypeTests
{
    [Test]
    public void CustomTypeDefaultsAndContentIdentityAreStable()
    {
        Assert.That((int)GameObjectType.MonsterSpecies, Is.EqualTo(19));
        Assert.That((int)GameObjectType.MonsterType, Is.EqualTo(20));
        Assert.That(GameObjectType.MonsterType.CreateNew(), Is.TypeOf<MonsterTypeDescriptor>());
        var type = new MonsterTypeDescriptor(Guid.NewGuid());
        Assert.That(type.IsValid, Is.True);
        Assert.That(type.DisplayColor, Is.EqualTo("#FFFFFF"));
        Assert.That(type.Effectiveness, Is.Empty);
        var id = type.Id;
        type.Load(new MonsterTypeDescriptor(Guid.NewGuid()) { Name = "Custom Type", DisplayColor = "#12ABef" }.JsonData);
        Assert.That(type.Id, Is.EqualTo(id));
        Assert.That(type.IsValid, Is.True);
    }

    [Test]
    public void DirectionalNeutralAndDualMultipliersAreIndependentOfEntities()
    {
        var attacker = new MonsterTypeDescriptor(Guid.NewGuid());
        var first = new MonsterTypeDescriptor(Guid.NewGuid());
        var second = new MonsterTypeDescriptor(Guid.NewGuid());
        foreach (var (a, b, expected) in new[] { (2d, 2d, 4d), (2d, .5d, 1d), (.5d, .5d, .25d), (0d, 2d, 0d), (2d, 0d, 0d) })
        {
            attacker.Effectiveness[first.Id] = a;
            attacker.Effectiveness[second.Id] = b;
            Assert.That(MonsterTypeEffectiveness.Evaluate(attacker, first.Id, second.Id), Is.EqualTo(expected));
        }
        attacker.Effectiveness[first.Id] = 2;
        Assert.That(MonsterTypeEffectiveness.Evaluate(attacker, first.Id), Is.EqualTo(2));
        Assert.That(MonsterTypeEffectiveness.Evaluate(first, attacker.Id), Is.EqualTo(1));
        Assert.That(MonsterTypeEffectiveness.Evaluate(attacker, Guid.NewGuid()), Is.EqualTo(1));
        Assert.That(MonsterTypeEffectiveness.Evaluate(null, first.Id), Is.EqualTo(1));
        Assert.That(MonsterTypeEffectiveness.Evaluate(attacker, null), Is.EqualTo(1));
        Assert.That(MonsterTypeEffectiveness.Evaluate(attacker, first.Id, first.Id), Is.EqualTo(2));
    }

    [Test]
    public void InvalidColorsMultipliersAndAssignmentsAreRejected()
    {
        var type = new MonsterTypeDescriptor(Guid.NewGuid());
        foreach (var color in new[] { "red", "#GG0000", "#12345", "#FFFFFF\n" })
        {
            type.DisplayColor = color;
            Assert.That(type.IsValid, Is.False);
        }
        type.DisplayColor = "#FFFFFF";
        var other = Guid.NewGuid();
        foreach (var multiplier in new[] { -1d, .25d, 1.5d, 4d, double.NaN, double.PositiveInfinity })
        {
            type.Effectiveness[other] = multiplier;
            Assert.That(type.IsValid, Is.False);
        }
        type.Effectiveness[other] = 2;
        Assert.Throws<InvalidOperationException>(() => MonsterTypeRules.Validate(type, _ => false));
        var species = new MonsterSpeciesDescriptor(Guid.NewGuid());
        foreach (var ids in new[] { new[] { other, other }, new[] { other, Guid.NewGuid(), Guid.NewGuid() }, new[] { Guid.Empty } })
        {
            species.TypeIds = ids;
            Assert.That(species.IsValid, Is.False);
        }
        species.TypeIds = [other];
        Assert.Throws<InvalidOperationException>(() => MonsterTypeRules.Validate(species, _ => false));
        species.Load("{\"Name\":\"Old Species\",\"BaseStats\":{\"HP\":8,\"Attack\":9,\"Defense\":10,\"Speed\":11}}");
        Assert.That(species.TypeIds, Is.Empty);
        Assert.That(species.IsValid, Is.True);
    }

    [Test]
    public void TypeAndSpeciesDraftCopiesUndoAndCancelPreserveReferences()
    {
        var defense = Guid.NewGuid();
        var source = new MonsterTypeDescriptor(Guid.NewGuid()) { DisplayColor = "#123456", Effectiveness = new() { [defense] = .5 } };
        var target = new MonsterTypeDescriptor(Guid.NewGuid());
        var id = target.Id;
        var before = target.JsonData;
        target.MakeBackup();
        target.Load(source.JsonData, keepCreationTime: true);
        Assert.That(target.Id, Is.EqualTo(id));
        Assert.That(target.Effectiveness[defense], Is.EqualTo(.5));
        target.Effectiveness[defense] = 2;
        Assert.That(source.Effectiveness[defense], Is.EqualTo(.5));
        target.RestoreBackup();
        Assert.That(target.JsonData, Is.EqualTo(before));
        var species = new MonsterSpeciesDescriptor(Guid.NewGuid()) { TypeIds = [source.Id, target.Id] };
        var draft = new MonsterSpeciesDescriptor(Guid.NewGuid());
        var draftId = draft.Id;
        draft.MakeBackup();
        draft.Load(species.JsonData);
        Assert.That(draft.Id, Is.EqualTo(draftId));
        Assert.That(draft.TypeIds, Is.EqualTo(species.TypeIds));
        draft.TypeIds[0] = Guid.NewGuid();
        Assert.That(species.TypeIds[0], Is.EqualTo(source.Id));
        draft.RestoreBackup();
        Assert.That(draft.TypeIds, Is.Empty);
    }

    [Test]
    public void ExistingPacketsCarryTypesAndAssignmentsWithoutNewPacketKeys()
    {
        var registry = new PacketTypeRegistry(NullLogger.Instance, typeof(IntersectPacket).Assembly);
        Assert.That(registry.TryRegisterBuiltIn(), Is.True);
        PackedIntersectPacket.AddKnownTypes(registry.Types);
        var type = new MonsterTypeDescriptor(Guid.NewGuid()) { Effectiveness = new() { [Guid.NewGuid()] = 0 } };
        var save = new SaveGameObjectPacket(type.Type, type.Id, type.JsonData);
        var restored = MessagePacker.Instance.Deserialize<SaveGameObjectPacket>(MessagePacker.Instance.Serialize(save))!;
        var content = new GameObjectPacket(restored.Id, restored.Type, restored.Data, false, false);
        var packet = MessagePacker.Instance.Deserialize<GameObjectPacket>(MessagePacker.Instance.Serialize(content))!;
        var loaded = new MonsterTypeDescriptor(packet.Id);
        loaded.Load(packet.Data);
        Assert.That(loaded.JsonData, Is.EqualTo(type.JsonData));
        var species = new MonsterSpeciesDescriptor(Guid.NewGuid()) { TypeIds = [type.Id] };
        var copy = new MonsterSpeciesDescriptor(species.Id);
        copy.Load(species.JsonData);
        Assert.That(copy.TypeIds, Is.EqualTo(new[] { type.Id }));
        MonsterTypeDescriptor.Lookup.Set(type.Id, type);
        try
        {
            var packets = new List<GameObjectPacket>();
            Intersect.Server.Networking.PacketSender.SendGameObjects(null!, GameObjectType.MonsterType, packets);
            Assert.That(packets.Single(packet => packet.Id == type.Id).Data, Is.EqualTo(type.JsonData));
        }
        finally { MonsterTypeDescriptor.Lookup.DeleteAt(type.Id); }
        var speciesSave = new SaveGameObjectPacket(species.Type, species.Id, species.JsonData);
        var speciesPacket = MessagePacker.Instance.Deserialize<SaveGameObjectPacket>(MessagePacker.Instance.Serialize(speciesSave))!;
        copy.Load(speciesPacket.Data);
        Assert.That(copy.TypeIds, Is.EqualTo(species.TypeIds));
    }

    private static DatabaseContextOptions OptionsFor(string directory) => new()
    {
        DatabaseType = DatabaseType.Sqlite, AutoDetectChanges = true,
        ConnectionStringBuilder = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "game.db"), Pooling = false },
    };
    private static void WithDatabase(Action<DatabaseContextOptions> test)
    {
        Options.EnsureCreated();
        var directory = Path.Combine(Path.GetTempPath(), "Intersect-MonsterTypes-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try { test(OptionsFor(directory)); }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(directory)) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())))
                throw new InvalidOperationException("Not a disposable test directory.");
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void SqliteMigrationAndReopenedContextsPreserveLegacyAndNewContent()
    {
        WithDatabase(options =>
        {
            var legacyId = Guid.NewGuid();
            var first = new MonsterTypeDescriptor(Guid.NewGuid()) { Name = "Ember", DisplayColor = "#FF3311" };
            var second = new MonsterTypeDescriptor(Guid.NewGuid()) { Name = "Rain" };
            using (var db = new SqliteGameContext(options))
            {
                db.GetService<IMigrator>().Migrate("20261107000000_AddMonsterSpecies");
                db.Database.ExecuteSqlInterpolated($"INSERT INTO MonsterSpecies (Id, Name, Description, Sprite, Folder, TimeCreated, BaseStats) VALUES ({legacyId}, {"Legacy"}, {string.Empty}, {string.Empty}, {string.Empty}, {1L}, {"{\"HP\":17,\"Attack\":9,\"Defense\":8,\"Speed\":7}"})");
                db.Database.Migrate();
                Assert.That(db.Database.HasPendingModelChanges(), Is.False);
                var legacy = db.MonsterSpecies.Single();
                Assert.That(legacy.Id, Is.EqualTo(legacyId));
                Assert.That(legacy.TypeIds, Is.Empty);
                Assert.That(legacy.BaseStats.HP, Is.EqualTo(17));
                first.Effectiveness[second.Id] = .5;
                db.MonsterTypes.AddRange(first, second);
                legacy.TypeIds = [first.Id];
                db.SaveChanges();
            }
            using (var db = new SqliteGameContext(options))
            {
                var loaded = db.MonsterTypes.Single(type => type.Id == first.Id);
                Assert.That(loaded.Effectiveness[second.Id], Is.EqualTo(.5));
                Assert.That(loaded.DisplayColor, Is.EqualTo("#FF3311"));
                loaded.Name = "Edited Ember";
                loaded.Effectiveness[second.Id] = 2;
                db.MonsterSpecies.Single().TypeIds = [first.Id, second.Id];
                db.SaveChanges();
            }
            using (var db = new SqliteGameContext(options))
            {
                Assert.That(db.MonsterTypes.Count(), Is.EqualTo(2));
                Assert.That(db.MonsterTypes.Single(type => type.Id == first.Id).Name, Is.EqualTo("Edited Ember"));
                Assert.That(db.MonsterTypes.Single(type => type.Id == first.Id).Effectiveness[second.Id], Is.EqualTo(2));
                Assert.That(db.MonsterSpecies.Single().TypeIds, Is.EqualTo(new[] { first.Id, second.Id }));
                Assert.That(db.MonsterSpecies.Single().Id, Is.EqualTo(legacyId));
            }
        });
    }

    [Test]
    public void DatabaseSaveRejectsDanglingDuplicateAndExcessiveAssignments()
    {
        WithDatabase(options =>
        {
            using var db = new SqliteGameContext(options);
            db.Database.EnsureCreated();
            var first = new MonsterTypeDescriptor(Guid.NewGuid());
            db.MonsterTypes.Add(first);
            db.SaveChanges();
            foreach (var ids in new[] { new[] { first.Id, first.Id }, new[] { first.Id, Guid.NewGuid(), Guid.NewGuid() }, new[] { Guid.NewGuid() } })
            {
                db.MonsterSpecies.Add(new MonsterSpeciesDescriptor(Guid.NewGuid()) { TypeIds = ids });
                Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
                db.ChangeTracker.Clear();
            }
            db.MonsterTypes.Add(new MonsterTypeDescriptor(Guid.NewGuid()) { Effectiveness = new() { [Guid.NewGuid()] = 2 } });
            Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        });
    }

    [Test]
    public void ReferencedTypeDeletionIsRejectedUntilAllReferencesAreRemoved()
    {
        WithDatabase(options =>
        {
            using var db = new SqliteGameContext(options);
            db.Database.EnsureCreated();
            var first = new MonsterTypeDescriptor(Guid.NewGuid());
            var second = new MonsterTypeDescriptor(Guid.NewGuid()) { Effectiveness = new() { [first.Id] = 2 } };
            var species = new MonsterSpeciesDescriptor(Guid.NewGuid()) { TypeIds = [first.Id] };
            db.AddRange(first, second, species);
            db.SaveChanges();
            db.MonsterTypes.Remove(first);
            Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
            species.TypeIds = [];
            Assert.Throws<InvalidOperationException>(() => db.SaveChanges(), "Other type still references it.");
            second.Effectiveness.Clear();
            db.SaveChanges();
            Assert.That(db.MonsterTypes.Any(type => type.Id == first.Id), Is.False);
            Assert.That(db.MonsterTypes.Single().Id, Is.EqualTo(second.Id));
        });
    }

    [Test]
    public void MySqlMigrationAndSnapshotMatchRuntimeModelWithoutConnecting()
    {
        Options.EnsureCreated();
        using var db = new OfflineMySql(new DatabaseContextOptions
        {
            DatabaseType = DatabaseType.MySql,
            ConnectionStringBuilder = new MySqlConnector.MySqlConnectionStringBuilder { Server = "127.0.0.1", Database = "unused_types", UserID = "unused" },
        });
        var migration = new Intersect.Server.Migrations.MySql.Game.AddMonsterTypes();
        var target = db.GetService<IModelRuntimeInitializer>().Initialize(new Intersect.Server.Migrations.MySql.Game.AddMonsterStatuses().TargetModel, designTime: true);
        var current = db.GetService<IDesignTimeModel>().Model;
        Assert.That(db.GetService<IMigrationsModelDiffer>().GetDifferences(target.GetRelationalModel(), current.GetRelationalModel()), Is.Empty);
        var sql = string.Join("\n", db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, current).Select(command => command.CommandText));
        Assert.That(sql, Does.Contain("CREATE TABLE `MonsterTypes`"));
        Assert.That(sql, Does.Contain("ADD `Types` longtext"));
        Assert.That(sql, Does.Not.Contain("DROP TABLE"));
    }
    private sealed class OfflineMySql(DatabaseContextOptions options) : GameContext(options)
    {
        public override DatabaseType DatabaseType => DatabaseType.MySql;
        protected override void OnConfiguringProvider(DbContextOptionsBuilder builder, string connectionString) =>
            builder.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36)));
    }
}
