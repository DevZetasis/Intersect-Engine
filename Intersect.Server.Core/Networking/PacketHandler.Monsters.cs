using Intersect.Network.Packets.Client;
using Intersect.Server.Monsters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Intersect.Server.Networking;

internal sealed partial class PacketHandler
{
    public void HandlePacket(Client client, MonsterBattleActionPacket packet)
    {
        if (client.IsEditor || client.Entity is not { InGame: true } player) return;
        try { Intersect.Server.Monsters.Battles.MonsterPracticeBattles.Registry.Submit(player.Id, packet); }
        catch (InvalidOperationException exception) { Logger.LogDebug("Rejected monster battle action: {Reason}", exception.Message); }
        catch (Exception exception) { Logger.LogError(exception, "Monster battle resolution failed for {PlayerId}", player.Id); }
    }

    public void HandlePacket(Client client, RequestOwnedMonstersPacket packet)
    {
        if (!client.IsEditor && client.Entity is { InGame: true } player) MonsterRoster.Send(player);
    }

    public void HandlePacket(Client client, MoveOwnedMonsterPacket packet)
    {
        if (client.IsEditor || client.Entity is not { InGame: true } player) return;
        var message = string.Empty;
        try
        {
            lock (Intersect.Server.Monsters.Battles.MonsterPracticeBattles.Registry.SyncRoot)
            {
                if (Intersect.Server.Monsters.Battles.MonsterPracticeBattles.Registry.HasActive(player.Id)) throw new InvalidOperationException("Finish or forfeit the monster battle before moving your monsters.");
                MonsterRoster.CreateService().Move(player.Id, packet.MonsterId, packet.ExpectedRevision, packet.Destination);
            }
            Logger.LogInformation("Accepted monster move for {PlayerId}: monster {MonsterId}, destination {Destination}, expected revision {Revision}",
                player.Id, packet.MonsterId, packet.Destination, packet.ExpectedRevision);
        }
        catch (InvalidOperationException exception) { message = exception.Message; }
        catch (DbUpdateConcurrencyException) { message = "The collection changed. Please try again."; }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Failed to move owned monster for {PlayerId}", player.Id);
            message = "Unable to move this monster. Please refresh.";
        }
        MonsterRoster.Send(player, message);
    }
}
