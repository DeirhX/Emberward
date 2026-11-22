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
        // tank with 40 aboard passes HeatSafeMax in a few seconds unless it unloads. Ground soaks up a hot tank's heat
        // fast, so these rates are what it takes to show.
        public const float HeatPerOrePerSecond = 0.1f;
        public const int HeatStartOre = 20;
        public const float HeatPerOreAboveStartPerSecond = 3.5f;

        // The insulated hold upgrade (BaseUpgrade.InsulatedHold, mostly insulation and a little room: Upgrades.
        // InsulatedHoldOrePerLevel). Each level moves the start HeatStartOrePerLevel units later and divides the whole
        // rate by 1 + InsulationPerLevel * level, so the heat stays linear in the load at every level. With the room
        // each level adds, a full hold is exactly as hot as the stock hold at 40: 50 units at level 1, 60 at 2 and 70
        // at 3 all heat at 74 a second, and half a hold (20, 25, 30, 35) is where the heating starts at each level.
        public const int HeatStartOrePerLevel = 5;
        public const float InsulationPerLevel = 0.25f;

        // Unloading (Tank.AdvanceUnload): only a tank that touches its own base's core, or one of its own powered
        // relays, puts its load down. Touching is a cell of the tank's shape within ContactReach cells of the core's
        // disc or of the relay's square. At the core the hold empties at Base.HomeAbsorbOre a tick; at a relay at
        // Ore.RelayUnloadPerSecond, the units travelling home along the links.
        public const int ContactReach = 2;

        // Heat a unit of cargo brings into the base reactor when the tank banks it (TankBase.BankFromHold), in energy
        // units, as much as the reactor has room for, booked as injected: pyrite is hot. The reactor holds 50,000 and
        // the stock radiator vents 500 a tick, so a full hold of 40 (10,000) is a fifth of the store, vented in under a
        // second when nothing else feeds the reactor. The bank comes after the tank's dock pump, so it never takes room
        // that tick's cooling wanted; but the heat it leaves takes room in the next ticks, so near a full reactor a
        // deposit costs some later cooling, relay extraction and shield recharge. Harvested ore and ore burned to
        // restart a dark base bring none. At 0 banking adds no heat.
        public const float DepositHeatPerOre = 250f;
    }
}
