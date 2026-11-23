using Emberward.Core.Entities.Powerups;
using Emberward.Desktop.Gui;
using Emberward.Desktop.Presentation;
using Emberward.Desktop.Rendering.Entities;

namespace Emberward.Tests;

/// <summary>
/// The table that says how each kind of powerup is shown (<see cref="PowerupPresentations"/>): every kind has a row,
/// and a row can be told from the others and from a tank without its colour.
/// </summary>
public class PowerupPresentationTests
{
    private const int TankSlots = 8;

    public static TheoryData<PowerupKind> Kinds()
    {
        var kinds = new TheoryData<PowerupKind>();
        foreach (var kind in Enum.GetValues<PowerupKind>())
            kinds.Add(kind);
        return kinds;
    }

    [Fact]
    public void EveryKind_HasItsRow()
    {
        Assert.Equal(PowerupKinds.Count, PowerupPresentations.All.Count);
        foreach (var kind in Enum.GetValues<PowerupKind>())
            Assert.Equal(kind, PowerupPresentations.Get(kind).Kind);
        Assert.Throws<ArgumentOutOfRangeException>(() => PowerupPresentations.Get((PowerupKind)PowerupKinds.Count));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void ARow_HasWordsTheHudAndTheEntityLayerCanWrite(PowerupKind kind)
    {
        var row = PowerupPresentations.Get(kind);

        Assert.False(string.IsNullOrWhiteSpace(row.Name));
        Assert.False(string.IsNullOrWhiteSpace(row.Effect));
        Assert.EndsWith(".", row.Effect);
        Assert.InRange(row.Tag.Length, 1, PowerupPresentations.MaxTagLength);
        Assert.Equal(row.Name.ToUpperInvariant(), row.Tag);
        Assert.Equal($"{row.Mark} {row.Tag}", row.MarkedTag);
        // The label over a site and the letter in it are drawn from the entity layer's own glyphs.
        Assert.True(PowerupQuads.CanWrite(row.Tag), $"the entity layer has no glyph for a character of {row.Tag}");
        Assert.True(PowerupQuads.CanWrite(row.Mark.ToString()), $"the entity layer has no glyph for the mark '{row.Mark}'");
        Assert.False(char.IsWhiteSpace(row.Mark));
        Assert.False(PowerupQuads.CanWrite("\u00e9"));
        Assert.Equal(PowerupKinds.IsTimed(kind), row.IsTimed);
        Assert.Equal(HudPalette.Hex(row.Color), row.Hex);
        Assert.Matches("^#[0-9a-f]{6}$", row.Hex);
    }

    [Fact]
    public void ATimedKind_SaysHowLongItRuns_AndAnInstantOneDoesNot()
    {
        foreach (var row in PowerupPresentations.All)
        {
            if (row.IsTimed)
                Assert.Contains($"{PowerupKinds.DurationSeconds(row.Kind):0} s", row.Effect);
            else
                Assert.Contains("at once", row.Effect);
        }
    }

    [Fact]
    public void Rows_AreToldApart_ByNameMarkAndColour()
    {
        var rows = PowerupPresentations.All;
        Assert.Equal(rows.Count, rows.Select(r => r.Name).Distinct().Count());
        Assert.Equal(rows.Count, rows.Select(r => r.Tag).Distinct().Count());
        Assert.Equal(rows.Count, rows.Select(r => r.Mark).Distinct().Count());
        for (int a = 0; a < rows.Count; a++)
            for (int b = a + 1; b < rows.Count; b++)
                Assert.True(Distance(rows[a].Color, rows[b].Color) > 90f,
                    $"{rows[a].Name} and {rows[b].Name} are too alike in colour ({Distance(rows[a].Color, rows[b].Color):0})");
    }

    [Fact]
    public void NoKind_HasATanksColour()
    {
        foreach (var row in PowerupPresentations.All)
            for (int slot = 0; slot < TankSlots; slot++)
                Assert.True(Distance(row.Color, HudPalette.TankColor(slot)) > 60f,
                    $"{row.Name} is too close to the colour of tank {slot} ({Distance(row.Color, HudPalette.TankColor(slot)):0})");
    }

    [Fact]
    public void TheEntityLayersColour_IsTheHudsColour()
    {
        foreach (var row in PowerupPresentations.All)
        {
            var c = PowerupQuads.ColorOf(row.Kind);
            Assert.Equal(row.Hex, $"#{c.R:x2}{c.G:x2}{c.B:x2}");
        }
    }

    // ABGR colours, compared by their red, green and blue.
    private static float Distance(uint a, uint b)
    {
        float r = (int)(a & 0xFF) - (int)(b & 0xFF);
        float g = (int)((a >> 8) & 0xFF) - (int)((b >> 8) & 0xFF);
        float bl = (int)((a >> 16) & 0xFF) - (int)((b >> 16) & 0xFF);
        return MathF.Sqrt(r * r + g * g + bl * bl);
    }
}
