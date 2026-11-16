using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Intersect.Editor.Forms.Editors.Events;
using Intersect.Editor.Forms.Editors.Events.Event_Commands;
using Intersect.Framework.Core.GameObjects.Events;
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
    [Test]
    public void BattleCommandStagesSpeciesAndLevelAndPrintsAllBranches()
    {
        if (Options.Instance == null) Options.LoadFromServer("{}");
        var page = new EventPage();
        var command = new StartMonsterBattleCommand(page.CommandLists);
        page.CommandLists.Values.First().Add(command);
        var species = new MonsterSpeciesDescriptor(Guid.NewGuid()) { Name = "Mossling" };
        MonsterSpeciesDescriptor.Lookup.Set(species.Id, species);
        try
        {
            using (var panel = new EventCommandStartMonsterBattle(command, null))
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var apply = panel.GetType().GetMethod("TryApply", flags)!;
                Assert.That(apply.Invoke(panel, null), Is.EqualTo(false));
                var select = Field(panel, "_species");
                select.SelectedItem = select.Items.Cast<object>().Single(item => item.ToString() == "Mossling");
                var level = (NumericUpDown)panel.GetType().GetField("_level", flags)!.GetValue(panel)!;
                level.Value = 10;
                var allowCapture = (CheckBox)panel.GetType().GetField("_allowCapture", flags)!.GetValue(panel)!;
                Assert.That(allowCapture.Checked, Is.False);
                allowCapture.Checked = true;
                Assert.That(command.AllowCapture, Is.False);
                Assert.That(command.SpeciesId, Is.EqualTo(Guid.Empty));
                Assert.That(command.Level, Is.EqualTo(1));
                Assert.That(apply.Invoke(panel, null), Is.EqualTo(true));
            }

            Assert.That(command.SpeciesId, Is.EqualTo(species.Id));
            Assert.That(command.Level, Is.EqualTo(10));
            Assert.That(command.AllowCapture, Is.True);
            using var list = new ListBox();
            CommandPrinter.PrintCommandList(page, page.CommandLists.Values.First(), "", list, [], null!);
            var rows = list.Items.Cast<string>().ToArray();
            Assert.That(rows.Any(row => row.Contains("Mossling, level 10")), Is.True);
            foreach (var branch in new[] { "Victory", "Defeat", "Escape", "Capture" })
                Assert.That(rows.Any(row => row.Contains("On " + branch)), Is.True);
        }
        finally { MonsterSpeciesDescriptor.Lookup.DeleteAt(species.Id); }
    }
}
