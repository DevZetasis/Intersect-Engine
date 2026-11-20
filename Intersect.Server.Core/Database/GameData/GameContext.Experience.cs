using Intersect.Config;
using Intersect.Framework.Core.GameObjects.Monsters;
using Microsoft.EntityFrameworkCore;

namespace Intersect.Server.Database.GameData;

public abstract partial class GameContext
{
    private void ValidateExperienceCurves()
    {
        var curves = ChangeTracker.Entries<MonsterExperienceCurveDescriptor>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        var species = ChangeTracker.Entries<MonsterSpeciesDescriptor>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        if (curves.Length == 0 && species.Length == 0) return;

        var catalog = MonsterExperienceCurves.AsNoTracking().ToDictionary(curve => curve.Id);
        var cap = Options.Instance.Monsters.Battle.MaxLevel;
        foreach (var entry in curves)
        {
            if (entry.State == EntityState.Deleted) catalog.Remove(entry.Entity.Id);
            else
            {
                if (!entry.Entity.IsValid(cap))
                    throw new InvalidOperationException($"Experience curves require increasing cumulative EXP from 0 through level {cap}.");
                catalog[entry.Entity.Id] = entry.Entity;
            }
        }

        var references = MonsterSpecies.AsNoTracking().Where(item => item.ExperienceCurveId != null)
            .Select(item => new { item.Id, item.ExperienceCurveId }).ToDictionary(item => item.Id, item => item.ExperienceCurveId);
        foreach (var entry in species)
        {
            references.Remove(entry.Entity.Id);
            if (entry.State != EntityState.Deleted && entry.Entity.ExperienceCurveId is { } id)
                references.Add(entry.Entity.Id, id);
        }
        if (references.Values.Any(id => !catalog.ContainsKey(id!.Value)))
            throw new InvalidOperationException("The experience curve is referenced by a species or does not exist.");
    }
}
