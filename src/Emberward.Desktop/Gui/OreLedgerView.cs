namespace Emberward.Desktop.Gui;

using Emberward.Core;
using Emberward.Core.Config;

/// <summary>
/// How fast the local player's base store is growing: the units banked into it (<see cref="StepEventKind.OreUnloaded"/>
/// by a tank of the side, at the core or a relay) and mined into it (<see cref="StepEventKind.OreHarvested"/> by its
/// harvesters) over the last <see cref="WindowSeconds"/>. A statistic of the step events, not a game rule, so it is
/// counted here; display state only. Shared by desktop and browser.
/// </summary>
public sealed class OreLedgerView
{
    /// <summary>The window the rate is counted over.</summary>
    public const int WindowSeconds = 30;

    private const int WindowSteps = WindowSeconds * Tweaks.Perf.TargetFps;

    private readonly int[] _perStep = new int[WindowSteps];
    private int _next;
    private int _sum;

    /// <summary>Units a minute into the player's base store, over the last <see cref="WindowSeconds"/>.</summary>
    public int RatePerMinute => _sum * 60 / WindowSeconds;

    /// <summary>The player's base store as of the last step.</summary>
    public int Store { get; private set; }

    /// <summary>Call after each simulation step, while <see cref="World.StepEvents"/> still holds its events.</summary>
    public void CaptureStep(World world, int localSlot)
    {
        ArgumentNullException.ThrowIfNull(world);
        int units = 0;
        if (localSlot >= 0 && localSlot < world.TankList.Count && world.TankList.Tanks[localSlot].Base is { } home)
        {
            Store = home.Store.Ore;
            foreach (ref readonly StepEvent e in world.StepEvents)
                if (e.Kind is StepEventKind.OreUnloaded or StepEventKind.OreHarvested && e.Color == home.Color)
                    units += e.Count;
        }
        _sum += units - _perStep[_next];
        _perStep[_next] = units;
        _next = (_next + 1) % WindowSteps;
    }
}
