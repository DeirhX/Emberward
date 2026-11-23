namespace Emberward.Tests;

using System.Text.Json;
using System.Text.RegularExpressions;
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
        var status = BrowserArcade.Capture(world, world.TankList.Tanks[slot], new ScoreboardCache(), drops, SlotNames.Local(slot));
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
    [Trait("Category", "Slow")]
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

        Assert.Null(BrowserArcade.Capture(world, world.TankList.Tanks[0], new ScoreboardCache(), drops, Names));
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

        // The page reads these names of the last lines (arcade.js, showOver).
        var frame = BrowserFrameBuilder.Capture(new BrowserSession(7, mode: MatchMode.Arcade), includeEvents: false) with { Arcade = winner };
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(frame, BrowserJsonContext.Default.BrowserFrame)))
        {
            var over = json.RootElement.GetProperty("arcade").GetProperty("over");
            foreach (string name in (string[])["won", "title", "reason", "own"])
                Assert.True(over.TryGetProperty(name, out _), name);
        }

        var loser = Capture(world, drops, slot: 1).Over;
        Assert.NotNull(loser);
        Assert.Equal((false, "DEFEAT"), (loser.Won, loser.Title));
        // Seen from slot 1 the winner is a bot, and the line names it.
        Assert.StartsWith("BOT 1 won with 1 kill", loser.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TheClock_TurnsUrgentInItsLastMinute_AndBeatsInItsLastSeconds_AndThePageStylesEveryPhase()
    {
        var world = ArcadeWorlds.Create(ArcadeWorlds.Timed(120));
        var drops = new DropBoard();
        Assert.Equal((nameof(ClockPhase.Running), false), (Capture(world, drops).Phase, Capture(world, drops).Urgent));

        ArcadeWorlds.RunClockDownTo(world, 59 * Fps);
        var lastMinute = Capture(world, drops);
        Assert.Equal((nameof(ClockPhase.LastMinute), Scoreboards.Caption(ClockPhase.LastMinute), true, "0:59"),
            (lastMinute.Phase, lastMinute.Caption, lastMinute.Urgent, lastMinute.Clock));

        ArcadeWorlds.RunClockDownTo(world, 5 * Fps);
        var lastSeconds = Capture(world, drops);
        Assert.Equal((nameof(ClockPhase.LastSeconds), true), (lastSeconds.Phase, lastSeconds.Urgent));

        // The page tells the phases apart by the class arcade.js gives the plate (phase-<name>); the beat is styled.
        string css = PageFile("style.css");
        foreach (var phase in (ClockPhase[])[ClockPhase.LastSeconds, ClockPhase.SuddenDeath])
            Assert.Contains($"#arcade-clock.phase-{phase}", css, StringComparison.Ordinal);
        Assert.Contains("phase-${a.phase}", PageFile("arcade.js"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Every element arcade.js looks up is on the page and styled: a missing one would throw on the first arcade
    /// frame, an unstyled one would stand in the page's flow over the world.
    /// </summary>
    [Fact]
    public void ThePage_HasAndStylesEveryElementTheArcadeHudWritesTo()
    {
        string script = PageFile("arcade.js"), html = PageFile("index.html"), css = PageFile("style.css");

        var ids = Regex.Matches(script, @"\$\(""([a-z-]+)""\)").Select(m => m.Groups[1].Value)
            .Concat(Regex.Matches(script, @"""#([a-z-]+)""").Select(m => m.Groups[1].Value))
            .Concat(["arcade-clock", "scoreboard", "effects", "arcade-dim", "final", "final-foot"])
            .Distinct().ToArray();
        Assert.Contains("drop-lines", ids);
        foreach (string id in ids)
            Assert.Contains($"id=\"{id}\"", html, StringComparison.Ordinal);
        foreach (string id in (string[])["arcade-clock", "scoreboard", "effects", "arcade-dim", "final", "drop-lines", "score-rows"])
            Assert.Contains($"\n#{id} {{", css, StringComparison.Ordinal);
    }

    internal static string PageFile(string name) =>
        File.ReadAllText(Path.Combine(EntityLayoutGuardTests.FindRepositoryRoot(), "src", "Emberward.Browser", "wwwroot", name));
}

/// <summary>
/// A whole local arcade match as the browser plays it, bots and all. A class of its own: it is slow, and xUnit runs a
/// class's tests one after another.
/// </summary>
public class BrowserArcadeMatchTests
{
    private const int Fps = Tweaks.Perf.TargetFps;

    /// <summary>
    /// A local arcade match in the browser, as the page starts it (<c>?mode=arcade&amp;players=4</c>): bots in every
    /// other slot, the forecast counting down to the first drop and, once it lands, its line with how long it lies.
    /// </summary>
    [Fact]
    [Trait("Category", "Slow")]
    public void ALocalArcadeMatch_ShowsTheForecastCountingDown_AndTheFirstDropLanding()
    {
        var session = new BrowserSession(1234, players: BrowserLaunch.Players(4), mode: BrowserLaunch.Mode("ARCADE"));
        Assert.Equal((MatchMode.Arcade, 4), (session.Mode, session.World.TankList.Count));
        Assert.True(session.Advance(default));
        var first = BrowserFrameBuilder.Capture(session, includeEvents: false).Arcade;
        Assert.NotNull(first);
        Assert.Equal("in 0:45", Assert.Single(first.Drops).When);

        int bound = (Tweaks.Powerups.GroundSeconds + 60) * Fps;
        int steps = 0;
        while (session.World.PowerupDropCount == 0)
        {
            Assert.True(++steps < bound, $"no drop landed in {bound} steps");
            Assert.True(session.Advance(default), "the match ended before the first drop");
        }

        var arcade = BrowserFrameBuilder.Capture(session, includeEvents: true).Arcade;
        Assert.NotNull(arcade);
        var landed = Assert.Single(arcade.Drops, d => d.State == nameof(DropLineState.Landed));
        Assert.StartsWith("LANDED ", landed.When, StringComparison.Ordinal);
        Assert.StartsWith(landed.Mark, landed.PointerText, StringComparison.Ordinal);
        Assert.True(landed.Pointed);
        // The next one is forecast as soon as this one lands: its line comes first.
        Assert.Equal(nameof(DropLineState.Forecast), arcade.Drops[0].State);
        Assert.Equal(4, arcade.Rows.Length);
    }
}
