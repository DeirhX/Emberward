using Emberward.Core;
using Emberward.Core.Match;
using Emberward.Desktop.Gui;
using static Emberward.Tests.ArcadeWorlds;

namespace Emberward.Tests;

/// <summary>
/// The board and the clock of a timed match as the HUD words them (<see cref="Scoreboards"/>, DESIGN.md §6.8): the
/// rows in rank order with their names, the clock's text and phase, and the lines of the match-over screen.
/// </summary>
public class ScoreboardTests
{
    private static Scoreboard Board(World world, int localSlot = 0) =>
        Scoreboards.Of(world, localSlot, SlotNames.Local(localSlot)) ?? throw new InvalidOperationException("no board");

    private static MatchClock Clock(int remainingTicks, int limitSeconds = 480, bool suddenDeath = false) =>
        new(Active: true, ElapsedTicks: limitSeconds * Fps - remainingTicks, LimitTicks: limitSeconds * Fps, suddenDeath);

    [Fact]
    public void AMatchWithNoClock_HasNoBoard() =>
        Assert.Null(Scoreboards.Of(Create(new MatchRules { TerrainRegrowth = false }), 0, SlotNames.Local(0)));

    [Fact]
    public void AtTheStart_EveryTankIsFirst_NobodyLeads_AndTheClockIsFull()
    {
        var board = Board(Create(Timed(480)), localSlot: 2);

        Assert.Equal(4, board.Rows.Count);
        Assert.All(board.Rows, row => Assert.Equal((1, 0, 0, false), (row.Rank, row.Kills, row.Deaths, row.IsLeader)));
        Assert.Equal(["BOT 1", "BOT 2", "YOU", "BOT 4"], board.Rows.OrderBy(r => r.Slot).Select(r => r.Name));
        Assert.Equal(2, board.Local!.Value.Slot);
        Assert.Null(board.Leader);
        Assert.Equal(("8:00", ClockPhase.Running, "TIME", false), (board.Clock, board.Phase, board.Caption, board.IsUrgent));
        Assert.Equal(HudPalette.Text, board.ClockColor);
    }

    [Fact]
    public void Rows_AreInRankOrder_InTheTanksColours_AndTheSoleLeaderIsMarked()
    {
        var world = Create(Timed(480));
        var tanks = world.TankList.Tanks;
        Kill(world, killer: 1, tanks[0]);
        Kill(world, killer: 1, tanks[2]);
        Kill(world, killer: 3, tanks[0]);

        var board = Board(world);

        // Level on kills, the tank that died less often is listed first; both share the place.
        Assert.Equal([1, 3, 2, 0], board.Rows.Select(r => r.Slot));
        Assert.Equal([1, 2, 3, 3], board.Rows.Select(r => r.Rank));
        Assert.Equal(["1st", "2nd", "3rd", "3rd"], board.Rows.Select(r => r.Place));
        Assert.Equal(["BOT 2", "BOT 4", "BOT 3", "YOU"], board.Rows.Select(r => r.Name));
        Assert.Equal([2, 1, 0, 0], board.Rows.Select(r => r.Kills));
        Assert.Equal([0, 0, 1, 2], board.Rows.Select(r => r.Deaths));
        Assert.Equal([true, false, false, false], board.Rows.Select(r => r.IsLeader));
        Assert.Equal([false, false, false, true], board.Rows.Select(r => r.IsLocal));
        Assert.All(board.Rows, row => Assert.Equal(HudPalette.TankColor(row.Slot), row.Color));
        Assert.All(board.Rows, row => Assert.Matches("^#[0-9a-f]{6}$", row.Hex));
        Assert.Equal("BOT 2", board.Leader!.Value.Name);
        Assert.Equal("3rd place - 0 kills, 2 deaths", board.OwnResult);
    }

    [Fact]
    public void ASpectator_HasNoLineOfItsOwn()
    {
        var board = Board(Create(Timed(60)), localSlot: -1);

        Assert.Null(board.Local);
        Assert.Null(board.OwnResult);
        Assert.DoesNotContain(board.Rows, row => row.Name == SlotNames.You);
    }

    [Theory]
    [InlineData(480 * 24, "8:00")]
    [InlineData(480 * 24 - 1, "8:00")]   // rounded up: a second shows until it is over
    [InlineData(479 * 24, "7:59")]
    [InlineData(61 * 24, "1:01")]
    [InlineData(60 * 24, "1:00")]
    [InlineData(9 * 24 + 1, "0:10")]
    [InlineData(1, "0:01")]
    [InlineData(0, "0:00")]
    public void TheClock_ReadsMinutesAndSeconds_RoundedUp(int remainingTicks, string expected)
    {
        Assert.Equal(24, Fps);
        Assert.Equal(expected, Scoreboards.ClockText(Clock(remainingTicks)));
    }

    [Fact]
    public void PastTheLimit_TheClockCountsTheOvertimeUp()
    {
        Assert.Equal("+0:00", Scoreboards.ClockText(Clock(-1, suddenDeath: true)));
        Assert.Equal("+0:12", Scoreboards.ClockText(Clock(-12 * Fps, suddenDeath: true)));
        Assert.Equal("+1:05", Scoreboards.ClockText(Clock(-65 * Fps, suddenDeath: true)));
    }

    [Theory]
    [InlineData(61, ClockPhase.Running, "TIME")]
    [InlineData(60, ClockPhase.LastMinute, "LAST MINUTE")]
    [InlineData(11, ClockPhase.LastMinute, "LAST MINUTE")]
    [InlineData(10, ClockPhase.LastSeconds, "LAST MINUTE")]
    [InlineData(1, ClockPhase.LastSeconds, "LAST MINUTE")]
    public void TheClocksPhase_FollowsTheTimeLeft(int secondsLeft, ClockPhase expected, string caption)
    {
        ClockPhase phase = Scoreboards.PhaseOf(Clock(secondsLeft * Fps), matchOver: false);

        Assert.Equal(expected, phase);
        Assert.Equal(caption, Scoreboards.Caption(phase));
    }

    [Fact]
    public void SuddenDeathAndTheEnd_ComeBeforeTheTimeLeft()
    {
        Assert.Equal(ClockPhase.SuddenDeath, Scoreboards.PhaseOf(Clock(0, suddenDeath: true), matchOver: false));
        Assert.Equal(ClockPhase.Final, Scoreboards.PhaseOf(Clock(0, suddenDeath: true), matchOver: true));
        Assert.Equal(ClockPhase.Final, Scoreboards.PhaseOf(Clock(200 * Fps), matchOver: true));
        Assert.Equal("SUDDEN DEATH", Scoreboards.Caption(ClockPhase.SuddenDeath));
        Assert.Equal("FINAL", Scoreboards.Caption(ClockPhase.Final));
    }

    [Fact]
    public void TheClocksColour_WarnsFromTheLastMinute_AndAlarmsInTheLastSecondsAndSuddenDeath()
    {
        static Scoreboard At(ClockPhase phase) => new([], "0:00", phase, Scoreboards.Caption(phase));

        Assert.Equal((HudPalette.Text, false), (At(ClockPhase.Running).ClockColor, At(ClockPhase.Running).IsUrgent));
        Assert.Equal((HudPalette.Warn, true), (At(ClockPhase.LastMinute).ClockColor, At(ClockPhase.LastMinute).IsUrgent));
        Assert.Equal((HudPalette.Danger, true), (At(ClockPhase.LastSeconds).ClockColor, At(ClockPhase.LastSeconds).IsUrgent));
        Assert.Equal((HudPalette.Danger, true), (At(ClockPhase.SuddenDeath).ClockColor, At(ClockPhase.SuddenDeath).IsUrgent));
        Assert.Equal((HudPalette.Text, false), (At(ClockPhase.Final).ClockColor, At(ClockPhase.Final).IsUrgent));
    }

    [Fact]
    public void ATimedMatch_GoesThroughItsPhases_ToAFinalBoardThatNamesTheWinner()
    {
        var world = Create(Timed(62));
        var tanks = world.TankList.Tanks;
        Assert.Equal(ClockPhase.Running, Board(world).Phase);

        RunClockDownTo(world, 60 * Fps);
        Assert.Equal(("1:00", ClockPhase.LastMinute), (Board(world).Clock, Board(world).Phase));
        RunClockDownTo(world, 10 * Fps);
        Assert.Equal(("0:10", ClockPhase.LastSeconds), (Board(world).Clock, Board(world).Phase));

        // Nobody has a kill: the clock runs into sudden death and counts up.
        RunClockDownTo(world, 0);
        for (int i = 0; i < 3 * Fps; i++)
            world.Advance(_ => default);
        var tied = Board(world);
        Assert.Equal((ClockPhase.SuddenDeath, "SUDDEN DEATH", "+0:03"), (tied.Phase, tied.Caption, tied.Clock));
        Assert.Null(tied.Leader);

        Kill(world, killer: 1, tanks[0]);
        AssertWon(world, tanks[1], MatchWinReason.TopScore);

        var lost = Board(world, localSlot: 0);
        Assert.Equal((ClockPhase.Final, "FINAL"), (lost.Phase, lost.Caption));
        Assert.StartsWith("+", lost.Clock);
        Assert.Equal("BOT 2 won with 1 kill", Scoreboards.DefeatLine(lost, winnerName: null));
        Assert.Equal("Ada won with 1 kill", Scoreboards.DefeatLine(lost, "Ada"));
        Assert.Equal("2nd place - 0 kills, 1 death", lost.OwnResult);

        var won = Board(world, localSlot: 1);
        Assert.Equal("Most kills when the clock ran out: 1 kill", Scoreboards.VictoryLine(won));
        Assert.Equal("1st place - 1 kill, 0 deaths", won.OwnResult);
    }

    [Fact]
    public void Words_CountAndRank()
    {
        Assert.Equal(("1st", "2nd", "3rd"), (Scoreboards.Ordinal(1), Scoreboards.Ordinal(2), Scoreboards.Ordinal(3)));
        Assert.Equal(("4th", "8th"), (Scoreboards.Ordinal(4), Scoreboards.Ordinal(8)));
        Assert.Equal("0 kills", Scoreboards.Count(0, "kill"));
        Assert.Equal("1 kill", Scoreboards.Count(1, "kill"));
        Assert.Equal("12 deaths", Scoreboards.Count(12, "death"));
        Assert.Equal("0:00", Scoreboards.MinutesSeconds(-5));
        Assert.Equal("10:07", Scoreboards.MinutesSeconds(607));
        Assert.Equal("YOU", SlotNames.Local(3)(3));
        Assert.Equal("BOT 1", SlotNames.Local(3)(0));
        Assert.Contains("most kills", Scoreboards.Objective);
    }
}
