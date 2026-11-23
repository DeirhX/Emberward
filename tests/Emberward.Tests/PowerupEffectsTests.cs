using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities;
using Emberward.Core.Entities.Powerups;
using Emberward.Core.Entities.Projectiles;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Input;
using Emberward.Core.Terrain;
using Emberward.Core.Types;
using Xunit.Abstractions;
using static Emberward.Tests.PowerupWorlds;

namespace Emberward.Tests;

/// <summary>
/// What each kind of powerup drop does to the tank that takes it, and how a timed one ends (DESIGN.md §6.8).
/// </summary>
public class PowerupEffectsTests(ITestOutputHelper output)
{
    private static double Balance(World world) =>
        world.SumSystemThermalEnergy() + world.SumSystemCharge() - world.Terrain.Ledger.Injected + world.Terrain.Ledger.TotalSunk;

    [Fact]
    public void Overhaul_MendsTheHull_FillsTheShield_AndCoolsTheTank_WithTheLedgerBalanced()
    {
        var world = Create(Rules with { TankShield = true }, tanks: 2);
        Tank tank = world.TankList.Tanks[0];
        Position site = AwaitLanding(world, PowerupKind.Overhaul);

        // A shield spent, a hull hurt and a tank run hot.
        Assert.True(tank.TakeDamage(Tweaks.TankShield.Capacity + 300, DamageCause.Emberite));
        Assert.Equal(0, world.TankShieldOf(tank).Shield);
        Assert.Equal(tank.Reactor.HealthCapacity - 300, (int)tank.Reactor.Health);
        TankHeatTestHelpers.SetTankHeat(tank, 60f);
        tank.Position = site;

        double balance = Balance(world), sunk = world.Terrain.Ledger.SunkTank;
        Step(world);
        StepEvent taken = Assert.Single(Events(world, StepEventKind.PowerupCollected));
        Assert.Equal((site, tank.Color, (int)PowerupKind.Overhaul), (taken.Position, taken.Color, taken.Count));

        Assert.Equal((int)tank.Reactor.HealthCapacity, (int)tank.Reactor.Health);
        Assert.Equal(Tweaks.TankShield.Capacity, world.TankShieldOf(tank).Shield);
        Assert.InRange(tank.Heat, -1f, 0f);
        // The heat it shed left the system through the ledger: nothing was created or lost.
        double shed = world.Terrain.Ledger.SunkTank - sunk;
        output.WriteLine($"shed {shed:F1} of {60f * tank.ThermalCapacity:F1}");
        Assert.True(shed > 0.5 * 60f * tank.ThermalCapacity, $"the tank shed only {shed}");
        Assert.InRange(Balance(world) - balance, -1e-3, 1e-3);

        // It acts at once and is over: nothing runs on, and the ledger stays balanced as the tank stands there.
        Assert.False(world.PowerupsOf(tank).Any);
        for (int i = 0; i < 48; i++)
        {
            Step(world);
            Assert.InRange(Balance(world) - balance, -1e-3, 1e-3);
            Assert.Empty(Events(world, StepEventKind.PowerupEnded));
        }
    }

    [Fact]
    public void Overhaul_LeavesAColdTankCold()
    {
        var world = Create(tanks: 2);
        Tank tank = world.TankList.Tanks[0];
        Position site = AwaitLanding(world, PowerupKind.Overhaul);
        TankHeatTestHelpers.SetTankHeat(tank, -30f);
        tank.Position = site;
        double balance = Balance(world), sunk = world.Terrain.Ledger.SunkTank;

        Step(world);

        Assert.Single(Events(world, StepEventKind.PowerupCollected));
        Assert.True(tank.Heat < -20f, $"the cold tank was warmed to {tank.Heat}");
        Assert.Equal(sunk, world.Terrain.Ledger.SunkTank);
        Assert.InRange(Balance(world) - balance, -1e-3, 1e-3);
    }

    [Fact]
    public void Overcharge_DoublesWhatTheTanksShellsDeal_UntilItEnds()
    {
        var world = Create(tanks: 2);
        Tank shooter = world.TankList.Tanks[0], victim = world.TankList.Tanks[1];
        Position site = Take(world, shooter, PowerupKind.Overcharge);
        TankPowerupView view = world.PowerupsOf(shooter);
        Assert.True(view.Active && view.Any && view.IsRunning(PowerupKind.Overcharge));
        Assert.Equal(PowerupKinds.DurationTicks(PowerupKind.Overcharge), view.TicksLeft(PowerupKind.Overcharge));
        Assert.Equal(1f, view.FractionLeft(PowerupKind.Overcharge));
        Assert.False(world.PowerupsOf(victim).Any);

        Assert.Equal(Tweaks.Powerups.OverchargeDamageFactor * Tweaks.Weapon.BulletDamage, ShellHit(world, shooter, victim, site));
        // The other tank's shells are as they were.
        Assert.Equal(Tweaks.Weapon.BulletDamage, ShellHit(world, victim, shooter, site));

        SendHome(shooter, victim);
        AwaitEnd(world, shooter, PowerupKind.Overcharge);
        Assert.Equal(Tweaks.Weapon.BulletDamage, ShellHit(world, shooter, victim, site + new Offset(0, site.Y < MapSize.Y / 2 ? 20 : -20)));
    }

    /// <summary>
    /// <paramref name="shooter"/>, stood at <paramref name="at"/>, fires one shell at <paramref name="victim"/> a
    /// little way off in the open. Returns the hull the victim lost.
    /// </summary>
    private static int ShellHit(World world, Tank shooter, Tank victim, Position at)
    {
        shooter.Position = at;
        victim.Position = at + new Offset(18, 0);
        for (int i = 0; i < 12 * Fps && (victim.SpawnProtectionTicks > 0 || shooter.SpawnProtectionTicks > 0); i++)
            Step(world);
        Assert.Equal(0, victim.SpawnProtectionTicks);
        int before = victim.Reactor.Health;
        var fire = new ControllerOutput { ShootPrimary = true, AimDirection = new DirectionF(1, 0) };
        world.Advance(slot => slot == shooter.Color ? fire : default);
        Assert.Single(Events(world, StepEventKind.ShotFired));
        for (int i = 0; i < 2 * Fps && victim.Reactor.Health == before; i++)
            Step(world);
        return before - victim.Reactor.Health;
    }

    [Fact]
    public void Bulwark_HalvesEverythingThatHurts_ThroughTheShieldToo_AndARecallStillKills()
    {
        var world = Create(Rules with { TankShield = true }, tanks: 2);
        Tank tank = world.TankList.Tanks[0], other = world.TankList.Tanks[1];
        Position site = Take(world, tank, PowerupKind.Bulwark);
        Assert.Equal(PowerupKinds.DurationTicks(PowerupKind.Bulwark), world.PowerupsOf(tank).BulwarkTicks);
        Assert.Equal(0, tank.SpawnProtectionTicks);

        // Without one a hit lands whole; with one half of it does, and the shield takes that half first.
        Assert.True(other.TakeDamage(100, DamageCause.Emberite));
        Assert.Equal((100, 0), (other.LastDamageTaken.Shield, other.LastDamageTaken.Hull));
        Assert.True(tank.TakeDamage(100, DamageCause.Emberite));
        Assert.Equal((50, 0), (tank.LastDamageTaken.Shield, tank.LastDamageTaken.Hull));
        Assert.Equal(Tweaks.TankShield.Capacity - 50, world.TankShieldOf(tank).Shield);

        // A shell: half of it, off the shield.
        world.Projectiles.Add(Projectile.CreateBullet(site + new Offset(-5, 0), new VectorF(1, 0), other.Color));
        for (int i = 0; i < 6 && world.TankShieldOf(tank).Shield == Tweaks.TankShield.Capacity - 50; i++)
            Step(world);
        Assert.Equal((Tweaks.Weapon.BulletDamage / 2, 0), (tank.LastDamageTaken.Shield, tank.LastDamageTaken.Hull));
        int shield = world.TankShieldOf(tank).Shield;

        // A hit bigger than the shield: half of it, what the shield has left off the shield and the rest off the hull.
        int hull = tank.Reactor.Health;
        Assert.True(tank.TakeDamage(2 * (shield + 120), DamageCause.Emberite));
        Assert.Equal((shield, 120), (tank.LastDamageTaken.Shield, tank.LastDamageTaken.Hull));
        Assert.Equal(hull - 120, (int)tank.Reactor.Health);

        // Heat comes a point at a time and goes straight to the hull: every other point is taken.
        hull = tank.Reactor.Health;
        Assert.False(tank.TakeDamage(1, DamageCause.Overheat));
        Assert.True(tank.TakeDamage(1, DamageCause.Overheat));
        Assert.Equal(hull - 1, (int)tank.Reactor.Health);
        // Odd hits lose nothing to rounding: 3 and 3 are 6, of which 3 land.
        Assert.True(tank.TakeDamage(3, DamageCause.Frostbite));
        Assert.True(tank.TakeDamage(3, DamageCause.Frostbite));
        Assert.Equal(hull - 4, (int)tank.Reactor.Health);

        // A recall is not a hit: it ends the tank whatever it carries.
        Assert.True(tank.TakeDamage(ArcadeWorlds.Lethal, DamageCause.Recalled));
        Assert.True(tank.IsDead);
    }

    [Fact]
    public void Bulwark_EndsWhenItsTimeIsUp_AndHitsLandWholeAgain()
    {
        var world = Create(tanks: 2);
        Tank tank = world.TankList.Tanks[0];
        Take(world, tank, PowerupKind.Bulwark);
        SendHome(tank);
        int full = PowerupKinds.DurationTicks(PowerupKind.Bulwark);

        for (int left = full - 1; left > 0; left--)
        {
            Step(world);
            Assert.Equal(left, world.PowerupsOf(tank).TicksLeft(PowerupKind.Bulwark));
            Assert.Empty(Events(world, StepEventKind.PowerupEnded));
        }
        Assert.Equal(1f / full, world.PowerupsOf(tank).FractionLeft(PowerupKind.Bulwark), 5);
        int hull = tank.Reactor.Health;
        Assert.True(tank.TakeDamage(100, DamageCause.Emberite));
        Assert.Equal(hull - 50, (int)tank.Reactor.Health);

        Step(world);
        StepEvent ended = Assert.Single(Events(world, StepEventKind.PowerupEnded));
        Assert.Equal((tank.Position, tank.Color, (int)PowerupKind.Bulwark), (ended.Position, ended.Color, ended.Count));
        Assert.False(world.PowerupsOf(tank).Any);
        Assert.True(world.PowerupsOf(tank).Active);
        // (Its base has mended it a little in that step.)
        hull = tank.Reactor.Health;
        Assert.True(tank.TakeDamage(100, DamageCause.Emberite));
        Assert.Equal(hull - 100, (int)tank.Reactor.Health);

        Step(world, 5);
        Assert.Empty(Events(world, StepEventKind.PowerupEnded));
    }

    [Fact]
    public void Drill_DigsSoilAtTheSpeedOfOpenGround_AndStopsAtRock()
    {
        // Two seconds: short of the far base from any site.
        const int ticks = 2 * Fps;
        var soil = Create(tanks: 2, ground: TerrainPixel.Dirt);
        Tank driller = soil.TankList.Tanks[0], digger = soil.TankList.Tanks[1];
        Position site = Take(soil, driller, PowerupKind.Drill);
        Assert.True(soil.PowerupsOf(driller).IsRunning(PowerupKind.Drill));
        digger.Position = site + new Offset(0, site.Y < MapSize.Y / 2 ? 30 : -30);
        var drive = new ControllerOutput { MoveSpeed = new Offset(site.X < MapSize.X / 2 ? 1 : -1, 0) };
        (Position drillerStart, Position diggerStart) = (driller.Position, digger.Position);
        for (int i = 0; i < ticks; i++)
            soil.Advance(_ => drive);
        int drilled = Math.Abs(driller.Position.X - drillerStart.X), dug = Math.Abs(digger.Position.X - diggerStart.X);

        // The same drive over open ground.
        var open = Create(tanks: 2);
        Tank driver = open.TankList.Tanks[0];
        driver.Position = site;
        for (int i = 0; i < ticks; i++)
            open.Advance(_ => drive);
        int driven = Math.Abs(driver.Position.X - site.X);

        output.WriteLine($"in {ticks} ticks: {drilled} cells drilling soil, {dug} digging it, {driven} over open ground");
        Assert.Equal(driven, drilled);
        Assert.True(drilled >= 2 * dug, $"the drill made {drilled} cells where plain digging made {dug}");
        // It dug what it drove through: the ground behind it is open.
        Assert.True(Pixel.IsOpenGround(soil.Terrain.GetPixel(drillerStart + new Offset(drive.MoveSpeed.X * (drilled / 2), 0))));

        // Rock is no soil: the drill does not get through it.
        int ahead = driller.Position.X + (drive.MoveSpeed.X * 8);
        for (int dy = -12; dy <= 12; dy++)
            soil.Terrain.SetPixel(new Position(ahead, driller.Position.Y + dy), TerrainPixel.Rock);
        for (int i = 0; i < 2 * Fps; i++)
            soil.Advance(slot => slot == driller.Color ? drive : default);
        Assert.True(soil.PowerupsOf(driller).IsRunning(PowerupKind.Drill));
        Assert.True(Math.Abs(driller.Position.X - ahead) >= 1 && (ahead - driller.Position.X) * drive.MoveSpeed.X > 0,
            $"the drilling tank at {driller.Position.X} passed the rock at {ahead}");
        Assert.Equal(TerrainPixel.Rock, soil.Terrain.GetPixel(new Position(ahead, driller.Position.Y)));
    }

    [Fact]
    public void Death_EndsEveryEffectTheTankHad()
    {
        var world = Create(tanks: 2);
        Tank tank = world.TankList.Tanks[0];
        // Two kinds at once: a timed drop taken in its last moments on the ground, and the timed one that lands next.
        // (Three of the four kinds are timed, so two of them are dealt one after the other in every round.)
        Position firstSite = AwaitLanding(world);
        var first = (PowerupKind)Events(world, StepEventKind.PowerupLanded)[0].Count;
        Bounded.Until(() => PowerupKinds.IsTimed(first) && PowerupKinds.IsTimed(world.PowerupForecast.Kind), () =>
        {
            firstSite = AwaitLanding(world);
            first = (PowerupKind)Events(world, StepEventKind.PowerupLanded)[0].Count;
        }, 2 * PowerupKinds.Count, "a timed drop landing with a timed one forecast");
        PowerupKind second = world.PowerupForecast.Kind;
        Assert.NotEqual(first, second);
        Step(world, world.PowerupForecast.TicksUntilLanding - 2);
        tank.Position = firstSite;
        Step(world);
        Assert.Single(Events(world, StepEventKind.PowerupCollected));
        tank.Position = AwaitLanding(world, second);
        Step(world);
        Assert.Single(Events(world, StepEventKind.PowerupCollected));
        PowerupKind[] running = [first, second];
        foreach (PowerupKind kind in running)
            Assert.True(world.PowerupsOf(tank).IsRunning(kind), $"{kind} should be running");

        Assert.True(tank.TakeDamage(ArcadeWorlds.Lethal, DamageCause.Recalled));
        Position diedAt = tank.Position;
        Step(world);

        var ended = Events(world, StepEventKind.PowerupEnded);
        Assert.Equal(running.Select(kind => (int)kind).Order(), ended.Select(e => e.Count).Order());
        Assert.All(ended, e => Assert.Equal((diedAt, tank.Color), (e.Position, e.Color)));
        Assert.False(world.PowerupsOf(tank).Any);

        // It comes back with none.
        for (int i = 0; i < 30 * Fps && tank.IsDead; i++)
        {
            Step(world);
            Assert.Empty(Events(world, StepEventKind.PowerupEnded));
        }
        Assert.False(tank.IsDead);
        Assert.False(world.PowerupsOf(tank).Any);
        int hull = tank.Reactor.Health;
        Assert.True(tank.TakeDamage(100, DamageCause.Emberite));
        Assert.Equal(hull - 100, (int)tank.Reactor.Health);
    }

    [Fact]
    public void ARecall_EndsEveryEffect_ThoughTheTankRespawnsInTheSameStep()
    {
        // The off-network clock runs out in a tick, so the tank may recall as soon as it is out of its base.
        var settings = SimulationSettings.FromTweaks() with { TankOffNetworkHeatPerTick = Tweaks.Tank.HeatSafeMax };
        var world = Create(tanks: 2, settings: settings);
        Tank tank = world.TankList.Tanks[0];
        Take(world, tank, PowerupKind.Bulwark);
        Assert.True(world.PowerupsOf(tank).IsRunning(PowerupKind.Bulwark));
        // A hit of an odd amount leaves a carried remainder behind.
        Assert.True(tank.TakeDamage(101, DamageCause.Emberite));
        for (int i = 0; i < 10 && !tank.CanRecall; i++)
            Step(world);
        Assert.True(tank.CanRecall, "the tank could not recall within 10 ticks of taking the drop");
        Assert.True(world.PowerupsOf(tank).IsRunning(PowerupKind.Bulwark));
        Position recalledFrom = tank.Position;

        world.Advance(slot => slot == tank.Color ? new ControllerOutput { Recall = true } : default);

        // Back home in the same step, and with nothing running.
        Assert.False(tank.IsDead);
        Assert.Single(Events(world, StepEventKind.TankRespawned));
        StepEvent ended = Assert.Single(Events(world, StepEventKind.PowerupEnded));
        Assert.Equal((recalledFrom, tank.Color, (int)PowerupKind.Bulwark), (ended.Position, ended.Color, ended.Count));
        Assert.False(world.PowerupsOf(tank).Any);
        // Nothing of the bulwark is left: a hit takes all it should, carry and all.
        int hull = tank.Reactor.Health;
        Assert.True(tank.TakeDamage(100, DamageCause.Emberite));
        Assert.Equal(hull - 100, (int)tank.Reactor.Health);
        Step(world);
        Assert.Empty(Events(world, StepEventKind.PowerupEnded));
    }

    [Fact]
    public void TakingAKindAgain_StartsItsTimeOver()
    {
        var world = Create(tanks: 2);
        Tank tank = world.TankList.Tanks[0];
        Take(world, tank, PowerupKind.Drill);
        SendHome(tank);
        // The same kind comes round again within two rounds of the bag, long after the first has run out; so a
        // second drop of it is left to lie while a first is taken late. Here: take one, let half its time pass, and
        // take the next of the same kind as soon as it lands.
        int full = PowerupKinds.DurationTicks(PowerupKind.Drill);
        Position again = AwaitLanding(world, PowerupKind.Drill);
        Assert.False(world.PowerupsOf(tank).Any);
        tank.Position = again;
        Step(world);
        Assert.Equal(full, world.PowerupsOf(tank).DrillTicks);
        Step(world, full / 2);
        Assert.Equal(full - (full / 2), world.PowerupsOf(tank).DrillTicks);
    }

    private static void SendHome(params Tank[] tanks)
    {
        foreach (Tank tank in tanks)
            tank.Position = tank.Base!.Position;
    }

    /// <summary>Steps the world until <paramref name="tank"/>'s <paramref name="kind"/> ends; the step it ends in is the last.</summary>
    private static void AwaitEnd(World world, Tank tank, PowerupKind kind)
    {
        for (int i = 0; i <= PowerupKinds.DurationTicks(kind); i++)
        {
            Step(world);
            if (Events(world, StepEventKind.PowerupEnded).Any(e => e.Color == tank.Color && e.Count == (int)kind))
            {
                Assert.False(world.PowerupsOf(tank).IsRunning(kind));
                return;
            }
        }
        Assert.Fail($"{kind} did not end in its time");
    }
}
