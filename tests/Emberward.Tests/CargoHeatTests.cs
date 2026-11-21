using Emberward.Core.Config;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Types;

namespace Emberward.Tests;

/// <summary>
/// Carried pyrite heats the tank (DESIGN.md §6.3, <see cref="Tweaks.Cargo"/>): mild below 20 units, a clear climb from
/// 20, and at 40 an emergency that takes a tank past its safe maximum in a few seconds unless it unloads.
/// </summary>
public class CargoHeatTests
{
    private static readonly SimulationSettings Settings = SimulationSettings.FromTweaks();

    private static float PerSecond(int ore) => TankHeatModel.ComputeCargoHeat(Settings, ore) * Tweaks.Perf.TargetFps;

    [Theory]
    [InlineData(0, 0f)]
    [InlineData(19, 1.9f)]
    [InlineData(20, 5f)]
    [InlineData(30, 22f)]
    [InlineData(40, 71f)]
    public void TheCurve_GivesTheDocumentedRates(int ore, float degreesPerSecond) =>
        Assert.Equal(degreesPerSecond, PerSecond(ore), precision: 3);

    [Fact]
    public void TheCurve_RisesWithEveryUnit_AndKeepsRisingPastForty()
    {
        for (int ore = 1; ore <= 130; ore++)
            Assert.True(PerSecond(ore) > PerSecond(ore - 1), $"{ore} units heat no faster than {ore - 1}");
        // Smooth past the danger mark: the step from 40 to 41 is the step from 39 to 40, near enough.
        float before = PerSecond(40) - PerSecond(39), after = PerSecond(41) - PerSecond(40);
        Assert.InRange(after, before, before * 1.1f);
    }

    [Fact]
    public void BelowTwenty_ItIsMild_AndFromTwentyItOutrunsTheOffNetworkClock()
    {
        Assert.True(PerSecond(19) <= 2f * Tweaks.Tank.OffNetworkHeatPerSecond, "19 units should be mild");
        Assert.True(PerSecond(20) > 4f * Tweaks.Tank.OffNetworkHeatPerSecond, "20 units should start to heat the tank");
        Assert.True(PerSecond(40) > 10f * Tweaks.Tank.NetworkCoolingPerSecond, "40 units should swamp a full link");
    }

    /// <summary>
    /// In a world: a cool tank idle in a tunnel with a load aboard. 40 units take it past the safe maximum within
    /// five seconds even inside its base's coverage; 19 leave it barely warm and 30 well short of it.
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
            Assert.True(hottest < Tweaks.Tank.HeatSafeMax * 0.5f, $"{ore} units reached {hottest:0}");
        if (ore < Tweaks.Cargo.HeatStartOre)
            Assert.True(hottest < 10f, $"{ore} units should be mild; reached {hottest:0}");
    }
}
