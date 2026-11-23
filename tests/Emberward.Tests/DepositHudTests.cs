using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Types;
using Emberward.Desktop.Audio;
using Emberward.Desktop.Gui;
using Emberward.Desktop.Gui.Guidance;

namespace Emberward.Tests;

/// <summary>
/// The deposit on the HUD (docs/pyrite-deposit-feedback-design.md §5, §6): the MATERIALS line (<see cref="MaterialsView"/>,
/// <see cref="OreLedgerView"/>), the deposit sound and the first-deposit toast.
/// </summary>
public class DepositHudTests
{
    private static (RelayHeatTests.Scenario S, OreLedgerView Ledger) AtCore(int ore = 40)
    {
        var s = RelayHeatTests.CreateScenario();
        s.Tank.Position = TestHelpers.AgainstCore(s.Base);
        s.Tank.Hold.Ore = ore;
        return (s, new OreLedgerView());
    }

    private static void Step(RelayHeatTests.Scenario s, OreLedgerView ledger, int steps = 1)
    {
        for (int i = 0; i < steps; i++)
        {
            s.World.Advance(_ => default);
            ledger.CaptureStep(s.World, 0);
        }
    }

    [Fact]
    public void TheStore_CountsUpOverThePour_AndReachesTheStoreAtItsEnd()
    {
        var (s, ledger) = AtCore();
        int before = s.Base.Store.Ore;
        Step(s, ledger);
        Assert.Equal(before, ledger.DisplayedStore);

        Step(s, ledger, 6);
        Assert.Equal(before + 40, s.Base.Store.Ore);
        Assert.InRange(ledger.DisplayedStore, before + 1, before + 39);
        Assert.True(ledger.DisplayedHold > 0, "the hold drains over the pour, not in four ticks");
        Assert.True(ledger.IsPouring);

        Step(s, ledger, OreLedgerView.PourSteps);
        Assert.Equal(s.Base.Store.Ore, ledger.DisplayedStore);
        Assert.Equal(0, ledger.DisplayedHold);
        Assert.False(ledger.IsPouring);
        Assert.Equal(40, ledger.DepositUnits);
    }

    [Fact]
    public void TheMaterialsRows_ShowHoldAndBase_WithTheRate_AndTheDeposit()
    {
        var (s, ledger) = AtCore();
        var idle = MaterialsView.Of(s.Tank, ledger);
        Assert.Equal("40", idle.HoldText);
        Assert.True(idle.HoldFull);
        Assert.Null(idle.RateText);
        Assert.Null(idle.DeltaText);

        Step(s, ledger, 5);
        var pouring = MaterialsView.Of(s.Tank, ledger);
        Assert.True(pouring.Unloading);
        Assert.Equal("UNLOADING", pouring.OreLine);
        Assert.Equal("+40", pouring.DeltaText);
        Assert.True(pouring.Flash > 0f);
        Assert.Equal($"+{40 * 60 / OreLedgerView.WindowSeconds}/m", pouring.RateText);
        Assert.True(pouring.PileFrame >= 0, "the pile is still draining");

        Step(s, ledger, MaterialsView.DeltaSteps);
        var after = MaterialsView.Of(s.Tank, ledger);
        Assert.Equal("0", after.HoldText);
        Assert.Equal(s.Base.Store.Ore.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), after.BaseText);
        Assert.Null(after.DeltaText);
        Assert.Equal(0f, after.Flash);
        Assert.Equal(-1, after.PileFrame);
        Assert.False(after.Unloading);
    }

    [Fact]
    public void TheBaseNumber_IsThousandsSeparated()
    {
        var (s, ledger) = AtCore(ore: 0);
        s.Base.Store.Ore = 12345;
        Step(s, ledger);
        Assert.Equal("12,345", MaterialsView.Of(s.Tank, ledger).BaseText);
    }

    [Fact]
    public void TheMaterialsLines_EachStayInItsPlate()
    {
        var hold = LedgerLayout.Text(LedgerLayout.HoldLineY);
        Assert.True(hold.Top >= LedgerLayout.MaterialsUpperTop, $"HOLD starts at {hold.Top}, above its plate");
        Assert.True(hold.Bottom <= LedgerLayout.MaterialsUpperBottom, $"HOLD ends at {hold.Bottom}, below its plate");
        var store = LedgerLayout.Text(LedgerLayout.BaseLineY);
        Assert.True(store.Top >= LedgerLayout.MaterialsLowerTop, $"BASE starts at {store.Top}, above its plate");
        Assert.True(store.Bottom <= LedgerLayout.MaterialsLowerBottom, $"BASE ends at {store.Bottom}, below its plate");
        Assert.True(LedgerLayout.RateSize <= LedgerLayout.NumberSize);
        Assert.True(LedgerLayout.MaterialsLeft < LedgerLayout.MaterialsNumberRight
            && LedgerLayout.MaterialsNumberRight < LedgerLayout.MaterialsRight);
        // The deposit's +N, at the top of its rise, stays clear of the HOLD line.
        var risen = LedgerLayout.Text(LedgerLayout.BaseLineY - 2f - LedgerLayout.DeltaRise);
        Assert.True(risen.Top >= hold.Bottom, $"the +N rises to {risen.Top}, into HOLD ({hold.Bottom})");
    }

    [Fact]
    public void ACoreBank_IsADeposit_PlayedOncePerPour_AndARelayUnitThePickup()
    {
        var core = new StepEvent(StepEventKind.OreUnloaded, new Position(10, 10), 1, Target: ImpactTarget.Base, Count: 10);
        var relay = core with { Target = ImpactTarget.Machine, Count = 1 };
        var burn = new StepEvent(StepEventKind.RestartOreBurned, new Position(10, 10), 1, Target: ImpactTarget.Base, Count: 15);

        var coreCue = StepEventSounds.CueFor(core)!.Value;
        Assert.Equal(SoundEffect.Deposit, coreCue.Effect);
        Assert.Equal(CueVoice.Pour, coreCue.Voice);
        Assert.Equal(CueHeard.Player, coreCue.Heard);
        Assert.Equal(SoundEffect.Pickup, StepEventSounds.CueFor(relay)!.Value.Effect);
        var burnCue = StepEventSounds.CueFor(burn)!.Value;
        Assert.Equal(SoundEffect.Deposit, burnCue.Effect);
        Assert.True(burnCue.Pitch < 1f);
        Assert.True(StepEventSounds.DepositGain(40) > StepEventSounds.DepositGain(1));
        Assert.NotEmpty(new SoundBank()[SoundEffect.Deposit]);
    }

    [Fact]
    public void ACoreBank_IsHeardOnce_ForItsFourEvents_AsLoudAsItsWholeLoad()
    {
        float GainOf(int ore, out int plays)
        {
            var (s, _) = AtCore(ore);
            var sink = new CountingSink();
            var director = new SoundDirector(sink);
            for (int i = 0; i < 8; i++)
            {
                s.World.Advance(_ => default);
                director.AfterStep(s.World, 0);
            }
            plays = sink.Plays(SoundEffect.Deposit);
            return sink.Gain(SoundEffect.Deposit);
        }

        float full = GainOf(40, out int fullPlays), tick = GainOf(Tweaks.Base.HomeAbsorbOre, out _);
        Assert.Equal(1, fullPlays);
        // The first event of both carries a tick's 10; the full hold still sounds for its 40.
        Assert.Equal(StepEventSounds.DepositGain(40) / StepEventSounds.DepositGain(Tweaks.Base.HomeAbsorbOre), full / tick, 4);
    }

    [Fact]
    public void ARelayVisit_CountsUp_AndKeepsItsPlusNUp_WhileUnitsArrive()
    {
        var s = RelayHeatTests.CreateScenario();
        RelayHeatTests.PlantRelay(s);
        var ledger = new OreLedgerView();
        Step(s, ledger, 12);
        s.Tank.Position = s.RelayPos;
        s.Tank.Hold.Ore = 20;

        Step(s, ledger, Tweaks.Perf.TargetFps * 3);

        int banked = 20 - s.Tank.OreHeld;
        Assert.True(banked >= 10, $"a relay banks 4 a second, got {banked}");
        Assert.Equal(banked, ledger.DepositUnits);
        Assert.True(ledger.DepositAge > MaterialsView.DeltaSteps, "the visit has lasted longer than one +N's rise");
        var view = MaterialsView.Of(s.Tank, ledger);
        Assert.Equal($"+{banked}", view.DeltaText);
        Assert.Equal(1f, view.DeltaRise);
        Assert.True(view.DeltaAlpha > 0f && view.Flash > 0f, "each unit restarts the flash and holds the +N up");
        Assert.True(view.Unloading);
    }

    [Fact]
    public void SpendingOrMiningDuringACountUp_MovesItWithTheStore()
    {
        var (s, ledger) = AtCore();
        Step(s, ledger, 5);
        int shown = ledger.DisplayedStore;
        s.Base.Store.Ore -= 25;
        Step(s, ledger);
        Assert.InRange(ledger.DisplayedStore, shown - 25, shown - 25 + 40);
        Step(s, ledger, OreLedgerView.PourSteps);
        Assert.Equal(s.Base.Store.Ore, ledger.DisplayedStore);

        s.Base.Store.Ore += 7;
        Step(s, ledger);
        Assert.Equal(s.Base.Store.Ore, ledger.DisplayedStore);
    }

    [Fact]
    public void BurningOre_IsStretchedOnTheHudLikeABank_ButTheStoreStaysPut()
    {
        var s = RelayHeatTests.CreateScenario(energyNetwork: true);
        var ledger = new OreLedgerView();
        s.Base.DrawCharge(s.Base.Charge);
        s.Tank.Position = s.Base.SpawnPosition;
        s.Tank.Hold.Ore = 12;
        Step(s, ledger);
        Assert.True(s.World.IsBaseDark(s.Base));
        int store = s.Base.Store.Ore;
        s.Tank.Position = TestHelpers.AgainstCore(s.Base);

        Step(s, ledger);

        Assert.True(s.Tank.OreHeld < 12, "the hold burned");
        Assert.Equal(store, s.Base.Store.Ore);
        Assert.True(ledger.IsPouring);
        Assert.True(ledger.DisplayedHold > s.Tank.OreHeld, "the hold drains over the pour");
        var view = MaterialsView.Of(s.Tank, ledger);
        Assert.Equal("UNLOADING", view.OreLine);
        Assert.Equal(0, ledger.DepositUnits);
        Assert.Null(view.DeltaText);
    }

    /// <summary>
    /// HOLD 70 and BASE 99,999 +999/m fit across their plates: the labels, numbers and rate do not run into each other,
    /// at a generous width for the HUD font (each character at most <c>Advance</c> of the text size; Chakra Petch's
    /// capitals and digits measure about 0.45).
    /// </summary>
    [Fact]
    public void TheMaterialsLines_FitAcrossThePlates()
    {
        const float Advance = 0.5f;
        const float Gap = 3f;
        static float Width(string text, float size) => text.Length * size * Advance;
        float numbers = LedgerLayout.MaterialsNumberRight;
        float labelEnd = LedgerLayout.MaterialsLeft + Width("HOLD", LedgerLayout.LabelSize);
        float storeStart = numbers - Width("99,999", LedgerLayout.NumberSize);
        float rateStart = LedgerLayout.MaterialsRight - Width("+999/m", LedgerLayout.RateSize);

        Assert.True(labelEnd + Gap < storeStart, $"the label ends at {labelEnd}, past the store's number at {storeStart}");
        Assert.True(numbers + Gap < rateStart, $"the store ends at {numbers}, past the rate's start at {rateStart}");
        Assert.True(LedgerLayout.MaterialsRight <= 213f, "the rate ends inside the plate");
    }

    [Fact]
    public void TheFirstDeposit_IsToastedOnce_WithWhatTheBaseHasNow()
    {
        var (s, _) = AtCore();
        var feed = new NotificationFeed();
        var events = new GameEventWatcher();
        float now = 0f;
        events.Update(s.World, s.Tank, feed, now);
        for (int i = 0; i < 50; i++)
        {
            s.World.Advance(_ => default);
            events.AfterStep(s.World, s.Tank);
            now += 1f / Tweaks.Perf.TargetFps;
            events.Update(s.World, s.Tank, feed, now);
        }

        var toast = Assert.Single(feed.Active, n => n.Key == "first-deposit");
        Assert.Equal($"Pyrite banked: +40 (base {s.Base.Store.Ore:N0})", toast.Title);

        s.Tank.Hold.Ore = 10;
        for (int i = 0; i < 50; i++)
        {
            s.World.Advance(_ => default);
            events.AfterStep(s.World, s.Tank);
            now += 1f / Tweaks.Perf.TargetFps;
            events.Update(s.World, s.Tank, feed, now);
        }
        Assert.Single(feed.Active, n => n.Key == "first-deposit");
    }

    private sealed class CountingSink : ISoundSink
    {
        private readonly List<(SoundEffect Effect, float Gain)> _plays = new();

        public int Plays(SoundEffect effect) => _plays.Count(p => p.Effect == effect);

        public float Gain(SoundEffect effect) => _plays.First(p => p.Effect == effect).Gain;

        public void Play(SoundEffect effect, float gain, float pan, float rate, float muffle = 0f) => _plays.Add((effect, gain));

        public int AddLoop(SoundEffect effect) => 0;

        public void SetLoop(int handle, float gain, float pan = 0f, float rate = 1f, float muffle = 0f)
        {
        }
    }
}
