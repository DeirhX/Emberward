// ============================================================================
// Entity/TankShield.hlsli — A tank's shield: a living film of blue over the hull, flaring when it is hit
// ============================================================================
//
// While the shield holds, a faint bubble sits around the tank: blue flow swirling across it, a hexagonal lattice whose
// cells shimmer and whose veins carry travelling waves, a brighter rim, all breathing slowly and thin enough that the
// tank reads clearly beneath. The bubble shows how much shield is left: as it drains its outer edge erodes away first,
// down to a small core at the centre (regeneration grows it back outward), and it turns orange when low.
//
// A hit lights the veins of what is left of the bubble with a wavefront from the impact, and a red patch glows where
// the shot landed. The hit that breaks the shield shows the whole bubble once more, flashing and collapsing to the
// centre as the pulse fades. Params are documented in Layout.hlsli (kind 22).
//
// Every animation runs on `loop` (0..1 over TankShieldQuads.LoopSeconds) through whole turns of kTau, so the picture is
// the same at 1 as at 0 and the wrap is seamless.
// ============================================================================

#ifndef EMBERWARD_ENTITY_TANKSHIELD_HLSLI
#define EMBERWARD_ENTITY_TANKSHIELD_HLSLI

#include "ShieldVeins.hlsli"

static const float kTankShieldRadius = 6.6;
static const float kTankShieldVeinScale = 0.42;
static const float kTankShieldTau = 6.2831853;
static const float3 kTankShieldCool = float3(0.45, 0.85, 1.00);
static const float3 kTankShieldHot = float3(1.00, 0.35, 0.18);
static const float3 kTankShieldRed = float3(1.00, 0.10, 0.06);
static const float3 kTankShieldDeep = float3(0.04, 0.14, 0.55);
static const float3 kTankShieldCyan = float3(0.40, 0.92, 1.00);
static const float3 kTankShieldEmber = float3(0.45, 0.10, 0.04);

// How much of the bubble is left, from the shield's fill: 1 inside what remains, 0 beyond it, with a soft, eroded edge.
// `edge` is where the cell stands against the cutoff (normalised distance from the centre, roughened by noise and
// the vein lattice) and `reach` is the cutoff. The cutoff's radius grows with the square root of the fill, so the area
// left falls roughly in step with the fill (a little over half the bubble at half fill): the outer edge goes first and a
// core at the centre last. Any shield at all keeps a core a couple of cells across, so the last sliver still reads.
// A broken shield (fill 0 or less) has its whole bubble at the hit, shrinking to nothing as the pulse ages.
float TankShieldReach(float fill, float age, float broken)
{
    float left = max(sqrt(saturate(fill)) * 1.3 - 0.16, 0.3);
    return lerp(left, lerp(1.14, -0.2, age), broken);
}

float TankShieldEdge(float2 p, float r, float veins)
{
    float rough = ShieldNoise(p * 0.55) - 0.5;
    // The veins outlast the hexagons between them, so the edge reads as a lattice crumbling rather than a circle.
    return r / kTankShieldRadius + 0.16 * rough - 0.05 * veins;
}

// The swirling flow, 0..1: noise warped by noise, turned once a loop and twisted more near the centre, its pattern
// drifting along circles in noise space (whole turns, so it loops).
float TankShieldFlow(float2 p, float r, float theta)
{
    float angle = theta + 0.6 * (1.0 - saturate(r / kTankShieldRadius)) * sin(2.0 * theta);
    float s = sin(angle);
    float c = cos(angle);
    float2 turned = float2(c * p.x - s * p.y, s * p.x + c * p.y) * 0.2;
    float2 drift = 2.5 * float2(cos(2.0 * theta), sin(2.0 * theta));
    float2 warp = float2(ShieldNoise(turned + drift), ShieldNoise(turned + float2(5.2, 1.3) - drift));
    return ShieldNoise(turned * 1.6 + 2.2 * warp + float2(-drift.y, drift.x));
}

float4 TankShield(float2 p, float3 team, float4 params, float aa)
{
    float fill = params.x;
    float age = saturate(params.y);
    float hitAngle = params.z;
    float theta = params.w * kTankShieldTau;
    float broken = step(fill, 0.0);
    float fade = 1.0 - age;
    float r = length(p);

    float2 latticePoint = p * kTankShieldVeinScale;
    float veins = VeinLines(latticePoint, 0.09);
    float reach = TankShieldReach(fill, age, broken);
    float edge = TankShieldEdge(p, r, veins);
    float remaining = 1.0 - smoothstep(reach - 0.08, reach, edge);
    // A bright fringe where the bubble is eroding; past the bubble (a full shield) there is none to see.
    float fringeOffset = (edge - reach + 0.04) / 0.05;
    float fringe = exp(-fringeOffset * fringeOffset) * (1.0 - smoothstep(1.0, 1.15, reach));

    float inside = 1.0 - smoothstep(kTankShieldRadius - 0.7, kTankShieldRadius, r);
    float rim = exp(-(r - kTankShieldRadius + 0.3) * (r - kTankShieldRadius + 0.3) * 1.6);
    // Breathing over 8 s; the rim brightens towards the bubble's edge like a film seen edge-on.
    float breathe = 0.82 + 0.18 * sin(5.0 * theta);
    float edgeness = saturate(r / kTankShieldRadius);
    float fresnel = edgeness * edgeness * edgeness;

    // Blue flow, deep to cyan, turning orange-red as the shield runs low.
    float flow = TankShieldFlow(p, r, theta);
    float lowShift = saturate(fill * 2.0);
    float3 deep = lerp(kTankShieldEmber, lerp(kTankShieldDeep, team, 0.12), lowShift);
    float3 bright = lerp(kTankShieldHot, kTankShieldCyan, lowShift);
    float3 flowColor = lerp(deep, bright, flow * flow);
    float highlight = smoothstep(0.62, 0.85, flow);

    // The lattice lives: each cell shimmers on its own phase (4 s), waves run outward along the veins (2 s), and a cell
    // at the peak of its shimmer sparkles.
    float cellSeed = ShieldHash(HexCellCentre(latticePoint) * 7.13);
    float shimmer = 0.5 + 0.5 * sin(10.0 * theta + cellSeed * kTankShieldTau);
    float waveOffset = sin(r * 0.95 - 20.0 * theta);
    float wave = waveOffset * waveOffset * waveOffset * waveOffset;
    float peak = shimmer * shimmer;
    peak *= peak;
    peak *= peak;
    float sparkle = peak * peak * step(0.7, cellSeed);
    float idleVeins = veins * (0.10 + 0.22 * wave + 0.08 * shimmer);

    float3 tint = lerp(kTankShieldHot, lerp(kTankShieldCool, team, 0.25), lowShift);
    float3 film = flowColor * (0.10 + 0.20 * highlight + 0.25 * fresnel) * breathe
        + bright * (idleVeins + sparkle * 0.25) * breathe
        + tint * rim * 0.22 * breathe
        + bright * (1.0 - veins) * shimmer * 0.025;

    // The impact cell is on the hull, a few cells inside the bubble: show the patch on the bubble where the shot came
    // in, or at the edge of what is left of it when the shield is low. A break marks where the bubble was hit.
    // An angle past 9 means no hit point (a bite), and no patch.
    float hasHit = step(hitAngle, 9.0);
    float hitRadius = lerp(clamp(reach * kTankShieldRadius - 1.0, 0.0, kTankShieldRadius - 1.2),
        kTankShieldRadius - 1.2, broken);
    float2 hit = float2(cos(hitAngle), sin(hitAngle)) * hitRadius * hasHit;

    float front = VeinFront(p, hit, age, 15.0, 2.6);
    float light = (0.22 + 1.1 * front) * fade * (1.0 + 0.5 * broken);
    // A low shield's small core glows a little more, so it is seen over the hull.
    float core = 0.06 + 0.25 * (1.0 - saturate(fill * 5.0)) * (1.0 - broken);
    float3 add = film + tint * (veins * light * 1.7 + rim * fade * 0.8 + inside * fade * core + fringe * (0.25 + fade * 0.9));
    // The break flashes the whole bubble, quickly gone.
    add += lerp(kTankShieldHot, float3(1.0, 1.0, 1.0), 0.35) * inside * fade * fade * 0.45 * broken;

    // Where the shot landed: a red patch that widens and fades.
    float2 toHit = p - hit;
    float spotRadius = lerp(1.6, 4.2, age);
    float spot = exp(-dot(toHit, toHit) / (spotRadius * spotRadius)) * sqrt(fade) * hasHit * inside;
    add += kTankShieldRed * spot * 1.4;
    add += float3(1.0, 1.0, 1.0) * spot * spot * 0.35;

    // Premultiplied: mostly added light, with a thin film of alpha (at most about 0.12) so the flow tints the tank
    // a little without hiding it. Only what is left of the bubble shows.
    float alpha = (0.04 + 0.05 * flow + 0.03 * fresnel) * breathe * inside;
    float3 color = add * inside + tint * rim * (0.08 + fade * 0.3) * (1.0 - inside);
    return float4(color, alpha) * remaining;
}

#endif // EMBERWARD_ENTITY_TANKSHIELD_HLSLI
