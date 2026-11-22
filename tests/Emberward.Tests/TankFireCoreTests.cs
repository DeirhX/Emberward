using System.Globalization;
using System.Text.RegularExpressions;
using Emberward.Core.Config;
using Emberward.Core.Entities;
using Emberward.Core.Rendering;
using Emberward.Core.Sensing;
using Emberward.Core.Types;
using Emberward.Desktop.Rendering;
using Emberward.Desktop.Rendering.Entities;

namespace Emberward.Tests;

/// <summary>
/// The exposed core on a burning hull and a dead tank's core glow: how each part of the fire eases in, what the
/// TankFire quad carries for the shader (hull fill, core glow, soot), and the hull-heat ramp the core is coloured with.
/// </summary>
public class TankFireCoreTests
{
    private const int East = 3;

    private static TankRenderState Tank(int x, int y, int color = 0, bool dead = false) =>
        new(new Position(x, y), Color: color, Direction: East, IsDead: dead, TurretDirection: default);

    // Core's rule (TankShieldSystem.FireLevelOf) for the level a hull burns at.
    private static float LevelAt(float hull) =>
        Math.Clamp((Tweaks.TankShield.FireIgniteHullFraction - hull) / Tweaks.TankShield.FireIgniteHullFraction, 0f, 1f);

    private static EntityQuads Draw(TankFireQuads fire, EntityMotion motion, VisionMap? vision = null)
    {
        var quads = new EntityQuads();
        quads.Begin(new RenderView(new Size(1600, 1200), new Size(400, 300), new Position(0, 0), 4), 1.3f, 0f);
        fire.EmitLight(quads, motion, vision);
        fire.Emit(quads, motion, vision);
        return quads;
    }

    private static List<EntityVertex> Quads(EntityQuads quads, float kind)
    {
        var found = new List<EntityVertex>();
        for (int v = 0; v < quads.VertexCount; v += 6)
        {
            if (quads.Vertices[v].Kind == kind)
                found.Add(quads.Vertices[v]);
        }
        return found;
    }

    private static float MaxLocalY(EntityQuads quads, float kind)
    {
        float max = float.MinValue;
        for (int v = 0; v < quads.VertexCount; v++)
        {
            if (quads.Vertices[v].Kind == kind)
                max = Math.Max(max, quads.Vertices[v].LocalY);
        }
        return max;
    }

    /// <summary>Tanks at (100 + 50 * slot, 100), captured <paramref name="steps"/> times with the given hull and shield.</summary>
    private static void Capture(TankFireQuads fire, EntityMotion motion, int steps, float[] hulls, float[] shields, bool[] dead)
    {
        var states = new TankRenderState[hulls.Length];
        for (int step = 0; step < steps; step++)
        {
            for (int i = 0; i < hulls.Length; i++)
                states[i] = Tank(100 + (50 * i), 100, color: i, dead: dead[i]);
            motion.CaptureTanks(states);
            fire.BeginCapture(hulls.Length);
            for (int i = 0; i < hulls.Length; i++)
            {
                fire.CaptureTank(i, new Position(100 + (50 * i), 100), dead[i] ? 0f : LevelAt(hulls[i]), hulls[i], shields[i],
                    dead[i]);
            }
        }
        for (int i = 0; i < hulls.Length; i++)
            motion.UpdateHullHeading(i, East, 0f, snap: true);
    }

    private static (TankFireQuads Fire, EntityMotion Motion) Burning(float hull, float shield = 0f)
    {
        var fire = new TankFireQuads();
        var motion = new EntityMotion();
        Capture(fire, motion, 4, [hull], [shield], [false]);
        return (fire, motion);
    }

    /// <summary>A tank that burned at a low hull for a few steps, then died and has been dead for <paramref name="deadSteps"/>.</summary>
    private static (TankFireQuads Fire, EntityMotion Motion) Died(int deadSteps, int tanks = 1, int dyingSlot = 0)
    {
        var fire = new TankFireQuads();
        var motion = new EntityMotion();
        float[] hulls = new float[tanks], shields = new float[tanks];
        bool[] dead = new bool[tanks];
        Array.Fill(hulls, 0.1f);
        Capture(fire, motion, 4, hulls, shields, dead);
        hulls[dyingSlot] = 0f;
        dead[dyingSlot] = true;
        Capture(fire, motion, deadSteps, hulls, shields, dead);
        return (fire, motion);
    }

    [Theory]
    [InlineData(0.749f)]
    [InlineData(0.7f)]
    [InlineData(0.5f)]
    [InlineData(0.25f)]
    [InlineData(0.05f)]
    [InlineData(0.01f)]
    public void TheCore_CarriesTheHullFill_ToTheFlamesAndTheLight(float hull)
    {
        var (fire, motion) = Burning(hull);
        var quads = Draw(fire, motion);
        float level = LevelAt(hull);

        EntityVertex flames = Assert.Single(Quads(quads, EntityShape.KindTankFire));
        Assert.Equal(hull, flames.R, 4);
        Assert.Equal(FireOnset.CoreGlow(level), flames.G, 4);
        Assert.Equal(FireOnset.Soot(level), flames.B, 4);
        Assert.Equal(FireOnset.Flames(level), flames.P0, 4);
        EntityVertex light = Assert.Single(Quads(quads, EntityShape.KindTankFireLight));
        Assert.Equal(FireOnset.Light(level), light.R, 4);
        Assert.Equal(hull, light.P3, 4);
        // The core's glow fits in the quad; a sagging core (below 25%) drips further down.
        Assert.Equal(hull < 0.25f ? 8f : 6.5f, MaxLocalY(quads, EntityShape.KindTankFire), 3);
    }

    private static float[] Onsets(float hull)
    {
        float level = LevelAt(hull);
        return [FireOnset.Light(level), FireOnset.CoreGlow(level), FireOnset.Soot(level), FireOnset.Flames(level),
            FireOnset.SmokeShare(level), FireOnset.EmberShare(level), FireOnset.Sound(level), FireOnset.FlameShare(level)];
    }

    [Fact]
    public void EveryPart_EasesInFromZero_AtTheIgnitionThreshold()
    {
        Assert.All(Onsets(0.76f), v => Assert.Equal(0f, v));
        Assert.All(Onsets(0.75f), v => Assert.Equal(0f, v));
        float[] at70 = Onsets(0.70f), at60 = Onsets(0.60f);
        for (int part = 0; part < at70.Length; part++)
            Assert.True(at60[part] >= at70[part], $"part {part} fell from 70% to 60% hull");
        Assert.InRange(at70[0], 0.05f, 0.95f);   // the light is already coming up at 70%

        // Continuous and never falling as the hull drops, in steps of 0.1% hull.
        float[] last = Onsets(0.8f);
        for (int i = 799; i >= 0; i--)
        {
            float[] now = Onsets(i / 1000f);
            for (int part = 0; part < now.Length; part++)
            {
                Assert.True(now[part] >= last[part] - 1e-6f, $"part {part} fell at hull {i / 1000f}");
                Assert.True(now[part] - last[part] < 0.02f, $"part {part} jumps at hull {i / 1000f}");
            }
            last = now;
        }
    }

    [Fact]
    public void ThePartsComeIn_Staggered()
    {
        // The highest hull at which each part shows at all: the light first, then the core, its soot, the flames,
        // smoke and, last, embers.
        float First(int part)
        {
            for (int i = 750; i >= 0; i--)
            {
                if (Onsets(i / 1000f)[part] > 0f)
                    return i / 1000f;
            }
            return 0f;
        }
        float[] firsts = [First(0), First(1), First(2), First(3), First(4), First(5)];
        for (int part = 1; part < firsts.Length; part++)
            Assert.True(firsts[part] < firsts[part - 1], $"part {part} starts at {firsts[part]}, before part {part - 1}");
    }

    [Fact]
    public void ASuddenHit_EasesTheFireIn_AndARepairEasesItOut()
    {
        var fire = new TankFireQuads();
        var motion = new EntityMotion();
        Capture(fire, motion, 2, [1f], [0f], [false]);
        Assert.Equal(0f, fire.DrawnLevel(0));

        // One hit takes the hull from full to 10%.
        float target = LevelAt(0.1f), last = 0f;
        int fps = Tweaks.Perf.TargetFps;
        for (int step = 0; step < 2 * fps; step++)
        {
            Capture(fire, motion, 1, [0.1f], [0f], [false]);
            float level = fire.DrawnLevel(0);
            Assert.True(level > last && level - last < 0.15f, $"step {step}: {last} to {level}");
            last = level;
        }
        Assert.InRange(last, 0.95f * target, target);

        // Docked: the hull is repaired at once; the fire dies down, then stops drawing.
        int drawnSteps = 0;
        for (int step = 0; step < 4 * fps; step++)
        {
            Capture(fire, motion, 1, [1f], [0f], [false]);
            float level = fire.DrawnLevel(0);
            Assert.True(level < last || level == 0f);
            Assert.True(last - level < 0.15f);
            last = level;
            if (Draw(fire, motion).VertexCount > 0)
                drawnSteps++;
        }
        Assert.Equal(0f, last);
        Assert.InRange(drawnSteps, 1, 4 * fps - 1);
    }

    [Fact]
    public void TheParticleCount_ChangesSmoothly_WithTheLevel()
    {
        int previous = 0;
        for (int i = 0; i <= 100; i++)
        {
            float hull = 0.75f * (1f - (i / 100f));
            var (fire, motion) = Burning(hull);
            int count = Quads(Draw(fire, motion), EntityShape.KindFireParticle).Count;
            Assert.True(count >= previous, $"level {i / 100f}: {count} particles after {previous}");
            Assert.True(count - previous <= 3, $"level {i / 100f}: {count} particles after {previous}");
            previous = count;
        }
        Assert.Equal(FireParticles.MaxPerTank, previous);
    }

    [Fact]
    public void ARegeneratingShield_HidesTheCore()
    {
        var (fire, motion) = Burning(0.3f, shield: 1f);
        Assert.Equal(0f, Assert.Single(Quads(Draw(fire, motion), EntityShape.KindTankFire)).G);

        (fire, motion) = Burning(0.3f, shield: 0.4f);
        Assert.Equal(0.6f * FireOnset.CoreGlow(LevelAt(0.3f)),
            Assert.Single(Quads(Draw(fire, motion), EntityShape.KindTankFire)).G, 4);

        // The shield coming back fades the core out over the steps, rather than at once.
        (fire, motion) = Burning(0.3f, shield: 0f);
        Capture(fire, motion, 1, [0.3f], [1f], [false]);
        Assert.InRange(Assert.Single(Quads(Draw(fire, motion), EntityShape.KindTankFire)).G, 0.5f, 0.99f);
    }

    [Fact]
    public void AWorldTank_HandsOverItsHullAndShield()
    {
        var world = TestHelpers.CreateSeededWorld(enableTerrainRegrowth: false, tankShield: true);
        var tank = world.TankList.Tanks[0];
        tank.TakeDamage(Tweaks.TankShield.Capacity + (tank.Reactor.Health / 2), DamageCause.EnemyFire);
        var fire = new TankFireQuads();
        var motion = new EntityMotion();
        var states = new TankRenderState[world.TankList.Count];
        for (int step = 0; step < 3; step++)
        {
            int count = world.TankList.CopyRenderStates(states);
            motion.CaptureTanks(states.AsSpan(0, count));
            fire.CaptureStep(world);
        }
        motion.UpdateHullHeading(0, states[0].Direction, 0f, snap: true);
        var view = new RenderView(new Size(1600, 1200), world.Terrain.Size, new Position(
            (int)((tank.Position.X * 4) - 800), (int)((tank.Position.Y * 4) - 600)), 4);
        var quads = new EntityQuads();
        quads.Begin(view, 1.3f, 0f);
        fire.Emit(quads, motion, null);

        EntityVertex flames = Assert.Single(Quads(quads, EntityShape.KindTankFire));
        Assert.Equal(tank.HullFraction, flames.R, 4);
        Assert.Equal(FireOnset.CoreGlow(tank.FireLevel) * (1f - world.TankShieldOf(tank).ShieldFill), flames.G, 4);
        Assert.True(flames.R is > 0f and < 0.75f);
    }

    // The death glow: a core's glow with no flames and no soot (a dying tank's own fire quad has no core glow).
    private static EntityVertex DeathQuad(EntityQuads quads) =>
        Quads(quads, EntityShape.KindTankFire).Single(v => v.P0 == 0f && v.B == 0f && v.G > 0f);

    [Fact]
    public void TheDeathGlow_FlaresFromYellowToWhite_ThenFadesSmoothly()
    {
        var (glow0, hull0) = TankFireQuads.DeathGlow(1f, 0f);
        Assert.Equal(1f, glow0);
        Assert.Equal(0.05f, hull0, 4);      // yellow: the ramp at 5%
        var (peak, white) = TankFireQuads.DeathGlow(1f, TankFireQuads.DeathFlareSeconds);
        Assert.Equal(0f, white);            // white-hot: the ramp at an empty hull
        Assert.True(peak > 1f);
        Assert.Equal(0f, TankFireQuads.DeathGlow(1f, TankFireQuads.DeathSeconds).Glow, 4);

        float lastGlow = glow0, lastHull = hull0;
        for (int ms = 1; ms <= (int)(TankFireQuads.DeathSeconds * 1000f); ms++)
        {
            var (glow, hull) = TankFireQuads.DeathGlow(1f, ms / 1000f);
            Assert.True(Math.Abs(glow - lastGlow) < 0.02f, $"the glow jumps at {ms} ms");
            Assert.True(hull <= lastHull + 1e-6f, $"the colour cools back at {ms} ms");
            if (ms / 1000f > TankFireQuads.DeathFlareSeconds)
                Assert.True(glow <= lastGlow + 1e-6f, $"the glow brightens again at {ms} ms");
            lastGlow = glow;
            lastHull = hull;
        }
    }

    [Fact]
    public void ADeadTank_GlowsWithNoEdge_ForTheDeathWindowOnly()
    {
        var (fire, motion) = Died(1);
        var quads = Draw(fire, motion);
        EntityVertex glow = DeathQuad(quads);
        Assert.Equal(0f, glow.P0);              // no flames
        Assert.Equal(0f, glow.B);               // no soot ring, no crust: nothing with an edge
        Assert.Equal(0.05f, glow.R, 3);         // yellow
        Assert.Equal(FireOnset.CoreGlow(LevelAt(0.1f)), glow.G, 3);   // from the glow it had
        // The quad reaches past the glow's soft falloff (1.6 times the core's 3.5 cells, off the hull's centre).
        Assert.True(MaxLocalY(quads, EntityShape.KindTankFire) >= (1.6f * 3.5f) + 0.5f);

        int fps = Tweaks.Perf.TargetFps;
        (fire, motion) = Died(1 + (int)(TankFireQuads.DeathFlareSeconds * fps));
        Assert.Equal(0f, DeathQuad(Draw(fire, motion)).R, 3);       // white-hot

        (fire, motion) = Died(1 + (int)MathF.Ceiling(TankFireQuads.DeathSeconds * fps));
        Assert.DoesNotContain(Quads(Draw(fire, motion), EntityShape.KindTankFire), v => v.G > 0f);
    }

    [Fact]
    public void OnTheStepATankDies_ItsFireDiesDown_AndItsSmokeLivesOut()
    {
        var (alive, aliveMotion) = Died(0);
        var before = Draw(alive, aliveMotion);
        float levelBefore = alive.DrawnLevel(0);
        int particlesBefore = Quads(before, EntityShape.KindFireParticle).Count;

        var (fire, motion) = Died(1);
        var quads = Draw(fire, motion);
        // The flames and the light pool are still there, easing out; the core has become the death glow.
        EntityVertex flames = Quads(quads, EntityShape.KindTankFire).Single(v => v.P0 > 0f);
        Assert.Equal(0f, flames.G);
        Assert.InRange(fire.DrawnLevel(0), 0.8f * levelBefore, levelBefore);
        Assert.Equal(2, Quads(quads, EntityShape.KindTankFireLight).Count);
        // The smoke and embers already in the air stay (a step's worth of births fewer at most).
        int particles = Quads(quads, EntityShape.KindFireParticle).Count;
        Assert.InRange(particles, particlesBefore - 4, particlesBefore);

        // Then they thin out as their lives end, none newly born, and the fire goes out.
        int fps = Tweaks.Perf.TargetFps;
        (fire, motion) = Died(1 + fps);
        Assert.True(Quads(Draw(fire, motion), EntityShape.KindFireParticle).Count < particles);
        (fire, motion) = Died(4 * fps);
        Assert.Equal(0, Draw(fire, motion).VertexCount);
    }

    [Fact]
    public void TheDeathGlow_HoldsStill_WhileTheWorldIsPaused()
    {
        var (fire, motion) = Died(3);
        var quads = new EntityQuads();
        quads.Begin(new RenderView(new Size(1600, 1200), new Size(400, 300), new Position(0, 0), 4), 1.3f, 0.5f);
        fire.Emit(quads, motion, null);
        float glow = DeathQuad(quads).G;

        fire.Hold();
        for (int frame = 0; frame < 30; frame++)
        {
            quads.Begin(new RenderView(new Size(1600, 1200), new Size(400, 300), new Position(0, 0), 4), 1.3f + frame,
                frame % 2 == 0 ? 1f : 0f);
            fire.Emit(quads, motion, null);
            Assert.Equal(glow, DeathQuad(quads).G);
        }
    }

    [Fact]
    public void ASlotThatJumps_ToADeadTank_StartsNoDeathGlow()
    {
        var fire = new TankFireQuads();
        var motion = new EntityMotion();
        Capture(fire, motion, 4, [0.1f], [0f], [false]);
        // The list was reordered: slot 0 now holds a dead tank far from where its last tank was.
        motion.CaptureTanks([Tank(300, 250, dead: true)]);
        fire.BeginCapture(1);
        fire.CaptureTank(0, new Position(300, 250), 0f, 0f, 0f, dead: true);

        Assert.Equal(0, Draw(fire, motion).VertexCount);
    }

    [Fact]
    public void ATankAlreadyDead_WhenFirstSeen_DoesNotGlow()
    {
        var fire = new TankFireQuads();
        var motion = new EntityMotion();
        Capture(fire, motion, 3, [0f], [0f], [true]);

        Assert.Equal(0, Draw(fire, motion).VertexCount);
    }

    [Fact]
    public void TheDeathGlow_StaysWhereTheTankDied_AfterARespawn()
    {
        var (fire, motion) = Died(10);
        // Respawned at a base far away, at a full hull.
        motion.CaptureTanks([Tank(300, 250)]);
        fire.BeginCapture(1);
        fire.CaptureTank(0, new Position(300, 250), 0f, 1f, 1f, dead: false);

        var quads = Draw(fire, motion);
        Assert.Single(Quads(quads, EntityShape.KindTankFire));
        int v = 0;
        while (quads.Vertices[v].Kind != EntityShape.KindTankFire)
            v += 6;
        Assert.Equal(EntityQuads.Centre(100), (quads.Vertices[v].CellX + quads.Vertices[v + 2].CellX) / 2f, 3);
        Assert.Equal(EntityQuads.Centre(100), (quads.Vertices[v].CellY + quads.Vertices[v + 2].CellY) / 2f, 3);
    }

    [Fact]
    public void ADeathOutOfSight_DrawsNothing()
    {
        var world = TestHelpers.CreateSeededWorld();
        var vision = new VisionMap(world.Terrain);
        var (fire, motion) = Died(2);

        Assert.Equal(0, Draw(fire, motion, vision).VertexCount);
    }

    [Fact]
    public void AnEnemyTank_ShowsItsCoreAndDiesLikeThePlayers()
    {
        static float[] Core(EntityQuads quads)
        {
            EntityVertex v = Assert.Single(Quads(quads, EntityShape.KindTankFire));
            return [v.R, v.G, v.B, v.P0];
        }

        var fire = new TankFireQuads();
        var motion = new EntityMotion();
        Capture(fire, motion, 4, [0.2f, 1f], [0.3f, 0f], [false, false]);
        float[] player = Core(Draw(fire, motion));
        fire = new TankFireQuads();
        motion = new EntityMotion();
        Capture(fire, motion, 4, [1f, 0.2f], [0f, 0.3f], [false, false]);
        Assert.Equal(player, Core(Draw(fire, motion)));

        var (playerDied, playerMotion) = Died(5, tanks: 2, dyingSlot: 0);
        var (enemyDied, enemyMotion) = Died(5, tanks: 2, dyingSlot: 1);
        EntityVertex playerDeath = DeathQuad(Draw(playerDied, playerMotion));
        EntityVertex enemyDeath = DeathQuad(Draw(enemyDied, enemyMotion));
        Assert.Equal(playerDeath.G, enemyDeath.G);
        Assert.Equal(playerDeath.R, enemyDeath.R);
    }

    // The hull-heat ramp's stops (hull fill, colour), as the HUD's HudPalette.HullEmber has them.
    private static readonly (string Name, float Hull, string Hex)[] HullHeatStops =
    [
        ("kHullHeat0", 0f, "FFF6D8"),
        ("kHullHeat5", 0.05f, "FFD040"),
        ("kHullHeat25", 0.25f, "FF6A10"),
        ("kHullHeat50", 0.5f, "B3260C"),
        ("kHullHeat75", 0.75f, "5A1408"),
        ("kCoreSoot", float.NaN, "140A08"),
        ("kSlag", float.NaN, "2A1A14"),
        ("kSlagCrack", float.NaN, "FF5A1A"),
    ];

    [Fact]
    public void TheShaderRamp_HasTheHullHeatStops()
    {
        string shader = File.ReadAllText(Path.Combine(EntityLayoutGuardTests.FindRepositoryRoot(), "src", "Emberward.Desktop", "Rendering",
            "Shaders", "Entity", "TankFire.hlsli"));
        foreach (var (name, _, hex) in HullHeatStops)
        {
            var match = Regex.Match(shader, $@"static const float3 {name} = float3\((?<r>[\d.]+), (?<g>[\d.]+), (?<b>[\d.]+)\);");
            Assert.True(match.Success, $"TankFire.hlsli no longer declares {name}");
            for (int c = 0; c < 3; c++)
            {
                float expected = int.Parse(hex.AsSpan(c * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f;
                float actual = float.Parse(match.Groups[c switch { 0 => "r", 1 => "g", _ => "b" }].Value, CultureInfo.InvariantCulture);
                Assert.True(Math.Abs(expected - actual) < 0.001f, $"{name} channel {c}: {actual}, #{hex} is {expected}");
            }
        }
    }

    /// <summary>
    /// The HUD colours the hull gauge with the same ramp (HudPalette.HullEmber, ABGR), so the two cannot drift.
    /// </summary>
    [Fact]
    public void TheHudRamp_MatchesTheShaderStops()
    {
        foreach (var (name, hull, hex) in HullHeatStops)
        {
            if (float.IsNaN(hull))
                continue;
            uint color = Emberward.Desktop.Gui.HudPalette.HullEmber(hull);
            string actual = string.Create(CultureInfo.InvariantCulture,
                $"{color & 0xFF:X2}{(color >> 8) & 0xFF:X2}{(color >> 16) & 0xFF:X2}");
            Assert.True(actual == hex, $"HudPalette.HullEmber({hull}) is #{actual}, {name} is #{hex}");
        }
    }
}
