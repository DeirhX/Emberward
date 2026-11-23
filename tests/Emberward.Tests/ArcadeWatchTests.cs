using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities;
using Emberward.Core.Entities.Powerups;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Match;
using Emberward.Core.Rendering;
using Emberward.Desktop.Gui.Guidance;
using Emberward.Desktop.Presentation;
using static Emberward.Tests.ArcadeWorlds;

namespace Emberward.Tests;

/// <summary>
/// The toasts of a timed match and its drops (<c>ArcadeWatch</c>, DESIGN.md §6.8): the rules as it starts, a drop
/// landing and who took it, who destroyed whom without flooding the stack, the lead changing hands, the last minute
/// and sudden death; and the guidance of the core race keeping quiet meanwhile.
/// </summary>
public class ArcadeWatchTests
{
    /// <summary>A world with a HUD's guidance listening to it, as the game runs them.</summary>
    private sealed class Match
    {
        public required World World { get; init; }
        public NotificationFeed Feed { get; } = new();
        public GameEventWatcher Events { get; } = new();
        public Func<int, string>? Names { get; init; }
        public int Slot { get; init; }
        public Tank Player => World.TankList.Tanks[Slot];
        public float Now => World.AdvanceCount / (float)Fps;

        /// <summary>What the HUD does after a step: hear its events, then post.</summary>
        public void Hear()
        {
            Events.AfterStep(World, Player);
            Events.Update(World, Player, Feed, Now, names: Names);
        }

        public void Step(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                World.Advance(_ => default);
                Hear();
            }
        }

        public Notification? Toast(string key) => Feed.Active.SingleOrDefault(n => n.Key == key);

        /// <summary>A shell of <paramref name="killer"/>'s destroys the tank in slot <paramref name="victim"/>, and the guidance hears of it.</summary>
        public void Kill(int killer, int victim)
        {
            ArcadeWorlds.Kill(World, killer, World.TankList.Tanks[victim]);
            Hear();
        }
    }

    private static Match Timed(int seconds = 480, int slot = 0) => new() { World = Create(ArcadeWorlds.Timed(seconds)), Slot = slot };

    private static Match WithDrops(MatchRules? rules = null, Func<int, string>? names = null) =>
        new() { World = PowerupWorlds.Create(rules), Names = names };

    [Fact]
    public void ATimedMatchWithDrops_TellsItsRulesOnce_AsItStarts()
    {
        var m = WithDrops(PowerupWorlds.Rules with { TimeLimitSeconds = 480 });
        m.Step();

        Notification rules = m.Toast("arcade-rules")!;
        Assert.Equal("Arcade match", rules.Title);
        Assert.Equal($"Most kills in 8:00 wins. Drops land every {Tweaks.Powerups.DropPeriodSeconds} s - watch the marker.", rules.Detail);
        Assert.Equal(NotificationKind.Info, rules.Kind);

        // Read and gone, it is not said again.
        m.Feed.Update(m.Now + 60f);
        m.Step(5);
        Assert.Null(m.Toast("arcade-rules"));
    }

    [Fact]
    public void TheRulesToast_SaysOnlyWhatTheMatchHas()
    {
        var timed = Timed(90);
        timed.Step();
        Assert.Equal(("Arcade match", "Most kills in 1:30 wins."), (timed.Toast("arcade-rules")!.Title, timed.Toast("arcade-rules")!.Detail));

        var drops = WithDrops();
        drops.Step();
        Assert.Equal("Powerup drops", drops.Toast("arcade-rules")!.Title);
        Assert.StartsWith("Drops land every", drops.Toast("arcade-rules")!.Detail);

        var plain = new Match { World = Create(new MatchRules { TerrainRegrowth = false }) };
        plain.Step(3);
        Assert.Empty(plain.Feed.Active);
    }

    [Fact]
    public void AMatchJoinedLate_DoesNotAnnounceItsRules()
    {
        var m = Timed();
        for (int i = 0; i < 6 * Fps; i++)
            m.World.Advance(_ => default);

        m.Step(3);

        Assert.Null(m.Toast("arcade-rules"));
    }

    [Fact]
    public void ADropLanding_IsTold_WithWhatItDoesAndHowFar()
    {
        var m = WithDrops();
        while (m.World.PowerupDropCount == 0)
            m.Step();

        // The forecast has moved on to the next drop; the toast is about the one on the ground.
        Span<PowerupRenderState> drops = stackalloc PowerupRenderState[1];
        m.World.CopyPowerupRenderStates(drops);
        var kind = PowerupPresentations.Get(drops[0].Kind);
        Notification landed = m.Toast("drop-landed")!;
        Assert.Equal($"{kind.Name} landed", landed.Title);
        Assert.StartsWith(kind.Effect, landed.Detail);
        Assert.Matches(" [0-9]+ m away\\.$", landed.Detail);
        Assert.Equal(NotificationKind.Info, landed.Kind);
    }

    [Fact]
    public void WhoTookADrop_IsNamed_AndThePlayersOwnIsGoodNews()
    {
        var m = WithDrops();
        m.Step();
        PowerupWorlds.Take(m.World, m.World.TankList.Tanks[1], PowerupKind.Drill);
        m.Hear();

        Notification theirs = m.Toast("drop-taken")!;
        Assert.Equal(("BOT 2 took Drill", PowerupPresentations.Drill.Effect, NotificationKind.Warning), (theirs.Title, theirs.Detail, theirs.Kind));

        PowerupWorlds.Take(m.World, m.Player, PowerupKind.Overcharge);
        m.Hear();

        // One toast for drops taken: the next replaces the last.
        Notification own = m.Toast("drop-taken")!;
        Assert.Equal(("You took Overcharge", NotificationKind.Good), (own.Title, own.Kind));
    }

    [Fact]
    public void InANetworkMatch_ToastsUseThePlayersNames()
    {
        var m = WithDrops(PowerupWorlds.Rules with { TimeLimitSeconds = 480 }, names: slot => slot == 1 ? "Ada" : "Grace");
        m.Step();
        PowerupWorlds.Take(m.World, m.World.TankList.Tanks[1], PowerupKind.Bulwark);
        m.Hear();

        Assert.Equal("Ada took Bulwark", m.Toast("drop-taken")!.Title);
    }

    [Fact]
    public void AKill_SaysWhoDestroyedWhom_AndWhereThatPutsTheKiller()
    {
        var m = Timed();
        m.Step();

        m.Kill(killer: 1, victim: 2);

        Notification kill = m.Toast("kill-feed")!;
        Assert.Equal(("BOT 2 destroyed BOT 3", "1 kill, 1st place", NotificationKind.Info), (kill.Title, kill.Detail, kill.Kind));
        Assert.Null(m.Toast("kill-own"));
    }

    [Fact]
    public void KillsOfOthers_ShareOneToast_ThatTheNextReplaces()
    {
        var m = Timed();
        m.Step();

        m.Kill(killer: 1, victim: 2);
        m.Kill(killer: 3, victim: 2);
        m.Kill(killer: 1, victim: 3);

        Notification kill = Assert.Single(m.Feed.Active, n => n.Key == "kill-feed");
        Assert.Equal(("BOT 2 destroyed BOT 4", "2 kills, 1st place"), (kill.Title, kill.Detail));
        Assert.True(m.Feed.Active.Count(n => n.Key.StartsWith("kill", StringComparison.Ordinal)) == 1);
    }

    [Fact]
    public void ThePlayersOwnKillsAndDeaths_HaveAToastOfTheirOwn()
    {
        var m = Timed();
        m.Step();

        m.Kill(killer: 0, victim: 1);
        Notification won = m.Toast("kill-own")!;
        Assert.Equal(("You destroyed BOT 2", "1 kill, 1st place", NotificationKind.Good), (won.Title, won.Detail, won.Kind));

        m.Kill(killer: 2, victim: 0);
        Notification lost = m.Toast("kill-own")!;
        Assert.Equal(("BOT 3 destroyed you", NotificationKind.Danger), (lost.Title, lost.Kind));
        Assert.Null(m.Toast("kill-feed"));
    }

    [Fact]
    public void ADeathNobodyScored_IsNoKillToast()
    {
        var m = Timed();
        m.Step();

        Perish(m.World, m.World.TankList.Tanks[1], DamageCause.Overheat);
        m.Hear();

        Assert.DoesNotContain(m.Feed.Active, n => n.Key.StartsWith("kill", StringComparison.Ordinal));
    }

    [Fact]
    public void TheLead_IsAnnouncedWhenItChangesHands_NotWhenItIsFirstTaken()
    {
        var m = Timed();
        m.Step();

        // The first kill of the match is the first lead: its own toast says so.
        m.Kill(killer: 1, victim: 2);
        Assert.Null(m.Toast("lead"));
        // Level again: nobody leads, and nothing is announced.
        m.Kill(killer: 3, victim: 2);
        Assert.Null(m.Toast("lead"));
        // The same tank ahead again is no change of hands.
        m.Kill(killer: 1, victim: 2);
        Assert.Null(m.Toast("lead"));

        m.Kill(killer: 3, victim: 2);
        m.Kill(killer: 3, victim: 2);

        Notification lead = m.Toast("lead")!;
        Assert.Equal(("BOT 4 takes the lead", NotificationKind.Warning), (lead.Title, lead.Kind));
        Assert.Matches("^3 kills\\. [0-9]:[0-9][0-9] left\\.$", lead.Detail);
    }

    [Fact]
    public void ThePlayerTakingTheLead_IsGoodNews()
    {
        var m = Timed();
        m.Step();
        m.Kill(killer: 1, victim: 2);

        m.Kill(killer: 0, victim: 2);
        m.Kill(killer: 0, victim: 3);

        Notification lead = m.Toast("lead")!;
        Assert.Equal(("You take the lead", NotificationKind.Good), (lead.Title, lead.Kind));
        Assert.StartsWith("2 kills.", lead.Detail);
    }

    [Fact]
    public void TheLastMinute_IsTold_WithWhoLeads()
    {
        var tied = Timed(62);
        tied.Step();
        Assert.Null(tied.Toast("clock"));
        while (tied.World.MatchClock.RemainingSeconds > 60)
            tied.Step();
        Assert.Equal(("One minute left", "The top score is tied: a tie at the end means sudden death.", NotificationKind.Warning),
            (tied.Toast("clock")!.Title, tied.Toast("clock")!.Detail, tied.Toast("clock")!.Kind));

        var led = Timed(90);
        led.Step();
        led.Kill(killer: 2, victim: 1);
        while (led.World.MatchClock.RemainingSeconds > 60)
            led.Step();
        Assert.Equal("BOT 3 leads with 1 kill.", led.Toast("clock")!.Detail);

        var own = Timed(90);
        own.Step();
        own.Kill(killer: 0, victim: 1);
        while (own.World.MatchClock.RemainingSeconds > 60)
            own.Step();
        Assert.Equal("You lead with 1 kill.", own.Toast("clock")!.Detail);
    }

    [Fact]
    public void SuddenDeath_IsTold_AndSaysHowTheMatchEnds()
    {
        var m = Timed(3);
        m.Step();
        while (!m.World.MatchClock.IsSuddenDeath)
            m.Step();

        Notification sudden = m.Toast("clock-sudden")!;
        Assert.Equal(("Sudden death", NotificationKind.Danger), (sudden.Title, sudden.Kind));
        Assert.Contains("ends the moment one tank leads alone", sudden.Detail);
    }

    [Fact]
    public void AClockAlreadyInItsLastMinuteWhenJoined_IsNoNews()
    {
        var m = Timed(62);
        for (int i = 0; i < 10 * Fps; i++)
            m.World.Advance(_ => default);

        m.Step(3);

        Assert.Null(m.Toast("clock"));
    }

    [Fact]
    public void InATimedMatch_TheTutorialNamesTheScoreAsTheGoal_NotTheCore()
    {
        var timed = Create(ArcadeWorlds.Timed(480));
        var plain = Create(new MatchRules { TerrainRegrowth = false });
        var tank = timed.TankList.Tanks[0];

        TutorialPrompt first = new TutorialDirector().Update(timed, tank, 0f)!.Value;
        Assert.Equal("Leave the base", first.Title);
        Assert.Contains("most kills when the clock runs out", first.Body);
        Assert.DoesNotContain("core", first.Body);
        Assert.Contains("tap the hot core", new TutorialDirector().Update(plain, plain.TankList.Tanks[0], 0f)!.Value.Body);
    }
}
