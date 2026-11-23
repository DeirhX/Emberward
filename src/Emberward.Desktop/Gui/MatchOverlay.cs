namespace Emberward.Desktop.Gui;

using System.Numerics;
using ImGuiNET;
using Emberward.Core;
using Emberward.Core.Entities;
using Emberward.Core.Match;
using Emberward.Core.Entities.Tanks;
using static Emberward.Desktop.Gui.HudDraw;

/// <summary>
/// Death screen: what destroyed the tank and when it respawns; victory and defeat when a base falls or taps the core,
/// or when a timed match's clock runs out, which adds the player's own result and the final standings.
/// </summary>
public static class MatchOverlay
{
    /// <param name="canRestart">Whether Enter starts a new match once this one is over.</param>
    /// <param name="winnerName">What to call a winner that is not the player; null for "another tank".</param>
    /// <param name="board">The board of a timed match, or null.</param>
    /// <param name="top">Nothing is drawn above this line: the HUD has a plate at the top centre of the view down to it.</param>
    public static void Draw(
        (float x, float y, float w, float h) vp, Tank player, World world, bool canRestart = true, string? winnerName = null,
        Scoreboard? board = null, float top = 0f)
    {
        if (vp.w <= 0)
            return;

        MatchOutcome outcome = world.MatchOutcomeFor(player);

        var dl = ImGui.GetForegroundDrawList();
        float cx = vp.x + vp.w * 0.5f;
        float cy = MathF.Max(vp.y + vp.h * 0.24f, top + 40f);

        if (outcome != MatchOutcome.InProgress && board != null && world.MatchWinner?.Reason == MatchWinReason.TopScore)
        {
            DrawFinalScore(dl, vp, cx, cy, outcome == MatchOutcome.Victory, board, canRestart, winnerName);
            return;
        }

        if (outcome == MatchOutcome.Victory)
        {
            DrawCenteredText(dl, 44f, cx, cy - 30f, "VICTORY", 0xFF40FF80u);
            DrawCenteredText(dl, 20f, cx, cy + 20f, VictoryLine(world), 0xFFFFFFFFu);
            if (canRestart)
                DrawCenteredText(dl, 20f, cx, cy + 50f, "Press Enter to play again", 0xFFC0E0FFu);
            return;
        }

        if (outcome == MatchOutcome.Defeat)
        {
            dl.AddRectFilled(new Vector2(vp.x, vp.y), new Vector2(vp.x + vp.w, vp.y + vp.h), 0x70000000u);
            DrawCenteredText(dl, 44f, cx, cy - 30f, "DEFEAT", 0xFF4040FFu);
            DrawCenteredText(dl, 20f, cx, cy + 20f, DefeatLine(world, player, winnerName), 0xFFFFFFFFu);
            if (canRestart)
                DrawCenteredText(dl, 20f, cx, cy + 50f, "Press Enter to play again", 0xFFC0E0FFu);
            return;
        }

        if (!player.IsDead)
        {
            if (world.RecallOffered(player))
                DrawCenteredText(dl, 20f, cx, cy, "Stranded. Press X to drop cargo and respawn", 0xFFC0E0FFu);
            else if (world.ReserveOf(player) is var reserve && ReserveTags.BurnPrompt(reserve) is string burn)
            {
                // From the warning on the prompt asks for attention; before it, a quiet hint below the tank says what
                // X would do, so it never burns cargo unannounced.
                if (ReserveTags.BurnIsUrgent(reserve))
                    DrawCenteredText(dl, 20f, cx, cy, $"X: {burn}", 0xFFC0E0FFu);
                else
                    DrawCenteredText(dl, 15f, cx, cy + 40f, $"X: {burn}", HudTheme.WithAlpha(0xFFC0E0FFu, 0.55f));
            }
            return;
        }

        dl.AddRectFilled(new Vector2(vp.x, vp.y), new Vector2(vp.x + vp.w, vp.y + vp.h), 0x70000000u);
        string cause = player.DeathCause switch
        {
            DamageCause.EnemyFire => "Destroyed by enemy fire",
            DamageCause.Overheat => "Destroyed by overheating",
            DamageCause.Frostbite => "Destroyed by frostbite",
            DamageCause.Emberite => "Destroyed by emberites",
            _ => "Destroyed",
        };

        DrawCenteredText(dl, 44f, cx, cy - 30f, "TANK DESTROYED", 0xFF4060FFu);
        DrawCenteredText(dl, 20f, cx, cy + 20f, cause, 0xFFFFFFFFu);
        DrawCenteredText(dl, 20f, cx, cy + 48f,
            $"Respawning at base in {player.RespawnRemaining.TotalSeconds:0.0} s", 0xFFC0E0FFu);
    }

    /// <summary>
    /// A timed match the score decided: who won and with how many kills, the player's own place, kills and deaths, and
    /// the final standings on a plate.
    /// </summary>
    private static void DrawFinalScore(ImDrawListPtr dl, (float x, float y, float w, float h) vp, float cx, float cy,
        bool won, Scoreboard board, bool canRestart, string? winnerName)
    {
        dl.AddRectFilled(new Vector2(vp.x, vp.y), new Vector2(vp.x + vp.w, vp.y + vp.h), 0x70000000u);
        DrawCenteredText(dl, 44f, cx, cy - 30f, won ? "VICTORY" : "DEFEAT", won ? 0xFF40FF80u : 0xFF4040FFu);
        DrawCenteredText(dl, 20f, cx, cy + 20f,
            won ? Scoreboards.VictoryLine(board) : Scoreboards.DefeatLine(board, winnerName), 0xFFFFFFFFu);
        if (board.OwnResult is { } own)
            DrawCenteredText(dl, 18f, cx, cy + 46f, own, HudTheme.Amber);

        const float width = 340f, pad = 14f;
        float rows = ArcadeLines.RowsHeight(board.Rows.Count);
        var min = new Vector2(MathF.Floor(cx - width * 0.5f), MathF.Floor(cy + 78f));
        var max = min + new Vector2(width, pad + 22f + rows + pad);
        HudTheme.DrawPanel(dl, min, max);
        HudTheme.DrawTitle(dl, min + new Vector2(pad + 4f, pad - 2f), width - 2f * pad - 8f, "FINAL STANDINGS", 16f);
        ArcadeLines.DrawRows(dl, min + new Vector2(pad + 6f, pad + 22f), width - 2f * pad - 12f, board);
        if (canRestart)
            DrawCenteredText(dl, 20f, cx, max.Y + 14f, "Press Enter to play again", 0xFFC0E0FFu);
    }

    /// <summary>Why the player lost, in a line. A match of two has "the enemy"; one of more says who won, if it knows.</summary>
    /// <param name="winnerName">What to call the winner; null for "another tank".</param>
    public static string DefeatLine(World world, Tank player, string? winnerName = null) =>
        player.Base?.IsDestroyed == true ? "Your base was destroyed"
        : world.MatchWinner?.Reason == MatchWinReason.TappedTheCore ? $"{winnerName ?? "Another tank"} tapped the core first"
        : world.TankList.Count > 2 ? $"{(winnerName is null ? "Another base" : winnerName + "'s base")} is the last standing"
        : "The enemy base is the last standing";

    /// <summary>Why the player won, in a line.</summary>
    public static string VictoryLine(World world) => world.MatchWinner?.Reason switch
    {
        MatchWinReason.TappedTheCore => "Your base tapped the core first",
        _ when world.TankList.Count > 2 => "Every other base was destroyed",
        _ => "The enemy base was destroyed",
    };
}
