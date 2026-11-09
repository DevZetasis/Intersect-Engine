using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using Intersect.Editor.Forms.Editors;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Network.Packets.Server;
using NUnit.Framework;
using Handler = Intersect.Editor.Networking.PacketHandler;

namespace Intersect.Tests.Editor;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public class MonsterNatureEditorTests
{
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Invoke(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, args);
    private static MonsterStat ChoiceStat(object item) => (MonsterStat)item.GetType().GetProperty("Stat")!.GetValue(item)!;
    private static void Select(ComboBox combo, MonsterStat stat) => combo.SelectedItem = combo.Items.Cast<object>().Single(item => ChoiceStat(item) == stat);
    private static IEnumerable<TreeNode> Nodes(TreeNodeCollection nodes) => nodes.Cast<TreeNode>().SelectMany(node => new[] { node }.Concat(Nodes(node.Nodes)));
    private static void SelectNature(FrmMonsterNature editor, Guid id)
    {
        var tree = Field<TreeView>(editor, "_natureList");
        tree.SelectedNode = Nodes(tree.Nodes).Single(node => node.Tag is Guid value && value == id);
        Invoke(editor, "SelectNature", id);
    }

    [Test]
    public void NatureFieldsPreviewFoldersCopyPasteUndoAndCancelPreserveSourceContent()
    {
        var source = new MonsterNatureDescriptor(Guid.NewGuid()) { Name = "First" };
        var target = new MonsterNatureDescriptor(Guid.NewGuid()) { Name = "Second" };
        MonsterNatureDescriptor.Lookup.Set(source.Id, source);
        MonsterNatureDescriptor.Lookup.Set(target.Id, target);
        var before = source.JsonData;
        var targetBefore = target.JsonData;
        try
        {
            using var editor = new FrmMonsterNature();
            editor.InitEditor();
            SelectNature(editor, source.Id);
            Assert.That(Field<TextBox>(editor, "_preview").ReadOnly, Is.True);
            Assert.That(Field<TextBox>(editor, "_preview").Text, Does.Contain("Neutral"));
            Assert.That(Field<NumericUpDown>(editor, "_selectionWeight").Value, Is.EqualTo(100));
            foreach (var name in new[] { "_increasedStat", "_decreasedStat" })
                Assert.That(Field<ComboBox>(editor, name).Items.Cast<object>().Select(ChoiceStat), Is.EqualTo(new[]
                {
                    MonsterStat.None, MonsterStat.Attack, MonsterStat.Defense, MonsterStat.SpecialAttack, MonsterStat.SpecialDefense, MonsterStat.Speed,
                }));
            foreach (var name in new[] { "_increasePercentage", "_decreasePercentage" })
            {
                var number = Field<NumericUpDown>(editor, name);
                Assert.That(number.Minimum, Is.EqualTo(0));
                Assert.That(number.Maximum, Is.EqualTo(50));
                Assert.That(number.Value, Is.EqualTo(10));
            }
            Field<TextBox>(editor, "_name").Text = "Fierce";
            Field<TextBox>(editor, "_description").Text = "Original custom nature";
            Field<TextBox>(editor, "_folder").Text = "Rare";
            Select(Field<ComboBox>(editor, "_increasedStat"), MonsterStat.Attack);
            Select(Field<ComboBox>(editor, "_decreasedStat"), MonsterStat.SpecialDefense);
            Field<NumericUpDown>(editor, "_increasePercentage").Value = 15m;
            Field<NumericUpDown>(editor, "_decreasePercentage").Value = 10m;
            Field<NumericUpDown>(editor, "_selectionWeight").Value = 5;
            editor.InitEditor();
            var draft = Field<MonsterNatureDescriptor>(editor, "_selected");
            Assert.That(draft.Folder, Is.EqualTo("Rare"));
            Assert.That(Field<TreeView>(editor, "_natureList").SelectedNode!.Parent!.Text, Is.EqualTo("Rare"));
            var preview = Field<TextBox>(editor, "_preview").Text;
            Assert.That(preview, Does.Contain("Nature: Fierce"));
            Assert.That(preview, Does.Contain("Attack: +15%"));
            Assert.That(preview, Does.Contain("Special Defense: -10%"));
            Assert.That(preview, Does.Contain("Selection Weight: 5"));
            Assert.That(source.JsonData, Is.EqualTo(before));
            Invoke(editor, "CopyNature", null, EventArgs.Empty);
            SelectNature(editor, target.Id);
            Invoke(editor, "PasteNature", null, EventArgs.Empty);
            var pasted = Field<MonsterNatureDescriptor>(editor, "_selected");
            Assert.That(pasted.Id, Is.EqualTo(target.Id));
            Assert.That(pasted.ModifiersJson, Is.EqualTo(draft.ModifiersJson));
            Assert.That(pasted.SelectionWeight, Is.EqualTo(5));
            Assert.That(pasted.Folder, Is.EqualTo("Rare"));
            Invoke(editor, "UndoNature", null, EventArgs.Empty);
            Assert.That(Field<MonsterNatureDescriptor>(editor, "_selected").JsonData, Is.EqualTo(targetBefore));
            editor.Close();
            Assert.That(source.JsonData, Is.EqualTo(before));
            Assert.That(target.JsonData, Is.EqualTo(targetBefore));
        }
        finally
        {
            MonsterNatureDescriptor.Lookup.DeleteAt(source.Id);
            MonsterNatureDescriptor.Lookup.DeleteAt(target.Id);
            Handler.GameObjectUpdatedDelegate = null!;
        }
    }

    [Test]
    public void PreviewExplainsConflictsAndExcludedWeightWithoutMutatingCanonicalNature()
    {
        var nature = new MonsterNatureDescriptor(Guid.NewGuid());
        MonsterNatureDescriptor.Lookup.Set(nature.Id, nature);
        var before = nature.JsonData;
        try
        {
            using var editor = new FrmMonsterNature();
            editor.InitEditor();
            Select(Field<ComboBox>(editor, "_increasedStat"), MonsterStat.Speed);
            Select(Field<ComboBox>(editor, "_decreasedStat"), MonsterStat.Speed);
            Field<NumericUpDown>(editor, "_selectionWeight").Value = 0;
            Assert.That(Field<MonsterNatureDescriptor>(editor, "_selected").IsValid, Is.False);
            Assert.That(Field<TextBox>(editor, "_preview").Text, Does.Contain("cannot increase and decrease the same stat"));
            Assert.That(Field<TextBox>(editor, "_preview").Text, Does.Contain("Excluded"));
            Select(Field<ComboBox>(editor, "_decreasedStat"), MonsterStat.None);
            Assert.That(Field<MonsterNatureDescriptor>(editor, "_selected").IsValid, Is.True);
            Assert.That(nature.JsonData, Is.EqualTo(before));
        }
        finally { MonsterNatureDescriptor.Lookup.DeleteAt(nature.Id); Handler.GameObjectUpdatedDelegate = null!; }
    }

    [Test]
    public void NatureContentPacketsRefreshCleanDraftsRetainLocalEditsAndHandleDeletion()
    {
        var nature = new MonsterNatureDescriptor(Guid.NewGuid());
        MonsterNatureDescriptor.Lookup.Set(nature.Id, nature);
        try
        {
            using var editor = new FrmMonsterNature();
            editor.InitEditor();
            var handler = (Handler)RuntimeHelpers.GetUninitializedObject(typeof(Handler));
            nature.SelectionWeight = 25;
            nature.Modifiers.Increase.Stat = MonsterStat.Defense;
            handler.HandlePacket(null!, new GameObjectPacket(nature.Id, nature.Type, nature.JsonData, false, false));
            Assert.That(Field<NumericUpDown>(editor, "_selectionWeight").Value, Is.EqualTo(25));
            Assert.That(Field<TextBox>(editor, "_preview").Text, Does.Contain("Defense: +10%"));
            Field<TextBox>(editor, "_name").Text = "Local work";
            nature.Name = "Remote edit";
            handler.HandlePacket(null!, new GameObjectPacket(nature.Id, nature.Type, nature.JsonData, false, false));
            Assert.That(Field<TextBox>(editor, "_name").Text, Is.EqualTo("Local work"));
            handler.HandlePacket(null!, new GameObjectPacket(nature.Id, nature.Type, string.Empty, true, false));
            Assert.That(MonsterNatureDescriptor.Get(nature.Id), Is.Null);
            Assert.That(Field<TableLayoutPanel>(editor, "_fields").Enabled, Is.False);
        }
        finally { MonsterNatureDescriptor.Lookup.DeleteAt(nature.Id); Handler.GameObjectUpdatedDelegate = null!; }
    }

    [Test]
    public void ScrollableNatureLayoutKeepsLabelsAndActionsReadableAtScaledViewportSizes()
    {
        foreach (var (width, height, scale) in new[] { (800, 560, 1f), (1280, 680, 1.25f), (1920, 1040, 1.5f) })
        {
            using var editor = new FrmMonsterNature();
            editor.Scale(new System.Drawing.SizeF(scale, scale));
            Invoke(editor, "ConstrainToWorkingArea", new System.Drawing.Rectangle(0, 0, width, height));
            editor.PerformLayout();
            var fields = Field<TableLayoutPanel>(editor, "_fields");
            fields.PerformLayout();
            Assert.That(editor.Right, Is.LessThanOrEqualTo(width));
            Assert.That(editor.Bottom, Is.LessThanOrEqualTo(height));
            Assert.That(fields.AutoScroll, Is.True);
            foreach (var label in fields.Controls.OfType<Label>().Where(label => fields.GetColumn(label) == 0))
                Assert.That(label.PreferredSize.Width, Is.LessThanOrEqualTo((int)fields.ColumnStyles[0].Width));
            foreach (var name in new[] { "_selectionWeight", "_increasePercentage", "_decreasePercentage" })
            {
                var number = Field<NumericUpDown>(editor, name);
                Assert.That(number.Width, Is.GreaterThanOrEqualTo(100));
                Assert.That(number.Height, Is.GreaterThanOrEqualTo(20));
            }
            foreach (var button in Descendants(editor).OfType<Button>().Where(button => button.Text is "Save" or "Cancel"))
            {
                var position = button.Parent!.PointToScreen(button.Location);
                var local = editor.PointToClient(position);
                Assert.That(local.Y + button.Height, Is.LessThanOrEqualTo(editor.ClientSize.Height));
                Assert.That(button.Width, Is.GreaterThanOrEqualTo(65));
            }
            Handler.GameObjectUpdatedDelegate = null!;
        }
    }
    private static IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Descendants(child)));
}
