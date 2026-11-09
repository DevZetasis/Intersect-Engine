using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using Intersect.Editor.Forms.Controls;
using Intersect.Editor.Forms.Editors;
using Intersect.Framework.Core.GameObjects.Animations;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Network.Packets.Server;
using NUnit.Framework;
using Handler = Intersect.Editor.Networking.PacketHandler;

namespace Intersect.Tests.Editor;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public class MonsterMoveEditorTests
{
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Invoke(object owner, string name, params object?[] args) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, args);
    // PerformClick requires a visible form; invoke the real click event for hidden-control tests.
    private static void Click(Button button) => typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(button, [EventArgs.Empty]);
    private static void Select(ComboBox combo, Guid id) => combo.SelectedItem = combo.Items.Cast<object>()
        .Single(item => (Guid)item.GetType().GetProperty("Id")!.GetValue(item)! == id);

    [Test]
    public void MoveControlsEditAllPropertiesCopyUndoAndCancelWithoutChangingSource()
    {
        var type = new MonsterTypeDescriptor(Guid.NewGuid());
        var animation = new AnimationDescriptor(Guid.NewGuid());
        var source = new MonsterMoveDescriptor(Guid.NewGuid()) { TypeId = type.Id, Name = "First" };
        var target = new MonsterMoveDescriptor(Guid.NewGuid()) { TypeId = type.Id, Name = "Second" };
        MonsterTypeDescriptor.Lookup.Set(type.Id, type);
        AnimationDescriptor.Lookup.Set(animation.Id, animation);
        MonsterMoveDescriptor.Lookup.Set(source.Id, source);
        MonsterMoveDescriptor.Lookup.Set(target.Id, target);
        var before = source.JsonData;
        var targetBefore = target.JsonData;
        try
        {
            using var editor = new FrmMonsterMove();
            editor.InitEditor();
            Invoke(editor, "SelectMove", source.Id);
            editor.Size = editor.MinimumSize;
            editor.PerformLayout();
            var fields = Field<TableLayoutPanel>(editor, "_fields");
            fields.PerformLayout();
            foreach (var name in new[] { "_power", "_accuracy", "_priority" })
            {
                Assert.That(Field<NumericUpDown>(editor, name).Height, Is.GreaterThanOrEqualTo(20));
                Assert.That(Field<NumericUpDown>(editor, name).Width, Is.GreaterThanOrEqualTo(100));
            }
            Field<TextBox>(editor, "_name").Text = "Edited";
            Field<TextBox>(editor, "_description").Text = "Custom move description";
            Field<ComboBox>(editor, "_category").SelectedIndex = (int)MonsterMoveCategory.Special;
            Field<NumericUpDown>(editor, "_power").Value = 81;
            Field<NumericUpDown>(editor, "_accuracy").Value = 92;
            Field<NumericUpDown>(editor, "_priority").Value = -4;
            Field<CheckBox>(editor, "_alwaysHits").Checked = true;
            Field<ComboBox>(editor, "_target").SelectedIndex = (int)MonsterMoveTarget.AllBattlefield;
            Select(Field<ComboBox>(editor, "_animation"), animation.Id);
            Assert.That(Field<NumericUpDown>(editor, "_accuracy").Enabled, Is.False);
            var draft = Field<MonsterMoveDescriptor>(editor, "_selected");
            Assert.That((draft.Category, draft.BasePower, draft.Accuracy, draft.AlwaysHits, draft.Priority, draft.Target, draft.AnimationId),
                Is.EqualTo((MonsterMoveCategory.Special, 81, 92, true, -4, MonsterMoveTarget.AllBattlefield, (Guid?)animation.Id)));
            Assert.That(source.JsonData, Is.EqualTo(before));
            Invoke(editor, "CopyMove", null, EventArgs.Empty);
            Invoke(editor, "SelectMove", target.Id);
            Invoke(editor, "PasteMove", null, EventArgs.Empty);
            Assert.That(Field<MonsterMoveDescriptor>(editor, "_selected").Id, Is.EqualTo(target.Id));
            Assert.That(Field<NumericUpDown>(editor, "_power").Value, Is.EqualTo(81));
            Invoke(editor, "UndoMove", null, EventArgs.Empty);
            Assert.That(Field<MonsterMoveDescriptor>(editor, "_selected").JsonData, Is.EqualTo(targetBefore));
            Field<ComboBox>(editor, "_category").SelectedIndex = (int)MonsterMoveCategory.Status;
            Field<NumericUpDown>(editor, "_power").Value = 0;
            Assert.That(Field<MonsterMoveDescriptor>(editor, "_selected").BasePower, Is.Zero);
            Field<ComboBox>(editor, "_category").SelectedIndex = (int)MonsterMoveCategory.Physical;
            Assert.That(Field<NumericUpDown>(editor, "_power").Minimum, Is.EqualTo(1));
            Assert.That(Field<MonsterMoveDescriptor>(editor, "_selected").BasePower, Is.EqualTo(1));
            editor.Close();
            Assert.That(source.JsonData, Is.EqualTo(before));
            Assert.That(target.JsonData, Is.EqualTo(targetBefore));
        }
        finally
        {
            MonsterMoveDescriptor.Lookup.DeleteAt(source.Id);
            MonsterMoveDescriptor.Lookup.DeleteAt(target.Id);
            MonsterTypeDescriptor.Lookup.DeleteAt(type.Id);
            AnimationDescriptor.Lookup.DeleteAt(animation.Id);
            Handler.GameObjectUpdatedDelegate = null!;
        }
    }

    [Test]
    public void MoveContentPacketsRefreshCleanDraftsPreserveEditsAndHandleDeletion()
    {
        var type = new MonsterTypeDescriptor(Guid.NewGuid());
        var move = new MonsterMoveDescriptor(Guid.NewGuid()) { TypeId = type.Id };
        MonsterTypeDescriptor.Lookup.Set(type.Id, type);
        MonsterMoveDescriptor.Lookup.Set(move.Id, move);
        try
        {
            using var editor = new FrmMonsterMove();
            editor.InitEditor();
            var handler = (Handler)RuntimeHelpers.GetUninitializedObject(typeof(Handler));
            move.Priority = -3;
            handler.HandlePacket(null!, new GameObjectPacket(move.Id, move.Type, move.JsonData, false, false));
            Assert.That(Field<NumericUpDown>(editor, "_priority").Value, Is.EqualTo(-3));
            Field<TextBox>(editor, "_name").Text = "Local";
            move.Name = "Remote";
            handler.HandlePacket(null!, new GameObjectPacket(move.Id, move.Type, move.JsonData, false, false));
            Assert.That(Field<TextBox>(editor, "_name").Text, Is.EqualTo("Local"));
            handler.HandlePacket(null!, new GameObjectPacket(move.Id, move.Type, string.Empty, true, false));
            Assert.That(Field<TableLayoutPanel>(editor, "_fields").Enabled, Is.False);
        }
        finally
        {
            MonsterMoveDescriptor.Lookup.DeleteAt(move.Id);
            MonsterTypeDescriptor.Lookup.DeleteAt(type.Id);
            Handler.GameObjectUpdatedDelegate = null!;
        }
    }

    [Test]
    public void SpeciesLearnsetsAddEditRemoveSortCopyUndoAndCancelOnDrafts()
    {
        var type = new MonsterTypeDescriptor(Guid.NewGuid());
        var first = new MonsterMoveDescriptor(Guid.NewGuid()) { TypeId = type.Id, Name = "First" };
        var second = new MonsterMoveDescriptor(Guid.NewGuid()) { TypeId = type.Id, Name = "Second" };
        var source = new MonsterSpeciesDescriptor(Guid.NewGuid()) { Name = "First species" };
        var target = new MonsterSpeciesDescriptor(Guid.NewGuid()) { Name = "Second species" };
        MonsterTypeDescriptor.Lookup.Set(type.Id, type);
        MonsterMoveDescriptor.Lookup.Set(first.Id, first);
        MonsterMoveDescriptor.Lookup.Set(second.Id, second);
        MonsterSpeciesDescriptor.Lookup.Set(source.Id, source);
        MonsterSpeciesDescriptor.Lookup.Set(target.Id, target);
        try
        {
            using var editor = new FrmMonsterSpecies();
            editor.InitEditor();
            Invoke(editor, "SelectSpecies", source.Id);
            var control = Field<MonsterLearnsetEditor>(editor, "_learnset");
            var choice = Field<ComboBox>(control, "_move");
            var level = Field<NumericUpDown>(control, "_level");
            var rows = Field<ListBox>(control, "_rows");
            Select(choice, first.Id);
            level.Value = 8;
            Click(Field<Button>(control, "_add"));
            level.Value = 1;
            Select(choice, second.Id);
            Click(Field<Button>(control, "_add"));
            Select(choice, first.Id);
            Click(Field<Button>(control, "_add"));
            Assert.That(rows.Items.Cast<object>().Select(row => row.ToString()), Is.EqualTo(new[] { "1: Second", "1: First", "8: First" }));
            Assert.That(source.Learnset, Is.Empty);
            rows.SelectedIndex = 2;
            level.Value = 2;
            Assert.That(rows.Items[2]!.ToString(), Is.EqualTo("2: First"));
            Click(Field<Button>(control, "_remove"));
            Assert.That(Field<MonsterSpeciesDescriptor>(editor, "_selected").Learnset.Count, Is.EqualTo(2));
            Invoke(editor, "CopySpecies", null, EventArgs.Empty);
            Invoke(editor, "SelectSpecies", target.Id);
            Invoke(editor, "PasteSpecies", null, EventArgs.Empty);
            Assert.That(Field<MonsterSpeciesDescriptor>(editor, "_selected").Id, Is.EqualTo(target.Id));
            Assert.That(Field<MonsterSpeciesDescriptor>(editor, "_selected").Learnset.Count, Is.EqualTo(2));
            Invoke(editor, "UndoSpecies", null, EventArgs.Empty);
            Assert.That(Field<MonsterSpeciesDescriptor>(editor, "_selected").Learnset, Is.Empty);
            editor.Close();
            Assert.That(source.Learnset, Is.Empty);
            Assert.That(target.Learnset, Is.Empty);
        }
        finally
        {
            MonsterSpeciesDescriptor.Lookup.DeleteAt(source.Id);
            MonsterSpeciesDescriptor.Lookup.DeleteAt(target.Id);
            MonsterMoveDescriptor.Lookup.DeleteAt(first.Id);
            MonsterMoveDescriptor.Lookup.DeleteAt(second.Id);
            MonsterTypeDescriptor.Lookup.DeleteAt(type.Id);
            Handler.GameObjectUpdatedDelegate = null!;
        }
    }
}
