namespace Emberward.Desktop.Rendering.Entities;

using Emberward.Core.Types;

/// <summary>
/// The kinds of particle a burning tank throws (params.y of <see cref="EntityShape.KindFireParticle"/>; the shader's
/// kFireSparkKind, kFireSmokeKind, kFireFlameKind and kFireCinderKind in Entity/TankFire.hlsli).
/// </summary>
public enum FireParticleKind
{
    /// <summary>A small hot spark that shoots up off the hull: a short streak cooling from white to red.</summary>
    Spark = 0,

    /// <summary>A puff of smoke, swelling and darkening as it rises.</summary>
    Smoke = 1,

    /// <summary>A flame lick: a soft glowing tongue that rises off the core and the hull, cooling to smoke as it dies.</summary>
    Flame = 2,

    /// <summary>A larger ember thrown off the core that arcs up, then falls, glowing deep red.</summary>
    Cinder = 3,
}

/// <summary>
/// One family of a burning tank's particles: how many slots each tank keeps for it, how long each lives, and the salt of
/// its hashes. A slot's life is a loop (see <see cref="TankFireQuads"/>), so the family's birth rate is
/// <c>Count / LifeSeconds</c> times its share of slots in the air.
/// </summary>
public readonly record struct FireParticleFamily(FireParticleKind Kind, int Count, float LifeSeconds, int Salt);

/// <summary>
/// Where a burning tank's particles are and how big, at a time in their lives: rendering only, a pure function of the
/// particle's hashes, its age and the fire level, so the particles are step-locked and the same on every machine. Each
/// is born on or around the hull (a flame lick mostly on the exposed core) and rises with a little turbulence; sparks
/// shoot up and slow, cinders arc up and fall, smoke drifts up and spreads. Positions are offsets in cells from the
/// tank's centre where the particle was born (screen +y is down).
/// </summary>
public static class FireParticles
{
    /// <summary>A particle's four hashes (0..1), fixed for its life.</summary>
    public readonly record struct Hashes(float A, float B, float C, float D);

    /// <summary>The families, in the order they are drawn: smoke under the core, the flames, sparks and cinders over it.</summary>
    public static readonly FireParticleFamily[] Families =
    [
        new(FireParticleKind.Smoke, 16, 1.8f, 71),
        new(FireParticleKind.Flame, 30, 0.5f, 53),
        new(FireParticleKind.Spark, 18, 0.75f, 83),
        new(FireParticleKind.Cinder, 4, 1.4f, 97),
    ];

    /// <summary>The families drawn before the core (under it): the first <see cref="UnderCore"/> of <see cref="Families"/>.</summary>
    public const int UnderCore = 1;

    /// <summary>How hard a cinder falls (cells per second squared).</summary>
    public const float CinderGravity = 16f;

    private const float HullRadius = 2.2f;
    private const float FlameBuoyancy = 10f;
    private const float SparkDrag = 2.2f;
    private const float SmokeLifeSeconds = 1.8f;
    private const float ProbeSeconds = 0.02f;

    /// <summary>Every family's slots together: the most particles one tank can have in the air.</summary>
    public static int MaxPerTank
    {
        get
        {
            int total = 0;
            foreach (FireParticleFamily family in Families)
                total += family.Count;
            return total;
        }
    }

    /// <summary>The share of a family's slots in the air at fire level <paramref name="level"/>.</summary>
    public static float Share(FireParticleKind kind, float level) => kind switch
    {
        FireParticleKind.Flame => FireOnset.FlameShare(level),
        FireParticleKind.Smoke => FireOnset.SmokeShare(level),
        _ => FireOnset.EmberShare(level),
    };

    /// <summary>
    /// Where a particle is <paramref name="seconds"/> into its life, as an offset from the tank's centre when it was born,
    /// burning at <paramref name="level"/>; <paramref name="coreRadius"/> is the exposed core's radius (cells), which most
    /// flame licks rise from.
    /// </summary>
    public static PositionF Offset(FireParticleKind kind, float seconds, float level, float coreRadius, in Hashes h)
    {
        float t = Math.Max(seconds, 0f);
        switch (kind)
        {
            case FireParticleKind.Flame:
            {
                // Mostly off the exposed core, the rest off the hull round it; rising faster as it goes (buoyant), and
                // swirling side to side.
                bool onCore = h.C < 0.6f;
                PositionF at = Disc(onCore ? Math.Max(coreRadius * 0.8f, 0.6f) : HullRadius, h.A, h.B);
                float rise = (3f + (3f * h.D)) * (0.7f + (0.6f * level));
                float drift = (h.B - 0.5f) * 1.5f * t;
                float swirl = 0.4f * MathF.Sin(MathF.Tau * ((2.25f * t) + h.C)) * Math.Min(t * 3f, 1f);
                return new PositionF(at.X + drift + swirl, at.Y - (rise * t) - (0.5f * FlameBuoyancy * t * t));
            }
            case FireParticleKind.Spark:
            {
                // Shot up off the hull, slowed by the air, still lifted by the heat, jinking side to side.
                PositionF at = Disc(HullRadius * 0.9f, h.A, h.B);
                float speed = 0.75f + (0.5f * level);
                float vx = (h.B - 0.5f) * 7f * speed, vy = -(5f + (7f * h.D)) * speed;
                float slowed = (1f - MathF.Exp(-SparkDrag * t)) / SparkDrag;
                float jink = 0.5f * MathF.Sin(MathF.Tau * ((3f * t) + h.C)) * Math.Min(t * 2f, 1f);
                return new PositionF(at.X + (vx * slowed) + jink, at.Y + (vy * slowed) - (3f * t));
            }
            case FireParticleKind.Cinder:
            {
                // Thrown off the core to one side and up, then down under gravity, past where it started.
                PositionF at = Disc(1f, h.A, h.B);
                float side = h.C < 0.5f ? -1f : 1f;
                float vx = side * (2.5f + (4f * h.B)), vy = -(7f + (4f * h.D));
                return new PositionF(at.X + (vx * t), at.Y + (vy * t) + (0.5f * CinderGravity * t * t));
            }
            default:
            {
                // Smoke: rises above the hull and spreads as it goes.
                float age = t / SmokeLifeSeconds;
                return new PositionF(((h.A - 0.5f) * 2f) + ((h.B - 0.5f) * 2.5f * age), -0.5f - (4f * t));
            }
        }
    }

    /// <summary>
    /// The way a particle is moving <paramref name="seconds"/> into its life (a unit vector; straight up the screen when
    /// it stands still) and how fast (cells per second).
    /// </summary>
    public static (PositionF Direction, float Speed) Motion(FireParticleKind kind, float seconds, float level,
        float coreRadius, in Hashes h)
    {
        PositionF a = Offset(kind, seconds, level, coreRadius, h);
        PositionF b = Offset(kind, seconds + ProbeSeconds, level, coreRadius, h);
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        return length < 1e-5f
            ? (new PositionF(0f, -1f), 0f)
            : (new PositionF(dx / length, dy / length), length / ProbeSeconds);
    }

    /// <summary>
    /// A particle's size (cells: a flame lick's radius across its motion, a spark's or cinder's hot head, a smoke puff's
    /// radius) at <paramref name="age"/> (0 born, 1 gone) of its life, at fire level <paramref name="level"/>; licks and
    /// sparks grow with the fire.
    /// </summary>
    public static float Size(FireParticleKind kind, float age, float level, float hash) => kind switch
    {
        FireParticleKind.Flame => (0.45f + (0.85f * level)) * (0.7f + (0.6f * hash)) * (1f - (0.45f * age)),
        FireParticleKind.Spark => 0.12f + (0.08f * level),
        FireParticleKind.Cinder => 0.26f + (0.12f * hash),
        _ => 0.5f * (1.5f + (3f * age)),
    };

    /// <summary>The exposed core's radius (cells) at hull fill <paramref name="hull"/>, as Entity/TankFire.hlsli sizes it.</summary>
    public static float CoreRadius(float hull) => 1.5f + (2f * Math.Clamp((0.75f - hull) * 1.333333f, 0f, 1f));

    // A point in a disc of the given radius, flattened to the hull's top-down footprint, from two hashes.
    private static PositionF Disc(float radius, float h1, float h2)
    {
        float r = radius * MathF.Sqrt(h1);
        float angle = MathF.Tau * h2;
        return new PositionF(r * MathF.Cos(angle), r * 0.7f * MathF.Sin(angle));
    }
}
