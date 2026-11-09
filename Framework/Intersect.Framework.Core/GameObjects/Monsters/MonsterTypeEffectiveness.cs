namespace Intersect.Framework.Core.GameObjects.Monsters;

/// <summary>Pure type multiplier calculation, independent of entities, stats, moves and battles.</summary>
public static class MonsterTypeEffectiveness
{
    public static bool IsSupported(double multiplier) => multiplier is 0d or 0.5d or 1d or 2d;

    public static double Evaluate(MonsterTypeDescriptor? attacking, Guid? primaryDefender, Guid? secondaryDefender = null)
    {
        double Against(Guid? id) => id is { } value && value != Guid.Empty &&
            attacking?.Effectiveness is { } rules && rules.TryGetValue(value, out var multiplier) && IsSupported(multiplier)
                ? multiplier : 1d;
        // A repeated defending UUID must never count twice, even for malformed callers.
        return Against(primaryDefender) * (secondaryDefender == primaryDefender ? 1d : Against(secondaryDefender));
    }
}
