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
    public const float ShieldRowY = 173.8f, HullRowY = 185f;

    /// <summary>The hull row's text line when the match runs no shield: alone, in the middle of the plate.</summary>
    public const float SoloRowY = 180f;

    /// <summary>How far below a row's text line its bar runs, and the bar's half height and outline.</summary>
    public const float BarDrop = 5.4f, BarHalf = 0.6f, BarOutline = 0.6f;

    public const float LabelSize = 8.5f, NumberSize = 10f;

    /// <summary>The share of a text size its capitals and digits stand (a little over most fonts', to be safe).</summary>
    public const float InkHeight = 0.75f;

    /// <summary>The top and bottom of a row's number ink (the taller of its label and number).</summary>
    public static (float Top, float Bottom) Text(float rowY) =>
        (rowY - (NumberSize * InkHeight * 0.5f), rowY + (NumberSize * InkHeight * 0.5f));

    /// <summary>The top and bottom of a row's bar, outline included.</summary>
    public static (float Top, float Bottom) Bar(float rowY) =>
        (rowY + BarDrop - BarHalf - BarOutline, rowY + BarDrop + BarHalf + BarOutline);
}
