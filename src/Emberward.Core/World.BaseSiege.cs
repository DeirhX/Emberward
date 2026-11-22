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
    /// <see cref="Tweaks.Bot.SiegeWindowSeconds"/>, and only until its off-network clock sends it home, a clock that
    /// first runs for the drive to the base.
    /// </para>
    /// <para>
    /// It is a reckoning, not a forecast, and what it leaves out does not all lean one way. Toward the attacker: shells
    /// that miss, the charge the base's generators bring in during the siege, and ground the attacker must dig through
    /// on its way (the drive is counted at open-ground pace in a straight line). Toward the base: the charge the base's
    /// own machines and turrets spend meanwhile, which leaves less to buy shield back with; the whole drive counted off
    /// the attacker's network, though the first of it may lie in coverage; and a generator of the attacker's side near
    /// the base, which would hold its off-network clock (<see cref="TankReserveSystem.HoldsClock"/>), while the
    /// reckoning lets the clock run. A bot told it cannot break a base can be wrong near the edge; the margin it holds a
    /// decision with (<see cref="Tweaks.Bot.SiegeHoldFireFactor"/>) keeps it from changing its mind at every tick there.
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
        return BaseFallsTo(target, damagePerTick, SiegeFireTicks(attacker, target, fireDelay));
    }

    /// <summary>
    /// Ticks <paramref name="attacker"/> can keep firing at <paramref name="target"/> before it heads home (the bot's own
    /// rule, which ends an outing on heat): at most <see cref="Tweaks.Bot.SiegeWindowSeconds"/>. A base it besieges lies
    /// off its network, so its off-network floor climbs from where it is now, first for the drive there
    /// (<see cref="SiegeDriveTicks"/>) and then while it fires, until the heat the bot turns back at
    /// (<see cref="Tweaks.Bot.ReturnHeatBudgetFractionOffNetwork"/> of its budget left, or the off-network clock at
    /// <see cref="Tweaks.Tank.OffNetworkUrgentSeconds"/>). Steady fire holds the tank above its surroundings by what its
    /// shot heat takes to shed into the ground (shot heat a tick times the tank's heat capacity over its ground
    /// conductance), which comes off that room. A gun that fires every <paramref name="fireDelay"/> ticks.
    /// </summary>
    /// <remarks>
    /// Counting the drive up front gives the bot the same answer at home as on its way: the clock it spends driving is
    /// the clock it no longer has once it arrives. The bot turns back before its reserve reaches the stage that slows its
    /// gun (the urgent mark is above <see cref="Tweaks.Reserve.DegradedSeconds"/>), so the gun keeps its present rate for
    /// the whole outing.
    /// </remarks>
    public int SiegeFireTicks(Tank attacker, TankBase target, int fireDelay)
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
        double ticks = (turnBackAt - firingExcess - attacker.OffNetworkFloor) / rise - SiegeDriveTicks(attacker, target);
        return (int)Math.Clamp(ticks, 0.0, window);
    }

    /// <summary>
    /// Ticks <paramref name="attacker"/> needs to reach <paramref name="target"/>'s wall: the cells to it (a tank steps
    /// one cell a tick, diagonals included, so the larger of the two distances) at the pace it moves now (slowed by the
    /// cold, <see cref="Tank.SpeedFactor"/>, and by its reserve, <see cref="TankReserveEffects"/>). Open ground in a
    /// straight line: digging and detours are not counted.
    /// </summary>
    public double SiegeDriveTicks(Tank attacker, TankBase target)
    {
        int half = _simulationSettings.BaseSize / 2;
        int cells = Math.Max(Math.Abs(attacker.Position.X - target.Position.X), Math.Abs(attacker.Position.Y - target.Position.Y)) - half;
        if (cells <= 0)
            return 0.0;
        double pace = attacker.SpeedFactor * (double)TankReserveEffects.For(ReserveOf(attacker).Stage).MoveFactor;
        return pace > 0.0 ? cells / pace : double.PositiveInfinity;
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
