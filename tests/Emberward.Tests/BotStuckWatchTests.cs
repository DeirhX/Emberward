namespace Emberward.Tests;

using Emberward.Core;
using Emberward.Core.Bots;
using Emberward.Core.Config;
using Emberward.Core.Match;
using Emberward.Core.Terrain;
using Emberward.Core.Types;

/// <summary>
/// A bot that stays near one spot for <see cref="Tweaks.Bot.StuckWindowSeconds"/> while trying to get somewhere drops
/// its goal and heads somewhere else (<see cref="BotStuckWatch"/>).
/// </summary>
public class BotStuckWatchTests
{
    private const int Fps = Tweaks.Perf.TargetFps;
    private const int Window = Tweaks.Bot.StuckWindowSeconds * Fps;

    [Fact]
    public void StayingNearOneSpot_ForTheWindow_IsGoingNowhere()
    {
        var watch = new BotStuckWatch();
        var spot = new Position(50, 50);
        int fired = -1;
        for (int tick = 0; tick <= Window + 1 && fired < 0; tick++)
        {
            // Rocking about within the radius is not headway.
            var at = spot + new Offset(tick % 2 == 0 ? Tweaks.Bot.StuckRadiusCells : -Tweaks.Bot.StuckRadiusCells / 2, 0);
            if (watch.Watch(tick == 0 ? spot : at))
                fired = tick;
        }
        Assert.Equal(Window, fired);
        Assert.Equal(0, watch.FramesNearAnchor);
    }

    [Fact]
    public void MovingOn_OrStandingStillOnPurpose_StartsTheCountAgain()
    {
        var watch = new BotStuckWatch();
        var at = new Position(50, 50);
        for (int tick = 0; tick < Window - 1; tick++)
            Assert.False(watch.Watch(at));
        Assert.False(watch.Watch(at + new Offset(Tweaks.Bot.StuckRadiusCells + 1, 0)));
        Assert.Equal(0, watch.FramesNearAnchor);

        for (int tick = 0; tick < Window - 1; tick++)
            Assert.False(watch.Watch(at));
        watch.Reset();
        Assert.False(watch.Watch(at));
        Assert.Equal(0, watch.FramesNearAnchor);
    }

    [Fact]
    public void ADetour_EndsWhenReached_OrWhenItsTimeIsUp_AndStaysOnTheMap()
    {
        var watch = new BotStuckWatch();
        var size = new Size(100, 80);
        // Against the edge the clamped end is already within reach: no detour, nothing to give up for.
        Assert.False(watch.StartDetour(new Position(90, 40), new Offset(1, 0), size));
        Assert.Null(watch.Detour);

        Assert.True(watch.StartDetour(new Position(80, 40), new Offset(1, 0), size));
        var end = Assert.NotNull(watch.Detour);
        Assert.True(end.X < size.X && end.X > 80, $"the detour end should be pulled inside the map: {end}");

        watch.AdvanceDetour(end);
        Assert.Null(watch.Detour);

        watch.StartDetour(new Position(10, 40), new Offset(1, 0), size);
        for (int tick = 0; tick < Tweaks.Bot.DetourSeconds * Fps; tick++)
            watch.AdvanceDetour(new Position(10, 40));
        Assert.Null(watch.Detour);

        watch.StartDetour(new Position(10, 40), default, size);
        Assert.Null(watch.Detour);
    }

    /// <summary>
    /// The enemy hides in a box of frost rock, which nothing gets through. The bot chasing it drives into a pocket of
    /// frost rock that opens away from the enemy, and is pinned against the pocket's far wall: no step toward the enemy
    /// gets anywhere, and a breakout to either side meets the pocket's walls. It gives up on the enemy for a while and
    /// heads somewhere else.
    /// </summary>
    [Fact]
    public void ABotPinnedAgainstAWall_PicksANewGoal()
    {
        var size = new Size(160, 110);
        var ground = new TerrainGrid(size);
        ground.Fill(TerrainPixel.Blank);
        var world = new World(size, rules: new MatchRules { TerrainRegrowth = false });
        world.Initialize(ground, [new Position(25, 30), new Position(135, 30)]);
        var (enemy, bot) = (world.TankList.Tanks[0], world.TankList.Tanks[1]);

        var hideout = new Position(80, 98);
        for (int dy = -9; dy <= 9; dy++)
            for (int dx = -9; dx <= 9; dx++)
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) >= 7)
                    world.Terrain.SetPixel(hideout + new Offset(dx, dy), TerrainPixel.FrostRock);
        // The pocket: walls three cells thick left, right and on the enemy's side, open on the far side.
        var pocket = new Position(80, 60);
        for (int dy = -8; dy <= 10; dy++)
            for (int dx = -11; dx <= 11; dx++)
                if (Math.Abs(dx) >= 9 || dy >= 8)
                    world.Terrain.SetPixel(pocket + new Offset(dx, dy), TerrainPixel.FrostRock);
        enemy.Position = hideout;
        bot.Position = pocket;

        var ai = new BotTankAI(9);
        for (int tick = 0; tick < 600 && ai.Mode != TwitchMode.Twitch; tick++)
            ai.GetInput(bot, enemy, world);
        Assert.Equal(TwitchMode.Twitch, ai.Mode);

        Position? detour = null;
        int farthest = 0;
        var start = bot.Position;
        var path = new System.Text.StringBuilder();
        for (int tick = 0; tick < 3 * Window; tick++)
        {
            world.Advance(i => i == 1 ? ai.GetInput(bot, enemy, world) : default);
            enemy.Heat = 0f;
            enemy.Position = hideout;
            detour ??= ai.Detour;
            if (tick % Fps == 0)
                path.Append(bot.Position - hideout).Append(ai.Mode).Append(' ');
            if (detour != null)
                farthest = Math.Max(farthest, Math.Max(Math.Abs(bot.Position.X - start.X), Math.Abs(bot.Position.Y - start.Y)));
        }

        Assert.True(detour != null, $"the bot should have given up on the enemy. each second: {path}");
        int detourFromEnemy = Math.Max(Math.Abs(detour.Value.X - hideout.X), Math.Abs(detour.Value.Y - hideout.Y));
        Assert.True(detourFromEnemy > Tweaks.Bot.StuckRadiusCells, $"the new goal should be away from the old one: {detour}");
        Assert.True(farthest > Tweaks.Bot.StuckRadiusCells, $"the bot should leave the wall it was pinned against: {farthest} cells");
    }
}
