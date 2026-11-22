namespace Emberward.Core.Bots;

using Emberward.Core.Config;
using Emberward.Core.Entities.Bases;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Terrain;
using Emberward.Core.Types;

/// <summary>
/// Where the bot waits while its enemy sits docked in a base it cannot break (<see cref="World.CanBreakBase"/>): a post
/// <see cref="Tweaks.Bot.LoiterCellsOutsideWall"/> cells out from the wall, in front of the gate nearest the bot, so
/// that the enemy meets it on the way out. It decides nothing by itself: <see cref="BotTankAI"/> asks it.
/// </summary>
public static class BotLoiter
{
    /// <summary>Cells from the base's centre to a post, along the gate's axis.</summary>
    public const int PostDistance = BaseLayout.Half + Tweaks.Bot.LoiterCellsOutsideWall;

    /// <summary>How far from its post (along either axis) the bot counts as standing at it.</summary>
    public const int PostSlack = 3;

    /// <summary>Room a post keeps from the map's edge, so the tank fits there.</summary>
    private const int EdgeMargin = TankSpriteShape.Radius + 2;

    /// <summary>
    /// The post in front of the gate of <paramref name="target"/> nearest <paramref name="from"/>, among those inside
    /// the map; the base's centre pulled in from the edge when no gate has room outside it (a map smaller than a
    /// base and its posts).
    /// </summary>
    public static Position Post(Position from, TankBase target, ITerrainPixelReadOnly terrain)
    {
        Position best = target.Position;
        long bestDistance = long.MaxValue;
        foreach (var gate in BaseLayout.GateDirections)
        {
            var post = target.Position + new Offset(gate.X * PostDistance, gate.Y * PostDistance);
            if (post.X < EdgeMargin || post.Y < EdgeMargin ||
                post.X >= terrain.Width - EdgeMargin || post.Y >= terrain.Height - EdgeMargin)
                continue;
            long distance = Position.DistanceSquared(from, post);
            if (distance < bestDistance)
            {
                best = post;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>Whether <paramref name="at"/> is within <see cref="PostSlack"/> of <paramref name="post"/>.</summary>
    public static bool IsAtPost(Position at, Position post) =>
        Math.Abs(at.X - post.X) <= PostSlack && Math.Abs(at.Y - post.Y) <= PostSlack;
}
