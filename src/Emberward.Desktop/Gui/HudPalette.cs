namespace Emberward.Desktop.Gui;

using System.Globalization;
using Emberward.Core;
using Emberward.Core.Rendering;
using Emberward.Core.Types;

/// <summary>
/// The HUD's colours and colour arithmetic, with no ImGui in it, so that the browser client (which compiles this file
/// through <c>Emberward.Rendering</c>) colours what it shows as the desktop does. Desktop code reads them through
/// <see cref="HudTheme"/>. Colours are ImGui ABGR.
/// </summary>
public static class HudPalette
{
    public static uint Rgba(byte r, byte g, byte b, byte a = 255) => (uint)(a << 24 | b << 16 | g << 8 | r);

    /// <summary><paramref name="color"/> with its alpha scaled by <paramref name="alpha"/> (0 to 1).</summary>
    public static uint WithAlpha(uint color, float alpha) =>
        (color & 0x00FFFFFFu) | (uint)(Math.Clamp(alpha, 0f, 1f) * (color >> 24)) << 24;

    /// <summary><paramref name="color"/> with its alpha replaced by <paramref name="alpha"/> (0 to 255).</summary>
    public static uint WithAlphaByte(uint color, uint alpha) => (color & 0x00FFFFFFu) | alpha << 24;

    /// <summary>The colour a status gauge shows at <paramref name="level"/>, with <paramref name="ok"/> for a fine one.</summary>
    public static uint LevelColor(StatusLevel level, uint ok) => level switch
    {
        StatusLevel.Ok => ok,
        StatusLevel.Warn => Warn,
        _ => Danger,
    };

    /// <summary>An ABGR colour as CSS <c>#rrggbb</c> (its alpha left out).</summary>
    public static string Hex(uint color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color & 0xFF:x2}{(color >> 8) & 0xFF:x2}{(color >> 16) & 0xFF:x2}");

    public static readonly uint PlateTop = Rgba(30, 32, 36, 238);
    public static readonly uint PlateBottom = Rgba(13, 14, 16, 242);
    public static readonly uint Bronze = Rgba(122, 86, 44);
    public static readonly uint BronzeDark = Rgba(58, 40, 22);
    public static readonly uint Amber = Rgba(255, 178, 64);
    public static readonly uint AmberDim = Rgba(176, 112, 40);
    public static readonly uint Teal = Rgba(96, 214, 255);
    public static readonly uint Text = Rgba(234, 228, 216);
    public static readonly uint TextDim = Rgba(158, 150, 138);
    public static readonly uint DarkBase = Rgba(233, 132, 139);
    public static readonly uint Producer = Rgba(194, 156, 255);
    // A relay that runs hot from the charge it carries: throttled, then overheated and losing health.
    public static readonly uint Throttled = Rgba(255, 140, 48);
    public static readonly uint Overheated = Rgba(255, 72, 168);
    public static readonly uint Good = Rgba(118, 230, 128);
    public static readonly uint Warn = Rgba(255, 198, 64);
    public static readonly uint Danger = Rgba(255, 86, 66);
    public static readonly uint Shadow = Rgba(0, 0, 0, 150);
    public static readonly uint Track = Rgba(8, 8, 10, 230);

    // The SHIELD/HULL plate: the shield's cyan, the slate of an empty shield, and the hull's own warm colour while
    // it is above the heat ramp.
    public static readonly uint Shield = Rgba(160, 224, 255);
    public static readonly uint ShieldEmpty = Rgba(95, 127, 153);
    public static readonly uint HullNeutral = Rgba(255, 184, 112);

    // The arcade powerups (PowerupPresentations). Yellow, orange, violet and lime: none is a tank's colour (blue,
    // green, red, pink, purple, white, aqua, gray), and each kind also has a mark, so colour is never the only sign.
    public static readonly uint PowerupOverhaul = Rgba(255, 226, 72);
    public static readonly uint PowerupOvercharge = Rgba(255, 132, 40);
    public static readonly uint PowerupBulwark = Rgba(178, 140, 255);
    public static readonly uint PowerupDrill = Rgba(160, 242, 62);

    /// <summary>The body colour of the tank in <paramref name="slot"/>, as the scoreboard, the name tags and the lobby show it.</summary>
    public static uint TankColor(int slot)
    {
        Color c = TankSprites.GetPixelColor(1, slot);
        return Rgba(c.R, c.G, c.B);
    }

    /// <summary>The hull fraction where the heat ramp starts: above it the hull is not hot.</summary>
    public const float HullHeatStart = 0.75f;

    // The heat ramp's stops, from the hull fraction where it starts down to an empty hull: a dull red ember, through
    // red and orange, to yellow and white-hot.
    private static readonly (float Hull, uint Color)[] HullHeatStops =
    [
        (HullHeatStart, Rgba(0x5A, 0x14, 0x08)),
        (0.50f, Rgba(0xB3, 0x26, 0x0C)),
        (0.25f, Rgba(0xFF, 0x6A, 0x10)),
        (0.05f, Rgba(0xFF, 0xD0, 0x40)),
        (0.00f, Rgba(0xFF, 0xF6, 0xD8)),
    ];

    /// <summary>
    /// The colour of a hull that has <paramref name="hullFraction"/> (0 to 1, clamped) of its health left: the shared
    /// hotspot ramp that the HUD's hull number and bar and the tank's own hotspot all use. Above
    /// <see cref="HullHeatStart"/> it is <see cref="HullNeutral"/>; below it, it eases from there onto the ramp
    /// (<see cref="HullEmber"/>) by <see cref="HullHeatBlendEnd"/>, so the colour never jumps as the hull starts to burn,
    /// and from there on it brightens as the hull falls.
    /// </summary>
    public static uint HullHeat(float hullFraction)
    {
        float f = float.IsNaN(hullFraction) ? 0f : Math.Clamp(hullFraction, 0f, 1f);
        if (f >= HullHeatStart)
            return HullNeutral;
        float t = Math.Clamp((HullHeatStart - f) / (HullHeatStart - HullHeatBlendEnd), 0f, 1f);
        return Lerp(HullNeutral, HullEmber(f), t * t * (3f - (2f * t)));
    }

    /// <summary>The hull fraction by which <see cref="HullHeat"/> has eased fully from the neutral colour onto the ramp.</summary>
    public const float HullHeatBlendEnd = 0.6f;

    /// <summary>
    /// The heat ramp itself, with no neutral above <see cref="HullHeatStart"/>: a healthy hull gives the ramp's dull
    /// red end. For glows (the crystal's centre, the hotspot), whose strength <see cref="HullHeatIntensity"/> gives.
    /// </summary>
    public static uint HullEmber(float hullFraction)
    {
        float f = float.IsNaN(hullFraction) ? 0f : Math.Clamp(hullFraction, 0f, 1f);
        for (int i = 0; i < HullHeatStops.Length - 1; i++)
        {
            var (hi, hiColor) = HullHeatStops[i];
            var (lo, loColor) = HullHeatStops[i + 1];
            if (f >= lo)
                return Lerp(loColor, hiColor, Math.Min(1f, (f - lo) / (hi - lo)));
        }
        return HullHeatStops[^1].Color;
    }

    /// <summary>How hot the hull is, 0 at <see cref="HullHeatStart"/> and above to 1 at an empty hull.</summary>
    public static float HullHeatIntensity(float hullFraction) =>
        float.IsNaN(hullFraction) ? 1f : Math.Clamp((HullHeatStart - hullFraction) / HullHeatStart, 0f, 1f);

    /// <summary>
    /// How strongly the HUD's crystal shows the tank's hot centre through an empty shield (0 to 1): a faint ember on a
    /// healthy hull, full as it empties.
    /// </summary>
    public static float HullGlowStrength(float hullFraction) => 0.35f + 0.6f * HullHeatIntensity(hullFraction);

    /// <summary>Each channel (alpha too) of <paramref name="a"/> moved toward <paramref name="b"/> by <paramref name="t"/> (0 to 1).</summary>
    public static uint Lerp(uint a, uint b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        uint result = 0;
        for (int shift = 0; shift < 32; shift += 8)
        {
            float ca = (a >> shift) & 0xFF, cb = (b >> shift) & 0xFF;
            result |= (uint)MathF.Round(ca + (cb - ca) * t) << shift;
        }
        return result;
    }
}
