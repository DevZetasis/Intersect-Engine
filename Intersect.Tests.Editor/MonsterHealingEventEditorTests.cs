using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Intersect.Editor.Forms.Editors.Events.Event_Commands;
using Intersect.Framework.Core.GameObjects.Events.Commands;
using Intersect.Framework.Core.GameObjects.Monsters;
using NUnit.Framework;

namespace Intersect.Tests.Editor;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public class MonsterHealingEventEditorTests
{
    private static ComboBox Field(object panel, string name) => (ComboBox)panel.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(panel)!;

    [Test]
    public void HealingOptionsAreStagedUntilSaveAndCancelPreservesCommand()
    {
        var command = new HealPlayerMonstersCommand();
        Assert.That(command.Scope, Is.EqualTo(MonsterHealingScope.Party));
        Assert.That(command.Recovery, Is.EqualTo(MonsterRecovery.Both));
        using (var panel = new EventCommandHealPlayerMonsters(command, null))
        {
            Field(panel, "_scope").SelectedIndex = 1;
            Field(panel, "_recovery").SelectedIndex = 0;
            Assert.That(command.Scope, Is.EqualTo(MonsterHealingScope.Party));
            Assert.That(command.Recovery, Is.EqualTo(MonsterRecovery.Both));
            // Disposing an unsaved panel models cancelling the command edit.
        }
        Assert.That(command.Scope, Is.EqualTo(MonsterHealingScope.Party));
        using (var panel = new EventCommandHealPlayerMonsters(command, null))
        {
            Field(panel, "_scope").SelectedIndex = 1;
            Field(panel, "_recovery").SelectedIndex = 1;
            typeof(EventCommandHealPlayerMonsters).GetMethod("SaveChanges", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(panel, null);
        }
        Assert.That(command.Scope, Is.EqualTo(MonsterHealingScope.AllOwned));
        Assert.That(command.Recovery, Is.EqualTo(MonsterRecovery.PP));
    }
}
