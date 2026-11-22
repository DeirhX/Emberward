// ============================================================================
// Entity/TankFire.hlsli — The fire on a tank with a damaged hull
// ============================================================================
//
// Three kinds (params in Layout.hlsli):
//   23 TankFire      the exposed core in its soot ring at the hull's centre and flame tongues, in the "flame frame":
//                    local -y points the way the flames go (up the screen, leaning away from the drift), and params.w
//                    turns the hull box into that frame. A dead tank's core glow, flaring and fading, is this kind
//                    too (no flames, no soot: only the soft glow).
//   24 TankFireLight the flickering light the fire throws on the ground around the tank (additive).
//   25 FireParticle  one puff of smoke or one ember, placed by the C# side where the tank was when it was born.
// Every animation loops on a 4 s cycle: each frequency is a multiple of 1/4 Hz, and the flame noise scrolls a whole
// number of lattice periods per cycle. How much of each part shows (FireOnset, eased in from nothing as the fire
// grows) and the death glow's flare and fade come from the C# side.
// ============================================================================

#ifndef EMBERWARD_ENTITY_TANKFIRE_HLSLI
#define EMBERWARD_ENTITY_TANKFIRE_HLSLI

static const float3 kFireWhite = float3(1.00, 0.93, 0.65);
static const float3 kFireOrange = float3(1.00, 0.45, 0.08);
static const float3 kFireRed = float3(0.70, 0.10, 0.03);
static const float2 kFireHullCenter = float2(-0.5, 0.0);   // the hull box of Tank.hlsli
static const float2 kFireHullHalf = float2(2.45, 1.55);

// The hull-heat ramp: the exposed core's colour by how full the hull is. HudPalette.HullEmber has the same stops
// (TankFireCoreTests checks them).
static const float3 kHullHeat0 = float3(1.0000, 0.9647, 0.8471);    // #FFF6D8 at an empty hull
static const float3 kHullHeat5 = float3(1.0000, 0.8157, 0.2510);    // #FFD040 at 5%
static const float3 kHullHeat25 = float3(1.0000, 0.4157, 0.0627);   // #FF6A10 at 25%
static const float3 kHullHeat50 = float3(0.7020, 0.1490, 0.0471);   // #B3260C at 50%
static const float3 kHullHeat75 = float3(0.3529, 0.0784, 0.0314);   // #5A1408 at 75% (HudPalette.HullEmber)
static const float3 kCoreSoot = float3(0.0784, 0.0392, 0.0314);     // #140A08
static const float3 kSlag = float3(0.1647, 0.1020, 0.0784);         // #2A1A14
static const float3 kSlagCrack = float3(1.0000, 0.3529, 0.1020);    // #FF5A1A

// The hull-heat ramp at hull fill h (0..1), interpolated between the stops; above 75% it stays at the 75% stop.
float3 HullHeatColour(float h)
{
    float3 c = lerp(kHullHeat0, kHullHeat5, saturate(h * 20.0));
    c = lerp(c, kHullHeat25, saturate((h - 0.05) * 5.0));
    c = lerp(c, kHullHeat50, saturate((h - 0.25) * 4.0));
    return lerp(c, kHullHeat75, saturate((h - 0.5) * 4.0));
}

// A hash of a lattice point built on frac (no sin), which stays exact enough at reduced precision for the small
// lattice coordinates FireNoise hands it.
float FireHash(float2 c)
{
    c = frac(c * float2(123.34, 456.21));
    c += dot(c, c + 45.32);
    return frac(c.x * c.y);
}

// Value noise (0..1). The lattice repeats every 16 in x and every 8 in y, so a pattern scrolled by 8 in y is the same.
float FireNoise(float2 q)
{
    float2 i = floor(q);
    float2 f = q - i;
    float2 u = f * f * (3.0 - 2.0 * f);
    float x0 = i.x - 16.0 * floor(i.x / 16.0);
    float x1 = x0 + 1.0 - 16.0 * step(15.0, x0);
    float y0 = i.y - 8.0 * floor(i.y / 8.0);
    float y1 = y0 + 1.0 - 8.0 * step(7.0, y0);
    float a = FireHash(float2(x0, y0));
    float b = FireHash(float2(x1, y0));
    float c = FireHash(float2(x0, y1));
    float d = FireHash(float2(x1, y1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// 0.8 to 1.04 and back: the fire's flicker, at whole multiples of 1/4 Hz.
float FireFlicker(float t, float s)
{
    return 0.8 + 0.12 * sin(18.849556 * t + s) + 0.08 * sin(32.986723 * t + 2.0 * s);
}

// One flame tongue whose foot stands at `base`, `height` cells tall: 1 at its foot, falling to 0 at its tip.
float FlameMask(float2 p, float2 base, float height, float sway)
{
    float v = (base.y - p.y) / max(height, 0.01);
    float vc = saturate(v);
    float centre = base.x + sway * vc * vc;
    float halfWidth = lerp(1.4, 0.1, vc);
    float across = 1.0 - smoothstep(halfWidth * 0.25, halfWidth, abs(p.x - centre));
    float foot = smoothstep(-0.45, 0.05, v);
    return across * foot * (1.0 - vc * vc);
}

// params.x = the flames' level (0..1, eased in); core.x = hull fill (0..1: the core's size and colour), core.y = the
// core's glow (0 hidden, 1 full; up to 2 for a dying core's flare), core.z = soot (0..1: the soot ring, the core's
// crust, its drips and its pulse). A dead tank's glow has flames and soot 0: only the soft glow, no edge.
float4 TankFire(float2 p, float4 params, float3 core, float aa)
{
    float level = saturate(params.x);
    float t = params.y;                          // seconds into the 4 s cycle
    float seed = params.z;                       // 0..1, per tank
    float s = seed * 6.283185;
    float c = cos(params.w);
    float sn = sin(params.w);
    float hullFill = saturate(core.x);
    float glow = max(core.y, 0.0);
    float solid = saturate(core.z);

    // The noise the soot, the crust and the flames read, scrolling up the screen 8 lattice rows per cycle.
    float2 q = p * float2(0.9, 0.6) + float2(seed * 8.0, 2.0 * t);
    float n = 0.65 * FireNoise(q) + 0.35 * FireNoise(q * 2.0 + float2(5.0, 3.0));

    float4 result = float4(0.0, 0.0, 0.0, 0.0);

    // The core sits at the hull box's centre (turned into the flame frame); it grows from 1.5 to 3.5 cells as the hull
    // falls from 75% to empty, and grows in with its glow (which a regenerating shield takes back).
    float2 cp = p - kFireHullCenter.x * float2(c, sn);
    float d = length(cp);
    float size = 1.5 + 2.0 * saturate((0.75 - hullFill) * 1.333333);
    float coreShown = saturate(glow);
    float radius = size * coreShown;
    float crust = solid * coreShown;
    float sag = saturate((0.25 - hullFill) * 4.0) * crust;

    // Soot: a dark, ragged ring round the core keeps it readable on any hull colour, with embers in its cracks.
    float soot = FillAlpha(d - (radius + 0.5 + 0.7 * level) + 0.8 * (n - 0.5), aa) * 0.85 * solid;
    Over(result, kCoreSoot, soot);
    result.rgb += kFireOrange * soot * level * 0.35 * smoothstep(0.62, 0.8, n);

    // Molten drips down the screen (+y in the flame frame) from a sagging core: up to three lanes, one per 2 s.
    float lane = clamp(floor(cp.x / 1.1 + 0.5), -1.0, 1.0);
    float along = (cp.y - 0.6 * radius) / ((1.0 + 2.5 * frac(0.5 * t + FireHash(float2(lane + 4.0, seed * 8.0)))) * max(sag, 0.01));
    float drip = (1.0 - smoothstep(0.18, 0.32, abs(cp.x - 1.1 * lane) + 0.1 * along)) * step(0.0, along)
        * (1.0 - smoothstep(0.85, 1.0, along)) * sag;
    Over(result, lerp(kSlag, kSlagCrack, smoothstep(0.5, 0.95, along)), drip);

    // The core: the hull-heat ramp, brightest at its centre, pulsing white-hot at 2 Hz below 5%, crusted with slag and
    // cracked as it sags.
    float3 heat = HullHeatColour(hullFill);
    heat = lerp(heat, kHullHeat0, saturate((0.05 - hullFill) * 20.0) * solid * (0.5 + 0.5 * sin(12.566371 * t + s)));
    float inner = saturate(1.0 - d / max(radius, 0.001));
    float3 body = heat * (0.75 + 0.5 * inner);
    body = lerp(body, kSlag, smoothstep(0.45, 0.6, n) * sag * smoothstep(0.4, 0.9, 1.0 - inner));
    body = lerp(body, kSlagCrack, (1.0 - smoothstep(0.02, 0.07, abs(n - 0.52))) * sag);
    Over(result, body, FillAlpha(d - radius, aa) * crust);

    // Its glow: a soft pool of the ramp's colour with no edge, the same falloff as the firelight on the ground.
    float r2 = size * size * 2.56;
    float fall = saturate(1.0 - d * d / r2);
    result.rgb += heat * glow * 0.6 * fall * fall;

    // Flames: four tongues standing on the hull (positions in its frame), the later ones lighting as the hull falls.
    float flicker = FireFlicker(t, s);
    float height = (2.5 + 7.5 * level) * (0.85 + 0.15 * flicker);
    float2 b0 = float2(-2.0, 0.0);
    float2 b1 = float2(-0.5, 1.1);
    float2 b2 = float2(-0.5, -1.1);
    float2 b3 = float2(1.0, 0.0);
    float m0 = FlameMask(p, float2(c * b0.x - sn * b0.y, sn * b0.x + c * b0.y), height,
        0.4 * sin(4.712389 * t + s)) * smoothstep(0.0, 0.15, level);
    float m1 = FlameMask(p, float2(c * b1.x - sn * b1.y, sn * b1.x + c * b1.y), height * (0.8 + 0.08 * sin(25.132741 * t + s)),
        0.4 * sin(6.283185 * t + s + 1.7)) * smoothstep(0.35, 0.5, level);
    float m2 = FlameMask(p, float2(c * b2.x - sn * b2.y, sn * b2.x + c * b2.y), height * (0.8 + 0.08 * sin(21.991149 * t + s + 2.3)),
        0.4 * sin(7.853982 * t + s + 3.1)) * smoothstep(0.35, 0.5, level);
    float m3 = FlameMask(p, float2(c * b3.x - sn * b3.y, sn * b3.x + c * b3.y), height * 0.75,
        0.4 * sin(3.141593 * t + s + 4.4)) * smoothstep(0.7, 0.85, level);
    float mask = max(max(m0, m1), max(m2, m3));
    float field = mask - 0.55 * n;

    float3 flame = lerp(kFireRed, kFireOrange, smoothstep(0.35, 0.45, field));
    flame = lerp(flame, kFireWhite, smoothstep(0.65, 0.75, field));
    float shown = smoothstep(0.12, 0.2, field);
    float alpha = 0.25 * shown;
    result.rgb = flame * 1.5 * shown + result.rgb * (1.0 - alpha);
    result.a = alpha + result.a * (1.0 - alpha);
    return result;
}

// The light a burning tank throws on the ground around it: a soft pool that grows with the fire, in the core's colour
// on the hull-heat ramp (params.w = hull fill), brought up to full brightness; strength (eased in) scales it.
float4 TankFireLight(float2 p, float4 params, float strength)
{
    float level = saturate(params.x);
    float radius = 5.0 + 7.0 * level;
    float fall = saturate(1.0 - dot(p, p) / (radius * radius));
    fall *= fall;
    float3 heat = HullHeatColour(saturate(params.w));
    float3 colour = heat / max(max(heat.r, heat.g), 0.35);
    float intensity = (0.08 + 0.32 * level) * strength * FireFlicker(params.y, params.z * 6.283185);
    return float4(colour * intensity * fall, 0.0);
}

// One puff of smoke (params.y = 1) or one ember (params.y = 0) at age params.x (0 born, 1 gone), params.z strong.
float4 FireParticle(float2 p, float4 params, float aa)
{
    float age = saturate(params.x);
    float strength = saturate(params.z);
    float seed = params.w;
    float4 result = float4(0.0, 0.0, 0.0, 0.0);
    if (params.y > 0.5)
    {
        // Smoke: a lumpy grey puff that swells as it rises, lit orange by the flames while it is young.
        float radius = 0.5 * (1.5 + 3.0 * age);
        float2 r = p / radius;
        float2 lump = float2(cos(seed * 6.283185), sin(seed * 6.283185)) * 0.35;
        float d = min(dot(r, r), dot(r - lump, r - lump) * 1.8);
        float body = saturate(1.0 - d);
        body *= body;
        float3 grey = float3(0.36, 0.34, 0.33);
        float3 colour = lerp(float3(0.80, 0.42, 0.16), grey, smoothstep(0.0, 0.2, age));
        float alpha = 0.45 * (1.0 - age) * smoothstep(0.0, 0.15, age) * strength * body;
        result = float4(colour * alpha, alpha);
    }
    else
    {
        // Ember: a hot dot with a little halo, cooling from white-yellow through orange to red, and twinkling.
        float d = length(p);
        float core = 1.0 - smoothstep(0.35 - aa, 0.35 + aa, d);
        float halo = saturate(1.0 - d / 0.8);
        float3 colour = lerp(kFireWhite, kFireOrange, saturate(age / 0.35));
        colour = lerp(colour, kFireRed, saturate((age - 0.35) / 0.65));
        float twinkle = 0.65 + 0.35 * sin(age * 37.0 + seed * 6.283185);
        result = float4(colour * (core + 0.35 * halo * halo) * twinkle * (1.0 - age) * smoothstep(0.0, 0.08, age) * strength * 1.6, 0.0);
    }
    return result;
}

#endif // EMBERWARD_ENTITY_TANKFIRE_HLSLI
