// ============================================================================
// Entity/Powerup.hlsli — Powerup drops: the forecast site, the drop on the ground, a tank's halo
// ============================================================================
//
// PowerupQuads marks where the next drop lands (a reticle that closes as it
// comes), draws a drop on the ground (a dark capsule with a bright rim in a
// glow, and a ring that runs down with its time) and rings a tank that has a
// powerup running (an arc of what is left of it). All three take the kind's
// colour from the quad; the kind's letter is a glyph quad drawn over them, so
// colour is never the only sign. Each function returns once, at its end.
// ============================================================================

#ifndef EMBERWARD_ENTITY_POWERUP_HLSLI
#define EMBERWARD_ENTITY_POWERUP_HLSLI

#include "Common.hlsli"

static const float kPowerupTau = 6.2831853;

// 1 on the part of a circle that starts at the top and runs clockwise for `fraction` of the way round, 0 on the rest.
float PowerupArc(float2 p, float fraction)
{
    float turn = frac(atan2(p.x, -p.y) / kPowerupTau + 1.0);
    return step(turn, saturate(fraction));
}

float4 Powerup(float2 p, float3 color, float4 params, float aa)
{
    float radius = params.x;
    float amount = saturate(params.y);
    float phase = params.z;
    float mode = floor(params.w + 0.25);
    float urgent = step(0.25, frac(params.w));
    float r = length(p);
    float turn = atan2(p.y, p.x) / kPowerupTau;
    float blink = 0.5 + 0.5 * sin(phase * lerp(3.0, 13.0, urgent));

    float3 rgb = float3(0.0, 0.0, 0.0);
    float alpha = 0.0;
    if (mode < 0.5)
    {
        // The forecast site: a dashed ring the size of the pickup, turning slowly, four ticks pointing in, and an
        // outer ring that closes onto it over the last seconds (amount 1 well before the landing, 0 as it lands).
        float dash = step(0.5, frac(turn * 12.0 + phase * 0.06));
        float ring = FillAlpha(abs(r - radius) - 0.16, aa) * dash;
        float outer = radius * (1.0 + 1.5 * amount);
        float closing = FillAlpha(abs(r - outer) - (0.10 + 0.10 * urgent), aa) * lerp(0.55, 1.0, urgent);
        float2 a = abs(p);
        float along = max(a.x, a.y);
        float tick = FillAlpha(min(a.x, a.y) - 0.13, aa) * step(radius * 1.15, along) * step(along, radius * 1.45);
        float glow = exp(-r * r / (radius * radius) * 0.9) * (0.10 + 0.22 * urgent * blink);
        float lines = saturate(ring + closing + tick) * lerp(0.7 + 0.3 * blink, 0.45 + 0.55 * blink, urgent);
        rgb = color * (lines + glow);
        alpha = lines * 0.75;
    }
    else if (mode < 1.5)
    {
        // A drop on the ground: a dark capsule with a bright rim (the kind's letter is drawn on it), a soft glow that
        // breathes, slow rays, and at the pickup's edge a ring of which the part still lit is the time it has left.
        float breathe = 0.5 + 0.5 * sin(phase * 2.6);
        float core = radius * 0.62;
        float disc = FillAlpha(r - core, aa);
        float rim = disc * (1.0 - FillAlpha(r - (core - 0.34), aa));
        float band = FillAlpha(abs(r - radius) - 0.2, aa);
        float arc = band * lerp(0.2, 1.0, PowerupArc(p, amount));
        float rays = pow(saturate(cos((turn + phase * 0.03) * kPowerupTau * 4.0)), 10.0) * saturate(1.0 - r / (radius * 1.7)) * 0.35;
        float glow = exp(-r * r / (radius * radius) * 0.8) * (0.30 + 0.18 * breathe) + rays * (1.0 - disc);
        float show = lerp(1.0, 0.35 + 0.65 * blink, urgent);
        rgb = (color * (rim + arc + glow * (1.0 - disc * 0.7)) + color * 0.16 * disc * (1.0 - rim)) * show;
        alpha = saturate(disc * 0.9 + arc * 0.8) * show;
    }
    else
    {
        // A tank's halo: a ring of dashes that runs round it, lit for the share of the powerup that is left.
        float dash = lerp(0.45, 1.0, step(0.5, frac(turn * 8.0 - phase * 0.22)));
        float band = FillAlpha(abs(r - radius) - 0.17, aa);
        float left = lerp(0.22, 1.0, PowerupArc(p, amount));
        float show = lerp(0.85 + 0.15 * blink, 0.25 + 0.75 * blink, urgent);
        float glow = exp(-abs(r - radius) * 1.7) * 0.28;
        float lines = band * left * dash * show;
        rgb = color * (lines + glow * show);
        alpha = lines * 0.6;
    }
    return float4(rgb, alpha);
}

#endif // EMBERWARD_ENTITY_POWERUP_HLSLI
