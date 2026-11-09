using Newtonsoft.Json;

namespace Intersect.Framework.Core.GameObjects.Monsters;

/// <summary>A species template, not a monster's learned-move inventory.</summary>
public sealed class MonsterLearnsetEntry
{
    public Guid MoveId { get; set; }
    public int Level { get; set; } = 1;
    [JsonIgnore]
    public bool IsValid => MoveId != Guid.Empty && Level >= 1;
}
