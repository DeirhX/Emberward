using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities;
using Emberward.Core.Entities.Projectiles;
using Emberward.Core.Rendering;
using Emberward.Core.Terrain;
using Emberward.Core.Types;
using Emberward.Desktop.Rendering;
using Emberward.Desktop.Rendering.Entities;

namespace Emberward.Tests;

/// <summary>What a shell that the tank's shield stops looks and sounds like (events, pulse, screen shake).</summary>
public class TankShieldFeedbackTests
{
    private static readonly Position Centre = new(60, 100);

    /// <summary>A world where player 0's shell is one tick from tank 1 in open ground.</summary>
    private static (World World, Emberward.Core.Entities.Tanks.Tank Victim) Shot(int damage)
    {
        var world = TestHelpers.CreateSeededWorld(enableTerrainRegrowth: false, tankShield: true);
        for (int y = -30; y <= 30; y++)
            for (int x = -60; x <= 60; x++)
                world.Terrain.SetPixel(Centre + new Offset(x, y), TerrainPixel.Blank);
        var victim = world.TankList.Tanks[1];
        victim.Position = Centre;
        world.Projectiles.Add(Projectile.CreateBullet(Centre + new Offset(-4, 0), new VectorF(1, 0), 0, damage));
        world.Advance(_ => default);
        return (world, victim);
    }

    [Fact]
    public void AShellTheShieldStops_RecordsAShieldHit_AndNoHullHit()
    {
        var (world, victim) = Shot(Tweaks.Weapon.BulletDamage);

        var events = world.StepEvents.ToArray();
        var hit = Assert.Single(events, e => e.Kind == StepEventKind.TankShieldHit);
        Assert.Equal(victim.Color, hit.Color);
        Assert.Equal(Tweaks.Weapon.BulletDamage, hit.Count);
        Assert.DoesNotContain(events, e => e.Kind is StepEventKind.TankHit or StepEventKind.TankShieldBroken);
    }

    [Fact]
    public void AShellThatBreaksTheShield_RecordsTheBreak_AndTheHullHit()
    {
        var (world, _) = Shot(Tweaks.TankShield.Capacity + 50);

        var kinds = world.StepEvents.ToArray().Select(e => e.Kind).ToArray();
        Assert.DoesNotContain(StepEventKind.TankShieldHit, kinds);
        Assert.Contains(StepEventKind.TankShieldBroken, kinds);
        Assert.Contains(StepEventKind.TankHit, kinds);
    }

    [Fact]
    public void AShieldHit_StartsAPulse_ThatEndsAfterAFewSteps()
    {
        var (world, _) = Shot(Tweaks.Weapon.BulletDamage);
        var quads = new TankShieldQuads();

        quads.CaptureStep(world);
        Assert.Equal(1, quads.PulseCount);

        for (int i = 0; i < 12; i++)
        {
            world.Advance(_ => default);
            quads.CaptureStep(world);
        }
        Assert.Equal(0, quads.PulseCount);
    }

    /// <summary>The tank shield quads drawn for <paramref name="victim"/> after <paramref name="world"/>'s last step.</summary>
    private static EntityVertex[] EmitPulse(World world, Emberward.Core.Entities.Tanks.Tank victim, float phase = 0f)
    {
        var shield = new TankShieldQuads();
        shield.CaptureStep(world);
        return Draw(shield, world, victim, phase);
    }

    private static EntityVertex[] Draw(TankShieldQuads shield, World world, Emberward.Core.Entities.Tanks.Tank victim,
        float phase)
    {
        var motion = new EntityMotion();
        motion.CaptureTanks([new TankRenderState(victim.Position, victim.Color, Direction: 4, IsDead: false,
            TurretDirection: default)]);
        motion.SetAlpha(0f);
        var quads = new EntityQuads();
        quads.Begin(new RenderView(new Size(1000, 1000), world.Terrain.Size, default, 2), phase, alpha: 0f);
        shield.Emit(quads, motion, vision: null);
        return quads.Vertices.AsSpan(0, quads.VertexCount).ToArray();
    }

    [Fact]
    public void AShieldedTankAtRest_ShowsTheBubble_WithItsFillAndNoPulse()
    {
        var world = TestHelpers.CreateSeededWorld(enableTerrainRegrowth: false, tankShield: true);
        world.Advance(_ => default);
        var tank = world.TankList.Tanks[1];
        var shield = new TankShieldQuads();
        shield.CaptureStep(world);
        float fill = world.TankShieldOf(tank).ShieldFill;

        var vertices = Draw(shield, world, tank, phase: 10f);

        Assert.True(fill > 0f);
        Assert.Equal(world.TankList.Tanks.Count, shield.ShieldedCount);
        Assert.Equal(6, vertices.Length);
        Assert.All(vertices, v => Assert.Equal(EntityShape.KindTankShield, v.Kind));
        Assert.All(vertices, v => Assert.Equal(fill, v.P0));
        Assert.All(vertices, v => Assert.True(v.P1 >= 1f, $"age {v.P1}"));
        Assert.All(vertices, v => Assert.Equal(TankShieldQuads.NoHitAngle, v.P2));
        Assert.All(vertices, v => Assert.Equal(10f / TankShieldQuads.LoopSeconds, v.P3, precision: 5));
    }

    [Fact]
    public void WithoutTheShieldRule_NoBubbleIsDrawn()
    {
        var world = TestHelpers.CreateSeededWorld(enableTerrainRegrowth: false, tankShield: false);
        world.Advance(_ => default);
        var shield = new TankShieldQuads();
        shield.CaptureStep(world);

        Assert.Equal(0, shield.ShieldedCount);
        Assert.Empty(Draw(shield, world, world.TankList.Tanks[1], phase: 0f));
    }

    [Fact]
    public void TheAnimationLoop_DividesThePhaseWrap_SoItIsSeamless()
    {
        // The entity layer wraps its phase at 1000 s; a loop that divides it runs on unbroken through the wrap.
        Assert.Equal(0f, 1000f % TankShieldQuads.LoopSeconds);
        var world = TestHelpers.CreateSeededWorld(enableTerrainRegrowth: false, tankShield: true);
        world.Advance(_ => default);
        var tank = world.TankList.Tanks[1];
        var shield = new TankShieldQuads();
        shield.CaptureStep(world);

        float justBefore = Draw(shield, world, tank, phase: 999.99f)[0].P3;
        float justAfter = Draw(shield, world, tank, phase: 0.01f)[0].P3;

        Assert.InRange(justBefore, 0.999f, 1f);
        Assert.InRange(justAfter, 0f, 0.001f);
    }

    [Fact]
    public void AShieldHit_DrawsThePulse_WithTheFillLeftAfterTheHit_AndWhereTheShellCameIn()
    {
        var (world, victim) = Shot(Tweaks.Weapon.BulletDamage);
        float fill = world.TankShieldOf(victim).ShieldFill;

        var vertices = EmitPulse(world, victim);

        Assert.NotEmpty(vertices);
        Assert.All(vertices, v => Assert.Equal(EntityShape.KindTankShield, v.Kind));
        // The shader keeps only this much of the bubble: a partly drained shield shows a partly eroded one.
        Assert.InRange(fill, 0.01f, 0.99f);
        Assert.All(vertices, v => Assert.Equal(fill, v.P0));
        // A fresh pulse, and the shell came in from the west: the red patch goes on that side.
        Assert.All(vertices, v => Assert.InRange(v.P1, 0f, 0.5f));
        Assert.All(vertices, v => Assert.True(MathF.Abs(v.P2) > 2.5f && MathF.Abs(v.P2) <= MathF.PI, $"hit angle {v.P2}"));
    }

    [Fact]
    public void AHitThatLeavesTheLastSliver_StillSendsAPositiveFill()
    {
        var (world, victim) = Shot(Tweaks.TankShield.Capacity - 1);
        float fill = world.TankShieldOf(victim).ShieldFill;

        var vertices = EmitPulse(world, victim);

        // Not the break: the shader draws a small core for any fill above 0.
        Assert.InRange(fill, float.Epsilon, 0.05f);
        Assert.NotEmpty(vertices);
        Assert.All(vertices, v => Assert.Equal(fill, v.P0));
    }

    [Fact]
    public void AShieldThatBreaks_DrawsTheCollapse_AtWhereTheShellCameIn()
    {
        var (world, victim) = Shot(Tweaks.TankShield.Capacity + 50);
        Assert.Equal(0f, world.TankShieldOf(victim).ShieldFill);

        var vertices = EmitPulse(world, victim);

        // The break marker, not the empty shield's 0 (which would draw nothing), and the hit for the red patch.
        Assert.NotEmpty(vertices);
        Assert.All(vertices, v => Assert.Equal(EntityShape.KindTankShield, v.Kind));
        Assert.All(vertices, v => Assert.Equal(TankShieldQuads.BrokenFill, v.P0));
        Assert.All(vertices, v => Assert.InRange(v.P1, 0f, 0.5f));
        Assert.All(vertices, v => Assert.True(MathF.Abs(v.P2) > 2.5f && MathF.Abs(v.P2) <= MathF.PI, $"hit angle {v.P2}"));
    }

    [Fact]
    public void AShieldHitOnThePlayer_ShakesTheScreen()
    {
        var (_, victim) = Shot(Tweaks.Weapon.BulletDamage);
        var effects = new ScreenEffects();
        effects.Update(victim, 0.016f);

        effects.AfterStep([new StepEvent(StepEventKind.TankShieldHit, victim.Position, victim.Color, Count: 10,
            Cause: DamageCause.EnemyFire)], victim);
        var levels = effects.Update(victim, 0.016f);

        Assert.True(levels.Hit > 0f);
    }

    [Fact]
    public void ABiteOnTheShield_DoesNotShakeTheScreen()
    {
        var (_, victim) = Shot(Tweaks.Weapon.BulletDamage);
        var effects = new ScreenEffects();
        effects.Update(victim, 0.016f);

        effects.AfterStep([new StepEvent(StepEventKind.TankShieldHit, victim.Position, victim.Color, Count: 10,
            Cause: DamageCause.Emberite)], victim);
        var levels = effects.Update(victim, 0.016f);

        Assert.Equal(0f, levels.Hit);
    }
}
