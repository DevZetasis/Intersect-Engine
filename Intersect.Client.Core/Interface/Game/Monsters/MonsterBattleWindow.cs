using System.Diagnostics;
using Intersect.Client.Core;
using Intersect.Client.Framework.Content;
using Intersect.Client.Framework.File_Management;
using Intersect.Client.Framework.Graphics;
using Intersect.Client.Framework.Gwen;
using Intersect.Client.Framework.Gwen.Control;
using Intersect.Client.General;
using Intersect.Client.Localization;
using Intersect.Framework.Core.GameObjects.Animations;
using Intersect.Network.Packets.Client;
using Intersect.Network.Packets.Server;
using ClientNetwork = Intersect.Client.Networking.Network;

namespace Intersect.Client.Interface.Game.Monsters;

/// <summary>Classic battle composition with a separate clock-driven authoritative-event player.</summary>
public sealed class MonsterBattleWindow : Window
{
    private readonly IFont _font;
    private readonly Label _status, _playerName, _opponentName, _playerHPValue, _opponentHPValue, _experience, _playerMissing, _opponentMissing;
    private readonly Panel _commands, _mainMenu, _fightMenu, _playerInfo, _opponentInfo;
    private readonly MonsterBattleScene _scene;
    private readonly ImagePanel _background, _playerSprite, _opponentSprite, _lowerEffect, _upperEffect;
    private readonly ProgressBar _playerHP, _opponentHP, _xp;
    private readonly Button[] _moves = new Button[4];
    private readonly Button _fight, _bag, _monsters, _run, _back, _forfeit, _close, _advance, _skip;
    private readonly Action<MonsterBattleActionPacket> _send;
    private readonly MonsterBattlePlayback _playback = new();
    private MonsterBattleStatePacket? _seen;
    private MonsterBattleSprite? _playerArt, _opponentArt;
    private bool _inFight;
    private long _lastTick = Stopwatch.GetTimestamp();
    public MonsterBattleWindow(Canvas canvas, Action<MonsterBattleActionPacket>? send = null) : base(canvas, Strings.MonsterBattle.Title, false, "MonsterBattleWindowV2")
    {
        _send = send ?? (packet => { ClientNetwork.SendPacket(packet); });
        _font = canvas.Skin.DefaultFont ?? GameContentManager.Current.GetFont("sourcesansproblack") ?? throw new InvalidOperationException("Battle UI requires a font.");
        Size = new Point(940, 680); MinimumSize = new Point(600, 420);
        Alignment = [Alignments.Center]; RestrictToParent = true; IsResizable = false; IsClosable = false; IsHidden = true;
        InnerPanelPadding = new Padding(10); TitleLabel.Font = _font; TitleLabel.FontSize = 18;
        var footer = new Panel(this, "BattleFooter") { Dock = Pos.Bottom, Height = 42 };
        _forfeit = Button(footer, "ForfeitButton", Strings.MonsterBattle.Forfeit); _forfeit.Width = 110; _forfeit.Dock = Pos.Left;
        _close = Button(footer, "CloseBattleButton", Strings.MonsterBattle.Return); _close.Width = 160; _close.Dock = Pos.Left;
        _advance = Button(footer, "AdvanceButton", Strings.MonsterBattle.Next); _advance.Width = 90; _advance.Dock = Pos.Left;
        _skip = Button(footer, "SkipButton", Strings.MonsterBattle.Skip); _skip.Width = 150; _skip.Dock = Pos.Left;
        _forfeit.Clicked += (_, _) => Submit(-1, true);
        _close.Clicked += (_, _) => { if (_playback.Phase == MonsterBattlePresentationPhase.ShowingResult) Hide(); };
        _advance.Clicked += (_, _) => { _playback.Advance(); RefreshControls(); };
        _skip.Clicked += (_, _) => { _playback.Skip(); RefreshControls(); };
        _commands = new Panel(this, "BattleCommands") { Dock = Pos.Bottom, Height = 178 };
        _mainMenu = new Panel(_commands, "BattleMainMenu") { Dock = Pos.Fill };
        _fightMenu = new Panel(_commands, "BattleFightMenu") { Dock = Pos.Fill };
        _fight = Button(_mainMenu, "FightButton", Strings.MonsterBattle.Fight);
        _bag = Button(_mainMenu, "BagButton", Strings.MonsterBattle.Bag);
        _monsters = Button(_mainMenu, "MonstersBattleButton", Strings.MonsterBattle.Monsters);
        _run = Button(_mainMenu, "RunButton", Strings.MonsterBattle.Run);
        _fight.Clicked += (_, _) => { if (CanCommand) { _inFight = true; RefreshControls(); } };
        _back = Button(_fightMenu, "BackBattleButton", Strings.MonsterBattle.Back);
        _back.Clicked += (_, _) => { _inFight = false; RefreshControls(); };
        for (var i = 0; i < 4; i++)
        {
            var slot = i; _moves[i] = Button(_fightMenu, "BattleMove" + i, string.Empty);
            _moves[i].WrappingBehavior = WrappingBehavior.Wrapped;
            _moves[i].Clicked += (_, _) => Submit(slot, false);
        }
        _status = Text(this, "BattleStatus", 64); _status.Dock = Pos.Bottom;
        _scene = new MonsterBattleScene(this) { Dock = Pos.Fill };
        _background = new ImagePanel(_scene, "BattleBackground") { MouseInputEnabled = false };
        _playerSprite = new ImagePanel(_scene, "PlayerSprite") { MouseInputEnabled = false };
        _opponentSprite = new ImagePanel(_scene, "OpponentSprite") { MouseInputEnabled = false };
        _playerMissing = Text(_scene, "PlayerPlaceholder", 44); _playerMissing.Text = Strings.MonsterBattle.NoArt;
        _opponentMissing = Text(_scene, "OpponentPlaceholder", 44); _opponentMissing.Text = Strings.MonsterBattle.NoArt;
        _playerMissing.SetTextColor(new Color(255, 37, 48, 61), ComponentState.Normal);
        _opponentMissing.SetTextColor(new Color(255, 37, 48, 61), ComponentState.Normal);
        (_playerInfo, _playerName, _playerHP, _playerHPValue) = Info("Player");
        (_opponentInfo, _opponentName, _opponentHP, _opponentHPValue) = Info("Opponent");
        _xp = new ProgressBar(_playerInfo) { Name = "BattleExperience", AutoLabel = false, Height = 6, Dock = Pos.Top, IsDisabled = true, Value = 0 };
        _experience = Text(_playerInfo, "BattleExperienceLabel", 20); _experience.Dock = Pos.Top; _experience.Text = Strings.MonsterBattle.Experience;
        _lowerEffect = new ImagePanel(_scene, "BattleLowerEffect") { MouseInputEnabled = false };
        _upperEffect = new ImagePanel(_scene, "BattleUpperEffect") { MouseInputEnabled = false };
        _playback.EffectDuration = EventDuration;
        Interface.InputBlockingComponents.Add(this);
    }
    private bool CanCommand => _playback.Phase == MonsterBattlePresentationPhase.CommandSelection;
    private Label Text(Base parent, string name, int height) => new(parent, name)
    { AutoSizeToContents = false, Font = _font, FontSize = 14, Height = height, MinimumSize = new Point(0, height), WrappingBehavior = WrappingBehavior.Wrapped };
    private Button Button(Base parent, string name, string text) => new(parent, name)
    { Font = _font, FontSize = 14, AutoSizeToContents = false, Text = text, Height = 38, MinimumSize = new Point(0, 32), Margin = new Margin(2) };
    private (Panel, Label, ProgressBar, Label) Info(string name)
    {
        var panel = new Panel(_scene, name + "Panel") { Height = name == "Player" ? 126 : 102, ShouldDrawBackground = true, BackgroundColor = new Color(240, 37, 48, 61), Padding = new Padding(8) };
        var label = Text(panel, name + "Name", 42); label.Dock = Pos.Top; label.FontSize = 16;
        var value = Text(panel, name + "HPValue", 22); value.Dock = Pos.Top;
        var hp = new ProgressBar(panel) { Name = name + "HP", Dock = Pos.Top, Height = 16, MinimumSize = new Point(0, 16), Font = _font, FontSize = 14, AutoLabel = false };
        return (panel, label, hp, value);
    }
    protected override void EnsureInitialized()
    { if (Graphics.Renderer is { } renderer) LoadJsonUi(GameContentManager.UI.InGame, renderer.GetResolutionString()); }
    protected override void OnJsonReloaded()
    {
        base.OnJsonReloaded();
        // JSON controls geometry/style, never battle legality, phase, HP or rendered artwork.
        if (_playback.State is { } state) { LoadArt(state); RefreshControls(); }
    }
    protected override void Layout(Intersect.Client.Framework.Gwen.Skin.Base skin)
    {
        if (Canvas is { } canvas)
        {
            var w = Math.Max(320, canvas.Width - 32); var h = Math.Max(240, canvas.Height - 32);
            MinimumSize = new Point(Math.Min(600, w), Math.Min(420, h));
            SetSize(Math.Clamp(Width, MinimumSize.X, w), Math.Clamp(Height, MinimumSize.Y, h));
        }
        base.Layout(skin);
        LayoutMenus();
        _background.SetBounds(0, 0, _scene.Width, _scene.Height);
        if (_background.Texture is { Width: > 0, Height: > 0 } texture && _scene.Width > 0 && _scene.Height > 0)
        {
            var scale = Math.Max(_scene.Width / (double)texture.Width, _scene.Height / (double)texture.Height);
            var cropW = Math.Max(1, (int)(_scene.Width / scale)); var cropH = Math.Max(1, (int)(_scene.Height / scale));
            _background.SetTextureRect((texture.Width - cropW) / 2, (texture.Height - cropH) / 2, cropW, cropH);
        }
        var compact = _scene.Height < 260;
        var infoHeight = compact ? 90 : 126;
        _playerName.Height = _opponentName.Height = compact ? 30 : 42;
        _opponentInfo.SetBounds(8, 8, Math.Max(140, _scene.Width * 52 / 100), compact ? 90 : 102);
        _playerInfo.SetBounds(_scene.Width * 48 / 100 - 8, Math.Max(4, _scene.Height - infoHeight - 4), Math.Max(140, _scene.Width * 52 / 100), infoHeight);
        _xp.IsHidden = _experience.IsHidden = compact;
        PoseSprites();
    }
    private void LayoutMenus()
    {
        // Hidden docked panels are not laid out by Gwen. Size both views before changing visibility.
        _mainMenu.SetBounds(0, 0, _commands.Width, _commands.Height);
        _fightMenu.SetBounds(0, 0, _commands.Width, _commands.Height);
        Grid(_mainMenu, [_fight, _bag, _monsters, _run], 2, _commands.Height);
        Grid(_fightMenu, _moves, 2, Math.Max(96, _commands.Height - 34));
        _back.SetBounds(0, _commands.Height - 32, Math.Min(180, _commands.Width), 30);
    }
    private static void Grid(Panel parent, Button[] controls, int columns, int height)
    {
        for (var i = 0; i < controls.Length; i++) controls[i].SetBounds((i % columns) * parent.Width / columns + 3,
            (i / columns) * height / 2 + 2, Math.Max(1, parent.Width / columns - 6), Math.Max(32, height / 2 - 4));
    }
    public void Update()
    {
        var now = Stopwatch.GetTimestamp(); var elapsed = Math.Max(0, (now - _lastTick) / (double)Stopwatch.Frequency); _lastTick = now;
        var state = Globals.Me?.MonsterBattleState;
        if (state == null) { _seen = null; _playback.Reset(); _inFight = false; ClearArt(); Hide(); return; }
        if (!ReferenceEquals(state, _seen))
        {
            _seen = state;
            if (_playback.Accept(state)) { _inFight = false; LoadArt(state); Show(); elapsed = 0; }
        }
        _playback.Tick(Math.Min(elapsed, 1));
        RefreshControls();
    }
    private void LoadArt(MonsterBattleStatePacket state)
    {
        IGameTexture? Entity(string name) => Globals.ContentManager.GetTexture(TextureType.Entity, name);
        _playerArt = MonsterBattleArt.Resolve(state.Player, true, Entity); _opponentArt = MonsterBattleArt.Resolve(state.Opponent, false, Entity);
        void Set(ImagePanel panel, MonsterBattleSprite art) { panel.Texture = art.Texture; panel.SetTextureRect(art.Frame.X, art.Frame.Y, art.Frame.Width, art.Frame.Height); }
        Set(_playerSprite, _playerArt); Set(_opponentSprite, _opponentArt);
        _background.Texture = MonsterBattleArt.Background(state.Background, name => Globals.ContentManager.GetTexture(TextureType.Image, name));
        _background.ResetUVs();
        _playerSprite.SetToolTipText(_playerArt.Fallback ? Strings.MonsterBattle.RearFallback : state.Player.Name);
        _opponentSprite.SetToolTipText(state.Opponent.Name);
    }
    private void ClearArt()
    {
        _playerSprite.Texture = _opponentSprite.Texture = _background.Texture = _lowerEffect.Texture = _upperEffect.Texture = null;
        _playerArt = _opponentArt = null;
    }
    private static string Short(string text, int limit) => text.Length <= limit ? text : text[..Math.Max(1, limit - 3)] + "...";
    private void RefreshControls()
    {
        if (_playback.State is not { } state) return;
        _playerName.Text = Strings.MonsterBattle.Actor.ToString(Short(state.Player.Name, 60), state.Player.Level); _playerName.SetToolTipText(state.Player.Name);
        _opponentName.Text = Strings.MonsterBattle.Actor.ToString(Short(state.Opponent.Name, 60), state.Opponent.Level); _opponentName.SetToolTipText(state.Opponent.Name);
        HP(_playerHP, _playerHPValue, _playback.PlayerHP, state.Player.MaximumHP);
        HP(_opponentHP, _opponentHPValue, _playback.OpponentHP, state.Opponent.MaximumHP);
        var playing = _playback.Phase == MonsterBattlePresentationPhase.PlayingSequence;
        _status.Text = _playback.Phase switch
        {
            MonsterBattlePresentationPhase.AwaitingServer => Strings.MonsterBattle.Waiting,
            MonsterBattlePresentationPhase.ShowingResult => Strings.MonsterBattle.Result.ToString(state.Outcome),
            MonsterBattlePresentationPhase.CommandSelection => Strings.MonsterBattle.Turn.ToString(state.Turn),
            _ => Short(_playback.Text, 220),
        };
        _status.SetToolTipText(_playback.Text);
        LayoutMenus();
        _mainMenu.IsHidden = !CanCommand || _inFight; _fightMenu.IsHidden = !CanCommand || !_inFight;
        _fight.IsDisabled = !CanCommand || _inFight; _back.IsDisabled = !CanCommand || !_inFight;
        foreach (var button in new[] { _bag, _monsters, _run }) { button.IsDisabled = true; button.SetToolTipText(Strings.MonsterBattle.Unavailable); }
        for (var i = 0; i < 4; i++)
        {
            var move = state.Moves.FirstOrDefault(m => m.Slot == i);
            _moves[i].Text = move == null ? Strings.MonsterBattle.Empty : $"{Short(move.Name, 40)}\n{Short(move.Type, 20)} / {move.Category}\nPP {move.PP}/{move.MaximumPP}";
            _moves[i].SetToolTipText(move == null ? Strings.MonsterBattle.Empty : move.Name + " - " + move.Type + " / " + move.Category + " " + move.UnavailableReason);
            _moves[i].IsDisabled = !CanCommand || !_inFight || move?.Usable != true;
        }
        _forfeit.IsDisabled = !CanCommand; // Deliberately separate from Run; no escape calculation exists.
        _close.IsDisabled = _playback.Phase != MonsterBattlePresentationPhase.ShowingResult;
        _advance.IsDisabled = _skip.IsDisabled = !playing;
        IsClosable = !_close.IsDisabled;
        _xp.Value = 0; _xp.IsDisabled = true; _experience.Text = Strings.MonsterBattle.Experience;
        PoseSprites();
    }
    private static void HP(ProgressBar bar, Label label, double hp, long maximum)
    {
        bar.Value = maximum > 0 ? (float)Math.Clamp(hp / maximum, 0, 1) : 0;
        var value = hp <= 0 ? 0 : hp >= maximum ? maximum : (long)Math.Ceiling(hp);
        bar.Text = string.Empty;
        var low = bar.Value <= .2f; label.Text = $"HP {value} / {maximum}" + (low ? " - LOW HP" : "");
        bar.RenderColor = low ? new Color(255, 245, 90, 90) : bar.Value <= .5f ? new Color(255, 245, 207, 90) : new Color(255, 100, 225, 160);
    }
    private void PoseSprites()
    {
        if (_playback.State is not { } state) return;
        void Pose(ImagePanel panel, Label missing, MonsterBattleSprite? art, bool player)
        {
            var bounds = MonsterBattleArt.SpriteBounds(_scene.Width, _scene.Height, player, art?.Frame.Width ?? 80, art?.Frame.Height ?? 80);
            var actor = player ? state.Player : state.Opponent; var e = _playback.CurrentEvent; var p = _playback.Progress;
            var offset = e?.Kind == MonsterBattleEventKind.MoveUsed && e.ActorId == actor.Id ? Math.Sin(Math.PI * p) * 16 :
                e?.Kind == MonsterBattleEventKind.DamageApplied && e.TargetId == actor.Id && e.HPAfter < e.HPBefore ? -Math.Sin(Math.PI * p) * 7 : 0;
            var fainted = player ? _playback.PlayerFainted : _playback.OpponentFainted;
            var opacity = fainted ? 0 : e?.Kind == MonsterBattleEventKind.MonsterFainted && e.TargetId == actor.Id ? 1 - p : 1;
            bounds.X += (int)(offset * (player ? 1 : -1)); bounds.Y += (int)(offset * (player ? -.35 : .35));
            panel.SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            var hit = e?.Kind == MonsterBattleEventKind.DamageApplied && e.TargetId == actor.Id && e.HPAfter < e.HPBefore && (int)(p * 8) % 2 == 0;
            panel.RenderColor = new Color((int)(255 * opacity), 255, hit ? 120 : 255, hit ? 120 : 255);
            panel.IsHidden = art?.Texture == null || opacity <= 0;
            missing.SetBounds(bounds.X, bounds.Y + bounds.Height / 2, Math.Max(100, bounds.Width), 44);
            missing.IsHidden = art?.Texture != null || opacity <= 0;
        }
        Pose(_playerSprite, _playerMissing, _playerArt, true); Pose(_opponentSprite, _opponentMissing, _opponentArt, false);
        DrawEffect(_lowerEffect, false); DrawEffect(_upperEffect, true);
    }
    private double EventDuration(MonsterBattlePresentationEvent e)
    {
        if (e.Kind != MonsterBattleEventKind.DamageApplied || e.HPAfter >= e.HPBefore) return e.Kind switch { MonsterBattleEventKind.MoveUsed => .35, MonsterBattleEventKind.MonsterFainted => .55, _ => .45 };
        var animation = e.AnimationId is { } id ? AnimationDescriptor.Get(id) : null;
        return animation == null ? .5 : Math.Clamp(Math.Max(LayerSeconds(animation.Lower), LayerSeconds(animation.Upper)), .5, 2);
    }
    private static double LayerSeconds(AnimationLayer layer) => Math.Max(1, layer.FrameCount) * (double)Math.Max(1, layer.FrameSpeed) / 1000d;
    private void DrawEffect(ImagePanel image, bool upper)
    {
        image.IsHidden = true;
        if (_playback.CurrentEvent is not { Kind: MonsterBattleEventKind.DamageApplied } e || e.HPAfter >= e.HPBefore || e.AnimationId is not { } id) return;
        var layer = upper ? AnimationDescriptor.Get(id)?.Upper : AnimationDescriptor.Get(id)?.Lower;
        if (layer == null || string.IsNullOrWhiteSpace(layer.Sprite)) return;
        IGameTexture? texture;
        try { texture = Globals.ContentManager.GetTexture(TextureType.Animation, layer.Sprite); } catch { return; }
        if (texture == null || texture.IsMissingOrCorrupt) return;
        var columns = Math.Max(1, layer.XFrames); var rows = Math.Max(1, layer.YFrames);
        var frame = (int)(_playback.Elapsed * 1000 / Math.Max(1, layer.FrameSpeed));
        if (frame >= Math.Max(1, layer.FrameCount) || frame >= (long)columns * rows) return;
        var w = texture.Width / columns; var h = texture.Height / rows; if (w < 1 || h < 1) return;
        image.Texture = texture; image.SetTextureRect(frame % columns * w, frame / columns * h, w, h);
        var target = e.TargetId == _playback.State!.Player.Id ? _playerSprite : _opponentSprite;
        var scale = Math.Min(160d / w, 160d / h); var width = Math.Max(1, (int)(w * scale)); var height = Math.Max(1, (int)(h * scale));
        image.SetBounds(target.X + target.Width / 2 - width / 2, target.Y + target.Height / 2 - height / 2, width, height); image.IsHidden = false;
    }
    private void Submit(int slot, bool forfeit)
    {
        if (_playback.State is not { Outcome: MonsterBattleOutcome.Active } state || !CanCommand) return;
        if (!forfeit && (!_inFight || !state.Moves.Any(move => move.Slot == slot && move.Usable))) return;
        if (!_playback.BeginAwaiting()) return;
        RefreshControls();
        _send(new() { BattleId = state.BattleId, Turn = state.Turn, Revision = state.Revision, MoveSlot = slot, Forfeit = forfeit });
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { Interface.InputBlockingComponents.Remove(this); _playback.Reset(); ClearArt(); }
        base.Dispose(disposing);
    }

}
