namespace Emberward.Desktop.Gui;

using System.Globalization;
using Emberward.Core.Entities.Tanks;

/// <summary>
/// What the MATERIALS plate shows (docs/pyrite-deposit-feedback-design.md §5.1), the same on desktop
/// (<see cref="HudStrip"/>) and in the browser (<c>strip.js</c>, <c>hud.js</c>): a line on each of its two plates,
/// HOLD (what the hold carries; the pile shows how full it is) on the upper and BASE (the base store, which every relay
/// shares, with how fast it grows) on the lower, the hold's pile, and the line beneath the hold window. A deposit
/// is stretched over its pour: the hold drains, the store counts up, the store's number flashes gold once and a
/// <c>+N</c> rises out of it.
/// </summary>
/// <param name="HoldText">The ore in the hold, <c>23</c>: the amount alone, not the capacity.</param>
/// <param name="HoldFull">The hold is full: its number turns deep orange.</param>
/// <param name="BaseText">The base store's number, thousands separated.</param>
/// <param name="RateText">How fast the store grows, <c>+18/m</c>, or null when it does not.</param>
/// <param name="Flash">The store number's gold flash, 1 as a deposit begins, fading to 0 over its pour.</param>
/// <param name="DeltaText">The rising <c>+N</c> of the latest deposit, or null when none shows.</param>
/// <param name="DeltaRise">How far it has risen, 0 to 1.</param>
/// <param name="DeltaAlpha">Its opacity, 0 to 1.</param>
/// <param name="PileFrame">The hold pile's frame (<see cref="BuildSlotView.OrePileFrame(int, int)"/>), -1 when empty.</param>
/// <param name="OreLine">The line beneath the hold window (<see cref="BuildSlotView.OreLine"/>).</param>
/// <param name="Unloading">The hold is emptying, or its pour still showing: the ore line is green, with arrows.</param>
public sealed record MaterialsView(
    string HoldText, bool HoldFull, string BaseText, string? RateText, float Flash, string? DeltaText, float DeltaRise,
    float DeltaAlpha, int PileFrame, string OreLine, bool Unloading)
{
    /// <summary>How long the <c>+N</c> rises (1.2 s), and the last part of that over which it fades.</summary>
    public const int DeltaSteps = 29;
    public const int DeltaFadeSteps = 10;

    public static MaterialsView Of(Tank player, OreLedgerView ledger)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(ledger);
        var inv = CultureInfo.InvariantCulture;
        int held = Math.Max(player.OreHeld, ledger.DisplayedHold);
        int rate = ledger.RatePerMinute;
        // The +N rises from the deposit's start and stays up while it keeps growing (a relay visit); it fades, and the
        // store's flash restarts, from its latest growth.
        long age = ledger.DepositAge, last = ledger.LastDepositAge;
        bool showDelta = ledger.DepositUnits > 0 && last >= 0 && last < DeltaSteps;
        return new MaterialsView(
            held.ToString(inv),
            player.IsOreHoldFull,
            ledger.DisplayedStore.ToString("N0", inv),
            rate > 0 ? string.Create(inv, $"+{rate}/m") : null,
            ledger.DepositUnits > 0 ? Math.Clamp(1f - last / (float)OreLedgerView.PourSteps, 0f, 1f) : 0f,
            showDelta ? string.Create(inv, $"+{ledger.DepositUnits}") : null,
            showDelta ? Math.Clamp(age / (float)DeltaSteps, 0f, 1f) : 0f,
            showDelta ? Math.Clamp((DeltaSteps - last) / (float)DeltaFadeSteps, 0f, 1f) : 0f,
            BuildSlotView.OrePileFrame(held, player.OreHoldCapacity),
            BuildSlotView.OreLine(player, ledger.IsPouring),
            player.IsUnloadingOre || ledger.IsPouring);
    }
}
