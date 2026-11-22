using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities;
using Emberward.Core.Entities.Projectiles;
using Emberward.Core.Rendering;
using Emberward.Core.Sensing;
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

    /// <summary>Advances <paramref name="steps"/> steps, capturing each, with a light shell on the victim's west side at the given steps.</summary>
    private static void Run(World world, TankShieldQuads shield, Emberward.Core.Entities.Tanks.Tank victim, int steps,
        params int[] shotAt)
    {
        for (int i = 1; i <= steps; i++)
        {
            if (shotAt.Contains(i))
                world.Projectiles.Add(Projectile.CreateBullet(victim.Position + new Offset(-4, 0), new VectorF(1, 0), 0, 5));
            world.Advance(_ => default);
            shield.CaptureStep(world);
        }
    }

    [Fact]
    public void AShieldHit_StartsAPulse_ThatEndsAfterTheBubbleHasShown()
    {
        var (world, victim) = Shot(Tweaks.Weapon.BulletDamage);
        var shield = new TankShieldQuads();

        shield.CaptureStep(world);
        Assert.Equal(1, shield.PulseCount);

        Run(world, shield, victim, TankShieldQuads.ShowSteps - 2);
        Assert.Equal(1, shield.PulseCount);
        Run(world, shield, victim, 4);
        Assert.Equal(0, shield.PulseCount);
        Assert.Empty(Draw(shield, world, victim, phase: 0f));
    }

    /// <summary>A shield event on <paramref name="victim"/>, landing <paramref name="dx"/> cells east of its centre.</summary>
    private static StepEvent[] ShieldEvent(Emberward.Core.Entities.Tanks.Tank victim, DamageCause cause, int dx = -3,
        StepEventKind kind = StepEventKind.TankShieldHit) =>
        [new StepEvent(kind, victim.Position + new Offset(dx, 0), victim.Color, Count: 5, Cause: cause)];

    [Fact]
    public void RepeatedHits_KeepTheBubbleUp_WithoutFadingInAgain()
    {
        var (world, victim) = Shot(Tweaks.Weapon.BulletDamage);
        var shield = new TankShieldQuads();
        shield.CaptureStep(world);

        // Hits every 8 steps keep it showing well past one hit's span.
        for (int i = 1; i <= 2 * TankShieldQuads.ShowSteps; i++)
            shield.CaptureStep(world, i % 8 == 0 ? ShieldEvent(victim, DamageCause.EnemyFire) : []);

        Assert.Equal(1, shield.PulseCount);
        var vertices = Draw(shield, world, victim, phase: 0f);
        Assert.NotEmpty(vertices);
        // Four steps after the last hit (step 32): fully shown, where a fresh run would still be fading in at alpha
        // 0.5, and that hit's effects half spent.
        Assert.All(vertices, v => Assert.Equal(TankShieldQuads.VisibleLevels, MathF.Floor(v.P3)));
        Assert.All(vertices, v => Assert.Equal(4.5f / 9f, v.P1, precision: 4));
    }

    [Fact]
    public void ABite_ShowsTheBubble_WithNoHitPoint()
    {
        var (world, victim) = Shot(Tweaks.Weapon.BulletDamage);
        var shield = new TankShieldQuads();
        shield.CaptureStep(world);
        shield.CaptureStep(world, ShieldEvent(victim, DamageCause.Emberite));

        var vertices = Draw(shield, world, victim, phase: 0f);

        Assert.NotEmpty(vertices);
        Assert.All(vertices, v => Assert.Equal(TankShieldQuads.NoHitAngle, v.P2));
    }

    [Fact]
    public void AShellAndABreakingBiteInOneStep_ShowTheBreak_WhereTheShellCameIn()
    {
        var (world, victim) = Shot(Tweaks.Weapon.BulletDamage);
        var shield = new TankShieldQuads();
        shield.CaptureStep(world);
        victim.TakeDamage(Tweaks.TankShield.Capacity, DamageCause.Emberite);
        Assert.Equal(0f, world.TankShieldOf(victim).ShieldFill);

        shield.CaptureStep(world, [.. ShieldEvent(victim, DamageCause.EnemyFire),
            .. ShieldEvent(victim, DamageCause.Emberite, dx: 0, kind: StepEventKind.TankShieldBroken)]);
        var vertices = Draw(shield, world, victim, phase: 0f);

        Assert.NotEmpty(vertices);
        Assert.All(vertices, v => Assert.Equal(0f, v.P0));
        Assert.All(vertices, v => Assert.True(MathF.Abs(v.P2) > 2.5f && MathF.Abs(v.P2) <= MathF.PI, $"hit angle {v.P2}"));
    }

    [Fact]
    public void AnUnseenTank_OrADeadOne_ShowsNoShield()
    {
        var (world, victim) = Shot(Tweaks.Weapon.BulletDamage);
        var vision = world.TrackVision(world.TankList.Tanks[0]);
        var shield = new TankShieldQuads();
        shield.CaptureStep(world);
        Assert.False(vision.IsVisible(victim.Position));

        Assert.NotEmpty(Draw(shield, world, victim, phase: 0f));
        Assert.Empty(Draw(shield, world, victim, phase: 0f, vision: vision));
        Assert.Empty(Draw(shield, world, victim, phase: 0f, dead: true));
    }

    [Fact]
    public void ANewWorld_OrAJumpInSteps_DropsThePulses()
    {
        var (world, victim) = Shot(Tweaks.Weapon.BulletDamage);
        var shield = new TankShieldQuads();
        shield.CaptureStep(world);
        Assert.Equal(1, shield.PulseCount);

        // A restart: another world, whose tank of the same colour was never hit.
        var (other, _) = Shot(Tweaks.Weapon.BulletDamage);
        shield.CaptureStep(other, []);
        Assert.Equal(0, shield.PulseCount);

        // A late joiner's loaded match: the same world, many steps on.
        shield.CaptureStep(world);
        Assert.Equal(1, shield.PulseCount);
        world.Advance(_ => default);
        world.Advance(_ => default);
        shield.CaptureStep(world, []);
        Assert.Equal(0, shield.PulseCount);
    }

    [Fact]
    public void WhileHeld_TheBubbleFreezesAsDrawn()
    {
        var (world, victim) = Shot(Tweaks.Weapon.BulletDamage);
        var shield = new TankShieldQuads();
        shield.CaptureStep(world);
        var before = Draw(shield, world, victim, phase: 3f, alpha: 0.5f)[0];

        shield.Hold();
        var held = Draw(shield, world, victim, phase: 9f, alpha: 1f)[0];

        Assert.Equal(before.P1, held.P1);
        Assert.Equal(before.P3, held.P3);
    }

    /// <summary>The tank shield quads drawn for <paramref name="victim"/> after <paramref name="world"/>'s last step.</summary>
    private static EntityVertex[] EmitPulse(World world, Emberward.Core.Entities.Tanks.Tank victim, float phase = 0f)
    {
        var shield = new TankShieldQuads();
        shield.CaptureStep(world);
        return Draw(shield, world, victim, phase);
    }

    private static EntityVertex[] Draw(TankShieldQuads shield, World world, Emberward.Core.Entities.Tanks.Tank victim,
        float phase, float alpha = 0.5f, VisionMap? vision = null, bool dead = false)
    {
        var motion = new EntityMotion();
        motion.CaptureTanks([new TankRenderState(victim.Position, victim.Color, Direction: 4, IsDead: dead,
            TurretDirection: default)]);
        motion.SetAlpha(alpha);
        var quads = new EntityQuads();
        quads.Begin(new RenderView(new Size(1000, 1000), world.Terrain.Size, default, 2), phase, alpha);
        shield.Emit(quads, motion, vision);
        return quads.Vertices.AsSpan(0, quads.VertexCount).ToArray();
    }

    [Fact]
    public void AShieldedTankAtRest_ShowsNothing()
    {
        var world = TestHelpers.CreateSeededWorld(enableTerrainRegrowth: false, tankShield: true);
        world.Advance(_ => default);
        var tank = world.TankList.Tanks[1];
        var shield = new TankShieldQuads();
        shield.CaptureStep(world);

        Assert.True(world.TankShieldOf(tank).ShieldFill > 0f);
        Assert.Equal(0, shield.PulseCount);
        Assert.Empty(Draw(shield, world, tank, phase: 10f));
    }

    [Fact]
    public void TheBubble_FadesInQuickly_HoldsWhileHit_AndDissolvesAfter()
    {
        int show = TankShieldQuads.ShowSteps;
        Assert.Equal(0f, TankShieldQuads.Visibility(0f, 0f));
        Assert.InRange(TankShieldQuads.Visibility(0.5f, 0.5f), 0.4f, 0.6f);
        Assert.Equal(1f, TankShieldQuads.Visibility(1f, 1f));
        Assert.Equal(1f, TankShieldQuads.Visibility(30f, show * 0.3f));
        float last = 1f;
        for (float since = show * 0.4f; since <= show; since += 0.5f)
        {
            float now = TankShieldQuads.Visibility(100f, since);
            Assert.True(now <= last, $"visibility rose at {since}");
            last = now;
        }
        Assert.Equal(0f, TankShieldQuads.Visibility(100f, show));
        Assert.InRange(TankShieldQuads.Visibility(100f, show * 0.7f), 0.3f, 0.7f);
    }

    [Fact]
    public void TheFirstFrameOfAHit_IsNotYetShown_AndTheNextFadesIn()
    {
        var (world, victim) = Shot(Tweaks.Weapon.BulletDamage);
        var shield = new TankShieldQuads();
        shield.CaptureStep(world);

        Assert.Empty(Draw(shield, world, victim, phase: 0f, alpha: 0f));
        var vertices = Draw(shield, world, victim, phase: 0f, alpha: 0.5f);
        Assert.NotEmpty(vertices);
        Assert.All(vertices, v => Assert.InRange(MathF.Floor(v.P3), 100f, 155f));
    }

    [Fact]
    public void TheAnimationLoop_DividesThePhaseWrap_SoItIsSeamless()
    {
        // The entity layer wraps its phase at 1000 s; a loop that divides it runs on unbroken through the wrap.
        Assert.Equal(0f, 1000f % TankShieldQuads.LoopSeconds);
        var (world, victim) = Shot(Tweaks.Weapon.BulletDamage);
        var shield = new TankShieldQuads();
        shield.CaptureStep(world);

        float justBefore = Draw(shield, world, victim, phase: 999.99f)[0].P3;
        float justAfter = Draw(shield, world, victim, phase: 0.01f)[0].P3;

        Assert.Equal(MathF.Floor(justBefore), MathF.Floor(justAfter));
        Assert.InRange(justBefore - MathF.Floor(justBefore), 0.998f, 1f);
        Assert.InRange(justAfter - MathF.Floor(justAfter), 0f, 0.002f);
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

        // Fill 0 on a pulse is the break (the shader collapses the whole bubble), with the hit for the red patch.
        Assert.NotEmpty(vertices);
        Assert.All(vertices, v => Assert.Equal(EntityShape.KindTankShield, v.Kind));
        Assert.All(vertices, v => Assert.Equal(0f, v.P0));
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
