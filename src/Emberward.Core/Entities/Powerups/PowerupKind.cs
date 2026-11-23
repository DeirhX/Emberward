namespace Emberward.Core.Entities.Powerups;

using Emberward.Core.Config;

/// <summary>What a powerup drop gives the tank that takes it (DESIGN.md §6.8).</summary>
public enum PowerupKind : byte
{
    /// <summary>At once: the hull whole, the shield full (when the rules run one) and the tank cooled to the ground's rest temperature.</summary>
    Overhaul,

    /// <summary>For <see cref="Tweaks.Powerups.OverchargeSeconds"/>: the tank's shells deal <see cref="Tweaks.Powerups.OverchargeDamageFactor"/> times their damage.</summary>
    Overcharge,

    /// <summary>For <see cref="Tweaks.Powerups.BulwarkSeconds"/>: the tank takes one part in <see cref="Tweaks.Powerups.BulwarkDamageDivisor"/> of all damage.</summary>
    Bulwark,

    /// <summary>For <see cref="Tweaks.Powerups.DrillSeconds"/>: the tank digs soil at the speed it drives open ground.</summary>
    Drill,
}

/// <summary>What every host knows of the kinds without asking a world: how many there are and how long each lasts.</summary>
public static class PowerupKinds
{
    /// <summary>How many kinds there are; a kind's number is below it.</summary>
    public const int Count = 4;

    /// <summary>Ticks <paramref name="kind"/>'s effect runs once taken; 0 for a kind that acts at once.</summary>
    public static int DurationTicks(PowerupKind kind) => kind switch
    {
        PowerupKind.Overcharge => Tweaks.Powerups.OverchargeSeconds * Tweaks.Perf.TargetFps,
        PowerupKind.Bulwark => Tweaks.Powerups.BulwarkSeconds * Tweaks.Perf.TargetFps,
        PowerupKind.Drill => Tweaks.Powerups.DrillSeconds * Tweaks.Perf.TargetFps,
        _ => 0,
    };

    /// <summary>Seconds <paramref name="kind"/>'s effect runs once taken; 0 for a kind that acts at once.</summary>
    public static float DurationSeconds(PowerupKind kind) => DurationTicks(kind) / (float)Tweaks.Perf.TargetFps;

    /// <summary>Whether <paramref name="kind"/> runs for a time (true) or acts at once and is over (false).</summary>
    public static bool IsTimed(PowerupKind kind) => DurationTicks(kind) > 0;
}

/// <summary>
/// What a tank's running powerups change about it this tick, as <see cref="Tanks.Tank.Advance"/> reads it:
/// <see cref="Drill"/> lets a dig and the step into the dug ground share a tick, and a shell it fires deals
/// <see cref="ShellDamageFactor"/> times its damage. (What they take off a hit is asked per hit,
/// <see cref="PowerupDrops.ReduceDamage"/>.)
/// </summary>
public readonly record struct PowerupEffects(bool Drill, int ShellDamageFactor)
{
    /// <summary>No powerup running.</summary>
    public static readonly PowerupEffects None = new(false, 1);
}

/// <summary>
/// The next drop, known to every side wherever it looks: where it lands, what it is and how long until it does.
/// <see cref="Active"/> is false (and the rest zero) when the match rules run no drops.
/// </summary>
public readonly record struct PowerupForecast(bool Active, Types.Position Site, PowerupKind Kind, int TicksUntilLanding)
{
    /// <summary>Seconds until the drop lands.</summary>
    public float SecondsUntilLanding => TicksUntilLanding / (float)Tweaks.Perf.TargetFps;
}

/// <summary>
/// A tank's running timed powerups as the HUD and the renderer read them: ticks left of each, 0 for one that is not
/// running. <see cref="Active"/> is false (and the rest zero) when the match rules run no drops.
/// </summary>
public readonly record struct TankPowerupView(bool Active, int OverchargeTicks, int BulwarkTicks, int DrillTicks)
{
    /// <summary>Whether any timed powerup is running on the tank.</summary>
    public bool Any => OverchargeTicks > 0 || BulwarkTicks > 0 || DrillTicks > 0;

    /// <summary>Ticks left of <paramref name="kind"/> on the tank; 0 when it is not running or acts at once.</summary>
    public int TicksLeft(PowerupKind kind) => kind switch
    {
        PowerupKind.Overcharge => OverchargeTicks,
        PowerupKind.Bulwark => BulwarkTicks,
        PowerupKind.Drill => DrillTicks,
        _ => 0,
    };

    /// <summary>Whether <paramref name="kind"/> is running on the tank.</summary>
    public bool IsRunning(PowerupKind kind) => TicksLeft(kind) > 0;

    /// <summary>Seconds left of <paramref name="kind"/> on the tank.</summary>
    public float SecondsLeft(PowerupKind kind) => TicksLeft(kind) / (float)Tweaks.Perf.TargetFps;

    /// <summary>The share of <paramref name="kind"/>'s full time that is left, 0 to 1.</summary>
    public float FractionLeft(PowerupKind kind) =>
        PowerupKinds.DurationTicks(kind) is > 0 and int full ? TicksLeft(kind) / (float)full : 0f;
}
