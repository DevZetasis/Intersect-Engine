using Intersect.Framework.Core.GameObjects.Monsters;
using MessagePack;

namespace Intersect.Network.Packets.Server;

[MessagePackObject]
public sealed class OwnedMonstersPacket : IntersectPacket
{
    [Key(0)] public Guid PlayerId { get; set; }
    [Key(1)] public int PartyCapacity { get; set; }
    [Key(2)] public OwnedMonsterSummary[] Monsters { get; set; } = [];
    [Key(3)] public string Message { get; set; } = string.Empty;
}
