namespace Emberward.Desktop.Gui.Guidance.Tutorial;

using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities.Machines;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Types;

/// <summary>A turret against enemy tanks (the energy network only).</summary>
internal sealed class TurretStep : TutorialStep
{
    public override string Title => "Defend with a turret";

    protected override string Text =>
        $"With the energy network, press {Key(MachineType.Turret)} to build a turret ({Cost(MachineType.Turret)} ore) and plant it inside your network's coverage. While its island feeds it, it fires at enemy tanks within {Tweaks.Turret.Range} cells that it can see. Each shot heats the ground around it, and a hot turret fires more slowly: a relay nearby keeps it cool. F beside it offers Rapid or Long.";

    public override bool IsDone(World world, Tank player) =>
        !world.Rules.EnergyNetwork || OwnsAny(world, player, MachineType.Turret);

    public override (string Text, float Fraction)? Progress(World world, Tank player) => StoreRatio(player, Cost(MachineType.Turret));

    public override Position? Target(World world, Tank player) => FirstOwned(world, player, MachineType.Relay);
}

/// <summary>A gatling against emberites (the energy network with emberites only).</summary>
internal sealed class GatlingStep : TutorialStep
{
    public override string Title => "Hold a nest off with a gatling";

    protected override string Text =>
        $"Emberites nest in pyrite and hunt a tank that runs hot. Press {Key(MachineType.Gatling)} to build a gatling ({Cost(MachineType.Gatling)} ore) and plant it inside your network's coverage, on the way the creatures come. It sprays the ones within {Tweaks.Gatling.Range} cells, even just inside a tunnel wall, and now and then a dead one drops a unit of its nest's ore. Firing heats its ground and a hot gatling slows: keep a relay nearby. F beside it offers Spin or Scope.";

    public override bool IsDone(World world, Tank player) =>
        !world.Rules.EnergyNetwork || !world.Rules.Emberites || OwnsAny(world, player, MachineType.Gatling);

    public override (string Text, float Fraction)? Progress(World world, Tank player) => StoreRatio(player, Cost(MachineType.Gatling));

    public override Position? Target(World world, Tank player) => FirstOwned(world, player, MachineType.Relay);
}
