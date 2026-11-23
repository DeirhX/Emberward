namespace Emberward.Browser.Client;

using Emberward.Core;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Match;
using Emberward.Core.Types;
using Emberward.Desktop.Gui;
using Emberward.Desktop.Presentation;

/// <summary>
/// A timed match as the page shows it (DESIGN.md §6.8): the clock, the board, the powerup drops, the player's running
/// powerups and, once the score has decided the match, its last lines. Every word and colour is the desktop's
/// (<see cref="Scoreboard"/>, <see cref="DropBoard"/>); colours are #rrggbb.
/// </summary>
/// <param name="Clock">Time left as <c>7:15</c>; <c>+0:12</c> in sudden death.</param>
/// <param name="Caption">The word over the clock: <c>TIME</c>, <c>LAST MINUTE</c>, <c>SUDDEN DEATH</c>, <c>FINAL</c>.</param>
/// <param name="Phase">A <see cref="ClockPhase"/> name.</param>
/// <param name="Urgent">From the last minute on, until the match is over (<see cref="Scoreboard.IsUrgent"/>).</param>
/// <param name="Objective">What the match asks of the player, in a line (<see cref="Scoreboards.Objective"/>).</param>
/// <param name="Rows">Every tank, in rank order.</param>
/// <param name="Drops">The drop lines, the next drop first; empty once the match is over.</param>
/// <param name="Effects">The powerups running on the player's tank; empty while it is dead.</param>
/// <param name="Over">The match-over lines of a match the score decided; null while it plays.</param>
public sealed record ArcadeStatus(
    string Clock, string Caption, string Phase, string ClockColor, bool Urgent, string Objective,
    ScoreLine[] Rows, DropStatus[] Drops, EffectStatus[] Effects, MatchOverStatus? Over);

/// <summary>One tank's line on the board (<see cref="ScoreRow"/>).</summary>
/// <param name="Local">The line of the player at this page.</param>
/// <param name="Leader">It leads alone.</param>
public sealed record ScoreLine(int Rank, int Slot, string Name, string Color, int Kills, int Deaths, bool Local, bool Leader);

/// <summary>One line of the drop board (<see cref="DropLine"/>).</summary>
/// <param name="State">A <see cref="DropLineState"/> name.</param>
/// <param name="Mark">The kind's mark, which stands for it wherever its colour does.</param>
/// <param name="Name">The kind's name on the HUD: <c>OVERCHARGE</c>.</param>
/// <param name="When"><c>in 0:32</c>, <c>LANDED 38s</c>, <c>TAKEN</c>, <c>GONE</c>.</param>
/// <param name="Detail">How far it is (<c>120 m</c>), or who took it; may be empty.</param>
/// <param name="Urgent">About to land or to run out: the line blinks.</param>
/// <param name="Pointed">The page points at <paramref name="Site"/> while it is off screen (<see cref="DropLine.IsPointedAt"/>).</param>
/// <param name="Site">The cell it lands or lies on.</param>
/// <param name="PointerText">What the pointer at the edge of the view says (<see cref="DropLine.PointerText"/>).</param>
public sealed record DropStatus(
    string State, string Mark, string Name, string When, string Detail, string Color, bool Urgent, bool Pointed,
    Position Site, string PointerText);

/// <summary>One running powerup of the player's tank (<see cref="EffectTag"/>).</summary>
/// <param name="Kind">A <c>PowerupKind</c> name, which tells one tag from another.</param>
/// <param name="Text">Mark, name and seconds left: <c>B BULWARK 14s</c>.</param>
/// <param name="Fraction">The share of its time that is left, 1 down to 0.</param>
/// <param name="Blink">In its last seconds.</param>
public sealed record EffectStatus(string Kind, string Text, float Fraction, string Color, bool Blink);

/// <summary>The last lines of a match the score decided, as the desktop's <c>MatchOverlay</c> writes them.</summary>
/// <param name="Title"><c>VICTORY</c> or <c>DEFEAT</c>.</param>
/// <param name="Reason">Why, in a line: the player's kills, or who won and with how many.</param>
/// <param name="Own">The player's own place, kills and deaths; null with no line on the board.</param>
public sealed record MatchOverStatus(bool Won, string Title, string Reason, string? Own);

/// <summary>Builds <see cref="ArcadeStatus"/>. It words what Core and the shared HUD types say and decides nothing.</summary>
public static class BrowserArcade
{
    /// <summary>
    /// What the page shows of a timed match, or null when <paramref name="world"/> has no clock: a standard match
    /// sends nothing and pays nothing.
    /// </summary>
    /// <param name="player">The tank of the player at this page.</param>
    /// <param name="board">The match's board, kept between frames so that it is built again only when what it shows changed.</param>
    /// <param name="drops">The match's drop board, which has heard every step (<see cref="DropBoard.AfterStep"/>).</param>
    /// <param name="names">What to call the tank in a slot (<see cref="BrowserSession.Names"/>).</param>
    public static ArcadeStatus? Capture(World world, Tank player, ScoreboardCache board, DropBoard drops, Func<int, string> names)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(drops);
        if (board.Get(world, player.Color, names) is not { } now)
            return null;

        var rows = new ScoreLine[now.Rows.Count];
        for (int i = 0; i < rows.Length; i++)
        {
            ScoreRow row = now.Rows[i];
            rows[i] = new ScoreLine(row.Rank, row.Slot, row.Name, row.Hex, row.Kills, row.Deaths, row.IsLocal, row.IsLeader);
        }
        return new ArcadeStatus(now.Clock, now.Caption, now.Phase.ToString(), HudPalette.Hex(now.ClockColor),
            now.IsUrgent, Scoreboards.Objective, rows,
            // Over, the match has its final board to show; what drops next is no longer anyone's business.
            world.IsMatchOver ? [] : Drops(drops.Lines(world, player, names)),
            player.IsDead ? [] : Effects(drops.EffectsNow(world.PowerupsOf(player))),
            Over(world, player, now, names));
    }

    private static DropStatus[] Drops(IReadOnlyList<DropLine> lines)
    {
        if (lines.Count == 0)
            return [];
        var drops = new DropStatus[lines.Count];
        for (int i = 0; i < drops.Length; i++)
        {
            DropLine line = lines[i];
            var kind = PowerupPresentations.Get(line.Kind);
            drops[i] = new DropStatus(line.State.ToString(), kind.Mark.ToString(), kind.Tag,
                line.Left.Length > 0 ? line.When + " " + line.Left : line.When, line.Detail, line.Hex, line.Urgent,
                line.IsPointedAt, line.Site, line.PointerText);
        }
        return drops;
    }

    private static EffectStatus[] Effects(IReadOnlyList<EffectTag> tags)
    {
        if (tags.Count == 0)
            return [];
        var effects = new EffectStatus[tags.Count];
        for (int i = 0; i < effects.Length; i++)
            effects[i] = new EffectStatus(tags[i].Kind.ToString(), tags[i].Text, tags[i].Fraction, tags[i].Hex, tags[i].Blink);
        return effects;
    }

    private static MatchOverStatus? Over(World world, Tank player, Scoreboard board, Func<int, string> names)
    {
        MatchOutcome outcome = world.MatchOutcomeFor(player);
        if (outcome == MatchOutcome.InProgress || world.MatchWinner is not { Reason: MatchWinReason.TopScore } winner)
            return null;
        bool won = outcome == MatchOutcome.Victory;
        return new MatchOverStatus(won, won ? "VICTORY" : "DEFEAT",
            won ? Scoreboards.VictoryLine(board) : Scoreboards.DefeatLine(board, names(winner.Tank.Color)),
            board.OwnResult);
    }
}
