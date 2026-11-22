using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities.Emberites;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Terrain;
using Emberward.Core.Types;

namespace Emberward.Tests;

/// <summary>
/// The bounds on what a spent emberite does (<see cref="EmberiteSwarm"/>): a trip that finds no charge ends at home
/// however many unreachable hot spots lie about (<see cref="Tweaks.Emberites.MaxFailedSeeks"/>), and the hot-ground rule
/// keeps a tank only near the creature and never one docked in its own base.
/// </summary>
public class EmberiteHotspotLimitsTests
{
    // Clear of both bases of the 320 x 200 test map, which stand at (130, 21) and (185, 178).
    private static readonly Position Centre = new(60, 100);

    private static void Clear(World world, Position centre, int halfWidth, int halfHeight, TerrainPixel pixel)
    {
        for (int y = -halfHeight; y <= halfHeight; y++)
            for (int x = -halfWidth; x <= halfWidth; x++)
                world.Terrain.SetPixel(centre + new Offset(x, y), pixel);
    }

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

    /// <summary>A hot pocket of dirt sealed in rock: ground a creature could stand in and drink from, but never reach.</summary>
    private static void SealedHotPocket(World world, Position at)
    {
        Clear(world, at, 4, 4, TerrainPixel.Rock);
        Clear(world, at, 1, 1, TerrainPixel.Dirt);
        Heat(world.Terrain, at, 1, 60f);
    }

    [Fact]
    public void UnreachableHotSpots_DoNotKeepACreatureOut_ItGivesUpAndGoesHome()
    {
        var world = TestHelpers.CreateSeededWorld(enableTerrainRegrowth: false, emberites: true);
        Clear(world, Centre, 60, 30, TerrainPixel.Blank);
        var tank = world.TankList.Tanks[0];
        tank.Position = Centre;
        var nest = world.Emberites!.AddNest(Centre + new Offset(30, 0), radius: 4, creatures: 1);
        // Two of them, far enough apart that leaving one does not rule out the other, both on the way home.
        SealedHotPocket(world, Centre + new Offset(16, 5));
        SealedHotPocket(world, Centre + new Offset(16, -5));

        bool bit = false;
        int ticks = 0;
        for (; ticks < 1200 && !(bit && nest.Resting == 1); ticks++)
        {
            tank.Heat = 60f;
            // Kept hot: the rock round them would soon cool them below notice.
            Heat(world.Terrain, Centre + new Offset(16, 5), 1, 60f);
            Heat(world.Terrain, Centre + new Offset(16, -5), 1, 60f);
            world.Advance(_ => default);
            bit |= world.StepEvents.ToArray().Any(e => e.Kind == StepEventKind.EmberiteBit);
        }

        Assert.True(bit, "no creature bit");
        Assert.Equal(1, nest.Resting);
        Assert.Equal(0, nest.Out);
        // One creature, out for a bounded trip: a creature step a tick at most, and the trip ended.
        Assert.True(world.Emberites.CreatureSteps <= ticks, $"{world.Emberites.CreatureSteps} creature steps in {ticks} ticks");
        Assert.True(ticks < 1200, "the creature never got home");
    }

    /// <summary>
    /// Creatures out of a nest below the tank's base hunt it, a rock wall then cuts them off, and the tank cools on hot
    /// ground at <paramref name="tankAt"/>; returns how many are still out after a while, and the ground's heat there.
    /// </summary>
    private static (int Out, float Ground) CutOffOnHotGround(Func<World, Position> tankAt)
    {
        var world = TestHelpers.CreateSeededWorld(enableTerrainRegrowth: false, emberites: true);
        var home = world.TankBases.Bases[0];
        var tank = world.TankList.Tanks[0];
        Assert.Same(home, tank.Base);
        var open = new Position(home.Position.X, home.Position.Y + 45);
        Clear(world, open, 30, 25, TerrainPixel.Blank);
        world.Emberites!.AddNest(new Position(open.X, open.Y + 10), radius: 4, creatures: 12);
        tank.Position = new Position(open.X, open.Y - 15);
        for (int i = 0; i < Tweaks.Emberites.StirTicks + 6; i++)
        {
            tank.Heat = 80f;
            world.Advance(_ => default);
        }
        Assert.True(world.Emberites.OutCount > 0);

        for (int x = 0; x < world.Terrain.Width; x++)
            for (int y = open.Y - 5; y <= open.Y - 3; y++)
                world.Terrain.SetPixel(new Position(x, y), TerrainPixel.Rock);
        Position at = tankAt(world);
        tank.Position = at;
        for (int i = 0; i < 500; i++)
        {
            tank.Heat = 20f;
            Heat(world.Terrain, at, 2, Tweaks.Emberites.HotZoneHeat + 20f);
            world.Advance(_ => default);
        }
        return (world.Emberites.OutCount, world.Terrain.GetHeatTemperature(at));
    }

    [Fact]
    public void ATankOnHotGround_IsKept_OnlyWhenNotDockedInItsBase()
    {
        // Just outside the base, within reach of the creatures behind the wall: kept.
        var (outside, groundOutside) = CutOffOnHotGround(w => new Position(w.TankBases.Bases[0].Position.X, w.TankBases.Bases[0].Position.Y + 22));
        Assert.True(groundOutside >= Tweaks.Emberites.HotZoneHeat);
        Assert.True(outside > 0, "creatures should keep after a tank on hot ground near them");

        // Docked in its own base on ground as hot, as near: let go.
        var (docked, groundDocked) = CutOffOnHotGround(w => w.TankBases.Bases[0].Position + new Offset(0, 8));
        Assert.True(groundDocked >= Tweaks.Emberites.HotZoneHeat, $"the test needs hot ground in the base: {groundDocked}");
        Assert.Equal(0, docked);
    }

    [Fact]
    public void ATankOnHotGroundFarAway_IsNotKept()
    {
        var world = TestHelpers.CreateSeededWorld(enableTerrainRegrowth: false, emberites: true);
        Clear(world, Centre, 60, 30, TerrainPixel.Blank);
        var tank = world.TankList.Tanks[0];
        tank.Position = Centre;
        world.Emberites!.AddNest(Centre + new Offset(30, 0), radius: 4, creatures: 12);
        for (int i = 0; i < Tweaks.Emberites.StirTicks + 6; i++)
        {
            tank.Heat = 80f;
            world.Advance(_ => default);
        }
        Assert.True(world.Emberites.OutCount > 0);

        // Far off behind rock, on hot ground, too cool to be scented: the creatures go home.
        for (int y = 0; y < world.Terrain.Height; y++)
            for (int x = 125; x <= 128; x++)
                world.Terrain.SetPixel(new Position(x, y), TerrainPixel.Rock);
        var far = new Position(200, 100);
        Assert.True(Position.DistanceSquared(far, Centre + new Offset(30, 0)) > (long)Tweaks.Emberites.HotZoneKeepCells * Tweaks.Emberites.HotZoneKeepCells * 4);
        tank.Position = far;
        for (int i = 0; i < 500; i++)
        {
            tank.Heat = 20f;
            Heat(world.Terrain, far, 2, Tweaks.Emberites.HotZoneHeat + 20f);
            world.Advance(_ => default);
        }

        Assert.Equal(0, world.Emberites.OutCount);
    }
}
