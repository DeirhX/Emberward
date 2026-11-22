namespace Emberward.Desktop.Rendering.Entities;

using Emberward.Core;
using Emberward.Core.Entities.Machines;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Sensing;
using Emberward.Core.Types;
using Emberward.Desktop.Gui;

/// <summary>Emits the in-world charge and flow labels after the other entities, beneath the HUD.</summary>
internal sealed class PowerLabelQuads
{
    private const float BarWidth = 40f;
    private const float BarHeight = 3f;
    private const float LineGap = 2f;
    private const float DotSize = 1.5f;
    private const int GlyphHeight = GlyphText.GlyphHeight;

    private readonly List<MachinePowerLabel> _labels = new();
    private readonly List<Machine> _near = new();
    private World? _world;
    private Tank? _player;
    private VisionMap? _vision;
    private bool _labelsCurrent;

    public void CaptureStep(World world, int localSlot)
    {
        _world = world;
        _player = localSlot >= 0 && localSlot < world.TankList.Count ? world.TankList.Tanks[localSlot] : null;
        _labelsCurrent = false;
    }

    public void Emit(EntityQuads quads, VisionMap? vision, float cellSize)
    {
        float zoom = PowerLabels.ZoomFade(cellSize);
        if (zoom <= 0f || _world?.Energy == null || _player == null)
            return;
        if (!_labelsCurrent || _vision != vision)
        {
            PowerLabels.Collect(_world, _player, vision, _labels, _near);
            _vision = vision;
            _labelsCurrent = true;
        }

        // The view can be rendered at a different density from the UI. Keep each dot at least one render pixel wide.
        float scale = quads.View.PixelScale;
        float pixelsPerUiUnit = scale / cellSize;
        float dot = MathF.Max(1f, DotSize * pixelsPerUiUnit) / scale;
        float barHeight = BarHeight / cellSize;
        float gap = LineGap / cellSize;
        float halfBar = BarWidth * 0.5f / cellSize;
        float flowTop = -barHeight - gap - GlyphHeight * dot;
        float chargeTop = flowTop - gap - GlyphHeight * dot;
        foreach (var (machine, label, opacity) in _labels)
        {
            float screenX = quads.ScreenX(machine.Position.X);
            float screenY = quads.ScreenY(machine.Position.Y - PowerLabels.LiftCells - 0.5f);
            if (screenX < 0f || screenX > quads.View.ViewSize.X || screenY < -chargeTop * scale || screenY > quads.View.ViewSize.Y)
                continue;
            float x = EntityQuads.Centre(machine.Position.X);
            float y = EntityQuads.Centre(machine.Position.Y - PowerLabels.LiftCells - 0.5f);

            float alpha = opacity * zoom;
            AddPlate(quads, x, y, -halfBar, halfBar, barHeight, HudPalette.Track, alpha);
            if (label.Fill > 0f)
                AddPlate(quads, x, y, -halfBar, -halfBar + 2f * halfBar * label.Fill, barHeight, label.Color, alpha);
            AddLine(quads, x, y, chargeTop, label.Charge, dot, HudPalette.Text, alpha);
            AddLine(quads, x, y, flowTop, label.Flow, dot, label.Color, alpha);
        }
    }

    private static void AddPlate(EntityQuads quads, float x, float y, float left, float right, float height, uint color, float alpha) =>
        quads.Add(x, y, 1f, 0f, left, right, -height, 0f, Rgb(color), EntityShape.KindPowerLabel, 0f, 0f, 0f,
            alpha * (color >> 24) / 255f);

    private static void AddLine(EntityQuads quads, float x, float y, float top, string text, float dot, uint color, float alpha) =>
        GlyphText.AddLine(quads, x, y, top, text, dot, Rgb(color), alpha);

    private static Color Rgb(uint abgr) => new((byte)abgr, (byte)(abgr >> 8), (byte)(abgr >> 16));
}
