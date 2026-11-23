namespace Emberward.Core;

using Emberward.Core.Entities.Powerups;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Rendering;

public partial class World
{
    /// <summary>
    /// The next powerup drop (DESIGN.md §6.8): its site, its kind and the time until it lands, known a full period
    /// ahead. Every side may know it whatever it sees, so this is not behind the fog. Inactive (the default) when the
    /// rules run no drops. The HUD and the bots ask this alike.
    /// </summary>
    public PowerupForecast PowerupForecast => _powerups?.Forecast ?? default;

    /// <summary>How many powerup drops lie on the ground; 0 when the rules run no drops.</summary>
    public int PowerupDropCount => _powerups?.GroundCount ?? 0;

    /// <summary>
    /// Writes the powerup drops on the ground into <paramref name="destination"/>, oldest first, and returns how many
    /// (0 when the rules run no drops). Like the forecast, not behind the fog.
    /// </summary>
    public int CopyPowerupRenderStates(Span<PowerupRenderState> destination) =>
        _powerups?.CopyRenderStates(destination) ?? 0;

    /// <summary>
    /// <paramref name="tank"/>'s running timed powerups, with the ticks left of each (the full time of a kind is
    /// <see cref="PowerupKinds.DurationTicks"/>). Inactive (the default) when the rules run no drops. Output only.
    /// </summary>
    public TankPowerupView PowerupsOf(Tank tank) => _powerups?.ViewOf(tank) ?? default;

    /// <summary>The powerup drops, for the hashes; null when the match rules leave them out.</summary>
    internal PowerupDrops? PowerupDrops => _powerups;
}
