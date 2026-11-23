using Emberward.Core;
using Emberward.Core.Bots;
using Emberward.Core.Config;
using Emberward.Core.Match;
using Emberward.Core.Terrain;
using Emberward.Core.Types;
using Emberward.Desktop;
using Xunit.Abstractions;
using static Emberward.Tests.PowerupWorlds;

namespace Emberward.Tests;

/// <summary>Bots and powerup drops (DESIGN.md §6.8): a roaming bot goes for a drop near it, and only where the rules run drops.</summary>
public class PowerupBotTests
{
    internal static BotTankAI?[] Bots(int seed, int count) =>
        Enumerable.Range(0, count).Select(slot => new BotTankAI(MatchSetup.BotSeed(seed, slot))).ToArray();

    /// <summary>One step, each bot driving its slot's tank as a host has it do; a slot without a bot stands still.</summary>
    internal static void Step(World world, BotTankAI?[] bots) => SlotStepper.Advance(world, bots, _ => default);

    /// <summary>Slow: up to the first drop's landing and a bot's way to it through soil, a minute or more of match.</summary>
    [Fact]
    [Trait("Category", "Slow")]
    public void Bot_HeadsForADropOnlyOnceItIsNear_AndTakesIt()
    {
        // Soil all over (on bare ground a bot sees its enemy from its base and only fights), and one bot: the other
        // tank stands in its base.
        var world = Create(tanks: 2, ground: TerrainPixel.Dirt);
        var bot = new BotTankAI(MatchSetup.BotSeed(3, 1));
        BotTankAI?[] bots = [null, bot];
        int lead = Tweaks.Powerups.BotSeekLeadSeconds * Fps;
        var sought = new HashSet<Position>();
        StepEvent? taken = null;
        for (int tick = 0; tick < Tweaks.Powerups.FirstDropTicks + Tweaks.Powerups.GroundTicks && taken == null; tick++)
        {
            Step(world, bots);
            // The bot goes for a drop no earlier than the lead it has on its landing.
            if (tick < Tweaks.Powerups.FirstDropTicks - lead)
                Assert.Null(bot.DropSought);
            if (bot.DropSought is Position site)
                sought.Add(site);
            if (Events(world, StepEventKind.PowerupCollected) is [var collected, ..])
                taken = collected;
        }
        Assert.NotEmpty(sought);
        Assert.True(taken != null, "the bot took no drop while one lay");
        Assert.Equal(1, taken.Value.Color);
        Assert.Contains(taken.Value.Position, sought);
    }

    [Fact]
    public void WithoutTheRule_ABotSeeksNothing()
    {
        var world = Create(Rules with { Powerups = false }, tanks: 2);
        var bots = Bots(seed: 3, count: 2);
        for (int tick = 0; tick < 20 * Fps; tick++)
        {
            Step(world, bots);
            Assert.All(bots, bot => Assert.Null(bot!.DropSought));
        }
    }
}

/// <summary>
/// Two arcade matches of four bots from one seed, on a generated map: the bots take most of the drops that land, and
/// the matches stay alike, hash for hash, through drops landing, being taken and running out. Slow: minutes of two
/// four-bot matches (stepped side by side on two threads, which the worlds share nothing to mind).
/// </summary>
public class PowerupBotMatchTests(ITestOutputHelper output)
{
    private const int Seed = 1;
    private const int Seconds = 180;
    private const int CompareEvery = 12;

    [Fact]
    [Trait("Category", "Slow")]
    public void FourBots_TakeMostOfTheDropsThatLand_AndTwoMatchesOfOneSeedStayAlike()
    {
        var a = new Match();
        var b = new Match();
        for (int tick = 0; tick < Seconds * Fps; tick += CompareEvery)
        {
            Parallel.Invoke(a.Play, b.Play);
            SyncDigest da = WorldStateHash.ComputeSync(a.World, a.Bots), db = WorldStateHash.ComputeSync(b.World, b.Bots);
            Assert.True(da == db, $"the matches differ {tick + CompareEvery} ticks in: {da} against {db}");
            Assert.True(a.Drops.SequenceEqual(b.Drops), $"the drops' events differ {tick + CompareEvery} ticks in");
        }

        int landed = a.Count(StepEventKind.PowerupLanded), collected = a.Count(StepEventKind.PowerupCollected);
        var takers = a.Drops.Where(e => e.Kind == StepEventKind.PowerupCollected).GroupBy(e => e.Color)
            .OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Count()}");
        output.WriteLine($"{landed} landed, {collected} collected ({string.Join(", ", takers)}), " +
            $"{a.Count(StepEventKind.PowerupExpired)} expired, {a.Count(StepEventKind.PowerupEnded)} effects ended");

        Assert.Equal(1 + ((Seconds - Tweaks.Powerups.FirstDropSeconds) / Tweaks.Powerups.DropPeriodSeconds), landed);
        Assert.True(collected * 3 >= landed * 2, $"the bots took {collected} of {landed} drops");
        Assert.True(takers.Count() >= 2, "one bot took every drop");
    }

    private sealed class Match
    {
        public readonly World World = MatchSetup.Build(new Size(320, 200), Seed, MatchRules.Arcade, players: 4);
        public readonly BotTankAI?[] Bots = PowerupBotTests.Bots(Seed, 4);
        public readonly List<StepEvent> Drops = [];

        public int Count(StepEventKind kind) => Drops.Count(e => e.Kind == kind);

        public void Play()
        {
            for (int i = 0; i < CompareEvery; i++)
            {
                PowerupBotTests.Step(World, Bots);
                foreach (StepEvent e in World.StepEvents)
                    if (e.Kind is StepEventKind.PowerupAnnounced or StepEventKind.PowerupLanded or StepEventKind.PowerupCollected
                        or StepEventKind.PowerupExpired or StepEventKind.PowerupEnded)
                        Drops.Add(e);
            }
        }
    }
}
