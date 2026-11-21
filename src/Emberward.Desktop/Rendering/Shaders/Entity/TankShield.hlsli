// ============================================================================
// Entity/TankShield.hlsli — A tank's shield, shown for a moment when it is hit
// ============================================================================
//
// A bubble around the tank. Nothing is drawn until a hit; then the veins of what is left of the bubble light up with a
// wavefront from the impact, and a red patch glows where the shot landed. The bubble shows how much shield is left:
// as it drains its outer edge erodes away first, down to a small core at the centre, and nothing at 0 (regeneration
// grows it back outward). The hit that breaks the shield shows the whole bubble once more, flashing and collapsing
// to the centre as the pulse fades. Params are documented in Layout.hlsli (kind 22).
// ============================================================================

#ifndef EMBERWARD_ENTITY_TANKSHIELD_HLSLI
#define EMBERWARD_ENTITY_TANKSHIELD_HLSLI

#include "ShieldVeins.hlsli"

static const float kTankShieldRadius = 6.6;
static const float kTankShieldVeinScale = 0.42;
static const float3 kTankShieldCool = float3(0.45, 0.85, 1.00);
static const float3 kTankShieldHot = float3(1.00, 0.35, 0.18);
static const float3 kTankShieldRed = float3(1.00, 0.10, 0.06);

// How much of the bubble is left, from the shield's fill: 1 inside what remains, 0 beyond it, with a soft, eroded edge.
// `edge` is where the cell stands against the cutoff (normalised distance from the centre, roughened by noise and
// the vein lattice) and `reach` is the cutoff. The cutoff's radius grows with the square root of the fill, so the area
// left falls roughly in step with the fill (a little over half the bubble at half fill): the outer edge goes first and a core
// at the centre last. Any shield at all keeps a core a couple of cells across, so the last sliver still reads.
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

float4 TankShield(float2 p, float3 team, float4 params, float aa)
{
    float fill = params.x;
    float age = saturate(params.y);
    float broken = step(fill, 0.0);
    float2 hit = params.zw;
    float fade = 1.0 - age;
    float r = length(p);

    float veins = VeinLines(p * kTankShieldVeinScale, 0.09);
    float reach = TankShieldReach(fill, age, broken);
    float edge = TankShieldEdge(p, r, veins);
    float remaining = 1.0 - smoothstep(reach - 0.08, reach, edge);
    // A bright fringe where the bubble is eroding; past the bubble (a full shield) there is none to see.
    float fringeOffset = (edge - reach + 0.04) / 0.05;
    float fringe = exp(-fringeOffset * fringeOffset) * (1.0 - smoothstep(1.0, 1.15, reach));

    // The impact cell is on the hull, a few cells inside the bubble: show the patch on the bubble where the shot came
    // in, or at the edge of what is left of it when the shield is low. A break marks where the bubble was hit.
    float hitLength = length(hit);
    float hitRadius = lerp(clamp(reach * kTankShieldRadius - 1.0, 0.0, kTankShieldRadius - 1.2),
        kTankShieldRadius - 1.2, broken);
    hit = hit * (hitLength > 0.01 ? hitRadius / hitLength : 1.0);

    float inside = 1.0 - smoothstep(kTankShieldRadius - 0.7, kTankShieldRadius, r);
    float rim = exp(-(r - kTankShieldRadius + 0.3) * (r - kTankShieldRadius + 0.3) * 1.6);
    float front = VeinFront(p, hit, age, 15.0, 2.6);
    float light = (0.22 + 1.1 * front) * fade * (1.0 + 0.5 * broken);

    float3 tint = lerp(kTankShieldHot, lerp(kTankShieldCool, team, 0.25), saturate(fill * 2.0));
    // A low shield's small core glows a little more, so it is seen over the hull.
    float core = 0.06 + 0.25 * (1.0 - saturate(fill * 5.0)) * (1.0 - broken);
    float3 add = tint * (veins * light * 1.7 + rim * fade * 0.8 + inside * fade * core + fringe * fade * 0.9);
    // The break flashes the whole bubble, quickly gone.
    add += lerp(kTankShieldHot, float3(1.0, 1.0, 1.0), 0.35) * inside * fade * fade * 0.45 * broken;

    // Where the shot landed: a red patch that widens and fades. No hit point (0, 0) means no patch.
    float2 toHit = p - hit;
    float spotRadius = lerp(1.6, 4.2, age);
    float hasHit = step(0.01, hitLength);
    float spot = exp(-dot(toHit, toHit) / (spotRadius * spotRadius)) * sqrt(fade) * hasHit * inside;
    add += kTankShieldRed * spot * 1.4;
    add += float3(1.0, 1.0, 1.0) * spot * spot * 0.35;

    // Additive, like the base shield's flashes: colour with no alpha. Only what is left of the bubble shows.
    return float4((add * inside + tint * rim * fade * 0.3 * (1.0 - inside)) * remaining, 0.0);
}

#endif // EMBERWARD_ENTITY_TANKSHIELD_HLSLI
