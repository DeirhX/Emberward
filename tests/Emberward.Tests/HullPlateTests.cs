using Emberward.Desktop.Gui;

namespace Emberward.Tests;

/// <summary>
/// The SHIELD/HULL plate: the hull's heat ramp (<see cref="HudPalette.HullHeat"/>, which the tank's hotspot shares)
/// the HULL label's colour (<see cref="HullStatuses"/>), and where the two lines sit (<see cref="LedgerLayout"/>).
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
    [InlineData(false, 0f, 0.9f, HullStatus.Intact)]
    [InlineData(false, 0f, 0.05f, HullStatus.Intact)]
    [InlineData(false, 0.1f, 0.67f, HullStatus.Burning)]
    [InlineData(false, 0.5f, 0.2f, HullStatus.Burning)]
    [InlineData(false, 0.9f, 0.04f, HullStatus.Melting)]
    [InlineData(false, 0f, 0.049f, HullStatus.Melting)]
    [InlineData(true, 0f, 0f, HullStatus.Slag)]
    [InlineData(true, 1f, 0.5f, HullStatus.Slag)]
    public void TheHullLabel_NamesTheWorstThing(bool dead, float fire, float hull, HullStatus expected) =>
        Assert.Equal(expected, HullStatuses.Of(dead, fire, hull).Status);

    [Fact]
    public void EachStatus_HasItsOwnColour_AndOnlyMeltingFlashes()
    {
        var labels = new[]
        {
            HullStatuses.Of(false, 0f, 1f), HullStatuses.Of(false, 0.5f, 0.3f),
            HullStatuses.Of(false, 1f, 0.01f), HullStatuses.Of(true, 0f, 0f),
        };
        Assert.Equal(labels.Length, labels.Select(l => l.Color).Distinct().Count());
        Assert.Equal([HullStatus.Melting], labels.Where(l => l.Flashes).Select(l => l.Status));
        var melting = labels[2];
        Assert.NotEqual(melting.ColorAt(0f), melting.ColorAt(0.3f));
        // A whole hull's label is its warm neutral and calls no attention; anything worse glows.
        Assert.Equal(HudPalette.HullNeutral, labels[0].Color);
        Assert.Equal([false, true, true, true], labels.Select(l => l.Alarms));
    }

    [Fact]
    public void TheLedgerLines_TextAndBars_NeitherOverlapNorLeaveTheirPlates()
    {
        foreach (var (y, top, bottom) in new[]
        {
            (LedgerLayout.ShieldLineY, LedgerLayout.UpperPlateTop, LedgerLayout.UpperPlateBottom),
            (LedgerLayout.HullLineY, LedgerLayout.LowerPlateTop, LedgerLayout.LowerPlateBottom),
        })
        {
            // Top to bottom: the line's text, then its bar.
            var text = LedgerLayout.Text(y);
            var bar = LedgerLayout.Bar(y);
            Assert.True(text.Top >= top, $"the line at {y} starts at {text.Top}, above its plate");
            Assert.True(bar.Top >= text.Bottom, $"the bar ({bar.Top}) overlaps the text ({text.Bottom})");
            Assert.True(bar.Bottom <= bottom, $"the bar at {y} ends at {bar.Bottom}, below its plate");
        }
        Assert.True(LedgerLayout.LabelSize <= LedgerLayout.NumberSize);
        // No smaller than the one-line readout it replaced (labels 12, numbers 15).
        Assert.True(LedgerLayout.LabelSize >= 12f && LedgerLayout.NumberSize >= 15f);
    }

    /// <summary>
    /// SHIELD 400 and HULL 1000 fit across their plates: each label stays clear of its number, at a generous width for
    /// the HUD font (each character at most <c>Advance</c> of the text size; Chakra Petch's capitals and digits measure
    /// about 0.45).
    /// </summary>
    [Fact]
    public void TheLedgerLines_FitAcrossThePlates()
    {
        const float Advance = 0.5f;
        static float Width(string text, float size) => text.Length * size * Advance;
        float labelEnd = LedgerLayout.LedgerLeft + Width("SHIELD", LedgerLayout.LabelSize);
        float numberStart = LedgerLayout.LedgerRight - Width("1000", LedgerLayout.NumberSize);
        Assert.True(labelEnd + 10f < numberStart, $"SHIELD ends at {labelEnd}, too near its number at {numberStart}");
    }
}
