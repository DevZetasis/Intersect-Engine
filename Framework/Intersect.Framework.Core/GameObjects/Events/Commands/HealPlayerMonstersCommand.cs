using System.ComponentModel;
using Intersect.Framework.Core.GameObjects.Monsters;
namespace Intersect.Framework.Core.GameObjects.Events.Commands;
public sealed class HealPlayerMonstersCommand : EventCommand
{
    public override EventCommandType Type => EventCommandType.HealPlayerMonsters;
    public MonsterHealingScope Scope { get; set; } = MonsterHealingScope.Party;
    [DefaultValue(MonsterRecovery.Both)] public MonsterRecovery Recovery { get; set; } = MonsterRecovery.Both;
}
