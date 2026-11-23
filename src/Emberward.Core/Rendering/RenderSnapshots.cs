namespace Emberward.Core.Rendering;

using Emberward.Core.Entities;
using Emberward.Core.Entities.Machines;
using Emberward.Core.Entities.Projectiles;
using Emberward.Core.Types;
using Emberward.Core.Network;

public readonly record struct TankRenderState(
    Position Position,
    int Color,
    int Direction,
    bool IsDead,
    DirectionF TurretDirection,
    int HitFlashTicks = 0,
    bool IsProtected = false);

public readonly record struct ProjectileRenderState(
    PositionF Position,
    VectorF Velocity,
    ProjectileType Type,
    bool IsAlive);

/// <param name="Charge">Capacitor fill, 0..1.</param>
/// <param name="HasCapacitor">It has a capacitor: every planted machine on the energy network, none without it.</param>
public readonly record struct MachineRenderState(
    Position Position,
    MachineType Type,
    MachineState State,
    bool IsAlive,
    bool IsPowered,
    bool IsPumping = false,
    float PumpFraction = 0f,
    bool IsPumpLimited = false,
    bool IsStalled = false,
    Offset Heading = default,
    float SenseHeat = 0f,
    float Charge = 0f,
    bool HasCapacitor = false,
    float GenerationFraction = 0f);

/// <param name="HeatFlow">Heat carried in the last tick, in full relay pump rates (2 = two relays' worth).</param>
/// <param name="OreFlow">Ore unloaded into the network on its way home: 1 just after a unit passed, fading to 0.</param>
public readonly record struct LinkRenderState(
    Position From,
    Position To,
    LinkType Type,
    bool IsAlive,
    float HeatFlow = 0f,
    bool FlowsFromTo = true,
    float OreFlow = 0f,
    bool OreFlowsFromTo = true);

/// <summary>A creature out of its nest, as it is drawn.</summary>
/// <param name="Id">Stable for the creature's life; drives its wriggle.</param>
/// <param name="IsCharged">It still carries its heat: drawn bright, and dull once spent.</param>
/// <param name="IsBurrowing">It is in soil: drawn as a dim glow under the ground.</param>
public readonly record struct EmberiteRenderState(int Id, PositionF Position, VectorF Velocity, bool IsCharged, bool IsBurrowing);

/// <summary>A nest, as it is drawn: <paramref name="Occupied"/> creatures are in it, resting.</summary>
/// <param name="IsStirring">A pulse of creatures is about to leave it: its embers flare.</param>
public readonly record struct NestRenderState(Position Centre, int Radius, int Occupied, bool IsAwake, bool IsCleared, bool IsStirring = false);

/// <summary>A powerup drop on the ground, as it is drawn (<see cref="Entities.Powerups.PowerupDrops"/>).</summary>
/// <param name="Cell">The middle of its disc.</param>
/// <param name="TicksUntilExpiry">Ticks before it is gone if nobody takes it.</param>
/// <param name="Radius">Cells from <paramref name="Cell"/> within which a tank's sprite takes it.</param>
public readonly record struct PowerupRenderState(Position Cell, Entities.Powerups.PowerupKind Kind, int TicksUntilExpiry, float Radius)
{
    /// <summary>Seconds before it is gone if nobody takes it.</summary>
    public float SecondsUntilExpiry => TicksUntilExpiry / (float)Config.Tweaks.Perf.TargetFps;
}

/// <summary>How dangerous a discovered nest is now (<see cref="NestDanger"/>).</summary>
public enum NestDangerLevel : byte
{
    /// <summary>Asleep with creatures in it: it wakes to a tank that comes within its reach.</summary>
    Dormant,

    /// <summary>Awake, hunting a tank, with none of its creatures out.</summary>
    Awake,

    /// <summary>Its creatures are out.</summary>
    Swarming,
}

/// <summary>
/// A nest a side has discovered and that still has creatures, as its danger ring is drawn: centred on the nest, at the
/// nest's ground plus a margin (<see cref="Entities.Emberites.EmberiteSwarm.DangerRadius"/>).
/// </summary>
public readonly record struct NestDanger(Position Centre, float Radius, NestDangerLevel Level);
