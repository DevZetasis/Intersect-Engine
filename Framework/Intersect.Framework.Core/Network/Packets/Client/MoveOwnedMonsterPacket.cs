using Intersect.Framework.Core.GameObjects.Monsters;
using MessagePack;

namespace Intersect.Network.Packets.Client;

[MessagePackObject]
public sealed class MoveOwnedMonsterPacket : IntersectPacket
{
    [Key(0)] public Guid MonsterId { get; set; }
    [Key(1)] public Guid ExpectedRevision { get; set; }
    [Key(2)] public MonsterLocation Destination { get; set; }
}
