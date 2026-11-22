namespace Emberward.Desktop.Gui.Guidance.Tutorial;

using Emberward.Core;
using Emberward.Core.Entities.Machines;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Types;

/// <summary>Mining pyrite into the hold.</summary>
internal sealed class MineOreStep : TutorialStep
{
    public override string Title => "Mine pyrite";

    protected override string Text =>
        "The gold crystal is pyrite ore, what everything is paid with. Drive into it to mine it into your hold (MATERIALS). The hold is small: bring it home.";

    public override bool IsDone(World world, Tank player) =>
        player.OreHeld >= Cost(MachineType.Relay) || OwnsAny(world, player, MachineType.Relay);

    public override (string Text, float Fraction)? Progress(World world, Tank player) =>
        Ratio("Ore", player.OreHeld, Cost(MachineType.Relay));
}

/// <summary>The tank's heat, and banking a haul at the core.</summary>
internal sealed class TemperatureStep : TutorialStep
{
    private bool _carriedHaul;
    private bool _unloadedHaul;

    public override string Title => "Mind your temperature";

    protected override string Text =>
        "Digging heats the tank (TEMP), and so does pyrite in the hold, hard from 20. Above 100° it burns your shield. Drive back into your base to cool down, and touch the core at its centre to bank your ore.";

    public override void Observe(World world, Tank player)
    {
        // A real haul: the tank came home with at least a relay's worth of ore and unloaded it at the core or a relay.
        if (!player.IsDockedAtOwnBase && player.OreHeld >= Cost(MachineType.Relay))
            _carriedHaul = true;
        if (player.IsUnloadingOre && _carriedHaul)
            _unloadedHaul = true;
    }

    public override bool IsDone(World world, Tank player) => _unloadedHaul || OwnsAny(world, player, MachineType.Relay);

    public override Position? Target(World world, Tank player) => player.Base?.Position;
}
