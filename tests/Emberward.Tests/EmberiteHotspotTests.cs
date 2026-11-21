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
/// going home, taking the charge out of the ground so the energy balance holds; with none it goes home; and a tank on
/// hot ground is kept whatever its own heat.
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
