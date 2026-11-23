namespace Emberward.Core.Bots;

using Emberward.Core.Config;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.LevelGen;
using Emberward.Core.Terrain;
using Emberward.Core.Types;

/// <summary>
/// Where a prospecting bot goes for ore (DESIGN.md §6.4): the ore cell it digs toward, in the nearest pocket or speck
/// within <see cref="Tweaks.Bot.ProspectRadiusCells"/> of its own base that still has plain ore, or, with none, a place
/// in that area to roam to. Lumps are passed over (their core cooks the tank).
/// </summary>
/// <remarks>
/// Its cost follows what it does. While its ore cell is still ore, a tick reads that one cell. Only when the cell has
/// been dug does it look round its deposit again (<see cref="TerrainGrid.TryFindPlainOre"/>, which reads nothing
/// where the tiles hold no ore), and only when the deposit is dry does it go through the deposit list, at most once
/// every <see cref="Tweaks.Bot.ProspectSearchSeconds"/>. Shares the bot's random stream (for roaming places).
/// </remarks>
internal sealed class BotProspector
{
    private const int SearchFrames = Tweaks.Bot.ProspectSearchSeconds * Tweaks.Perf.TargetFps;
    private const int SkipFrames = Tweaks.Bot.SkipDepositSeconds * Tweaks.Perf.TargetFps;
    private const int RoamFrames = Tweaks.Bot.RoamPointSeconds * Tweaks.Perf.TargetFps;
    /// <summary>A roaming place counts as reached within this many cells (along either axis).</summary>
    private const int RoamReach = 8;
    /// <summary>Room a roaming place keeps from the map's edge.</summary>
    private const int EdgeMargin = 10;

    private readonly BotRandom _rng;
    /// <summary>Index into <see cref="World.OreDeposits"/> of the deposit being mined; -1 for none.</summary>
    private int _deposit = -1;
    private Position _cell;
    private bool _hasCell;
    private int _searchCooldown;
    /// <summary>A deposit given up on (rock or a wall in the way), passed over for <see cref="SkipFrames"/>; -1 for none.</summary>
    private int _skipDeposit = -1;
    private int _skipFrames;
    private Position _roamPoint;
    private int _roamFramesLeft;
    private Position _goal;
    private bool _hasGoal;
    private int _stallFrames;
    private Position _stallPosition;
    private int _stallOre;

    public BotProspector(BotRandom rng) => _rng = rng;

    /// <summary>Where the bot last headed while prospecting (an ore cell or a roaming place); null when it is not.</summary>
    public Position? Goal => _hasGoal ? _goal : null;

    /// <summary>The ore cell the bot is after, or null; for tests and diagnostics.</summary>
    public Position? OreCell => _hasCell ? _cell : null;

    public void SetGoal(Position goal)
    {
        _goal = goal;
        _hasGoal = true;
    }

    /// <summary>The bot is not prospecting this tick.</summary>
    public void ClearGoal()
    {
        _hasGoal = false;
        _stallFrames = 0;
    }

    /// <summary>
    /// The plain ore cell to dig toward, or null when none is in the bot's area (or it looked lately, with no deposit
    /// in hand, and found none).
    /// Called each tick the bot prospects.
    /// </summary>
    public Position? NextOre(Tank tank, World world)
    {
        if (_skipFrames > 0 && --_skipFrames == 0)
            _skipDeposit = -1;
        if (_searchCooldown > 0)
            _searchCooldown--;

        var terrain = world.Terrain;
        if (_hasCell && IsPlainOre(terrain.GetPixelRaw(_cell)))
            return _cell;
        _hasCell = false;

        var deposits = world.OreDeposits;
        if (_deposit >= 0 && _deposit < deposits.Count &&
            terrain.TryFindPlainOre(deposits[_deposit].Center, Tweaks.Bot.DepositReachCells, tank.Position, out Position next))
            return Take(next);
        if (_deposit >= 0)
            _searchCooldown = 0; // A deposit just dug out: the next is looked for at once.
        _deposit = -1;
        if (_searchCooldown > 0)
            return null;
        _searchCooldown = SearchFrames;
        return Search(tank, world);
    }

    /// <summary>
    /// The nearest deposit to the tank, among the pockets and specks within reach of its base that still hold plain
    /// ore; its ore cell nearest the tank.
    /// </summary>
    private Position? Search(Tank tank, World world)
    {
        var home = tank.Base?.Position ?? tank.Position;
        var deposits = world.OreDeposits;
        int best = -1, bestDistance = int.MaxValue;
        Position bestCell = default;
        for (int i = 0; i < deposits.Count; i++)
        {
            OreDeposit deposit = deposits[i];
            if (deposit.Kind == OreDepositKind.Lump || i == _skipDeposit ||
                Math.Max(Math.Abs(deposit.Center.X - home.X), Math.Abs(deposit.Center.Y - home.Y)) > Tweaks.Bot.ProspectRadiusCells)
                continue;
            int distance = Position.DistanceSquared(deposit.Center, tank.Position);
            if (distance >= bestDistance ||
                !world.Terrain.TryFindPlainOre(deposit.Center, Tweaks.Bot.DepositReachCells, tank.Position, out Position cell))
                continue;
            best = i;
            bestDistance = distance;
            bestCell = cell;
        }
        if (best < 0)
            return null;
        _deposit = best;
        return Take(bestCell);
    }

    private Position Take(Position cell)
    {
        _cell = cell;
        _hasCell = true;
        _stallFrames = 0;
        return cell;
    }

    private static bool IsPlainOre(TerrainPixel pixel) => Pixel.IsOre(pixel) && !Pixel.IsOreCore(pixel);

    /// <summary>
    /// A tick of digging at the ore: ticks in a row the tank has neither moved nor gained ore (rock or a wall in the
    /// way), 0 while it is getting somewhere.
    /// </summary>
    public int TrackDig(Tank tank)
    {
        if (tank.Position != _stallPosition || tank.OreHeld > _stallOre)
            _stallFrames = 0;
        else
            _stallFrames++;
        _stallPosition = tank.Position;
        _stallOre = tank.OreHeld;
        return _stallFrames;
    }

    /// <summary>Leaves the deposit being mined alone for a while and looks for another at once.</summary>
    public void GiveUpDeposit()
    {
        _skipDeposit = _deposit;
        _skipFrames = _deposit >= 0 ? SkipFrames : 0;
        _deposit = -1;
        _hasCell = false;
        _stallFrames = 0;
        _searchCooldown = 0;
    }

    /// <summary>
    /// A place within <see cref="Tweaks.Bot.ProspectRadiusCells"/> of the bot's base to roam to while it knows of no
    /// ore: kept until it is reached or <see cref="Tweaks.Bot.RoamPointSeconds"/> are up, then a new one.
    /// </summary>
    public Position RoamPoint(Tank tank, World world)
    {
        if (_roamFramesLeft > 0)
        {
            _roamFramesLeft--;
            if (Math.Abs(tank.Position.X - _roamPoint.X) > RoamReach || Math.Abs(tank.Position.Y - _roamPoint.Y) > RoamReach)
                return _roamPoint;
        }
        var home = tank.Base?.Position ?? tank.Position;
        var size = world.Terrain.Size;
        int r = Tweaks.Bot.ProspectRadiusCells;
        int x = home.X + _rng.Next(-r, r + 1);
        int y = home.Y + _rng.Next(-r, r + 1);
        _roamPoint = new Position(
            Math.Clamp(x, EdgeMargin, Math.Max(EdgeMargin, size.X - 1 - EdgeMargin)),
            Math.Clamp(y, EdgeMargin, Math.Max(EdgeMargin, size.Y - 1 - EdgeMargin)));
        _roamFramesLeft = RoamFrames;
        return _roamPoint;
    }

    /// <summary>Mixes what the prospector remembers, for the sync digest.</summary>
    public ulong MixState(ulong hash)
    {
        hash = Fnv1a.Mix(hash, _deposit);
        hash = Fnv1a.Mix(Fnv1a.Mix(Fnv1a.Mix(hash, _hasCell), _cell.X), _cell.Y);
        hash = Fnv1a.Mix(hash, _searchCooldown);
        hash = Fnv1a.Mix(Fnv1a.Mix(hash, _skipDeposit), _skipFrames);
        hash = Fnv1a.Mix(Fnv1a.Mix(Fnv1a.Mix(hash, _roamPoint.X), _roamPoint.Y), _roamFramesLeft);
        hash = Fnv1a.Mix(Fnv1a.Mix(Fnv1a.Mix(hash, _hasGoal), _goal.X), _goal.Y);
        hash = Fnv1a.Mix(Fnv1a.Mix(Fnv1a.Mix(hash, _stallFrames), _stallPosition.X), _stallPosition.Y);
        return Fnv1a.Mix(hash, _stallOre);
    }
}
