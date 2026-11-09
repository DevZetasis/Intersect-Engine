using Intersect.Core;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Network.Packets.Server;
using Intersect.Server.Database;
using Intersect.Server.Entities;
using Microsoft.Extensions.Logging;

namespace Intersect.Server.Monsters;

public static class MonsterRoster
{
    public static MonsterOwnershipService CreateService(ILogger? logger = null) => new(
        () => DbInterface.CreatePlayerContext(readOnly: false, autoDetectChanges: true, killServerOnConcurrencyException: false),
        speciesId => MonsterSpeciesDescriptor.Get(speciesId) != null,
        Options.Instance.Monsters.PartyCapacity,
        natures: () => MonsterNatureDescriptor.Lookup.Values.Cast<MonsterNatureDescriptor>(),
        rules: Options.Instance.Monsters.Battle,
        logger: logger ?? ApplicationContext.Context.Value?.Logger
    );

    public static void Send(Player player, string message = "")
    {
        try
        {
            var service = CreateService();
            player.SendPacket(new OwnedMonstersPacket
            {
                PlayerId = player.Id, PartyCapacity = service.PartyCapacity,
                Monsters = service.GetOwned(player.Id), Message = message,
            });
        }
        catch (Exception exception)
        {
            ApplicationContext.Context.Value?.Logger.LogError(exception, "Failed to load monster roster for {PlayerId}", player.Id);
            player.SendPacket(new OwnedMonstersPacket
            {
                PlayerId = player.Id, Message = "Unable to load your monsters. Please refresh.",
            });
        }
    }
}
