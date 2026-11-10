using Intersect.Editor.Localization;
using Intersect.Editor.Maps;
using Intersect.Framework.Core.GameObjects.Events.Commands;
using Intersect.Framework.Core.GameObjects.Monsters;
namespace Intersect.Editor.Forms.Editors.Events;
public static partial class CommandPrinter
{
    private static string GetCommandText(HealPlayerMonstersCommand command, MapInstance map) =>
        Strings.MonsterHealing.Title + ": " + (command.Scope == MonsterHealingScope.AllOwned ? Strings.MonsterHealing.AllOwned : Strings.MonsterHealing.Party) + " / " +
        (command.Recovery == MonsterRecovery.HP ? Strings.MonsterHealing.HP : command.Recovery == MonsterRecovery.PP ? Strings.MonsterHealing.PP : Strings.MonsterHealing.Both);
}
