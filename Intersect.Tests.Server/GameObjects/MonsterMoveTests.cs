using Intersect.Config;
using Intersect.Enums;
using Intersect.Framework.Core.GameObjects.Animations;
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
public class MonsterMoveTests
{
    private static MonsterMoveDescriptor Move(Guid type) => new(Guid.NewGuid()) { Name = "Custom Move", TypeId = type };

    [Test]
    public void CategoriesTargetsAccuracyAndSignedPriorityAreValidatedIndependentlyOfType()
    {
        Assert.That((int)GameObjectType.MonsterType, Is.EqualTo(20));
        Assert.That((int)GameObjectType.MonsterMove, Is.EqualTo(21));
        Assert.That(GameObjectType.MonsterMove.CreateNew(), Is.TypeOf<MonsterMoveDescriptor>());
        var move = Move(Guid.NewGuid());
        Assert.That(move.IsValid, Is.True);
        foreach (var category in Enum.GetValues<MonsterMoveCategory>())
        {
            move.Category = category;
            move.BasePower = 0;
            Assert.That(move.IsValid, Is.EqualTo(category == MonsterMoveCategory.Status));
            move.BasePower = 10;
            foreach (var target in Enum.GetValues<MonsterMoveTarget>())
            foreach (var priority in new[] { int.MinValue, -2, 0, 3, int.MaxValue })
            {
                move.Target = target;
                move.Priority = priority;
                Assert.That(move.IsValid, Is.True);
            }
        }
        foreach (var accuracy in new[] { 1, 100 }) { move.Accuracy = accuracy; Assert.That(move.IsValid, Is.True); }
        foreach (var invalid in new[] { -1, 0, 101 })
        {
            move.Accuracy = invalid;
            move.AlwaysHits = true;
            Assert.That(move.IsValid, Is.False, "Always Hits is explicit, not a sentinel accuracy value.");
        }
        move.Accuracy = 100;
        move.Target = (MonsterMoveTarget)99;
        Assert.That(move.IsValid, Is.False);
        move.Target = MonsterMoveTarget.Self;
        move.Category = (MonsterMoveCategory)99;
        Assert.That(move.IsValid, Is.False);
        move.Category = MonsterMoveCategory.Status;
        move.BasePower = -1;
        Assert.That(move.IsValid, Is.False);
        move.BasePower = 0;
        move.AnimationId = Guid.Empty;
        Assert.That(move.IsValid, Is.False);
        move.AnimationId = null;
        move.TypeId = Guid.Empty;
        Assert.That(move.IsValid, Is.False);
    }

    [Test]
    public void MoveCopyUndoAndLegacyLearnsetLoadsPreserveIdentitiesAndDraftIsolation()
    {
        var source = Move(Guid.NewGuid());
        source.Description = "Reusable definition";
        source.Category = MonsterMoveCategory.Special;
        source.BasePower = 81;
        source.Accuracy = 92;
        source.Priority = -2;
        source.Target = MonsterMoveTarget.OneAlly;
        source.AnimationId = Guid.NewGuid();
        var target = Move(source.TypeId);
        var before = target.JsonData;
        var id = target.Id;
        target.MakeBackup();
        target.Load(source.JsonData, keepCreationTime: true);
        Assert.That(target.Id, Is.EqualTo(id));
        Assert.That((target.TypeId, target.Category, target.BasePower, target.Accuracy, target.Priority, target.Target, target.AnimationId),
            Is.EqualTo((source.TypeId, source.Category, 81, 92, -2, source.Target, source.AnimationId)));
        target.RestoreBackup();
        Assert.That(target.JsonData, Is.EqualTo(before));
        var species = new MonsterSpeciesDescriptor(Guid.NewGuid())
        {
            Learnset = [new() { Level = 1, MoveId = source.Id }, new() { Level = 1, MoveId = target.Id }, new() { Level = 10, MoveId = source.Id }],
        };
        Assert.That(species.IsValid, Is.True);
        var draft = new MonsterSpeciesDescriptor(species.Id);
        draft.Load(species.JsonData);
        draft.MakeBackup();
        draft.Learnset[0].Level = 42;
        Assert.That(species.Learnset[0].Level, Is.EqualTo(1));
        draft.RestoreBackup();
        Assert.That(draft.JsonData, Is.EqualTo(species.JsonData));
        draft.Load("{\"Name\":\"Legacy\",\"BaseStats\":{\"HP\":19,\"Attack\":7,\"Defense\":8,\"Speed\":9}}");
        Assert.That(draft.Id, Is.EqualTo(species.Id));
        Assert.That(draft.Learnset, Is.Empty);
        Assert.That(draft.BaseStats.HP, Is.EqualTo(19));
        draft.LearnsetJson = null!;
        Assert.That(draft.Learnset, Is.Empty);
    }

    [Test]
    public void MoveAndLearnsetReferencesAndPositiveLevelsAreRequired()
    {
        var move = Move(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => MonsterMoveRules.Validate(move, _ => false, _ => true));
        move.AnimationId = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => MonsterMoveRules.Validate(move, _ => true, _ => false));
        var species = new MonsterSpeciesDescriptor(Guid.NewGuid());
        foreach (var entry in new[] { new MonsterLearnsetEntry { MoveId = move.Id, Level = 0 }, new() { MoveId = Guid.Empty, Level = 1 }, new() { MoveId = move.Id, Level = -1 } })
        {
            species.Learnset = [entry];
            Assert.That(species.IsValid, Is.False);
            Assert.Throws<InvalidOperationException>(() => MonsterMoveRules.ValidateLearnset(species, _ => true));
        }
        species.Learnset = [new() { MoveId = move.Id, Level = int.MaxValue }];
        Assert.That(species.IsValid, Is.True);
        Assert.Throws<InvalidOperationException>(() => MonsterMoveRules.ValidateLearnset(species, _ => false));
    }

    [Test]
    public void ExistingContentPacketsAndServerContentListCarryMovesAndLearnsets()
    {
        var registry = new PacketTypeRegistry(NullLogger.Instance, typeof(IntersectPacket).Assembly);
        Assert.That(registry.TryRegisterBuiltIn(), Is.True);
        PackedIntersectPacket.AddKnownTypes(registry.Types);
        var move = Move(Guid.NewGuid());
        move.AlwaysHits = true;
        var save = new SaveGameObjectPacket(move.Type, move.Id, move.JsonData);
        var packet = MessagePacker.Instance.Deserialize<SaveGameObjectPacket>(MessagePacker.Instance.Serialize(save))!;
        var loaded = new MonsterMoveDescriptor(packet.Id);
        loaded.Load(packet.Data);
        Assert.That(loaded.JsonData, Is.EqualTo(move.JsonData));
        MonsterMoveDescriptor.Lookup.Set(move.Id, move);
        try
        {
            var packets = new List<GameObjectPacket>();
            Intersect.Server.Networking.PacketSender.SendGameObjects(null!, GameObjectType.MonsterMove, packets);
            Assert.That(packets.Single(item => item.Id == move.Id).Data, Is.EqualTo(move.JsonData));
        }
        finally { MonsterMoveDescriptor.Lookup.DeleteAt(move.Id); }
        var species = new MonsterSpeciesDescriptor(Guid.NewGuid()) { Learnset = [new() { Level = 1, MoveId = move.Id }] };
        var content = new GameObjectPacket(species.Id, species.Type, species.JsonData, false, false);
        var restored = MessagePacker.Instance.Deserialize<GameObjectPacket>(MessagePacker.Instance.Serialize(content))!;
        var copy = new MonsterSpeciesDescriptor(restored.Id);
        copy.Load(restored.Data);
        Assert.That(copy.Learnset.Single().MoveId, Is.EqualTo(move.Id));
        Assert.That(copy.Learnset.Single().Level, Is.EqualTo(1));
    }

    private static void WithDatabase(Action<DatabaseContextOptions> test)
    {
        Options.EnsureCreated();
        var directory = Path.Combine(Path.GetTempPath(), "Intersect-MonsterMoves-" + Guid.NewGuid());
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
                throw new InvalidOperationException("Refusing to delete outside the disposable test directory.");
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void SqliteUpgradePersistsSharedMovesLearnsetsAndLegacyContentAcrossContexts()
    {
        WithDatabase(options =>
        {
            var typeId = Guid.NewGuid();
            var legacyId = Guid.NewGuid();
            var physical = Move(typeId);
            physical.BasePower = 45;
            physical.Accuracy = 95;
            physical.Priority = 1;
            physical.AnimationPosition = BattleMoveAnimationPosition.Both;
            physical.Effects = [new() { Stat = MonsterStat.Defense, Target = MonsterMoveEffectTarget.Opponent, Amount = -2 }];
            var status = Move(typeId);
            status.Category = MonsterMoveCategory.Status;
            status.BasePower = 0;
            status.AlwaysHits = true;
            status.Target = MonsterMoveTarget.AllOpponents;
            var secondSpecies = new MonsterSpeciesDescriptor(Guid.NewGuid()) { Learnset = [new() { MoveId = physical.Id, Level = 1 }] };
            using (var db = new SqliteGameContext(options))
            {
                db.GetService<IMigrator>().Migrate("20261108010000_AddMonsterTypes");
                db.Database.ExecuteSqlInterpolated($"INSERT INTO MonsterTypes (Id, Name, Description, Folder, DisplayColor, TimeCreated, Effectiveness) VALUES ({typeId}, {"Legacy type"}, {string.Empty}, {string.Empty}, {"#123456"}, {1L}, {"{}"})");
                db.Database.ExecuteSqlInterpolated($"INSERT INTO MonsterSpecies (Id, Name, Description, Sprite, Folder, TimeCreated, BaseStats, Types) VALUES ({legacyId}, {"Legacy species"}, {string.Empty}, {string.Empty}, {string.Empty}, {1L}, {"{\"HP\":17,\"Attack\":9,\"Defense\":8,\"Speed\":7}"}, {"[]"})");
                db.Database.Migrate();
                Assert.That(db.Database.HasPendingModelChanges(), Is.False);
                var legacy = db.MonsterSpecies.Single();
                Assert.That(legacy.Id, Is.EqualTo(legacyId));
                Assert.That(legacy.BaseStats.HP, Is.EqualTo(17));
                Assert.That(legacy.Learnset, Is.Empty);
                db.Database.OpenConnection();
                using (var command = db.Database.GetDbConnection().CreateCommand())
                {
                    command.CommandText = "SELECT Learnset FROM MonsterSpecies";
                    Assert.That(command.ExecuteScalar(), Is.EqualTo(DBNull.Value), "Migration/read does not populate old learnsets.");
                }
                db.MonsterMoves.AddRange(physical, status);
                legacy.Learnset = [new() { MoveId = physical.Id, Level = 8 }, new() { MoveId = physical.Id, Level = 1 }, new() { MoveId = status.Id, Level = 1 }];
                db.MonsterSpecies.Add(secondSpecies);
                db.SaveChanges();
            }
            using (var db = new SqliteGameContext(options))
            {
                var loaded = db.MonsterMoves.Single(move => move.Id == physical.Id);
                Assert.That(loaded.AnimationPosition, Is.EqualTo(BattleMoveAnimationPosition.Both));
                Assert.That(loaded.Effects, Is.EqualTo(physical.Effects));
                Assert.That((loaded.BasePower, loaded.Accuracy, loaded.Priority), Is.EqualTo((45, 95, 1)));
                Assert.That(db.MonsterMoves.Single(move => move.Id == status.Id).BasePower, Is.Zero);
                Assert.That(db.MonsterSpecies.Single(species => species.Id == legacyId).Learnset.Count(entry => entry.Level == 1), Is.EqualTo(2));
                Assert.That(db.MonsterSpecies.Single(species => species.Id == secondSpecies.Id).Learnset.Single().MoveId, Is.EqualTo(physical.Id));
                loaded.Category = MonsterMoveCategory.Special;
                loaded.BasePower = 71;
                loaded.Priority = -3;
                loaded.Target = MonsterMoveTarget.AllBattlefield;
                loaded.Effects.Clear();
                db.SaveChanges();
            }
            using (var db = new SqliteGameContext(options))
            {
                var loaded = db.MonsterMoves.Single(move => move.Id == physical.Id);
                Assert.That((loaded.Category, loaded.BasePower, loaded.Priority, loaded.Target), Is.EqualTo((MonsterMoveCategory.Special, 71, -3, MonsterMoveTarget.AllBattlefield)));
                Assert.That(db.MonsterTypes.Single().DisplayColor, Is.EqualTo("#123456"));
                Assert.That(db.MonsterSpecies.Single(species => species.Id == legacyId).Learnset.Count, Is.EqualTo(3));
            }
        });
    }

    [Test]
    public void LegacyAnimationPositionDefaultsToTargetInJsonAndAfterSqliteUpgrade()
    {
        var move = Move(Guid.NewGuid());
        move.AnimationPosition = BattleMoveAnimationPosition.Both;
        var oldJson = Newtonsoft.Json.Linq.JObject.Parse(move.JsonData);
        oldJson.Remove(nameof(MonsterMoveDescriptor.AnimationPosition));
        move.Load(oldJson.ToString());
        Assert.That(move.AnimationPosition, Is.EqualTo(BattleMoveAnimationPosition.Target));
        move.AnimationPosition = (BattleMoveAnimationPosition)99;
        Assert.That(move.IsValid, Is.False);

        WithDatabase(options =>
        {
            var animationId = Guid.NewGuid();
            using var db = new SqliteGameContext(options);
            db.GetService<IMigrator>().Migrate("20261109000200_AddMonsterBattleAppearance");
            db.Database.ExecuteSqlInterpolated($"INSERT INTO MonsterMoves (Id, Name, TimeCreated, Description, Folder, TypeId, Category, BasePower, Accuracy, AlwaysHits, Priority, Target, MaximumPP, AnimationId) VALUES ({move.Id}, {"Legacy move"}, {1L}, {string.Empty}, {string.Empty}, {move.TypeId}, {0}, {1}, {100}, {false}, {0}, {0}, {20}, {animationId})");
            db.Database.Migrate();
            Assert.That(db.Database.HasPendingModelChanges(), Is.False);
            var loaded = db.MonsterMoves.Single();
            Assert.That(loaded.AnimationId, Is.EqualTo(animationId));
            Assert.That(loaded.AnimationPosition, Is.EqualTo(BattleMoveAnimationPosition.Target));
            Assert.That(loaded.Effects, Is.Empty);
        });
    }

    [Test]
    public void DeletionGuardsProtectSharedLearnsetsTypesAndOptionalAnimations()
    {
        WithDatabase(options =>
        {
            using var db = new SqliteGameContext(options);
            db.Database.EnsureCreated();
            var type = new MonsterTypeDescriptor(Guid.NewGuid());
            var animation = new AnimationDescriptor(Guid.NewGuid());
            var move = Move(type.Id);
            move.AnimationId = animation.Id;
            var first = new MonsterSpeciesDescriptor(Guid.NewGuid()) { Learnset = [new() { MoveId = move.Id, Level = 1 }] };
            var second = new MonsterSpeciesDescriptor(Guid.NewGuid()) { Learnset = [new() { MoveId = move.Id, Level = 2 }] };
            db.AddRange(type, animation, move, first, second);
            db.SaveChanges();
            db.MonsterTypes.Remove(type);
            Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
            db.Entry(type).State = EntityState.Unchanged;
            db.Animations.Remove(animation);
            Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
            db.Entry(animation).State = EntityState.Unchanged;
            db.MonsterMoves.Remove(move);
            Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
            first.Learnset.Clear();
            Assert.Throws<InvalidOperationException>(() => db.SaveChanges(), "The other species still references the shared move.");
            second.Learnset.Clear();
            db.SaveChanges();
            db.MonsterTypes.Remove(type);
            db.Animations.Remove(animation);
            db.SaveChanges();
            Assert.That(db.MonsterMoves.Count(), Is.Zero);
            Assert.That(db.MonsterTypes.Count(), Is.Zero);
        });
    }

    [Test]
    public void DatabaseSaveRejectsMissingMoveTypeAnimationAndInvalidLearnsetReferences()
    {
        WithDatabase(options =>
        {
            using var db = new SqliteGameContext(options);
            db.Database.EnsureCreated();
            db.MonsterMoves.Add(Move(Guid.NewGuid()));
            Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
            db.ChangeTracker.Clear();
            db.MonsterSpecies.Add(new MonsterSpeciesDescriptor(Guid.NewGuid()) { Learnset = [new() { MoveId = Guid.NewGuid(), Level = 1 }] });
            Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
            db.ChangeTracker.Clear();
            var type = new MonsterTypeDescriptor(Guid.NewGuid());
            db.MonsterTypes.Add(type);
            var missingAnimation = Move(type.Id);
            missingAnimation.AnimationId = Guid.NewGuid();
            db.MonsterMoves.Add(missingAnimation);
            Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        });
    }

    [Test]
    public void MySqlMigrationMatchesModelAndGeneratesAdditiveSqlWithoutConnecting()
    {
        Options.EnsureCreated();
        using var db = new OfflineMySql(new DatabaseContextOptions
        {
            DatabaseType = DatabaseType.MySql,
            ConnectionStringBuilder = new MySqlConnector.MySqlConnectionStringBuilder { Server = "127.0.0.1", Database = "unused_moves", UserID = "unused" },
        });
        var migration = new Intersect.Server.Migrations.MySql.Game.AddMonsterMoves();
        var target = db.GetService<IModelRuntimeInitializer>().Initialize(new Intersect.Server.Migrations.MySql.Game.AddMonsterStatuses().TargetModel, designTime: true);
        var current = db.GetService<IDesignTimeModel>().Model;
        Assert.That(db.GetService<IMigrationsModelDiffer>().GetDifferences(target.GetRelationalModel(), current.GetRelationalModel()), Is.Empty);
        var sql = string.Join("\n", db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, current).Select(command => command.CommandText));
        Assert.That(sql, Does.Contain("CREATE TABLE `MonsterMoves`"));
        Assert.That(sql, Does.Contain("ADD `Learnset` longtext"));
        Assert.That(sql, Does.Not.Contain("DROP TABLE"));
        var positionMigration = new Intersect.Server.Migrations.MySql.Game.AddMonsterMoveAnimationPosition();
        var positionSql = string.Join("\n", db.GetService<IMigrationsSqlGenerator>()
            .Generate(positionMigration.UpOperations, current).Select(command => command.CommandText));
        Assert.That(positionSql, Does.Contain("ADD `AnimationPosition` int NOT NULL DEFAULT 0"));
        var effectsMigration = new Intersect.Server.Migrations.MySql.Game.AddMonsterMoveEffects();
        var effectsSql = string.Join("\n", db.GetService<IMigrationsSqlGenerator>()
            .Generate(effectsMigration.UpOperations, current).Select(command => command.CommandText));
        Assert.That(effectsSql, Does.Contain("ADD `Effects` longtext NULL"));
    }
    [Test]
    public void StatusDefinitionAndReferencesSurviveMigrationAndProtectDeletion()
    {
        WithDatabase(options =>
        {
            var status = new MonsterStatusDescriptor(Guid.NewGuid())
            {
                Name = "Example", Damage = MonsterStatusDamage.Percent, DamageAmount = 12.5m,
                PersistOutsideBattle = true,
            };
            using (var db = new SqliteGameContext(options))
            {
                db.GetService<IMigrator>().Migrate("20261110000200_AddMonsterMoveEffects");
                db.Database.Migrate();
                Assert.That(db.Database.HasPendingModelChanges(), Is.False);
                var type = new MonsterTypeDescriptor(Guid.NewGuid());
                var move = Move(type.Id);
                move.Effects = [new() { Kind = MonsterMoveEffectKind.ApplyStatus, StatusId = status.Id }];
                db.AddRange(type, status, move);
                db.SaveChanges();
            }
            using (var db = new SqliteGameContext(options))
            {
                var loaded = db.MonsterStatuses.Single();
                Assert.That(loaded.Id, Is.EqualTo(status.Id));
                Assert.That(loaded.DamageAmount, Is.EqualTo(12.5m));
                Assert.That(loaded.PersistOutsideBattle, Is.True);
                db.MonsterStatuses.Remove(loaded);
                Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
                db.MonsterMoves.Single().Effects.Clear();
                db.StatusReferenceExists = id => id == status.Id;
                Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
                db.StatusReferenceExists = null;
                db.SaveChanges();
                Assert.That(db.MonsterStatuses, Is.Empty);
            }
        });
    }

    private sealed class OfflineMySql(DatabaseContextOptions options) : GameContext(options)
    {
        public override DatabaseType DatabaseType => DatabaseType.MySql;
        protected override void OnConfiguringProvider(DbContextOptionsBuilder builder, string connectionString) =>
            builder.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36)));
    }
}
