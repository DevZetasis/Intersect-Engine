using Newtonsoft.Json;

namespace Intersect.Framework.Core.GameObjects.Monsters;

/// <summary>Monster-only stat identifiers. Values are persistent; do not renumber them.</summary>
public enum MonsterStat { None = 0, HP = 1, Attack = 2, Defense = 3, SpecialAttack = 4, SpecialDefense = 5, Speed = 6 }

public sealed class MonsterNatureStatModifier
{
    public MonsterStat Stat { get; set; } = MonsterStat.None;
    // Magnitudes are unsigned percentages; the containing Increase/Decrease property gives direction.
    public decimal Percentage { get; set; } = 10m;
    [JsonIgnore]
    public bool IsValid => Enum.IsDefined(Stat) && Stat != MonsterStat.HP && Percentage is >= 0 and <= 50;
}

/// <summary>Definition data only. Future effects can extend this JSON object without flattening stat fields.</summary>
public sealed class MonsterNatureModifiers
{
    public MonsterNatureStatModifier Increase { get; set; } = new();
    public MonsterNatureStatModifier Decrease { get; set; } = new();
    [JsonIgnore]
    public bool HasConflict => Increase != null && Decrease != null && Increase.Stat != MonsterStat.None && Increase.Stat == Decrease.Stat;
    [JsonIgnore]
    public bool IsValid => Increase is { IsValid: true } && Decrease is { IsValid: true } && !HasConflict;
}
