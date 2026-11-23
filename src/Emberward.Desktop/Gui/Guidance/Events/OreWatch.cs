namespace Emberward.Desktop.Gui.Guidance.Events;

using System.Globalization;
using Emberward.Core;
using Emberward.Core.Entities.Tanks;

/// <summary>
/// Ore: the first pyrite in the hold, the first pyrite banked, a full hold, a full base store, and what the harvesters
/// banked. The deliveries
/// are counted from <see cref="StepEventKind.OreHarvested"/> after every step, not from the store, so a tank unloading
/// in the same frame is not mistaken for them.
/// </summary>
internal sealed class OreWatch : IEventWatch
{
    /// <summary>Seconds between reports of what the harvesters delivered.</summary>
    private const float HarvestReportSeconds = 20f;

    /// <summary>
    /// Steps without a deposit after which the player's first bank counts as done and is told: the HUD's join window,
    /// well past a relay's unit interval, so a relay visit is told once, with its total.
    /// </summary>
    private const int DepositSettleSteps = OreLedgerView.JoinSteps;

    private Latch _storeFull;
    private Latch _holdingOre;
    private Latch _holdFull;
    private int _harvestedSinceReport;
    private float _lastHarvestReport;
    private bool _depositTold;
    private int _depositUnits;
    private int _stepsSinceDeposit;

    public void Reset()
    {
        _harvestedSinceReport = 0;
        _depositTold = false;
        _depositUnits = 0;
    }

    public void OnStep(World world) => _stepsSinceDeposit++;

    public void OnStepEvent(World world, Tank player, in StepEvent e)
    {
        if (e.Kind == StepEventKind.OreHarvested && e.Color == player.Color)
            _harvestedSinceReport += e.Count;
        else if (e.Kind == StepEventKind.OreUnloaded && e.Color == player.Color && !_depositTold)
        {
            _depositUnits += e.Count;
            _stepsSinceDeposit = 0;
        }
    }

    public void Update(in GuidanceFrame f)
    {
        // The first pyrite the player banks this match, once the bank is done: what the store is for.
        if (!_depositTold && _depositUnits > 0 && _stepsSinceDeposit > DepositSettleSteps)
        {
            _depositTold = true;
            f.Feed.Post(f.Now, "first-deposit", string.Create(CultureInfo.InvariantCulture, $"Pyrite banked: +{_depositUnits} (base {f.Home.Store.Ore:N0})"),
                "The base store pays for what you build inside the network. Touch the core or a powered relay to unload.",
                NotificationKind.Good, cooldown: 1e9f);
        }

        bool storeFilled = _storeFull.Rose(f.Home.Store.IsFull);
        bool firstOre = _holdingOre.Rose(f.Player.OreHeld > 0);
        bool holdFilled = _holdFull.Rose(f.Player.IsOreHoldFull);
        if (!f.Primed)
        {
            _lastHarvestReport = f.Now;
            return;
        }

        if (storeFilled)
            f.Feed.Post(f.Now, "store-full", "Base ore store full",
                "Harvesters idle until you spend ore: dock and buy upgrades (1-7) or build machines.", NotificationKind.Warning);
        // Once each per match: the first ore in the hold, and the first time the hold fills.
        if (firstOre)
            f.Feed.Post(f.Now, "ore-collected", "Pyrite collected",
                "Ore rides in your hold (MATERIALS) and heats the tank, hard from 20. Touch your base's core, or a relay, to bank it; if you die, it spills where you fall.",
                NotificationKind.Good, cooldown: 1e9f);
        if (holdFilled)
            f.Feed.Post(f.Now, "ore-hold-full", "Ore hold full",
                $"The hold takes {f.Player.OreHoldCapacity}, and a full load cooks the tank. Touch your core or a relay to bank it before you dig more ore.",
                NotificationKind.Warning, cooldown: 1e9f);

        if (_harvestedSinceReport > 0 && f.Now - _lastHarvestReport >= HarvestReportSeconds)
        {
            f.Feed.Post(f.Now, "harvested", $"Harvesters delivered +{_harvestedSinceReport} ore",
                $"Base ore store {f.Home.Store.Ore}.", NotificationKind.Good, cooldown: 0f);
            _harvestedSinceReport = 0;
            _lastHarvestReport = f.Now;
        }
    }
}
