using Intersect.Config;
using Intersect.Enums;
using Intersect.Framework.Core.GameObjects.Maps;
using Intersect.Framework.Core.GameObjects.Maps.Attributes;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Network.Packets.Client;
using Intersect.Network.Packets.Server;
using Intersect.Server.Database;
using Intersect.Server.Database.GameData;
using Intersect.Server.Maps;
using Intersect.Server.Monsters;
using Intersect.Server.Monsters.Battles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NUnit.Framework;

namespace Intersect.Tests.Server.GameObjects;

[TestFixture, NonParallelizable]
public sealed class MonsterEncounterTests
{
    private static MonsterEncounterTableDescriptor Table(Guid first, Guid second) => new(Guid.NewGuid())
    {
        Name = "Route 1", Enabled = true, Rate = 37, Cooldown = 2,
        Entries = [new(first, 50, 2, 5), new(first, 0, 7, 7), new(second, 30, 3, 6), new(first, 20, 8, 8)],
    };

    [Test]
    public void RateEntryAndLevelDrawsAreSeparateAndRespectBothBoundaries()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var table = Table(first, second);
        var random = new ControlledMonsterRandom(36, 50, 3);
        var result = MonsterEncounterSelection.Roll(table, null, 100, _ => true, random);
        Assert.That(result, Is.EqualTo(new MonsterEncounterSelection.Encounter(table.Id, second, 6)));
        Assert.That(random.Bounds, Is.EqualTo(new long[] { 100, 100, 4 }));
        Assert.That(MonsterEncounterSelection.Roll(table, null, 100, _ => true, new ControlledMonsterRandom(37)), Is.Null);
        Assert.That(MonsterEncounterSelection.Roll(table, 0, 100, _ => true, new ControlledMonsterRandom()), Is.Null);
        Assert.That(MonsterEncounterSelection.Roll(table, 100, 100, id => id == first, new ControlledMonsterRandom(99, 49, 0))!.Level, Is.EqualTo(2));
        Assert.That(MonsterEncounterSelection.Roll(table, 100, 100, id => id == first, new ControlledMonsterRandom(99, 50, 0))!.Level, Is.EqualTo(8));
    }

    [Test]
    public void ValidationAndDuplicateKeepIndependentOrderedEntries()
    {
        var species = Guid.NewGuid();
        var table = Table(species, species);
        var duplicate = MonsterEncounterTableDescriptor.Copy(Guid.NewGuid(), table);
        Assert.That(duplicate.Id, Is.Not.EqualTo(table.Id));
        Assert.That(duplicate.Entries, Is.EqualTo(table.Entries));
        duplicate.Entries.RemoveAt(0);
        Assert.That(table.Entries.Count, Is.EqualTo(4));
        Assert.That(table.ValidationError(100, id => id == species), Is.Null);
        Assert.That(table.ValidationError(100, _ => false), Is.Not.Null);
        Assert.That(table.ValidationError(4, _ => true), Is.Not.Null);
        table.Entries = [new(species, -1, 1, 1)];
        Assert.That(table.ValidationError(100, _ => true), Is.Not.Null);
        table.Entries = [new(species, 0, 1, 1)];
        Assert.That(table.ValidationError(100, _ => true), Is.Not.Null);
        table.Enabled = false;
        Assert.That(table.ValidationError(100, _ => true), Is.Null);
        Assert.That(MonsterEncounterSelection.Roll(table, 100, 100, _ => true, new ControlledMonsterRandom()), Is.Null);
    }

    [Test]
    public void MapOverlayPrecedencePreservesExistingAttributesAndOldJson()
    {
        Options.EnsureCreated();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var map = new MapDescriptor(Guid.NewGuid());
        var oldJson = Newtonsoft.Json.Linq.JObject.Parse(map.JsonData);
        oldJson.Remove(nameof(MapDescriptor.Encounters));
        var legacy = new MapDescriptor(map.Id);
        legacy.Load(oldJson.ToString());
        Assert.That(legacy.Encounters.Mode, Is.EqualTo(MapEncounterMode.Disabled));

        map.Attributes[1, 1] = new MapBlockedAttribute();
        map.Attributes[2, 2] = new MapWarpAttribute();
        map.Attributes[3, 3] = new MapResourceAttribute();
        map.Encounters.DefaultTableId = first;
        map.Encounters.Rate = 17;
        map.Encounters.Tiles[MapEncounters.TileKey(1, 1)] = new(second, 80);
        map.Encounters.Tiles[MapEncounters.TileKey(2, 2)] = new(Guid.Empty);
        Assert.That(map.Encounters.Resolve(1, 1), Is.Null);
        map.Encounters.Mode = MapEncounterMode.PaintedOnly;
        Assert.That(map.Encounters.Resolve(1, 1), Is.EqualTo(new MapEncounterTile(second, 80)));
        Assert.That(map.Encounters.Resolve(3, 3), Is.Null);
        map.Encounters.Mode = MapEncounterMode.WholeMap;
        Assert.That(map.Encounters.Resolve(3, 3), Is.EqualTo(new MapEncounterTile(first, 17)));
        Assert.That(map.Encounters.Resolve(2, 2), Is.Null);

        var copy = new MapDescriptor(map);
        Assert.That(copy.Attributes[1, 1], Is.TypeOf<MapBlockedAttribute>());
        Assert.That(copy.Attributes[2, 2], Is.TypeOf<MapWarpAttribute>());
        Assert.That(copy.Attributes[3, 3], Is.TypeOf<MapResourceAttribute>());
        copy.Encounters.Tiles.Clear();
        Assert.That(map.Encounters.Tiles.Count, Is.EqualTo(2));
        copy.Load(map.JsonData);
        Assert.That(copy.Encounters.Tiles, Is.EqualTo(map.Encounters.Tiles));
        copy.Load(oldJson.ToString());
        Assert.That(copy.Encounters.Mode, Is.EqualTo(MapEncounterMode.Disabled));
        Assert.That(copy.Encounters.Tiles, Is.Empty);
    }

    [Test]
    public void OnlyCompletedLocalStepsCountAndCooldownIsPerPlayer()
    {
        var map = Guid.NewGuid();
        Assert.That(MonsterEncounterSelection.IsEligibleStep(map, 2, 2, map, 3, 3, false), Is.True);
        Assert.That(MonsterEncounterSelection.IsEligibleStep(map, 2, 2, map, 2, 2, false), Is.False);
        Assert.That(MonsterEncounterSelection.IsEligibleStep(map, 2, 2, map, 2, 3, true), Is.False);
        Assert.That(MonsterEncounterSelection.IsEligibleStep(map, 2, 2, Guid.NewGuid(), 2, 3, false), Is.False);
        Assert.That(MonsterEncounterSelection.IsEligibleStep(map, 2, 2, map, 4, 2, false), Is.False);
        var first = new MonsterEncounterCooldown();
        var second = new MonsterEncounterCooldown();
        first.Start(2);
        Assert.That(first.ConsumeEligibleStep(), Is.True);
        Assert.That(second.ConsumeEligibleStep(), Is.False);
        Assert.That(first.ConsumeEligibleStep(), Is.True);
        Assert.That(first.ConsumeEligibleStep(), Is.False);
    }

    [Test]
    public void SqliteUpgradeMapAndTableEditsPersistAndReferencesPreventDeletion()
    {
        Options.EnsureCreated();
        var source = Path.Combine(Path.GetTempPath(), $"intersect-m6ib-{Guid.NewGuid():N}.db");
        var options = new DatabaseContextOptions
        {
            DatabaseType = DatabaseType.Sqlite, AutoDetectChanges = true,
            ConnectionStringBuilder = new SqliteConnectionStringBuilder { DataSource = source, Pooling = false },
        };
        try
        {
            var species = new MonsterSpeciesDescriptor(Guid.NewGuid());
            var table = Table(species.Id, species.Id);
            var map = new MapController(Guid.NewGuid()) { Name = "Existing Map" };
            using (var db = new SqliteGameContext(options))
            {
                db.GetService<IMigrator>().Migrate("20261113000000_AddMonsterGenders");
                InsertLegacyMap(db, map);
                db.Database.Migrate();
                Assert.That(db.Database.HasPendingModelChanges(), Is.False);
                var existing = db.Maps.Single();
                Assert.That(existing.Encounters.Mode, Is.EqualTo(MapEncounterMode.Disabled));
                db.MonsterSpecies.Add(species);
                db.MonsterEncounterTables.Add(table);
                existing.Encounters = new() { Mode = MapEncounterMode.WholeMap, DefaultTableId = table.Id };
                existing.Encounters.Tiles[MapEncounters.TileKey(1, 1)] = new(Guid.Empty);
                db.SaveChanges();
            }
            using (var db = new SqliteGameContext(options))
            {
                var persisted = db.Maps.Single();
                Assert.That(persisted.Encounters.Resolve(1, 1), Is.Null);
                Assert.That(persisted.Encounters.Resolve(2, 1)?.TableId, Is.EqualTo(table.Id));
                var savedTable = db.MonsterEncounterTables.Single();
                Assert.That(savedTable.Entries, Is.EqualTo(table.Entries));
                savedTable.Rate = 100;
                persisted.Name = "Renamed Map";
                db.SaveChanges();
                db.MonsterEncounterTables.Remove(savedTable);
                Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
                db.Entry(savedTable).State = EntityState.Unchanged;
                db.MonsterSpecies.Remove(db.MonsterSpecies.Single());
                Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
            }
            using (var db = new SqliteGameContext(options))
            {
                Assert.That(db.Maps.Single().Encounters.DefaultTableId, Is.EqualTo(table.Id));
                Assert.That(db.MonsterEncounterTables.Single().Rate, Is.EqualTo(100));
            }
        }
        finally { File.Delete(source); }
    }

    internal static void InsertLegacyMap(SqliteGameContext db, MapController map)
    {
        var empty = Guid.Empty;
        db.Database.ExecuteSqlInterpolated($"""
            INSERT INTO Maps (Id, Name, Attributes, TileData, AHue, BHue, Brightness, Down, Events,
                FogTransparency, FogXSpeed, FogYSpeed, GHue, HideEquipment, IsIndoors, Left, Lights, NpcSpawns,
                PlayerLightColor, PlayerLightExpand, PlayerLightIntensity, PlayerLightSize, RHue, Revision, Right,
                TimeCreated, Up, WeatherAnimation, WeatherIntensity, WeatherXSpeed, WeatherYSpeed, ZoneType)
            VALUES ({map.Id}, {map.Name}, {map.GetAttributeData()}, {map.TileData}, 0, 0, 100, {empty}, '[]',
                0, 0, 0, 0, 0, 0, {empty}, '[]', '[]', {map.PlayerLightColorJson}, 0, 255, 300, 0, 0, {empty},
                0, {empty}, {empty}, 0, 0, 0, 0)
            """);
    }

    [Test]
    public void WildGenerationRetainsSpeciesIvsNatureGenderAndMoves()
    {
        var type = Guid.NewGuid();
        var move = new MonsterMoveDescriptor(Guid.NewGuid()) { Name = "Strike", TypeId = type };
        var nature = new MonsterNatureDescriptor(Guid.NewGuid()) { Name = "Field Nature" };
        var gender = new MonsterGenderDescriptor(Guid.NewGuid()) { Name = "Field Gender" };
        var species = new MonsterSpeciesDescriptor(Guid.NewGuid())
        {
            GenderWeights = new() { [gender.Id] = 15 },
            Learnset = [new() { Level = 2, MoveId = move.Id }],
        };
        MonsterNatureDescriptor.Lookup.Set(nature.Id, nature);
        MonsterGenderDescriptor.Lookup.Set(gender.Id, gender);
        MonsterMoveDescriptor.Lookup.Set(move.Id, move);
        try
        {
            var random = new ControlledMonsterRandom(0, 1, 2, 3, 4, 5, 31, 0);
            var generated = GeneratedWildMonster.Create(species, 2, random);
            Assert.That(generated.SpeciesId, Is.EqualTo(species.Id));
            Assert.That(generated.Level, Is.EqualTo(2));
            Assert.That(generated.IVs.ToArray(), Is.EqualTo(new long[] { 1, 2, 3, 4, 5, 31 }));
            Assert.That(generated.NatureId, Is.EqualTo(nature.Id));
            Assert.That(generated.GenderId, Is.EqualTo(gender.Id));
            Assert.That(generated.Moves[0], Is.EqualTo(move.Id));
            Assert.That(generated.Id, Is.Not.EqualTo(species.Id));
        }
        finally
        {
            MonsterNatureDescriptor.Lookup.DeleteAt(nature.Id);
            MonsterGenderDescriptor.Lookup.DeleteAt(gender.Id);
            MonsterMoveDescriptor.Lookup.DeleteAt(move.Id);
        }
    }

    [Test]
    public void RegistryIsolatesOwnersAndRunIsWildOnlyWithNoTurnCost()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var practiceOwner = Guid.NewGuid();
        var table = Guid.NewGuid();
        var publications = new Dictionary<Guid, MonsterBattleStatePacket>();
        var registry = new MonsterBattleRegistry((id, state) => publications[id] = state);
        var type = new MonsterTypeDescriptor(Guid.NewGuid()) { Name = "Neutral" };
        var move = new MonsterMoveDescriptor(Guid.NewGuid()) { Name = "Strike", TypeId = type.Id };
        MonsterBattleCombatant Actor() => new(new(Guid.NewGuid(), 5, new(20, 8, 8, 8, 8, 8), [type.Id]),
            "Battler", "", [new(move, type), null, null, null]);
        MonsterBattleSession Session(Guid trainer, bool wild)
        {
            var opponent = Actor();
            var generated = wild ? new GeneratedWildMonster(opponent.Id, Guid.NewGuid(), 5, new(), null, null, [move.Id]) : null;
            return new(new(trainer, [Actor()]), new(null, [opponent]), MonsterBattleRules.Generation4,
                new ControlledMonsterRandom(), wild: generated, encounterTableId: wild ? table : null);
        }
        registry.Start(owner, () => Session(owner, true));
        registry.Start(other, () => Session(other, true));
        registry.Start(practiceOwner, () => Session(practiceOwner, false));
        Assert.That(registry.HasActiveWild(owner), Is.True);
        Assert.That(registry.HasActiveWild(other), Is.True);
        Assert.That(registry.HasActiveWild(practiceOwner), Is.False);
        Assert.That(registry.InspectWild(owner)?.Monster.Id, Is.Not.EqualTo(registry.InspectWild(other)?.Monster.Id));
        Assert.That(registry.InspectWild(owner)?.TableId, Is.EqualTo(table));
        Assert.Throws<InvalidOperationException>(() => registry.Start(owner, () => Session(owner, true)));
        MonsterBattleActionPacket Run(Guid player)
        {
            var state = publications[player];
            return new() { BattleId = state.BattleId, Revision = state.Revision, Turn = state.Turn, Kind = MonsterBattleActionKind.Run };
        }
        Assert.That(publications[practiceOwner].CanRun, Is.False);
        Assert.Throws<InvalidOperationException>(() => registry.Submit(practiceOwner, Run(practiceOwner)));
        Assert.That(publications[owner].CanRun, Is.True);
        var before = publications[owner];
        registry.Submit(owner, Run(owner));
        var after = publications[owner];
        Assert.That(after.Outcome, Is.EqualTo(MonsterBattleOutcome.Escaped));
        Assert.That(after.IsWild, Is.True);
        Assert.That(after.CanRun, Is.False);
        Assert.That(after.AwaitingPresentationClose, Is.False);
        Assert.That(after.Player.HP, Is.EqualTo(before.Player.HP));
        Assert.That(after.Moves[0].PP, Is.EqualTo(before.Moves[0].PP));
        Assert.That(registry.HasActive(owner), Is.False);
        Assert.That(registry.HasActive(other), Is.True);
        registry.Start(owner, () => Session(owner, true));
        registry.Cancel(owner, "Disconnect", disconnect: true);
        Assert.That(registry.HasActive(owner), Is.False);
        Assert.That(registry.HasActive(other), Is.True);
    }
    [Test]
    public void WildOpponentWithNoMovesPassesAndVictoryPersistsOnlyTheParty()
    {
        var owner = Guid.NewGuid();
        var table = Guid.NewGuid();
        var type = new MonsterTypeDescriptor(Guid.NewGuid()) { Name = "Neutral" };
        var move = new MonsterMoveDescriptor(Guid.NewGuid()) { Name = "Strike", TypeId = type.Id, BasePower = 100, AlwaysHits = true };
        var lead = new MonsterBattleCombatant(new(Guid.NewGuid(), 5, new(20, 10, 10, 10, 10, 10), [type.Id]),
            "Lead", "", [new(move, type), null, null, null]);
        var opponent = new MonsterBattleCombatant(new(Guid.NewGuid(), 1, new(1, 1, 1, 1, 1, 1), [type.Id]),
            "Wild", "", [null, null, null, null]);
        var generated = new GeneratedWildMonster(opponent.Id, Guid.NewGuid(), 1, new(), null, null, [null, null, null, null]);
        var snapshot = new MonsterBattleStatePacket();
        var registry = new MonsterBattleRegistry((_, state) => snapshot = state);
        var persisted = new List<Guid>();
        registry.Start(owner, () => new(new(owner, [lead]), new(null, [opponent]), MonsterBattleRules.Generation4,
            new ControlledMonsterRandom(0, 0), persistParty: party => persisted.AddRange(party.Select(member => member.Id)),
            wild: generated, encounterTableId: table));
        Assert.That(snapshot.Opponent.Id, Is.EqualTo(generated.Id));
        registry.Submit(owner, new() { BattleId = snapshot.BattleId, Turn = snapshot.Turn, Revision = snapshot.Revision, MoveSlot = 0 });
        Assert.That(snapshot.Outcome, Is.EqualTo(MonsterBattleOutcome.Victory));
        Assert.That(snapshot.Opponent.Id, Is.EqualTo(generated.Id));
        Assert.That(snapshot.Player.HP, Is.EqualTo(lead.HP));
        Assert.That(snapshot.Moves[0].PP, Is.EqualTo(move.MaximumPP - 1));
        Assert.That(persisted, Is.EqualTo(new[] { lead.Id }));
        Assert.That(registry.HasActive(owner), Is.False);
    }
}
