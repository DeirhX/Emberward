namespace Emberward.Tests;

using Emberward.Core;
using Emberward.Core.Bots;
using Emberward.Core.Config;
using Emberward.Core.Entities.Bases;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Match;
using Emberward.Core.Terrain;
using Emberward.Core.Types;

/// <summary>
/// The bot puts its ore down where a player must (DESIGN.md §6.3): it drives home and touches its core, which banks the
/// hold, or burns it to restart a dark base. A hot load sends it home before the heat does.
/// </summary>
public class BotUnloadTests
{
    private const int Fps = Tweaks.Perf.TargetFps;

    /// <summary>Blank map, two bases side by side; tank 1 is the bot, sent out of its base and roaming.</summary>
    private static (World World, Tank Bot, Tank Enemy, BotTankAI Ai) Roaming(bool energyNetwork)
    {
        var size = new Size(100, 72);
        var ground = new TerrainGrid(size);
        ground.Fill(TerrainPixel.Blank);
        var world = new World(size, rules: new MatchRules { TerrainRegrowth = false, EnergyNetwork = energyNetwork });
        world.Initialize(ground, [new Position(25, 36), new Position(75, 36)]);
        var enemy = world.TankList.Tanks[0];
        var bot = world.TankList.Tanks[1];
        var ai = new BotTankAI(5);
        // Just outside the top gate of its own base.
        bot.Position = bot.Base!.Position + new Offset(0, -(BaseLayout.Half + 6));
        for (int tick = 0; tick < 600 && ai.Mode != TwitchMode.Twitch; tick++)
            ai.GetInput(bot, enemy, world);
        Assert.Equal(TwitchMode.Twitch, ai.Mode);
        return (world, bot, enemy, ai);
    }

    [Fact]
    public void ABotWithAFullHold_GoesHome_TouchesTheCore_AndBanksIt()
    {
        var (world, bot, enemy, ai) = Roaming(energyNetwork: false);
        var home = bot.Base!;
        int store = home.Store.Ore;
        bot.Hold.Ore = Tweaks.Ore.TankHold;

        bool touched = false;
        int hotTicks = 0, longestHot = 0;
        for (int tick = 0; tick < 20 * Fps && bot.OreHeld > 0; tick++)
        {
            world.Advance(i => i == 1 ? ai.GetInput(bot, enemy, world) : default);
            Assert.False(bot.IsDead, $"the bot died with {bot.OreHeld} ore aboard at {bot.Position - home.Position}, mode {ai.Mode}");
            touched |= bot.TouchesCore(home);
            hotTicks = bot.IsCargoHot ? hotTicks + 1 : 0;
            longestHot = Math.Max(longestHot, hotTicks);
        }

        Assert.True(touched, "the bot should drive up to its core");
        Assert.Equal(0, bot.OreHeld);
        Assert.Equal(store + Tweaks.Ore.TankHold, home.Store.Ore);
        Assert.True(longestHot < 8 * Fps, $"the bot carried a hot load for {longestHot / (float)Fps:0.0} s");
    }

    [Fact]
    public void ABotWithOre_RestartsItsDarkBase_AtTheCore()
    {
        var (world, bot, enemy, ai) = Roaming(energyNetwork: true);
        var home = bot.Base!;
        home.DrawCharge(home.Charge);
        world.Advance(_ => default);
        Assert.True(world.IsBaseDark(home));
        bot.Hold.Ore = 19;

        for (int tick = 0; tick < 30 * Fps && world.IsBaseDark(home); tick++)
            world.Advance(i => i == 1 ? ai.GetInput(bot, enemy, world) : default);

        Assert.False(world.IsBaseDark(home), $"the base should restart; the bot is at {bot.Position - home.Position} in mode {ai.Mode}");
        Assert.True(bot.OreHeld < 19);
        // What the restart did not need banks once the base has power again.
        for (int tick = 0; tick < 5 * Fps && bot.OreHeld > 0; tick++)
            world.Advance(i => i == 1 ? ai.GetInput(bot, enemy, world) : default);
        Assert.Equal(0, bot.OreHeld);
    }
}
