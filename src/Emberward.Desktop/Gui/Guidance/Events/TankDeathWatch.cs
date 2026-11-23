namespace Emberward.Desktop.Gui.Guidance.Events;

using Emberward.Core;
using Emberward.Core.Entities;
using Emberward.Core.Entities.Tanks;
using Emberward.Desktop.Rendering.Entities;

/// <summary>
/// Another tank went down, anywhere on the map: a toast saying who killed it ("You destroyed the Red tank", "Green's
/// turret destroyed the Red tank"), or, when no side did, the tank and why. The player's own death has its death
/// screen, and a recall is not a death to report (the tank went home of its own accord). A kill by the player has its
/// own toast; other sides' deaths share one, so a brawl elsewhere on the map cannot push out a warning.
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
            bool bot = (uint)tank < (uint)f.World.TankList.Count && f.World.TankList.Tanks[tank].Base is { IsBotControlled: true };
            var (title, detail) = TankDeathView.DeathToast(tank, cause, by, f.Player.Color, TankPlateQuads.DefaultName(tank, bot));
            // A kill by the player's side is good news with a toast of its own; another side's kill is just news. Every
            // other death shares one slot.
            bool mine = by is { } killer && killer.Color == f.Player.Color;
            var kind = by is { } k && k.Color != f.Player.Color ? NotificationKind.Info : NotificationKind.Good;
            f.Feed.Post(f.Now, mine ? $"tank-died-{tank}" : "tank-died-other", title, detail, kind, cooldown: 0f);
        }
        _died.Clear();
    }
}
