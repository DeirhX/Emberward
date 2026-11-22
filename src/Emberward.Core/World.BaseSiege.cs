namespace Emberward.Core;

using Emberward.Core.Config;
using Emberward.Core.Entities.Bases;
using Emberward.Core.Entities.Tanks;

public partial class World
{
    /// <summary>
    /// Whether <paramref name="attacker"/> alone has the power to destroy <paramref name="target"/>: what its gun
    /// fires in <see cref="Tweaks.Bot.SiegeWindowSeconds"/> of steady fire is at least what the base can soak up in
    /// that time. The base soaks up its gate shield while the gates are shut, and the shield its store can still buy
    /// back over the window (none while dark); then its core's armour and what the core repairs over the window (none
    /// while dark, where a shell also hits harder). A slowed gun counts slower and a gun that cannot fire counts
    /// nothing (<see cref="TankReserveEffects"/>). The bot asks this before it lays siege: a base it cannot break it
    /// leaves alone, and waits outside while its owner is docked there.
    /// </summary>
    public bool CanBreakBase(Tank attacker, TankBase target)
    {
        if (target.IsDestroyed || ReferenceEquals(attacker.Base, target))
            return false;
        var gun = TankReserveEffects.For(ReserveOf(attacker).Stage);
        if (!gun.CanShoot)
            return false;

        const int ticks = Tweaks.Bot.SiegeWindowSeconds * Tweaks.Perf.TargetFps;
        double fired = (double)Tweaks.Weapon.BulletDamage * ticks / (Tweaks.Tank.TurretDelay * gun.FireDelayFactor);

        bool dark = IsBaseDark(target);
        double soaked = 0.0;
        if (_basePower is { } power && power.GatesShut(target))
        {
            soaked += power.ShieldPoints(target);
            if (!dark)
            {
                double affordable = Math.Max(0.0, target.Charge - Tweaks.Keep.ShieldReserveCharge) / Tweaks.Keep.ChargePerShieldPoint;
                soaked += Math.Min(Tweaks.Keep.ShieldRechargePerTick * (double)ticks, affordable);
            }
        }
        double core = (int)target.Reactor.Health;
        soaked += dark ? core / Tweaks.Base.DarkDamageMultiplier : core + Tweaks.Base.HealthRegen * (double)ticks;
        return fired >= soaked;
    }
}
