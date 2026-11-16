namespace Intersect.Framework.Core.GameObjects.Monsters;

/// <summary>Deterministic percentage rules used by the editor preview and the server.</summary>
public static class MonsterCaptureChance
{
    public const decimal MaximumDeviceMultiplier = 10m;
    public const decimal MinimumDeviceMultiplier = 0.01m;
    public const decimal MaximumStatusMultiplier = 5m;
    public const decimal NormalCap = 95m;

    public static bool IsDeviceMultiplier(decimal value) =>
        value is >= MinimumDeviceMultiplier and <= MaximumDeviceMultiplier && decimal.Round(value, 2) == value;

    public static bool IsStatusMultiplier(decimal value) =>
        value is >= 0m and <= MaximumStatusMultiplier && decimal.Round(value, 2) == value;

    // Null means the action is ineligible. Zero is an eligible attempt that always fails.
    public static decimal? Calculate(bool allowed, int baseChance, long currentHP, long maximumHP,
        IEnumerable<decimal> statusMultipliers, decimal deviceMultiplier, bool guaranteed)
    {
        if (!allowed || maximumHP <= 0 || currentHP <= 0 || currentHP > maximumHP ||
            baseChance is < 0 or > 100 || !IsDeviceMultiplier(deviceMultiplier))
            return null;

        if (guaranteed || baseChance == 100) return 100m;
        if (baseChance == 0) return 0m;

        var status = 1m;
        var hasStatus = false;
        foreach (var multiplier in statusMultipliers)
        {
            if (!IsStatusMultiplier(multiplier)) return null;
            status = hasStatus ? Math.Max(status, multiplier) : multiplier;
            hasStatus = true;
        }

        var hp = 1m + (1m - (decimal)currentHP / maximumHP) * 0.5m;
        return Math.Min(NormalCap, baseChance * hp * status * deviceMultiplier);
    }
}
