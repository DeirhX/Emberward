namespace Emberward.Desktop.Gui;

using System.Globalization;
using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities.Powerups;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Rendering;
using Emberward.Core.Types;
using Emberward.Desktop.Presentation;

/// <summary>What a line of the drop board is about.</summary>
public enum DropLineState
{
    /// <summary>The next drop, still to land.</summary>
    Forecast,

    /// <summary>A drop on the ground, to be taken.</summary>
    Landed,

    /// <summary>A drop a tank took a moment ago.</summary>
    Taken,

    /// <summary>A drop nobody took, gone a moment ago.</summary>
    Gone,
}

/// <summary>One line of the drop board: what, when and how far.</summary>
/// <param name="Site">The cell it lands or lies on; where the marker and the arrow point.</param>
/// <param name="Tag">The kind, with its mark: <c>X OVERCHARGE</c>.</param>
/// <param name="When">
/// <c>in 0:32</c> for a forecast; <c>LANDED</c> for a drop on the ground; <c>TAKEN</c> and <c>GONE</c> for a moment
/// after one was taken or ran out.
/// </param>
/// <param name="Detail">
/// How far from the player's tank, in cells: <c>120 m</c>. On a drop that was taken, who took it. Empty on one that
/// ran out.
/// </param>
/// <param name="Left">On a drop on the ground, how long it still lies: <c>52s</c>; otherwise empty.</param>
/// <param name="Color">The kind's colour (<see cref="PowerupPresentation.Color"/>).</param>
/// <param name="Urgent">It lands within <see cref="DropBoard.UrgentSeconds"/>, or runs out within them: the line blinks.</param>
public readonly record struct DropLine(
    DropLineState State, PowerupKind Kind, Position Site, string Tag, string When, string Detail, string Left,
    uint Color, bool Urgent)
{
    /// <summary>The colour as CSS <c>#rrggbb</c>, for the browser's page.</summary>
    public string Hex => HudPalette.Hex(Color);

    /// <summary>The line's words in one string: <c>X OVERCHARGE  in 0:32  120 m</c>.</summary>
    public string Text => Detail.Length > 0 ? $"{Tag}  {When}  {Detail}" : $"{Tag}  {When}";

    /// <summary>
    /// What the arrow at the edge of the view says, which is short and can stand apart from its badge: the kind's
    /// mark, when it lands and how far, <c>X  in 0:32  120 m</c>; for a drop on the ground how long it still lies,
    /// <c>B  52s left  45 m</c>.
    /// </summary>
    public string PointerText => State == DropLineState.Landed
        ? $"{PowerupPresentations.Get(Kind).Mark}  {Left} left  {Detail}"
        : $"{PowerupPresentations.Get(Kind).Mark}  {When}  {Detail}";

    /// <summary>Whether the marker on the ground and the arrow at the edge of the view point to it.</summary>
    public bool IsPointedAt => State is DropLineState.Forecast or DropLineState.Landed;
}

/// <summary>One running powerup of the player's tank, as a tag with its countdown.</summary>
/// <param name="Tag">The kind, with its mark: <c>B BULWARK</c>.</param>
/// <param name="Seconds">Time left, rounded up: <c>14s</c>.</param>
/// <param name="Fraction">The share of its time that is left, 1 down to 0, for a bar.</param>
/// <param name="Blink">In its last <see cref="DropBoard.UrgentSeconds"/>.</param>
public readonly record struct EffectTag(PowerupKind Kind, string Tag, string Seconds, float Fraction, uint Color, bool Blink)
{
    /// <summary>The colour as CSS <c>#rrggbb</c>, for the browser's page.</summary>
    public string Hex => HudPalette.Hex(Color);

    /// <summary>Tag and countdown in one string: <c>B BULWARK 14s</c>.</summary>
    public string Text => Tag + " " + Seconds;
}

/// <summary>
/// What the HUD says of the powerup drops (DESIGN.md §6.8), shared by the desktop (ImGui) and the browser (DOM) so
/// that both say the same: the next drop (what, when, how far), the drops on the ground, and for a moment the one
/// that was just taken or ran out. Output only: what and where come from <see cref="World.PowerupForecast"/> and
/// <see cref="World.CopyPowerupRenderStates"/>, the "just taken" from the step's events. It keeps that last notice
/// and the lines it gave last, which it builds again only when what they show changes; it decides nothing the
/// simulation owns.
/// </summary>
public sealed class DropBoard
{
    /// <summary>A forecast this close to landing, a drop this close to running out and an effect this close to its end blink.</summary>
    public const int UrgentSeconds = 10;

    /// <summary>How long a "TAKEN" or "GONE" line stays, in seconds.</summary>
    public const int NoticeSeconds = 5;

    private const int MaxDrops = 8;

    private World? _world;
    private int _noticeUntil;
    private DropLineState _noticeState;
    private PowerupKind _noticeKind;
    private Position _noticeSite;
    private int _noticeTaker = -1;
    private readonly List<DropLine> _lines = [];

    // What _lines show, line by line, the name in a "TAKEN" line and the world they are of: Lines builds the lines
    // again only when one of these changes (a second of a countdown, a metre of a distance, a drop, a name).
    private readonly List<LineKey> _keys = [];
    private string? _keyedName;
    private World? _keyedWorld;
    private readonly List<EffectTag> _effects = [];

    /// <summary>Call after each simulation step, while <see cref="World.StepEvents"/> still holds that step's events.</summary>
    public void AfterStep(World world)
    {
        Follow(world);
        foreach (ref readonly var e in world.StepEvents)
        {
            if (e.Kind is not (StepEventKind.PowerupCollected or StepEventKind.PowerupExpired))
                continue;
            _noticeState = e.Kind == StepEventKind.PowerupCollected ? DropLineState.Taken : DropLineState.Gone;
            _noticeKind = (PowerupKind)e.Count;
            _noticeSite = e.Position;
            _noticeTaker = e.Kind == StepEventKind.PowerupCollected ? e.Color : -1;
            _noticeUntil = world.AdvanceCount + NoticeSeconds * Tweaks.Perf.TargetFps;
        }
    }

    /// <summary>
    /// The board's lines now: the forecast first, then each drop on the ground (the one that runs out first on top),
    /// then the last notice while it lasts. Empty when the rules run no drops. The list is reused: read it before the
    /// next call. A host may call this every frame: while nothing it shows has changed (the second of a countdown, the
    /// metre of a distance, a drop, the name of who took one) the lines are not built again.
    /// </summary>
    /// <param name="player">The tank distances are measured from.</param>
    /// <param name="names">What to call the tank in a slot (<see cref="SlotNames"/>).</param>
    public IReadOnlyList<DropLine> Lines(World world, Tank player, Func<int, string> names)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(names);
        Follow(world);
        PowerupForecast forecast = world.PowerupForecast;
        Span<PowerupRenderState> drops = stackalloc PowerupRenderState[MaxDrops];
        Span<LineKey> keys = stackalloc LineKey[MaxDrops + 2];
        int count = 0, lines = 0;
        string? name = null;
        if (forecast.Active)
        {
            Position from = player.Position;
            keys[lines++] = new LineKey(DropLineState.Forecast, forecast.Kind, forecast.Site,
                CeilSeconds(forecast.TicksUntilLanding), DistanceCells(from, forecast.Site));
            count = world.CopyPowerupRenderStates(drops);
            // The one that runs out first on top. Insertion sort: there are never more than a few.
            for (int i = 1; i < count; i++)
                for (int j = i; j > 0 && drops[j].TicksUntilExpiry < drops[j - 1].TicksUntilExpiry; j--)
                    (drops[j], drops[j - 1]) = (drops[j - 1], drops[j]);
            for (int i = 0; i < count; i++)
                keys[lines++] = new LineKey(DropLineState.Landed, drops[i].Kind, drops[i].Cell,
                    CeilSeconds(drops[i].TicksUntilExpiry), DistanceCells(from, drops[i].Cell));
            if (world.AdvanceCount < _noticeUntil)
            {
                keys[lines++] = new LineKey(_noticeState, _noticeKind, _noticeSite, 0, 0);
                name = _noticeState == DropLineState.Taken ? names(_noticeTaker) : null;
            }
        }
        if (Unchanged(world, keys[..lines], name))
            return _lines;

        _lines.Clear();
        _keys.Clear();
        for (int i = 0; i < lines; i++)
            _keys.Add(keys[i]);
        (_keyedWorld, _keyedName) = (world, name);
        if (!forecast.Active)
            return _lines;
        _lines.Add(ForecastLine(forecast, player.Position));
        for (int i = 0; i < count; i++)
            _lines.Add(LandedLine(drops[i], player.Position));
        if (lines > count + 1)
            _lines.Add(NoticeLine(names));
        return _lines;
    }

    /// <summary>
    /// The running timed powerups of a tank, in kind order, as <see cref="Effects"/> gives them, in a list this board
    /// reuses: read it before the next call. For a host that shows them every frame.
    /// </summary>
    public IReadOnlyList<EffectTag> EffectsNow(in TankPowerupView view)
    {
        _effects.Clear();
        if (view.Active && view.Any)
            AddEffects(view, _effects);
        return _effects;
    }

    private bool Unchanged(World world, ReadOnlySpan<LineKey> keys, string? name)
    {
        if (!ReferenceEquals(world, _keyedWorld) || keys.Length != _keys.Count || !string.Equals(name, _keyedName, StringComparison.Ordinal))
            return false;
        for (int i = 0; i < keys.Length; i++)
            if (keys[i] != _keys[i])
                return false;
        return true;
    }

    /// <summary>The line of the next drop: <c>X OVERCHARGE  in 0:32  120 m</c>.</summary>
    public static DropLine ForecastLine(in PowerupForecast forecast, Position from)
    {
        var kind = PowerupPresentations.Get(forecast.Kind);
        int seconds = CeilSeconds(forecast.TicksUntilLanding);
        return new DropLine(DropLineState.Forecast, forecast.Kind, forecast.Site, kind.MarkedTag,
            "in " + Scoreboards.MinutesSeconds(seconds), Distance(from, forecast.Site), "", kind.Color,
            Urgent: seconds <= UrgentSeconds);
    }

    /// <summary>The line of a drop on the ground: <c>B BULWARK  LANDED  45 m</c>, with <c>52s</c> left.</summary>
    public static DropLine LandedLine(in PowerupRenderState drop, Position from)
    {
        var kind = PowerupPresentations.Get(drop.Kind);
        int seconds = CeilSeconds(drop.TicksUntilExpiry);
        return new DropLine(DropLineState.Landed, drop.Kind, drop.Cell, kind.MarkedTag, "LANDED",
            Distance(from, drop.Cell), Seconds(seconds), kind.Color, Urgent: seconds <= UrgentSeconds);
    }

    /// <summary>The running timed powerups of a tank, in kind order; empty when none runs.</summary>
    public static IReadOnlyList<EffectTag> Effects(in TankPowerupView view)
    {
        if (!view.Active || !view.Any)
            return [];
        var tags = new List<EffectTag>(PowerupKinds.Count);
        AddEffects(view, tags);
        return tags;
    }

    private static void AddEffects(in TankPowerupView view, List<EffectTag> tags)
    {
        foreach (var kind in PowerupPresentations.All)
        {
            if (!view.IsRunning(kind.Kind))
                continue;
            int seconds = CeilSeconds(view.TicksLeft(kind.Kind));
            tags.Add(new EffectTag(kind.Kind, kind.MarkedTag, Seconds(seconds), view.FractionLeft(kind.Kind), kind.Color,
                Blink: seconds <= UrgentSeconds / 2));
        }
    }

    /// <summary>The distance between two cells as the HUD writes it: <c>120 m</c> (a cell is a metre).</summary>
    public static string Distance(Position from, Position to) => HudNumbers.Metres(DistanceCells(from, to));

    private static int DistanceCells(Position from, Position to)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        return (int)MathF.Round(MathF.Sqrt(dx * dx + dy * dy));
    }

    private DropLine NoticeLine(Func<int, string> names)
    {
        var kind = PowerupPresentations.Get(_noticeKind);
        return _noticeState == DropLineState.Taken
            ? new DropLine(DropLineState.Taken, _noticeKind, _noticeSite, kind.MarkedTag, "TAKEN",
                "by " + names(_noticeTaker), "", kind.Color, Urgent: false)
            : new DropLine(DropLineState.Gone, _noticeKind, _noticeSite, kind.MarkedTag, "GONE", "", "", kind.Color,
                Urgent: false);
    }

    // A new match starts the board over: a notice of the last one says nothing about this one.
    private void Follow(World world)
    {
        if (ReferenceEquals(world, _world))
            return;
        _world = world;
        _noticeUntil = 0;
    }

    private static int CeilSeconds(int ticks) => (Math.Max(0, ticks) + Tweaks.Perf.TargetFps - 1) / Tweaks.Perf.TargetFps;

    private static string Seconds(int seconds) => HudNumbers.Seconds(seconds);

    /// <summary>What a line shows: its state, kind and site, the seconds of its countdown and its distance in cells.</summary>
    private readonly record struct LineKey(DropLineState State, PowerupKind Kind, Position Site, int Seconds, int Metres);
}
