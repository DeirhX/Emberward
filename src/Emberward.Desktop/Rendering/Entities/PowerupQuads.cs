namespace Emberward.Desktop.Rendering.Entities;

using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities.Powerups;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Rendering;
using Emberward.Core.Sensing;
using Emberward.Core.Types;
using Emberward.Desktop.Gui;
using Emberward.Desktop.Presentation;

/// <summary>
/// The powerup drops in the world (DESIGN.md §6.8): a reticle on the site where the next drop lands, with its kind
/// above it and its countdown below, closing in over the last seconds; each drop on the ground as a glowing capsule
/// with its kind's letter, in a ring that runs down with the time it has left; and a ring of dashes round every tank
/// that has a powerup running, lit for the share that is left, with the kind's letter beside it. Everything is in the
/// kind's colour (<see cref="PowerupPresentations"/>) and carries its letter, so colour is never the only sign.
/// The site and the drops are drawn whatever the player sees, as every side knows them; a tank's ring only while the
/// tank is in sight. Core answers what and where (<see cref="World.PowerupForecast"/>,
/// <see cref="World.CopyPowerupRenderStates"/>, <see cref="World.PowerupsOf"/>); this only draws, and costs nothing
/// while the rules run no drops.
/// </summary>
public sealed class PowerupQuads
{
    /// <summary>The radius of a tank's first ring, in cells, and how much farther out each further one is.</summary>
    public const float HaloRadius = 6.3f;
    public const float HaloSpacing = 1.3f;

    /// <summary>A label dot and a mark's dot, in cells, and the gap between a ring and its labels.</summary>
    private const float LabelDot = 0.3f;
    private const float CountdownDot = 0.42f;
    private const float MarkDot = 0.5f;
    private const float GapCells = 0.9f;

    private static readonly Color White = new(255, 255, 255);

    private PowerupForecast _forecast;
    private PowerupRenderState[] _drops = new PowerupRenderState[4];
    private int _dropCount;
    private (int Color, TankPowerupView View)[] _effects = [];
    private int _effectCount;

    /// <summary>The forecast, the drops and the tanks with a powerup captured at the last step, for tests.</summary>
    public PowerupForecast Forecast => _forecast;
    public ReadOnlySpan<PowerupRenderState> Drops => _drops.AsSpan(0, _dropCount);
    public int TanksWithEffects => _effectCount;

    public void CaptureStep(World world)
    {
        // A match that is over has its result to show: nothing is marked on the ground any more.
        _forecast = world.IsMatchOver ? default : world.PowerupForecast;
        _dropCount = _effectCount = 0;
        if (!_forecast.Active)
            return;
        if (_drops.Length < world.PowerupDropCount)
            _drops = new PowerupRenderState[world.PowerupDropCount * 2];
        _dropCount = world.CopyPowerupRenderStates(_drops);
        var tanks = world.TankList.Tanks;
        if (_effects.Length < tanks.Count)
            _effects = new (int, TankPowerupView)[tanks.Count];
        foreach (Tank tank in tanks)
        {
            TankPowerupView view = world.PowerupsOf(tank);
            if (view.Any && !tank.IsDead)
                _effects[_effectCount++] = (tank.Color, view);
        }
    }

    /// <summary>
    /// Whether the entity layer has a glyph for every character of <paramref name="text"/>: a kind's tag and mark are
    /// written on the ground with them (<c>PowerupPresentationTests</c>).
    /// </summary>
    public static bool CanWrite(string text)
    {
        foreach (char character in text)
            if (!GlyphText.Has(character))
                return false;
        return true;
    }

    /// <summary>The kind's colour as the entity layer takes it.</summary>
    public static Color ColorOf(PowerupKind kind)
    {
        uint c = PowerupPresentations.Get(kind).Color;
        return new Color((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF));
    }

    /// <summary>The site's reticle and the drops, on the ground under machines and tanks. Not behind the fog.</summary>
    public void EmitGround(EntityQuads quads)
    {
        if (!_forecast.Active)
            return;
        float scale = quads.View.PixelScale;
        float radius = Tweaks.Powerups.PickupDiameter * 0.5f;
        float seconds = _forecast.SecondsUntilLanding;
        float extent = radius * 2.5f + 2f;
        if (!quads.IsOffScreen(quads.ScreenX(_forecast.Site.X), quads.ScreenY(_forecast.Site.Y), extent * scale))
            quads.Add(EntityQuads.Centre(_forecast.Site.X), EntityQuads.Centre(_forecast.Site.Y), 1f, 0f,
                -extent, extent, -extent, extent, ColorOf(_forecast.Kind), EntityShape.KindPowerup,
                radius, Math.Clamp(seconds / DropBoard.UrgentSeconds, 0f, 1f), quads.Phase,
                Mode(0, seconds <= DropBoard.UrgentSeconds));

        for (int i = 0; i < _dropCount; i++)
        {
            ref readonly PowerupRenderState drop = ref _drops[i];
            extent = drop.Radius * 1.8f + 1f;
            if (quads.IsOffScreen(quads.ScreenX(drop.Cell.X), quads.ScreenY(drop.Cell.Y), extent * scale))
                continue;
            quads.Add(EntityQuads.Centre(drop.Cell.X), EntityQuads.Centre(drop.Cell.Y), 1f, 0f,
                -extent, extent, -extent, extent, ColorOf(drop.Kind), EntityShape.KindPowerup,
                drop.Radius, drop.TicksUntilExpiry / (float)Tweaks.Powerups.GroundTicks, quads.Phase,
                Mode(1, drop.SecondsUntilExpiry <= DropBoard.UrgentSeconds));
        }
    }

    /// <summary>
    /// The rings of tanks with a powerup running, over the tanks: one ring a kind, the first innermost, each with its
    /// kind's letter at its upper right. A tank out of sight has none.
    /// </summary>
    public void EmitHalos(EntityQuads quads, EntityMotion motion, VisionMap? vision)
    {
        if (_effectCount == 0)
            return;
        float scale = quads.View.PixelScale;
        float dot = MathF.Max(LabelDot, 1f / scale);
        ReadOnlySpan<TankRenderState> tanks = motion.Tanks;
        for (int e = 0; e < _effectCount; e++)
        {
            var (color, view) = _effects[e];
            for (int i = 0; i < tanks.Length; i++)
            {
                ref readonly TankRenderState tank = ref tanks[i];
                if (tank.Color != color || tank.IsDead || !Sight.Sees(vision, tank.Position))
                    continue;
                var (start, drift) = motion.TankMotion(i);
                float reach = HaloRadius + HaloSpacing * PowerupKinds.Count + 2f;
                if (quads.IsOffScreen(quads.ScreenX(start.X), quads.ScreenY(start.Y), reach * scale))
                    continue;
                float cx = EntityQuads.Centre(start.X), cy = EntityQuads.Centre(start.Y);
                int ring = 0;
                foreach (var kind in PowerupPresentations.All)
                {
                    if (!view.IsRunning(kind.Kind))
                        continue;
                    float radius = HaloRadius + HaloSpacing * ring;
                    float extent = radius + 2f;
                    Color tint = ColorOf(kind.Kind);
                    quads.Add(cx, cy, 1f, 0f, -extent, extent, -extent, extent, tint, EntityShape.KindPowerup,
                        radius, view.FractionLeft(kind.Kind), quads.Phase,
                        Mode(2, view.SecondsLeft(kind.Kind) <= DropBoard.UrgentSeconds * 0.5f), drift);
                    // The letters sit in a row above the rings' upper right, the first ring's first.
                    float markX = cx + HaloRadius * 0.75f + ring * (GlyphText.GlyphWidth + 2) * dot;
                    float markTop = -HaloRadius * 0.75f - (GlyphText.GlyphHeight + 2) * dot;
                    GlyphText.AddLine(quads, markX, cy, markTop, kind.Mark.ToString(), dot, tint, 1f, drift);
                    ring++;
                }
            }
        }
    }

    /// <summary>
    /// The letters and the labels, over everything but the HUD: the kind's letter in the middle of the site and of
    /// each drop, the kind's name above, and below the time until the landing (<c>0:32</c>) or the seconds a drop has
    /// left (<c>52s</c>).
    /// </summary>
    public void EmitLabels(EntityQuads quads)
    {
        if (!_forecast.Active)
            return;
        float radius = Tweaks.Powerups.PickupDiameter * 0.5f;
        int seconds = (int)MathF.Ceiling(_forecast.SecondsUntilLanding);
        Label(quads, _forecast.Site, radius, _forecast.Kind, Scoreboards.MinutesSeconds(seconds), markAlpha: 0.85f, onCapsule: false);
        for (int i = 0; i < _dropCount; i++)
        {
            ref readonly PowerupRenderState drop = ref _drops[i];
            int left = (int)MathF.Ceiling(drop.SecondsUntilExpiry);
            Label(quads, drop.Cell, drop.Radius, drop.Kind,
                left.ToString(System.Globalization.CultureInfo.InvariantCulture) + "s", markAlpha: 1f, onCapsule: true);
        }
    }

    private static void Label(
        EntityQuads quads, Position cell, float radius, PowerupKind kind, string below, float markAlpha, bool onCapsule)
    {
        float scale = quads.View.PixelScale;
        float reach = radius + GapCells + (GlyphText.GlyphHeight + 1) * CountdownDot + 2f;
        if (quads.IsOffScreen(quads.ScreenX(cell.X), quads.ScreenY(cell.Y), MathF.Max(reach, 14f) * scale))
            return;
        var presentation = PowerupPresentations.Get(kind);
        Color tint = ColorOf(kind);
        float x = EntityQuads.Centre(cell.X), y = EntityQuads.Centre(cell.Y);
        // Keep each dot at least one render pixel wide.
        float markDot = MathF.Max(MarkDot, 1f / scale);
        float nameDot = MathF.Max(LabelDot, 1f / scale);
        float belowDot = MathF.Max(CountdownDot, 1f / scale);
        // On a drop's dark capsule the letter is nearly white; on the bare ground of a site it keeps the kind's colour.
        Color mark = onCapsule ? Mix(tint, White, 0.7f) : tint;
        GlyphText.AddLine(quads, x, y, -GlyphText.GlyphHeight * 0.5f * markDot, presentation.Mark.ToString(), markDot, mark, markAlpha);
        GlyphText.AddLine(quads, x, y, -radius - GapCells - GlyphText.GlyphHeight * nameDot, presentation.Tag, nameDot, tint, 1f);
        GlyphText.AddLine(quads, x, y, radius + GapCells, below, belowDot, tint, 1f);
    }

    /// <summary>The shader's .w: the mode in the whole part, a half more while the thing is urgent.</summary>
    private static float Mode(int mode, bool urgent) => mode + (urgent ? 0.5f : 0f);

    private static Color Mix(Color a, Color b, float t) => new(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
