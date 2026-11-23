namespace Emberward.Desktop.Rendering.Entities;

using Emberward.Core.Types;

/// <summary>
/// Short lines of text drawn in the world as entity quads: one <see cref="EntityShape.KindPowerLabel"/> quad per glyph of
/// a 5x7 bitmap alphabet, packed into the quad's parameters (<c>PowerLabel.hlsli</c> unpacks it). The charge labels and
/// the nest danger labels, the pyrite deposit labels (DepositQuads) and the powerup marks (PowerupQuads) use it.
/// </summary>
internal static class GlyphText
{
    public const int GlyphWidth = 5;
    public const int GlyphHeight = 7;

    /// <summary>The width of <paramref name="text"/> in cells at <paramref name="dot"/> cells a dot.</summary>
    public static float Width(string text, float dot) => text.Length * (GlyphWidth + 1) * dot - dot;

    /// <summary>
    /// Adds <paramref name="text"/> centred on (<paramref name="x"/>, <paramref name="y"/> + <paramref name="top"/>),
    /// its top at <paramref name="top"/> cells, each dot <paramref name="dot"/> cells.
    /// </summary>
    /// <param name="motion">How the line moves through a simulation step (cells per step), for one that rides a tank.</param>
    public static void AddLine(EntityQuads quads, float x, float y, float top, string text, float dot, Color color, float alpha,
        PositionF motion = default)
    {
        float advance = (GlyphWidth + 1) * dot;
        float left = -Width(text, dot) * 0.5f;
        foreach (char character in text)
        {
            ulong glyph = Glyph(character);
            if (glyph != 0UL)
            {
                // Both pieces fit exactly in floats. The quad includes one dot of room for the shadow.
                quads.Add(x + left, y + top, 1f, 0f, 0f, advance, 0f, (GlyphHeight + 1) * dot,
                    color, EntityShape.KindPowerLabel, glyph & 0xFFFFFUL, glyph >> 20, 1f / dot, alpha, motion);
            }
            left += advance;
        }
    }

    /// <summary>Whether <paramref name="character"/> has a bitmap of its own; one that has none is drawn as a question mark.</summary>
    public static bool Has(char character) => character is ' ' or '?' || Glyph(character) != Glyph('?');

    /// <summary>The 5x7 bitmap of <paramref name="character"/>: each group of five bits is one row, top to bottom; 0 for a space.</summary>
    public static ulong Glyph(char character) => character switch
    {
        '0' => 0b01110_10001_10011_10101_11001_10001_01110UL,
        '1' => 0b00100_01100_00100_00100_00100_00100_01110UL,
        '2' => 0b01110_10001_00001_00010_00100_01000_11111UL,
        '3' => 0b11110_00001_00001_01110_00001_00001_11110UL,
        '4' => 0b00010_00110_01010_10010_11111_00010_00010UL,
        '5' => 0b11111_10000_10000_11110_00001_00001_11110UL,
        '6' => 0b01110_10000_10000_11110_10001_10001_01110UL,
        '7' => 0b11111_00001_00010_00100_01000_01000_01000UL,
        '8' => 0b01110_10001_10001_01110_10001_10001_01110UL,
        '9' => 0b01110_10001_10001_01111_00001_00001_01110UL,
        '.' => 0b00000_00000_00000_00000_00000_01100_01100UL,
        ':' => 0b00000_01100_01100_00000_01100_01100_00000UL,
        '/' => 0b00001_00001_00010_00100_01000_10000_10000UL,
        '+' => 0b00000_00100_00100_11111_00100_00100_00000UL,
        '-' => 0b00000_00000_00000_11111_00000_00000_00000UL,
        '>' => 0b01000_00100_00010_00001_00010_00100_01000UL,
        's' => 0b00000_00000_01111_10000_01110_00001_11110UL,
        'k' => 0b10000_10000_10010_10100_11000_10100_10010UL,
        'g' => 0b00000_01111_10001_10001_01111_00001_01110UL,
        'e' => 0b00000_00000_01110_10001_11111_10000_01111UL,
        'n' => 0b00000_00000_11110_10001_10001_10001_10001UL,
        'A' => 0b01110_10001_10001_11111_10001_10001_10001UL,
        'B' => 0b11110_10001_10001_11110_10001_10001_11110UL,
        'C' => 0b01110_10001_10000_10000_10000_10001_01110UL,
        'D' => 0b11100_10010_10001_10001_10001_10010_11100UL,
        'E' => 0b11111_10000_10000_11110_10000_10000_11111UL,
        'F' => 0b11111_10000_10000_11110_10000_10000_10000UL,
        'G' => 0b01110_10001_10000_10111_10001_10001_01111UL,
        'H' => 0b10001_10001_10001_11111_10001_10001_10001UL,
        'I' => 0b01110_00100_00100_00100_00100_00100_01110UL,
        'J' => 0b00111_00010_00010_00010_00010_10010_01100UL,
        'K' => 0b10001_10010_10100_11000_10100_10010_10001UL,
        'L' => 0b10000_10000_10000_10000_10000_10000_11111UL,
        'M' => 0b10001_11011_10101_10101_10001_10001_10001UL,
        'N' => 0b10001_11001_10101_10011_10001_10001_10001UL,
        'O' => 0b01110_10001_10001_10001_10001_10001_01110UL,
        'P' => 0b11110_10001_10001_11110_10000_10000_10000UL,
        'Q' => 0b01110_10001_10001_10001_10101_10010_01101UL,
        'R' => 0b11110_10001_10001_11110_10100_10010_10001UL,
        'S' => 0b01111_10000_10000_01110_00001_00001_11110UL,
        'T' => 0b11111_00100_00100_00100_00100_00100_00100UL,
        'U' => 0b10001_10001_10001_10001_10001_10001_01110UL,
        'V' => 0b10001_10001_10001_10001_10001_01010_00100UL,
        'W' => 0b10001_10001_10001_10101_10101_11011_10001UL,
        'X' => 0b10001_10001_01010_00100_01010_10001_10001UL,
        'Y' => 0b10001_10001_01010_00100_00100_00100_00100UL,
        'Z' => 0b11111_00001_00010_00100_01000_10000_11111UL,
        ' ' => 0UL,
        _ => 0b01110_10001_00010_00100_00100_00000_00100UL,
    };
}
