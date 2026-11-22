namespace Emberward.Desktop.Gui.Guidance;

using Emberward.Core;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Sensing;
using Emberward.Core.Types;

/// <summary>
/// Whether the local side may hear of a nest waking: only when some of the nest's ground is in what that side can see, or shows on its
/// radar. Presentation only. Vision and radar are output of the local client, so the simulation never asks, and the
/// event itself names no tank or side.
/// </summary>
public static class NestAlert
{
    /// <summary>A radar blip within this many cells of a nest's centre (beyond its half size) belongs to it.</summary>
    private const int BlipSlack = 3;

    public static bool Covers(World world, Tank? player, Position nest)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (player is null || player.IsDead)
            return false;
        int radius = NestRadius(world, nest);
        // Any of its ground, not only its centre: that lies inside the deposit, deeper than sight reaches.
        if (world.TrackVision(player).IsAnyVisibleWithin(nest, radius))
            return true;
        if (world.RadarOf(player) is not { } radar)
            return false;
        int reach = BlipSlack + Math.Max(2, radius / 2);
        for (int i = 0; i < radar.ContactCount; i++)
        {
            RadarContact contact = radar.ContactAt(i);
            if (contact.Echo == RadarEcho.Life && Math.Abs(contact.Position.X - nest.X) <= reach && Math.Abs(contact.Position.Y - nest.Y) <= reach)
                return true;
        }
        return false;
    }

    private static int NestRadius(World world, Position centre)
    {
        if (world.Emberites is { } swarm)
            foreach (var nest in swarm.Nests)
                if (nest.Centre == centre)
                    return nest.Radius;
        return 0;
    }
}
