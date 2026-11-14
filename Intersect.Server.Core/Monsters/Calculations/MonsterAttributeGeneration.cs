using Intersect.Framework.Core.GameObjects.Monsters;

namespace Intersect.Server.Monsters.Calculations;

public static class MonsterAttributeGeneration
{
    public static MonsterStatValues GenerateIVs(IMonsterRandom random) => new(
        MonsterRandom.Draw(random, 32), MonsterRandom.Draw(random, 32), MonsterRandom.Draw(random, 32),
        MonsterRandom.Draw(random, 32), MonsterRandom.Draw(random, 32), MonsterRandom.Draw(random, 32));

    public static Guid? SelectNature(IEnumerable<MonsterNatureDescriptor> definitions, IMonsterRandom random)
    {
        var eligible = definitions.OrderBy(nature => nature.Id).ToArray();
        if (eligible.Any(nature => !nature.IsValid)) throw new ArgumentException("Invalid nature definition in generation catalog.");
        long total = 0;
        foreach (var nature in eligible) total = checked(total + nature.SelectionWeight);
        if (total == 0) return null; // Neutral/unassigned: no draw, no zero-weight selection.
        var roll = MonsterRandom.Draw(random, total);
        foreach (var nature in eligible)
        {
            if (roll < nature.SelectionWeight) return nature.Id;
            roll -= nature.SelectionWeight;
        }
        throw new InvalidOperationException("Nature selection did not resolve.");
    }
    public static Guid? SelectGender(MonsterSpeciesDescriptor? species, IMonsterRandom random,
        Func<Guid, bool>? exists = null)
    {
        exists ??= id => MonsterGenderDescriptor.Get(id) is { IsValid: true };
        var weights = species?.GenderWeights?.Where(pair => pair.Key != Guid.Empty &&
            pair.Value is > 0 and <= 100000 && exists(pair.Key)).OrderBy(pair => pair.Key).ToArray();
        if (weights is not { Length: > 0 }) return null;
        var total = weights.Sum(pair => (long)pair.Value);
        var roll = MonsterRandom.Draw(random, total);
        foreach (var (id, weight) in weights)
        {
            if (roll < weight) return id;
            roll -= weight;
        }
        throw new InvalidOperationException("Gender weight selection did not resolve.");
    }
}
