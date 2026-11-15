using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Server.Monsters.Calculations;

namespace Intersect.Server.Monsters;

public static class MonsterEncounterSelection
{
    public sealed record Encounter(Guid TableId, Guid SpeciesId, int Level);

    public static bool IsEligibleStep(Guid oldMap, int oldX, int oldY,
        Guid map, int x, int y, bool interrupted) =>
        !interrupted && oldMap == map && (oldX != x || oldY != y) &&
        Math.Abs((long)oldX - x) <= 1 && Math.Abs((long)oldY - y) <= 1;

    public static Encounter? Roll(MonsterEncounterTableDescriptor table, int? rateOverride,
        int maxLevel, Func<Guid, bool> speciesExists, IMonsterRandom random)
    {
        if (!table.Enabled || (rateOverride ?? table.Rate) is not (> 0 and <= 100) ||
            table.Cooldown is < 0 or > MonsterEncounterTableDescriptor.MaximumCooldown)
            return null;

        var entries = table.Entries?.Where(entry => entry is { Weight: > 0 } &&
            entry.IsValid(maxLevel, speciesExists)).ToArray() ?? [];
        var weight = entries.Sum(entry => (long)entry.Weight);
        if (weight == 0 || MonsterRandom.Draw(random, 100) >= (rateOverride ?? table.Rate)) return null;

        var roll = MonsterRandom.Draw(random, weight);
        foreach (var entry in entries)
        {
            if (roll < entry.Weight)
                return new(table.Id, entry.SpeciesId,
                    entry.MinimumLevel + (int)MonsterRandom.Draw(random, (long)entry.MaximumLevel - entry.MinimumLevel + 1));
            roll -= entry.Weight;
        }
        return null;
    }
}

public sealed class MonsterEncounterCooldown
{
    public int Remaining { get; private set; }

    public void Start(int steps) => Remaining = steps;

    public bool ConsumeEligibleStep()
    {
        if (Remaining == 0) return false;
        Remaining--;
        return true;
    }
}
