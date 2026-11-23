namespace Emberward.Desktop.Gui;

using System.Numerics;
using ImGuiNET;
using Emberward.Core.Config;
using Emberward.Core.Entities;
using Emberward.Desktop.Rendering;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Match;
using static Emberward.Desktop.Gui.HudLayout;

/// <summary>
/// The art panels along the bottom of the screen: TEMP, HULL and MATERIALS in the bottom-left corner, BUILD
/// STRUCTURES in the bottom-right (<see cref="HudLayout"/>), with their readouts and bars. The middle of the view is
/// left clear. The art carries no text: titles and numbers are drawn with the UI font, so they stay sharp at any
/// resolution.
/// </summary>
public sealed class HudStrip
{
    // The readout numbers' green and size (art pixels).
    private const uint ReadoutColor = 0xFFAAFACDu;
    private const float ReadoutSize = 22f;

    // The SHIELD/HULL plates hold a line each, SHIELD on the upper and HULL on the lower, each with a hairline bar
    // under it (art pixels; LedgerLayout places them).
    private const float LedgerLeft = ShieldX + LedgerLayout.LedgerLeft, LedgerRight = ShieldX + LedgerLayout.LedgerRight;

    // The ore line, drawn on the narrow plate beneath the hold window (art pixels): the pale gold of the Pyrite in
    // the hold (tools/hud/make_hold_pyrite.py), deep orange once the hold is full, bright green while it unloads.
    private const float OreLineX = MaterialsX + 122f, OreLineY = 133f, OreLineSize = 15f;
    private const uint OreLineColor = 0xFF78E6FFu, OreLineFullColor = 0xFF2880FFu, OreLineUnloadColor = 0xFF60FF90u;
    private const uint OreLineGlow = 0x2840B0FFu;

    // The MATERIALS lines: the labels' warm grey, the rate's dim gold, the store's pale gold and its deposit flash.
    private const uint MaterialsLabelColor = 0xFF9AC8E8u, MaterialsRateColor = 0xFF5AA0C8u;
    private const uint StoreColor = 0xFFB8F0FFu, StoreFlashColor = 0xFF40C4FFu;

    /// <summary>
    /// The TEMP panel's plates, measured on the art (art pixels): the centre both lines share, the title plate's centre,
    /// and the readout plate's inner edges. The readout is centred in its plate. The SHIELD/HULL and MATERIALS plates
    /// carry a line each instead (<see cref="LedgerLayout"/>).
    /// </summary>
    private readonly record struct Plate(float CenterX, float TitleY, float ReadoutLeft, float ReadoutTop, float ReadoutRight, float ReadoutBottom)
    {
        public float ReadoutY => (ReadoutTop + ReadoutBottom) * 0.5f;
    }

    private static readonly Plate TempPlate = new(HeatX + 170f, 151.5f, HeatX + 101f, 168f, HeatX + 239f, 190f);

    private readonly HudLayout _layout = new();
    private readonly HeatGauge _heat;
    private readonly LiquidGauges _liquids;
    private readonly BuildPanel _build;

    private nint _heatIconTex, _shieldIconTex;
    private nint _panelFrameTex, _orePileTex, _buildPanelTex;

    public HudStrip()
    {
        _heat = new HeatGauge(_layout);
        _liquids = new LiquidGauges(_layout);
        _build = new BuildPanel(_layout);
    }

    /// <summary>State of the temperature orb shader for the frame just drawn; the game passes it to the backend.</summary>
    public HeatOrbState HeatOrb => _heat.HeatOrb;

    /// <summary>Screen y of the strip's top edge, as of the last frame drawn.</summary>
    public float Top => _layout.Origin.Y;

    private Vector2 At(float x, float y) => _layout.At(x, y);
    private float S(float v) => _layout.S(v);

    public void Init(nint heatIcon, nint shieldIcon, nint panelFrame, nint orePile, nint buildPanel)
    {
        _heatIconTex = heatIcon;
        _shieldIconTex = shieldIcon;
        _panelFrameTex = panelFrame;
        _orePileTex = orePile;
        _buildPanelTex = buildPanel;
    }

    /// <param name="reserve">The reserve's stage tag beside OFF NETWORK (<see cref="ReserveTags.Of"/>), if any.</param>
    /// <param name="ledger">The player's ore as the HUD tells it (the base store's rate, a deposit's pour).</param>
    public void Draw(Tank player, float time, MatchRules rules, TankShieldView shield, OreLedgerView ledger, ReserveTag? reserve = null)
    {
        var displaySize = ImGui.GetIO().DisplaySize;
        var dl = ImGui.GetForegroundDrawList();

        _layout.Update(displaySize);

        DrawPanelArt(dl, _heatIconTex, HeatX, HeatPanelW);
        DrawPanelArt(dl, _shieldIconTex, ShieldX, ShieldPanelW);
        DrawPanelArt(dl, _panelFrameTex, MaterialsX, MaterialsPanelW);
        DrawPanelArt(dl, _buildPanelTex, BuildX, BuildPanelW);
        DrawPanelLabels(dl);

        _heat.DrawOrb(dl, player, time);
        // The crystal holds the shield when the match has one, else the hull; the tank's hot centre shows through
        // what has emptied.
        float crystal = shield.Active ? (player.IsDead ? 0f : shield.ShieldFill) : player.HullFraction;
        bool crystalLow = shield.Active ? shield.Low : player.IsHullLow(latched: false);
        _liquids.Draw(dl, player, ShieldX, time, crystal, crystalLow);

        // TEMP: the tank's temperature in degrees above ambient. Digging and firing heat it; past the safe maximum
        // the heat burns the shield. A tank docked at its own base is pre-cooled below ambient (negative degrees).
        int degrees = player.IsDead ? 0 : (int)MathF.Round(player.Heat);
        DrawReadout(dl, TempPlate, degrees.ToString(System.Globalization.CultureInfo.InvariantCulture) + "°");
        _heat.DrawTrend(dl, player, TempPlate.ReadoutRight - 12f, TempPlate.ReadoutY);
        _heat.DrawOffNetworkTag(dl, player, HeatX + HeatPanelW * 0.5f, time, reserve);

        // SHIELD and HULL: a line each, with a hairline bar under it.
        DrawLedger(dl, player, shield, time);

        // MATERIALS: the hold and the base store (MaterialsView). The pile is a sheet of 8 x 5 frames drawn by
        // tools/hud/make_hold_pyrite.py; each stone carries its own corona.
        var materials = MaterialsView.Of(player, ledger);
        if (materials.PileFrame >= 0)
        {
            var cell = new Vector2(1f / 8f, 1f / 5f);
            var uv = new Vector2(materials.PileFrame % 8, materials.PileFrame / 8) * cell;
            dl.AddImage(_orePileTex, At(MaterialsX + 51f, 48f), At(MaterialsX + 195f, 108f), uv, uv + cell);
        }
        DrawMaterials(dl, materials);
        // The hold emptying into the base (docked, or through the network), else a plain ORE label.
        uint oreColor = materials.Unloading ? OreLineUnloadColor : materials.HoldFull ? OreLineFullColor : OreLineColor;
        DrawStripText(dl, materials.OreLine, OreLineX, OreLineY, OreLineSize, oreColor, glow: OreLineGlow);

        if (materials.Unloading)
        {
            // Down arrows either side of the word, bobbing: the ore is going down into the base.
            float bob = (time * 3f) % 1f * 5f;
            foreach (float dx in (ReadOnlySpan<float>)[-55f, 55f])
            {
                float cx = OreLineX + dx, top = OreLineY - 5f + bob;
                dl.AddTriangleFilled(At(cx - 4f, top), At(cx + 4f, top), At(cx, top + 5f), OreLineUnloadColor);
            }
        }

        _build.Draw(dl, player, BuildX, time, rules);

        // The COMMAND button stands for the base upgrades: lit while docked (keys 1-7 buy), dimmed elsewhere.
        if (!player.IsDockedAtOwnBase)
            dl.AddRectFilled(At(BuildX + 77f, 172f), At(BuildX + 277f, 212f), 0x90000000u, S(10f));
        else
            dl.AddRect(At(BuildX + 77f, 172f), At(BuildX + 277f, 212f),
                (int)(time * 2f) % 2 == 0 ? 0xFF40FF60u : 0xFF208030u, S(10f), ImDrawFlags.None, S(2.5f));
    }

    /// <summary>One panel's art, placed by its left edge and sized from its width (its right edge may be where the
    /// other part of the HUD begins, which <see cref="HudLayout.At(float, float)"/> would move).</summary>
    private void DrawPanelArt(ImDrawListPtr dl, nint texture, float x, float w)
    {
        var min = At(x, 0f);
        dl.AddImage(texture, min, min + new Vector2(S(w), S(StripH)));
    }

    /// <summary>
    /// The panel titles, drawn on the plates of the art (which carries no text of its own). Positions are the plate
    /// centres in art pixels.
    /// </summary>
    private void DrawPanelLabels(ImDrawListPtr dl)
    {
        DrawStripText(dl, "TEMP", TempPlate.CenterX, TempPlate.TitleY, 19f, 0xFF78C8FFu, glow: 0x181880FFu);
        DrawStripText(dl, "BUILD STRUCTURES", BuildX + 162f, 23.5f, 17f, 0xFFB0E4F8u, glow: 0x181880FFu);
        DrawStripText(dl, "COMMAND", BuildX + 171f, 191.5f, 21f, 0xFF98E8FFu, glow: 0x202080FFu);
    }

    /// <summary>
    /// The SHIELD/HULL readout, a line on each plate: SHIELD and its number on the upper, HULL and its number on the
    /// lower, the numbers ending at the right end and a hairline bar under each line filled from the left. The shield is
    /// cyan, slate once empty; the hull's number and bar take the hotspot ramp's colour (<see cref="HudPalette.HullHeat"/>),
    /// and its label says what the hull is doing (<see cref="HullStatuses"/>: breach orange while it burns, flashing
    /// embers when almost gone, slag grey once dead). A match with no shield shows HULL alone, on the lower plate. A dead
    /// tank's shield is spent too (death resets it to full for the respawn).
    /// </summary>
    private void DrawLedger(ImDrawListPtr dl, Tank player, in TankShieldView shield, float time)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (shield.Active)
        {
            int points = player.IsDead ? 0 : Math.Max(0, shield.Shield);
            float fill = player.IsDead ? 0f : shield.ShieldFill;
            uint color = points > 0 ? HudPalette.Shield : HudPalette.ShieldEmpty;
            DrawLedgerLine(dl, LedgerLayout.ShieldLineY, "SHIELD", color, 0u, points.ToString(inv), color, 0u, fill,
                HudTheme.WithAlphaByte(HudPalette.ShieldEmpty, 0x70));
        }

        int health = Math.Max(0, player.Reactor.Health);
        float hull = player.IsDead ? 0f : player.HullFraction;
        // A dead hull has cooled to slag; a living one glows on the ramp as it falls.
        uint hullColor = player.IsDead ? HudPalette.TextDim : HudPalette.HullHeat(hull);
        var label = HullStatuses.Of(player);
        DrawLedgerLine(dl, LedgerLayout.HullLineY, "HULL", label.ColorAt(time),
            label.Alarms ? HudTheme.WithAlphaByte(label.Color, 0x40) : 0u, health.ToString(inv), hullColor, 0x30106AFFu, hull,
            HudTheme.WithAlphaByte(HudPalette.HullEmber(HudPalette.HullHeatStart), 0x90));
    }

    /// <summary>
    /// One SHIELD/HULL line at art y <paramref name="y"/>: the label at the left end, the number ending at the right
    /// end, and under them a hairline bar across the plate (a dark outline, the track, and the filled part from the
    /// left).
    /// </summary>
    private void DrawLedgerLine(ImDrawListPtr dl, float y, string label, uint labelColor, uint labelGlow, string number,
        uint numberColor, uint numberGlow, float fill, uint trackColor)
    {
        DrawStripText(dl, label, LedgerLeft, y, LedgerLayout.LabelSize, labelColor, labelGlow, align: 0f);
        DrawStripText(dl, number, LedgerRight, y, LedgerLayout.NumberSize, numberColor, numberGlow, align: 1f);

        float barY = y + LedgerLayout.BarDrop, half = LedgerLayout.BarHalf, edge = LedgerLayout.BarOutline;
        dl.AddRectFilled(At(LedgerLeft - edge, barY - half - edge), At(LedgerRight + edge, barY + half + edge), 0xC0000000u);
        dl.AddRectFilled(At(LedgerLeft, barY - half), At(LedgerRight, barY + half), trackColor);
        float filled = (LedgerRight - LedgerLeft) * Math.Clamp(fill, 0f, 1f);
        if (filled > 0f)
            dl.AddRectFilled(At(LedgerLeft, barY - half), At(LedgerLeft + filled, barY + half), numberColor);
    }

    /// <summary>
    /// The MATERIALS readout, a line on each plate (<see cref="LedgerLayout"/>): HOLD and the hold's amount on the upper,
    /// BASE, the base store and its rate on the lower, the numbers ending in one column. The hold is in the readout
    /// green, deep orange when full; the store in pale gold, flashing gold as a deposit lands, with the deposit's
    /// <c>+N</c> rising out of it.
    /// </summary>
    private void DrawMaterials(ImDrawListPtr dl, MaterialsView view)
    {
        float left = MaterialsX + LedgerLayout.MaterialsLeft, numbers = MaterialsX + LedgerLayout.MaterialsNumberRight;
        float hold = LedgerLayout.HoldLineY, store = LedgerLayout.BaseLineY;
        DrawStripText(dl, "HOLD", left, hold, LedgerLayout.LabelSize, MaterialsLabelColor, align: 0f);
        DrawStripText(dl, view.HoldText, numbers, hold, LedgerLayout.NumberSize,
            view.HoldFull ? OreLineFullColor : ReadoutColor, align: 1f);

        DrawStripText(dl, "BASE", left, store, LedgerLayout.LabelSize, MaterialsLabelColor, align: 0f);
        uint storeColor = HudPalette.Lerp(StoreColor, StoreFlashColor, view.Flash);
        DrawStripText(dl, view.BaseText, numbers, store, LedgerLayout.NumberSize, storeColor,
            view.Flash > 0f ? HudTheme.WithAlpha(StoreFlashColor, 0.3f * view.Flash) : 0u, align: 1f);
        if (view.RateText != null)
            DrawStripText(dl, view.RateText, MaterialsX + LedgerLayout.MaterialsRight, store, LedgerLayout.RateSize,
                MaterialsRateColor, align: 1f);
        if (view.DeltaText != null)
            // Rising out of the store's number, from just above the line.
            DrawStripText(dl, view.DeltaText, numbers, store - 2f - LedgerLayout.DeltaRise * view.DeltaRise,
                LedgerLayout.NumberSize, HudTheme.WithAlpha(StoreFlashColor, view.DeltaAlpha), align: 1f);
    }

    /// <summary>A plate's number, centred in its readout plate above the bar.</summary>
    private void DrawReadout(ImDrawListPtr dl, Plate plate, string text) =>
        DrawStripText(dl, text, plate.CenterX, plate.ReadoutY, ReadoutSize, ReadoutColor, glow: 0x2830FF60u);

    /// <summary>
    /// Text in the strip, centred on art pixel (<paramref name="cx"/>, <paramref name="cy"/>) and sized in art pixels,
    /// so it scales with the art; drawn from the large font bake when big on screen, so it stays sharp at 4K. A dark
    /// outline keeps it legible on the plates, and an optional soft <paramref name="glow"/> lights it like the art.
    /// <paramref name="align"/> places the text's width about <paramref name="cx"/>: 0.5 centred, 0 starting there,
    /// 1 ending there.
    /// </summary>
    private void DrawStripText(ImDrawListPtr dl, string text, float cx, float cy, float size, uint color, uint glow = 0, float align = 0.5f)
    {
        float px = S(size);
        float deviceScale = ImGuiPlatform.DeviceScale;
        var font = ImGuiPlatform.FontFor(px * deviceScale);
        var extent = font.CalcTextSizeA(px, float.MaxValue, 0f, text);
        // The bold pass is drawn bold px to the right, so start half of that to the left to keep the ink centred.
        float bold = S(0.6f);
        // Centre the ink, not the line box: capitals and digits sit between the top and bottom of '0', which is
        // not the middle of the line (that includes room for descenders).
        float inkMidY = InkMiddle(font) * px;
        // Snap to whole device pixels so the glyphs are not resampled between texels.
        var pos = At(cx, cy) - new Vector2(extent.X * align + bold * 0.5f, inkMidY);
        pos = new Vector2(MathF.Round(pos.X * deviceScale) / deviceScale, MathF.Round(pos.Y * deviceScale) / deviceScale);

        if (glow != 0)
        {
            float r = S(3f);
            for (int i = 0; i < 12; i++)
            {
                float a = i * MathF.PI / 6f;
                dl.AddText(font, px, pos + new Vector2(MathF.Cos(a), MathF.Sin(a) * 0.7f) * r, glow, text);
            }
        }
        // Heavier than the font's own weight, to match the art's chunky lettering: each pass is drawn twice, a
        // fraction of a pixel apart.
        float o = MathF.Max(1f / deviceScale, S(1f));
        const uint outline = 0xE0000000u;
        foreach (var d in (ReadOnlySpan<Vector2>)[new(-o, 0f), new(o + bold, 0f), new(0f, -o), new(bold, -o), new(0f, o * 1.6f), new(bold, o * 1.6f)])
            dl.AddText(font, px, pos + d, outline, text);
        dl.AddText(font, px, pos, color, text);
        dl.AddText(font, px, pos + new Vector2(bold, 0f), color, text);
    }

    /// <summary>
    /// Where the middle of a capital or digit sits below the top of the line, as a fraction of the font size: the
    /// centre of the '0' glyph's box.
    /// </summary>
    private static float InkMiddle(ImFontPtr font)
    {
        // FindGlyph falls back to the font's fallback glyph, so it always returns one.
        var glyph = font.FindGlyph('0');
        return (glyph.Y0 + glyph.Y1) * 0.5f / font.FontSize;
    }
}
