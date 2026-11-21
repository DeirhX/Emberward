namespace Emberward.Core.Entities.Emberites;

using Emberward.Core.Types;

/// <summary>
/// What a creature out of its nest is doing. It hunts with a charge, bites, runs to the nearest hot ground
/// (<see cref="Seeking"/>), drinks a new charge there for a second (<see cref="Recharging"/>) and hunts again; it goes
/// home only when it has lost its tank or finds no hot ground near.
/// </summary>
public enum EmberiteState : byte
{
    /// <summary>Going for a tank, to bite it.</summary>
    Hunting,

    /// <summary>Going home to its nest, to rest and recharge.</summary>
    Returning,

    /// <summary>Spent by a bite, running to the hot ground it found (<see cref="Emberite.Hotspot"/>).</summary>
    Seeking,

    /// <summary>At its hot ground, drinking a new charge out of it until <see cref="Emberite.Timer"/> runs out.</summary>
    Recharging,
}

/// <summary>
/// A creature out of its nest. It moves by its committed <see cref="Velocity"/> and then chooses the next one, so a
/// renderer that draws it at <c>position + velocity * alpha</c> is exact unless something blocks it.
/// </summary>
public struct Emberite
{
    public int Id { get; set; }
    public PositionF Position { get; set; }
    public VectorF Velocity { get; set; }

    /// <summary>The nest it came from, an index into <see cref="EmberiteSwarm.Nests"/>.</summary>
    public int Nest { get; set; }

    /// <summary>The tank it hunts, an index into the world's tank list; -1 when none.</summary>
    public int Target { get; set; }

    public EmberiteState State { get; set; }

    /// <summary>The heat it carries, in energy (what a tank's heat times its heat capacity is).</summary>
    public double Charge { get; set; }

    /// <summary>The pyrite it has taken from a tank's hold and carries home (units); spilled where it dies.</summary>
    public int Pyrite { get; set; }

    /// <summary>The trail block (<see cref="EmberiteTrails.BlockOf"/> plus one) it last wore, so that it wears each block once per entry; 0 when none.</summary>
    public int WornBlock { get; set; }

    /// <summary>The tick a creature last looked for a clear line to its target; 0 when it has not.</summary>
    public int SightTick { get; set; }

    /// <summary>Whether that look found a line clear of rock (kept until the next look).</summary>
    public bool Sees { get; set; }

    /// <summary>The hot cell a spent creature runs to and drinks from (<see cref="EmberiteState.Seeking"/>, <see cref="EmberiteState.Recharging"/>).</summary>
    public Position Hotspot { get; set; }

    /// <summary>Ticks left: of the run to <see cref="Hotspot"/> before it gives up, or of the drink there.</summary>
    public int Timer { get; set; }

    /// <summary>Killed or back in its nest this step; removed before the next one.</summary>
    public bool Gone { get; set; }
}

/// <summary>What a woken nest is doing between its pulses of creatures.</summary>
public enum NestPhase : byte
{
    /// <summary>Nothing is coming: asleep, or in the lull after a pulse.</summary>
    Quiet,

    /// <summary>About to let a pulse out: the warning before it.</summary>
    Stirring,

    /// <summary>Letting a pulse out, one creature at a time.</summary>
    Releasing,
}

/// <summary>
/// A nest in a pyrite deposit. A nest holds creatures it never released (<see cref="NeverReleased"/>) and ones
/// resting (<see cref="Resting"/>, with their stored charge); the rest are out, in <see cref="EmberiteSwarm"/>'s list.
/// </summary>
public sealed class Nest
{
    public Position Centre { get; init; }

    /// <summary>How far from <see cref="Centre"/> the deposit reaches (cells): the ground its creatures burrow freely.</summary>
    public int Radius { get; init; }

    /// <summary>Creatures that have never been released; their charge is made when they first leave.</summary>
    public int NeverReleased { get; set; }

    /// <summary>Creatures at rest in the nest.</summary>
    public int Resting { get; set; }

    /// <summary>The charge the resting creatures hold between them.</summary>
    public double Stored { get; set; }

    /// <summary>The pyrite its creatures have brought home (units); freed into the ground when the nest is cleared.</summary>
    public int Pyrite { get; set; }

    /// <summary>Creatures of this nest that are out.</summary>
    public int Out { get; set; }

    /// <summary>The nest is hunting: it releases creatures and recharges those that come home.</summary>
    public bool Awake { get; set; }

    /// <summary>Where the nest is in its pulse: quiet, stirring before one, or letting one out.</summary>
    public NestPhase Phase { get; set; }

    /// <summary>Ticks left of the lull or the stir, or to the next creature of the pulse.</summary>
    public int ReleaseTimer { get; set; }

    /// <summary>Ticks in a row the awake nest has had no tank to hunt (<see cref="Config.Tweaks.Emberites.TargetGraceTicks"/>).</summary>
    public int LostTicks { get; set; }

    /// <summary>Creatures the pulse being let out still has to come.</summary>
    public int PulseLeft { get; set; }

    /// <summary>Ticks a resting creature has gone short of a full charge with the nest filling none (<see cref="Config.Tweaks.Emberites.StarveTicks"/>).</summary>
    public int HungerTicks { get; set; }

    /// <summary>The tank its creatures are sent after, an index into the tank list; -1 when none is sensed.</summary>
    public int Target { get; set; } = -1;

    /// <summary>The tank that dug this nest's ore, hunted for <see cref="ThiefTicks"/> whatever its heat; -1 when none.</summary>
    public int Thief { get; set; } = -1;

    public int ThiefTicks { get; set; }

    /// <summary>Creatures left, in the nest or out.</summary>
    public int Alive => NeverReleased + Resting + Out;

    /// <summary>No creature is left: the nest is cleared and its ore is free to work.</summary>
    public bool IsCleared => Alive == 0;

    /// <summary>Creatures in the nest that could be released.</summary>
    public int Inside => NeverReleased + Resting;
}
