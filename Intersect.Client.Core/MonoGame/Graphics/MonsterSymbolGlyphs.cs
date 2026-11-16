using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework.Graphics;
using AtlasColor = Microsoft.Xna.Framework.Color;
using AtlasRectangle = Microsoft.Xna.Framework.Rectangle;

namespace Intersect.Client.MonoGame.Graphics;

/// <summary>Checks the glyph artwork used for monster symbols, including placeholders baked into sprite fonts.</summary>
internal sealed class MonsterSymbolGlyphs(
    IReadOnlyDictionary<char, AtlasRectangle> glyphs,
    Func<AtlasRectangle, AtlasColor[]>? readPixels,
    Action<AtlasRectangle, AtlasColor[]>? writePixels)
{
    private const char Male = '\u2642';
    private const char Female = '\u2640';
    // U+2065 is unassigned. The bundled Source Sans atlases map it to their missing-glyph artwork.
    private const char Unassigned = '\u2065';
    private static readonly ConditionalWeakTable<SpriteFont, MonsterSymbolGlyphs> CoverageByFont = new();
    private readonly Dictionary<char, bool> _coverage = [];
    private AtlasColor[]? _missingGlyph;

    public static bool Supports(SpriteFont font, string symbol) =>
        symbol.All(CoverageByFont.GetValue(font, Create).Supports);

    private static MonsterSymbolGlyphs Create(SpriteFont font)
    {
        var bounds = font.GetGlyphs().ToDictionary(pair => pair.Key, pair => pair.Value.BoundsInTexture);
        if (font.Texture is not { Format: SurfaceFormat.Color } texture)
            return new(bounds, null, null);

        return new(bounds, region =>
        {
            var pixels = new AtlasColor[region.Width * region.Height];
            texture.GetData(0, region, pixels, 0, pixels.Length);
            return pixels;
        }, (region, pixels) => texture.SetData(0, region, pixels, 0, pixels.Length));
    }

    internal bool Supports(char character)
    {
        if (_coverage.TryGetValue(character, out var result)) return result;
        return _coverage[character] = Check(character);
    }

    private bool Check(char character)
    {
        if (!glyphs.TryGetValue(character, out var bounds)) return false;
        if (readPixels == null || char.IsWhiteSpace(character)) return true;

        if (!glyphs.TryGetValue(Unassigned, out var missing) && !glyphs.TryGetValue('\uFFFD', out missing))
            return true;

        if (missing.Width <= 0 || missing.Height <= 0 || bounds.Size != missing.Size) return true;
        _missingGlyph ??= readPixels(missing);
        if (!_missingGlyph.Any(pixel => pixel.A > 0) || !readPixels(bounds).AsSpan().SequenceEqual(_missingGlyph))
            return true;

        // Replace only a dedicated tile, keeping the font's texture allocation and glyph metrics.
        if (character is not (Male or Female) || writePixels == null ||
            glyphs.Any(pair => pair.Key != character && pair.Value.Intersects(bounds)))
            return false;

        writePixels(bounds, DrawSymbol(character, bounds.Width, bounds.Height));
        return true;
    }

    private static AtlasColor[] DrawSymbol(char character, int width, int height)
    {
        var male = character == Male;
        var center = male ? new Vector2(.37f, .63f) : new Vector2(.50f, .31f);
        const float radius = .23f;
        var corner = new Vector2(.87f, .13f);
        var stem = male ? center + Vector2.Normalize(corner - center) * radius : center + new Vector2(0, radius);
        var end = male ? corner : new Vector2(.50f, .91f);
        var barStart = male ? new Vector2(.59f, .13f) : new Vector2(.30f, .75f);
        var barEnd = male ? corner : new Vector2(.70f, .75f);
        const int samples = 4;
        var side = Math.Min(width, height);
        var left = (width - side) / 2f;
        var top = (height - side) / 2f;
        var halfStroke = Math.Max(.055f, .62f / side);
        var pixels = new AtlasColor[width * height];

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var coverage = 0;
            for (var sy = 0; sy < samples; sy++)
            for (var sx = 0; sx < samples; sx++)
            {
                var point = new Vector2(
                    (x + (sx + .5f) / samples - left) / side,
                    (y + (sy + .5f) / samples - top) / side);
                if (Math.Abs(Vector2.Distance(point, center) - radius) <= halfStroke ||
                    TouchesSegment(point, stem, end, halfStroke) ||
                    TouchesSegment(point, barStart, barEnd, halfStroke) ||
                    male && TouchesSegment(point, corner, new Vector2(.87f, .41f), halfStroke))
                    coverage++;
            }
            var alpha = (byte)(coverage * byte.MaxValue / (samples * samples));
            pixels[y * width + x] = new AtlasColor(alpha, alpha, alpha, alpha);
        }

        return pixels;
    }

    private static bool TouchesSegment(Vector2 point, Vector2 start, Vector2 end, float halfStroke)
    {
        var segment = end - start;
        var progress = Math.Clamp(Vector2.Dot(point - start, segment) / segment.LengthSquared(), 0f, 1f);
        return Vector2.DistanceSquared(point, start + progress * segment) <= halfStroke * halfStroke;
    }
}
