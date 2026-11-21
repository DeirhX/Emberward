namespace Emberward.Core;

using Emberward.Core.Entities.Emberites;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Rendering;
using Emberward.Core.Types;

public partial class World
{
    /// <summary>The emberites (<see cref="Match.MatchRules.Emberites"/>); null when the rules leave them out.</summary>
    public EmberiteSwarm? Emberites => _emberites;

    /// <summary>
    /// Whether ore at <paramref name="cell"/> belongs to a nest that still has creatures. Harvesters pass over guarded ore
    /// (they work it once the nest is dead); a tank may still dig it, which wakes the nest.
    /// </summary>
    public bool IsOreGuarded(Position cell) => _emberites?.IsGuarded(cell) ?? false;

    /// <summary>Writes the creatures out of their nests for drawing; returns how many (<see cref="EmberiteRenderState"/>).</summary>
    public int CopyEmberiteRenderStates(Span<EmberiteRenderState> destination) => _emberites?.CopyRenderStates(destination) ?? 0;

    /// <summary>
    /// Writes the nests <paramref name="tank"/>'s side has discovered (seen, or found by its radar) that still have
    /// creatures, with their danger rings for that tank's heat; returns how many (<see cref="NestDanger"/>). Output only:
    /// discovery is what the side's vision (<see cref="TrackVision"/>) and radar learned, which nothing in the simulation
    /// reads. A side whose vision is not tracked plays without fog of war and knows every nest.
    /// </summary>
    public int CopyNestDangers(Tank tank, Span<NestDanger> destination) => _sensing.CopyNestDangers(tank, destination);

    /// <summary>Writes the nests for drawing; returns how many (<see cref="NestRenderState"/>).</summary>
    public int CopyNestRenderStates(Span<NestRenderState> destination) => _emberites?.CopyNestRenderStates(destination) ?? 0;
}
