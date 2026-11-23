namespace Emberward.Desktop.Net;

using Emberward.Core.Match;
using Emberward.Core.Types;
using Emberward.Desktop.Config;

/// <summary>
/// The map sizes the network menu offers when a room is created (docs/multiplayer-lobby-ux.md section 3.3). Shared with
/// the browser, which opens rooms by the same rule.
/// </summary>
public enum MapPreset
{
    Small,
    Medium,
    Large,
}

public static class MapPresets
{
    /// <summary>The size of <c>--large</c> and of the Large preset.</summary>
    public static readonly Size LargeSize = new(1500, 750);

    public static Size SizeOf(MapPreset preset) => preset switch
    {
        MapPreset.Small => DesktopScreenTweaks.RenderSurfaceSize,
        MapPreset.Medium => NetworkTweaks.LargeMatchMapSize,
        _ => LargeSize,
    };

    /// <summary>The preset with exactly this size, or null.</summary>
    public static MapPreset? PresetOf(Size size) =>
        Enum.GetValues<MapPreset>().Cast<MapPreset?>().FirstOrDefault(preset => SizeOf(preset!.Value) == size);

    /// <summary>The most tanks an arcade match has on the small map when nobody chose one.</summary>
    public const int ArcadeSmallMapPlayers = 4;

    /// <summary>
    /// The preset a match gets when nobody chose a map. This is the one rule: the command line, the network menu and
    /// the browser all ask it, for a local match and for the room a player opens.
    /// </summary>
    /// <remarks>
    /// A standard match of two tanks plays on the small map and a larger one on the medium map, which is kept modest
    /// because every machine simulates it in real time. An arcade match of up to <see cref="ArcadeSmallMapPlayers"/>
    /// tanks stays on the small map: in twenty four-bot matches the medium map split the fight toward two duels (54%
    /// of hits on the enemy with the nearest base, 7% between the pair that fought least), while on the small map
    /// the fight was an even three-way mix and the time a player spent out of it fell from 5.6% to 0.3%.
    /// </remarks>
    public static MapPreset DefaultFor(MatchMode mode, int players) =>
        mode == MatchMode.Arcade && players <= ArcadeSmallMapPlayers ? MapPreset.Small
        : players > 2 ? MapPreset.Medium
        : MapPreset.Small;

    /// <summary>The size of <see cref="DefaultFor"/>'s preset.</summary>
    public static Size DefaultSizeFor(MatchMode mode, int players) => SizeOf(DefaultFor(mode, players));

    public static string Label(MapPreset preset)
    {
        var size = SizeOf(preset);
        return $"{preset} {size.X}x{size.Y}";
    }

    /// <summary>Every machine simulates the whole map in real time; the biggest one is more than a slow machine can do.</summary>
    public static string? Warning(MapPreset preset) => preset == MapPreset.Large
        ? "Large maps need a fast computer: a slow one falls behind the match."
        : null;
}
