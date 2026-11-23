namespace Emberward.Desktop.Gui.Guidance.Tutorial;

using Emberward.Core;
using Emberward.Core.Entities.Machines;
using Emberward.Core.Entities.Tanks;

/// <summary>Building and linking the first relay.</summary>
internal sealed class BuildRelayStep : TutorialStep
{
    public override string Title => "Build a relay";

    protected override string Text =>
        $"Press {Key(MachineType.Relay)} to pick up a relay ({Cost(MachineType.Relay)} ore; inside your network the base store pays). Drive out of the base and press {Key(MachineType.Relay)} again where the line to your network shows green.";

    public override bool IsDone(World world, Tank player) =>
        Owned(world, player, MachineType.Relay).Any(m => m.LinkPoint?.IsPowered == true);

    public override (string Text, float Fraction)? Progress(World world, Tank player) => player.IsCarryingMachine
        ? ($"Carrying it: find a spot with a green line, then press {Key(MachineType.Relay)}", 1f)
        : Ratio("Ore you can spend", player.QuoteBuild(MachineType.Relay).Stock, Cost(MachineType.Relay));
}

/// <summary>The network's circles, and the clock that runs outside them.</summary>
internal sealed class StayOnNetworkStep : TutorialStep
{
    public const string StepTitle = "Stay on your network";

    private bool _wasOffNetwork;
    private bool _cameBackCovered;

    public override string Title => StepTitle;

    protected override string Text =>
        "The pale circles are your network. Inside them the links pull heat out of your tank, and touching a relay unloads your hold into the base. Outside, the OFF NETWORK clock runs: get back before it reaches 0, or plant another relay to reach further.";

    public override float ReadSeconds(World world) => 14f;

    public override void Observe(World world, Tank player)
    {
        if (player.IsOffNetwork)
            _wasOffNetwork = true;
        else if (_wasOffNetwork && (player.Coverage.Covered || player.IsDockedAtOwnBase))
            _cameBackCovered = true;
    }

    public override bool IsDone(World world, Tank player) => _cameBackCovered;
}
