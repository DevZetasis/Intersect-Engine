using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using Intersect.Editor.Forms.Editors;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Network.Packets.Server;
using NUnit.Framework;
using EditorPacketHandler = Intersect.Editor.Networking.PacketHandler;

namespace Intersect.Tests.Editor;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public class MonsterSpeciesEditorTests
{
    private static T Field<T>(FrmMonsterSpecies editor, string name) where T : class =>
        (T)typeof(FrmMonsterSpecies).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;

    [Test]
    public void EditingFieldsAndCancelDoNotChangeSynchronizedSpecies()
    {
        var species = new MonsterSpeciesDescriptor(Guid.NewGuid()) { Name = "Editor Test" };
        MonsterSpeciesDescriptor.Lookup.Set(species.Id, species);
        var before = species.JsonData;
        try
        {
            using var editor = new FrmMonsterSpecies();
            editor.InitEditor();
            Field<TextBox>(editor, "_name").Text = "Unsaved Name";
            Assert.That(Field<TreeView>(editor, "_speciesList").SelectedNode!.Text, Is.EqualTo("Unsaved Name"));
            foreach (var name in new[] { "_frontBattleSprite", "_backBattleSprite", "_battleBackground" })
            {
                var combo = Field<ComboBox>(editor, name); combo.Items.Add("test-art.png"); combo.SelectedItem = "test-art.png";
            }
            Assert.That(Field<MonsterSpeciesDescriptor>(editor, "_selected").FrontBattleSprite, Is.EqualTo("test-art.png"));
            Assert.That(Field<MonsterSpeciesDescriptor>(editor, "_selected").BackBattleSprite, Is.EqualTo("test-art.png"));
            Assert.That(Field<MonsterSpeciesDescriptor>(editor, "_selected").BattleBackground, Is.EqualTo("test-art.png"));
            Field<TextBox>(editor, "_description").Text = "Unsaved Description";
            Field<NumericUpDown>(editor, "_hp").Value = 123;
            Field<NumericUpDown>(editor, "_specialAttack").Value = 456;
            Field<NumericUpDown>(editor, "_specialDefense").Value = 789;
            Assert.That(Field<MonsterSpeciesDescriptor>(editor, "_selected").BaseStats.SpecialAttack, Is.EqualTo(456));
            Assert.That(Field<MonsterSpeciesDescriptor>(editor, "_selected").BaseStats.SpecialDefense, Is.EqualTo(789));
            Assert.That(species.JsonData, Is.EqualTo(before));
            editor.Close();
            Assert.That(MonsterSpeciesDescriptor.Get(species.Id).JsonData, Is.EqualTo(before));
        }
        finally
        {
            MonsterSpeciesDescriptor.Lookup.DeleteAt(species.Id);
            EditorPacketHandler.GameObjectUpdatedDelegate = null!;
        }
    }

    [Test]
    public void ContentPacketsRefreshCleanFieldsAndRemoveDeletedSpecies()
    {
        var species = new MonsterSpeciesDescriptor(Guid.NewGuid()) { Name = "Original" };
        MonsterSpeciesDescriptor.Lookup.Set(species.Id, species);
        try
        {
            using var editor = new FrmMonsterSpecies();
            editor.InitEditor();
            // This handler overload only applies content packets; it needs no connection.
            var handler = (EditorPacketHandler)RuntimeHelpers.GetUninitializedObject(typeof(EditorPacketHandler));
            species.Name = "Updated From Server";
            species.BaseStats.Speed = 23;
            species.BaseStats.SpecialAttack = 34;
            species.BaseStats.SpecialDefense = 45;
            handler.HandlePacket(null!, new GameObjectPacket(species.Id, species.Type, species.JsonData, false, false));
            Assert.That(Field<TextBox>(editor, "_name").Text, Is.EqualTo("Updated From Server"));
            Assert.That(Field<NumericUpDown>(editor, "_speed").Value, Is.EqualTo(23));
            Assert.That(Field<NumericUpDown>(editor, "_specialAttack").Value, Is.EqualTo(34));
            Assert.That(Field<NumericUpDown>(editor, "_specialDefense").Value, Is.EqualTo(45));
            handler.HandlePacket(null!, new GameObjectPacket(species.Id, species.Type, string.Empty, true, false));
            Assert.That(MonsterSpeciesDescriptor.Get(species.Id), Is.Null);
            Assert.That(Field<TableLayoutPanel>(editor, "_fields").Enabled, Is.False);
        }
        finally
        {
            MonsterSpeciesDescriptor.Lookup.DeleteAt(species.Id);
            EditorPacketHandler.GameObjectUpdatedDelegate = null!;
        }
    }

    [Test]
    public void SixStatControlsCopyPasteAndUndoPreserveValuesAndDestinationIdentity()
    {
        var source = new MonsterSpeciesDescriptor(Guid.NewGuid()) { Name = "Source" };
        var target = new MonsterSpeciesDescriptor(Guid.NewGuid()) { Name = "Target" };
        MonsterSpeciesDescriptor.Lookup.Set(source.Id, source);
        MonsterSpeciesDescriptor.Lookup.Set(target.Id, target);
        var sourceJson = source.JsonData;
        var targetJson = target.JsonData;
        var fields = new[] { "_hp", "_attack", "_defense", "_specialAttack", "_specialDefense", "_speed" };
        var values = new[] { 71, 32, 43, 54, 65, 86 };
        try
        {
            using var editor = new FrmMonsterSpecies();
            editor.InitEditor();
            typeof(FrmMonsterSpecies).GetMethod("SelectSpecies", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(editor, [source.Id]);
            for (var index = 0; index < fields.Length; index++)
            {
                var input = Field<NumericUpDown>(editor, fields[index]);
                Assert.That(input.Minimum, Is.EqualTo(1));
                Assert.That(input.Maximum, Is.EqualTo(int.MaxValue));
                input.Value = values[index];
            }
            var selected = Field<MonsterSpeciesDescriptor>(editor, "_selected");
            selected.FrontBattleSprite = "front.png"; selected.BackBattleSprite = "back.png"; selected.BattleBackground = "scene.png";
            var draftJson = selected.BaseStatsJson;
            typeof(FrmMonsterSpecies).GetMethod("CopySpecies", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(editor, [null, EventArgs.Empty]);
            typeof(FrmMonsterSpecies).GetMethod("SelectSpecies", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(editor, [target.Id]);
            typeof(FrmMonsterSpecies).GetMethod("PasteSpecies", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(editor, [null, EventArgs.Empty]);
            var pasted = Field<MonsterSpeciesDescriptor>(editor, "_selected");
            Assert.That(pasted.Id, Is.EqualTo(target.Id));
            Assert.That(pasted.BaseStatsJson, Is.EqualTo(draftJson));
            Assert.That(pasted.FrontBattleSprite, Is.EqualTo("front.png")); Assert.That(pasted.BackBattleSprite, Is.EqualTo("back.png")); Assert.That(pasted.BattleBackground, Is.EqualTo("scene.png"));
            var table = Field<TableLayoutPanel>(editor, "_fields");
            editor.PerformLayout();
            table.PerformLayout();
            for (var index = 0; index < fields.Length; index++)
            {
                var input = Field<NumericUpDown>(editor, fields[index]);
                Assert.That(input.Value, Is.EqualTo(values[index]));
                Assert.That(table.GetRow(input), Is.EqualTo(9 + index));
                var label = table.GetControlFromPosition(0, 9 + index)!;
                Assert.That(label.PreferredSize.Width, Is.LessThanOrEqualTo((int)table.ColumnStyles[0].Width));
            }
            typeof(FrmMonsterSpecies).GetMethod("UndoSpecies", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(editor, [null, EventArgs.Empty]);
            Assert.That(Field<MonsterSpeciesDescriptor>(editor, "_selected").JsonData, Is.EqualTo(targetJson));
            foreach (var field in fields) Assert.That(Field<NumericUpDown>(editor, field).Value, Is.EqualTo(1));
            editor.Close();
            Assert.That(source.JsonData, Is.EqualTo(sourceJson));
            Assert.That(target.JsonData, Is.EqualTo(targetJson));
        }
        finally
        {
            MonsterSpeciesDescriptor.Lookup.DeleteAt(source.Id);
            MonsterSpeciesDescriptor.Lookup.DeleteAt(target.Id);
            EditorPacketHandler.GameObjectUpdatedDelegate = null!;
        }
    }
}
