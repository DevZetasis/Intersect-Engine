using Intersect.Network.Packets.Server;

namespace Intersect.Client.Entities;

public partial class Player
{
    public OwnedMonstersPacket? MonsterRoster { get; set; }
}
