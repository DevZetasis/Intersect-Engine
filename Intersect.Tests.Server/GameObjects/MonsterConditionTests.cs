using Intersect.Config;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Framework.Core.GameObjects.Events;
using Intersect.Framework.Core.GameObjects.Events.Commands;
using Intersect.Network.Packets.Client;
using Intersect.Network.Packets.Server;
using Intersect.Server.Database;
using Intersect.Server.Database.PlayerData;
using Intersect.Server.Database.PlayerData.Players;
using Intersect.Server.Entities;
using Intersect.Server.Monsters;
using Intersect.Server.Monsters.Battles;
using Intersect.Server.Monsters.Calculations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using NUnit.Framework;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Intersect.Tests.Server.GameObjects;

[TestFixture, NonParallelizable]
public sealed class MonsterConditionTests
{
    private string _directory = null!;
    private DatabaseContextOptions _options = null!;
    private Guid _owner, _other;
    private MonsterSpeciesDescriptor _species = null!;
    private MonsterMoveDescriptor _move = null!, _second = null!;
    private readonly HashSet<Guid> _reserved = [];
    private bool _beforeFailure, _afterFailure;
    private PlayerContext Context()
    {
        var db = new SqlitePlayerContext(_options);
        db.SavingChanges += (_, _) => { if (_beforeFailure) throw new IOException("Injected pre-commit failure"); };
        db.SavedChanges += (_, _) => { if (_afterFailure) throw new IOException("Injected ambiguous post-commit failure"); };
        return db;
    }
    private MonsterOwnershipService Service(Func<Guid, bool>? reserved = null) => new(Context, id => id == _species.Id, 6,
        species: id => id == _species.Id ? _species : null, moveExists: id => id == _move.Id || id == _second.Id,
        move: id => id == _move.Id ? _move : id == _second.Id ? _second : null, reserved: reserved ?? _reserved.Contains);
    [SetUp]
    public void Setup()
    {
        Options.EnsureCreated(); _reserved.Clear(); _beforeFailure = _afterFailure = false;
        _directory = Path.Combine(Path.GetTempPath(), "Intersect-M6E-" + Guid.NewGuid()); Directory.CreateDirectory(_directory);
        _options = new() { DatabaseType = DatabaseType.Sqlite, AutoDetectChanges = true,
            ConnectionStringBuilder = new SqliteConnectionStringBuilder { DataSource = Path.Combine(_directory, "players.db"), Pooling = false } };
        var type = Guid.NewGuid(); _move = new(Guid.NewGuid()) { TypeId = type, BasePower = 40, AlwaysHits = true, MaximumPP = 20 };
        _second = new(Guid.NewGuid()) { TypeId = type, BasePower = 30, AlwaysHits = true, MaximumPP = 7 };
        _species = new(Guid.NewGuid()) { BaseStats = new() { HP = 80, Attack = 50, Defense = 50, SpecialAttack = 50, SpecialDefense = 50, Speed = 50 }, Learnset = [new() { MoveId = _move.Id, Level = 1 }] };
        using var db = Context(); db.Database.EnsureCreated();
        var p = new Player { Name = "Condition Trainer" }; var other = new Player { Name = "Other Trainer" };
        Array.Fill(p.Equipment, -1); Array.Fill(other.Equipment, -1);
        db.Users.Add(new User { Name = "condition", Email = "condition@example.invalid", Players = [p, other] }); db.SaveChanges(); _owner = p.Id; _other = other.Id;
    }
    [TearDown]
    public void Cleanup()
    {
        if (Path.GetDirectoryName(Path.GetFullPath(_directory)) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))) throw new InvalidOperationException();
        Directory.Delete(_directory, recursive: true);
    }
    private MonsterInstance Load(Guid id) { using var db = Context(); return db.OwnedMonsters.AsNoTracking().Single(m => m.Id == id); }
    private Guid Grant() => Service().Grant(_owner, _species.Id, Guid.NewGuid()).Id;
    private void Spend(Guid id, int hp = 2, int pp = 3)
    {
        using var db = Context(); var m = db.OwnedMonsters.Single(m => m.Id == id);
        m.SetCondition(Math.Max(0, m.CurrentHP - hp), m.MaximumHPAtLastUpdate, m.MovePP.Select((p, i) => i == 0 ? p with { Current = Math.Max(0, p.Current - pp) } : p).ToArray(), Guid.NewGuid()); db.SaveChanges();
    }
    private sealed class LastRandom : IMonsterRandom { public long NextInt64(long bound) => bound - 1; }
    private MonsterBattleSession Session(MonsterOwnershipService service, Guid id, bool lethal = false, int pp = 20, long enemyHP = 1000)
    {
        var m = service.GetForBattle(_owner, id); var revision = m.Revision;
        var type = new MonsterTypeDescriptor(_move.TypeId); var stats = MonsterStatCalculator.Calculate(_species.BaseStats, m.Level, m.IVs, m.EVs, null, MonsterBattleRules.Generation4);
        var own = new MonsterBattleCombatant(new(m.Id, m.Level, stats, [_move.TypeId]), "Owned", "",
            m.MoveIds.Select((value, i) => value is { } move ? new MonsterBattleMove(move == _move.Id ? _move : _second, type, m.MovePP[i].Current) : null).ToArray(), currentHP: m.CurrentHP);
        var enemyMove = new MonsterMoveDescriptor(Guid.NewGuid()) { TypeId = type.Id, BasePower = lethal ? 10000 : 1, AlwaysHits = true, MaximumPP = pp };
        var enemy = new MonsterBattleCombatant(new(Guid.NewGuid(), 1, new(enemyHP, 5, 50, 5, 50, 1), [type.Id]), "Practice", "", [new(enemyMove, type), null, null, null]);
        return new(new(_owner, [own]), new(null, [enemy]), MonsterBattleRules.Generation4, new LastRandom(),
            persist: actor => revision = service.CommitBattle(_owner, id, revision, actor.HP, actor.MaximumHP,
                actor.Moves.Select(move => new MonsterMovePP(move?.Definition.Id, move?.PP ?? 0, move?.Definition.MaximumPP ?? 0)).ToArray()));
    }
    private static MonsterBattleActionPacket Action(MonsterBattleSession s, bool forfeit = false) => new() { BattleId = s.Id, Turn = s.Turn, Revision = s.Revision, MoveSlot = 0, Forfeit = forfeit };

    [Test] public void NewGrantStartsFullAndEmptySlotsHaveZeroPp()
    {
        var m = Load(Grant()); Assert.That(m.ConditionInitialized, Is.True); Assert.That(m.CurrentHP, Is.EqualTo(m.MaximumHPAtLastUpdate));
        Assert.That(m.MovePP[0], Is.EqualTo(new MonsterMovePP(_move.Id, 20, 20))); Assert.That(m.MovePP.Skip(1).All(p => p.MoveId == null && p.Current == 0 && p.Maximum == 0), Is.True);
    }
    [Test] public void LegacyConditionInitializesOnceWithoutChangingAttributesOrFaintedZero()
    {
        var id = Grant(); using (var db = Context()) db.Database.ExecuteSqlInterpolated($"UPDATE OwnedMonsters SET ConditionInitialized = {false}, CurrentHP = {0L}, MaximumHPAtLastUpdate = {0L}, MovePP = {"[]"} WHERE Id = {id}");
        var before = Load(id); Service().GetOwned(_owner); var initialized = Load(id);
        Assert.That(initialized.CurrentHP, Is.EqualTo(initialized.MaximumHPAtLastUpdate)); Assert.That(initialized.MoveIds, Is.EqualTo(before.MoveIds));
        Assert.That(initialized.IVs, Is.EqualTo(before.IVs)); Assert.That(initialized.Slot, Is.EqualTo(before.Slot));
        Spend(id, int.MaxValue, 20); var zero = Load(id); Service().GetOwned(_owner); var again = Load(id);
        Assert.That(again.CurrentHP, Is.Zero); Assert.That(again.MovePP[0].Current, Is.Zero); Assert.That(again.Revision, Is.EqualTo(zero.Revision));
    }
    [Test] public void MaximumHpIncreasePreservesDeficitAndDecreaseClampsWithoutRevival()
    {
        var id = Grant(); Spend(id, 4, 1); var old = Load(id); _species.BaseStats.HP = 1000; Service().GetOwned(_owner); var grown = Load(id);
        Assert.That(grown.MaximumHPAtLastUpdate - grown.CurrentHP, Is.EqualTo(old.MaximumHPAtLastUpdate - old.CurrentHP));
        _species.BaseStats.HP = 1; Service().GetOwned(_owner); var smaller = Load(id); Assert.That(smaller.CurrentHP, Is.LessThanOrEqualTo(smaller.MaximumHPAtLastUpdate));
        Spend(id, int.MaxValue, 0); _species.BaseStats.HP = 2000; Service().GetOwned(_owner); Assert.That(Load(id).CurrentHP, Is.Zero);
    }
    [Test] public void MovePpMaxReductionClampsAndIncreaseDoesNotRefill()
    {
        var id = Grant(); Spend(id, 1, 4); _move.MaximumPP = 10; Service().GetOwned(_owner); Assert.That(Load(id).MovePP[0].Current, Is.EqualTo(10));
        _move.MaximumPP = 30; Service().GetOwned(_owner); Assert.That(Load(id).MovePP[0].Current, Is.EqualTo(10));
    }
    [Test] public void ReplacementMoveStartsFullAndSameAssignmentPreservesRemainingPp()
    {
        var id = Grant(); Spend(id); var service = Service(); service.ReplaceMove(_owner, id, 0, _move.Id); Assert.That(Load(id).MovePP[0].Current, Is.EqualTo(17));
        service.ReplaceMove(_owner, id, 0, _second.Id); Assert.That(Load(id).MovePP[0], Is.EqualTo(new MonsterMovePP(_second.Id, 7, 7)));
        service.ReplaceMove(_owner, id, 0, null); Assert.That(Load(id).MovePP[0], Is.EqualTo(new MonsterMovePP(null, 0, 0)));
    }
    [Test] public void RetryReloadAndPartyStorageMovementNeverHeal()
    {
        var service = Service(); var request = Guid.NewGuid(); var id = service.Grant(_owner, _species.Id, request).Id; Spend(id); var spent = Load(id);
        Assert.That(service.Grant(_owner, _species.Id, request).Id, Is.EqualTo(id)); service.GetOwned(_owner);
        service.Move(_owner, id, Load(id).Revision, MonsterLocation.Storage); service.Move(_owner, id, Load(id).Revision, MonsterLocation.Party);
        var loaded = Service().GetForBattle(_owner, id); Assert.That(loaded.CurrentHP, Is.EqualTo(spent.CurrentHP)); Assert.That(loaded.MovePP, Is.EqualTo(spent.MovePP));
    }
    [Test] public void HealingOneResourcePreservesTheOtherAndAllAttributes()
    {
        var id = Grant(); Spend(id); var before = Load(id); var heal = new MonsterHealingService(Service());
        heal.HealOne(_owner, id, MonsterRecovery.HP); var hp = Load(id); Assert.That(hp.CurrentHP, Is.EqualTo(hp.MaximumHPAtLastUpdate)); Assert.That(hp.MovePP, Is.EqualTo(before.MovePP));
        Spend(id, 2, 0); var damaged = Load(id); heal.HealOne(_owner, id, MonsterRecovery.PP); var pp = Load(id);
        Assert.That(pp.CurrentHP, Is.EqualTo(damaged.CurrentHP)); Assert.That(pp.MovePP[0].Current, Is.EqualTo(20));
        Assert.That(pp.Id, Is.EqualTo(before.Id)); Assert.That(pp.IVs, Is.EqualTo(before.IVs)); Assert.That(pp.EVs, Is.EqualTo(before.EVs)); Assert.That(pp.NatureId, Is.EqualTo(before.NatureId));
        Assert.That(pp.Level, Is.EqualTo(before.Level)); Assert.That(pp.Experience, Is.EqualTo(before.Experience)); Assert.That(pp.MoveIds, Is.EqualTo(before.MoveIds)); Assert.That(pp.Slot, Is.EqualTo(before.Slot));
    }
    [Test] public void PartyHealExcludesStorageAndAllOwnedIncludesIt()
    {
        var a = Grant(); var b = Grant(); Service().Move(_owner, b, Load(b).Revision, MonsterLocation.Storage); Spend(a); Spend(b);
        var heal = new MonsterHealingService(Service()); Assert.That(heal.HealCollection(_owner, MonsterHealingScope.Party, MonsterRecovery.Both), Is.EqualTo(1));
        Assert.That(Load(a).CurrentHP, Is.EqualTo(Load(a).MaximumHPAtLastUpdate)); Assert.That(Load(b).MovePP[0].Current, Is.EqualTo(17));
        Assert.That(heal.HealCollection(_owner, MonsterHealingScope.AllOwned, MonsterRecovery.Both), Is.EqualTo(2)); Assert.That(Load(b).MovePP[0].Current, Is.EqualTo(20));
    }
    [Test] public void HealingAuthorizationAndReservationsAreCheckedBeforeAnyWrites()
    {
        var a = Grant(); var b = Grant(); Spend(a); Spend(b); _reserved.Add(b); var service = Service();
        Assert.Throws<InvalidOperationException>(() => service.Heal(_owner, null, MonsterHealingScope.AllOwned, MonsterRecovery.Both)); Assert.That(Load(a).MovePP[0].Current, Is.EqualTo(17));
        Assert.Throws<InvalidOperationException>(() => service.Heal(_other, a, MonsterHealingScope.AllOwned, MonsterRecovery.HP));
        Assert.Throws<InvalidOperationException>(() => service.Heal(_owner, a, (MonsterHealingScope)99, MonsterRecovery.Both));
    }
    [Test] public void TurnCommitsBeforePublicationAndDuplicatesCannotSpendTwice()
    {
        var id = Grant(); MonsterBattleRegistry? registry = null;
        registry = new((_, packet) => { var saved = Load(id); Assert.That(saved.CurrentHP, Is.EqualTo(packet.Player.HP)); Assert.That(saved.MovePP[0].Current, Is.EqualTo(packet.Moves[0].PP)); });
        var service = Service(registry.ReservesMonster); var session = Session(service, id); registry.Start(_owner, () => session);
        var request = Action(session); registry.Submit(_owner, request); var first = Load(id); Assert.That(first.MovePP[0].Current, Is.EqualTo(19)); Assert.That(first.CurrentHP, Is.LessThan(first.MaximumHPAtLastUpdate));
        Assert.Throws<InvalidOperationException>(() => registry.Submit(_owner, request)); Assert.That(Load(id).Revision, Is.EqualTo(first.Revision));
        Assert.Throws<InvalidOperationException>(() => service.Heal(_owner, id, MonsterHealingScope.AllOwned, MonsterRecovery.Both));
        registry.Cancel(_owner, "operator"); var next = Session(Service(), id).Snapshot(1); Assert.That(next.Player.HP, Is.EqualTo(first.CurrentHP)); Assert.That(next.Moves[0].PP, Is.EqualTo(19));
    }
    [Test] public void EveryCleanupPathKeepsCommittedDamageAndPp()
    {
        foreach (var cleanup in new[] { "forfeit", "disconnect", "cancel", "shutdown" })
        {
            var id = Grant(); var registry = new MonsterBattleRegistry((_, _) => { }); var session = Session(Service(registry.ReservesMonster), id);
            registry.Start(_owner, () => session); registry.Submit(_owner, Action(session)); var before = Load(id);
            if (cleanup == "forfeit") registry.Submit(_owner, Action(session, true)); else if (cleanup == "shutdown") registry.CancelAll(); else registry.Cancel(_owner, cleanup, cleanup == "disconnect");
            Assert.That(registry.HasActive(_owner), Is.False); Assert.That(Load(id).CurrentHP, Is.EqualTo(before.CurrentHP)); Assert.That(Load(id).MovePP, Is.EqualTo(before.MovePP));
        }
    }
    [Test] public void DefeatAndDrawCommitBeforeReservationRelease()
    {
        var id = Grant(); var registry = new MonsterBattleRegistry((_, _) => { }); var service = Service(registry.ReservesMonster); var session = Session(service, id, lethal: true);
        registry.Start(_owner, () => session); registry.Submit(_owner, Action(session)); Assert.That(Load(id).CurrentHP, Is.Zero); Assert.That(registry.HasActive(_owner), Is.False);
        Assert.Throws<InvalidOperationException>(() => Session(Service(), id));
        Service().Heal(_owner, id, MonsterHealingScope.AllOwned, MonsterRecovery.Both);
        session = Session(service, id, pp: 1); registry.Start(_owner, () => session); registry.Submit(_owner, Action(session));
        Assert.That(session.Outcome, Is.EqualTo(MonsterBattleOutcome.Draw)); Assert.That(Load(id).MovePP[0].Current, Is.EqualTo(19)); Assert.That(registry.HasActive(_owner), Is.False);
    }
    [Test] public void PreCommitFailureCancelsAndRestoresOnlyUncommittedInMemoryChanges()
    {
        var id = Grant(); var before = Load(id); var packets = new List<MonsterBattleStatePacket>(); var registry = new MonsterBattleRegistry((_, p) => packets.Add(p));
        var session = Session(Service(registry.ReservesMonster), id); registry.Start(_owner, () => session); _beforeFailure = true;
        Assert.Throws<IOException>(() => registry.Submit(_owner, Action(session))); _beforeFailure = false;
        Assert.That(registry.HasActive(_owner), Is.False); Assert.That(packets.Last().Outcome, Is.EqualTo(MonsterBattleOutcome.Cancelled));
        Assert.That(packets.Last().Player.HP, Is.EqualTo(before.CurrentHP)); Assert.That(Load(id).Revision, Is.EqualTo(before.Revision)); Assert.That(Load(id).MovePP[0].Current, Is.EqualTo(20));
    }
    [Test] public void AmbiguousPostCommitFailureIsVerifiedRatherThanRolledBackOrReapplied()
    {
        var id = Grant(); var registry = new MonsterBattleRegistry((_, _) => { }); var session = Session(Service(registry.ReservesMonster), id); registry.Start(_owner, () => session); _afterFailure = true;
        registry.Submit(_owner, Action(session)); _afterFailure = false; Assert.That(Load(id).MovePP[0].Current, Is.EqualTo(19)); Assert.That(session.Outcome, Is.EqualTo(MonsterBattleOutcome.Active)); registry.CancelAll();
    }
    [Test] public void OptimisticRevisionPreventsAStaleBattleOverwritingOtherChanges()
    {
        var id = Grant(); var registry = new MonsterBattleRegistry((_, _) => { }); var session = Session(Service(registry.ReservesMonster), id); registry.Start(_owner, () => session);
        Spend(id, 1, 0); var changed = Load(id); Assert.Throws<InvalidOperationException>(() => registry.Submit(_owner, Action(session)));
        Assert.That(Load(id).Revision, Is.EqualTo(changed.Revision)); Assert.That(registry.HasActive(_owner), Is.False);
    }
    [Test] public void CommitSurvivesPublicationFailure()
    {
        var id = Grant(); var fail = false; var registry = new MonsterBattleRegistry((_, _) => { if (fail) throw new IOException(); });
        var session = Session(Service(registry.ReservesMonster), id); registry.Start(_owner, () => session); fail = true; registry.Submit(_owner, Action(session));
        Assert.That(Load(id).MovePP[0].Current, Is.EqualTo(19)); Assert.That(registry.HasActive(_owner), Is.False);
    }
    [Test] public void ConditionBoundsRejectInvalidHpPpAndWrongMoveAssociation()
    {
        var id = Grant(); var m = Load(id); Assert.Throws<InvalidOperationException>(() => m.SetCondition(-1, m.MaximumHPAtLastUpdate, m.MovePP.ToArray(), Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => m.SetCondition(m.MaximumHPAtLastUpdate + 1, m.MaximumHPAtLastUpdate, m.MovePP.ToArray(), Guid.NewGuid()));
        var pp = m.MovePP.ToArray(); pp[0] = pp[0] with { Current = 21 }; Assert.Throws<InvalidOperationException>(() => m.SetCondition(m.CurrentHP, m.MaximumHPAtLastUpdate, pp, Guid.NewGuid()));
        pp[0] = new(_second.Id, 3, 7); Assert.Throws<InvalidOperationException>(() => m.SetCondition(m.CurrentHP, m.MaximumHPAtLastUpdate, pp, Guid.NewGuid()));
    }
    [Test] public void EventCommandHealsOnlyTheTriggeringCharacterAndRoundTrips()
    {
        var id = Grant(); Spend(id); var command = new HealPlayerMonstersCommand { Scope = MonsterHealingScope.AllOwned, Recovery = MonsterRecovery.Both };
        var json = command.GetCopyData([], []); var loaded = Newtonsoft.Json.JsonConvert.DeserializeObject<EventCommand>(json, new Newtonsoft.Json.JsonSerializerSettings { TypeNameHandling = Newtonsoft.Json.TypeNameHandling.Auto, SerializationBinder = new Intersect.Framework.Core.Serialization.IntersectTypeSerializationBinder() });
        Assert.That(loaded, Is.TypeOf<HealPlayerMonstersCommand>()); Assert.That(typeof(HealPlayerMonstersCommand).GetProperties().Any(p => p.Name.Contains("PlayerId") || p.Name.Contains("OwnerId")), Is.False);
        MonsterHealingEvents.Execute(command, new Player { Id = _other }, Service()); Assert.That(Load(id).MovePP[0].Current, Is.EqualTo(17));
        MonsterHealingEvents.Execute(command, new Player { Id = _owner }, Service()); Assert.That(Load(id).MovePP[0].Current, Is.EqualTo(20));
    }
    [Test] public void InspectionAndRosterUseStoredConditionWithoutHealing()
    {
        var id = Grant(); Spend(id); var m = Load(id); var before = m.Revision;
        var text = MonsterDiagnostics.Describe(m, _species, null, MonsterBattleRules.Generation4); Assert.That(text, Does.Contain("Current HP:")); Assert.That(text, Does.Contain("17/20"));
        var details = MonsterRosterDetails.Create(m, _species, null, MonsterBattleRules.Generation4); Assert.That(details.CurrentHP, Is.EqualTo(m.CurrentHP)); Assert.That(details.Moves[0].CurrentPP, Is.EqualTo(17));
        Assert.That(Load(id).Revision, Is.EqualTo(before));
    }
    [Test] public void VictoryKeepsStartingDamageAndCommitsUsedPp()
    {
        var id = Grant(); Spend(id); var before = Load(id); var registry = new MonsterBattleRegistry((_, _) => { });
        var session = Session(Service(registry.ReservesMonster), id, enemyHP: 1); registry.Start(_owner, () => session); registry.Submit(_owner, Action(session));
        Assert.That(session.Outcome, Is.EqualTo(MonsterBattleOutcome.Victory)); Assert.That(Load(id).CurrentHP, Is.EqualTo(before.CurrentHP));
        Assert.That(Load(id).MovePP[0].Current, Is.EqualTo(16)); Assert.That(registry.HasActive(_owner), Is.False);
    }
    [Test] public void DefinitionChangesAreDeferredUntilBattleReservationEnds()
    {
        var id = Grant(); Spend(id); var before = Load(id); _reserved.Add(id); _species.BaseStats.HP = 1000; _move.MaximumPP = 5;
        Service().GetOwned(_owner); Assert.That(Load(id).Revision, Is.EqualTo(before.Revision));
        _reserved.Remove(id); Service().GetOwned(_owner); var after = Load(id);
        Assert.That(after.MaximumHPAtLastUpdate - after.CurrentHP, Is.EqualTo(before.MaximumHPAtLastUpdate - before.CurrentHP)); Assert.That(after.MovePP[0].Current, Is.EqualTo(5));
    }
    [Test] public void AdditiveConditionMigrationInitializesLegacyExactlyOnceAndPreservesIdentity()
    {
        var options = _options with { ConnectionStringBuilder = new SqliteConnectionStringBuilder { DataSource = Path.Combine(_directory, "legacy.db"), Pooling = false } };
        var id = Guid.NewGuid(); var revision = Guid.NewGuid(); Guid owner;
        using (var db = new SqlitePlayerContext(options))
        {
            db.GetService<IMigrator>().Migrate("20261109000000_AddMonsterMoveSlots");
            var player = new Player { Name = "Legacy" }; Array.Fill(player.Equipment, -1);
            db.Users.Add(new User { Name = "legacy", Email = "legacy@example.invalid", Players = [player] }); db.SaveChanges(); owner = player.Id;
            var request = Guid.NewGuid();
            db.Database.ExecuteSqlInterpolated($"INSERT INTO OwnedMonsters (Id, OwnerPlayerId, SpeciesId, GrantRequestId, Revision, Location, Slot, Level, Experience, CreatedAtUtcMilliseconds, Move1Id, MovesInitialized) VALUES ({id}, {owner}, {_species.Id}, {request}, {revision}, {0}, {0}, {1}, {0L}, {1L}, {_move.Id}, {true})");
            db.Database.Migrate(); Assert.That(db.Database.HasPendingModelChanges(), Is.False);
            var legacy = db.OwnedMonsters.Single(); Assert.That(legacy.ConditionInitialized, Is.False); Assert.That(legacy.CurrentHP, Is.Zero); Assert.That(legacy.MovePP, Is.Empty);
        }
        var service = new MonsterOwnershipService(() => new SqlitePlayerContext(options), _ => true, 6, species: _ => _species, moveExists: id => id == _move.Id, move: _ => _move, reserved: _ => false);
        service.GetOwned(owner);
        using (var db = new SqlitePlayerContext(options))
        {
            var m = db.OwnedMonsters.Single(); Assert.That(m.Id, Is.EqualTo(id)); Assert.That(m.OwnerPlayerId, Is.EqualTo(owner)); Assert.That(m.IVs, Is.EqualTo(new MonsterStatValues()));
            Assert.That(m.CurrentHP, Is.EqualTo(m.MaximumHPAtLastUpdate)); Assert.That(m.MoveIds[0], Is.EqualTo(_move.Id));
            m.SetCondition(0, m.MaximumHPAtLastUpdate, m.MovePP.Select(p => p with { Current = 0 }).ToArray(), Guid.NewGuid()); db.SaveChanges();
        }
        service.GetOwned(owner);
        using var reopened = new SqlitePlayerContext(options); Assert.That(reopened.OwnedMonsters.Single().CurrentHP, Is.Zero); Assert.That(reopened.OwnedMonsters.Single().MovePP[0].Current, Is.Zero);
        foreach (var migration in new Migration[] { new Intersect.Server.Migrations.Sqlite.Player.AddMonsterCondition(), new Intersect.Server.Migrations.MySql.Player.AddMonsterCondition() })
        {
            Assert.That(migration.UpOperations.Count, Is.EqualTo(4)); Assert.That(migration.UpOperations.All(o => o is Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation), Is.True);
        }
    }
    private sealed class FailSecondUpdate : DbCommandInterceptor
    {
        private int _updates;
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result)
        {
            if (command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains("OwnedMonsters") && ++_updates == 2)
                throw new IOException("Injected second-row failure");
            return result;
        }
    }
    private sealed class AtomicFailureContext(DatabaseContextOptions options, DbCommandInterceptor failure) : PlayerContext(options)
    {
        public override DatabaseType DatabaseType => DatabaseType.Sqlite;
        protected override void OnConfiguring(DbContextOptionsBuilder builder)
        {
            base.OnConfiguring(builder);
            builder.AddInterceptors(failure);
        }
    }
    [Test] public void PartyHealingRollsBackTheFirstRowIfTheSecondWriteFails()
    {
        var a = Grant(); var b = Grant(); Spend(a); Spend(b); var beforeA = Load(a); var beforeB = Load(b);
        var failure = new FailSecondUpdate();
        var service = new MonsterOwnershipService(() => new AtomicFailureContext(_options, failure), _ => true, 6,
            species: _ => _species, moveExists: id => id == _move.Id, move: _ => _move, reserved: _ => false);
        Assert.Catch(() => service.Heal(_owner, null, MonsterHealingScope.AllOwned, MonsterRecovery.Both));
        Assert.That(Load(a).Revision, Is.EqualTo(beforeA.Revision)); Assert.That(Load(b).Revision, Is.EqualTo(beforeB.Revision));
        Assert.That(Load(a).MovePP[0].Current, Is.EqualTo(17));
    }


    [Test]
    public void BattlePartyLoadExcludesStorageAndOtherOwnersAndReservesBench()
    {
        var a = Grant();
        var b = Grant();
        var stored = Grant();
        var other = Service().Grant(_other, _species.Id, Guid.NewGuid()).Id;
        Service().Move(_owner, stored, Load(stored).Revision, MonsterLocation.Storage);
        Spend(b);
        var registry = new MonsterBattleRegistry((_, _) => { });
        var service = Service(registry.ReservesMonster);
        var party = service.GetPartyForBattle(_owner);
        Assert.That(party.Select(member => member.Id), Is.EqualTo(new[] { a, b }));
        Assert.That(party[1].MovePP[0].Current, Is.EqualTo(17));
        Assert.That(party[1].CurrentHP, Is.EqualTo(Load(b).CurrentHP));

        var type = new MonsterTypeDescriptor(_move.TypeId);
        MonsterBattleCombatant Owned(MonsterInstance row) =>
            new(new(row.Id, row.Level, new(row.MaximumHPAtLastUpdate, 50, 50, 50, 50, 50), [type.Id]),
                row.Id.ToString(), "", [new(_move, type, row.MovePP[0].Current), null, null, null],
                currentHP: row.CurrentHP, partySlot: row.Slot);
        var opponent = new MonsterBattleCombatant(new(Guid.NewGuid(), 1, new(500, 5, 50, 5, 50, 5), [type.Id]),
            "Practice", "", [new(_move, type), null, null, null]);
        var revisions = party.ToDictionary(row => row.Id, row => row.Revision);
        var session = new MonsterBattleSession(new(_owner, party.Select(Owned).ToArray()), new(null, [opponent]),
            MonsterBattleRules.Generation4, new SystemMonsterRandom(), persistParty: actors =>
            {
                var token = service.CommitBattle(_owner, actors.Select(actor => new MonsterOwnershipService.BattleCondition(
                    actor.Id, revisions[actor.Id], actor.HP, actor.MaximumHP,
                    actor.Moves.Select(move => new MonsterMovePP(move?.Definition.Id, move?.PP ?? 0, move?.Definition.MaximumPP ?? 0)).ToArray())).ToArray());
                foreach (var actor in actors) revisions[actor.Id] = token;
            });
        registry.Start(_owner, () => session);
        var turn = Action(session);
        turn.Kind = MonsterBattleActionKind.Switch;
        foreach (var invalid in new[] { stored, other })
        {
            turn.MonsterId = invalid;
            Assert.Throws<InvalidOperationException>(() => registry.Submit(_owner, turn));
        }
        Assert.Throws<InvalidOperationException>(() => service.Move(_owner, b, Load(b).Revision, MonsterLocation.Storage));
        Assert.Throws<InvalidOperationException>(() => service.Heal(_owner, b, MonsterHealingScope.Party, MonsterRecovery.Both));
        Assert.Throws<InvalidOperationException>(() => service.ReplaceMove(_owner, b, 0, _second.Id));

        var originalA = Load(a);
        var originalB = Load(b);
        turn.MonsterId = b;
        registry.Submit(_owner, turn);
        var switched = Load(b);
        Assert.That(switched.CurrentHP, Is.LessThan(originalB.CurrentHP));
        Assert.That(switched.MovePP, Is.EqualTo(originalB.MovePP));
        Assert.That(Load(a).Revision, Is.EqualTo(originalA.Revision));
        var switchBack = Action(session);
        switchBack.Kind = MonsterBattleActionKind.Switch;
        switchBack.MonsterId = a;
        registry.Submit(_owner, switchBack);
        Assert.That(Load(b).Revision, Is.EqualTo(switched.Revision));
        Assert.That(Load(a).CurrentHP, Is.LessThan(originalA.CurrentHP));
        Assert.That(Load(a).MovePP, Is.EqualTo(originalA.MovePP));
        registry.Cancel(_owner, "Done.");
        Assert.DoesNotThrow(() => service.Move(_owner, b, Load(b).Revision, MonsterLocation.Storage));
    }

    [Test]
    public void BattleBatchRollsBackOnSecondWriteAndVerifiesEveryRowAfterAmbiguousCommit()
    {
        var a = Load(Grant());
        var b = Load(Grant());
        MonsterOwnershipService.BattleCondition Change(MonsterInstance row) => new(row.Id, row.Revision,
            row.CurrentHP - 1, row.MaximumHPAtLastUpdate, row.MovePP.ToArray());
        var changes = new[] { Change(a), Change(b) };
        var failure = new FailSecondUpdate();
        var failingService = new MonsterOwnershipService(() => new AtomicFailureContext(_options, failure), _ => true, 6,
            species: _ => _species, moveExists: id => id == _move.Id, move: _ => _move, reserved: _ => false);
        Assert.Catch(() => failingService.CommitBattle(_owner, changes));
        Assert.That(Load(a.Id).Revision, Is.EqualTo(a.Revision));
        Assert.That(Load(b.Id).Revision, Is.EqualTo(b.Revision));
        _afterFailure = true;
        var token = Service().CommitBattle(_owner, changes);
        Assert.That(Load(a.Id).Revision, Is.EqualTo(token));
        Assert.That(Load(b.Id).Revision, Is.EqualTo(token));
        Assert.That(Load(a.Id).CurrentHP, Is.EqualTo(a.CurrentHP - 1));
        Assert.That(Load(b.Id).CurrentHP, Is.EqualTo(b.CurrentHP - 1));
    }
}
