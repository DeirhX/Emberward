namespace Emberward.Desktop.Gui.Guidance.Events;

using Emberward.Core;
using Emberward.Core.Entities;
using Emberward.Core.Entities.Tanks;

/// <summary>
/// Another tank went down, anywhere on the map: a toast saying who killed it ("You destroyed the Red tank", "Green's
/// turret destroyed the Red tank"), or, when no side did, the tank and why. The player's own death has its death
/// screen, and a recall is not a death to report (the tank went home of its own accord).
/// </summary>
internal sealed class TankDeathWatch : IEventWatch
{
    private readonly List<(int Tank, DamageCause Cause, Killer? By)> _died = new();

    public void Reset() => _died.Clear();

    public void OnStepEvent(World world, Tank player, in StepEvent e)
    {
        if (e.Kind == StepEventKind.TankDied && e.Color != player.Color && e.Cause != DamageCause.Recalled)
            _died.Add((e.Color, e.Cause, e.By));
    }

    public void Update(in GuidanceFrame f)
    {
        foreach (var (tank, cause, by) in _died)
        {
            var (title, detail) = TankDeathView.DeathToast(tank, cause, by, f.Player.Color);
            // A kill by the player's side is good news; another side's kill is just news.
            var kind = by is { } killer && killer.Color != f.Player.Color ? NotificationKind.Info : NotificationKind.Good;
            f.Feed.Post(f.Now, $"tank-died-{tank}", title, detail, kind, cooldown: 0f);
        }
        _died.Clear();
    }
}
