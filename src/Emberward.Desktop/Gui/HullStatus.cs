namespace Emberward.Desktop.Gui;

using Emberward.Core.Entities.Tanks;

/// <summary>The one word on the SHIELD/HULL plate's title, worst first: what the tank's armour is doing now.</summary>
public enum HullStatus
{
    /// <summary>The match gives tanks no shield, and the hull is intact enough to say nothing more.</summary>
    Hull,
    Shielded,
    ShieldDown,
    /// <summary>The hull is burning.</summary>
    HullBreach,
    /// <summary>The hull is almost gone (under <see cref="HullStatuses.MeltingHullFraction"/>).</summary>
    Melting,
    /// <summary>The tank is dead.</summary>
    Slag,
}

/// <param name="Text">The word, as the plate writes it.</param>
/// <param name="Color">Its colour (ImGui ABGR).</param>
/// <param name="FlashColor">The colour it alternates with when it flashes; <paramref name="Color"/> when it does not.</param>
public readonly record struct HullStatusWord(HullStatus Status, string Text, uint Color, uint FlashColor)
{
    public bool Flashes => FlashColor != Color;

    /// <summary>The colour to draw at <paramref name="time"/> (seconds): flashing words alternate four times a second.</summary>
    public uint ColorAt(float time) => Flashes && (int)(time * 4f) % 2 != 0 ? FlashColor : Color;
}

/// <summary>
/// What the SHIELD/HULL plate's title says, shared by the desktop (ImGui) and the browser so that both say the same.
/// Output only: it words what the tank and its shield already report.
/// </summary>
public static class HullStatuses
{
    /// <summary>Below this share of its health the hull is melting.</summary>
    public const float MeltingHullFraction = 0.05f;

    /// <summary>
    /// While the shield is empty, a fire at or under this level still reads SHIELD DOWN (the empty shield is the news);
    /// above it, HULL BREACH.
    /// </summary>
    public const float BreachFireLevel = 0.2f;

    private static readonly uint Slate = HudPalette.Rgba(138, 168, 194);
    private static readonly uint Breach = HudPalette.Rgba(255, 106, 16);
    private static readonly uint SlagGrey = HudPalette.Rgba(140, 134, 128);
    private static readonly uint HullTitle = HudPalette.Rgba(144, 232, 248);

    /// <summary>The plate's word for the player's tank.</summary>
    public static HullStatusWord Of(Tank player, in TankShieldView shield) =>
        Of(player.IsDead, shield.Active, shield.Shield, player.FireLevel, player.HullFraction);

    /// <summary>
    /// The plate's word, worst first: SLAG when dead, MELTING under <see cref="MeltingHullFraction"/> of the hull,
    /// HULL BREACH while burning, SHIELD DOWN with an empty shield, else SHIELDED (HULL with no shield in the match).
    /// An empty shield on a hull that has only just caught (fire up to <see cref="BreachFireLevel"/>) reads SHIELD
    /// DOWN, so the empty shield is named before the breach takes over.
    /// </summary>
    public static HullStatusWord Of(bool dead, bool shieldActive, int shield, float fireLevel, float hullFraction)
    {
        var status = dead ? HullStatus.Slag
            : hullFraction < MeltingHullFraction ? HullStatus.Melting
            : fireLevel > BreachFireLevel || (fireLevel > 0f && (!shieldActive || shield > 0)) ? HullStatus.HullBreach
            : !shieldActive ? HullStatus.Hull
            : shield <= 0 ? HullStatus.ShieldDown
            : HullStatus.Shielded;
        return status switch
        {
            HullStatus.Slag => new(status, "SLAG", SlagGrey, SlagGrey),
            HullStatus.Melting => new(status, "MELTING", HudPalette.HullEmber(0f), HudPalette.HullEmber(MeltingHullFraction)),
            HullStatus.HullBreach => new(status, "HULL BREACH", Breach, Breach),
            HullStatus.ShieldDown => new(status, "SHIELD DOWN", Slate, Slate),
            HullStatus.Shielded => new(status, "SHIELDED", HudPalette.Shield, HudPalette.Shield),
            _ => new(status, "HULL", HullTitle, HullTitle),
        };
    }
}
