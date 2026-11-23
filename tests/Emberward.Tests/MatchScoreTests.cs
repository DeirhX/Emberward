using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities;
using Emberward.Core.Match;
using static Emberward.Tests.ArcadeWorlds;

namespace Emberward.Tests;

/// <summary>
/// The timed score match (<see cref="MatchRules.TimeLimitSeconds"/>, <see cref="MatchScore"/>, DESIGN.md §6.8): who a
/// <see cref="StepEventKind.TankDied"/> names as the killer, the kills and deaths the board keeps, the clock, the win
/// of the sole leader when it runs out, and sudden death while the top score is tied.
/// </summary>
public class MatchScoreTests
{
    [Fact]
    public void AShellKill_NamesTheTankWhoseShellItWas()
    {
        // Not behind a flag: the event says who killed in every match.
        var world = Create(new MatchRules { TerrainRegrowth = false });
        var victim = world.TankList.Tanks[1];

        StepEvent died = Kill(world, killer: 2, victim);

        Assert.Equal(victim.Color, died.Color);
        Assert.Equal(2, died.Killer);
        Assert.Equal(DamageCause.EnemyFire, died.Cause);
        Assert.Equal(2, victim.KillerColor);
    }

    [Theory]
    [InlineData(DamageCause.Overheat)]
    [InlineData(DamageCause.Frostbite)]
    [InlineData(DamageCause.Recalled)]
    public void ADeathNoShellCaused_NamesNoKiller(DamageCause cause)
    {
        var world = Create(new MatchRules { TerrainRegrowth = false });
        var tank = world.TankList.Tanks[1];

        StepEvent died = Perish(world, tank, cause);

        Assert.Equal(tank.Color, died.Color);
        Assert.Equal(-1, died.Killer);
        Assert.Equal(cause, died.Cause);
        Assert.Equal(-1, tank.KillerColor);
    }

    [Fact]
    public void ATanksOwnShell_NamesNoKiller_AndGivesItNoClaim()
    {
        var world = Create(new MatchRules { TerrainRegrowth = false });
        var tank = world.TankList.Tanks[0];

        Expose(world, tank);
        Assert.True(tank.TakeDamage(Lethal, DamageCause.EnemyFire, attacker: tank.Color));
        world.Advance(_ => default);
        Assert.Equal(-1, Died(world).Killer);

        // Wounded by its own shell, then finished by the heat: nobody's, and never its own.
        Expose(world, tank);
        Assert.True(tank.TakeDamage(tank.Reactor.Health - 1, DamageCause.EnemyFire, attacker: tank.Color));
        Assert.True(tank.TakeDamage(Lethal, DamageCause.Overheat));
        world.Advance(_ => default);
        Assert.Equal(-1, Died(world).Killer);
        Assert.Equal(-1, tank.KillerColor);

        // Its own shell after an enemy's does not take the enemy's claim away.
        Expose(world, tank);
        Assert.True(tank.TakeDamage(1, DamageCause.EnemyFire, attacker: 2));
        Assert.True(tank.TakeDamage(1, DamageCause.EnemyFire, attacker: tank.Color));
        Assert.True(tank.TakeDamage(Lethal, DamageCause.Emberite));
        world.Advance(_ => default);
        Assert.Equal(2, Died(world).Killer);
    }

    /// <summary>The kill credit: shot by an enemy, then finished by something else within <see cref="Tweaks.Arcade.KillCreditSeconds"/>.</summary>
    [Theory]
    [InlineData(DamageCause.Overheat)]
    [InlineData(DamageCause.Frostbite)]
    [InlineData(DamageCause.Emberite)]
    public void ADeathSoonAfterAnEnemysShell_IsThatEnemysKill_AndItsCauseIsWhatKilled(DamageCause cause)
    {
        var world = Create(Timed(600));
        var victim = world.TankList.Tanks[1];
        Expose(world, victim);

        // Tank 0's shell first, then tank 2's: the last to hurt it has the claim.
        Assert.True(victim.TakeDamage(1, DamageCause.EnemyFire, attacker: 0));
        Assert.True(victim.TakeDamage(1, DamageCause.EnemyFire, attacker: 2));
        for (int step = 0; step < Tweaks.Arcade.KillCreditTicks; step++)
            world.Advance(_ => default);
        Assert.False(victim.IsDead);
        Assert.True(victim.TakeDamage(Lethal, cause));
        world.Advance(_ => default);

        StepEvent died = Died(world);
        Assert.Equal((victim.Color, 2, cause), (died.Color, died.Killer, died.Cause));
        Assert.Equal((2, cause), (victim.KillerColor, victim.DeathCause));
        Assert.Equal(1, world.ScoreOf(world.TankList.Tanks[2]).Kills);
        Assert.Equal(0, world.ScoreOf(world.TankList.Tanks[0]).Kills);
        Assert.Equal(1, world.ScoreOf(victim).Deaths);
    }

    [Fact]
    public void ADeathAfterTheCreditTime_OrByARecall_IsNobodysKill()
    {
        var world = Create(Timed(600));
        var victim = world.TankList.Tanks[1];

        // One step past the time: the shell only wounded it, and the heat's kill is the heat's.
        Expose(world, victim);
        Assert.True(victim.TakeDamage(victim.Reactor.Health - 1, DamageCause.EnemyFire, attacker: 2));
        for (int step = 0; step <= Tweaks.Arcade.KillCreditTicks; step++)
            world.Advance(_ => default);
        Assert.False(victim.IsDead);
        Assert.True(victim.TakeDamage(Lethal, DamageCause.Overheat));
        world.Advance(_ => default);
        StepEvent died = Died(world);
        Assert.Equal((-1, DamageCause.Overheat), (died.Killer, died.Cause));

        // A recall right after a hit is the tank's own doing.
        Expose(world, victim);
        Assert.True(victim.TakeDamage(1, DamageCause.EnemyFire, attacker: 2));
        Assert.True(victim.TakeDamage(Lethal, DamageCause.Recalled));
        world.Advance(_ => default);
        Assert.Equal((-1, DamageCause.Recalled), (Died(world).Killer, Died(world).Cause));

        Assert.Equal(0, world.ScoreOf(world.TankList.Tanks[2]).Kills);
        Assert.Equal(2, world.ScoreOf(victim).Deaths);
    }

    [Fact]
    public void AShellTheRespawnShieldRefuses_GivesNoClaim_AndARespawnEndsOne()
    {
        var world = Create(new MatchRules { TerrainRegrowth = false, QuickRespawn = true });
        var tank = world.TankList.Tanks[1];

        // Hit, killed by the heat (the enemy's kill), and back: the claim died with it.
        Expose(world, tank);
        Assert.True(tank.TakeDamage(1, DamageCause.EnemyFire, attacker: 2));
        Assert.True(tank.TakeDamage(Lethal, DamageCause.Overheat));
        world.Advance(_ => default);
        Assert.Equal(2, Died(world).Killer);
        for (int step = 0; step < Tweaks.Arcade.RespawnSeconds * Fps && tank.IsDead; step++)
            world.Advance(_ => default);
        Assert.False(tank.IsDead);

        // Behind its respawn shield a shell takes nothing, and so claims nothing.
        Assert.True(tank.SpawnProtectionTicks > 0);
        Assert.False(tank.TakeDamage(1, DamageCause.EnemyFire, attacker: 2));
        Assert.True(tank.TakeDamage(Lethal, DamageCause.Overheat));
        world.Advance(_ => default);
        Assert.Equal(-1, Died(world).Killer);
    }

    [Fact]
    public void ARespawnedTank_ForgetsWhoKilledIt()
    {
        var world = Create(new MatchRules { TerrainRegrowth = false, QuickRespawn = true });
        var tank = world.TankList.Tanks[1];
        Kill(world, killer: 0, tank);

        StepEvent died = Perish(world, tank, DamageCause.Overheat);

        Assert.Equal(-1, died.Killer);
    }

    [Fact]
    public void KillsAndDeaths_AreCounted_AndTheBoardIsInRankOrder()
    {
        var world = Create(Timed(600));
        var tanks = world.TankList.Tanks;

        // Before the first kill every tank shares first place, and the board is in colour order.
        Assert.Equal([0, 1, 2, 3], Standings(world).Select(line => line.Color));
        Assert.All(Standings(world), line => Assert.Equal(new TankScore(true, line.Color, 0, 0, Rank: 1), line));
        Assert.Null(world.ScoreLeader);

        Kill(world, killer: 0, tanks[2]);
        Assert.Equal(0, world.ScoreLeader?.Color);
        Kill(world, killer: 3, tanks[0]);
        Assert.Null(world.ScoreLeader);

        // 3 and 0 have a kill each, and 3 has not died; 1 and 2 have none, and 1 has not died.
        Assert.Equal(
            [
                new TankScore(true, Color: 3, Kills: 1, Deaths: 0, Rank: 1),
                new TankScore(true, Color: 0, Kills: 1, Deaths: 1, Rank: 1),
                new TankScore(true, Color: 1, Kills: 0, Deaths: 0, Rank: 3),
                new TankScore(true, Color: 2, Kills: 0, Deaths: 1, Rank: 3),
            ],
            Standings(world));
        foreach (TankScore line in Standings(world))
        {
            Assert.Equal(line, world.ScoreOf(tanks[line.Color]));
            Assert.Equal(line.Kills, line.Score);
        }

        // A death no shell caused is a death and nobody's kill.
        Perish(world, tanks[3], DamageCause.Overheat);
        Assert.Equal(new TankScore(true, Color: 3, Kills: 1, Deaths: 1, Rank: 1), world.ScoreOf(tanks[3]));
        Assert.Equal(2, Standings(world).Sum(line => line.Kills));
        Assert.Equal([0, 3, 1, 2], Standings(world).Select(line => line.Color));
    }

    [Fact]
    public void TheBoard_FillsAShortSpanWithItsTopLines_AndAllocatesNothing()
    {
        var world = Create(Timed(600));
        var tanks = world.TankList.Tanks;
        Kill(world, killer: 2, tanks[1]);
        Kill(world, killer: 2, tanks[3]);

        Span<TankScore> top = stackalloc TankScore[2];
        Assert.Equal(2, world.CopyStandings(top));
        Assert.Equal(new TankScore(true, Color: 2, Kills: 2, Deaths: 0, Rank: 1), top[0]);
        Assert.Equal(new TankScore(true, Color: 0, Kills: 0, Deaths: 0, Rank: 2), top[1]);

        Span<TankScore> all = stackalloc TankScore[8];
        Assert.Equal(4, world.CopyStandings(all));
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int round = 0; round < 10; round++)
        {
            world.CopyStandings(all);
            _ = world.ScoreOf(tanks[round % 4]);
            _ = world.MatchClock;
        }
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void TheClock_CountsDownInTicks_AndItsSecondsRoundUp()
    {
        var world = Create(Timed(2), tanks: 2);

        MatchClock clock = world.MatchClock;
        Assert.True(clock.Active);
        Assert.Equal((0, 2 * Fps, 2, 2 * Fps, 2), (clock.ElapsedTicks, clock.LimitTicks, clock.LimitSeconds, clock.RemainingTicks, clock.RemainingSeconds));

        world.Advance(_ => default);
        clock = world.MatchClock;
        Assert.Equal((1, 2 * Fps - 1, 2), (clock.ElapsedTicks, clock.RemainingTicks, clock.RemainingSeconds));

        RunClockDownTo(world, Fps);
        Assert.Equal(1, world.MatchClock.RemainingSeconds);
        RunClockDownTo(world, 1);
        Assert.Equal(1, world.MatchClock.RemainingSeconds);
        Assert.False(world.MatchClock.IsSuddenDeath);
        Assert.Equal(0, world.MatchClock.OvertimeTicks);
    }

    [Fact]
    public void WhenTheClockRunsOut_TheSoleLeaderHasWon()
    {
        var world = Create(Timed(8));
        var tanks = world.TankList.Tanks;
        Kill(world, killer: 2, tanks[0]);

        RunClockDownTo(world, 1);
        Assert.False(world.IsMatchOver);
        Assert.Null(world.MatchWinner);
        world.Advance(_ => default);

        Assert.True(world.IsMatchOver);
        AssertWon(world, tanks[2], MatchWinReason.TopScore);
        Assert.Equal(MatchOutcome.Victory, world.MatchOutcomeFor(tanks[2]));
        foreach (int loser in (int[])[0, 1, 3])
            Assert.Equal(MatchOutcome.Defeat, world.MatchOutcomeFor(tanks[loser]));
        Assert.False(world.MatchClock.IsSuddenDeath);
        Assert.Equal(0, world.MatchClock.RemainingSeconds);

        // The match is over: the clock and the board stand as they were.
        MatchClock stopped = world.MatchClock;
        TankScore[] final = Standings(world);
        Kill(world, killer: 1, tanks[2]);
        for (int i = 0; i < 20; i++)
            world.Advance(_ => default);
        Assert.Equal(stopped, world.MatchClock);
        Assert.Equal(final, Standings(world));
        AssertWon(world, tanks[2], MatchWinReason.TopScore);
    }

    [Fact]
    public void ATiedClock_GoesToSuddenDeath_AndTheNextKillEndsIt()
    {
        var world = Create(Timed(8));
        var tanks = world.TankList.Tanks;
        Kill(world, killer: 0, tanks[2]);
        Kill(world, killer: 1, tanks[3]);

        RunClockDownTo(world, 0);
        Assert.False(world.IsMatchOver);
        Assert.True(world.MatchClock.IsSuddenDeath);
        for (int i = 0; i < 30; i++)
            world.Advance(_ => default);
        Assert.False(world.IsMatchOver);
        Assert.True(world.MatchClock.IsSuddenDeath);
        Assert.Equal((0, 0, 30), (world.MatchClock.RemainingTicks, world.MatchClock.RemainingSeconds, world.MatchClock.OvertimeTicks));

        // A death that is nobody's kill leaves the tie standing.
        Perish(world, tanks[2], DamageCause.Overheat);
        Assert.False(world.IsMatchOver);

        Kill(world, killer: 1, tanks[0]);
        Assert.True(world.IsMatchOver);
        AssertWon(world, tanks[1], MatchWinReason.TopScore);
        Assert.False(world.MatchClock.IsSuddenDeath);
        Assert.Equal(new TankScore(true, Color: 1, Kills: 2, Deaths: 0, Rank: 1), world.ScoreOf(tanks[1]));
    }

    [Fact]
    public void WithNoKillAtAll_TheClockRunsIntoSuddenDeath_AndTheFirstKillWins()
    {
        var world = Create(Timed(1), tanks: 2);
        var tanks = world.TankList.Tanks;

        RunClockDownTo(world, 0);
        world.Advance(_ => default);
        Assert.False(world.IsMatchOver);
        Assert.True(world.MatchClock.IsSuddenDeath);

        Kill(world, killer: 1, tanks[0]);
        AssertWon(world, tanks[1], MatchWinReason.TopScore);
    }

    [Fact]
    public void ASideWhoseBaseFell_CannotWinOnScore()
    {
        // Bases can fall here (no BasesHold): the leader loses its base before the clock runs out.
        var world = Create(Timed(8));
        var tanks = world.TankList.Tanks;
        Kill(world, killer: 0, tanks[1]);
        tanks[0].Base!.TakeDamage(tanks[0].Base!.Reactor.HealthCapacity);
        Assert.Null(world.ScoreLeader);

        RunClockDownTo(world, 0);
        Assert.False(world.IsMatchOver);
        Assert.True(world.MatchClock.IsSuddenDeath);

        Kill(world, killer: 3, tanks[2]);
        AssertWon(world, tanks[3], MatchWinReason.TopScore);
    }

    [Fact]
    public void AMatchWonAnotherWayInSuddenDeath_StopsTheClock_AndIsInSuddenDeathNoLonger()
    {
        var world = Create(Timed(1), tanks: 2);
        var tanks = world.TankList.Tanks;
        RunClockDownTo(world, 0);
        Assert.True(world.MatchClock.IsSuddenDeath);

        tanks[1].Base!.TakeDamage(tanks[1].Base!.Reactor.HealthCapacity);
        for (int i = 0; i < 10; i++)
            world.Advance(_ => default);

        AssertWon(world, tanks[0], MatchWinReason.LastBaseStanding);
        Assert.False(world.MatchClock.IsSuddenDeath);
        Assert.Equal(1, world.MatchClock.OvertimeTicks);
    }

    [Fact]
    public void WithoutATimeLimit_TheWorldKeepsNoScoreAndNoClock()
    {
        var world = Create(new MatchRules { TerrainRegrowth = false });
        var tanks = world.TankList.Tanks;
        Kill(world, killer: 0, tanks[1]);
        for (int i = 0; i < 2 * Fps; i++)
            world.Advance(_ => default);

        Assert.Equal(default, world.ScoreOf(tanks[0]));
        Assert.False(world.ScoreOf(tanks[0]).Active);
        Assert.Equal(default, world.MatchClock);
        Assert.False(world.MatchClock.Active);
        Assert.Equal(0, world.CopyStandings(new TankScore[4]));
        Assert.Null(world.ScoreLeader);
        Assert.False(world.IsMatchOver);
    }

    [Fact]
    public void TheScore_IsInTheStateHash_OnlyWhereThereIsOne()
    {
        // The same world with and without a clock hashes differently once built, and a kill moves the hash on.
        var plain = Create(new MatchRules { TerrainRegrowth = false });
        var timed = Create(Timed(600));
        Assert.NotEqual(WorldStateHash.Compute(plain), WorldStateHash.Compute(timed));

        var (a, b) = (Create(Timed(600)), Create(Timed(600)));
        Kill(a, killer: 0, a.TankList.Tanks[1]);
        Kill(b, killer: 2, b.TankList.Tanks[1]);
        Assert.NotEqual(WorldStateHash.Compute(a), WorldStateHash.Compute(b));
    }

    [Fact]
    public void MidMatch_ASnapshotRoundTripsAndPlaysOnAlike()
    {
        static World Build() => Create(MatchRules.Arcade with { TerrainRegrowth = false, TimeLimitSeconds = 12 });
        var world = Build();
        var tanks = world.TankList.Tanks;
        Kill(world, killer: 0, tanks[1]);
        Kill(world, killer: 0, tanks[2]);
        Kill(world, killer: 3, tanks[0]); // 0 lies dead, killed by 3, when the snapshot is taken
        Assert.True(tanks[0].IsDead);
        Assert.False(world.IsMatchOver);

        // Played on past the end of the clock: the loaded world names the same winner off the tallies it loaded.
        int ticks = world.MatchClock.RemainingTicks + 10;
        World loaded = SnapshotRoundTrip.AssertPlaysOnAlike(world, [null, null, null, null], Build, ticks, (_, _) => default);

        Assert.True(world.IsMatchOver);
        AssertWon(loaded, loaded.TankList.Tanks[0], MatchWinReason.TopScore);
        Assert.Equal(Standings(world), Standings(loaded));
        Assert.Equal(world.MatchClock, loaded.MatchClock);
        Assert.Equal(new TankScore(true, Color: 0, Kills: 2, Deaths: 1, Rank: 1), loaded.ScoreOf(loaded.TankList.Tanks[0]));
    }

    [Fact]
    public void TheSameMatch_GivesTheSameStandings()
    {
        static World Play()
        {
            var world = Create(MatchRules.Arcade with { TerrainRegrowth = false, TimeLimitSeconds = 40 });
            var tanks = world.TankList.Tanks;
            Kill(world, killer: 1, tanks[0]);
            Kill(world, killer: 2, tanks[1]);
            Kill(world, killer: 1, tanks[3]);
            Kill(world, killer: 0, tanks[2]);
            Kill(world, killer: 1, tanks[0]); // 0 has come back by now: the quick respawn
            RunClockDownTo(world, 0);
            return world;
        }

        var (first, second) = (Play(), Play());

        Assert.True(first.IsMatchOver);
        Assert.Equal(1, first.MatchWinner?.Tank.Color);
        Assert.Equal(Standings(first), Standings(second));
        Assert.Equal(first.MatchClock, second.MatchClock);
        Assert.Equal(WorldStateHash.Compute(first), WorldStateHash.Compute(second));
        Assert.Equal(WorldStateHash.ComputeSync(first), WorldStateHash.ComputeSync(second));
    }
}
