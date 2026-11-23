namespace Emberward.Core.Config;

public static partial class Tweaks
{
    /// <summary>
    /// The tank's shield (DESIGN.md §4.7), in a match whose rules run it (<c>MatchRules.TankShield</c>): a regenerating
    /// layer in front of the hull. Damage takes the shield first and the overflow goes to the hull, which only the
    /// base repairs. Starting values; tune in play.
    /// </summary>
    public static class TankShield
    {
        // 400 points is two and a half tank shells (BulletDamage 160) in front of the 1000-point hull.
        public const int Capacity = 400;

        // Below this share of the shield the HUD calls it low.
        public const float LowFraction = 0.25f;

        // It refills only while the tank is on its network and its base is not dark; elsewhere it holds what it has. After a hit it waits this long (4 s at TargetFps 24), then refills RegenPerTick a tick:
        // empty to full in about 5.6 s.
        public const int RegenDelayTicks = 96;
        public const int RegenPerTick = 3;

        // Fire (DESIGN.md §4.7). A hull below FireIgniteHullFraction burns, and the fire (its level, what is drawn) grows
        // linearly as the hull falls: nothing at that fraction, full size at an empty hull. Its heat grows much faster,
        // as the level to the power FireHeatPower: at full size it adds FireTankHeatPerTickMax degrees a tick to the tank
        // (48 a second at 24 ticks, eight times what a full-strength link takes away) and puts FireAirEnergyPerTickMax
        // energy a tick into the air around it (as much again as the tank keeps). Squared, a 70% hull adds 0.2 degrees a
        // second (nothing a link notices), 60% 1.9, 50% 5.3 (about a link), 30% 17, 10% 36: a minor wound is shrugged off, a
        // major one cooks the tank within about 20 s even on a full link, sooner in hot ground, later in cold, because the
        // tank sheds the fire's heat through its ordinary exchange with the ground and air it touches. Only the base puts
        // it out, by repairing the hull.
        public const float FireIgniteHullFraction = 0.75f;
        public const int FireHeatPower = 2;
        public const float FireTankHeatPerTickMax = 2.0f;
        public const double FireAirEnergyPerTickMax = 40.0;
        public const int FireAirRadius = 3;
        // A burning hull loses at most this many hull points a second to overheating, however hot the fire has made
        // the tank (about 2 a tick at 24 ticks; TankThermals spreads the fraction evenly). The fire's heat can climb far
        // past HeatSafeMax, and at Tank.OverheatDamagePerDegree a tick that took a hull in a moment. Shots are not
        // capped, and a tank whose hull is not alight overheats at the plain rate.
        public const int FireMaxHullLossPerSecond = 50;
    }
}
