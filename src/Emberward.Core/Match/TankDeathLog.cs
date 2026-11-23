namespace Emberward.Core.Match;

using Emberward.Core.Entities;
using Emberward.Core.Entities.Tanks;

/// <summary>One tank's death: which tank (its colour, the same as its place in the tank list), the step and why.</summary>
public readonly record struct TankDeath(int Tank, int Step, DamageCause Cause);

/// <summary>
/// Every tank death of the match, oldest first, for the HUD's scoreboard. It is output only: nothing in the simulation
/// reads it back. A tank records its death on the step it goes down (the same step as its
/// <see cref="StepEventKind.TankDied"/>), and a late joiner's snapshot carries the list.
/// </summary>
public sealed class TankDeathLog
{
    private readonly List<TankDeath> _deaths = new();
    private int _step;

    public IReadOnlyList<TankDeath> Deaths => _deaths;

    /// <summary>Stamps the deaths recorded from now on with <paramref name="step"/> (<see cref="World.AdvanceCount"/>).</summary>
    internal void BeginStep(int step) => _step = step;

    internal void Record(Tank tank) => _deaths.Add(new TankDeath(tank.Color, _step, tank.DeathCause));
}
