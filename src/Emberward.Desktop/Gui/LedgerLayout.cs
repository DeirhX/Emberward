namespace Emberward.Desktop.Gui;

/// <summary>
/// Where the SHIELD/HULL plate's two ledger rows sit (art pixels; the plate's inside runs from <see cref="PlateTop"/> to
/// <see cref="PlateBottom"/>): each row a label and a number on one line and a hairline bar with a dark outline under
/// them. Kept apart from the drawing so a test can check that no number, bar or outline overlaps another or leaves
/// the plate.
/// </summary>
public static class LedgerLayout
{
    public const float PlateTop = 168.5f, PlateBottom = 192.5f;

    /// <summary>The rows' text lines (their ink's middle), SHIELD above HULL.</summary>
    public const float ShieldRowY = 173.8f, HullRowY = 185.3f;

    /// <summary>The hull row's text line when the match runs no shield: alone, in the middle of the plate.</summary>
    public const float SoloRowY = 180f;

    /// <summary>How far below a row's text line its bar runs, and the bar's half height and outline.</summary>
    public const float BarDrop = 5.9f, BarHalf = 0.6f, BarOutline = 0.6f;

    public const float LabelSize = 10f, NumberSize = 11f;

    /// <summary>The share of a text size its capitals and digits stand (a little over most fonts', to be safe).</summary>
    public const float InkHeight = 0.75f;

    /// <summary>The MATERIALS readout plate's inside (art pixels), 20 high where SHIELD/HULL's is 24.</summary>
    public const float MaterialsPlateTop = 174f, MaterialsPlateBottom = 194f;

    /// <summary>The MATERIALS rows' text lines, HOLD above BASE. They carry no bars (the pile shows the hold).</summary>
    public const float HoldRowY = 179.2f, BaseRowY = 189f;

    /// <summary>The MATERIALS rows' columns, relative to the panel's left edge: labels, the rate after BASE, numbers.</summary>
    public const float MaterialsLeft = 35f, MaterialsRateX = 72f, MaterialsRight = 205f;

    /// <summary>Where a deposit's rising <c>+N</c> is centred, between the labels and the numbers.</summary>
    public const float MaterialsDeltaX = 130f;

    /// <summary>The rate's text size, smaller than the numbers.</summary>
    public const float RateSize = 9f;

    /// <summary>The top and bottom of a row's number ink (the taller of its label and number).</summary>
    public static (float Top, float Bottom) Text(float rowY) =>
        (rowY - (NumberSize * InkHeight * 0.5f), rowY + (NumberSize * InkHeight * 0.5f));

    /// <summary>The top and bottom of a row's bar, outline included.</summary>
    public static (float Top, float Bottom) Bar(float rowY) =>
        (rowY + BarDrop - BarHalf - BarOutline, rowY + BarDrop + BarHalf + BarOutline);
}
