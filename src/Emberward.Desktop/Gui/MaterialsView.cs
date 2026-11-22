namespace Emberward.Desktop.Gui;

using System.Globalization;
using Emberward.Core.Entities.Tanks;

/// <summary>
/// What the MATERIALS plate shows (docs/pyrite-deposit-feedback-design.md §5.1), the same on desktop
/// (<see cref="HudStrip"/>) and in the browser (<c>strip.js</c>, <c>hud.js</c>): two rows, HOLD (the hold against
/// what it takes) above BASE (the base store, which every relay shares, with how fast it grows), the hold's pile, and
/// the line beneath the hold window. A deposit is stretched over its pour: the hold drains, the store counts up, the
/// BASE row flashes gold once and a <c>+N</c> rises out of it.
/// </summary>
/// <param name="HoldText">The HOLD row's number, <c>23 / 40</c>.</param>
/// <param name="HoldFull">The hold is full: its number turns deep orange.</param>
/// <param name="BaseText">The BASE row's number, thousands separated.</param>
/// <param name="RateText">How fast the store grows, <c>+18/m</c>, or null when it does not.</param>
/// <param name="Flash">The BASE row's gold flash, 1 as a deposit begins, fading to 0 over its pour.</param>
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
        long age = ledger.DepositAge;
        bool showDelta = ledger.DepositUnits > 0 && age >= 0 && age < DeltaSteps;
        return new MaterialsView(
            string.Create(inv, $"{held} / {player.OreHoldCapacity}"),
            player.IsOreHoldFull,
            ledger.DisplayedStore.ToString("N0", inv),
            rate > 0 ? string.Create(inv, $"+{rate}/m") : null,
            ledger.DepositUnits > 0 ? Math.Clamp(1f - age / (float)OreLedgerView.PourSteps, 0f, 1f) : 0f,
            showDelta ? string.Create(inv, $"+{ledger.DepositUnits}") : null,
            showDelta ? age / (float)DeltaSteps : 0f,
            showDelta ? Math.Clamp((DeltaSteps - age) / (float)DeltaFadeSteps, 0f, 1f) : 0f,
            BuildSlotView.OrePileFrame(held, player.OreHoldCapacity),
            BuildSlotView.OreLine(player, ledger.IsPouring),
            player.IsUnloadingOre || ledger.IsPouring);
    }
}
