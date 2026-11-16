using Intersect.Client.Framework.Graphics;
using Intersect.Client.MonoGame.Graphics;
using Microsoft.Xna.Framework.Graphics;

namespace Intersect.Client.Interface.Game.Monsters;

internal static class MonsterGenderText
{
    internal static string Format(Guid? id, string? name, string? symbol, IFont? font, int size)
    {
        if (id == null) return "Unspecified";
        if (string.IsNullOrWhiteSpace(name)) return "Unavailable";
        if (string.IsNullOrWhiteSpace(symbol) || font is not Font<SpriteFont> platformFont) return name;
        var spriteFont = platformFont.GetRendererFor(size).PlatformObject;
        return MonsterSymbolGlyphs.Supports(spriteFont, symbol) ? $"{name} {symbol}" : name;
    }
}
