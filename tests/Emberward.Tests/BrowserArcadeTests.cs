namespace Emberward.Tests;

using System.Text.Json;
using Emberward.Browser.Client;
using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities.Powerups;
using Emberward.Core.Match;
using Emberward.Desktop.Gui;
using Emberward.Desktop.Presentation;

/// <summary>
/// What the browser's page is told of a timed match (<see cref="BrowserArcade"/>, DESIGN.md §6.8): the clock, the
/// board, the drops, the player's running powerups and the last lines, in the desktop's words; and nothing at all in
/// a match without a clock.
/// </summary>
public class BrowserArcadeTests
{
    private const int Fps = Tweaks.Perf.TargetFps;

    private static readonly Func<int, string> Names = SlotNames.Local(0);

    private static readonly MatchRules TimedDrops = PowerupWorlds.Rules with { TimeLimitSeconds = 300 };

    private static ArcadeStatus Capture(World world, DropBoard drops, int slot = 0)
    {
        var status = BrowserArcade.Capture(world, world.TankList.Tanks[slot], drops, SlotNames.Local(slot));
        Assert.NotNull(status);
        return status;
    }

    [Fact]
    public void AnArcadeMatch_CarriesTheClockTheBoardTheNextDropAndTheObjective_AndNoCoreGoal()
    {
        var session = new BrowserSession(1234, players: 4, mode: MatchMode.Arcade);
        session.Advance(default);

        var frame = BrowserFrameBuilder.Capture(session, includeEvents: false);

        var arcade = frame.Arcade;
        Assert.NotNull(arcade);
        Assert.Equal(("8:00", "TIME", nameof(ClockPhase.Running), false), (arcade.Clock, arcade.Caption, arcade.Phase, arcade.Urgent));
        Assert.Equal(HudPalette.Hex(Scoreboards.Of(session.World, 0, Names)!.ClockColor), arcade.ClockColor);
        Assert.Equal(Scoreboards.Objective, arcade.Objective);
        Assert.Equal("YOU,BOT 2,BOT 3,BOT 4", string.Join(',', arcade.Rows.OrderBy(r => r.Slot).Select(r => r.Name)));
        Assert.Equal(0, Assert.Single(arcade.Rows, r => r.Local).Slot);
        Assert.DoesNotContain(arcade.Rows, r => r.Leader);
        Assert.All(arcade.Rows, r => Assert.Matches("^#[0-9a-f]{6}$", r.Color));
        Assert.Equal(4, arcade.Rows.Select(r => r.Color).Distinct().Count());

        var forecast = session.World.PowerupForecast;
        var kind = PowerupPresentations.Get(forecast.Kind);
        var drop = Assert.Single(arcade.Drops);
        Assert.Equal((nameof(DropLineState.Forecast), forecast.Site, true, false), (drop.State, drop.Site, drop.Pointed, drop.Urgent));
        Assert.Equal((kind.Mark.ToString(), kind.Tag, kind.Hex, "in 0:45"), (drop.Mark, drop.Name, drop.Color, drop.When));
        Assert.Matches("^[0-9]+ m$", drop.Detail);
        Assert.Equal($"{kind.Mark}  in 0:45  {drop.Detail}", drop.PointerText);
        Assert.Empty(arcade.Effects);
        Assert.Null(arcade.Over);
        // The clock decides this match: the core's goal lines and its bearing stay off the page.
        Assert.Null(frame.Goal);
    }

    /// <summary>The page reads these names; a renamed field would leave it blank without a test failing elsewhere.</summary>
    [Fact]
    public void TheFramesJson_NamesTheFieldsThePageReads()
    {
        var world = PowerupWorlds.Create(TimedDrops);
        var drops = new DropBoard();
        var player = world.TankList.Tanks[0];
        PowerupWorlds.Take(world, player, PowerupKind.Bulwark);
        drops.AfterStep(world);
        var session = new BrowserSession(7, mode: MatchMode.Arcade);
        var frame = BrowserFrameBuilder.Capture(session, includeEvents: false) with { Arcade = Capture(world, drops) };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(frame, BrowserJsonContext.Default.BrowserFrame));

        var arcade = json.RootElement.GetProperty("arcade");
        foreach (string name in (string[])["clock", "caption", "phase", "clockColor", "urgent", "objective", "over"])
            Assert.True(arcade.TryGetProperty(name, out _), name);
        var row = arcade.GetProperty("rows")[0];
        foreach (string name in (string[])["rank", "slot", "name", "color", "kills", "deaths", "local", "leader"])
            Assert.True(row.TryGetProperty(name, out _), name);
        var drop = arcade.GetProperty("drops")[0];
        foreach (string name in (string[])["state", "mark", "name", "when", "detail", "color", "urgent", "pointed", "pointerText"])
            Assert.True(drop.TryGetProperty(name, out _), name);
        Assert.Equal(JsonValueKind.Number, drop.GetProperty("site").GetProperty("x").ValueKind);
        Assert.Equal(JsonValueKind.Number, drop.GetProperty("site").GetProperty("y").ValueKind);
        var effect = arcade.GetProperty("effects")[0];
        foreach (string name in (string[])["kind", "text", "fraction", "color", "blink"])
            Assert.True(effect.TryGetProperty(name, out _), name);
    }

    [Fact]
    public void AStandardMatch_CarriesNoArcadeHud_AndKeepsItsCoreGoal()
    {
        var session = new BrowserSession(1234);
        session.Advance(default);

        var frame = BrowserFrameBuilder.Capture(session, includeEvents: false);

        Assert.Null(frame.Arcade);
        Assert.NotNull(frame.Goal);
        string json = JsonSerializer.Serialize(frame, BrowserJsonContext.Default.BrowserFrame);
        Assert.Contains("\"arcade\":null", json, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAClock_NothingIsSent_DropsOrNot()
    {
        var world = PowerupWorlds.Create();
        var drops = new DropBoard();
        PowerupWorlds.Step(world);
        drops.AfterStep(world);

        Assert.Null(BrowserArcade.Capture(world, world.TankList.Tanks[0], drops, Names));
    }

    [Fact]
    public void AKill_ReachesTheRows_AndMarksTheSoleLeader()
    {
        var world = ArcadeWorlds.Create(ArcadeWorlds.Timed(300));
        var drops = new DropBoard();
        ArcadeWorlds.Kill(world, killer: 2, world.TankList.Tanks[1]);

        var arcade = Capture(world, drops);

        var first = arcade.Rows[0];
        Assert.Equal((1, 2, "BOT 3", 1, 0, false, true), (first.Rank, first.Slot, first.Name, first.Kills, first.Deaths, first.Local, first.Leader));
        var victim = Assert.Single(arcade.Rows, r => r.Slot == 1);
        Assert.Equal((0, 1, false), (victim.Kills, victim.Deaths, victim.Leader));
        var own = Assert.Single(arcade.Rows, r => r.Local);
        Assert.Equal((0, "YOU", false), (own.Slot, own.Name, own.Leader));
        // A timed match without the drops has no drop lines.
        Assert.Empty(arcade.Drops);
    }

    [Fact]
    public void ADropOnTheGround_HasItsLineUnderTheNextOne_WithHowLongItLies()
    {
        var world = PowerupWorlds.Create(TimedDrops);
        var drops = new DropBoard();
        var site = PowerupWorlds.AwaitLanding(world);
        drops.AfterStep(world);

        var arcade = Capture(world, drops);

        Assert.Equal(nameof(DropLineState.Forecast), arcade.Drops[0].State);
        var landed = Assert.Single(arcade.Drops, d => d.State == nameof(DropLineState.Landed));
        Assert.Equal((site, true), (landed.Site, landed.Pointed));
        Assert.Equal($"LANDED {Tweaks.Powerups.GroundSeconds}s", landed.When);
        Assert.Equal($"{landed.Mark}  {Tweaks.Powerups.GroundSeconds}s left  {landed.Detail}", landed.PointerText);
    }

    [Fact]
    public void ATakenPowerup_IsATagThatDrains_AndANoticeThatNamesTheTaker_AndADeadTankShowsNoTags()
    {
        var world = PowerupWorlds.Create(TimedDrops);
        var drops = new DropBoard();
        var player = world.TankList.Tanks[0];
        PowerupWorlds.Take(world, player, PowerupKind.Bulwark);
        drops.AfterStep(world);

        var arcade = Capture(world, drops);

        var bulwark = PowerupPresentations.Get(PowerupKind.Bulwark);
        var effect = Assert.Single(arcade.Effects);
        Assert.Equal((nameof(PowerupKind.Bulwark), bulwark.Hex, false), (effect.Kind, effect.Color, effect.Blink));
        Assert.Equal($"{bulwark.MarkedTag} {Tweaks.Powerups.BulwarkSeconds}s", effect.Text);
        Assert.InRange(effect.Fraction, 0.95f, 1f);
        var notice = Assert.Single(arcade.Drops, d => d.State == nameof(DropLineState.Taken));
        Assert.Equal(("TAKEN", "by YOU", false), (notice.When, notice.Detail, notice.Pointed));

        // Half its time later the bar is half as long, and the notice has gone.
        for (int i = 0; i < Tweaks.Powerups.BulwarkSeconds * Fps / 2; i++)
        {
            PowerupWorlds.Step(world);
            drops.AfterStep(world);
        }
        arcade = Capture(world, drops);
        Assert.InRange(Assert.Single(arcade.Effects).Fraction, 0.45f, 0.55f);
        Assert.DoesNotContain(arcade.Drops, d => d.State == nameof(DropLineState.Taken));

        Assert.True(player.TakeDamage(ArcadeWorlds.Lethal, default));
        PowerupWorlds.Step(world);
        Assert.True(player.IsDead);
        Assert.Empty(Capture(world, drops).Effects);
    }

    [Fact]
    public void WhenTheClockRunsOut_TheWinnerReadsVictoryAndTheRestWhoWon_AndTheDropLinesGo()
    {
        var world = ArcadeWorlds.Create(ArcadeWorlds.Timed(40) with { Powerups = true });
        var drops = new DropBoard();
        ArcadeWorlds.Kill(world, killer: 0, world.TankList.Tanks[1]);
        Assert.Null(Capture(world, drops).Over);
        Assert.NotEmpty(Capture(world, drops).Drops);

        for (int i = 0; i < 60 * Fps && !world.IsMatchOver; i++)
        {
            world.Advance(_ => default);
            drops.AfterStep(world);
        }
        ArcadeWorlds.AssertWon(world, world.TankList.Tanks[0], MatchWinReason.TopScore);

        var winner = Capture(world, drops);
        var board = Scoreboards.Of(world, 0, Names)!;
        Assert.Equal(("FINAL", nameof(ClockPhase.Final)), (winner.Caption, winner.Phase));
        Assert.NotNull(winner.Over);
        Assert.Equal((true, "VICTORY", Scoreboards.VictoryLine(board), board.OwnResult),
            (winner.Over.Won, winner.Over.Title, winner.Over.Reason, winner.Over.Own));
        Assert.Empty(winner.Drops);
        Assert.True(winner.Rows[0].Leader);

        var loser = Capture(world, drops, slot: 1).Over;
        Assert.NotNull(loser);
        Assert.Equal((false, "DEFEAT"), (loser.Won, loser.Title));
        // Seen from slot 1 the winner is a bot, and the line names it.
        Assert.StartsWith("BOT 1 won with 1 kill", loser.Reason, StringComparison.Ordinal);
    }
}
