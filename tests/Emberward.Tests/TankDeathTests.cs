using Emberward.Browser.Client;
using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities;
using Emberward.Core.Entities.Machines;
using Emberward.Core.Entities.Projectiles;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Match;
using Emberward.Core.Terrain;
using Emberward.Core.Types;
using Emberward.Desktop.Gui;
using Emberward.Desktop.Gui.Guidance;

namespace Emberward.Tests;

/// <summary>
/// Tank deaths on the HUD: <see cref="World.Deaths"/> records each one and who dealt the killing hit, another tank's
/// posts a toast saying who killed it, and the scoreboard (<see cref="TankDeathView"/>) lists every tank with its kills
/// and the times it died.
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
        Assert.Equal("PLAYER 2: Shot down.", toast.Detail);
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
        Assert.Equal(1, board.Rows[enemy.Color].DeathCount);
        Assert.Matches("^#[0-9a-f]{6}$", board.Rows[enemy.Color].Color);
    }

    /// <summary>The scenario's tank, out of its base in an open pocket and past its respawn shield.</summary>
    private static RelayHeatTests.Scenario ExposedInTheOpen()
    {
        var s = RelayHeatTests.CreateScenario();
        for (int dy = -12; dy <= 12; dy++)
            for (int dx = -12; dx <= 12; dx++)
                s.World.Terrain.SetPixel(s.RelayPos + new Offset(dx, dy), TerrainPixel.Blank);
        s.Tank.Position = s.RelayPos;
        for (int i = 0; i < Tank.SpawnProtectionDuration + 2; i++)
            s.World.Advance(_ => default);
        Assert.Equal(0, s.Tank.SpawnProtectionTicks);
        return s;
    }

    /// <summary>Fires <paramref name="shot"/> into the scenario's tank and steps until it dies.</summary>
    private static List<StepEvent> ShootDown(RelayHeatTests.Scenario s, Projectile shot)
    {
        s.World.Projectiles.Add(shot);
        var events = new List<StepEvent>();
        for (int i = 0; i < 60 && !events.Any(e => e.Kind == StepEventKind.TankDied); i++)
        {
            s.World.Advance(_ => default);
            events.AddRange(s.World.StepEvents.ToArray());
        }
        return events;
    }

    [Fact]
    public void AnEnemyShell_IsTheKiller_InTheLog_TheEvent_AndTheDeathScreen()
    {
        var s = ExposedInTheOpen();
        int enemy = Enemy(s).Color;
        var events = ShootDown(s, Projectile.CreateBullet(s.Tank.Position + new Offset(-10, 0),
            new VectorF(Tweaks.Weapon.CannonBulletSpeed, 0), enemy, damage: 100_000));

        var expected = new Killer(enemy);
        Assert.Equal(expected, Assert.Single(events, e => e.Kind == StepEventKind.TankDied).By);
        Assert.Equal(expected, Assert.Single(s.World.Deaths).By);
        Assert.Equal(expected, s.Tank.DeathBy);
        Assert.Equal($"Destroyed by {TankDeathView.ColorName(enemy)}", TankDeathView.DeathLine(s.Tank));
        Assert.Equal($"shot by {TankDeathView.ColorName(enemy)}", TankDeathView.Rows(s.World)[s.Tank.Color].Deaths[0].Cause);
        Assert.Equal(1, TankDeathView.Rows(s.World)[enemy].Kills);

        // Once the tank is back, it is no longer dead to anyone.
        for (int i = 0; i < 400 && s.Tank.IsDead; i++)
            s.World.Advance(_ => default);
        Assert.False(s.Tank.IsDead);
        Assert.Null(s.Tank.DeathBy);
    }

    [Fact]
    public void ATurretsShot_NamesTheTurret_AsTheKiller()
    {
        var s = ExposedInTheOpen();
        int enemy = Enemy(s).Color;
        ShootDown(s, Projectile.CreateBullet(s.Tank.Position + new Offset(-10, 0),
            new VectorF(Tweaks.Weapon.CannonBulletSpeed, 0), enemy, damage: 100_000, firedBy: MachineType.Turret));

        Assert.Equal(new Killer(enemy, MachineType.Turret), Assert.Single(s.World.Deaths).By);
        Assert.Equal($"Destroyed by {TankDeathView.ColorName(enemy)}'s turret", TankDeathView.DeathLine(s.Tank));
        Assert.Equal($"{TankDeathView.ColorName(enemy)}'s turret", TankDeathView.Rows(s.World)[s.Tank.Color].Deaths[0].Cause);
    }

    [Fact]
    public void AKillByThePlayer_IsToastedAsTheirs()
    {
        var s = RelayHeatTests.CreateScenario();
        var feed = new NotificationFeed();
        var events = new GameEventWatcher();
        Step(s, events, feed, 0f);

        Tank enemy = Enemy(s);
        enemy.TakeDamage(100_000, DamageCause.EnemyFire, by: new Killer(s.Tank.Color));
        Step(s, events, feed, 1f);
        Notification toast = Assert.Single(feed.Active, n => n.Key.StartsWith("tank-died-", StringComparison.Ordinal));
        Assert.Equal($"You destroyed the {TankDeathView.ColorName(enemy.Color)} tank", toast.Title);
        Assert.Equal(NotificationKind.Good, toast.Kind);
        Assert.Equal(1, TankDeathView.Rows(s.World)[s.Tank.Color].Kills);
    }

    /// <summary>Kills <paramref name="tank"/> by overheating (which a respawn shield does not stop) and steps until it is back.</summary>
    private static void DieAndRespawn(RelayHeatTests.Scenario s, Tank tank)
    {
        tank.TakeDamage(100_000, DamageCause.Overheat);
        for (int i = 0; i < 20_000 && (tank.IsDead || i == 0); i++)
            s.World.Advance(_ => default);
        Assert.False(tank.IsDead);
    }

    [Fact]
    public void ARecall_IsNotADeathOnTheScoreboard()
    {
        var s = RelayHeatTests.CreateScenario();
        Tank enemy = Enemy(s);
        enemy.TakeDamage(10_000, DamageCause.Recalled);
        s.World.Advance(_ => default);

        ScoreRow row = TankDeathView.Rows(s.World)[enemy.Color];
        Assert.Equal(0, row.DeathCount);
        Assert.Empty(row.Deaths);
        Assert.Equal("no deaths", TankDeathView.TimesLine(row));
    }

    [Fact]
    public void ALongMatch_ListsTheLatestDeaths_AndCountsTheRest()
    {
        var s = RelayHeatTests.CreateScenario();
        Tank enemy = Enemy(s);
        int total = TankDeathView.MaxShownDeaths + 2;
        for (int i = 0; i < total; i++)
            DieAndRespawn(s, enemy);

        ScoreRow row = TankDeathView.Rows(s.World)[enemy.Color];
        Assert.Equal(total, row.DeathCount);
        Assert.Equal(TankDeathView.MaxShownDeaths, row.Deaths.Count);
        Assert.StartsWith("+2 earlier    ", TankDeathView.TimesLine(row), StringComparison.Ordinal);
        Assert.Equal(total, BrowserFrameBuilder.Scoreboard(s.World, s.Tank).Rows[enemy.Color].DeathCount);
    }

    [Fact]
    public void OtherSidesDeaths_ShareOneToast_ButAKillByThePlayerKeepsItsOwn()
    {
        var s = RelayHeatTests.CreateScenario();
        var feed = new NotificationFeed();
        var events = new GameEventWatcher();
        Step(s, events, feed, 0f);

        Tank enemy = Enemy(s);
        enemy.TakeDamage(100_000, DamageCause.Overheat);
        Step(s, events, feed, 1f);
        Assert.Single(feed.Active, n => n.Key == "tank-died-other");

        for (int i = 0; i < 20_000 && enemy.IsDead; i++)
            Step(s, events, feed, 2f);
        enemy.TakeDamage(100_000, DamageCause.Frostbite);
        Step(s, events, feed, 3f);
        Assert.Single(feed.Active, n => n.Key == "tank-died-other");

        for (int i = 0; i < 20_000 && enemy.IsDead; i++)
            Step(s, events, feed, 4f);
        enemy.TakeDamage(100_000, DamageCause.Overheat, by: new Killer(s.Tank.Color));
        Step(s, events, feed, 5f);
        Assert.Contains(feed.Active, n => n.Key == $"tank-died-{enemy.Color}");
    }

    [Fact]
    public void ABaseShotDown_CreditsItsKiller_WithTheTanksItTakesDown()
    {
        var s = RelayHeatTests.CreateScenario();
        Tank enemy = Enemy(s);
        Assert.NotNull(enemy.Base);
        enemy.Base!.TakeDamage(1_000_000, new Killer(s.Tank.Color, MachineType.Turret));
        Assert.Equal(new Killer(s.Tank.Color, MachineType.Turret), enemy.Base.DestroyedBy);
        s.World.Advance(_ => default);

        TankDeath death = Assert.Single(s.World.Deaths, d => d.Tank == enemy.Color);
        Assert.Equal(DamageCause.BaseDestroyed, death.Cause);
        Assert.Equal(new Killer(s.Tank.Color, MachineType.Turret), death.By);
        Assert.Equal(1, TankDeathView.Rows(s.World)[s.Tank.Color].Kills);
        Assert.Equal($"base fell to {TankDeathView.ColorName(s.Tank.Color)}", TankDeathView.CauseWord(death.Cause, death.By));
    }

    [Fact]
    public void TheBrowserScoreboard_IsBuiltOnlyWhenWhatItShowsChanges()
    {
        var session = new BrowserSession(seed: 1);
        var first = session.ScoreboardFor(session.World, session.Player);
        Assert.Same(first, session.ScoreboardFor(session.World, session.Player));

        Tank enemy = session.World.TankList.Tanks.First(t => t.Color != session.Player.Color);
        enemy.TakeDamage(100_000, DamageCause.Overheat);
        session.World.Advance(_ => default);
        var after = session.ScoreboardFor(session.World, session.Player);
        Assert.NotSame(first, after);
        Assert.Equal(1, after.Rows[enemy.Color].DeathCount);
    }

    [Theory]
    [InlineData(0, null, "You destroyed the Red tank")]
    [InlineData(0, MachineType.Turret, "Your turret destroyed the Red tank")]
    [InlineData(1, null, "Green destroyed the Red tank")]
    [InlineData(1, MachineType.Gatling, "Green's gatling destroyed the Red tank")]
    public void TheToast_SaysWhoKilledWhom(int killer, MachineType? machine, string title) =>
        Assert.Equal(title, TankDeathView.DeathToast(2, DamageCause.EnemyFire, new Killer(killer, machine), player: 0).Title);
}
