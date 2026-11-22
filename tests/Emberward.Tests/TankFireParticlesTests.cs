using System.Globalization;
using System.Text.RegularExpressions;
using Emberward.Core.Rendering;
using Emberward.Core.Types;
using Emberward.Desktop.Rendering;
using Emberward.Desktop.Rendering.Entities;

namespace Emberward.Tests;

/// <summary>A burning tank's particles: their kinds as the shader knows them, where they are born, and after a death.</summary>
public class TankFireParticlesTests
{
    private const int East = 3;

    private static TankRenderState Tank(int x, int y, bool dead = false) =>
        new(new Position(x, y), Color: 0, Direction: East, IsDead: dead, TurretDirection: default);

    private static EntityQuads DrawAt(TankFireQuads fire, EntityMotion motion, float alpha)
    {
        var quads = new EntityQuads();
        quads.Begin(new RenderView(new Size(1600, 1200), new Size(400, 300), new Position(0, 0), 4), 1.3f, alpha);
        fire.Emit(quads, motion, null);
        return quads;
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
    public void TheParticleKinds_MatchTheShader()
    {
        string shader = File.ReadAllText(Path.Combine(EntityLayoutGuardTests.FindRepositoryRoot(), "src", "Emberward.Desktop",
            "Rendering", "Shaders", "Entity", "TankFire.hlsli"));
        FireParticleKind[] kinds = Enum.GetValues<FireParticleKind>();
        foreach (FireParticleKind kind in kinds)
        {
            Match match = Regex.Match(shader, $@"static const float kFire{kind}Kind = ([0-9.]+);");
            Assert.True(match.Success, $"TankFire.hlsli declares no kFire{kind}Kind for FireParticleKind.{kind}");
            Assert.Equal((float)kind, float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        }
        Assert.Equal(kinds.Length, Regex.Count(shader, @"static const float kFire\w+Kind ="));
    }

    [Fact]
    public void AFreshParticle_IsBornUnderTheDrawnHull_NotAheadOfAFastTank()
    {
        var fire = new TankFireQuads();
        var motion = new EntityMotion();
        const int Steps = 12, Speed = 3;
        for (int s = 0; s < Steps; s++)
        {
            motion.CaptureTanks([Tank(100 + (Speed * s), 100)]);
            motion.SetAlpha(1f);
            fire.BeginCapture(1);
            fire.CaptureTank(0, new Position(100 + (Speed * s), 100), 1f);
        }
        motion.SetAlpha(0f);
        motion.UpdateHullHeading(0, East, 0f, snap: true);
        var (start, drift) = motion.TankMotion(0);
        Assert.True(drift.X > 1f);
        double now = fire.ParticleClock(0f);

        // Born during the step being drawn: where the drawn tank was at that moment.
        PositionF born = fire.BirthPosition(0, now + 0.5, start, drift);
        Assert.Equal(start.X + (0.5f * drift.X), born.X, 3);
        // Born a moment before it: close behind the drawn tank, on its trail.
        PositionF earlier = fire.BirthPosition(0, now - 0.25, start, drift);
        Assert.InRange(earlier.X, start.X - (2f * Speed), start.X + 1f);

        // No flame lick is drawn ahead of the drawn hull's front (about 2.5 cells ahead of its centre).
        var quads = DrawAt(fire, motion, 0f);
        int licks = 0;
        for (int v = 0; v < quads.VertexCount; v += 6)
        {
            if (quads.Vertices[v].Kind != EntityShape.KindFireParticle || quads.Vertices[v].P1 != (float)FireParticleKind.Flame)
                continue;
            licks++;
            float x = (quads.Vertices[v].CellX + quads.Vertices[v + 2].CellX) / 2f;
            Assert.True(x < EntityQuads.Centre(start.X) + 3.5f, $"a lick at {x}, the drawn tank at {EntityQuads.Centre(start.X)}");
        }
        Assert.True(licks > 0);
    }

    [Fact]
    public void ADeadTanksParticles_KeepTheCoreItDiedWith()
    {
        static PositionF[] AfterDeath(float hullWhileDead)
        {
            var fire = new TankFireQuads();
            var motion = new EntityMotion();
            for (int s = 0; s < 6; s++)
            {
                motion.CaptureTanks([Tank(100, 100)]);
                fire.BeginCapture(1);
                fire.CaptureTank(0, new Position(100, 100), 0.9f, hull: 0.4f);
            }
            for (int s = 0; s < 4; s++)
            {
                motion.CaptureTanks([Tank(100, 100, dead: true)]);
                fire.BeginCapture(1);
                fire.CaptureTank(0, new Position(100, 100), 0f, hull: hullWhileDead, dead: true);
            }
            motion.UpdateHullHeading(0, East, 0f, snap: true);
            return ParticleCorners(DrawAt(fire, motion, 0f));
        }

        PositionF[] emptied = AfterDeath(0f);
        Assert.NotEmpty(emptied);
        // The hull reported after the death does not move the licks still rising from where the core was.
        Assert.Equal(emptied, AfterDeath(0.7f));
    }
}
