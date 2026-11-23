namespace Emberward.Desktop.Gui;

using ImGuiNET;
using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Match;
using Emberward.Core.Entities.Bases;
using static Emberward.Desktop.Gui.HudDraw;

/// <summary>
/// The goal lines at the top of the base status panel: the sector goal (DESIGN.md §4.8), or in a timed match the
/// score that wins it and the board (§6.8, <see cref="ArcadeLines"/>).
/// </summary>
public static class GoalLines
{
    public static void Draw(World world, TankBase tankBase, in ArcadePanel arcade = default)
    {
        ArcadeLines.DrawInPanel(arcade.Board, arcade.Drops ?? [], arcade.WithClock, arcade.Time);
        // A timed match is won on the score: the core is there, and tapping it is not the goal.
        if (arcade.Board != null || world.CoreTapOf(tankBase) is not CoreTap core)
            return;
        if (core.HasTapped)
        {
            ColoredText(HudTheme.Good, "SECTOR COMPLETE - the core is tapped");
            return;
        }
        double rate = core.TapPerSecond;
        bool meeting = core.IsMeetingGoal;
        ColoredText(HudTheme.Teal, "GOAL  tap the hot core at the centre");
        ColoredText(meeting ? HudTheme.Good : HudTheme.Text,
            $"Core tap {rate / 1000.0:0.0}k / {Tweaks.Goal.GoalChargePerSecond / 1000.0:0.0}k charge/s");
        HudTheme.BarRow(core.HoldFraction, meeting ? HudTheme.Good : HudTheme.Amber,
            $"held {core.HoldSeconds:0} / {Tweaks.Goal.GoalHoldSeconds:0} s", 20f);
        ImGui.Separator();
    }
}
