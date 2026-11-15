using Intersect.Core;
using Intersect.Enums;
using Intersect.Framework.Core;
using Intersect.Framework.Core.GameObjects.Events;
using Intersect.Framework.Core.GameObjects.Events.Commands;
using Intersect.Server.Monsters.Battles;
using Intersect.Server.Networking;
using Intersect.Utilities;
using Microsoft.Extensions.Logging;

namespace Intersect.Server.Entities.Events;

public static partial class CommandProcessing
{
    private static void ProcessCommand(StartMonsterBattleCommand command, Player player, Event instance,
        CommandInstance stackInfo, Stack<CommandInstance> callStack)
    {
        try
        {
            if (stackInfo.TryBeginMonsterBattle(command, () => MonsterPracticeBattles.TryStartEventBattle(player, command, instance)))
                return;
        }
        catch (Exception exception)
        {
            ApplicationContext.Context.Value?.Logger.LogWarning(exception, "Could not start monster battle for event {EventId} and player {PlayerId}",
                instance.Descriptor.Id, player.Id);
        }

        if (instance.PageInstance?.Trigger == EventTrigger.Autorun)
            instance.WaitTimer = Timing.Global.Milliseconds + 1000;
        else if (player.Client != null)
            PacketSender.SendChatMsg(player, "This monster battle is unavailable. You need a healthy party monster with a usable move.", ChatMessageType.Notice, Color.Red);

        instance.AbortMonsterBattleScript();
    }
}
