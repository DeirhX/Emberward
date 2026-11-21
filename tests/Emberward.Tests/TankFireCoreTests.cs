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
/// The exposed core on a burning hull and a dead tank's flash and slag disc: what the TankFire quad carries for the
/// shader (hull fill, shield fill, death age), and the hull-heat ramp the shader colours the core with.
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
        fire.Emit(quads, motion, vision, 1f / 60f, snap: false);
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
    [InlineData(0.5f)]
    [InlineData(0.25f)]
    [InlineData(0.05f)]
    [InlineData(0.01f)]
    public void TheCore_CarriesTheHullFill_ToTheFlamesAndTheLight(float hull)
    {
        var (fire, motion) = Burning(hull);
        var quads = Draw(fire, motion);

        EntityVertex flames = Assert.Single(Quads(quads, EntityShape.KindTankFire));
        Assert.Equal(hull, flames.R, 4);
        Assert.Equal(0f, flames.G);
        Assert.Equal(0f, flames.B);
        Assert.Equal(LevelAt(hull), flames.P0, 4);
        EntityVertex light = Assert.Single(Quads(quads, EntityShape.KindTankFireLight));
        Assert.Equal(hull, light.P3, 4);
        // A sagging core (below 25%) drips past the hull, so its quad reaches further down.
        Assert.Equal(hull < 0.25f ? 8f : 4.5f, MaxLocalY(quads, EntityShape.KindTankFire), 3);
    }

    [Fact]
    public void ARegeneratedShield_HidesTheCore()
    {
        var (fire, motion) = Burning(0.3f, shield: 1f);

        EntityVertex flames = Assert.Single(Quads(Draw(fire, motion), EntityShape.KindTankFire));
        Assert.Equal(1f, flames.G);

        (fire, motion) = Burning(0.3f, shield: 0.4f);
        Assert.Equal(0.4f, Assert.Single(Quads(Draw(fire, motion), EntityShape.KindTankFire)).G, 4);
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
        fire.Emit(quads, motion, null, 1f / 60f, snap: false);

        EntityVertex flames = Assert.Single(Quads(quads, EntityShape.KindTankFire));
        Assert.Equal(tank.HullFraction, flames.R, 4);
        Assert.Equal(world.TankShieldOf(tank).ShieldFill, flames.G, 4);
        Assert.True(flames.R is > 0f and < 0.75f);
    }

    [Fact]
    public void ADeadTank_FlashesThenLeavesSlag_ForTheDeathWindowOnly()
    {
        var (fire, motion) = Died(1);
        var quads = Draw(fire, motion);
        EntityVertex flash = Assert.Single(Quads(quads, EntityShape.KindTankFire));
        Assert.Equal(1f, flash.B, 3);            // just died
        Assert.Equal(0f, flash.P0);              // no flames
        Assert.Equal(0f, flash.R);
        EntityVertex light = Assert.Single(Quads(quads, EntityShape.KindTankFireLight));
        Assert.Equal(1f, light.P0, 3);           // the flash lights the ground at full size
        Assert.Empty(Quads(quads, EntityShape.KindFireParticle));

        int fps = Tweaks.Perf.TargetFps;
        (fire, motion) = Died(1 + (2 * fps));
        EntityVertex slag = Assert.Single(Quads(Draw(fire, motion), EntityShape.KindTankFire));
        Assert.Equal(1f + (2f / TankFireQuads.DeathSeconds), slag.B, 3);

        (fire, motion) = Died(1 + (int)MathF.Ceiling(TankFireQuads.DeathSeconds * fps));
        Assert.Equal(0, Draw(fire, motion).VertexCount);
    }

    [Fact]
    public void ATankAlreadyDead_WhenFirstSeen_DoesNotFlash()
    {
        var fire = new TankFireQuads();
        var motion = new EntityMotion();
        Capture(fire, motion, 3, [0f], [0f], [true]);

        Assert.Equal(0, Draw(fire, motion).VertexCount);
    }

    [Fact]
    public void TheSlag_StaysWhereTheTankDied_AfterARespawn()
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
        var playerDeath = Quads(Draw(playerDied, playerMotion), EntityShape.KindTankFire).Single(v => v.B > 0.5f);
        var enemyDeath = Quads(Draw(enemyDied, enemyMotion), EntityShape.KindTankFire).Single(v => v.B > 0.5f);
        Assert.Equal(playerDeath.B, enemyDeath.B);
        Assert.Equal(playerDeath.R, enemyDeath.R);
    }

    // The hull-heat ramp's stops (hull fill, colour), as the HUD's HudPalette.HullHeat has them.
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
    /// The HUD colours the hull gauge with the same ramp (HudPalette.HullHeat, ABGR). Checked when that function is in
    /// the build, so the two cannot drift once both are merged.
    /// </summary>
    [Fact]
    public void TheHudRamp_MatchesTheShaderStops()
    {
        var method = typeof(Emberward.Desktop.Gui.HudPalette).GetMethod("HullHeat", [typeof(float)]);
        if (method == null || method.ReturnType != typeof(uint))
            return;
        foreach (var (name, hull, hex) in HullHeatStops)
        {
            if (float.IsNaN(hull))
                continue;
            uint color = (uint)method.Invoke(null, [hull])!;
            string actual = string.Create(CultureInfo.InvariantCulture,
                $"{color & 0xFF:X2}{(color >> 8) & 0xFF:X2}{(color >> 16) & 0xFF:X2}");
            Assert.True(actual == hex, $"HudPalette.HullHeat({hull}) is #{actual}, {name} is #{hex}");
        }
    }
}
