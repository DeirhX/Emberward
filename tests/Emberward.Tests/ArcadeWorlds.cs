using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities;
using Emberward.Core.Entities.Projectiles;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Match;
using Emberward.Core.Terrain;
using Emberward.Core.Types;

namespace Emberward.Tests;

/// <summary>
/// What the tests of the timed score match and the arcade rules share (DESIGN.md §6.8): a small world of blank ground
/// with a base in each corner, and a shell that destroys a tank standing in the open near its middle.
/// </summary>
internal static class ArcadeWorlds
{
    internal const int Fps = Tweaks.Perf.TargetFps;

    /// <summary>More than any hull holds.</summary>
    internal const int Lethal = 10_000;

    internal static readonly Size MapSize = new(160, 120);

    /// <summary>Open ground in the middle of the map, outside every base.</summary>
    internal static readonly Position Open = new(80, 60);

    private static readonly Position[] Spawns = [new(30, 30), new(130, 90), new(130, 30), new(30, 90)];

    /// <summary>Blank ground with <paramref name="tanks"/> bases in its corners, played under <paramref name="rules"/>.</summary>
    internal static World Create(MatchRules rules, int tanks = 4)
    {
        var ground = new TerrainGrid(MapSize);
        ground.Fill(TerrainPixel.Blank);
        var world = new World(MapSize, rules: rules);
        world.Initialize(ground, Spawns[..tanks]);
        return world;
    }

    /// <summary>A match of <paramref name="seconds"/> on the clock and nothing else switched on.</summary>
    internal static MatchRules Timed(int seconds) => new() { TerrainRegrowth = false, TimeLimitSeconds = seconds };

    /// <summary>
    /// Stands <paramref name="tank"/> in the open with no respawn shield left: steps the world until it is alive and
    /// the shield it came back with has run out. Returns where it stands.
    /// </summary>
    internal static Position Expose(World world, Tank tank)
    {
        Position? spot = null;
        for (int i = 0; i < 60 * Fps && (tank.IsDead || tank.SpawnProtectionTicks > 0); i++)
        {
            if (!tank.IsDead)
                tank.Position = spot ??= CoolSpot(world, tank);
            world.Advance(_ => default);
        }
        Assert.False(tank.IsDead);
        Assert.Equal(0, tank.SpawnProtectionTicks);
        return tank.Position = spot ?? CoolSpot(world, tank);
    }

    /// <summary>
    /// The coolest of nine spots around <see cref="Open"/> where no other tank stands or lies: a tank's death heats
    /// the ground under it, and the next tank to stand there would overheat.
    /// </summary>
    private static Position CoolSpot(World world, Tank tank)
    {
        (Position Spot, float Heat) best = (Open, float.MaxValue);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                Position spot = Open + new Offset(dx * 16, dy * 14);
                if (world.TankList.Tanks.Any(other => other != tank && other.Position == spot))
                    continue;
                float heat = Math.Max(world.Terrain.GetHeatTemperature(spot), world.Terrain.GetAirTemperature(spot));
                if (heat < best.Heat)
                    best = (spot, heat);
            }
        return best.Spot;
    }

    /// <summary>
    /// A shell of tank <paramref name="killer"/>'s destroys <paramref name="victim"/> in the open, a step or two after
    /// it is fired (and after the steps <see cref="Expose"/> takes). Returns the <see cref="StepEventKind.TankDied"/>
    /// of the step it died in, the last one stepped.
    /// </summary>
    internal static StepEvent Kill(World world, int killer, Tank victim)
    {
        Position spot = Expose(world, victim);
        world.Projectiles.Add(Projectile.CreateBullet(spot + new Offset(-4, 0), new VectorF(1, 0), killer, Lethal));
        // The shell crosses the cells to the hull, which are fewer or more by the way the tank faces.
        for (int step = 0; step < 4 && !victim.IsDead; step++)
            world.Advance(_ => default);
        Assert.True(victim.IsDead, "the shell should have destroyed the tank");
        return Died(world);
    }

    /// <summary><paramref name="tank"/> dies of <paramref name="cause"/>, with no shell in it. Returns the step's <see cref="StepEventKind.TankDied"/>.</summary>
    internal static StepEvent Perish(World world, Tank tank, DamageCause cause)
    {
        Expose(world, tank);
        Assert.True(tank.TakeDamage(Lethal, cause));
        world.Advance(_ => default);
        return Died(world);
    }

    /// <summary>The last step's only <see cref="StepEventKind.TankDied"/>.</summary>
    internal static StepEvent Died(World world) =>
        Assert.Single(world.StepEvents.ToArray(), e => e.Kind == StepEventKind.TankDied);

    /// <summary>Steps the world until <paramref name="remainingTicks"/> are left on the clock.</summary>
    internal static void RunClockDownTo(World world, int remainingTicks)
    {
        Assert.True(world.MatchClock.RemainingTicks >= remainingTicks, "the clock is already past that");
        while (world.MatchClock.RemainingTicks > remainingTicks)
            world.Advance(_ => default);
    }

    /// <summary>
    /// The match is over and <paramref name="winner"/> won it by <paramref name="reason"/>. Compares colours: a failed
    /// comparison of tanks would have to print them.
    /// </summary>
    internal static void AssertWon(World world, Tank winner, MatchWinReason reason)
    {
        Assert.True(world.IsMatchOver);
        var won = world.MatchWinner;
        Assert.NotNull(won);
        Assert.Equal(winner.Color, won.Value.Tank.Color);
        Assert.Equal(reason, won.Value.Reason);
    }

    /// <summary>The board as <see cref="World.CopyStandings"/> writes it.</summary>
    internal static TankScore[] Standings(World world)
    {
        Span<TankScore> lines = stackalloc TankScore[world.TankList.Tanks.Count];
        int count = world.CopyStandings(lines);
        return lines[..count].ToArray();
    }
}
