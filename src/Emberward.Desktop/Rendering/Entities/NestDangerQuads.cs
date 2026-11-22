namespace Emberward.Desktop.Rendering.Entities;

using Emberward.Core;
using Emberward.Core.Rendering;
using Emberward.Core.Types;

/// <summary>
/// A thin pink danger ring over each nest the player's side has discovered and that still has creatures, at the
/// distance from which the nest senses the player's tank now, with a title above it. Core answers which nests and how
/// far (<see cref="World.CopyNestDangers"/>); this only draws. A dormant nest's ring is faint, an awake one's full, and
/// one whose creatures are out pulses. Drawn whether or not the nest is in sight: a discovered nest stays marked.
/// </summary>
public sealed class NestDangerQuads
{
    /// <summary>The label's title line.</summary>
    public const string Title = "EMBERITE NEST";

    /// <summary>The label's warning line, under the title.</summary>
    public const string Warning = "DANGER: EMBERITES";

    /// <summary>The emberite pink of the ring and the title.</summary>
    public static readonly Color Pink = new(255, 92, 150);

    /// <summary>The red of the warning line.</summary>
    public static readonly Color Red = new(255, 60, 60);

    /// <summary>Room around the ring for its glow, in cells.</summary>
    private const float RingMargin = 3f;

    /// <summary>A label dot, in cells, and the gap between the ring and the label and between its lines.</summary>
    private const float DotCells = 0.3f;
    private const float GapCells = 1f;

    /// <summary>The drawn ring's radius, in cells: a small mark around the nest, not the distance it senses from.</summary>
    public const float RingRadius = 6f;

    private NestDanger[] _dangers = [];
    private int _count;

    /// <summary>The nests captured at the last step, for tests.</summary>
    public ReadOnlySpan<NestDanger> Dangers => _dangers.AsSpan(0, _count);

    public void CaptureStep(World world, int localSlot)
    {
        int nests = world.Emberites?.Nests.Count ?? 0;
        if (_dangers.Length < nests)
            _dangers = new NestDanger[nests];
        _count = localSlot >= 0 && localSlot < world.TankList.Count
            ? world.CopyNestDangers(world.TankList.Tanks[localSlot], _dangers)
            : 0;
    }

    /// <summary>How bright a ring and its label are: faint asleep, full awake (the shader pulses one swarming).</summary>
    public static float Opacity(NestDangerLevel level) => level == NestDangerLevel.Dormant ? 0.45f : 1f;

    /// <summary>The rings, on the ground under machines and tanks.</summary>
    public void EmitRings(EntityQuads quads)
    {
        float scale = quads.View.PixelScale;
        for (int i = 0; i < _count; i++)
        {
            NestDanger danger = _dangers[i];
            float extent = RingRadius + RingMargin;
            if (quads.IsOffScreen(quads.ScreenX(danger.Centre.X), quads.ScreenY(danger.Centre.Y), extent * scale))
                continue;
            quads.Add(EntityQuads.Centre(danger.Centre.X), EntityQuads.Centre(danger.Centre.Y), 1f, 0f,
                -extent, extent, -extent, extent, Pink, EntityShape.KindDangerRing,
                RingRadius, (float)danger.Level, quads.Phase, Opacity(danger.Level));
        }
    }

    /// <summary>The labels, over everything but the HUD: the title and the warning just above each ring's top.</summary>
    public void EmitLabels(EntityQuads quads)
    {
        float scale = quads.View.PixelScale;
        // Keep each dot at least one render pixel wide.
        float dot = MathF.Max(DotCells, 1f / scale);
        float line = (GlyphText.GlyphHeight + 1) * dot;
        float width = GlyphText.Width(Warning, dot) * 0.5f;
        for (int i = 0; i < _count; i++)
        {
            NestDanger danger = _dangers[i];
            float x = EntityQuads.Centre(danger.Centre.X);
            float y = EntityQuads.Centre(danger.Centre.Y) - RingRadius - GapCells;
            float warningTop = -line;
            float titleTop = warningTop - GapCells * 0.5f - line;
            if (quads.IsOffScreen(quads.ScreenX(danger.Centre.X), quads.ScreenY(y + titleTop * 0.5f), MathF.Max(width, -titleTop) * scale))
                continue;
            float alpha = Opacity(danger.Level);
            GlyphText.AddLine(quads, x, y, titleTop, Title, dot, Pink, alpha);
            GlyphText.AddLine(quads, x, y, warningTop, Warning, dot, Red, alpha);
        }
    }
}
