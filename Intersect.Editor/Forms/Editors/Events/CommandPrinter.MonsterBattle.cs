using Intersect.Editor.Localization;
using Intersect.Editor.Maps;
using Intersect.Framework.Core.GameObjects.Events;
using Intersect.Framework.Core.GameObjects.Events.Commands;
using Intersect.Framework.Core.GameObjects.Monsters;

namespace Intersect.Editor.Forms.Editors.Events;

public static partial class CommandPrinter
{
    private static void PrintMonsterBattle(EventPage page, List<EventCommand> commands, int index, string indent,
        ListBox list, List<CommandListProperties> properties, MapInstance map)
    {
        var command = (StartMonsterBattleCommand)commands[index];
        var species = MonsterSpeciesDescriptor.Get(command.SpeciesId)?.Name ?? $"Missing species ({command.SpeciesId})";

        void AddRow(string text, bool editable)
        {
            list.Items.Add(indent + text);
            properties.Add(new CommandListProperties
            {
                MyIndex = index, MyList = commands, Editable = editable, Type = command.Type, Cmd = command,
            });
        }

        AddRow($"{Strings.EventCommandList.linestart}Start Monster Battle — {species}, level {command.Level}{(command.AllowCapture ? ", capture allowed" : "")}", true);
        foreach (var (name, id) in new[] { ("Victory", command.Victory), ("Defeat", command.Defeat), ("Escape", command.Escape), ("Capture", command.Capture) })
        {
            if (name == "Capture" && id == Guid.Empty) continue;
            AddRow("      On " + name, false);
            if (page.CommandLists.TryGetValue(id, out var branch))
                PrintCommandList(page, branch, indent + "          ", list, properties, map);
        }
        AddRow("      End Monster Battle", false);
    }
}
