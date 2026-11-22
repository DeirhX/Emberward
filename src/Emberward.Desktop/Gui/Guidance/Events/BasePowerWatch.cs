namespace Emberward.Desktop.Gui.Guidance.Events;

using Emberward.Core;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Types;

/// <summary>The base's heat and charge: the heat store filling, charge running low, the engine making charge.</summary>
internal sealed class BasePowerWatch : IEventWatch
{
    /// <summary>Charge counts as low below this share of the base's capacity, while harvesters draw on it.</summary>
    private const double LowChargeShare = 0.1;

    private Latch _heatStoreFull;
    private Latch _chargeLow;
    private Latch _engineRunning;

    public void Reset() { }

    public void OnStepEvent(World world, Tank player, in StepEvent e) { }

    public void Update(in GuidanceFrame f)
    {
        bool heatStoreFull = _heatStoreFull.Rose(f.Network.HeatStoreLevel == StatusLevel.Bad);
        bool chargeLow = _chargeLow.Rose(f.Home.Charge < f.Home.ChargeCapacity * LowChargeShare && f.Network.Harvesters > 0);
        bool engineStarted = _engineRunning.Rose(f.Home.IsEngineRunning);
        if (!f.Primed)
            return;

        if (heatStoreFull)
            f.Feed.Post(f.Now, "heat-full", "Base heat store nearly full",
                "Relays and docking cool less until the radiator catches up.", NotificationKind.Warning);
        if (chargeLow)
            f.Feed.Post(f.Now, "charge-low", "Base charge low",
                "Machines stop when it runs out. Put a relay by hot rock to make more.", NotificationKind.Danger);
        if (engineStarted)
            f.Feed.Post(f.Now, "engine-running", "Base engine making charge",
                "Relay heat is being turned into charge. Hotter sources make more.", NotificationKind.Good, cooldown: 120f);
    }
}
