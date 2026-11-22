namespace Emberward.Core.Bots;

using Emberward.Core.Config;
using Emberward.Core.Types;

/// <summary>
/// Notices a bot going nowhere whatever it is after: it has stayed within <see cref="Tweaks.Bot.StuckRadiusCells"/> of
/// one spot (along either axis) for <see cref="Tweaks.Bot.StuckWindowSeconds"/>, though it was trying to get somewhere.
/// Then it holds the detour the bot takes instead: a place <see cref="Tweaks.Bot.DetourCells"/> away that the bot heads
/// for, for up to <see cref="Tweaks.Bot.DetourSeconds"/>, before it takes up its own goal again. One anchor and a count:
/// a few comparisons a tick. Fighting in sight, waiting outside a base and recharging at home are standing still on
/// purpose; <see cref="BotTankAI"/> does not watch those (<see cref="Reset"/>).
/// </summary>
public sealed class BotStuckWatch
{
    private const int WindowFrames = Tweaks.Bot.StuckWindowSeconds * Tweaks.Perf.TargetFps;
    private const int DetourFrames = Tweaks.Bot.DetourSeconds * Tweaks.Perf.TargetFps;
    /// <summary>A detour counts as reached within this many cells (along either axis).</summary>
    private const int DetourReach = 6;
    /// <summary>Room a detour's end keeps from the map's edge.</summary>
    private const int EdgeMargin = 8;

    private Position _anchor;
    private bool _hasAnchor;
    private int _framesNearAnchor;
    private Position _detour;
    private int _detourFramesLeft;

    /// <summary>Ticks the bot has stayed near one spot, for tests and diagnostics.</summary>
    public int FramesNearAnchor => _framesNearAnchor;

    /// <summary>Where the running detour heads, or null when there is none.</summary>
    public Position? Detour => _detourFramesLeft > 0 ? _detour : null;

    /// <summary>
    /// Notes that the bot, trying to get somewhere, is at <paramref name="at"/>. True on the tick it has been near one
    /// spot for the whole window; the count then starts again.
    /// </summary>
    public bool Watch(Position at)
    {
        if (!_hasAnchor || Math.Abs(at.X - _anchor.X) > Tweaks.Bot.StuckRadiusCells ||
            Math.Abs(at.Y - _anchor.Y) > Tweaks.Bot.StuckRadiusCells)
        {
            _anchor = at;
            _hasAnchor = true;
            _framesNearAnchor = 0;
            return false;
        }
        if (++_framesNearAnchor < WindowFrames)
            return false;
        Reset();
        return true;
    }

    /// <summary>Forgets the spot: the bot is standing still on purpose, or has just changed what it is doing.</summary>
    public void Reset()
    {
        _hasAnchor = false;
        _framesNearAnchor = 0;
    }

    /// <summary>
    /// Starts a detour from <paramref name="at"/> along <paramref name="heading"/>, its end kept inside a map of
    /// <paramref name="size"/>. No heading (every way walled off), or an end already within reach of <paramref name="at"/>
    /// (an edge in the way), starts none and returns false.
    /// </summary>
    public bool StartDetour(Position at, Offset heading, Size size)
    {
        if (heading.X == 0 && heading.Y == 0)
            return false;
        var end = at + new Offset(heading.X * Tweaks.Bot.DetourCells, heading.Y * Tweaks.Bot.DetourCells);
        var clamped = new Position(
            Math.Clamp(end.X, EdgeMargin, Math.Max(EdgeMargin, size.X - 1 - EdgeMargin)),
            Math.Clamp(end.Y, EdgeMargin, Math.Max(EdgeMargin, size.Y - 1 - EdgeMargin)));
        if (Math.Abs(at.X - clamped.X) <= DetourReach && Math.Abs(at.Y - clamped.Y) <= DetourReach)
            return false;
        _detour = clamped;
        _detourFramesLeft = DetourFrames;
        return true;
    }

    /// <summary>A tick on the detour from <paramref name="at"/>: it ends when reached or when its time is up.</summary>
    public void AdvanceDetour(Position at)
    {
        if (_detourFramesLeft <= 0)
            return;
        _detourFramesLeft--;
        if (Math.Abs(at.X - _detour.X) <= DetourReach && Math.Abs(at.Y - _detour.Y) <= DetourReach)
            _detourFramesLeft = 0;
    }

    public void EndDetour() => _detourFramesLeft = 0;

    /// <summary>Mixes the anchor, the count and the detour, for the sync digest.</summary>
    public ulong MixState(ulong hash)
    {
        hash = Fnv1a.Mix(Fnv1a.Mix(hash, _anchor.X), _anchor.Y);
        hash = Fnv1a.Mix(hash, _hasAnchor);
        hash = Fnv1a.Mix(hash, _framesNearAnchor);
        hash = Fnv1a.Mix(Fnv1a.Mix(hash, _detour.X), _detour.Y);
        return Fnv1a.Mix(hash, _detourFramesLeft);
    }
}
