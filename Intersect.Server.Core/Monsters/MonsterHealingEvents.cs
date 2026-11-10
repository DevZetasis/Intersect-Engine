using Intersect.Framework.Core.GameObjects.Events.Commands;
using Intersect.Server.Entities;
namespace Intersect.Server.Monsters;
public static class MonsterHealingEvents
{
    public static int Execute(HealPlayerMonstersCommand command, Player triggeringPlayer, MonsterOwnershipService? ownership = null)
    {
        if (triggeringPlayer.Id == Guid.Empty) throw new InvalidOperationException("A triggering character is required.");
        var count = new MonsterHealingService(ownership ?? MonsterRoster.CreateService()).HealCollection(triggeringPlayer.Id, command.Scope, command.Recovery);
        if (triggeringPlayer.Client != null) MonsterRoster.Send(triggeringPlayer);
        return count;
    }
}
