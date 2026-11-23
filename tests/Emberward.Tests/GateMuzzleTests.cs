namespace Emberward.Tests;

using Emberward.Core;
using Emberward.Core.Collision;
using Emberward.Core.Config;
using Emberward.Core.Entities;
using Emberward.Core.Entities.Bases;
using Emberward.Core.Entities.Machines;
using Emberward.Core.Entities.Projectiles;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Input;
using Emberward.Core.Match;
using Emberward.Core.Rendering;
using Emberward.Core.Terrain;
using Emberward.Core.Types;

/// <summary>
/// A barrel does not reach through a keep (DESIGN.md §4.9). A shell starts at the barrel's tip, a few cells past the
/// hull, and is tested from its first step on: a tank touching a wall or a shut gate has its tip in or beyond it. Such
/// a shell must stop on the wall, or on the gate shield, exactly as one fired from further back does
/// (<see cref="ProjectileList.MuzzleOf"/>).
/// </summary>
public class GateMuzzleTests
{
    private const int Fps = Tweaks.Perf.TargetFps;
    private const int Half = BaseLayout.Half;

    /// <summary>Open ground with two keeps; the second one's tank is docked just inside its west gate.</summary>
    private static World CreateWorld(bool energyNetwork = true)
    {
        var size = new Size(140, 100);
        var ground = new TerrainGrid(size);
        ground.Fill(TerrainPixel.Blank);
        var world = new World(size, rules: new MatchRules { EnergyNetwork = energyNetwork, TerrainRegrowth = false });
        world.Initialize(ground, [new Position(30, 50), new Position(100, 50)]);
        Assert.Equal(new Offset(-1, 0), world.TankBases.Bases[1].Front);
        return world;
    }

    private readonly record struct Tally(int Shots, int ShieldHits, int CoreHits, int TankHits, int WallHits);

    /// <summary>
    /// <paramref name="tank"/> holds the trigger for <paramref name="seconds"/>, aiming along <paramref name="aim"/> (at
    /// the middle of <paramref name="target"/> when null) and driving by <paramref name="move"/>; the last half second
    /// lets the shells in flight land. A tank that dies meanwhile (bursts this near overheat it within seconds) does
    /// nothing more: the count is of the shots it fired from where it stood.
    /// </summary>
    private static Tally Fire(World world, Tank tank, TankBase target, double seconds, Offset move = default, Offset? aim = null)
    {
        int slot = world.TankList.Tanks.ToList().IndexOf(tank);
        int shots = 0, shield = 0, core = 0, tanks = 0, walls = 0;
        int firing = (int)(seconds * Fps);
        bool down = false;
        for (int tick = 0; tick < firing + Fps / 2; tick++)
        {
            down |= tank.IsDead;
            var toward = aim ?? (target.Position - tank.Position);
            float length = MathF.Sqrt(toward.X * toward.X + toward.Y * toward.Y);
            var input = new ControllerOutput
            {
                MoveSpeed = move,
                ShootPrimary = tick < firing,
                AimDirection = new DirectionF(toward.X / length, toward.Y / length),
            };
            world.Advance(i => i == slot && !down ? input : default);
            foreach (ref readonly StepEvent e in world.StepEvents)
            {
                if (e.Kind == StepEventKind.ShotFired) shots++;
                else if (e.Kind is StepEventKind.BaseShieldHit or StepEventKind.BaseShieldBroken) shield++;
                else if (e.Kind is StepEventKind.BaseDamaged or StepEventKind.BaseDestroyed) core++;
                else if (e.Kind == StepEventKind.BulletImpact && e.Target == ImpactTarget.Tank) tanks++;
                else if (e.Kind == StepEventKind.BulletImpact && e.Target == ImpactTarget.Terrain) walls++;
            }
        }
        return new Tally(shots, shield, core, tanks, walls);
    }

    /// <summary>
    /// Puts <paramref name="tank"/> 30 cells out from <paramref name="target"/> on <paramref name="side"/>,
    /// <paramref name="across"/> cells along the wall from the gate's middle, and drives it straight at the base until
    /// it is <paramref name="standoff"/> cells short of where the keep stops it. Returns the way it drove.
    /// </summary>
    private static Offset DriveUp(World world, Tank tank, TankBase target, Offset side, int across = 0, int standoff = 0)
    {
        var toward = new Offset(-side.X, -side.Y);
        tank.Position = target.Position + new Offset(side.X * 30 + side.Y * across, side.Y * 30 + side.X * across);
        for (int i = 0; i < 2 * Fps; i++)
            world.Advance(slot => slot == 0 ? new ControllerOutput { MoveSpeed = toward } : default);
        Assert.NotEqual(StepObstacle.None, world.ProbeTankStep(tank, toward));
        tank.Position += new Offset(side.X * standoff, side.Y * standoff);
        return toward;
    }

    private static void AssertUntouched(TankBase target)
    {
        Assert.False(target.IsDestroyed);
        Assert.Equal(Tweaks.Base.InitialHealth, (int)target.Reactor.Health);
    }

    [Theory]
    [InlineData(-1, 0)] // the tip of the barrel is on the gate's line here, and the owner is docked just behind it
    [InlineData(1, 0)]
    [InlineData(0, -1)] // a tank facing up or down stops a cell nearer: its tip is past the gate, over the floor
    [InlineData(0, 1)]
    public void ATankTouchingAShutGate_WearsTheShield_AndNeverReachesTheCore(int sideX, int sideY)
    {
        var world = CreateWorld();
        var attacker = world.TankList.Tanks[0];
        var owner = world.TankList.Tanks[1];
        var target = world.TankBases.Bases[1];
        var toward = DriveUp(world, attacker, target, new Offset(sideX, sideY));
        Assert.Equal(StepObstacle.Entity, world.ProbeTankStep(attacker, toward));

        // It keeps pushing against the gate while it fires. The base used to fall in five seconds.
        var tally = Fire(world, attacker, target, seconds: 10, move: toward);

        Assert.True(tally.Shots >= 15, $"only {tally.Shots} shots");
        Assert.Equal(tally.Shots, tally.ShieldHits);
        Assert.Equal(0, tally.CoreHits);
        Assert.Equal(0, tally.TankHits);
        AssertUntouched(target);
        Assert.True(world.AreGatesShut(target));
        Assert.Equal(Tweaks.Tank.InitialHealth, (int)owner.Reactor.Health);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void ATankFurtherBackFromAShutGate_WearsTheShieldToo(int standoff)
    {
        var world = CreateWorld();
        var attacker = world.TankList.Tanks[0];
        var target = world.TankBases.Bases[1];
        DriveUp(world, attacker, target, new Offset(0, -1), standoff: standoff);

        var tally = Fire(world, attacker, target, seconds: 10);

        Assert.True(tally.Shots > 20, $"only {tally.Shots} shots");
        Assert.Equal(tally.Shots, tally.ShieldHits);
        Assert.Equal(0, tally.CoreHits);
        AssertUntouched(target);
    }

    [Fact]
    public void ATankTheGatesShutOn_WithItsCentreOutsideTheLine_WearsTheShield()
    {
        var world = CreateWorld();
        var attacker = world.TankList.Tanks[0];
        var target = world.TankBases.Bases[1];
        // Its hull lies across the gate, as when the shield came back while it drove in.
        attacker.Position = target.Position + new Offset(0, -Half - 1);

        var tally = Fire(world, attacker, target, seconds: 5);

        Assert.Equal(tally.Shots, tally.ShieldHits);
        Assert.Equal(0, tally.CoreHits);
        AssertUntouched(target);
    }

    [Theory]
    [InlineData(-1, 0, true)]
    [InlineData(0, -1, true)]
    [InlineData(0, 1, true)]
    [InlineData(0, -1, false)] // with no gate shield the walls are the same
    public void ATankTouchingAWall_DoesNotShootThroughIt(int sideX, int sideY, bool energyNetwork)
    {
        var world = CreateWorld(energyNetwork);
        var attacker = world.TankList.Tanks[0];
        var target = world.TankBases.Bases[1];
        // Beside the gate, against the wall, with the core in its sights across the courtyard.
        DriveUp(world, attacker, target, new Offset(sideX, sideY), across: BaseLayout.GateHalf + 4);

        var tally = Fire(world, attacker, target, seconds: 3);

        Assert.True(tally.Shots > 15, $"only {tally.Shots} shots");
        Assert.Equal(tally.Shots, tally.WallHits);
        Assert.Equal(0, tally.ShieldHits);
        Assert.Equal(0, tally.CoreHits);
        AssertUntouched(target);
    }

    [Fact]
    public void TheOwnersBarrel_ReachesOutThroughItsOwnShutGate_ButNotThroughItsWall()
    {
        var world = CreateWorld();
        var raider = world.TankList.Tanks[0];
        var owner = world.TankList.Tanks[1];
        var target = world.TankBases.Bases[1];
        var north = new Offset(0, -1);
        // Three cells inside the north gate: the tip of its barrel is one cell outside. A raider waits in front of it.
        owner.Position = target.Position + new Offset(0, -Half + 3);
        raider.Position = target.Position + new Offset(0, -Half - 8);

        var tally = Fire(world, owner, target, seconds: 0.5, aim: north);

        Assert.True(tally.Shots >= 3, $"only {tally.Shots} shots");
        Assert.Equal(new Tally(tally.Shots, ShieldHits: 0, CoreHits: 0, TankHits: tally.Shots, WallHits: 0), tally);
        Assert.Equal(1.0, world.BasePowerOf(target)!.Value.ShieldFill);

        // Beside the gate the same shot meets its own wall.
        owner.Position = target.Position + new Offset(BaseLayout.GateHalf + 4, -Half + 3);
        tally = Fire(world, owner, target, seconds: 0.5, aim: north);
        Assert.True(tally.Shots >= 3, $"only {tally.Shots} shots");
        Assert.Equal(new Tally(tally.Shots, ShieldHits: 0, CoreHits: 0, TankHits: 0, WallHits: tally.Shots), tally);
    }

    [Fact]
    public void ATurretBesideAnEnemysShutGate_WearsTheShield_AndHitsNothingBehindIt()
    {
        var size = new Size(140, 100);
        var ground = new TerrainGrid(size);
        ground.Fill(TerrainPixel.Blank);
        var world = new World(size, rules: new MatchRules { EnergyNetwork = true, TerrainRegrowth = false });
        // The keeps stand ten cells apart, so the first one's coverage reaches the second one's west gate.
        world.Initialize(ground, [new Position(30, 50), new Position(30 + Tweaks.Base.BaseSize + 10, 50)]);
        var home = world.TankBases.Bases[0];
        var target = world.TankBases.Bases[1];
        var owner = world.TankList.Tanks[1];
        // Three cells from the gate: the turret's barrel reaches a cell past it. The owner stands just inside.
        var turret = new Machine(target.Position + new Offset(-Half - 3, 0), MachineType.Turret, home.Color);
        world.Machines.Add(turret);
        world.PlantMachine(turret, home);
        owner.Position = target.Position + new Offset(-Half + 6, 0);

        int fired = 0, shield = 0, other = 0;
        for (int tick = 0; tick < 20 * Fps; tick++)
        {
            world.Advance(_ => default);
            foreach (ref readonly StepEvent e in world.StepEvents)
            {
                if (e.Kind == StepEventKind.TurretFired) fired++;
                else if (e.Kind is StepEventKind.BaseShieldHit or StepEventKind.BaseShieldBroken) shield++;
                else if (e.Kind is StepEventKind.BaseDamaged or StepEventKind.BaseDestroyed) other++;
                else if (e.Kind == StepEventKind.BulletImpact && e.Target != ImpactTarget.Base) other++;
            }
        }

        Assert.True(fired >= 5, $"the turret fired {fired} times");
        Assert.InRange(shield, fired - 1, fired);
        Assert.Equal(0, other);
        Assert.Equal(Tweaks.Tank.InitialHealth, (int)owner.Reactor.Health);
        AssertUntouched(target);
    }

    [Theory]
    [InlineData(true)]  // the gate shield is spent
    [InlineData(false)] // no energy rule: there is no gate shield
    public void ATankTouchingAnOpenGate_HitsTheCoreThroughIt(bool energyNetwork)
    {
        var world = CreateWorld(energyNetwork);
        var attacker = world.TankList.Tanks[0];
        var target = world.TankBases.Bases[1];
        attacker.Position = target.Position + new Offset(0, -Half - 4);
        if (energyNetwork)
        {
            world.Projectiles.Add(Projectile.CreateBullet(target.Position + new Offset(Half + 1, 0), new VectorF(-1, 0),
                attacker.Color, (int)Tweaks.Keep.ShieldCapacity));
            world.Advance(_ => default);
        }
        Assert.False(world.AreGatesShut(target));

        var tally = Fire(world, attacker, target, seconds: 2);

        Assert.True(tally.Shots > 10, $"only {tally.Shots} shots");
        Assert.Equal(tally.Shots, tally.CoreHits);
        Assert.Equal(0, tally.ShieldHits);
        Assert.True(target.Reactor.Health < Tweaks.Base.InitialHealth);
    }
}
