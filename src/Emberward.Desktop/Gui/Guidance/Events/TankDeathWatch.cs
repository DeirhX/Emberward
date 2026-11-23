namespace Emberward.Desktop.Gui.Guidance.Events;

using Emberward.Core;
using Emberward.Core.Entities;
using Emberward.Core.Entities.Tanks;

/// <summary>
/// Another tank went down with no side to credit: heat, frost, emberites, or the loss of its base. A death another tank
/// is credited with is <see cref="ArcadeWatch"/>'s kill toast, the player's own has its death screen, and a recall is
/// not a death to report. These share one toast that the next replaces, so a brawl elsewhere cannot push out a warning.
/// </summary>
internal sealed class TankDeathWatch : IEventWatch
{
    private readonly List<(int Tank, DamageCause Cause)> _died = new();

    public void Reset() => _died.Clear();

    public void OnStepEvent(World world, Tank player, in StepEvent e)
    {
        if (e.Kind == StepEventKind.TankDied && e.Killer < 0 && e.Color != player.Color && e.Cause != DamageCause.Recalled)
            _died.Add((e.Color, e.Cause));
    }

    public void Update(in GuidanceFrame f)
    {
        if (!f.World.IsMatchOver)
        {
            foreach (var (tank, cause) in _died)
                f.Feed.Post(f.Now, "tank-died-other", $"{f.Names(tank)} was destroyed", Detail(cause), NotificationKind.Info,
                    duration: 4.5f, cooldown: 0f);
        }
        _died.Clear();
    }

    private static string Detail(DamageCause cause) => cause switch
    {
        DamageCause.Overheat => "It overheated.",
        DamageCause.Frostbite => "It froze.",
        DamageCause.BaseDestroyed => "Its base fell with it.",
        DamageCause.Emberite => "Emberites got it.",
        _ => "It is out until it respawns.",
    };
}
