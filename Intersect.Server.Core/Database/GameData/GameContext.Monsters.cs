using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Framework.Core.GameObjects.Animations;
using Intersect.Server.Monsters;
using Microsoft.EntityFrameworkCore;

namespace Intersect.Server.Database.GameData;

public abstract partial class GameContext
{
    private void ValidateMonsterContent()
    {
        ChangeTracker.DetectChanges();
        var changed = ChangeTracker.Entries().Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(entry => entry.Entity is MonsterSpeciesDescriptor or MonsterTypeDescriptor or MonsterMoveDescriptor ||
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
                    if (MonsterMoveRules.IsReferenced(move.Id, species.Values))
                        throw new InvalidOperationException("This move is referenced by species learnsets. Remove those references before deleting it.");
                }
                else MonsterMoveRules.Validate(move, types.ContainsKey, animations.Contains);
            }
            else if (entry.State != EntityState.Deleted && entry.Entity is MonsterSpeciesDescriptor item)
            {
                MonsterTypeRules.Validate(item, types.ContainsKey);
                MonsterMoveRules.ValidateLearnset(item, moves.ContainsKey);
            }
            else if (entry.State == EntityState.Deleted && entry.Entity is AnimationDescriptor animation &&
                     moves.Values.Any(move => move.AnimationId == animation.Id))
                throw new InvalidOperationException("This animation is referenced by monster moves.");
        }
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ChangeTracker.DetectChanges();
        if (!ChangeTracker.Entries().Any(entry => (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
                (entry.Entity is MonsterTypeDescriptor or MonsterSpeciesDescriptor or MonsterMoveDescriptor ||
                 (entry.State == EntityState.Deleted && entry.Entity is AnimationDescriptor))))
            return base.SaveChanges(acceptAllChangesOnSuccess);
        lock (MonsterTypeRules.ContentGate)
        {
            ValidateMonsterContent();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
    }
}
