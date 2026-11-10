using Intersect.Client.General;
using Intersect.Network;
using Intersect.Network.Packets.Server;

namespace Intersect.Client.Networking;

internal sealed partial class PacketHandler
{
    public void HandlePacket(IPacketSender sender, MonsterBattleStatePacket packet)
    {
        if (Globals.Me is not { } player || player.Id != packet.PlayerId || packet.Sequence <= player.LastMonsterBattleSequence) return;
        player.LastMonsterBattleSequence = packet.Sequence;
        player.MonsterBattleState = packet;
    }
    public void HandlePacket(IPacketSender sender, OwnedMonstersPacket packet)
    {
        // Discard snapshots queued for a previous character after character selection.
        if (Globals.Me is { } player && player.Id == packet.PlayerId) player.MonsterRoster = packet;
    }
}
