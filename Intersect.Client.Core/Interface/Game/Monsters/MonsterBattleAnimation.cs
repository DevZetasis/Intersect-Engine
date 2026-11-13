using Intersect.Client.Framework.Core.Sounds;
using Intersect.Client.Framework.Graphics;
using Intersect.Client.Framework.Gwen.Control;
using Intersect.Framework.Core.GameObjects.Animations;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Network.Packets.Server;

namespace Intersect.Client.Interface.Game.Monsters;

/// <summary>Adapts authored sprite-sheet frames and sound to a bounded Gwen presentation.</summary>
internal sealed class MonsterBattleAnimation(
    ImagePanel[] lowerPanels, ImagePanel[] upperPanels,
    Func<Guid, AnimationDescriptor?> findAnimation, Func<string, IGameTexture?> findTexture,
    Func<string, ISound?> playSound)
{
    internal const double MaximumSeconds = 4;
    private Layer? _lower;
    private Layer? _upper;
    private MonsterBattlePresentationEvent? _attack;
    private bool _completeSound;
    private ISound? _currentSound;
    private readonly List<ISound> _finishingSounds = [];

    internal double Duration => Math.Max(.35, Math.Max(_lower?.Duration ?? 0, _upper?.Duration ?? 0));

    internal bool HasAuthoredImpact(MonsterBattlePresentationEvent impact) =>
        _attack is { } attack && attack.ActorId == impact.ActorId &&
        attack.TargetId == impact.TargetId && attack.MoveId == impact.MoveId;

    internal void Begin(MonsterBattlePresentationEvent entry)
    {
        if (entry.Kind is not (MonsterBattleEventKind.MoveUsed or MonsterBattleEventKind.StatusApplied or
            MonsterBattleEventKind.StatusRemoved or MonsterBattleEventKind.StatusBlocked or MonsterBattleEventKind.StatusDamage)) return;
        _attack = null;
        if (entry.AnimationId is not { } id || findAnimation(id) is not { } animation) return;

        _lower = LoadLayer(animation.Lower);
        _upper = LoadLayer(animation.Upper);
        if (_lower != null || _upper != null) _attack = entry;
        _completeSound = animation.CompleteSound;
        if (!string.IsNullOrWhiteSpace(animation.Sound))
        {
            try { _currentSound = playSound(animation.Sound); }
            catch (Exception) { _currentSound = null; } // Missing or corrupt audio must not interrupt presentation.
        }
    }

    internal void End(bool completed)
    {
        _lower = _upper = null;
        ResetPanels();
        if (_currentSound is not { } sound) return;
        _currentSound = null;
        if (completed && _completeSound && sound.Loaded) _finishingSounds.Add(sound);
        else Stop(sound);
    }

    internal void Cancel()
    {
        End(false);
        _attack = null;
        foreach (var sound in _finishingSounds) Stop(sound);
        _finishingSounds.Clear();
    }

    private static void Stop(ISound sound)
    {
        try { sound.Stop(); }
        catch (Exception) { /* Audio teardown must not prevent battle cleanup. */ }
    }

    internal static ImagePanel[] CreatePanels(Base parent, string name) =>
        [new(parent, name) { MouseInputEnabled = false, IsHidden = true },
            new(parent, name + "Second") { MouseInputEnabled = false, IsHidden = true }];

    internal static (Guid First, Guid Second) Anchors(MonsterBattlePresentationEvent entry) => entry.AnimationPosition switch
    {
        BattleMoveAnimationPosition.Attacker => (entry.ActorId, Guid.Empty),
        BattleMoveAnimationPosition.Both when entry.ActorId != entry.TargetId => (entry.ActorId, entry.TargetId),
        _ => (entry.TargetId, Guid.Empty),
    };

    internal void Draw(double elapsed, Guid playerId, ImagePanel player, Guid opponentId, ImagePanel opponent)
    {
        if (_attack is not { } entry || (_lower == null && _upper == null)) return;
        var (first, second) = Anchors(entry);
        ImagePanel? Anchor(Guid id) => id == playerId ? player : id == opponentId ? opponent : null;
        DrawLayer(_lower, lowerPanels[0], Anchor(first), elapsed);
        DrawLayer(_upper, upperPanels[0], Anchor(first), elapsed);
        DrawLayer(_lower, lowerPanels[1], Anchor(second), elapsed);
        DrawLayer(_upper, upperPanels[1], Anchor(second), elapsed);
    }

    internal void ResetPanels()
    {
        foreach (var panel in lowerPanels) Hide(panel);
        foreach (var panel in upperPanels) Hide(panel);
    }

    private static void Hide(ImagePanel panel)
    {
        panel.IsHidden = true;
        panel.Texture = null;
    }

    private static void DrawLayer(Layer? layer, ImagePanel panel, ImagePanel? anchor, double elapsed)
    {
        if (layer == null || anchor == null || layer.FrameAt(elapsed) is not { } frame ||
            layer.Texture.IsMissingOrCorrupt)
        {
            Hide(panel);
            return;
        }

        panel.Texture = layer.Texture;
        panel.SetTextureRect(frame % layer.Columns * layer.Width, frame / layer.Columns * layer.Height, layer.Width, layer.Height);
        var scale = Math.Min(1, 160d / Math.Max(layer.Width, layer.Height));
        var width = Math.Max(1, (int)(scale * layer.Width));
        var height = Math.Max(1, (int)(scale * layer.Height));
        panel.SetBounds(anchor.X + (anchor.Width - width) / 2, anchor.Y + (anchor.Height - height) / 2, width, height);
        panel.IsHidden = false;
    }

    private Layer? LoadLayer(AnimationLayer? layer)
    {
        if (layer == null || string.IsNullOrWhiteSpace(layer.Sprite) ||
            layer.XFrames <= 0 || layer.YFrames <= 0 || layer.FrameCount <= 0 || layer.FrameSpeed <= 0) return null;

        try
        {
            var texture = findTexture(layer.Sprite);
            if (texture == null || texture.IsMissingOrCorrupt) return null;
            var width = texture.Width / layer.XFrames;
            var height = texture.Height / layer.YFrames;
            if (width <= 0 || height <= 0) return null;
            var count = (int)Math.Min(layer.FrameCount, (long)layer.XFrames * layer.YFrames);
            var passes = Math.Clamp(1L + layer.LoopCount, 1, 4);
            var secondsPerFrame = layer.FrameSpeed / 1000d;
            return new(texture, layer.XFrames, width, height, count, secondsPerFrame,
                Math.Min(MaximumSeconds, secondsPerFrame * count * passes));
        }
        catch (Exception) { return null; } // Resource load failures use the generic reaction.
    }

    private sealed record Layer(IGameTexture Texture, int Columns, int Width, int Height, int FrameCount,
        double SecondsPerFrame, double Duration)
    {
        public int? FrameAt(double elapsed) => elapsed < Duration
            ? (int)Math.Floor(elapsed / SecondsPerFrame) % FrameCount : null;
    }
}
