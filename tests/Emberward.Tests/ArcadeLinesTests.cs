using System.Numerics;
using Emberward.Desktop.Gui;

namespace Emberward.Tests;

/// <summary>
/// Where the drop pointers go: the tags so that two never lie over each other (<see cref="ArcadeLines.ClearOf"/>), the
/// badges side by side (<see cref="ArcadeLines.Beside"/>), and which sites a panel of the HUD hides
/// (<see cref="ArcadeLines.IsUnder"/>).
/// </summary>
public class ArcadeLinesTests
{
    private static (Vector2 Min, Vector2 Max) Box(Vector2 centre, Vector2 half) => (centre - half, centre + half);

    private static bool Overlap((Vector2 Min, Vector2 Max) a, (Vector2 Min, Vector2 Max) b) =>
        a.Max.X > b.Min.X && a.Min.X < b.Max.X && a.Max.Y > b.Min.Y && a.Min.Y < b.Max.Y;

    [Fact]
    public void ATagWithNothingInItsWay_StaysWhereItIsWanted()
    {
        var wanted = new Vector2(300f, 120f);
        var half = new Vector2(60f, 14f);

        Assert.Equal(wanted, ArcadeLines.ClearOf([], wanted, half));
        Assert.Equal(wanted, ArcadeLines.ClearOf([Box(new Vector2(600f, 120f), half)], wanted, half));
    }

    [Fact]
    public void ATagOverAnother_GoesUnderIt()
    {
        var half = new Vector2(60f, 14f);
        var first = Box(new Vector2(300f, 120f), half);

        Vector2 placed = ArcadeLines.ClearOf([first], new Vector2(320f, 126f), half);

        Assert.Equal(320f, placed.X);
        Assert.True(placed.Y - half.Y >= first.Max.Y);
        Assert.False(Overlap(first, Box(placed, half)));
    }

    [Fact]
    public void ATagPushedOntoAThird_GoesUnderThatToo()
    {
        var half = new Vector2(60f, 14f);
        var taken = new List<(Vector2, Vector2)> { Box(new Vector2(300f, 150f), half), Box(new Vector2(300f, 120f), half) };

        Vector2 placed = ArcadeLines.ClearOf(taken, new Vector2(300f, 121f), half);

        Assert.All(taken, box => Assert.False(Overlap(box, Box(placed, half))));
    }

    /// <summary>
    /// The first version moved a tag to exactly under the box in its way and looked again; where the sum rounded down
    /// it still overlapped, and the frame never ended. Coordinates that round badly must place and return.
    /// </summary>
    [Fact]
    public void PlacingATag_AlwaysReturns_WhateverTheRounding()
    {
        var random = new Random(7);
        for (int run = 0; run < 2000; run++)
        {
            var half = new Vector2(20f + random.NextSingle() * 90f, 9.3f + random.NextSingle() * 11f);
            var taken = new List<(Vector2, Vector2)>();
            for (int i = 0; i < 6; i++)
            {
                var wanted = new Vector2(600f + random.NextSingle() * 80f, 137.77f + random.NextSingle() * 30f);
                Vector2 placed = ArcadeLines.ClearOf(taken, wanted, half);
                Assert.All(taken, box => Assert.False(Overlap(box, Box(placed, half))));
                taken.Add(Box(placed, half));
            }
        }
    }

    [Fact]
    public void ASiteUnderAPanel_OrJustBesideIt_IsHidden()
    {
        var panels = new List<(Vector2, Vector2)> { (new Vector2(0f, 0f), new Vector2(484f, 160f)), (new Vector2(866f, 0f), new Vector2(1280f, 375f)) };

        Assert.True(ArcadeLines.IsUnder(panels, new Vector2(60f, 90f), pad: 28f));
        Assert.True(ArcadeLines.IsUnder(panels, new Vector2(1000f, 300f), pad: 28f));
        // A marker half under the panel's edge cannot be read either.
        Assert.True(ArcadeLines.IsUnder(panels, new Vector2(850f, 300f), pad: 28f));
        Assert.False(ArcadeLines.IsUnder(panels, new Vector2(640f, 300f), pad: 28f));
        Assert.False(ArcadeLines.IsUnder(panels, new Vector2(60f, 200f), pad: 28f));
        Assert.False(ArcadeLines.IsUnder([], new Vector2(60f, 90f), pad: 28f));
    }

    [Fact]
    public void ABadgeOnAnother_GoesBesideIt_AndOneClearOfItStays()
    {
        var side = new Vector2(1f, 0f);
        var first = new Vector2(600f, 200f);

        Assert.Equal(new Vector2(700f, 200f), ArcadeLines.Beside([first], new Vector2(700f, 200f), side, apart: 44f));
        Assert.Equal(new Vector2(644f, 200f), ArcadeLines.Beside([first], new Vector2(610f, 204f), side, apart: 44f));
        Assert.Equal(new Vector2(556f, 200f), ArcadeLines.Beside([first], new Vector2(590f, 204f), side, apart: 44f));
        // Exactly on it: either side will do, and it must not stay.
        Vector2 placed = ArcadeLines.Beside([first], first, side, apart: 44f);
        Assert.Equal(44f, Vector2.Distance(first, placed));
    }
}
