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
    [Key(6)] public bool ConditionInitialized { get; set; }
    [Key(7)] public long CurrentHP { get; set; }
    [Key(8)] public long MaximumHP { get; set; }
    [Key(9)] public OwnedMonsterMovePP[] Moves { get; set; } = [];

    [Key(10)] public string StatusName { get; set; } = "";
}

[MessagePackObject]
public sealed class OwnedMonsterMovePP
{
    [Key(0)] public int Slot { get; set; }
    [Key(1)] public Guid? MoveId { get; set; }
    [Key(2)] public string Name { get; set; } = string.Empty;
    [Key(3)] public int CurrentPP { get; set; }
    [Key(4)] public int MaximumPP { get; set; }
}
