namespace Emberward.Desktop.Gui.Guidance.Tutorial;

using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Types;

/// <summary>The first step: out through the gate.</summary>
internal sealed class LeaveBaseStep : TutorialStep
{
    private bool _hasLeftBase;

    public override string Title => "Leave the base";

    protected override string Text =>
        "Your goal is to tap the hot core at the centre of the map. First, get out: drive with W A S D through the gate in front of you. Your base has a gate in each wall.";

    public override void Observe(World world, Tank player)
    {
        if (!player.IsDockedAtOwnBase)
            _hasLeftBase = true;
    }

    public override bool IsDone(World world, Tank player) => _hasLeftBase;

    public override Position? Target(World world, Tank player) => player.Base is { } b
        ? b.Position + new Offset(b.Front.X * (Tweaks.Base.BaseSize / 2), b.Front.Y * (Tweaks.Base.BaseSize / 2))
        : null;
}
