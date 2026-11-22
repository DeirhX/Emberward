namespace Emberward.Core.Bots;

using Emberward.Core.Collision;
using Emberward.Core.Config;
using Emberward.Core.Entities.Bases;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Input;
using Emberward.Core.Types;

/// <summary>
/// What the bot does besides roaming and fighting: waiting outside a base it cannot break while its enemy is docked
/// there (<see cref="BotLoiter"/>), and dropping a goal it is getting nowhere with (<see cref="BotStuckWatch"/>).
/// </summary>
public partial class BotTankAI
{
    /// <summary>
    /// Watches for the bot going nowhere (<see cref="BotStuckWatch"/>) while it roams out of the enemy's sight or heads
    /// home. Laying siege, waiting outside a base (<paramref name="loiter"/>), fighting in sight and anything in its own
    /// base are not watched. Gone nowhere, it drops its goal (a return is given up, as when it stalls) and takes a
    /// detour along a heading that is not walled off.
    /// </summary>
    private void WatchForStuck(Tank tank, Tank? enemy, World world, bool canSiege, bool loiter)
    {
        bool besieging = canSiege && enemy?.Base is { } enemyBase && BotSiege.InSiegeRange(tank.Position, enemyBase);
        bool watched = _mode == TwitchMode.Return ||
            (_mode == TwitchMode.Twitch && !_gunner.EnemyInSight && !loiter && !besieging);
        if (!watched || IsInsideOwnBase(tank))
        {
            _stuckWatch.Reset();
            _stuckWatch.EndDetour();
            return;
        }
        _stuckWatch.AdvanceDetour(tank.Position);
        if (!_stuckWatch.Watch(tank.Position))
            return;
        if (!BotStuckWatch.TryPlanDetour(tank.Position, _navigator.ChooseBreakoutHeading(tank, target: null, world, MayTorchRock(tank)),
                world.Terrain.Size, out Position detourEnd))
            return;
        if (_mode == TwitchMode.Return)
        {
            SetMode(TwitchMode.Twitch);
            _returnCooldownFrames = ReturnCooldownFrames;
        }
        _maneuverFrames = 0;
        _timeToChange = 0;
        _navigator.ResetProgress();
        _stuckWatch.BeginDetour(detourEnd);
    }

    /// <summary>
    /// Waits for an enemy docked in <paramref name="target"/>, a base the bot cannot break: it drives to its post
    /// outside the gate nearest it and to one side of the gate's line of fire (<see cref="BotLoiter"/>), and stands
    /// there with its turret on the base. It does not fire, at the base or at the enemy inside; a manoeuvre round
    /// something in its way may torch it. Seeing the enemy, or hit, it stops waiting (<see cref="TrackHits"/>).
    /// </summary>
    private ControllerOutput DoLoiter(Tank tank, TankBase target, World world)
    {
        _stuckWatch.EndDetour();
        var post = BotLoiter.Post(tank.Position, target, world.Terrain);
        if (BotLoiter.IsAtPost(tank.Position, post))
        {
            _timeToChange = 0;
            _navigator.ResetStuck();
            _navigator.ResetProgress();
            return new ControllerOutput { AimDirection = BotGunner.AimTowardsOffset(target.Position - tank.Position) };
        }

        if (_timeToChange <= 0 || world.ProbeTankStep(tank, _move) is StepObstacle.Hard or StepObstacle.Solid)
        {
            _move = _navigator.ChooseBestMove(tank, post, world, _move, _profiles.Active);
            _timeToChange = LoiterReplanFrames;
        }
        _timeToChange--;
        if (IsStuck(tank, world, _move, GeneralStuckThresholdFrames, torchRock: false))
            return StartManeuver(tank, world, _navigator.ChooseUnstuckMove(tank, world, MayTorchRock(tank)),
                _rng.Next(UnstuckReplanMinFrames, UnstuckReplanMaxFrames));
        // The plain dig: no shell flies so near the base.
        return new ControllerOutput { MoveSpeed = _move, AimDirection = BotGunner.AimTowardsOffset(_move) };
    }

    /// <summary>Ticks a move toward the loiter post is held before it is chosen again.</summary>
    private const int LoiterReplanFrames = 6;

    /// <summary>Ticks after a hit in which the bot does not wait at a post: it fights or roams as when there is no post.</summary>
    private const int LoiterBreakFrames = 5 * Tweaks.Perf.TargetFps;

    /// <summary>Whether the bot chose to wait outside its enemy's base (<see cref="BotLoiter"/>) on its last tick.</summary>
    private bool _waiting;

    /// <summary>Whether the bot waited outside its enemy's base on its last tick; for tests and diagnostics.</summary>
    public bool IsWaiting => _waiting;

    /// <summary>The tank's health last tick, to tell a hit.</summary>
    private int _lastHealth;

    /// <summary>Ticks left in which the bot does not wait at a post (see <see cref="LoiterBreakFrames"/>).</summary>
    private int _loiterBreakFrames;

    /// <summary>Notes a hit (health lower than last tick, or enemy fire the shield took: the hit flash just lit), which keeps the bot from waiting at a post for a while.</summary>
    private void TrackHits(Tank tank)
    {
        int health = (int)tank.Reactor.Health;
        if (health < _lastHealth || tank.HitFlashTicks >= Tank.HitFlashDuration - 1)
            _loiterBreakFrames = LoiterBreakFrames;
        else if (_loiterBreakFrames > 0)
            _loiterBreakFrames--;
        _lastHealth = health;
    }
}
