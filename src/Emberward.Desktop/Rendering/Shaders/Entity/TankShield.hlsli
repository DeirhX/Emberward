// ============================================================================
// Entity/TankShield.hlsli — A tank's shield, shown while it is blocking hits
// ============================================================================
//
// Nothing is drawn while nothing hits the shield. A hit brings up a faint bubble around the tank for a moment: blue
// flow swirling across it, a hexagonal lattice whose cells shimmer and whose veins carry travelling waves, a brighter
// rim, all thin enough that the tank reads clearly beneath. It is drawn as a translucent plastic dome lit from the upper
// left: a fresnel rim, a broad gloss and a sharp glint, the pattern bunching towards the rim, shading deeper on the far
// side, and the hexagon edges raised as ridges. It fades in over a step, holds while hits keep coming and dissolves
// after the last (`visible`, from TankShieldQuads). The bubble shows how much shield is left: as it drains its outer
// edge erodes away first, leaving a thick lit lip, down to a small core at the centre, and it turns orange when low.
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
// A broken shield (fill 0: a pulse is drawn only for a hit, so an empty shield was just broken) has its whole bubble at the hit, shrinking to nothing as the pulse ages.
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
    // .w packs how much of the bubble shows (whole 255ths) with the animation loop (the fraction).
    float visible = floor(params.w) / 255.0;
    float theta = frac(params.w) * kTankShieldTau;
    float broken = step(fill, 0.0);
    float fade = 1.0 - age;
    float r = length(p);
    float2 outward = p / max(r, 1e-4);

    // The bubble is a dome: its normal from the normalised radius, lit from the upper left (local +y is down). The light
    // sways a little with the swirl (whole turns of theta, so it loops).
    float rn = saturate(r / kTankShieldRadius);
    float3 normal = float3(p / kTankShieldRadius * step(r, kTankShieldRadius), sqrt(max(1.0 - rn * rn, 0.0)));
    float sway = 0.18 * sin(theta);
    float2 lightXY = float2(-0.45 * cos(sway) + 0.55 * sin(sway), -0.55 * cos(sway) - 0.45 * sin(sway));
    float3 lightDir = normalize(float3(lightXY, 0.70));
    float lit = dot(normal, lightDir);
    float3 halfway = normalize(lightDir + float3(0.0, 0.0, 1.0));
    float nh = saturate(dot(normal, halfway));
    float nh2 = nh * nh;
    float nh4 = nh2 * nh2;
    float nh8 = nh4 * nh4;
    float nh16 = nh8 * nh8;
    float nh64 = nh16 * nh16;
    nh64 *= nh64;
    float specular = nh8 * nh4 * 0.22 + nh64 * 0.9;
    float rimFacing = 1.0 - normal.z;
    float fresnel = rimFacing * rimFacing;

    // On a curved surface the pattern bunches up towards the rim: lattice and flow are laid out by arc length.
    float arc = asin(min(rn, 0.999)) * kTankShieldRadius * 0.6366198;
    float2 surface = outward * lerp(r + 0.35 * kTankShieldRadius, arc * 1.35, step(r, kTankShieldRadius));

    float2 latticePoint = surface * kTankShieldVeinScale;
    float veins = VeinLines(latticePoint, 0.09);
    // The cell edges are raised ridges: their slopes face away from the cell's middle, bright where that faces the light.
    float2 cellOffset = latticePoint - HexCellCentre(latticePoint);
    float slope = veins * (1.0 - veins) * 4.0;
    float bevel = dot(cellOffset / max(length(cellOffset), 1e-4), lightXY) * slope;

    float reach = TankShieldReach(fill, age, broken);
    float edge = TankShieldEdge(p, r, veins);
    float remaining = 1.0 - smoothstep(reach - 0.08, reach, edge);
    // Where the bubble is eroding it has a thick rim: a lit lip (bright where its edge faces the light) and a shadowed
    // band just inside. Past the bubble (a full shield) there is none to see.
    float eroding = 1.0 - smoothstep(1.0, 1.15, reach);
    float lipOffset = (edge - reach + 0.04) / 0.05;
    float lip = exp(-lipOffset * lipOffset) * eroding;
    float lipLight = 0.45 + 0.9 * saturate(dot(outward, lightXY) * 0.8 + 0.4);
    float bandOffset = (edge - reach + 0.13) / 0.045;
    float band = exp(-bandOffset * bandOffset) * eroding;

    float inside = 1.0 - smoothstep(kTankShieldRadius - 0.7, kTankShieldRadius, r);
    float rim = exp(-(r - kTankShieldRadius + 0.3) * (r - kTankShieldRadius + 0.3) * 1.6);
    float breathe = 0.85 + 0.15 * sin(5.0 * theta);

    // Blue flow, deep to cyan, turning orange-red as the shield runs low; shaded deeper on the far side of the dome.
    float flow = TankShieldFlow(surface, r, theta);
    float lowShift = saturate(fill * 2.0);
    float3 deep = lerp(kTankShieldEmber, lerp(kTankShieldDeep, team, 0.12), lowShift);
    float3 bright = lerp(kTankShieldHot, kTankShieldCyan, lowShift);
    float shade = saturate(0.55 + 0.6 * lit);
    float3 flowColor = lerp(deep * 0.6, lerp(deep, bright, flow * flow), shade);
    float highlight = smoothstep(0.62, 0.85, flow);

    // The lattice lives: each cell shimmers on its own phase (4 s), waves run outward along the ridges (2 s), and a cell
    // at the peak of its shimmer sparkles.
    float cellSeed = ShieldHash(HexCellCentre(latticePoint) * 7.13);
    float shimmer = 0.5 + 0.5 * sin(10.0 * theta + cellSeed * kTankShieldTau);
    float waveOffset = sin(r * 0.95 - 20.0 * theta);
    float wave = waveOffset * waveOffset * waveOffset * waveOffset;
    float peak = shimmer * shimmer;
    peak *= peak;
    peak *= peak;
    float sparkle = peak * peak * step(0.7, cellSeed);
    float ridges = veins * (0.08 + 0.2 * wave + 0.06 * shimmer) * shade + saturate(bevel) * 0.35;

    float3 tint = lerp(kTankShieldHot, lerp(kTankShieldCool, team, 0.25), lowShift);
    float3 white = float3(1.0, 1.0, 1.0);
    float3 film = flowColor * (0.09 + 0.18 * highlight + 0.35 * fresnel) * breathe
        + bright * (ridges + sparkle * 0.25) * breathe
        + tint * rim * 0.22 * breathe
        + lerp(bright, white, 0.6) * specular * (0.6 + 0.4 * flow)
        + lerp(tint, white, 0.3) * lip * lipLight * 0.7;

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
    float3 add = film + tint * (veins * light * 1.7 + rim * fade * 0.8 + inside * fade * core + lip * fade * 0.6);
    // The break flashes the whole bubble, quickly gone.
    add += lerp(kTankShieldHot, white, 0.35) * inside * fade * fade * 0.45 * broken;

    // Where the shot landed: a red patch that widens and fades.
    float2 toHit = p - hit;
    float spotRadius = lerp(1.6, 4.2, age);
    float spot = exp(-dot(toHit, toHit) / (spotRadius * spotRadius)) * sqrt(fade) * hasHit * inside;
    add += kTankShieldRed * spot * 1.4;
    add += white * spot * spot * 0.35;

    // Premultiplied: mostly added light over a thin film of alpha, thicker towards the rim like a curved shell seen
    // edge-on (about 0.05 in the middle, 0.2 at the rim), and on the shadowed side of the ridges and of the eroding lip,
    // which darken a little. The tank reads clearly beneath. Only what is left of the bubble shows.
    float alpha = (0.03 + 0.03 * flow + 0.14 * fresnel + 0.10 * saturate(-bevel) + 0.14 * band) * breathe * inside;
    float3 color = add * inside + tint * rim * (0.08 + fade * 0.3) * (1.0 - inside);
    return float4(color, alpha) * (remaining * visible);
}

#endif // EMBERWARD_ENTITY_TANKSHIELD_HLSLI
