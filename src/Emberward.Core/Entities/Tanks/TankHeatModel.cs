namespace Emberward.Core.Entities.Tanks;

using Emberward.Core.Config;

public static class TankHeatModel
{
    public static float ComputeActionHeat(in SimulationSettings s, int frameDugPixels, bool frameShotFired)
    {
        float heat = frameDugPixels * s.TankHeatDigPerPixel;
        if (frameShotFired)
            heat += s.TankHeatShootPerShot;
        return heat;
    }

    /// <summary>
    /// Degrees a tick a load of <paramref name="ore"/> pyrite adds to the tank before cooling (DESIGN.md §6.3,
    /// <see cref="Tweaks.Cargo"/>): linear in the ore held, a mild share for every unit and a larger one for every unit
    /// past <see cref="CargoHeatStartOre"/>, continuous there, with no step or spike anywhere. An insulated hold
    /// (<paramref name="insulationLevel"/>, <c>BaseUpgrade.InsulatedHold</c>) divides the whole rate by
    /// <c>1 + TankCargoInsulationPerLevel * level</c>.
    /// </summary>
    public static float ComputeCargoHeat(in SimulationSettings s, int ore, int insulationLevel = 0)
    {
        if (ore <= 0)
            return 0f;
        float raw = ore * s.TankHeatPerOrePerTick +
            Math.Max(0, ore - CargoHeatStartOre(in s, insulationLevel)) * s.TankCargoHeatPerOreAboveStartPerTick;
        return raw / (1f + s.TankCargoInsulationPerLevel * Math.Max(0, insulationLevel));
    }

    /// <summary>
    /// How hard a fire of <paramref name="fireLevel"/> (0 to 1, <see cref="TankShieldSystem.FireLevelOf"/>) burns, as a
    /// share of a full fire's heat: the level raised to <c>TankFireHeatPower</c>, so a hull just alight gives almost
    /// nothing and a hull nearly gone nearly all of it (DESIGN.md §4.7). Plain multiplication, no transcendental.
    /// </summary>
    public static float FireHeatShare(in SimulationSettings s, float fireLevel)
    {
        float level = Math.Clamp(fireLevel, 0f, 1f);
        float share = 1f;
        for (int i = 0; i < s.TankFireHeatPower; i++)
            share *= level;
        return level > 0f ? share : 0f;
    }

    /// <summary>The load from which each unit heats the tank hard, at insulated hold level <paramref name="insulationLevel"/>.</summary>
    public static int CargoHeatStartOre(in SimulationSettings s, int insulationLevel = 0) =>
        s.TankCargoHeatStartOre + Math.Max(0, insulationLevel) * s.TankCargoHeatStartOrePerLevel;

    /// <summary>
    /// Heat flowing into the tank from what it touches this tick (negative: the tank loses heat). Terrain and air
    /// contact use the same formula, with the conductance and heat capacity of the contact.
    /// </summary>
    public static float ComputeHeatFlowIntoTank(in SimulationSettings s, float conductance, float tankTemperature,
        float surroundingTemperature, double surroundingCapacity)
    {
        if (s.TankHeatCapacity <= 0f || surroundingCapacity <= 0.0)
            return 0f;
        // Moving Q changes the temperature difference by Q * (1/Ctank + 1/Cfield).
        double equilibriumConductance = 1.0 / (1.0 / s.TankHeatCapacity + 1.0 / surroundingCapacity);
        return (float)(Math.Min(conductance, equilibriumConductance) * (surroundingTemperature - tankTemperature));
    }

    public static float ComputeTankDeltaFromHeatFlow(in SimulationSettings s, float dQ)
    {
        return s.TankHeatCapacity > 0f ? dQ / s.TankHeatCapacity : 0f;
    }

    public static int ComputeOverheatDamage(in SimulationSettings s, float heat)
    {
        if (heat <= s.TankHeatSafeMax) return 0;
        float excess = heat - s.TankHeatSafeMax;
        return (int)(excess * s.TankOverheatDamagePerDegree);
    }

    /// <summary>Damage from deep cold: below the frostbite temperature, per degree, like overheating.</summary>
    public static int ComputeFrostbiteDamage(in SimulationSettings s, float heat)
    {
        if (heat >= s.TankFrostbiteTemperature) return 0;
        float deficit = s.TankFrostbiteTemperature - heat;
        return (int)(deficit * s.TankFrostbiteDamagePerDegree);
    }

    /// <summary>
    /// Fraction of its normal speed a tank at <paramref name="heat"/> moves at: full speed at or above
    /// <c>ColdSlowStart</c>, falling linearly to <c>ColdMinSpeed</c> at <c>ColdSlowFull</c> and below.
    /// </summary>
    public static float ColdSpeedFactor(float heat)
    {
        float start = Tweaks.Tank.ColdSlowStart, full = Tweaks.Tank.ColdSlowFull;
        if (heat >= start) return 1f;
        float t = Math.Clamp((start - heat) / (start - full), 0f, 1f);
        return 1f - (1f - Tweaks.Tank.ColdMinSpeed) * t;
    }
}
