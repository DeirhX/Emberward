using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Types;
using Emberward.Desktop.Audio;
using Emberward.Desktop.Gui;
using Emberward.Desktop.Gui.Guidance;

namespace Emberward.Tests;

/// <summary>
/// The deposit on the HUD (docs/pyrite-deposit-feedback-design.md §5, §6): the MATERIALS rows (<see cref="MaterialsView"/>,
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
        Assert.Equal("40 / 40", idle.HoldText);
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
        Assert.Equal("0 / 40", after.HoldText);
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
    public void TheMaterialsRows_NeitherOverlapNorLeaveThePlate()
    {
        var hold = LedgerLayout.Text(LedgerLayout.HoldRowY);
        var store = LedgerLayout.Text(LedgerLayout.BaseRowY);
        Assert.True(hold.Top >= LedgerLayout.MaterialsPlateTop, $"HOLD starts at {hold.Top}, above the plate");
        Assert.True(store.Top >= hold.Bottom, $"BASE ({store.Top}) overlaps HOLD ({hold.Bottom})");
        Assert.True(store.Bottom <= LedgerLayout.MaterialsPlateBottom, $"BASE ends at {store.Bottom}, below the plate");
        Assert.True(LedgerLayout.RateSize <= LedgerLayout.NumberSize);
        Assert.True(LedgerLayout.MaterialsLeft < LedgerLayout.MaterialsRateX && LedgerLayout.MaterialsRateX < LedgerLayout.MaterialsRight);
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
    public void ACoreBank_IsHeardOnce_ForItsFourEvents()
    {
        var (s, _) = AtCore();
        var sink = new CountingSink();
        var director = new SoundDirector(sink);
        for (int i = 0; i < 8; i++)
        {
            s.World.Advance(_ => default);
            director.AfterStep(s.World, 0);
        }
        Assert.Equal(1, sink.Plays(SoundEffect.Deposit));
    }

    [Fact]
    public void TheFirstDeposit_IsToastedOnce_WithWhatTheBaseHasNow()
    {
        var (s, _) = AtCore();
        var feed = new NotificationFeed();
        var events = new GameEventWatcher();
        float now = 0f;
        events.Update(s.World, s.Tank, feed, now);
        for (int i = 0; i < 20; i++)
        {
            s.World.Advance(_ => default);
            events.AfterStep(s.World, s.Tank);
            now += 1f / Tweaks.Perf.TargetFps;
            events.Update(s.World, s.Tank, feed, now);
        }

        var toast = Assert.Single(feed.Active, n => n.Key == "first-deposit");
        Assert.Equal($"Pyrite banked: +40 (base {s.Base.Store.Ore})", toast.Title);

        s.Tank.Hold.Ore = 10;
        for (int i = 0; i < 20; i++)
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
        private readonly List<SoundEffect> _plays = new();

        public int Plays(SoundEffect effect) => _plays.Count(p => p == effect);

        public void Play(SoundEffect effect, float gain, float pan, float rate, float muffle = 0f) => _plays.Add(effect);

        public int AddLoop(SoundEffect effect) => 0;

        public void SetLoop(int handle, float gain, float pan = 0f, float rate = 1f, float muffle = 0f)
        {
        }
    }
}
