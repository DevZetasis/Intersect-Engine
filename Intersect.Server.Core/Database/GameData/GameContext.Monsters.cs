using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Framework.Core.GameObjects.Animations;
using Intersect.Server.Monsters;
using Intersect.Server.Maps;
using Intersect.Framework.Core.GameObjects.Maps;
using Intersect.Config;
using Microsoft.EntityFrameworkCore;

namespace Intersect.Server.Database.GameData;

public abstract partial class GameContext
{
    public Func<Guid, bool>? GenderReferenceExists { get; set; }
    public Func<Guid, bool>? StatusReferenceExists { get; set; }
    public Func<Guid, bool>? MoveReferenceExists { get; set; }
    public Func<Guid, bool>? NatureReferenceExists { get; set; }
    private void ValidateMonsterContent()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<MonsterNatureDescriptor>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            MonsterNatureRules.Validate(entry.Entity);
        foreach (var entry in ChangeTracker.Entries<MonsterNatureDescriptor>().Where(entry => entry.State == EntityState.Deleted))
            if (NatureReferenceExists?.Invoke(entry.Entity.Id) == true)
                throw new InvalidOperationException("This nature is assigned to owned monsters and cannot be deleted.");
        var changed = ChangeTracker.Entries().Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(entry => entry.Entity is MonsterSpeciesDescriptor or MonsterTypeDescriptor or MonsterMoveDescriptor or MonsterStatusDescriptor or MonsterGenderDescriptor ||
                (entry.State == EntityState.Deleted && entry.Entity is AnimationDescriptor)).ToArray();
        if (changed.Length == 0) return;
        var types = MonsterTypes.AsNoTracking().ToDictionary(item => item.Id);
        var species = MonsterSpecies.AsNoTracking().ToDictionary(item => item.Id);
        var moves = MonsterMoves.AsNoTracking().ToDictionary(item => item.Id);
        var animations = Animations.AsNoTracking().Select(item => item.Id).ToHashSet();
        foreach (var entry in ChangeTracker.Entries<AnimationDescriptor>())
        {
            if (entry.State == EntityState.Deleted) animations.Remove(entry.Entity.Id);
            else animations.Add(entry.Entity.Id);
        }
        foreach (var entry in changed)
        {
            if (entry.Entity is MonsterTypeDescriptor type)
            {
                if (entry.State == EntityState.Deleted) types.Remove(type.Id);
                else types[type.Id] = type;
            }
            else if (entry.Entity is MonsterMoveDescriptor move)
            {
                if (entry.State == EntityState.Deleted) moves.Remove(move.Id);
                else moves[move.Id] = move;
            }
            else if (entry.Entity is MonsterSpeciesDescriptor item)
            {
                if (entry.State == EntityState.Deleted) species.Remove(item.Id);
                else species[item.Id] = item;
            }
        }
        var genders = MonsterGenders.AsNoTracking().Select(gender => gender.Id).ToHashSet();
        foreach (var entry in ChangeTracker.Entries<MonsterGenderDescriptor>())
        {
            var gender = entry.Entity;
            if (entry.State == EntityState.Deleted)
            {
                if (gender.Id == MonsterGenderDescriptor.DefaultMale || gender.Id == MonsterGenderDescriptor.DefaultFemale ||
                    GenderReferenceExists?.Invoke(gender.Id) == true ||
                    species.Values.Any(item => item.GenderWeights?.ContainsKey(gender.Id) == true))
                    throw new InvalidOperationException("This gender is used by the default distribution, a species or an owned monster.");
                genders.Remove(gender.Id);
            }
            else
            {
                if ((entry.State is EntityState.Added or EntityState.Modified) && !gender.IsValid)
                    throw new InvalidOperationException("Enter a gender name of 1–80 characters and an optional symbol of at most 16 characters.");
                genders.Add(gender.Id);
            }
        }
        // The built-in defaults are installed by the game migration. EnsureCreated test worlds can use the implicit defaults.
        genders.Add(MonsterGenderDescriptor.DefaultMale);
        genders.Add(MonsterGenderDescriptor.DefaultFemale);
        var statuses = MonsterStatuses.AsNoTracking().ToDictionary(item => item.Id);
        foreach (var entry in ChangeTracker.Entries<MonsterStatusDescriptor>())
        {
            var status = entry.Entity;
            if (entry.State == EntityState.Deleted)
            {
                statuses.Remove(status.Id);
                if (StatusReferenceExists?.Invoke(status.Id) == true ||
                    Intersect.Server.Monsters.Battles.MonsterPracticeBattles.Registry.ReservesStatus(status.Id) ||
                    moves.Values.Any(move => move.Effects.Any(effect => effect.StatusId == status.Id)))
                    throw new InvalidOperationException("This status is referenced by a move, owned monster or active battle.");
            }
            else
            {
                if (entry.State is EntityState.Added or EntityState.Modified &&
                    (!status.IsValid || status.AnimationReferences().Any(id => !animations.Contains(id))))
                    throw new InvalidOperationException("Invalid monster status settings or animation.");
                statuses[status.Id] = status;
            }
        }
        foreach (var entry in changed)
        {
            if (entry.Entity is MonsterTypeDescriptor type)
            {
                if (entry.State == EntityState.Deleted)
                {
                    if (MonsterTypeRules.IsReferenced(type.Id, species.Values, types.Values, moves.Values))
                        throw new InvalidOperationException("This type is referenced by species, moves or another type's effectiveness. Remove those references before deleting it.");
                }
                else MonsterTypeRules.Validate(type, types.ContainsKey);
            }
            else if (entry.Entity is MonsterMoveDescriptor move)
            {
                if (entry.State == EntityState.Deleted)
                {
                    if (MoveReferenceExists?.Invoke(move.Id) == true || MonsterMoveRules.IsReferenced(move.Id, species.Values))
                        throw new InvalidOperationException("This move is referenced by owned monsters or species learnsets. Remove those references before deleting it.");
                }
                else
                {
                    MonsterMoveRules.Validate(move, types.ContainsKey, animations.Contains);
                    if (move.Effects.Any(effect => effect.StatusId is { } id && !statuses.ContainsKey(id)))
                        throw new InvalidOperationException("A move effect references an unavailable status.");
                }
            }
            else if (entry.State != EntityState.Deleted && entry.Entity is MonsterSpeciesDescriptor item)
            {
                MonsterTypeRules.Validate(item, types.ContainsKey);
                MonsterMoveRules.ValidateLearnset(item, moves.ContainsKey);
                if (!MonsterGenderDescriptor.ValidDistribution(item.GenderWeights, genders.Contains))
                    throw new InvalidOperationException("Gender weights must reference existing genders, be 0–100000, and include a positive weight.");
            }
            else if (entry.State == EntityState.Deleted && entry.Entity is AnimationDescriptor animation &&
                     (moves.Values.Any(move => move.AnimationId == animation.Id) || statuses.Values.Any(status => status.AnimationReferences().Contains(animation.Id))))
                throw new InvalidOperationException("This animation is referenced by monster moves or statuses.");
        }
    }
    private void ValidateEncounters()
    {
        var tables = ChangeTracker.Entries<MonsterEncounterTableDescriptor>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        var removedSpecies = ChangeTracker.Entries<MonsterSpeciesDescriptor>()
            .Where(entry => entry.State == EntityState.Deleted).Select(entry => entry.Entity.Id).ToHashSet();
        var changedMaps = ChangeTracker.Entries<MapController>()
            .Where(entry => entry.State == EntityState.Added ||
                entry.State == EntityState.Modified && entry.Property(map => map.EncountersJson).IsModified).ToArray();

        if (tables.Length > 0 || removedSpecies.Count > 0)
        {
            var tableCatalog = MonsterEncounterTables.AsNoTracking().ToDictionary(table => table.Id);
            foreach (var entry in tables)
            {
                if (entry.State == EntityState.Deleted) tableCatalog.Remove(entry.Entity.Id);
                else tableCatalog[entry.Entity.Id] = entry.Entity;
            }

            if (removedSpecies.Count > 0 && tableCatalog.Values.Any(table =>
                table.Entries?.Any(row => row != null && removedSpecies.Contains(row.SpeciesId)) == true))
                throw new InvalidOperationException("This species is referenced by an encounter table.");

            var deletedTables = tables.Where(entry => entry.State == EntityState.Deleted)
                .Select(entry => entry.Entity.Id).ToArray();
            if (deletedTables.Length > 0)
            {
                var maps = Maps.AsNoTracking().Select(map => new { map.Id, map.EncountersJson }).AsEnumerable()
                    .ToDictionary(map => map.Id, map => MapEncounters.Parse(map.EncountersJson));
                foreach (var entry in ChangeTracker.Entries<MapController>())
                {
                    if (entry.State == EntityState.Deleted) maps.Remove(entry.Entity.Id);
                    else maps[entry.Entity.Id] = entry.Entity.Encounters;
                }
                if (maps.Values.Any(encounters => deletedTables.Any(id => encounters?.References(id) == true)))
                    throw new InvalidOperationException("This encounter table is assigned to a map or painted tile.");
            }

            if (tables.Any(entry => entry.State != EntityState.Deleted))
            {
                var speciesIds = MonsterSpecies.AsNoTracking().Select(species => species.Id).ToHashSet();
                foreach (var entry in ChangeTracker.Entries<MonsterSpeciesDescriptor>())
                {
                    if (entry.State == EntityState.Deleted) speciesIds.Remove(entry.Entity.Id);
                    else speciesIds.Add(entry.Entity.Id);
                }
                foreach (var entry in tables.Where(entry => entry.State != EntityState.Deleted))
                    if (entry.Entity.ValidationError(Options.Instance.Monsters.Battle.MaxLevel, speciesIds.Contains) is { } error)
                        throw new InvalidOperationException(error);
            }
        }

        foreach (var entry in changedMaps)
        {
            var encounters = entry.Entity.Encounters;
            if (encounters == null || encounters.Mode == MapEncounterMode.Disabled &&
                encounters.DefaultTableId == Guid.Empty && encounters.Rate == null && encounters.Tiles?.Count == 0)
                continue;

            var ids = MonsterEncounterTables.AsNoTracking().Select(table => table.Id).ToHashSet();
            foreach (var table in tables)
            {
                if (table.State == EntityState.Deleted) ids.Remove(table.Entity.Id);
                else ids.Add(table.Entity.Id);
            }
            if (!encounters.IsValid(Options.Instance.Map.MapWidth, Options.Instance.Map.MapHeight, ids.Contains))
                throw new InvalidOperationException("Invalid encounter settings or missing encounter table.");
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ChangeTracker.DetectChanges();
        if (!ChangeTracker.Entries().Any(entry => (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
                (entry.Entity is MonsterTypeDescriptor or MonsterSpeciesDescriptor or MonsterMoveDescriptor or MonsterStatusDescriptor or MonsterGenderDescriptor or MonsterNatureDescriptor or MonsterEncounterTableDescriptor or MonsterExperienceCurveDescriptor ||
                 entry.Entity is MapController && (entry.State == EntityState.Added || entry.Property(nameof(MapController.EncountersJson)).IsModified) ||
                 (entry.State == EntityState.Deleted && entry.Entity is AnimationDescriptor))))
            return base.SaveChanges(acceptAllChangesOnSuccess);
        lock (MonsterTypeRules.ContentGate)
        {
            ValidateExperienceCurves();
            ValidateMonsterContent();
            ValidateEncounters();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
    }
}
