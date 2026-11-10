using Intersect.Client.Framework.Graphics;
using Intersect.Client.Framework.GenericClasses;
using Intersect.Network.Packets.Server;

namespace Intersect.Client.Interface.Game.Monsters;

public sealed record MonsterBattleSprite(IGameTexture? Texture, Rectangle Frame, bool Fallback);

public static class MonsterBattleArt
{
    private static IGameTexture? SafeTexture(Func<string, IGameTexture?> load, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try { return load(name) is { IsMissingOrCorrupt: false, Width: > 0, Height: > 0 } texture ? texture : null; }
        catch (Exception exception) { System.Diagnostics.Trace.TraceWarning("Battle artwork unavailable: {0}: {1}", name, exception.Message); return null; }
    }
    public static MonsterBattleSprite Resolve(MonsterBattleActorView actor, bool back, Func<string, IGameTexture?> load)
    {
        var dedicated = SafeTexture(load, back ? actor.BackBattleSprite : actor.FrontBattleSprite);
        if (dedicated != null) return new(dedicated, new(0, 0, dedicated.Width, dedicated.Height), false);
        var sheet = SafeTexture(load, actor.Sprite);
        if (sheet != null) return new(sheet, new(0, 0, Math.Max(1, sheet.Width / Math.Max(1, Options.Instance.Sprites.NormalFrames)),
            Math.Max(1, sheet.Height / Math.Max(1, Options.Instance.Sprites.Directions))), true);
        return new(null, new(0, 0, 1, 1), true);
    }
    public static IGameTexture? Background(string? configured, Func<string, IGameTexture?> load) =>
        SafeTexture(load, configured) ?? SafeTexture(load, "monster-battle-default.png");
    public static Rectangle SpriteBounds(int width, int height, bool player, int frameWidth, int frameHeight)
    {
        var area = player ? new Rectangle(width * 3 / 100, height * 43 / 100, width * 42 / 100, height * 53 / 100)
                          : new Rectangle(width * 59 / 100, height * 3 / 100, width * 38 / 100, height * 45 / 100);
        var scale = Math.Min(area.Width / (double)Math.Max(1, frameWidth), area.Height / (double)Math.Max(1, frameHeight));
        // Integer enlargement preserves sharp pixel boundaries. PointClamp is the existing renderer sampler.
        if (scale >= 1) scale = Math.Min(4, Math.Floor(scale));
        var w = Math.Max(1, (int)(frameWidth * scale)); var h = Math.Max(1, (int)(frameHeight * scale));
        return new(area.X + (area.Width - w) / 2, area.Y + area.Height - h, w, h);
    }
}
