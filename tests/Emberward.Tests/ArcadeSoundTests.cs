using Emberward.Core;
using Emberward.Core.Entities;
using Emberward.Core.Match;
using Emberward.Desktop.Audio;
using Emberward.Desktop.Config;
using Emberward.Desktop.Gui;
using static Emberward.Tests.ArcadeWorlds;

namespace Emberward.Tests;

/// <summary>
/// What a timed match and its drops sound like (<see cref="StepEventSounds"/>, DESIGN.md §6.8), checked without a
/// sound device: a drop's announcements reach every player, taking one and scoring a kill are the player's own
/// reward, and the clock warns, ticks and ends.
/// </summary>
public class ArcadeSoundTests
{
    private static MatchClock Clock(int remainingTicks, bool suddenDeath = false) =>
        new(Active: true, ElapsedTicks: 480 * Fps - remainingTicks, LimitTicks: 480 * Fps, suddenDeath);

    private static StepEvent Died(int victim, int killer) =>
        new(StepEventKind.TankDied, default, Color: victim, Cause: DamageCause.EnemyFire, Killer: killer);

    [Theory]
    [InlineData(StepEventKind.PowerupAnnounced)]
    [InlineData(StepEventKind.PowerupLanded)]
    [InlineData(StepEventKind.PowerupCollected)]
    [InlineData(StepEventKind.PowerupExpired)]
    public void ADropsAnnouncements_AreHeardByEveryone_WhereverTheyAre(StepEventKind kind)
    {
        StepEventCue cue = StepEventSounds.CueFor(kind, default)!.Value;

        Assert.Equal(CueHeard.Everyone, cue.Heard);
        Assert.True(StepEventSounds.Hears(cue, tracked: false, isPlayer: false));
        Assert.True(StepEventSounds.Hears(cue, tracked: true, isPlayer: true));
    }

    [Fact]
    public void ADropRunningOut_IsTheAnnouncementAgain_LowerAndQuieter()
    {
        StepEventCue announced = StepEventSounds.CueFor(StepEventKind.PowerupAnnounced, default)!.Value;
        StepEventCue expired = StepEventSounds.CueFor(StepEventKind.PowerupExpired, default)!.Value;

        Assert.Equal(announced.Effect, expired.Effect);
        Assert.True(expired.Gain < announced.Gain);
        Assert.True(expired.Pitch < announced.Pitch);
    }

    [Fact]
    public void TheLanding_IsTheLoudestOfADropsAnnouncements()
    {
        float landed = StepEventSounds.CueFor(StepEventKind.PowerupLanded, default)!.Value.Gain;

        Assert.True(landed > StepEventSounds.CueFor(StepEventKind.PowerupAnnounced, default)!.Value.Gain);
        Assert.True(landed > StepEventSounds.CueFor(StepEventKind.PowerupCollected, default)!.Value.Gain);
    }

    [Fact]
    public void TakingADropYourself_SoundsLikeAReward_NotLikeAChanceGone()
    {
        var taken = new StepEvent(StepEventKind.PowerupCollected, default, Color: 0);

        StepEventCue own = StepEventSounds.PlayerCueFor(taken)!.Value;

        Assert.Equal(SoundEffect.DropGained, own.Effect);
        Assert.NotEqual(StepEventSounds.CueFor(taken)!.Value.Effect, own.Effect);
        Assert.True(own.Gain > 0f);
        Assert.True(StepEventSounds.Hears(own, tracked: true, isPlayer: true));
    }

    [Fact]
    public void OnlyTakingADrop_HasASoundOfThePlayersOwn()
    {
        foreach (var kind in Enum.GetValues<StepEventKind>())
            if (kind != StepEventKind.PowerupCollected)
                Assert.Null(StepEventSounds.PlayerCueFor(new StepEvent(kind, default, Color: 0)));
    }

    [Fact]
    public void AKillOfThePlayers_IsConfirmed_AndNoOtherDeathIs()
    {
        StepEventCue kill = StepEventSounds.KillCueFor(Died(victim: 2, killer: 0), playerColor: 0)!.Value;

        Assert.Equal((SoundEffect.KillConfirm, AudioTweaks.KillConfirmGain), (kill.Effect, kill.Gain));
        Assert.True(StepEventSounds.Hears(kill, tracked: false, isPlayer: false));
        // Somebody else's kill, the player's own death, a death nobody scored, the player's own shell, no player.
        Assert.Null(StepEventSounds.KillCueFor(Died(victim: 2, killer: 1), playerColor: 0));
        Assert.Null(StepEventSounds.KillCueFor(Died(victim: 0, killer: 1), playerColor: 0));
        Assert.Null(StepEventSounds.KillCueFor(Died(victim: 2, killer: -1), playerColor: 0));
        Assert.Null(StepEventSounds.KillCueFor(Died(victim: 0, killer: 0), playerColor: 0));
        Assert.Null(StepEventSounds.KillCueFor(Died(victim: 2, killer: -1), playerColor: -1));
        Assert.Null(StepEventSounds.KillCueFor(new StepEvent(StepEventKind.TankHit, default, Color: 2, Killer: 0), playerColor: 0));
    }

    [Fact]
    public void AShellKillInAWorld_IsTheKillersToHear()
    {
        var world = Create(Timed(60));

        StepEvent died = Kill(world, killer: 0, world.TankList.Tanks[1]);

        Assert.NotNull(StepEventSounds.KillCueFor(died, playerColor: 0));
        Assert.Null(StepEventSounds.KillCueFor(died, playerColor: 1));
        Assert.Null(StepEventSounds.KillCueFor(died, playerColor: 2));
    }

    [Theory]
    [InlineData(ClockPhase.Running, ClockPhase.LastMinute, SoundEffect.ClockWarning)]
    [InlineData(ClockPhase.LastSeconds, ClockPhase.SuddenDeath, SoundEffect.SuddenDeath)]
    [InlineData(ClockPhase.LastSeconds, ClockPhase.Final, SoundEffect.MatchHorn)]
    [InlineData(ClockPhase.SuddenDeath, ClockPhase.Final, SoundEffect.MatchHorn)]
    [InlineData(ClockPhase.Running, ClockPhase.Final, SoundEffect.MatchHorn)]
    public void TheClock_SoundsWhenItsPhaseChanges(ClockPhase before, ClockPhase now, SoundEffect expected)
    {
        StepEventCue cue = StepEventSounds.ClockCue(before, now, Clock(now == ClockPhase.LastMinute ? 60 * Fps : 0))!.Value;

        Assert.Equal(expected, cue.Effect);
        Assert.Equal(CueHeard.Everyone, cue.Heard);
        Assert.True(cue.Gain > 0f);
    }

    [Theory]
    [InlineData(ClockPhase.Running)]
    [InlineData(ClockPhase.LastMinute)]
    [InlineData(ClockPhase.SuddenDeath)]
    [InlineData(ClockPhase.Final)]
    public void AClockThatStaysInItsPhase_IsSilent(ClockPhase phase) =>
        Assert.Null(StepEventSounds.ClockCue(phase, phase, Clock(30 * Fps)));

    [Fact]
    public void TheLastSeconds_TickOnceASecond_HigherForTheLastThree()
    {
        var ticks = new List<(int Second, float Pitch)>();
        ClockPhase before = ClockPhase.LastMinute;
        for (int remaining = Scoreboards.CountdownSeconds * Fps + 5; remaining > 0; remaining--)
        {
            MatchClock clock = Clock(remaining);
            ClockPhase now = Scoreboards.PhaseOf(clock, matchOver: false);
            if (StepEventSounds.ClockCue(before, now, clock) is { } cue)
            {
                Assert.Equal((SoundEffect.ClockTick, AudioTweaks.ClockTickGain), (cue.Effect, cue.Gain));
                ticks.Add((clock.RemainingSeconds, cue.Pitch));
            }
            before = now;
        }

        Assert.Equal(Enumerable.Range(1, Scoreboards.CountdownSeconds).Reverse(), ticks.Select(t => t.Second));
        Assert.All(ticks, t => Assert.Equal(t.Second <= 3 ? AudioTweaks.ClockLastTicksPitch : 1f, t.Pitch));
    }

    [Fact]
    public void ATimedMatchPlayedThrough_SoundsItsWarning_ItsTicks_SuddenDeath_AndTheEnd()
    {
        var world = Create(Timed(62));
        var heard = new List<SoundEffect>();
        ClockPhase before = Scoreboards.PhaseOf(world.MatchClock, world.IsMatchOver);

        void Listen()
        {
            ClockPhase now = Scoreboards.PhaseOf(world.MatchClock, world.IsMatchOver);
            if (StepEventSounds.ClockCue(before, now, world.MatchClock) is { } cue)
                heard.Add(cue.Effect);
            before = now;
        }

        while (!world.MatchClock.IsSuddenDeath)
        {
            world.Advance(_ => default);
            Listen();
        }
        // Sudden death goes on in silence until a kill ends it. The steps the kill takes are not listened to one by
        // one, so the end is heard as the change from sudden death to the final board.
        Kill(world, killer: 1, world.TankList.Tanks[0]);
        Listen();

        Assert.Equal(SoundEffect.ClockWarning, heard[0]);
        Assert.Equal(Scoreboards.CountdownSeconds, heard.Count(e => e == SoundEffect.ClockTick));
        Assert.Equal([SoundEffect.SuddenDeath, SoundEffect.MatchHorn], heard.TakeLast(2));
        Assert.Equal(1 + Scoreboards.CountdownSeconds + 2, heard.Count);
    }
}
