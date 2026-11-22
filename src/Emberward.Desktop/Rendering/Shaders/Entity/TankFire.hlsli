// ============================================================================
// Entity/TankFire.hlsli — The fire on a tank with a damaged hull
// ============================================================================
//
// Three kinds (params in Layout.hlsli):
//   23 TankFire      the exposed core in its soot ring at the hull's centre, upright on the screen (params.w, the hull
//                    heading, finds the hull box's centre). A dead tank's core glow, flaring and fading, is this kind
//                    too (no soot: only the soft glow).
//   24 TankFireLight the flickering light the fire throws on the ground around the tank (additive).
//   25 FireParticle  the fire itself: one flame lick, spark, cinder or puff of smoke, placed and turned along its
//                    motion by the C# side (FireParticles), which steps it with the world. Each cools over its life
//                    on FireRamp: white-yellow, orange, deep red, dark smoke.
// The core's and the light's animations loop on a 4 s cycle: each frequency is a multiple of 1/4 Hz, and the noise
// scrolls a whole number of lattice periods per cycle. How much of each part shows (FireOnset, eased in from nothing as
// the fire grows) and the death glow's flare and fade come from the C# side.
// ============================================================================

#ifndef EMBERWARD_ENTITY_TANKFIRE_HLSLI
#define EMBERWARD_ENTITY_TANKFIRE_HLSLI

static const float3 kFireWhite = float3(1.00, 0.93, 0.65);
static const float3 kFireOrange = float3(1.00, 0.45, 0.08);
static const float3 kFireYellow = float3(1.00, 0.72, 0.22);
static const float3 kFireRed = float3(0.55, 0.07, 0.02);
static const float3 kFireSmoke = float3(0.10, 0.085, 0.08);

// What a fire particle is (params.y); FireParticleKind in FireParticles.cs has the same values.
static const float kFireSparkKind = 0.0;
static const float kFireSmokeKind = 1.0;
static const float kFireFlameKind = 2.0;
static const float kFireCinderKind = 3.0;
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

// The exposed core in its soot ring, upright on the screen (+y down): params.x = the flames' level (0..1, eased in: the
// soot ring's reach and the embers in its cracks), params.w = the hull heading (the core sits at the hull box's
// centre); core.x = hull fill (0..1: the core's size and colour), core.y = the core's glow (0 hidden, 1 full; up to 2
// for a dying core's flare), core.z = soot (0..1: the soot ring, the core's crust, its drips and its pulse). A dead
// tank's glow has level and soot 0: only the soft glow, no edge. The flames are particles (FireParticle).
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

    // The noise the soot and the crust read, scrolling up the screen 8 lattice rows per cycle.
    float2 q = p * float2(0.9, 0.6) + float2(seed * 8.0, 2.0 * t);
    float n = 0.65 * FireNoise(q) + 0.35 * FireNoise(q * 2.0 + float2(5.0, 3.0));

    float4 result = float4(0.0, 0.0, 0.0, 0.0);

    // The core sits at the hull box's centre (turned by the heading); it grows from 1.5 to 3.5 cells as the hull
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

    // Molten drips down the screen (+y) from a sagging core: up to three lanes, one per 2 s.
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

// The colour of fire as it cools (heat 1 to 0): white-yellow, yellow, orange, deep red, then dark smoke.
float3 FireRamp(float heat)
{
    float3 c = lerp(kFireSmoke, kFireRed, saturate(heat * 4.0));
    c = lerp(c, kFireOrange, saturate((heat - 0.25) * 4.0));
    c = lerp(c, kFireYellow, saturate((heat - 0.5) * 4.0));
    return lerp(c, kFireWhite, saturate((heat - 0.75) * 4.0));
}

// A puff of smoke of the given radius: a lumpy grey puff that swells and darkens as it rises, lit orange by the flames
// while it is young.
float4 FireSmokePuff(float2 p, float age, float strength, float seed, float radius)
{
    float2 r = p / max(radius, 0.05);
    float2 lump = float2(cos(seed * 6.283185), sin(seed * 6.283185)) * 0.35;
    float d = min(dot(r, r), dot(r - lump, r - lump) * 1.8);
    float body = saturate(1.0 - d);
    body *= body;
    float3 grey = lerp(float3(0.36, 0.34, 0.33), float3(0.17, 0.15, 0.14), age);
    float3 colour = lerp(float3(0.80, 0.42, 0.16), grey, smoothstep(0.0, 0.2, age));
    float alpha = 0.45 * (1.0 - age) * smoothstep(0.0, 0.15, age) * strength * body;
    return float4(colour * alpha, alpha);
}

// A flame lick, local +x along its motion: a soft teardrop, round at its foot and drawn out to its tip, `radius` cells
// across and `stretch` times that along. It glows white-yellow when born and cools through orange and deep red,
// flickering, to a wisp of dark smoke as it dies. Its glow adds light; only the smoke darkens what is under it.
float4 FireFlame(float2 p, float age, float strength, float seed, float stretch, float radius, float swirl)
{
    float2 q = p / max(radius, 0.01);
    q.x /= max(stretch, 0.1);
    float taper = 1.0 - 0.5 * saturate(q.x);                    // narrower toward the tip
    float across = q.y / taper;
    float body = exp(-2.6 * (q.x * q.x + across * across));
    float heat = saturate(1.1 - 1.2 * age - 0.15 * seed + 0.2 * body);
    float flicker = 0.75 + 0.25 * sin(age * 47.0 + seed * 6.283185 + swirl * 3.0);
    float life = smoothstep(0.0, 0.1, age) * (1.0 - smoothstep(0.75, 1.0, age)) * strength;
    float burning = smoothstep(0.1, 0.55, heat);
    float smoke = (1.0 - burning) * body * 0.3 * life;
    float3 glow = FireRamp(heat) * body * burning * flicker * life * 0.6;
    return float4(glow + kFireSmoke * smoke, smoke);
}

// A spark or a cinder, local +x along its motion: a hot head `radius` cells across with a streak `trail` cells long
// behind it and a halo, as a shot is drawn (HotShot). It starts at `heat0` on FireRamp and cools as it ages, twinkling;
// it only adds light.
float4 FireStreak(float2 p, float age, float strength, float seed, float trail, float radius, float heat0, float aa)
{
    float len = max(trail, 0.01);
    float dist = sdSegment(p, float2(-len, 0.0), float2(0.0, 0.0));
    float along = saturate(1.0 + p.x / len);                    // 0 at the tail, 1 at the head
    float heat = saturate(heat0 * (1.05 - age));
    float core = FillAlpha(dist - radius * lerp(0.4, 1.0, along), aa) * lerp(0.2, 1.0, along);
    float halo = exp(-max(dist - radius * 0.5, 0.0) * (2.4 / max(radius, 0.05))) * lerp(0.15, 1.0, along);
    float twinkle = 0.7 + 0.3 * sin(age * 53.0 + seed * 6.283185);
    float life = smoothstep(0.0, 0.06, age) * (1.0 - smoothstep(0.75, 1.0, age)) * strength;
    float3 colour = FireRamp(heat * lerp(0.8, 1.0, along));
    return float4(colour * (1.3 * core + 0.6 * halo) * twinkle * life * smoothstep(0.0, 0.35, heat + 0.1), 0.0);
}

// One of a burning tank's particles (TankFireQuads, FireParticles): params.x = age (0 born, 1 gone), params.y = what it
// is (kFireSpark, kFireSmoke, kFireFlame, kFireCinder), params.z = strength (0..1: it fades in with the fire),
// params.w = a seed (0..1); shape = colour channels: smoke (unused, radius, unused), a flame lick (stretch, radius,
// swirl seed), a spark or cinder (trail, radius, starting heat).
float4 FireParticle(float2 p, float4 params, float3 shape, float aa)
{
    float age = saturate(params.x);
    float strength = saturate(params.z);
    float seed = params.w;
    float4 result;
    if (abs(params.y - kFireSmokeKind) < 0.5)
        result = FireSmokePuff(p, age, strength, seed, shape.y);
    else if (abs(params.y - kFireFlameKind) < 0.5)
        result = FireFlame(p, age, strength, seed, shape.x, shape.y, shape.z);
    else
        result = FireStreak(p, age, strength, seed, shape.x, shape.y, shape.z, aa);
    return result;
}

#endif // EMBERWARD_ENTITY_TANKFIRE_HLSLI
