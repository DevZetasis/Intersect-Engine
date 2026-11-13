using Intersect.Editor.Localization;
using Intersect.Editor.Maps;
using Intersect.Framework.Core.GameObjects.Events.Commands;
using Intersect.Framework.Core.GameObjects.Monsters;
namespace Intersect.Editor.Forms.Editors.Events;
public static partial class CommandPrinter
{
    private static string GetCommandText(HealPlayerMonstersCommand command, MapInstance map) =>
        Strings.MonsterHealing.Title + ": " + (command.Scope == MonsterHealingScope.AllOwned ? Strings.MonsterHealing.AllOwned : Strings.MonsterHealing.Party) + " / " +
        (command.Recovery switch
        {
            MonsterRecovery.HP => Strings.MonsterHealing.HP,
            MonsterRecovery.PP => Strings.MonsterHealing.PP,
            MonsterRecovery.Status => Strings.MonsterHealing.Status,
            MonsterRecovery.All => Strings.MonsterHealing.Everything,
            _ => Strings.MonsterHealing.Both,
        });
}
