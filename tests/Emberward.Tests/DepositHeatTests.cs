using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Resources;
using Emberward.Core.Types;

namespace Emberward.Tests;

/// <summary>
/// Deposit heat (docs/pyrite-deposit-feedback-design.md §3.2): each unit a tank banks brings
/// <see cref="Tweaks.Cargo.DepositHeatPerOre"/> into its base's reactor, as much as the reactor has room for, booked as
/// injected. Each case runs the same world twice, with the stock deposit heat and with none, and compares.
/// </summary>
public class DepositHeatTests
{
    private static readonly SimulationSettings NoDepositHeat = SimulationSettings.FromTweaks() with { BaseDepositHeatPerOre = 0 };

    private static RelayHeatTests.Scenario AgainstCore(int ore, SimulationSettings? settings, bool energyNetwork = false)
    {
        var s = RelayHeatTests.CreateScenario(energyNetwork: energyNetwork, simulationSettings: settings);
        s.Tank.Position = TestHelpers.AgainstCore(s.Base);
        s.Tank.Hold.Ore = ore;
        return s;
    }

    private static double Balance(World world) =>
        world.SumSystemThermalEnergy() + world.SumSystemCharge() - world.Terrain.Ledger.Injected + world.Terrain.Ledger.TotalSunk;

    /// <summary>
    /// Advances both worlds, checking the hot world's ledger every tick; returns how much more the hot world injected
    /// and how much more its reactor holds.
    /// </summary>
    private static (double Injected, double Reactor) Compare(RelayHeatTests.Scenario hot, RelayHeatTests.Scenario plain, int ticks)
    {
        double hotStart = hot.World.Terrain.Ledger.Injected, plainStart = plain.World.Terrain.Ledger.Injected;
        double balance = Balance(hot.World);
        for (int i = 0; i < ticks; i++)
        {
            hot.World.Advance(_ => default);
            plain.World.Advance(_ => default);
            Assert.InRange(Balance(hot.World) - balance, -1e-3, 1e-3);
        }
        return ((hot.World.Terrain.Ledger.Injected - hotStart) - (plain.World.Terrain.Ledger.Injected - plainStart),
            hot.Base.ReactorHeatEnergy - plain.Base.ReactorHeatEnergy);
    }

    [Fact]
    public void BankingAFullHold_BringsItsHeatIntoTheReactor_BookedAsInjected()
    {
        int ore = Tweaks.Ore.TankHold;
        var hot = AgainstCore(ore, settings: null);
        var plain = AgainstCore(ore, NoDepositHeat);

        var (injected, reactor) = Compare(hot, plain, ticks: 6);

        Assert.Equal(0, hot.Tank.OreHeld);
        Assert.Equal(plain.Base.Store.Ore, hot.Base.Store.Ore);
        Assert.Equal(ore * (double)Tweaks.Cargo.DepositHeatPerOre, injected, 6);
        // The radiator has started venting it, so the reactor holds part of it, but it is warmer than without.
        Assert.InRange(reactor, 1.0, injected);
    }

    [Fact]
    public void AFullReactor_TakesNoDepositHeat_AndNothingIsCreated()
    {
        var hot = AgainstCore(Tweaks.Ore.TankHold, settings: null);
        var plain = AgainstCore(Tweaks.Ore.TankHold, NoDepositHeat);
        foreach (var home in new[] { hot.Base, plain.Base })
            home.Reactor.Add(new ReactorState(home.Reactor.HeatCapacity, 0));

        var (injected, _) = Compare(hot, plain, ticks: 1);

        Assert.Equal(plain.Tank.OreHeld, hot.Tank.OreHeld);
        Assert.True(hot.Tank.OreHeld < Tweaks.Ore.TankHold, "the store still takes the ore");
        Assert.Equal(0.0, injected, 6);
    }

    [Fact]
    public void BurningOreToRestartADarkBase_BringsNoDepositHeat()
    {
        var hot = AgainstCore(19, settings: null, energyNetwork: true);
        var plain = AgainstCore(19, NoDepositHeat, energyNetwork: true);
        // The base goes dark first, with the tank away from the core.
        foreach (var s in new[] { hot, plain })
        {
            s.Base.DrawCharge(s.Base.Charge);
            s.Tank.Position = s.Base.SpawnPosition;
            s.World.Advance(_ => default);
            Assert.True(s.World.IsBaseDark(s.Base));
            s.Tank.Position = TestHelpers.AgainstCore(s.Base);
        }

        var (injected, _) = Compare(hot, plain, ticks: 1);

        Assert.True(hot.Tank.OreHeld < 19, "the hold burned");
        Assert.Equal(0.0, injected, 6);
    }

    [Fact]
    public void ARelay_BanksIntoTheSameStore_WithTheSameHeat()
    {
        var hot = RelayHeatTests.CreateScenario();
        var plain = RelayHeatTests.CreateScenario(simulationSettings: NoDepositHeat);
        foreach (var s in new[] { hot, plain })
        {
            RelayHeatTests.PlantRelay(s);
            for (int i = 0; i < 12; i++)
                s.World.Advance(_ => default);
            s.Tank.Position = s.RelayPos;
            s.Tank.Hold.Ore = 10;
        }
        int store = hot.Base.Store.Ore;

        var (injected, _) = Compare(hot, plain, ticks: Tweaks.Perf.TargetFps);

        int banked = hot.Base.Store.Ore - store;
        Assert.True(banked > 0);
        Assert.Equal(10 - banked, hot.Tank.OreHeld);
        Assert.Equal(banked * (double)Tweaks.Cargo.DepositHeatPerOre, injected, 6);
    }

    /// <summary>
    /// The dock pump comes before the bank: with less reactor room than the pump and the deposit want together, the
    /// hot tank cools exactly as much as without deposit heat, and the deposit gets only what the pump left.
    /// </summary>
    [Fact]
    public void ThePumpComesBeforeTheBank_SoTheDepositTakesOnlyTheRoomLeft()
    {
        int ore = Tweaks.Base.HomeAbsorbOre;
        var hot = AgainstCore(ore, settings: null);
        var plain = AgainstCore(ore, NoDepositHeat);
        const int roomUnits = 4;   // 2,000 energy: less than the 2,500 the deposit alone brings
        foreach (var s in new[] { hot, plain })
        {
            TankHeatTestHelpers.SetTankHeat(s.Tank, 80f);
            s.Base.Reactor.Add(new ReactorState(s.Base.Reactor.HeatCapacity, 0));
            s.Base.Reactor.Exhaust(new ReactorState(roomUnits, 0));
        }
        double room = hot.Base.ReactorRoomEnergy;
        Assert.True(room < ore * (double)Tweaks.Cargo.DepositHeatPerOre);

        var (injected, _) = Compare(hot, plain, ticks: 1);

        Assert.Equal(plain.Tank.Heat, hot.Tank.Heat);
        Assert.True(plain.Tank.Heat < 80f, "the pump cooled the tank");
        Assert.Equal(0, hot.Tank.OreHeld);
        // The deposit filled what the cooling left, and no more: less than it brings.
        Assert.InRange(injected, 0.0, room);
        Assert.True(injected < ore * (double)Tweaks.Cargo.DepositHeatPerOre);
    }

    [Fact]
    public void TheDefault_IsTheTweak() =>
        Assert.Equal(Tweaks.Cargo.DepositHeatPerOre, SimulationSettings.FromTweaks().BaseDepositHeatPerOre);

    /// <summary>Readers tell a core bank from a relay's by the position: a core bank is at the core's centre.</summary>
    [Fact]
    public void ACoreBank_IsRecordedAtTheCore()
    {
        var s = AgainstCore(5, settings: null);
        s.World.Advance(_ => default);
        Assert.Contains(s.World.StepEvents.ToArray(),
            e => e.Kind == StepEventKind.OreUnloaded && e.Count == 5 && e.Position == s.Base.Position);
        Assert.Equal(CoreDropOff.Banks, s.Tank.CoreDropOff);
    }
}
