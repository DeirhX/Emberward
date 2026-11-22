namespace Emberward.Desktop.Audio;

using Emberward.Core;
using Emberward.Desktop.Config;

public static partial class StepEventSounds
{
    /// <summary>Tanks: their shots, hits on them and their shields, death and respawn.</summary>
    private static StepEventCue? TankCue(StepEventKind kind) => kind switch
    {
        StepEventKind.ShotFired => new(SoundEffect.Fire, AudioTweaks.FireGain, CueHeard.Anywhere, CueVoice.ShotCycle),
        StepEventKind.BulletImpact => new(SoundEffect.Impact, AudioTweaks.ImpactGain, CueHeard.Anywhere, CueVoice.Burst),
        StepEventKind.TankHit => new(SoundEffect.TankHit, AudioTweaks.TankHitGain, CueHeard.TrackedTank),
        StepEventKind.TankShieldHit => new(SoundEffect.ShieldHit, AudioTweaks.ShieldHitGain, CueHeard.TrackedTank, CueVoice.Burst),
        StepEventKind.TankShieldBroken => new(SoundEffect.ShieldBreak, AudioTweaks.ShieldBreakGain, CueHeard.TrackedTank),
        StepEventKind.TankDied => new(SoundEffect.TankDeath, AudioTweaks.TankDeathGain, CueHeard.TrackedTank),
        StepEventKind.TankRespawned => new(SoundEffect.Respawn, AudioTweaks.RespawnGain, CueHeard.TrackedTank),
        _ => null,
    };
}
