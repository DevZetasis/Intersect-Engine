using Intersect.Enums;
using Intersect.Framework.Core.GameObjects.Events.Commands;
using Intersect.Server.Monsters;
using Intersect.Server.Networking;
using Microsoft.Extensions.Logging;
namespace Intersect.Server.Entities.Events;
public static partial class CommandProcessing
{
    private static void ProcessCommand(HealPlayerMonstersCommand command, Player player, Event instance, CommandInstance stackInfo, Stack<CommandInstance> callStack)
    {
        try { MonsterHealingEvents.Execute(command, player); }
        catch (Exception exception)
        {
            Intersect.Core.ApplicationContext.Context.Value?.Logger.LogWarning(exception, "Monster healing event failed for {PlayerId}", player.Id);
            PacketSender.SendChatMsg(player, Intersect.Server.Localization.Strings.MonsterHealing.Failed, ChatMessageType.Notice, Color.Red);
            callStack.Clear(); // Do not continue to a scripted success message after a rejected/failed heal.
        }
    }
}
