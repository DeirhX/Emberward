namespace Emberward.Desktop.Gui.Guidance.Events;

using Emberward.Core;
using Emberward.Core.Entities;
using Emberward.Core.Entities.Tanks;

/// <summary>
/// An enemy tank went down, anywhere on the map: a toast naming it by its colour and saying why. A recall is not a
/// death to report (the tank went home of its own accord).
/// </summary>
internal sealed class TankDeathWatch : IEventWatch
{
    private readonly List<(int Tank, DamageCause Cause)> _died = new();

    public void Reset() => _died.Clear();

    public void OnStepEvent(World world, Tank player, in StepEvent e)
    {
        if (e.Kind == StepEventKind.TankDied && e.Color != player.Color && e.Cause != DamageCause.Recalled)
            _died.Add((e.Color, e.Cause));
    }

    public void Update(in GuidanceFrame f)
    {
        foreach (var (tank, cause) in _died)
            f.Feed.Post(f.Now, $"tank-died-{tank}", $"{TankDeathView.ColorName(tank)} tank destroyed",
                TankDeathView.CauseLine(cause), NotificationKind.Good, cooldown: 0f);
        _died.Clear();
    }
}
