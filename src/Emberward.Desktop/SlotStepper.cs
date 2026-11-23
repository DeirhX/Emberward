namespace Emberward.Desktop;

using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Input;
using Emberward.Core.Bots;
using Emberward.Core.Entities.Tanks;

/// <summary>
/// Advances the world one step with each tank driven by its slot's source: a slot with a bot asks the bot, every
/// other slot takes the input given for it. A single-player step, a network step and a replayed one all go through
/// here, so a bot behaves the same in each.
/// </summary>
/// <remarks>
/// Every slot's input is decided before the step, from the world as the step before left it. A player's input is
/// fixed before the step too, so a bot knows no more than a player does, and no slot more than another: asked during
/// the step, tank by tank, the bot in a later slot saw where the tanks before it had just moved, and won more for it
/// (17, 17, 27 and 39 of 100 four-bot arcade matches, by slot).
/// </remarks>
public static class SlotStepper
{
    // The step's inputs by slot, kept from step to step. One per thread: worlds on different threads step at once.
    [ThreadStatic]
    private static ControllerOutput[]? t_inputs;

    /// <param name="bots">Indexed by slot; null (or past the end) where no bot drives the tank.</param>
    /// <param name="inputForSlot">The input of a slot without a bot.</param>
    public static void Advance(World world, IReadOnlyList<BotTankAI?> bots, Func<int, ControllerOutput> inputForSlot)
    {
        IReadOnlyList<Tank> tanks = world.TankList.Tanks;
        ControllerOutput[] inputs = t_inputs ??= new ControllerOutput[Tweaks.World.MaxPlayers];
        if (inputs.Length < tanks.Count)
            inputs = t_inputs = new ControllerOutput[tanks.Count];
        for (int slot = 0; slot < tanks.Count; slot++)
            inputs[slot] = slot < bots.Count && bots[slot] is { } bot
                ? bot.GetInput(tanks[slot], BotTankAI.PickEnemy(tanks, slot), world)
                : inputForSlot(slot);
        world.Advance(slot => inputs[slot]);
    }
}
