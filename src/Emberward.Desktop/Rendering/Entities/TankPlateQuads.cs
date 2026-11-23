namespace Emberward.Desktop.Rendering.Entities;

using System.Globalization;
using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Rendering;
using Emberward.Core.Sensing;
using Emberward.Core.Types;
using Emberward.Desktop.Config;
using Emberward.Desktop.Gui;

/// <summary>
/// A name plate over each enemy tank the player's side sees, in the tank's colour. When the tank goes out of sight its
/// plate stays where it was last seen and fades out over <see cref="DesktopScreenTweaks.TankPlateFadeSeconds"/>; seen
/// again, anywhere, the plate follows it again at once. A dead tank's plate goes. Output only: one record per tank,
/// kept up from Core's answers once a step (<see cref="Tank.IsRivalOf"/>, <see cref="Tank.IsDead"/> and the tank
/// sprites' fog rule, <see cref="Sight"/>), so the cost follows the number of tanks and never the map.
/// </summary>
public sealed class TankPlateQuads
{
    /// <summary>Longest name a plate shows; a longer one is cut.</summary>
    public const int MaxNameLength = 16;

    /// <summary>A plate dot, in cells, and the room around the name on its backing.</summary>
    private const float DotCells = 0.25f;
    private const float PadCells = 0.5f;

    /// <summary>Cells from the tank's centre to the plate's bottom: clear of the hull (5.5 cells) and its shield.</summary>
    private const float LiftCells = 8f;

    /// <summary>The plate's dark backing (opacity 0..1) behind the name.</summary>
    private static readonly Color Backing = new(8, 8, 10);
    private const float BackingOpacity = 0.55f;

    /// <summary>Steps a plate out of sight takes to fade out.</summary>
    public static readonly int FadeSteps =
        Math.Max(1, (int)MathF.Round(DesktopScreenTweaks.TankPlateFadeSeconds * Tweaks.Perf.TargetFps));

    private Plate[] _plates = [];
    private string?[] _playerNames = [];

    /// <summary>What a tank's plate shows, by slot (indexed like the world's tank list).</summary>
    public struct Plate
    {
        /// <summary>A plate shows: the tank is in sight, or was seen less than <see cref="FadeSteps"/> ago.</summary>
        public bool Active;

        /// <summary>The tank is in sight at the last step; the plate follows it.</summary>
        public bool InSight;

        /// <summary>Where the tank was last seen.</summary>
        public Position Cell;

        /// <summary>Steps since the tank was last seen: 0 while in sight.</summary>
        public int StepsLost;

        /// <summary>The tank's colour index (<see cref="TankSprites.GetPixelColor"/>).</summary>
        public int Color;

        public string Name;

        /// <summary>The computer plays this tank's side (its name is then the default, <c>BOT n</c>).</summary>
        public bool Bot;
    }

    /// <summary>Every tank's plate after the last step, for tests; an inactive one draws nothing.</summary>
    public ReadOnlySpan<Plate> Plates => _plates;

    /// <summary>
    /// The name a tank goes by with no player name from the match (<see cref="SetPlayerName"/>): <c>BOT n</c> for a side
    /// the computer plays, <c>PLAYER n</c> otherwise, n counting slots from 1.
    /// </summary>
    public static string DefaultName(int slot, bool botControlled) =>
        string.Create(CultureInfo.InvariantCulture, $"{(botControlled ? "BOT" : "PLAYER")} {slot + 1}");

    /// <summary>
    /// The name of whoever drives <paramref name="slot"/>, from the match (a network match's roster), or null for the
    /// default. A side the computer plays is always <c>BOT n</c>, so a player who left shows as the bot that took over.
    /// </summary>
    public void SetPlayerName(int slot, string? name)
    {
        if (slot < 0)
            return;
        if (_playerNames.Length <= slot)
            Array.Resize(ref _playerNames, slot + 1);
        _playerNames[slot] = string.IsNullOrWhiteSpace(name) ? null : name;
        // A plate already made (a ghost too) takes the new name at once.
        if (slot < _plates.Length && _plates[slot].Active)
            _plates[slot].Name = NameOf(slot, _plates[slot].Bot, _plates[slot].Name);
    }

    /// <summary>
    /// Updates every tank's plate after a simulation step. <paramref name="vision"/> is the player's (null: no fog, all
    /// is seen).
    /// </summary>
    public void CaptureStep(World world, int localSlot, VisionMap? vision)
    {
        var tanks = world.TankList.Tanks;
        if (_plates.Length != tanks.Count)
            Array.Resize(ref _plates, tanks.Count);
        Tank? player = localSlot >= 0 && localSlot < tanks.Count ? tanks[localSlot] : null;
        for (int slot = 0; slot < tanks.Count; slot++)
        {
            Tank tank = tanks[slot];
            bool rival = player != null && player.IsRivalOf(tank);
            Observe(slot, rival && !tank.IsDead, rival && !tank.IsDead && Sight.Sees(vision, tank.Position), tank.Position,
                tank.Color, tank.Base?.IsBotControlled ?? false);
        }
    }

    /// <summary>
    /// One tank's plate after a step. <paramref name="shown"/>: it is a living enemy tank (otherwise it has no plate);
    /// <paramref name="inSight"/>: the player's side sees it at <paramref name="cell"/>.
    /// </summary>
    private void Observe(int slot, bool shown, bool inSight, Position cell, int color, bool botControlled)
    {
        ref Plate plate = ref _plates[slot];
        if (!shown)
        {
            plate = default;
            return;
        }
        if (inSight)
        {
            plate.Active = true;
            plate.InSight = true;
            plate.Cell = cell;
            plate.StepsLost = 0;
            plate.Color = color;
            plate.Bot = botControlled;
            plate.Name = NameOf(slot, botControlled, plate.Name);
            return;
        }
        if (!plate.Active)
            return;
        plate.InSight = false;
        plate.StepsLost++;
        if (plate.StepsLost > FadeSteps)
            plate = default;
    }

    private string NameOf(int slot, bool botControlled, string? current)
    {
        string? player = botControlled || slot >= _playerNames.Length ? null : _playerNames[slot];
        string name = player != null ? Shorten(player.Trim().ToUpperInvariant()) : DefaultName(slot, botControlled);
        // Keep the string a step already made, so a plate allocates only when its name changes.
        return name == current ? current : name;
    }

    private static string Shorten(string name) => name.Length <= MaxNameLength ? name : name[..MaxNameLength];

    /// <summary>
    /// How bright a plate is: 1 over a tank in sight; once lost, from <see cref="DesktopScreenTweaks.TankPlateGhostOpacity"/>
    /// down to 0 over <see cref="FadeSteps"/>, <paramref name="alpha"/> of the way through the current step.
    /// </summary>
    public static float Opacity(in Plate plate, float alpha)
    {
        if (!plate.Active)
            return 0f;
        if (plate.InSight)
            return 1f;
        float lost = plate.StepsLost - 1 + Math.Clamp(alpha, 0f, 1f);
        return DesktopScreenTweaks.TankPlateGhostOpacity * Math.Clamp(1f - lost / FadeSteps, 0f, 1f);
    }

    /// <summary>
    /// The plates, over the other entities and under the HUD. A plate in sight moves with its tank as drawn
    /// (<paramref name="motion"/>); a lost one stands where its tank was last seen. <paramref name="cellSize"/> is a
    /// cell's displayed size in UI units: zoomed out, the plates fade like the charge labels (<see cref="PowerLabels.ZoomFade"/>).
    /// </summary>
    public void Emit(EntityQuads quads, EntityMotion motion, float cellSize)
    {
        float zoom = PowerLabels.ZoomFade(cellSize);
        if (zoom <= 0f)
            return;
        float scale = quads.View.PixelScale;
        // Keep each dot at least one render pixel wide.
        float dot = MathF.Max(DotCells, 1f / scale);
        float textHeight = GlyphText.GlyphHeight * dot;
        float rule = dot;
        int tankCount = motion.Tanks.Length;
        for (int slot = 0; slot < _plates.Length; slot++)
        {
            ref readonly Plate plate = ref _plates[slot];
            float opacity = Opacity(plate, quads.Alpha) * zoom;
            if (opacity <= 0f)
                continue;

            var at = (PositionF)plate.Cell;
            PositionF drift = default;
            if (plate.InSight && slot < tankCount)
                (at, drift) = motion.TankMotion(slot);
            float x = EntityQuads.Centre(at.X);
            float y = EntityQuads.Centre(at.Y) - LiftCells;
            float half = GlyphText.Width(plate.Name, dot) * 0.5f + PadCells;
            float top = -(PadCells + textHeight + dot + rule + PadCells);
            if (quads.IsOffScreen(quads.ScreenX(at.X), quads.ScreenY(at.Y - LiftCells), MathF.Max(half, -top) * scale + 8f * scale))
                continue;

            Color color = TankSprites.GetPixelColor(1, plate.Color);
            AddRect(quads, x, y, -half, half, top, 0f, Backing, BackingOpacity * opacity, drift);
            GlyphText.AddLine(quads, x, y, top + PadCells, plate.Name, dot, color, opacity, drift);
            AddRect(quads, x, y, -half + PadCells, half - PadCells, -PadCells - rule, -PadCells, color, opacity, drift);
        }
    }

    /// <summary>A solid rectangle (a glyph quad with no glyph) in the plate's local cells.</summary>
    private static void AddRect(
        EntityQuads quads, float x, float y, float left, float right, float top, float bottom, Color color, float alpha,
        PositionF motion) =>
        quads.Add(x, y, 1f, 0f, left, right, top, bottom, color, EntityShape.KindPowerLabel, 0f, 0f, 0f, alpha, motion);
}
