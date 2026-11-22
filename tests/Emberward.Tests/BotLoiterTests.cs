namespace Emberward.Tests;

using Emberward.Core;
using Emberward.Core.Bots;
using Emberward.Core.Config;
using Emberward.Core.Entities.Bases;
using Emberward.Core.Entities.Projectiles;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Input;
using Emberward.Core.Match;
using Emberward.Core.Terrain;
using Emberward.Core.Types;

/// <summary>
/// Whether a bot can break a base (<see cref="World.CanBreakBase"/>), and what it does when it cannot: while its enemy
/// sits docked there it waits outside the gate nearest it with its fire held (<see cref="BotLoiter"/>), and it fights
/// the enemy once it comes out.
/// </summary>
public class BotLoiterTests
{
    private const int Fps = Tweaks.Perf.TargetFps;
    private const int Half = BaseLayout.Half;

    /// <summary>Blank ground, tank 0's base on the left and tank 1's (the bot's) on the right.</summary>
    private static World CreateWorld(bool energyNetwork = true, int width = 100, bool tankShield = false)
    {
        var size = new Size(width, 72);
        var ground = new TerrainGrid(size);
        ground.Fill(TerrainPixel.Blank);
        var world = new World(size, rules: new MatchRules { EnergyNetwork = energyNetwork, TerrainRegrowth = false, TankShield = tankShield });
        world.Initialize(ground, [new Position(25, 36), new Position(width - 25, 36)]);
        return world;
    }

    [Fact]
    public void ASoundKeep_IsMoreThanALoneTankCanBreak()
    {
        var world = CreateWorld();
        var (enemy, bot) = (world.TankList.Tanks[0], world.TankList.Tanks[1]);

        Assert.False(world.CanBreakBase(bot, enemy.Base!));
        Assert.False(world.CanBreakBase(bot, bot.Base!), "a tank never lays siege to its own base");
    }

    [Fact]
    public void WithoutAGateShield_TheCoreAloneFalls()
    {
        var world = CreateWorld(energyNetwork: false);

        Assert.True(world.CanBreakBase(world.TankList.Tanks[1], world.TankList.Tanks[0].Base!));
    }

    [Fact]
    public void ADarkBase_OrOneWithNoChargeToSpare_OrAWornShield_CanBeBroken()
    {
        var dark = CreateWorld();
        dark.TankList.Tanks[0].Base!.DrawCharge(dark.TankList.Tanks[0].Base!.Charge);
        Assert.True(dark.IsBaseDark(dark.TankList.Tanks[0].Base!));
        Assert.True(dark.CanBreakBase(dark.TankList.Tanks[1], dark.TankList.Tanks[0].Base!), "a dark base buys no shield back");

        // The store down to its reserve: the shield cannot be bought back, and the shield and core alone are breakable.
        var spent = CreateWorld();
        var spentBase = spent.TankList.Tanks[0].Base!;
        spentBase.DrawCharge(spentBase.Charge - Tweaks.Keep.ShieldReserveCharge);
        Assert.False(spent.IsBaseDark(spentBase));
        Assert.True(spent.CanBreakBase(spent.TankList.Tanks[1], spentBase));

        // A full store, but most of the shield shot away.
        var worn = CreateWorld();
        var wornBase = worn.TankList.Tanks[0].Base!;
        worn.Projectiles.Add(Projectile.CreateBullet(wornBase.Position + new Offset(Half + 1, 0), new VectorF(-1, 0), ownerColor: 1,
            damage: (int)(Tweaks.Keep.ShieldCapacity * 0.8)));
        worn.Advance(_ => default);
        Assert.True(worn.AreGatesShut(wornBase));
        Assert.True(worn.BasePowerOf(wornBase)!.Value.ShieldFill < 0.25);
        Assert.True(worn.CanBreakBase(worn.TankList.Tanks[1], wornBase));
    }

    [Fact]
    public void WhileTheEnemyIsDockedInASoundBase_TheBotWaitsOutsideAGate_WithoutFiring_ThenFightsWhenItComesOut()
    {
        var world = CreateWorld(width: 160);
        var (enemy, bot) = (world.TankList.Tanks[0], world.TankList.Tanks[1]);
        var enemyBase = enemy.Base!;
        Assert.True(enemy.IsDockedAtOwnBase);
        var ai = new BotTankAI(9);

        bool fired = false;
        // Past the gunner's opening grace, so that once the enemy comes out nothing but the loiter holds its fire.
        for (int tick = 0; tick < 40 * Fps; tick++)
        {
            world.Advance(i =>
            {
                if (i != 1)
                    return default;
                var output = ai.GetInput(bot, enemy, world);
                fired |= output.ShootPrimary;
                return output;
            });
            enemy.Heat = 0f; // Docked, it stays cool anyway; keep the scene still.
        }

        Assert.True(enemy.IsDockedAtOwnBase);
        Assert.False(world.CanBreakBase(bot, enemyBase));
        Assert.False(fired, "the bot should not fire at a base it cannot break, nor at the enemy inside");
        Assert.Equal(1.0, world.BasePowerOf(enemyBase)!.Value.ShieldFill);
        Assert.Equal(1.0, enemyBase.ArmorFill);
        int fromCentre = Math.Max(Math.Abs(bot.Position.X - enemyBase.Position.X), Math.Abs(bot.Position.Y - enemyBase.Position.Y));
        Assert.True(fromCentre > Half + 3 && fromCentre <= BotLoiter.PostDistance + BotLoiter.PostSlack,
            $"the bot should wait just outside the base: {bot.Position - enemyBase.Position} from its centre");
        Assert.Equal(TwitchMode.Twitch, ai.Mode);

        // The enemy drives out through the east gate, toward the bot: it is fought.
        enemy.Position = enemyBase.Position + new Offset(Half + 5, 0);
        Assert.False(enemy.IsDockedAtOwnBase);
        bool firedAtEnemy = false;
        for (int tick = 0; tick < 5 * Fps && !firedAtEnemy; tick++)
        {
            world.Advance(i =>
            {
                if (i != 1)
                    return default;
                var output = ai.GetInput(bot, enemy, world);
                if (output.ShootPrimary)
                {
                    var toEnemy = enemy.Position - bot.Position;
                    firedAtEnemy = output.AimDirection.X * toEnemy.X + output.AimDirection.Y * toEnemy.Y > 0f;
                }
                return output;
            });
        }
        Assert.True(firedAtEnemy, "out of its base the enemy should be fought");
    }

    [Fact]
    public void ThePost_IsInFrontOfTheNearestGate_InsideTheMap()
    {
        var world = CreateWorld(width: 160);
        var target = world.TankBases.Bases[0];

        var east = BotLoiter.Post(target.Position + new Offset(60, 3), target, world.Terrain);
        Assert.Equal(target.Position + new Offset(BotLoiter.PostDistance, BotLoiter.PostSideStep), east);
        Assert.True(BotLoiter.PostSideStep - TankSpriteShape.Radius > BaseLayout.GateHalf,
            "the tank at its post should stand clear of the straight line out of the gate");
        var eastNorth = BotLoiter.Post(target.Position + new Offset(60, -3), target, world.Terrain);
        Assert.Equal(target.Position + new Offset(BotLoiter.PostDistance, -BotLoiter.PostSideStep), eastNorth);

        // The base is 25 cells from the west edge: a post there would be off the map, so the next nearest is taken.
        var fromWest = BotLoiter.Post(target.Position + new Offset(-24, 4), target, world.Terrain);
        Assert.NotNull(fromWest);
        Assert.True(world.Terrain.IsInside(fromWest.Value));
        Assert.NotEqual(target.Position + new Offset(-BotLoiter.PostDistance, 0), fromWest);
    }

    /// <summary>
    /// Runs the bot (slot 1) against a still, cool enemy for <paramref name="ticks"/>: whether it fired while waiting at
    /// a post, and whether it waited at all.
    /// </summary>
    private static (bool Fired, bool Waited) Run(World world, BotTankAI ai, int ticks)
    {
        var (enemy, bot) = (world.TankList.Tanks[0], world.TankList.Tanks[1]);
        bool fired = false, waited = false;
        for (int tick = 0; tick < ticks; tick++)
        {
            world.Advance(i =>
            {
                if (i != 1)
                    return default;
                var output = ai.GetInput(bot, enemy, world);
                // Only fire from a post counts: out of it the bot fights an enemy it sees, as it should.
                fired |= output.ShootPrimary && ai.IsWaiting;
                waited |= ai.IsWaiting;
                return output;
            });
            enemy.Heat = 0f;
        }
        return (fired, waited);
    }

    [Fact]
    public void PastTheSiegeGrace_TheBotStillWaitsOutsideASoundBase()
    {
        var world = CreateWorld(width: 160);
        var (enemy, bot) = (world.TankList.Tanks[0], world.TankList.Tanks[1]);
        var ai = new BotTankAI(9);

        var (fired, _) = Run(world, ai, (Tweaks.Weapon.BotBaseSiegeGraceSeconds + 20) * Fps);

        Assert.True(enemy.IsDockedAtOwnBase);
        Assert.True(ai.IsWaiting, "past the grace a base it cannot break is still waited outside");
        Assert.False(fired, "nor is it fired on from the post once the grace is over");
        Assert.True(BotLoiter.IsAtPost(bot.Position, BotLoiter.Post(bot.Position, enemy.Base!, world.Terrain)!.Value),
            $"the bot should stand at its post: {bot.Position - enemy.Base!.Position} from the base's centre");
    }

    [Fact]
    public void DuringTheSiegeGrace_ABreakableBase_IsNotWaitedOutside()
    {
        var world = CreateWorld(energyNetwork: false, width: 160);
        var (enemy, bot) = (world.TankList.Tanks[0], world.TankList.Tanks[1]);
        Assert.True(world.CanBreakBase(bot, enemy.Base!));
        var ai = new BotTankAI(9);

        var (_, waited) = Run(world, ai, 40 * Fps);

        Assert.True(enemy.IsDockedAtOwnBase);
        Assert.False(waited, "a base it can break is only not yet allowed: the bot roams until the grace is over");
    }

    [Fact]
    public void HitAtItsPost_TheBotStopsWaiting()
    {
        var world = CreateWorld(width: 160);
        var (enemy, bot) = (world.TankList.Tanks[0], world.TankList.Tanks[1]);
        var ai = new BotTankAI(9);
        Run(world, ai, 40 * Fps);
        Assert.True(ai.IsWaiting);

        // A shot from inside the base lands on the waiting bot.
        world.Projectiles.Add(Projectile.CreateBullet(bot.Position + new Offset(-6, 0), new VectorF(1, 0), ownerColor: 0, damage: 10));
        bool stopped = false;
        for (int tick = 0; tick < Fps && !stopped; tick++)
        {
            world.Advance(i => i == 1 ? ai.GetInput(bot, enemy, world) : default);
            stopped = !ai.IsWaiting;
        }
        Assert.True(stopped, "a bot that is shot at its post should leave it or fight back");
    }

    [Fact]
    public void AHitTheShieldTook_AlsoStopsTheWaiting()
    {
        var world = CreateWorld(width: 160, tankShield: true);
        var (enemy, bot) = (world.TankList.Tanks[0], world.TankList.Tanks[1]);
        var ai = new BotTankAI(9);
        Run(world, ai, 40 * Fps);
        Assert.True(ai.IsWaiting);

        float hull = bot.Reactor.Health;
        bot.TakeDamage(5, Emberward.Core.Entities.DamageCause.EnemyFire);
        Assert.Equal(hull, bot.Reactor.Health);
        world.Advance(i => i == 1 ? ai.GetInput(bot, enemy, world) : default);
        Assert.False(ai.IsWaiting, "fire the shield absorbed is still fire");
    }
    [Fact]
    public void APostInRockOrAWall_IsPassedOver_AndWithNoPostLeftTheBotDoesNotWait()
    {
        var world = CreateWorld(width: 160);
        var target = world.TankBases.Bases[0];
        var from = target.Position + new Offset(60, 3);
        var east = target.Position + new Offset(BotLoiter.PostDistance, BotLoiter.PostSideStep);
        Assert.Equal(east, BotLoiter.Post(from, target, world.Terrain));

        // Rock over the nearest post: the next nearest, across the gate, is taken.
        Fill(world, east, TerrainPixel.Rock);
        Assert.False(BotLoiter.CanStandAt(east, world.Terrain));
        Assert.Equal(target.Position + new Offset(BotLoiter.PostDistance, -BotLoiter.PostSideStep),
            BotLoiter.Post(from, target, world.Terrain));

        // Rock over every post: there is nowhere to wait.
        foreach (var gate in BaseLayout.GateDirections)
            foreach (int side in new[] { 1, -1 })
                Fill(world, target.Position + new Offset(gate.X * BotLoiter.PostDistance + gate.Y * side * BotLoiter.PostSideStep,
                    gate.Y * BotLoiter.PostDistance + gate.X * side * BotLoiter.PostSideStep), TerrainPixel.Rock);
        Assert.Null(BotLoiter.Post(from, target, world.Terrain));

        var (enemy, bot) = (world.TankList.Tanks[0], world.TankList.Tanks[1]);
        var (_, waited) = Run(world, new BotTankAI(9), 40 * Fps);
        Assert.True(enemy.IsDockedAtOwnBase);
        Assert.False(world.CanBreakBase(bot, enemy.Base!));
        Assert.False(waited, "with no post a tank can stand at, the bot should not wait (and jitter) at the base");
    }

    /// <summary>Writes <paramref name="pixel"/> over the square a tank at <paramref name="centre"/> would stand on, and round it.</summary>
    private static void Fill(World world, Position centre, TerrainPixel pixel)
    {
        int reach = TankSpriteShape.Radius + 1;
        for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
                if (world.Terrain.IsInside(centre + new Offset(dx, dy)))
                    world.Terrain.SetPixel(centre + new Offset(dx, dy), pixel);
    }

    [Fact]
    public void BoxedInOnTheWayToItsPost_TheBotNeverTorchesWhileWaiting_AndGivesUp()
    {
        var world = CreateWorld(width: 160);
        var (enemy, bot) = (world.TankList.Tanks[0], world.TankList.Tanks[1]);
        var ai = new BotTankAI(9);
        ControllerOutput Step()
        {
            ControllerOutput output = default;
            world.Advance(i => i == 1 ? output = ai.GetInput(bot, enemy, world) : default);
            enemy.Heat = 0f;
            return output;
        }

        // On its way to its post, out of its own base, the bot is walled in by rock: it cannot get out without torching,
        // which it does not do near the gate, not even in the manoeuvres that try to get it unstuck.
        for (int tick = 0; tick < 30 * Fps && !(ai.IsWaiting && !bot.Base!.IsInside(bot.Position) &&
            bot.Position.X < bot.Base.Position.X - Half - 10); tick++)
            Step();
        Assert.True(ai.IsWaiting);
        var boxed = bot.Position;
        // Rock right up to the hull, the cells every facing of it covers: any step or turn runs into it.
        int inner = TankSpriteShape.Radius;
        int[] facings = [0, 1, 2, 3, 5, 6, 7, 8];
        for (int dy = -inner - 3; dy <= inner + 3; dy++)
            for (int dx = -inner - 3; dx <= inner + 3; dx++)
            {
                var cell = boxed + new Offset(dx, dy);
                if (world.Terrain.IsInside(cell) && !facings.All(f => TankSpriteShape.Contains(f, boxed, cell)))
                    world.Terrain.SetPixel(cell, TerrainPixel.Rock);
            }

        bool fired = false, gaveUp = false;
        for (int tick = 0; tick < (Tweaks.Bot.LoiterApproachSeconds + 5) * Fps && !gaveUp; tick++)
        {
            bool wasWaiting = ai.IsWaiting;
            var output = Step();
            fired |= output.ShootPrimary && wasWaiting && ai.IsWaiting;
            gaveUp = !ai.IsWaiting;
        }

        Assert.False(fired, "on the way to its post the bot should hold its fire, torch included");
        Assert.True(gaveUp, "a bot that gets no nearer its post should give up waiting there");
        Assert.True(Math.Abs(bot.Position.X - boxed.X) <= inner && Math.Abs(bot.Position.Y - boxed.Y) <= inner,
            "the rock should have held it");
    }
}
