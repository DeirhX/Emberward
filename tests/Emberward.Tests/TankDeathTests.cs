using Emberward.Browser.Client;
using Emberward.Core;
using Emberward.Core.Entities;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Match;
using Emberward.Desktop.Gui;
using Emberward.Desktop.Gui.Guidance;

namespace Emberward.Tests;

/// <summary>
/// Tank deaths on the HUD: <see cref="World.Deaths"/> records each one, an enemy's posts a toast, and the scoreboard
/// (<see cref="TankDeathView"/>) lists every tank with the times it died.
/// </summary>
public class TankDeathTests
{
    private static Tank Enemy(RelayHeatTests.Scenario s) => s.World.TankList.Tanks.First(t => t.Color != s.Tank.Color);

    /// <summary>Steps the world once, letting the watcher hear the step, then lets it post at HUD time <paramref name="now"/>.</summary>
    private static void Step(RelayHeatTests.Scenario s, GameEventWatcher events, NotificationFeed feed, float now)
    {
        s.World.Advance(_ => default);
        events.AfterStep(s.World, s.Tank);
        events.Update(s.World, s.Tank, feed, now);
    }

    [Fact]
    public void ADeath_IsLogged_OnTheStepOfItsEvent_WithItsCause()
    {
        var s = RelayHeatTests.CreateScenario();
        s.World.Advance(_ => default);
        Assert.Empty(s.World.Deaths);

        Tank enemy = Enemy(s);
        enemy.TakeDamage(10_000, DamageCause.Overheat);
        s.World.Advance(_ => default);

        TankDeath death = Assert.Single(s.World.Deaths);
        Assert.Equal(new TankDeath(enemy.Color, s.World.AdvanceCount, DamageCause.Overheat), death);
        StepEvent died = Assert.Single(s.World.StepEvents.ToArray(), e => e.Kind == StepEventKind.TankDied);
        Assert.Equal(DamageCause.Overheat, died.Cause);

        // The wreck waiting to respawn is not a second death.
        for (int i = 0; i < 10; i++)
            s.World.Advance(_ => default);
        Assert.Single(s.World.Deaths);
    }

    [Fact]
    public void AnEnemyDeath_PostsAToastNamingIt_ButTheTanksOwnDoesNot()
    {
        var s = RelayHeatTests.CreateScenario();
        var feed = new NotificationFeed();
        var events = new GameEventWatcher();
        Step(s, events, feed, 0f);

        Tank enemy = Enemy(s);
        enemy.TakeDamage(10_000, DamageCause.EnemyFire);
        Step(s, events, feed, 1f);
        Notification toast = Assert.Single(feed.Active, n => n.Key.StartsWith("tank-died-", StringComparison.Ordinal));
        Assert.Equal($"{TankDeathView.ColorName(enemy.Color)} tank destroyed", toast.Title);
        Assert.Equal(NotificationKind.Good, toast.Kind);

        feed.Clear();
        s.Tank.TakeDamage(10_000, DamageCause.Overheat);
        Step(s, events, feed, 2f);
        Assert.DoesNotContain(feed.Active, n => n.Key.StartsWith("tank-died-", StringComparison.Ordinal));
    }

    [Fact]
    public void ARecall_IsNotToastedAsADeath()
    {
        var events = new GameEventWatcher();
        var feed = new NotificationFeed();
        var s = RelayHeatTests.CreateScenario();
        Step(s, events, feed, 0f);
        Enemy(s).TakeDamage(10_000, DamageCause.Recalled);
        Step(s, events, feed, 1f);
        Assert.DoesNotContain(feed.Active, n => n.Key.StartsWith("tank-died-", StringComparison.Ordinal));
        Assert.Equal(DamageCause.Recalled, Assert.Single(s.World.Deaths).Cause);
    }

    [Fact]
    public void TheScoreboard_ListsEveryTank_WithTheTimesItDied()
    {
        var s = RelayHeatTests.CreateScenario();
        Tank enemy = Enemy(s);
        for (int i = 0; i < 100; i++)
            s.World.Advance(_ => default);
        enemy.TakeDamage(10_000, DamageCause.EnemyFire);
        s.World.Advance(_ => default);
        TimeSpan diedAt = s.World.MatchTimeAt(s.World.AdvanceCount);

        var rows = TankDeathView.Rows(s.World);
        Assert.Equal(s.World.TankList.Count, rows.Count);
        Assert.Empty(rows[s.Tank.Color].Deaths);
        Assert.Equal([new DeathMark(TankDeathView.Clock(diedAt), "shot")], rows[enemy.Color].Deaths);
        Assert.Equal(TankDeathView.ColorName(enemy.Color), rows[enemy.Color].Name);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(7.9, "0:07")]
    [InlineData(187, "3:07")]
    [InlineData(3725, "62:05")]
    public void TheClock_ShowsMinutesAndSeconds(double seconds, string expected) =>
        Assert.Equal(expected, TankDeathView.Clock(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void EveryTankColour_HasAName_AndEveryCauseAWord()
    {
        for (int tank = 0; tank < Emberward.Core.Rendering.TankSprites.TankColors.Length; tank++)
            Assert.DoesNotContain("Tank", TankDeathView.ColorName(tank), StringComparison.Ordinal);
        foreach (DamageCause cause in Enum.GetValues<DamageCause>().Where(c => c != DamageCause.None))
            Assert.NotEqual("destroyed", TankDeathView.CauseWord(cause));
    }

    [Fact]
    public void TheBrowserFrame_CarriesTheScoreboard()
    {
        var s = RelayHeatTests.CreateScenario();
        Tank enemy = Enemy(s);
        enemy.TakeDamage(10_000, DamageCause.Frostbite);
        s.World.Advance(_ => default);

        ScoreboardStatus board = BrowserFrameBuilder.Scoreboard(s.World, s.Tank);
        Assert.Equal(s.Tank.Color, board.You);
        Assert.Equal(s.World.TankList.Count, board.Rows.Length);
        Assert.Equal(["0:00 froze"], board.Rows[enemy.Color].Deaths);
        Assert.Matches("^#[0-9a-f]{6}$", board.Rows[enemy.Color].Color);
    }
}
