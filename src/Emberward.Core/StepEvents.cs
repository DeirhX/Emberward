namespace Emberward.Core;

using Emberward.Core.Snapshots;
using System.Runtime.InteropServices;
using Emberward.Core.Entities.Machines;
using Emberward.Core.Types;

/// <summary>What happened in a <see cref="StepEvent"/>.</summary>
public enum StepEventKind
{
    /// <summary>A tank fired a bullet. <see cref="StepEvent.Color"/> is the tank.</summary>
    ShotFired,

    /// <summary>
    /// A bullet struck something and burst. <see cref="StepEvent.Color"/> is the shooter, <see cref="StepEvent.Target"/>
    /// what it struck. A bullet that flies off the map makes no event.
    /// </summary>
    BulletImpact,

    /// <summary>An explosion threw shrapnel: a bullet burst, a tank's death or a machine's wreck.</summary>
    Explosion,

    /// <summary>A shrapnel fragment struck something hard or burnt out inside the map.</summary>
    ShrapnelSpent,

    /// <summary>Enemy fire took armor (shield or hull) off a tank's hull (not blocked by its respawn shield, nor, with the tank shield rule, by its shield). <see cref="StepEvent.Color"/> is the tank.</summary>
    TankHit,

    /// <summary>
    /// A tank was destroyed. <see cref="StepEvent.Position"/> is where it died, <see cref="StepEvent.Color"/> the tank,
    /// <see cref="StepEvent.Cause"/> what killed it and <see cref="StepEvent.Killer"/> the colour of the tank the
    /// death is credited to (<see cref="Entities.Tanks.Tank.KillerColor"/>): the enemy whose shell did it (its own,
    /// or its turret's or gatling's), or the last enemy whose shell hurt the tank shortly before heat, cold or a
    /// creature finished it. -1 when it is nobody's: no enemy's shell in that time, a recall, the loss of its base.
    /// </summary>
    TankDied,

    /// <summary>A destroyed tank came back at its base.</summary>
    TankRespawned,

    /// <summary>A tank moved one step under its own power (a respawn is not a move).</summary>
    TankMoved,

    /// <summary>
    /// A tank cleared <see cref="StepEvent.Count"/> cells of terrain by driving into it; <see cref="StepEvent.Torch"/>
    /// when it was torching.
    /// </summary>
    TankDug,


    /// <summary>
    /// A tank dug a cell of pyrite into its hold: <see cref="StepEvent.Count"/> units (a core cell is worth more).
    /// </summary>
    OreCollected,

    /// <summary>A live tank drove into its own base.</summary>
    Docked,

    /// <summary>A live tank drove out of its own base.</summary>
    Undocked,

    /// <summary>A tank's own base did something for it this step: repaired its shield, cooled it or banked its ore.</summary>
    BaseServiced,

    /// <summary>A tank built a machine of <see cref="StepEvent.Machine"/> and now carries it.</summary>
    MachineBuilt,

    /// <summary>A machine of <see cref="StepEvent.Machine"/> was planted. <see cref="StepEvent.Color"/> is its owner.</summary>
    MachinePlanted,

    /// <summary>A tank refused to plant the machine it carries because it would not link.</summary>
    PlantRefused,

    /// <summary>
    /// A build key asked for a machine of <see cref="StepEvent.Machine"/> that nothing in reach could pay for (not
    /// enough ore).
    /// </summary>
    BuildRefused,

    /// <summary>
    /// A tank moved <see cref="StepEvent.Count"/> units from its hold into its base's store this step, touching the core
    /// or one of its own powered relays. <see cref="StepEvent.Position"/> is the receiver and <see cref="StepEvent.Target"/>
    /// says which: <see cref="ImpactTarget.Base"/> at the core's centre (the base's
    /// <see cref="Entities.Bases.TankBase.Position"/>), <see cref="ImpactTarget.Machine"/> at the relay's position.
    /// <see cref="StepEvent.Color"/> is the tank (a reader finds it by its colour).
    /// </summary>
    OreUnloaded,

    /// <summary>
    /// A harvester banked the ore cell at <see cref="StepEvent.Position"/> into its base's store:
    /// <see cref="StepEvent.Count"/> units. <see cref="StepEvent.Color"/> is its owner.
    /// </summary>
    OreHarvested,

    /// <summary>
    /// An enemy bullet took health off a machine that survived it. <see cref="StepEvent.Color"/> is the machine's
    /// owner. The hit that wrecks a machine is <see cref="MachineWrecked"/> instead.
    /// </summary>
    MachineDamaged,

    /// <summary>A machine was shot to pieces. <see cref="StepEvent.Color"/> is its owner.</summary>
    MachineWrecked,

    /// <summary>
    /// An enemy bullet damaged a base's core. <see cref="StepEvent.Position"/> is the core cell it struck and
    /// <see cref="StepEvent.Color"/> the base's side.
    /// </summary>
    BaseDamaged,

    /// <summary>The hit that destroyed a base; its fields are those of <see cref="BaseDamaged"/>.</summary>
    BaseDestroyed,

    /// <summary>
    /// A base's gate shield took an enemy bullet (DESIGN.md §4.9), or took one and is spent, which opens the gates to
    /// the enemy. <see cref="StepEvent.Position"/> is the gate cell, <see cref="StepEvent.Color"/> the base's side.
    /// </summary>
    BaseShieldHit,
    BaseShieldBroken,

    /// <summary>A base's gate shield is back and its gates are shut to the enemy again. At the base's centre.</summary>
    BaseShieldRestored,

    /// <summary>
    /// A docked tank burned <see cref="StepEvent.Count"/> units from its hold to restart its base.
    /// <see cref="StepEvent.Position"/> is the core's centre (<see cref="StepEvent.Target"/> is
    /// <see cref="ImpactTarget.Base"/>), <see cref="StepEvent.Color"/> the tank.
    /// </summary>
    RestartOreBurned,

    /// <summary>
    /// A tank bought a level for the machine of <see cref="StepEvent.Machine"/> at <see cref="StepEvent.Position"/>;
    /// <see cref="StepEvent.Count"/> is its new level. <see cref="StepEvent.Color"/> is the tank.
    /// </summary>
    MachineUpgraded,

    /// <summary>
    /// A tank's machine upgrade press bought nothing; <see cref="StepEvent.Count"/> is the
    /// <see cref="Entities.Machines.MachineUpgradeRefusal"/>. <see cref="StepEvent.Color"/> is the tank.
    /// </summary>
    MachineUpgradeRefused,

    /// <summary>
    /// A turret or a gatling (<see cref="StepEvent.Machine"/>) at <see cref="StepEvent.Position"/> fired;
    /// <see cref="StepEvent.Count"/> is the colour of the enemy tank it fired at, or -1 for an emberite.
    /// <see cref="StepEvent.Color"/> is the turret's owner.
    /// </summary>
    TurretFired,

    /// <summary>
    /// A tank off its network burned <see cref="StepEvent.Count"/> units of pyrite from its hold to buy back its
    /// reserve (<see cref="Entities.Tanks.TankReserveSystem"/>). <see cref="StepEvent.Color"/> is the tank.
    /// </summary>
    ReserveBurned,

    /// <summary>
    /// A tank's reserve passed into another stage; <see cref="StepEvent.Count"/> is the new
    /// <see cref="Entities.Tanks.TankReserveStage"/>. <see cref="StepEvent.Color"/> is the tank.
    /// </summary>
    ReserveStageChanged,

    /// <summary>A nest of emberites woke; <see cref="StepEvent.Position"/> is the nest, <see cref="StepEvent.Count"/> the creatures in it.</summary>
    NestWoke,

    /// <summary>
    /// An emberite bit a tank, dumping its heat into it and hurting it. <see cref="StepEvent.Color"/> is the tank;
    /// <see cref="StepEvent.Count"/> is the pyrite the bite took from its hold.
    /// </summary>
    EmberiteBit,

    /// <summary>An emberite was killed at <see cref="StepEvent.Position"/>; what it carried spilled into the ground there.</summary>
    EmberiteKilled,

    /// <summary>
    /// A woken nest stirred: a pulse of creatures follows in <see cref="Config.Tweaks.Emberites.StirTicks"/>.
    /// <see cref="StepEvent.Position"/> is the nest, <see cref="StepEvent.Count"/> the creatures ready to leave it.
    /// </summary>
    NestStirred,

    /// <summary>
    /// A resting emberite starved for want of heat in its nest. <see cref="StepEvent.Position"/> is the nest,
    /// <see cref="StepEvent.Count"/> the creatures it has left (0: the nest is cleared).
    /// </summary>
    EmberiteStarved,

    /// <summary>
    /// A tank's shield took a hit (<see cref="Config.Tweaks.TankShield"/>). <see cref="StepEvent.Position"/> is where it
    /// landed, <see cref="StepEvent.Color"/> the tank, <see cref="StepEvent.Count"/> the damage the shield took.
    /// </summary>
    TankShieldHit,

    /// <summary>A hit emptied a tank's shield (recorded instead of <see cref="TankShieldHit"/> for that hit). Same fields.</summary>
    TankShieldBroken,

    /// <summary>
    /// The next powerup drop was decided (<see cref="Entities.Powerups.PowerupDrops"/>), a full period before it lands.
    /// <see cref="StepEvent.Position"/> is its site, <see cref="StepEvent.Count"/> its
    /// <see cref="Entities.Powerups.PowerupKind"/> as a number. The first drop's is recorded in the first step.
    /// </summary>
    PowerupAnnounced,

    /// <summary>A powerup drop landed. <see cref="StepEvent.Position"/> is its site, <see cref="StepEvent.Count"/> its kind.</summary>
    PowerupLanded,

    /// <summary>
    /// A tank took a powerup drop. <see cref="StepEvent.Position"/> is the drop's site, <see cref="StepEvent.Color"/>
    /// the tank, <see cref="StepEvent.Count"/> the kind.
    /// </summary>
    PowerupCollected,

    /// <summary>A powerup drop nobody took is gone. <see cref="StepEvent.Position"/> is its site, <see cref="StepEvent.Count"/> its kind.</summary>
    PowerupExpired,

    /// <summary>
    /// A tank's timed powerup ran out, or was lost when the tank died. <see cref="StepEvent.Position"/> is the tank,
    /// <see cref="StepEvent.Color"/> its colour, <see cref="StepEvent.Count"/> the kind.
    /// </summary>
    PowerupEnded,
}

/// <summary>
/// What a <see cref="StepEventKind.BulletImpact"/> struck; for <see cref="StepEventKind.OreUnloaded"/> and
/// <see cref="StepEventKind.RestartOreBurned"/>, what took the ore: <see cref="Base"/> the core, <see cref="Machine"/> a relay.
/// </summary>
public enum ImpactTarget { Terrain, Tank, Machine, Base }

/// <summary>
/// One thing that happened during a <see cref="World.Advance"/> step. Fields that do not apply to the
/// <see cref="Kind"/> keep their defaults; <see cref="Color"/> is -1 when no tank or side is involved, and
/// <see cref="Killer"/> is -1 for every kind but a <see cref="StepEventKind.TankDied"/> credited to another tank.
/// </summary>
public readonly record struct StepEvent(
    StepEventKind Kind,
    Position Position,
    int Color = -1,
    MachineType Machine = default,
    ImpactTarget Target = default,
    int Count = 0,
    bool Torch = false,
    Entities.DamageCause Cause = default,
    int Killer = -1);

/// <summary>
/// The events of the current step, cleared when the next one begins. It is output: nothing carries over to the next
/// step, and the two systems that read it do so later in the step that recorded (the swarm, for ore dug from a nest;
/// the match score, for deaths), so recording never changes a result. The backing list keeps its capacity, so
/// recording does not allocate once it has grown to a busy step's size.
/// </summary>
[NotSaved("output only: what happened in the last step, for sound and the HUD; cleared before each step")]
public sealed class StepEventLog
{
    private readonly List<StepEvent> _events = new();

    public int Count => _events.Count;

    /// <summary>The events recorded since the last <see cref="Clear"/>, in the order they happened.</summary>
    public ReadOnlySpan<StepEvent> Events => CollectionsMarshal.AsSpan(_events);

    public void Record(in StepEvent e) => _events.Add(e);

    public void Clear() => _events.Clear();
}
