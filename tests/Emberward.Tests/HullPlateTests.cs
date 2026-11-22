using Emberward.Desktop.Gui;

namespace Emberward.Tests;

/// <summary>
/// The SHIELD/HULL plate: the hull's heat ramp (<see cref="HudPalette.HullHeat"/>, which the tank's hotspot shares)
/// and the plate's status word (<see cref="HullStatuses"/>).
/// </summary>
public class HullPlateTests
{
    private static uint Rgb(int r, int g, int b) => HudPalette.Rgba((byte)r, (byte)g, (byte)b);

    // Perceived brightness of an ABGR colour (Rec. 601 weights).
    private static float Brightness(uint c) =>
        0.299f * (c & 0xFF) + 0.587f * ((c >> 8) & 0xFF) + 0.114f * ((c >> 16) & 0xFF);

    [Fact]
    public void TheRamp_HitsItsStops()
    {
        Assert.Equal(Rgb(0x5A, 0x14, 0x08), HudPalette.HullEmber(0.75f));
        Assert.Equal(Rgb(0xB3, 0x26, 0x0C), HudPalette.HullHeat(0.50f));
        Assert.Equal(Rgb(0xFF, 0x6A, 0x10), HudPalette.HullHeat(0.25f));
        Assert.Equal(Rgb(0xFF, 0xD0, 0x40), HudPalette.HullHeat(0.05f));
        Assert.Equal(Rgb(0xFF, 0xF6, 0xD8), HudPalette.HullHeat(0f));
    }

    [Fact]
    public void TheRamp_InterpolatesBetweenStops()
    {
        // Halfway from 50% (#B3260C) to 25% (#FF6A10).
        Assert.Equal(Rgb(0xD9, 0x48, 0x0E), HudPalette.HullHeat(0.375f));
    }

    [Fact]
    public void AboveTheRamp_TheHullIsItsNeutralColour()
    {
        Assert.Equal(HudPalette.HullNeutral, HudPalette.HullHeat(0.76f));
        Assert.Equal(HudPalette.HullNeutral, HudPalette.HullHeat(1f));
        // The ember (for glows) stays at the ramp's dull red end instead.
        Assert.Equal(Rgb(0x5A, 0x14, 0x08), HudPalette.HullEmber(1f));
    }

    [Fact]
    public void TheColour_EasesOntoTheRamp_WithoutAJump()
    {
        uint last = HudPalette.HullHeat(0.76f);
        for (int i = 760; i >= 590; i--)
        {
            uint color = HudPalette.HullHeat(i / 1000f);
            for (int shift = 0; shift < 24; shift += 8)
            {
                int step = Math.Abs((int)((color >> shift) & 0xFF) - (int)((last >> shift) & 0xFF));
                Assert.True(step <= 4, $"the colour jumps by {step} at hull {i / 1000f}");
            }
            last = color;
        }
        Assert.Equal(HudPalette.HullEmber(0.6f), HudPalette.HullHeat(0.6f));
    }

    [Fact]
    public void TheRamp_BrightensAsTheHullFalls()
    {
        float last = -1f;
        for (int i = (int)(HudPalette.HullHeatBlendEnd * 1000f); i >= 0; i--)
        {
            float b = Brightness(HudPalette.HullHeat(i / 1000f));
            Assert.True(b >= last, $"brightness fell at hull {i / 1000f}");
            last = b;
        }
        float lastIntensity = -1f;
        for (int i = 1000; i >= 0; i--)
        {
            float intensity = HudPalette.HullHeatIntensity(i / 1000f);
            Assert.True(intensity >= lastIntensity);
            lastIntensity = intensity;
        }
        Assert.Equal(0f, HudPalette.HullHeatIntensity(0.75f));
        Assert.Equal(1f, HudPalette.HullHeatIntensity(0f));
    }

    [Fact]
    public void TheRamp_ClampsOutOfRangeFractions()
    {
        Assert.Equal(HudPalette.HullHeat(0f), HudPalette.HullHeat(-0.5f));
        Assert.Equal(HudPalette.HullHeat(0f), HudPalette.HullHeat(float.NaN));
        Assert.Equal(HudPalette.HullNeutral, HudPalette.HullHeat(3f));
        Assert.Equal(1f, HudPalette.HullHeatIntensity(-1f));
        Assert.Equal(0f, HudPalette.HullHeatIntensity(2f));
        Assert.All([HudPalette.HullHeat(0.3f), HudPalette.HullEmber(-1f)], c => Assert.Equal(0xFFu, c >> 24));
    }

    [Theory]
    [InlineData(false, true, 40, 0f, 0.9f, HullStatus.Shielded)]
    [InlineData(false, true, 0, 0f, 0.9f, HullStatus.ShieldDown)]
    [InlineData(false, true, 0, 0.3f, 0.3f, HullStatus.HullBreach)]
    [InlineData(false, true, 25, 0.3f, 0.3f, HullStatus.HullBreach)]
    [InlineData(false, true, 0, 0.1f, 0.67f, HullStatus.ShieldDown)]
    [InlineData(false, true, 0, 0.2f, 0.6f, HullStatus.ShieldDown)]
    [InlineData(false, true, 0, 0.21f, 0.59f, HullStatus.HullBreach)]
    [InlineData(false, true, 25, 0.1f, 0.67f, HullStatus.HullBreach)]
    [InlineData(false, false, 0, 0.1f, 0.67f, HullStatus.HullBreach)]
    [InlineData(false, true, 0, 0.9f, 0.04f, HullStatus.Melting)]
    [InlineData(false, true, 0, 0f, 0.049f, HullStatus.Melting)]
    [InlineData(false, true, 0, 0f, 0.05f, HullStatus.ShieldDown)]
    [InlineData(true, true, 50, 0f, 0f, HullStatus.Slag)]
    [InlineData(true, false, 0, 1f, 0.5f, HullStatus.Slag)]
    [InlineData(false, false, 0, 0f, 0.9f, HullStatus.Hull)]
    [InlineData(false, false, 0, 0.5f, 0.2f, HullStatus.HullBreach)]
    public void TheStatusWord_NamesTheWorstThing(bool dead, bool shieldActive, int shield, float fire, float hull, HullStatus expected) =>
        Assert.Equal(expected, HullStatuses.Of(dead, shieldActive, shield, fire, hull).Status);

    [Fact]
    public void EachStatus_HasItsWord_AndOnlyMeltingFlashes()
    {
        var words = new[]
        {
            HullStatuses.Of(false, true, 10, 0f, 1f), HullStatuses.Of(false, true, 0, 0f, 1f),
            HullStatuses.Of(false, true, 0, 0.5f, 0.3f), HullStatuses.Of(false, true, 0, 1f, 0.01f),
            HullStatuses.Of(true, true, 0, 0f, 0f), HullStatuses.Of(false, false, 0, 0f, 1f),
        };
        Assert.Equal(["SHIELDED", "SHIELD DOWN", "HULL BREACH", "MELTING", "SLAG", "HULL"], words.Select(w => w.Text));
        Assert.Equal([HullStatus.Melting], words.Where(w => w.Flashes).Select(w => w.Status));
        var melting = words[3];
        Assert.NotEqual(melting.ColorAt(0f), melting.ColorAt(0.3f));
        Assert.Equal(HudPalette.Shield, words[0].Color);
    }

    [Fact]
    public void TheLedgerRows_NumbersBarsAndOutlines_NeitherOverlapNorLeaveThePlate()
    {
        // Top to bottom: the SHIELD row's text, its bar, the HULL row's text, its bar; and the lone hull row of a match
        // with no shield.
        (float Top, float Bottom)[] stacked =
        [
            LedgerLayout.Text(LedgerLayout.ShieldRowY), LedgerLayout.Bar(LedgerLayout.ShieldRowY),
            LedgerLayout.Text(LedgerLayout.HullRowY), LedgerLayout.Bar(LedgerLayout.HullRowY),
        ];
        (float Top, float Bottom)[] solo = [LedgerLayout.Text(LedgerLayout.SoloRowY), LedgerLayout.Bar(LedgerLayout.SoloRowY)];
        foreach (var rows in new[] { stacked, solo })
        {
            Assert.True(rows[0].Top >= LedgerLayout.PlateTop, $"the top row starts at {rows[0].Top}, above the plate");
            Assert.True(rows[^1].Bottom <= LedgerLayout.PlateBottom, $"the last bar ends at {rows[^1].Bottom}, below the plate");
            for (int i = 1; i < rows.Length; i++)
                Assert.True(rows[i].Top >= rows[i - 1].Bottom, $"rectangle {i} ({rows[i].Top}) overlaps the one above ({rows[i - 1].Bottom})");
        }
        Assert.True(LedgerLayout.LabelSize <= LedgerLayout.NumberSize);
    }
}
