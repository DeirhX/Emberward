using Emberward.Core.Config;
using Emberward.Core.Entities;
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
        fire.Emit(quads, motion, vision);
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
        Assert.Equal(FireParticles.MaxPerTank, particles);
        // The cap: every family's slots, at six vertices each.
        Assert.InRange(particles, 1, 80);
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
        Assert.Equal(0f, FireOnset.EmberShare(0.2f));
        Assert.True(FireOnset.EmberShare(0.5f) > 0f);
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

    // How many particles of each FireParticleKind a frame draws (the kind is params.y).
    private static int[] CountParticles(EntityQuads quads)
    {
        int[] counts = new int[4];
        for (int v = 0; v < quads.VertexCount; v += 6)
        {
            if (quads.Vertices[v].Kind == EntityShape.KindFireParticle)
                counts[(int)quads.Vertices[v].P1]++;
        }
        return counts;
    }

    [Fact]
    public void AFullFire_IsMadeOfFlameLicksSparksCindersAndSmoke()
    {
        var (fire, motion) = Burning(1f);
        int[] counts = CountParticles(Draw(fire, motion));

        foreach (FireParticleFamily family in FireParticles.Families)
            Assert.Equal(family.Count, counts[(int)family.Kind]);
        // The flame licks are the body of the fire: the most numerous.
        Assert.True(counts[(int)FireParticleKind.Flame] > counts[(int)FireParticleKind.Spark]);
        Assert.True(counts[(int)FireParticleKind.Cinder] < counts[(int)FireParticleKind.Spark]);
    }

    [Fact]
    public void AFireJustCatching_HasOnlyAFewFlameLicks()
    {
        var (fire, motion) = Burning(0.15f);
        int[] counts = CountParticles(Draw(fire, motion));

        Assert.InRange(counts[(int)FireParticleKind.Flame], 1, 10);
        Assert.Equal(0, counts[(int)FireParticleKind.Spark]);
        Assert.Equal(0, counts[(int)FireParticleKind.Cinder]);
    }

    [Fact]
    public void TheCore_IsDrawnUpright_WithTheHullHeading()
    {
        var (fire, motion) = Burning(1f);
        var quads = Draw(fire, motion);
        int v = 0;
        while (quads.Vertices[v].Kind != EntityShape.KindTankFire)
            v += 6;

        // Upright: the first corner (local min x, min y) is up and to the left of the anchor, axis-aligned.
        EntityVertex corner = quads.Vertices[v];
        Assert.Equal(EntityQuads.Centre(100) - 6.5f, corner.CellX, 3);
        Assert.Equal(EntityQuads.Centre(100) - 6.5f, corner.CellY, 3);
        Assert.Equal(motion.HullHeading(0), corner.P3, 4);
    }

    [Fact]
    public void FlameLicksAndSparks_Rise_AndCindersArcUpThenFall()
    {
        var h = new FireParticles.Hashes(0.5f, 0.3f, 0.7f, 0.4f);
        foreach (FireParticleFamily family in FireParticles.Families)
        {
            float life = family.LifeSeconds;
            PositionF born = FireParticles.Offset(family.Kind, 0f, 1f, 2f, h);
            PositionF mid = FireParticles.Offset(family.Kind, 0.4f * life, 1f, 2f, h);
            PositionF end = FireParticles.Offset(family.Kind, life, 1f, 2f, h);
            Assert.True(mid.Y < born.Y - 1f, $"{family.Kind} does not rise: {born.Y} to {mid.Y}");
            if (family.Kind == FireParticleKind.Cinder)
                Assert.True(end.Y > born.Y, $"a cinder ends at {end.Y}, above where it started ({born.Y})");
            else
                Assert.True(end.Y < mid.Y, $"{family.Kind} falls back: {mid.Y} to {end.Y}");
            // Born on the hull or close round it.
            Assert.InRange(MathF.Abs(born.X), 0f, 2.5f);
        }

        // A lick is turned along its motion: up the screen.
        var (direction, speed) = FireParticles.Motion(FireParticleKind.Flame, 0.2f, 1f, 2f, h);
        Assert.True(direction.Y < -0.8f && speed > 1f);
    }

    [Fact]
    public void TheParticles_GrowWithTheFire()
    {
        Assert.True(FireParticles.Size(FireParticleKind.Flame, 0.3f, 1f, 0.5f) > 1.5f * FireParticles.Size(FireParticleKind.Flame, 0.3f, 0.2f, 0.5f));
        Assert.True(FireParticles.Size(FireParticleKind.Spark, 0.3f, 1f, 0.5f) > FireParticles.Size(FireParticleKind.Spark, 0.3f, 0.2f, 0.5f));
    }

    [Fact]
    public void AMovingTank_LeavesItsFireBehindIt()
    {
        var fire = new TankFireQuads();
        var motion = new EntityMotion();
        const int Steps = 40;
        for (int x = 0; x < Steps; x++)
        {
            motion.CaptureTanks([Tank(100 + x, 100)]);
            fire.BeginCapture(1);
            fire.CaptureTank(0, new Position(100 + x, 100), 1f);
        }
        motion.UpdateHullHeading(0, East, 0f, snap: true);
        var quads = Draw(fire, motion);

        float sum = 0f;
        int count = 0;
        for (int v = 0; v < quads.VertexCount; v += 6)
        {
            if (quads.Vertices[v].Kind == EntityShape.KindFireParticle && quads.Vertices[v].P1 == (float)FireParticleKind.Flame)
            {
                sum += (quads.Vertices[v].CellX + quads.Vertices[v + 2].CellX) / 2f;
                count++;
            }
        }
        Assert.True(count > 0);
        // The tank is at x = 139 now; its flames stream out behind it (west), where it was as they were born.
        Assert.True(sum / count < EntityQuads.Centre(100 + Steps - 1) - 2f, $"flames centred at {sum / count}");
    }

    [Fact]
    public void TheParticles_AreTheSame_OnEveryRun()
    {
        EntityVertex[] Run()
        {
            var (fire, motion) = Burning(0.8f);
            var quads = Draw(fire, motion);
            return quads.Vertices.AsSpan(0, quads.VertexCount).ToArray();
        }

        Assert.Equal(Run(), Run());
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
                fire.CaptureTank(0, new Position(100, 100), burningSlot == 0 ? 1f : 0f);
                fire.CaptureTank(1, new Position(200, 150), burningSlot == 1 ? 1f : 0f);
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
    private static PositionF[] ParticleCorners(EntityQuads quads)
    {
        var corners = new List<PositionF>();
        for (int v = 0; v < quads.VertexCount; v++)
        {
            if (quads.Vertices[v].Kind == EntityShape.KindFireParticle)
                corners.Add(new PositionF(quads.Vertices[v].CellX, quads.Vertices[v].CellY));
        }
        return [.. corners];
    }

    [Fact]
    public void Particles_HoldStill_WhileTheWorldTakesNoStep()
    {
        var (fire, motion) = Burning(1f);
        PositionF[] before = ParticleCorners(Draw(fire, motion, phase: 1.3f));

        // The visual clock runs on (a pause, a network stall), the world does not.
        Assert.Equal(before, ParticleCorners(Draw(fire, motion, phase: 7.9f)));
        Assert.Equal(before, ParticleCorners(Draw(fire, motion, phase: 999.9f)));

        fire.BeginCapture(1);
        fire.CaptureTank(0, new Position(100, 100), 1f);
        Assert.NotEqual(before, ParticleCorners(Draw(fire, motion, phase: 1.3f)));
    }

    [Fact]
    public void TheTrail_KeepsTheLatestSteps_AsItWrapsAround()
    {
        var fire = new TankFireQuads();
        const int Steps = TankFireQuads.TrailSteps + 54;
        for (int x = 0; x < Steps; x++)
        {
            fire.BeginCapture(1);
            fire.CaptureTank(0, new Position(100 + x, 100), 1f);
        }

        for (int k = 0; k < TankFireQuads.TrailSteps; k++)
        {
            Assert.Equal(new Position(100 + Steps - 1 - k, 100), fire.TrailPosition(0, k));
        }
        // Further back than the trail goes is the oldest it keeps.
        Assert.Equal(fire.TrailPosition(0, TankFireQuads.TrailSteps - 1), fire.TrailPosition(0, 500));
    }

    [Fact]
    public void ANewMatch_StartsTheTrailsAgain()
    {
        var first = TestHelpers.CreateSeededWorld();
        var fire = new TankFireQuads();
        var tank = first.TankList.Tanks[0];
        Position start = tank.Position;
        for (int x = 0; x < 10; x++)
        {
            tank.Position = start + new Offset(x, 0);
            fire.CaptureStep(first);
        }
        Assert.NotEqual(fire.TrailPosition(0, 0), fire.TrailPosition(0, 5));

        var second = TestHelpers.CreateSeededWorld();
        Position there = start + new Offset(4, 3);
        second.TankList.Tanks[0].Position = there;
        fire.CaptureStep(second);

        for (int k = 0; k < TankFireQuads.TrailSteps; k++)
            Assert.Equal(there, fire.TrailPosition(0, k));
    }

    [Fact]
    public void ABurningTank_ShimmersTheAirAboveIt()
    {
        var world = TestHelpers.CreateSeededWorld(enableTerrainRegrowth: false, tankShield: true);
        var tank = world.TankList.Tanks[0];
        tank.TakeDamage(Tweaks.TankShield.Capacity + tank.Reactor.Health - 1, DamageCause.EnemyFire);
        Assert.True(tank.FireLevel > 0.9f);
        var view = new RenderView(new Size(1600, 1200), world.Terrain.Size, new Position(
            (int)((tank.Position.X * 4) - 800), (int)((tank.Position.Y * 4) - 600)), 4);
        var target = new float[Tweaks.World.MaxPlayers * 4];

        int count = new TerrainAuxBuilder().BuildGpuTankHeatGlowData(
            world.TankList.Tanks, [new PositionF(tank.Position.X, tank.Position.Y)], view, target);

        Assert.Equal(1, count);
        Assert.True(target[3] >= 0.55f + (0.4f * tank.FireLevel) - 1e-4f, $"haze {target[3]}");
    }

    private static EntityQuads DrawAt(TankFireQuads fire, EntityMotion motion, float alpha)
    {
        var quads = new EntityQuads();
        quads.Begin(new RenderView(new Size(1600, 1200), new Size(400, 300), new Position(0, 0), 4), 1.3f, alpha);
        fire.Emit(quads, motion, null);
        return quads;
    }

    [Fact]
    public void Particles_DoNotRunBackOrOn_WhenAlphaWrapsWithoutAStep()
    {
        var (fire, motion) = Burning(1f);
        PositionF[] late = ParticleCorners(DrawAt(fire, motion, 0.9f));

        // A stalled network match: the frames' alpha wraps to 0 and climbs again, but no step arrives.
        Assert.Equal(late, ParticleCorners(DrawAt(fire, motion, 0f)));
        Assert.Equal(late, ParticleCorners(DrawAt(fire, motion, 0.5f)));
        PositionF[] capped = ParticleCorners(DrawAt(fire, motion, 1f));
        Assert.Equal(capped, ParticleCorners(DrawAt(fire, motion, 0.3f)));
        Assert.Equal(fire.ParticleClock(0.3f), fire.ParticleClock(1.7f));

        // Held (the world takes no step): nothing moves at any alpha until the next capture.
        fire.Hold();
        Assert.Equal(capped, ParticleCorners(DrawAt(fire, motion, 0.2f)));
        fire.BeginCapture(1);
        fire.CaptureTank(0, new Position(100, 100), 1f);
        // The next step carries on from where the capped clock stood, then runs on.
        Assert.Equal(capped, ParticleCorners(DrawAt(fire, motion, 0f)));
        double atStep = fire.ParticleClock(0f);
        Assert.True(fire.ParticleClock(0.5f) > atStep);
    }
}
