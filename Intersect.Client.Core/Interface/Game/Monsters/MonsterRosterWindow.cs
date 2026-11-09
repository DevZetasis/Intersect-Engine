using Intersect.Client.Core;
using Intersect.Client.Framework.File_Management;
using Intersect.Client.Framework.Gwen;
using Intersect.Client.Framework.Gwen.Control;
using Intersect.Client.Framework.Gwen.Control.Layout;
using Intersect.Client.General;
using Intersect.Client.Localization;
using Intersect.Client.Framework.Graphics;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Network.Packets.Client;
using Intersect.Network.Packets.Server;
using ClientNetwork = Intersect.Client.Networking.Network;

namespace Intersect.Client.Interface.Game.Monsters;

public sealed class MonsterRosterWindow : Window
{
    private readonly ListBox _list;
    private readonly Label _status;
    private readonly ImagePanel _sprite;
    private readonly Button _move;
    private readonly Button _refresh;
    private readonly Label _name;
    private readonly Label _species;
    private readonly IFont _font;
    private OwnedMonstersPacket? _displayed;

    public MonsterRosterWindow(Canvas canvas) : base(canvas, Strings.Monsters.Title, false, "MonsterRosterWindowV2")
    {
        _font = canvas.Skin.DefaultFont ?? GameContentManager.Current.GetFont("sourcesansproblack")
            ?? throw new InvalidOperationException("The monster roster requires a UI font.");
        Size = new Point(720, 460);
        MinimumSize = new Point(640, 420);
        Alignment = [Alignments.Center];
        RestrictToParent = true;
        InnerPanelPadding = new Padding(10);
        IsVisibleInTree = false;
        IsClosable = true;
        IsResizable = false;
        TitleLabel.Font = _font;
        TitleLabel.FontSize = 18;
        _status = ReadableLabel(this, "RosterStatus", 44);
        _status.Dock = Pos.Top;
        _status.Text = Strings.Monsters.Waiting;
        _status.WrappingBehavior = WrappingBehavior.Wrapped;

        var actions = new Panel(this, "RosterActions") { Dock = Pos.Bottom, Height = 44 };
        _refresh = new Button(actions, "RefreshButton")
        {
            Dock = Pos.Left, Width = 130, Height = 36, MinimumSize = new Point(130, 36),
            Margin = new Margin(0, 4, 10, 4), Font = _font, FontSize = 14, Text = Strings.Monsters.Refresh,
        };
        _refresh.Clicked += (_, _) => ClientNetwork.SendPacket(new RequestOwnedMonstersPacket());
        _move = new Button(actions, "MoveButton")
        {
            Dock = Pos.Left, Width = 190, Height = 36, MinimumSize = new Point(190, 36),
            Margin = new Margin(0, 4, 0, 4), Font = _font, FontSize = 14,
            Text = Strings.Monsters.Move, IsDisabled = true,
        };
        _move.Clicked += (_, _) => MoveSelected();

        var details = new Panel(this, "MonsterDetails")
        {
            Dock = Pos.Right, Width = 180, Margin = new Margin(12, 0, 0, 0),
        };
        var preview = new Panel(details, "MonsterPreview")
        {
            Dock = Pos.Top, Height = 144, Margin = new Margin(0, 0, 0, 12),
        };
        _sprite = new ImagePanel(preview, "MonsterSprite")
        {
            Size = new Point(144, 144), Alignment = [Alignments.Center],
        };
        _name = ReadableLabel(details, "SelectedName", 52);
        _name.Dock = Pos.Top;
        _name.FontSize = 16;
        _name.WrappingBehavior = WrappingBehavior.Wrapped;
        _species = ReadableLabel(details, "SelectedSpecies", 64);
        _species.Dock = Pos.Top;
        _species.WrappingBehavior = WrappingBehavior.Wrapped;

        _list = new ListBox(this, "OwnedMonsters")
        {
            Dock = Pos.Fill, FontName = _font.Name, FontSize = 14, ColumnCount = 3,
            CellSpacing = new Point(6, 4),
        };
        _list.EnableScroll(false, true);
        var table = _list.Children.OfType<Table>().Single();
        table.Dock = Pos.Top;
        table.AutoSizeToContentHeight = true;
        table.FitRowHeightToContents = false;
        _list.RowSelected += (_, _) => UpdateSelection();
    }

    private Label ReadableLabel(Base parent, string name, int height) => new(parent, name)
    {
        AutoSizeToContents = false, Height = height, MinimumSize = new Point(0, height),
        Font = _font, FontSize = 14, TextAlign = Pos.Left | Pos.CenterV,
    };

    protected override void EnsureInitialized()
    {
        if (Graphics.Renderer is { } renderer)
            LoadJsonUi(GameContentManager.UI.InGame, renderer.GetResolutionString());
        EnsureReadableFonts();
    }

    protected override void OnJsonReloaded()
    {
        base.OnJsonReloaded();
        EnsureReadableFonts();
    }

    private void EnsureReadableFonts()
    {
        // These controls must be measurable, not just drawable with a skin fallback.
        foreach (var label in new Label[] { _status, _refresh, _move, _name, _species, TitleLabel })
        {
            label.Font ??= _font;
            label.FontSize = Math.Max(14, label.FontSize);
        }
        _list.FontName = _font.Name;
        _list.FontSize = Math.Max(14, _list.FontSize);
    }

    protected override void Layout(Intersect.Client.Framework.Gwen.Skin.Base skin)
    {
        // Canvas dimensions are logical UI pixels; keep this window inside its viewport.
        if (Canvas is { } canvas)
        {
            var width = Math.Max(320, canvas.Width - 32);
            var height = Math.Max(240, canvas.Height - 32);
            MinimumSize = new Point(Math.Min(640, width), Math.Min(420, height));
            SetSize(Math.Clamp(Width, MinimumSize.X, width), Math.Clamp(Height, MinimumSize.Y, height));
        }
        base.Layout(skin);
        // ListBox's content-sized table can otherwise collapse while its scroll panel is laid out.
        var tableWidth = Math.Max(348, _list.Width - 24);
        var table = _list.Children.OfType<Table>().Single();
        table.MinimumSize = new Point(tableWidth, 0);
        _list.SetColumnWidth(0, tableWidth - 188);
        _list.SetColumnWidth(1, 60);
        _list.SetColumnWidth(2, 108);
    }

    public void RefreshFromServer()
    {
        ClientNetwork.SendPacket(new RequestOwnedMonstersPacket());
        Update();
    }

    public void Update()
    {
        if (!IsVisibleInTree) return;
        if (Globals.Me?.MonsterRoster is not { } roster || roster.PlayerId != Globals.Me.Id)
        {
            if (_displayed != null)
            {
                _displayed = null;
                _list.Clear();
                _status.Text = Strings.Monsters.Waiting;
                UpdateSelection();
            }
            return;
        }
        if (ReferenceEquals(_displayed, roster)) return;
        _displayed = roster;
        var selectedId = (_list.SelectedRow?.UserData as OwnedMonsterSummary)?.Id;
        _list.Clear();
        foreach (var monster in roster.Monsters)
        {
            var species = MonsterSpeciesDescriptor.Get(monster.SpeciesId);
            var speciesName = species?.Name ?? Strings.Monsters.MissingSpecies.ToString(monster.SpeciesId);
            var name = string.IsNullOrWhiteSpace(monster.Nickname) ? speciesName : monster.Nickname;
            var placement = Strings.Monsters.Placement.ToString(
                monster.Location == MonsterLocation.Party ? Strings.Monsters.Party : Strings.Monsters.Storage, monster.Slot + 1);
            var row = _list.AddRow(name, userData: monster);
            row.MinimumSize = new Point(0, 32);
            row.Height = 32;
            row.SetCellText(1, Strings.Monsters.Level.ToString(monster.Level));
            row.SetCellText(2, placement);
            row.SetToolTipText(Strings.Monsters.Entry.ToString(
                monster.Location == MonsterLocation.Party ? Strings.Monsters.Party : Strings.Monsters.Storage,
                monster.Slot + 1, name, speciesName, monster.Level));
            for (var column = 0; column < 3; column++)
            {
                if (row.GetCellContents(column) is not Label cell) continue;
                cell.Font = _font;
                cell.FontSize = 14;
                cell.MinimumSize = new Point(0, 32);
                cell.Padding = new Padding(4);
            }
            if (monster.Id == selectedId) _list.SelectedRow = row;
        }
        var partyCount = roster.Monsters.Count(monster => monster.Location == MonsterLocation.Party);
        _status.Text = string.IsNullOrEmpty(roster.Message)
            ? Strings.Monsters.Counts.ToString(partyCount, roster.PartyCapacity, roster.Monsters.Length - partyCount)
            : roster.Message;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        _move.IsDisabled = _list.SelectedRow?.UserData is not OwnedMonsterSummary;
        _sprite.Texture = null;
        _name.Text = string.Empty;
        _species.Text = string.Empty;
        _move.Text = Strings.Monsters.Move;
        if (_list.SelectedRow?.UserData is not OwnedMonsterSummary monster) return;
        var species = MonsterSpeciesDescriptor.Get(monster.SpeciesId);
        var speciesName = species?.Name ?? Strings.Monsters.MissingSpecies.ToString(monster.SpeciesId);
        _name.Text = string.IsNullOrWhiteSpace(monster.Nickname) ? speciesName : monster.Nickname;
        _species.Text = speciesName;
        _name.SetToolTipText(_name.Text);
        _species.SetToolTipText(speciesName);
        _move.Text = monster.Location == MonsterLocation.Party ? Strings.Monsters.Store : Strings.Monsters.Retrieve;
        if (species == null || string.IsNullOrEmpty(species.Sprite)) return;
        var texture = Globals.ContentManager.GetTexture(Framework.Content.TextureType.Entity, species.Sprite);
        _sprite.Texture = texture;
        if (texture != null)
        {
            var frameWidth = Math.Max(1, texture.Width / Math.Max(1, Options.Instance.Sprites.NormalFrames));
            var frameHeight = Math.Max(1, texture.Height / Math.Max(1, Options.Instance.Sprites.Directions));
            _sprite.SetTextureRect(0, 0, frameWidth, frameHeight);
            var scale = 144f / Math.Max(frameWidth, frameHeight);
            _sprite.Size = new Point(Math.Max(1, (int)(frameWidth * scale)), Math.Max(1, (int)(frameHeight * scale)));
        }
    }

    private void MoveSelected()
    {
        if (_list.SelectedRow?.UserData is not OwnedMonsterSummary monster) return;
        _move.IsDisabled = true;
        ClientNetwork.SendPacket(new MoveOwnedMonsterPacket
        {
            MonsterId = monster.Id, ExpectedRevision = monster.Revision,
            Destination = monster.Location == MonsterLocation.Party ? MonsterLocation.Storage : MonsterLocation.Party,
        });
    }
}
