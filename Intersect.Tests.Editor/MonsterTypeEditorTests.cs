using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using DarkUI.Controls;
using Intersect.Editor.Forms.Editors;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Network.Packets.Server;
using NUnit.Framework;
using Handler = Intersect.Editor.Networking.PacketHandler;

namespace Intersect.Tests.Editor;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public class MonsterTypeEditorTests
{
    private static T Field<T>(object editor, string name) => (T)editor.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
    private static void Invoke(object editor, string name, params object?[] args) =>
        editor.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, args);

    [Test]
    public void TypeFieldsCopyPasteUndoAndCancelLeaveSynchronizedContentUntouched()
    {
        var source = new MonsterTypeDescriptor(Guid.NewGuid()) { Name = "Alpha" };
        var target = new MonsterTypeDescriptor(Guid.NewGuid()) { Name = "Beta" };
        MonsterTypeDescriptor.Lookup.Set(source.Id, source);
        MonsterTypeDescriptor.Lookup.Set(target.Id, target);
        var sourceBefore = source.JsonData;
        var targetBefore = target.JsonData;
        try
        {
            using var editor = new FrmMonsterType();
            editor.InitEditor();
            Invoke(editor, "SelectType", source.Id);
            editor.Size = editor.MinimumSize;
            editor.PerformLayout();
            var effects = Field<TableLayoutPanel>(editor, "_effectiveness");
            effects.PerformLayout();
            foreach (var choice in Field<Dictionary<Guid, DarkComboBox>>(editor, "_multipliers").Values)
            {
                Assert.That(choice.Items.Count, Is.EqualTo(4));
                Assert.That(choice.SelectedIndex, Is.EqualTo(2));
                Assert.That(choice.Height, Is.GreaterThanOrEqualTo(20));
                Assert.That(choice.Width, Is.GreaterThanOrEqualTo(100));
            }
            Field<TextBox>(editor, "_name").Text = "Edited";
            Field<TextBox>(editor, "_description").Text = "Custom description";
            Field<TextBox>(editor, "_displayColor").Text = "#123456";
            Field<Dictionary<Guid, DarkComboBox>>(editor, "_multipliers")[target.Id].SelectedIndex = 1;
            var draft = Field<MonsterTypeDescriptor>(editor, "_selected");
            Assert.That(draft.Effectiveness[target.Id], Is.EqualTo(.5));
            Assert.That(draft.DisplayColor, Is.EqualTo("#123456"));
            Assert.That(source.JsonData, Is.EqualTo(sourceBefore));
            Invoke(editor, "CopyType", null, EventArgs.Empty);
            Invoke(editor, "SelectType", target.Id);
            Invoke(editor, "PasteType", null, EventArgs.Empty);
            var pasted = Field<MonsterTypeDescriptor>(editor, "_selected");
            Assert.That(pasted.Id, Is.EqualTo(target.Id));
            Assert.That(pasted.DisplayColor, Is.EqualTo("#123456"));
            Assert.That(pasted.Effectiveness[target.Id], Is.EqualTo(.5));
            Invoke(editor, "UndoType", null, EventArgs.Empty);
            Assert.That(Field<MonsterTypeDescriptor>(editor, "_selected").JsonData, Is.EqualTo(targetBefore));
            editor.Close();
            Assert.That(source.JsonData, Is.EqualTo(sourceBefore));
            Assert.That(target.JsonData, Is.EqualTo(targetBefore));
        }
        finally
        {
            MonsterTypeDescriptor.Lookup.DeleteAt(source.Id);
            MonsterTypeDescriptor.Lookup.DeleteAt(target.Id);
            Handler.GameObjectUpdatedDelegate = null!;
        }
    }

    [Test]
    public void TypeContentPacketsRefreshCleanDraftsRetainEditsAndHandleDeletion()
    {
        var type = new MonsterTypeDescriptor(Guid.NewGuid()) { Name = "Initial" };
        MonsterTypeDescriptor.Lookup.Set(type.Id, type);
        try
        {
            using var editor = new FrmMonsterType();
            editor.InitEditor();
            var handler = (Handler)RuntimeHelpers.GetUninitializedObject(typeof(Handler));
            type.DisplayColor = "#FEDCBA";
            handler.HandlePacket(null!, new GameObjectPacket(type.Id, type.Type, type.JsonData, false, false));
            Assert.That(Field<TextBox>(editor, "_displayColor").Text, Is.EqualTo("#FEDCBA"));
            Field<TextBox>(editor, "_name").Text = "Local edit";
            type.Name = "Remote edit";
            handler.HandlePacket(null!, new GameObjectPacket(type.Id, type.Type, type.JsonData, false, false));
            Assert.That(Field<TextBox>(editor, "_name").Text, Is.EqualTo("Local edit"));
            handler.HandlePacket(null!, new GameObjectPacket(type.Id, type.Type, string.Empty, true, false));
            Assert.That(MonsterTypeDescriptor.Get(type.Id), Is.Null);
            Assert.That(Field<TableLayoutPanel>(editor, "_fields").Enabled, Is.False);
        }
        finally { MonsterTypeDescriptor.Lookup.DeleteAt(type.Id); Handler.GameObjectUpdatedDelegate = null!; }
    }

    private static void Select(ComboBox combo, Guid id) => combo.SelectedItem = combo.Items.Cast<object>()
        .Single(item => (Guid)item.GetType().GetProperty("Id")!.GetValue(item)! == id);

    [Test]
    public void SpeciesTypeSelectorsRejectDuplicatesAndSupportCopyUndoAndCancel()
    {
        var first = new MonsterTypeDescriptor(Guid.NewGuid()) { Name = "First" };
        var second = new MonsterTypeDescriptor(Guid.NewGuid()) { Name = "Second" };
        var species = new MonsterSpeciesDescriptor(Guid.NewGuid()) { Name = "Typed" };
        var target = new MonsterSpeciesDescriptor(Guid.NewGuid()) { Name = "Untyped" };
        MonsterTypeDescriptor.Lookup.Set(first.Id, first);
        MonsterTypeDescriptor.Lookup.Set(second.Id, second);
        MonsterSpeciesDescriptor.Lookup.Set(species.Id, species);
        MonsterSpeciesDescriptor.Lookup.Set(target.Id, target);
        try
        {
            using var editor = new FrmMonsterSpecies();
            editor.InitEditor();
            Invoke(editor, "SelectSpecies", species.Id);
            Select(Field<ComboBox>(editor, "_primaryType"), first.Id);
            Select(Field<ComboBox>(editor, "_secondaryType"), first.Id);
            Assert.That(Field<MonsterSpeciesDescriptor>(editor, "_selected").TypeIds, Is.EqualTo(new[] { first.Id }));
            Select(Field<ComboBox>(editor, "_secondaryType"), second.Id);
            Assert.That(Field<MonsterSpeciesDescriptor>(editor, "_selected").TypeIds, Is.EqualTo(new[] { first.Id, second.Id }));
            Invoke(editor, "CopySpecies", null, EventArgs.Empty);
            Invoke(editor, "SelectSpecies", target.Id);
            Invoke(editor, "PasteSpecies", null, EventArgs.Empty);
            Assert.That(Field<MonsterSpeciesDescriptor>(editor, "_selected").Id, Is.EqualTo(target.Id));
            Assert.That(Field<MonsterSpeciesDescriptor>(editor, "_selected").TypeIds, Is.EqualTo(new[] { first.Id, second.Id }));
            Invoke(editor, "UndoSpecies", null, EventArgs.Empty);
            Assert.That(Field<MonsterSpeciesDescriptor>(editor, "_selected").TypeIds, Is.Empty);
            editor.Close();
            Assert.That(species.TypeIds, Is.Empty);
            Assert.That(target.TypeIds, Is.Empty);
        }
        finally
        {
            MonsterTypeDescriptor.Lookup.DeleteAt(first.Id);
            MonsterTypeDescriptor.Lookup.DeleteAt(second.Id);
            MonsterSpeciesDescriptor.Lookup.DeleteAt(species.Id);
            MonsterSpeciesDescriptor.Lookup.DeleteAt(target.Id);
            Handler.GameObjectUpdatedDelegate = null!;
        }
    }
}
