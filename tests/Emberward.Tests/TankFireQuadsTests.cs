using Emberward.Core.Rendering;
using Emberward.Core.Sensing;
using Emberward.Core.Types;
using Emberward.Desktop.Rendering;
using Emberward.Desktop.Rendering.Entities;

namespace Emberward.Tests;

/// <summary>What the entity layer draws for a burning tank: ground light, flames, smoke and embers.</summary>
public class TankFireQuadsTests
{
    private const int East = 3;

    private static TankRenderState Tank(int x, int y, int color = 0) =>
        new(new Position(x, y), Color: color, Direction: East, IsDead: false, TurretDirection: default);

    private static EntityQuads Frame(float phase = 1.3f)
    {
        var quads = new EntityQuads();
        quads.Begin(new RenderView(new Size(1600, 1200), new Size(400, 300), new Position(0, 0), 4), phase, 0f);
        return quads;
    }

    /// <summary>One tank at (100, 100) burning at <paramref name="level"/>, captured for a few steps standing still.</summary>
    private static (TankFireQuads Fire, EntityMotion Motion) Burning(float level)
    {
        var fire = new TankFireQuads();
        var motion = new EntityMotion();
        for (int step = 0; step < 4; step++)
        {
            motion.CaptureTanks([Tank(100, 100)]);
            fire.BeginCapture(1);
            fire.CaptureTank(0, new Position(100, 100), level);
        }
        motion.UpdateHullHeading(0, East, 0f, snap: true);
        return (fire, motion);
    }

    private static int CountKind(EntityQuads quads, float kind)
    {
        int count = 0;
        for (int v = 0; v < quads.VertexCount; v += 6)
        {
            if (quads.Vertices[v].Kind == kind)
                count++;
        }
        return count;
    }

    private static EntityQuads Draw(TankFireQuads fire, EntityMotion motion, VisionMap? vision = null, float phase = 1.3f)
    {
        var quads = Frame(phase);
        fire.EmitLight(quads, motion, vision);
        fire.Emit(quads, motion, vision, 1f / 60f, snap: false);
        return quads;
    }

    [Fact]
    public void ATankThatIsNotBurning_DrawsNothing()
    {
        var (fire, motion) = Burning(0f);

        Assert.Equal(0, Draw(fire, motion).VertexCount);
    }

    [Fact]
    public void ABurningTank_DrawsLightFlamesAndParticles_NoMoreThanTheCap()
    {
        var (fire, motion) = Burning(1f);
        var quads = Draw(fire, motion);

        Assert.Equal(1, CountKind(quads, EntityShape.KindTankFireLight));
        Assert.Equal(1, CountKind(quads, EntityShape.KindTankFire));
        int particles = CountKind(quads, EntityShape.KindFireParticle);
        Assert.Equal(TankFireQuads.SmokeCount(1f) + TankFireQuads.EmberCount(1f), particles);
        Assert.InRange(particles, 1, 36);
    }

    [Fact]
    public void TheParticleCount_NeverFallsAsTheFireGrows()
    {
        int previous = 0;
        for (int i = 1; i <= 20; i++)
        {
            float level = i / 20f;
            var (fire, motion) = Burning(level);
            int particles = CountKind(Draw(fire, motion), EntityShape.KindFireParticle);
            Assert.True(particles >= previous, $"level {level}: {particles} particles after {previous}");
            previous = particles;
        }
        Assert.Equal(0, TankFireQuads.EmberCount(0.2f));
        Assert.True(TankFireQuads.EmberCount(0.5f) > 0);
    }

    [Fact]
    public void ARespawnJump_StartsTheTrailAgain()
    {
        var fire = new TankFireQuads();
        for (int x = 0; x < 50; x++)
        {
            fire.BeginCapture(1);
            fire.CaptureTank(0, new Position(100 + x, 100), 1f);
        }
        Assert.Equal(new Position(149 - 10, 100), fire.TrailPosition(0, 10));

        fire.BeginCapture(1);
        fire.CaptureTank(0, new Position(300, 200), 1f);

        for (int k = 0; k < TankFireQuads.TrailSteps; k++)
            Assert.Equal(new Position(300, 200), fire.TrailPosition(0, k));
    }

    [Fact]
    public void ATankOutOfSight_DrawsNoFireLightOrSmoke()
    {
        var world = TestHelpers.CreateSeededWorld();
        var vision = new VisionMap(world.Terrain);
        var (fire, motion) = Burning(1f);

        Assert.Equal(0, Draw(fire, motion, vision).VertexCount);
    }

    [Fact]
    public void TheFlames_LeanIntoTheDrift_WithinHalfASecond()
    {
        var fire = new TankFireQuads();
        var motion = new EntityMotion();
        for (int x = 0; x < 6; x++)
        {
            motion.CaptureTanks([Tank(100 + x, 100)]);
            motion.SetAlpha(1f);
            fire.BeginCapture(1);
            fire.CaptureTank(0, new Position(100 + x, 100), 1f);
        }
        motion.UpdateHullHeading(0, East, 0f, snap: true);
        var (_, drift) = motion.TankMotion(0);
        Assert.True(drift.X > 0.9f);

        PositionF straight = fire.FlameDirection(0);
        Assert.Equal(-1f, straight.Y, 3);

        for (int frame = 0; frame < 30; frame++)
            fire.Emit(Frame(), motion, null, 1f / 60f, snap: false);

        float tx = -0.5f * MathF.Min(drift.X, 1.2f), ty = -1f;
        float length = MathF.Sqrt((tx * tx) + (ty * ty));
        PositionF leaned = fire.FlameDirection(0);
        float dot = ((leaned.X * tx) + (leaned.Y * ty)) / length;
        Assert.True(dot > 0.995f, $"flames point ({leaned.X}, {leaned.Y})");
        Assert.True(leaned.X < -0.3f);
    }

    [Fact]
    public void AnEnemyTank_BurnsLikeThePlayers()
    {
        int[] Kinds(int burningSlot)
        {
            var fire = new TankFireQuads();
            var motion = new EntityMotion();
            for (int step = 0; step < 4; step++)
            {
                motion.CaptureTanks([Tank(100, 100, color: 0), Tank(200, 150, color: 1)]);
                fire.BeginCapture(2);
                fire.CaptureTank(0, new Position(100, 100), burningSlot == 0 ? 0.8f : 0f);
                fire.CaptureTank(1, new Position(200, 150), burningSlot == 1 ? 0.8f : 0f);
            }
            motion.UpdateHullHeading(0, East, 0f, snap: true);
            motion.UpdateHullHeading(1, East, 0f, snap: true);
            var quads = Draw(fire, motion);
            return [CountKind(quads, EntityShape.KindTankFireLight), CountKind(quads, EntityShape.KindTankFire),
                CountKind(quads, EntityShape.KindFireParticle)];
        }

        int[] player = Kinds(0);
        Assert.Equal(player, Kinds(1));
        Assert.True(player[2] > 0);
    }
}
