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
    private readonly Panel _partyMenu, _partyRows;
    private readonly ScrollControl _partyScroll;
    private readonly Button _partyBack;
    private readonly MonsterBattleScene _scene;
    private readonly ImagePanel _background, _playerSprite, _opponentSprite;
    private readonly MonsterBattleAnimation _animation;
    private readonly ProgressBar _playerHP, _opponentHP, _xp;
    private readonly Button[] _moves = new Button[4];
    private readonly Button _fight, _bag, _monsters, _run, _back, _forfeit, _close, _advance, _skip;
    private readonly Action<MonsterBattleActionPacket> _send;
    private readonly MonsterBattlePlayback _playback = new();
    private MonsterBattleStatePacket? _seen;
    private MonsterBattleSprite? _playerArt, _opponentArt;
    private bool _inFight;
    private bool _inParty;
    private MonsterBattleStatePacket? _partySnapshot;
    private MonsterBattleActorView? _displayedArtActor;
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
        _forfeit.Clicked += (_, _) => Submit(MonsterBattleActionKind.Forfeit);
        _close.Clicked += (_, _) => { if (_playback.Phase == MonsterBattlePresentationPhase.ShowingResult) Hide(); };
        _advance.Clicked += (_, _) => { _playback.Advance(); RefreshControls(); };
        _skip.Clicked += (_, _) => { _playback.Skip(); RefreshControls(); };
        _commands = new Panel(this, "BattleCommands") { Dock = Pos.Bottom, Height = 178 };
        _mainMenu = new Panel(_commands, "BattleMainMenu") { Dock = Pos.Fill };
        _fightMenu = new Panel(_commands, "BattleFightMenu") { Dock = Pos.Fill };
        _partyMenu = new Panel(_commands, "BattlePartyMenu") { Dock = Pos.Fill, IsHidden = true };
        _partyBack = Button(_partyMenu, "BattlePartyBack", Strings.MonsterBattle.Back);
        _partyBack.Clicked += (_, _) => { if (CanCommand) { _inParty = false; RefreshControls(); } };
        _partyScroll = new ScrollControl(_partyMenu, "BattlePartyScroll") { OverflowX = OverflowBehavior.Hidden, OverflowY = OverflowBehavior.Auto };
        _partyRows = new Panel(_partyScroll, "BattlePartyRows") { Dock = Pos.Top };
        _fight = Button(_mainMenu, "FightButton", Strings.MonsterBattle.Fight);
        _bag = Button(_mainMenu, "BagButton", Strings.MonsterBattle.Bag);
        _monsters = Button(_mainMenu, "MonstersBattleButton", Strings.MonsterBattle.PartyMenu);
        _monsters.Clicked += (_, _) => { if (CanCommand) { _inParty = true; RefreshControls(); } };
        _run = Button(_mainMenu, "RunButton", Strings.MonsterBattle.Run);
        _fight.Clicked += (_, _) => { if (CanCommand) { _inFight = true; RefreshControls(); } };
        _back = Button(_fightMenu, "BackBattleButton", Strings.MonsterBattle.Back);
        _back.Clicked += (_, _) => { _inFight = false; RefreshControls(); };
        for (var i = 0; i < 4; i++)
        {
            var slot = i; _moves[i] = Button(_fightMenu, "BattleMove" + i, string.Empty);
            _moves[i].WrappingBehavior = WrappingBehavior.Wrapped;
            _moves[i].Clicked += (_, _) => Submit(MonsterBattleActionKind.Move, slot);
        }
        _status = Text(this, "BattleStatus", 64); _status.Dock = Pos.Bottom;
        _scene = new MonsterBattleScene(this) { Dock = Pos.Fill };
        _background = new ImagePanel(_scene, "BattleBackground") { MouseInputEnabled = false };
        var lowerEffects = MonsterBattleAnimation.CreatePanels(_scene, "BattleLowerEffect");
        _playerSprite = new ImagePanel(_scene, "PlayerSprite") { MouseInputEnabled = false };
        _opponentSprite = new ImagePanel(_scene, "OpponentSprite") { MouseInputEnabled = false };
        _playerMissing = Text(_scene, "PlayerPlaceholder", 44); _playerMissing.Text = Strings.MonsterBattle.NoArt;
        _opponentMissing = Text(_scene, "OpponentPlaceholder", 44); _opponentMissing.Text = Strings.MonsterBattle.NoArt;
        _playerMissing.SetTextColor(new Color(255, 37, 48, 61), ComponentState.Normal);
        _opponentMissing.SetTextColor(new Color(255, 37, 48, 61), ComponentState.Normal);
        var upperEffects = MonsterBattleAnimation.CreatePanels(_scene, "BattleUpperEffect");
        _animation = new(lowerEffects, upperEffects, AnimationDescriptor.Get,
            name => Globals.ContentManager?.GetTexture(TextureType.Animation, name),
            name => Audio.AddGameSound(name, loop: false));
        (_playerInfo, _playerName, _playerHP, _playerHPValue) = Info("Player");
        (_opponentInfo, _opponentName, _opponentHP, _opponentHPValue) = Info("Opponent");
        _xp = new ProgressBar(_playerInfo) { Name = "BattleExperience", AutoLabel = false, Height = 6, Dock = Pos.Top, IsDisabled = true, Value = 0 };
        _experience = Text(_playerInfo, "BattleExperienceLabel", 20); _experience.Dock = Pos.Top; _experience.Text = Strings.MonsterBattle.Experience;
        _playback.EffectDuration = entry => entry.Kind switch
        {
            MonsterBattleEventKind.MoveUsed => _animation.Duration,
            MonsterBattleEventKind.DamageApplied => .5,
            MonsterBattleEventKind.MonsterFainted => .55,
            _ => .45,
        };
        _playback.EventStarted = _animation.Begin;
        _playback.EventEnded = _animation.End;
        _playback.Interrupted = _animation.Cancel;
        Interface.InputBlockingComponents.Add(this);
    }
    private bool CanCommand => _playback.Phase == MonsterBattlePresentationPhase.CommandSelection;
    private bool MustReplace => _playback.Phase == MonsterBattlePresentationPhase.Replacement;
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
        _partySnapshot = null;
        _animation.ResetPanels();
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
        _partyMenu.SetBounds(0, 0, _commands.Width, _commands.Height);
        _partyScroll.SetBounds(0, 0, _commands.Width, Math.Max(1, _commands.Height - (MustReplace ? 0 : 34)));
        _partyBack.SetBounds(0, _commands.Height - 32, Math.Min(180, _commands.Width), 30);
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
        if (state == null) { _seen = null; _playback.Reset(); _inFight = _inParty = false; _partySnapshot = null; ClearArt(); Hide(); return; }
        if (!ReferenceEquals(state, _seen))
        {
            _seen = state;
            if (_playback.Accept(state)) { _inFight = _inParty = false; LoadArt(state); Show(); elapsed = 0; }
        }
        _playback.Tick(Math.Min(elapsed, 1));
        RefreshControls();
    }
    private void LoadArt(MonsterBattleStatePacket state)
    {
        IGameTexture? Entity(string name) => Globals.ContentManager.GetTexture(TextureType.Entity, name);
        _displayedArtActor = null;
        LoadPlayerArt();
        _opponentArt = MonsterBattleArt.Resolve(state.Opponent, false, Entity);
        void Set(ImagePanel panel, MonsterBattleSprite art) { panel.Texture = art.Texture; panel.SetTextureRect(art.Frame.X, art.Frame.Y, art.Frame.Width, art.Frame.Height); }
        Set(_opponentSprite, _opponentArt);
        _background.Texture = MonsterBattleArt.Background(state.Background, name => Globals.ContentManager.GetTexture(TextureType.Image, name));
        _background.ResetUVs();
        _opponentSprite.SetToolTipText(state.Opponent.Name);
    }
    private void LoadPlayerArt()
    {
        var actor = _playback.DisplayedPlayer;
        if (actor == null || ReferenceEquals(actor, _displayedArtActor)) return;
        _displayedArtActor = actor;
        _playerArt = MonsterBattleArt.Resolve(actor, true, name => Globals.ContentManager.GetTexture(TextureType.Entity, name));
        _playerSprite.Texture = _playerArt.Texture;
        var frame = _playerArt.Frame;
        _playerSprite.SetTextureRect(frame.X, frame.Y, frame.Width, frame.Height);
        _playerSprite.SetToolTipText(_playerArt.Fallback ? Strings.MonsterBattle.RearFallback : actor.Name);
    }

    private void RefreshParty(MonsterBattleStatePacket state)
    {
        if (ReferenceEquals(state, _partySnapshot)) return;
        _partySnapshot = state;
        foreach (var child in _partyRows.Children.ToArray()) _partyRows.RemoveChild(child, dispose: true);
        _partyRows.Height = state.Party.Length * 58;
        foreach (var member in state.Party.OrderBy(member => member.Slot))
        {
            var actor = member.Actor;
            var active = actor.Id == state.Player.Id;
            var fainted = actor.HP == 0;
            var row = new Panel(_partyRows, "BattlePartyMember" + member.Slot) { Dock = Pos.Top, Height = 58, Padding = new Padding(4) };
            if (active)
            {
                row.ShouldDrawBackground = true;
                row.BackgroundColor = new Color(240, 37, 48, 61);
            }
            var sprite = new ImagePanel(row, "BattlePartySprite") { Dock = Pos.Left, Width = 48, MouseInputEnabled = false };
            var art = MonsterBattleArt.Resolve(actor, false, name => Globals.ContentManager.GetTexture(TextureType.Entity, name));
            sprite.Texture = art.Texture;
            sprite.SetTextureRect(art.Frame.X, art.Frame.Y, art.Frame.Width, art.Frame.Height);
            var select = Button(row, "BattlePartySelect", fainted ? Strings.MonsterBattle.PartyFainted : active ? Strings.MonsterBattle.PartyActive : Strings.MonsterBattle.PartySwitch);
            select.Dock = Pos.Right;
            select.Width = 95;
            select.IsDisabled = active || fainted;
            select.SetToolTipText(fainted ? Strings.MonsterBattle.PartyHasFainted : active ? Strings.MonsterBattle.PartyAlreadyActive : Strings.MonsterBattle.PartySendOut);
            select.Clicked += (_, _) => Submit(MustReplace ? MonsterBattleActionKind.Replace : MonsterBattleActionKind.Switch, target: actor.Id);
            var info = new Panel(row, "BattlePartyInfo") { Dock = Pos.Fill, Padding = new Padding(6, 0, 6, 0) };
            var name = Text(info, "BattlePartyName", 20);
            name.Dock = Pos.Top;
            name.Text = Strings.MonsterBattle.PartyMember.ToString(member.Slot + 1, Short(actor.Name, 32), actor.Level);
            name.SetToolTipText(actor.Name);
            var value = Text(info, "BattlePartyHPValue", 18);
            value.Dock = Pos.Top;
            var bar = new ProgressBar(info) { Name = "BattlePartyHP", Dock = Pos.Top, Height = 10, AutoLabel = false };
            HP(bar, value, actor.HP, actor.MaximumHP);
        }
    }

    private void ClearArt()
    {
        _playerSprite.Texture = _opponentSprite.Texture = _background.Texture = null;
        _playerArt = _opponentArt = null;
    }
    private static string Short(string text, int limit) => text.Length <= limit ? text : text[..Math.Max(1, limit - 3)] + "...";
    private void RefreshControls()
    {
        if (_playback.State is not { } state) return;
        var player = _playback.DisplayedPlayer ?? state.Player;
        LoadPlayerArt();
        _playerName.Text = Strings.MonsterBattle.Actor.ToString(Short(player.Name, 60), player.Level);
        _playerName.SetToolTipText(player.Name);
        _opponentName.Text = Strings.MonsterBattle.Actor.ToString(Short(state.Opponent.Name, 60), state.Opponent.Level); _opponentName.SetToolTipText(state.Opponent.Name);
        HP(_playerHP, _playerHPValue, _playback.PlayerHP, player.MaximumHP);
        HP(_opponentHP, _opponentHPValue, _playback.OpponentHP, state.Opponent.MaximumHP);
        var playing = _playback.Phase == MonsterBattlePresentationPhase.PlayingSequence;
        _status.Text = _playback.Phase switch
        {
            MonsterBattlePresentationPhase.AwaitingServer => Strings.MonsterBattle.Waiting,
            MonsterBattlePresentationPhase.ShowingResult => Strings.MonsterBattle.Result.ToString(state.Outcome),
            MonsterBattlePresentationPhase.CommandSelection when state.IsCorrection => Short(_playback.Text, 220),
            MonsterBattlePresentationPhase.CommandSelection => Strings.MonsterBattle.Turn.ToString(state.Turn),
            MonsterBattlePresentationPhase.Replacement => state.IsCorrection ? Short(_playback.Text, 220) : Strings.MonsterBattle.Replacement,
            _ => Short(_playback.Text, 220),
        };
        _status.SetToolTipText(_playback.Text);
        LayoutMenus();
        _mainMenu.IsHidden = !CanCommand || _inFight || _inParty;
        _fightMenu.IsHidden = !CanCommand || !_inFight;
        _partyMenu.IsHidden = !MustReplace && (!CanCommand || !_inParty);
        _partyBack.IsHidden = MustReplace;
        if (!_partyMenu.IsHidden) RefreshParty(state);
        _monsters.IsDisabled = !CanCommand || state.Party.Length == 0;
        _fight.IsDisabled = !CanCommand || _inFight; _back.IsDisabled = !CanCommand || !_inFight;
        foreach (var button in new[] { _bag, _run }) { button.IsDisabled = true; button.SetToolTipText(Strings.MonsterBattle.Unavailable); }
        for (var i = 0; i < 4; i++)
        {
            var move = state.Moves.FirstOrDefault(m => m.Slot == i);
            _moves[i].Text = move == null ? Strings.MonsterBattle.Empty : $"{Short(move.Name, 40)}\n{Short(move.Type, 20)} / {move.Category}\nPP {move.PP}/{move.MaximumPP}";
            _moves[i].SetToolTipText(move == null ? Strings.MonsterBattle.Empty : move.Name + " - " + move.Type + " / " + move.Category + " " + move.UnavailableReason);
            _moves[i].IsDisabled = !CanCommand || !_inFight || move?.Usable != true;
        }
        _forfeit.IsDisabled = !CanCommand && !MustReplace; // Deliberately separate from Run; no escape calculation exists.
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
            var actor = player ? _playback.DisplayedPlayer ?? state.Player : state.Opponent; var e = _playback.CurrentEvent; var p = _playback.Progress;
            var offset = e?.Kind == MonsterBattleEventKind.MoveUsed && e.ActorId == actor.Id ? Math.Sin(Math.PI * Math.Clamp(_playback.Elapsed / .35, 0, 1)) * 16 :
                e?.Kind == MonsterBattleEventKind.DamageApplied && e.TargetId == actor.Id && e.HPAfter < e.HPBefore ? -Math.Sin(Math.PI * p) * 7 : 0;
            var fainted = player ? _playback.PlayerFainted : _playback.OpponentFainted;
            var opacity = fainted ? 0 : e?.Kind == MonsterBattleEventKind.MonsterFainted && e.TargetId == actor.Id ? 1 - p : 1;
            if (player && e?.Kind == MonsterBattleEventKind.Withdraw) opacity = 1 - p;
            if (player && e?.Kind == MonsterBattleEventKind.SendOut) opacity = p;
            bounds.X += (int)(offset * (player ? 1 : -1)); bounds.Y += (int)(offset * (player ? -.35 : .35));
            panel.SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            var hit = e?.Kind == MonsterBattleEventKind.DamageApplied && e.TargetId == actor.Id && e.HPAfter < e.HPBefore && !_animation.HasAuthoredImpact(e) && (int)(p * 8) % 2 == 0;
            panel.RenderColor = new Color((int)(255 * opacity), 255, hit ? 120 : 255, hit ? 120 : 255);
            panel.IsHidden = art?.Texture == null || opacity <= 0;
            missing.SetBounds(bounds.X, bounds.Y + bounds.Height / 2, Math.Max(100, bounds.Width), 44);
            missing.IsHidden = art?.Texture != null || opacity <= 0;
        }
        Pose(_playerSprite, _playerMissing, _playerArt, true); Pose(_opponentSprite, _opponentMissing, _opponentArt, false);
        _animation.Draw(_playback.Elapsed, _playback.DisplayedPlayer?.Id ?? state.Player.Id, _playerSprite,
            state.Opponent.Id, _opponentSprite);
    }

    private void Submit(MonsterBattleActionKind kind, int slot = -1, Guid target = default)
    {
        if (_playback.State is not { Outcome: MonsterBattleOutcome.Active } state || (!CanCommand && !MustReplace)) return;
        if (kind == MonsterBattleActionKind.Move && (!CanCommand || !_inFight || !state.Moves.Any(move => move.Slot == slot && move.Usable))) return;
        if (kind is MonsterBattleActionKind.Switch or MonsterBattleActionKind.Replace &&
            (_partyMenu.IsHidden || !state.Party.Any(member => member.Actor.Id == target && target != state.Player.Id && member.Actor.HP > 0))) return;
        if (!_playback.BeginAwaiting()) return;
        RefreshControls();
        _send(new() { BattleId = state.BattleId, Turn = state.Turn, Revision = state.Revision, MoveSlot = slot, Kind = kind, MonsterId = target });
    }
    public override void Hide()
    {
        _animation?.Cancel();
        base.Hide();
    }

    protected override void OnClose(Base sender, EventArgs args)
    {
        _animation.Cancel();
        base.OnClose(sender, args);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { Interface.InputBlockingComponents.Remove(this); _playback.Reset(); ClearArt(); }
        base.Dispose(disposing);
    }

}
