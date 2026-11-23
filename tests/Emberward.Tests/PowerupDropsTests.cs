using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities;
using Emberward.Core.Entities.Powerups;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Match;
using Emberward.Core.Rendering;
using Emberward.Core.Types;
using static Emberward.Tests.PowerupWorlds;

namespace Emberward.Tests;

/// <summary>
/// Powerup drops (DESIGN.md §6.8): the schedule everyone knows ahead, the bag the kinds come from, who takes a drop
/// and when one is gone, and that a world whose rules run none has none.
/// </summary>
public class PowerupDropsTests
{
    private const int Period = Tweaks.Powerups.DropPeriodTicks;

    [Fact]
    public void Forecast_IsKnownFromTheStart_AFullPeriodAhead_AndWhatLandsIsWhatWasForecast()
    {
        var world = Create();

        PowerupForecast forecast = world.PowerupForecast;
        Assert.True(forecast.Active);
        Assert.Equal(Tweaks.Powerups.FirstDropTicks, forecast.TicksUntilLanding);
        Assert.True(forecast.TicksUntilLanding >= Period, "the first drop is known at least a period ahead");
        Assert.Equal(Tweaks.Powerups.FirstDropSeconds, forecast.SecondsUntilLanding);

        for (int drop = 0; drop < 5; drop++)
        {
            // Until it lands the forecast says the same place and kind, and its time runs down a tick a step.
            for (int left = forecast.TicksUntilLanding - 1; left > 0; left--)
            {
                Step(world);
                Assert.Equal(forecast with { TicksUntilLanding = left }, world.PowerupForecast);
                Assert.Empty(Events(world, StepEventKind.PowerupLanded));
            }
            Step(world);
            StepEvent landed = Assert.Single(Events(world, StepEventKind.PowerupLanded));
            Assert.Equal((forecast.Site, (int)forecast.Kind, -1), (landed.Position, landed.Count, landed.Color));

            // The moment it lands the next is decided, a full period ahead, and announced.
            PowerupForecast next = world.PowerupForecast;
            Assert.True(next.Active);
            Assert.Equal(Period, next.TicksUntilLanding);
            StepEvent announced = Assert.Single(Events(world, StepEventKind.PowerupAnnounced));
            Assert.Equal((next.Site, (int)next.Kind, -1), (announced.Position, announced.Count, announced.Color));
            forecast = next;
        }
    }

    [Fact]
    public void FirstStep_AnnouncesTheFirstDrop()
    {
        var world = Create();
        PowerupForecast forecast = world.PowerupForecast;

        Step(world);
        StepEvent announced = Assert.Single(Events(world, StepEventKind.PowerupAnnounced));
        Assert.Equal((forecast.Site, (int)forecast.Kind), (announced.Position, announced.Count));

        Step(world);
        Assert.Empty(Events(world, StepEventKind.PowerupAnnounced));
    }

    [Fact]
    public void Schedule_IsTheSameForTheSameSeed_AndDiffersForAnother()
    {
        // A world's seed decides its drops: the first two, as two worlds of one seed and one of another forecast them.
        var first = WorldSchedule(seed: 11);
        Assert.Equal(first, WorldSchedule(seed: 11));
        Assert.NotEqual(first, WorldSchedule(seed: 12));

        // And over many drops, stepped alone on the same ground.
        var world = Create();
        var long1 = AloneSchedule(world, seed: 5, drops: 16);
        Assert.Equal(long1, AloneSchedule(world, seed: 5, drops: 16));
        Assert.NotEqual(long1, AloneSchedule(world, seed: 6, drops: 16));
        Assert.True(long1.Select(drop => drop.Site).Distinct().Count() > 8, "the sites of a schedule vary");
    }

    private static List<(Position Site, PowerupKind Kind)> WorldSchedule(int seed)
    {
        var world = Create(seed: seed);
        var schedule = new List<(Position, PowerupKind)> { (world.PowerupForecast.Site, world.PowerupForecast.Kind) };
        AwaitLanding(world);
        schedule.Add((world.PowerupForecast.Site, world.PowerupForecast.Kind));
        return schedule;
    }

    private static List<(Position Site, PowerupKind Kind)> AloneSchedule(World world, int seed, int drops)
    {
        var log = new StepEventLog();
        var alone = Alone(world, seed, log);
        var schedule = new List<(Position, PowerupKind)>();
        for (int i = 0; i < drops; i++)
        {
            PowerupForecast landed = LandNext(alone, log);
            schedule.Add((landed.Site, landed.Kind));
        }
        return schedule;
    }

    [Fact]
    public void Kinds_ComeFromAShuffleBag_EachOnceARound_AndNeverTheSameTwiceRunning()
    {
        var world = Create();
        var orders = new HashSet<string>();
        for (int seed = 1; seed <= 40; seed++)
        {
            var log = new StepEventLog();
            var drops = Alone(world, seed, log);
            var kinds = new List<PowerupKind>();
            for (int i = 0; i < 6 * PowerupKinds.Count; i++)
                kinds.Add(LandNext(drops, log).Kind);

            for (int round = 0; round < kinds.Count; round += PowerupKinds.Count)
            {
                var dealt = kinds.GetRange(round, PowerupKinds.Count);
                Assert.Equal(PowerupKinds.Count, dealt.Distinct().Count());
                orders.Add(string.Join(',', dealt));
            }
            for (int i = 1; i < kinds.Count; i++)
                Assert.NotEqual(kinds[i - 1], kinds[i]);
        }
        // Shuffled, not dealt in one fixed order: 240 rounds show most of the 24 orders there are.
        Assert.True(orders.Count >= 20, $"only {orders.Count} orders of the kinds were dealt");
    }

    [Fact]
    public void Tank_TakesADrop_WhenItsSpriteReachesTheDisc_AndNotBefore()
    {
        var world = Create();
        Tank tank = world.TankList.Tanks[0];
        Position site = AwaitLanding(world);
        Assert.Equal(1, world.PowerupDropCount);
        Span<PowerupRenderState> ground = stackalloc PowerupRenderState[4];
        Assert.Equal(1, world.CopyPowerupRenderStates(ground));
        Assert.Equal(site, ground[0].Cell);
        Assert.Equal(Tweaks.Powerups.PickupDiameter / 2f, ground[0].Radius);
        Assert.Equal(Tweaks.Powerups.GroundTicks, ground[0].TicksUntilExpiry);

        // Coming from the side, cell by cell: nothing until a cell of the tank is in the disc, which is while its
        // centre is still outside it.
        int reach = -1;
        for (int away = 16; away >= 0 && reach < 0; away--)
        {
            tank.Position = site + new Offset(away, 0);
            bool touches = TankSpriteShape.ReachesDisc(tank.Direction, tank.Position, site, Tweaks.Powerups.PickupDiameter);
            Step(world);
            Assert.Equal(touches ? 0 : 1, world.PowerupDropCount);
            Assert.Equal(touches ? 1 : 0, Events(world, StepEventKind.PowerupCollected).Length);
            if (touches)
                reach = away;
        }
        Assert.True(reach > Tweaks.Powerups.PickupDiameter / 2, $"the tank took the drop only from {reach} cells");
        Assert.Equal(0, world.CopyPowerupRenderStates(ground));
    }

    [Fact]
    public void OfTwoTanksOnADrop_TheNearerTakesIt_AndOfTwoAsNearTheLowerSlot()
    {
        var world = Create();
        var tanks = world.TankList.Tanks;
        Position site = AwaitLanding(world);
        tanks[1].Position = site + new Offset(2, 0);
        tanks[2].Position = site + new Offset(-1, 0);
        Step(world);
        Assert.Equal(2, Assert.Single(Events(world, StepEventKind.PowerupCollected)).Color);

        // Home again: a tank left standing in the open heats up.
        (tanks[1].Position, tanks[2].Position) = (tanks[1].Base!.Position, tanks[2].Base!.Position);
        site = AwaitLanding(world);
        tanks[3].Position = site + new Offset(0, 2);
        tanks[1].Position = site + new Offset(0, -2);
        Step(world);
        Assert.Equal(1, Assert.Single(Events(world, StepEventKind.PowerupCollected)).Color);
    }

    [Fact]
    public void FirstTankThere_TakesTheDrop_AndTheNextFindsNothing()
    {
        var world = Create();
        var tanks = world.TankList.Tanks;
        Position site = AwaitLanding(world);
        tanks[3].Position = site;
        Step(world);
        Assert.Equal(3, Assert.Single(Events(world, StepEventKind.PowerupCollected)).Color);

        tanks[3].Position = site + new Offset(40, 0);
        tanks[0].Position = site;
        Step(world, 5);
        Assert.Empty(Events(world, StepEventKind.PowerupCollected));
        Assert.Equal(0, world.PowerupDropCount);
    }

    [Fact]
    public void DeadTank_TakesNoDrop()
    {
        var world = Create();
        Tank tank = world.TankList.Tanks[0];
        Position site = AwaitLanding(world);
        Assert.True(tank.TakeDamage(ArcadeWorlds.Lethal, DamageCause.Emberite));
        tank.Position = site;

        for (int i = 0; i < 10; i++)
        {
            Step(world);
            Assert.True(tank.IsDead);
            Assert.Empty(Events(world, StepEventKind.PowerupCollected));
        }
        Assert.Equal(1, world.PowerupDropCount);
    }

    [Fact]
    public void DropNobodyTakes_LiesItsTime_ThenIsGone_BeforeTheNextLands()
    {
        var world = Create();
        Position site = AwaitLanding(world);
        int kind = Events(world, StepEventKind.PowerupLanded)[0].Count;
        Assert.Equal(Period, Tweaks.Powerups.GroundTicks);

        Step(world, Period - 1);
        Assert.Empty(Events(world, StepEventKind.PowerupExpired));
        Span<PowerupRenderState> ground = stackalloc PowerupRenderState[4];
        Assert.Equal(1, world.CopyPowerupRenderStates(ground));
        Assert.Equal((site, 1), (ground[0].Cell, ground[0].TicksUntilExpiry));

        // The step its time is up in is the step the next lands in: the old one goes first, so one lies, not two.
        PowerupForecast next = world.PowerupForecast;
        Step(world);
        StepEvent expired = Assert.Single(Events(world, StepEventKind.PowerupExpired));
        Assert.Equal((site, kind, -1), (expired.Position, expired.Count, expired.Color));
        StepEvent landed = Assert.Single(Events(world, StepEventKind.PowerupLanded));
        Assert.Equal((next.Site, (int)next.Kind), (landed.Position, landed.Count));
        StepEvent[] events = world.StepEvents.ToArray();
        Assert.True(Array.IndexOf(events, expired) < Array.IndexOf(events, landed), "the old drop goes before the new one lands");
        Assert.Equal(1, world.CopyPowerupRenderStates(ground));
        Assert.Equal((next.Site, Tweaks.Powerups.GroundTicks), (ground[0].Cell, ground[0].TicksUntilExpiry));

        // And so in every step after: with nobody taking any, never two on the ground.
        for (int step = 0; step < 3 * Period; step++)
        {
            Step(world);
            Assert.Equal(1, world.PowerupDropCount);
        }
    }

    [Fact]
    public void TankOnADropInItsLastStep_TakesIt_AndOneWaitingOnTheNextSiteTakesThatAsItLands()
    {
        var world = Create();
        var tanks = world.TankList.Tanks;
        Position site = AwaitLanding(world);
        int kind = Events(world, StepEventKind.PowerupLanded)[0].Count;
        PowerupForecast next = world.PowerupForecast;
        Step(world, Period - 1);

        tanks[0].Position = site;
        tanks[1].Position = next.Site;
        Step(world);

        Assert.Empty(Events(world, StepEventKind.PowerupExpired));
        Assert.Equal(
            [(site, 0, kind), (next.Site, 1, (int)next.Kind)],
            Events(world, StepEventKind.PowerupCollected).Select(e => (e.Position, e.Color, e.Count)));
        Assert.Equal(0, world.PowerupDropCount);
    }

    [Fact]
    public void WithoutTheRule_AWorldHasNoDrops()
    {
        var world = Create(Rules with { Powerups = false });
        Tank tank = world.TankList.Tanks[0];
        Assert.False(world.PowerupForecast.Active);
        Assert.Equal(default, world.PowerupForecast);

        var kinds = new HashSet<StepEventKind>();
        for (int i = 0; i < Tweaks.Powerups.FirstDropTicks + 10; i++)
        {
            Step(world);
            foreach (StepEvent e in world.StepEvents)
                kinds.Add(e.Kind);
        }
        Assert.DoesNotContain(kinds, kind => kind.ToString().StartsWith("Powerup", StringComparison.Ordinal));
        Assert.False(world.PowerupForecast.Active);
        Assert.Equal(0, world.PowerupDropCount);
        Assert.Equal(0, world.CopyPowerupRenderStates(new PowerupRenderState[4]));
        Assert.Equal(default, world.PowerupsOf(tank));
        Assert.False(world.PowerupsOf(tank).Active);
        Assert.DoesNotContain("powerups", world.Profile.Sections);
    }

    [Fact]
    public void WithTheRule_TheStepRunsTheDropsRightAfterTheTanks()
    {
        var world = Create();
        var sections = world.Profile.Sections.ToList();
        Assert.Contains("powerups", sections);
        Assert.Equal(sections.IndexOf("tanks") + 1, sections.IndexOf("powerups"));
    }

    [Fact]
    public void Rules_ArcadeRunsTheDrops_AndTheyAreInTheFingerprintOnlyWhenOn()
    {
        Assert.True(MatchRules.Arcade.Powerups);
        Assert.False(MatchRules.Standard.Powerups);
        Assert.False(new MatchRules().Powerups);
        Assert.NotEqual(MatchRules.Arcade.Fingerprint(), (MatchRules.Arcade with { Powerups = false }).Fingerprint());
        Assert.NotEqual(new MatchRules().Fingerprint(), new MatchRules { Powerups = true }.Fingerprint());
        foreach (bool coreTapWins in new[] { false, true })
        {
            MatchRules arcade = MatchRules.ForMode(MatchMode.Arcade, coreTapWins);
            Assert.True(arcade.Powerups);
            foreach (MatchMode mode in Enum.GetValues<MatchMode>())
                if (mode != MatchMode.Arcade)
                {
                    Assert.False(MatchRules.ForMode(mode, coreTapWins).Powerups);
                    Assert.NotEqual(arcade.Fingerprint(), MatchRules.ForMode(mode, coreTapWins).Fingerprint());
                }
        }
    }

    [Fact]
    public void KindTable_SaysHowLongEachKindRuns()
    {
        Assert.Equal(PowerupKinds.Count, Enum.GetValues<PowerupKind>().Length);
        Assert.False(PowerupKinds.IsTimed(PowerupKind.Overhaul));
        Assert.Equal(0, PowerupKinds.DurationTicks(PowerupKind.Overhaul));
        Assert.Equal(Tweaks.Powerups.OverchargeSeconds, PowerupKinds.DurationSeconds(PowerupKind.Overcharge));
        Assert.Equal(Tweaks.Powerups.BulwarkSeconds, PowerupKinds.DurationSeconds(PowerupKind.Bulwark));
        Assert.Equal(Tweaks.Powerups.DrillSeconds, PowerupKinds.DurationSeconds(PowerupKind.Drill));
        foreach (PowerupKind kind in Enum.GetValues<PowerupKind>())
            Assert.Equal(PowerupKinds.DurationTicks(kind) > 0, PowerupKinds.IsTimed(kind));
    }
}
