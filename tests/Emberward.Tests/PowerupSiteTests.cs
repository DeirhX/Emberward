using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities.Powerups;
using Emberward.Core.Match;
using Emberward.Core.Terrain;
using Emberward.Core.Types;
using Xunit.Abstractions;
using static Emberward.Tests.PowerupWorlds;

namespace Emberward.Tests;

/// <summary>
/// Where powerup drops land (DESIGN.md §6.8), on generated maps with four bases over many seeds: always on ground a
/// tank can reach, away from the bases, the core and the pockets, and fair in the two ways that take turns.
/// </summary>
public class PowerupSiteTests(ITestOutputHelper output)
{
    private const int DropsPerMap = 24;

    /// <summary>
    /// 320x200: an "everyone's" drop's farthest base is at most 70 cells farther than its nearest (40 in the middle of
    /// all the drops), a "contested" drop's second base at most 25 farther than its nearest. The bounds are what was
    /// measured, with room to spare; the hot core sits where the bases' distances are most alike, so an "everyone's"
    /// drop lies beside it, not on it. Slow: eight generated maps.
    /// </summary>
    [Fact]
    [Trait("Category", "Slow")]
    public void Sites_AreReachableGround_AwayFromBasesAndPockets_AndFair() =>
        AssertFairSites(output, new Size(320, 200), seeds: 8, maxSpread: 70, maxMedianSpread: 40, maxGap: 25);

    /// <summary>
    /// Plays <see cref="DropsPerMap"/> drops alone on each of <paramref name="seeds"/> generated maps of four bases and
    /// checks every site: its ground, its distances, and its fairness by the goal whose turn it was (cells).
    /// </summary>
    internal static void AssertFairSites(ITestOutputHelper output, Size size, int seeds, int maxSpread, int maxMedianSpread,
        int maxGap)
    {
        (int width, int height) = (size.X, size.Y);
        var spreads = new List<int>();
        var gaps = new List<int>();
        int nearestBase = int.MaxValue, nearestDrop = int.MaxValue, worstSpread = 0, beside = 0, distinct = 0;
        string worst = "";
        for (int seed = 1; seed <= seeds; seed++)
        {
            World world = MatchSetup.Build(new Size(width, height), seed, MatchRules.Arcade with { Powerups = false }, players: 4);
            var bases = world.TankBases.Bases.Select(tankBase => tankBase.Position).ToArray();
            Assert.Equal(4, bases.Length);
            var log = new StepEventLog();
            PowerupDrops drops = Alone(world, seed, log);
            var sites = new List<Position>();
            var contested = new List<int[]>();
            for (int drop = 0; drop < DropsPerMap; drop++)
            {
                Position site = LandNext(drops, log).Site;
                string where = $"{width}x{height} seed {seed} drop {drop} at {site}";
                AssertReachableGround(world, site, where);

                int[] distances = bases.Select(at => Distance(at, site)).ToArray();
                int[] ranked = distances.Order().ToArray();
                nearestBase = Math.Min(nearestBase, ranked[0]);
                Assert.True(ranked[0] >= Tweaks.Powerups.MinBaseDistance, $"{where} is {ranked[0]} cells from a base");
                // Never beside the drop before it, which may still lie there; seldom where the one before that was.
                if (sites.Count > 0)
                {
                    nearestDrop = Math.Min(nearestDrop, Distance(sites[^1], site));
                    Assert.True(Distance(sites[^1], site) >= Tweaks.Powerups.MinDropSpacing, $"{where} is beside the drop at {sites[^1]}");
                }
                if (sites.Count > 1 && Distance(sites[^2], site) < Tweaks.Powerups.MinDropSpacing)
                    beside++;
                sites.Add(site);

                if (drop % 2 == 0)
                {
                    // Everyone's: as nearly the same distance from every base as the ground allows.
                    spreads.Add(ranked[^1] - ranked[0]);
                    if (ranked[^1] - ranked[0] >= worstSpread)
                        (worstSpread, worst) = (ranked[^1] - ranked[0], $"{where}: bases at {string.Join(',', distances)}");
                }
                else
                {
                    // Contested: between two neighbours, each base in its turn the nearest.
                    gaps.Add(ranked[1] - ranked[0]);
                    Assert.True(ranked[1] - ranked[0] <= maxGap, $"{where}: bases at {string.Join(',', distances)}");
                    contested.Add(distances);
                }
            }
            AssertTurnsTaken(contested, bases.Length, $"{width}x{height} seed {seed}");
            distinct += sites.Distinct().Count();
        }
        spreads.Sort();
        gaps.Sort();
        output.WriteLine($"{width}x{height}, {seeds} seeds, {DropsPerMap} drops each: everyone's spread min {spreads[0]} " +
            $"median {spreads[spreads.Count / 2]} max {spreads[^1]}; contested gap min {gaps[0]} median {gaps[gaps.Count / 2]} " +
            $"max {gaps[^1]}; nearest base {nearestBase}, nearest to the drop before {nearestDrop}");
        output.WriteLine($"spread's ninth decile {spreads[spreads.Count * 9 / 10]}; {beside} of {seeds * DropsPerMap} drops beside " +
            $"the site before the last; {distinct} distinct sites; worst: {worst}");
        Assert.True(beside * 3 <= seeds * DropsPerMap, $"{beside} of {seeds * DropsPerMap} drops landed where the one before the last did");
        Assert.True(distinct * 10 >= seeds * DropsPerMap * 8, $"only {distinct} distinct sites in {seeds * DropsPerMap} drops");
        Assert.True(worstSpread <= maxSpread, worst);
        Assert.True(spreads[spreads.Count / 2] <= maxMedianSpread, $"the median spread is {spreads[spreads.Count / 2]}");
    }

    /// <summary>
    /// The turns of the contested drops, read from their sites (<paramref name="contested"/>: each drop's distance
    /// from every base): in every round of as many drops as there are bases each base is the nearest once, and the
    /// base that ended a round does not begin the next. Two bases as near as each other may either have had the turn.
    /// </summary>
    internal static void AssertTurnsTaken(List<int[]> contested, int bases, string where)
    {
        for (int round = 0; round + bases <= contested.Count; round += bases)
        {
            string distances = string.Join(" | ", contested.GetRange(round, bases).Select(d => string.Join(',', d)));
            Assert.True(EachBaseOnce(contested, round, bases, taken: 0),
                $"{where}: not every base had its turn in the contested drops from {round}: {distances}");
            if (round > 0 && SoleNearest(contested[round - 1]) is int last && last >= 0)
                Assert.True(SoleNearest(contested[round]) != last, $"{where}: base {last} had two turns running, at contested drop {round}");
        }
    }

    /// <summary>Whether the round's drops from <paramref name="drop"/> on can each be given to a base it is nearest to that has had none.</summary>
    private static bool EachBaseOnce(List<int[]> contested, int drop, int left, int taken)
    {
        if (left == 0)
            return true;
        int[] distances = contested[drop];
        for (int color = 0; color < distances.Length; color++)
            if (distances[color] == distances.Min() && (taken & (1 << color)) == 0
                && EachBaseOnce(contested, drop + 1, left - 1, taken | (1 << color)))
                return true;
        return false;
    }

    /// <summary>The base nearer the site than every other, or -1 when two are as near.</summary>
    internal static int SoleNearest(int[] distances)
    {
        int nearest = distances.Min();
        return distances.Count(d => d == nearest) == 1 ? Array.IndexOf(distances, nearest) : -1;
    }

    /// <summary>
    /// The turn order alone, on bare ground over many seeds: a round at a time as above, and a different base first
    /// from seed to seed, so no slot is the nearest more often than another. (A site as near two bases as each other
    /// does not say whose turn it was, and is left out of the counts.)
    /// </summary>
    [Fact]
    public void ContestedTurns_GoRoundEveryBaseInAnOrderOfTheSeed_NeverTheSameBaseTwiceRunning()
    {
        const int seeds = 64, rounds = 3;
        World world = Create();
        var bases = world.TankBases.Bases.Select(tankBase => tankBase.Position).ToArray();
        int[] first = new int[bases.Length], nearest = new int[bases.Length];
        for (int seed = 1; seed <= seeds; seed++)
        {
            var log = new StepEventLog();
            PowerupDrops drops = Alone(world, seed, log);
            var contested = new List<int[]>();
            for (int drop = 0; drop < 2 * rounds * bases.Length; drop++)
            {
                Position site = LandNext(drops, log).Site;
                if (drop % 2 == 1)
                    contested.Add(bases.Select(at => Distance(at, site)).ToArray());
            }
            AssertTurnsTaken(contested, bases.Length, $"seed {seed}");
            if (SoleNearest(contested[0]) is int opener and >= 0)
                first[opener]++;
            foreach (int[] distances in contested)
                if (SoleNearest(distances) is int turn and >= 0)
                    nearest[turn]++;
        }
        output.WriteLine($"{seeds} seeds, {rounds} rounds each: the first contested drop is nearest base {string.Join(',', first)}; " +
            $"all of them, base {string.Join(',', nearest)}");
        Assert.All(first, count => Assert.True(count > 0, $"first turns by base: {string.Join(',', first)}"));
    }

    /// <summary>Two and three bases, and a standard match's nests: the same search, the same ground rules.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Sites_AreReachableGround_WithFewerBases_AndAmongNests(int players)
    {
        var rules = MatchRules.ForMode(MatchMode.Standard, coreTapWins: false);
        Assert.True(rules.Emberites);
        for (int seed = 1; seed <= 4; seed++)
        {
            World world = MatchSetup.Build(new Size(320, 200), seed, rules, players: players);
            Assert.NotEmpty(world.Emberites!.Nests);
            var log = new StepEventLog();
            PowerupDrops drops = Alone(world, seed, log);
            for (int drop = 0; drop < DropsPerMap; drop++)
            {
                Position site = LandNext(drops, log).Site;
                string where = $"{players} bases, seed {seed}, drop {drop} at {site}";
                AssertReachableGround(world, site, where);
                int nearest = world.TankBases.Bases.Min(tankBase => Distance(tankBase.Position, site));
                Assert.True(nearest >= Tweaks.Powerups.MinBaseDistance, $"{where} is {nearest} cells from a base");
            }
        }
    }

    /// <summary>
    /// The site's disc is soil or open ground (no rock, ore, base or machine cell), outside every base, clear of the
    /// hot core, the hot and frost pockets and the nests, and neither hot nor frozen.
    /// </summary>
    private static void AssertReachableGround(World world, Position site, string where)
    {
        int radius = Tweaks.Powerups.PickupDiameter / 2;
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if ((dx * dx) + (dy * dy) > radius * radius)
                    continue;
                TerrainPixel pixel = world.Terrain.GetPixel(site + new Offset(dx, dy));
                Assert.True(Pixel.IsSoil(pixel) || Pixel.IsOpenGround(pixel), $"{where}: {pixel} in its disc");
            }
        Assert.False(world.IsInsideAnyBase(site), $"{where} is inside a base");
        if (world.CorePocket is Position core)
            Assert.True(Distance(core, site) >= Tweaks.World.HotRockCoreRadius + Tweaks.Powerups.CoreClearance - 1,
                $"{where} is {Distance(core, site)} cells from the core");
        foreach (Position pocket in world.HotRockPockets)
            Assert.True(Distance(pocket, site) >= Tweaks.World.HotRockMaxRadius + Tweaks.Powerups.PocketClearance - 1,
                $"{where} is {Distance(pocket, site)} cells from the hot pocket at {pocket}");
        foreach (Position pocket in world.FrostRockPockets)
            Assert.True(Distance(pocket, site) >= Tweaks.World.FrostRockMaxRadius + Tweaks.Powerups.PocketClearance - 1,
                $"{where} is {Distance(pocket, site)} cells from the frost pocket at {pocket}");
        if (world.Emberites is { } swarm)
            foreach (var nest in swarm.Nests)
                Assert.True(Distance(nest.Centre, site) >= nest.Radius + Tweaks.Powerups.NestClearance - 1,
                    $"{where} is {Distance(nest.Centre, site)} cells from the nest at {nest.Centre}");
        float heat = world.Terrain.SampleAverageHeat(site, radius) * 255f;
        Assert.InRange(heat, Tweaks.Powerups.MinSiteHeat, Tweaks.Powerups.MaxSiteHeat);
    }

    /// <summary>Whole cells between two places, rounded down, as the site search counts them.</summary>
    private static int Distance(Position a, Position b) => (int)Math.Sqrt(Position.DistanceSquared(a, b));
}

/// <summary>The same on a larger map, where every distance is longer. Slow: four generated maps of 480x300.</summary>
public class PowerupSiteLargeMapTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Slow")]
    public void Sites_AreReachableGround_AwayFromBasesAndPockets_AndFair() =>
        PowerupSiteTests.AssertFairSites(output, new Size(480, 300), seeds: 4, maxSpread: 100, maxMedianSpread: 60, maxGap: 25);
}
