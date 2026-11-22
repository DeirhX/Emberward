namespace Emberward.Desktop.Rendering.Entities;

using Emberward.Core.Config;

/// <summary>
/// How each part of a hull fire comes in as the fire level (<see cref="Emberward.Core.Entities.Tanks.Tank.FireLevel"/>:
/// 0 at the ignition threshold, 75% hull, to 1 at an empty hull) rises from zero: every part eases in from nothing, in
/// turn, so that nothing pops in when a hull starts to burn and nothing pops out when it is repaired. First the light
/// on the ground and the heat shimmer, then the dull core's glow, then its soot and crust, then the flame licks, then
/// smoke, and last the sparks and cinders. The renderer, the heat haze and the fire loop all read these.
/// </summary>
public static class FireOnset
{
    /// <summary>How long the drawn fire takes to follow a change in the hull or shield (seconds; exponential).</summary>
    public const float FollowSeconds = 0.35f;

    /// <summary>The share of the way to a new level a drawn fire moves in one simulation step.</summary>
    public static float StepFollow { get; } = 1f - MathF.Exp(-1f / (Tweaks.Perf.TargetFps * FollowSeconds));

    /// <summary>0 at or below <paramref name="from"/>, 1 at or above <paramref name="to"/>, smoothstep between.</summary>
    public static float Ease(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / (to - from), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    /// <summary>The firelight on the ground (its strength) and the fire's heat shimmer.</summary>
    public static float Light(float level) => Ease(0f, 0.12f, level);

    /// <summary>The exposed core's glow, before a shield hides it.</summary>
    public static float CoreGlow(float level) => Ease(0.02f, 0.16f, level);

    /// <summary>The soot ring round the core and the core's crust, its drips and its white-hot pulse.</summary>
    public static float Soot(float level) => Ease(0.04f, 0.2f, level);

    /// <summary>The level the flames are drawn at: none until the core shows, full from a level of 0.3.</summary>
    public static float Flames(float level) => level * Ease(0.08f, 0.3f, level);

    /// <summary>The share of a tank's flame-lick slots in the air: the licks come in with the flames.</summary>
    public static float FlameShare(float level) => Ease(0.08f, 0.3f, level) * (0.3f + (0.7f * Math.Min(level, 1f)));

    /// <summary>The share of a tank's smoke slots in the air.</summary>
    public static float SmokeShare(float level) => Ease(0.12f, 0.4f, level) * (0.3f + (0.7f * Math.Min(level, 1f)));

    /// <summary>The share of a tank's ember slots in the air.</summary>
    public static float EmberShare(float level) => Ease(0.3f, 0.6f, level) * (0.25f + (0.75f * Math.Min(level, 1f)));

    /// <summary>How loud the fire loop is, as a share of its full gain at this level.</summary>
    public static float Sound(float level) => Ease(0f, 0.15f, level);
}
