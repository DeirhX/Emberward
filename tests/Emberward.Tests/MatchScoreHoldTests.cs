using Emberward.Core;
using Emberward.Core.Entities;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Match;
using Emberward.Core.Types;
using Emberward.Desktop.Audio;
using Emberward.Desktop.Gui;
using static Emberward.Tests.ArcadeWorlds;

namespace Emberward.Tests;

/// <summary>
/// A death told a step late on the deciding tick of a timed match (<see cref="MatchScore"/>, DESIGN.md §6.8): a tank
/// destroyed after its own tick (here by heat at its end) is told on its next one, and the score holds the decision
/// for that step at 0:00, not yet in sudden death, so a kill credited on the last tick still counts.
/// </summary>
public class MatchScoreHoldTests
{
    [Fact]
    public void AKillToldLate_OnTheLastTick_Counts_AndTiesTheMatchIntoSuddenDeath()
    {
        var world = Create(Timed(60), tanks: 3);
        var (a, b, c) = (world.TankList.Tanks[0], world.TankList.Tanks[1], world.TankList.Tanks[2]);
        Kill(world, killer: 0, c);

        // B's shell hurts C a little before the end: C's death in the next five seconds is B's kill.
        Position spot = Expose(world, c);
        RunHeldDownTo(world, c, spot, 100);
        Assert.True(c.TakeDamage(5, DamageCause.EnemyFire, attacker: 1));
        RunHeldDownTo(world, c, spot, 1);

        OverheatOnTheLastTick(world, c);
        Assert.True(c.IsDead);
        Assert.True(c.IsDeathUntold);
        AssertHeldAtZero(world);

        world.Advance(_ => default);
        StepEvent died = Died(world);
        Assert.Equal((c.Color, 1), (died.Color, died.Killer));
        Assert.False(world.IsMatchOver);
        Assert.True(world.MatchClock.IsSuddenDeath);
        Assert.Equal(0, world.MatchClock.OvertimeTicks);
        Assert.Equal((1, 1, 2), (world.ScoreOf(a).Kills, world.ScoreOf(b).Kills, world.ScoreOf(c).Deaths));

        // Overtime is counted from the start of sudden death.
        world.Advance(_ => default);
        Assert.Equal(1, world.MatchClock.OvertimeTicks);
    }

    [Fact]
    public void AHeldStepThatEndsDecided_NeverReportsSuddenDeath()
    {
        var world = Create(Timed(60), tanks: 3);
        var (a, c) = (world.TankList.Tanks[0], world.TankList.Tanks[2]);
        Kill(world, killer: 0, world.TankList.Tanks[1]);

        Position spot = Expose(world, c);
        RunHeldDownTo(world, c, spot, 100);
        Assert.True(c.TakeDamage(5, DamageCause.EnemyFire, attacker: 0));
        RunHeldDownTo(world, c, spot, 1);

        var phases = new List<ClockPhase>();
        OverheatOnTheLastTick(world, c);
        phases.Add(Scoreboards.PhaseOf(world.MatchClock, world.IsMatchOver));
        AssertHeldAtZero(world);

        world.Advance(_ => default);
        phases.Add(Scoreboards.PhaseOf(world.MatchClock, world.IsMatchOver));
        AssertWon(world, a, MatchWinReason.TopScore);
        Assert.Equal(2, world.ScoreOf(a).Kills);
        Assert.False(world.MatchClock.IsSuddenDeath);
        Assert.Equal("0:00", Scoreboards.ClockText(world.MatchClock));
        Assert.Equal([ClockPhase.LastSeconds, ClockPhase.Final], phases);
        // The clock goes from its countdown straight to the horn: no sudden-death alarm in between.
        Assert.Equal(SoundEffect.MatchHorn, StepEventSounds.ClockCue(phases[0], phases[1], world.MatchClock)?.Effect);
    }

    [Fact]
    public void AShellKillOnTheLastTick_StillDecidesOnThatTick()
    {
        var world = Create(Timed(60), tanks: 3);
        var (b, c) = (world.TankList.Tanks[1], world.TankList.Tanks[2]);
        Position spot = Expose(world, c);
        RunHeldDownTo(world, c, spot, 1);

        // A shell lands before the tanks tick (the projectiles step first), so the death is told in the same step.
        Assert.True(c.TakeDamage(Lethal, DamageCause.EnemyFire, attacker: 1));
        world.Advance(_ => default);

        Assert.Equal(1, Died(world).Killer);
        AssertWon(world, b, MatchWinReason.TopScore);
        Assert.Equal(0, world.MatchClock.RemainingTicks);
        Assert.False(world.MatchClock.IsSuddenDeath);
    }

    [Fact]
    public void WithNoDeathUntold_TheClockDecidesOnItsLastTick()
    {
        var world = Create(Timed(8), tanks: 3);
        Kill(world, killer: 2, world.TankList.Tanks[0]);
        Assert.DoesNotContain(world.TankList.Tanks, t => t.IsDeathUntold);

        RunClockDownTo(world, 1);
        Assert.False(world.IsMatchOver);
        world.Advance(_ => default);

        AssertWon(world, world.TankList.Tanks[2], MatchWinReason.TopScore);
        Assert.Equal((8 * Fps, 0), (world.MatchClock.ElapsedTicks, world.MatchClock.OvertimeTicks));
    }

    /// <summary>Steps the world down to <paramref name="remainingTicks"/> on the clock, keeping <paramref name="tank"/> at <paramref name="spot"/>.</summary>
    private static void RunHeldDownTo(World world, Tank tank, Position spot, int remainingTicks)
    {
        Bounded.Until(() => world.MatchClock.RemainingTicks <= remainingTicks, () =>
        {
            tank.Position = spot;
            world.Advance(_ => default);
        }, world.MatchClock.RemainingTicks - remainingTicks + 1, $"{remainingTicks} ticks left on the clock");
        Assert.False(tank.IsDead);
    }

    /// <summary>The last tick of the clock, at whose end <paramref name="tank"/> overheats with one point of hull left.</summary>
    private static void OverheatOnTheLastTick(World world, Tank tank)
    {
        Assert.Equal(1, world.MatchClock.RemainingTicks);
        Assert.True(tank.TakeDamage((int)tank.Reactor.Health - 1, DamageCause.Overheat));
        tank.Heat = 5000;
        world.Advance(_ => default);
        Assert.DoesNotContain(world.StepEvents.ToArray(), e => e.Kind == StepEventKind.TankDied);
    }

    /// <summary>The step is held: the match goes on, the clock reads 0:00 and is not in sudden death.</summary>
    private static void AssertHeldAtZero(World world)
    {
        Assert.False(world.IsMatchOver);
        MatchClock clock = world.MatchClock;
        Assert.False(clock.IsSuddenDeath);
        Assert.Equal((0, 0), (clock.RemainingTicks, clock.OvertimeTicks));
        Assert.Equal("0:00", Scoreboards.ClockText(clock));
        Assert.Equal(ClockPhase.LastSeconds, Scoreboards.PhaseOf(clock, world.IsMatchOver));
    }
}
