using MessagePack;

namespace Intersect.Network.Packets.Client;

[MessagePackObject]
public sealed class MonsterBattleActionPacket : IntersectPacket
{
    [Key(0)] public Guid BattleId { get; set; }
    [Key(1)] public int Turn { get; set; }
    [Key(2)] public Guid Revision { get; set; }
    [Key(3)] public int MoveSlot { get; set; }
    [Key(4)] public bool Forfeit { get; set; }
}
