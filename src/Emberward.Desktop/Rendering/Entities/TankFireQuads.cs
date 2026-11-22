namespace Emberward.Desktop.Rendering.Entities;

using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Rendering;
using Emberward.Core.Sensing;
using Emberward.Core.Types;

/// <summary>
/// The fire on a tank whose hull is damaged (DESIGN.md §4.7): a flickering light on the ground under it, the exposed
/// core glowing through a soot ring at the hull's centre, flame tongues that lean away from the way it drives, and
/// smoke and embers that rise from where it has been. How big it is comes from
/// <see cref="Emberward.Core.Entities.Tanks.Tank.FireLevel"/> and the hull and shield Core answers; this only draws.
/// Each part eases in from nothing as the level rises (<see cref="FireOnset"/>), and the drawn level, hull and shield
/// follow Core's over <see cref="FireOnset.FollowSeconds"/>, so a hit or a repair never snaps the fire. When a tank
/// dies its hot core flares from yellow to white and fades (<see cref="DeathSeconds"/>, timed here on the steps
/// captured). A tank that is not burning costs nothing but its step trail. Output only.
/// </summary>
/// <remarks>
/// Smoke and embers keep no particle pool: each of a tank's particles is a slot on a loop of its own life, born where
/// the tank was that long ago (the trail of its last <see cref="TrailSteps"/> step positions), so a moving tank leaves
/// a trail that stays where it was made, and nothing is allocated per step or per frame. A slot is in the air in a
/// life when its hash falls under the level's share (<see cref="FireOnset.SmokeShare"/>), fading in as the share
/// passes it, so the count grows smoothly with the fire.
/// </remarks>
public sealed class TankFireQuads
{
    /// <summary>How many of each tank's step positions the trail keeps (four seconds at 24 steps a second).</summary>
    public const int TrailSteps = 96;

    /// <summary>A tank that moved further than this in one step was respawned: its trail starts again where it is.</summary>
    public const float RespawnJumpCells = 20f;

    /// <summary>How long the flames take to lean most of the way into a new drift (seconds; exponential).</summary>
    public const float FlameLeanSeconds = 0.25f;

    /// <summary>Smoke slots per tank: the most smoke puffs a fully burning tank keeps in the air.</summary>
    public const int MaxSmoke = 20;

    /// <summary>Ember slots per tank.</summary>
    public const int MaxEmbers = 16;

    /// <summary>How long the glow of a dead tank's core lasts: a flare from yellow to white, then a fade (seconds).</summary>
    public const float DeathSeconds = DeathFlareSeconds + DeathFadeSeconds;

    /// <summary>How long a dead tank's core takes to flare from yellow to white-hot (seconds).</summary>
    public const float DeathFlareSeconds = 0.3f;

    private const float DeathFadeSeconds = 1.5f;
    private const float DeathPeakGlow = 2f;
    private const float PhaseCycleSeconds = 4f;
    private const float SagHullFraction = 0.25f;
    private const float SmokeLifeSeconds = 1.6f;
    private const float SmokeRiseCellsPerSecond = 4f;
    private const float EmberLifeSeconds = 0.7f;
    private const float EmberRiseCellsPerSecond = 7f;
    private const float ShareFade = 12f;
    private const float MaxLeanDrift = 1.2f;
    private const float ParticleSmoke = 1f;
    private const float ParticleEmber = 0f;

    // Per tank slot (the world's tank list index). Level, hull and shield are as drawn: following Core's.
    private float[] _levels = [];
    private float[] _hull = [];
    private float[] _shield = [];
    private Position[] _trail = [];
    private int[] _trailHead = [];
    private bool[] _trailKnown = [];
    private PositionF[] _lean = [];
    private bool[] _alive = [];
    private long[] _diedStep = [];          // the capture a tank was seen to die at (0: no death glow drawn)
    private float[] _deathGlow = [];        // its core's glow just before it died
    private float[] _deathLevel = [];       // and its fire level, which the smoke and embers then in the air keep
    private Position[] _deathAt = [];
    private float[] _deathHeading = [];
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
        {
            var tank = tanks[i];
            CaptureTank(i, tank.Position, tank.IsDead ? 0f : tank.FireLevel, tank.HullFraction,
                world.TankShieldOf(tank).ShieldFill, tank.IsDead);
        }
    }

    /// <summary>Forgets every trail and lean: the next capture starts each tank afresh where it is.</summary>
    public void Reset()
    {
        Array.Clear(_trailKnown);
        Array.Clear(_levels);
        Array.Clear(_diedStep);
        _count = 0;
    }

    /// <summary>Starts a step's capture of <paramref name="count"/> tanks (then <see cref="CaptureTank"/> for each).</summary>
    public void BeginCapture(int count)
    {
        if (_levels.Length < count)
        {
            int old = _levels.Length;
            Array.Resize(ref _levels, count);
            Array.Resize(ref _hull, count);
            Array.Resize(ref _shield, count);
            Array.Resize(ref _trail, count * TrailSteps);
            Array.Resize(ref _trailHead, count);
            Array.Resize(ref _trailKnown, count);
            Array.Resize(ref _lean, count);
            Array.Resize(ref _alive, count);
            Array.Resize(ref _diedStep, count);
            Array.Resize(ref _deathGlow, count);
            Array.Resize(ref _deathLevel, count);
            Array.Resize(ref _deathAt, count);
            Array.Resize(ref _deathHeading, count);
            for (int i = old; i < count; i++)
                _trailKnown[i] = false;
        }
        for (int i = count; i < _count; i++)
        {
            _trailKnown[i] = false;
            _diedStep[i] = 0;
        }
        _count = count;
        _steps++;
        _held = false;
    }

    /// <summary>
    /// Records tank <paramref name="slot"/>'s position and fire level (0: not burning) after this step, with its hull
    /// and shield fill (0 to 1) and whether it is dead. The drawn values follow these (a dead tank's fire dies down the
    /// same way); a new or respawned tank's are taken at once. A tank seen alive and then dead where it was starts its
    /// death glow here (a slot that jumped holds another tank, or a respawn, and starts none).
    /// </summary>
    public void CaptureTank(int slot, Position position, float level, float hull = 1f, float shieldFill = 0f, bool dead = false)
    {
        bool continuous = Advance(slot, position);
        if (dead && continuous && _alive[slot])
        {
            _diedStep[slot] = _steps;
            _deathAt[slot] = position;
            _deathGlow[slot] = CoreGlow(slot);
            _deathLevel[slot] = _levels[slot];
        }
        _alive[slot] = !dead;
        float follow = continuous ? FireOnset.StepFollow : 1f;
        _levels[slot] += (Math.Clamp(level, 0f, 1f) - _levels[slot]) * follow;
        if (_levels[slot] < 0.001f)
            _levels[slot] = 0f;     // a fire put out ends, rather than burning on at a vanishing level

        _hull[slot] += (Math.Clamp(hull, 0f, 1f) - _hull[slot]) * follow;
        _shield[slot] += (Math.Clamp(shieldFill, 0f, 1f) - _shield[slot]) * follow;
    }

    /// <summary>The fire level tank <paramref name="slot"/> is drawn at (following Core's).</summary>
    public float DrawnLevel(int slot) => _levels[slot];

    /// <summary>Every tank's drawn fire level, indexed like the world's tank list.</summary>
    public ReadOnlySpan<float> DrawnLevels => _levels.AsSpan(0, _count);

    // Adds the position to the trail; false when the trail started again (a new tank, or a respawn jump).
    private bool Advance(int slot, Position position)
    {
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
                return true;
            }
        }
        // A new tank, or one that respawned: its trail starts where it is now, and its flames stand straight.
        trail.Fill(position);
        _trailHead[slot] = 0;
        _trailKnown[slot] = true;
        _lean[slot] = default;
        return false;
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
        double clock = ParticleClock(quads.Alpha);
        for (int i = 0; i < tanks.Length && i < _count; i++)
        {
            if (DeathAge(i, clock) is float age && Sight.Sees(vision, _deathAt[i]))
            {
                // The dying core lights the ground with it as it flares and fades.
                var (glow, hull) = DeathGlow(_deathGlow[i], age);
                AddLight(quads, i, new PositionF(_deathAt[i].X, _deathAt[i].Y), default, 1f, 0.5f * glow, hull, t, scale);
            }
            float level = _levels[i];
            if (!IsDrawn(tanks[i], level, vision))
                continue;
            var (start, drift) = motion.TankMotion(i);
            AddLight(quads, i, start, drift, level, FireOnset.Light(level), _hull[i], t, scale);
        }
    }

    private static void AddLight(EntityQuads quads, int slot, PositionF at, PositionF drift, float level, float strength,
        float hull, float t, float scale)
    {
        float radius = 5f + (7f * level);
        if (strength <= 0f || quads.IsOffScreen(quads.ScreenX(at.X), quads.ScreenY(at.Y), (radius + 2f) * scale))
            return;
        quads.Add(EntityQuads.Centre(at.X), EntityQuads.Centre(at.Y), 1f, 0f, -radius, radius, -radius, radius,
            strength, 0f, 0f, EntityShape.KindTankFireLight, level, t, Seed(slot), hull, drift);
    }

    /// <summary>
    /// The core and flames on each burning hull, and its smoke and embers: drawn over the tanks (call after
    /// <see cref="TankQuads"/>, which turns the hulls this draws on). <paramref name="frameSeconds"/> is the game
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
            EmitDeath(quads, vision, i, tanks[i].IsDead ? motion.HullHeading(i) : float.NaN, clock, t, scale);
            if (!IsDrawn(tanks[i], level, vision))
                continue;

            EmitParticles(quads, vision, i, level, clock, smoke: true);

            if (!quads.IsOffScreen(quads.ScreenX(start.X), quads.ScreenY(start.Y), 16f * scale))
            {
                // The flame frame: local -y points the way the flames go, so the shader draws them straight up.
                PositionF flame = FlameDirection(i);
                float axisX = -flame.Y, axisY = flame.X;
                float heading = MathF.IEEERemainder(motion.HullHeading(i) - MathF.Atan2(axisY, axisX), MathF.Tau);
                // The core's glow reaches 6 cells round it, and a sagging core's drips run further below.
                float below = _hull[i] < SagHullFraction ? 8f : 6.5f;
                // A dead tank's core is the death glow now; its flames and soot die down here.
                float core = tanks[i].IsDead ? 0f : CoreGlow(i);
                quads.Add(EntityQuads.Centre(start.X), EntityQuads.Centre(start.Y), axisX, axisY, -6.5f, 6.5f, -14f, below,
                    _hull[i], core, FireOnset.Soot(level), EntityShape.KindTankFire, FireOnset.Flames(level), t,
                    Seed(i), heading, drift);
            }

            EmitParticles(quads, vision, i, level, clock, smoke: false);
        }
    }

    // The exposed core's glow as drawn: eased in with the fire, hidden behind the shield as it regenerates.
    private float CoreGlow(int slot) => FireOnset.CoreGlow(_levels[slot]) * (1f - _shield[slot]);

    /// <summary>
    /// How long ago (seconds of the particle clock <paramref name="clock"/>) tank <paramref name="slot"/> was seen to die,
    /// while its core's death glow is still drawn; null otherwise.
    /// </summary>
    public float? DeathAge(int slot, double clock)
    {
        long died = _diedStep[slot];
        if (died == 0)
            return null;
        float age = (float)((clock - died) / Tweaks.Perf.TargetFps);
        return age < DeathSeconds ? Math.Max(age, 0f) : null;
    }

    /// <summary>
    /// A dead tank's core glow <paramref name="age"/> seconds after it died, from <paramref name="start"/> (its glow just
    /// before): it flares to twice the full glow and from yellow (the ramp at 5% hull) to white-hot (empty) over
    /// <see cref="DeathFlareSeconds"/>, then fades smoothly to nothing. Hull is the ramp position its colour is at.
    /// </summary>
    public static (float Glow, float Hull) DeathGlow(float start, float age)
    {
        float flare = FireOnset.Ease(0f, DeathFlareSeconds, age);
        float glow = age < DeathFlareSeconds
            ? start + ((DeathPeakGlow - start) * flare)
            : DeathPeakGlow * (1f - FireOnset.Ease(DeathFlareSeconds, DeathSeconds, age));
        return (glow, 0.05f * (1f - flare));
    }

    /// <summary>A dead tank's core glow, where it died, for <see cref="DeathSeconds"/>: no flames, no soot, no crust.</summary>
    private void EmitDeath(EntityQuads quads, VisionMap? vision, int slot, float deadHeading, double clock, float t, float scale)
    {
        if (!float.IsNaN(deadHeading))
            _deathHeading[slot] = deadHeading;
        if (_diedStep[slot] == 0)
            return;
        if (DeathAge(slot, clock) is not float age)
        {
            _diedStep[slot] = 0;
            return;
        }
        Position at = _deathAt[slot];
        if (!Sight.Sees(vision, at) || quads.IsOffScreen(quads.ScreenX(at.X), quads.ScreenY(at.Y), 7f * scale))
            return;
        var (glow, hull) = DeathGlow(_deathGlow[slot], age);
        quads.Add(EntityQuads.Centre(at.X), EntityQuads.Centre(at.Y), 1f, 0f, -6.5f, 6.5f, -6.5f, 6.5f,
            hull, glow, 0f, EntityShape.KindTankFire, 0f, t, Seed(slot), _deathHeading[slot]);
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

    // A dead tank's fire still dies down where it fell, and its smoke and embers drift off as the level falls.
    private static bool IsDrawn(in TankRenderState tank, float level, VisionMap? vision) =>
        level > 0f && Sight.Sees(vision, tank.Position);

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
    /// it was born where the tank was <c>age * L</c> ago and has risen since. It is in the air in this life when its
    /// hash is under the level's share, and fades in as the share passes the hash. Once the tank has died, those born
    /// before live out their lives at the level it died at, and no more are born.
    /// </summary>
    private void EmitParticles(EntityQuads quads, VisionMap? vision, int slot, float level, double clock, bool smoke)
    {
        long died = _alive[slot] ? 0 : _diedStep[slot];
        if (died != 0)
            level = _deathLevel[slot];
        float kind = smoke ? ParticleSmoke : ParticleEmber;
        int count = smoke ? MaxSmoke : MaxEmbers;
        float share = smoke ? FireOnset.SmokeShare(level) : FireOnset.EmberShare(level);
        if (share <= 0f)
            return;
        float life = smoke ? SmokeLifeSeconds : EmberLifeSeconds;
        float rise = smoke ? SmokeRiseCellsPerSecond : EmberRiseCellsPerSecond;
        float strength = smoke ? 0.4f + (0.6f * level) : 1f;
        float stepsPerSecond = Tweaks.Perf.TargetFps;
        double lifeSteps = life * stepsPerSecond;
        float scale = quads.View.PixelScale;
        for (int k = 0; k < count; k++)
        {
            double loop = (clock / lifeSteps) + ((double)k / count);
            double cycleStart = Math.Floor(loop);
            float age = (float)(loop - cycleStart);
            int cycle = unchecked((int)(long)cycleStart);
            if (died != 0 && clock - (age * lifeSteps) >= died)
                continue;
            float shown = Math.Clamp((share - Hash(slot, k, cycle, smoke ? 71 : 83)) * ShareFade, 0f, 1f);
            if (shown <= 0f)
                continue;
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
            quads.Add(x, y, 1f, 0f, -half, half, -half, half, EntityShape.KindFireParticle, age, kind, strength * shown, h2);
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
