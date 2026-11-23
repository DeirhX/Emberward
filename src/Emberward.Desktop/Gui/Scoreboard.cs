namespace Emberward.Desktop.Gui;

using System.Globalization;
using System.Numerics;
using ImGuiNET;
using Emberward.Core;
using Emberward.Core.Rendering;
using Emberward.Core.Types;
using Emberward.Core.Entities.Tanks;

/// <summary>
/// The scoreboard shown while Tab is held: every tank in its colour, with its driver's name in a network match, how
/// many times it died and when (<see cref="TankDeathView.Rows"/>), over the match clock.
/// </summary>
public static class Scoreboard
{
    private const float MinWidth = 420f;
    private const float MinTimesWidth = 300f;
    private const float Pad = 22f;
    private const float TitleHeight = 34f;
    private const float RowGap = 10f;
    private const float NameSize = 17f;
    private const float TimeSize = 15f;
    private const float Swatch = 20f;
    private const float CountColumn = 36f;

    /// <param name="driverName">The driver of a tank by its slot in a network match; null outside one.</param>
    public static void Draw((float x, float y, float w, float h) vp, World world, Tank player, Func<int, string>? driverName)
    {
        if (vp.w <= 0)
            return;

        var rows = TankDeathView.Rows(world);
        var dl = ImGui.GetForegroundDrawList();

        // Measure first, so the columns fit the names and the plate fits the rows.
        var names = new string[rows.Count];
        float nameWidth = 0f;
        for (int i = 0; i < rows.Count; i++)
        {
            ScoreRow row = rows[i];
            names[i] = driverName?.Invoke(row.Tank) is { Length: > 0 } driver ? $"{row.Name}  {driver}" : row.Name;
            if (row.Tank == player.Color)
                names[i] += " (you)";
            nameWidth = MathF.Max(nameWidth, HudTheme.Measure(NameSize, names[i]).X);
        }
        float countX = Swatch + nameWidth + 18f;
        float timesX = countX + CountColumn;
        float width = MathF.Min(MathF.Max(MinWidth, timesX + MinTimesWidth + 2 * Pad), MathF.Max(MinWidth, vp.w - 32f));
        float timesWidth = MathF.Max(80f, width - 2 * Pad - timesX);

        var lines = new string[rows.Count];
        var rowHeights = new float[rows.Count];
        float height = Pad + TitleHeight;
        for (int i = 0; i < rows.Count; i++)
        {
            lines[i] = rows[i].Deaths.Count == 0
                ? "no deaths"
                : string.Join("    ", rows[i].Deaths.Select(d => $"{d.Clock} {d.Cause}"));
            rowHeights[i] = MathF.Max(NameSize + 4f, HudTheme.Measure(TimeSize, lines[i], timesWidth).Y) + RowGap;
            height += rowHeights[i];
        }
        height += Pad - RowGap;

        var min = new Vector2(MathF.Floor(vp.x + (vp.w - width) * 0.5f), MathF.Floor(vp.y + MathF.Max(24f, vp.h * 0.14f)));
        var max = min + new Vector2(width, height);
        HudTheme.DrawPanel(dl, min, max);

        var at = min + new Vector2(Pad, Pad);
        HudTheme.DrawTitle(dl, at, width - 2 * Pad, "DEATHS", 19f);
        string clock = TankDeathView.Clock(world.MatchTime);
        var clockSize = HudTheme.Measure(16f, clock);
        HudTheme.TextAt(dl, 16f, new Vector2(max.X - Pad - clockSize.X, at.Y + 2f), clock, HudTheme.TextDim);
        at.Y += TitleHeight;

        for (int i = 0; i < rows.Count; i++)
        {
            ScoreRow row = rows[i];
            Color c = TankSprites.GetPixelColor(1, row.Tank);
            dl.AddRectFilled(at + new Vector2(0f, 3f), at + new Vector2(12f, 15f), HudTheme.Rgba(c.R, c.G, c.B));
            HudTheme.TextAt(dl, NameSize, at + new Vector2(Swatch, 0f), names[i],
                row.Tank == player.Color ? HudTheme.Amber : HudTheme.Text);
            string count = row.Deaths.Count.ToString(CultureInfo.InvariantCulture);
            HudTheme.TextAt(dl, NameSize, at + new Vector2(countX, 0f), count,
                row.Deaths.Count == 0 ? HudTheme.TextDim : HudTheme.Danger);
            HudTheme.TextAt(dl, TimeSize, at + new Vector2(timesX, 1.5f), lines[i],
                row.Deaths.Count == 0 ? HudTheme.TextDim : HudTheme.Text, wrap: timesWidth);
            at.Y += rowHeights[i];
        }
    }
}
