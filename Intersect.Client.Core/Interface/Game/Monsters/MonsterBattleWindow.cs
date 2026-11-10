using Intersect.Client.Core;
using Intersect.Client.Framework.File_Management;
using Intersect.Client.Framework.Graphics;
using Intersect.Client.Framework.Gwen;
using Intersect.Client.Framework.Gwen.Control;
using Intersect.Client.Framework.Gwen.Control.Layout;
using Intersect.Client.General;
using Intersect.Client.Localization;
using Intersect.Network.Packets.Client;
using Intersect.Network.Packets.Server;
using ClientNetwork = Intersect.Client.Networking.Network;

namespace Intersect.Client.Interface.Game.Monsters;

/// <summary>Presentation of private server snapshots; contains no combat calculations.</summary>
public sealed class MonsterBattleWindow : Window
{
    private readonly IFont _font;
    private readonly Label _status, _playerName, _opponentName;
    private readonly ImagePanel _playerSprite, _opponentSprite;
    private readonly ProgressBar _playerHP, _opponentHP;
    private readonly Button[] _moves = new Button[4];
    private readonly Button _forfeit, _close;
    private readonly ListBox _log;
    private MonsterBattleStatePacket? _displayed;
    private bool _awaiting;
    private readonly Action<MonsterBattleActionPacket> _send;
    public MonsterBattleWindow(Canvas canvas, Action<MonsterBattleActionPacket>? send = null) : base(canvas, Strings.MonsterBattle.Title, false, "MonsterBattleWindowV1")
    {
        _send = send ?? (packet => { ClientNetwork.SendPacket(packet); });
        _font = canvas.Skin.DefaultFont ?? GameContentManager.Current.GetFont("sourcesansproblack") ?? throw new InvalidOperationException("Battle UI requires a font.");
        Size = new Point(720, 680); MinimumSize = new Point(560, 420);
        Alignment = [Alignments.Center]; RestrictToParent = true; IsResizable = false; IsClosable = false; IsHidden = true;
        InnerPanelPadding = new Padding(10); TitleLabel.Font = _font; TitleLabel.FontSize = 18;
        var footer = new Panel(this, "BattleFooter") { Dock = Pos.Bottom, Height = 44 };
        _forfeit = MakeButton(footer, "ForfeitButton", Strings.MonsterBattle.Forfeit, Pos.Left, 150, 36);
        _close = MakeButton(footer, "CloseBattleButton", Strings.MonsterBattle.Return, Pos.Left, 190, 36);
        _forfeit.Clicked += (_, _) => Submit(-1, true);
        _close.Clicked += (_, _) => { if (_displayed?.Outcome != MonsterBattleOutcome.Active) Hide(); };
        var scroll = new ScrollControl(this, "BattleScroll") { Dock = Pos.Fill, OverflowX = OverflowBehavior.Hidden, OverflowY = OverflowBehavior.Auto };
        var content = new Panel(scroll, "BattleContent") { Dock = Pos.Top, Height = 680 };
        _status = TextLabel(content, "BattleStatus", 48); _status.Dock = Pos.Top;
        (_opponentName, _opponentSprite, _opponentHP) = ActorPanel(content, "Opponent");
        (_playerName, _playerSprite, _playerHP) = ActorPanel(content, "Player");
        for (var i = 0; i < 4; i++)
        {
            var slot = i;
            _moves[i] = MakeButton(content, "BattleMove" + i, string.Empty, Pos.Top, 200, 52);
            _moves[i].WrappingBehavior = WrappingBehavior.Wrapped;
            _moves[i].Clicked += (_, _) => Submit(slot, false);
        }
        _log = new ListBox(content, "BattleLog") { Dock = Pos.Top, Height = 160, FontName = _font.Name, FontSize = 14 };
        _log.EnableScroll(false, true);
        var table = _log.Children.OfType<Table>().Single(); table.Dock = Pos.Top; table.AutoSizeToContentHeight = true;
        Interface.InputBlockingComponents.Add(this);
    }
    private Label TextLabel(Base parent, string name, int height) => new(parent, name)
    {
        AutoSizeToContents = false, Font = _font, FontSize = 14, Height = height,
        MinimumSize = new Point(0, height), WrappingBehavior = WrappingBehavior.Wrapped,
    };
    private Button MakeButton(Base parent, string name, string text, Pos dock, int width, int height) => new(parent, name)
    {
        Font = _font, FontSize = 14, AutoSizeToContents = false, Text = text, Dock = dock, Width = width, Height = height,
        MinimumSize = new Point(dock == Pos.Left ? width : 0, height), Margin = new Margin(0, 2, 8, 2),
    };
    private (Label, ImagePanel, ProgressBar) ActorPanel(Base parent, string name)
    {
        var panel = new Panel(parent, name + "Panel") { Dock = Pos.Top, Height = 108 };
        var portrait = new Panel(panel, name + "Portrait") { Dock = Pos.Left, Width = 88 };
        var sprite = new ImagePanel(portrait, name + "Sprite") { Size = new Point(80, 80), Alignment = [Alignments.Center] };
        var text = new Panel(panel, name + "Text") { Dock = Pos.Fill };
        var label = TextLabel(text, name + "Name", 56); label.Dock = Pos.Top; label.FontSize = 16;
        var hp = new ProgressBar(text) { Name = name + "HP", Dock = Pos.Top, Height = 28, MinimumSize = new Point(0, 28), Font = _font, FontSize = 14, AutoLabel = false };
        return (label, sprite, hp);
    }
    protected override void EnsureInitialized()
    {
        if (Graphics.Renderer is { } renderer) LoadJsonUi(GameContentManager.UI.InGame, renderer.GetResolutionString());
    }
    protected override void OnJsonReloaded()
    {
        base.OnJsonReloaded();
        // Layouts persist Disabled flags. Battle legality and labels come from the latest snapshot.
        if (_displayed is { } state) RenderControls(state);
    }
    protected override void Layout(Intersect.Client.Framework.Gwen.Skin.Base skin)
    {
        if (Canvas is { } canvas)
        {
            var width = Math.Max(320, canvas.Width - 32); var height = Math.Max(240, canvas.Height - 32);
            MinimumSize = new Point(Math.Min(560, width), Math.Min(420, height));
            SetSize(Math.Clamp(Width, MinimumSize.X, width), Math.Clamp(Height, MinimumSize.Y, height));
        }
        base.Layout(skin);
        var table = _log.Children.OfType<Table>().Single();
        table.MinimumSize = new Point(Math.Max(200, _log.Width - 24), 0);
        _log.SetColumnWidth(0, Math.Max(200, _log.Width - 24));
    }
    public void Update()
    {
        var state = Globals.Me?.MonsterBattleState;
        if (state == null) { _displayed = null; _log.Clear(); _awaiting = false; Hide(); return; }
        if (ReferenceEquals(state, _displayed)) return;
        var newBattle = _displayed?.BattleId != state.BattleId;
        if (newBattle) _log.Clear();
        _displayed = state; _awaiting = false;
        RenderControls(state);
        if (_log.RowCount + state.Messages.Length > 100) _log.Clear();
        foreach (var message in state.Messages.TakeLast(100))
        {
            var row = _log.AddRow(message); row.Height = 30; row.MinimumSize = new Point(0, 30);
            if (row.GetCellContents(0) is Label label) { label.Font = _font; label.FontSize = 14; label.SetToolTipText(message); }
        }
        Show(); // A new authoritative result reopens the window; closing an ended result does not loop.
    }
    private void RenderControls(MonsterBattleStatePacket state)
    {
        RenderActor(state.Player, _playerName, _playerSprite, _playerHP);
        RenderActor(state.Opponent, _opponentName, _opponentSprite, _opponentHP);
        _status.Text = _awaiting ? Strings.MonsterBattle.Waiting : state.Outcome == MonsterBattleOutcome.Active ? Strings.MonsterBattle.Turn.ToString(state.Turn) : Strings.MonsterBattle.Result.ToString(state.Outcome);
        for (var i = 0; i < 4; i++)
        {
            var move = state.Moves.FirstOrDefault(move => move.Slot == i);
            _moves[i].Text = move == null ? Strings.MonsterBattle.Empty : $"{move.Name} \u2014 {move.Type} / {move.Category} \u2014 PP {move.PP}/{move.MaximumPP}";
            _moves[i].SetToolTipText(move?.UnavailableReason ?? Strings.MonsterBattle.Empty);
            _moves[i].IsDisabled = _awaiting || state.Outcome != MonsterBattleOutcome.Active || move?.Usable != true;
        }
        _forfeit.IsDisabled = _awaiting || state.Outcome != MonsterBattleOutcome.Active;
        _close.IsDisabled = state.Outcome == MonsterBattleOutcome.Active;
        IsClosable = state.Outcome != MonsterBattleOutcome.Active;
    }
    private void RenderActor(MonsterBattleActorView actor, Label label, ImagePanel sprite, ProgressBar hp)
    {
        label.Text = Strings.MonsterBattle.Actor.ToString(actor.Name, actor.Level); label.SetToolTipText(label.Text);
        hp.Value = actor.MaximumHP > 0 ? (float)(actor.HP / (double)actor.MaximumHP) : 0;
        hp.Text = $"HP {actor.HP} / {actor.MaximumHP}";
        sprite.Texture = null;
        if (string.IsNullOrEmpty(actor.Sprite)) return;
        var texture = Globals.ContentManager.GetTexture(Framework.Content.TextureType.Entity, actor.Sprite);
        sprite.Texture = texture;
        if (texture == null) return;
        var width = Math.Max(1, texture.Width / Math.Max(1, Options.Instance.Sprites.NormalFrames));
        var height = Math.Max(1, texture.Height / Math.Max(1, Options.Instance.Sprites.Directions));
        sprite.SetTextureRect(0, 0, width, height);
        var scale = 80f / Math.Max(width, height);
        sprite.Size = new Point(Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
    }
    private void Submit(int slot, bool forfeit)
    {
        if (_awaiting || _displayed is not { Outcome: MonsterBattleOutcome.Active } state) return;
        if (!forfeit && !state.Moves.Any(move => move.Slot == slot && move.Usable)) return;
        _awaiting = true;
        foreach (var button in _moves) button.IsDisabled = true;
        _forfeit.IsDisabled = true;
        _status.Text = Strings.MonsterBattle.Waiting;
        _send(new MonsterBattleActionPacket { BattleId = state.BattleId, Turn = state.Turn, Revision = state.Revision, MoveSlot = slot, Forfeit = forfeit });
    }
}
