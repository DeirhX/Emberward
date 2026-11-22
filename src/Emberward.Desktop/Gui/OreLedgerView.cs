namespace Emberward.Desktop.Gui;

using Emberward.Core;
using Emberward.Core.Config;

/// <summary>
/// The local player's ore as the HUD tells it (docs/pyrite-deposit-feedback-design.md §5.2): how fast the base store is
/// growing (the units banked into it by a tank of the side, <see cref="StepEventKind.OreUnloaded"/>, and mined into it
/// by its harvesters, <see cref="StepEventKind.OreHarvested"/>, over the last <see cref="WindowSeconds"/>), and the
/// player's latest deposit stretched over a pour: the store counting up to its new value, the hold draining, the units
/// banked. Statistics of the step events, not game rules, so they are counted here; display state only. Shared by
/// desktop and browser.
/// </summary>
public sealed class OreLedgerView
{
    /// <summary>The window the rate is counted over.</summary>
    public const int WindowSeconds = 30;

    /// <summary>How long a deposit is stretched over on the HUD, in steps (0.9 s, the length of the pour in the world).</summary>
    public const int PourSteps = 22;

    /// <summary>Deposits by the player this close together (steps) count as one: a bank at the core, a relay visit.</summary>
    public const int JoinSteps = 30;

    private const int WindowSteps = WindowSeconds * Tweaks.Perf.TargetFps;
    private const long Never = long.MinValue / 4;

    private readonly int[] _perStep = new int[WindowSteps];
    private int _next;
    private int _sum;

    private long _step;
    private long _pourStart = Never, _lastDeposit = Never, _countStart = Never, _lastPour = Never;
    private int _pourUnits;
    private int _storeFrom, _storeTo;
    private bool _seen;
    private float _shownHold;

    /// <summary>Units a minute into the player's base store, over the last <see cref="WindowSeconds"/>.</summary>
    public int RatePerMinute => _sum * 60 / WindowSeconds;

    /// <summary>The player's base store as of the last step.</summary>
    public int Store { get; private set; }

    /// <summary>The store as the HUD shows it: counting up from its old value to its new over a deposit's pour.</summary>
    public int DisplayedStore =>
        _storeFrom + (int)MathF.Round((_storeTo - _storeFrom) * Math.Clamp((_step - _countStart) / (float)PourSteps, 0f, 1f));

    /// <summary>The player's hold as the HUD shows it: draining over a deposit's pour instead of in a few ticks.</summary>
    public int DisplayedHold => (int)MathF.Ceiling(_shownHold - 1e-3f);

    /// <summary>The units of the player's latest deposit (a bank at the core, or a relay visit so far).</summary>
    public int DepositUnits { get; private set; }

    /// <summary>Steps since the player's latest deposit began.</summary>
    public long DepositAge => _step - _pourStart;

    /// <summary>Steps since the player's latest deposit last grew (a relay visit grows a unit at a time).</summary>
    public long LastDepositAge => _step - _lastDeposit;

    /// <summary>
    /// True from the player's deposit until its pour has had time to land: ore banked, or burned to restart a dark
    /// base (which the world pours too, though the store does not change).
    /// </summary>
    public bool IsPouring => _step - _lastPour <= PourSteps;

    /// <summary>Call after each simulation step, while <see cref="World.StepEvents"/> still holds its events.</summary>
    public void CaptureStep(World world, int localSlot)
    {
        ArgumentNullException.ThrowIfNull(world);
        _step++;
        int units = 0, deposited = 0, burned = 0;
        if (localSlot >= 0 && localSlot < world.TankList.Count && world.TankList.Tanks[localSlot] is { Base: { } home } player)
        {
            foreach (ref readonly StepEvent e in world.StepEvents)
            {
                if (e.Kind == StepEventKind.RestartOreBurned && e.Color == player.Color)
                    burned += e.Count;
                if (e.Kind is not (StepEventKind.OreUnloaded or StepEventKind.OreHarvested) || e.Color != home.Color)
                    continue;
                units += e.Count;
                if (e.Kind == StepEventKind.OreUnloaded && e.Color == player.Color)
                    deposited += e.Count;
            }
            CaptureStore(home.Store.Ore, deposited);
            CapturePour(deposited + burned);
            CaptureHold(player.OreHeld);
        }
        _sum += units - _perStep[_next];
        _perStep[_next] = units;
        _next = (_next + 1) % WindowSteps;
    }

    private void CaptureStore(int store, int deposited)
    {
        if (!_seen)
        {
            _seen = true;
            _storeFrom = _storeTo = Store = store - deposited;
        }
        if (deposited > 0)
        {
            if (_step - _lastDeposit > JoinSteps)
            {
                _pourStart = _step;
                DepositUnits = 0;
            }
            DepositUnits += deposited;
            _lastDeposit = _step;
            _storeFrom = DisplayedStore;
            _storeTo = store;
            _countStart = _step;
        }
        else if (store != Store)
        {
            // Spent or mined: the count-up, if one runs, moves with it.
            _storeFrom += store - Store;
            _storeTo += store - Store;
        }
        Store = store;
    }

    /// <summary>The units the hold has poured out (banked or burned) since this pour began, which set how fast it drains.</summary>
    private void CapturePour(int poured)
    {
        if (poured <= 0)
            return;
        if (_step - _lastPour > JoinSteps)
            _pourUnits = 0;
        _pourUnits += poured;
        _lastPour = _step;
    }

    private void CaptureHold(int held)
    {
        if (held < _shownHold && IsPouring)
            _shownHold = MathF.Max(held, _shownHold - MathF.Max(1, _pourUnits) / (float)PourSteps);
        else
            _shownHold = held;
    }
}
