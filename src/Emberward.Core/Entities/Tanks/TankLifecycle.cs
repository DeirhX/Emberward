namespace Emberward.Core.Entities.Tanks;

using Emberward.Core.Config;
using Emberward.Core.Entities.Projectiles;
using Emberward.Core.Resources;
using Emberward.Core.Terrain;
using Emberward.Core.Types;

/// <summary>
/// Damage, death and respawn: records why armor was lost, runs the hit flash and the respawn shield, and counts
/// down to the next spawn once the tank is destroyed.
/// </summary>
internal sealed class TankLifecycle
{
    private readonly Tank _tank;
    private readonly TankBuildController _build;
    private bool _protectedUntilExit;
    private TimeSpan _respawnTimer;
    private bool _respawning;

    // The kill credit: the last enemy tank whose shell hurt this one, and the ticks its claim still holds. A death
    // by anything but an enemy's shell within them is that tank's kill.
    private int _claimant = -1;
    private int _claimTicks;

    public TankLifecycle(Tank tank, TankBuildController build)
    {
        _tank = tank;
        _build = build;
    }

    public DamageCause LastDamageCause { get; private set; }
    public DamageCause DeathCause { get; private set; }

    /// <summary>
    /// The colour of the tank this one's death is credited to; -1 while alive and when it is nobody's. The one place
    /// that says (<see cref="TakeDamage"/>): the enemy whose shell took the last of the hull, or, when something else
    /// did (heat, cold, a creature), the last enemy whose shell hurt it in the
    /// <see cref="Tweaks.Arcade.KillCreditSeconds"/> before. Never the tank itself, and nobody for a recall or the
    /// loss of its base.
    /// </summary>
    public int Killer { get; private set; } = -1;
    public int HitFlashTicks { get; private set; }
    public VectorF LastHitVelocity { get; private set; }
    public int SpawnProtectionTicks { get; private set; }

    public TimeSpan RespawnRemaining =>
        _respawning && _tank.Base is { IsDestroyed: false } ? _respawnTimer : TimeSpan.Zero;

    public void BeginTick()
    {
        if (HitFlashTicks > 0)
            HitFlashTicks--;
        if (_claimTicks > 0)
            _claimTicks--;
    }

    /// <summary>What the last hit took, from the shield and from the hull; nothing when it was refused.</summary>
    public DamageTaken LastDamageTaken { get; private set; }

    /// <summary>
    /// Takes armor off the tank. With a shield, it takes the hit first and the hull the rest. Heat and cold, losing the
    /// base and a recall go straight to the hull: a shield stops shells, not the temperature, and never keeps a tank
    /// alive through the last two. An enemy's shell that takes anything gives its tank (<paramref name="attacker"/>,
    /// the colour of the tank whose shell it was) a claim on the kill for <see cref="Tweaks.Arcade.KillCreditSeconds"/>,
    /// and the hit that destroys the tank names its <see cref="Killer"/>.
    /// </summary>
    public bool TakeDamage(int amount, DamageCause cause, VectorF hitVelocity, int attacker = -1)
    {
        LastDamageTaken = default;
        if (amount <= 0 || _tank.IsDead)
            return false;
        if (cause == DamageCause.EnemyFire && SpawnProtectionTicks > 0)
            return false;
        // A bulwark takes its share of everything that hurts, before the shield; losing the base and a recall still kill.
        if (_tank.Powerups is { } powerups && cause is not (DamageCause.BaseDestroyed or DamageCause.Recalled))
        {
            amount = powerups.ReduceDamage(_tank, amount);
            if (amount <= 0)
                return false;
        }
        bool bypass = cause is DamageCause.BaseDestroyed or DamageCause.Recalled or DamageCause.Overheat or DamageCause.Frostbite;
        var taken = _tank.Shield is { } shield && !bypass ? shield.Absorb(_tank, amount) : new DamageTaken(0, amount);
        if (taken.Hull > 0)
            _tank.Reactor.Exhaust(new ReactorState(0, taken.Hull));
        LastDamageTaken = taken;
        LastDamageCause = cause;
        bool enemyShell = cause == DamageCause.EnemyFire && attacker >= 0 && attacker != _tank.Color;
        if (cause == DamageCause.EnemyFire)
        {
            HitFlashTicks = Tank.HitFlashDuration;
            LastHitVelocity = hitVelocity;
        }
        if (enemyShell)
        {
            // The claim holds for KillCreditTicks steps, the step the shell lands in being the first. One more
            // here, because the tank's own tick counts that step down before anything in it can kill.
            _claimant = attacker;
            _claimTicks = Tweaks.Arcade.KillCreditTicks + 1;
        }
        if (_tank.IsDead)
        {
            DeathCause = cause;
            Killer = CreditFor(cause, enemyShell ? attacker : -1);
        }
        return true;
    }

    /// <summary>
    /// Whose kill a death by <paramref name="cause"/> is: the enemy whose shell it was (<paramref name="shellOf"/>,
    /// -1 when it was no enemy's shell); for any other death but a recall or the loss of the base, the enemy whose
    /// claim still holds; else nobody.
    /// </summary>
    private int CreditFor(DamageCause cause, int shellOf)
    {
        if (shellOf >= 0)
            return shellOf;
        if (cause is DamageCause.Recalled or DamageCause.BaseDestroyed)
            return -1;
        return _claimTicks > 0 ? _claimant : -1;
    }

    /// <summary>
    /// The respawn shield holds while the tank is in its own base, then lasts <see cref="Tank.SpawnProtectionDuration"/>
    /// more ticks after it leaves; it never exceeds <see cref="Tank.SpawnProtectionCap"/> and firing ends it at once.
    /// </summary>
    public void UpdateSpawnProtection(bool inOwnBase, bool firing)
    {
        if (SpawnProtectionTicks <= 0)
            return;
        if (firing)
        {
            SpawnProtectionTicks = 0;
            _protectedUntilExit = false;
            return;
        }
        if (_protectedUntilExit && !inOwnBase)
        {
            _protectedUntilExit = false;
            SpawnProtectionTicks = Math.Min(SpawnProtectionTicks, Tank.SpawnProtectionDuration);
        }
        SpawnProtectionTicks--;
    }

    public void Die(ProjectileList? projectiles, ITerrainPixelAccessor? terrain, Func<Position, bool>? isOccupied = null,
        TimeSpan? delay = null, bool recall = false)
    {
        if (_respawning)
            return;

        var reactor = _tank.Reactor;
        reactor.Exhaust(new ReactorState(reactor.Heat, reactor.Health));
        _build.DiscardCarried();
        DropOre(terrain, isOccupied);
        _respawning = true;
        _respawnTimer = delay ?? Tweaks.Tank.RespawnDelay;

        if (!recall)
            projectiles?.AddExplosion(_tank.Position, Tweaks.Explosion.Death, Tweaks.Explosion.DeathHeatScale);
    }

    /// <summary>
    /// The hold spills back into the ground around the wreck (<see cref="OreDrop"/>), not under live tanks or planted
    /// machines (<paramref name="isOccupied"/>); what finds no room, or all of it when there is no terrain to drop
    /// into, is lost. Either way the hold is empty.
    /// </summary>
    private void DropOre(ITerrainPixelAccessor? terrain, Func<Position, bool>? isOccupied)
    {
        var hold = _tank.Hold;
        if (terrain != null && hold.Ore > 0)
            OreDrop.Scatter(terrain, _tank.Position, hold.Ore, isOccupied);
        hold.Ore = 0;
    }

    /// <summary>Drops cargo once, then respawns at an intact base when the timer runs out.</summary>
    public void AdvanceDeath(in SimulationContext ctx, bool recall)
    {
        if (!_respawning)
        {
            ctx.Events?.Record(new StepEvent(StepEventKind.TankDied, _tank.Position, _tank.Color, Cause: DeathCause, Killer: Killer));
            Die(ctx.Projectiles, ctx.Terrain, ctx.IsOccupied, RespawnDelay(in ctx), recall);
        }

        if (_tank.Base is not { IsDestroyed: false })
            return;

        _respawnTimer -= ctx.Settings.AdvanceStep;
        if (!recall && _respawnTimer > TimeSpan.Zero)
            return;

        _tank.Spawn(ctx.Heat.Ledger);
        ctx.Events?.Record(new StepEvent(StepEventKind.TankRespawned, _tank.Position, _tank.Color));
    }

    /// <summary>
    /// How long the tank that has just died waits to respawn, the one place that says: a flat
    /// <see cref="Tweaks.Arcade.RespawnSeconds"/> under <see cref="Match.MatchRules.QuickRespawn"/>; else, in an energy
    /// match, longer the emptier its base's store is (<see cref="Network.BasePowerSystem.RespawnDelay"/>); else
    /// <see cref="Tweaks.Tank.RespawnDelay"/>.
    /// </summary>
    private TimeSpan RespawnDelay(in SimulationContext ctx)
    {
        if (ctx.Rules?.QuickRespawn == true)
            return TimeSpan.FromSeconds(Tweaks.Arcade.RespawnSeconds);
        return _tank.Base is { } home && ctx.BasePower is { } power ? power.RespawnDelay(home) : Tweaks.Tank.RespawnDelay;
    }

    /// <summary>Clears the death state and raises the respawn shield.</summary>
    public void OnSpawned()
    {
        _respawning = false;
        DeathCause = DamageCause.None;
        Killer = -1;
        _claimant = -1;
        _claimTicks = 0;
        LastDamageCause = DamageCause.None;
        HitFlashTicks = 0;
        SpawnProtectionTicks = Tank.SpawnProtectionCap;
        _protectedUntilExit = true;
    }

    /// <summary>Mixes the respawn countdown and the spawn protection, for the sync digest.</summary>
    internal ulong MixLatent(ulong hash)
    {
        hash = Fnv1a.Mix(hash, _respawning);
        hash = Fnv1a.Mix(hash, _respawnTimer);
        hash = Fnv1a.Mix(hash, SpawnProtectionTicks);
        hash = Fnv1a.Mix(hash, _protectedUntilExit);
        // Mixed only while an enemy's claim on the kill holds, and while the tank lies killed by another.
        if (_claimTicks > 0)
            hash = Fnv1a.Mix(Fnv1a.Mix(hash, _claimant), _claimTicks);
        return Killer >= 0 ? Fnv1a.Mix(hash, Killer) : hash;
    }
}
