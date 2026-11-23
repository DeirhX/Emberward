namespace Emberward.Desktop.Presentation;

using System.Globalization;
using Emberward.Core.Entities.Powerups;
using Emberward.Desktop.Gui;

/// <summary>
/// How the clients show one kind of powerup (DESIGN.md §6.8): its names, its mark, its colour and what it does in a
/// line. The desktop and the browser share it, and so do the forecast line, the effect tags, the markers on the
/// ground, the toasts and the sounds' captions: one row here is all a kind's words and colour. What a kind does and
/// for how long lives in Core (<see cref="PowerupKinds"/>).
/// </summary>
/// <param name="Name">The kind's name in a sentence ("BOT 2 took Overcharge").</param>
/// <param name="Tag">Upper-case name on the HUD, at most <see cref="PowerupPresentations.MaxTagLength"/> characters.</param>
/// <param name="Mark">
/// One character that stands for the kind wherever its colour does (the marker on the ground, a tag, an arrow), so
/// that a kind is never told by colour alone. One the entity layer's glyphs can draw (<c>GlyphText</c>).
/// </param>
/// <param name="Color">Its colour (<see cref="HudPalette"/>, ABGR); none is a tank's colour.</param>
/// <param name="Effect">What taking it does, in a line.</param>
public sealed record PowerupPresentation(PowerupKind Kind, string Name, string Tag, char Mark, uint Color, string Effect)
{
    /// <summary>The mark and the tag, as the HUD writes a kind: <c>X OVERCHARGE</c>.</summary>
    public string MarkedTag { get; } = Mark + " " + Tag;

    /// <summary>The colour as CSS <c>#rrggbb</c>, for the browser's page.</summary>
    public string Hex => HudPalette.Hex(Color);

    /// <summary>Whether the kind runs for a time on the tank that took it (an Overhaul is over at once).</summary>
    public bool IsTimed => PowerupKinds.IsTimed(Kind);
}

public static class PowerupPresentations
{
    /// <summary>The longest <see cref="PowerupPresentation.Tag"/> the HUD's lines leave room for.</summary>
    public const int MaxTagLength = 10;

    public static readonly PowerupPresentation Overhaul = new(
        PowerupKind.Overhaul, "Overhaul", "OVERHAUL", '+', HudPalette.PowerupOverhaul,
        "Hull whole, shield full, tank cooled - at once.");

    public static readonly PowerupPresentation Overcharge = new(
        PowerupKind.Overcharge, "Overcharge", "OVERCHARGE", 'X', HudPalette.PowerupOvercharge,
        string.Create(CultureInfo.InvariantCulture,
            $"Shells deal double damage for {PowerupKinds.DurationSeconds(PowerupKind.Overcharge):0} s."));

    public static readonly PowerupPresentation Bulwark = new(
        PowerupKind.Bulwark, "Bulwark", "BULWARK", 'B', HudPalette.PowerupBulwark,
        string.Create(CultureInfo.InvariantCulture,
            $"The tank takes half damage for {PowerupKinds.DurationSeconds(PowerupKind.Bulwark):0} s."));

    public static readonly PowerupPresentation Drill = new(
        PowerupKind.Drill, "Drill", "DRILL", 'D', HudPalette.PowerupDrill,
        string.Create(CultureInfo.InvariantCulture,
            $"Digs soil at driving speed for {PowerupKinds.DurationSeconds(PowerupKind.Drill):0} s."));

    /// <summary>Every kind's presentation, indexed by <see cref="PowerupKind"/>.</summary>
    public static readonly IReadOnlyList<PowerupPresentation> All = BuildTable(Overhaul, Overcharge, Bulwark, Drill);

    public static PowerupPresentation Get(PowerupKind kind) =>
        (uint)kind < (uint)All.Count ? All[(int)kind] : throw new ArgumentOutOfRangeException(nameof(kind));

    private static PowerupPresentation[] BuildTable(params PowerupPresentation[] entries)
    {
        var table = new PowerupPresentation[PowerupKinds.Count];
        foreach (var entry in entries)
            table[(int)entry.Kind] = entry;
        if (Array.IndexOf(table, null) >= 0)
            throw new InvalidOperationException("Every PowerupKind needs a PowerupPresentation.");
        return table;
    }
}
