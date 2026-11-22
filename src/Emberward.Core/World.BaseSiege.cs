namespace Emberward.Core;

using Emberward.Core.Config;
using Emberward.Core.Entities.Bases;
using Emberward.Core.Entities.Tanks;

public partial class World
{
    /// <summary>Ticks in one step of the siege reckoning (<see cref="BaseFallsTo"/>): a second.</summary>
    private const int SiegeReckoningStep = Tweaks.Perf.TargetFps;

    /// <summary>
    /// Whether <paramref name="attacker"/> alone has the power to destroy <paramref name="target"/> in one outing. The
    /// bot asks this before it lays siege: a base it cannot break it leaves alone, and waits outside while its owner is
    /// docked there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reckoning plays the siege forward a second at a time, from the state both sides are in now, by the rules the
    /// simulation applies (<see cref="BaseFallsTo"/>). Each side gets its best: every shell the attacker fires lands,
    /// and the base defends with all it holds now and all it can add while the attacker fires.
    /// </para>
    /// <para>
    /// The attacker fires at its gun's rate (a slowed gun counts slower, one that cannot fire counts nothing,
    /// <see cref="TankReserveEffects"/>) for as long as it would stay out (<see cref="SiegeFireTicks"/>): at most
    /// <see cref="Tweaks.Bot.SiegeWindowSeconds"/>, and only until its off-network clock sends it home. The drive to the
    /// base is not counted (the clock as it stands now is), nor are shells that miss, nor the charge the base's
    /// generators bring in during the siege: these lean the attacker's way, so a base it is told it cannot break it
    /// surely cannot.
    /// </para>
    /// <para>
    /// The base soaks the shells with its gate shield while its gates are shut, and buys shield back as
    /// <c>BasePowerSystem</c> does: at its recharge rate, from the charge its store holds above the reserve, and only as
    /// far as its reactor has room for the heat (what is free now plus what the radiator vents). Gates that are open, or
    /// open during the siege, shut again once the shield has grown back to <see cref="Tweaks.Keep.ShieldRestoreFill"/>,
    /// and that shield must be shot through again. While the gates stand open the core takes the shells (more of each
    /// while the base is dark), and it repairs itself only while it is damaged. A dark base buys no shield and repairs
    /// nothing.
    /// </para>
    /// </remarks>
    public bool CanBreakBase(Tank attacker, TankBase target) => CanBreakBase(attacker, target, fireFactor: 1.0);

    /// <summary>
    /// <see cref="CanBreakBase(Tank, TankBase)"/> with the attacker's fire counted <paramref name="fireFactor"/> times as
    /// strong: the bot holds a decision it has made with a margin (<see cref="Tweaks.Bot.SiegeHoldFireFactor"/>).
    /// </summary>
    public bool CanBreakBase(Tank attacker, TankBase target, double fireFactor)
    {
        if (target.IsDestroyed || ReferenceEquals(attacker.Base, target))
            return false;
        var gun = TankReserveEffects.For(ReserveOf(attacker).Stage);
        if (!gun.CanShoot)
            return false;
        int fireDelay = Tweaks.Tank.TurretDelay * gun.FireDelayFactor;
        double damagePerTick = fireFactor * Tweaks.Weapon.BulletDamage / fireDelay;
        return BaseFallsTo(target, damagePerTick, SiegeFireTicks(attacker, fireDelay));
    }

    /// <summary>
    /// Ticks <paramref name="attacker"/> can keep firing at a base before it heads home (the bot's own rule, which ends
    /// an outing on heat): at most <see cref="Tweaks.Bot.SiegeWindowSeconds"/>. A base it besieges lies off its network,
    /// so its off-network floor climbs from where it is now, until the heat the bot turns back at
    /// (<see cref="Tweaks.Bot.ReturnHeatBudgetFractionOffNetwork"/> of its budget left, or the off-network clock at
    /// <see cref="Tweaks.Tank.OffNetworkUrgentSeconds"/>). Steady fire holds the tank above its surroundings by what its
    /// shot heat takes to shed into the ground (shot heat a tick times the tank's heat capacity over its ground
    /// conductance), which comes off that room. A gun that fires every <paramref name="fireDelay"/> ticks.
    /// </summary>
    /// <remarks>
    /// The bot turns back before its reserve reaches the stage that slows its gun (the urgent mark is above
    /// <see cref="Tweaks.Reserve.DegradedSeconds"/>), so the gun keeps its present rate for the whole outing.
    /// </remarks>
    public int SiegeFireTicks(Tank attacker, int fireDelay)
    {
        const int window = Tweaks.Bot.SiegeWindowSeconds * Tweaks.Perf.TargetFps;
        var settings = _simulationSettings;
        double rise = settings.TankOffNetworkHeatPerTick * (double)(attacker.Base?.Upgrades.OffNetworkHeatFactor ?? 1f);
        if (rise <= 0.0)
            return window;
        double shotHeat = settings.TankHeatShootPerShot / (double)fireDelay;
        double firingExcess = settings.TankTerrainConductance > 0f
            ? shotHeat * settings.TankHeatCapacity / settings.TankTerrainConductance
            : shotHeat * window;
        double safeMax = Tweaks.Tank.HeatSafeMax;
        double turnBackAt = Math.Min(safeMax * (1.0 - Tweaks.Bot.ReturnHeatBudgetFractionOffNetwork),
            safeMax - Tweaks.Tank.OffNetworkUrgentSeconds * (double)attacker.OffNetworkHeatPerSecond);
        double ticks = (turnBackAt - firingExcess - attacker.OffNetworkFloor) / rise;
        return (int)Math.Clamp(ticks, 0.0, window);
    }

    /// <summary>
    /// Plays <paramref name="fireTicks"/> of steady fire at <paramref name="damagePerTick"/> against
    /// <paramref name="target"/> as it stands now (see <see cref="CanBreakBase(Tank, TankBase)"/>), a second at a time:
    /// whether its core falls.
    /// </summary>
    private bool BaseFallsTo(TankBase target, double damagePerTick, int fireTicks)
    {
        bool dark = IsBaseDark(target);
        var power = _basePower;
        bool buysShield = power != null && !dark;
        double shield = power?.ShieldPoints(target) ?? 0.0;
        bool gatesOpen = power == null || !power.GatesShut(target);
        double restoreAt = Tweaks.Keep.ShieldRestoreFill * Tweaks.Keep.ShieldCapacity;
        double spareCharge = Math.Max(0.0, target.Charge - Tweaks.Keep.ShieldReserveCharge);
        double reactorRoom = target.ReactorRoomEnergy;
        double reactorCapacity = (int)target.Reactor.HeatCapacity * (double)Tweaks.Base.ReactorEnergyPerHeatUnit;
        double core = (int)target.Reactor.Health;
        double coreCapacity = (int)target.Reactor.HealthCapacity;
        double coreHit = dark ? Tweaks.Base.DarkDamageMultiplier : 1.0;

        for (int done = 0; done < fireTicks; done += SiegeReckoningStep)
        {
            int ticks = Math.Min(SiegeReckoningStep, fireTicks - done);
            double shells = damagePerTick * ticks;
            if (!gatesOpen)
            {
                double absorbed = Math.Min(shield, shells);
                shield -= absorbed;
                shells -= absorbed;
                gatesOpen = shield <= 0.0;
            }
            if (gatesOpen)
            {
                core -= shells * coreHit;
                if (core <= 0.0)
                    return true;
            }
            if (dark)
                continue;
            if (buysShield)
            {
                double bought = Math.Min(Math.Min(Tweaks.Keep.ShieldRechargePerTick * ticks, Tweaks.Keep.ShieldCapacity - shield),
                    Math.Min(spareCharge, reactorRoom) / Tweaks.Keep.ChargePerShieldPoint);
                bought = Math.Max(0.0, bought);
                shield += bought;
                spareCharge -= bought * Tweaks.Keep.ChargePerShieldPoint;
                reactorRoom = Math.Min(reactorCapacity,
                    reactorRoom - bought * Tweaks.Keep.ChargePerShieldPoint + target.VentCapacityPerTick * ticks);
                if (gatesOpen && shield >= restoreAt)
                    gatesOpen = false;
            }
            if (core < coreCapacity)
                core = Math.Min(coreCapacity, core + Tweaks.Base.HealthRegen * (double)ticks);
        }
        return false;
    }
}
