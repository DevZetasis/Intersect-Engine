using Newtonsoft.Json;

namespace Intersect.Framework.Core.GameObjects.Monsters;

/// <summary>Species stats, independent of the real-time RPG stat model.</summary>
public sealed class MonsterBaseStats
{
    public int HP { get; set; } = 1;
    public int Attack { get; set; } = 1;
    public int Defense { get; set; } = 1;
    public int Speed { get; set; } = 1;

    [JsonIgnore]
    public bool IsValid => HP > 0 && Attack > 0 && Defense > 0 && Speed > 0;
}
