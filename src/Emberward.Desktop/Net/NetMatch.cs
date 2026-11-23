namespace Emberward.Desktop.Net;

using System.Reflection;
using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Input;
using Emberward.Core.LevelGen;
using Emberward.Core.Match;
using Emberward.Core.Types;
using Emberward.Protocol;

/// <summary>What a client says about itself and the match it proposes when it joins a room: the desktop and the browser alike.</summary>
public static class NetMatch
{
    /// <summary>
    /// Identifies the simulation build: the version of the Core assembly, which carries the source commit. Clients
    /// of different builds would not simulate alike, so the relay keeps them out of one room. The desktop and the
    /// browser built from one commit have the same id, and compute the same world.
    /// </summary>
    public static string BuildId =>
        typeof(World).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(World).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    /// <summary>The match this client proposes if it turns out to be the one that opens the room.</summary>
    public static MatchConfig ProposeConfig(
        int seed, Size mapSize, LevelGenMode genMode, int players, MatchMode mode = MatchMode.Standard) => new(
        seed, mapSize.X, mapSize.Y, (int)genMode, players, Tweaks.Perf.TargetFps, ControllerOutputCodec.KeyMask, (byte)mode);

    /// <summary>
    /// The room's mode. The protocol carries it as a byte it does not interpret; this is where a client turns it
    /// into the game's own type. Ask <see cref="CannotBuild"/> first: a byte no mode has is not one to build from.
    /// </summary>
    public static MatchMode ModeOf(MatchConfig config) => (MatchMode)config.Mode;

    /// <summary>
    /// The rules every client of a network match builds its world with, on the desktop and in the browser: the
    /// room's mode decides them, and a network match is a race, so the first base to tap the core wins.
    /// </summary>
    public static MatchRules RulesOf(MatchConfig config) => MatchRules.ForMode(ModeOf(config), coreTapWins: true);

    /// <summary>
    /// Why this client cannot build the room's match, or null when it can. The relay passes the settings through
    /// without reading the level generator or the mode, which this client must know.
    /// </summary>
    public static string? CannotBuild(MatchConfig config)
    {
        if (!Enum.IsDefined((LevelGenMode)config.GenMode))
            return string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"The room's level generator ({config.GenMode}) is not one this game knows. Update the game.");
        return Enum.IsDefined(ModeOf(config))
            ? null
            : string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"The room's match mode ({config.Mode}) is not one this game knows. Update the game.");
    }
}
