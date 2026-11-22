namespace Emberward.Core.Entities.Emberites;

using Emberward.Core.Config;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Terrain;
using Emberward.Core.Types;

/// <summary>
/// What a creature does after a bite: it runs to the nearest hot ground, drinks a new charge out of it for a second and
/// charges the tank again (<see cref="EmberiteState.Seeking"/>, <see cref="EmberiteState.Recharging"/>). Heat is
/// conserved: the charge is taken out of the ground with <see cref="TerrainGrid.ExtractEnergyInRadiusArea"/> and is the
/// swarm's (<see cref="TotalCharge"/>) until a bite moves it into a tank. Nothing here runs while no creature is out.
/// </summary>
public sealed partial class EmberiteSwarm
{
    /// <summary>
    /// A spent creature looks for hot ground near where it bit (<see cref="TryFindHotspot"/>) and runs to it, or goes
    /// home if there is none.
    /// </summary>
    private void SeekHeat(ref Emberite emberite)
    {
        if (!TryFindHotspot((Position)emberite.Position, _nests[emberite.Nest], out Position spot))
        {
            TurnHome(ref emberite);
            return;
        }
        StartSeeking(ref emberite, spot);
    }

    /// <summary>
    /// A creature that found no hot ground, could not reach it or drank too little turns for home: one more failure
    /// counted (<see cref="Emberite.FailedSeeks"/>; past <see cref="Tweaks.Emberites.MaxFailedSeeks"/> it looks for no
    /// more hot ground until it next fills up), and its next look on the way waits <see cref="Tweaks.Emberites.HomeScanTicks"/>,
    /// since the ground it stands by has just been looked at. So a trip that finds no charge ends at home.
    /// </summary>
    private void TurnHome(ref Emberite emberite)
    {
        emberite.FailedSeeks++;
        emberite.State = EmberiteState.Returning;
        emberite.Timer = Tweaks.Emberites.HomeScanTicks;
        GoHome(ref emberite);
    }

    /// <summary>Sets off for the hot cell <paramref name="spot"/>, to drink there.</summary>
    private void StartSeeking(ref Emberite emberite, Position spot)
    {
        emberite.State = EmberiteState.Seeking;
        emberite.Hotspot = spot;
        emberite.Timer = Tweaks.Emberites.HotspotSeekTicks;
        Seek(ref emberite);
    }

    /// <summary>Whether a creature carries a whole charge. A hair under counts: the grid hands heat back in single precision.</summary>
    private bool HasFullCharge(in Emberite emberite) => emberite.Charge >= _fullCharge * (1.0 - 1e-6);

    /// <summary>Runs to the hot ground; there it starts to drink. One that cannot reach it in time goes home.</summary>
    private void Seek(ref Emberite emberite)
    {
        float dx = emberite.Hotspot.X + 0.5f - emberite.Position.X, dy = emberite.Hotspot.Y + 0.5f - emberite.Position.Y;
        float arrive = Tweaks.Emberites.HotspotArriveCells;
        if (dx * dx + dy * dy <= arrive * arrive)
        {
            emberite.State = EmberiteState.Recharging;
            emberite.Timer = Tweaks.Emberites.HotspotRechargeTicks;
            emberite.Velocity = default;
            return;
        }
        if (--emberite.Timer <= 0)
        {
            TurnHome(ref emberite);
            return;
        }
        SteerStraight(ref emberite, emberite.Hotspot, _nests[emberite.Nest]);
    }

    /// <summary>
    /// Holds still at the hot ground for <see cref="Tweaks.Emberites.HotspotRechargeTicks"/>, then takes a full charge
    /// out of the ground around it and hunts again. Ground that has cooled and gives less sends it home with what it
    /// got, so a creature never bites on part of a charge; on its way it looks for other hot ground (<see cref="GoHome"/>).
    /// </summary>
    private void Drink(ref Emberite emberite)
    {
        emberite.Velocity = default;
        if (--emberite.Timer > 0)
            return;
        double want = _fullCharge - emberite.Charge;
        if (want > 0.0 && _terrain.IsInside(emberite.Hotspot))
            emberite.Charge += _terrain.ExtractEnergyInRadiusArea(emberite.Hotspot, Tweaks.Emberites.HotspotDrinkRadius, want, TerrainGrid.ThermalField.Terrain);
        if (!HasFullCharge(emberite))
        {
            TurnHome(ref emberite);
            return;
        }
        emberite.FailedSeeks = 0;
        emberite.State = EmberiteState.Hunting;
        emberite.SightTick = 0;
        emberite.Sees = false;
        Hunt(ref emberite);
    }

    /// <summary>
    /// The nearest cell to <paramref name="from"/> within <see cref="Tweaks.Emberites.HotspotSearchCells"/>, sampled every
    /// <see cref="Tweaks.Emberites.HotspotSearchStep"/> cells, whose ground is at <see cref="Tweaks.Emberites.HotspotHeat"/>
    /// or hotter and that a creature of <paramref name="nest"/> can stand in; the first in row order on a tie. Asked once
    /// per bite, so it looks at a fixed number of cells and never at the map.
    /// </summary>
    private bool TryFindHotspot(Position from, Nest nest, out Position spot)
    {
        spot = default;
        const int reach = Tweaks.Emberites.HotspotSearchCells, step = Tweaks.Emberites.HotspotSearchStep;
        const long limit = (long)reach * reach;
        long best = limit + 1;
        for (int dy = -reach; dy <= reach; dy += step)
            for (int dx = -reach; dx <= reach; dx += step)
            {
                long distance = (long)dx * dx + (long)dy * dy;
                if (distance >= best)
                    continue;
                var cell = new Position(from.X + dx, from.Y + dy);
                if (!_terrain.IsInside(cell) || _terrain.GetHeatTemperature(cell) < Tweaks.Emberites.HotspotHeat)
                    continue;
                if (Classify(_terrain.GetPixelRaw(cell), cell, nest.Centre, nest.Radius) == EmberiteGround.Blocked)
                    continue;
                spot = cell;
                best = distance;
            }
        return best <= limit;
    }

    /// <summary>
    /// Hot ground a creature short of a charge can drink at on its way home: the nearest cell within
    /// <see cref="Tweaks.Emberites.HomeScanCells"/> (sampled every <see cref="Tweaks.Emberites.HotspotSearchStep"/> cells)
    /// at <see cref="Tweaks.Emberites.HotspotHeat"/> or hotter that it can stand in, whose detour (there, then to the nest,
    /// against straight to the nest) is at most <see cref="Tweaks.Emberites.HomeDetourCells"/>, and that is not the ground
    /// it last drank from or failed to (within <see cref="Tweaks.Emberites.HotspotDrinkRadius"/> twice over of
    /// <see cref="Emberite.Hotspot"/>). Looks at a fixed number of cells, at most once in <see cref="Tweaks.Emberites.HomeScanTicks"/>.
    /// </summary>
    private bool TryFindHotspotOnWayHome(in Emberite emberite, Nest nest, out Position spot)
    {
        spot = default;
        const int reach = Tweaks.Emberites.HomeScanCells, step = Tweaks.Emberites.HotspotSearchStep;
        const int spent = 2 * Tweaks.Emberites.HotspotDrinkRadius;
        const long limit = (long)reach * reach;
        var from = (Position)emberite.Position;
        float straight = Distance(from, nest.Centre);
        long best = limit + 1;
        for (int dy = -reach; dy <= reach; dy += step)
            for (int dx = -reach; dx <= reach; dx += step)
            {
                long distance = (long)dx * dx + (long)dy * dy;
                if (distance >= best)
                    continue;
                var cell = new Position(from.X + dx, from.Y + dy);
                if (Math.Abs(cell.X - emberite.Hotspot.X) <= spent && Math.Abs(cell.Y - emberite.Hotspot.Y) <= spent)
                    continue;
                if (!_terrain.IsInside(cell) || _terrain.GetHeatTemperature(cell) < Tweaks.Emberites.HotspotHeat)
                    continue;
                if (MathF.Sqrt(distance) + Distance(cell, nest.Centre) > straight + Tweaks.Emberites.HomeDetourCells)
                    continue;
                if (Classify(_terrain.GetPixelRaw(cell), cell, nest.Centre, nest.Radius) == EmberiteGround.Blocked)
                    continue;
                spot = cell;
                best = distance;
            }
        return best <= limit;
    }

    private static float Distance(Position a, Position b) => MathF.Sqrt(Position.DistanceSquared(a, b));

    /// <summary>
    /// Whether <paramref name="tank"/> stands on ground at <see cref="Tweaks.Emberites.HotZoneHeat"/> or hotter, within
    /// <see cref="Tweaks.Emberites.HotZoneKeepCells"/> of the creature and not docked in its own base: the creature keeps
    /// after it whatever its own heat. One read of the heat field per creature hunting.
    /// </summary>
    private bool StandsInHeat(Tank tank, in Emberite emberite) =>
        !tank.IsDockedAtOwnBase &&
        Position.DistanceSquared(tank.Position, (Position)emberite.Position) <= (long)Tweaks.Emberites.HotZoneKeepCells * Tweaks.Emberites.HotZoneKeepCells &&
        _terrain.IsInside(tank.Position) && _terrain.GetHeatTemperature(tank.Position) >= Tweaks.Emberites.HotZoneHeat;
}
