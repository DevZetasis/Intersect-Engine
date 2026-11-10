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
        var species = _species(monster.SpeciesId);
        if (species == null) return false; // Missing legacy content must not fabricate a heal or maximum.
        var nature = monster.NatureId is { } id ? _natures().SingleOrDefault(n => n.Id == id) : null;
        if (monster.NatureId != null && nature == null) return false;
        var maximum = MonsterStatCalculator.Calculate(species.BaseStats, monster.Level, monster.IVs, monster.EVs, nature, _rules).HP;
        return monster.ReconcileCondition(maximum, monster.MoveIds.Select(id => id is { } value ? _move(value) : null).ToArray());
    }
    public int Heal(Guid owner, Guid? monsterId, MonsterHealingScope scope, MonsterRecovery recovery)
    {
        if (owner == Guid.Empty || !Enum.IsDefined(scope) || recovery is not (MonsterRecovery.HP or MonsterRecovery.PP or MonsterRecovery.Both)) throw new InvalidOperationException("Invalid healing request.");
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
    internal Guid CommitBattle(Guid owner, Guid monsterId, Guid expectedRevision, long hp, long maximum, MonsterMovePP[] pp)
    {
        lock (MonsterTypeRules.ContentGate)
        lock (Gate(owner))
        {
            var token = Guid.NewGuid();
            try
            {
                using var db = _createContext();
                var monster = db.OwnedMonsters.SingleOrDefault(m => m.Id == monsterId && m.OwnerPlayerId == owner && m.Location == MonsterLocation.Party)
                    ?? throw new InvalidOperationException("Owned battle monster is unavailable.");
                if (monster.Revision != expectedRevision) throw new InvalidOperationException("Monster changed while the battle was active.");
                monster.SetCondition(hp, maximum, pp, token);
                if (db.SaveChanges() <= 0) throw new InvalidOperationException("Turn condition was not committed.");
                return token;
            }
            catch
            {
                // Resolve an ambiguous connection failure after commit using its unique revision token.
                using var verify = _createContext();
                var saved = verify.OwnedMonsters.AsNoTracking().SingleOrDefault(m => m.Id == monsterId && m.OwnerPlayerId == owner);
                if (saved?.Revision == token && saved.CurrentHP == hp && saved.MaximumHPAtLastUpdate == maximum && saved.MovePP.SequenceEqual(pp)) return token;
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
