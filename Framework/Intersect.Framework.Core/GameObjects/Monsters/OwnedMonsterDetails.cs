using MessagePack;

namespace Intersect.Framework.Core.GameObjects.Monsters;

/// <summary>Read-only server snapshot for the owning character. Array order follows MonsterStat (HP through Speed).</summary>
[MessagePackObject]
public sealed class OwnedMonsterDetails
{
    [Key(0)] public Guid? NatureId { get; set; }
    [Key(1)] public string? NatureName { get; set; }
    [Key(2)] public long[] IVs { get; set; } = [];
    [Key(3)] public long[] EVs { get; set; } = [];
    [Key(4)] public long[]? CalculatedStats { get; set; }
    [Key(5)] public string? CalculationError { get; set; }
}
