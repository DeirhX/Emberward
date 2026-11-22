namespace Emberward.Desktop.Audio;

using Emberward.Core;
using Emberward.Core.Entities.Machines;
using Emberward.Desktop.Config;

public static partial class StepEventSounds
{
    /// <summary>Machines: building, planting and upgrading them, refusals, and turrets and gatlings firing.</summary>
    private static StepEventCue? MachineCue(StepEventKind kind, MachineType machine) => kind switch
    {
        StepEventKind.TurretFired => MachineBehaviors.Get(machine).HuntsCreatures
            ? new(SoundEffect.GatlingFire, AudioTweaks.GatlingFireGain, CueHeard.Anywhere, CueVoice.Burst)
            : new(SoundEffect.TurretFire, AudioTweaks.TurretFireGain, CueHeard.Anywhere, CueVoice.Burst),
        StepEventKind.MachineBuilt => new(BuildSound(machine), AudioTweaks.BuildGain, CueHeard.TrackedTank),
        StepEventKind.MachinePlanted => new(SoundEffect.Plant, AudioTweaks.PlantGain, CueHeard.TrackedTank),
        StepEventKind.MachineUpgraded => new(SoundEffect.Plant, AudioTweaks.PlantGain, CueHeard.Player),
        StepEventKind.PlantRefused or StepEventKind.BuildRefused or StepEventKind.MachineUpgradeRefused =>
            new(SoundEffect.PlantRefused, AudioTweaks.RefusedGain, CueHeard.Player),
        _ => null,
    };

    /// <summary>The sound of building a machine of <paramref name="type"/>; each machine has its own.</summary>
    public static SoundEffect BuildSound(MachineType type) =>
        (uint)type < (uint)BuildSounds.Length ? BuildSounds[(int)type] : throw new ArgumentOutOfRangeException(nameof(type));

    private static readonly SoundEffect[] BuildSounds = BuildSoundTable(
        (MachineType.Relay, SoundEffect.BuildRelay),
        (MachineType.Harvester, SoundEffect.BuildHarvester),
        (MachineType.Generator, SoundEffect.BuildGenerator),
        (MachineType.Turret, SoundEffect.BuildTurret),
        (MachineType.Gatling, SoundEffect.BuildGatling));

    private static SoundEffect[] BuildSoundTable(params (MachineType Type, SoundEffect Sound)[] entries)
    {
        var table = new SoundEffect?[Enum.GetValues<MachineType>().Length];
        foreach (var (type, sound) in entries)
            table[(int)type] = sound;
        if (Array.IndexOf(table, null) >= 0)
            throw new InvalidOperationException("Every MachineType needs a build sound.");
        return Array.ConvertAll(table, s => s!.Value);
    }
}
