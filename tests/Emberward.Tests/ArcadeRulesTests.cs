using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities;
using Emberward.Core.Entities.Bases;
using Emberward.Core.Entities.Projectiles;
using Emberward.Core.Match;
using Emberward.Core.Types;
using static Emberward.Tests.ArcadeWorlds;

namespace Emberward.Tests;

/// <summary>
/// The rules of arcade mode beside the score (DESIGN.md §6.8): bases that hold (<see cref="MatchRules.BasesHold"/>),
/// the quick respawn (<see cref="MatchRules.QuickRespawn"/>), and the presets a host picks by
/// <see cref="MatchMode"/> (<see cref="MatchRules.ForMode"/>).
/// </summary>
public class ArcadeRulesTests
{
    /// <summary>
    /// An enemy shell from a corner of <paramref name="target"/>'s courtyard along the diagonal, at its core. Returns
    /// whether a step said the base was damaged or destroyed before the shell was gone.
    /// </summary>
    private static bool FireAtCore(World world, TankBase target, int owner)
    {
        world.Projectiles.Add(Projectile.CreateBullet(
            target.Position + BaseLayout.NearestCorner(-1, -1), new VectorF(1, 1), owner));
        for (int tick = 1; tick <= BaseLayout.Lane; tick++)
        {
            world.Advance(_ => default);
            if (world.StepEvents.ToArray().Any(e => e.Kind is StepEventKind.BaseDamaged or StepEventKind.BaseDestroyed))
                return true;
        }
        return false;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WhereBasesHold_ACoreUnderFireTakesNothing_ThatFallsWithoutTheRule(bool energyNetwork)
    {
        var rules = new MatchRules { TerrainRegrowth = false, EnergyNetwork = energyNetwork };

        // Without the rule: a core on its last points falls to one shell, and its side is out.
        var falls = Create(rules, tanks: 2);
        var lost = falls.TankBases.Bases[1];
        lost.TakeDamage(Tweaks.Base.InitialHealth - 50);
        Assert.True(FireAtCore(falls, lost, owner: 0));
        Assert.True(lost.IsDestroyed);
        Assert.True(falls.IsMatchOver);

        // With it: the same core takes shell after shell and loses nothing.
        var holds = Create(rules with { BasesHold = true }, tanks: 2);
        var target = holds.TankBases.Bases[1];
        target.TakeDamage(Tweaks.Base.InitialHealth - 50);
        int health = target.Reactor.Health;
        for (int shell = 0; shell < 5; shell++)
        {
            Assert.False(FireAtCore(holds, target, owner: 0), "a core that holds reports no damage");
            Assert.True(target.Reactor.Health >= health, "and loses none (it may repair meanwhile)");
            health = target.Reactor.Health;
        }

        Assert.False(target.IsDestroyed);
        Assert.False(holds.IsMatchOver);
        Assert.Equal(MatchOutcome.InProgress, holds.MatchOutcomeFor(holds.TankList.Tanks[1]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WhereBasesHold_NoTankReckonsItCanBreakABase(bool energyNetwork)
    {
        var rules = new MatchRules { TerrainRegrowth = false, EnergyNetwork = energyNetwork };

        // A dark base with a core on its last points: as breakable as a base gets.
        static (World World, TankBase Target) Weakened(MatchRules rules)
        {
            var world = Create(rules, tanks: 2);
            var target = world.TankList.Tanks[0].Base!;
            target.DrawCharge(target.Charge);
            target.TakeDamage(Tweaks.Base.InitialHealth - 50);
            return (world, target);
        }

        var (falls, weak) = Weakened(rules);
        Assert.True(falls.CanBreakBase(falls.TankList.Tanks[1], weak));

        var (holds, held) = Weakened(rules with { BasesHold = true });
        Assert.False(holds.CanBreakBase(holds.TankList.Tanks[1], held));
    }

    [Theory]
    [InlineData(false, 15.0)]
    [InlineData(true, Tweaks.Arcade.RespawnSeconds)]
    public void TheQuickRespawn_IsFlat_WhateverTheBaseHasStored(bool quickRespawn, double seconds)
    {
        // An energy match, where the wait otherwise grows as the store empties: 15 s on an empty one.
        var world = Create(new MatchRules { TerrainRegrowth = false, EnergyNetwork = true, QuickRespawn = quickRespawn }, tanks: 2);
        var tank = world.TankList.Tanks[0];
        var home = tank.Base!;
        home.DrawCharge(home.Charge);
        tank.TakeDamage(tank.Reactor.HealthCapacity, DamageCause.Overheat);

        world.Advance(_ => default);

        Assert.Equal(TimeSpan.FromSeconds(seconds) - world.Settings.AdvanceStep, tank.RespawnRemaining);
    }

    [Fact]
    public void AQuicklyRespawnedTank_IsBackAfterThreeSeconds()
    {
        var world = Create(new MatchRules { TerrainRegrowth = false, EnergyNetwork = true, QuickRespawn = true }, tanks: 2);
        var tank = world.TankList.Tanks[0];
        tank.Base!.DrawCharge(tank.Base!.Charge);
        Kill(world, killer: 1, tank);

        int ticks = 0;
        while (tank.IsDead && ticks < 60 * Fps)
        {
            world.Advance(_ => default);
            ticks++;
        }

        Assert.Equal(Tweaks.Arcade.RespawnSeconds * Fps, ticks);
        Assert.True(tank.Base!.IsInside(tank.Position), "it comes back in its base");
    }

    [Fact]
    public void ForMode_Standard_IsTheStandardMatch_WithTheSwarm_AndTheCoreRaceAsAsked()
    {
        Assert.Equal(MatchRules.Standard with { CoreTapWins = true, Emberites = true }, MatchRules.ForMode(MatchMode.Standard, coreTapWins: true));
        Assert.Equal(MatchRules.Standard with { CoreTapWins = false, Emberites = true }, MatchRules.ForMode(MatchMode.Standard, coreTapWins: false));

        MatchRules standard = MatchRules.ForMode(MatchMode.Standard, coreTapWins: true);
        Assert.Equal((0, false, false), (standard.TimeLimitSeconds, standard.BasesHold, standard.QuickRespawn));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForMode_Arcade_IsTheArcadePreset_WhateverTheCoreRaceSetting(bool coreTapWins)
    {
        MatchRules arcade = MatchRules.ForMode(MatchMode.Arcade, coreTapWins);

        Assert.Equal(MatchRules.Arcade, arcade);
        Assert.Equal(
            MatchRules.Standard with
            {
                Emberites = true,
                CoreTapWins = false,
                TimeLimitSeconds = Tweaks.Arcade.TimeLimitSeconds,
                BasesHold = true,
                QuickRespawn = true,
                Powerups = true,
            },
            arcade);
        Assert.Equal(480, arcade.TimeLimitSeconds);
        Assert.Equal(3, Tweaks.Arcade.RespawnSeconds);
        Assert.Throws<ArgumentOutOfRangeException>(() => MatchRules.ForMode((MatchMode)2, coreTapWins));
    }

    [Fact]
    public void TheModes_AndEachArcadeRule_HaveFingerprintsOfTheirOwn()
    {
        ulong standard = MatchRules.ForMode(MatchMode.Standard, coreTapWins: false).Fingerprint();
        Assert.NotEqual(standard, MatchRules.ForMode(MatchMode.Arcade, coreTapWins: false).Fingerprint());

        // Each rule alone tells two matches apart, and so does the length of the clock.
        var plain = new MatchRules();
        ulong[] prints =
        [
            plain.Fingerprint(),
            (plain with { TimeLimitSeconds = 480 }).Fingerprint(),
            (plain with { TimeLimitSeconds = 481 }).Fingerprint(),
            (plain with { BasesHold = true }).Fingerprint(),
            (plain with { QuickRespawn = true }).Fingerprint(),
            (plain with { BasesHold = true, QuickRespawn = true }).Fingerprint(),
        ];
        Assert.Equal(prints.Length, prints.Distinct().Count());
    }

    [Fact]
    public void ANegativeTimeLimit_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchRules { TimeLimitSeconds = -1 });
    }

    [Fact]
    public void AnArcadeWorld_HasItsClockAndItsBoard_FromTheFirstTick()
    {
        var world = TestHelpers.CreateSeededWorld(timeLimitSeconds: Tweaks.Arcade.TimeLimitSeconds, basesHold: true, quickRespawn: true);

        Assert.True(world.MatchClock.Active);
        Assert.Equal(Tweaks.Arcade.TimeLimitSeconds, world.MatchClock.RemainingSeconds);
        Assert.Equal(2, Standings(world).Length);
        Assert.False(world.CanBreakBase(world.TankList.Tanks[0], world.TankList.Tanks[1].Base!));
    }
}
