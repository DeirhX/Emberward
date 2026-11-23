using Emberward.Core;
using Emberward.Core.Bots;
using Emberward.Core.Input;
using Emberward.Core.Types;
using Emberward.Desktop;

namespace Emberward.Tests;

/// <summary>
/// The order of a step (<see cref="SlotStepper"/>): every slot's input is decided before the world moves, so a bot
/// knows no more than a player and no slot more than another. Asked during the step, a bot in a later slot saw where
/// the tanks before it had just moved, and won more for it.
/// </summary>
public class SlotStepperTests
{
    /// <summary>
    /// Two worlds the same up to a step in which the tank in slot 0 drives and fires in one and stands still in the
    /// other. The bot in slot 1 decided before either moved, so it is in the same state after the step in both.
    /// </summary>
    [Fact]
    public void ABotsInput_DoesNotDependOnWhatATankBeforeItDoesInTheSameStep()
    {
        // Bare ground: the bot sees its enemy from its base, and aims at where it is.
        var (moves, movesBots) = BotInSlotOne();
        var (stands, standsBots) = BotInSlotOne();
        for (int step = 0; step < 90; step++)
        {
            SlotStepper.Advance(moves, movesBots, _ => default);
            SlotStepper.Advance(stands, standsBots, _ => default);
        }
        Assert.Equal(WorldStateHash.ComputeSync(moves, movesBots), WorldStateHash.ComputeSync(stands, standsBots));

        var drive = new ControllerOutput { MoveSpeed = new Offset(1, 1), ShootPrimary = true, AimDirection = new DirectionF(1, 0) };
        Position before = moves.TankList.Tanks[0].Position;
        SlotStepper.Advance(moves, movesBots, _ => drive);
        SlotStepper.Advance(stands, standsBots, _ => default);

        Assert.NotEqual(before, moves.TankList.Tanks[0].Position);
        Assert.Equal(before, stands.TankList.Tanks[0].Position);
        Assert.Equal(WorldStateHash.ComputeSync(moves, movesBots).Bots, WorldStateHash.ComputeSync(stands, standsBots).Bots);
        // And the bot's tank was given the same input: it stands and aims alike in both worlds.
        var (a, b) = (moves.TankList.Tanks[1], stands.TankList.Tanks[1]);
        Assert.Equal((a.Position, a.Direction, a.Turret.Direction), (b.Position, b.Direction, b.Turret.Direction));
    }

    /// <summary>
    /// The same match stepped as the game steps it, and with every bot asked up front, the last slot first, and the
    /// world handed the answers: the same world, so no bot's answer waits for another tank's move.
    /// </summary>
    [Fact]
    public void AStep_IsTheSameWhicheverSlotIsAskedFirst()
    {
        var game = SimulationFingerprintTests.BotVsBot();
        var asked = SimulationFingerprintTests.BotVsBot();
        var tanks = asked.World.TankList.Tanks;
        var answers = new ControllerOutput[tanks.Count];

        for (int step = 0; step < 600; step++)
        {
            SlotStepper.Advance(game.World, game.Bots, _ => default);
            for (int slot = tanks.Count - 1; slot >= 0; slot--)
                answers[slot] = asked.Bots[slot]!.GetInput(tanks[slot], BotTankAI.PickEnemy(tanks, slot), asked.World);
            asked.World.Advance(slot => answers[slot]);
        }

        Assert.Equal(
            WorldStateHash.ComputeSync(game.World, game.Bots), WorldStateHash.ComputeSync(asked.World, asked.Bots));
    }

    [Fact]
    public void APlayersInput_IsAskedForBeforeTheStepToo()
    {
        var (world, bots) = BotInSlotOne();
        int askedAt = -1;

        SlotStepper.Advance(world, bots, _ =>
        {
            askedAt = world.AdvanceCount;
            return default;
        });

        Assert.Equal(0, askedAt);
        Assert.Equal(1, world.AdvanceCount);
    }

    private static (World World, BotTankAI?[] Bots) BotInSlotOne() =>
        (PowerupWorlds.Create(tanks: 2), [null, new BotTankAI(seed: 5)]);
}
