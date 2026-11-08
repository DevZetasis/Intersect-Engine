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
            Field<TextBox>(editor, "_description").Text = "Unsaved Description";
            Field<NumericUpDown>(editor, "_hp").Value = 123;
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
            handler.HandlePacket(null!, new GameObjectPacket(species.Id, species.Type, species.JsonData, false, false));
            Assert.That(Field<TextBox>(editor, "_name").Text, Is.EqualTo("Updated From Server"));
            Assert.That(Field<NumericUpDown>(editor, "_speed").Value, Is.EqualTo(23));
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
}
