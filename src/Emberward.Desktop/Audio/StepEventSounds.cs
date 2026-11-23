namespace Emberward.Desktop.Audio;

using Emberward.Core;
using Emberward.Core.Entities.Machines;

/// <summary>Whose event a one-shot is heard for.</summary>
public enum CueHeard
{
    /// <summary>Whoever caused it, tracked tank or not (a shot, a bullet bursting, ore picked up).</summary>
    Anywhere,

    /// <summary>Only a tank <see cref="GameAudio"/> tracks: what happens to a tank itself.</summary>
    TrackedTank,

    /// <summary>Only the player's own tank: feedback on what the player did.</summary>
    Player,

    /// <summary>
    /// Every player, wherever they are: an announcement about the match (a drop, the clock), played at the listener
    /// and not at the place it is about.
    /// </summary>
    Everyone,
}

/// <summary>How a one-shot varies from one play to the next.</summary>
public enum CueVoice
{
    /// <summary>The same every time.</summary>
    Plain,

    /// <summary>Only the first few in a step are heard, each at a slightly different pitch.</summary>
    Burst,

    /// <summary>The pitch cycles over the tank's last few shots.</summary>
    ShotCycle,

    /// <summary>Rate-limited per tank, rising in pitch over a quick run of them.</summary>
    PickupStreak,

    /// <summary>Once per pour: events of the same tank within <see cref="StepEventSounds.PourJoinSteps"/> of each other
    /// are one sound (a bank at the core is four events a tick apart).</summary>
    Pour,
}

/// <summary>The one-shot a simulation event plays: what, how loud, for whom, and how it varies.</summary>
public readonly record struct StepEventCue(SoundEffect Effect, float Gain, CueHeard Heard, CueVoice Voice = CueVoice.Plain,
    float Pitch = 1f);

/// <summary>
/// Which one-shot effect a simulation event (<see cref="World.StepEvents"/>) plays. This is the one table of event
/// sounds: <see cref="GameAudio"/> plays what it says. Each system has its rows in a partial of its own
/// (<c>StepEventSounds.Tanks.cs</c>, <c>.Base.cs</c>, <c>.Machines.cs</c>, <c>.Ore.cs</c>, <c>.Emberites.cs</c>,
/// <c>.Arcade.cs</c>), and
/// <see cref="CueFor(StepEventKind, MachineType)"/> asks each in turn: a new system's sounds are a new partial and a
/// line there; retiring a system deletes its partial and that line. Events that make no one-shot of their own
/// (moving, digging, base service and spent shrapnel, which drive loops and the debris rattle in
/// <see cref="GameAudio"/>) and events with no sound yet have no cue. The browser client shares these files and plays
/// the cues they pick with its own tones.
/// </summary>
public static partial class StepEventSounds
{
    /// <summary>
    /// The one-shot of <paramref name="e"/>: as <see cref="CueFor(StepEventKind, MachineType)"/>, except for the
    /// events whose sound depends on more than their kind (<see cref="OreCueFor(in StepEvent)"/>). An event of the
    /// player's own may sound different still (<see cref="PlayerCueFor"/>), and a kill and the clock of a timed match
    /// have cues of their own (<see cref="KillCueFor"/>, <see cref="ClockCue"/>).
    /// </summary>
    public static StepEventCue? CueFor(in StepEvent e) => OreCueFor(in e) ?? CueFor(e.Kind, e.Machine);

    public static StepEventCue? CueFor(StepEventKind kind, MachineType machine) =>
        TankCue(kind) ?? BaseCue(kind) ?? MachineCue(kind, machine) ?? OreCue(kind) ?? EmberiteCue(kind) ?? PowerupCue(kind);

    /// <summary>
    /// Whether <paramref name="cue"/> is heard for an event of a tank that is <paramref name="tracked"/> or not, and
    /// is the player's own (<paramref name="isPlayer"/>) or not.
    /// </summary>
    public static bool Hears(in StepEventCue cue, bool tracked, bool isPlayer) => cue.Heard switch
    {
        CueHeard.Anywhere or CueHeard.Everyone => true,
        CueHeard.TrackedTank => tracked,
        _ => tracked && isPlayer,
    };
}
