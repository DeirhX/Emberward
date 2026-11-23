namespace Emberward.Core.Bots;

using Emberward.Core.Config;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Input;
using Emberward.Core.Rendering;
using Emberward.Core.Types;

/// <summary>
/// Powerup drops (DESIGN.md §6.8): a roaming bot goes for a drop that lies on the ground, or lands soon, within its
/// range. It asks the world what every player may know (<see cref="World.PowerupForecast"/>,
/// <see cref="World.CopyPowerupRenderStates"/>) each tick and plans no path: the drop's site takes the place of the
/// enemy as the point it roams toward, so it fights what it meets on the way as it always does.
/// </summary>
public partial class BotTankAI
{
    /// <summary>Ticks without getting nearer a drop after which the bot leaves that drop alone.</summary>
    private const int DropGiveUpFrames = Tweaks.Powerups.BotSeekGiveUpSeconds * Tweaks.Perf.TargetFps;

    /// <summary>The bot heads for a drop that has yet to land only this many ticks before it does.</summary>
    private const int DropLeadTicks = Tweaks.Powerups.BotSeekLeadSeconds * Tweaks.Perf.TargetFps;

    /// <summary>More drops than can lie on the ground at once (a drop lies for less than two periods).</summary>
    private const int DropsLookedAt = 4;

    /// <summary>The site of the drop the bot is going for this tick; null when it goes for none.</summary>
    private Position? _dropSite;

    /// <summary>Whether that drop lies on the ground (true) or has yet to land.</summary>
    private bool _dropLanded;

    /// <summary>The nearest to the drop's site (cells, along either axis) the bot has been on its way there.</summary>
    private int _dropNearestCells = int.MaxValue;

    /// <summary>Ticks since the bot last got nearer the site.</summary>
    private int _dropStallFrames;

    /// <summary>The site of a drop the bot gave up on; it goes for no drop there.</summary>
    private Position? _dropGivenUp;

    /// <summary>The site of the drop the bot is going for; for tests and diagnostics.</summary>
    public Position? DropSought => _dropSite;

    /// <summary>
    /// Chooses the drop to go for, if any: roaming, the nearest site within <see cref="Tweaks.Powerups.BotSeekRange"/>
    /// of a drop on the ground or one landing within <see cref="Tweaks.Powerups.BotSeekLeadSeconds"/>. On the way, a
    /// bot that gets no nearer for <see cref="Tweaks.Powerups.BotSeekGiveUpSeconds"/> (rock it will not torch round a
    /// site) gives that site up. Time spent fighting an enemy in sight does not count against it. A world without
    /// drops changes nothing here.
    /// </summary>
    private void ChooseDrop(Tank tank, World world)
    {
        Position? was = _dropSite;
        _dropSite = null;
        _dropLanded = false;
        var forecast = world.PowerupForecast;
        if (!forecast.Active)
            return;
        if (_mode == TwitchMode.Twitch)
        {
            const long range = Tweaks.Powerups.BotSeekRange;
            long nearest = range * range + 1;
            Span<PowerupRenderState> ground = stackalloc PowerupRenderState[DropsLookedAt];
            int count = world.CopyPowerupRenderStates(ground);
            for (int i = 0; i < count; i++)
                Consider(tank, ground[i].Cell, landed: true, ref nearest);
            if (forecast.TicksUntilLanding <= DropLeadTicks)
                Consider(tank, forecast.Site, landed: false, ref nearest);
        }

        if (_dropSite != was)
        {
            _dropNearestCells = int.MaxValue;
            _dropStallFrames = 0;
        }
        if (_dropSite is not Position site)
            return;
        // Heading for a drop it is not waiting at a post: that way's headway starts over afterwards.
        _loiterNearestCells = int.MaxValue;
        _loiterStallFrames = 0;
        int cells = CellsTo(tank, site);
        if (cells < _dropNearestCells - ReturnHeadwayCells)
        {
            _dropNearestCells = cells;
            _dropStallFrames = 0;
        }
        else if (!_gunner.EnemyInSight && !WaitsAtDrop(tank) && ++_dropStallFrames > DropGiveUpFrames)
        {
            _dropGivenUp = site;
            _dropSite = null;
            _dropNearestCells = int.MaxValue;
            _dropStallFrames = 0;
        }
    }

    private void Consider(Tank tank, Position site, bool landed, ref long nearest)
    {
        long distance = Position.DistanceSquared(tank.Position, site);
        if (distance >= nearest || site == _dropGivenUp)
            return;
        nearest = distance;
        _dropSite = site;
        _dropLanded = landed;
    }

    /// <summary>Whether the bot stands on the site of a drop that has yet to land: it waits there for it.</summary>
    private bool WaitsAtDrop(Tank tank) =>
        _dropSite is Position site && !_dropLanded && CellsTo(tank, site) <= Tweaks.Powerups.BotArriveCells;

    /// <summary>Stands on the site until the drop lands; an enemy in sight is fought instead (the caller sees to that).</summary>
    private ControllerOutput WaitAtDrop()
    {
        _timeToChange = 0;
        _navigator.ResetStuck();
        _navigator.ResetProgress();
        _stuckWatch.EndDetour();
        return new ControllerOutput { AimDirection = _aimDirection };
    }

    private static int CellsTo(Tank tank, Position site) =>
        Math.Max(Math.Abs(tank.Position.X - site.X), Math.Abs(tank.Position.Y - site.Y));

    /// <summary>
    /// Mixes what the bot remembers of drops into the sync digest (<see cref="MixState"/>). A bot that has never gone
    /// for one mixes nothing, so in a world without drops it hashes as it always did.
    /// </summary>
    private ulong MixDropState(ulong hash)
    {
        if (_dropSite == null && _dropGivenUp == null && _dropStallFrames == 0 && _dropNearestCells == int.MaxValue)
            return hash;
        hash = Fnv1a.Mix(hash, _dropSite.HasValue);
        hash = Fnv1a.Mix(Fnv1a.Mix(hash, _dropSite.GetValueOrDefault().X), _dropSite.GetValueOrDefault().Y);
        hash = Fnv1a.Mix(hash, _dropLanded);
        hash = Fnv1a.Mix(hash, _dropNearestCells);
        hash = Fnv1a.Mix(hash, _dropStallFrames);
        hash = Fnv1a.Mix(hash, _dropGivenUp.HasValue);
        return Fnv1a.Mix(Fnv1a.Mix(hash, _dropGivenUp.GetValueOrDefault().X), _dropGivenUp.GetValueOrDefault().Y);
    }
}
