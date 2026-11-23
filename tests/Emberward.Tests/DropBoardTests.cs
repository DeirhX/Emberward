using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities.Powerups;
using Emberward.Core.Rendering;
using Emberward.Core.Types;
using Emberward.Desktop.Gui;
using Emberward.Desktop.Presentation;
using static Emberward.Tests.PowerupWorlds;

namespace Emberward.Tests;

/// <summary>
/// What the HUD says of the powerup drops (<see cref="DropBoard"/>, DESIGN.md §6.8): at any moment what drops next,
/// where and when; the drops on the ground and how long they lie; who took the last one; and the tags of the
/// powerups running on the player's tank.
/// </summary>
public class DropBoardTests
{
    private static readonly Func<int, string> Names = SlotNames.Local(0);

    /// <summary>Steps the world with the board listening, as the HUD does.</summary>
    private static void Step(World world, DropBoard board, int ticks = 1)
    {
        for (int i = 0; i < ticks; i++)
        {
            PowerupWorlds.Step(world);
            board.AfterStep(world);
        }
    }

    [Fact]
    public void WithoutDrops_TheBoardIsEmpty()
    {
        var world = ArcadeWorlds.Create(ArcadeWorlds.Timed(60));
        var board = new DropBoard();

        Step(world, board, 10);

        Assert.Empty(board.Lines(world, world.TankList.Tanks[0], Names));
        Assert.Empty(DropBoard.Effects(world.PowerupsOf(world.TankList.Tanks[0])));
    }

    [Fact]
    public void FromTheFirstStep_TheBoardSaysWhatDropsNext_WhenAndHowFar()
    {
        var world = Create();
        var player = world.TankList.Tanks[0];
        var board = new DropBoard();
        Step(world, board);

        PowerupForecast forecast = world.PowerupForecast;
        DropLine line = Assert.Single(board.Lines(world, player, Names));

        var kind = PowerupPresentations.Get(forecast.Kind);
        Assert.Equal((DropLineState.Forecast, forecast.Kind, forecast.Site), (line.State, line.Kind, line.Site));
        Assert.Equal(kind.MarkedTag, line.Tag);
        Assert.Equal("in 0:45", line.When);
        Assert.Equal(DropBoard.Distance(player.Position, forecast.Site), line.Detail);
        Assert.Matches("^[0-9]+ m$", line.Detail);
        Assert.Equal("", line.Left);
        Assert.Equal((kind.Color, kind.Hex), (line.Color, line.Hex));
        Assert.False(line.Urgent);
        Assert.True(line.IsPointedAt);
        Assert.Equal($"{kind.MarkedTag}  in 0:45  {line.Detail}", line.Text);
    }

    [Fact]
    public void AForecastLine_CountsDownInMinutesAndSeconds_AndIsUrgentInItsLastSeconds()
    {
        var site = new Position(40, 30);
        DropLine At(int ticks) =>
            DropBoard.ForecastLine(new PowerupForecast(true, site, PowerupKind.Overcharge, ticks), from: default);

        Assert.Equal("X OVERCHARGE  in 0:32  50 m", At(32 * Fps).Text);
        Assert.Equal("X  in 0:32  50 m", At(32 * Fps).PointerText);
        Assert.Equal("in 1:15", At(75 * Fps).When);
        // Rounded up, like the clock: 0:00 only at the landing itself.
        Assert.Equal("in 0:01", At(1).When);
        Assert.Equal("in 0:00", At(0).When);
        Assert.False(At(DropBoard.UrgentSeconds * Fps + 1).Urgent);
        Assert.True(At(DropBoard.UrgentSeconds * Fps).Urgent);
    }

    [Fact]
    public void ALandedLine_SaysHowFar_AndHowLongTheDropStillLies()
    {
        var cell = new Position(27, 36);
        DropLine At(int ticks) => DropBoard.LandedLine(new PowerupRenderState(cell, PowerupKind.Bulwark, ticks, 3.5f), from: default);

        DropLine line = At(52 * Fps);
        Assert.Equal((DropLineState.Landed, PowerupKind.Bulwark, cell), (line.State, line.Kind, line.Site));
        Assert.Equal("B BULWARK  LANDED  45 m", line.Text);
        Assert.Equal("52s", line.Left);
        Assert.Equal("B  52s left  45 m", line.PointerText);
        Assert.True(line.IsPointedAt);
        Assert.False(line.Urgent);
        Assert.Equal("52s", At(51 * Fps + 1).Left);
        Assert.True(At(DropBoard.UrgentSeconds * Fps).Urgent);
        Assert.Equal("1s", At(1).Left);
    }

    [Fact]
    public void ADropOnTheGround_HasItsLineUnderTheForecastOfTheNext()
    {
        var world = Create();
        var player = world.TankList.Tanks[0];
        var board = new DropBoard();
        PowerupForecast first = world.PowerupForecast;
        while (world.PowerupDropCount == 0)
            Step(world, board);

        var lines = board.Lines(world, player, Names);

        Assert.Equal([DropLineState.Forecast, DropLineState.Landed], lines.Select(l => l.State));
        Assert.Equal((first.Kind, first.Site), (lines[1].Kind, lines[1].Site));
        Assert.Equal($"{Tweaks.Powerups.GroundSeconds}s", lines[1].Left);
        Assert.Equal($"in 0:{Tweaks.Powerups.DropPeriodSeconds:00}", lines[0].When);
    }

    [Fact]
    public void OneDropLiesAtATime_SoTheBoardNeverListsTwoOnTheGround()
    {
        var world = Create();
        var player = world.TankList.Tanks[0];
        var board = new DropBoard();
        while (world.PowerupDropCount == 0)
            Step(world, board);

        // Through the landing of the next two: the old drop is gone in the step the new one lands.
        for (int tick = 0; tick < 2 * Tweaks.Powerups.DropPeriodSeconds * Fps + Fps; tick++)
        {
            Step(world, board);
            var lines = board.Lines(world, player, Names);
            Assert.Equal(DropLineState.Forecast, lines[0].State);
            Assert.True(lines.Count(l => l.State == DropLineState.Landed) <= 1, $"two drops listed on the ground at tick {world.AdvanceCount}");
        }
    }

    [Fact]
    public void ATakenDrop_NamesWhoTookIt_ForAFewSeconds()
    {
        var world = Create();
        var player = world.TankList.Tanks[0];
        var board = new DropBoard();
        Position site = Take(world, world.TankList.Tanks[1], PowerupKind.Drill);
        board.AfterStep(world);

        DropLine taken = Assert.Single(board.Lines(world, player, Names), l => l.State == DropLineState.Taken);
        Assert.Equal((PowerupKind.Drill, site), (taken.Kind, taken.Site));
        Assert.Equal("D DRILL  TAKEN  by BOT 2", taken.Text);
        Assert.False(taken.IsPointedAt);
        Assert.False(taken.Urgent);
        Assert.DoesNotContain(board.Lines(world, player, Names), l => l.State == DropLineState.Landed && l.Site == site);

        Step(world, board, DropBoard.NoticeSeconds * Fps - 1);
        Assert.Contains(board.Lines(world, player, Names), l => l.State == DropLineState.Taken);
        Step(world, board);
        Assert.DoesNotContain(board.Lines(world, player, Names), l => l.State == DropLineState.Taken);
    }

    [Fact]
    public void TheDropThePlayerTook_SaysYou()
    {
        var world = Create();
        var player = world.TankList.Tanks[0];
        var board = new DropBoard();
        Take(world, player, PowerupKind.Overcharge);
        board.AfterStep(world);

        DropLine taken = Assert.Single(board.Lines(world, player, Names), l => l.State == DropLineState.Taken);

        Assert.Equal("by YOU", taken.Detail);
        Assert.Equal("by Ada", Assert.Single(board.Lines(world, player, _ => "Ada"), l => l.State == DropLineState.Taken).Detail);
    }

    [Fact]
    public void ADropNobodyTook_IsGone_AndTheBoardSaysSoForAMoment()
    {
        var world = Create();
        var player = world.TankList.Tanks[0];
        var board = new DropBoard();
        while (world.PowerupDropCount == 0)
            Step(world, board);
        PowerupKind kind = board.Lines(world, player, Names)[1].Kind;

        Step(world, board, Tweaks.Powerups.GroundTicks);

        DropLine gone = Assert.Single(board.Lines(world, player, Names), l => l.State == DropLineState.Gone);
        Assert.Equal(kind, gone.Kind);
        Assert.Equal($"{PowerupPresentations.Get(kind).MarkedTag}  GONE", gone.Text);
        Assert.Equal(("", ""), (gone.Detail, gone.Left));
        Assert.False(gone.IsPointedAt);
    }

    [Fact]
    public void ANewMatch_StartsTheBoardOver()
    {
        var world = Create();
        var board = new DropBoard();
        Take(world, world.TankList.Tanks[1], PowerupKind.Bulwark);
        board.AfterStep(world);
        Assert.Contains(board.Lines(world, world.TankList.Tanks[0], Names), l => l.State == DropLineState.Taken);

        // The next match reaches the same tick while the last one's notice would still be up.
        var next = Create();
        while (next.AdvanceCount < world.AdvanceCount)
            PowerupWorlds.Step(next);

        Assert.DoesNotContain(board.Lines(next, next.TankList.Tanks[0], Names), l => l.State is DropLineState.Taken or DropLineState.Gone);
    }

    [Fact]
    public void EffectTags_CountDownEachRunningPowerup_InKindOrder_AndBlinkAtTheEnd()
    {
        var view = new TankPowerupView(Active: true, OverchargeTicks: Tweaks.Powerups.OverchargeSeconds * Fps, BulwarkTicks: 0, DrillTicks: 3 * Fps);

        var tags = DropBoard.Effects(view);

        Assert.Equal([PowerupKind.Overcharge, PowerupKind.Drill], tags.Select(t => t.Kind));
        Assert.Equal($"X OVERCHARGE {Tweaks.Powerups.OverchargeSeconds}s", tags[0].Text);
        Assert.Equal((1f, false), (tags[0].Fraction, tags[0].Blink));
        Assert.Equal(("D DRILL", "3s", true), (tags[1].Tag, tags[1].Seconds, tags[1].Blink));
        Assert.Equal(3f / Tweaks.Powerups.DrillSeconds, tags[1].Fraction, precision: 4);
        Assert.Equal((PowerupPresentations.Drill.Color, PowerupPresentations.Drill.Hex), (tags[1].Color, tags[1].Hex));

        Assert.Empty(DropBoard.Effects(new TankPowerupView(Active: true, 0, 0, 0)));
        Assert.Empty(DropBoard.Effects(default));
        // Rounded up: the last second reads 1s, never 0s.
        Assert.Equal("1s", DropBoard.Effects(new TankPowerupView(true, 0, 1, 0))[0].Seconds);
        Assert.False(DropBoard.Effects(new TankPowerupView(true, 0, DropBoard.UrgentSeconds / 2 * Fps + 1, 0))[0].Blink);
    }

    [Fact]
    public void TheTankThatTookATimedPowerup_HasItsTag_AndOneThatTookAnOverhaulHasNone()
    {
        var world = Create();
        var tank = world.TankList.Tanks[0];
        Take(world, tank, PowerupKind.Drill);

        EffectTag tag = Assert.Single(DropBoard.Effects(world.PowerupsOf(tank)));
        Assert.Equal(PowerupKind.Drill, tag.Kind);
        Assert.Equal($"D DRILL {Tweaks.Powerups.DrillSeconds}s", tag.Text);
        Assert.Empty(DropBoard.Effects(world.PowerupsOf(world.TankList.Tanks[1])));

        var other = Create();
        Take(other, other.TankList.Tanks[0], PowerupKind.Overhaul);
        Assert.Empty(DropBoard.Effects(other.PowerupsOf(other.TankList.Tanks[0])));
    }

    [Fact]
    public void Distances_AreInWholeCells()
    {
        Assert.Equal("0 m", DropBoard.Distance(new Position(5, 5), new Position(5, 5)));
        Assert.Equal("5 m", DropBoard.Distance(new Position(10, 10), new Position(13, 14)));
        Assert.Equal("120 m", DropBoard.Distance(new Position(0, 60), new Position(120, 60)));
    }
}
