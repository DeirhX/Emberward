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

    // Steps captured: the particles' clock, so smoke and embers stand still while the world does (a pause, a network
    // stall) and keep pace with it when it catches up. Counted in a long and read in doubles, so it never wraps.
    private long _steps;

    // The particle clock last drawn, and whether the world is held (no step is coming): the clock never runs back,
    // never runs more than a step past the latest capture, and stands still while held.
    private double _clock;
    private bool _held;

    // The world the trails were recorded in: a new match (or a late join's copy) starts them again.
    private WeakReference<World>? _world;

    /// <summary>Smoke particles a tank burning at <paramref name="level"/> keeps in the air.</summary>
    public static int SmokeCount(float level) => level > 0f ? (int)(6f + 14f * Math.Min(level, 1f)) : 0;

    /// <summary>Embers a tank burning at <paramref name="level"/> throws; none while the fire is small.</summary>
    public static int EmberCount(float level) => level > EmberMinLevel ? (int)(4f + 12f * Math.Min(level, 1f)) : 0;

    /// <summary>Records each tank's fire and position after a simulation step.</summary>
    public void CaptureStep(World world)
    {
        if (_world == null)
            _world = new WeakReference<World>(world);
        else if (!_world.TryGetTarget(out World? last) || !ReferenceEquals(last, world))
        {
            Reset();
            _world.SetTarget(world);
        }
        var tanks = world.TankList.Tanks;
        BeginCapture(tanks.Count);
        for (int i = 0; i < tanks.Count; i++)
            CaptureTank(i, tanks[i].Position, tanks[i].IsDead ? 0f : tanks[i].FireLevel);
    }

    /// <summary>Forgets every trail and lean: the next capture starts each tank afresh where it is.</summary>
    public void Reset()
    {
        Array.Clear(_trailKnown);
        Array.Clear(_levels);
        _count = 0;
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
        _steps++;
        _held = false;
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
        float t = quads.Phase % PhaseCycleSeconds;
        double clock = ParticleClock(quads.Alpha);
        float follow = snap ? 1f : 1f - MathF.Exp(-Math.Max(0f, frameSeconds) / FlameLeanSeconds);
        for (int i = 0; i < tanks.Length && i < _count; i++)
        {
            float level = _levels[i];
            var (start, drift) = motion.TankMotion(i);
            LeanInto(i, drift, follow);
            if (!IsDrawn(tanks[i], level, vision))
                continue;

            EmitParticles(quads, vision, i, level, clock, smoke: true);

            if (!quads.IsOffScreen(quads.ScreenX(start.X), quads.ScreenY(start.Y), 16f * scale))
            {
                // The flame frame: local -y points the way the flames go, so the shader draws them straight up.
                PositionF flame = FlameDirection(i);
                float axisX = -flame.Y, axisY = flame.X;
                float heading = MathF.IEEERemainder(motion.HullHeading(i) - MathF.Atan2(axisY, axisX), MathF.Tau);
                quads.Add(EntityQuads.Centre(start.X), EntityQuads.Centre(start.Y), axisX, axisY, -5.5f, 5.5f, -14f, 4.5f,
                    EntityShape.KindTankFire, level, t, Seed(i), heading, drift);
            }

            EmitParticles(quads, vision, i, level, clock, smoke: false);
        }
    }

    /// <summary>
    /// The world takes no step (a pause, or a network tick that carried none): smoke and embers stand where they are
    /// drawn now until the next capture, whatever the frames' alpha does meanwhile.
    /// </summary>
    public void Hold() => _held = true;

    /// <summary>
    /// The particles' clock (steps) for a frame drawn at <paramref name="alpha"/> of the step after the latest capture:
    /// never behind the last frame's, never past the next step, and fixed while held.
    /// </summary>
    public double ParticleClock(float alpha)
    {
        if (!_held)
            _clock = Math.Min(Math.Max(_steps + Math.Clamp(alpha, 0f, 1f), _clock), _steps + 1.0);
        return _clock;
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
    /// <c>frac(clock / L + k / n)</c>, the clock being the steps captured plus the frame's progress through the next;
    /// it was born where the tank was <c>age * L</c> ago and has risen since.
    /// </summary>
    private void EmitParticles(EntityQuads quads, VisionMap? vision, int slot, float level, double clock, bool smoke)
    {
        float kind = smoke ? ParticleSmoke : ParticleEmber;
        int count = smoke ? SmokeCount(level) : EmberCount(level);
        float life = smoke ? SmokeLifeSeconds : EmberLifeSeconds;
        float rise = smoke ? SmokeRiseCellsPerSecond : EmberRiseCellsPerSecond;
        float stepsPerSecond = Tweaks.Perf.TargetFps;
        double lifeSteps = life * stepsPerSecond;
        float scale = quads.View.PixelScale;
        for (int k = 0; k < count; k++)
        {
            double loop = (clock / lifeSteps) + ((double)k / count);
            double cycleStart = Math.Floor(loop);
            float age = (float)(loop - cycleStart);
            int cycle = unchecked((int)(long)cycleStart);
            // The frame is drawn alpha of a step after the latest step, so that much less of the trail lies behind it.
            int stepsAgo = (int)Math.Round((age * lifeSteps) - (clock - _steps));
            Position birth = TrailPosition(slot, stepsAgo);
            float h1 = Hash(slot, k, cycle, smoke ? 17 : 29);
            float h2 = Hash(slot, k, cycle, smoke ? 43 : 61);
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
