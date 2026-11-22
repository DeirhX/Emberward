namespace Emberward.Desktop.Audio;

using Emberward.Core;
using Emberward.Desktop.Config;

public static partial class StepEventSounds
{
    /// <summary>The player's base: hits on it and its gates, its fall, and docking at it.</summary>
    private static StepEventCue? BaseCue(StepEventKind kind) => kind switch
    {
        StepEventKind.BaseDamaged => new(SoundEffect.TankHit, AudioTweaks.BaseHitGain, CueHeard.Player, CueVoice.Burst),
        // A gate hit is the base's shield taking a hit: the shield's own sound, its breaking the shatter.
        StepEventKind.BaseShieldHit => new(SoundEffect.ShieldHit, AudioTweaks.ShieldHitGain, CueHeard.Player, CueVoice.Burst),
        StepEventKind.BaseShieldBroken => new(SoundEffect.ShieldBreak, AudioTweaks.ShieldBreakGain, CueHeard.Player),
        StepEventKind.BaseDestroyed => new(SoundEffect.TankDeath, AudioTweaks.TankDeathGain, CueHeard.Player),
        StepEventKind.Docked => new(SoundEffect.Dock, AudioTweaks.DockGain, CueHeard.Player),
        _ => null,
    };
}
