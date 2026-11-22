namespace Emberward.Desktop.Gui;

/// <summary>
/// Where the readout lines on the SHIELD/HULL and MATERIALS plates sit (art pixels, x relative to the panel's left
/// edge). Each plate holds one line, its values meeting in the middle at a split with a label at each outer end:
/// <c>SHIELD 400 | 1000 HULL</c> and <c>HOLD 23 | 1,234 +18/m BASE</c>. The shield and hull each have a hairline bar
/// under their half of the line, filled from the plate's outer edge. A match with no shield shows the hull alone: the
/// label left, the number centred and the bar across the plate. Kept apart from the drawing so a test can check that
/// nothing overlaps or leaves its plate.
/// </summary>
public static class LedgerLayout
{
    /// <summary>The share of a text size its capitals and digits stand (a little over most fonts', to be safe).</summary>
    public const float InkHeight = 0.75f;

    public const float LabelSize = 12f, NumberSize = 15f;

    /// <summary>The gap between a value and the split between the two halves, and the split's hairline half height.</summary>
    public const float SplitGap = 3.5f, SplitHalf = 4.5f;

    // SHIELD/HULL.

    /// <summary>The SHIELD/HULL readout plate's inside, top to bottom.</summary>
    public const float PlateTop = 168.5f, PlateBottom = 192.5f;

    /// <summary>The line's left and right ends (where the labels start and end) and the split between the halves.</summary>
    public const float LedgerLeft = 15f, LedgerRight = 144f, LedgerSplit = 79.5f;

    /// <summary>The line's text (its ink's middle).</summary>
    public const float LineY = 178.5f;

    /// <summary>How far below the line its bars run, and a bar's half height and outline.</summary>
    public const float BarDrop = 9.5f, BarHalf = 0.9f, BarOutline = 0.6f;

    /// <summary>The gap at the split between the shield's bar and the hull's.</summary>
    public const float BarGap = 1.5f;

    // MATERIALS.

    /// <summary>The MATERIALS readout plate's inside, 20 high where SHIELD/HULL's is 24. It carries no bars (the pile
    /// shows the hold).</summary>
    public const float MaterialsPlateTop = 174f, MaterialsPlateBottom = 194f;

    /// <summary>The MATERIALS line's text.</summary>
    public const float MaterialsLineY = 184f;

    /// <summary>
    /// The MATERIALS line's columns: the labels' outer ends, and the split, left of the plate's middle because the store
    /// and its rate need more room than the hold. The rate ends at <see cref="MaterialsRateRight"/>, before BASE, so it
    /// stays put as the store's digits change.
    /// </summary>
    public const float MaterialsLeft = 35f, MaterialsSplit = 87f, MaterialsRateRight = 175f, MaterialsRight = 205f;

    /// <summary>The rate's text size, the labels', smaller than the numbers.</summary>
    public const float RateSize = 12f;

    /// <summary>How far a deposit's <c>+N</c> rises, from just above the line, over the store's number.</summary>
    public const float DeltaRise = 14f;

    /// <summary>The top and bottom of a line's ink (the taller of its labels and numbers).</summary>
    public static (float Top, float Bottom) Text(float lineY) =>
        (lineY - (NumberSize * InkHeight * 0.5f), lineY + (NumberSize * InkHeight * 0.5f));

    /// <summary>The top and bottom of the SHIELD/HULL line's bars, outline included.</summary>
    public static (float Top, float Bottom) Bar(float lineY) =>
        (lineY + BarDrop - BarHalf - BarOutline, lineY + BarDrop + BarHalf + BarOutline);
}
