namespace Emberward.Desktop.Gui.Guidance;

using Emberward.Core;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Types;
using Emberward.Desktop.Gui.Guidance.Events;

/// <summary>
/// Posts a toast when something happens that the player should know about. Each system has its
/// own watch in <c>Guidance/Events</c> (base attacks, enemy deaths, upgrades, the sector goal, the network, the base's power, ore,
/// radar, nests); this holds them in the order their toasts stack within a frame. A new system's notifications are a
/// new watch and a line in <see cref="_watches"/>; retiring a system deletes its watch and that line. It only reads
/// Core state. What happens inside a step a watch hears from <see cref="World.StepEvents"/> in <see cref="AfterStep"/>.
/// </summary>
public sealed class GameEventWatcher
{
    private readonly BaseAttackWatch _baseAttack = new();
    private readonly IEventWatch[] _watches;
    private bool _primed;

    public GameEventWatcher()
    {
        _watches =
        [
            _baseAttack,
            new TankDeathWatch(),
            new UpgradeWatch(),
            new SectorGoalWatch(),
            new NetworkWatch(),
            new BasePowerWatch(),
            new OreWatch(),
            new RadarWatch(),
            new NestWatch(),
        ];
    }

    /// <summary>When the player's base last took enemy fire, in HUD seconds; negative before the first hit.</summary>
    public float LastBaseHitTime => _baseAttack.LastHitTime;

    /// <summary>Where the last enemy bullet struck the player's base.</summary>
    public Position LastBaseHitAt => _baseAttack.LastHitAt;

    /// <summary>How long after a hit the base counts as under attack.</summary>
    public const float UnderAttackSeconds = 3f;

    public bool IsBaseUnderAttack(float now) => now - LastBaseHitTime < UnderAttackSeconds;

    /// <summary>The player is told the radar is being scattered once heat takes this share of a pulse's rays.</summary>
    public const float RadarScatteredShare = 0.08f;

    public void Reset()
    {
        _primed = false;
        foreach (IEventWatch watch in _watches)
            watch.Reset();
    }

    /// <summary>
    /// Call after each simulation step, while <see cref="World.StepEvents"/> still holds that step's events, however
    /// many steps a displayed frame runs.
    /// </summary>
    public void AfterStep(World world, Tank player)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(player);
        foreach (IEventWatch watch in _watches)
            watch.OnStep();
        foreach (ref readonly StepEvent e in world.StepEvents)
            foreach (IEventWatch watch in _watches)
                watch.OnStepEvent(world, player, in e);
    }

    /// <param name="introQuiet">
    /// The tutorial has not yet explained the network, so the toasts for leaving it and for scattered radar wait: the
    /// player is told what they mean at that step, and the clock on the HUD shows the state meanwhile.
    /// </param>
    public void Update(World world, Tank player, NotificationFeed feed, float now, bool introQuiet = false)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(player);
        if (player.Base is not { } home)
            return;

        var frame = new GuidanceFrame(world, player, home, world.GetNetworkStatus(home), feed, now, introQuiet, _primed);
        foreach (IEventWatch watch in _watches)
            watch.Update(in frame);
        _primed = true;
    }
}
