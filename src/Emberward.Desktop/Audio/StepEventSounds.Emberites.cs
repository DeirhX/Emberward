namespace Emberward.Desktop.Audio;

using Emberward.Core;
using Emberward.Desktop.Config;

public static partial class StepEventSounds
{
    /// <summary>
    /// Emberites: a nest waking is an alarm for whoever is near enough to hear it, and its stir before each wave a
    /// quieter one; a bite is a hit on a tank; a kill is a small burst.
    /// </summary>
    private static StepEventCue? EmberiteCue(StepEventKind kind) => kind switch
    {
        StepEventKind.NestWoke => new(SoundEffect.WarnOverheat, AudioTweaks.NestWakeGain, CueHeard.Anywhere),
        StepEventKind.NestStirred => new(SoundEffect.WarnEnergy, AudioTweaks.NestStirGain, CueHeard.Anywhere),
        StepEventKind.EmberiteBit => new(SoundEffect.TankHit, AudioTweaks.EmberiteBiteGain, CueHeard.TrackedTank, CueVoice.Burst),
        StepEventKind.EmberiteKilled => new(SoundEffect.Impact, AudioTweaks.EmberiteKilledGain, CueHeard.Anywhere, CueVoice.Burst),
        _ => null,
    };
}
