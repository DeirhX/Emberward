namespace Emberward.Core.Entities.Powerups;

using Emberward.Core.Config;
using Emberward.Core.Entities.Bases;
using Emberward.Core.Entities.Emberites;
using Emberward.Core.Terrain;
using Emberward.Core.Types;

/// <summary>
/// Where a drop lands. A site is fair and can be reached: its disc is soil or open ground (no rock, base, ore,
/// machine or nest), it is away from every base, from the hot core and the pockets and from the site before it, and
/// it keeps off the site before that where fairness allows.
/// </summary>
/// <remarks>
/// <para>
/// Two fairness goals take turns, drop by drop. An "everyone's" drop is as nearly the same distance from every base
/// as the ground allows: its unfairness is the farthest base's distance less the nearest's. A "contested" drop lies
/// between two neighbours: one base, each in its turn, is the nearest, and its unfairness is how much nearer it is
/// than the next. Destroyed bases do not count.
/// </para>
/// <para>
/// The search is bounded and the same for every map size and number of bases: <see cref="Tweaks.Powerups.WideDraws"/>
/// random cells over the ground between the bases, then <see cref="Tweaks.Powerups.RefineDraws"/> near the best so
/// far, the later ones nearer still. A draw within tolerance is taken at once, so sites vary; otherwise the fairest is.
/// A draw beside the site before the last counts as less fair by half the drop spacing; one beside the last site,
/// whose drop may still lie there, ranks behind every draw that is not, and one too near a base behind those. With no
/// draw that can be used at all, a fixed lattice round the middle of the bases is tried (<see cref="FallbackSite"/>).
/// A drop is always scheduled.
/// Distances are whole cells (the integer root of an integer), so every machine agrees.
/// </para>
/// </remarks>
public sealed partial class PowerupDrops
{
    /// <summary>A grade no draw that can be used has.</summary>
    private const int Unusable = int.MaxValue;

    /// <summary>What a contested draw costs when the base whose turn it is is not the nearest: more than any that is.</summary>
    private const int OutOfTurn = 1 << 20;

    private const int PickupRadius = Tweaks.Powerups.PickupDiameter / 2;

    private Position ChooseSite(bool contested)
    {
        bool anyAlive = false;
        foreach (TankBase tankBase in _bases.Bases)
            anyAlive |= !tankBase.IsDestroyed;
        var scale = MeasureBases(anyAlive);
        int turn = contested ? TakeTurn(anyAlive, scale.Bases) : -1;
        int tolerance = Math.Max(2, scale.Spacing * Tweaks.Powerups.FairTolerancePercent / 100);
        int minBase = Math.Max(Tweaks.Powerups.MinBaseDistance, scale.Spacing * Tweaks.Powerups.MinBaseDistancePercent / 100);
        int minSpacing = Math.Max(Tweaks.Powerups.MinDropSpacing, scale.Spacing * Tweaks.Powerups.MinDropSpacingPercent / 100);

        Position best = default;
        int bestGrade = Unusable, bestCost = int.MaxValue;
        for (int draw = 0; draw < Tweaks.Powerups.WideDraws + Tweaks.Powerups.RefineDraws; draw++)
        {
            bool refine = draw >= Tweaks.Powerups.WideDraws;
            if (refine && bestGrade == Unusable)
                break;
            // The second half of the refining draws looks half as far.
            int refineReach = Math.Max(4, scale.Spacing / (draw < Tweaks.Powerups.WideDraws + (Tweaks.Powerups.RefineDraws / 2) ? 8 : 16));
            Position site = refine
                ? new Position(
                    Math.Clamp(best.X + _rng.NextInt(-refineReach, refineReach + 1), scale.MinX, scale.MaxX),
                    Math.Clamp(best.Y + _rng.NextInt(-refineReach, refineReach + 1), scale.MinY, scale.MaxY))
                : new Position(_rng.NextInt(scale.MinX, scale.MaxX + 1), _rng.NextInt(scale.MinY, scale.MaxY + 1));
            int grade = Grade(site, anyAlive, minBase, minSpacing, out int cost, contested, turn);
            // The ground is looked at only for a draw that would be the best so far.
            if (grade > bestGrade || (grade == bestGrade && cost >= bestCost) || !IsClearGround(site))
                continue;
            (best, bestGrade, bestCost) = (site, grade, cost);
            if (grade == 0 && cost <= tolerance)
                break;
        }
        return bestGrade != Unusable ? best : FallbackSite(scale);
    }

    /// <summary>
    /// How good a draw is by where it lies: 0 when it keeps its distance from every base and from the last site (whose
    /// drop may still lie there), more when it is beside that site (1) or too near a base (2), <see cref="Unusable"/>
    /// when it is in a base's lap. <paramref name="cost"/> is its unfairness in cells, and half the drop spacing more
    /// when it is beside the site before the last: sites move about, but not at any price in fairness.
    /// </summary>
    private int Grade(Position site, bool anyAlive, int minBase, int minSpacing, out int cost, bool contested, int turn)
    {
        cost = int.MaxValue;
        int nearest = int.MaxValue, farthest = 0, own = int.MaxValue, rival = int.MaxValue;
        foreach (TankBase tankBase in _bases.Bases)
        {
            if (anyAlive && tankBase.IsDestroyed)
                continue;
            int distance = Distance(site, tankBase.Position);
            nearest = Math.Min(nearest, distance);
            farthest = Math.Max(farthest, distance);
            if (tankBase.Color == turn)
                own = distance;
            else
                rival = Math.Min(rival, distance);
        }
        bool hasBases = nearest != int.MaxValue;
        if (hasBases && nearest < minBase / 2)
            return Unusable;

        if (!hasBases)
            cost = 0;
        else if (!contested)
            cost = farthest - nearest;
        else if (own == int.MaxValue || rival == int.MaxValue)
            cost = 0;
        else
            cost = own <= rival ? rival - own : own - rival + OutOfTurn;

        int grade = hasBases && nearest < minBase ? 2 : 0;
        if (_lastSite is Position last && Within(site, last, minSpacing))
            grade |= 1;
        else if (_siteBefore is Position before && Within(site, before, minSpacing))
            cost += minSpacing / 2;
        return grade;
    }

    /// <summary>
    /// Whether a drop can lie at <paramref name="site"/>: clear of the core, the pockets and the nests, its disc all
    /// soil or open ground with no machine on it, and the ground neither hot nor frozen.
    /// </summary>
    private bool IsClearGround(Position site)
    {
        if (_core is Position core && Within(site, core, Tweaks.World.HotRockCoreRadius + Tweaks.Powerups.CoreClearance))
            return false;
        foreach (Position pocket in _hotPockets)
            if (Within(site, pocket, Tweaks.World.HotRockMaxRadius + Tweaks.Powerups.PocketClearance))
                return false;
        foreach (Position pocket in _frostPockets)
            if (Within(site, pocket, Tweaks.World.FrostRockMaxRadius + Tweaks.Powerups.PocketClearance))
                return false;
        if (!IsDiscGround(site))
            return false;
        if (_swarm != null)
            foreach (Nest nest in _swarm.Nests)
                if (!nest.IsCleared && Within(site, nest.Centre, nest.Radius + Tweaks.Powerups.NestClearance))
                    return false;
        _machines.CollectNear(site, PickupRadius + Tweaks.Machine.BoundingBoxHalfSize, _nearMachines, inListOrder: false);
        foreach (var machine in _nearMachines)
            if (machine.IsStanding)
                return false;
        float heat = _terrain.SampleAverageHeat(site, PickupRadius) * 255f;
        return heat <= Tweaks.Powerups.MaxSiteHeat && heat >= Tweaks.Powerups.MinSiteHeat;
    }

    /// <summary>Whether every cell of a drop's disc at <paramref name="site"/> is on the map and is soil or open ground.</summary>
    private bool IsDiscGround(Position site)
    {
        const int limit = Tweaks.Powerups.PickupDiameter * Tweaks.Powerups.PickupDiameter;
        for (int dy = -PickupRadius; dy <= PickupRadius; dy++)
            for (int dx = -PickupRadius; dx <= PickupRadius; dx++)
            {
                if (4 * (dx * dx + dy * dy) > limit)
                    continue;
                // Off the map reads as rock.
                TerrainPixel pixel = _terrain.GetPixel(site + new Offset(dx, dy));
                if (!Pixel.IsSoil(pixel) && !Pixel.IsOpenGround(pixel))
                    return false;
            }
        return true;
    }

    /// <summary>
    /// The site when no draw could be used: the first of a fixed lattice, ring by ring round the middle of the bases,
    /// whose disc is soil or open ground; the middle itself when none is.
    /// </summary>
    private Position FallbackSite(SiteScale scale)
    {
        var middle = new Position((scale.MinX + scale.MaxX) / 2, (scale.MinY + scale.MaxY) / 2);
        const int step = Tweaks.Powerups.PickupDiameter + 1;
        for (int ring = 0; ring <= Tweaks.Powerups.FallbackRings; ring++)
            for (int dy = -ring; dy <= ring; dy++)
                for (int dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring)
                        continue;
                    Position site = middle + new Offset(dx * step, dy * step);
                    if (site.X >= scale.MinX && site.X <= scale.MaxX && site.Y >= scale.MinY && site.Y <= scale.MaxY &&
                        IsDiscGround(site))
                        return site;
                }
        return middle;
    }

    /// <summary>
    /// The colour of the base whose turn the next contested drop is. The bases take turns in rounds: a round is every
    /// base that counts, once, in an order shuffled afresh from the drops' own random stream, and it never opens with
    /// the base that closed the round before. So which base goes first, and which has had a turn more when the match
    /// ends partway through a round, follows from the seed and not from the slot (in slot order, slot 0's base was the
    /// nearest to three drops in ten and every other to under a quarter). A base that has fallen since its round was
    /// shuffled is passed over while any stands.
    /// </summary>
    private int TakeTurn(bool anyAlive, int bases)
    {
        if (bases == 0)
            return -1;
        // Twice at most: what is left of a round may all have fallen, and a new round holds only bases that count.
        for (int round = 0; round < 2; round++)
        {
            while (_turnNext < _turnOrder.Count)
            {
                int color = _turnOrder[_turnNext++];
                if (!TakesTurns(color, anyAlive))
                    continue;
                _lastTurn = color;
                return color;
            }
            ShuffleTurns(anyAlive);
        }
        return -1;
    }

    /// <summary>A new round of turns: the bases that count, shuffled, and not the base of the last turn first.</summary>
    private void ShuffleTurns(bool anyAlive)
    {
        _turnOrder.Clear();
        foreach (TankBase tankBase in _bases.Bases)
            if (!anyAlive || !tankBase.IsDestroyed)
                _turnOrder.Add(tankBase.Color);
        for (int i = _turnOrder.Count - 1; i > 0; i--)
        {
            int j = _rng.NextInt(i + 1);
            (_turnOrder[i], _turnOrder[j]) = (_turnOrder[j], _turnOrder[i]);
        }
        if (_turnOrder.Count > 1 && _turnOrder[0] == _lastTurn)
        {
            int other = 1 + _rng.NextInt(_turnOrder.Count - 1);
            (_turnOrder[0], _turnOrder[other]) = (_turnOrder[other], _turnOrder[0]);
        }
        _turnNext = 0;
    }

    /// <summary>Whether the base of <paramref name="color"/> still takes turns: it stands, or none does.</summary>
    private bool TakesTurns(int color, bool anyAlive)
    {
        foreach (TankBase tankBase in _bases.Bases)
            if (tankBase.Color == color)
                return !anyAlive || !tankBase.IsDestroyed;
        return false;
    }

    /// <summary>
    /// What the search scales by: how many bases count, the spacing of the two nearest of them (half the map's shorter
    /// side with fewer than two) and the ground to draw from, which is the bases' bounding box widened by half the
    /// spacing and kept on the map.
    /// </summary>
    private SiteScale MeasureBases(bool anyAlive)
    {
        var bases = _bases.Bases;
        int count = 0, nearestSq = int.MaxValue;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int i = 0; i < bases.Count; i++)
        {
            if (anyAlive && bases[i].IsDestroyed)
                continue;
            count++;
            Position at = bases[i].Position;
            (minX, minY, maxX, maxY) = (Math.Min(minX, at.X), Math.Min(minY, at.Y), Math.Max(maxX, at.X), Math.Max(maxY, at.Y));
            for (int j = i + 1; j < bases.Count; j++)
                if (!anyAlive || !bases[j].IsDestroyed)
                    nearestSq = Math.Min(nearestSq, Position.DistanceSquared(at, bases[j].Position));
        }
        Size size = _terrain.Size;
        int spacing = nearestSq != int.MaxValue ? Root(nearestSq) : Math.Min(size.X, size.Y) / 2;
        int edge = PickupRadius + 2;
        if (count == 0)
            return new SiteScale(0, spacing, edge, edge, Math.Max(edge, size.X - 1 - edge), Math.Max(edge, size.Y - 1 - edge));
        int widen = spacing / 2;
        int left = Math.Clamp(minX - widen, edge, Math.Max(edge, size.X - 1 - edge));
        int top = Math.Clamp(minY - widen, edge, Math.Max(edge, size.Y - 1 - edge));
        int right = Math.Clamp(maxX + widen, left, Math.Max(left, size.X - 1 - edge));
        int bottom = Math.Clamp(maxY + widen, top, Math.Max(top, size.Y - 1 - edge));
        return new SiteScale(count, spacing, left, top, right, bottom);
    }

    private static bool Within(Position a, Position b, int cells) => Position.DistanceSquared(a, b) < cells * cells;

    /// <summary>Whole cells from <paramref name="a"/> to <paramref name="b"/>, rounded down.</summary>
    private static int Distance(Position a, Position b) => Root(Position.DistanceSquared(a, b));

    /// <summary>The integer square root: IEEE's square root is exact to the last bit on every machine, and its floor is the root.</summary>
    private static int Root(int square) => (int)Math.Sqrt(square);

    /// <summary>How many bases count, their nearest spacing (cells), and the box sites are drawn from.</summary>
    private readonly record struct SiteScale(int Bases, int Spacing, int MinX, int MinY, int MaxX, int MaxY);
}
