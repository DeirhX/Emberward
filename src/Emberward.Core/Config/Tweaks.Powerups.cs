namespace Emberward.Core.Config;

public static partial class Tweaks
{
    /// <summary>
    /// Powerup drops, the pickups of an arcade match (DESIGN.md §6.8, <c>PowerupDrops</c>). Starting values; tune in
    /// play.
    /// </summary>
    public static class Powerups
    {
        // ---- Schedule ----------------------------------------------------------------------------------------
        // The first drop lands FirstDropSeconds into the match. The moment a drop lands, the next is decided (place,
        // kind and time) and lands DropPeriodSeconds later, so one upcoming drop is always known a full period ahead.
        // A drop nobody takes lies on the ground for GroundSeconds: no longer than the period, so the old one is gone
        // as the new one lands and at most one lies at a time (73% of drops were taken within 20 s of landing and 8%
        // after 40 s; with 60 s on the ground two lay at once 4.6% of the time).
        public const int FirstDropSeconds = 45;
        public const int DropPeriodSeconds = 40;
        public const int GroundSeconds = 40;
        public const int FirstDropTicks = FirstDropSeconds * Perf.TargetFps;
        public const int DropPeriodTicks = DropPeriodSeconds * Perf.TargetFps;
        public const int GroundTicks = GroundSeconds * Perf.TargetFps;

        // ---- Pickup ------------------------------------------------------------------------------------------
        // A drop is a disc this many cells across; the first living tank with a cell of its sprite in the disc takes it.
        public const int PickupDiameter = 7;

        // ---- Kinds -------------------------------------------------------------------------------------------
        // Overcharge: the tank's shells deal OverchargeDamageFactor times their damage for OverchargeSeconds.
        public const int OverchargeSeconds = 20;
        public const int OverchargeDamageFactor = 2;
        // Bulwark: everything that hurts the tank takes one part in BulwarkDamageDivisor for BulwarkSeconds.
        public const int BulwarkSeconds = 20;
        public const int BulwarkDamageDivisor = 2;
        // Drill: the tank digs soil at its open-ground speed for DrillSeconds.
        public const int DrillSeconds = 25;

        // ---- Sites -------------------------------------------------------------------------------------------
        // A site is the best of a bounded number of draws: WideDraws over the ground between the bases, then
        // RefineDraws, each near the best so far. No pass over the map.
        public const int WideDraws = 128;
        public const int RefineDraws = 64;
        // Distances scale with the map: each is a share of the spacing of the two nearest bases, and at least the
        // cells given. A site is at least MinBaseDistance from every base and MinDropSpacing from the site before it
        // (whose drop may still lie there). One within MinDropSpacing of the site before that counts as less fair by
        // half that spacing, so sites move about unless fairness forbids.
        public const int MinBaseDistance = 30;
        public const int MinBaseDistancePercent = 30;
        public const int MinDropSpacing = 24;
        public const int MinDropSpacingPercent = 20;
        // A draw whose unfairness (cells of difference between base distances, see PowerupSites) is within this share
        // of the spacing is taken at once; otherwise the fairest draw is.
        public const int FairTolerancePercent = 5;
        // Cells from a site to the edge of the hot core, of a hot or frost pocket and of a nest's ground, and the
        // warmest and coldest ground (degrees) a site may have when it is chosen. The warm bound is well short of
        // what hurts a tank (Tank.HeatSafeMax, 100) and no nearer rest than it must be: the ground that is equally
        // far from every base lies round the core, where the fighting warms it past 20 degrees within two minutes,
        // and a bound of 20 drove the everyone's drops off it (the farthest base 27 cells farther than the nearest
        // for the first at the median of 100 matches, 50 to 62 from the third on; with 40, 35 to 41).
        public const int CoreClearance = 12;
        public const int PocketClearance = 9;
        public const int NestClearance = 4;
        public const float MaxSiteHeat = 40f;
        public const float MinSiteHeat = -20f;
        // With no draw that can be used, the first of a fixed lattice of this many rings round the middle of the bases is.
        public const int FallbackRings = 6;

        // ---- Bots --------------------------------------------------------------------------------------------
        // A roaming bot heads for a drop on the ground, or one that lands within BotSeekLeadSeconds, when its site is
        // within BotSeekRange cells. One it gets no nearer for BotSeekGiveUpSeconds it leaves alone. Within
        // BotArriveCells of a site whose drop has yet to land it stands and waits.
        public const int BotSeekLeadSeconds = 15;
        public const int BotSeekRange = 150;
        public const int BotSeekGiveUpSeconds = 8;
        public const int BotArriveCells = 3;
    }
}
