using Intersect.Framework.Core.GameObjects.Events;
using Intersect.Framework.Core.GameObjects.Events.Commands;
using Intersect.Network.Packets.Server;

namespace Intersect.Server.Entities.Events;


public partial class CommandInstance
{

    public enum EventResponse
    {

        None = 0,

        Dialogue,

        Shop,

        Bank,

        Crafting,

        Quest,

        Timer,

        Picture,

        Fade,

        MonsterBattle

    }

    public Guid[]
        BranchIds = null; //Potential Branches for Commands that require responses such as ShowingOptions or Offering a Quest

    public EventCommand Command;

    private int commandIndex;

    public List<EventCommand> CommandList;

    public Guid CommandListId;

    public EventPage Page;

    public EventResponse WaitingForResponse = EventResponse.None;

    public Guid WaitingForRoute;

    public Guid WaitingForRouteMap;

    public EventCommand WaitingOnCommand = null;

    internal PendingBattle? MonsterBattle { get; private set; }

    internal sealed record PendingBattle(Guid Id, StartMonsterBattleCommand Command, Task<MonsterBattleOutcome> Result);

    internal bool TryBeginMonsterBattle(StartMonsterBattleCommand command,
        Func<(Guid Id, Task<MonsterBattleOutcome> Result)?> start)
    {
        if (MonsterBattle != null) return false;
        var branches = command.Branches();
        if (branches.Distinct().Count() != branches.Length ||
            branches.Any(id => id == Guid.Empty || !Page.CommandLists.ContainsKey(id)) ||
            start() is not { } battle)
            return false;

        MonsterBattle = new(battle.Id, command, battle.Result);
        WaitingForResponse = EventResponse.MonsterBattle;
        return true;
    }

    internal bool TryConsumeMonsterBattle(out CommandInstance? branch)
    {
        branch = null;
        if (MonsterBattle is not { Result.IsCompleted: true } pending) return false;

        MonsterBattle = null;
        WaitingForResponse = EventResponse.None;
        var id = (pending.Result.IsCompletedSuccessfully ? pending.Result.Result : MonsterBattleOutcome.Cancelled) switch
        {
            MonsterBattleOutcome.Victory => pending.Command.Victory,
            MonsterBattleOutcome.Defeat => pending.Command.Defeat,
            MonsterBattleOutcome.Escaped => pending.Command.Escape,
            MonsterBattleOutcome.Captured when pending.Command.AllowCapture => pending.Command.Capture,
            _ => Guid.Empty,
        };
        if (id != Guid.Empty && Page.CommandLists.TryGetValue(id, out var commands))
            branch = new CommandInstance(Page, commands);
        return true;
    }

    public CommandInstance(EventPage page, int listIndex = 0)
    {
        Page = page;
        CommandList = page.CommandLists.Values.First();
        CommandIndex = listIndex;
    }

    public CommandInstance(EventPage page, List<EventCommand> commandList, int listIndex = 0)
    {
        Page = page;
        CommandList = commandList;
        CommandIndex = listIndex;
    }

    public CommandInstance(EventPage page, Guid commandListId, int listIndex = 0)
    {
        Page = page;
        CommandList = page.CommandLists[commandListId];
        CommandIndex = listIndex;
    }

    public int CommandIndex
    {
        get => commandIndex;
        set
        {
            commandIndex = value;
            Command = commandIndex >= 0 && commandIndex < CommandList.Count ? CommandList[commandIndex] : null;
        }
    }

}
