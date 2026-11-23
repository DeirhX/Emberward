using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Terrain;
using Emberward.Core.Types;

namespace Emberward.Tests;

/// <summary>
/// A damaged hull burns, and the fire heats the tank and the air around it, much harder the lower the hull: a minor wound
/// is shrugged off, a major one cooks the tank even on a full link, and cool surroundings carry some of it away
/// (DESIGN.md §4.7).
/// </summary>
public class TankFireTests
{
    private static readonly Position Centre = new(60, 100);

    private static (World World, Tank Tank) Burning(int hullLeft)
    {
        var world = TestHelpers.CreateSeededWorld(enableTerrainRegrowth: false, tankShield: true);
        var tank = world.TankList.Tanks[0];
        // In open ground clear of the bases, which would cool the tank and repair the hull.
        for (int y = -30; y <= 30; y++)
            for (int x = -60; x <= 60; x++)
                world.Terrain.SetPixel(Centre + new Offset(x, y), TerrainPixel.Blank);
        tank.Position = Centre;
        tank.TakeDamage(Tweaks.TankShield.Capacity + tank.Reactor.Health - hullLeft, DamageCause.EnemyFire);
        return (world, tank);
    }

    private static void Step(World world, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            world.Advance(_ => TestSimulation.Idle);
    }

    [Theory]
    [InlineData(1000, 0f)]
    [InlineData(750, 0f)]
    [InlineData(375, 0.5f)]
    [InlineData(1, 1f)]
    public void TheFire_GrowsAsTheHullFalls(int hull, float expected)
    {
        var (world, tank) = Burning(hull);

        Assert.Equal(expected, tank.FireLevel, 2);
        Assert.Equal(tank.FireLevel, world.TankShieldOf(tank).FireLevel);
    }

    [Fact]
    public void WithoutTheShieldRule_NothingBurns()
    {
        var world = TestHelpers.CreateSeededWorld(enableTerrainRegrowth: false);
        var tank = world.TankList.Tanks[0];
        tank.TakeDamage(900, DamageCause.EnemyFire);

        Assert.Equal(0f, tank.FireLevel);
    }

    [Fact]
    public void ABurningTank_EndsHotterThanAnUnhurtOne_AndWarmsTheAir()
    {
        var (burnWorld, burning) = Burning(100);
        var (calmWorld, calm) = Burning(1000);
        Assert.True(burning.FireLevel > 0.8f);
        Assert.Equal(0f, calm.FireLevel);

        Step(burnWorld, 60);
        Step(calmWorld, 60);

        Assert.True(burning.Heat > calm.Heat + 1f, $"burning {burning.Heat}, calm {calm.Heat}");
        // The air around the burning tank is warm; around the other it is not.
        Assert.True(burnWorld.Terrain.GetAirTemperature(burning.Position) > calmWorld.Terrain.GetAirTemperature(calm.Position) + 1f);
    }

    [Fact]
    public void TheLedger_BalancesEveryTickWhileBurning()
    {
        var (world, tank) = Burning(100);
        var ledger = world.Terrain.Ledger;
        double Balance() => world.SumSystemThermalEnergy() + world.SumSystemCharge() - ledger.Injected + ledger.TotalSunk;
        double start = Balance();

        for (int i = 0; i < 120; i++)
        {
            world.Advance(_ => TestSimulation.Idle);
            Assert.Equal(start, Balance(), tolerance: 1.0);
        }
        Assert.True(ledger.Injected > 0.0);
    }

    [Fact]
    public void DockingRepairsTheHull_AndPutsTheFireOut()
    {
        var world = TestHelpers.CreateSeededWorld(enableTerrainRegrowth: false, tankShield: true);
        var tank = world.TankList.Tanks[0];
        tank.TakeDamage(Tweaks.TankShield.Capacity + 900, DamageCause.EnemyFire);
        Assert.True(tank.FireLevel > 0.5f);
        Assert.True(tank.IsDockedAtOwnBase);

        for (int i = 0; i < 400 && tank.FireLevel > 0f; i++)
            world.Advance(_ => TestSimulation.Idle);

        Assert.Equal(0f, tank.FireLevel);
        Assert.False(tank.IsDead);
    }

    [Fact]
    public void TheFiresHeat_GrowsMuchFasterThanTheFire_FromNothingAtAMinorWoundToFarMoreThanALink()
    {
        var settings = SimulationSettings.FromTweaks();
        float Heat(float level) => TankHeatModel.FireHeatShare(settings, level) * settings.TankFireHeatPerTickMax;
        float link = settings.TankNetworkCoolingPerTick;

        Assert.Equal(0f, Heat(0f));
        Assert.Equal(settings.TankFireHeatPerTickMax, Heat(1f), 5);
        // Convex: half the fire gives well under half the heat.
        Assert.True(Heat(0.5f) < 0.5f * Heat(1f));
        // A hull at 70% (fire level 1/15) adds a small fraction of what a link takes away; a full fire far more.
        Assert.True(Heat(1f / 15f) < 0.05f * link, $"minor wound {Heat(1f / 15f)} a tick");
        Assert.True(Heat(1f) > 4f * link, $"full fire {Heat(1f)} a tick");
    }

    /// <summary>
    /// A burning tank idle in a tunnel just outside its base, inside its coverage at full strength. Ground warmed or
    /// chilled by <paramref name="groundOffset"/> degrees around it stands for a hot or cold place.
    /// </summary>
    private static (World World, Tank Tank) BurningInATunnel(int hullLeft, int groundOffset = 0)
    {
        var world = TestHelpers.CreateSeededWorld(seed: 4244, enableTerrainRegrowth: false, tankShield: true);
        var tank = world.TankList.Tanks[0];
        var tankBase = tank.Base!;
        // Seed 4244 puts the base against the top border; the tunnel runs out of its gate (RelayHeatTests).
        int dirY = tankBase.Front.Y;
        int first = Tweaks.Base.BaseSize / 2 + 1;
        for (int dx = -2; dx <= 2; dx++)
            for (int d = first; d <= first + 50; d++)
                world.Terrain.SetPixel(tankBase.Position + new Offset(dx, dirY * d), TerrainPixel.Blank);
        tank.Position = tankBase.Position + new Offset(0, dirY * (first + 10));
        if (groundOffset != 0)
            world.Terrain.AddHeatRadius(tank.Position, groundOffset, 12);
        tank.TakeDamage(Tweaks.TankShield.Capacity + tank.Reactor.Health - hullLeft, DamageCause.EnemyFire);
        return (world, tank);
    }

    private static float HottestOver(World world, Tank tank, int seconds)
    {
        float hottest = tank.Heat;
        for (int i = 0; i < seconds * Tweaks.Perf.TargetFps && !tank.IsDead; i++)
        {
            world.Advance(_ => TestSimulation.Idle);
            hottest = Math.Max(hottest, tank.Heat);
        }
        return hottest;
    }

    [Fact]
    public void AMinorWound_DoesNotWarmATankOnAFullLink()
    {
        var (world, tank) = BurningInATunnel(700);
        Assert.True(tank.FireLevel > 0f, "a 70% hull burns");

        float hottest = HottestOver(world, tank, 30);

        Assert.True(tank.Coverage.Covered && tank.Coverage.Strength > 0.99f, "the tank should be on a full-strength link");
        Assert.True(hottest < 1f, $"a 70% hull reached {hottest:0.0}");
        Assert.Equal(700, tank.Reactor.Health.Value);
    }

    [Fact]
    public void AMajorWound_CooksATankEvenOnAFullLink()
    {
        var (world, tank) = BurningInATunnel(100);

        bool overheated = false;
        for (int i = 0; i < 30 * Tweaks.Perf.TargetFps && !tank.IsDead && !overheated; i++)
        {
            world.Advance(_ => TestSimulation.Idle);
            overheated = tank.IsOverheating;
        }

        Assert.True(tank.Coverage.Covered && tank.Coverage.Strength > 0.99f, "the tank should be on a full-strength link");
        Assert.True(overheated, $"a 10% hull should overheat on a full link; it is at {tank.Heat:0.0}");
    }

    [Fact]
    public void ColdGround_KeepsABadlyBurningTankCoolerThanWarmGround()
    {
        var (coldWorld, cold) = BurningInATunnel(300, groundOffset: -30);
        var (warmWorld, warm) = BurningInATunnel(300, groundOffset: 30);

        float coldHottest = HottestOver(coldWorld, cold, 20);
        float warmHottest = HottestOver(warmWorld, warm, 20);

        Assert.True(coldHottest < warmHottest - 5f, $"cold {coldHottest:0.0}, warm {warmHottest:0.0}");
    }

    [Fact]
    public void ABurningHull_LosesAtMostTheCapASecond_HoweverHotTheTank()
    {
        var (world, tank) = Burning(800 - 300);
        int perSecond = Tweaks.TankShield.FireMaxHullLossPerSecond;
        int start = tank.Reactor.Health.Value;

        for (int second = 1; second <= 3; second++)
        {
            for (int t = 0; t < Tweaks.Perf.TargetFps; t++)
            {
                // Far past the safe limit: uncapped, a tick at this heat would take the whole hull.
                tank.Heat = 2000f;
                int before = tank.Reactor.Health.Value;
                world.Advance(_ => TestSimulation.Idle);
                int lost = before - tank.Reactor.Health.Value;
                Assert.True(tank.IsOverheating);
                Assert.InRange(lost, perSecond / Tweaks.Perf.TargetFps, perSecond / Tweaks.Perf.TargetFps + 1);
            }
            // Spread evenly, a full second takes exactly the cap.
            Assert.Equal(start - second * perSecond, tank.Reactor.Health.Value);
        }
        Assert.False(tank.IsDead);
    }

    [Fact]
    public void AHullNotAlight_OverheatsAtThePlainRate()
    {
        var (world, tank) = Burning(1000);
        tank.Heat = Tweaks.Tank.HeatSafeMax + 60f;

        world.Advance(_ => TestSimulation.Idle);

        Assert.True(1000 - tank.Reactor.Health.Value > Tweaks.TankShield.FireMaxHullLossPerSecond / Tweaks.Perf.TargetFps + 1,
            $"a hull not alight lost only {1000 - tank.Reactor.Health.Value}");
    }

    [Fact]
    public void ShotsAreNotCapped_OnABurningHull()
    {
        var (_, tank) = Burning(500);

        tank.TakeDamage(300, DamageCause.EnemyFire);

        Assert.Equal(200, tank.Reactor.Health.Value);
    }

    [Fact]
    public void ABurningTank_RaisesTheFireWarning_AndAnUnhurtOneDoesNot()
    {
        var (_, burning) = Burning(300);
        var (_, calm) = Burning(1000);
        var warnings = new Emberward.Desktop.Gui.Guidance.PlayerWarnings();

        Assert.Equal(Emberward.Desktop.Gui.Guidance.WarningKind.FireAboard, warnings.Update(burning, offerThermalView: false, 0f)?.Kind);
        Assert.NotEqual(Emberward.Desktop.Gui.Guidance.WarningKind.FireAboard, new Emberward.Desktop.Gui.Guidance.PlayerWarnings()
            .Update(calm, offerThermalView: false, 0f)?.Kind);
    }

    [Fact]
    public void AVeryDamagedHull_ShowsHullLow_NotJustTheFire()
    {
        var (_, tank) = Burning(200);

        var kind = new Emberward.Desktop.Gui.Guidance.PlayerWarnings().Update(tank, offerThermalView: false, 0f)?.Kind;

        Assert.Equal(Emberward.Desktop.Gui.Guidance.WarningKind.HullLow, kind);
    }
}
