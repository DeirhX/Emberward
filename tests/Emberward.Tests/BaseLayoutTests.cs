namespace Emberward.Tests;

using Emberward.Core;
using Emberward.Core.Collision;
using Emberward.Core.Config;
using Emberward.Core.Entities.Bases;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Match;
using Emberward.Core.Terrain;
using Emberward.Core.Types;

/// <summary>
/// The shape of a base (DESIGN.md §4.9, <see cref="BaseLayout"/>): walls with a gate in each side, the core at the
/// centre with open floor all round it. What the shape promises is checked here by walking it, so a change to a number
/// in <see cref="Tweaks.Keep"/> that walls the core off, or shuts the owner out, fails with the cell to look at.
/// </summary>
public class BaseLayoutTests
{
    private const int Half = BaseLayout.Half;

    private static World CreateWorld()
    {
        var size = new Size(100, 72);
        var ground = new TerrainGrid(size);
        ground.Fill(TerrainPixel.Blank);
        var world = new World(size, rules: new MatchRules { TerrainRegrowth = false });
        world.Initialize(ground, [new Position(25, 36), new Position(75, 36)]);
        return world;
    }

    private static bool StopsShots(BasePart part) => part is BasePart.Wall or BasePart.Core;

    /// <summary>
    /// The part a shot from <paramref name="x"/>, <paramref name="y"/> (cells from the centre, cell middles at whole
    /// numbers) toward <paramref name="tx"/>, <paramref name="ty"/> lands on first, walking it in fine steps as a
    /// shell does (the gates count as open).
    /// </summary>
    private static BasePart FirstStop(double x, double y, double tx, double ty)
    {
        double dx = tx - x, dy = ty - y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        const double Step = 0.05;
        for (double t = 0; t <= length + 4 * Half; t += Step)
        {
            int cx = (int)Math.Round(x + dx / length * t), cy = (int)Math.Round(y + dy / length * t);
            var part = BaseLayout.PartAt(cx, cy);
            if (StopsShots(part))
                return part;
        }
        return BasePart.Outside;
    }

    [Fact]
    public void TheShape_IsAWalledSquare_WithAGateInEachSide_AndTheCoreAtTheCentre()
    {
        Assert.Equal(BasePart.Core, BaseLayout.PartAt(0, 0));
        // The core is a disc: it reaches its full half along the axes and not into the corners of its square.
        Assert.Equal(BasePart.Core, BaseLayout.PartAt(BaseLayout.CoreHalf, 0));
        Assert.Equal(BasePart.Core, BaseLayout.PartAt(-1, BaseLayout.CoreHalf));
        Assert.Equal(BasePart.Floor, BaseLayout.PartAt(BaseLayout.CoreHalf, -BaseLayout.CoreHalf));
        Assert.Equal(BasePart.Floor, BaseLayout.PartAt(BaseLayout.CoreHalf + 1, 0));
        int coreCells = 0;
        for (int dy = -Half; dy <= Half; dy++)
            for (int dx = -Half; dx <= Half; dx++)
            {
                if (BaseLayout.PartAt(dx, dy) != BasePart.Core)
                    continue;
                coreCells++;
                // Round: the same under a quarter turn and a mirror.
                Assert.Equal(BasePart.Core, BaseLayout.PartAt(-dy, dx));
                Assert.Equal(BasePart.Core, BaseLayout.PartAt(dy, dx));
            }
        Assert.Equal(21, coreCells);
        Assert.All(BaseLayout.CoreAims, aim => Assert.Equal(BasePart.Core, BaseLayout.PartAt(aim.X, aim.Y)));
        Assert.Equal(9, BaseLayout.CoreAims.Length);
        Assert.Equal(BasePart.Outside, BaseLayout.PartAt(Half + 1, 0));
        foreach (var gate in BaseLayout.GateDirections)
        {
            Assert.Equal(BasePart.Gate, BaseLayout.PartAt(gate.X * Half, gate.Y * Half));
            Assert.Equal(BasePart.Gate, BaseLayout.PartAt(gate.X * Half + gate.Y * BaseLayout.GateHalf, gate.Y * Half + gate.X * BaseLayout.GateHalf));
            Assert.Equal(BasePart.Wall, BaseLayout.PartAt(gate.X * Half + gate.Y * (BaseLayout.GateHalf + 1), gate.Y * Half + gate.X * (BaseLayout.GateHalf + 1)));
            // Nothing stands between a gate and the core: floor all the way in from the lane.
            for (int step = BaseLayout.CoreHalf + 1; step < Half; step++)
                Assert.Equal(BasePart.Floor, BaseLayout.PartAt(gate.X * step, gate.Y * step));
            Assert.Equal(BasePart.Floor, BaseLayout.PartAt(gate.X * BaseLayout.Lane, gate.Y * BaseLayout.Lane));
        }
        Assert.Equal(BasePart.Wall, BaseLayout.PartAt(Half, Half));
        Assert.True(Tweaks.Keep.GateSize > TankSpriteShape.Size, "The owner should not have to line up with a gate.");
    }

    /// <summary>
    /// The core has no barrier of its own (DESIGN.md §4.9): a shot through an open gate, or from anywhere in the
    /// courtyard, reaches it. The gate shield is what keeps an enemy out.
    /// </summary>
    [Fact]
    public void AShotThroughAGate_OrFromTheCourtyard_ReachesTheCore()
    {
        foreach (var gate in BaseLayout.GateDirections)
        {
            Assert.Equal(BasePart.Core, FirstStop(gate.X * (Half + 8), gate.Y * (Half + 8), 0, 0));
            var inside = BaseLayout.InsideGate(gate);
            Assert.Equal(BasePart.Core, FirstStop(inside.X, inside.Y, 0, 0));
        }
        foreach (int sx in new[] { -1, 1 })
            foreach (int sy in new[] { -1, 1 })
            {
                var corner = BaseLayout.NearestCorner(sx, sy);
                Assert.Equal(BasePart.Floor, BaseLayout.PartAt(corner.X, corner.Y));
                Assert.Equal(BasePart.Core, FirstStop(corner.X, corner.Y, 0, 0));
            }
    }

    /// <summary>
    /// The owner's tank drives in from every gate straight up to the core and touches it (where it unloads,
    /// <see cref="Tank.TouchesCore"/>); from the courtyard lane it does not.
    /// </summary>
    [Fact]
    public void TheOwnersTank_DrivesFromEveryGateUpToTheCore_AndTouchesIt()
    {
        var world = CreateWorld();
        var tank = world.TankList.Tanks[0];
        var home = tank.Base!;
        foreach (var gate in BaseLayout.GateDirections)
        {
            var heading = new Offset(-gate.X, -gate.Y);
            int dir = TankSpriteShape.DirectionOf(heading);
            int step = Half + 4;
            // Drive in until the hull meets the core.
            while (CollisionSolver.TestTankTerrain(world.Terrain,
                       home.Position + new Offset(gate.X * (step - 1), gate.Y * (step - 1)), dir) == StepObstacle.None)
                step--;
            Assert.True(step <= BaseLayout.CoreHalf + TankSpriteShape.Radius + 1, $"gate {gate}: stopped {step} cells out");
            tank.Direction = dir;
            tank.Position = home.Position + new Offset(gate.X * step, gate.Y * step);
            Assert.True(tank.TouchesCore(home), $"gate {gate}: against the core at {step} but not touching it");
            tank.Position = home.Position + new Offset(gate.X * BaseLayout.Lane, gate.Y * BaseLayout.Lane);
            Assert.False(tank.TouchesCore(home), $"gate {gate}: touching the core from the lane");
        }
    }

    [Fact]
    public void TheTerrain_HoldsTheShape_AndNothingButTheCoreAndWallsBlocks()
    {
        var world = CreateWorld();
        var home = world.TankBases.Bases[0];
        for (int dy = -Half - 1; dy <= Half + 1; dy++)
            for (int dx = -Half - 1; dx <= Half + 1; dx++)
            {
                var at = home.Position + new Offset(dx, dy);
                var pixel = world.Terrain.GetPixelRaw(at);
                var part = BaseLayout.PartAt(dx, dy);
                Assert.Equal(part, home.PartAt(at));
                Assert.Equal(part != BasePart.Outside, Pixel.IsAnyBaseCell(pixel));
                Assert.Equal(part == BasePart.Core, Pixel.IsBaseCore(pixel));
                Assert.Equal(part == BasePart.Gate, Pixel.IsBaseGate(pixel));
                Assert.Equal(part is BasePart.Wall or BasePart.Core, Pixel.IsBlockingCollision(pixel));
            }
    }

    /// <summary>
    /// The base stays convenient to its owner: the tank spawns in the courtyard, and from there it drives straight out
    /// through the front gate and all the way round the keep to every other gate, without touching a wall.
    /// </summary>
    [Fact]
    public void TheOwnersTank_SpawnsInTheCourtyard_AndDrivesRoundTheKeepToEveryGate()
    {
        var world = CreateWorld();
        var tank = world.TankList.Tanks[0];
        var home = tank.Base!;

        Assert.Equal(home.SpawnPosition, tank.Position);
        Assert.Equal(home.Position + new Offset(home.Front.X * BaseLayout.Lane, home.Front.Y * BaseLayout.Lane), tank.Position);
        Assert.True(tank.IsDockedAtOwnBase);

        bool Fits(Offset at, Offset heading) => CollisionSolver.TestTankTerrain(
            world.Terrain, home.Position + at, TankSpriteShape.DirectionOf(heading)) == StepObstacle.None;

        int lane = BaseLayout.Lane;
        // Round the lane: along each side of the courtyard, corner to corner.
        for (int i = -lane; i <= lane; i++)
        {
            Assert.True(Fits(new Offset(i, -lane), new Offset(1, 0)), $"north lane at {i}");
            Assert.True(Fits(new Offset(i, lane), new Offset(1, 0)), $"south lane at {i}");
            Assert.True(Fits(new Offset(-lane, i), new Offset(0, 1)), $"west lane at {i}");
            Assert.True(Fits(new Offset(lane, i), new Offset(0, 1)), $"east lane at {i}");
        }
        // Out through each gate, with room to either side of the hull.
        int slack = BaseLayout.GateHalf - TankSpriteShape.Size / 2;
        foreach (var gate in BaseLayout.GateDirections)
            for (int step = lane; step <= Half + 4; step++)
                for (int off = -slack; off <= slack; off++)
                    Assert.True(Fits(new Offset(gate.X * step + gate.Y * off, gate.Y * step + gate.X * off), gate),
                        $"gate {gate} at {step}, {off} off its middle");
    }
}
