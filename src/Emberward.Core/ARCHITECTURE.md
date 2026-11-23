# Emberward.Core: the simulation

Core is the whole game without a window: terrain, heat, tanks, machines, the link network, projectiles, level
generation and the bot. It takes one `ControllerOutput` per tank per tick and exposes its state as plain data. It
references no SDL, ImGui or GPU code (`ArchitectureGuardTests`). The rules the tests enforce (determinism, energy
conservation, one heat solver) are listed in `AGENTS.md`; this file says where things are and how they fit.

## Module map

```
World.cs                     World: owns every subsystem, runs Advance over its list of systems (WorldSystem)
SimulationProfile.cs         Per-system timings, one section per system label
StepEvents.cs                StepEvent / StepEventLog: what happened during a step (World.StepEvents)
WorldStateHash.cs            One number for a world's state (determinism tests), and SyncDigest, which adds what it
                             does not read: heat per cell, charge per holder, economy, outcome and latent state (what
                             machines in a network match compare; each owner mixes its own private state)
StatusLevel.cs               Ok / Warn / Bad for a HUD gauge (Tank.HullLevel, TankBase.ChargeLevel, ...)
Match/
  MatchRules.cs              Which optional systems a world runs (regrowth, pockets, ore, the core race, the clock);
                             World.Rules, the presets (Standard, Arcade) and ForMode, which picks one by MatchMode
  MatchMode.cs               MatchMode: Standard or Arcade, what a host asks MatchRules.ForMode for
  MatchOutcome.cs            MatchReferee: Victory / Defeat / InProgress for one tank (World.MatchOutcomeFor)
  MatchResult.cs             Latches the referee's first answer (World.MatchWinner, World.IsMatchOver)
  MatchScore.cs              (only when MatchRules.TimeLimitSeconds is set) The timed match: each tank's kills and
                             deaths, the clock, the sole leader's win when it runs out, sudden death while tied
                             (World.Score.cs: World.ScoreOf, MatchClock, CopyStandings, ScoreLeader)
  SectorGoal.cs              The core tap: each base's CoreTap (tap, hold timer, tapped), the first base to tap the core
Config/
  Tweaks.cs                  Every gameplay and simulation constant (const / static readonly only)
  Tweaks.Turrets.cs          The turret's constants (Tweaks is partial; Tweaks.cs is near the file-length cap)
  Tweaks.Arcade.cs           Arcade mode's constants: the length of the match, the respawn wait
  Tweaks.Powerups.cs         The powerup drops' constants: schedule, kinds, the site search, what a bot does about them
  SimulationSettings.cs      The thermal and tank-heat constants, copied from Tweaks; tests inject their own
  FastRandom.cs              Small seeded RNG used by the simulation
Terrain/
  TerrainPixel.cs            TerrainPixel, ThermalMaterial, TerrainBehavior and the Pixel lookup tables
  TerrainGrid.cs             Pixel array, change list, SetPixel (metered) and SetPixelRaw (level gen only)
  TerrainGrid.Heat.cs        Terrain and air temperature fields, CoolDown, energy add / extract API
  TerrainGrid.HeatTiles.cs   The solver step over the tiles that are still changing
  TerrainGrid.HotRock.cs     PlaceHotRockPocket, PlaceFrostRockPocket, SeedHeatedPixel
  TerrainGrid.Ore.cs         PlaceOre (level gen); FindOre and the per-tile ore counts behind it
  TerrainGrid.Materialization.cs, .Decoration.cs, .Render.cs   Level-gen markers to pixels, cleanup, Draw*ToSurface
  SightlineChanges.cs        The cells that started or stopped blocking a line of sight, for the link map
  ITerrain*.cs               Narrow interfaces over TerrainGrid (pixel read / write, heat source, change tracker)
Regrowth/                    (only in a world whose MatchRules.TerrainRegrowth is on)
  TerrainRegrowth.cs         Dirt regrowth: the rules, applied to each cell when its appointment comes
  RegrowthSchedule.cs        The appointments: at most one per cell, filed by tick
  RegrowthWrites.cs          The pixel writes regrowth has yet to hear of (TerrainGrid lists them)
  RegrowthBlockers.cs        Where no dirt grows (link node range), filed by map area
Thermal/
  TerrainHeatEngine.cs       The exchange stencil, capacity, conductance and loss tables, ResolveCell
  ThermalScheduler.cs        Per tick: step or not (nothing active, or throttled when nearly everything is)
  ThermalActivityTracker.cs  Active tiles (which tiles to step) and the heat dirty rect (for the renderer)
  ThermalLedger.cs           Energy created or destroyed, by kind
  TankHeatEngine.cs          One tank's heat exchange with terrain and air
Entities/
  Tanks/Tank.cs              Tank state and the per-tick order (below); delegates to the classes beside it
  Tanks/TankList.cs          Tank collection
  Tanks/SimulationContext.cs The slice of World a tank sees during Advance
  Tanks/TankTurret.cs        Aim, cooldown, bullet spawn
  Tanks/TankHeatModel.cs     Heat-flow formula shared by tank contact with terrain and air
  Tanks/TankThermals.cs      Tank heat, network coverage floor, base sink, refrigeration, ledger bookings
  Tanks/TankDigger.cs        Movement, terrain contact, dig / torch, mining ore into the hold; owns the
                             tank's RNG
  Tanks/TankBuildController.cs   Build keys: build, carry, plant; the payer rule (Quote)
  Tanks/BuildQuote.cs        BuildQuote, OrePayer, BuildRefusal: what a machine costs here and which stock pays
  Tanks/TankLifecycle.cs     Damage, death (the hold spills as ore, OreDrop), respawn (the wait is RespawnDelay's
                             to say: flat under MatchRules.QuickRespawn), spawn shield, who killed it
  Tanks/TankSpriteShape.cs   The tank's one shape (its sprite's cells): terrain contact, bullet hits, planting
  Bases/TankBase.cs, TankBases.cs   Bases (reactor, radiator, engine, charge, ore store)
  Bases/BaseUpgrades.cs      BaseUpgrade (base track and tank track), levels, costs and what each level does
  DamageCause.cs
  Machines/Machine.cs        MachineType, MachineState, Machine
  Machines/MachineBehavior.cs   MachineBehavior (action, interval, cost, BuildKey, traits), MachineBehaviors table
  Machines/HarvesterPathing.cs  Where a harvester can stand; the flood that finds the ore nearest by path
  Machines/MachineList.cs    Machine collection, filed by place (`CollectNear`); wrecks dead machines (leave the network, charge to heat, scorch)
  Machines/MachinePlacementPreview.cs   Result of World.PreviewPlacement
  Machines/MachineUpgrades.cs   MachineUpgradePath, refusals, MachineUpgradeLevel / Offer and what each path and level is worth
  Machines/MachineUpgradeSystem.cs   (only when MatchRules.MachineUpgrades is on) Per-machine upgrades bought next to the
                             machine from the base store (DESIGN.md §4.9); World.MachineUpgradeOfferFor / MachineUpgradeOf answer
  Tanks/TankShieldSystem.cs   (only when MatchRules.TankShield is on) The tank's shield in front of its hull (DESIGN.md
                             §4.7): damage takes it first (Absorb), it refills after a delay (Advance); the state is kept
                             per tank colour here, not on Tank. World.TankShieldOf answers. FireLevelOf gives the fire on a damaged hull; TankThermals.Advance turns it into heat (TankHeatModel.FireHeatShare, the level squared) booked as injected in the tank, before its exchange with the ground, and in the air around it. TankDamageEvents records a hit as
                             TankShieldHit / TankShieldBroken / TankHit step events.
  Tanks/TankReserveSystem.cs, TankReserve.cs   (only when MatchRules.TankReserve is on) The tank's reserve (DESIGN.md
                             §4.9): the recall key burns carried pyrite to lower the off-network floor, the stages of
                             running out and their effects, a generator holding the clock; World.ReserveOf answers
  Machines/MachineMaterializer.cs   The machine a tank carries before it is planted
  Machines/TurretAction.cs   The turret's action: targeting, firing, overheat slowdown (energy matches only)
  Projectiles/Projectile.cs  ProjectileType, Projectile and its factories
  Projectiles/ProjectileList.cs   ProjectileBehavior table, advance, explosions; owns the projectile RNG
  Projectiles/ExplosionDesc.cs    ExplosionFactory (shrapnel fans)
  Emberites/Emberite.cs                Emberite (a creature out of its nest), Nest, NestPhase, EmberiteState
  Emberites/EmberiteSwarm*.cs          The emberites (MatchRules.Emberites): nests wake, pulse, recharge and starve (Nests), creatures move, bite and
                             go home (Movement), shots find and kill them (Combat), hashes and snapshots of the swarm
  Emberites/EmberiteFlowField.cs       The coarse cost-to-goal field creatures follow, one per hunted tank and per awake nest
  Powerups/PowerupKind.cs    PowerupKind, the kind table (PowerupKinds) and what hosts read: PowerupForecast (the next
                             drop), TankPowerupView (a tank's running powerups), PowerupEffects (what a tank asks)
  Powerups/PowerupDrops.cs   (only when MatchRules.Powerups is on) The powerup drops: the schedule and its shuffle bag,
                             drops on the ground, who takes one, each tank's timed effects; hashes
                             (World.Powerups.cs: World.PowerupForecast, PowerupDropCount, CopyPowerupRenderStates, PowerupsOf)
  Powerups/PowerupDrops.Sites.cs   Where a drop lands: the bounded search for fair ground a tank can reach
Network/                     The link network (not network matches: those are Desktop/Net and Emberward.Protocol)
  LinkPoint.cs, Link.cs, LinkMap.cs   Nodes, edges, refresh, power and flow
  NetworkCoverage.cs         Coverage nodes and the coverage a tank gets at a position
  NetworkStatus.cs           One base's harvesters, relays and heat store, for the HUD (World.GetNetworkStatus)
  EnergyNetwork.cs           (only when MatchRules.EnergyNetwork is on) Capacitors, generator reserves and islands rooted at bases or generators
                             (DESIGN.md §4.9); MachinePower / BasePower, what World.MachinePowerOf / BasePowerOf answer
  EnergyNetwork.Throughput.cs   Each island as a tree: node limits (Conduit), losses as heat at the node, ThroughputHeat
Sensing/
  SideSensing.cs             The vision system: which sides are tracked, what each sees through, and each tank's radar
  VisionMap.cs               Fog of war for one side: visibility levels, explored cells, the terrain as last seen
  VisionEye.cs               One source's sight, kept between updates, and the ray and wall casting that fills it
  Radar.cs                   A tank's radar: pulses running out from the tank and the fading contacts where they reflected
  RadarContact.cs            What a radar return is: RadarEcho, RadarContact, RadarTarget
  RadarReach.cs              The radar's ranges and heat budget, scaled per tank by its radar upgrade
Collision/
  CollisionSolver.cs         Point query against tanks, machines and terrain (visitor callbacks); what stops a tank's hull
  WorldSectors.cs            Sector buckets for the tank broad phase
  Raycaster.cs               Bresenham lines; HasOpenLine (the one line-of-sight test)
Input/
  Controller.cs              ControllerOutput
  ControllerOutputCodec.cs   ControllerOutput as 32 bits (network, input logs); quantises the aim
Bots/                        The computer player: produces a ControllerOutput like a person would
  BotTankAI.cs               The bot's mode machine (TwitchMode)
  BotNavigator.cs, BotGunner.cs, BotBehaviorProfile.cs   Bot movement, fire decisions, play styles
  BotRandom.cs               The one random stream a bot and its parts share: a FastRandom, so its state can be
                             hashed and saved (System.Random's cannot)
  BotTrail.cs                The way the bot came, retraced to get home
  BotSiege.cs, BotLoiter.cs  Laying siege to a base it can break; waiting outside one it cannot
  BotTankAI.Drops.cs         Going for a powerup drop near it, and giving up one it cannot reach
  BotStuckWatch.cs           Going nowhere near one spot, and the detour taken instead
  BotProspector.cs           Where a prospecting bot digs (BotTankAI.Prospecting: when it prospects, digging)
Snapshots/                   A match's state for a player who joins it in progress (docs/late-join-design.md §9)
  WorldSnapshot.cs           Save, Load, Compress, Decompress: canonical bytes of the world and the bots beside it
                             Digest: a hash of the raw bytes, which two players with one world agree on
  SnapshotWriter.cs, SnapshotReader.cs   The general saver: every field reachable, breadth first, lists through
                             their public API; loading fills the built world's own objects in place
  SnapshotLayout.cs          What writer and reader agree on: saved fields in order, kinds of value, shared constants
  NotSavedAttribute.cs       [NotSaved("reason")], the only way to leave a field out; ISnapshotLoaded rebuilds an
                             index (machine buckets, link tiles) once a snapshot is loaded
  SnapshotCoverage.cs        Reachable types allowed by the reader, unsupported fields, and every exclusion
LevelGen/
  ToastGenerator.cs          Cavern generator (LevelGenMode Deterministic or Optimized)
  GeneratorUtils.cs          Shared drawing and placement helpers (lines, TooClose)
  HotRockPockets.cs          Scatters hot rock pockets
  FrostRockPockets.cs        Scatters frost pockets
  OreDeposits.cs             Seeds pyrite: specks, pockets and lumps with a warm core (OreDeposit, OreDepositKind)
  TerrainTestPatternGenerator.cs   Synthetic terrain for the renderer's --terrain-test mode
Rendering/
  RenderSnapshots.cs         TankRenderState, ProjectileRenderState, MachineRenderState, LinkRenderState, EmberiteRenderState, NestRenderState,
                             PowerupRenderState
  TankSprites.cs             Tank bitmaps and colour palettes
Resources/Reactor.cs         Reactor, ReactorState, Heat, Health: a tank's or base's heat and shield
Resources/OreStore.cs        OreStore: a tank's hold, a base's store (ore is the only currency)
Resources/OreDrop.cs         Puts ore back into the ground, nearest free cells first, not under tanks or machines (a dead tank's hold)
Types/Position.cs, RectMath.cs   Position, Offset, Size, PositionF, VectorF, DirectionF, Color, Rect, BoundingBox
Types/DirtyRect.cs           The bounding box of changed cells, for the renderer (heat and vision)
Types/Fnv1a.cs               The 64-bit FNV-1a mixer the world, rules and economy hashes share
```

## World

`new World(size, simulationSeed, rules)` creates the subsystems. `MatchRules` says which optional systems the world
runs: regrowth, hot and frost rock pockets, ore, whether tapping the core wins the match, and the rules of arcade
mode (DESIGN.md §6.8: `TimeLimitSeconds`, `BasesHold`, `QuickRespawn`, `Powerups`; `MatchRules.ForMode` gives a host the preset
for a `MatchMode`). It is the one switch for
them (a system that can be turned off gets a flag there), and it is mixed into the hash clients compare before a
network match starts (`WorldStateHash.ComputeReady`). `World.Initialize(terrain, spawns, ...)` copies in a generated
level, materializes it, builds the bases, places hot and frost rock pockets (if the rules ask; the hot ones also give
the core, `World.CorePocket`, and with it the `SectorGoal`), seeds pyrite ore (if the rules ask, `SeedOre`;
`World.OreDeposits`) and adds one tank per spawn. Ore is placed last and grows each deposit cell by cell from its
start, so generation never sweeps the grid; deposit counts scale with map area (`Tweaks.Ore`), and on a small map the
distance rules shrink and a floor of pockets and lumps applies (`OreDeposits.DistanceScale`). Every RNG World owns
(projectiles, link map, regrowth, each tank, the powerup drops) is seeded from `simulationSeed`, and decorations and rock pockets from
the decoration seed (or, without one, from the spawns and
`simulationSeed`). Core never reads the clock or unseeded randomness; for a random match Desktop's `WorldFactory`
picks the seed. `MatchSetup` (`Match/MatchSetup.cs`) builds the match from it, for the desktop, a network match and the
browser alike, and derives each bot's seed (`MatchSetup.BotSeed`).

`World.Advance(getInput)` runs one fixed step: it goes through the world's systems, a list of `WorldSystem` (a
profile label and what the system does each step) that `World.BuildSystems` puts together once, when the world is
built and again when it is initialised. A system the world does not have is not in the list and costs nothing:
regrowth only when `MatchRules.TerrainRegrowth` is on, the sector goal only when the world has a core. A new system
is a new entry, in the place its order needs; the profile picks up its label. The list, by label:

1. **coll**: `CollisionSolver.Update` rebuilds the tank sector index.
2. **heat**: `TerrainGrid.CoolDown` (see below), and the solver's own parts for the profile.
3. **regrow**: `TerrainRegrowth.Advance`. No new dirt grows within the coverage radius of a planted link node
   (`RegrowthBlocker`), linked or not, so the ground a network runs through stays open. Nothing scans the
   map: each cell that can change (open ground with more than two dirt neighbours, scorch, dirt still growing) has
   an appointment in `RegrowthSchedule` at the tick it is next looked at, with the wait drawn so that the odds are
   those of rolling on every pass. Pixel writes (`RegrowthWrites`) start new appointments, and so does a blocker
   going away. Open ground away from dirt or inside a blocker has none and costs nothing. The map is cut into
   regions of `RegrowthRegionSize` cells that each take their pass on a tick of their own, so a large map's
   regrowth is spread over the ticks between passes.
4. **proj**: `ProjectileList.Advance`. A shell that reaches an enemy core takes the core's health there, the one
   place a core is damaged; where `MatchRules.BasesHold` is on it bursts on the core as on a wall instead (no damage,
   no `BaseDamaged`), so no base falls and nobody is eliminated, and `World.CanBreakBase` answers no.
5. **tanks**: `LinkMap.AgeOreFlow`, then `TankList.Advance` with the world's `SimulationContext` (built once). Per tank (`Tank.Advance`):
   idle heat, base interaction (recharge, heat sink anywhere inside the walls; touching the core, `Tank.TouchesCore`,
   the hold banks into the store through `TankBase.BankFromHold`, after the dock heat pump, bringing
   `SimulationSettings.BaseDepositHeatPerOre` a unit into the reactor and recorded as `StepEventKind.OreUnloaded` at the
   core's centre with `ImpactTarget.Base`), network coverage (with
   `MatchRules.TankReserve` on, a generator nearby holds the off-network floor), the reserve (only with that rule:
   `TankReserveSystem.Advance` burns a unit on the recall key, recorded as `StepEventKind.ReserveBurned`, settles the
   stage, recorded as `ReserveStageChanged` when it changes, and returns the stage's effects, which mask the gun and
   build keys and slow the move, dig and gun below), the relay unload (touching an own powered relay, `MachineBehavior.TakesOre`, `Tank.TouchedRelay`, the hold
   moves `Ore.RelayUnloadPerSecond` into the base store through `TankBase.BankFromHold`, booked on the links with
   `LinkMap.CarryOre` and recorded as `StepEventKind.OreUnloaded` at the relay with `ImpactTarget.Machine`; being in coverage is not enough), turret aim and fire,
   `TankDigger.Move`, spawn shield, shooting, `TankBuildController.Advance`, the upgrade keys, the machine upgrade
   key (only when `MatchRules.MachineUpgrades` is on: `MachineUpgradeSystem` buys a level for the nearest own machine
   in reach on a press of `ControllerOutput.UpgradeMachine` with a key, recorded as `StepEventKind.MachineUpgraded`
   or `MachineUpgradeRefused`), then `TankThermals.Advance` (heat from ore still in the hold, `TankHeatModel.ComputeCargoHeat`, and from tank actions,
   then exchange with the grid). Machine upgrades take effect where the rest of the game reads them: a relay asks its
   pump rate (`MachineContext.Upgrades`), the energy network holds a resized capacitor and idle draw
   (`EnergyNetwork.Resize`) and a Conduit's throughput (`EnergyNetwork.SetThroughputScale`), and vision asks a
   machine's sight bonus. Where the rules run powerup drops, **powerups** is stepped next (`PowerupDrops.Advance`,
   "Powerup drops" below), and where they run emberites, **emberites** after it.
6. **machines**: With the energy rule on, `EnergyNetwork.Refresh` first regroups the link nodes when
   `LinkMap.PowerVersion` changes. Each base's island takes the nodes whose power route ends at that base
   (`LinkPoint.IsPoweredBy`, the same rule harvester coverage uses in `NetworkCoverage`). The first generator in machine order roots
   each remaining component that has a generator. `MachineList.Advance` uses the world's `MachineContext` (a harvester asks `MachineContext.CoverageOf`, the coverage of its base's
   network at its position, before it works; it mines plain ore only and leaves lump cores to the tank's torch,
   idling as `HarvesterState.CoreOnly` when nothing else is left; without the energy rule it pays each action straight
   from its base's charge store, with no capacitor and no trips to recharge). A machine that needs the machines near a
   place (a harvester the nodes that cover it, a turret the tanks near it, anything asking whether a spot is taken) asks `MachineList.CollectNear`, which returns them
   in list order, and never walks the whole list. A relay that found no heat to pump keeps the ground's
   `QuietHeatStamp` and skips its sweep until that number changes, which it does when heat is written or stepped
   in its reach. A generator reads its local exhaust temperature, extracts heat from hotter ground past its exhaust
   area, and returns the unconverted heat there. Both writes wake the affected tiles. It uses the same quiet stamp
   to skip sampling when the ground has not changed since it found nothing to convert. A turret (`TurretAction`,
   energy matches only) counts down between looks; a look joins it to its island as a harvester joins
   (`EnergyNetwork.SetCovered` from `MachineContext.CoverageOf`), reads its ground's temperature, and, while it is on
   an island, its base is not dark and its capacitor can pay, shoots the nearest enemy tank in range and line of sight
   (`MachineContext.Tanks`, few, in spawn order; a raycast only for those in range) with a bullet carrying its
   damage (`MachineContext.Projectiles`, `Projectile.Damage`). The shot's charge is spent through
   `EnergyNetwork.Spend`, which puts it as heat beside the turret, and is recorded as `StepEventKind.TurretFired`.
   A gatling (`MachineBehavior.HuntsCreatures`) runs the same action with another gun: while creatures are out it
   asks `EmberiteSwarm.TryFindTarget` (`MachineContext.Emberites`) for the nearest one a round reaches
   (`Raycaster.HasShotThroughSoil`), aims ahead of it (`TurretAction.Lead`) and fires a `ProjectileType.Round`, which
   is spent after its `Life`, passes `Pierce` cells of soil and stops without bursting; with no creature it falls
   back on the turret's target. Its numbers are `MachineUpgrades.GatlingStatsOf`.
7. **power** (only when `MatchRules.EnergyNetwork` is on): `EnergyNetwork.Advance` (DESIGN.md §4.9). Every planted
   machine has a capacitor (`MachineBehavior.Capacitor`, its level in `Machine.Charge`). Harvesters can charge from
   a local island they can see within node coverage. Ore delivery still requires the base's network. A generator
   deposits output into the base's store if reachable and into its own shared reserve otherwise or when the base
   store is full. Reserves follow their producers on splits and become heat on wrecking. Each tick the island pays
   from these stores, tier by tier (`PowerTier`), its members' idle draw and then tops up the
   capacitors that are not full (each by at most its transfer rate and what the island's throughput tree allows it).
   Each island is a tree: a link node's parent is the next node of the island up its power route
   (`LinkPoint.UpstreamLink`), or the node a local island reached it from, and a harvester hangs off the node that
   covers it (`MachineContext.CoverageOf`). A node passes on at most `Throughput.NodeLimitPerTick` (times its Conduit
   scale, capped, and less while hot), so a chain is as fast as its slowest node. The tree is worked out
   (`RebuildTree`) only when the island's filling set or shape changes, and only over the filling capacitors and the
   nodes on their way: demand up from the leaves, each node's cap by tier, the share handed back down. Each node on
   the way loses `Throughput.LossFraction` of what it passes on into its pending charge, which settling turns into
   heat at the node; at its settling a node that carried charge reads its ground temperature and is throttled, or
   overheated and damaged (`ThroughputHeat`). A tier the store cannot pay is starved and drains
   like a cut-off machine; it is fed again once the store holds `Energy.RefeedIdleTicks` of its draw. Cost follows
   what changes: a full member's idle draw comes out of a per-tier sum, a cut-off machine's level falls on a straight
   line, and a capacitor is brought up to date (settled: its idle draw's heat put in the ground) only when it is used,
   changes island or tier, or once in `Energy.SettlePeriodTicks`. Islands are looked at again only when
   `LinkMap.PowerVersion` changes or a harvester's coverage does. Idle charge paid and not yet heat is
   counted by `EnergyNetwork.StoredCharge` alongside the generator reserves. Without the flag no machine has a
   capacitor. `World.MachinePowerOf` and `World.BasePowerOf` give an overlay its numbers (charge, capacity,
   flows a second over the last whole second, what a node passed on, its limit and heat); output only. The desktop and browser charge labels
   (`Gui/PowerLabels.cs` in Desktop, shared through `Emberward.Rendering`) read nothing else.
8. **bases**: `TankBases.Advance` (reactor sink, radiator, engine counters), then the bot's stand-in ore income for
   bases marked with `World.SetBotControlled`.
9. **goal**: `SectorGoal.Advance`, the core tap and its hold timer (only in a world with a core).
10. **score** (only when `MatchRules.TimeLimitSeconds` is set): `MatchScore.Advance`, while the match is undecided.
    It reads the step's own `TankDied` events (a death for the tank, a kill for the `Killer` it names), runs the
    clock in ticks, and once the clock has run out names the winner as soon as one tank whose base stands leads
    alone; until then the match is in sudden death. One pass over the step's events, and past the limit one over
    the tanks. The tallies are kept in the system by tank colour, saved in the snapshot and mixed into
    `WorldStateHash.Compute`.
11. **match**: `MatchResult.Latch`, the result, fixed once the referee has one: the first base to tap the core, else
    the last base standing, else the tank the score names (`MatchWinReason.TopScore`).
12. **links**: `LinkMap.Advance` brings the network up to date every `Tweaks.World.RefreshLinkMapInterval`
   (200 ms), not every tick, and at once when a machine's point is switched off.
13. **dock**: each tank notes whether it has left its base (`Tank.HasLeftBase`) and records docking (`Tank.ObserveDocking`).
14. **vision**: each `VisionMap` handed out by `World.TrackVision` is brought up to date and each `Radar` handed
    out by `World.TrackRadar` runs its pulse on (see "Radar" below).

`SimulationProfile` reports averages every 100 ticks when `ReportSink` is set: `[Profile]` (one time per system,
under its label), `[Profile+]` (heat solver parts) and `[Profile++]` (active and stepped tiles, stepped / parallel ticks).
With `Profile.AuditEnergy` (Desktop: `--energy-audit`) it adds `[ProfileE]`: the thermal energy and the ledger
residual over the report window, which should stay near float noise. That line sums every cell of the map, so it
is off by default and never runs per tick.

Queries Desktop asks instead of computing itself: `World.MatchOutcomeFor` / `IsMatchOver`,
`World.CoreTapOf` (a base's progress on the sector goal), `World.ScoreOf` (a tank's kills, deaths and place),
`World.MatchClock` (ticks and seconds left, the limit, sudden death), `World.CopyStandings` (the board in rank order,
into the caller's span) and `World.ScoreLeader` (all four inactive or empty in a match with no time limit),
`Tank.IsDockedAtOwnBase`, `Tank.IsInOwnNetwork`, `Tank.IsUnloadingOre`, `Tank.IsCargoHot`, `Tank.TouchesCore`, `Tank.QuoteBuild` (cost, payer, stock and
refusal for a machine), `Tank.LastBuildRefusal`, `World.GetNetworkStatus`, `World.PreviewPlacement`, `World.CoverageFor`,
`World.PowerupForecast` (the next drop: site, kind, ticks until it lands), `World.CopyPowerupRenderStates` (the drops
on the ground), `World.PowerupDropCount` and `World.PowerupsOf` (a tank's running powerups; all inactive or empty in a
match without drops, and none of them behind the fog),
`World.CollectCoverageNodes`, `World.TrackVision`, `World.TrackRadar`, `World.RadarOf`, and the `CopyRenderStates` snapshot of each entity list.

### Step events

Desktop learns about one-off happenings from `World.StepEvents`, never by comparing state between steps. Each
`Advance` clears the list first, then the code where something happens records a small `StepEvent` (kind, position,
the tank or side's colour, and a machine type, impact target, cell count or torch flag where they apply): shots
fired, bullet impacts, explosions, spent shrapnel, tank hits, deaths and respawns, moving, digging, mining ore into the hold,
docking and leaving the base, base service, ore unloaded by a tank or banked by a harvester, and machines built,
planted, refused, damaged and wrecked, and a powerup drop announced, landed, taken or gone and a powerup's effect
ended (`PowerupAnnounced`, `PowerupLanded`, `PowerupCollected`, `PowerupExpired`, `PowerupEnded`: the kind in
`Count`, the site in `Position`, and the tank in `Color` for the two that have one). The list
holds the last step's events until the next step begins, so a caller that runs several steps per frame reads it
after each one. It is output: nothing a step did before reads it, so recording cannot change a result
(`SimulationFingerprintTests`). Two systems read the step's own events, after the system that recorded them and only
where their rule is on: the swarm (who took ore from a nest's ground) and the match score (who died, and by whose
shell). A `TankDied` carries its `Cause`, which is what emptied the hull, and its `Killer`, the colour of the tank the
death is credited to: the enemy whose shell destroyed it (fired by that tank, its turret or its gatling: a machine's
shell carries its owner's colour), or, when heat, frost or a creature destroyed it, the last enemy whose shell took
armor off it within `Tweaks.Arcade.KillCreditSeconds` before. It is -1 with no such shell, for a recall, for a fallen
base, and never the tank's own colour. `TankLifecycle.TakeDamage` decides it and nothing else does: the tank keeps who
last hurt it and for how many more steps (`_claimant`, `_claimTicks`, set by an enemy shell that got past the respawn
shield, counted down in `BeginTick`, cleared on respawn). It is kept in every mode, as `Killer` is; the snapshot saves
it, the sync digest's latent part mixes it while a claim is live, and `WorldStateHash.Compute` does not read it, so a
world without bots hashes as it did (`MatchScoreTests`).
The `StepEventLog` reuses its storage, so recording does not allocate in steady state. Tanks record through `SimulationContext.Events`, machines through `MachineContext.Events`, projectiles through the log `ProjectileList` is built with,
and `World` itself records plantings and docking. A new kind of happening that presentation needs is a new
`StepEventKind`, recorded where it happens (`StepEventTests` covers each kind).

## Heat

Heat lives in two float fields on `TerrainGrid`, terrain and air. Energy is temperature times heat capacity
(`TerrainHeatEngine.GetHeatCapacity` per `ThermalMaterial`, `AirHeatCapacity` for air). In an air cell the
terrain field only mirrors the air field, so such a cell holds one store.

The exchange is `TerrainHeatEngine.ComputeRegionDeltaConservative`: a cell's terrain trades heat with its own air
and with the solid cells beside it, and its air with the air beside it, but only where at least one of the two
cells is open. The air inside solid ground carries nothing from one solid cell to the next, so ground conducts as
its material row says; dirt is the insulator (30 times worse than stone). `ResolveCell` then takes each material's
`Loss` from solid ground and open air, a share of the temperature above or below ambient, booked as `SunkAmbientLoss`.
The loss stands for exchange with the deep ground and the surface. It bounds a hot or frost pocket's halo, and open
air loses faster than ground. Custom settings can still pin a material for tests; the pin's change is booked as
`SunkFixedTemperature`, which stays zero with the standard materials.

Because of the loss, heat settles, and the solver only works where it has not. `ThermalActivityTracker` keeps a
grid of tiles (`ThermalActiveTileSize` cells a side). A tile is active when the last step still changed a cell in it
(`ThermalSettledGrowStep` away from ambient, `ThermalSettledFadeStep` back toward it) or when something wrote heat
into it or changed a cell's material next to heat (`SetPixel`). Each tick `TerrainGrid.CoolDown` steps the active
tiles and the ring of tiles around them (`TerrainGrid.HeatTiles.cs`), one tile per thread when there are many:
every tile's deltas are computed from the same state, tiles trade across the sides they share (each writes its own
half of the trade, `RegionEdges`), and what the tiles found is merged in tile order, so the result is the same on
any number of threads. A stepped tile's side toward a tile left out is closed, so before the step the set is grown
across every side that still carries heat (`EdgeCarriesHeat`): a settled pocket and its halo are stepped together
or not at all, never cut apart. Being stepped along does not make a settled tile active. A change of material
wakes its tile only where a cell involved is more than `ThermalWakeTemperature` from ambient. A settled pocket, old tunnel or untouched ground costs nothing until it is disturbed. With no
active tile the solver does not run; `ThermalScheduler` also holds it to every few ticks when nearly the whole map
is active.

Entities exchange energy through the `ITerrainHeatSource` API, never by writing temperatures:
`AddEnergyInRadiusArea` / `ExtractEnergyInRadiusArea` for transfers the entity books itself, `AddHeat` /
`AddHeatRadius` / `AddHeatTotalInRadiusArea` for heat that enters from outside (booked as injected). Extraction
takes only stored heat above ambient, including finite hot rock. Tank contact samples the heat capacity and
temperature of solid ground and air separately (`SampleTankContact`). The share of the footprint touching
solid ground scales its conductance. Each exchange stops at thermal equilibrium, and `ApplyTankExchange`
returns the energy to the sampled cells. Docking-area extraction uses the visited base's reservoir room.
A relay draws from the terrain field and then from the remaining air. The engine compares each draw's
energy-weighted source temperature to the base reactor's temperature, which rises as its reservoir fills. The base's
area cooling transfers heat into the same reservoir. Only the radiator books `SunkBase`, up to its vent capacity.
`SetPixel` books the capacity change when a cell changes
material; `SetPixelRaw` is unmetered and only for level generation.

`ThermalLedger` keeps `Injected`, the `Sunk*` totals and `ConvertedToCharge` (heat the base engine turned into
charge, or charge spent back as heat). `World.SumSystemThermalEnergy` (grid, tanks, base reactors) plus
`World.SumSystemCharge` (base stores and capacitors, and the energy network's reserves and pending charge) changes by exactly `Injected - TotalSunk` each tick.

The mechanics built on this (base reservoir and radiator, relays pumping heat home, the base engine and charge,
harvesters paying their work in charge, network coverage, the tank's temperature band, hot and frost rock pockets, the sector goal and
upgrades) are specified in `DESIGN.md` §4, the ore economy in §6.4; the code is in `TankBase`, `BaseUpgrades`,
`TankBuildController`, `MachineBehaviors`,
`TankThermals`, `TankHeatModel`, `NetworkCoverage`, `HotRockPockets`, `FrostRockPockets` and `SectorGoal`.
The heat render texture encodes temperature signed (`TerrainGrid.HeatToByte`), so the terrain shader can draw ground
below 0 as frost.

## Vision

`World.TrackVision(tank)` returns a `VisionMap` for that tank's side and keeps it up to date at the end of every
`Advance`; `SideSensing` keeps the tracked maps and radars and lists what each side sees through, and `World`
answers for it. A world nobody asked pays nothing. The map is output only: nothing in the simulation reads it, so
tracking cannot change a result (`VisionMapTests.TrackingVision_DoesNotChangeTheSimulation`, and the recorded
fingerprints do not move).

A side sees through `VisionSource`s (numbers in `Tweaks.Vision`):

- its tank, along open lines of sight to `TankRadius`, and everything within `TankNearRadius`, through solid ground
  too, so digging is not blind;
- its base, along open lines of sight from its core over its coverage circle (the radius of `NetworkCoverage`),
  with no near radius: it sees out only through its gates;
- its link nodes (relays, generators), over their whole coverage circles, through solid ground too, linked or not;
- every other planted machine of its base, `MachineNearRadius` around itself.

A source casts a Bresenham ray to each cell on the edge of its square, cut to a circle. A ray stops at the first
cell that blocks sight (`Pixel.BlocksSight`: what blocks a link, `Pixel.BlocksLink`, and a keep's walls, which links
pass but sight does not). The walls around the open cells in sight are then lit `WallSkin` cells deep, less with
every cell of depth; a keep's courtyard lights only the keep's one-cell wall, not the ground beyond it. What a
cell is to sight (open, courtyard, keep wall, other blocking ground) is what a change has to alter to recast a source. Each cell gets a level, 255 in plain sight
and fading over the last `EdgeFade` cells of the source's reach (a short reach: its outer half), so sight has no hard
edge anywhere.

The map holds, per cell: the level (`LevelAt`, `IsVisible`), whether it was ever seen (`IsExplored`), and `Known`,
the pixel the side last saw there (an `ITerrainPixelReadOnly`; it starts as a copy of the terrain). After each
update `KnownChanges` lists the cells whose known pixel changed, either because the terrain changed in sight or
because a cell that changed out of sight came back into it. `TryGetDirtyRect` bounds the cells whose level changed.

Cost follows what changes, never the size of the map or the number of cells in sight. Each source keeps its own
cast (a `VisionEye`: the level it gives every cell of the square it can reach), and a cell's level is the highest
any source gives it. `World` lists every source each tick, which costs next to nothing, and the map does work only
for:

- a source that is new or has moved (a source is its position and radii: the tank when it drives, a harvester when
  it crawls), which is cast, and one that is gone, whose cells fall back to what the other sources give them;
- ground that blocks sight where it did not, or the reverse, inside a source's square. A tank or base, which see
  along open lines, are cast again. A machine sees through the ground, so its rays light the same cells whatever
  the terrain is and only the walls lit around open ground can differ: it repairs the changed cells and
  `WallSkin` + 1 cells around them (`VisionCast.CastWindow`);
- a changed cell that is in sight, whose `Known` pixel is brought up to date.

A pixel change that blocks sight no differently (a scorch mark on open ground, regrowing dirt that settles) casts
nothing. The map learns of changes from the terrain's change list (`ITerrainChangeTracker`), which `World` keeps to
one step's worth; a cell written without `SetPixel` during play is not noticed, by vision or by the renderer.
`VisionMap.CellsCastInLastUpdate` is the measure of the work, and it is zero while nothing moves and nothing is dug.
`VisionMapTests.AMapKeptUpToDate_ShowsWhatAFreshLookShows` checks the result against a map that looks at the same
world for the first time. Keep it that way: maps are meant to grow, and so are networks of machines.

### Radar

`World.TrackRadar(tank)` returns that tank's `Radar` (and tracks its side's vision, which the radar needs). It is
output only, like vision (`RadarTests.AWorld_RunsTheRadar_WithoutChangingTheSimulation`). Numbers are in
`Tweaks.Radar`.

Every `PulsePeriodTicks` the radar sends a pulse from the tank's cell, which stays the pulse's origin however the
tank moves. The pulse is a fan of `RaysPerPulse` rays, turned a fraction of a ray spacing each pulse so later pulses
fill the gaps of earlier ones. Its front runs `FrontCellsPerTick` outward a tick to `Range` (both scaled by the radar
upgrade, so a pulse always takes the same time to reach full range; `PulseFront` keeps the fraction of a cell a
scaled front carries), and each ray walks only
the cells the front crossed that tick, so a return appears when the front reaches it, near ones first. One pulse is
in flight at a time; `PulseInFlight`, `PulseOrigin` and `PulseRadius` let Desktop draw its ring. A ray is a
Bresenham line that never cuts a corner: a diagonal step goes through the side cell the line crosses first, so it
cannot slip between two cells that touch only at a corner. Along a ray:

- dirt and open ground let it through, and so do the walls of the tank's own base;
- another side's machine (`RadarTarget`) reflects as `Metal` and stops it;
- another tank (a `RadarTarget` marked `Alive`) reflects as `Life` and stops it;
- rock, concrete, hot and frost rock and another side's base walls reflect as `Solid` and stop it;
- pyrite (plain or core) reflects as `Ore` and stops it;
- stepping from dirt into open ground (a tunnel, a cave) gives a `Cavity` return and the ray goes on, at most
  `WeakReturnsPerRay` times.

A ray stops at the first thing that reflects it whether or not the return is kept, so nothing behind it answers.

Every range below, and the heat budget, comes from the `RadarReach` the pulse was sent with: `Tweaks.Radar` scaled
by the tank's base's radar upgrade (`BaseUpgrades.RadarRangeScale`, `RadarHeatBudgetScale`), which World hands to
`Radar.Advance`. `Radar.PulseRange` is the reach of the pulse in flight, for drawing its ring. A pulse that runs
longer than `PulsePeriodTicks` delays the next one. Each kind of thing returns only from within its own range of the
pulse's origin (`Tweaks.Radar`): rock from
`SolidRangeMin` for a thin shell up to the full `Range` for a formation `FormationDepth` cells deep along the ray
(`Radar.FormationRange` looks behind the face, only beyond the near range), ore from `OreRangeMin` for a speck up to
`OreRangeMax` for a deposit `FormationDepth` cells deep (the same probe, counting only plain ore no warmer than
`HeatThreshold`, so a lump's warm heart lends its rim no reach), machines
from `MachineRange`, tanks from `LifeRange`, caves from `CavityRange`, heat from `HeatRangeMin` to `HeatRangeMax` by
intensity. Out of range the thing still stops the ray.

Heat weakens the rays. A ray can cross `HeatBudgetDegreeCells` in all: it starts at strength 1 and loses 1/budget for every degree a cell is above
`HeatThreshold`, scaled by the length of path a cell stands for along that ray (so the loss per distance is the same
in every direction), before anything in that cell can reflect it. What it then finds is recorded with the strength
left (`RadarContact.Strength`). Every hot cell the ray goes through gets a `Heat` contact whose strength is how hot
the cell is (1 at `HeatFullTemperature`), at most `HeatReturnsPerRay` a ray, and the ray stops when its strength runs
out: a few cells into very hot ground, many into warm ground. A reflector at or above `HeatScrambleTemperature`
returns `Heat` instead of what it is; ore included, so a lump, whose core is seeded hot (finite, not pinned) and heats its shell past
that, shows as heat and its contents are found by going there. So a hot zone shows as heat marks reaching a little way into it from the side
the pulse came from, and nothing behind them. A contact is removed as soon as the side sees its cell, and the side's
own machines are not radar targets: the radar tells a side only what it cannot see or does not remember. `Radar.HeatLoss` is the share of
the last finished pulse's rays that heat absorbed. Cold does nothing to the rays. This is the radar reading the heat
field, not changing it.

A return becomes a `RadarContact` (cell, echo, birth tick, strength) unless it tells the side nothing: the cell is in
sight, or it is terrain the side has explored and remembers as it is now. So remembered ground only shows where it
has changed. Contacts live at most `ContactLifetimeTicks` in a fixed ring (`ContactCount`, `ContactAt`), oldest
first, and never move. Each tick of a pulse, after the rays have walked, the radar reconciles the older contacts: a
new return in a cell replaces the old contact there, and a contact the front has just passed, on the nearest ray,
when that ray got at least that far, is checked against the world and dropped unless it still holds (the rock is
still there, the ore not yet mined, the ground still hot, a target still covers the cell) and is still news. A contact in the shadow of
whatever its ray stopped at is not checked, so the radar never learns what it could not reach; it fades instead.
A living target (`RadarTarget.Alive`: another tank, an occupied nest, an emberite out of its nest) is recorded at its
centre, not at the cell where the ray met its square, so one thing gives one blip however many rays meet it. Since
that cell lies past the face where its ray stopped, a living contact from an earlier pulse is checked when the front
passes it if its ray got that far or stopped on a living target whose square covers it (`Ray.OnLife`: the shadow is
that thing's own), and holds only while a living target is still centred on it (`LivesAt`); in any other shadow it
stays. Targets are gathered only for a tick that walks a pulse (`Radar.NeedsTargets`).
A tick of a pulse walks about `RaysPerPulse * FrontCellsPerTick` cells and looks at each contact once; ticks
between pulses only age contacts. Neither depends on the size of the map.

## Links

Planted machines whose behavior has `IsLinkNode` (relays, generators) and every base are `LinkPoint`s.
`LinkMap` pairs points within `MaximumTheoreticalLinkDistance`, marks a link live when it is short enough and
`Raycaster.HasOpenLine` finds no blocking pixel (`Pixel.BlocksLink`), and powers points breadth first from the
bases, recording each point's `UpstreamLink` and `Hops`. It keeps its links between refreshes and does only what
changed: a new point gets its links, a point switched off loses them, and a link's line of sight is cast again
only when a cell in a tile its line crosses started or stopped blocking (the terrain lists those cells in
`SightlineChanges`, from every pixel write). Power is searched again only when one of those changed something.
The result is the one a rebuild from nothing gives, in the same order (`LinkMapTests`). Relays book pumped heat along that
route (`LinkMap.CarryHeat`), which the renderer draws as flow. `LinkMap.PreviewLink` answers
"would a node here link?" for planting and for `World.PreviewPlacement`.

## Level generation and the bot

`ToastGenerator.Generate(size, seed, mode)` places the bases against the border (`EdgeSpawns.Place`, DESIGN.md
§6.7), scatters `Tweaks.LevelGen.TreeSize` points, joins the points and the spawns with a minimum spanning tree
(Kruskal) drawn as dirt lines, then either expands and smooths single-threaded (`Deterministic`) or dilates, adds
noise and smooths in parallel (`Optimized`, used by `--fast`). Last, it makes the ground round every base alike
(`EdgeSpawns.PrepareGround`) and carves a tunnel to any base the caves cut off. `World` asks `BaseLayout.FrontOf`
which gate of a base faces into the map, and its tank spawns inside that one.

`BotTankAI` is a mode machine (`TwitchMode`: leave base, roam and fight, return, recharge). `BotNavigator` picks
moves, `BotGunner` decides fire, and `BotProfileCycle` rotates the `BotBehaviorProfile` play styles. All three
share one `Random`, so the order of their calls within a tick is part of the behaviour.

A bot is asked for its input before the step, like a player: `SlotStepper.Advance` (the one stepper every host and
replay uses) asks every slot, bot or player, from the world as the last step left it, and hands `World.Advance` the
answers. `TankList.Advance` moves the tanks slot by slot, so a bot asked from inside the step would see where the
tanks in the slots before it had just moved, and the later slots won more for it (`SlotStepperTests`).

The bot drives its tank through the same `ControllerOutput` as a player, and asks Core what a player sees:

- **What is in the way.** `World.ProbeTankStep` gives the answer the tank's own move gets (`None`, `Dirt`, `Hard`,
  `Solid` or `Entity`), and `CollisionSolver.SurveyTankTerrain` how much of the hull would stand in the open. It
  never reads single cells, because the hull is 7 cells wide.
- **Scoring a move.** Open ground beats dirt in proportion to the open share of the hull; a step toward the target
  earns the profile's `TargetPull` times the cosine to it, the same at any range; hot ground ahead
  (`World.GroundHeatAt`) costs a point a degree; rock, walls and other bodies are not tried. Roaming near home it
  does not head back toward its base.
- **The torch.** Dirt is dug the plain way (half speed, no heat) unless the profile hurries and the heat budget is
  above three quarters. Rock is torched only by committed moves (leaving the base, heading home, a breakout) and
  only above half the budget, because torched rock leaves ground that cooks the tank.
- **Stuck and going nowhere.** Standing still counts as stuck after the patience the obstacle warrants (rock being
  torched takes a while). Stuck, or four seconds without coming nearer its target, it commits to a short manoeuvre
  (a sidestep, or a held heading toward the target) instead of choosing a new move every tick. A breakout (a held
  heading) ends when the enemy comes into sight, and a heading is only chosen when one is open: walled in on every
  side, `ChooseBreakoutHeading` says so (no movement) and the bot goes on with its plan. A roaming plan made in the
  enemy's sight is also dropped when sight is lost, and one made out of sight when it is found, so the turret is
  never left aimed for the other case.
- **The way home.** `BotTrail` keeps the way it came as waypoints (loops dropped); heading home it retraces them,
  so it needs no torch when it is hot. A return that gets no nearer the base for 20 s (a sealed door, a dead-end
  trail) gives up and roams again, and for the next 30 s low health or heat does not send it home (a kill, or
  standing in its own base, still does); a slow tank on a long way home keeps getting nearer and never gives up.
- **Going nowhere at all.** Whatever it is after, a bot that stays within `Tweaks.Bot.StuckRadiusCells` of one spot
  for `StuckWindowSeconds` while roaming out of sight or heading home (`BotStuckWatch`: one anchor and a count) drops
  its goal: a return is given up as above, and it heads for a point `DetourCells` away along an open heading for up
  to `DetourSeconds`. Fighting in sight, laying siege, waiting outside a base and anything in its own base are not
  watched.
- **Sieges and waiting.** The bot asks `World.CanBreakBase` (the one owner of the answer) whether its gun alone can
  break the enemy's base in one outing: at most `Tweaks.Bot.SiegeWindowSeconds` of steady fire, cut short by its
  off-network clock (`World.SiegeFireTicks`, which first runs it for the drive there, `World.SiegeDriveTicks`, so the
  answer at home is the answer on arrival), played forward a second at a time against the rules `BasePowerSystem`
  applies (buy-back limited by spare charge and reactor room, gates that shut again at `ShieldRestoreFill`, core
  repairs only while damaged); the remarks on `CanBreakBase` list what it leaves out, and which way each omission leans. The decision is held
  (`BotTankAI.HoldBreakDecision`): once it can, it keeps to that while fire `SiegeHoldFireFactor` times as strong
  would. Only then does it lay siege (`BotSiege`), once `Tweaks.Weapon.BotBaseSiegeGraceSeconds` are over; before
  that it roams. While the enemy sits docked in a base it cannot break it waits at a post `LoiterCellsOutsideWall`
  out from the wall and `BotLoiter.PostSideStep` to one side of the nearest gate, out of the gate's straight line of
  fire, turret on the base and fire held (its unstuck manoeuvres too; one begun while roaming ends once it chooses a
  post). A post a tank cannot stand at
  (`BotLoiter.CanStandAt`) is passed over; with none it does not wait, and one it gets no nearer for
  `LoiterApproachSeconds` it gives up on for a while. It stops waiting and fights while the enemy is in sight or for
  5 s after a hit (`BotTankAI.TrackHits`; a hit never shortens a longer give-up). `BotTankAI.PickEnemy` fights an enemy at the bot's own base before a
  nearer one.

- **Powerup drops.** Roaming, the bot goes for the nearest drop within `Tweaks.Powerups.BotSeekRange` that lies on the
  ground or lands within `BotSeekLeadSeconds` (`BotTankAI.Drops.cs`, `ChooseDrop`; it asks `World.PowerupForecast`
  and `World.CopyPowerupRenderStates`, what every player may know). It plans no path: the drop's site takes the
  enemy's place as the point it roams toward and comes before a siege and a wait at a post, and an enemy in sight is
  fought as always. Within `BotArriveCells` of a site whose drop has yet to land it stands and waits. A site it gets
  no nearer for `BotSeekGiveUpSeconds` (time fighting in sight does not count) it gives up for good. What it
  remembers is saved and mixed into its sync hash only once it has gone for a drop, so a bot in a match without
  drops plays and hashes as it always did.

- **Prospecting.** In a match with ore (`World.OreDeposits` not empty) and no clock (`MatchRules.TimeLimitSeconds` 0:
  a timed match is won on kills, so there it hunts from the start), while it goes for no powerup drop, for its first
  `Tweaks.Bot.ProspectSeconds` the roaming bot mines instead of hunting (`BotTankAI.Prospecting`), unless its enemy, out of its base, is within
  `EngageRangeCells`, in sight within `EngageSightCells`, at the bot's own base, or has hit it (then for
  `EngageHoldSeconds`). `BotProspector` picks the plain ore cell nearest the tank in the nearest pocket or speck within
  `ProspectRadiusCells` of its base (`TerrainGrid.TryFindPlainOre`, which skips tiles without ore); the bot roams there
  by the usual scoring and, within `MineReachCells`, drives straight at the cell, which the tank's own dig takes into
  the hold. A tick reads one cell while the target is still ore; the deposit list is searched only when a deposit runs
  dry, and at most every `ProspectSearchSeconds` when none was found. A hot hold sends it home to bank as before; with
  ore aboard and none left in its area it goes home too, and with none it roams to random places in its area. Digging
  in place is not watched by `BotStuckWatch`; its own stall count torches rock in the way and then gives the deposit up.

It still has no route planner (moves are scored one step at a time), sees the whole map and always knows where the
enemy is.

## Recipes

Each recipe lists every place that has to change today. Run `dotnet test Emberward.slnx` after; the table tests
named below fail when a step is missed.

### Adding a machine

Core:

1. A value in `MachineType` (`Entities/Machines/Machine.cs`).
2. A `MachineBehavior` in `MachineBehaviors` (`Entities/Machines/MachineBehavior.cs`), added to the
   `BuildTable(...)` call that fills `MachineBehaviors.All`: its type, action interval, action, colour, ore cost
   and `BuildKey`, its `Capacitor` (capacity, transfer rate, `PowerTier`, idle draw; DESIGN.md §4.9), plus the traits
   Core reads instead of the type: `RunsOnCharge` (its action pays in charge and stops when it cannot), `IsLinkNode`,
   `RequiresLinkToPlant`, `TakesOre` (a tank touching it unloads there), `IsMiner`, `IsHeatPump`, `IsGenerator`, `IsTurret`, `HuntsCreatures`, and `NeedsEnergyNetwork` (only
   energy matches can build it), and its `UpgradePaths`. The action is a static method taking `(Machine,
   MachineContext)`, in `MachineBehaviors` or, when it is long, a class of its own (`TurretAction`).
3. Constants (interval, capacitor) in `Tweaks.Machine`, its ore cost in `Tweaks.Ore`, colour in `Tweaks.Colors`.
4. Each `BuildKey` builds exactly one machine. Add a value in `BuildKey`, a field in `ControllerOutput`, edge
   detection in `TankBuildController.Advance` and a key bit in `ControllerOutputCodec` and its key mask. The five
   keys there are take every bit of the encoded input: a sixth needs a wider encoding, and a new protocol version.
5. `NetworkStatus.Collect` counts harvesters and relays by type; extend it if the HUD should report the new one.
6. Update `MachineBehaviorTableTests` (it names each machine's key and traits).

Desktop:

7. A `MachinePresentation` in `MachinePresentations` (`Presentation/MachinePresentation.cs`, shared with the
   browser): names, key label (the desktop's keyboard reads the SDL key of that name), build-panel slot and plant
   hint. Its `MachineSprite` in `MachineSprites` (`Rendering/Entities/MachineSprite.cs`,
   shared with the browser): shader shape kind, body size, whether it turns to its heading or stands upright with a
   gauge, its sense ring, capacitor gauge and preview reach. `MachineQuads` and `PreviewQuads` draw from it; neither
   switches on the type. Its build sound in `StepEventSounds.BuildSound` (`Audio/StepEventSounds.Machines.cs`, also shared).
   `MachinePresentationTests` checks keys and slots are distinct and fit the panel, and that every sprite is complete.
8. For a new `BuildKey`: `KeyboardController`, `GameSimulationStepper` (merges keyboard and scripted input) and
   `Game.ScriptedInput.cs` (scripted `Build` numbers). Browser: `BrowserApi`, `BrowserNetMatch`, `input.js` and the
   controls help.
9. A new shape (one no sprite uses yet) needs a `Kind*` constant in `EntityShape`, the matching `kKind*` in
   `Shaders/Entity/Layout.hlsli`, the shape function (e.g. `Shaders/Entity/Machines.hlsli`) and a branch in
   `EntityPs.hlsl`; `EntityLayoutGuardTests` checks the three agree.
10. `Gui/HintSelector.cs` names the machines in its hints; add one if the player needs telling.

### Adding a projectile

1. A value in `ProjectileType` (`Entities/Projectiles/Projectile.cs`) and a factory method on `Projectile`.
2. An entry in `ProjectileList.BuildBehaviors`: an advance method `(Projectile, CollisionSolver, ProjectileList)`
   and a draw colour. Use `CollisionSolver.TestPoint` to hit tanks, machines or terrain (bullets skip their own side's tanks and machines), `ProjectileList`'s RNG for chance,
   and the heat API (`AddHeat`, `AddHeatRadius`) for heat, so the ledger stays balanced.
3. Something has to spawn it: today `TankTurret` and `TurretAction` (bullets, and a gatling's rounds) and
   `ExplosionFactory` (shrapnel) do. A shot is tested from its first step on, never where it starts: anything fired
   from a barrel asks `ProjectileList.MuzzleOf` where to start, so that a barrel lying across a keep's wall or a shut
   gate does not put the shot past it.
4. Desktop: `Rendering/Entities/ProjectileQuads.cs` draws bullets and shrapnel as hot shots and throws for any
   other type; add a case for a new projectile. Record what presentation should hear about in `World.StepEvents` (bullets record `BulletImpact`, shrapnel `ShrapnelSpent`).

### Adding a thermal material

1. A value in `ThermalMaterial` (`Terrain/TerrainPixel.cs`), contiguous from zero.
2. Map pixels to it in `Pixel.DeriveThermalMaterial`. A new `TerrainPixel` also needs an entry in
   `Pixel.BuildBehaviorTable` and its name in the reviewed list of `SimulationTableGuardTests`.
3. Its row in `Tweaks.World.ThermalMaterials`, a `ThermalMaterialTable` with one `ThermalMaterialProperties` per
   material (the constructor throws on a missing or doubled row):
   - `HeatCapacity`: energy per degree.
   - `Conductivity`: the conductance of half a cell. Two touching cells conduct their halves in series,
     `ka * kb / (ka + kb)`, so a material against itself conducts half its value and against a much better
     conductor (a base core) nearly all of it. Air's value is the air field's, between neighbouring cells.
     A pair that needs a value the rule cannot produce gets a `ThermalPairOverride` in the table's override list
     (either order). The engine copies the pair table once
     at construction, so the solver's inner loop pays one array read.
   - `AirExchange`: conductance between the cell's terrain and the air in the same cell.
   - `Loss`: the share of the cell's temperature difference with ambient it exchanges with the outside each tick.
     All standard materials have a loss, so their heat settles. `FixedTemperature` is available for custom settings.
   - For initial heat or cold, use `SeedHeatedPixel`, as hot rock, frost rock and pyrite do. It sets the starting
     temperature, books the energy as injected and wakes the tile. From then on the cell exchanges heat normally.
   The table reaches thermal code through `SimulationSettings.ThermalMaterials` (`SourceGuardTests`), so a test can
   swap a row with `ThermalMaterialTable.With`.
4. Desktop shades pixels, not materials: a new pixel needs a case in `TerrainAuxBuilder` and
   `TerrainMaterialCoverage`.

### The emberites

Which nests a side has discovered is output, like its vision and radar: `SideSensing` keeps a `NestDiscovery` for each
side whose vision it tracks, which marks a nest found once that side's vision has explored any of its ground, `Nest.Radius` around its centre (a
radar echo does not discover a nest; the centre alone would not do, as it lies inside a lump deeper than sight reaches). It keeps what the side last learned of each
nest (`NestDangerLevel`, or cleared), refreshed only while some of that ground is in sight, so
the fog hides what a nest does meanwhile. It looks at each nest's ground on a grid of every second cell
(`VisionMap.IsAnyVisibleWithin`, and `IsAnyExploredWithin` until it is found), and the cost follows what changes: the
vision map stamps each tile when a cell in it comes into or goes out of sight, and an area whose tiles kept their
stamps answers from memory; one in sight looks first at the cell it was last seen by. So a nest out of changing sight
costs a look at a few tile stamps, and the renderer and the wake alert ask the same remembered answers
(`NestDangerTests` bounds the cell reads). `World.CopyNestDangers(tank)`
lists the found nests not last seen cleared, with that level and the ring radius `EmberiteSwarm.DangerRadius(nest.Radius)`, a small ring around the nest;
a side whose vision is not tracked (no fog) knows every nest as it is.

`EmberiteSwarm` (`Entities/Emberites`) exists only when `MatchRules.Emberites` is on and is stepped as `"emberites"` after `"tanks"`.
`Initialize` seeds a nest in each pocket and lump of `World.OreDeposits` that is far enough from every base. A nest is
counters (creatures never released, resting, out, stored charge); only creatures out of the nest are in the swarm's
`List<Emberite>`, so a world with nothing out does no per-creature work (`EmberiteSwarm.CreatureSteps` stays 0). A dormant nest
looks at each tank once in six ticks; digging a nest's ore (read from `OreCollected` step events) wakes it at once.
A creature out is a small state machine (`EmberiteState`): `Hunting` with a charge, a bite, then `Seeking` the hot cell
`TryFindHotspot` found near where it bit (one fixed scan per bite, `Emberite.Hotspot`, straight steering, given up after
`HotspotSeekTicks`; after `MaxFailedSeeks` failures since it last filled up, `Emberite.FailedSeeks`, it looks no more and goes
home, so every trip ends), `Recharging` there for `HotspotRechargeTicks` (`Emberite.Timer`) before it takes a full charge with
`ExtractEnergyInRadiusArea` and hunts again, and `Returning` home when it has lost its tank or found no hot ground
(`EmberiteSwarm.Hotspots.cs`). A tank docked in its own base is no target (`ValidTarget`, `PickTarget`) and wakes no
nest; a creature returning with a full charge takes up a tank it sees within `HuntLeashCells` again (`TryResumeHunt`,
once in `SightCheckTicks`). A creature going home short of a full charge looks for hot ground on its way
(`TryFindHotspotOnWayHome`: a fixed scan of `HomeScanCells` around it, at once and then once in `HomeScanTicks`, within
`HomeDetourCells` of detour and away from the `Emberite.Hotspot` it last drained) and seeks it as above. A tank on ground at `HotZoneHeat`, within `HotZoneKeepCells` of the creature and not docked in its own base, is kept
whatever its heat (one heat read per hunting creature).
A woken nest lets its creatures out in pulses (`Nest.Phase`, `AdvancePulse`): a stir of `StirTicks` (`StepEventKind.NestStirred`,
the warning), a group sized by the hunted tank's heat (`EmberiteSwarm.PulseSize`), then a lull. A resting creature leaves
only with a full charge, and while the nest cannot fill one for `StarveTicks` its hungry creatures die one by one
(`StepEventKind.EmberiteStarved`), so a nest with no heat clears itself and stops asking the grid for any.
Creatures move by the velocity they committed, then choose the next from an `EmberiteFlowField` (a Dijkstra over 4-cell
blocks, rebuilt every 8 ticks while read, saved in the snapshot because it is built from past terrain). Heat is
conserved: a first release books the charge as injected, a bite moves it into `Tank.Heat`, a kill spills it with
`AddEnergyInRadiusArea`, recharging takes it with `ExtractEnergyInRadiusArea`, and `World.SumSystemThermalEnergy`
adds `EmberiteSwarm.TotalCharge`. A bite also does `Tweaks.Emberites.BiteDamage` (`DamageCause.Emberite`) and moves a unit of
pyrite from the tank's hold to the creature (`Emberite.Pyrite`); it reaches `Nest.Pyrite` when the creature gets home, is
dropped with `OreDrop.Scatter` when it is killed, and a cleared nest frees its stored pyrite the same way, so the total of
holds, stores, ground, nests and carriers never changes (`EmberiteBiteTests`). Every `Tweaks.Emberites.DropEveryKills`-th
death of a nest (read off `Nest.Alive`, so no counter is kept) also moves one plain ore cell from the rim of the nest's
ground to where the creature died (`DropHomeOre`: one look at the nest's ground per drop, two `SetPixel` calls, nothing
if either end has no cell for it; `EmberiteDropTests`). Shots find creatures through a per-step sector index (`IndexCreatures`, called by
`CollisionSolver.Update`) and `CollisionSolver.TestPoint(onEmberite:)`. `World.IsOreGuarded` makes harvesters pass over the ore
of a nest that has creatures left (`MachineContext.IsOreGuarded`, `HarvesterState.Guarded`). The bot ignores creatures.
Gatlings find creatures with `TryFindTarget`, a walk over the creatures out that casts a line only to those in range
and nearer than the best so far; it is asked only while `OutCount` is above zero (`GatlingTests`).

### Powerup drops

`PowerupDrops` (`Entities/Powerups`, DESIGN.md §6.8) exists only when `MatchRules.Powerups` is on and is stepped as
`"powerups"` right after `"tanks"`, so a tank that reached a drop this step takes it this step. `World.Initialize` calls
`Begin` once the bases, pockets and nests are placed, which decides the first drop; from then on one upcoming drop is
always decided and public (`World.PowerupForecast`: site, kind, ticks until it lands). The first lands
`Tweaks.Powerups.FirstDropSeconds` in; the moment a drop lands the next is decided (`Schedule`) for
`DropPeriodSeconds` later, so every drop is known a full period ahead. A landed drop lies for `GroundSeconds`, which
is no longer than the period, so at most one lies at a time. Each step: running effects count down; the drop on the
ground is tested against each tank (`SettleGround`; `TankSpriteShape.ReachesDisc`, a disc `PickupDiameter` across): of
the living tanks touching it the one whose centre is nearest takes it, the lower slot on a tie, and one whose time is
up is gone (`PowerupExpired`); then the forecast drop lands when its time has come, and a tank already standing there
takes it in the same step. The old drop goes before the new one lands, so no step has two on the ground
(`PowerupDropsTests`). That is all a step costs (the drop on the ground times tanks, and one look at each tank's
timers); nothing scans the map.

The drops have a random stream of their own (`FastRandom`, seeded from a hash of the simulation seed), so a world
without them draws nothing more or less, and its hashes and `SimulationFingerprintTests` are as they were. Kinds come
from a shuffle bag (`NextKind`): every `PowerupKind` once a round, shuffled afresh each round, never the same kind
twice running.

A site (`PowerupDrops.Sites.cs`, `ChooseSite`) is chosen once per drop from a bounded number of draws, the same for any
map size and number of bases: `WideDraws` random cells over the bases' bounding box (widened by half the spacing of the
two nearest bases), then `RefineDraws` near the best so far. A draw is graded by where it lies (`Grade`: no nearer a
base than `MinBaseDistance` or `MinBaseDistancePercent` of that spacing, no nearer the site before it than
`MinDropSpacing`) and costed by its unfairness in whole cells, and its ground is looked at only if it would be the best
so far (`IsClearGround`: the drop's disc all soil or open ground, clear of the hot core, the pockets, uncleared nests
and standing machines, the ground neither hot nor frozen). Two fairness goals alternate. An "everyone's" drop costs
the farthest base's distance less the nearest's. A "contested" drop has one base, each in its turn (`TakeTurn`), as
its nearest, and costs how much nearer that base is than the next. The turns go in rounds: every base that counts
once, in an order shuffled afresh each round from the drops' own stream (`ShuffleTurns`), and a round never opens with
the base that closed the one before; so which base is first, and which is one turn ahead when the match ends, follows
from the seed and not from the slot. Destroyed bases do not count. A draw beside the
site before the last costs half the drop spacing more, so sites move about. With no usable draw a fixed lattice round
the middle of the bases is tried (`FallbackSite`); a drop is always scheduled. `PowerupSiteTests` bounds the
unfairness over seeds and checks every site's ground.

The system keeps each tank's running effects itself, by the tank's colour (`TankEffects`: ticks left of each timed
kind), so `Tank` has no powerup fields; a tank holds the system (`Tank.Powerups`, as it holds the shield system) and
asks it. `Tank.Advance` asks `EffectsOf` once: `Drill` lets `TankDigger.Move` step into what its plain dig cleared in
the same tick (soil at open-ground speed; rock is as it was), and `ShellDamageFactor` multiplies the damage of the
shell `TankTurret.TryShoot` fires (Overcharge). `TankLifecycle.TakeDamage` asks `ReduceDamage` (Bulwark) after the
spawn-protection check and before the shield, so shield and hull both lose half as much; the division's remainder is
carried to the next hit, so heat's point at a time is halved too. A recall and a fallen base still kill. An Overhaul
acts at once in `Give`: the hull whole, the tank shield full (`TankShieldSystem.Clear`), and the tank's heat above the
ground's rest temperature taken out of the system (`Tank.CoolFromPickup`, booked in `ThermalLedger` as sunk by the
tank, so the balance holds) with its off-network floor back at zero. Taking a kind again restarts its time; a tank's death ends all its effects at
once (`TankLifecycle.Die` calls `EndEffects`, each with its `PowerupEnded`, and the bulwark's carry is dropped), so a
recall, which dies and respawns in the same step, ends them too. The schedule, the drops, the effects and the stream are saved in the snapshot (plain fields and
lists), `MixState` mixes what shows into `WorldStateHash.Compute`, and `MixLatent` the stream, the bag and the turn
into the sync digest's latent part.

Hosts read `World.PowerupForecast`, `World.CopyPowerupRenderStates` (`PowerupRenderState`: cell, kind, ticks until it
is gone, radius), `World.PowerupDropCount`, `World.PowerupsOf(tank)` (`TankPowerupView`) and the kind table
`PowerupKinds`; none is behind the fog, because every side may know where and when a drop lands.

### Adding a collidable entity

`CollisionSolver.TestPoint` is the shared point query; only bullets use it today. A tank's step is stopped by the
ground under its hull (`CollisionSolver.TestTankTerrain`) and by `CollisionSolver.BlocksTank`: any other live tank,
and planted machines of another side (found with `MachineList.CollectNear`, the machines near the hull, not
the whole list). A tank's own machines never block it (its own bullets pass them too), and
something it already overlaps where it stands holds it only for a step that brings it nearer the other body's
centre, so two overlapping bodies can always part and never pass through one another.
`TankDigger.Move` asks both through `SimulationContext`; `World.ProbeTankStep` gives the same answer for a step not
yet taken. Machine placement checks `World.IsMachineSpotOccupied`, and roaming harvesters ask
`MachineContext.IsOccupied`. To make bullets (or a new projectile) hit a new entity:

1. A list for it reachable from `World`, and a parameter for it in `CollisionSolver.Update` (called first thing in
   `World.Advance`); add a sector index like the tanks' if there will be many.
2. An optional callback parameter on `TestPoint` for the new entity, checked in the same way as `onMachine`.
3. Pass the callback where hits should land, e.g. `AdvanceBullet` in `ProjectileList`.
4. If it should stop tanks, a loop for it in `CollisionSolver.BlocksTank` (a near-place query, not a walk over every
   one), with the same "already overlapping" exception (`Parts`: a step that does not bring the tank nearer is free).
