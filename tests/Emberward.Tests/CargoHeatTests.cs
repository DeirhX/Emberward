using Emberward.Core.Config;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Types;

namespace Emberward.Tests;

/// <summary>
/// Carried pyrite heats the tank (DESIGN.md §6.3, <see cref="Tweaks.Cargo"/>), linearly in the ore held: mild below 20
/// units, steeper from 20, and at 40 an emergency that takes a tank past its safe maximum in a few seconds unless it
/// unloads.
/// </summary>
public class CargoHeatTests
{
    private static readonly SimulationSettings Settings = SimulationSettings.FromTweaks();

    private static float PerSecond(int ore) => TankHeatModel.ComputeCargoHeat(Settings, ore) * Tweaks.Perf.TargetFps;

    [Theory]
    [InlineData(0, 0f)]
    [InlineData(19, 1.9f)]
    [InlineData(20, 2f)]
    [InlineData(30, 38f)]
    [InlineData(40, 74f)]
    [InlineData(70, 182f)]
    [InlineData(130, 398f)]
    public void TheRamp_GivesTheDocumentedRates(int ore, float degreesPerSecond) =>
        Assert.Equal(degreesPerSecond, PerSecond(ore), precision: 2);

    /// <summary>Linear: below the start and above it, every unit adds the same as the one before; no step, no spike.</summary>
    [Fact]
    public void TheRamp_IsLinear_OnEachSideOfTheStart_AndContinuousAtIt()
    {
        int start = Tweaks.Cargo.HeatStartOre;
        float below = PerSecond(1) - PerSecond(0), above = PerSecond(start + 1) - PerSecond(start);
        for (int ore = 1; ore <= start; ore++)
            Assert.Equal(below, PerSecond(ore) - PerSecond(ore - 1), precision: 4);
        for (int ore = start + 1; ore <= 130; ore++)
            Assert.Equal(above, PerSecond(ore) - PerSecond(ore - 1), precision: 3);
        // Continuous at the start: the unit that reaches it adds only the mild share.
        Assert.Equal(PerSecond(start - 1) + below, PerSecond(start), precision: 4);
        Assert.True(above > below, "past the start each unit should heat more");
    }

    [Fact]
    public void BelowTwenty_ItIsMild_AndFortySwampsAFullLink()
    {
        Assert.True(PerSecond(19) <= 2f * Tweaks.Tank.OffNetworkHeatPerSecond, "19 units should be mild");
        Assert.True(PerSecond(40) > 10f * Tweaks.Tank.NetworkCoolingPerSecond, "40 units should swamp a full link");
    }

    /// <summary>
    /// In a world: a cool tank idle in a tunnel with a load aboard. 40 units take it past the safe maximum within
    /// five seconds even inside its base's coverage; 19 leave it barely warm and 30 short of it.
    /// </summary>
    [Theory]
    [InlineData(19, false)]
    [InlineData(30, false)]
    [InlineData(40, true)]
    public void InATunnel_OnlyAFullLoad_OverheatsTheTankWithinFiveSeconds(int ore, bool overheats)
    {
        var s = RelayHeatTests.CreateScenario();
        s.Tank.Position = s.Base.Position + new Offset(0, s.DirY * (Tweaks.Base.BaseSize / 2 + 11));
        s.Tank.Hold.Ore = ore;
        float hottest = 0f;
        for (int i = 0; i < 5 * Tweaks.Perf.TargetFps; i++)
        {
            s.World.Advance(_ => default);
            hottest = Math.Max(hottest, s.Tank.Heat);
        }

        Assert.True(s.Tank.Coverage.Covered, "the tunnel mouth is inside the base's coverage");
        Assert.Equal(ore, s.Tank.OreHeld);
        Assert.Equal(overheats, hottest > Tweaks.Tank.HeatSafeMax);
        if (!overheats)
            Assert.True(hottest < Tweaks.Tank.HeatSafeMax * 0.75f, $"{ore} units reached {hottest:0}");
        if (ore < Tweaks.Cargo.HeatStartOre)
            Assert.True(hottest < 10f, $"{ore} units should be mild; reached {hottest:0}");
    }
}
