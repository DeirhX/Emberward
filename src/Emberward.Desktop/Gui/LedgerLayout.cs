namespace Emberward.Desktop.Gui;

/// <summary>
/// Where the readout lines on the SHIELD/HULL and MATERIALS plates sit (art pixels, x relative to the panel's left
/// edge). Each panel has two plates, one above the other, and each plate holds one line: its label at the left end and
/// its number ending at a column, so the two numbers of a panel line up. SHIELD sits on the upper plate and HULL on the
/// lower, each with a hairline bar under it filled from the left (a match with no shield shows HULL alone, on the lower
/// plate); HOLD sits on the upper MATERIALS plate and BASE on the lower, with the store's rate at the right end. Kept
/// apart from the drawing so a test can check that nothing overlaps or leaves its plate.
/// </summary>
public static class LedgerLayout
{
    /// <summary>The share of a text size its capitals and digits stand (a little over most fonts', to be safe).</summary>
    public const float InkHeight = 0.75f;

    public const float LabelSize = 13f, NumberSize = 16f;

    // SHIELD/HULL.

    /// <summary>The SHIELD/HULL panel's upper plate's inside, top to bottom (measured on the art).</summary>
    public const float UpperPlateTop = 144f, UpperPlateBottom = 164f;

    /// <summary>Its lower plate's inside.</summary>
    public const float LowerPlateTop = 168.5f, LowerPlateBottom = 192.5f;

    /// <summary>The lines' left and right ends: where the labels start and the numbers end, and the bars' ends.</summary>
    public const float LedgerLeft = 15f, LedgerRight = 144f;

    /// <summary>The SHIELD line's text (its ink's middle), on the upper plate, and the HULL line's, on the lower.</summary>
    public const float ShieldLineY = 151.5f, HullLineY = 178.5f;

    /// <summary>How far below a line its bar runs, and a bar's half height and outline.</summary>
    public const float BarDrop = 9f, BarHalf = 0.9f, BarOutline = 0.6f;

    // MATERIALS.

    /// <summary>The MATERIALS panel's upper plate's inside, top to bottom (measured on the art).</summary>
    public const float MaterialsUpperTop = 145f, MaterialsUpperBottom = 167f;

    /// <summary>Its lower plate's inside. The plates carry no bars (the pile shows the hold).</summary>
    public const float MaterialsLowerTop = 174f, MaterialsLowerBottom = 194f;

    /// <summary>The HOLD line's text, on the upper plate, and the BASE line's, on the lower.</summary>
    public const float HoldLineY = 156f, BaseLineY = 184f;

    /// <summary>
    /// The MATERIALS lines' columns: where the labels start, where the numbers end, and where the store's rate ends, at
    /// the right end so the store's number stays put whether or not it grows.
    /// </summary>
    public const float MaterialsLeft = 35f, MaterialsNumberRight = 162f, MaterialsRight = 205f;

    /// <summary>The rate's text size, smaller than the numbers.</summary>
    public const float RateSize = 12f;

    /// <summary>How far a deposit's <c>+N</c> rises, from just above the BASE line, over the store's number.</summary>
    public const float DeltaRise = 12f;

    /// <summary>The top and bottom of a line's ink (the taller of its labels and numbers).</summary>
    public static (float Top, float Bottom) Text(float lineY) =>
        (lineY - (NumberSize * InkHeight * 0.5f), lineY + (NumberSize * InkHeight * 0.5f));

    /// <summary>The top and bottom of a line's bar, outline included.</summary>
    public static (float Top, float Bottom) Bar(float lineY) =>
        (lineY + BarDrop - BarHalf - BarOutline, lineY + BarDrop + BarHalf + BarOutline);
}
