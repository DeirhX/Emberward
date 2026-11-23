namespace Emberward.Desktop.Net;

using Emberward.Core.Match;

/// <summary>
/// The match modes as the hosts write and show them: on the command line, in the browser's address, in the settings
/// file and in the lobby. What a mode does is Core's (<see cref="MatchRules.ForMode"/>).
/// </summary>
public static class MatchModes
{
    /// <summary>Every mode, in the order a menu offers them.</summary>
    public static IReadOnlyList<MatchMode> All { get; } = Enum.GetValues<MatchMode>();

    /// <summary>The mode as it is typed: <c>--mode=arcade</c>, <c>?mode=arcade</c>, <c>create.mode=arcade</c>.</summary>
    public static string Name(MatchMode mode) => mode switch
    {
        MatchMode.Arcade => "arcade",
        _ => "standard",
    };

    /// <summary>The mode as a menu or a lobby shows it.</summary>
    public static string Label(MatchMode mode) => mode switch
    {
        MatchMode.Arcade => "Arcade",
        _ => "Standard",
    };

    /// <summary>What a room of the mode is, in the three words there is room for beside the choice.</summary>
    public static string RoomHint(MatchMode mode) => mode switch
    {
        MatchMode.Arcade => "timed, for score",
        _ => "race to the core",
    };

    /// <summary>The mode <paramref name="text"/> names (<see cref="Name"/>, in any case), or null when it names none.</summary>
    public static MatchMode? Parse(string? text)
    {
        foreach (MatchMode mode in All)
            if (string.Equals(text?.Trim(), Name(mode), StringComparison.OrdinalIgnoreCase))
                return mode;
        return null;
    }

    /// <summary>The mode <paramref name="step"/> places on from <paramref name="mode"/> in <see cref="All"/>, going round.</summary>
    public static MatchMode Step(MatchMode mode, int step)
    {
        int at = Math.Max(0, All.ToList().IndexOf(mode));
        return All[((at + step) % All.Count + All.Count) % All.Count];
    }
}
