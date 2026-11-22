namespace Emberward.Core.Sensing;

using Emberward.Core.Entities.Emberites;
using Emberward.Core.Rendering;
using Emberward.Core.Types;

/// <summary>
/// Which nests one side has discovered, and what it last learned of each. A nest is found once the side has seen any
/// of its ground (<see cref="Nest.Radius"/> around its centre; in sight, or ground it has explored), and stays found: a
/// radar echo does not discover a nest. Not the centre alone: that is the deposit's first cell, inside a lump deeper
/// than sight reaches into solid ground, so a side could look at the lump's face and never find its nest. What the side
/// knows of it (asleep, awake, its creatures out, cleared) is brought up to date only while it sees some of that
/// ground, so the fog keeps what goes on there, and a nest it saw cleared loses its ring. Output only, like the vision it reads; <see cref="SideSensing"/> keeps one per side it tracks.
/// </summary>
public sealed class NestDiscovery
{
    /// <summary>What the side last learned of a nest: <see cref="NestDangerLevel"/>, or <see cref="Cleared"/>.</summary>
    private const byte Cleared = byte.MaxValue;

    private bool[] _found = [];
    private byte[] _known = [];

    /// <summary>Whether nest <paramref name="index"/> (an index into <see cref="EmberiteSwarm.Nests"/>) has been found.</summary>
    public bool IsFound(int index) => index >= 0 && index < _found.Length && _found[index];

    /// <summary>
    /// What the side last learned of a found nest: its danger level, or false once it saw the nest cleared (or has not
    /// found it).
    /// </summary>
    public bool TryGetKnownLevel(int index, out NestDangerLevel level)
    {
        level = default;
        if (!IsFound(index) || _known[index] == Cleared)
            return false;
        level = (NestDangerLevel)_known[index];
        return true;
    }

    /// <summary>Looks at what the side learns this tick through <paramref name="vision"/>: it sees a nest by looking at its ground.</summary>
    public void Update(EmberiteSwarm swarm, VisionMap vision)
    {
        var nests = swarm.Nests;
        if (_found.Length < nests.Count)
        {
            Array.Resize(ref _found, nests.Count);
            Array.Resize(ref _known, nests.Count);
        }
        for (int i = 0; i < nests.Count; i++)
        {
            Nest nest = nests[i];
            if (Sees(vision, nest))
                Learn(swarm, i);
            else if (!_found[i] && vision.IsAnyExploredWithin(nest.Centre, nest.Radius))
            {
                // Ground it remembers: it knows the nest is there, not what it is doing now.
                _found[i] = true;
                _known[i] = (byte)NestDangerLevel.Dormant;
            }
        }
    }

    /// <summary>Whether <paramref name="vision"/> sees any of <paramref name="nest"/>'s ground now.</summary>
    public static bool Sees(VisionMap vision, Nest nest)
    {
        ArgumentNullException.ThrowIfNull(vision);
        ArgumentNullException.ThrowIfNull(nest);
        return vision.IsAnyVisibleWithin(nest.Centre, nest.Radius);
    }

    private void Learn(EmberiteSwarm swarm, int index)
    {
        _found[index] = true;
        _known[index] = swarm.TryGetDangerLevel(index, out NestDangerLevel level) ? (byte)level : Cleared;
    }
}
