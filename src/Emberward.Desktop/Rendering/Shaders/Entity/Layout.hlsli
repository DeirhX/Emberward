// ============================================================================
// Entity/Layout.hlsli — Entity vertex layout, shape kinds and flags
// ============================================================================
//
// The contract with the C# side (Rendering/Entities): the vertex the pixel shader
// receives, the shape kinds and the machine flags. EntityShape's Kind* and
// MachineFlag* constants must match the ones here (EntityLayoutGuardTests).
//
// Shape kinds (color.a) and their params:
//   0 tank hull   — .xy = facing (cos, sin), .z = tread travel (cells),
//                   .w = state (0 normal, 1 hit flash, 2 spawn protection)
//   1 tank turret — .xy = barrel direction (cos, sin), .w = state as above
//   2 hot shot    — .x = trail length (cells), .y = core radius (cells)
//   3 ore beam    — .x = length (cells, along +x), .y = ore flow (1 just after a unit passed, fading to 0),
//                   .z = phase, .w = transition. Ore flows toward +x (the base).
//   4 relay       — .x = powered (0/1), .y = pump fraction, .z = animation phase (s), .w = machine flags
//   5 harvester   — .xy = facing (cos, sin), .z = drum/tread phase, .w = machine flags
//   6 sense ring  — .x = ground heat / stall temperature, .y = radius (cells), .z = phase, .w = machine flags
//   7 link        — .x = length (cells, along +x), .y = heat flow (relay pump rates), .z = phase,
//                   .w = 0 live, 1 blocked. Heat flows toward +x (the base).
//   8 battery     — .x = capacitor fill (0..1)
//   9 preview beam — .x = length (cells, toward the network point at +x), .y = 0 would link, 1 blocked,
//                   2 too far, .z = phase, .w = distance to the first blocking cell
//  10 reach area  — .x = reach (cells), .y = 0 square (relay cooling), 1 circle (turret range), .z = phase
//  11 network coverage — .x = radius (cells), .y = cooling strength (0..1), .z = phase, .w = 1 while the tank is off network
//  12 tether      — .x = length (cells, toward the node at +x), .y = cooling strength, .z = phase
//  13 radar blip  — .x = life (1 just found, 0 gone), .y = echo (RadarEcho: 0 solid, 1 metal, 2 cavity, 3 heat,
//                   4 life, 5 ore),
//                   .z = phase, .w = flash (1 just found, 0 after a few ticks);
//                   color.r = what heat left of the ray (0..1), color.g = a per-cell seed
//  14 radar pulse — .x = front radius (cells), .y = fade (1 just sent, 0 at full range)
//                   (machine flags: 1 stalled, 2 carried, 4 held back by a full base)
//  16 power label — .xy = packed 5x7 glyph bitmap (low 20 bits, then high 15), .z = dots per cell
//                   (0 for a solid bar), .w = opacity
//  18 emberite  — .x = charge (1 carrying its heat, 0 spent), .y = burrowing (1 in soil: a dim glow under the
//                   ground), .z = animation phase (the wriggle), .w = 0. With .w = 1 the quad is a nest's
//                   resting embers instead: .x = 1 while creatures rest in it (2 while it stirs before a wave: they
//                   flare), .y = the reach of the embers (cells).
//  19 gatling   — .xy = facing (cos, sin), .z = drum phase (its barrels turn with it), .w = machine flags
//  20 base shield — .x = shield strength (0..1 of its maximum), .y = armor (0..1), .z = hit glow (1 just hit, 0 after
//                   ~0.4 s), .w = flags (1 shell, 2 dark, 4 bars) plus the animation phase (0..1 of a 4 s cycle) as a fraction;
//                   color.rgb = team colour
//  21 shield hit  — .x = age (0 just hit, 1 gone), .y = shield strength when hit (0..1), .z = 1 for the killing hit;
//                   color.rgb = team colour
//  22 tank shield — .x = shield fill after the hit (0..1; the bubble erodes from its edge inward as it falls; 0 is the
//                   hit that broke it: the whole bubble flashes and collapses), .y = age of
//                   the latest hit's effects (0 just hit, 1 or more = spent), .z = the angle (radians) from the tank's
//                   centre to where the shot landed (TankShieldQuads.NoHitAngle, 10, for none), .w = how much of the
//                   bubble shows in whole 255ths plus the animation loop as the fraction (0..1 over
//                   TankShieldQuads.LoopSeconds); color.rgb = team colour. Drawn only while hits are fresh.
//  23 tank fire   — the exposed core: .x = the flames' level (0..1, eased in from the fire level: the soot ring's reach),
//                   .y = seconds into a 4 s animation cycle, .z = a per-tank seed (0..1), .w = the hull heading (radians;
//                   the quad is upright). Drawn while the hull burns, and for a moment where a tank died.
//                   color.r = hull fill (0..1: the exposed core's size and colour), color.g = the core's glow (0 hidden,
//                   1 full, up to 2 as a dying core flares), color.b = soot (0..1: the soot ring, crust, drips, pulse).
//                   A dead tank's glow has .x = 0 and color.b = 0: only a soft glow.
//  24 tank fire light — .x = fire level (0..1: the pool's size), .y = seconds into a 4 s cycle, .z = a per-tank seed (0..1),
//                   .w = hull fill (0..1: the light takes the core's colour); color.r = strength (eased in). Additive.
//  25 fire particle — .x = age (0 born, 1 gone), .y = FireParticleKind (0 spark, 1 smoke, 2 flame lick, 3 cinder),
//                   .z = strength (0..1: fades it in with the fire), .w = a per-particle seed (0..1); local +x along its
//                   motion (smoke: upright). color.rgb = smoke (0, radius, 0), a flame lick (stretch, radius, swirl
//                   phase), a spark or cinder (trail length, head radius, starting heat).
//  26 danger ring — .x = radius (cells), .y = level (NestDangerLevel: 0 dormant, 1 awake, 2 swarming: it pulses),
//                   .z = phase, .w = opacity; color.rgb = the ring's colour
//
// color.rgb is the team colour (tank hull, turret), the body colour (hot shot),
// the tint (reach area, coverage, power label) or data (radar blip, tank fire, fire particle). Every other kind owns its colours
// here in the shader; the C# side sends black for them.
// ============================================================================

#ifndef EMBERWARD_ENTITY_LAYOUT_HLSLI
#define EMBERWARD_ENTITY_LAYOUT_HLSLI

// Shape kinds (color.a); EntityShape's Kind* constants must match (EntityLayoutGuardTests).
static const float kKindTankHull = 0.0;
static const float kKindTankTurret = 1.0;
static const float kKindHotShot = 2.0;
static const float kKindOreBeam = 3.0;
static const float kKindRelay = 4.0;
static const float kKindHarvester = 5.0;
static const float kKindSenseRing = 6.0;
static const float kKindLink = 7.0;
static const float kKindBattery = 8.0;
static const float kKindPreviewBeam = 9.0;
static const float kKindReachArea = 10.0;
static const float kKindCoverage = 11.0;
static const float kKindTether = 12.0;
static const float kKindRadarBlip = 13.0;
static const float kKindRadarPulse = 14.0;
static const float kKindGenerator = 15.0;
static const float kKindPowerLabel = 16.0;
static const float kKindTurret = 17.0;
static const float kKindEmberite = 18.0;
static const float kKindGatling = 19.0;
static const float kKindBaseShield = 20.0;
static const float kKindShieldHit = 21.0;
static const float kKindTankShield = 22.0;
static const float kKindTankFire = 23.0;
static const float kKindTankFireLight = 24.0;
static const float kKindFireParticle = 25.0;
static const float kKindDangerRing = 26.0;

// Machine flags (params.w); EntityShape's MachineFlag* constants must match.
static const float kMachineFlagStalled = 1.0;
static const float kMachineFlagCarried = 2.0;
static const float kMachineFlagLimited = 4.0;

struct PSIn
{
    float4 pos : SV_POSITION;
    float2 local : TEXCOORD0;
    float4 color : COLOR0;
    float4 params : TEXCOORD1;
};

#endif // EMBERWARD_ENTITY_LAYOUT_HLSLI
