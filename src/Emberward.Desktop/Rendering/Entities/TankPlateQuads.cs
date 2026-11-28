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
/// A name plate over each enemy tank the player's side sees, in the tank's colour; with no tank of its own (a spectator)
/// the screen labels every living tank in sight. When the tank goes out of sight its plate stays where it was last drawn
/// and fades out over <see cref="DesktopScreenTweaks.TankPlateFadeSeconds"/>; seen again, anywhere, the plate follows
/// it again at once. A dead tank's plate goes. Output only: one record per tank,
/// kept up from Core's answers once a step (<see cref="Tank.IsRivalOf"/>, <see cref="Tank.IsDead"/> and the tank
/// sprites' fog rule, <see cref="Sight"/>), so the cost follows the number of tanks and never the map.
/// </summary>
public sealed class TankPlateQuads
{
    /// <summary>Longest name a plate shows; a longer one is cut.</summary>
    public const int MaxNameLength = 16;

    /// <summary>A plate dot, in cells, at the usual zoom (zoomed out it grows, <see cref="DotSize"/>).</summary>
    public const float DotCells = 0.25f;

    /// <summary>The room around the name on its backing, in dots.</summary>
    private const float PadDots = 2f;

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

        /// <summary>
        /// Where a lost plate stands: where the tank was last drawn, which the plate was following, so the plate does not
        /// jump from the tank as drawn (between steps, and led a step ahead) to the cell it was last seen at.
        /// </summary>
        public PositionF Anchor;

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
    /// the computer plays (<see cref="SlotNames.LocalBot"/>, as the board and the toasts say), <c>PLAYER n</c>
    /// otherwise, n counting slots from 1.
    /// </summary>
    public static string DefaultName(int slot, bool botControlled) =>
        botControlled ? SlotNames.LocalBot(slot) : string.Create(CultureInfo.InvariantCulture, $"PLAYER {slot + 1}");

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
    /// is seen). With no tank at <paramref name="localSlot"/> (a spectator, as the network name tags once allowed) every
    /// living tank is labelled, by the same sight rule. Once a timed match is over the final standings have the view, and
    /// no tank has a plate. <paramref name="motion"/>, already given this step's tanks, says where a tank was last drawn,
    /// which is where its plate stays once the tank is lost (without it, the last-seen cell).
    /// </summary>
    public void CaptureStep(World world, int localSlot, VisionMap? vision, EntityMotion? motion = null)
    {
        var tanks = world.TankList.Tanks;
        if (_plates.Length != tanks.Count)
            Array.Resize(ref _plates, tanks.Count);
        bool standings = world.IsMatchOver && world.MatchClock.Active;
        Tank? player = localSlot >= 0 && localSlot < tanks.Count ? tanks[localSlot] : null;
        for (int slot = 0; slot < tanks.Count; slot++)
        {
            Tank tank = tanks[slot];
            bool labelled = !standings && !tank.IsDead && (player == null || player.IsRivalOf(tank));
            Observe(slot, labelled, labelled && Sight.Sees(vision, tank.Position), tank.Position,
                tank.Color, tank.Base?.IsBotControlled ?? false, motion);
        }
    }

    /// <summary>
    /// One tank's plate after a step. <paramref name="shown"/>: it is a living tank this screen labels (otherwise it has
    /// no plate); <paramref name="inSight"/>: the player's side sees it at <paramref name="cell"/>.
    /// </summary>
    private void Observe(int slot, bool shown, bool inSight, Position cell, int color, bool botControlled, EntityMotion? motion)
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
            plate.Anchor = (PositionF)cell;
            plate.StepsLost = 0;
            plate.Color = color;
            plate.Bot = botControlled;
            plate.Name = NameOf(slot, botControlled, plate.Name);
            return;
        }
        if (!plate.Active)
            return;
        if (plate.InSight)
            plate.Anchor = LastDrawn(slot, plate.Cell, motion);
        plate.InSight = false;
        plate.StepsLost++;
        if (plate.StepsLost > FadeSteps)
            plate = default;
    }

    /// <summary>
    /// Where tank <paramref name="slot"/>'s plate was last drawn, given the step it was lost in: the start of the tank's
    /// motion for this step is where the last frame drew it. A tank that moved further than a drive in that step
    /// (respawned or teleported, <see cref="EntityMotion.TeleportDistanceCells"/>) starts from where it is now, out of
    /// sight, so then the plate stands at <paramref name="lastSeen"/> rather than give the tank away.
    /// </summary>
    private static PositionF LastDrawn(int slot, Position lastSeen, EntityMotion? motion)
    {
        var seen = (PositionF)lastSeen;
        if (motion == null || slot >= motion.Tanks.Length)
            return seen;
        PositionF start = motion.TankMotion(slot).Start;
        float dx = start.X - seen.X, dy = start.Y - seen.Y;
        float reach = EntityMotion.TeleportDistanceCells;
        return dx * dx + dy * dy <= reach * reach ? start : seen;
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
    /// (<paramref name="motion"/>); a lost one stands where its tank was last drawn (<see cref="Plate.Anchor"/>).
    /// <paramref name="cellSize"/> is a cell's displayed size in UI units, or zero to hide the plates. Unlike the charge
    /// labels they do not fade zoomed out: their lettering keeps a readable size instead (<see cref="DotSize"/>).
    /// </summary>
    public void Emit(EntityQuads quads, EntityMotion motion, float cellSize)
    {
        if (!(cellSize > 0f) || !float.IsFinite(cellSize))
            return;
        float scale = quads.View.PixelScale;
        float dot = DotSize(cellSize, scale);
        float pad = PadDots * dot;
        float textHeight = GlyphText.GlyphHeight * dot;
        float rule = dot;
        int tankCount = motion.Tanks.Length;
        for (int slot = 0; slot < _plates.Length; slot++)
        {
            ref readonly Plate plate = ref _plates[slot];
            float opacity = Opacity(plate, quads.Alpha);
            if (opacity <= 0f)
                continue;

            PositionF at = plate.Anchor;
            PositionF drift = default;
            if (plate.InSight && slot < tankCount)
                (at, drift) = motion.TankMotion(slot);
            float x = EntityQuads.Centre(at.X);
            float y = EntityQuads.Centre(at.Y) - LiftCells;
            float half = GlyphText.Width(plate.Name, dot) * 0.5f + pad;
            float top = -(pad + textHeight + dot + rule + pad);
            if (quads.IsOffScreen(quads.ScreenX(at.X), quads.ScreenY(at.Y - LiftCells), MathF.Max(half, -top) * scale + 8f * scale))
                continue;

            Color color = TankSprites.GetPixelColor(1, plate.Color);
            AddRect(quads, x, y, -half, half, top, 0f, Backing, BackingOpacity * opacity, drift);
            GlyphText.AddLine(quads, x, y, top + pad, plate.Name, dot, color, opacity, drift);
            AddRect(quads, x, y, -half + pad, half - pad, -pad - rule, -pad, color, opacity, drift);
        }
    }

    /// <summary>
    /// A plate dot's size in cells when a cell is shown <paramref name="cellSize"/> UI units wide and
    /// <paramref name="pixelScale"/> render pixels wide: <see cref="DotCells"/>, but never under
    /// <see cref="DesktopScreenTweaks.TankPlateMinDotPoints"/> UI units (so zoomed out the lettering stops shrinking and
    /// stays readable, as the ImGui name tags did) nor under one render pixel.
    /// </summary>
    public static float DotSize(float cellSize, float pixelScale)
    {
        float dot = DotCells;
        if (cellSize > 0f && float.IsFinite(cellSize))
            dot = MathF.Max(dot, DesktopScreenTweaks.TankPlateMinDotPoints / cellSize);
        return pixelScale > 0f ? MathF.Max(dot, 1f / pixelScale) : dot;
    }

    /// <summary>A solid rectangle (a glyph quad with no glyph) in the plate's local cells.</summary>
    private static void AddRect(
        EntityQuads quads, float x, float y, float left, float right, float top, float bottom, Color color, float alpha,
        PositionF motion) =>
        quads.Add(x, y, 1f, 0f, left, right, top, bottom, color, EntityShape.KindPowerLabel, 0f, 0f, 0f, alpha, motion);
}
