using Newtonsoft.Json;

namespace Intersect.Config;

/// <summary>Immutable server calculation rules. Generation4 is independent of live server options.</summary>
public sealed record MonsterBattleRules
{
    public static MonsterBattleRules Generation4 { get; } = new();
    public int MaxLevel { get; init; } = 100;
    public int MaxEvPerStat { get; init; } = 255;
    public int MaxEvTotal { get; init; } = 510;
    public decimal StabMultiplier { get; init; } = 1.5m;
    public decimal CriticalMultiplier { get; init; } = 2m;
    public int MinimumDamagePercent { get; init; } = 85;
    public int MaximumDamagePercent { get; init; } = 100;
    [JsonIgnore]
    public bool IsValid => MaxLevel is >= 1 and <= 10000 && MaxEvPerStat is >= 0 and <= 255 &&
        MaxEvTotal >= 0 && MaxEvTotal <= 6 * MaxEvPerStat && StabMultiplier is > 0 and <= 10 &&
        CriticalMultiplier is > 0 and <= 10 && MinimumDamagePercent is >= 1 and <= 100 &&
        MaximumDamagePercent >= MinimumDamagePercent && MaximumDamagePercent <= 100;
    public void Validate()
    {
        if (!IsValid) throw new ArgumentException("Invalid monster battle rules. Check level/EV limits, multipliers and random damage bounds.");
    }
}
