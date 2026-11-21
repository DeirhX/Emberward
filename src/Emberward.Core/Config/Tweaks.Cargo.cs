namespace Emberward.Core.Config;

public static partial class Tweaks
{
    /// <summary>
    /// Carrying pyrite (DESIGN.md §6.3): the heat a load gives the tank, and where the tank can put it down.
    /// Starting values; tune in play.
    /// </summary>
    public static class Cargo
    {
        // Heat of a load, in degrees a second before cooling (TankHeatModel.ComputeCargoHeat), linear in the ore held:
        // HeatPerOrePerSecond for every unit (mild: 1.9 a second at 19, about what the off-network clock adds), and
        // HeatPerOreAboveStartPerSecond more for every unit past HeatStartOre. No step and no spike: the rate is
        // continuous at 20 and climbs by the same amount for every unit beyond it. 20 units heat at 2 degrees a second,
        // 30 at 38, 40 at 74 (twelve times what a full-strength link takes away, Tank.NetworkCoolingPerSecond), so a
        // tank with 40 aboard passes HeatSafeMax in a few seconds unless it unloads; a cargo-upgraded hold goes on along
        // the same line (70 at 182, 100 at 290, 130 at 398). Ground soaks up a hot tank's heat fast, so these rates
        // are what it takes to show.
        public const float HeatPerOrePerSecond = 0.1f;
        public const int HeatStartOre = 20;
        public const float HeatPerOreAboveStartPerSecond = 3.5f;

        // Unloading (Tank.AdvanceUnload): only a tank that touches its own base's core, or one of its own powered
        // relays, puts its load down. Touching is a cell of the tank's shape within ContactReach cells of the core's
        // disc or of the relay's square. At the core the hold empties at Base.HomeAbsorbOre a tick; at a relay at
        // Ore.RelayUnloadPerSecond, the units travelling home along the links.
        public const int ContactReach = 2;
    }
}
