namespace Emberward.Core.Bots;

using Emberward.Core.Collision;
using Emberward.Core.Config;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Input;
using Emberward.Core.Types;

/// <summary>
/// Prospecting (DESIGN.md §6.4): in a match with ore, the bot's outings in the opening are for mining, not for its
/// enemy. <see cref="BotProspector"/> says where the ore is; the bot drives there as it roams, digs into it the plain
/// way (torching only rock that stops it), and goes home to bank the load when the hold runs hot or the area has no
/// ore left. It fights instead while its enemy is near, in sight, at its own base or has hit it.
/// </summary>
public partial class BotTankAI
{
    private const int ProspectFrames = Tweaks.Bot.ProspectSeconds * Tweaks.Perf.TargetFps;
    private const int EngageHoldFrames = Tweaks.Bot.EngageHoldSeconds * Tweaks.Perf.TargetFps;
    private const int MineTorchFrames = Tweaks.Bot.MineTorchSeconds * Tweaks.Perf.TargetFps;
    private const int MineGiveUpFrames = Tweaks.Bot.MineGiveUpSeconds * Tweaks.Perf.TargetFps;
    /// <summary>How many steps ahead the dig looks for ground hot enough to cook the tank.</summary>
    private const int MineHeatLookAhead = 3;

    private readonly BotProspector _prospector;
    /// <summary>Ticks left in which the bot fights instead of prospecting (see <see cref="Tweaks.Bot.EngageHoldSeconds"/>).</summary>
    private int _engageFrames;
    /// <summary>Whether the bot prospected on its last tick.</summary>
    private bool _prospecting;
    /// <summary>Whether the bot dug at its ore on its last tick (standing still on purpose: the stuck watch leaves it be).</summary>
    private bool _mining;

    /// <summary>Whether the bot prospected on its last tick; for tests and diagnostics.</summary>
    public bool IsProspecting => _prospecting;

    /// <summary>Whether the bot dug at ore on its last tick; for tests and diagnostics.</summary>
    public bool IsMining => _mining;

    /// <summary>The ore cell the bot is after, or null; for tests and diagnostics.</summary>
    public Position? OreTarget => _prospector.OreCell;

    /// <summary>
    /// Whether the bot prospects this tick: it roams, the match has ore, the opening (<see cref="Tweaks.Bot.ProspectSeconds"/>)
    /// is not over, and nothing has engaged it for <see cref="Tweaks.Bot.EngageHoldSeconds"/>: its enemy within
    /// <see cref="Tweaks.Bot.EngageRangeCells"/>, in sight nearer than <see cref="Tweaks.Bot.EngageSightCells"/>, or at the bot's own base, or a hit (<see cref="TrackHits"/>).
    /// </summary>
    private bool UpdateProspecting(Tank tank, Tank? enemy, World world)
    {
        if (_engageFrames > 0)
            _engageFrames--;
        bool may = _mode == TwitchMode.Twitch && world.OreDeposits.Count > 0 && _gunner.Frame < ProspectFrames;
        if (may && IsEngagedBy(tank, enemy))
            _engageFrames = EngageHoldFrames;
        _prospecting = may && _engageFrames == 0;
        if (!_prospecting)
        {
            _prospector.ClearGoal();
            _mining = false;
        }
        return _prospecting;
    }

    /// <summary>Whether <paramref name="enemy"/>, out of its base, calls for a fight: within the engage range, in sight within <see cref="Tweaks.Bot.EngageSightCells"/>, or at the bot's own base.</summary>
    private bool IsEngagedBy(Tank tank, Tank? enemy)
    {
        // An enemy docked in its own base is no threat out here.
        if (enemy is not { IsDead: false, IsDockedAtOwnBase: false })
            return false;
        int cells = Math.Max(Math.Abs(enemy.Position.X - tank.Position.X), Math.Abs(enemy.Position.Y - tank.Position.Y));
        if (cells <= Tweaks.Bot.EngageRangeCells || (_gunner.EnemyInSight && cells <= Tweaks.Bot.EngageSightCells))
            return true;
        return tank.Base is { IsDestroyed: false } home && BotSiege.InSiegeRange(enemy.Position, home);
    }

    /// <summary>
    /// A prospecting tick: digs at the ore when it is within <see cref="Tweaks.Bot.MineReachCells"/>, heads home with
    /// a full hold or with ore aboard and none left to find, and otherwise returns the place to roam to (an ore cell
    /// or a place in its area), with no output. A running detour comes first: then it only roams.
    /// </summary>
    private (ControllerOutput? Output, Position Goal) Prospect(Tank tank, World world)
    {
        if (tank.Hold.Room < Tweaks.Ore.PlainCellUnits)
            return (GoHomeToBank(), default);

        Position? ore = null;
        if (_stuckWatch.Detour is null)
        {
            ore = _prospector.NextOre(tank, world);
            if (ore is null && tank.OreHeld > 0)
                return (GoHomeToBank(), default);
            if (ore is Position cell && Math.Abs(cell.X - tank.Position.X) <= Tweaks.Bot.MineReachCells &&
                Math.Abs(cell.Y - tank.Position.Y) <= Tweaks.Bot.MineReachCells)
                return (Mine(tank, world, cell), cell);
        }
        _mining = false;
        var goal = ore ?? _prospector.RoamPoint(tank, world);
        _prospector.SetGoal(goal);
        return (null, goal);
    }

    private ControllerOutput GoHomeToBank()
    {
        _mining = false;
        SetMode(TwitchMode.Return);
        return default;
    }

    /// <summary>
    /// Drives straight at the ore <paramref name="cell"/>, which digs it into the hold (plain ore yields to the dig,
    /// slower than dirt). Held up for <see cref="Tweaks.Bot.MineTorchSeconds"/> it torches what is in the way while its
    /// heat allows; for <see cref="Tweaks.Bot.MineGiveUpSeconds"/>, or facing a wall or scorching ground, it gives the
    /// deposit up for a while.
    /// </summary>
    private ControllerOutput Mine(Tank tank, World world, Position cell)
    {
        _timeToChange = 0;
        _navigator.ResetStuck();
        _navigator.ResetProgress();
        var move = new Offset(Math.Sign(cell.X - tank.Position.X), Math.Sign(cell.Y - tank.Position.Y));
        int stall = _prospector.TrackDig(tank);
        var met = world.ProbeTankStep(tank, move);
        if ((move.X == 0 && move.Y == 0) || stall >= MineGiveUpFrames || met == StepObstacle.Solid ||
            world.GroundHeatAt(tank.Position + move * MineHeatLookAhead) >= BotNavigator.ScorchingGround)
        {
            _prospector.GiveUpDeposit();
            _mining = false;
            return default;
        }
        _mining = true;
        _move = move;
        bool torch = stall >= MineTorchFrames && met == StepObstacle.Hard && MayTorchRock(tank);
        return new ControllerOutput { MoveSpeed = move, ShootPrimary = torch, AimDirection = BotGunner.AimTowardsOffset(move) };
    }

    /// <summary>Mixes what prospecting remembers into the sync digest (<see cref="MixState"/>).</summary>
    private ulong MixProspectState(ulong hash)
    {
        hash = _prospector.MixState(hash);
        hash = Fnv1a.Mix(hash, _engageFrames);
        hash = Fnv1a.Mix(hash, _prospecting);
        return Fnv1a.Mix(hash, _mining);
    }
}
