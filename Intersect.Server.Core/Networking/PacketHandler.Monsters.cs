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
        try
        {
            if (packet.Kind == MonsterBattleActionKind.Capture)
            {
                lock (player.EntityLock)
                    Intersect.Server.Monsters.Battles.MonsterPracticeBattles.Registry.Submit(player.Id, packet);
            }
            else Intersect.Server.Monsters.Battles.MonsterPracticeBattles.Registry.Submit(player.Id, packet);
        }
        catch (InvalidOperationException exception) { Logger.LogDebug("Rejected monster battle action: {Reason}", exception.Message); }
        catch (Exception exception) { Logger.LogError(exception, "Monster battle resolution failed for {PlayerId}", player.Id); }
    }

    public void HandlePacket(Client client, RequestOwnedMonstersPacket packet)
    {
        if (!client.IsEditor && client.Entity is { InGame: true } player) MonsterRoster.Send(player);
    }

    public void HandlePacket(Client client, ResolveMonsterMoveLearningPacket packet)
    {
        if (client.IsEditor || client.Entity is not { InGame: true } player) return;
        var message = "";
        try
        {
            var registry = Intersect.Server.Monsters.Battles.MonsterPracticeBattles.Registry;
            lock (registry.SyncRoot)
                if (registry.HasActive(player.Id) || registry.HasActiveWild(player.Id) ||
                    !MonsterRoster.CreateService().ResolveMoveLearning(player.Id, packet.MonsterId, packet.DecisionId, packet.Slot))
                    message = "This choice is no longer available or the battle is still open.";
        }
        catch (DbUpdateConcurrencyException)
        {
            message = "The collection changed. Review the current move choice.";
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Could not resolve monster move learning for {PlayerId}", player.Id);
            message = "Unable to save the move choice. Please try again.";
        }
        MonsterRoster.Send(player, message);
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
