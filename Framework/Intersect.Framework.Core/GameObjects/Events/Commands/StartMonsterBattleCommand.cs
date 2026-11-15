using Newtonsoft.Json;

namespace Intersect.Framework.Core.GameObjects.Events.Commands;

public sealed class StartMonsterBattleCommand : EventCommand
{
    [JsonConstructor]
    public StartMonsterBattleCommand() { }

    public StartMonsterBattleCommand(Dictionary<Guid, List<EventCommand>> commandLists)
    {
        Victory = Guid.NewGuid();
        Defeat = Guid.NewGuid();
        Escape = Guid.NewGuid();
        foreach (var branch in Branches()) commandLists.Add(branch, []);
    }

    public override EventCommandType Type => EventCommandType.StartMonsterBattle;
    public Guid SpeciesId { get; set; }
    public int Level { get; set; } = 1;
    public Guid Victory { get; set; }
    public Guid Defeat { get; set; }
    public Guid Escape { get; set; }

    public Guid[] Branches() => [Victory, Defeat, Escape];

    public bool IsValid(int maxLevel, Func<Guid, bool> speciesExists) =>
        SpeciesId != Guid.Empty && speciesExists(SpeciesId) && Level >= 1 && Level <= maxLevel;

    public override string GetCopyData(Dictionary<Guid, List<EventCommand>> commandLists,
        Dictionary<Guid, List<EventCommand>> copyLists)
    {
        foreach (var branch in Branches())
        {
            if (branch != Guid.Empty && commandLists.TryGetValue(branch, out var commands) && copyLists.TryAdd(branch, commands))
                foreach (var command in commands) command.GetCopyData(commandLists, copyLists);
        }

        return base.GetCopyData(commandLists, copyLists);
    }

    public override void FixBranchIds(Dictionary<Guid, Guid> idDict)
    {
        Victory = idDict.GetValueOrDefault(Victory, Victory);
        Defeat = idDict.GetValueOrDefault(Defeat, Defeat);
        Escape = idDict.GetValueOrDefault(Escape, Escape);
    }
}
