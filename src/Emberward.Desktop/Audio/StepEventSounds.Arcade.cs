namespace Emberward.Desktop.Audio;

using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Match;
using Emberward.Desktop.Config;
using Emberward.Desktop.Gui;

public static partial class StepEventSounds
{
    /// <summary>
    /// Powerup drops (DESIGN.md §6.8). A drop is everyone's business wherever they are, as its forecast is, so its
    /// sounds are announcements (<see cref="CueHeard.Everyone"/>): the next one decided (a soft ping), one landed (a
    /// clear chime), one taken, one run out (the ping again, lower and quieter). A powerup running out on the
    /// player's own tank is the player's alone.
    /// </summary>
    private static StepEventCue? PowerupCue(StepEventKind kind) => kind switch
    {
        StepEventKind.PowerupAnnounced => new(SoundEffect.DropAnnounce, AudioTweaks.DropAnnounceGain, CueHeard.Everyone),
        StepEventKind.PowerupLanded => new(SoundEffect.DropLand, AudioTweaks.DropLandGain, CueHeard.Everyone),
        StepEventKind.PowerupCollected => new(SoundEffect.DropTaken, AudioTweaks.DropTakenGain, CueHeard.Everyone),
        StepEventKind.PowerupExpired =>
            new(SoundEffect.DropAnnounce, AudioTweaks.DropExpiredGain, CueHeard.Everyone, Pitch: AudioTweaks.DropExpiredPitch),
        StepEventKind.PowerupEnded => new(SoundEffect.EffectEnd, AudioTweaks.EffectEndGain, CueHeard.Player),
        _ => null,
    };

    /// <summary>
    /// The one-shot of an event that is the player's own, where that differs from what everyone else hears for it;
    /// null where it does not (then <see cref="CueFor(in StepEvent)"/> answers). Taking a drop yourself is a reward,
    /// not the chime of a chance gone.
    /// </summary>
    public static StepEventCue? PlayerCueFor(in StepEvent e) => e.Kind switch
    {
        StepEventKind.PowerupCollected => new(SoundEffect.DropGained, AudioTweaks.DropGainedGain, CueHeard.Everyone),
        _ => null,
    };

    /// <summary>
    /// The confirmation the player hears, beside the death itself, when <paramref name="e"/> is a tank the player's
    /// side destroyed (its shell, or its turret's or gatling's): null for any other event, a death nobody scored and
    /// the player's own death.
    /// </summary>
    /// <param name="playerColor">The player's tank; -1 for none.</param>
    public static StepEventCue? KillCueFor(in StepEvent e, int playerColor) =>
        e.Kind == StepEventKind.TankDied && playerColor >= 0 && e.Killer == playerColor && e.Color != playerColor
            ? new(SoundEffect.KillConfirm, AudioTweaks.KillConfirmGain, CueHeard.Everyone)
            : null;

    /// <summary>
    /// What the clock of a timed match sounds this step, having stood at <paramref name="before"/> the step before
    /// and at <paramref name="now"/> after it: three strokes as the last minute starts, a tick on each of the last
    /// seconds (higher for the last three), an alarm as sudden death starts and a horn when the match ends. Null on
    /// every other step.
    /// </summary>
    public static StepEventCue? ClockCue(ClockPhase before, ClockPhase now, in MatchClock clock)
    {
        if (now != before)
        {
            switch (now)
            {
                case ClockPhase.LastMinute:
                    return new(SoundEffect.ClockWarning, AudioTweaks.ClockWarningGain, CueHeard.Everyone);
                case ClockPhase.SuddenDeath:
                    return new(SoundEffect.SuddenDeath, AudioTweaks.SuddenDeathGain, CueHeard.Everyone);
                case ClockPhase.Final:
                    return new(SoundEffect.MatchHorn, AudioTweaks.MatchHornGain, CueHeard.Everyone);
            }
        }
        if (now == ClockPhase.LastSeconds && clock.RemainingTicks > 0 && clock.RemainingTicks % Tweaks.Perf.TargetFps == 0)
            return new(SoundEffect.ClockTick, AudioTweaks.ClockTickGain, CueHeard.Everyone,
                Pitch: clock.RemainingSeconds <= 3 ? AudioTweaks.ClockLastTicksPitch : 1f);
        return null;
    }
}
