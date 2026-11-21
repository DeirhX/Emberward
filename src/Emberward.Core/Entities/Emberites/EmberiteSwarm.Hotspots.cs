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
            GoHome(ref emberite);
            return;
        }
        emberite.State = EmberiteState.Seeking;
        emberite.Hotspot = spot;
        emberite.Timer = Tweaks.Emberites.HotspotSeekTicks;
        Seek(ref emberite);
    }

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
            GoHome(ref emberite);
            return;
        }
        SteerStraight(ref emberite, emberite.Hotspot, _nests[emberite.Nest]);
    }

    /// <summary>
    /// Holds still at the hot ground for <see cref="Tweaks.Emberites.HotspotRechargeTicks"/>, then takes a full charge
    /// out of the ground around it and hunts again. Ground that has cooled and gives less sends it home with what it
    /// got, so a creature never bites on part of a charge.
    /// </summary>
    private void Drink(ref Emberite emberite)
    {
        emberite.Velocity = default;
        if (--emberite.Timer > 0)
            return;
        double want = _fullCharge - emberite.Charge;
        if (want > 0.0 && _terrain.IsInside(emberite.Hotspot))
            emberite.Charge += _terrain.ExtractEnergyInRadiusArea(emberite.Hotspot, Tweaks.Emberites.HotspotDrinkRadius, want, TerrainGrid.ThermalField.Terrain);
        // A hair under counts: the grid hands heat back in single precision.
        if (emberite.Charge < _fullCharge * (1.0 - 1e-6))
        {
            GoHome(ref emberite);
            return;
        }
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
    /// Whether <paramref name="tank"/> stands on ground at <see cref="Tweaks.Emberites.HotZoneHeat"/> or hotter: creatures
    /// keep after it whatever its own heat. One read of the heat field per creature hunting.
    /// </summary>
    private bool StandsInHeat(Tank tank) =>
        _terrain.IsInside(tank.Position) && _terrain.GetHeatTemperature(tank.Position) >= Tweaks.Emberites.HotZoneHeat;
}
