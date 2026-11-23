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
            (_mode == TwitchMode.Twitch && (!_gunner.EnemyInSight || _prospecting) && !loiter && !besieging && !_mining);
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
    /// Whether the bot can break <paramref name="target"/>, its enemy's base (<see cref="World.CanBreakBase(Tank, TankBase)"/>),
    /// held: once it has decided it can, it keeps to that while its fire counted
    /// <see cref="Tweaks.Bot.SiegeHoldFireFactor"/> times as strong still would, so a store whose charge hovers at the
    /// edge does not flip it between siege and waiting every tick. A new base (or none) is decided afresh.
    /// </summary>
    private bool HoldBreakDecision(Tank tank, TankBase? target, World world)
    {
        int color = target is { IsDestroyed: false } ? target.Color : -1;
        if (color != _breakDecisionBase)
        {
            _breakDecisionBase = color;
            _breakHeld = false;
        }
        _breakHeld = target is { IsDestroyed: false } &&
            world.CanBreakBase(tank, target, _breakHeld ? Tweaks.Bot.SiegeHoldFireFactor : 1.0);
        return _breakHeld;
    }

    /// <summary>
    /// The post to wait at (<see cref="BotLoiter"/>), or null when the bot does not wait. It waits while it roams, its
    /// enemy is docked in a base it cannot break (<paramref name="breakable"/> false), it does not see the enemy and it
    /// has not been hit lately, and while a post can be stood at. On the way to its post, a bot that gets no nearer for
    /// <see cref="Tweaks.Bot.LoiterApproachSeconds"/> (rock or a wall it may not torch near the gate) gives up waiting
    /// for <see cref="LoiterGiveUpFrames"/> and roams, where the stuck watch sees it.
    /// </summary>
    private Position? ChooseLoiterPost(Tank tank, Tank? enemy, World world, bool breakable)
    {
        bool wanted = _mode == TwitchMode.Twitch && !_prospecting && enemy is { IsDockedAtOwnBase: true, Base: { IsDestroyed: false } } &&
            !breakable && !_gunner.EnemyInSight && _loiterBreakFrames == 0;
        Position? post = wanted ? BotLoiter.Post(tank.Position, enemy!.Base!, world.Terrain) : null;
        if (post is not Position at || BotLoiter.IsAtPost(tank.Position, at))
        {
            _loiterNearestCells = int.MaxValue;
            _loiterStallFrames = 0;
            return post;
        }
        int cells = Math.Max(Math.Abs(tank.Position.X - at.X), Math.Abs(tank.Position.Y - at.Y));
        if (cells < _loiterNearestCells - ReturnHeadwayCells)
        {
            _loiterNearestCells = cells;
            _loiterStallFrames = 0;
        }
        else if (++_loiterStallFrames > LoiterApproachFrames)
        {
            _loiterNearestCells = int.MaxValue;
            _loiterStallFrames = 0;
            _loiterBreakFrames = LoiterGiveUpFrames;
            return null;
        }
        return post;
    }

    /// <summary>
    /// Waits for an enemy docked in <paramref name="target"/>, a base the bot cannot break: it drives to its
    /// <paramref name="post"/> outside the gate nearest it and to one side of the gate's line of fire
    /// (<see cref="BotLoiter"/>), and stands there with its turret on the base. It does not fire, at the base or at the
    /// enemy inside, not even to torch round something in its way. Seeing the enemy, or hit, it stops waiting
    /// (<see cref="TrackHits"/>).
    /// </summary>
    private ControllerOutput DoLoiter(Tank tank, TankBase target, Position post, World world)
    {
        _stuckWatch.EndDetour();
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
            return StartManeuver(tank, world, _navigator.ChooseUnstuckMove(tank, world, mayTorchRock: false),
                _rng.Next(UnstuckReplanMinFrames, UnstuckReplanMaxFrames), holdsFire: true);
        // The plain dig: no shell flies so near the base.
        return new ControllerOutput { MoveSpeed = _move, AimDirection = BotGunner.AimTowardsOffset(_move) };
    }

    /// <summary>Ticks a move toward the loiter post is held before it is chosen again.</summary>
    private const int LoiterReplanFrames = 6;

    /// <summary>Ticks after a hit in which the bot does not wait at a post: it fights or roams as when there is no post.</summary>
    private const int LoiterBreakFrames = 5 * Tweaks.Perf.TargetFps;

    /// <summary>Ticks without getting nearer its post after which the bot gives up waiting there for a while.</summary>
    private const int LoiterApproachFrames = Tweaks.Bot.LoiterApproachSeconds * Tweaks.Perf.TargetFps;

    /// <summary>How long a bot that gave up on reaching its post roams before it may wait at a post again.</summary>
    private const int LoiterGiveUpFrames = 20 * Tweaks.Perf.TargetFps;

    /// <summary>The colour of the base <see cref="_breakHeld"/> is about; -1 for none.</summary>
    private int _breakDecisionBase = -1;

    /// <summary>Whether the bot held, on its last tick, that it can break its enemy's base (<see cref="HoldBreakDecision"/>).</summary>
    private bool _breakHeld;

    /// <summary>The nearest to its post (cells, along either axis) the bot has been on its way there.</summary>
    private int _loiterNearestCells = int.MaxValue;

    /// <summary>Ticks since the bot last got nearer its post.</summary>
    private int _loiterStallFrames;

    /// <summary>Whether the running manoeuvre holds its fire: one on the way to a post never torches near the gate.</summary>
    private bool _maneuverHoldsFire;

    /// <summary>Whether a manoeuvre is running that may torch (one begun while roaming); for tests and diagnostics.</summary>
    public bool IsInFiringManeuver => _maneuverFrames > 0 && !_maneuverHoldsFire;

    /// <summary>Whether the bot holds that it can break its enemy's base; for tests and diagnostics.</summary>
    public bool HoldsBaseBreakable => _breakHeld;

    /// <summary>Mixes what the waiting and the siege decision remember into the sync digest (<see cref="MixState"/>).</summary>
    private ulong MixWaitingState(ulong hash)
    {
        hash = Fnv1a.Mix(hash, _breakDecisionBase);
        hash = Fnv1a.Mix(hash, _breakHeld);
        hash = Fnv1a.Mix(hash, _loiterNearestCells);
        hash = Fnv1a.Mix(hash, _loiterStallFrames);
        return Fnv1a.Mix(hash, _maneuverHoldsFire);
    }

    /// <summary>Whether the bot chose to wait outside its enemy's base (<see cref="BotLoiter"/>) on its last tick.</summary>
    private bool _waiting;

    /// <summary>Whether the bot waited outside its enemy's base on its last tick; for tests and diagnostics.</summary>
    public bool IsWaiting => _waiting;

    /// <summary>The tank's health last tick, to tell a hit.</summary>
    private int _lastHealth;

    /// <summary>Ticks left in which the bot does not wait at a post (see <see cref="LoiterBreakFrames"/>).</summary>
    private int _loiterBreakFrames;

    /// <summary>Notes a hit (health lower than last tick, or enemy fire the shield took: the hit flash just lit), which keeps the bot from waiting at a post for a while and from prospecting (<see cref="UpdateProspecting"/>).</summary>
    private void TrackHits(Tank tank)
    {
        int health = (int)tank.Reactor.Health;
        if (health < _lastHealth || tank.HitFlashTicks >= Tank.HitFlashDuration - 1)
        {
            _loiterBreakFrames = Math.Max(_loiterBreakFrames, LoiterBreakFrames); // never cuts a longer give-up short
            _engageFrames = Math.Max(_engageFrames, EngageHoldFrames);
        }
        else if (_loiterBreakFrames > 0)
            _loiterBreakFrames--;
        _lastHealth = health;
    }
}
