using System.Reflection;
using System.Runtime.CompilerServices;
using Intersect.Client.Entities;
using Intersect.Client.Framework.Content;
using Intersect.Client.Framework.File_Management;
using Intersect.Client.Framework.Graphics;
using Intersect.Client.Framework.Gwen;
using Intersect.Client.Framework.Gwen.Control;
using Intersect.Client.General;
using Intersect.Client.Interface.Game.Monsters;
using Intersect.Core;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Network.Packets.Server;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Control = Intersect.Client.Framework.Gwen.Control.Base;

namespace Intersect.Tests.Client.Monsters;

[TestFixture, NonParallelizable]
public class MonsterRosterLayoutTests
{
    [TestCase(800, 600)]
    [TestCase(1024, 768)]
    [TestCase(1280, 720)]
    [TestCase(1920, 1080)]
    public void RosterHasReadableRowsAndControlsInsideViewport(int width, int height)
    {
        var optionsProperty = typeof(Options).GetProperty(nameof(Options.Instance))!;
        var previousOptions = optionsProperty.GetValue(null);
        optionsProperty.SetValue(null, new Options());
        var previousPlayer = Globals.Me;
        var previousGlobalContent = Globals.ContentManager;
        var species = new MonsterSpeciesDescriptor(Guid.NewGuid()) { Name = "Test Species", Sprite = "test.png" };
        MonsterSpeciesDescriptor.Lookup.Add(species);
        var previousContext = Intersect.Core.ApplicationContext.Context.Value;
        var currentField = typeof(GameContentManager).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousContent = currentField.GetValue(null);
        try
        {
            Intersect.Core.ApplicationContext.Context.Value = DispatchProxy.Create<IApplicationContext, QuietContext>();
            var content = new MemoryContent();
            Globals.ContentManager = content;
            using var skin = new LayoutSkin(new TextRenderer()) { DefaultFont = content.Font };
            using var canvas = new Canvas(skin);
            canvas.SetSize(width, height);
            var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            GC.SuppressFinalize(player); // This state-only test bypasses the entity/renderer constructor.
            player.Id = Guid.NewGuid();
            Globals.Me = player;
            player.MonsterRoster = new OwnedMonstersPacket
            {
                PlayerId = player.Id, PartyCapacity = 6,
                Monsters = [new() { Id = Guid.NewGuid(), SpeciesId = species.Id, Nickname = "Test Monster", Level = 12, Location = MonsterLocation.Party, Slot = 0 }],
            };
            var first = player.MonsterRoster.Monsters[0];
            player.MonsterRoster.Monsters = new[] { first }.Concat(Enumerable.Range(0, 19).Select(slot => new OwnedMonsterSummary
            {
                Id = Guid.NewGuid(), SpeciesId = species.Id, Level = 1, Location = MonsterLocation.Storage, Slot = slot,
            })).ToArray();
            var window = new MonsterRosterWindow(canvas);
            window.Show();
            window.Update();
            var layout = typeof(Control).GetMethod("RecurseLayout", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var queue = typeof(Control).GetMethod("InvokeThreadQueue", BindingFlags.Instance | BindingFlags.NonPublic)!;
            for (var i = 0; i < 8; i++) { queue.Invoke(canvas, null); layout.Invoke(canvas, [skin]); }
            Assert.That(window.Width, Is.InRange(640, width));
            Assert.That(window.Height, Is.InRange(360, height));
            var list = Descendants(window).OfType<ListBox>().Single();
            Assert.That(list.VerticalScrollBar.IsVisibleInTree, Is.True);
            Assert.That(list.InnerPanel.Height, Is.GreaterThan(list.Height));
            var row = list[0];
            Assert.That(row.Height, Is.GreaterThanOrEqualTo(32));
            for (var column = 0; column < 3; column++)
            {
                var cell = (Label)row.GetCellContents(column);
                Assert.That(cell.Font, Is.Not.Null);
                Assert.That(cell.FontSize, Is.GreaterThanOrEqualTo(14));
                Assert.That(cell.Width, Is.GreaterThan(45));
                Assert.That(cell.Height, Is.GreaterThanOrEqualTo(32));
            }
            Assert.That(((Label)row.GetCellContents(1)).Text, Is.EqualTo("Lv. 12"));
            Assert.That(((Label)row.GetCellContents(2)).Text, Is.EqualTo("Party #1"));
            foreach (var button in Descendants(window).OfType<Button>().Where(b => b.Name is "RefreshButton" or "MoveButton"))
            {
                Assert.That(button.Font, Is.Not.Null);
                Assert.That(button.FontSize, Is.GreaterThanOrEqualTo(14));
                Assert.That(button.Width, Is.GreaterThanOrEqualTo(130));
                Assert.That(button.Height, Is.GreaterThanOrEqualTo(36));
            }
            list.SelectedRow = row;
            for (var i = 0; i < 8; i++) { queue.Invoke(canvas, null); layout.Invoke(canvas, [skin]); }
            var sprite = Descendants(window).OfType<ImagePanel>().Single(p => p.Name == "MonsterSprite");
            Assert.That(sprite.Texture, Is.Not.Null);
            Assert.That(sprite.Width, Is.EqualTo(72));
            Assert.That(sprite.Height, Is.EqualTo(144));
            foreach (var control in Descendants(window).Where(c => c.Name is "SelectedName" or "SelectedSpecies" or "MonsterSprite" or "RefreshButton" or "MoveButton"))
            {
                var topLeft = control.ToCanvas(Point.Empty);
                var bottomRight = control.ToCanvas(control.Size);
                Assert.That(topLeft.X, Is.GreaterThanOrEqualTo(0), control.Name);
                Assert.That(topLeft.Y, Is.GreaterThanOrEqualTo(0), control.Name);
                Assert.That(bottomRight.X, Is.LessThanOrEqualTo(width), control.Name);
                Assert.That(bottomRight.Y, Is.LessThanOrEqualTo(height), control.Name);
                Assert.That(bottomRight.Y, Is.LessThanOrEqualTo(window.ToCanvas(window.Size).Y), control.Name);
            }
            Assert.That(Descendants(window).OfType<Button>().Single(b => b.Name == "MoveButton").Text, Is.EqualTo("Store in storage"));
            // A fresh server snapshot retains selection and updates its action label.
            var entry = player.MonsterRoster.Monsters[0];
            entry.Location = MonsterLocation.Storage;
            player.MonsterRoster = new OwnedMonstersPacket { PlayerId = player.Id, Monsters = [entry] };
            window.Update();
            Assert.That(list.SelectedRow?.UserData, Is.SameAs(entry));
            Assert.That(Descendants(window).OfType<Button>().Single(b => b.Name == "MoveButton").Text, Is.EqualTo("Add to party"));
        }
        finally
        {
            optionsProperty.SetValue(null, previousOptions);
            MonsterSpeciesDescriptor.Lookup.Delete(species);
            Globals.Me = previousPlayer;
            Globals.ContentManager = previousGlobalContent;
            currentField.SetValue(null, previousContent);
            Intersect.Core.ApplicationContext.Context.Value = previousContext;
        }
    }

    private static IEnumerable<Control> Descendants(Control control) =>
        control.Children.SelectMany(child => new[] { child }.Concat(Descendants(child)));

    public class QuietContext : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == "get_Logger"
            ? NullLogger.Instance : method?.ReturnType.IsValueType == true ? Activator.CreateInstance(method.ReturnType) : null;
    }

    public class TextureStub : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "get_Width" => 32 * Options.Instance.Sprites.NormalFrames,
            "get_Height" => 64 * Options.Instance.Sprites.Directions,
            "get_Name" => "test.png",
            _ => method?.ReturnType.IsValueType == true && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null,
        };
    }

    private sealed class FontStub : IFont
    {
        public string Name => "testfont";
        public ICollection<int> SupportedSizes => new[] { 14, 16, 18 };
        public int PickBestMatchFor(int size) => size;
        public int GetNextFontSize(int startSize, int direction, int limit = 0) => startSize + direction;
    }
    // Deterministic text metrics exercise real Gwen docking/table layout without a GPU or files.
    private sealed class TextRenderer : Intersect.Client.Framework.Gwen.Renderer.Base
    {
        public override Point MeasureText(IFont? font, int fontSize, string? text, float scale = 1) =>
            font == null ? Point.Empty : new Point((int)((text?.Length ?? 0) * fontSize * .55f * scale), (int)(fontSize * 1.4f * scale));
    }
    private sealed class LayoutSkin(Intersect.Client.Framework.Gwen.Renderer.Base renderer)
        : Intersect.Client.Framework.Gwen.Skin.Base(renderer);

    private sealed class MemoryContent : GameContentManager
    {
        public IFont Font { get; } = new FontStub();
        private readonly IGameTexture _texture = DispatchProxy.Create<IGameTexture, TextureStub>();
        public override IGameTexture? GetTexture(TextureType type, string name) => type == TextureType.Entity ? _texture : null;
        public override IFont? GetFont(string? name) => Font;
        public override bool GetLayout(UI stage, string name, string resolution, bool skipCache, Action<string, bool> layoutHandler) => false;
        public override void SaveUIJson(UI stage, string name, string json, string? resolution) { }
        protected override TAsset Load<TAsset>(Dictionary<string, IAsset> lookup, ContentType contentType, string assetName, Func<Stream> createStream) => throw new NotSupportedException();
        public override void LoadTexturePacks() { }
        public override void LoadTilesets(string[] tilesetnames) { }
        public override void LoadItems() { }
        public override void LoadEntities() { }
        public override void LoadSpells() { }
        public override void LoadAnimations() { }
        public override void LoadFaces() { }
        public override void LoadImages() { }
        public override void LoadFogs() { }
        public override void LoadResources() { }
        public override void LoadPaperdolls() { }
        public override void LoadGui() { }
        public override void LoadMisc() { }
        public override void LoadFonts() { }
        public override void LoadShaders() { }
        public override void LoadSounds() { }
        public override void LoadMusic() { }
    }
}

