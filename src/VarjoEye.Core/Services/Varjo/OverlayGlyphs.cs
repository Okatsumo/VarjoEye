namespace VarjoEye.Core.Services.Varjo;

internal static class OverlayGlyphs
{
    private const int GlyphWidth = 5;
    private const int GlyphHeight = 7;

    public static void DrawCentered(
        byte[] pixels,
        string text,
        int originX,
        int originY,
        int width,
        int height,
        int y,
        int scale,
        byte r,
        byte g,
        byte b,
        byte a)
    {
        var glyphWidth = (GlyphWidth + 1) * scale;
        var total = text.Length * glyphWidth - scale;
        var x = originX + Math.Max(0, (width - total) / 2);
        foreach (var character in text.ToUpperInvariant())
        {
            DrawGlyph(pixels, character, x, originY + y, originX, originY, width, height, scale, r, g, b, a);
            x += glyphWidth;
        }
    }

    private static void DrawGlyph(
        byte[] pixels,
        char character,
        int left,
        int top,
        int originX,
        int originY,
        int width,
        int height,
        int scale,
        byte r,
        byte g,
        byte b,
        byte a)
    {
        if (!Patterns.TryGetValue(character, out var pattern))
        {
            return;
        }

        for (var gy = 0; gy < GlyphHeight; gy++)
        {
            var row = (int)((pattern >> ((GlyphHeight - 1 - gy) * GlyphWidth)) & 0x1F);
            for (var gx = 0; gx < GlyphWidth; gx++)
            {
                if ((row & (1 << (GlyphWidth - 1 - gx))) == 0)
                {
                    continue;
                }

                for (var py = 0; py < scale; py++)
                {
                    var y = top + gy * scale + py;
                    if (y < originY || y >= originY + height)
                    {
                        continue;
                    }

                    for (var px = 0; px < scale; px++)
                    {
                        var x = left + gx * scale + px;
                        if (x < originX || x >= originX + width)
                        {
                            continue;
                        }

                        CalibrationOverlay.BlendPixel(pixels, y * 1024 + x, r, g, b, a);
                    }
                }
            }
        }
    }

    private static readonly Dictionary<char, long> Patterns = new()
    {
        ['A'] = 0b01110_10001_10001_11111_10001_10001_10001,
        ['J'] = 0b00111_00001_00001_00001_00001_10001_01110,
        ['Q'] = 0b01110_10001_10001_10001_10101_10010_01101,
        ['X'] = 0b10001_10001_01010_00100_01010_10001_10001,
        ['Z'] = 0b11111_00001_00010_00100_01000_10000_11111,
        ['B'] = 0b11110_10001_10001_11110_10001_10001_11110,
        ['C'] = 0b01110_10001_10000_10000_10000_10001_01110,
        ['D'] = 0b11110_10001_10001_10001_10001_10001_11110,
        ['E'] = 0b11111_10000_10000_11110_10000_10000_11111,
        ['F'] = 0b11111_10000_10000_11110_10000_10000_10000,
        ['G'] = 0b01110_10001_10000_10111_10001_10001_01110,
        ['H'] = 0b10001_10001_10001_11111_10001_10001_10001,
        ['I'] = 0b01110_00100_00100_00100_00100_00100_01110,
        ['K'] = 0b10001_10010_10100_11000_10100_10010_10001,
        ['L'] = 0b10000_10000_10000_10000_10000_10000_11111,
        ['M'] = 0b10001_11011_10101_10101_10001_10001_10001,
        ['N'] = 0b10001_11001_10101_10011_10001_10001_10001,
        ['O'] = 0b01110_10001_10001_10001_10001_10001_01110,
        ['P'] = 0b11110_10001_10001_11110_10000_10000_10000,
        ['R'] = 0b11110_10001_10001_11110_10100_10010_10001,
        ['S'] = 0b01111_10000_10000_01110_00001_00001_11110,
        ['T'] = 0b11111_00100_00100_00100_00100_00100_00100,
        ['U'] = 0b10001_10001_10001_10001_10001_10001_01110,
        ['V'] = 0b10001_10001_10001_10001_10001_01010_00100,
        ['W'] = 0b10001_10001_10001_10101_10101_10101_01010,
        ['Y'] = 0b10001_10001_01010_00100_00100_00100_00100,
        [' '] = 0
    };
}
