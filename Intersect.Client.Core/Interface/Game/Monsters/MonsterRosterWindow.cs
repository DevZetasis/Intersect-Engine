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
    private readonly ScrollControl _details;
    private readonly Panel _detailsContent;
    private readonly Label _identity;
    private readonly Label _nature;
    private readonly Label _totalEvs;
    private readonly Label _condition, _movePP;
    private readonly ProgressBar _conditionHP;
    private readonly Label _calculationStatus;
    private readonly List<Label[]> _statRows = [];
    private readonly List<Label> _detailLabels = [];
    private OwnedMonstersPacket? _displayed;

    public MonsterRosterWindow(Canvas canvas) : base(canvas, Strings.Monsters.Title, false, "MonsterRosterWindowV2")
    {
        _font = canvas.Skin.DefaultFont ?? GameContentManager.Current.GetFont("sourcesansproblack")
            ?? throw new InvalidOperationException("The monster roster requires a UI font.");
        Size = new Point(940, 680);
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

        _details = new ScrollControl(this, "MonsterDetails")
        {
            Dock = Pos.Right, Width = 410, Margin = new Margin(12, 0, 0, 0),
            OverflowX = OverflowBehavior.Hidden, OverflowY = OverflowBehavior.Auto,
        };
        _detailsContent = new Panel(_details, "DetailContent") { Dock = Pos.Top, Height = 690 };
        var preview = new Panel(_detailsContent, "MonsterPreview")
        {
            Dock = Pos.Top, Height = 96, Margin = new Margin(0, 0, 0, 12),
        };
        _sprite = new ImagePanel(preview, "MonsterSprite")
        {
            Size = new Point(96, 96), Alignment = [Alignments.Center],
        };
        _name = ReadableLabel(_detailsContent, "SelectedName", 32);
        _name.Dock = Pos.Top;
        _name.FontSize = 16;
        _name.WrappingBehavior = WrappingBehavior.Wrapped;
        _species = ReadableLabel(_detailsContent, "SelectedSpecies", 28);
        _species.Dock = Pos.Top;
        _species.WrappingBehavior = WrappingBehavior.Wrapped;

        _identity = DetailLabel("SelectedIdentity", 48);
        _nature = DetailLabel("SelectedNature", 28);
        _condition = DetailLabel("SelectedCondition", 28);
        _conditionHP = new ProgressBar(_detailsContent) { Name = "SelectedHPBar", Dock = Pos.Top, Height = 18, Font = _font, FontSize = 14, AutoLabel = false };
        _movePP = DetailLabel("SelectedMovePP", 100);
        _calculationStatus = DetailLabel("CalculationStatus", 0);
        AddStatRow("StatHeader", [Strings.Monsters.Stat, Strings.Monsters.Calculated, Strings.Monsters.IV, Strings.Monsters.EV]);
        var statNames = new string[] { Strings.Monsters.MaxHP, Strings.Monsters.Attack, Strings.Monsters.Defense,
            Strings.Monsters.SpecialAttack, Strings.Monsters.SpecialDefense, Strings.Monsters.Speed };
        for (var i = 0; i < statNames.Length; i++) AddStatRow("Stat" + i, [statNames[i], "—", "—", "—"]);
        _totalEvs = DetailLabel("TotalEvs", 32);
        _detailLabels.AddRange([_name, _species]);

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

    private Label DetailLabel(string name, int height)
    {
        var label = ReadableLabel(_detailsContent, name, height);
        label.Dock = Pos.Top;
        label.WrappingBehavior = WrappingBehavior.Wrapped;
        label.TextAlign = Pos.Left | Pos.Top;
        _detailLabels.Add(label);
        return label;
    }

    private void AddStatRow(string name, string[] values)
    {
        var panel = new Panel(_detailsContent, name) { Dock = Pos.Top, Height = 36 };
        var cells = values.Select((value, i) =>
        {
            var cell = ReadableLabel(panel, name + "Cell" + i, 36);
            cell.Dock = i == 3 ? Pos.Fill : Pos.Left;
            cell.Text = value;
            cell.SetToolTipText(value);
            return cell;
        }).ToArray();
        _statRows.Add(cells);
    }

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
        foreach (var label in new Label[] { _status, _refresh, _move, TitleLabel }.Concat(_detailLabels).Concat(_statRows.SelectMany(row => row)))
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
        _details.Width = Math.Clamp(Width / 2 - 20, 280, 430);
        foreach (var cells in _statRows)
        {
            var available = Math.Max(230, _details.Width - 24);
            cells[0].Width = available * 44 / 100;
            cells[1].Width = available * 24 / 100;
            cells[2].Width = available * 16 / 100;
        }
        foreach (var label in _detailLabels)
        {
            label.IsHidden = string.IsNullOrEmpty(label.Text);
            label.Height = label.IsHidden ? 0 : Math.Max(label.MinimumSize.Y, label.TextHeight + 8);
        }
        _detailsContent.Height = 126 + _detailLabels.Sum(label => label.Height) + 36 * _statRows.Count;
        base.Layout(skin);
        // ListBox's content-sized table can otherwise collapse while its scroll panel is laid out.
        var tableWidth = Math.Max(230, _list.Width - 24);
        var table = _list.Children.OfType<Table>().Single();
        table.MinimumSize = new Point(tableWidth, 0);
        _list.SetColumnWidth(0, tableWidth - 160);
        _list.SetColumnWidth(1, 60);
        _list.SetColumnWidth(2, 100);
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
        _identity.Text = string.Empty;
        _nature.Text = string.Empty;
        _condition.Text = _movePP.Text = string.Empty; _conditionHP.Value = 0; _conditionHP.Text = string.Empty; _conditionHP.IsHidden = true;
        _totalEvs.Text = string.Empty;
        _calculationStatus.Text = Strings.Monsters.SelectMonster;
        foreach (var cells in _statRows.Skip(1))
            for (var column = 1; column < 4; column++) cells[column].Text = "—";
        _move.Text = Strings.Monsters.Move;
        if (_list.SelectedRow?.UserData is not OwnedMonsterSummary monster) return;
        var species = MonsterSpeciesDescriptor.Get(monster.SpeciesId);
        var speciesName = species?.Name ?? Strings.Monsters.MissingSpecies.ToString(monster.SpeciesId);
        _name.Text = string.IsNullOrWhiteSpace(monster.Nickname) ? speciesName : monster.Nickname;
        _species.Text = speciesName;
        _name.SetToolTipText(_name.Text);
        _species.SetToolTipText(speciesName);
        _identity.Text = Strings.Monsters.Identity.ToString(monster.Level, monster.Experience,
            monster.Location == MonsterLocation.Party ? Strings.Monsters.Party : Strings.Monsters.Storage, monster.Slot + 1);
        var data = monster.Details;
        if (data?.ConditionInitialized == true)
        {
            _condition.Text = $"HP {data.CurrentHP} / {data.MaximumHP}" + (data.CurrentHP == 0 ? " - Fainted" : "");
            if (!string.IsNullOrEmpty(data.StatusName)) _condition.Text += $"  [{data.StatusName}]";
            _conditionHP.Value = data.MaximumHP > 0 ? (float)Math.Clamp(data.CurrentHP / (double)data.MaximumHP, 0, 1) : 0;
            _conditionHP.IsHidden = false;
            _movePP.Text = string.Join("\n", data.Moves.Select(m => m.MoveId == null ? $"{m.Slot + 1}. Empty slot" : $"{m.Slot + 1}. {m.Name}: PP {m.CurrentPP}/{m.MaximumPP}"));
        }
        else _condition.Text = "HP/PP unavailable";
        var natureName = data?.NatureId == null ? Strings.Monsters.Neutral.ToString()
            : string.IsNullOrWhiteSpace(data.NatureName) ? Strings.Monsters.MissingNature.ToString() : data.NatureName;
        _nature.Text = data == null ? string.Empty : Strings.Monsters.Nature.ToString(natureName);
        _nature.SetToolTipText(_nature.Text);
        _calculationStatus.Text = data == null ? Strings.Monsters.DetailsUnavailable
            : data.CalculatedStats is not { Length: 6 } ? Strings.Monsters.StatsUnavailable : string.Empty;
        for (var i = 0; i < 6; i++)
        {
            var cells = _statRows[i + 1];
            cells[1].Text = Value(data?.CalculatedStats, i);
            cells[2].Text = Value(data?.IVs, i);
            cells[3].Text = Value(data?.EVs, i);
            foreach (var cell in cells) cell.SetToolTipText(cell.Text);
        }
        _totalEvs.Text = data?.EVs is { Length: 6 } evs ? Strings.Monsters.TotalEvs.ToString(evs.Sum()) : string.Empty;
        _move.Text = monster.Location == MonsterLocation.Party ? Strings.Monsters.Store : Strings.Monsters.Retrieve;
        if (species == null || string.IsNullOrEmpty(species.Sprite)) return;
        var texture = Globals.ContentManager.GetTexture(Framework.Content.TextureType.Entity, species.Sprite);
        _sprite.Texture = texture;
        if (texture != null)
        {
            var frameWidth = Math.Max(1, texture.Width / Math.Max(1, Options.Instance.Sprites.NormalFrames));
            var frameHeight = Math.Max(1, texture.Height / Math.Max(1, Options.Instance.Sprites.Directions));
            _sprite.SetTextureRect(0, 0, frameWidth, frameHeight);
            var scale = 96f / Math.Max(frameWidth, frameHeight);
            _sprite.Size = new Point(Math.Max(1, (int)(frameWidth * scale)), Math.Max(1, (int)(frameHeight * scale)));
        }
    }

    private static string Value(long[]? values, int index) => values is { Length: 6 } ? values[index].ToString() : "—";

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
