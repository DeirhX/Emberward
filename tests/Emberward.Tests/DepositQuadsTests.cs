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

    private static Run AtCore(int ore = 40, int localSlot = 0, bool energyNetwork = false)
    {
        var s = RelayHeatTests.CreateScenario(energyNetwork: energyNetwork);
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

    /// <summary>A pause holds the pour where it was drawn: the crystals stay put, whatever progress the frames say.</summary>
    [Fact]
    public void Held_ThePourStaysWhereItWasDrawn()
    {
        var run = AtCore();
        run.Step(4);
        var drawn = Drawn(run.Draw(alpha: 0.6f));

        run.Deposits.Hold();
        var heldEarly = Drawn(run.Draw(alpha: 0f));
        var heldLate = Drawn(run.Draw(alpha: 1f));

        Assert.NotEmpty(drawn);
        Assert.Equal(drawn.Length, heldEarly.Length);
        for (int i = 0; i < drawn.Length; i++)
        {
            Assert.Equal(drawn[i].X, heldEarly[i].X, 3);
            Assert.Equal(drawn[i].Y, heldEarly[i].Y, 3);
            Assert.Equal(heldEarly[i], heldLate[i]);
        }
    }

    /// <summary>Where each crystal is drawn (corner plus motion times the step's progress), in emission order.</summary>
    private static (float X, float Y)[] Drawn(EntityQuads quads)
    {
        var at = new List<(float, float)>();
        for (int i = 0; i < quads.VertexCount; i += 6)
        {
            var v = quads.Vertices[i];
            if (v.Kind == EntityShape.KindOreCrystal)
                at.Add((v.CellX + v.MotionX * quads.Alpha, v.CellY + v.MotionY * quads.Alpha));
        }
        return [.. at];
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
    public void ABurnPour_IsCyan_AndSaysRestart()
    {
        var run = AtCore(ore: 19, energyNetwork: true);
        var s = run.S;
        s.Base.DrawCharge(s.Base.Charge);
        s.Tank.Position = s.Base.SpawnPosition;
        run.Step();
        Assert.True(s.World.IsBaseDark(s.Base));
        s.Tank.Position = TestHelpers.AgainstCore(s.Base);
        run.Step(DepositQuads.FlightSteps / 2);

        // The base restarts on what it burns, and banks the rest: a cyan pour and a gold one.
        var pour = Assert.Single(run.Deposits.Pours(), p => p.Text?.EndsWith("RESTART", StringComparison.Ordinal) == true);
        Assert.Equal($"+{pour.Units} RESTART", pour.Text);
        var quads = run.Draw();
        bool cyan = false;
        for (int i = 0; i < quads.VertexCount; i += 6)
            cyan |= quads.Vertices[i].Kind == EntityShape.KindOreCrystal && quads.Vertices[i].R == 1f;
        Assert.True(cyan, "burned ore pours cyan");
    }

    [Fact]
    public void ARivalsRelayPour_UnderFog_IsNotDrawn()
    {
        var s = RelayHeatTests.CreateScenario();
        RelayHeatTests.PlantRelay(s);
        var run = new Run { S = s, LocalSlot = -1 };
        run.Step(12);
        s.Tank.Position = s.RelayPos;
        s.Tank.Hold.Ore = 10;
        run.Step(Tweaks.Perf.TargetFps / 2);
        var blind = new VisionMap(s.World.Terrain);

        Assert.Null(Assert.Single(run.Deposits.Pours()).Text);
        Assert.Equal(0, run.CountKind(EntityShape.KindOreCrystal, blind));
        Assert.Equal(0, run.CountKind(EntityShape.KindPowerLabel, blind));
    }

    [Fact]
    public void ThePour_GoesOn_WhenItsTankDrivesOff()
    {
        var run = AtCore();
        run.Step(2);
        run.S.Tank.Position = run.S.Base.SpawnPosition;
        run.Step(DepositQuads.MaxCrystalsPerPour + DepositQuads.FlightSteps);

        var pour = Assert.Single(run.Deposits.Pours());
        Assert.True(pour.Units > 0);
        Assert.True(run.Deposits.CorePulse(run.S.Base.Position) > 0.2f, "every crystal landed");
    }

    [Fact]
    public void AtTheCore_TheOreBeamIsLeftOut_ForThePour()
    {
        var s = RelayHeatTests.CreateScenario();
        s.Tank.Position = TestHelpers.AgainstCore(s.Base);
        s.Tank.Hold.Ore = 40;
        var layer = new Emberward.Desktop.Rendering.EntityLayerBuilder();
        var size = s.World.Terrain.Size;
        int beams = 0;
        for (int i = 0; i < 4; i++)
        {
            s.World.Advance(_ => default);
            layer.CaptureStep(s.World, 0, vision: null);
            Assert.True(s.Tank.IsUnloadingOre || i > 0);
            layer.Build(new RenderView(size, size, default, 1), 0.5f, 0f, snapHeadings: true, visualSeconds: i * 0.1f);
            for (int v = 0; v < layer.VertexCount; v += 6)
                if (layer.Vertices[v].Kind == EntityShape.KindOreBeam)
                    beams++;
        }
        Assert.Equal(0, beams);
    }

    [Fact]
    public void TheStoreLabel_StopsAt24Cells_AndSkipsAnUnpoweredRelay_OrADarkBase()
    {
        var s = RelayHeatTests.CreateScenario(energyNetwork: true);
        RelayHeatTests.PlantRelay(s);
        var run = new Run { S = s, LocalSlot = 0 };
        run.Step(12);

        s.Tank.Position = s.RelayPos + new Offset(0, -s.DirY * DepositQuads.StoreLabelReach);
        run.Step();
        Assert.Equal(s.RelayPos, run.Deposits.StoreLabel);
        s.Tank.Position = s.RelayPos + new Offset(0, -s.DirY * (DepositQuads.StoreLabelReach + 1));
        run.Step();
        Assert.Null(run.Deposits.StoreLabel);

        s.Tank.Position = s.RelayPos + new Offset(0, -s.DirY * 10);
        s.Base.DrawCharge(s.Base.Charge);
        run.Step(2);
        Assert.True(s.World.IsBaseDark(s.Base));
        Assert.Null(run.Deposits.StoreLabel);

        var blocked = RelayHeatTests.CreateScenario(blockLineOfSight: true);
        RelayHeatTests.PlantRelay(blocked);
        var unpowered = new Run { S = blocked, LocalSlot = 0 };
        unpowered.Step(12);
        blocked.Tank.Position = blocked.RelayPos + new Offset(0, -blocked.DirY * 5);
        unpowered.Step();
        Assert.Null(unpowered.Deposits.StoreLabel);
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
