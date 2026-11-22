// ============================================================================
// Entity/ShieldVeins.hlsli — The vein lattice a shield shows when it is hit
// ============================================================================
//
// A shield is invisible until it takes a hit; then the hit lights its veins, a hexagonal lattice, across the whole
// shield, with a wavefront running out from the point of impact. The lattice is analytic (no loops, no textures), so
// it is cheap and sharp at any scale.
// ============================================================================

#ifndef EMBERWARD_ENTITY_SHIELDVEINS_HLSLI
#define EMBERWARD_ENTITY_SHIELDVEINS_HLSLI

// A value in [0, 1) for each whole cell `c`.
float ShieldHash(float2 c)
{
    return frac(sin(dot(c, float2(12.9898, 78.233))) * 43758.5453);
}

// Smooth noise in [0, 1): ShieldHash blended between the corners of the cell `p` falls in.
float ShieldNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = ShieldHash(i);
    float b = ShieldHash(i + float2(1.0, 0.0));
    float c = ShieldHash(i + float2(0.0, 1.0));
    float d = ShieldHash(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// Distance, in lattice units, from `p` to the nearest hexagon edge: 0 on a vein, about 0.5 at a cell's middle.
float HexVeinDistance(float2 p)
{
    const float2 r = float2(1.0, 1.7320508);
    const float2 h = float2(0.5, 0.8660254);
    float2 a = p - r * floor(p / r) - h;
    float2 q = p - h;
    float2 b = q - r * floor(q / r) - h;
    float2 g = (dot(a, a) < dot(b, b)) ? a : b;
    float2 ag = abs(g);
    return 0.5 - max(dot(ag, float2(0.5, 0.8660254)), ag.x);
}

// The centre, in lattice units, of the hexagon `p` falls in: the same for every point of a cell, so a cell can be
// given its own value (ShieldHash of it).
float2 HexCellCentre(float2 p)
{
    const float2 r = float2(1.0, 1.7320508);
    const float2 h = float2(0.5, 0.8660254);
    float2 a = p - r * floor(p / r) - h;
    float2 q = p - h;
    float2 b = q - r * floor(q / r) - h;
    return p - ((dot(a, a) < dot(b, b)) ? a : b);
}

// 1 on a vein, falling to 0 within `width` lattice units of it.
float VeinLines(float2 p, float width)
{
    return 1.0 - smoothstep(0.0, width, HexVeinDistance(p));
}

// A wavefront that leaves `hit` and travels `speed` cells over the pulse (age 0..1), `width` cells thick.
float VeinFront(float2 p, float2 hit, float age, float speed, float width)
{
    float offset = (length(p - hit) - age * speed) / width;
    return exp(-offset * offset);
}

#endif // EMBERWARD_ENTITY_SHIELDVEINS_HLSLI
