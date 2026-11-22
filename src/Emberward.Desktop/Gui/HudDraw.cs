namespace Emberward.Desktop.Gui;

using System.Numerics;
using ImGuiNET;

/// <summary>Drawing helpers the HUD pieces share: outlined text, colour packing, arcs and convex polygon bands.</summary>
public static class HudDraw
{
    /// <summary>An RGB colour (0-1) and alpha packed as ImGui's ABGR.</summary>
    public static uint PackColor(Vector3 rgb, float alpha) =>
        (uint)(Math.Clamp(alpha, 0f, 1f) * 255f) << 24 |
        (uint)(Math.Clamp(rgb.Z, 0f, 1f) * 255f) << 16 |
        (uint)(Math.Clamp(rgb.Y, 0f, 1f) * 255f) << 8 |
        (uint)(Math.Clamp(rgb.X, 0f, 1f) * 255f);

    public static void DrawOutlinedText(ImDrawListPtr dl, float size, Vector2 pos, string text, uint color, float outline = 1f)
    {
        var font = ImGui.GetFont();
        uint shadow = 0xE0000000u;
        dl.AddText(font, size, pos + new Vector2(outline, outline), shadow, text);
        dl.AddText(font, size, pos + new Vector2(-outline, outline), shadow, text);
        dl.AddText(font, size, pos + new Vector2(outline, -outline), shadow, text);
        dl.AddText(font, size, pos + new Vector2(-outline, -outline), shadow, text);
        dl.AddText(font, size, pos, color, text);
    }

    public static void DrawCenteredText(ImDrawListPtr dl, float size, float centerX, float y, string text, uint color)
    {
        var font = ImGui.GetFont();
        float w = font.CalcTextSizeA(size, float.MaxValue, 0f, text).X;
        DrawOutlinedText(dl, size, new Vector2(MathF.Floor(centerX - w * 0.5f), y), text, color);
    }

    /// <summary>A line of HUD text (outlined, never treated as a format string, so a literal '%' is shown).</summary>
    public static void ColoredText(uint color, string text) => HudTheme.LineRow(text, color, 16.5f);

    public static void StrokeArc(ImDrawListPtr dl, Vector2 c, float r, float a0, float a1, uint color, float thickness)
    {
        if (a1 <= a0)
            return;
        int segments = Math.Max(2, (int)((a1 - a0) / (MathF.PI * 2f) * 64f));
        dl.PathArcTo(c, r, a0, a1, segments);
        dl.PathStroke(color, ImDrawFlags.None, thickness);
    }
}
