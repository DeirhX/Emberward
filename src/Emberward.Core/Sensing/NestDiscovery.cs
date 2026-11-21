namespace Emberward.Core.Sensing;

using Emberward.Core.Entities.Emberites;

/// <summary>
/// Which nests one side has discovered: a nest is found once the side has seen its centre (sight, or ground it has
/// explored) or its radar has returned a life echo from it, and stays found. Output only, like the vision and the radar
/// it reads; <see cref="SideSensing"/> keeps one per side it tracks.
/// </summary>
/// <remarks>
/// Costs what is still undiscovered: once every nest is found an update returns at once. Until then it looks at each
/// unfound nest's centre and at the radar's contacts born this tick, never at the map.
/// </remarks>
public sealed class NestDiscovery
{
    /// <summary>A life blip within this many cells of a nest's centre, beyond half its radius, is the nest's.</summary>
    public const int BlipSlack = 3;

    private bool[] _found = [];
    private int _foundCount;

    /// <summary>Whether nest <paramref name="index"/> (an index into <see cref="EmberiteSwarm.Nests"/>) has been found.</summary>
    public bool IsFound(int index) => index >= 0 && index < _found.Length && _found[index];

    /// <summary>Looks for nests found this tick through <paramref name="vision"/> and <paramref name="radar"/> (null when the side has none).</summary>
    public void Update(IReadOnlyList<Nest> nests, VisionMap vision, Radar? radar)
    {
        if (_found.Length < nests.Count)
            Array.Resize(ref _found, nests.Count);
        if (_foundCount == nests.Count)
            return;
        for (int i = 0; i < nests.Count; i++)
        {
            if (_found[i])
                continue;
            Nest nest = nests[i];
            if (vision.IsExplored(nest.Centre) || vision.IsVisible(nest.Centre) || EchoedThisTick(radar, nest))
            {
                _found[i] = true;
                _foundCount++;
            }
        }
    }

    /// <summary>
    /// Whether one of the radar's contacts born this tick is a life echo on <paramref name="nest"/>. Contacts are kept
    /// oldest first, so the newest are read from the end and the walk stops at the first older one.
    /// </summary>
    private static bool EchoedThisTick(Radar? radar, Nest nest)
    {
        if (radar is null)
            return false;
        int reach = BlipSlack + Math.Max(2, nest.Radius / 2);
        for (int i = radar.ContactCount - 1; i >= 0; i--)
        {
            RadarContact contact = radar.ContactAt(i);
            if (contact.BornTick != radar.Tick)
                break;
            if (contact.Echo == RadarEcho.Life && Math.Abs(contact.Position.X - nest.Centre.X) <= reach &&
                Math.Abs(contact.Position.Y - nest.Centre.Y) <= reach)
                return true;
        }
        return false;
    }
}
