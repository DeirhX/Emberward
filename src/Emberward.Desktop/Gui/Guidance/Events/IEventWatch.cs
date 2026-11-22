namespace Emberward.Desktop.Gui.Guidance.Events;

using Emberward.Core;
using Emberward.Core.Entities.Bases;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Network;

/// <summary>
/// One system's toasts: it watches that system's part of the player's side and posts to the feed when something
/// the player should know about happens. <see cref="GameEventWatcher"/> holds one per system, so adding or retiring a
/// system's notifications touches only its watch and that list.
/// </summary>
internal interface IEventWatch
{
    /// <summary>Forgets what it saw: a new match.</summary>
    void Reset();

    /// <summary>Once for each simulated step, before its events reach <see cref="OnStepEvent"/>.</summary>
    void OnStep() { }

    /// <summary>One event of the step just simulated (<see cref="World.StepEvents"/>).</summary>
    void OnStepEvent(World world, Tank player, in StepEvent e);

    /// <summary>
    /// Once a displayed frame. Before <see cref="GuidanceFrame.Primed"/> a watch records what it sees and posts
    /// nothing that compares with an earlier frame, so a match does not open with a burst of "changed" toasts.
    /// </summary>
    void Update(in GuidanceFrame frame);
}

/// <summary>What every watch reads in a frame.</summary>
/// <param name="Network">The player's base's network, collected once for all watches.</param>
/// <param name="IntroQuiet">The tutorial has not yet explained the network; toasts about it wait.</param>
/// <param name="Primed">An earlier frame was seen since the last reset.</param>
internal readonly record struct GuidanceFrame(
    World World, Tank Player, TankBase Home, NetworkStatus Network, NotificationFeed Feed, float Now, bool IntroQuiet,
    bool Primed);

/// <summary>A state seen in the last frame, to tell when it turns on.</summary>
internal struct Latch
{
    private bool _was;

    /// <summary>True when <paramref name="now"/> is on and was off last time; remembers it either way.</summary>
    public bool Rose(bool now)
    {
        bool rose = now && !_was;
        _was = now;
        return rose;
    }
}

/// <summary>A count seen in the last frame, to tell how it changed.</summary>
internal struct Tally
{
    private int _was;

    /// <summary>How much <paramref name="now"/> differs from last time; remembers it either way.</summary>
    public int Change(int now)
    {
        int change = now - _was;
        _was = now;
        return change;
    }
}
