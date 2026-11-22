using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Rendering;
using Emberward.Core.Sensing;
using Emberward.Core.Types;
using Emberward.Desktop.Gui;
using Emberward.Desktop.Rendering;
using Emberward.Desktop.Rendering.Entities;

namespace Emberward.Tests;

/// <summary>
/// The pyrite deposit pour (docs/pyrite-deposit-feedback-design.md §4): crystals from the hold into the core or relay,
/// the core's pulse, the <c>+N</c> for the player's side, and the store label on the nearest relay.
/// </summary>
public class DepositQuadsTests
{
    private sealed class Run
    {
        public required RelayHeatTests.Scenario S { get; init; }
        public DepositQuads Deposits { get; } = new();
        public OreLedgerView Ledger { get; } = new();
        public int LocalSlot { get; init; }

        public void Step(int steps = 1)
        {
            for (int i = 0; i < steps; i++)
            {
                S.World.Advance(_ => default);
                Ledger.CaptureStep(S.World, LocalSlot);
                Deposits.CaptureStep(S.World, LocalSlot, Ledger);
            }
        }

        public EntityQuads Draw(VisionMap? vision = null, float alpha = 0f)
        {
            var size = S.World.Terrain.Size;
            var quads = new EntityQuads();
            quads.Begin(new RenderView(size, size, default, 1), phase: 1.25f, alpha: alpha);
            Deposits.EmitGlows(quads, vision);
            Deposits.EmitCrystals(quads, vision);
            Deposits.EmitLabels(quads);
            return quads;
        }

        public int CountKind(float kind, VisionMap? vision = null)
        {
            var quads = Draw(vision);
            int count = 0;
            for (int i = 0; i < quads.VertexCount; i += 6)
                if (quads.Vertices[i].Kind == kind)
                    count++;
            return count;
        }
    }

    private static Run AtCore(int ore = 40, int localSlot = 0)
    {
        var s = RelayHeatTests.CreateScenario();
        s.Tank.Position = TestHelpers.AgainstCore(s.Base);
        s.Tank.Hold.Ore = ore;
        return new Run { S = s, LocalSlot = localSlot };
    }

    [Fact]
    public void TheFourEventsOfOneBank_MakeOnePourOfTwelveCrystals()
    {
        var run = AtCore();
        run.Step(5);

        var pour = Assert.Single(run.Deposits.Pours());
        Assert.Equal(run.S.Base.Position, pour.Receiver);
        Assert.Equal(40, pour.Units);
        Assert.Equal(DepositQuads.MaxCrystalsPerPour, pour.Crystals);
        Assert.True(run.CountKind(EntityShape.KindOreCrystal) > 0, "crystals are in flight");
    }

    [Fact]
    public void ACrystal_LeavesTheHold_AndLandsOnTheReceiver()
    {
        var from = new PositionF(10f, 20f);
        var to = new PositionF(15f, 20f);
        foreach (float side in new[] { 1f, -1f })
        {
            Assert.Equal(from, DepositQuads.Arc(from, to, side, 0f));
            Assert.Equal(to, DepositQuads.Arc(from, to, side, 1f));
            // Halfway it stands off the straight line, so a short gap reads as a pour.
            Assert.True(MathF.Abs(DepositQuads.Arc(from, to, side, 0.5f).Y - 20f) > 1f);
        }
    }

    [Fact]
    public void TheCorePulse_RisesWithTheLandings_AndFallsAway()
    {
        var run = AtCore();
        run.Step(5);
        Assert.Equal(0f, run.Deposits.CorePulse(run.S.Base.Position));

        // The last of 12 crystals leaves 11 steps after the first and flies FlightSteps.
        int lastLanding = DepositQuads.MaxCrystalsPerPour - 1 + DepositQuads.FlightSteps;
        run.Step(lastLanding + 1 - 5);
        float peak = run.Deposits.CorePulse(run.S.Base.Position);
        Assert.True(peak > 0.5f, $"twelve landings pulse the core, got {peak}");

        // Within 1.5 s of the last landing it is gone.
        run.Step(Tweaks.Perf.TargetFps * 3 / 2);
        Assert.True(run.Deposits.CorePulse(run.S.Base.Position) < 0.05f);
    }

    [Fact]
    public void TheFloater_SaysPlusForty_ForThePlayersSide_AndNothingForARival()
    {
        var own = AtCore(localSlot: 0);
        var rival = AtCore(localSlot: -1);
        own.Step(DepositQuads.FlightSteps + 2);
        rival.Step(DepositQuads.FlightSteps + 2);

        Assert.Equal("+40", Assert.Single(own.Deposits.Pours()).Text);
        Assert.Null(Assert.Single(rival.Deposits.Pours()).Text);
        // The rival's pour still shows its crystals; only the numbers are the owner's.
        Assert.True(own.CountKind(EntityShape.KindPowerLabel) > 0);
        Assert.Equal(0, rival.CountKind(EntityShape.KindPowerLabel));
        Assert.True(rival.CountKind(EntityShape.KindOreCrystal) > 0);
    }

    [Fact]
    public void APourAtABaseOutOfSight_IsNotDrawn()
    {
        var run = AtCore();
        run.Step(4);
        var blind = new VisionMap(run.S.World.Terrain);

        Assert.True(run.CountKind(EntityShape.KindOreCrystal) > 0);
        Assert.Equal(0, run.CountKind(EntityShape.KindOreCrystal, blind));
        Assert.Equal(0, run.CountKind(EntityShape.KindCoreHeat, blind));
    }

    [Fact]
    public void TheSameSteps_GiveTheSameQuads()
    {
        var a = AtCore();
        var b = AtCore();
        a.Step(8);
        b.Step(8);

        var qa = a.Draw(alpha: 0.4f);
        var qb = b.Draw(alpha: 0.4f);
        Assert.Equal(qa.VertexCount, qb.VertexCount);
        Assert.True(qa.Vertices.AsSpan(0, qa.VertexCount).SequenceEqual(qb.Vertices.AsSpan(0, qb.VertexCount)));
    }

    [Fact]
    public void AtARelay_EachUnitIsACrystal_AndTheVisitCountsUp()
    {
        var s = RelayHeatTests.CreateScenario();
        RelayHeatTests.PlantRelay(s);
        var run = new Run { S = s, LocalSlot = 0 };
        run.Step(12);
        s.Tank.Position = s.RelayPos;
        s.Tank.Hold.Ore = 10;

        run.Step(Tweaks.Perf.TargetFps * 2);

        int banked = 10 - s.Tank.OreHeld;
        var pour = Assert.Single(run.Deposits.Pours());
        Assert.Equal(s.RelayPos, pour.Receiver);
        Assert.Equal(banked, pour.Units);
        Assert.Equal(banked, pour.Crystals);
        Assert.Equal($"+{banked}", pour.Text);
    }

    [Fact]
    public void TheNearestPoweredRelay_CarriesTheStoreLabel_OnlyInReach()
    {
        var s = RelayHeatTests.CreateScenario();
        RelayHeatTests.PlantRelay(s);
        var run = new Run { S = s, LocalSlot = 0 };
        run.Step(12);

        s.Tank.Position = s.RelayPos + new Offset(0, -s.DirY * 10);
        run.Step();
        Assert.Equal(s.RelayPos, run.Deposits.StoreLabel);
        Assert.Equal(s.RelayPos, s.World.NearestOreDropOff(s.Tank, DepositQuads.StoreLabelReach));
        Assert.True(run.CountKind(EntityShape.KindPowerLabel) > 0, "the label is drawn");

        s.Tank.Position = s.Base.SpawnPosition;
        run.Step();
        Assert.Null(run.Deposits.StoreLabel);
    }

    [Fact]
    public void TheLedger_CountsTheRate_OverThirtySeconds()
    {
        var run = AtCore();
        run.Step(6);
        Assert.Equal(40 * 60 / OreLedgerView.WindowSeconds, run.Ledger.RatePerMinute);
        Assert.Equal(run.S.Base.Store.Ore, run.Ledger.Store);

        run.Step(OreLedgerView.WindowSeconds * Tweaks.Perf.TargetFps);
        Assert.Equal(0, run.Ledger.RatePerMinute);
    }
}
