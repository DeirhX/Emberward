namespace Emberward.Desktop.Gui;

using System.Globalization;
using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Match;

/// <summary>Where a timed match's clock stands, as the HUD shows and sounds it.</summary>
public enum ClockPhase
{
    /// <summary>More than a minute left.</summary>
    Running,

    /// <summary>A minute or less left: the clock is drawn urgent.</summary>
    LastMinute,

    /// <summary><see cref="Scoreboards.CountdownSeconds"/> or fewer left: each second ticks.</summary>
    LastSeconds,

    /// <summary>Time is up and the top score is tied: the next kill that leaves one tank ahead ends the match.</summary>
    SuddenDeath,

    /// <summary>The match is over: the board and the clock stand as they ended.</summary>
    Final,
}

/// <summary>One tank's line on the board.</summary>
/// <param name="Rank">Its place, from 1; tanks level on kills share one.</param>
/// <param name="Slot">Its slot, which is its colour.</param>
/// <param name="Name">What to call it: the player's name in a network match, <c>YOU</c> or <c>BOT n</c> in a local one.</param>
/// <param name="Color">Its tank's colour (<see cref="HudPalette.TankColor"/>).</param>
/// <param name="IsLocal">The line of the player at this screen.</param>
/// <param name="IsLeader">It leads alone, and so wins if the clock runs out now.</param>
public readonly record struct ScoreRow(
    int Rank, int Slot, string Name, uint Color, int Kills, int Deaths, bool IsLocal, bool IsLeader)
{
    /// <summary>The colour as CSS <c>#rrggbb</c>, for the browser's page.</summary>
    public string Hex => HudPalette.Hex(Color);

    /// <summary>The place as a word: <c>1st</c>, <c>2nd</c>, <c>3rd</c>, <c>4th</c>.</summary>
    public string Place => Scoreboards.Ordinal(Rank);
}

/// <summary>
/// The board and the clock of a timed match (DESIGN.md §6.8) as the HUD shows them, shared by the desktop (ImGui) and
/// the browser (DOM) so that both say the same. Output only: everything comes from <see cref="World.CopyStandings"/>,
/// <see cref="World.MatchClock"/> and <see cref="World.ScoreLeader"/>; this words it and decides nothing.
/// </summary>
/// <param name="Rows">Every tank, in rank order.</param>
/// <param name="Clock">
/// Time left as <c>m:ss</c> (<c>8:00</c>, <c>0:09</c>); in sudden death the time played past the limit as
/// <c>+m:ss</c>; once the match is over, where it stopped.
/// </param>
/// <param name="Caption">The word over the clock: <c>TIME</c>, <c>LAST MINUTE</c>, <c>SUDDEN DEATH</c>, <c>FINAL</c>.</param>
public sealed record Scoreboard(IReadOnlyList<ScoreRow> Rows, string Clock, ClockPhase Phase, string Caption)
{
    /// <summary>The local player's line, or null when the player has no tank on the board.</summary>
    public ScoreRow? Local
    {
        get
        {
            foreach (var row in Rows)
                if (row.IsLocal)
                    return row;
            return null;
        }
    }

    /// <summary>The line of the tank that leads alone, or null while the top score is tied.</summary>
    public ScoreRow? Leader
    {
        get
        {
            foreach (var row in Rows)
                if (row.IsLeader)
                    return row;
            return null;
        }
    }

    /// <summary>The clock is drawn in the warning colour from the last minute on.</summary>
    public bool IsUrgent => Phase is ClockPhase.LastMinute or ClockPhase.LastSeconds or ClockPhase.SuddenDeath;

    /// <summary>The clock's colour: plain, amber in the last minute, red in the last seconds and in sudden death.</summary>
    public uint ClockColor => Phase switch
    {
        ClockPhase.LastMinute => HudPalette.Warn,
        ClockPhase.LastSeconds or ClockPhase.SuddenDeath => HudPalette.Danger,
        _ => HudPalette.Text,
    };

    /// <summary>The local player's result once the match is over: <c>2nd place - 5 kills, 3 deaths</c>; null with no line.</summary>
    public string? OwnResult => Local is { } row
        ? string.Create(CultureInfo.InvariantCulture,
            $"{row.Place} place - {Scoreboards.Count(row.Kills, "kill")}, {Scoreboards.Count(row.Deaths, "death")}")
        : null;
}

public static class Scoreboards
{
    /// <summary>What a timed match asks of the player, in a line; the HUD shows it where a core race shows its goal.</summary>
    public const string Objective = "GOAL  most kills when the clock runs out";

    /// <summary>From this many seconds left the clock is in <see cref="ClockPhase.LastSeconds"/>.</summary>
    public const int CountdownSeconds = 10;

    /// <summary>From this many seconds left the clock is in <see cref="ClockPhase.LastMinute"/>.</summary>
    public const int LastMinuteSeconds = 60;

    private const int MaxRows = 8;

    /// <summary>
    /// The board of <paramref name="world"/>, or null when its rules set no time limit.
    /// </summary>
    /// <param name="localSlot">The slot of the player at this screen; -1 for a spectator.</param>
    /// <param name="names">What to call the tank in a slot (<see cref="SlotNames"/>).</param>
    public static Scoreboard? Of(World world, int localSlot, Func<int, string> names)
    {
        MatchClock clock = world.MatchClock;
        if (!clock.Active)
            return null;
        Span<TankScore> standings = stackalloc TankScore[MaxRows];
        int count = world.CopyStandings(standings);
        int leader = world.ScoreLeader?.Color ?? -1;
        var rows = new ScoreRow[count];
        for (int i = 0; i < count; i++)
        {
            TankScore s = standings[i];
            rows[i] = new ScoreRow(s.Rank, s.Color, names(s.Color), HudPalette.TankColor(s.Color), s.Kills, s.Deaths,
                IsLocal: s.Color == localSlot, IsLeader: s.Color == leader);
        }
        ClockPhase phase = PhaseOf(clock, world.IsMatchOver);
        return new Scoreboard(rows, ClockText(clock), phase, Caption(phase));
    }

    /// <summary>Where <paramref name="clock"/> stands; <paramref name="matchOver"/> is <see cref="World.IsMatchOver"/>.</summary>
    public static ClockPhase PhaseOf(in MatchClock clock, bool matchOver) =>
        matchOver ? ClockPhase.Final
        : clock.IsSuddenDeath ? ClockPhase.SuddenDeath
        : clock.RemainingSeconds <= CountdownSeconds ? ClockPhase.LastSeconds
        : clock.RemainingSeconds <= LastMinuteSeconds ? ClockPhase.LastMinute
        : ClockPhase.Running;

    /// <summary>
    /// Time left as <c>m:ss</c>, rounded up so that <c>0:00</c> shows only when time is up; past the limit, the
    /// overtime played as <c>+m:ss</c>.
    /// </summary>
    public static string ClockText(in MatchClock clock) =>
        clock.OvertimeTicks > 0
            ? "+" + MinutesSeconds(clock.OvertimeTicks / Tweaks.Perf.TargetFps)
            : MinutesSeconds(clock.RemainingSeconds);

    /// <summary><paramref name="seconds"/> as <c>m:ss</c>.</summary>
    public static string MinutesSeconds(int seconds)
    {
        seconds = Math.Max(0, seconds);
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }

    public static string Caption(ClockPhase phase) => phase switch
    {
        ClockPhase.LastMinute or ClockPhase.LastSeconds => "LAST MINUTE",
        ClockPhase.SuddenDeath => "SUDDEN DEATH",
        ClockPhase.Final => "FINAL",
        _ => "TIME",
    };

    /// <summary><c>1st</c>, <c>2nd</c>, <c>3rd</c>, <c>4th</c>...</summary>
    public static string Ordinal(int rank) => rank switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => rank.ToString(CultureInfo.InvariantCulture) + "th",
    };

    /// <summary><c>1 kill</c>, <c>5 kills</c>.</summary>
    public static string Count(int count, string noun) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {noun}{(count == 1 ? "" : "s")}");

    /// <summary>Why the player won a match the score decided, in a line.</summary>
    public static string VictoryLine(Scoreboard board) => board.Local is { } row
        ? "Most kills when the clock ran out: " + Count(row.Kills, "kill")
        : "Most kills when the clock ran out";

    /// <summary>Why the player lost a match the score decided, in a line that names the winner.</summary>
    /// <param name="winnerName">What to call the winner; null for "another tank".</param>
    public static string DefeatLine(Scoreboard board, string? winnerName) => board.Leader is { } row
        ? $"{winnerName ?? row.Name} won with {Count(row.Kills, "kill")}"
        : $"{winnerName ?? "Another tank"} had the most kills";
}

/// <summary>What the board, the toasts and the name tags call the tank in a slot.</summary>
public static class SlotNames
{
    /// <summary>The player's own tank in a local match.</summary>
    public const string You = "YOU";

    /// <summary>A bot in a local match: <c>BOT 2</c> for slot 1.</summary>
    public static string LocalBot(int slot) => string.Create(CultureInfo.InvariantCulture, $"BOT {slot + 1}");

    /// <summary>A local match's names: <see cref="You"/> for <paramref name="localSlot"/>, a numbered bot for the rest.</summary>
    public static Func<int, string> Local(int localSlot) => slot => slot == localSlot ? You : LocalBot(slot);
}
