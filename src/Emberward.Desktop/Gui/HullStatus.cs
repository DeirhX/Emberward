namespace Emberward.Desktop.Gui;

using Emberward.Core.Entities.Tanks;

/// <summary>What the tank's hull is doing now, worst last: the SHIELD/HULL plate colours its HULL label by it.</summary>
public enum HullStatus
{
    Intact,
    /// <summary>The hull is burning.</summary>
    Burning,
    /// <summary>The hull is almost gone (under <see cref="HullStatuses.MeltingHullFraction"/>).</summary>
    Melting,
    /// <summary>The tank is dead.</summary>
    Slag,
}

/// <param name="Color">The HULL label's colour (ImGui ABGR).</param>
/// <param name="FlashColor">The colour it alternates with when it flashes; <paramref name="Color"/> when it does not.</param>
public readonly record struct HullLabel(HullStatus Status, uint Color, uint FlashColor)
{
    public bool Flashes => FlashColor != Color;

    /// <summary>Whether the label is lit to call attention (a glow in its colour): anything but an intact hull.</summary>
    public bool Alarms => Status != HullStatus.Intact;

    /// <summary>The colour to draw at <paramref name="time"/> (seconds): a flashing label alternates four times a second.</summary>
    public uint ColorAt(float time) => Flashes && (int)(time * 4f) % 2 != 0 ? FlashColor : Color;
}

/// <summary>
/// How the SHIELD/HULL plate's HULL label is coloured, shared by the desktop (ImGui) and the browser so that both
/// say the same: its warm neutral while the hull is whole, breach orange while it burns, flashing embers when it is
/// almost gone, slag grey once the tank is dead. Output only: it colours what the tank already reports.
/// </summary>
public static class HullStatuses
{
    /// <summary>Below this share of its health the hull is melting.</summary>
    public const float MeltingHullFraction = 0.05f;

    private static readonly uint Breach = HudPalette.Rgba(255, 106, 16);
    private static readonly uint SlagGrey = HudPalette.Rgba(140, 134, 128);

    /// <summary>The HULL label for the player's tank.</summary>
    public static HullLabel Of(Tank player) => Of(player.IsDead, player.FireLevel, player.HullFraction);

    /// <summary>
    /// The HULL label, worst first: slag when dead, melting under <see cref="MeltingHullFraction"/> of the hull,
    /// burning while on fire, else intact.
    /// </summary>
    public static HullLabel Of(bool dead, float fireLevel, float hullFraction)
    {
        var status = dead ? HullStatus.Slag
            : hullFraction < MeltingHullFraction ? HullStatus.Melting
            : fireLevel > 0f ? HullStatus.Burning
            : HullStatus.Intact;
        return status switch
        {
            HullStatus.Slag => new(status, SlagGrey, SlagGrey),
            HullStatus.Melting => new(status, HudPalette.HullEmber(0f), HudPalette.HullEmber(MeltingHullFraction)),
            HullStatus.Burning => new(status, Breach, Breach),
            _ => new(status, HudPalette.HullNeutral, HudPalette.HullNeutral),
        };
    }
}
