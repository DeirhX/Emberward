using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities.Emberites;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Terrain;
using Emberward.Core.Types;

namespace Emberward.Tests;

/// <summary>
/// What a creature does after a bite (<see cref="EmberiteSwarm"/>, DESIGN.md §6.2): with hot ground near the fight it
/// runs there, drinks a new charge for <see cref="Tweaks.Emberites.HotspotRechargeTicks"/> and bites again instead of
/// going home, taking the charge out of the ground so the energy balance holds; short of a charge it drinks at hot ground
/// on its way home; with none it goes home; and a tank on hot ground is kept whatever its own heat.
/// </summary>
public class EmberiteHotspotTests
{
    // Clear of both bases of the 320 x 200 test map, which stand at (130, 21) and (185, 178).
    private static readonly Position Centre = new(60, 100);

    private static (World World, Tank Tank, Nest Nest) Arena(int creatures)
    {
        var world = TestHelpers.CreateSeededWorld(enableTerrainRegrowth: false, emberites: true);
        for (int y = -30; y <= 30; y++)
            for (int x = -60; x <= 60; x++)
                world.Terrain.SetPixel(Centre + new Offset(x, y), TerrainPixel.Blank);
        var tank = world.TankList.Tanks[0];
        tank.Position = Centre;
        tank.Heat = 60f;
        var nest = world.Emberites!.AddNest(Centre + new Offset(30, 0), radius: 4, creatures: creatures);
        return (world, tank, nest);
    }

    /// <summary>Brings the ground within <paramref name="half"/> cells of <paramref name="at"/> up to <paramref name="temperature"/>.</summary>
    private static void Heat(TerrainGrid terrain, Position at, int half, float temperature)
    {
        for (int y = -half; y <= half; y++)
            for (int x = -half; x <= half; x++)
            {
                var cell = at + new Offset(x, y);
                int lift = (int)(temperature - terrain.GetHeatTemperature(cell));
                if (lift > 0)
                    terrain.AddHeat(cell, lift);
            }
    }

    private static bool Bit(World world) => world.StepEvents.ToArray().Any(e => e.Kind == StepEventKind.EmberiteBit);

    [Fact]
    public void OnHotGround_ASpentCreatureDrinksBesideTheTank_AndBitesAgain_WithoutGoingHome()
    {
        var (world, tank, nest) = Arena(creatures: 1);
        Heat(world.Terrain, Centre, half: 12, temperature: 60f);
        var ledger = world.Terrain.Ledger;
        double Balance() => world.SumSystemThermalEnergy() + world.SumSystemCharge() - ledger.Injected + ledger.TotalSunk;
        double start = Balance();

        // The tank's heat is left alone: setting it would be heat the ledger does not see. It stands in ground as hot.
        var biteTicks = new List<int>();
        for (int i = 0; i < 400; i++)
        {
            world.Advance(_ => default);
            if (Bit(world))
                biteTicks.Add(i);
            // Never home between bites: the one creature stays out.
            if (biteTicks.Count > 0)
            {
                Assert.Equal(0, nest.Resting);
                Assert.Equal(1, world.Emberites!.OutCount);
            }
            Assert.Equal(start, Balance(), tolerance: 1.0);
        }

        Assert.True(biteTicks.Count >= 3, $"the creature should keep biting: {biteTicks.Count} bites");
        for (int i = 1; i < biteTicks.Count; i++)
            Assert.True(biteTicks[i] - biteTicks[i - 1] >= Tweaks.Emberites.HotspotRechargeTicks,
                $"a drink takes {Tweaks.Emberites.HotspotRechargeTicks} ticks: bites at {biteTicks[i - 1]} and {biteTicks[i]}");
    }

    [Fact]
    public void TheSecondBite_CarriesAFullChargeDrunkFromTheGround()
    {
        var (world, tank, nest) = Arena(creatures: 1);
        Heat(world.Terrain, Centre, half: 12, temperature: 60f);
        int bites = 0;
        float gain = 0f;
        for (int i = 0; i < 400 && bites < 2; i++)
        {
            tank.Heat = 60f;
            world.Advance(_ => default);
            if (Bit(world))
            {
                bites++;
                gain = tank.Heat - 60f;
            }
        }

        Assert.Equal(2, bites);
        Assert.Equal(0, nest.Resting);
        // The creature went home after neither bite, so its second charge was drunk beside the tank: a full bite's heat.
        Assert.True(gain > Tweaks.Emberites.BiteDegrees * 0.75f, $"the second bite should carry a full charge: +{gain}");
    }

    /// <summary>
    /// The way-home arena: one dirt cell beside the tank warm enough to be found but holding less than a charge (the
    /// first spot), and, if <paramref name="secondSpot"/>, a hot patch of dirt between the tank and the nest.
    /// </summary>
    private static (World World, Tank Tank, Nest Nest) WayHomeArena(bool secondSpot)
    {
        var (world, tank, nest) = Arena(creatures: 1);
        var weak = Centre + new Offset(5, -5);
        world.Terrain.SetPixel(weak, TerrainPixel.Dirt);
        world.Terrain.AddHeat(weak, 24);
        if (secondSpot)
        {
            var patch = Centre + new Offset(16, 7);
            for (int y = -2; y <= 2; y++)
                for (int x = -2; x <= 2; x++)
                    world.Terrain.SetPixel(patch + new Offset(x, y), TerrainPixel.Dirt);
            Heat(world.Terrain, patch, half: 2, temperature: 60f);
        }
        return (world, tank, nest);
    }

    /// <summary>
    /// Runs the way-home arena without touching the tank's heat (setting it is heat the ledger does not see), checking the
    /// balance every tick; returns the bites, the heat the second one added, whether the creature got home and how warm
    /// the first spot is left.
    /// </summary>
    private static (int Bites, float SecondGain, bool Home, float FirstSpot) RunWayHome(bool secondSpot)
    {
        var (world, tank, nest) = WayHomeArena(secondSpot);
        Assert.True(Tweaks.Emberites.BiteDegrees * Tweaks.Tank.TankHeatCapacity > 24f * 1.4f,
            "the first spot must hold less than a charge");
        var ledger = world.Terrain.Ledger;
        double Balance() => world.SumSystemThermalEnergy() + world.SumSystemCharge() - ledger.Injected + ledger.TotalSunk;
        double start = Balance();
        int bites = 0;
        float gain = 0f;
        for (int i = 0; i < 600 && nest.Resting == 0; i++)
        {
            float before = tank.Heat;
            world.Advance(_ => default);
            if (Bit(world))
            {
                bites++;
                if (bites == 2)
                    gain = tank.Heat - before;
            }
            Assert.Equal(start, Balance(), tolerance: 1.0);
        }
        return (bites, gain, nest.Resting == 1, world.Terrain.GetHeatTemperature(Centre + new Offset(5, -5)));
    }

    [Fact]
    public void AShortDrink_SendsItHome_ByWayOfHotGroundOnTheWay_WhereItChargesAndBitesAgain()
    {
        var (bites, gain, _, firstSpot) = RunWayHome(secondSpot: true);

        // It drank the first spot dry (all it held, short of a charge) before it went on.
        Assert.True(firstSpot < 5f, $"the first spot should have been drunk: {firstSpot}");
        Assert.True(bites >= 2, $"the creature should have charged on the way home and bitten again: {bites} bites");
        Assert.True(gain > Tweaks.Emberites.BiteDegrees * 0.75f, $"the second bite should carry a full charge: +{gain}");
    }

    [Fact]
    public void AShortDrink_WithNoHotGroundOnTheWay_GoesHome()
    {
        var (bites, _, home, firstSpot) = RunWayHome(secondSpot: false);

        Assert.True(firstSpot < 5f, $"the first spot should have been drunk: {firstSpot}");
        Assert.Equal(1, bites);
        Assert.True(home, "the creature should have gone home");
    }

    [Fact]
    public void OnColdGround_ASpentCreatureGoesHome()
    {
        var (world, tank, nest) = Arena(creatures: 1);
        for (int i = 0; i < 400 && !Bit(world); i++)
        {
            tank.Heat = 60f;
            world.Advance(_ => default);
        }
        Assert.True(Bit(world), "no creature bit");

        for (int i = 0; i < 200 && nest.Resting == 0; i++)
        {
            tank.Heat = 60f;
            world.Advance(_ => default);
        }
        Assert.Equal(1, nest.Resting);
        Assert.Equal(0, world.Emberites!.OutCount);
    }

    [Fact]
    public void ACoolTankOnHotGround_IsKept_EvenBehindRock()
    {
        // As EmberiteSensingTests.ACoolTankBehindRock_IsLost, but the tank stands on hot ground: the creatures keep at it.
        foreach (bool hot in new[] { false, true })
        {
            var (world, tank, nest) = Arena(creatures: 12);
            // The wall goes up before any creature is out; the tank's heat keeps it hunted until they are.
            for (int i = 0; i < 8; i++)
            {
                tank.Heat = 80f;
                world.Advance(_ => default);
            }
            for (int y = 0; y < world.Terrain.Height; y++)
                for (int x = 70; x <= 74; x++)
                    world.Terrain.SetPixel(new Position(x, y), TerrainPixel.Rock);
            for (int i = 0; i < Tweaks.Emberites.StirTicks + 20; i++)
            {
                tank.Heat = 80f;
                world.Advance(_ => default);
            }
            Assert.True(world.Emberites!.OutCount > 0);

            for (int i = 0; i < 500; i++)
            {
                tank.Heat = 20f;
                if (hot)
                    Heat(world.Terrain, Centre, half: 3, temperature: Tweaks.Emberites.HotZoneHeat + 20f);
                world.Advance(_ => default);
            }

            if (hot)
            {
                Assert.True(world.Emberites!.OutCount > 0, "creatures should keep after a tank on hot ground");
                Assert.True(nest.Awake);
            }
            else
            {
                Assert.Equal(0, world.Emberites!.OutCount);
            }
        }
    }

    [Fact]
    public void ACreatureDrinkingAtAHotspot_RoundTripsThroughASnapshot()
    {
        var (world, tank, _) = Arena(creatures: 4);
        Heat(world.Terrain, Centre, half: 12, temperature: 60f);
        for (int i = 0; i < 400 && !Bit(world); i++)
        {
            tank.Heat = 60f;
            world.Advance(_ => default);
        }
        Assert.True(Bit(world), "no creature bit");
        world.Advance(_ => default);

        SnapshotRoundTrip.AssertPlaysOnAlike(world, [null, null],
            () => TestHelpers.CreateSeededWorld(enableTerrainRegrowth: false, emberites: true), ticks: 90,
            (_, _) => default, firstFrame: 0, before: (w, _) => w.TankList.Tanks[0].Heat = 60f);
    }
}
