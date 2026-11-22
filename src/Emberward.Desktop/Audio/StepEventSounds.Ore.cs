namespace Emberward.Desktop.Audio;

using Emberward.Core;
using Emberward.Desktop.Config;

public static partial class StepEventSounds
{
    /// <summary>Pour cues of one tank this close together (steps) are heard once (DepositQuads.JoinSteps).</summary>
    public const int PourJoinSteps = 6;

    /// <summary>Ore: picked up into the hold, and unloaded or burned at a relay.</summary>
    private static StepEventCue? OreCue(StepEventKind kind) => kind switch
    {
        StepEventKind.OreCollected => new(SoundEffect.Pickup, AudioTweaks.PickupGain, CueHeard.Anywhere, CueVoice.PickupStreak),
        StepEventKind.OreUnloaded or StepEventKind.ReserveBurned => new(SoundEffect.Pickup, AudioTweaks.UnloadGain, CueHeard.Player),
        _ => null,
    };

    /// <summary>
    /// Ore banked at the core (<see cref="ImpactTarget.Base"/>) is a deposit, louder for a bigger load, and ore burned
    /// to restart a dark base the same deposit pitched down. At a relay a unit is still the pickup streak's quieter
    /// note (<see cref="OreCue"/>).
    /// </summary>
    private static StepEventCue? OreCueFor(in StepEvent e) => e.Kind switch
    {
        StepEventKind.OreUnloaded when e.Target == ImpactTarget.Base =>
            new(SoundEffect.Deposit, DepositGain(e.Count), CueHeard.Player, CueVoice.Pour),
        StepEventKind.RestartOreBurned =>
            new(SoundEffect.Deposit, DepositGain(e.Count), CueHeard.Player, CueVoice.Pour, AudioTweaks.DepositBurnPitch),
        _ => null,
    };

    /// <summary>
    /// A deposit's gain for a pour of <paramref name="units"/>: 0.6 to 1 of <see cref="AudioTweaks.DepositGain"/>. The
    /// director passes the whole pour (the first event's units and what the hold still has to bank), since a bank's
    /// first event carries only a tick's worth.
    /// </summary>
    public static float DepositGain(int units) => AudioTweaks.DepositGain * (0.6f + 0.4f * Math.Min(units, 40) / 40f);
}
