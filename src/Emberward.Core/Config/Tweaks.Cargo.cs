namespace Emberward.Core.Config;

public static partial class Tweaks
{
    /// <summary>
    /// Carrying pyrite (DESIGN.md §6.3): the heat a load gives the tank, and where the tank can put it down.
    /// Starting values; tune in play.
    /// </summary>
    public static class Cargo
    {
        // Heat of a load, in degrees a second before cooling (TankHeatModel.ComputeCargoHeat). Every unit warms the
        // tank by HeatPerOrePerSecond: mild, 1.9 a second at 19 units, about what the off-network clock adds. At
        // HeatStartOre the load starts to cook the tank: StartHeatPerSecond more, and on top of that
        // SurgeHeatPerSecond * t^2, t = (ore - HeatStartOre) / (HeatDangerOre - HeatStartOre); past HeatDangerOre it
        // goes on rising along the line the curve leaves 40 on. So 20 units heat at 5 degrees a second, 30 at 22, and
        // 40 at 71: twelve times what a full-strength link takes away (Tank.NetworkCoolingPerSecond), so a tank with 40
        // aboard goes from cool to HeatSafeMax in a few seconds, in a tunnel or in the open, on the network or off it,
        // unless it unloads. (Ground soaks up a hot tank's heat fast, so these rates are what it takes to show.)
        public const float HeatPerOrePerSecond = 0.1f;
        public const int HeatStartOre = 20;
        public const int HeatDangerOre = 40;
        public const float StartHeatPerSecond = 3f;
        public const float SurgeHeatPerSecond = 64f;

        // Unloading (Tank.AdvanceUnload): only a tank that touches its own base's core, or one of its own powered
        // relays, puts its load down. Touching is a cell of the tank's shape within ContactReach cells of the core's
        // disc or of the relay's square. At the core the hold empties at Base.HomeAbsorbOre a tick; at a relay at
        // Ore.RelayUnloadPerSecond, the units travelling home along the links.
        public const int ContactReach = 2;
    }
}
