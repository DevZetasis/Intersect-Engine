using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Server.Monsters;
using Microsoft.EntityFrameworkCore;

namespace Intersect.Server.Database.GameData;

public abstract partial class GameContext
{
    private void ValidateMonsterContent()
    {
        ChangeTracker.DetectChanges();
        var changed = ChangeTracker.Entries().Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(entry => entry.Entity is MonsterSpeciesDescriptor or MonsterTypeDescriptor).ToArray();
        if (changed.Length == 0) return;
        var types = MonsterTypes.AsNoTracking().ToDictionary(item => item.Id);
        var species = MonsterSpecies.AsNoTracking().ToDictionary(item => item.Id);
        foreach (var entry in changed)
        {
            if (entry.Entity is MonsterTypeDescriptor type)
            {
                if (entry.State == EntityState.Deleted) types.Remove(type.Id);
                else types[type.Id] = type;
            }
            else if (entry.Entity is MonsterSpeciesDescriptor item)
            {
                if (entry.State == EntityState.Deleted) species.Remove(item.Id);
                else species[item.Id] = item;
            }
        }
        foreach (var entry in changed)
        {
            if (entry.Entity is MonsterTypeDescriptor type)
            {
                if (entry.State == EntityState.Deleted)
                {
                    if (MonsterTypeRules.IsReferenced(type.Id, species.Values, types.Values))
                        throw new InvalidOperationException("This type is referenced by species or another type's effectiveness. Remove those references before deleting it.");
                }
                else MonsterTypeRules.Validate(type, types.ContainsKey);
            }
            else if (entry.State != EntityState.Deleted && entry.Entity is MonsterSpeciesDescriptor item)
                MonsterTypeRules.Validate(item, types.ContainsKey);
        }
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ChangeTracker.DetectChanges();
        if (!ChangeTracker.Entries().Any(entry => (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
                (entry.Entity is MonsterTypeDescriptor or MonsterSpeciesDescriptor)))
            return base.SaveChanges(acceptAllChangesOnSuccess);
        lock (MonsterTypeRules.ContentGate)
        {
            ValidateMonsterContent();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
    }
}
