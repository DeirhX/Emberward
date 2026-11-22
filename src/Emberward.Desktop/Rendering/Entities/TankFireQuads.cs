namespace Emberward.Desktop.Rendering.Entities;

using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Rendering;
using Emberward.Core.Sensing;
using Emberward.Core.Types;

/// <summary>
/// The fire on a tank whose hull is damaged (DESIGN.md §4.7): a flickering light on the ground under it, the exposed
/// core glowing through a soot ring at the hull's centre, and the fire itself as particles (<see cref="FireParticles"/>):
/// flame licks rising off the core and the hull, sparks shooting up, cinders that arc off and fall, and smoke, all born
/// where the tank was, so a moving tank's fire streams out behind it. How big it is comes from
/// <see cref="Emberward.Core.Entities.Tanks.Tank.FireLevel"/> and the hull and shield Core answers; this only draws.
/// Each part eases in from nothing as the level rises (<see cref="FireOnset"/>), and the drawn level, hull and shield
/// follow Core's over <see cref="FireOnset.FollowSeconds"/>, so a hit or a repair never snaps the fire. When a tank
/// dies its hot core flares from yellow to white and fades (<see cref="DeathSeconds"/>, timed here on the steps
/// captured). A tank that is not burning costs nothing but its step trail. Output only.
/// </summary>
/// <remarks>
/// The particles keep no pool: each of a tank's particles is a slot on a loop of its own life, born where the tank was
/// that long ago (the trail of its last <see cref="TrailSteps"/> step positions), so a moving tank leaves a trail that
/// stays where it was made, and nothing is allocated per step or per frame. A slot is in the air in a life when its
/// hash falls under the level's share (<see cref="FireParticles.Share"/>), fading in as the share passes it, so the
/// count, and with it the birth rate, grows smoothly with the fire. Their clock is the steps this client has captured,
/// so they are step-locked: they stand still while the world does. They use no wall clock or shared random source, but
/// two clients that joined at different steps show different particles (output only, never compared).
/// </remarks>
public sealed class TankFireQuads
{
    /// <summary>How many of each tank's step positions the trail keeps (four seconds at 24 steps a second).</summary>
    public const int TrailSteps = 96;

    /// <summary>A tank that moved further than this in one step was respawned: its trail starts again where it is.</summary>
    public const float RespawnJumpCells = 20f;

    /// <summary>How long the glow of a dead tank's core lasts: a flare from yellow to white, then a fade (seconds).</summary>
    public const float DeathSeconds = DeathFlareSeconds + DeathFadeSeconds;

    /// <summary>How long a dead tank's core takes to flare from yellow to white-hot (seconds).</summary>
    public const float DeathFlareSeconds = 0.3f;

    /// <summary>How much longer than it is wide a flame lick is drawn, along the way it rises.</summary>
    public const float FlameStretch = 1.8f;

    /// <summary>
    /// The most quads one tank's fire adds to a frame: every particle slot, its light and core, and the light and glow
    /// of a death still fading where it died.
    /// </summary>
    public static int MaxQuadsPerTank => FireParticles.MaxPerTank + 4;

    private const float DeathFadeSeconds = 1.5f;
    private const float DeathPeakGlow = 2f;
    private const float PhaseCycleSeconds = 4f;
    private const float SagHullFraction = 0.25f;
    private const float ShareFade = 12f;

    // Per tank slot (the world's tank list index). Level, hull and shield are as drawn: following Core's.
    private float[] _levels = [];
    private float[] _hull = [];
    private float[] _shield = [];
    private Position[] _trail = [];
    private int[] _trailHead = [];
    private bool[] _trailKnown = [];
    private bool[] _alive = [];
    private long[] _diedStep = [];          // the capture a tank was seen to die at (0: no death glow drawn)
    private float[] _deathGlow = [];        // its core's glow just before it died
    private float[] _deathLevel = [];       // and its fire level, which the particles then in the air keep
    private float[] _deathHull = [];        // and its hull, which sizes the core those particles rise from
    private Position[] _deathAt = [];
    private float[] _deathHeading = [];
    private int _count;

    // Steps captured: the particles' clock, so they stand still while the world does (a pause, a network stall) and
    // keep pace with it when it catches up. Counted in a long and read in doubles, so it never wraps.
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

    /// <summary>Forgets every trail: the next capture starts each tank afresh where it is.</summary>
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
            Array.Resize(ref _alive, count);
            Array.Resize(ref _diedStep, count);
            Array.Resize(ref _deathGlow, count);
            Array.Resize(ref _deathLevel, count);
            Array.Resize(ref _deathHull, count);
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
            _deathHull[slot] = _hull[slot];
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
        // A new tank, or one that respawned: its trail starts where it is now.
        trail.Fill(position);
        _trailHead[slot] = 0;
        _trailKnown[slot] = true;
        return false;
    }

    /// <summary>Where tank <paramref name="slot"/> was <paramref name="stepsAgo"/> steps ago (as far back as the trail goes).</summary>
    public Position TrailPosition(int slot, int stepsAgo)
    {
        int back = Math.Clamp(stepsAgo, 0, TrailSteps - 1);
        return _trail[(slot * TrailSteps) + ((_trailHead[slot] - back + TrailSteps) % TrailSteps)];
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
    /// The core on each burning hull and its fire's particles: drawn over the tanks (call after <see cref="TankQuads"/>,
    /// which turns the hulls this draws on). Smoke goes under the core; flame licks, sparks and cinders over it.
    /// </summary>
    public void Emit(EntityQuads quads, EntityMotion motion, VisionMap? vision)
    {
        ReadOnlySpan<TankRenderState> tanks = motion.Tanks;
        float scale = quads.View.PixelScale;
        float t = quads.Phase % PhaseCycleSeconds;
        double clock = ParticleClock(quads.Alpha);
        ReadOnlySpan<FireParticleFamily> families = FireParticles.Families;
        for (int i = 0; i < tanks.Length && i < _count; i++)
        {
            float level = _levels[i];
            float heading = motion.HullHeading(i);
            EmitDeath(quads, vision, i, tanks[i].IsDead ? heading : float.NaN, clock, t, scale);
            if (!IsDrawn(tanks[i], level, vision))
                continue;

            var (start, drift) = motion.TankMotion(i);
            for (int f = 0; f < FireParticles.UnderCore; f++)
                EmitParticles(quads, vision, i, level, clock, start, drift, families[f]);

            if (!quads.IsOffScreen(quads.ScreenX(start.X), quads.ScreenY(start.Y), 9f * scale))
            {
                // The core's glow reaches 6 cells round it, and a sagging core's drips run further down the screen.
                float below = _hull[i] < SagHullFraction ? 8f : 6.5f;
                // A dead tank's core is the death glow now; its soot dies down here.
                float core = tanks[i].IsDead ? 0f : CoreGlow(i);
                quads.Add(EntityQuads.Centre(start.X), EntityQuads.Centre(start.Y), 1f, 0f, -6.5f, 6.5f, -6.5f, below,
                    _hull[i], core, FireOnset.Soot(level), EntityShape.KindTankFire, FireOnset.Flames(level), t,
                    Seed(i), heading, drift);
            }

            for (int f = FireParticles.UnderCore; f < families.Length; f++)
                EmitParticles(quads, vision, i, level, clock, start, drift, families[f]);
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

    /// <summary>A dead tank's core glow, where it died, for <see cref="DeathSeconds"/>: no soot, no crust.</summary>
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
    /// The world takes no step (a pause, or a network tick that carried none): the particles stand where they are
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

    // A dead tank's fire still dies down where it fell, and its particles drift off as the level falls.
    private static bool IsDrawn(in TankRenderState tank, float level, VisionMap? vision) =>
        level > 0f && Sight.Sees(vision, tank.Position);

    /// <summary>
    /// One family of particles for tank <paramref name="slot"/>. Particle k of n is on a loop of its life L, at age
    /// <c>frac(clock / L + k / n)</c>, the clock being the steps captured plus the frame's progress through the next;
    /// it was born where the tank was <c>age * L</c> ago and has moved since (<see cref="FireParticles.Offset"/>). It is
    /// in the air in this life when its hash is under the level's share, and fades in as the share passes the hash.
    /// Once the tank has died, those born before live out their lives at the level it died at, and no more are born.
    /// </summary>
    private void EmitParticles(EntityQuads quads, VisionMap? vision, int slot, float level, double clock,
        PositionF start, PositionF drift, in FireParticleFamily family)
    {
        long died = _alive[slot] ? 0 : _diedStep[slot];
        float hull = _hull[slot];
        if (died != 0)
        {
            level = _deathLevel[slot];
            hull = _deathHull[slot];
        }
        FireParticleKind kind = family.Kind;
        float share = FireParticles.Share(kind, level);
        if (share <= 0f)
            return;
        float life = family.LifeSeconds;
        int count = family.Count;
        float strength = kind == FireParticleKind.Smoke ? 0.4f + (0.6f * level) : 1f;
        float coreRadius = FireParticles.CoreRadius(hull);
        double lifeSteps = life * Tweaks.Perf.TargetFps;
        float scale = quads.View.PixelScale;
        for (int k = 0; k < count; k++)
        {
            double loop = (clock / lifeSteps) + ((double)k / count);
            double cycleStart = Math.Floor(loop);
            float age = (float)(loop - cycleStart);
            int cycle = unchecked((int)(long)cycleStart);
            if (died != 0 && clock - (age * lifeSteps) >= died)
                continue;
            float shown = Math.Clamp((share - Hash(slot, k, cycle, family.Salt)) * ShareFade, 0f, 1f);
            if (shown <= 0f)
                continue;
            PositionF birth = BirthPosition(slot, clock - (age * lifeSteps), start, drift);
            var h = new FireParticles.Hashes(Hash(slot, k, cycle, family.Salt + 1), Hash(slot, k, cycle, family.Salt + 2),
                Hash(slot, k, cycle, family.Salt + 3), Hash(slot, k, cycle, family.Salt + 4),
                Hash(slot, k, cycle, family.Salt + 5), Hash(slot, k, cycle, family.Salt + 6));
            float seconds = age * life;
            PositionF offset = FireParticles.Offset(kind, seconds, level, coreRadius, h);
            float x = EntityQuads.Centre(birth.X) + offset.X, y = EntityQuads.Centre(birth.Y) + offset.Y;
            float size = FireParticles.Size(kind, age, level, h.D);
            float reach = kind switch
            {
                FireParticleKind.Flame => 1.5f * size * FlameStretch,
                FireParticleKind.Smoke => 1.3f * size,
                _ => (3.5f * size) + 1.2f,
            };
            if (!Sight.Sees(vision, x, y)
                || quads.IsOffScreen(quads.ScreenX(x - 0.5f), quads.ScreenY(y - 0.5f), (reach + 1f) * scale))
                continue;
            AddParticle(quads, kind, x, y, age, size, strength * shown, seconds, level, coreRadius, h);
        }
    }

    /// <summary>
    /// Where tank <paramref name="slot"/> was drawn at step time <paramref name="born"/> (the steps captured, fractional):
    /// during the step being drawn, where the drawn tank was then (<paramref name="start"/> plus <paramref name="drift"/>
    /// times the progress), so a fresh particle is born under the drawn hull and never ahead of it; before that, on its
    /// trail, which the drawn tank follows about a step behind the latest capture.
    /// </summary>
    public PositionF BirthPosition(int slot, double born, PositionF start, PositionF drift)
    {
        double into = born - _steps;
        if (into >= 0)
        {
            float a = (float)Math.Min(into, 1.0);
            return new PositionF(start.X + (drift.X * a), start.Y + (drift.Y * a));
        }
        double back = 1.0 - into;                    // steps behind the latest capture, a step of lag included
        int n = (int)Math.Floor(back);
        float f = (float)(back - n);
        Position near = TrailPosition(slot, n), far = TrailPosition(slot, n + 1);
        return new PositionF(near.X + ((far.X - near.X) * f), near.Y + ((far.Y - near.Y) * f));
    }

    // One particle's quad. A smoke puff is upright; a lick, a spark and a cinder turn their local +x along their motion.
    private static void AddParticle(EntityQuads quads, FireParticleKind kind, float x, float y, float age, float size,
        float strength, float seconds, float level, float coreRadius, in FireParticles.Hashes h)
    {
        float particle = (float)kind;
        if (kind == FireParticleKind.Smoke)
        {
            float half = 1.3f * size;
            quads.Add(x, y, 1f, 0f, -half, half, -half, half, 0f, size, 0f, EntityShape.KindFireParticle, age, particle,
                strength, h.Seed);
            return;
        }
        var (dir, speed) = FireParticles.Motion(kind, seconds, level, coreRadius, h);
        if (kind == FireParticleKind.Flame)
        {
            float along = 1.5f * size * FlameStretch, across = 1.5f * size;
            quads.Add(x, y, dir.X, dir.Y, -along, along, -across, across, FlameStretch, size, h.Swirl,
                EntityShape.KindFireParticle, age, particle, strength, h.Seed);
            return;
        }
        // A spark or a cinder: a hot head with a streak behind it, as long as it is fast.
        float trail = Math.Clamp(speed * 0.045f, 0.1f, kind == FireParticleKind.Spark ? 1.2f : 0.9f);
        float glow = 3.5f * size;
        float heat = kind == FireParticleKind.Spark ? 1f : 0.75f;
        quads.Add(x, y, dir.X, dir.Y, -trail - glow, glow, -glow, glow, trail, size, heat,
            EntityShape.KindFireParticle, age, particle, strength, h.Seed);
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
