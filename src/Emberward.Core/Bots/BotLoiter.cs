namespace Emberward.Core.Bots;

using Emberward.Core.Collision;
using Emberward.Core.Config;
using Emberward.Core.Entities.Bases;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Terrain;
using Emberward.Core.Types;

/// <summary>
/// Where the bot waits while its enemy sits docked in a base it cannot break (<see cref="World.CanBreakBase"/>): a post
/// <see cref="Tweaks.Bot.LoiterCellsOutsideWall"/> cells out from the wall, beside the gate nearest the bot, so that the
/// enemy meets it on the way out. It stands to one side of the gate, out of the straight line of fire through it, so
/// that the enemy inside has no free shot at it. It decides nothing by itself: <see cref="BotTankAI"/> asks it.
/// </summary>
public static class BotLoiter
{
    /// <summary>Cells from the base's centre to a post, along the gate's axis.</summary>
    public const int PostDistance = BaseLayout.Half + Tweaks.Bot.LoiterCellsOutsideWall;

    /// <summary>Cells from the gate's axis to a post: past the gate's edge by more than the tank's width.</summary>
    public const int PostSideStep = BaseLayout.GateHalf + TankSpriteShape.Radius + 3;

    /// <summary>How far from its post (along either axis) the bot counts as standing at it.</summary>
    public const int PostSlack = 3;

    /// <summary>Room a post keeps from the map's edge, so the tank fits there.</summary>
    private const int EdgeMargin = TankSpriteShape.Radius + 2;

    /// <summary>
    /// The post beside the gate of <paramref name="target"/> nearest <paramref name="from"/>, among those a tank can
    /// stand at (<see cref="CanStandAt"/>): each gate has one on either side of its axis. Null when none can be stood
    /// at (all off the map, in rock or in a wall): the bot then does not wait at this base.
    /// </summary>
    public static Position? Post(Position from, TankBase target, ITerrainPixelReadOnly terrain)
    {
        Position? best = null;
        long bestDistance = long.MaxValue;
        foreach (var gate in BaseLayout.GateDirections)
        {
            foreach (int side in Sides)
            {
                // The side step runs across the gate's axis.
                var post = target.Position + new Offset(gate.X * PostDistance + gate.Y * side * PostSideStep,
                    gate.Y * PostDistance + gate.X * side * PostSideStep);
                long distance = Position.DistanceSquared(from, post);
                if (distance < bestDistance && CanStandAt(post, terrain))
                {
                    best = post;
                    bestDistance = distance;
                }
            }
        }
        return best;
    }

    /// <summary>
    /// Whether a tank can stand at <paramref name="post"/>: inside the map with room to spare, its hull on open ground
    /// or dirt (the plain dig the bot drives with clears it), and not on any base's cells. Rock (the bot does not torch
    /// near the gate) and walls rule a post out, as the bot would push at them forever.
    /// </summary>
    public static bool CanStandAt(Position post, ITerrainPixelReadOnly terrain)
    {
        if (post.X < EdgeMargin || post.Y < EdgeMargin ||
            post.X >= terrain.Width - EdgeMargin || post.Y >= terrain.Height - EdgeMargin)
            return false;
        if (Pixel.IsAnyBaseCell(terrain.GetPixelRaw(post)))
            return false;
        return CollisionSolver.TestTankTerrain(terrain, post, TankSpriteShape.DirectionOf(new Offset(0, -1))) <= StepObstacle.Dirt;
    }

    private static readonly int[] Sides = [1, -1];

    /// <summary>Whether <paramref name="at"/> is within <see cref="PostSlack"/> of <paramref name="post"/>.</summary>
    public static bool IsAtPost(Position at, Position post) =>
        Math.Abs(at.X - post.X) <= PostSlack && Math.Abs(at.Y - post.Y) <= PostSlack;
}
