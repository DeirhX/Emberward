using Emberward.Core;
using Emberward.Core.Bots;
using Emberward.Core.Config;
using Emberward.Core.Entities;
using Emberward.Core.Entities.Powerups;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Input;
using Emberward.Core.Match;
using Emberward.Core.Rendering;
using Emberward.Core.Terrain;
using Emberward.Core.Types;
using static Emberward.Tests.PowerupWorlds;

namespace Emberward.Tests;

/// <summary>
/// A late joiner's copy of a match with powerup drops (docs/late-join-design.md): the schedule, the drops lying, the
/// effects running and what a bot remembers of the drop it is after all come through a snapshot.
/// </summary>
public class PowerupSnapshotTests
{
    [Fact]
    public void WorldWithADropLying_AnEffectRunning_AndADropForecast_PlaysOnAlikeFromASnapshot()
    {
        MatchRules rules = Rules with { TankShield = true };
        World Build() => Create(rules, tanks: 2, seed: 5);
        World world = Build();
        Tank tank = world.TankList.Tanks[0];

        // The tank takes a timed drop in its last moments on the ground and keeps it nearly to its end: by then
        // the next drop has landed and lies.
        Position first = AwaitLanding(world);
        var kind = (PowerupKind)Events(world, StepEventKind.PowerupLanded)[0].Count;
        while (!PowerupKinds.IsTimed(kind))
        {
            first = AwaitLanding(world);
            kind = (PowerupKind)Events(world, StepEventKind.PowerupLanded)[0].Count;
        }
        Step(world, Tweaks.Powerups.GroundTicks - 2);
        tank.Position = first;
        Step(world);
        Assert.True(world.PowerupsOf(tank).IsRunning(kind), $"{kind} should be running");
        // A point of heat damage under a bulwark leaves a remainder carried to the next hit: saved too.
        tank.TakeDamage(1, DamageCause.Overheat);
        Step(world, PowerupKinds.DurationTicks(kind) - (2 * Fps));

        Assert.Equal(1, world.PowerupDropCount);
        Assert.True(world.PowerupForecast.Active);
        int left = world.PowerupsOf(tank).TicksLeft(kind);
        Assert.True(left > 0, $"{kind} should still be running");

        // On through the effect ending and the drop on the ground running out, the tank driving and firing.
        Span<PowerupRenderState> ground = stackalloc PowerupRenderState[4];
        Assert.Equal(1, world.CopyPowerupRenderStates(ground));
        int untilExpiry = ground[0].TicksUntilExpiry;
        Assert.True(untilExpiry > left);
        var kinds = new HashSet<StepEventKind>();
        World loaded = SnapshotRoundTrip.AssertPlaysOnAlike(world, [null, null], Build, untilExpiry + Fps,
            (frame, slot) => slot != 0 ? default : new ControllerOutput
            {
                MoveSpeed = new Offset((frame / 50) % 2 == 0 ? 1 : -1, (frame / 70) % 2 == 0 ? 0 : 1),
                ShootPrimary = frame % 9 == 0,
                AimDirection = new DirectionF(1, 0),
            },
            before: (stepped, _) =>
            {
                foreach (StepEvent e in stepped.StepEvents)
                    kinds.Add(e.Kind);
            });

        Assert.Contains(StepEventKind.PowerupEnded, kinds);
        Assert.Contains(StepEventKind.PowerupExpired, kinds);
        Assert.Equal(world.PowerupForecast, loaded.PowerupForecast);
        Assert.Equal(world.PowerupsOf(tank), loaded.PowerupsOf(loaded.TankList.Tanks[0]));
    }

    /// <summary>Slow: a bot digs to the first drop, then two worlds play on.</summary>
    [Fact]
    [Trait("Category", "Slow")]
    public void BotOnItsWayToADrop_PlaysOnAlikeFromASnapshot()
    {
        World Build() => Create(tanks: 2, seed: 9, ground: TerrainPixel.Dirt);
        World world = Build();
        BotTankAI?[] bots = [null, new BotTankAI(MatchSetup.BotSeed(9, 1))];
        SnapshotRoundTrip.Script idle = (_, _) => default;

        // Until two seconds before the drop lands: the bot has been after it since it came near enough in time.
        int frame = 0;
        bool sought = false;
        for (; world.PowerupForecast.TicksUntilLanding > 2 * Fps; frame++)
        {
            SnapshotRoundTrip.Run(world, bots, frame, 1, idle);
            sought |= bots[1]!.DropSought != null;
        }
        Assert.True(sought, "the bot never went for the drop");
        Assert.Equal(world.PowerupForecast.Site, bots[1]!.DropSought);

        // On through the landing, and the bot taking the drop.
        int collected = 0;
        SnapshotRoundTrip.AssertPlaysOnAlike(world, bots, Build, ticks: 8 * Fps, idle, firstFrame: frame,
            before: (stepped, _) => collected += Events(stepped, StepEventKind.PowerupCollected).Length);

        // Both worlds' bots took the drop (each is counted).
        Assert.Equal(2, collected);
    }
}
