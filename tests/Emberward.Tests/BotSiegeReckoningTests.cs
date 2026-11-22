namespace Emberward.Tests;

using Emberward.Core;
using Emberward.Core.Bots;
using Emberward.Core.Config;
using Emberward.Core.Entities.Bases;
using Emberward.Core.Entities.Projectiles;
using Emberward.Core.Match;
using Emberward.Core.Resources;
using Emberward.Core.Terrain;
using Emberward.Core.Types;

/// <summary>
/// How the bot reckons whether it can break a base (<see cref="World.CanBreakBase(Emberward.Core.Entities.Tanks.Tank, TankBase)"/>):
/// its off-network clock limits how long it fires, the base's buy-back needs room in its reactor, the core repairs only
/// while it is damaged, and a decision once made is held with a margin (<see cref="BotTankAI.HoldsBaseBreakable"/>).
/// </summary>
public class BotSiegeReckoningTests
{
    private const int Fps = Tweaks.Perf.TargetFps;

    /// <summary>Blank ground, tank 0's base on the left and tank 1's (the bot's) on the right.</summary>
    private static World CreateWorld(SimulationSettings? settings = null)
    {
        var size = new Size(100, 72);
        var ground = new TerrainGrid(size);
        ground.Fill(TerrainPixel.Blank);
        var world = new World(size, rules: new MatchRules { EnergyNetwork = true, TerrainRegrowth = false }, simulationSettings: settings);
        world.Initialize(ground, [new Position(25, 36), new Position(75, 36)]);
        return world;
    }

    /// <summary>Leaves <paramref name="points"/> of shield the store can still buy back, above the reserve.</summary>
    private static void LeaveBuyBack(TankBase target, double points) =>
        target.DrawCharge(target.Charge - Tweaks.Keep.ShieldReserveCharge - points * Tweaks.Keep.ChargePerShieldPoint);

    [Fact]
    public void AFreshTank_FiresTheWholeWindow_AFastOffNetworkClockCutsItShort()
    {
        var world = CreateWorld();
        var bot = world.TankList.Tanks[1];
        Assert.Equal(Tweaks.Bot.SiegeWindowSeconds * Fps, world.SiegeFireTicks(bot, Tweaks.Tank.TurretDelay));

        // A clock ten times as fast: the floor reaches the heat the bot turns back at in about 6.5 s.
        var fast = CreateWorld(SimulationSettings.FromTweaks() with { TankOffNetworkHeatPerTick = 10f * Tweaks.Tank.OffNetworkHeatPerSecond / Fps });
        var (target, fastBot) = (fast.TankList.Tanks[0].Base!, fast.TankList.Tanks[1]);
        int ticks = fast.SiegeFireTicks(fastBot, Tweaks.Tank.TurretDelay);
        Assert.InRange(ticks, 5 * Fps, 7 * Fps);

        // A dark base a fresh tank breaks is more than it can fire at before it must head home.
        target.DrawCharge(target.Charge);
        Assert.True(fast.IsBaseDark(target));
        Assert.True(world.CanBreakBase(bot, Darkened(world)));
        Assert.False(fast.CanBreakBase(fastBot, target), "a tank that must soon head home cannot shoot a shield away");
    }

    private static TankBase Darkened(World world)
    {
        var target = world.TankList.Tanks[0].Base!;
        target.DrawCharge(target.Charge);
        return target;
    }

    [Fact]
    public void CoreRepairs_CountOnlyWhileTheCoreIsDamaged()
    {
        // A full shield and 9,000 points to buy back: the shield falls after (60,000 + 9,000) / 53.3 = 1,294 ticks, and
        // the core, whole until then, after about 120 more. Repairs counted for the whole window (another 2,880) would
        // have held it: 60,000 + 9,000 + 6,000 + 2,880 is more than the 76,800 the bot fires.
        var world = CreateWorld();
        var (target, bot) = (world.TankList.Tanks[0].Base!, world.TankList.Tanks[1]);
        LeaveBuyBack(target, 9_000);

        Assert.True(world.CanBreakBase(bot, target));
    }

    [Fact]
    public void AReactorWithNoRoom_BuysNoShieldBack()
    {
        // The shield's charge ends as heat in the reactor (BasePowerSystem.RechargeShield): a reactor that can take no
        // heat, full now and venting into no room, holds the buy-back back however full the store, as a spent store does.
        var world = CreateWorld();
        var (target, bot) = (world.TankList.Tanks[0].Base!, world.TankList.Tanks[1]);
        Assert.False(world.CanBreakBase(bot, target));

        target.Reactor.Capacity = new ReactorState(0, (int)target.Reactor.HealthCapacity);
        target.Reactor.Current.Heat = new Heat(0);
        Assert.Equal(0.0, target.ReactorRoomEnergy);
        Assert.True(world.CanBreakBase(bot, target));

        // And the game agrees: a shot-down shield is not bought back.
        world.Projectiles.Add(Projectile.CreateBullet(target.Position + new Offset(BaseLayout.Half + 1, 0), new VectorF(-1, 0),
            ownerColor: 1, damage: 1_000));
        world.Advance(_ => default);
        double shield = world.BasePowerOf(target)!.Value.ShieldFill;
        Assert.True(shield < 1.0);
        world.Advance(_ => default);
        Assert.Equal(shield, world.BasePowerOf(target)!.Value.ShieldFill);
    }

    [Fact]
    public void OpenGates_ThatShutAgainInTime_CountTheirShield()
    {
        // The shield shot away: the gates stand open, and the core alone falls to a whole gun in five seconds.
        var world = CreateWorld();
        var (target, bot) = (world.TankList.Tanks[0].Base!, world.TankList.Tanks[1]);
        world.Projectiles.Add(Projectile.CreateBullet(target.Position + new Offset(BaseLayout.Half + 1, 0), new VectorF(-1, 0),
            ownerColor: 1, damage: (int)Tweaks.Keep.ShieldCapacity));
        world.Advance(_ => default);
        Assert.False(world.AreGatesShut(target));
        Assert.True(world.CanBreakBase(bot, target));

        // A gun at 15 % (8 a tick, 6 past the repairs) would wear the core down in 1,000 ticks, but the shield is back
        // at a quarter in 750 and the gates shut, and that gun cannot shoot through a shield that buys back 20 a tick.
        Assert.False(world.CanBreakBase(bot, target, 0.15));
    }

    [Fact]
    public void ADecisionToBreak_IsHeldWhileTheBaseIsNearTheEdge()
    {
        var world = CreateWorld();
        var (enemy, bot) = (world.TankList.Tanks[0], world.TankList.Tanks[1]);
        var target = enemy.Base!;
        var ai = new BotTankAI(9);

        // 16,000 points to buy back: just more than the bot can break, but less than fire counted
        // SiegeHoldFireFactor times as strong can.
        LeaveBuyBack(target, 16_000);
        Assert.False(world.CanBreakBase(bot, target));
        Assert.True(world.CanBreakBase(bot, target, Tweaks.Bot.SiegeHoldFireFactor));
        ai.GetInput(bot, enemy, world);
        Assert.False(ai.HoldsBaseBreakable, "a bot that has not decided to break a base decides on the plain reckoning");

        // The store spent: now it can, and it holds to that as the store refills to the edge, every tick.
        target.DrawCharge(target.Charge - Tweaks.Keep.ShieldReserveCharge);
        ai.GetInput(bot, enemy, world);
        Assert.True(ai.HoldsBaseBreakable);
        target.StoreCharge(16_000 * Tweaks.Keep.ChargePerShieldPoint);
        for (int tick = 0; tick < 3 * Fps; tick++)
        {
            ai.GetInput(bot, enemy, world);
            Assert.True(ai.HoldsBaseBreakable, $"the decision should hold near the edge (tick {tick})");
        }

        // A full store is past the margin: the bot lets go.
        target.StoreCharge(target.ChargeCapacity);
        ai.GetInput(bot, enemy, world);
        Assert.False(ai.HoldsBaseBreakable);
    }
}
