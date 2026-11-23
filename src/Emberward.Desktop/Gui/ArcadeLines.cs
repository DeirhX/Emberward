namespace Emberward.Desktop.Gui;

using System.Numerics;
using ImGuiNET;
using Emberward.Core.Entities.Powerups;
using Emberward.Desktop.Presentation;

/// <summary>
/// Draws a timed match's board, clock and drop lines (DESIGN.md §6.8). What they say is <see cref="Scoreboard"/> and
/// <see cref="DropLine"/>, shared with the browser; this only draws them: as rows of the base panel, as the plate at
/// the top of the view, and as the final standings of the match-over screen.
/// </summary>
public static class ArcadeLines
{
    /// <summary>The width of the clock plate at the top of the view.</summary>
    public const float PlateWidth = 292f;

    private const float RowHeight = 19f, HeaderHeight = 14f, ClockHeight = 48f, DropHeight = 20f, PlatePad = 10f;

    // A drop line's time and time left in one string, kept while the line's words stay the same.
    private static readonly JoinedText Joined = new();

    /// <summary>
    /// Inside the base panel, where a core race has its goal lines: the goal of a timed match and its board. When the
    /// view is too narrow for the plate at its top (<paramref name="withClock"/>), the clock and the drop lines too.
    /// </summary>
    public static void DrawInPanel(Scoreboard? board, IReadOnlyList<DropLine> drops, bool withClock, float time)
    {
        if (board == null && !(withClock && drops.Count > 0))
            return;
        var dl = ImGui.GetWindowDrawList();
        float w = ImGui.GetContentRegionAvail().X;
        if (board != null)
        {
            HudDraw.ColoredText(HudTheme.Teal, Scoreboards.Objective);
            if (withClock)
                ImGui.Dummy(new Vector2(w, DrawClock(dl, ImGui.GetCursorScreenPos(), w, board, time)));
            ImGui.Dummy(new Vector2(w, DrawRows(dl, ImGui.GetCursorScreenPos(), w, board)));
        }
        if (withClock && drops.Count > 0)
            ImGui.Dummy(new Vector2(w, DrawDrops(dl, ImGui.GetCursorScreenPos(), w, drops, time)));
        ImGui.Separator();
    }

    /// <summary>The height of the plate at the top of the view; 0 when it has nothing to show.</summary>
    public static float PlateHeight(Scoreboard? board, int dropCount) =>
        board == null && dropCount == 0 ? 0f
        : PlatePad * 2f + (board != null ? ClockHeight : 0f) + dropCount * DropHeight;

    /// <summary>The plate at the top of the view: the clock, and under it the drop lines.</summary>
    public static void DrawPlate(ImDrawListPtr dl, Vector2 min, Scoreboard? board, IReadOnlyList<DropLine> drops, float time)
    {
        float h = PlateHeight(board, drops.Count);
        if (h <= 0f)
            return;
        HudTheme.DrawPanel(dl, min, min + new Vector2(PlateWidth, h), board is { IsUrgent: true } ? board.ClockColor : null);
        var at = min + new Vector2(14f, PlatePad);
        float w = PlateWidth - 28f;
        if (board != null)
            at.Y += DrawClock(dl, at, w, board, time);
        DrawDrops(dl, at, w, drops, time);
    }

    /// <summary>
    /// The caption (<c>TIME</c>, <c>LAST MINUTE</c>, <c>SUDDEN DEATH</c>, <c>FINAL</c>) over the clock, centred in
    /// <paramref name="width"/>: amber in the last minute, red and beating in the last seconds and in sudden death.
    /// Returns its height.
    /// </summary>
    public static float DrawClock(ImDrawListPtr dl, Vector2 pos, float width, Scoreboard board, float time)
    {
        uint color = board.ClockColor;
        bool beat = board.Phase is ClockPhase.LastSeconds or ClockPhase.SuddenDeath;
        float pulse = beat ? 0.5f + 0.5f * MathF.Sin(time * MathF.Tau * 2f) : 1f;
        float cx = pos.X + width * 0.5f;
        HudDraw.DrawCenteredText(dl, 12.5f, cx, pos.Y, board.Caption,
            board.IsUrgent ? HudTheme.WithAlpha(color, 0.6f + 0.4f * pulse) : HudTheme.TextDim);
        HudDraw.DrawCenteredText(dl, 30f, cx, pos.Y + 13f, board.Clock, HudTheme.WithAlpha(color, 0.75f + 0.25f * pulse));
        return ClockHeight;
    }

    /// <summary>
    /// The board: a line per tank in rank order (place, colour, name, kills, deaths) under a small heading. The
    /// player's own line has a band in its colour; the line of the tank that leads alone has an amber mark and amber
    /// kills. Returns its height.
    /// </summary>
    public static float DrawRows(ImDrawListPtr dl, Vector2 pos, float width, Scoreboard board, float alpha = 1f)
    {
        float killsRight = pos.X + width - 66f, deathsRight = pos.X + width - 6f;
        uint dim = HudTheme.WithAlpha(HudTheme.TextDim, alpha);
        RightText(dl, 11.5f, killsRight, pos.Y, "KILLS", dim);
        RightText(dl, 11.5f, deathsRight, pos.Y, "DEATHS", dim);
        float y = pos.Y + HeaderHeight;
        foreach (ScoreRow row in board.Rows)
        {
            if (row.IsLocal)
            {
                dl.AddRectFilled(new Vector2(pos.X - 5f, y - 1f), new Vector2(pos.X + width + 5f, y + RowHeight - 2f),
                    HudTheme.WithAlpha(row.Color, 0.24f * alpha), 3f);
                dl.AddRectFilled(new Vector2(pos.X - 5f, y - 1f), new Vector2(pos.X - 2f, y + RowHeight - 2f),
                    HudTheme.WithAlpha(row.Color, alpha));
            }
            float mid = y + 8f;
            if (row.IsLeader)
                dl.AddTriangleFilled(new Vector2(pos.X + 1f, mid - 5f), new Vector2(pos.X + 1f, mid + 5f),
                    new Vector2(pos.X + 9f, mid), HudTheme.WithAlpha(HudTheme.Amber, alpha));
            HudTheme.TextAt(dl, 15f, new Vector2(pos.X + 14f, y), HudNumbers.Digits(row.Rank), dim);
            var swatch = new Vector2(pos.X + 31f, y + 2f);
            dl.AddRectFilled(swatch, swatch + new Vector2(12f, 12f), HudTheme.WithAlpha(row.Color, alpha), 2f);
            dl.AddRect(swatch, swatch + new Vector2(12f, 12f), HudTheme.WithAlpha(HudTheme.BronzeDark, alpha), 2f);
            HudTheme.TextAt(dl, 15f, new Vector2(pos.X + 51f, y), row.Name,
                HudTheme.WithAlpha(HudTheme.Text, (row.IsLocal ? 1f : 0.82f) * alpha));
            RightText(dl, 15.5f, killsRight, y, HudNumbers.Digits(row.Kills),
                HudTheme.WithAlpha(row.IsLeader ? HudTheme.Amber : HudTheme.Text, alpha));
            RightText(dl, 15f, deathsRight, y, HudNumbers.Digits(row.Deaths), dim);
            y += RowHeight;
        }
        return y - pos.Y + 2f;
    }

    /// <summary>The height <see cref="DrawRows"/> takes for <paramref name="rows"/> tanks.</summary>
    public static float RowsHeight(int rows) => HeaderHeight + rows * RowHeight + 2f;

    /// <summary>
    /// The drop lines: the kind's mark on a chip of its colour and its name, when it lands (or that it has, and how
    /// long it lies), and how far it is. A line about to land or run out blinks; one that is over is dim. Returns
    /// their height.
    /// </summary>
    public static float DrawDrops(ImDrawListPtr dl, Vector2 pos, float width, IReadOnlyList<DropLine> lines, float time)
    {
        float y = pos.Y;
        bool blinkOn = (int)(time * 4f) % 2 == 0;
        foreach (DropLine line in lines)
        {
            bool over = !line.IsPointedAt;
            float alpha = over ? 0.6f : 1f;
            DrawChip(dl, new Vector2(pos.X + 8f, y + 8f), line.Kind, alpha);
            HudTheme.TextAt(dl, 14.5f, new Vector2(pos.X + 21f, y), PowerupPresentations.Get(line.Kind).Tag,
                HudTheme.WithAlpha(line.Color, alpha));
            string when = line.Left.Length > 0 ? Joined.Join(line.When, " ", line.Left) : line.When;
            uint whenColor = line.Urgent ? (blinkOn ? HudTheme.Warn : HudTheme.Text) : HudTheme.WithAlpha(HudTheme.Text, alpha);
            HudTheme.TextAt(dl, 14.5f, new Vector2(pos.X + 118f, y), when, whenColor);
            RightText(dl, 14.5f, pos.X + width, y, line.Detail, HudTheme.WithAlpha(over ? HudTheme.TextDim : HudTheme.Text, alpha));
            y += DropHeight;
        }
        return y - pos.Y;
    }

    /// <summary>Whether a point of the view lies under one of the boxes, or within <paramref name="pad"/> of it.</summary>
    public static bool IsUnder(IReadOnlyList<(Vector2 Min, Vector2 Max)> covered, Vector2 point, float pad)
    {
        foreach (var (min, max) in covered)
            if (point.X > min.X - pad && point.X < max.X + pad && point.Y > min.Y - pad && point.Y < max.Y + pad)
                return true;
        return false;
    }

    /// <summary>
    /// Where a badge wanted at <paramref name="at"/> goes so that it is at least <paramref name="apart"/> from each of
    /// <paramref name="taken"/>: sideways along <paramref name="side"/>, away from the one in its way. One look at each.
    /// </summary>
    public static Vector2 Beside(IReadOnlyList<Vector2> taken, Vector2 at, Vector2 side, float apart)
    {
        foreach (Vector2 other in taken)
        {
            Vector2 between = at - other;
            if (between.LengthSquared() >= apart * apart)
                continue;
            float along = Vector2.Dot(between, side);
            at = other + side * (along < 0f ? -apart : apart);
        }
        return at;
    }

    /// <summary>
    /// Where a box of half-size <paramref name="half"/> wanted at <paramref name="centre"/> goes so that it overlaps
    /// none of <paramref name="taken"/>: straight down, under what is in its way. Each move puts it wholly below one
    /// box, so it passes each at most once; the passes are counted all the same, so that no rounding can hold a frame.
    /// </summary>
    public static Vector2 ClearOf(IReadOnlyList<(Vector2 Min, Vector2 Max)> taken, Vector2 centre, Vector2 half)
    {
        const float gap = 1f;
        for (int pass = 0; pass <= taken.Count; pass++)
        {
            bool moved = false;
            for (int i = 0; i < taken.Count; i++)
            {
                var (min, max) = taken[i];
                if (centre.X + half.X > min.X && centre.X - half.X < max.X
                    && centre.Y + half.Y > min.Y && centre.Y - half.Y < max.Y)
                {
                    centre.Y = max.Y + half.Y + gap;
                    moved = true;
                }
            }
            if (!moved)
                break;
        }
        return centre;
    }

    /// <summary>A kind's mark, dark on a chip of the kind's colour: the kind is never told by the colour alone.</summary>
    public static void DrawChip(ImDrawListPtr dl, Vector2 center, PowerupKind kind, float alpha = 1f, float half = 8f)
    {
        var p = PowerupPresentations.Get(kind);
        dl.AddRectFilled(center - new Vector2(half, half), center + new Vector2(half, half), HudTheme.WithAlpha(p.Color, alpha), 3f);
        string mark = p.MarkText;
        float size = half * 1.75f;
        var sz = HudTheme.Measure(size, mark);
        HudTheme.TextAt(dl, size, new Vector2(center.X - sz.X * 0.5f, center.Y - sz.Y * 0.5f), mark,
            HudTheme.WithAlpha(HudTheme.Rgba(14, 14, 16), alpha));
    }

    private static void RightText(ImDrawListPtr dl, float size, float right, float y, string text, uint color)
    {
        if (text.Length > 0)
            HudTheme.TextAt(dl, size, new Vector2(right - HudTheme.Measure(size, text).X, y), text, color);
    }
}
