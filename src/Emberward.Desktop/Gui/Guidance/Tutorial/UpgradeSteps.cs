namespace Emberward.Desktop.Gui.Guidance.Tutorial;

using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities.Bases;
using Emberward.Core.Entities.Machines;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Types;

/// <summary>The base upgrades, bought while docked.</summary>
internal sealed class BaseUpgradeStep : TutorialStep
{
    public const string StepTitle = "Upgrade";

    public override string Title => StepTitle;

    protected override string Text =>
        "Dock and press 1-7 to spend the base ore store: radiator, network reach, engine, insulation and sensors for the base, an insulated hold and a longer radar for your tank.";

    public override bool IsDone(World world, Tank player) =>
        player.Base is { } b && Enumerable.Range(0, BaseUpgrades.Count).Any(i => b.Upgrades.Level((BaseUpgrade)i) > 0);

    public override (string Text, float Fraction)? Progress(World world, Tank player) =>
        StoreRatio(player, Tweaks.Upgrades.LevelCosts[0]);

    public override Position? Target(World world, Tank player) => player.Base?.Position;
}

/// <summary>A relay's own upgrade paths (the machine upgrades rule).</summary>
internal sealed class RelayUpgradeStep : TutorialStep
{
    public override string Title => "Upgrade a relay";

    protected override string Text =>
        "Stop beside one of your relays and press F (or tap it): its panel offers Tap (pumps more heat, so more charge, and runs hotter), Sensor (sees further) or Conduit (a bigger capacitor). Press 1-3 to buy from the base ore store; a relay keeps the path it takes first.";

    public override bool IsDone(World world, Tank player) =>
        !world.Rules.MachineUpgrades || Owned(world, player, MachineType.Relay).Any(m => world.MachineUpgradeOf(m).Level > 0);

    public override (string Text, float Fraction)? Progress(World world, Tank player) =>
        StoreRatio(player, MachineUpgrades.CostOfLevel(0));

    public override Position? Target(World world, Tank player) =>
        world.MachineUpgradeOfferFor(player)?.Machine.Position ?? FirstOwned(world, player, MachineType.Relay);
}
