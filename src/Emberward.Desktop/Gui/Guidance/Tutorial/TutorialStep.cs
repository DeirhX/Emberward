namespace Emberward.Desktop.Gui.Guidance.Tutorial;

using Emberward.Core;
using Emberward.Core.Entities.Machines;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Types;
using Emberward.Desktop.Presentation;

/// <summary>
/// One step of the tutorial. Each system has its steps in a file of its own in <c>Guidance/Tutorial</c>;
/// <see cref="TutorialDirector"/> lists them in the order the player meets them. A step knows from the world when it
/// is done, and a step about something the player experiences (leaving the base, coming back to the network) watches
/// for it in <see cref="Observe"/>, every frame from the start, so that it is done when the tutorial reaches it.
/// </summary>
internal abstract class TutorialStep
{
    public abstract string Title { get; }

    /// <summary>What the step says under rules that ask nothing else of it.</summary>
    protected abstract string Text { get; }

    /// <summary>What the step says in <paramref name="world"/>'s rules.</summary>
    public virtual string Body(World world) => Text;

    /// <summary>Seconds on screen after which the step counts as read and finishes anyway; 0 for never.</summary>
    public virtual float ReadSeconds(World world) => 0f;

    /// <summary>Called every frame while the player has a base and is alive, before any step is asked if it is done.</summary>
    public virtual void Observe(World world, Tank player) { }

    public abstract bool IsDone(World world, Tank player);

    /// <summary>An optional progress line (e.g. "Ore 87 / 150") and its fill fraction.</summary>
    public virtual (string Text, float Fraction)? Progress(World world, Tank player) => null;

    /// <summary>An optional world cell the HUD points at.</summary>
    public virtual Position? Target(World world, Tank player) => null;

    protected static int Cost(MachineType type) => MachineBehaviors.Get(type).OreCost;

    protected static string Key(MachineType type) => MachinePresentations.Get(type).KeyLabel;

    protected static (string, float) Ratio(string label, int have, int need) =>
        ($"{label} {Math.Min(have, need)} / {need}", Math.Clamp(have / (float)need, 0f, 1f));

    /// <summary>"Base ore store n / cost", or nothing for a tank with no base.</summary>
    protected static (string, float)? StoreRatio(Tank player, int need) =>
        player.Base is { } b ? Ratio("Base ore store", b.Store.Ore, need) : null;

    protected static IEnumerable<Machine> Owned(World world, Tank player, MachineType type) =>
        world.Machines.Machines.Where(m => player.Base != null && m.StandsFor(player.Base) && m.Type == type);

    protected static bool OwnsAny(World world, Tank player, MachineType type) => Owned(world, player, type).Any();

    protected static Position? FirstOwned(World world, Tank player, MachineType type) =>
        Owned(world, player, type).Select(m => (Position?)m.Position).FirstOrDefault();

    protected static Position? Nearest(IReadOnlyList<Position> points, Position from)
    {
        Position? best = null;
        int bestDist = int.MaxValue;
        foreach (var p in points)
        {
            int d = Position.DistanceSquared(p, from);
            if (d < bestDist) { bestDist = d; best = p; }
        }
        return best;
    }
}
