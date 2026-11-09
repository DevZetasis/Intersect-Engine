using MessagePack;

namespace Intersect.Framework.Core.GameObjects.Monsters;

/// <summary>A private roster entry sent only to its owning character.</summary>
[MessagePackObject]
public sealed class OwnedMonsterSummary
{
    [Key(0)] public Guid Id { get; set; }
    [Key(1)] public Guid SpeciesId { get; set; }
    [Key(2)] public string? Nickname { get; set; }
    [Key(3)] public int Level { get; set; }
    [Key(4)] public long Experience { get; set; }
    [Key(5)] public MonsterLocation Location { get; set; }
    [Key(6)] public int Slot { get; set; }
    [Key(7)] public Guid Revision { get; set; }
}
