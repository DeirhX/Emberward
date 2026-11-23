using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities.Powerups;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Match;
using Emberward.Core.Terrain;
using Emberward.Core.Types;

namespace Emberward.Tests;

/// <summary>
/// What the powerup drop tests share (DESIGN.md §6.8): a small world of one kind of ground with a base in each corner
/// and the drops switched on, and the steps every test takes (wait for a drop to land, stand a tank on it).
/// </summary>
internal static class PowerupWorlds
{
    internal const int Fps = Tweaks.Perf.TargetFps;

    internal static readonly Size MapSize = new(200, 150);

    /// <summary>The drops and nothing else switched on.</summary>
    internal static readonly MatchRules Rules = new() { TerrainRegrowth = false, Powerups = true };

    private static readonly Position[] Spawns = [new(30, 30), new(170, 120), new(170, 30), new(30, 120)];

    /// <summary><paramref name="ground"/> all over, with <paramref name="tanks"/> bases in its corners, played under <paramref name="rules"/>.</summary>
    internal static World Create(MatchRules? rules = null, int tanks = 4, int seed = 7, TerrainPixel ground = TerrainPixel.Blank)
    {
        var terrain = new TerrainGrid(MapSize);
        terrain.Fill(ground);
        var world = new World(MapSize, simulationSeed: seed, rules ?? Rules);
        world.Initialize(terrain, Spawns[..tanks]);
        return world;
    }

    internal static void Step(World world) => world.Advance(_ => default);

    internal static void Step(World world, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            Step(world);
    }

    /// <summary>The last step's events of <paramref name="kind"/>.</summary>
    internal static StepEvent[] Events(World world, StepEventKind kind) =>
        world.StepEvents.ToArray().Where(e => e.Kind == kind).ToArray();

    /// <summary>
    /// Steps the world until a drop lands (of <paramref name="kind"/>, when one is named) and returns its site. Drops
    /// that land before it are left to lie.
    /// </summary>
    internal static Position AwaitLanding(World world, PowerupKind? kind = null)
    {
        int limit = Tweaks.Powerups.FirstDropTicks + (2 * PowerupKinds.Count * Tweaks.Powerups.DropPeriodTicks);
        for (int i = 0; i < limit; i++)
        {
            Step(world);
            foreach (StepEvent landed in Events(world, StepEventKind.PowerupLanded))
                if (kind == null || landed.Count == (int)kind)
                    return landed.Position;
        }
        Assert.Fail($"no {kind?.ToString() ?? "drop"} landed in {limit} ticks");
        return default;
    }

    /// <summary>
    /// <paramref name="tank"/> takes a drop of <paramref name="kind"/>: the world is stepped until one lands, the tank
    /// is stood on it and the world stepped once more. Returns the drop's site, where the tank now stands.
    /// </summary>
    internal static Position Take(World world, Tank tank, PowerupKind kind)
    {
        Position site = AwaitLanding(world, kind);
        tank.Position = site;
        Step(world);
        StepEvent taken = Assert.Single(Events(world, StepEventKind.PowerupCollected));
        Assert.Equal((site, tank.Color, (int)kind), (taken.Position, taken.Color, taken.Count));
        return site;
    }

    /// <summary>
    /// Drops of their own over <paramref name="world"/>'s ground and bases, begun and ready to step alone
    /// (<see cref="PowerupDrops.Advance"/>), which costs nothing of the world's step: for tests of the schedule and
    /// the sites over many drops.
    /// </summary>
    internal static PowerupDrops Alone(World world, int seed, StepEventLog log)
    {
        var drops = new PowerupDrops(world.Terrain, world.TankList, world.TankBases, world.Machines, world.Emberites,
            shield: null, world.HotRockPockets, world.FrostRockPockets, log, seed);
        drops.Begin(world.CorePocket);
        return drops;
    }

    /// <summary>Steps <paramref name="drops"/> alone until the forecast drop has landed; returns what was forecast.</summary>
    internal static PowerupForecast LandNext(PowerupDrops drops, StepEventLog log)
    {
        PowerupForecast forecast = drops.Forecast;
        for (int i = 0; i < forecast.TicksUntilLanding; i++)
        {
            log.Clear();
            drops.Advance();
        }
        return forecast;
    }
}
