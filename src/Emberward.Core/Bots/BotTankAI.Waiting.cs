namespace Emberward.Core.Bots;

using Emberward.Core.Collision;
using Emberward.Core.Entities.Bases;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Input;

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
            return;
        }
        _stuckWatch.AdvanceDetour(tank.Position);
        if (!_stuckWatch.Watch(tank.Position))
            return;
        if (_mode == TwitchMode.Return)
        {
            SetMode(TwitchMode.Twitch);
            _returnCooldownFrames = ReturnCooldownFrames;
        }
        _maneuverFrames = 0;
        _timeToChange = 0;
        _navigator.ResetProgress();
        _stuckWatch.StartDetour(tank.Position, _navigator.ChooseBreakoutHeading(tank, target: null, world, MayTorchRock(tank)),
            world.Terrain.Size);
    }

    /// <summary>
    /// Waits for an enemy docked in <paramref name="target"/>, a base the bot cannot break: it drives to its post
    /// outside the gate nearest it (<see cref="BotLoiter"/>) and stands there with its turret on the base. It does not
    /// fire, at the base or at the enemy inside; a manoeuvre round something in its way may torch it.
    /// </summary>
    private ControllerOutput DoLoiter(Tank tank, TankBase target, World world)
    {
        _stuckWatch.EndDetour();
        var post = BotLoiter.Post(tank.Position, target, world.Terrain);
        if (BotLoiter.IsAtPost(tank.Position, post))
        {
            _timeToChange = 0;
            _navigator.ResetStuck();
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
}
