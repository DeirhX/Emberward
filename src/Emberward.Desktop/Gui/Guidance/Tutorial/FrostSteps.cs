namespace Emberward.Desktop.Gui.Guidance.Tutorial;

using Emberward.Core;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Types;

/// <summary>Frost rock, where a hot tank cools.</summary>
internal sealed class FrostStep : TutorialStep
{
    private const int NearSquared = 14 * 14;

    private bool _nearFrost;

    public override string Title => "Cool off at the frost";

    protected override string Text =>
        "Blue rock is frost. Beside it a hot tank cools fast, a good stop on a long dig. Stay too long and the cold slows you down, then cracks your shield.";

    public override float ReadSeconds(World world) => 16f;

    public override void Observe(World world, Tank player)
    {
        foreach (var frost in world.FrostRockPockets)
            if (Position.DistanceSquared(frost, player.Position) <= NearSquared)
                _nearFrost = true;
    }

    public override bool IsDone(World world, Tank player) => _nearFrost;

    public override Position? Target(World world, Tank player) => Nearest(world.FrostRockPockets, player.Position);
}
