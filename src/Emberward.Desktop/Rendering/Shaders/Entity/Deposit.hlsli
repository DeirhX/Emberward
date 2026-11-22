// ============================================================================
// Entity/Deposit.hlsli — Pyrite deposits: the pour's crystals and the core's heat glow
// ============================================================================
//
// DepositQuads draws a banked load as crystals flying from the tank's hold into
// the core or relay that took it, and a glow on the core that follows its
// reactor's heat store and pulses as they land. Soft light, no hard circles;
// each function returns once, at its end.
// ============================================================================

#ifndef EMBERWARD_ENTITY_DEPOSIT_HLSLI
#define EMBERWARD_ENTITY_DEPOSIT_HLSLI

#include "Common.hlsli"

static const float3 kDepositCyan = float3(0.55, 0.93, 1.00);
static const float3 kDepositWhiteGold = float3(1.00, 0.96, 0.84);

// One crystal of a pour: a faceted diamond, lit on the side toward the light, with a pale heart and a soft halo. It
// shrinks to 60% and brightens to white-gold as it lands. Gold for banked ore, cyan for ore burned to restart a base.
float4 OreCrystal(float2 p, float3 data, float4 params, float aa)
{
    float age = saturate(params.x);
    float glint = step(0.8, params.y);
    float spin = params.z;
    float landing = saturate(params.w);
    float burn = step(0.5, data.r);

    float size = 0.42 * lerp(1.0, 0.6, landing);
    float d = OreCrystalDistance(p, spin, size);
    float3 base = lerp(lerp(kOreGold, kOreGlint, glint), kDepositCyan, burn);
    float3 color = lerp(base, kDepositWhiteGold, landing * 0.8);

    // Two facets: the half toward the light is brighter.
    float2 r = float2(p.x * cos(spin) - p.y * sin(spin), p.x * sin(spin) + p.y * cos(spin));
    float facet = step(0.0, -(r.x + r.y));
    float body = FillAlpha(d, aa);
    float heart = FillAlpha(d + size * 0.55, aa);
    float halo = exp(-max(d, 0.0) * 3.2) * (0.30 + 0.55 * landing) * (1.0 - body);
    float appear = smoothstep(0.0, 0.08, age);

    float4 result = float4(0.0, 0.0, 0.0, 0.0);
    Over(result, color * (0.78 + 0.22 * facet), body);
    result.rgb += kOreGlint * heart * 0.35 * body;
    result.rgb += color * halo;
    return result * appear;
}

// A core's heat glow (or a relay's flash): the centre brightens along the heat ramp (amber, orange, yellow-white)
// with the reactor's heat store, a soft halo whose reach grows with it, and on each pulse a soft ring that expands
// from the core's rim and fades, with a faint shimmer. Mode 1 (ore burned to restart a dark base) pulses cyan-white.
// Additive: colour with no alpha, faded out well before the quad's edge.
float4 CoreHeat(float2 p, float3 data, float4 params)
{
    float radius = max(data.r, 1.0);
    float heat = saturate(params.x);
    float pulse = saturate(params.y);
    float phase = params.z;
    float burn = step(0.5, params.w);

    float dist = length(p);
    float r = dist / radius;
    float edge = smoothstep(1.0, 0.75, r);
    float coreRadius = radius * 0.28;

    float3 amber = float3(1.00, 0.45, 0.10);
    float3 orange = float3(1.00, 0.62, 0.20);
    float3 hot = float3(1.00, 0.93, 0.70);
    float3 heatColor = lerp(lerp(amber, orange, saturate(heat * 2.0)), hot, saturate(heat * 2.0 - 1.0));

    // A slow breath, so a warm core looks alive rather than painted.
    float breath = 0.9 + 0.1 * sin(phase * 6.2831853);
    float centre = exp(-(dist * dist) / (coreRadius * coreRadius)) * heat * 0.9 * breath;
    float reach = coreRadius * (1.2 + 2.0 * heat);
    float halo = exp(-dist / max(reach, 0.5)) * heat * 0.35;

    // The pulse ring: born at the rim, expanding to about 0.8 of the radius as the pulse falls.
    float ringRadius = lerp(coreRadius, radius * 0.8, 1.0 - pulse);
    float angle = atan2(p.y, p.x);
    float shimmer = 1.0 + 0.15 * sin(angle * 7.0 + phase * 12.566) * sin(dist * 3.0 - phase * 18.85);
    float ringWidth = 0.35 + 0.5 * (1.0 - pulse);
    float ring = exp(-((dist - ringRadius) * (dist - ringRadius)) / (ringWidth * ringWidth)) * pulse * shimmer;
    float flash = exp(-(dist * dist) / (coreRadius * coreRadius * 1.5)) * pulse * 0.6;
    float3 pulseColor = lerp(lerp(kOreGold, kDepositWhiteGold, 0.4), kDepositCyan, burn);

    float3 light = heatColor * (centre + halo) + pulseColor * (ring * 0.8 + flash);
    return float4(light * edge, 0.0);
}

#endif // EMBERWARD_ENTITY_DEPOSIT_HLSLI
