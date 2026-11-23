namespace Emberward.Desktop.Gui.Guidance.Tutorial;

using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Types;

/// <summary>
/// The sector goal: tapping the hot core for a minute. Skipped in a timed match (<see cref="World.MatchClock"/>), where
/// the score is the goal and nobody should be sent to the core.
/// </summary>
internal sealed class TapCoreStep : TutorialStep
{
    public override string Title => "Tap the core";

    protected override string Text =>
        "Chain relays to the hot core at the centre. Relays beside it must make the goal rate of charge, held for a minute. More relays there, a bigger radiator and a better engine all help.";

    public override bool IsDone(World world, Tank player) =>
        world.CorePocket == null || world.MatchClock.Active || world.CoreTapOf(player.Base) is { HasTapped: true };

    public override (string Text, float Fraction)? Progress(World world, Tank player) => world.CoreTapOf(player.Base) is { } b
        ? ($"Core tap {b.TapPerSecond / 1000.0:0.0}k / {Tweaks.Goal.GoalChargePerSecond / 1000.0:0.0}k charge/s, held {b.HoldSeconds:0} / {Tweaks.Goal.GoalHoldSeconds:0} s",
            b.HoldFraction)
        : null;

    public override Position? Target(World world, Tank player) => world.CorePocket;
}
