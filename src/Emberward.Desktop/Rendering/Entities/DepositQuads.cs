namespace Emberward.Desktop.Rendering.Entities;

using Emberward.Core;
using Emberward.Core.Config;
using Emberward.Core.Rendering;
using Emberward.Core.Sensing;
using Emberward.Core.Types;
using Emberward.Desktop.Gui;

/// <summary>
/// Where banked pyrite goes (docs/pyrite-deposit-feedback-design.md §4): a pour of crystals from the tank's hold into
/// the core or relay that took it, a glow on each core that follows its reactor's real heat store and pulses as the
/// crystals land, a small flash at a relay, a rising <c>+N</c> for the player's own side, and a label on the player's
/// nearest relay with the base store it shares. Read from <see cref="World.StepEvents"/>
/// (<see cref="StepEventKind.OreUnloaded"/>, <see cref="StepEventKind.RestartOreBurned"/>) after each step. What it
/// draws is a function of the captured steps and the step's progress, with no clock or random source. Output only:
/// a late joiner starts with nothing in flight.
/// </summary>
public sealed class DepositQuads
{
    /// <summary>How near (cells, along both axes) one of the player's powered relays must be to carry the store label.</summary>
    public const int StoreLabelReach = 24;

    /// <summary>The store label's title line.</summary>
    public const string StoreTitle = "BASE STORE";

    /// <summary>Events from the same tank at the same core this many steps apart join one pour (a bank is 10 a tick).</summary>
    public const int JoinSteps = 6;

    /// <summary>At a relay a unit comes every 6 steps; a visit's count keeps running while they come this close.</summary>
    public const int RelayVisitSteps = 30;

    /// <summary>The most crystals a pour at the core shows, whatever it carries.</summary>
    public const int MaxCrystalsPerPour = 12;

    /// <summary>How long a crystal flies, in steps (0.45 s); they leave the hold a step apart.</summary>
    public const int FlightSteps = 11;

    /// <summary>What each landing adds to the receiver's pulse, which halves every <see cref="PulseHalfLifeSteps"/> (0.3 s).</summary>
    public const float PulsePerLanding = 0.12f;
    public const float PulseHalfLifeSteps = 7.2f;

    /// <summary>How long a <c>+N</c> rises (1.2 s) after the pour's first landing, and stays after its last.</summary>
    public const int FloaterSteps = 29;

    private const int MaxPours = 16;
    private const int MaxCrystals = MaxPours * MaxCrystalsPerPour;
    private const int MaxFlashes = 8;
    private const int FloaterFadeSteps = 10;
    private const float FloaterRise = 3f;
    private const int RelayFlashSteps = 7;
    private const float CoreGlowRadius = 9f;
    private const float RelayGlowRadius = 4f;
    private const float CrystalHalf = 0.7f;
    private const float ArcApex = 2.5f;
    private const float HoldOffset = 1.5f;
    private const float HeatEase = 0.25f;
    private const float PhaseCycleSeconds = 4f;
    private const float DotCells = 0.3f;

    public static readonly Color Gold = new(255, 196, 70);
    public static readonly Color Pale = new(255, 240, 190);
    public static readonly Color DimGold = new(205, 165, 90);
    public static readonly Color Cyan = new(150, 235, 255);

    private struct Pour
    {
        public long Id;
        public bool Active;
        public int Color;
        public Position Receiver;
        public bool AtCore;
        public bool Burn;
        public bool Own;
        public long LastEventStep;
        public long FirstLaunchStep;
        public long NextLaunchStep;
        public int Units;
        public int Crystals;
        public PositionF LastFrom;
        public string Text;
        public string? Store;
    }

    private struct Crystal
    {
        public long PourId;
        public int Pour;
        public bool Active;
        public bool Launched;
        public long LaunchStep;
        public PositionF From;
        public float Side;
        public float Seed;
    }

    private struct Glow
    {
        public Position Position;
        public int Color;
        public float Heat;
        public float Pulse;
        public long PulseStep;
        public bool Burn;
    }

    private struct Flash
    {
        public Position Position;
        public long Step;
    }

    private readonly Pour[] _pours = new Pour[MaxPours];
    private readonly Crystal[] _crystals = new Crystal[MaxCrystals];
    private readonly Flash[] _flashes = new Flash[MaxFlashes];
    private readonly List<Glow> _glows = new();
    private readonly List<(int Color, PositionF At)> _tanks = new();
    private int _pourNext, _crystalNext, _flashNext;
    private long _nextPourId = 1;
    private long _step;
    private bool _held;
    private float _heldAlpha, _lastAlpha;

    private Position? _storeLabel;
    private int _labelStore = -1, _labelRate = -1;
    private string _storeText = "";
    private string? _rateText;

    /// <summary>The steps captured so far.</summary>
    public long Step => _step;

    /// <summary>
    /// Where the store label stands (the player's nearest powered relay in reach, out of its base, while the base is
    /// not dark), or null.
    /// </summary>
    public Position? StoreLabel => _storeLabel;

    /// <summary>Call after each simulation step, while <see cref="World.StepEvents"/> still holds that step's events.</summary>
    public void CaptureStep(World world, int localSlot, OreLedgerView ledger)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(ledger);
        _step++;
        _held = false;
        _lastAlpha = 0f;
        CaptureBases(world);
        _tanks.Clear();
        foreach (var tank in world.TankList.Tanks)
            if (!tank.IsDead)
                _tanks.Add((tank.Color, new PositionF(tank.Position.X, tank.Position.Y)));

        var player = localSlot >= 0 && localSlot < world.TankList.Count ? world.TankList.Tanks[localSlot] : null;
        int localColor = player?.Color ?? -1;
        foreach (ref readonly StepEvent e in world.StepEvents)
            if (e.Kind is StepEventKind.OreUnloaded or StepEventKind.RestartOreBurned && e.Count > 0)
                AddDeposit(e, world, localColor);
        AdvanceCrystals();
        for (int i = 0; i < MaxPours; i++)
            if (_pours[i].Active && _step > Math.Max(LastLanding(_pours[i]) + FloaterSteps, _pours[i].LastEventStep + RelayVisitSteps))
                _pours[i].Active = false;

        CaptureStoreLabel(world, player, ledger);
    }

    /// <summary>
    /// The world takes no step (a pause, or a network tick that carried none): everything holds where it was last
    /// drawn, at that frame's step progress, until the next step is captured.
    /// </summary>
    public void Hold()
    {
        if (_held)
            return;
        _held = true;
        _heldAlpha = _lastAlpha;
    }

    private void CaptureBases(World world)
    {
        var bases = world.TankBases.Bases;
        if (_glows.Count != bases.Count)
        {
            _glows.Clear();
            foreach (var b in bases)
                _glows.Add(new Glow { Position = b.Position, Color = b.Color, PulseStep = long.MinValue / 2 });
        }
        for (int i = 0; i < bases.Count; i++)
        {
            var glow = _glows[i];
            float target = bases[i].IsDestroyed ? 0f : (float)bases[i].HeatStoreFill;
            glow.Position = bases[i].Position;
            glow.Color = bases[i].Color;
            glow.Heat += (target - glow.Heat) * HeatEase;
            _glows[i] = glow;
        }
    }

    private void AddDeposit(in StepEvent e, World world, int localColor)
    {
        bool atCore = e.Target == ImpactTarget.Base;
        bool burn = e.Kind == StepEventKind.RestartOreBurned;
        int index = FindPour(e.Color, e.Position, burn, atCore ? JoinSteps : RelayVisitSteps);
        if (index < 0)
        {
            index = _pourNext;
            _pourNext = (_pourNext + 1) % MaxPours;
            _pours[index] = new Pour
            {
                Id = _nextPourId++,
                Active = true,
                Color = e.Color,
                Receiver = e.Position,
                AtCore = atCore,
                Burn = burn,
                Own = e.Color == localColor,
                FirstLaunchStep = _step,
                NextLaunchStep = _step,
                LastFrom = new PositionF(e.Position.X, e.Position.Y),
                Text = "",
            };
        }
        ref Pour pour = ref _pours[index];
        pour.Units += e.Count;
        pour.LastEventStep = _step;
        // Only the player's side reads numbers, so only its pours build them.
        if (pour.Own)
        {
            pour.Text = burn ? $"+{pour.Units} RESTART" : $"+{pour.Units}";
            if (atCore && !burn)
                foreach (var b in world.TankBases.Bases)
                    if (b.Color == e.Color)
                        pour.Store = $"BASE {b.Store.Ore}";
        }

        int add = atCore ? Math.Min(e.Count, MaxCrystalsPerPour - pour.Crystals) : Math.Min(e.Count, MaxCrystalsPerPour);
        for (int k = 0; k < add; k++)
        {
            long launch = Math.Max(pour.NextLaunchStep, _step);
            pour.NextLaunchStep = launch + 1;
            pour.Crystals++;
            uint hash = Hash((uint)pour.Id, (uint)pour.Crystals);
            _crystals[_crystalNext] = new Crystal
            {
                PourId = pour.Id,
                Pour = index,
                Active = true,
                LaunchStep = launch,
                Side = (hash & 1) == 0 ? 1f : -1f,
                Seed = (hash >> 8) / 16777216f,
            };
            _crystalNext = (_crystalNext + 1) % MaxCrystals;
        }
    }

    private int FindPour(int color, Position receiver, bool burn, int window)
    {
        for (int i = 0; i < MaxPours; i++)
        {
            ref readonly Pour p = ref _pours[i];
            if (p.Active && p.Color == color && p.Receiver == receiver && p.Burn == burn && _step - p.LastEventStep <= window)
                return i;
        }
        return -1;
    }

    private void AdvanceCrystals()
    {
        for (int i = 0; i < MaxCrystals; i++)
        {
            ref Crystal c = ref _crystals[i];
            if (!c.Active)
                continue;
            ref Pour pour = ref _pours[c.Pour];
            if (pour.Id != c.PourId)
            {
                c.Active = false;
                continue;
            }
            if (!c.Launched && c.LaunchStep <= _step)
            {
                pour.LastFrom = LaunchPoint(pour);
                c.From = pour.LastFrom;
                c.Launched = true;
            }
            if (c.Launched && c.LaunchStep + FlightSteps == _step)
                Land(pour);
            if (_step >= c.LaunchStep + FlightSteps)
                c.Active = false;
        }
    }

    /// <summary>The tank's hold, 1.5 cells from its centre toward the receiver; where it was last seen once it is gone.</summary>
    private PositionF LaunchPoint(in Pour pour)
    {
        foreach (var (color, at) in _tanks)
        {
            if (color != pour.Color)
                continue;
            float dx = pour.Receiver.X - at.X, dy = pour.Receiver.Y - at.Y;
            float length = MathF.Sqrt(dx * dx + dy * dy);
            return length < 1e-3f ? at : new PositionF(at.X + dx / length * HoldOffset, at.Y + dy / length * HoldOffset);
        }
        return pour.LastFrom;
    }

    private void Land(in Pour pour)
    {
        if (!pour.AtCore)
        {
            _flashes[_flashNext] = new Flash { Position = pour.Receiver, Step = _step };
            _flashNext = (_flashNext + 1) % MaxFlashes;
            return;
        }
        for (int i = 0; i < _glows.Count; i++)
        {
            var glow = _glows[i];
            if (glow.Position != pour.Receiver)
                continue;
            glow.Pulse = MathF.Min(1f, PulseAt(glow, _step) + PulsePerLanding);
            glow.PulseStep = _step;
            glow.Burn = pour.Burn;
            _glows[i] = glow;
        }
    }

    private static float PulseAt(in Glow glow, float stepNow) =>
        glow.Pulse * MathF.Pow(0.5f, MathF.Max(0f, stepNow - glow.PulseStep) / PulseHalfLifeSteps);

    private static long LastLanding(in Pour pour) => pour.NextLaunchStep - 1 + FlightSteps;

    private void CaptureStoreLabel(World world, Core.Entities.Tanks.Tank? player, OreLedgerView ledger)
    {
        // Inside its own base the tank has the core beside it; the label is for the field.
        // A dark base refuses ore at its relays, so none is labelled then.
        _storeLabel = player is not { Base: { } home } || player.IsDockedAtOwnBase || world.IsBaseDark(home)
            ? null
            : world.NearestOreDropOff(player, StoreLabelReach);
        if (ledger.Store != _labelStore)
        {
            _labelStore = ledger.Store;
            _storeText = _labelStore.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (ledger.RatePerMinute != _labelRate)
        {
            _labelRate = ledger.RatePerMinute;
            _rateText = _labelRate > 0 ? $"+{_labelRate}/MIN" : null;
        }
    }

    /// <summary>
    /// Where a crystal is at <paramref name="u"/> (0 leaving the hold, 1 landing) of its flight: a short arc whose apex
    /// stands up to <see cref="ArcApex"/> cells off the straight line, on <paramref name="side"/> (1 or -1).
    /// </summary>
    public static PositionF Arc(PositionF from, PositionF to, float side, float u)
    {
        float t = Math.Clamp(u, 0f, 1f);
        float e = t * (0.6f + 0.4f * t);   // speeds up into the receiver
        float dx = to.X - from.X, dy = to.Y - from.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 1e-3f)
            return to;
        // A quadratic's apex is half its control point's offset.
        float offset = 2f * ArcApex * MathF.Min(1f, length / 5f) * side;
        float cx = (from.X + to.X) * 0.5f - dy / length * offset, cy = (from.Y + to.Y) * 0.5f + dx / length * offset;
        float a = (1f - e) * (1f - e), b = 2f * e * (1f - e), c = e * e;
        return new PositionF(a * from.X + b * cx + c * to.X, a * from.Y + b * cy + c * to.Y);
    }

    /// <summary>The core glows and relay flashes, over the ground and under machines and tanks.</summary>
    public void EmitGlows(EntityQuads quads, VisionMap? vision)
    {
        float scale = quads.View.PixelScale;
        float stepNow = StepNow(quads);
        float phase = quads.Phase % PhaseCycleSeconds / PhaseCycleSeconds;
        foreach (var glow in _glows)
        {
            float pulse = PulseAt(glow, stepNow);
            if ((glow.Heat < 0.01f && pulse < 0.01f) || !Sight.Sees(vision, glow.Position))
                continue;
            if (quads.IsOffScreen(quads.ScreenX(glow.Position.X), quads.ScreenY(glow.Position.Y), (CoreGlowRadius + 1f) * scale))
                continue;
            AddGlow(quads, glow.Position, CoreGlowRadius, glow.Heat, pulse, phase, glow.Burn ? 1f : 0f);
        }
        foreach (var flash in _flashes)
        {
            float age = (stepNow - flash.Step) / RelayFlashSteps;
            if (flash.Step <= 0 || age < 0f || age >= 1f || !Sight.Sees(vision, flash.Position))
                continue;
            if (quads.IsOffScreen(quads.ScreenX(flash.Position.X), quads.ScreenY(flash.Position.Y), (RelayGlowRadius + 1f) * scale))
                continue;
            AddGlow(quads, flash.Position, RelayGlowRadius, 0f, 1f - age, phase, 0f);
        }
    }

    private static void AddGlow(EntityQuads quads, Position at, float radius, float heat, float pulse, float phase, float mode) =>
        quads.Add(EntityQuads.Centre(at.X), EntityQuads.Centre(at.Y), 1f, 0f, -radius, radius, -radius, radius,
            radius, 0f, 0f, EntityShape.KindCoreHeat, heat, pulse, phase, mode);

    /// <summary>The crystals in flight, over tanks: they leave the hold.</summary>
    public void EmitCrystals(EntityQuads quads, VisionMap? vision)
    {
        float stepNow = StepNow(quads);
        foreach (ref readonly Crystal c in _crystals.AsSpan())
        {
            if (!c.Active || !c.Launched)
                continue;
            ref readonly Pour pour = ref _pours[c.Pour];
            if (pour.Id != c.PourId || !Sight.Sees(vision, pour.Receiver))
                continue;
            float age = (stepNow - c.LaunchStep) / FlightSteps;
            if (age < 0f || age >= 1f)
                continue;
            // A pour spans its tank and its receiver: cull it by the box round both.
            float midX = (c.From.X + pour.Receiver.X) * 0.5f, midY = (c.From.Y + pour.Receiver.Y) * 0.5f;
            float span = MathF.Abs(c.From.X - pour.Receiver.X) + MathF.Abs(c.From.Y - pour.Receiver.Y);
            if (quads.IsOffScreen(quads.ScreenX(midX), quads.ScreenY(midY), (span + ArcApex + 2f) * quads.View.PixelScale))
                continue;
            var to = new PositionF(pour.Receiver.X, pour.Receiver.Y);
            // Anchored where the step left it and moving on through the step; held, anchored where it was drawn.
            float u0 = (float)(_step - c.LaunchStep) / FlightSteps;
            PositionF at = Arc(c.From, to, c.Side, u0);
            PositionF next = Arc(c.From, to, c.Side, u0 + 1f / FlightSteps);
            PositionF motion = new(next.X - at.X, next.Y - at.Y);
            if (_held)
                (at, motion) = (new PositionF(at.X + motion.X * _heldAlpha, at.Y + motion.Y * _heldAlpha), default);
            float landing = Math.Clamp((age - 0.7f) / 0.3f, 0f, 1f);
            float spin = c.Seed * 6.2831853f + age * 5f;
            quads.Add(EntityQuads.Centre(at.X), EntityQuads.Centre(at.Y), 1f, 0f, -CrystalHalf, CrystalHalf, -CrystalHalf,
                CrystalHalf, pour.Burn ? 1f : 0f, 0f, 0f, EntityShape.KindOreCrystal, age, c.Seed, spin, landing, motion);
        }
    }

    /// <summary>The <c>+N</c> floaters of the player's side and the store label, over everything but the HUD.</summary>
    public void EmitLabels(EntityQuads quads)
    {
        float scale = quads.View.PixelScale;
        float stepNow = StepNow(quads);
        float dot = MathF.Max(DotCells, 1f / scale);
        float small = MathF.Max(DotCells * 0.7f, 1f / scale);
        float line = (GlyphText.GlyphHeight + 1) * dot;
        foreach (ref readonly Pour pour in _pours.AsSpan())
        {
            if (!pour.Active || !pour.Own)
                continue;
            float first = pour.FirstLaunchStep + FlightSteps, end = LastLanding(pour) + FloaterSteps;
            if (stepNow < first || stepNow >= end)
                continue;
            float rise = FloaterRise * MathF.Min(1f, (stepNow - first) / FloaterSteps);
            float alpha = MathF.Min(1f, (end - stepNow) / FloaterFadeSteps);
            // Above the core; beside a relay, clear of its charge label above it and the store label below it.
            float x = EntityQuads.Centre(pour.Receiver.X)
                + (pour.AtCore ? 0f : Tweaks.Machine.BoundingBoxHalfSize + 1.5f + GlyphText.Width(pour.Text, dot) * 0.5f);
            float y = EntityQuads.Centre(pour.Receiver.Y) - (pour.AtCore ? Tweaks.Keep.CoreSize * 0.5f + 2f : 0f) - rise;
            if (quads.IsOffScreen(quads.ScreenX(x), quads.ScreenY(y), 12f * scale))
                continue;
            GlyphText.AddLine(quads, x, y, -line, pour.Text, dot, pour.Burn ? Cyan : Gold, alpha);
            if (pour.Store != null)
                GlyphText.AddLine(quads, x, y, 0.5f, pour.Store, small, Pale, alpha * 0.85f);
        }

        if (_storeLabel is { } relay)
        {
            float x = EntityQuads.Centre(relay.X);
            float y = EntityQuads.Centre(relay.Y) + Tweaks.Machine.BoundingBoxHalfSize + 1.5f;
            if (!quads.IsOffScreen(quads.ScreenX(x), quads.ScreenY(y), 12f * scale))
            {
                float smallLine = (GlyphText.GlyphHeight + 1) * small;
                GlyphText.AddLine(quads, x, y, 0f, StoreTitle, small, DimGold, 0.9f);
                GlyphText.AddLine(quads, x, y, smallLine + 0.3f, _storeText, dot, Pale, 1f);
                if (_rateText != null)
                    GlyphText.AddLine(quads, x, y, smallLine + line + 0.6f, _rateText, small, DimGold, 0.8f);
            }
        }
    }

    /// <summary>The step and its progress as drawn this frame; held, the progress of the last frame drawn before the hold.</summary>
    private float StepNow(EntityQuads quads)
    {
        if (!_held)
            _lastAlpha = quads.Alpha;
        return _step + (_held ? _heldAlpha : quads.Alpha);
    }

    /// <summary>The pours still showing, for tests: units, crystals, receiver, the floater's text (null for a rival's).</summary>
    public IEnumerable<(Position Receiver, int Units, int Crystals, string? Text)> Pours()
    {
        foreach (var pour in _pours)
            if (pour.Active)
                yield return (pour.Receiver, pour.Units, pour.Crystals, pour.Own ? pour.Text : null);
    }

    /// <summary>The pulse of the core at <paramref name="core"/> as of the last captured step, for tests.</summary>
    public float CorePulse(Position core)
    {
        foreach (var glow in _glows)
            if (glow.Position == core)
                return PulseAt(glow, _step);
        return 0f;
    }

    private static uint Hash(uint a, uint b)
    {
        uint h = a * 0x9E3779B1u ^ (b + 0x7F4A7C15u) * 0x85EBCA77u;
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        return h;
    }
}
