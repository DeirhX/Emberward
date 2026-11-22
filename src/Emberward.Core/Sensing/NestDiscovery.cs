namespace Emberward.Core.Sensing;

using Emberward.Core.Entities.Emberites;
using Emberward.Core.Rendering;
using Emberward.Core.Types;

/// <summary>
/// Which nests one side has discovered, and what it last learned of each. A nest is found once the side has seen its
/// centre (in sight, or ground it has explored) or its radar has returned the nest's own life echo, and stays found.
/// What the side knows of it (asleep, awake, its creatures out, cleared) is brought up to date only while it sees the
/// centre or the nest's echo comes back, so the fog keeps what goes on there. Output only, like the vision and the radar
/// it reads; <see cref="SideSensing"/> keeps one per side it tracks.
/// </summary>
/// <remarks>
/// A tick looks at each nest's centre once and at the radar's contacts born that tick, never at the map. Only an echo
/// on the nest's centre cell, where the radar records the nest's own echo, counts: a creature or a tank beside a nest
/// echoes on its own cell, so it tells nothing of the nest.
/// </remarks>
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

    /// <summary>Looks at what the side learns this tick through <paramref name="vision"/> and <paramref name="radar"/> (null when it has none).</summary>
    public void Update(EmberiteSwarm swarm, VisionMap vision, Radar? radar)
    {
        var nests = swarm.Nests;
        if (_found.Length < nests.Count)
        {
            Array.Resize(ref _found, nests.Count);
            Array.Resize(ref _known, nests.Count);
        }
        for (int i = 0; i < nests.Count; i++)
        {
            Position centre = nests[i].Centre;
            if (vision.IsVisible(centre))
                Learn(swarm, i);
            else if (!_found[i] && vision.IsExplored(centre))
            {
                // Ground it remembers: it knows the nest is there, not what it is doing now.
                _found[i] = true;
                _known[i] = (byte)NestDangerLevel.Dormant;
            }
        }
        if (radar is null)
            return;
        // Contacts are kept oldest first, so this tick's are read from the end, stopping at the first older one.
        for (int c = radar.ContactCount - 1; c >= 0; c--)
        {
            RadarContact contact = radar.ContactAt(c);
            if (contact.BornTick != radar.Tick)
                break;
            if (contact.Echo != RadarEcho.Life)
                continue;
            for (int i = 0; i < nests.Count; i++)
                if (nests[i].Centre == contact.Position)
                    Learn(swarm, i);
        }
    }

    private void Learn(EmberiteSwarm swarm, int index)
    {
        _found[index] = true;
        _known[index] = swarm.TryGetDangerLevel(index, out NestDangerLevel level) ? (byte)level : Cleared;
    }
}
