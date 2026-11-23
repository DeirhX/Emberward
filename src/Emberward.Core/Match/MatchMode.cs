namespace Emberward.Core.Match;

/// <summary>
/// The kind of match a host offers; <see cref="MatchRules.ForMode"/> gives its rules. The numbers travel (a host tells
/// the others which mode it chose), so they do not change.
/// </summary>
public enum MatchMode : byte
{
    /// <summary>The long game: networks, the core, and a side is out when its base falls.</summary>
    Standard = 0,

    /// <summary>A timed match won on kills, with quick respawns and bases that hold (<see cref="MatchRules.Arcade"/>).</summary>
    Arcade = 1,
}
