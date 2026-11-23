namespace Emberward.Tests;

using Emberward.Core;
using Emberward.Core.Bots;
using Emberward.Core.Config;
using Emberward.Core.Entities.Bases;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.LevelGen;
using Emberward.Core.Match;
using Emberward.Core.Terrain;
using Emberward.Core.Types;

/// <summary>
/// The bot prospects (DESIGN.md §6.4): in the opening of a match with ore it mines the deposits round its base and
/// banks the load at its core, instead of making for its enemy. It fights when the enemy comes near, is in sight, is at
/// its base or hits it; in a match without ore it goes for the enemy as before.
/// </summary>
public class BotProspectTests
{
    private const int Fps = Tweaks.Perf.TargetFps;

    /// <summary>
    /// A field of dirt with two bases far apart (tank 0 the enemy, tank 1 the bot), ore seeded over it (or not), and
    /// the bot just outside its gate, roaming. The enemy's base is beyond the engage range.
    /// </summary>
    private static (World World, Tank Bot, Tank Enemy, BotTankAI Ai) Field(bool seedOre = true, int timeLimitSeconds = 0)
    {
        var size = new Size(240, 100);
        var ground = new TerrainGrid(size);
        ground.Fill(TerrainPixel.Dirt);
        var world = new World(size, rules: new MatchRules { TerrainRegrowth = false, SeedOre = seedOre, TimeLimitSeconds = timeLimitSeconds });
        world.Initialize(ground, [new Position(30, 50), new Position(210, 50)]);
        var enemy = world.TankList.Tanks[0];
        var bot = world.TankList.Tanks[1];
        var home = bot.Base!;
        Assert.Equal(seedOre, world.OreDeposits.Any(d => d.Kind != OreDepositKind.Lump));

        var ai = new BotTankAI(7);
        bot.Position = home.Position + new Offset(0, -(BaseLayout.Half + 8));
        for (int tick = 0; tick < 600 && ai.Mode != TwitchMode.Twitch; tick++)
            ai.GetInput(bot, enemy, world);
        Assert.Equal(TwitchMode.Twitch, ai.Mode);
        return (world, bot, enemy, ai);
    }

    private static int Distance(Position a, Position b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    [Fact]
    public void InATimedMatch_TheBotDoesNotProspect_ItHuntsFromTheStart()
    {
        // A timed match (arcade) is won on kills alone: ore wins it nothing, so the bot leaves the deposits be.
        var (world, bot, enemy, ai) = Field(timeLimitSeconds: Tweaks.Arcade.TimeLimitSeconds);
        for (int tick = 0; tick < 20 * Fps; tick++)
        {
            world.Advance(i => i == 1 ? ai.GetInput(bot, enemy, world) : default);
            Assert.False(ai.IsProspecting, $"tick {tick}: the bot prospected in a timed match");
        }
    }

    [Fact]
    public void ABotWithOreNearby_MinesIt_AndBanksItAtItsCore_InsteadOfHeadingForTheEnemy()
    {
        var (world, bot, enemy, ai) = Field();
        var home = bot.Base!;
        int store = home.Store.Ore;
        int startToEnemy = Distance(bot.Position, enemy.Position);
        int mined = 0, nearestToEnemy = int.MaxValue;
        bool minedWhileProspecting = false;
        var path = new System.Text.StringBuilder();
        for (int tick = 0; tick < 90 * Fps && home.Store.Ore <= store; tick++)
        {
            world.Advance(i => i == 1 ? ai.GetInput(bot, enemy, world) : default);
            foreach (var e in world.StepEvents)
                if (e.Kind == StepEventKind.OreCollected && e.Color == bot.Color)
                {
                    mined += e.Count;
                    minedWhileProspecting |= ai.IsMining;
                }
            Assert.False(bot.IsDead, $"the bot died: {bot.DeathCause}. each second: {path}");
            nearestToEnemy = Math.Min(nearestToEnemy, Distance(bot.Position, enemy.Position));
            if (tick % Fps == 0)
                path.Append(bot.Position).Append(ai.Mode).Append(ai.IsProspecting ? "P" : "").Append(ai.IsMining ? "M" : "")
                    .Append(bot.OreHeld).Append(' ');
        }

        Assert.True(minedWhileProspecting, $"the bot should dig ore into its hold. each second: {path}");
        Assert.True(mined >= 1);
        Assert.True(home.Store.Ore > store, $"the bot should bank what it mined at its core. each second: {path}");
        Assert.True(nearestToEnemy > startToEnemy / 2, $"the bot headed for the enemy: {nearestToEnemy} of {startToEnemy}. {path}");
    }

    [Fact]
    public void AnEnemyNearby_IsFought_NotMinedAround()
    {
        var (world, bot, enemy, ai) = Field();
        enemy.Position = bot.Position + new Offset(-(Tweaks.Bot.EngageRangeCells - 10), 0);
        world.Advance(i => i == 1 ? ai.GetInput(bot, enemy, world) : default);
        Assert.False(ai.IsProspecting, "an enemy within the engage range is fought");

        // It stays engaged a while after the enemy leaves the range (the world held still), then digs again.
        enemy.Position = enemy.Base!.Position;
        for (int tick = 0; tick < Tweaks.Bot.EngageHoldSeconds * Fps - 2; tick++)
        {
            ai.GetInput(bot, enemy, world);
            Assert.False(ai.IsProspecting, $"still engaged at tick {tick}");
        }
        for (int tick = 0; tick < 4; tick++)
            ai.GetInput(bot, enemy, world);
        Assert.True(ai.IsProspecting, "the enemy gone, it goes back to its digging");
    }

    [Fact]
    public void AHit_EndsProspecting_ForAWhile()
    {
        var (world, bot, enemy, ai) = Field();
        world.Advance(i => i == 1 ? ai.GetInput(bot, enemy, world) : default);
        Assert.True(ai.IsProspecting);

        bot.TakeDamage(5, Emberward.Core.Entities.DamageCause.EnemyFire);
        world.Advance(i => i == 1 ? ai.GetInput(bot, enemy, world) : default);
        Assert.False(ai.IsProspecting, "a hit is answered");
    }

    [Fact]
    public void WithoutOre_TheBotGoesForTheEnemy_AsBefore()
    {
        var (world, bot, enemy, ai) = Field(seedOre: false);
        int start = Distance(bot.Position, enemy.Position);
        for (int tick = 0; tick < 5 * Fps; tick++)
        {
            world.Advance(i => i == 1 ? ai.GetInput(bot, enemy, world) : default);
            Assert.False(ai.IsProspecting);
        }
        Assert.True(Distance(bot.Position, enemy.Position) < start - 20, "with nothing to mine it heads for the enemy");
    }

    /// <summary>On a generated level the bot leaves its base and digs ore in the opening, rather than making for its enemy.</summary>
    [Theory]
    [Trait("Category", "Slow")]
    [InlineData(7)]
    [InlineData(42)]
    public void OnAGeneratedLevel_TheBotMinesInTheOpening(int seed)
    {
        var world = TestHelpers.CreateSeededWorld(seed: seed, seedOre: true);
        var (player, botTank) = (world.TankList.Tanks[0], world.TankList.Tanks[1]);
        var ai = new BotTankAI(seed + 2);
        int start = Distance(botTank.Position, player.Position), nearest = start, mined = 0;
        var path = new System.Text.StringBuilder();
        for (int tick = 0; tick < 60 * Fps; tick++)
        {
            world.Advance(i => i == 1 ? ai.GetInput(botTank, player, world) : default);
            foreach (var e in world.StepEvents)
                if (e.Kind == StepEventKind.OreCollected && e.Color == botTank.Color)
                    mined += e.Count;
            nearest = Math.Min(nearest, Distance(botTank.Position, player.Position));
            if (tick % Fps == 0)
                path.Append(botTank.Position).Append(ai.Mode).Append(ai.IsProspecting ? "P" : "").Append(ai.IsMining ? "M" : "")
                    .Append(ai.OreTarget).Append(botTank.OreHeld).Append(' ');
        }
        Assert.True(mined > 0, $"the bot mined nothing in a minute; deposits: {world.OreDeposits.Count}. {path}");
        Assert.True(nearest > Tweaks.Bot.EngageRangeCells / 2, $"the bot closed on its idle enemy: {nearest} of {start}. home {botTank.Base!.Position} enemy {player.Position} deposits {string.Join(" ", world.OreDeposits.Select(d => $"{d.Kind}{d.Center}"))} {path}");
    }
}
