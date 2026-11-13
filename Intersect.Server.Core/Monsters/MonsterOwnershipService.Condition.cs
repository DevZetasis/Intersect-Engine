using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Server.Database.PlayerData.Players;
using Intersect.Server.Monsters.Calculations;
using Microsoft.EntityFrameworkCore;

namespace Intersect.Server.Monsters;

public sealed partial class MonsterOwnershipService
{
    private readonly Func<Guid, MonsterMoveDescriptor?> _move;
    private readonly Func<Guid, bool> _reserved;
    private void EnsureUnreserved(Guid monster) { if (_reserved(monster)) throw new InvalidOperationException("Finish the monster battle before changing or healing this monster."); }
    private bool ReconcileCondition(MonsterInstance monster)
    {
        var cleared = monster.StatusId is { } id &&
            MonsterStatusDescriptor.Get(id) is not { IsValid: true, PersistOutsideBattle: true };
        if (cleared) monster.SetStatus(null, null, Guid.NewGuid());
        var species = _species(monster.SpeciesId);
        if (species == null) return cleared; // Missing legacy content must not fabricate a heal or maximum.
        var nature = monster.NatureId is { } natureId ? _natures().SingleOrDefault(n => n.Id == natureId) : null;
        if (monster.NatureId != null && nature == null) return cleared;
        var maximum = MonsterStatCalculator.Calculate(species.BaseStats, monster.Level, monster.IVs, monster.EVs, nature, _rules).HP;
        return monster.ReconcileCondition(maximum, monster.MoveIds.Select(id => id is { } value ? _move(value) : null).ToArray()) || cleared;
    }
    public int Heal(Guid owner, Guid? monsterId, MonsterHealingScope scope, MonsterRecovery recovery)
    {
        if (owner == Guid.Empty || !Enum.IsDefined(scope) || recovery == 0 || (recovery & ~MonsterRecovery.All) != 0) throw new InvalidOperationException("Invalid healing request.");
        lock (MonsterTypeRules.ContentGate)
        lock (Gate(owner))
        {
            using var db = _createContext();
            var query = db.OwnedMonsters.Where(m => m.OwnerPlayerId == owner);
            if (monsterId is { } id) query = query.Where(m => m.Id == id);
            else if (scope == MonsterHealingScope.Party) query = query.Where(m => m.Location == MonsterLocation.Party);
            var monsters = query.ToArray();
            if (monsterId != null && monsters.Length != 1) throw new InvalidOperationException("Monster is not owned by this character.");
            foreach (var m in monsters) EnsureUnreserved(m.Id);
            foreach (var m in monsters)
            {
                if (recovery.HasFlag(MonsterRecovery.Status))
                    m.SetStatus(null, null, Guid.NewGuid());
                if ((recovery & MonsterRecovery.Both) == 0) continue;
                if (!m.MovesInitialized) m.InitializeMoves(MonsterMoveAssignment.Initial(_species(m.SpeciesId), m.Level, _moveExists));
                ReconcileCondition(m);
                if (!m.ConditionInitialized || m.MovePP.Any(p => p.MoveId != null && p.Maximum == 0)) throw new InvalidOperationException("Referenced species, nature or move definition is unavailable.");
                var hp = recovery.HasFlag(MonsterRecovery.HP) ? m.MaximumHPAtLastUpdate : m.CurrentHP;
                var pp = m.MovePP.Select(p => recovery.HasFlag(MonsterRecovery.PP) ? p with { Current = p.Maximum } : p).ToArray();
                m.SetCondition(hp, m.MaximumHPAtLastUpdate, pp, Guid.NewGuid());
            }
            if (monsters.Length > 0 && db.SaveChanges() <= 0) throw new InvalidOperationException("Healing could not be committed.");
            return monsters.Length;
        }
    }
    internal sealed record BattleCondition(Guid Id, Guid ExpectedRevision, long HP, long MaximumHP, MonsterMovePP[] PP, Guid? StatusId = null, int? StatusTurns = null);

    internal Guid CommitBattle(Guid owner, Guid monsterId, Guid expectedRevision, long hp, long maximum, MonsterMovePP[] pp) =>
        CommitBattle(owner, [new BattleCondition(monsterId, expectedRevision, hp, maximum, pp)]);

    internal Guid CommitBattle(Guid owner, IReadOnlyList<BattleCondition> changes)
    {
        if (changes.Count == 0) throw new ArgumentException("A battle commit requires condition changes.", nameof(changes));
        var ids = changes.Select(change => change.Id).ToArray();
        if (ids.Distinct().Count() != ids.Length) throw new ArgumentException("Duplicate battle monster.", nameof(changes));

        lock (MonsterTypeRules.ContentGate)
        lock (Gate(owner))
        {
            var token = Guid.NewGuid();
            try
            {
                using var db = _createContext();
                var monsters = db.OwnedMonsters.Where(m => ids.Contains(m.Id) && m.OwnerPlayerId == owner &&
                    m.Location == MonsterLocation.Party).ToDictionary(m => m.Id);
                foreach (var change in changes)
                {
                    if (!monsters.TryGetValue(change.Id, out var monster) || monster.Revision != change.ExpectedRevision)
                        throw new InvalidOperationException("Owned battle monster changed or is no longer in the party.");
                    monster.SetCondition(change.HP, change.MaximumHP, change.PP, token);
                    monster.SetStatus(change.StatusId, change.StatusTurns, token);
                }
                // EF commits all tracked updates in one transaction, including concurrency checks.
                if (db.SaveChanges() <= 0) throw new InvalidOperationException("Turn condition was not committed.");
                return token;
            }
            catch
            {
                // Only accept an ambiguous commit if every participating write matches the new token and values.
                using var verify = _createContext();
                var saved = verify.OwnedMonsters.AsNoTracking().Where(m => ids.Contains(m.Id) && m.OwnerPlayerId == owner)
                    .ToDictionary(m => m.Id);
                if (changes.All(change => saved.TryGetValue(change.Id, out var monster) && monster.Revision == token &&
                    monster.CurrentHP == change.HP && monster.MaximumHPAtLastUpdate == change.MaximumHP &&
                    monster.StatusId == change.StatusId && monster.StatusTurns == change.StatusTurns &&
                    monster.MovePP.SequenceEqual(change.PP))) return token;
                throw;
            }
        }
    }
    internal void ReplaceMove(Guid owner, Guid monsterId, int slot, Guid? moveId)
    {
        lock (MonsterTypeRules.ContentGate)
        lock (Gate(owner))
        {
            using var db = _createContext(); var m = db.OwnedMonsters.Single(x => x.Id == monsterId && x.OwnerPlayerId == owner);
            EnsureUnreserved(m.Id); if (moveId is { } id && !_moveExists(id)) throw new InvalidOperationException("Move unavailable.");
            m.AssignMove(slot, moveId); ReconcileCondition(m); if (db.SaveChanges() <= 0) throw new InvalidOperationException("Move assignment was not committed.");
        }
    }
}

/// <summary>Shared healing entry points; no client owner IDs or healing packet.</summary>
public sealed class MonsterHealingService(MonsterOwnershipService ownership)
{
    public int HealOne(Guid owner, Guid monster, MonsterRecovery recovery) => ownership.Heal(owner, monster, MonsterHealingScope.AllOwned, recovery);
    public int HealCollection(Guid owner, MonsterHealingScope scope, MonsterRecovery recovery) => ownership.Heal(owner, null, scope, recovery);
}
