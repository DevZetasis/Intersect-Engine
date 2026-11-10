using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Server.Database.PlayerData;
using Intersect.Server.Database.PlayerData.Players;
using Microsoft.EntityFrameworkCore;
using Intersect.Config;
using Intersect.Server.Monsters.Calculations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Intersect.Server.Monsters;

public sealed partial class MonsterOwnershipService
{
    // Serialize each character's allocations without an ever-growing lock dictionary.
    private static readonly object[] Gates = Enumerable.Range(0, 64).Select(_ => new object()).ToArray();
    private readonly Func<PlayerContext> _createContext;
    private readonly Func<Guid, bool> _speciesExists;
    public int PartyCapacity { get; }
    private readonly IMonsterRandom _random;
    private readonly Func<IEnumerable<MonsterNatureDescriptor>> _natures;
    private readonly MonsterBattleRules _rules;
    private readonly ILogger _logger;
    private readonly Func<Guid, MonsterSpeciesDescriptor?> _species;
    private readonly Func<Guid, bool> _moveExists;

    public MonsterOwnershipService(Func<PlayerContext> createContext, Func<Guid, bool> speciesExists, int partyCapacity,
        IMonsterRandom? random = null, Func<IEnumerable<MonsterNatureDescriptor>>? natures = null,
        MonsterBattleRules? rules = null, ILogger? logger = null,
        Func<Guid, MonsterSpeciesDescriptor?>? species = null, Func<Guid, bool>? moveExists = null, Func<Guid, MonsterMoveDescriptor?>? move = null, Func<Guid, bool>? reserved = null)
    {
        _createContext = createContext;
        _speciesExists = speciesExists;
        PartyCapacity = Math.Clamp(partyCapacity, 1, 64);
        _species = species ?? MonsterSpeciesDescriptor.Get;
        _move = move ?? MonsterMoveDescriptor.Get;
        _reserved = reserved ?? Intersect.Server.Monsters.Battles.MonsterPracticeBattles.Registry.ReservesMonster;
        _moveExists = moveExists ?? (id => MonsterMoveDescriptor.Get(id) != null);
        _random = random ?? new SystemMonsterRandom();
        _natures = natures ?? (() => Array.Empty<MonsterNatureDescriptor>());
        _rules = rules ?? MonsterBattleRules.Generation4;
        _rules.Validate();
        _createContext = () =>
        {
            var context = createContext();
            context.MonsterRules = _rules;
            context.MonsterMoveExists = _moveExists;
            return context;
        };
        _logger = logger ?? NullLogger.Instance;
    }

    private static object Gate(Guid ownerId) => Gates[(int)((uint)ownerId.GetHashCode() % (uint)Gates.Length)];

    public OwnedMonsterSummary[] GetOwned(Guid ownerId, Func<MonsterInstance, OwnedMonsterDetails>? describe = null)
    {
        lock (MonsterTypeRules.ContentGate)
        lock (Gate(ownerId))
        {
            using var db = _createContext();
            var owned = db.OwnedMonsters.Where(monster => monster.OwnerPlayerId == ownerId).ToArray();
            var needsMoves = owned.Where(monster => !monster.MovesInitialized).ToArray();
            foreach (var monster in needsMoves)
                monster.InitializeMoves(MonsterMoveAssignment.Initial(_species(monster.SpeciesId), monster.Level, _moveExists));
            var conditionsChanged = owned.Where(monster => !_reserved(monster.Id)).Count(ReconcileCondition);
            var overflow = owned.Where(monster => monster.Location == MonsterLocation.Party && monster.Slot >= PartyCapacity && !_reserved(monster.Id)).ToArray();
            foreach (var monster in overflow)
                monster.Place(MonsterLocation.Storage, FindSlot(owned, MonsterLocation.Storage, int.MaxValue));
            if ((overflow.Length > 0 || needsMoves.Length > 0 || conditionsChanged > 0) && db.SaveChanges() <= 0)
                throw new InvalidOperationException("Monster placement was not committed.");
            return owned.OrderBy(monster => monster.Location).ThenBy(monster => monster.Slot)
                .Select(monster =>
                {
                    var summary = monster.ToSummary();
                    summary.Details = describe?.Invoke(monster);
                    return summary;
                }).ToArray();
        }
    }

    public MonsterInstance GetForBattle(Guid ownerId, Guid monsterId)
    {
        lock (MonsterTypeRules.ContentGate)
        lock (Gate(ownerId))
        {
            GetOwned(ownerId); // One-time move initialization and existing capacity normalization.
            using var db = _createContext();
            return db.OwnedMonsters.AsNoTracking().SingleOrDefault(monster => monster.OwnerPlayerId == ownerId && monster.Id == monsterId)
                ?? throw new InvalidOperationException("Monster is not in your collection.");
        }
    }

    /// <summary>Server administrative operation; intentionally has no client grant packet.</summary>
    public OwnedMonsterSummary Grant(Guid ownerId, Guid speciesId, Guid requestId)
        => Grant(ownerId, speciesId, requestId, out _);

    public OwnedMonsterSummary Grant(Guid ownerId, Guid speciesId, Guid requestId, out bool created)
    {
        created = false;
        if (ownerId == Guid.Empty || speciesId == Guid.Empty || requestId == Guid.Empty)
            throw new InvalidOperationException("Character, species, and request UUIDs are required.");
        lock (MonsterTypeRules.ContentGate)
        lock (Gate(ownerId))
        {
            using var db = _createContext();
            var existing = db.OwnedMonsters.AsNoTracking().SingleOrDefault(monster => monster.GrantRequestId == requestId);
            if (existing != null) return ValidateRetry(existing, ownerId, speciesId);
            if (!_speciesExists(speciesId)) throw new InvalidOperationException("Monster species does not exist.");
            if (!db.Players.Any(player => player.Id == ownerId)) throw new InvalidOperationException("Character does not exist.");
            var owned = db.OwnedMonsters.AsNoTracking().Where(monster => monster.OwnerPlayerId == ownerId).ToArray();
            var partySlot = FindSlot(owned, MonsterLocation.Party, PartyCapacity);
            var location = partySlot >= 0 ? MonsterLocation.Party : MonsterLocation.Storage;
            var slot = partySlot >= 0 ? partySlot : FindSlot(owned, MonsterLocation.Storage, int.MaxValue);
            var ivs = MonsterAttributeGeneration.GenerateIVs(_random);
            var natureId = MonsterAttributeGeneration.SelectNature(_natures(), _random);
            var monster = new MonsterInstance(ownerId, speciesId, requestId, location, slot, ivs, natureId);
            monster.InitializeMoves(MonsterMoveAssignment.Initial(_species(speciesId), monster.Level, _moveExists));
            ReconcileCondition(monster);
            db.OwnedMonsters.Add(monster);
            try
            {
                if (db.SaveChanges() <= 0) throw new InvalidOperationException("Monster grant was not committed.");
            }
            catch (DbUpdateException)
            {
                // A simultaneous retry on another connection may have committed first.
                using var retry = _createContext();
                existing = retry.OwnedMonsters.AsNoTracking().SingleOrDefault(item => item.GrantRequestId == requestId);
                if (existing == null) throw;
                return ValidateRetry(existing, ownerId, speciesId);
            }
            if (natureId == null)
                _logger.LogWarning("No positive-weight monster natures are available. Monster {MonsterId} was created neutral/unassigned; its IVs are preserved.", monster.Id);
            created = true;
            return monster.ToSummary();
        }
    }

    private static OwnedMonsterSummary ValidateRetry(MonsterInstance existing, Guid ownerId, Guid speciesId)
    {
        if (existing.OwnerPlayerId != ownerId || existing.SpeciesId != speciesId)
            throw new InvalidOperationException("That request UUID was already used for a different grant.");
        return existing.ToSummary();
    }

    public void Move(Guid ownerId, Guid monsterId, Guid expectedRevision, MonsterLocation destination)
    {
        if (destination is not (MonsterLocation.Party or MonsterLocation.Storage))
            throw new InvalidOperationException("Invalid monster location.");
        lock (MonsterTypeRules.ContentGate)
        lock (Gate(ownerId))
        {
            using var db = _createContext();
            // Never fetch by monster UUID alone on a character-originated operation.
            var monster = db.OwnedMonsters.SingleOrDefault(item => item.OwnerPlayerId == ownerId && item.Id == monsterId)
                ?? throw new InvalidOperationException("Monster is not in your collection.");
            EnsureUnreserved(monster.Id);
            if (monster.Revision != expectedRevision) throw new InvalidOperationException("The collection changed. Please try again.");
            if (monster.Location == destination) return;
            var owned = db.OwnedMonsters.AsNoTracking().Where(item => item.OwnerPlayerId == ownerId).ToArray();
            var slot = FindSlot(owned, destination, destination == MonsterLocation.Party ? PartyCapacity : int.MaxValue);
            if (slot < 0) throw new InvalidOperationException("Your monster party is full.");
            monster.Place(destination, slot);
            if (db.SaveChanges() <= 0) throw new InvalidOperationException("Monster placement was not committed.");
        }
    }

    /// <summary>Internal progression seam, not a packet, reward or public editing command.</summary>
    internal void SetEVs(Guid ownerId, Guid monsterId, Guid expectedRevision, MonsterStatValues evs)
    {
        MonsterStatCalculator.ValidateEVs(evs, _rules);
        lock (MonsterTypeRules.ContentGate)
        lock (Gate(ownerId))
        {
            using var db = _createContext();
            var monster = db.OwnedMonsters.SingleOrDefault(item => item.OwnerPlayerId == ownerId && item.Id == monsterId)
                ?? throw new InvalidOperationException("Monster is not in your collection.");
            EnsureUnreserved(monster.Id);
            if (monster.Revision != expectedRevision) throw new InvalidOperationException("The collection changed. Please try again.");
            if (monster.EVs == evs) return;
            monster.SetEVs(evs, _rules);
            ReconcileCondition(monster);
            if (db.SaveChanges() <= 0) throw new InvalidOperationException("Monster EV changes were not committed.");
        }
    }

    private static int FindSlot(IEnumerable<MonsterInstance> monsters, MonsterLocation location, int capacity)
    {
        var used = monsters.Where(monster => monster.Location == location).Select(monster => monster.Slot).ToHashSet();
        for (var slot = 0; slot < capacity; slot++) if (!used.Contains(slot)) return slot;
        return -1;
    }
}
