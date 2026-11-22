namespace Emberward.Tests;

using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.LevelGen;
using Emberward.Core.Match;
using Emberward.Core.Terrain;
using Emberward.Core.Types;
using Emberward.Core.Entities.Tanks;

internal static class TestHelpers
{
    public const int DefaultSeed = 42;
    public static readonly Size DefaultMapSize = new(320, 200);

    /// <summary>
    /// Where a tank of <paramref name="tankBase"/> stands pressed against its core on the side of the front gate,
    /// touching it (<see cref="Tank.TouchesCore"/>): the only place inside the base where its hold unloads.
    /// </summary>
    public static Position AgainstCore(Emberward.Core.Entities.Bases.TankBase tankBase) =>
        tankBase.Position + new Offset(tankBase.Front.X * AgainstCoreDistance, tankBase.Front.Y * AgainstCoreDistance);

    /// <summary>Cells from a base's centre to a tank against its core: the core's half, the tank's and a cell between.</summary>
    public const int AgainstCoreDistance = Emberward.Core.Entities.Bases.BaseLayout.CoreHalf + TankSpriteShape.Radius + 1;

    public static World CreateSeededWorld(
        Size? size = null,
        int seed = DefaultSeed,
        bool enableTerrainRegrowth = true,
        int hotRockPocketCount = 0,
        int frostRockPocketCount = 0,
        bool seedOre = false,
        bool energyNetwork = false,
        bool machineUpgrades = false,
        bool tankReserve = false,
        bool emberites = false,
        bool tankShield = false,
        SimulationSettings? simulationSettings = null)
    {
        var mapSize = size ?? DefaultMapSize;
        var gen = new ToastGenerator();
        var (terrain, spawns) = gen.Generate(mapSize, seed: seed);
        var world = new World(
            mapSize,
            simulationSeed: seed,
            simulationSettings: simulationSettings,
            rules: new MatchRules
            {
                TerrainRegrowth = enableTerrainRegrowth,
                HotRockPockets = hotRockPocketCount,
                FrostRockPockets = frostRockPocketCount,
                SeedOre = seedOre,
                EnergyNetwork = energyNetwork,
                MachineUpgrades = machineUpgrades,
                TankReserve = tankReserve,
                Emberites = emberites,
                TankShield = tankShield,
            });
        world.Initialize(terrain, spawns, decorationSeed: seed + 1);
        return world;
    }

    /// <summary><see cref="CreateSeededWorld"/> plus the world's first tank.</summary>
    public static (World world, Tank tank) CreateSeededWorldWithTank(int seed = DefaultSeed, bool enableTerrainRegrowth = true)
    {
        var world = CreateSeededWorld(seed: seed, enableTerrainRegrowth: enableTerrainRegrowth);
        return (world, world.TankList.Tanks[0]);
    }

    public static double CountDirtPercentage(TerrainGrid terrain, Size size)
    {
        int dirtCount = 0;
        for (int i = 0; i < size.Area; i++)
            if (Pixel.IsDirt(terrain[i])) dirtCount++;
        return 100.0 * dirtCount / size.Area;
    }

    public static void AssertBorderIsRock(TerrainGrid terrain, int w, int h)
    {
        for (int x = 0; x < w; x++)
        {
            Assert.Equal(TerrainPixel.LevelGenRock, terrain.GetPixelRaw(new Position(x, 0)));
            Assert.Equal(TerrainPixel.LevelGenRock, terrain.GetPixelRaw(new Position(x, h - 1)));
        }
        for (int y = 0; y < h; y++)
        {
            Assert.Equal(TerrainPixel.LevelGenRock, terrain.GetPixelRaw(new Position(0, y)));
            Assert.Equal(TerrainPixel.LevelGenRock, terrain.GetPixelRaw(new Position(w - 1, y)));
        }
    }

    public static void AssertNoLevelGenPixels(TerrainGrid terrain, Size size)
    {
        for (int i = 0; i < size.Area; i++)
        {
            var pix = terrain[i];
            Assert.False(pix == TerrainPixel.LevelGenDirt || pix == TerrainPixel.LevelGenRock || pix == TerrainPixel.LevelGenMark,
                $"LevelGen pixel {pix} survived at offset {i}");
        }
    }
}
