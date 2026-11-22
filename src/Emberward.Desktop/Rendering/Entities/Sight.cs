namespace Emberward.Desktop.Rendering.Entities;

using Emberward.Core.Sensing;
using Emberward.Core.Types;

/// <summary>
/// Whether the entity layer draws something at a cell. With fog of war only what stands in the player's sight is
/// drawn; the player's own tank and machines always do, because each of them sees the ground it stands on.
/// </summary>
public static class Sight
{
    /// <summary>True without fog (<paramref name="vision"/> null) or when the cell is in sight.</summary>
    public static bool Sees(VisionMap? vision, float x, float y) =>
        vision == null || vision.IsVisible(new Position((int)MathF.Floor(x), (int)MathF.Floor(y)));

    public static bool Sees(VisionMap? vision, Position cell) => vision == null || vision.IsVisible(cell);

    /// <summary>True without fog or when any of the ground within <paramref name="radius"/> of <paramref name="centre"/> is in sight (a nest).</summary>
    public static bool SeesAny(VisionMap? vision, Position centre, int radius) =>
        vision == null || vision.IsAnyVisibleWithin(centre, radius);
}
