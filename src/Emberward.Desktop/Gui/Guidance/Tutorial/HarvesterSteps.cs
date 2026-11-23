namespace Emberward.Desktop.Gui.Guidance.Tutorial;

using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities.Machines;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Types;

/// <summary>Building the first harvester.</summary>
internal sealed class BuildHarvesterStep : TutorialStep
{
    public override string Title => "Build a harvester";

    protected override string Text =>
        $"Harvesters mine pyrite into your base store on their own, while they stand inside your network. Press {Key(MachineType.Harvester)} to build one ({Cost(MachineType.Harvester)} ore) and plant it near ore, inside your network.";

    public override bool IsDone(World world, Tank player) => OwnsAny(world, player, MachineType.Harvester);

    public override (string Text, float Fraction)? Progress(World world, Tank player) =>
        player.IsCarryingMachine && player.CarriedMachineType == MachineType.Harvester
            ? ($"Carrying it: drive near ore, inside your network, and press {Key(MachineType.Harvester)}", 1f)
            : Ratio("Ore you can spend", player.QuoteBuild(MachineType.Harvester).Stock, Cost(MachineType.Harvester));
}

/// <summary>A linked relay beside a harvester, to pump away the heat its mining makes.</summary>
internal sealed class CoolHarvesterStep : TutorialStep
{
    public override string Title => "Keep the harvester cool";

    protected override string Text =>
        "Mining heats the ground, and a harvester stalls when its ring turns orange. Plant a linked relay close to it to pump that heat away.";

    public override bool IsDone(World world, Tank player)
    {
        int reach = Tweaks.Machine.RelayCoolingRadius + Tweaks.Machine.HarvesterHeatSenseRadius;
        var relays = Owned(world, player, MachineType.Relay).Where(r => r.LinkPoint?.IsPowered == true).ToList();
        return Owned(world, player, MachineType.Harvester).Any(h =>
            relays.Any(r => Math.Abs(r.Position.X - h.Position.X) <= reach && Math.Abs(r.Position.Y - h.Position.Y) <= reach));
    }

    public override Position? Target(World world, Tank player) => FirstOwned(world, player, MachineType.Harvester);
}

/// <summary>What a harvester runs on: the base's charge, or with the energy network a generator.</summary>
internal sealed class PowerHarvesterStep : TutorialStep
{
    public override string Title => "Power the harvester";

    protected override string Text =>
        "Harvesters run on your base's charge (the Charge bar in the base panel) wherever your network covers them. Relays make that charge from warm ground: keep one by hot rock so the store never runs dry.";

    public override string Body(World world) => world.Rules.EnergyNetwork
        ? $"Harvesters draw charge through the network. Press {Key(MachineType.Generator)} to build a generator ({Cost(MachineType.Generator)} ore) and plant it beside hot ground. A generator can power nearby machines through their links even when cut off from the base."
        : Text;

    public override float ReadSeconds(World world) => world.Rules.EnergyNetwork ? 0f : 12f;

    // Without the energy network there is nothing to do: a harvester already runs on the base's store, so the step
    // only explains that and finishes once read. With it the player builds a generator.
    public override bool IsDone(World world, Tank player) =>
        world.Rules.EnergyNetwork && OwnsAny(world, player, MachineType.Generator);

    public override Position? Target(World world, Tank player) => world.Rules.EnergyNetwork
        ? Nearest(world.HotRockPockets, player.Position)
        : FirstOwned(world, player, MachineType.Harvester);
}
