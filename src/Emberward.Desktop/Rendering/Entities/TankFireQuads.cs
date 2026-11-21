namespace Emberward.Desktop.Rendering.Entities;

using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Rendering;
using Emberward.Core.Sensing;
using Emberward.Core.Types;

/// <summary>
/// The fire on a tank whose hull is damaged (DESIGN.md §4.7): a flickering light on the ground under it, soot and flame
/// tongues on the hull that lean away from the way it drives, and smoke and embers that rise from where it has been.
/// How big it is comes from <see cref="Emberward.Core.Entities.Tanks.Tank.FireLevel"/>, which Core answers; this only
/// draws. A tank that is not burning costs nothing but its step trail. Output only.
/// </summary>
/// <remarks>
/// Smoke and embers keep no particle pool: each of a tank's particles is a slot on a loop of its own life, born where
/// the tank was that long ago (the trail of its last <see cref="TrailSteps"/> step positions), so a moving tank leaves
/// a trail that stays where it was made, and nothing is allocated per step or per frame.
/// </remarks>
public sealed class TankFireQuads
{
    /// <summary>How many of each tank's step positions the trail keeps (four seconds at 24 steps a second).</summary>
    public const int TrailSteps = 96;

    /// <summary>A tank that moved further than this in one step was respawned: its trail starts again where it is.</summary>
    public const float RespawnJumpCells = 20f;

    /// <summary>How long the flames take to lean most of the way into a new drift (seconds; exponential).</summary>
    public const float FlameLeanSeconds = 0.25f;

    private const float PhaseCycleSeconds = 4f;
    private const float SmokeLifeSeconds = 1.6f;
    private const float SmokeRiseCellsPerSecond = 4f;
    private const float EmberLifeSeconds = 0.7f;
    private const float EmberRiseCellsPerSecond = 7f;
    private const float EmberMinLevel = 0.3f;
    private const float MaxLeanDrift = 1.2f;
    private const float ParticleSmoke = 1f;
    private const float ParticleEmber = 0f;

    // Per tank slot (the world's tank list index).
    private float[] _levels = [];
    private Position[] _trail = [];
    private int[] _trailHead = [];
    private bool[] _trailKnown = [];
    private PositionF[] _lean = [];
    private int _count;

    /// <summary>Smoke particles a tank burning at <paramref name="level"/> keeps in the air.</summary>
    public static int SmokeCount(float level) => level > 0f ? (int)(6f + 14f * Math.Min(level, 1f)) : 0;

    /// <summary>Embers a tank burning at <paramref name="level"/> throws; none while the fire is small.</summary>
    public static int EmberCount(float level) => level > EmberMinLevel ? (int)(4f + 12f * Math.Min(level, 1f)) : 0;

    /// <summary>Records each tank's fire and position after a simulation step.</summary>
    public void CaptureStep(World world)
    {
        var tanks = world.TankList.Tanks;
        BeginCapture(tanks.Count);
        for (int i = 0; i < tanks.Count; i++)
            CaptureTank(i, tanks[i].Position, tanks[i].IsDead ? 0f : tanks[i].FireLevel);
    }

    /// <summary>Starts a step's capture of <paramref name="count"/> tanks (then <see cref="CaptureTank"/> for each).</summary>
    public void BeginCapture(int count)
    {
        if (_levels.Length < count)
        {
            int old = _levels.Length;
            Array.Resize(ref _levels, count);
            Array.Resize(ref _trail, count * TrailSteps);
            Array.Resize(ref _trailHead, count);
            Array.Resize(ref _trailKnown, count);
            Array.Resize(ref _lean, count);
            for (int i = old; i < count; i++)
                _trailKnown[i] = false;
        }
        for (int i = count; i < _count; i++)
            _trailKnown[i] = false;
        _count = count;
    }

    /// <summary>Records tank <paramref name="slot"/>'s position and fire level (0: not burning) after this step.</summary>
    public void CaptureTank(int slot, Position position, float level)
    {
        _levels[slot] = Math.Clamp(level, 0f, 1f);
        int head = _trailHead[slot];
        Span<Position> trail = _trail.AsSpan(slot * TrailSteps, TrailSteps);
        if (_trailKnown[slot])
        {
            Position last = trail[head];
            float dx = position.X - last.X, dy = position.Y - last.Y;
            if (dx * dx + dy * dy <= RespawnJumpCells * RespawnJumpCells)
            {
                head = (head + 1) % TrailSteps;
                trail[head] = position;
                _trailHead[slot] = head;
                return;
            }
        }
        // A new tank, or one that respawned: its trail starts where it is now, and its flames stand straight.
        trail.Fill(position);
        _trailHead[slot] = 0;
        _trailKnown[slot] = true;
        _lean[slot] = default;
    }

    /// <summary>Where tank <paramref name="slot"/> was <paramref name="stepsAgo"/> steps ago (as far back as the trail goes).</summary>
    public Position TrailPosition(int slot, int stepsAgo)
    {
        int back = Math.Clamp(stepsAgo, 0, TrailSteps - 1);
        return _trail[(slot * TrailSteps) + ((_trailHead[slot] - back + TrailSteps) % TrailSteps)];
    }

    /// <summary>The way tank <paramref name="slot"/>'s flames point (a unit vector; (0, -1) is straight up the screen).</summary>
    public PositionF FlameDirection(int slot)
    {
        float x = -0.5f * _lean[slot].X, y = -1f - (0.5f * _lean[slot].Y);
        float length = MathF.Sqrt((x * x) + (y * y));
        return length < 1e-4f ? new PositionF(0f, -1f) : new PositionF(x / length, y / length);
    }

    /// <summary>
    /// The light each burning tank throws on the ground: drawn under the tanks (call before <see cref="TankQuads"/>).
    /// </summary>
    public void EmitLight(EntityQuads quads, EntityMotion motion, VisionMap? vision)
    {
        ReadOnlySpan<TankRenderState> tanks = motion.Tanks;
        float scale = quads.View.PixelScale;
        float t = quads.Phase % PhaseCycleSeconds;
        for (int i = 0; i < tanks.Length && i < _count; i++)
        {
            float level = _levels[i];
            if (!IsDrawn(tanks[i], level, vision))
                continue;
            var (start, drift) = motion.TankMotion(i);
            float radius = 5f + (7f * level);
            if (quads.IsOffScreen(quads.ScreenX(start.X), quads.ScreenY(start.Y), (radius + 2f) * scale))
                continue;
            quads.Add(EntityQuads.Centre(start.X), EntityQuads.Centre(start.Y), 1f, 0f, -radius, radius, -radius, radius,
                EntityShape.KindTankFireLight, level, t, Seed(i), 0f, drift);
        }
    }

    /// <summary>
    /// The soot and flames on each burning hull, and its smoke and embers: drawn over the tanks (call after
    /// <see cref="TankQuads"/>, which turns the hulls this draws soot on). <paramref name="frameSeconds"/> is the game
    /// time since the last displayed frame, which leans the flames into the drift; <paramref name="snap"/> leans them at once.
    /// </summary>
    public void Emit(EntityQuads quads, EntityMotion motion, VisionMap? vision, float frameSeconds, bool snap)
    {
        ReadOnlySpan<TankRenderState> tanks = motion.Tanks;
        float scale = quads.View.PixelScale;
        float phase = quads.Phase;
        float t = phase % PhaseCycleSeconds;
        float follow = snap ? 1f : 1f - MathF.Exp(-Math.Max(0f, frameSeconds) / FlameLeanSeconds);
        for (int i = 0; i < tanks.Length && i < _count; i++)
        {
            float level = _levels[i];
            var (start, drift) = motion.TankMotion(i);
            LeanInto(i, drift, follow);
            if (!IsDrawn(tanks[i], level, vision))
                continue;

            EmitParticles(quads, vision, i, level, phase, smoke: true);

            if (!quads.IsOffScreen(quads.ScreenX(start.X), quads.ScreenY(start.Y), 16f * scale))
            {
                // The flame frame: local -y points the way the flames go, so the shader draws them straight up.
                PositionF flame = FlameDirection(i);
                float axisX = -flame.Y, axisY = flame.X;
                float heading = MathF.IEEERemainder(motion.HullHeading(i) - MathF.Atan2(axisY, axisX), MathF.Tau);
                quads.Add(EntityQuads.Centre(start.X), EntityQuads.Centre(start.Y), axisX, axisY, -5.5f, 5.5f, -14f, 4.5f,
                    EntityShape.KindTankFire, level, t, Seed(i), heading, drift);
            }

            EmitParticles(quads, vision, i, level, phase, smoke: false);
        }
    }

    private static bool IsDrawn(in TankRenderState tank, float level, VisionMap? vision) =>
        level > 0f && !tank.IsDead && Sight.Sees(vision, tank.Position);

    private void LeanInto(int slot, PositionF drift, float follow)
    {
        float dx = drift.X, dy = drift.Y;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length > MaxLeanDrift)
        {
            dx *= MaxLeanDrift / length;
            dy *= MaxLeanDrift / length;
        }
        PositionF lean = _lean[slot];
        _lean[slot] = new PositionF(lean.X + ((dx - lean.X) * follow), lean.Y + ((dy - lean.Y) * follow));
    }

    /// <summary>
    /// One kind of particle for tank <paramref name="slot"/>. Particle k of n is on a loop of its life L, at age
    /// <c>frac(phase / L + k / n)</c>; it was born where the tank was <c>age * L</c> ago and has risen since.
    /// </summary>
    private void EmitParticles(EntityQuads quads, VisionMap? vision, int slot, float level, float phase, bool smoke)
    {
        float kind = smoke ? ParticleSmoke : ParticleEmber;
        int count = smoke ? SmokeCount(level) : EmberCount(level);
        float life = smoke ? SmokeLifeSeconds : EmberLifeSeconds;
        float rise = smoke ? SmokeRiseCellsPerSecond : EmberRiseCellsPerSecond;
        float stepsPerSecond = Tweaks.Perf.TargetFps;
        float scale = quads.View.PixelScale;
        for (int k = 0; k < count; k++)
        {
            float loop = (phase / life) + ((float)k / count);
            float cycle = MathF.Floor(loop);
            float age = loop - cycle;
            // The frame is drawn alpha of a step after the latest step, so that much less of the trail lies behind it.
            int stepsAgo = (int)MathF.Round((age * life * stepsPerSecond) - quads.Alpha);
            Position birth = TrailPosition(slot, stepsAgo);
            float h1 = Hash(slot, k, (int)cycle, smoke ? 17 : 29);
            float h2 = Hash(slot, k, (int)cycle, smoke ? 43 : 61);
            float risen = rise * age * life;
            float x, y, half;
            if (smoke)
            {
                x = EntityQuads.Centre(birth.X) + ((h1 - 0.5f) * 2f) + ((h2 - 0.5f) * 2.5f * age);
                y = EntityQuads.Centre(birth.Y) - 0.5f - risen;
                half = 0.5f * (1.5f + (3f * age)) * 1.3f;
            }
            else
            {
                x = EntityQuads.Centre(birth.X) + ((h1 - 0.5f) * 3f) + (0.6f * MathF.Sin(MathF.Tau * ((age * 1.5f) + h2)));
                y = EntityQuads.Centre(birth.Y) - 0.5f - risen;
                half = 0.8f;
            }
            if (!Sight.Sees(vision, x, y) || quads.IsOffScreen(quads.ScreenX(x - 0.5f), quads.ScreenY(y - 0.5f), (half + 1f) * scale))
                continue;
            quads.Add(x, y, 1f, 0f, -half, half, -half, half, EntityShape.KindFireParticle, age, kind, level, h2);
        }
    }

    private static float Seed(int slot) => ((slot * 0.618034f) + 0.31f) % 1f;

    private static float Hash(int slot, int index, int cycle, int salt)
    {
        uint h = (uint)slot * 0x9E3779B1u;
        h ^= ((uint)index + 0x7F4A7C15u) * 0x85EBCA77u;
        h ^= (uint)cycle * 0xC2B2AE3Du;
        h ^= (uint)salt * 0x27D4EB2Fu;
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        h *= 0x297A2D39u;
        h ^= h >> 15;
        return (h >> 8) * (1f / 16777216f);
    }
}
