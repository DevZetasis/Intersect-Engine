using MessagePack;

namespace Intersect.Network.Packets.Client;

[MessagePackObject]
public sealed class ResolveMonsterMoveLearningPacket : IntersectPacket
{
    [Key(0)] public Guid MonsterId { get; set; }
    [Key(1)] public Guid DecisionId { get; set; }
    [Key(2)] public int Slot { get; set; } = -1;
}
