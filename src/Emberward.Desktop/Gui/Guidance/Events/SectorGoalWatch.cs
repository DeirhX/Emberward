namespace Emberward.Desktop.Gui.Guidance.Events;

using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Match;

/// <summary>
/// The sector goal: tapping the core, holding the goal rate, the sector complete. Silent in a timed match
/// (<see cref="World.MatchClock"/>), whose goal is the score: the core is there, and tapping it is not the point.
/// </summary>
internal sealed class SectorGoalWatch : IEventWatch
{
    private Latch _tapping;
    private Latch _meetingGoal;
    private Latch _tapped;

    public void Reset() { }

    public void OnStepEvent(World world, Tank player, in StepEvent e) { }

    public void Update(in GuidanceFrame f)
    {
        CoreTap? core = f.World.MatchClock.Active ? null : f.World.CoreTapOf(f.Home);
        if (_tapping.Rose(core?.TapPerSecond > 1.0) && f.Primed)
            f.Feed.Post(f.Now, "core-tap", "Tapping the core",
                $"Your relays by the core make charge. Reach {Tweaks.Goal.GoalChargePerSecond / 1000:0}k/s and hold it for {Tweaks.Goal.GoalHoldSeconds:0} s.",
                NotificationKind.Good, cooldown: 60f);
        if (_meetingGoal.Rose(core?.IsMeetingGoal == true) && f.Primed)
            f.Feed.Post(f.Now, "core-goal", "Core tap at the goal rate",
                "Keep it there: the hold timer is running. Falling short drains it.", NotificationKind.Good, cooldown: 30f);
        if (_tapped.Rose(core?.HasTapped == true) && f.Primed)
            f.Feed.Post(f.Now, "sector-complete", "Sector complete", "The core is tapped.", NotificationKind.Good,
                duration: 12f, cooldown: 1e9f);
    }
}
