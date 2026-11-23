namespace Emberward.Core.Entities.Powerups;

using Emberward.Core.Config;
using Emberward.Core.Entities.Bases;
using Emberward.Core.Entities.Emberites;
using Emberward.Core.Entities.Machines;
using Emberward.Core.Entities.Tanks;
using Emberward.Core.Match;
using Emberward.Core.Rendering;
using Emberward.Core.Snapshots;
using Emberward.Core.Terrain;
using Emberward.Core.Types;

/// <summary>
/// Powerup drops (DESIGN.md §6.8), in a world whose rules run them (<see cref="MatchRules.Powerups"/>): pickups that
/// land on a fixed, seeded schedule. The next drop's place, kind and time are decided a full period before it lands
/// and are public (<see cref="Forecast"/>), fog or no fog; the first living tank to touch a landed drop takes it.
/// </summary>
/// <remarks>
/// <para>
/// The system keeps its own state: the schedule, the drops on the ground and each tank's running effects, by the
/// tank's colour, so <see cref="Tank"/> has no powerup fields. A tank reads what its effects change from here
/// (<see cref="EffectsOf"/>, <see cref="ReduceDamage"/>).
/// </para>
/// <para>
/// Cost follows what is out: a step looks at each tank's timers and tests each drop on the ground against each tank.
/// A site is chosen once per drop from a bounded number of draws (<c>PowerupDrops.Sites.cs</c>); nothing here scans
/// the map. The drops have a random stream of their own, so a world without them draws nothing more or less.
/// </para>
/// </remarks>
public sealed partial class PowerupDrops
{
    private readonly TerrainGrid _terrain;
    private readonly TankList _tanks;
    private readonly TankBases _bases;
    private readonly MachineList _machines;
    private readonly EmberiteSwarm? _swarm;
    private readonly TankShieldSystem? _shield;
    private readonly IReadOnlyList<Position> _hotPockets;
    private readonly IReadOnlyList<Position> _frostPockets;
    private readonly StepEventLog _events;
    private FastRandom _rng;

    // Steps taken: the schedule's clock.
    private int _tick;

    // The drop to come: decided when the one before it landed (the first, when the match was set up).
    private bool _hasNext;
    private Position _nextSite;
    private PowerupKind _nextKind;
    private int _nextLandTick;

    // The drops lying on the ground, oldest first.
    private readonly List<GroundDrop> _ground = new();

    // The kinds still to come this round, from _bagNext on; and the last kind dealt, -1 before the first.
    private readonly PowerupKind[] _bag = new PowerupKind[PowerupKinds.Count];
    private int _bagNext = PowerupKinds.Count;
    private int _lastKind = -1;

    // Drops scheduled so far: the fairness goal alternates on it.
    private int _dropCount;

    // Whose turn a contested drop is: this round's order of the bases (by colour), the next place in it, and the
    // base of the last turn taken, -1 before the first (TakeTurn).
    private readonly List<int> _turnOrder = new();
    private int _turnNext;
    private int _lastTurn = -1;

    // The hot core, when the world has one: its pocket is the widest.
    private Position? _core;

    // The sites of the last two drops decided, newest first: the next keeps away from both.
    private Position? _lastSite;
    private Position? _siteBefore;

    // Each tank's running effects, by its colour; grown as tanks first take a timed powerup.
    private readonly List<TankEffects> _effects = new();

    [NotSaved("scratch: a site check fills it before it reads it")]
    private readonly List<Machine> _nearMachines = new();

    public PowerupDrops(TerrainGrid terrain, TankList tanks, TankBases bases, MachineList machines, EmberiteSwarm? swarm,
        TankShieldSystem? shield, IReadOnlyList<Position> hotPockets, IReadOnlyList<Position> frostPockets,
        StepEventLog events, int seed)
    {
        _terrain = terrain;
        _tanks = tanks;
        _bases = bases;
        _machines = machines;
        _swarm = swarm;
        _shield = shield;
        _hotPockets = hotPockets;
        _frostPockets = frostPockets;
        _events = events;
        // Hashed, so that matches of neighbouring seeds do not start with kindred draws.
        _rng = new FastRandom(FastRandom.Hash32(unchecked((uint)seed)));
    }

    /// <summary>
    /// Decides the first drop, once the level, the bases and the pockets are in place (<paramref name="core"/> is the
    /// hot core, if the world has one). From here on one upcoming drop is always known.
    /// </summary>
    public void Begin(Position? core)
    {
        _core = core;
        Schedule(Tweaks.Powerups.FirstDropTicks);
    }

    /// <summary>The next drop: where it lands, what it is and the ticks until it does.</summary>
    public PowerupForecast Forecast =>
        _hasNext ? new PowerupForecast(true, _nextSite, _nextKind, Math.Max(0, _nextLandTick - _tick)) : default;

    /// <summary>How many drops lie on the ground.</summary>
    public int GroundCount => _ground.Count;

    /// <summary>
    /// One step: running effects count down and end; each drop on the ground is taken by a tank touching it or, after
    /// <see cref="Tweaks.Powerups.GroundSeconds"/>, is gone; then the forecast drop lands when its time has come (and
    /// the next is decided), and a tank already on the spot takes it in the same step. A drop lies no longer than the
    /// period, and the old one goes before the new one lands, so no step has two on the ground.
    /// </summary>
    public void Advance()
    {
        _tick++;
        // The first drop was decided before the first step, whose log is the first that can tell of it.
        if (_tick == 1 && _hasNext)
            _events.Record(new StepEvent(StepEventKind.PowerupAnnounced, _nextSite, Count: (int)_nextKind));
        AdvanceEffects();
        SettleGround(from: 0);
        if (_hasNext && _tick >= _nextLandTick)
        {
            Land();
            SettleGround(from: _ground.Count - 1);
        }
    }

    /// <summary>
    /// Each drop on the ground from index <paramref name="from"/> on is taken by a tank touching it, or is gone when
    /// its time is up.
    /// </summary>
    private void SettleGround(int from)
    {
        for (int i = from; i < _ground.Count; i++)
        {
            GroundDrop drop = _ground[i];
            Tank? taker = TakerOf(drop.Site);
            if (taker == null && _tick < drop.ExpireTick)
                continue;
            if (taker != null)
                Give(taker, drop);
            else
                _events.Record(new StepEvent(StepEventKind.PowerupExpired, drop.Site, Count: (int)drop.Kind));
            _ground.RemoveAt(i--);
        }
    }

    /// <summary>The forecast drop lands, and the one after it is decided and announced.</summary>
    private void Land()
    {
        Position site = _nextSite;
        _ground.Add(new GroundDrop { Site = site, Kind = _nextKind, ExpireTick = _tick + Tweaks.Powerups.GroundTicks });
        _events.Record(new StepEvent(StepEventKind.PowerupLanded, site, Count: (int)_nextKind));
        Schedule(_tick + Tweaks.Powerups.DropPeriodTicks);
        _events.Record(new StepEvent(StepEventKind.PowerupAnnounced, _nextSite, Count: (int)_nextKind));
    }

    /// <summary>Decides the drop that lands at <paramref name="landTick"/>: its kind from the bag, its site by the fairness goal whose turn it is.</summary>
    private void Schedule(int landTick)
    {
        _nextKind = NextKind();
        _nextSite = ChooseSite(contested: _dropCount % 2 == 1);
        (_siteBefore, _lastSite) = (_lastSite, _nextSite);
        _nextLandTick = landTick;
        _hasNext = true;
        _dropCount++;
    }

    /// <summary>
    /// The next kind from the shuffle bag: every kind once per round, in an order shuffled afresh each round, and
    /// never the kind that ended the round before first.
    /// </summary>
    private PowerupKind NextKind()
    {
        if (_bagNext >= _bag.Length)
        {
            for (int i = 0; i < _bag.Length; i++)
                _bag[i] = (PowerupKind)i;
            for (int i = _bag.Length - 1; i > 0; i--)
            {
                int j = _rng.NextInt(i + 1);
                (_bag[i], _bag[j]) = (_bag[j], _bag[i]);
            }
            if ((int)_bag[0] == _lastKind)
            {
                int other = 1 + _rng.NextInt(_bag.Length - 1);
                (_bag[0], _bag[other]) = (_bag[other], _bag[0]);
            }
            _bagNext = 0;
        }
        PowerupKind kind = _bag[_bagNext++];
        _lastKind = (int)kind;
        return kind;
    }

    /// <summary>
    /// The tank that takes a drop at <paramref name="site"/> this step: of the living tanks whose sprite reaches the
    /// drop's disc, the one whose centre is nearest the site; of two as near, the one in the lower slot.
    /// </summary>
    private Tank? TakerOf(Position site)
    {
        Tank? taker = null;
        int nearest = int.MaxValue;
        foreach (Tank tank in _tanks.Tanks)
        {
            if (tank.IsDead || tank.Color < 0 ||
                !TankSpriteShape.ReachesDisc(tank.Direction, tank.Position, site, Tweaks.Powerups.PickupDiameter))
                continue;
            int distance = Position.DistanceSquared(tank.Position, site);
            if (distance < nearest)
            {
                nearest = distance;
                taker = tank;
            }
        }
        return taker;
    }

    /// <summary><paramref name="tank"/> takes <paramref name="drop"/>: an overhaul acts at once, a timed kind starts (or starts over).</summary>
    private void Give(Tank tank, GroundDrop drop)
    {
        if (drop.Kind == PowerupKind.Overhaul)
        {
            tank.Reactor.Add(new Resources.ReactorState(0, tank.Reactor.HealthCapacity));
            _shield?.Clear(tank);
            tank.CoolFromPickup(_terrain.Ledger);
        }
        else
        {
            while (_effects.Count <= tank.Color)
                _effects.Add(default);
            TankEffects effects = _effects[tank.Color];
            effects.Set(drop.Kind, PowerupKinds.DurationTicks(drop.Kind));
            _effects[tank.Color] = effects;
        }
        _events.Record(new StepEvent(StepEventKind.PowerupCollected, drop.Site, tank.Color, Count: (int)drop.Kind));
    }

    /// <summary>Counts every running effect down a tick; one that runs out, and every effect of a tank that died, ends.</summary>
    private void AdvanceEffects()
    {
        for (int color = 0; color < _effects.Count; color++)
        {
            TankEffects effects = _effects[color];
            if (!effects.Any || color >= _tanks.Tanks.Count)
                continue;
            Tank tank = _tanks.Tanks[color];
            for (int k = 0; k < PowerupKinds.Count; k++)
            {
                var kind = (PowerupKind)k;
                int left = effects.Get(kind);
                if (left <= 0)
                    continue;
                left = tank.IsDead ? 0 : left - 1;
                effects.Set(kind, left);
                if (left == 0)
                    _events.Record(new StepEvent(StepEventKind.PowerupEnded, tank.Position, color, Count: k));
            }
            if (effects.Bulwark == 0)
                effects.Carry = 0;
            _effects[color] = effects;
        }
    }

    /// <summary>What <paramref name="tank"/>'s running powerups change about its digging and its shells this tick.</summary>
    internal PowerupEffects EffectsOf(Tank tank)
    {
        if (tank.Color < 0 || tank.Color >= _effects.Count)
            return PowerupEffects.None;
        TankEffects effects = _effects[tank.Color];
        return new PowerupEffects(effects.Drill > 0, effects.Overcharge > 0 ? Tweaks.Powerups.OverchargeDamageFactor : 1);
    }

    /// <summary>
    /// What is left of <paramref name="amount"/> damage to <paramref name="tank"/>: all of it, or with a bulwark running
    /// one part in <see cref="Tweaks.Powerups.BulwarkDamageDivisor"/>, before the shield takes its share, so shield and
    /// hull both lose half as much. The remainder of the division is carried to the tank's next hit, so that damage
    /// that comes a point at a time (heat) is halved too and not rounded away or let through whole.
    /// </summary>
    internal int ReduceDamage(Tank tank, int amount)
    {
        if (tank.Color < 0 || tank.Color >= _effects.Count)
            return amount;
        TankEffects effects = _effects[tank.Color];
        if (effects.Bulwark <= 0)
            return amount;
        long whole = (long)amount + effects.Carry;
        effects.Carry = (int)(whole % Tweaks.Powerups.BulwarkDamageDivisor);
        _effects[tank.Color] = effects;
        return (int)(whole / Tweaks.Powerups.BulwarkDamageDivisor);
    }

    /// <summary>What the HUD and the renderer read of <paramref name="tank"/>'s running powerups.</summary>
    public TankPowerupView ViewOf(Tank tank)
    {
        TankEffects effects = tank.Color >= 0 && tank.Color < _effects.Count ? _effects[tank.Color] : default;
        return new TankPowerupView(true, effects.Overcharge, effects.Bulwark, effects.Drill);
    }

    /// <summary>Writes the drops on the ground into <paramref name="destination"/>, oldest first; returns how many.</summary>
    public int CopyRenderStates(Span<PowerupRenderState> destination)
    {
        int count = Math.Min(destination.Length, _ground.Count);
        for (int i = 0; i < count; i++)
        {
            GroundDrop drop = _ground[i];
            destination[i] = new PowerupRenderState(drop.Site, drop.Kind, Math.Max(0, drop.ExpireTick - _tick),
                Tweaks.Powerups.PickupDiameter / 2f);
        }
        return count;
    }

    /// <summary>Mixes the forecast, the drops on the ground and every tank's running effects, for <see cref="WorldStateHash.Compute"/>.</summary>
    internal ulong MixState(ulong hash)
    {
        hash = Fnv1a.Mix(hash, _tick);
        hash = Fnv1a.Mix(hash, _hasNext);
        hash = Fnv1a.Mix(Fnv1a.Mix(hash, _nextSite.X), _nextSite.Y);
        hash = Fnv1a.Mix(hash, (int)_nextKind);
        hash = Fnv1a.Mix(hash, _nextLandTick);
        hash = Fnv1a.Mix(hash, _ground.Count);
        foreach (GroundDrop drop in _ground)
        {
            hash = Fnv1a.Mix(Fnv1a.Mix(hash, drop.Site.X), drop.Site.Y);
            hash = Fnv1a.Mix(hash, (int)drop.Kind);
            hash = Fnv1a.Mix(hash, drop.ExpireTick);
        }
        hash = Fnv1a.Mix(hash, _effects.Count);
        foreach (TankEffects effects in _effects)
        {
            hash = Fnv1a.Mix(hash, effects.Overcharge);
            hash = Fnv1a.Mix(hash, effects.Bulwark);
            hash = Fnv1a.Mix(hash, effects.Drill);
            hash = Fnv1a.Mix(hash, effects.Carry);
        }
        return hash;
    }

    /// <summary>
    /// Mixes what decides the drops to come and shows in none yet: the random stream, the rest of the bag and the
    /// round of turns the contested sites go by. For the sync digest's latent part.
    /// </summary>
    internal ulong MixLatent(ulong hash)
    {
        hash = Fnv1a.Mix(hash, _rng.State);
        foreach (PowerupKind kind in _bag)
            hash = Fnv1a.Mix(hash, (int)kind);
        hash = Fnv1a.Mix(hash, _bagNext);
        hash = Fnv1a.Mix(hash, _lastKind);
        hash = Fnv1a.Mix(hash, _dropCount);
        hash = Fnv1a.Mix(hash, _turnOrder.Count);
        foreach (int color in _turnOrder)
            hash = Fnv1a.Mix(hash, color);
        hash = Fnv1a.Mix(hash, _turnNext);
        hash = Fnv1a.Mix(hash, _lastTurn);
        if (_siteBefore is Position before)
            hash = Fnv1a.Mix(Fnv1a.Mix(hash, before.X), before.Y);
        return _core is Position core ? Fnv1a.Mix(Fnv1a.Mix(hash, core.X), core.Y) : hash;
    }

    /// <summary>A drop on the ground: where, what, and the tick it is gone at if nobody takes it.</summary>
    private struct GroundDrop
    {
        public Position Site;
        public PowerupKind Kind;
        public int ExpireTick;
    }

    /// <summary>A tank's running effects: ticks left of each timed kind, and the bulwark's carried remainder.</summary>
    private struct TankEffects
    {
        public int Overcharge;
        public int Bulwark;
        public int Drill;
        public int Carry;

        public readonly bool Any => Overcharge > 0 || Bulwark > 0 || Drill > 0;

        public readonly int Get(PowerupKind kind) => kind switch
        {
            PowerupKind.Overcharge => Overcharge,
            PowerupKind.Bulwark => Bulwark,
            PowerupKind.Drill => Drill,
            _ => 0,
        };

        public void Set(PowerupKind kind, int ticks)
        {
            switch (kind)
            {
                case PowerupKind.Overcharge: Overcharge = ticks; break;
                case PowerupKind.Bulwark: Bulwark = ticks; break;
                case PowerupKind.Drill: Drill = ticks; break;
            }
        }
    }
}
