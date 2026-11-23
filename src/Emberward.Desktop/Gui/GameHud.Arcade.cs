namespace Emberward.Desktop.Gui;

using System.Numerics;
using ImGuiNET;
using Emberward.Core;
using Emberward.Core.Entities.Tanks;

/// <summary>
/// A timed match and its powerup drops (DESIGN.md §6.8): the clock and the drop lines on a plate at the top of the
/// view, an arrow at the edge of the view to each drop that is off screen, and the player's running powerups as tags
/// above the HUD strip. The board itself is in the base panel (<see cref="ArcadeLines.DrawInPanel"/>). What all of
/// them say is <see cref="Scoreboard"/> and <see cref="DropBoard"/>, shared with the browser; this places and draws.
/// </summary>
public partial class GameHud
{
    private readonly DropBoard _drops = new();
    private Func<int, string>? _localNames;
    private int _localNamesSlot = -1;

    // This frame's board and drop lines; null and empty in a match without the rules.
    private Scoreboard? _board;
    private IReadOnlyList<DropLine> _dropLines = [];

    // Where this frame's drop pointers have their badges and tags, so that two do not overlap.
    private readonly List<Vector2> _pointerBadges = [];
    private readonly List<(Vector2 Min, Vector2 Max)> _pointerTags = [];

    // What the HUD's panels cover of the view this frame: a site under one of them is pointed at like one off screen.
    private readonly List<(Vector2 Min, Vector2 Max)> _covered = [];
    private float _guidanceBottom;
    private float _gapLeft, _gapRight;

    // Whether the view is wide enough for the plate between the tutorial card and the base panel.
    private bool _clockPlateFits;

    /// <summary>
    /// What to call the tank in a slot, on the board and in the toasts. The game sets the players' names in a network
    /// match; null, as in a local match, says <c>YOU</c> and <c>BOT n</c> (<see cref="SlotNames.Local"/>).
    /// </summary>
    public Func<int, string>? Names
    {
        get => _guidance.Names;
        set => _guidance.Names = value;
    }

    /// <summary>
    /// The bottom edge of what this HUD draws at the top centre of the view (the clock plate), or the view's top when
    /// it draws nothing there: the warning banner, the network notice and the match-over lines start below it.
    /// </summary>
    public float TopCentreBottom { get; private set; }

    private Func<int, string> NamesFor(Tank player)
    {
        if (Names is { } names)
            return names;
        if (_localNames == null || _localNamesSlot != player.Color)
            (_localNames, _localNamesSlot) = (SlotNames.Local(player.Color), player.Color);
        return _localNames;
    }

    /// <summary>Reads the frame's board and drop lines and decides where they go. Call before any panel is drawn.</summary>
    private void ReadArcade(Tank player, World world)
    {
        var names = NamesFor(player);
        _board = Scoreboards.Of(world, player.Color, names);
        // Over, the match has its final board to show; what drops next is no longer anyone's business.
        _dropLines = world.IsMatchOver ? [] : _drops.Lines(world, player, names);
        var vp = ViewportRect;
        // The tutorial card and toasts on the left, the base panel on the right, and a margin either side of the plate.
        _clockPlateFits = vp.w >= 14f + CardWidth + 12f + ArcadeLines.PlateWidth + 12f + NetworkPanel.Width + 14f;
        float plate = _clockPlateFits ? ArcadeLines.PlateHeight(_board, _dropLines.Count) : 0f;
        TopCentreBottom = plate > 0f ? vp.y + 14f + plate : vp.y;
    }

    /// <summary>What the base panel shows of the match: the board, and in a narrow view the clock and the drops too.</summary>
    private ArcadePanel ArcadePanel => new(_board, _dropLines, WithClock: !_clockPlateFits, _time);

    /// <summary>The view below the clock plate, for what would otherwise be drawn at its top centre.</summary>
    private (float x, float y, float w, float h) BelowClockPlate((float x, float y, float w, float h) vp) =>
        TopCentreBottom > vp.y ? (vp.x, TopCentreBottom, vp.w, vp.h - (TopCentreBottom - vp.y)) : vp;

    private void DrawArcade(Tank player, World world)
    {
        var vp = ViewportRect;
        var dl = ImGui.GetForegroundDrawList();
        // The card and the toasts on the left, the base panel on the right.
        float left = vp.x + 14f + CardWidth, right = vp.x + vp.w - 14f - NetworkPanel.Width;
        (_gapLeft, _gapRight) = (left, right);
        _covered.Clear();
        _covered.Add((new Vector2(vp.x, vp.y), new Vector2(left, _guidanceBottom)));
        _covered.Add((new Vector2(right, vp.y), new Vector2(vp.x + vp.w, _network.Bottom)));
        if (_clockPlateFits)
        {
            // Centred in the gap between the card and the base panel, which is not the view's centre.
            var min = new Vector2(MathF.Floor((left + right - ArcadeLines.PlateWidth) * 0.5f), vp.y + 14f);
            ArcadeLines.DrawPlate(dl, min, _board, _dropLines, _time);
            _covered.Add((min, new Vector2(min.X + ArcadeLines.PlateWidth, TopCentreBottom)));
        }
        _pointerBadges.Clear();
        _pointerTags.Clear();
        if (!world.IsMatchOver)
            foreach (DropLine line in _dropLines)
                if (line.IsPointedAt)
                    DrawDropPointer(dl, line, vp);
        DrawEffectTags(dl, player, world, vp);
    }

    /// <summary>
    /// While a drop's site is off screen, or under one of the HUD's panels, a round badge at the edge of the view with
    /// the kind's mark, in the kind's colour, a beak toward the site, and a tag saying which, when and how far
    /// (<see cref="DropLine.PointerText"/>). On screen the marker on the ground says it. Round and marked, it is not the
    /// tutorial's amber arrow nor the red one to a base under fire, and it sits further in than they do.
    /// </summary>
    private void DrawDropPointer(ImDrawListPtr dl, in DropLine line, (float x, float y, float w, float h) vp)
    {
        var cam = Camera;
        float sx = vp.x + ToUi((line.Site.X + 0.5f) * cam.Scale - cam.X);
        float sy = vp.y + ToUi((line.Site.Y + 0.5f) * cam.Scale - cam.Y);
        // Below the clock plate, and below where a warning banner goes under it.
        const float bannerRoom = 34f;
        float top = MathF.Max(vp.y, TopCentreBottom) + bannerRoom, bottom = ViewBottom(vp);
        const float seen = 28f, margin = 62f, radius = 15f;
        var site = new Vector2(sx, sy);
        if (sx >= vp.x + seen && sx <= vp.x + vp.w - seen && sy >= vp.y + seen && sy <= bottom - seen
            && !ArcadeLines.IsUnder(_covered, site, seen))
            return;

        var center = new Vector2(vp.x + vp.w * 0.5f, (vp.y + bottom) * 0.5f);
        var dir = site - center;
        float len = dir.Length();
        if (len < 1f)
            return;
        dir /= len;
        // The top is already below the plate: there the beak may reach it.
        float halfW = vp.w * 0.5f - margin, halfDown = bottom - center.Y - margin;
        float halfUp = MathF.Max(center.Y - top - (radius + 15f), radius);
        float t = MathF.Min(halfW / MathF.Max(MathF.Abs(dir.X), 1e-3f),
            (dir.Y < 0f ? halfUp : halfDown) / MathF.Max(MathF.Abs(dir.Y), 1e-3f));
        // A site under a panel is nearer than the edge: stop short of it, so that the beak still points at it.
        t = MathF.Min(t, MathF.Max(len - (radius + 30f), 0f));
        var side = new Vector2(-dir.Y, dir.X);
        var wanted = center + dir * t;
        // Not on the card, a toast or the base panel: into the gap between them, still pointing the same way.
        float clear = radius + 22f;
        if (ArcadeLines.IsUnder(_covered, wanted, clear))
        {
            // In a view so narrow that the gap has no room for it, the gap's middle.
            float lo = _gapLeft + clear, hi = _gapRight - clear;
            wanted.X = lo <= hi ? Math.Clamp(wanted.X, lo, hi) : (lo + hi) * 0.5f;
        }
        var at = ArcadeLines.Beside(_pointerBadges, wanted, side, radius * 2f + 14f);
        _pointerBadges.Add(at);

        // A drop about to land, or about to run out, beats.
        float pulse = line.Urgent ? 0.5f + 0.5f * MathF.Sin(_time * 9f) : 1f;
        uint color = HudTheme.WithAlpha(line.Color, 0.65f + 0.35f * pulse);
        dl.AddTriangleFilled(at + dir * (radius + 13f), at + dir * (radius - 2f) + side * 9f, at + dir * (radius - 2f) - side * 9f, color);
        dl.AddCircleFilled(at, radius, HudTheme.Rgba(14, 14, 16, 235), 32);
        dl.AddCircle(at, radius, color, 32, 2.6f);
        if (line.State == DropLineState.Forecast)
            // Still to land: a broken outer ring, as the marker on the ground has.
            for (int i = 0; i < 4; i++)
                HudDraw.StrokeArc(dl, at, radius + 5f, i * MathF.PI * 0.5f + _time, i * MathF.PI * 0.5f + _time + 0.9f, color, 1.6f);
        ArcadeLines.DrawChip(dl, at, line.Kind, 1f, 9f);

        string text = line.PointerText;
        var sz = HudTheme.Measure(14f, text);
        // The tag toward the middle of the view, clear of the badge whichever way the arrow points.
        float reach = radius + 12f + MathF.Abs(dir.X) * (sz.X * 0.5f + 9f) + MathF.Abs(dir.Y) * (sz.Y * 0.5f + 5f);
        var tag = at - dir * reach;
        // Two drops the same way: the later tag goes under the earlier one, not over it.
        var half = new Vector2(sz.X * 0.5f + 11f, sz.Y * 0.5f + 7f);
        tag = ArcadeLines.ClearOf(_pointerTags, tag, half);
        _pointerTags.Add((tag - half, tag + half));
        HudTheme.DrawTag(dl, tag, text, line.Color, 14f);
    }

    /// <summary>
    /// The player's running powerups, side by side just above the HUD strip: the kind's mark and name, the seconds
    /// left, and a bar that drains. One in its last seconds blinks.
    /// </summary>
    private void DrawEffectTags(ImDrawListPtr dl, Tank player, World world, (float x, float y, float w, float h) vp)
    {
        var tags = DropBoard.Effects(world.PowerupsOf(player));
        if (tags.Count == 0 || player.IsDead)
            return;
        const float size = 15f, gap = 8f, height = 30f;
        Span<float> widths = stackalloc float[tags.Count];
        float total = -gap;
        for (int i = 0; i < tags.Count; i++)
        {
            widths[i] = HudTheme.Measure(size, tags[i].Text).X + 22f;
            total += widths[i] + gap;
        }
        float x = MathF.Floor(vp.x + vp.w * 0.5f - total * 0.5f);
        float y = MathF.Floor(ViewBottom(vp) - height - 46f);
        bool blinkOn = (int)(_time * 4f) % 2 == 0;
        for (int i = 0; i < tags.Count; i++)
        {
            EffectTag tag = tags[i];
            var min = new Vector2(x, y);
            var max = min + new Vector2(widths[i], height);
            uint color = tag.Blink && !blinkOn ? HudTheme.WithAlpha(tag.Color, 0.45f) : tag.Color;
            dl.AddRectFilled(min, max, HudTheme.Rgba(14, 14, 16, 225), 4f);
            dl.AddRect(min, max, HudTheme.WithAlpha(color, 0.85f), 4f, ImDrawFlags.None, 1.4f);
            HudTheme.TextAt(dl, size, min + new Vector2(11f, 4f), tag.Text, color);
            var barMin = new Vector2(min.X + 5f, max.Y - 6f);
            float barW = widths[i] - 10f;
            dl.AddRectFilled(barMin, barMin + new Vector2(barW, 3f), HudTheme.Track);
            dl.AddRectFilled(barMin, barMin + new Vector2(barW * Math.Clamp(tag.Fraction, 0f, 1f), 3f), color);
            x += widths[i] + gap;
        }
    }
}

/// <summary>What the base panel shows of a timed match (<see cref="ArcadeLines.DrawInPanel"/>).</summary>
/// <param name="Board">The board, or null in a match without a clock.</param>
/// <param name="Drops">The drop lines; empty in a match without drops.</param>
/// <param name="WithClock">The view is too narrow for the plate at its top: the panel shows the clock and the drops as well.</param>
/// <param name="Time">The HUD's clock in seconds, for what blinks.</param>
public readonly record struct ArcadePanel(Scoreboard? Board, IReadOnlyList<DropLine> Drops, bool WithClock, float Time);
