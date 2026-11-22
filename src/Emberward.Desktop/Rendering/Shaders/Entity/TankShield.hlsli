// ============================================================================
// Entity/TankShield.hlsli — A tank's shield, shown while it is blocking hits
// ============================================================================
//
// Nothing is drawn while nothing hits the shield. A hit brings up a bubble around the tank for a moment: blue flow
// swirling inside it and a hexagonal lattice whose cells shimmer and whose veins carry travelling waves, all thin enough
// that the tank reads clearly beneath. Over that lies a glossy coat, as on a glass or plastic sphere lit from a fixed
// light at the upper left: a sphere normal from the radius, a fresnel rim that brightens and thickens where the surface
// turns edge-on, a broad soft gloss with a small crisp hot spot, a faint arc of focused light inside the far (lower
// right) edge, a darker and deeper blue gathering in the lower inside, and a thin bright outline on a crisp silhouette.
// The pattern dims under the coat towards the rim, where the coat reflects more. The pattern is laid out by arc length,
// so it bunches towards the rim, and the hexagon edges are raised as ridges.
//
// The bubble fades in over a step, holds while hits keep coming and dissolves after the last (`visible`, from
// TankShieldQuads). It shows how much shield is left: as it drains its outer edge erodes away first, leaving a thick lit
// lip, down to a small core at the centre, and it turns orange when low.
//
// A hit lights the veins of what is left of the bubble with a wavefront from the impact, flares the rim, and a red patch
// glows where the shot landed. The hit that breaks the shield shows the whole bubble once more, flashing and collapsing
// to the centre as the pulse fades. Params are documented in Layout.hlsli (kind 22).
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
// The fixed light, normalize(-0.45, -0.55, 0.70): up and to the left (local +y is down), above the screen. The halfway
// vector between it and the viewer (+z), and its direction across the screen.
static const float3 kTankShieldLight = float3(-0.45113, -0.55138, 0.70176);
static const float3 kTankShieldHalfway = float3(-0.24453, -0.29887, 0.92243);
static const float2 kTankShieldLightXY = float2(-0.63324, -0.77396);

// How much of the bubble is left, from the shield's fill: 1 inside what remains, 0 beyond it, with a soft, eroded edge.
// `edge` is where the cell stands against the cutoff (normalised distance from the centre, roughened by noise and
// the vein lattice) and `reach` is the cutoff. The cutoff's radius grows with the square root of the fill, so the area
// left falls roughly in step with the fill (a little over half the bubble at half fill): the outer edge goes first and a
// core at the centre last. Any shield at all keeps a core a couple of cells across, so the last sliver still reads.
// A full shield reaches 1.19, past the furthest edge on the dome (1 + 0.08 of noise) plus the 0.08 cutoff band, so a
// near-full bubble is not nibbled at its rim.
// A broken shield (fill 0: a pulse is drawn only for a hit, so an empty shield was just broken) has its whole bubble at the hit, shrinking to nothing as the pulse ages.
float TankShieldReach(float fill, float age, float broken)
{
    float left = max(sqrt(saturate(fill)) * 1.35 - 0.16, 0.3);
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

    // The bubble is a sphere seen from above: its normal from the normalised radius, with z towards the viewer, 1 at the
    // centre and 0 at the silhouette.
    float rn = saturate(r / kTankShieldRadius);
    float nz = sqrt(max(1.0 - rn * rn, 0.0));
    float3 normal = float3(p / kTankShieldRadius * step(r, kTankShieldRadius), nz);
    float lit = dot(normal, kTankShieldLight);
    // Blinn-Phong against the fixed light: a broad soft gloss (nh^12, about 40 degrees across at half height: some four
    // cells) and a small hot spot with a crisp edge (full within 4.5 degrees of the halfway vector, gone by 8: about a
    // cell across, fading out over the next). Both centre where the normal meets the halfway vector, 0.39 of the radius
    // up and to the left of the middle.
    float nh = saturate(dot(normal, kTankShieldHalfway));
    float nh2 = nh * nh;
    float nh4 = nh2 * nh2;
    float gloss = nh4 * nh4 * nh4;
    float hotSpot = smoothstep(0.9903, 0.9969, nh);
    // Fresnel: 0 facing the viewer, 1 edge-on. Its square (the coat's opacity and the reflected rim's body) reaches a
    // third of the way in; its cube (the bright rim) gathers in the outer cell or so, so the rim is both brighter and
    // thicker where the surface turns away.
    float facing = 1.0 - nz;
    float fresnel = facing * facing;
    float rimHot = fresnel * facing;
    // Which side of the bubble a point is on: 1 facing the light (upper left), -1 away from it (lower right).
    float towardLight = dot(outward, kTankShieldLightXY);
    float away = saturate(-towardLight);

    // On a curved surface the pattern bunches up towards the rim: lattice and flow are laid out by arc length.
    float arc = asin(min(rn, 0.999)) * kTankShieldRadius * 0.6366198;
    float2 surface = outward * lerp(r + 0.35 * kTankShieldRadius, arc * 1.35, step(r, kTankShieldRadius));

    float2 latticePoint = surface * kTankShieldVeinScale;
    float veins = VeinLines(latticePoint, 0.09);
    // The cell edges are raised ridges: their slopes face away from the cell's middle, bright where that faces the light.
    float2 cellOffset = latticePoint - HexCellCentre(latticePoint);
    float slope = veins * (1.0 - veins) * 4.0;
    float bevel = dot(cellOffset / max(length(cellOffset), 1e-4), kTankShieldLightXY) * slope;

    float reach = TankShieldReach(fill, age, broken);
    float edge = TankShieldEdge(p, r, veins);
    float remaining = 1.0 - smoothstep(reach - 0.08, reach, edge);
    // Where the bubble is eroding it has a thick rim: a lit lip (bright where its edge faces the light) and a shadowed
    // band just inside. Past the bubble (a full shield) there is none to see.
    float eroding = 1.0 - smoothstep(1.0, 1.15, reach);
    float lipOffset = (edge - reach + 0.04) / 0.05;
    float lip = exp(-lipOffset * lipOffset) * eroding;
    float lipLight = 0.45 + 0.9 * saturate(towardLight * 0.8 + 0.4);
    float bandOffset = (edge - reach + 0.13) / 0.045;
    float band = exp(-bandOffset * bandOffset) * eroding;

    // A crisp silhouette, anti-aliased over a pixel, with a thin bright outline just inside it (at least a pixel wide),
    // and a faint glow just outside. `outline` is also 1 past the silhouette; the colour and alpha are both multiplied by
    // `inside` once, at the end.
    float inset = kTankShieldRadius - r;
    float inside = saturate(inset / aa + 0.5);
    float outlineWidth = max(0.28, 1.5 * aa);
    float outline = 1.0 - smoothstep(outlineWidth, outlineWidth + aa, inset);
    float halo = exp(-inset * inset * 1.6) * (1.0 - inside);
    float breathe = 0.85 + 0.15 * sin(5.0 * theta);

    // Blue flow, deep to cyan, turning orange-red as the shield runs low; shaded deeper on the far side of the dome.
    float flow = TankShieldFlow(surface, r, theta);
    float lowShift = saturate(fill * 2.0);
    float3 deep = lerp(kTankShieldEmber, lerp(kTankShieldDeep, team, 0.12), lowShift);
    float3 bright = lerp(kTankShieldHot, kTankShieldCyan, lowShift);
    float shade = saturate(0.5 + 0.7 * lit);
    float3 flowColor = lerp(deep * 0.5, lerp(deep, bright, flow * flow), shade);
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

    // What is in the shell (flow, lattice, the hit's veins, the eroding lip) is seen through its glossy coat, which
    // reflects more and lets less through towards the rim, so the pattern dims there under the reflected light.
    float through = 1.0 - 0.55 * fresnel;
    float3 film = (flowColor * (0.10 + 0.20 * highlight) + bright * (ridges + sparkle * 0.25)) * breathe
        + lerp(tint, white, 0.3) * lip * lipLight * 0.7;
    float3 add = (film + tint * (veins * light * 1.7 + core * fade + lip * fade * 0.6)) * through;

    // The coat, over everything in the shell. The rim reflects a pale sky, brighter on the side facing the light and
    // flaring with a hit; light focused through the bubble lands as a faint arc just inside the far (lower right) edge,
    // widest opposite the light; the gloss and the hot spot are the light's reflection; the outline is the shell seen
    // end-on at the silhouette. Within the outline its light replaces the rim's rather than adding to it, so the lit
    // edge peaks near 1.1 (rim at most 0.55 x 1.1 x 1.35 just inside the outline, the outline at most 1.1) instead of
    // clipping to a flat white band.
    float rimSide = 0.75 + 0.35 * towardLight;
    float3 rimLight = lerp(tint, white, 0.35) * ((rimHot * 0.55 + fresnel * 0.2) * rimSide * (1.0 + 0.35 * fade));
    float3 outlineLight = lerp(tint, white, 0.55) * (0.7 + 0.15 * saturate(towardLight + 0.5) + 0.25 * fade);
    add += lerp(rimLight, outlineLight, outline);
    float causticOffset = (rn - 0.82) / (0.035 + 0.035 * away);
    float caustic = exp(-causticOffset * causticOffset) * smoothstep(0.25, 0.9, away);
    add += lerp(bright, white, 0.45) * caustic * (0.45 + 0.08 * sin(3.0 * theta));
    add += lerp(bright, white, 0.6) * gloss * 0.45 + white * hotSpot * 0.95;

    // The far side of the shell looks thicker: a darker, deeper blue gathers in its lower inside towards the rim. Its
    // alpha (below) dims the ground there and its colour tints it, so the lit upper left stands out against it.
    float thickness = saturate(0.35 - 0.75 * towardLight) * smoothstep(0.3, 1.0, rn);
    add += deep * (0.2 * thickness);

    // The break flashes the whole bubble, quickly gone.
    add += lerp(kTankShieldHot, white, 0.35) * fade * fade * 0.45 * broken;

    // Where the shot landed: a red patch that widens and fades.
    float2 toHit = p - hit;
    float spotRadius = lerp(1.6, 4.2, age);
    float spot = exp(-dot(toHit, toHit) / (spotRadius * spotRadius)) * sqrt(fade) * hasHit;
    add += kTankShieldRed * spot * 1.4;
    add += white * spot * spot * 0.35;

    // Premultiplied: mostly added light over a thin film of alpha. The middle stays nearly clear (about 0.05) so the
    // tank reads beneath; the coat grows more opaque towards the rim, as a curved shell does edge-on, and on the thick
    // far side, capped at 0.6 so the ground still shows through the outline (about 0.65 there with the film). The hot
    // spot adds a little (0.15: it sits over the hull, which must stay readable), as do the shadowed side of the ridges
    // and of the eroding lip. Only what is left of the bubble shows.
    float alpha = (0.03 + 0.025 * flow + 0.10 * saturate(-bevel) + 0.14 * band) * breathe
        + min(0.28 * fresnel + 0.26 * thickness + 0.25 * outline, 0.6) + 0.15 * hotSpot;
    float3 color = add * inside + tint * halo * (0.08 + fade * 0.3);
    return float4(color, saturate(alpha) * inside) * (remaining * visible);
}

#endif // EMBERWARD_ENTITY_TANKSHIELD_HLSLI
