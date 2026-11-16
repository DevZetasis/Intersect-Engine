using Intersect.Network.Packets.Server;

namespace Intersect.Client.Entities;

public partial class Player
{
    internal Guid PresentedCaptureRevision { get; set; }
    public OwnedMonstersPacket? MonsterRoster { get; set; }
    public MonsterBattleStatePacket? MonsterBattleState { get; set; }
    public long LastMonsterBattleSequence { get; set; }
}
