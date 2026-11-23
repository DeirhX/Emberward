namespace Emberward.Core;

using Emberward.Core.Entities.Tanks;
using Emberward.Core.Match;

public partial class World
{
    /// <summary>
    /// <paramref name="tank"/>'s line on the board of a timed match (DESIGN.md §6.8): its kills, deaths and place.
    /// Inactive (the default) when the rules set no time limit (<see cref="MatchRules.TimeLimitSeconds"/>). Output only.
    /// </summary>
    public TankScore ScoreOf(Tank tank) => _score?.ScoreOf(tank) ?? default;

    /// <summary>
    /// The clock of a timed match: ticks played, the limit, and whether it has run into sudden death. Inactive (the
    /// default) when the rules set no time limit. It stops when the match is over, however it was won, and is then
    /// in sudden death no longer. Output only.
    /// </summary>
    public MatchClock MatchClock =>
        _score is not { } score ? default
        : _matchResult.IsDecided ? score.Clock with { IsSuddenDeath = false }
        : score.Clock;

    /// <summary>
    /// Writes the board of a timed match in rank order (most kills, then fewest deaths, then lowest colour); returns
    /// how many lines, 0 when the rules set no time limit. Allocates nothing. Output only.
    /// </summary>
    public int CopyStandings(Span<TankScore> destination) => _score?.CopyStandings(destination) ?? 0;

    /// <summary>
    /// The tank that leads a timed match alone, which wins when the clock runs out; null while the top score is tied
    /// (as before the first kill) and when the rules set no time limit. Output only.
    /// </summary>
    public Tank? ScoreLeader => _score?.SoleLeader();

    /// <summary>The score and clock of a timed match, for the hashes; null when the match rules set no time limit.</summary>
    internal MatchScore? ScoreSystem => _score;

    /// <summary>The tank the score has named the winner (the clock ran out and it leads alone), or null.</summary>
    private Tank? ScoreWinner => _score?.Winner;

    /// <summary>One step of the score, while the match is undecided: after it the board and the clock stand as they ended.</summary>
    private void AdvanceScore(MatchScore score)
    {
        if (!_matchResult.IsDecided)
            score.Advance();
    }
}
