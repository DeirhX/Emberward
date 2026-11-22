namespace Emberward.Desktop.Gui;

using System.Numerics;
using ImGuiNET;
using Emberward.Core.Entities.Tanks;
using static Emberward.Desktop.Gui.HudDraw;

/// <summary>
/// The shield crystal is dark where it has emptied, from its edge inward. A low level pulses red and the crystal flashes when hit.
/// </summary>
public sealed class LiquidGauges(HudLayout layout)
{
    private static readonly Vector2[] ShieldOutline =
    [
        new(44f, 44f), new(60f, 40f), new(82f, 37f), new(104f, 40f), new(121f, 44f), new(121f, 80f),
        new(114f, 100f), new(100f, 116f), new(82f, 128f), new(64f, 116f), new(50f, 100f), new(44f, 80f),
    ];

    private float _shownShieldFill = -1f;

    /// <param name="fill">What the crystal shows, 0 to 1: the shield (cyan crystal) when the match runs one, else the hull.</param>
    /// <param name="low">Whether that level is low enough to pulse red.</param>
    public void Draw(ImDrawListPtr dl, Tank player, float shieldPanelX, float time, float fill, bool low)
    {
        float shieldFill = fill;
        // Ease the shown levels toward the real ones so the liquid moves rather than jumps.
        _shownShieldFill = _shownShieldFill < 0f ? shieldFill : _shownShieldFill + (shieldFill - _shownShieldFill) * 0.20f;

        bool pulse = (int)(time * 4f) % 2 == 0;

        // Shield crystal.
        var o = new Vector2(shieldPanelX, 0f);
        var poly = new Vector2[ShieldOutline.Length];
        for (int i = 0; i < poly.Length; i++)
        {
            poly[i] = layout.At(o + ShieldOutline[i]);
        }
        // The crystal empties from its edge inward on every side: what is left is the outline shrunk toward its middle,
        // so the area left follows the fill. The dark ring between the outline and the shrunk shape is the empty part.
        var centre = Vector2.Zero;
        foreach (var point in poly)
            centre += point;
        centre /= poly.Length;
        float shrink = MathF.Sqrt(Math.Clamp(_shownShieldFill, 0f, 1f));
        var inner = new Vector2[poly.Length];
        for (int i = 0; i < poly.Length; i++)
            inner[i] = centre + (poly[i] - centre) * shrink;

        if (player.HitFlashTicks > Tank.HitFlashDuration - 6)
            dl.AddConvexPolyFilled(ref inner[0], inner.Length, 0x90FFFFFFu);        // hit flash
        else if (low && shieldFill > 0f && pulse)
            dl.AddConvexPolyFilled(ref inner[0], inner.Length, 0x703030FFu);        // low shield (red pulse)
        for (int i = 0; i < poly.Length; i++)
        {
            int n = (i + 1) % poly.Length;
            var ring = new[] { poly[i], poly[n], inner[n], inner[i] };
            dl.AddConvexPolyFilled(ref ring[0], ring.Length, 0xC8080808u);          // empty part
        }
        if (shieldFill > 0f)
        {
            // A bright line along the edge of what is left.
            dl.AddPolyline(ref inner[0], inner.Length, HudTheme.WithAlphaByte(0xFFFFE0A0u, 0x50), ImDrawFlags.Closed, layout.S(4f));
            dl.AddPolyline(ref inner[0], inner.Length, 0xFFFFE0A0u, ImDrawFlags.Closed, layout.S(1.5f));
        }
    }
}
