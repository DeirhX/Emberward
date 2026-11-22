namespace Emberward.Desktop.Rendering.Entities;

using Emberward.Core;
using Emberward.Core.Entities;
using Emberward.Core.Rendering;
using Emberward.Core.Sensing;
using Emberward.Core.Types;

/// <summary>
/// A tank's shield, shown while it blocks hits (DESIGN.md §4.7). A hit brings up a faint bubble of swirling blue with a
/// living hexagonal lattice around the tank, showing how much shield is left (its outer edge goes first as it drains,
/// a small core at the centre last); the veins light up with a wavefront from the impact and a red patch glows where the
/// shot landed. The bubble fades in over a step, stays while hits keep coming (each one refreshes it) and dissolves
/// after the last. The hit that breaks the shield sends <see cref="BrokenFill"/>, and the whole bubble flashes and
/// collapses. Hits are read from <see cref="World.StepEvents"/> after each step. Nothing is drawn, and nothing costs
/// anything, while no hit is fresh. Output only.
/// </summary>
public sealed class TankShieldQuads
{
    /// <summary>How long a hit's own effects (wavefront, red patch, break collapse) last, in simulation steps.</summary>
    private const int HitSteps = 9;

    /// <summary>How long the bubble shows after the latest hit, in simulation steps (0.75 s at 24 steps a second).</summary>
    public const int ShowSteps = 18;

    /// <summary>The bubble fades in over this many steps from the first hit of a run.</summary>
    private const float FadeInSteps = 1f;

    /// <summary>The share of <see cref="ShowSteps"/> after the latest hit when the bubble starts to dissolve.</summary>
    private const float FadeOutFrom = 0.4f;

    private const float Radius = 8.5f;

    /// <summary>
    /// The length of the shader's animation loop, in seconds. It divides the entity layer's phase wrap (1000 s), so the
    /// loop runs on unbroken when the phase wraps; the shader turns everything a whole number of times per loop.
    /// </summary>
    public const float LoopSeconds = 40f;

    /// <summary>The steps of visibility .w carries in its whole part (the loop is its fraction).</summary>
    public const float VisibleLevels = 255f;

    /// <summary>
    /// The fill a pulse sends for the hit that broke the shield: the shader then shows the whole bubble flashing and
    /// collapsing to the centre, rather than the nothing an empty shield has left.
    /// </summary>
    public const float BrokenFill = -1f;

    /// <summary>The hit angle sent when there is no hit point (a bite): past any real angle.</summary>
    public const float NoHitAngle = 10f;

    private struct Pulse
    {
        public int Color;
        /// <summary>The step of the first hit of this run of hits: the bubble fades in from it.</summary>
        public long Started;
        /// <summary>The step of the latest hit: its effects and the bubble's fade out run from it.</summary>
        public long LastHit;
        public float HitAngle;
        public float Fill;
    }

    private readonly List<Pulse> _pulses = new();
    private readonly Dictionary<int, Position> _before = new();
    private long _step;

    /// <summary>Call after each simulation step, while <see cref="World.StepEvents"/> still holds that step's events.</summary>
    public void CaptureStep(World world)
    {
        _step++;
        for (int i = _pulses.Count - 1; i >= 0; i--)
        {
            if (_step - _pulses[i].LastHit >= ShowSteps + 1)
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
            int existing = _pulses.FindIndex(p => p.Color == color);
            if (!shell && existing >= 0 && _pulses[existing].LastHit == _step)
                continue;
            // Projectiles run before tanks move, so the impact is measured from where the tank stood at the step's start.
            var from = _before.TryGetValue(color, out Position before) ? before : tank.Position;
            int dx = e.Position.X - from.X;
            int dy = e.Position.Y - from.Y;
            var pulse = new Pulse
            {
                Color = color,
                // A hit while the bubble shows keeps it up: it refreshes the run rather than fading in again.
                Started = existing >= 0 ? _pulses[existing].Started : _step,
                LastHit = _step,
                HitAngle = shell && (dx != 0 || dy != 0) ? MathF.Atan2(dy, dx) : NoHitAngle,
                Fill = e.Kind == StepEventKind.TankShieldBroken ? BrokenFill : world.TankShieldOf(tank).ShieldFill,
            };
            if (existing >= 0)
                _pulses[existing] = pulse;
            else
                _pulses.Add(pulse);
        }
        foreach (var tank in world.TankList.Tanks)
            _before[tank.Color] = tank.Position;
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

    /// <summary>How many pulses are showing (the tanks hit within the last <see cref="ShowSteps"/> steps).</summary>
    public int PulseCount => _pulses.Count;

    /// <summary>
    /// How much of the bubble shows (0..1), <paramref name="sinceStart"/> steps after the first hit of a run and
    /// <paramref name="sinceHit"/> after the latest: in over <see cref="FadeInSteps"/>, held, then dissolving.
    /// </summary>
    public static float Visibility(float sinceStart, float sinceHit)
    {
        float fadeIn = Math.Clamp(sinceStart / FadeInSteps, 0f, 1f);
        float t = Math.Clamp((sinceHit / ShowSteps - FadeOutFrom) / (1f - FadeOutFrom), 0f, 1f);
        return fadeIn * (1f - t * t * (3f - 2f * t));
    }

    public void Emit(EntityQuads quads, EntityMotion motion, VisionMap? vision)
    {
        if (_pulses.Count == 0)
            return;
        float scale = quads.View.PixelScale;
        float stepNow = _step + quads.Alpha;
        float loop = quads.Phase % LoopSeconds / LoopSeconds;
        ReadOnlySpan<TankRenderState> tanks = motion.Tanks;
        foreach (var pulse in _pulses)
        {
            // The step's alpha runs from the previous step, so a hit captured this step is drawn from alpha 0.
            float sinceHit = stepNow - pulse.LastHit;
            float visible = Visibility(stepNow - pulse.Started, sinceHit);
            if (sinceHit >= ShowSteps || visible <= 0f)
                continue;
            float age = Math.Max(0f, sinceHit / HitSteps);
            // .w: visibility in whole 255ths, the animation loop as the fraction.
            float packed = MathF.Round(visible * VisibleLevels) + Math.Min(loop, 0.9999f);
            for (int i = 0; i < tanks.Length; i++)
            {
                ref readonly TankRenderState tank = ref tanks[i];
                if (tank.Color != pulse.Color || tank.IsDead || !Sight.Sees(vision, tank.Position))
                    continue;
                var (start, drift) = motion.TankMotion(i);
                if (quads.IsOffScreen(quads.ScreenX(start.X), quads.ScreenY(start.Y), (Radius + 2f) * scale))
                    continue;
                float cx = EntityQuads.Centre(start.X);
                float cy = EntityQuads.Centre(start.Y);
                quads.Add(cx, cy, 1f, 0f, -Radius, Radius, -Radius, Radius, TankSprites.GetPixelColor(1, tank.Color),
                    EntityShape.KindTankShield, pulse.Fill, age, pulse.HitAngle, packed, drift);
            }
        }
    }
}
