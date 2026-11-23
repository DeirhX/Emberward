namespace Emberward.Browser.Client;

using Emberward.Core.Config;
using Emberward.Core.Match;
using Emberward.Desktop.Net;

/// <summary>
/// What the page's address asks of a match (<c>?mode=arcade</c>, <c>?players=4</c>), read as the desktop reads its
/// command line. The page passes the parameters on as it found them; what they mean is decided here, where the tests
/// reach it.
/// </summary>
public static class BrowserLaunch
{
    /// <summary>
    /// The mode <paramref name="text"/> names (<c>standard</c>, <c>arcade</c>, in any case); the standard one when
    /// it is missing or names none.
    /// </summary>
    public static MatchMode Mode(string? text) => MatchModes.Parse(text) ?? MatchMode.Standard;

    /// <summary>
    /// The tanks of a local match for <c>?players=N</c>: N from 2 to the most a match takes; the normal two when it
    /// is missing (the page passes 0) or outside that.
    /// </summary>
    public static int Players(int asked) =>
        asked is >= 2 and <= Tweaks.World.MaxPlayers ? asked : Tweaks.LevelGen.DefaultPlayers;
}
