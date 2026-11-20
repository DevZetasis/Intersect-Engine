using Intersect.Config;

namespace Intersect.Framework.Core.GameObjects.Monsters;

public enum MonsterGrowthPreset
{
    MediumFast,
    Fast,
    MediumSlow,
    Slow,
    Custom,
}

public static class MonsterExperience
{
    public const int MaximumSupportedLevel = 10000;

    public static long Threshold(MonsterGrowthPreset preset, int level)
    {
        if (level is < 1 or > MaximumSupportedLevel)
            throw new ArgumentOutOfRangeException(nameof(level));
        if (level == 1 && preset != MonsterGrowthPreset.Custom && Enum.IsDefined(preset)) return 0;

        long n = level;
        var cube = n * n * n;
        return preset switch
        {
            MonsterGrowthPreset.Fast => 4 * cube / 5,
            MonsterGrowthPreset.MediumFast => cube,
            MonsterGrowthPreset.MediumSlow => 6 * cube / 5 - 15 * n * n + 100 * n - 140,
            MonsterGrowthPreset.Slow => 5 * cube / 4,
            _ => throw new ArgumentOutOfRangeException(nameof(preset)),
        };
    }

    public static bool ValidMultiplier(decimal value) => value is >= 0 and <= 10 && decimal.Round(value, 2) == value;

    public static long Reward(int yield, int opponentLevel, MonsterExperienceOptions settings,
        decimal encounterMultiplier, bool captured, bool participated, bool fainted)
    {
        if (!settings.IsValid || !ValidMultiplier(encounterMultiplier) || yield < 0 ||
            opponentLevel is < 1 or > MaximumSupportedLevel)
            throw new ArgumentException("Invalid monster experience settings.");
        if ((!participated && !settings.ShareWithEntireParty) || (fainted && !settings.AllowFainted) ||
            (captured && !settings.AwardExperienceForCaptures)) return 0;

        var baseReward = (long)yield * opponentLevel / 7;
        var outcome = captured ? settings.CapturePercent : 100;
        var participation = participated ? settings.ParticipatingPercent : settings.NonparticipatingPercent;
        return (long)decimal.Floor(baseReward * settings.GlobalMultiplier * encounterMultiplier * outcome * participation / 10000m);
    }
}
