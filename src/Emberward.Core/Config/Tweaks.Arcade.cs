namespace Emberward.Core.Config;

public static partial class Tweaks
{
    /// <summary>
    /// Arcade mode (DESIGN.md §6.8, <c>MatchRules.Arcade</c>): a match of fixed length won on kills, with quick respawns
    /// and bases that cannot fall. Starting values; tune in play.
    /// </summary>
    public static class Arcade
    {
        // A match fits a lunch break: eight minutes on the clock, then sudden death while the top score is tied.
        public const int TimeLimitSeconds = 480;

        // A dead tank is back after this long whatever its base's charge (MatchRules.QuickRespawn).
        public const int RespawnSeconds = 3;

        // A tank that dies of anything but an enemy's shell (heat, cold, a creature) within this long of an enemy's
        // shell hurting it is that enemy's kill (TankLifecycle.Killer): the tank that drove another into the heat or
        // the swarm scores for it. A fifth of deaths were to heat or creatures and scored for nobody, and nearly half
        // of those tanks had been hit by an enemy in the five seconds before.
        public const int KillCreditSeconds = 5;
        public const int KillCreditTicks = KillCreditSeconds * Perf.TargetFps;
    }
}
