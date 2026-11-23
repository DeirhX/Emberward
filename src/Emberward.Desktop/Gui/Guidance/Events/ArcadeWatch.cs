namespace Emberward.Desktop.Gui.Guidance.Events;

using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities.Powerups;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Match;
using Emberward.Core.Types;
using Emberward.Desktop.Presentation;

/// <summary>
/// A timed match and its powerup drops (DESIGN.md §6.8): the rules as the match starts, a drop landing and who took
/// it, who destroyed whom, the lead changing hands, the last minute and sudden death. Kills share one toast that the next
/// replaces (the player's own kills and deaths another), so a busy fight never fills the stack. Silent in a match
/// with no clock and no drops.
/// </summary>
internal sealed class ArcadeWatch : IEventWatch
{
    /// <summary>A match joined later than this has started already: its rules are not announced.</summary>
    private const int RulesToastSeconds = 5;

    /// <summary>A new leader is announced at most this often, in seconds.</summary>
    private const float LeadCooldown = 12f;

    private readonly List<StepEvent> _pending = new();
    private bool _rulesTold;

    // The tank that leads alone now (-1 while the top score is tied), and the last one that did.
    private int _leader = -1;
    private int _lastLeader = -1;
    private ClockPhase _phase;

    public void Reset()
    {
        _pending.Clear();
        _rulesTold = false;
        _leader = -1;
        _lastLeader = -1;
        _phase = ClockPhase.Running;
    }

    public void OnStepEvent(World world, Tank player, in StepEvent e)
    {
        if (e.Kind is StepEventKind.PowerupLanded or StepEventKind.PowerupCollected
            || (e.Kind == StepEventKind.TankDied && e.Killer >= 0 && e.Killer != e.Color))
            _pending.Add(e);
    }

    public void Update(in GuidanceFrame f)
    {
        MatchClock clock = f.World.MatchClock;
        TellRules(f, clock);
        foreach (StepEvent e in _pending)
        {
            switch (e.Kind)
            {
                case StepEventKind.PowerupLanded:
                    Landed(f, (PowerupKind)e.Count, e.Position);
                    break;
                case StepEventKind.PowerupCollected:
                    Taken(f, (PowerupKind)e.Count, e.Color);
                    break;
                default:
                    Kill(f, e.Killer, e.Color);
                    break;
            }
        }
        _pending.Clear();
        if (clock.Active)
        {
            TellLeader(f, clock);
            TellClock(f, clock);
        }
    }

    /// <summary>The rules of a timed match in a line, and of its drops in another: <c>Most kills in 8:00 wins.</c></summary>
    public static string RulesText(World world)
    {
        MatchClock clock = world.MatchClock;
        string text = clock.Active ? $"Most kills in {Scoreboards.MinutesSeconds(clock.LimitSeconds)} wins." : "";
        if (world.PowerupForecast.Active)
            text += $"{(text.Length > 0 ? " " : "")}Drops land every {Tweaks.Powerups.DropPeriodSeconds} s - watch the marker.";
        return text;
    }

    private void TellRules(in GuidanceFrame f, in MatchClock clock)
    {
        if (_rulesTold)
            return;
        _rulesTold = true;
        if ((clock.Active || f.World.PowerupForecast.Active) && f.World.AdvanceCount <= RulesToastSeconds * Tweaks.Perf.TargetFps)
            f.Feed.Post(f.Now, "arcade-rules", clock.Active ? "Arcade match" : "Powerup drops", RulesText(f.World),
                NotificationKind.Info, duration: 12f, cooldown: 1e9f);
    }

    private static void Landed(in GuidanceFrame f, PowerupKind kind, Position site)
    {
        var p = PowerupPresentations.Get(kind);
        f.Feed.Post(f.Now, "drop-landed", $"{p.Name} landed", $"{p.Effect} {DropBoard.Distance(f.Player.Position, site)} away.",
            NotificationKind.Info, duration: 7f, cooldown: 0f);
    }

    private static void Taken(in GuidanceFrame f, PowerupKind kind, int taker)
    {
        var p = PowerupPresentations.Get(kind);
        bool own = taker == f.Player.Color;
        f.Feed.Post(f.Now, "drop-taken", $"{(own ? "You" : f.Names(taker))} took {p.Name}", p.Effect,
            own ? NotificationKind.Good : NotificationKind.Warning, duration: 5f, cooldown: 0f);
    }

    private static void Kill(in GuidanceFrame f, int killer, int victim)
    {
        int self = f.Player.Color;
        string who = killer == self ? "You" : f.Names(killer);
        string whom = victim == self ? "you" : f.Names(victim);
        string standing = "";
        if (TankOf(f.World, killer) is { } tank && f.World.ScoreOf(tank) is { Active: true } score)
            standing = $"{Scoreboards.Count(score.Kills, "kill")}, {Scoreboards.Ordinal(score.Rank)} place";
        // The player's own kills and deaths keep a toast of their own; everyone else's share one that the next replaces.
        (string key, NotificationKind kind) = killer == self ? ("kill-own", NotificationKind.Good)
            : victim == self ? ("kill-own", NotificationKind.Danger)
            : ("kill-feed", NotificationKind.Info);
        f.Feed.Post(f.Now, key, $"{who} destroyed {whom}", standing, kind, duration: 4.5f, cooldown: 0f);
    }

    private void TellLeader(in GuidanceFrame f, in MatchClock clock)
    {
        int leader = f.World.ScoreLeader?.Color ?? -1;
        if (leader == _leader)
            return;
        _leader = leader;
        if (leader < 0)
            return;
        // The first lead of a match is the first kill, which its own toast tells; so is a lead regained after a tie.
        int before = _lastLeader;
        _lastLeader = leader;
        if (before < 0 || before == leader || !f.Primed || f.World.IsMatchOver)
            return;
        bool own = leader == f.Player.Color;
        int kills = f.World.ScoreLeader is { } tank ? f.World.ScoreOf(tank).Kills : 0;
        string left = clock.IsSuddenDeath ? "Sudden death." : $"{Scoreboards.ClockText(clock)} left.";
        f.Feed.Post(f.Now, "lead", own ? "You take the lead" : $"{f.Names(leader)} takes the lead",
            $"{Scoreboards.Count(kills, "kill")}. {left}", own ? NotificationKind.Good : NotificationKind.Warning,
            duration: 5f, cooldown: LeadCooldown);
    }

    private void TellClock(in GuidanceFrame f, in MatchClock clock)
    {
        ClockPhase phase = Scoreboards.PhaseOf(clock, f.World.IsMatchOver);
        if (phase == _phase)
            return;
        _phase = phase;
        if (!f.Primed)
            return;
        if (phase == ClockPhase.LastMinute)
            f.Feed.Post(f.Now, "clock", "One minute left", LeaderLine(f), NotificationKind.Warning, cooldown: 1e9f);
        else if (phase == ClockPhase.SuddenDeath)
            f.Feed.Post(f.Now, "clock-sudden", "Sudden death",
                "The top score is tied. The match ends the moment one tank leads alone.", NotificationKind.Danger,
                duration: 9f, cooldown: 1e9f);
    }

    private static string LeaderLine(in GuidanceFrame f)
    {
        if (f.World.ScoreLeader is not { } leader)
            return "The top score is tied: a tie at the end means sudden death.";
        string kills = Scoreboards.Count(f.World.ScoreOf(leader).Kills, "kill");
        return leader.Color == f.Player.Color ? $"You lead with {kills}." : $"{f.Names(leader.Color)} leads with {kills}.";
    }

    private static Tank? TankOf(World world, int color)
    {
        foreach (Tank tank in world.TankList.Tanks)
            if (tank.Color == color)
                return tank;
        return null;
    }
}
