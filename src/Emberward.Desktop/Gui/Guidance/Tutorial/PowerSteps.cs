namespace Emberward.Desktop.Gui.Guidance.Tutorial;

using Emberward.Core;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Types;

/// <summary>Heat from hot rock, pumped home and turned into charge by the base engine.</summary>
internal sealed class HotRockPowerStep : TutorialStep
{
    private bool _engineRan;

    public override string Title => "Turn hot rock into power";

    protected override string Text =>
        "Red rock is hot. Plant a relay near it (its line must reach your network). It pumps the heat home, and your base engine turns it into charge.";

    public override void Observe(World world, Tank player)
    {
        if (player.Base is { IsEngineRunning: true })
            _engineRan = true;
    }

    public override bool IsDone(World world, Tank player) => _engineRan;

    public override (string Text, float Fraction)? Progress(World world, Tank player) => player.Base is { } b
        ? ($"Engine +{b.EngineOutputPerSecond / 1000.0:0.0}k charge/s", b.IsEngineRunning ? 1f : 0f)
        : null;

    public override Position? Target(World world, Tank player) =>
        Nearest(world.HotRockPockets, player.Base?.Position ?? player.Position);
}
