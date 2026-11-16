using System.Reflection;
using System.Runtime.CompilerServices;
using Intersect.Client.Core;
using Intersect.Client.Entities;
using Intersect.Client.Framework.File_Management;
using Intersect.Client.Framework.Graphics;
using Intersect.Client.Framework.Gwen.Control;
using Intersect.Client.General;
using Intersect.Client.Interface.Game;
using Intersect.Client.Interface.Game.Inventory;
using Intersect.Client.Items;
using Intersect.Client.MonoGame.Input;
using Intersect.Core;
using Intersect.Framework.Core.GameObjects.Items;
using Intersect.Tests.Client.Monsters;
using NUnit.Framework;
using ClientInterface = Intersect.Client.Interface.Interface;

namespace Intersect.Tests.Client.Inventory;

[TestFixture, NonParallelizable]
public sealed class ItemDescriptionWindowTests
{
    [Test]
    public void OpenInventoryAndHoverCaptureDeviceStacksAndAnOrdinaryItem()
    {
        WithInventory((inventory, player) =>
        {
            var entries = new[]
            {
                Create("Basic Capture Device", ItemType.MonsterCaptureDevice, 1m),
                Create("Improved Capture Device", ItemType.MonsterCaptureDevice, 1.5m),
                Create("Guaranteed Capture Device", ItemType.MonsterCaptureDevice, 1m, guaranteed: true),
                Create("Ordinary Item", ItemType.Currency, 1m),
            };

            try
            {
                for (var i = 0; i < entries.Length; i++)
                {
                    var item = entries[i];
                    ItemDescriptor.Lookup.Add(item);
                    player.Inventory[i] = new Item { ItemId = item.Id, Quantity = i < 3 ? 5 : 1 };
                }

                inventory.Show();
                // Provide the inventory slot viewport normally loaded from the skin.
                var slots = inventory.Children.OfType<ScrollControl>().Single();
                slots.SetSize(225, 275);
                slots.UpdateScrollBars();
                typeof(InventoryWindow).GetMethod("EnsureInitialized", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(inventory, null);
                inventory.Update();
                Assert.That(inventory.IsVisibleInTree, Is.True);
                for (var i = 0; i < entries.Length; i++)
                {
                    var slot = (InventoryItem)inventory.Items[i];
                    Assert.That(slot.Icon.Texture, Is.Not.Null);
                    var quantity = Labels(slot).Single(label => label.Name == "Quantity");
                    Assert.That(quantity.IsVisibleInParent, Is.EqualTo(i < 3));
                    if (i < 3) Assert.That(quantity.Text, Is.EqualTo("5"));

                    Hover(slot, "Icon_HoverEnter");
                    var tooltip = ClientInterface.GameUi.ItemDescriptionWindow!;
                    Assert.That(tooltip.IsVisibleInTree, Is.True);
                    Assert.That(Text(tooltip, "Title"), Is.EqualTo(entries[i].Name));
                    Assert.That(Text(tooltip, "Subtitle"), Is.EqualTo(i < 3 ? "Monster Capture Device" : "Currency"));
                    Assert.That(Descendants(tooltip).OfType<RichLabel>().Any(label => label.Text == entries[i].Description), Is.True);
                    Assert.That(Labels(tooltip).Any(label => label.Text == "Amount:"), Is.EqualTo(i < 3));
                    if (i < 3) Assert.That(Labels(tooltip).Any(label => label.Name == "Value" && label.Text == "5"), Is.True);
                    Assert.That(player.Inventory[i].Quantity, Is.EqualTo(i < 3 ? 5 : 1));

                    Hover(slot, "Icon_HoverLeave");
                    Assert.That(ClientInterface.GameUi.GameCanvas.Children, Does.Not.Contain(tooltip));
                }

                inventory.Hide();
                Assert.That(inventory.IsVisibleInTree, Is.False);
            }
            finally
            {
                foreach (var item in entries) ItemDescriptor.Lookup.DeleteAt(item.Id);
            }
        });
    }

    [Test]
    public void MissingTypeIconAndDescriptionStillAllowAConfiguredRarityAndItemName()
    {
        WithInventory((_, _) =>
        {
            Options.Instance.Items.RarityTiers.Add("Custom rarity");
            var item = Create("Unclassified item", (ItemType)999, 1m);
            item.Rarity = Options.Instance.Items.RarityTiers.Count - 1;
            item.Icon = "";
            item.Description = "";
            var tooltip = ClientInterface.GameUi.ItemDescriptionWindow!;
            tooltip.Show(item, 1, valueLabel: null!);
            Assert.That(Text(tooltip, "Title"), Is.EqualTo(item.Name));
            Assert.That(Text(tooltip, "Subtitle"), Is.Null.Or.Empty);
            Assert.That(Text(tooltip, "Description"), Is.EqualTo("Custom rarity"));
            Assert.That(Labels(tooltip).Any(label => label.Text == "Amount:"), Is.False);
        });
    }

    private static ItemDescriptor Create(string name, ItemType type, decimal multiplier, bool guaranteed = false) =>
        new(Guid.NewGuid())
        {
            Name = name, ItemType = type, Description = $"Description for {name}.", Icon = "test.png",
            Stackable = true, CaptureMultiplier = multiplier, GuaranteedCapture = guaranteed,
        };

    private static void Hover(InventoryItem slot, string method) =>
        typeof(InventoryItem).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(slot, [slot.Icon, EventArgs.Empty]);

    private static string? Text(Base parent, string name) =>
        Labels(parent.Children.Single(child => child.Name == "DescriptionWindowHeader"))
            .Single(label => label.Name == name).Text;

    private static IEnumerable<Label> Labels(Base parent) => Descendants(parent).OfType<Label>();

    private static IEnumerable<Base> Descendants(Base parent) =>
        parent.Children.SelectMany(child => new[] { child }.Concat(Descendants(child)));

    private static void WithInventory(Action<InventoryWindow, Player> test)
    {
        // Exercise the real Gwen window and inventory hover handlers without initializing desktop graphics.
        var options = typeof(Options).GetProperty(nameof(Options.Instance))!;
        var contentField = typeof(GameContentManager).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!;
        var interfaceField = typeof(ClientInterface).GetField("_uiInGame", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousOptions = options.GetValue(null);
        var previousContent = contentField.GetValue(null);
        var previousGlobalContent = Globals.ContentManager;
        var previousInterface = interfaceField.GetValue(null);
        var previousPlayer = Globals.Me;
        var previousInput = Globals.InputManager;
        var previousRenderer = Graphics.Renderer;
        var previousContext = ApplicationContext.Context.Value;
        try
        {
            options.SetValue(null, new Options());
            ApplicationContext.Context.Value = DispatchProxy.Create<IApplicationContext, MonsterRosterLayoutTests.QuietContext>();
            var content = new MonsterRosterLayoutTests.MemoryContent();
            Globals.ContentManager = content;
            using var skin = new MonsterRosterLayoutTests.LayoutSkin(new MonsterRosterLayoutTests.TextRenderer()) { DefaultFont = content.Font };
            using var canvas = new Canvas(skin);
            canvas.SetSize(1280, 720);

            var rendererType = typeof(Player).Assembly.GetType("Intersect.Client.MonoGame.Graphics.MonoRenderer", throwOnError: true)!;
            Graphics.Renderer = (GameRenderer)RuntimeHelpers.GetUninitializedObject(rendererType);
            typeof(GameRenderer).GetField("_whitePixel", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Graphics.Renderer, DispatchProxy.Create<IGameTexture, MonsterRosterLayoutTests.TextureStub>());
            Globals.InputManager = (MonoInput)RuntimeHelpers.GetUninitializedObject(typeof(MonoInput));

            var gameUi = (GameInterface)RuntimeHelpers.GetUninitializedObject(typeof(GameInterface));
            typeof(GameInterface).GetField("<GameCanvas>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(gameUi, canvas);
            interfaceField.SetValue(null, gameUi);

            var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            GC.SuppressFinalize(player);
            player.MyEquipment = [];
            player.ItemCooldowns = [];
            typeof(Entity).GetField("<Inventory>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(player, new Intersect.Client.Framework.Items.IItem[Options.Instance.Player.MaxInventory]);
            Globals.Me = player;

            var inventory = new InventoryWindow(canvas);
            test(inventory, player);
        }
        finally
        {
            options.SetValue(null, previousOptions);
            contentField.SetValue(null, previousContent);
            interfaceField.SetValue(null, previousInterface);
            Globals.ContentManager = previousGlobalContent;
            Globals.Me = previousPlayer;
            Globals.InputManager = previousInput;
            Graphics.Renderer = previousRenderer;
            ApplicationContext.Context.Value = previousContext;
        }
    }
}


