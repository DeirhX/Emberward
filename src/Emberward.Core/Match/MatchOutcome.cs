namespace Emberward.Core.Match;

using Emberward.Core.Entities.Tanks;
using Emberward.Core.Entities.Bases;

/// <summary>How the match stands for one tank (see <see cref="World.MatchOutcomeFor"/>).</summary>
public enum MatchOutcome
{
    InProgress,
    Victory,
    Defeat,
}

/// <summary>How a match was won (see <see cref="World.MatchWinner"/>).</summary>
public enum MatchWinReason
{
    LastBaseStanding,
    TappedTheCore,

    /// <summary>The clock ran out and this tank led on kills alone, at once or after sudden death (<see cref="MatchScore"/>).</summary>
    TopScore,
}

/// <summary>
/// Only losing a base eliminates a side. In a core race the first intact base to tap the core wins; in a timed match
/// the tank the score names when the clock has run out.
/// </summary>
public static class MatchReferee
{
    public static bool IsEliminated(Tank tank) => tank.Base?.IsDestroyed == true;

    public static bool NobodyIsLeft(IReadOnlyList<Tank> tanks)
    {
        foreach (var tank in tanks)
            if (!IsEliminated(tank))
                return false;
        return tanks.Count > 0;
    }

    /// <param name="topScorer">The tank a timed match's score has named the winner (<see cref="MatchScore.Winner"/>), or null.</param>
    public static (Tank Tank, MatchWinReason Reason)? Winner(IReadOnlyList<Tank> tanks, TankBase? firstToTapCore,
        Tank? topScorer = null)
    {
        if (firstToTapCore != null)
            foreach (var tank in tanks)
                if (tank.Base == firstToTapCore)
                    return (tank, MatchWinReason.TappedTheCore);
        Tank? last = null;
        int left = 0;
        foreach (var tank in tanks)
        {
            if (IsEliminated(tank))
                continue;
            last = tank;
            left++;
        }
        // The last side standing has won that way, even when the clock has run out and it also leads the score.
        if (left == 1 && tanks.Count > 1)
            return (last!, MatchWinReason.LastBaseStanding);
        return topScorer != null ? (topScorer, MatchWinReason.TopScore) : null;
    }

    public static MatchOutcome OutcomeFor(Tank player, Tank? winner)
    {
        if (winner == player)
            return MatchOutcome.Victory;
        return winner != null || IsEliminated(player) ? MatchOutcome.Defeat : MatchOutcome.InProgress;
    }
}
