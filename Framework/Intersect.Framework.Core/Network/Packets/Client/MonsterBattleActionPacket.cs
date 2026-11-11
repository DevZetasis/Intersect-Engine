using MessagePack;

namespace Intersect.Network.Packets.Client;

public enum MonsterBattleActionKind { Move, Switch, Replace, Forfeit }

[MessagePackObject]
public sealed class MonsterBattleActionPacket : IntersectPacket
{
    [Key(0)] public Guid BattleId { get; set; }
    [Key(1)] public int Turn { get; set; }
    [Key(2)] public Guid Revision { get; set; }
    [Key(3)] public int MoveSlot { get; set; }
    [Key(4)] public bool Forfeit { get; set; }
    [Key(5)] public MonsterBattleActionKind Kind { get; set; }
    [Key(6)] public Guid MonsterId { get; set; }
}
