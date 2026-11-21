namespace Emberward.Desktop.Rendering.Entities;

using Emberward.Core;
using Emberward.Core.Entities;
using Emberward.Core.Rendering;
using Emberward.Core.Sensing;
using Emberward.Core.Types;

/// <summary>
/// A tank's shield (DESIGN.md §4.7). While it holds, a faint bubble of swirling blue with a living hexagonal lattice sits
/// around the tank, showing how much shield is left: its outer edge goes first as it drains, a small core at the centre
/// last. When the shield takes a hit, for a moment the veins light up with a wavefront from the impact and a red patch
/// glows where the shot landed; the hit that breaks it sends <see cref="BrokenFill"/>, and the whole bubble flashes and
/// collapses. Fills are read each step from <see cref="World.TankShieldOf"/> and hits from <see cref="World.StepEvents"/>.
/// It costs one quad per visible shielded tank a frame and one pass over the tanks a step, and draws nothing while no
/// tank has shield (or the match runs none). Output only.
/// </summary>
public sealed class TankShieldQuads
{
    /// <summary>How long the pulse lives, in simulation steps (about 0.4 s at 24 steps a second).</summary>
    private const int PulseSteps = 9;

    private const float Radius = 8.5f;

    /// <summary>
    /// The length of the shader's animation loop, in seconds. It divides the entity layer's phase wrap (1000 s), so the
    /// loop runs on unbroken when the phase wraps; the shader turns everything a whole number of times per loop.
    /// </summary>
    public const float LoopSeconds = 40f;

    /// <summary>
    /// The fill a pulse sends for the hit that broke the shield: the shader then shows the whole bubble flashing and
    /// collapsing to the centre, rather than the nothing an empty shield has left.
    /// </summary>
    public const float BrokenFill = -1f;

    /// <summary>The hit angle sent when there is no hit point (the shield at rest, or a bite): past any real angle.</summary>
    public const float NoHitAngle = 10f;

    private struct Pulse
    {
        public int Color;
        public long Step;
        public float HitAngle;
        public float Fill;
    }

    private readonly List<Pulse> _pulses = new();
    private readonly Dictionary<int, Position> _before = new();
    // Each tank's shield fill by colour, from the last step, for the tanks with any shield.
    private readonly Dictionary<int, float> _fills = new();
    private long _step;

    /// <summary>Call after each simulation step, while <see cref="World.StepEvents"/> still holds that step's events.</summary>
    public void CaptureStep(World world)
    {
        _step++;
        for (int i = _pulses.Count - 1; i >= 0; i--)
        {
            if (_step - _pulses[i].Step >= PulseSteps + 1)
                _pulses.RemoveAt(i);
        }
        foreach (ref readonly StepEvent e in world.StepEvents)
        {
            if (e.Kind is not (StepEventKind.TankShieldHit or StepEventKind.TankShieldBroken))
                continue;
            int color = e.Color;
            var tank = FindTank(world, color);
            if (tank == null)
                continue;
            // A bite has no direction: it never replaces the pulse of a shell that landed in the same step.
            bool shell = e.Cause == DamageCause.EnemyFire;
            int existing = _pulses.FindIndex(p => p.Color == color && p.Step == _step);
            if (!shell && existing >= 0)
                continue;
            // Projectiles run before tanks move, so the impact is measured from where the tank stood at the step's start.
            var from = _before.TryGetValue(color, out Position before) ? before : tank.Position;
            int dx = e.Position.X - from.X;
            int dy = e.Position.Y - from.Y;
            var pulse = new Pulse
            {
                Color = color,
                Step = _step,
                HitAngle = shell && (dx != 0 || dy != 0) ? MathF.Atan2(dy, dx) : NoHitAngle,
                Fill = e.Kind == StepEventKind.TankShieldBroken ? BrokenFill : world.TankShieldOf(tank).ShieldFill,
            };
            // A hit restarts the tank's pulse.
            _pulses.RemoveAll(p => p.Color == color);
            _pulses.Add(pulse);
        }
        _fills.Clear();
        foreach (var tank in world.TankList.Tanks)
        {
            _before[tank.Color] = tank.Position;
            var shield = world.TankShieldOf(tank);
            if (shield.Active && shield.ShieldFill > 0f)
                _fills[tank.Color] = shield.ShieldFill;
        }
    }

    private static Emberward.Core.Entities.Tanks.Tank? FindTank(World world, int color)
    {
        foreach (var tank in world.TankList.Tanks)
        {
            if (tank.Color == color)
                return tank;
        }
        return null;
    }

    /// <summary>How many pulses are showing (the tanks hit within the last <c>PulseSteps</c> steps).</summary>
    public int PulseCount => _pulses.Count;

    /// <summary>How many tanks had any shield at the last step: those that show the bubble at rest.</summary>
    public int ShieldedCount => _fills.Count;

    public void Emit(EntityQuads quads, EntityMotion motion, VisionMap? vision)
    {
        if (_pulses.Count == 0 && _fills.Count == 0)
            return;
        float scale = quads.View.PixelScale;
        float stepNow = _step + quads.Alpha;
        float loop = quads.Phase % LoopSeconds / LoopSeconds;
        ReadOnlySpan<TankRenderState> tanks = motion.Tanks;
        for (int i = 0; i < tanks.Length; i++)
        {
            ref readonly TankRenderState tank = ref tanks[i];
            if (tank.IsDead)
                continue;
            // A fresh hit draws the pulse (the bubble at rest is part of it); otherwise a shielded tank shows the bubble.
            float fill, age = 1f, hitAngle = NoHitAngle;
            int pulse = FindPulse(tank.Color, stepNow);
            if (pulse >= 0)
            {
                fill = _pulses[pulse].Fill;
                age = Math.Max(0f, (stepNow - _pulses[pulse].Step) / PulseSteps);
                hitAngle = _pulses[pulse].HitAngle;
            }
            else if (!_fills.TryGetValue(tank.Color, out fill))
            {
                continue;
            }
            if (!Sight.Sees(vision, tank.Position))
                continue;
            var (start, drift) = motion.TankMotion(i);
            if (quads.IsOffScreen(quads.ScreenX(start.X), quads.ScreenY(start.Y), (Radius + 2f) * scale))
                continue;
            float cx = EntityQuads.Centre(start.X);
            float cy = EntityQuads.Centre(start.Y);
            quads.Add(cx, cy, 1f, 0f, -Radius, Radius, -Radius, Radius, TankSprites.GetPixelColor(1, tank.Color),
                EntityShape.KindTankShield, fill, age, hitAngle, loop, drift);
        }
    }

    private int FindPulse(int color, float stepNow)
    {
        for (int i = 0; i < _pulses.Count; i++)
        {
            if (_pulses[i].Color == color && stepNow - _pulses[i].Step < PulseSteps)
                return i;
        }
        return -1;
    }
}
