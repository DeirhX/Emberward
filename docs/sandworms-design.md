# Sandworms: design

Status: **proposed, nothing built.** This document is the plan; the numbers are starting values to tune in play.
It follows the emberites (`Entities/Emberites`, DESIGN.md §6.2), the closest precedent, and says where it departs
from them. Times assume 24 ticks a second (`Tweaks.Perf.TargetFps`); distances are terrain cells (a tank is 7 by 7,
a tank's tunnel 7 wide).

## 1. What the worms are for

The worms exist to **end reckless digging and fighting in one place**. Today a tank can torch a lump for a minute,
or stand in one tunnel mouth and shell everything that comes, and the only thing that pushes back is its own heat.
The worms make *staying loud in one place* the thing that is punished:

- Every loud act (digging, torching, shooting, shells bursting, machines working) leaves **unrest** in the ground
  where it happened. Unrest only rises while a place is noisy and only falls once it has been quiet.
- The more unrest a place holds, the **farther away it is heard**. Worms sleep in lairs spread over the map; each
  one whose lair is inside a place's reach wakes and comes. So a short noise calls nothing, ten seconds of
  torching in one place (the loudest thing a tank does, §2.1) calls the nearest worm, and a long siege calls several, which arrive one after another as their travel
  time allows. Time spent is what lets them catch up.
- The answers are the behaviours we want: **move on** (a new place starts from nothing and the old one becomes a
  decoy while it fades), **go quiet** (a worm strikes only what it feels moving, digging or shooting), **retreat
  down a tunnel you already dug** (a worm is slower than a driving or torching tank and faster than one digging
  without the torch), or **make
  noise somewhere else** (a shell burst is louder than the shot that made it).

Emberites punish heat; worms punish noise and dwell. The two meet in the ground: a worm's burrowing warms it, it
has to surface to vent that heat, and what it vents is hot ground an emberite drinks from (§5.4).

What a worm is: one armoured burrower about five tank lengths long, as wide as a tunnel, killable by one tank that
fights it well. It does not swallow a tank. It bites, holds and chews, and a tank that does nothing about it loses
most of its armour to one hold.

## 2. Noise and unrest

### 2.1 Where noise comes from

Noise is read from `World.StepEvents`, as `EmberiteSwarm.WatchForThieves` already reads `OreCollected`: the worm
system walks the step's events once and adds each loud one to the block it happened in. No machine or tank is
polled, so a step with no loud event costs the walk over a short list and nothing else.

| Event (`StepEventKind`) | Unrest added | In continuous use, about |
| --- | --- | --- |
| `TankMoved` | 1 | 24 a second, spread along the drive (32 per block crossed) |
| `TankDug`, no torch | `Count / 2`, rounded up | 40 a second |
| `TankDug`, torch | `Count` | 170 to 300 a second: the torch clears the face (7 cells going straight, up to 13 on a diagonal) and the tank moves on in the same tick |
| `ShotFired` | 3 | 24 a second at the tank |
| `Explosion` (a shell burst, a death, a wreck) | 12 | 96 a second where the shells land |
| `TurretFired` (turret or gatling) | 2 | 16 a second for a gatling |
| `OreHarvested` | 6 | a few a second per harvester |
| `MachinePlanted` | 20 | once |

An idling engine, a turning turret, relays, generators and links are silent: they record no event, and a worm
system that polled them would cost by machine count. Events inside a base (`Pixel.IsAnyBaseCell` at the event's
cell) are ignored, so a docked tank is silent. Emberite events are not noise.

**How an event is counted.** Each event counts once, in the block that holds its `Position`; nothing spreads to
other blocks. A tank passing through leaves only what it recorded inside the block: about 32 driving across it, 160
digging, 260 torching (more on a diagonal). That is heard from 10 to 16 cells at most, so travel calls nothing
unless it passes over a lair. A worm's own movement is not noise, so a worm passing through or under a block adds
nothing.

Machine noise (`TurretFired`, `OreHarvested`) stops adding once its block holds `MachineUnrestCap` (1600, heard
from 100 cells). Lairs stand at least 150 cells from every base, so the machinery around a base never calls a worm
by itself; a tank fighting or torching there can. On a small map the lair distance shrinks by
`OreDeposits.DistanceScale` (down to 0.4: 60 cells), inside the 100 cells a capped block is heard from, so the cap
shrinks by the same scale (640 at 0.4, heard from 40 cells) and keeps the 100-to-150 margin on every map.

A machine event refreshes its block's `LastNoiseTick` only while the block is below `MachineUnrestCap`. Otherwise a
working gatling would hold a block a tank had torched up to the cap there for ever; with the rule, such a block
decays to the machine cap and the machines hold it there.

The per-second column depends on how often the tank code records each event (`TankDigger` records one `TankDug`
per dig step, with the cells cleared as `Count`). `NoiseMapTests` pins the rates, so that a change to the tank
does not silently retune the worms.

### 2.2 The noise map

`NoiseMap` cuts the map into blocks of `BlockCells` = 32 (a 1500 by 750 map is 47 by 24 = 1128 blocks). Per block
it holds three numbers in flat arrays: `Unrest` (int), `LastNoiseTick` (int, an absolute tick, so it is saved in the
snapshot and mixed in `MixLatent`) and `LastAt` (the cell of the last event, which is where a worm heads). A `List<int>` holds the indices of the blocks whose unrest is not zero, in the
order they became noisy.

Unrest is settled lazily, in integers, when a block is written or read at tick `t`:

```
quiet  = t - LastNoiseTick
unrest = quiet <= QuietGraceTicks ? Unrest : max(0, Unrest - (quiet - QuietGraceTicks) * DecayPerTick)
```

So unrest **does not fall while the place stays noisy** (any event within the last 3 seconds holds it), and falls
at 72 a second once it is quiet. It is capped at `UnrestCap` = 6400. Nothing sweeps the map: an event settles and
writes one block, a hearing check walks the short list of noisy blocks, and a block that settles to zero leaves
the list at the next check.

### 2.3 How far a place is heard

A block is heard from `Unrest / UnrestPerCell` cells (16 unrest per cell), and not at all below `WakeUnrest` =
160. That gives the ramp:

| What the tank does in one place | Heard from 150 cells after | 300 cells after | Cap (400 cells) after |
| --- | --- | --- | --- |
| Torch-mines without a pause | 8 to 14 s | 16 to 28 s | 21 to 38 s |
| Digs or widens without a pause | 60 s | 120 s | 160 s |
| Fires without a pause at something close (shots and bursts in one block) | 20 s | 40 s | 53 s |
| Fires without a pause at something far (bursts land elsewhere) | 100 s | 200 s | 267 s |

and the decay: after 3 quiet seconds the reach shrinks 4.5 cells a second, so 10 seconds of torching (1700 to 3000
unrest) is forgotten 27 to 45 seconds after it stops, and a place at the cap in 92.

Driving or tunnelling straight through is heard from 16 cells at most (§2.1, how an event is counted): travel
calls nothing. With about one lair per 100 000 cells (eleven on the standard map) the nearest lair is typically
100 to 200 cells away, so the first worm sets out after 8 to 14 seconds of torching, or about a minute of plain digging, and needs 5 to 15
seconds to arrive; a second and a third follow as the reach passes their lairs. That is the convergence asked
for, with no timer of its own: it falls out of reach growing with dwell and worms travelling at a fixed speed.

### 2.4 What a worm hears and feels

Two senses, both cheap:

- **The call** (far). Every `HearTicks` = 12 (staggered by index) a worm, asleep or awake, walks the noisy-block
  list and takes the block with the largest `Unrest - 16 * distance` above zero, the lower block index on a tie.
  That block's `LastAt` is where it goes. A worm that has prowled a block and found nothing ignores that block for
  `IgnoreTicks` (30 s).
- **The tremor** (near). A worm feels a tank within `FeelRange` = 40 cells of its head if that tank itself moved,
  dug or fired in the last `FeelMemoryTicks` = 24, and is not docked or spawn-protected. The noise pass stamps a
  last-noise tick per tank from `TankMoved`, `TankDug` and `ShotFired` only, whose `Color` is the tank. The other
  loud events add unrest but stamp no tank: `TurretFired`, `OreHarvested` and `MachinePlanted` carry the owner's
  colour, not a tank's act, and a burst (`Explosion`) is noise where it lands. So a side's working gatling or
  harvester calls worms to its own block and never gives away a tank that is holding still. A felt tank overrides
  the call. A tank that is still and silent is not felt, even beside the worm, unless the worm's head blunders
  into it (within `BiteRange`).

A worm lunges only at a tank it feels. So the last-second answer to a worm is to stop: no move, no dig, no shot.
Machines of the tank's side may keep working; they draw the worm to themselves, not to the tank.

### 2.5 Counters, in one place

- **Move on.** Unrest belongs to the block, not the tank. Driving two blocks away starts from zero, and the place
  left behind keeps calling worms to an empty tunnel while it fades.
- **Go quiet.** Three seconds of silence start the decay, and a silent tank is not felt. Going quiet where the
  noise was made is the risky version, because that is exactly where the worm arrives and prowls; move off first.
- **Retreat.** A worm burrows at 0.8 cells a tick; a tank drives at 1, torches through soil at 1 and digs without
  the torch at about 0.5. A tunnel already dug is an escape route, and so is the torch: it outruns a worm in soil,
  though it keeps the tank felt while the worm is within 40 cells, and it heats the tank. Digging without the torch
  with a worm behind is not. Past 40 cells the tank is no longer felt.
- **Decoy.** A shell burst adds 12 where it lands against 3 at the gun. Shelling a far wall builds unrest there
  four times as fast as at the tank, and a worm goes to the louder call, but only a worm more than `FeelRange` (40
  cells) away: nearer, the gun's own `ShotFired` makes the tank felt, and a felt tank overrides the call. The old work site is a decoy by itself.
  Later, the thumper (§10, phase 6) is a decoy that can be planted.
- **Kill it.** §6. A dead worm's lair is silent for four minutes.

## 3. Lairs and lifecycle

`SandwormPack` (`Entities/Sandworms`, `MatchRules.Sandworms`) keeps the lairs, the worms that are out and the
noise map. As a nest is counters, a lair is a position and a few numbers; a sleeping worm has no body and takes no
step.

- **Placing.** `World.Initialize` places lairs last, after ore, from an RNG of their own seeded from the
  decoration seed, so that a world without the rule generates exactly as today. One lair per
  `CellsPerLair` = 100 000 cells of map (at least 2), each in plain soil at least `MinLairDistanceFromBase` = 150
  cells from every base and `MinLairSpacing` = 120 from another lair, by rejection sampling with a fixed number of
  tries. The distances shrink on small maps by `OreDeposits.DistanceScale`, as the ore rules do.
- **Waking.** A lair whose hearing check finds a call wakes: `StepEventKind.WormWoke`, and its worm appears at the
  lair with all segments on one cell and uncoils as it leaves. Nothing wakes in the first `GraceSeconds` = 240 of a
  match (the opening and the tutorial), as long as the bot's siege waits (`Tweaks.Weapon.BotBaseSiegeGraceSeconds`).
- **Out.** Travelling to a call, hunting a felt tank, prowling a place that went quiet (`ProwlTicks` = 6 s on a
  circle of 12 cells around `LastAt`), venting (§5.4) or going home.
- **Home.** With no call and no felt tank for `LoseInterestTicks` = 8 s it burrows back to its lair, puts the heat
  it holds into the lair's ground and becomes counters again. A worm stuck against rock for 3 seconds gives up its
  goal the same way, so every trip ends.
- **Death and return.** A killed worm's lair is spent for `RebirthTicks` = 4 minutes, then holds the next
  generation: health times 1.25 per generation, up to generation 3. That is the difficulty ramp inside a match;
  across maps it is lair density. At most `MaxOut` = 6 worms are out at once.

No lair is discovered or drawn in the first version: unlike a nest it guards nothing, and the tremor gauge (§8.3)
is the warning. Radar shows a worm that is out (§8.1).

## 4. Anatomy and movement

- **Body.** A head and `BodySegments` = 9 segments `SegmentSpacing` = 4 cells apart: 36 cells nose to tail. The
  head is a disc of radius 3, a body segment 2.5, so the worm fills a tank's tunnel. Positions are a fixed
  `PositionF[10]` per worm.
- **Chain.** The head moves by the velocity it committed last step and then chooses the next, as an emberite
  does, so the renderer's `position + motion * alpha` is exact. Each segment then moves toward the one ahead until
  it is `SegmentSpacing` from it (one `MathF.Sqrt` per segment and no transcendentals, so nothing here needs
  `DeterministicMath`). A segment's motion for the renderer is what it moved this step.
- **Steering.** The head turns its velocity toward the goal by at most `TurnPerTick` (a rotation by a fixed
  cosine and sine constant, as `EmberiteSwarm.Diagonal` avoids trigonometry), which gives the long curves. Three
  feelers 8 cells out (ahead and 45 degrees to each side) read one pixel each; a blocked one turns the head to the
  free side nearer the goal. No flow field: a worm crosses soil and open ground alike, so its only obstacles are
  rock, hot and frost rock, concrete, ore, base cells and the map edge, which a tank can put between itself and a
  worm. A worm caught in a pocket of rock gives up after `StuckTicks` (§3).
- **Speed.** `SoilSpeed` 0.8 cells a tick, `OpenSpeed` 0.55, `LungeSpeed` 2.5 for at most 8 ticks. It is a
  burrower: slower and exposed in the open, where a tank outruns it, and faster than a tank digs without the torch;
  a torching tank (1 cell a tick) outruns it in soil too. The steering
  keeps it in soil beside a tunnel when it has the choice, which is why it comes out of the wall.
- **What it does to the ground.** Burrowing writes no pixels, as with the emberites: the worm passes through soil
  and the soil closes behind it. Tunnels stay as the player dug them, so the map is not honeycombed over a match,
  a narrow tunnel stays narrow, and emberites gain no highways. (Marking the track with `DirtGrow` was considered
  and dropped: `DirtGrow` is thermally air and does not stop a tank, so it would change both heat and movement.)
  It writes in two places only, through `TerrainGrid.SetPixel`, so that regrowth, vision, links and the renderer
  hear of it:
  - *Breach.* Where the head passes from soil into open ground or back, soil within `BreachRadius` = 3 becomes
    `Blank`: the mouth of a burrow, recorded as `StepEventKind.WormBreached`. These are real holes, about 30
    cells each, and they regrow like any other.
  - *Vent chamber.* A worm that must vent in soil clears `VentRadius` = 4 the same way (§5.4).
  Rock, ore, concrete and base cells are never written.
- **Solid.** Segments stop shells (§6) and block tanks: a loop in `CollisionSolver.BlocksTank` over the segments
  filed near the hull, with the same already-overlapping exception as machines. A worm that surfaces behind a tank
  closes the tunnel it would have left by. Worms do not block each other, emberites or harvesters.

## 5. Attack and heat

### 5.1 Lunge

When a felt tank is within `LungeRange` = 14 cells of the head with no rock on the line, the worm stops, opens its
maw and winds up for `WindUpTicks` = 12 (half a second): `StepEventKind.WormLunging`, the tell. The lunge then goes
2.5 cells a tick for up to 8 ticks along the line fixed when the wind-up ended, through soil or open ground. In a
cavern a tank steps aside; in a tunnel it can only back away, and a tank that backs away from the first tick just
gets clear (20 cells against 20). A lunge that misses leaves the worm stretched out with its flank open for
`RecoverTicks` = 48 before it turns.

### 5.2 Bite, hold, chew

A lunge whose head comes within `BiteRange` = 4.5 cells of the tank's centre bites: `BiteDamage` = 250, and the
worm **holds** the tank. A held tank cannot move (the tank's step asks the pack, as it asks the reserve for its
move factor); it can aim, shoot and torch. Every `ChewTicks` = 12 the worm chews for `ChewDamage` = 90 and
passes `ChewEnergy` = 30 of heat from its own store into the tank (1.5 degrees for the tank, §5.3). The hold ends
when:

- the worm has taken `HoldBreakDamage` = 600 since it bit (three shells into the open maw, §6), or
- `HoldMaxTicks` = 120 have passed (5 seconds), or
- the tank dies, or the worm does.

Then it lets go, dives and leaves the tank alone for `RecoverTicks`.

All of it goes through `Tank.TakeDamage(amount, DamageCause.Sandworm)`: the shield takes it first (the cause is
not in the bypass list), the overflow reaches the hull, and each chew restarts the shield's refill delay. A full
hold is 250 + 10 x 90 = 1150 of a fresh tank's 1400 (400 shield, 1000 hull): it survives, burning, with a quarter
of its hull. A tank that answers in the first second loses about its shield. A tank that was already hurt dies.
That is "not swallowed, but serious". A docked or spawn-protected tank is neither felt nor bitten.

Worms attack tanks only in the first version, the bot's tank like the player's. Machines are not bitten (§11).

### 5.3 Energy

Damage is not heat and the ledger does not see it, as with an emberite's `BiteDamage`. The heat a chew passes is
an amount of **energy**, moved as an emberite's bite moves its charge: the worm gives up
`energy = min(ChewEnergy, Heat * HeatCapacity)` (nothing when it holds none above 0), its own `Heat` falls by
`energy / HeatCapacity` and the tank's rises by `energy / tank.ThermalCapacity`. The two capacities differ twenty
to one (400 against the tank's 20), so the same 30 of energy is 1.5 degrees for the tank and 0.075 for the worm;
degrees are never copied across. Ten chews of a full hold warm the tank 15 degrees and cool the worm 0.75. Nothing
is made or lost, so the ledger needs no entry.

### 5.4 The worm's own heat

A worm holds `Heat` (degrees) with a heat capacity of `HeatCapacity` = 400, twenty tanks' worth; its energy is
`Heat * HeatCapacity`, and every exchange below is in energy.

- **In.** Each cell of soil the head travels adds `BurrowEnergyPerCell` = 40, booked with
  `ThermalLedger.RecordInjected`, as a tank's dig heat is: 0.1 degrees a cell, so about 1.9 degrees a second
  burrowing flat out, and the 20 degrees from `RestHeat` to `VentHeat` in 200 cells of soil (300 from cold).
- **Out, slowly.** In open ground the head gives 0.5 degrees a second (200 of energy) to the air around it
  (`AddEnergyInRadiusArea`, air field), which wakes those tiles as any heat write does.
- **Out, all at once.** At `VentHeat` = 30 the worm must vent. It finishes a hold first, then stops at the next
  open ground it enters, or clears a chamber where it is after 2 seconds in soil, and for `VentTicks` = 72 lies
  still with its maw open while everything above `RestHeat` = 10 goes into the air and ground around its head in
  equal parts per tick. `StepEventKind.WormVenting`.
- **Death.** What it holds spills at the head (`AddEnergyInRadiusArea`, radius 4).
- **Asleep.** It holds none: going home puts it in the lair's ground, so every outing starts from 0.

`SandwormPack.TotalHeat()` joins `World.SumSystemThermalEnergy`, so `ThermalLedgerTests` closes every tick. Every
write goes through the heat field's own writers, so tiles wake and relays hear it.

This is the tie to the rest of the game, and the numbers are set so that a vent happens within one outing. A worm
leaves its lair at 0 and must vent after 300 cells of soil. The nearest lair is typically 100 to 200 cells away, so
the first worm arrives at 10 to 20 degrees and vents after another 100 to 200 cells: one prowl (six seconds is
about 115 cells), a chase down a tunnel, or a trip to a decoy and back. A worm called from 300 cells or more (the
second and third to come, §2.3) is due to vent as it arrives or on the way. After a vent it is at `RestHeat` and
the next is 200 cells off, so a worm kept moving vents about every ten seconds of burrowing. The burst window is
something the player makes, by making the worm travel. And a vent site is hot
ground, which is what spent emberites drink from (`TryFindHotspot`), so a worm fight near a nest can feed the nest:
only a site at `HotspotHeat` (20 degrees) or more, within `HotspotSearchCells` (24) of where an emberite bit a tank,
or within `HomeScanCells` (20) of a spent one on its way home, at a detour of at most `HomeDetourCells` (12).

## 6. Killing a worm

One health pool: `Health` = 4000 for the first generation, 5000 and 6250 after. Where a shot lands decides what it
does:

| Hit | Factor | A shell (160) does | Shells to kill 4000 |
| --- | --- | --- | --- |
| Head, maw shut | 0.25 | 40 | 100 |
| Body segment | 1 | 160 | 25 |
| Head, maw open (wind-up, hold, venting) | 1.5 | 240 | 17 |

A tank fires a shell every 3 ticks, so 25 shells is three seconds of fire that all lands. Gatling rounds and
turret shots do their own `Projectile.Damage` by the same factors. Shrapnel does nothing to a worm, as it does nothing
to a tank (`ProjectileList.AdvanceShrapnel` kills only emberites).

- **In the open** the body is in view: circle it and fire into the flank. A cavern is where a worm is killed.
- **In a tunnel** the tank sees the head, end on, and shells do a quarter damage. What is left is the half second
  of open maw before each lunge (four shells, 960), the hold (shoot the maw to break it) and the vent. A tunnel
  fight is several lunges long and costs armour each time. That is the serious case the brief asks for, and the
  reason to fight where there is room or not at all.
- **Burst windows**, in order of size: venting (3 s, maw open, still), a missed lunge (2 s, flank open, in a
  cavern), a crossing of open ground at 0.55 cells a tick, the wind-up (0.5 s).
- **Shots find it** as they find emberites: `SandwormPack.IndexSegments` files the segments of the worms out by
  sector at the start of the step (`CollisionSolver.Update`), `CollisionSolver.TestPoint(onSandworm:)` is asked
  only while one is out, and a shell that hits bursts as on a tank. The worm is tested **before the terrain**, as
  shrapnel tests emberites before the pixel: a shot entering a cell covered by any segment's disc hits the worm,
  whatever the pixel. So a head in or against a tunnel is always exposed, and a segment buried deeper than its
  radius stays behind its soil.
- **Death.** `StepEventKind.WormKilled`, the heat spills, the body is gone, the lair is spent for four minutes. The
  reward in the first version is that quiet: a region where loud work is safe for a while (§11).

## 7. Rules, state and the network

### 7.1 `MatchRules` and the step

`MatchRules.Sandworms` (bool, off by default, not in `Standard`, and not set by `WorldFactory` or `BrowserSession`
until phase 5; needs no other flag). Its `Fingerprint` mixes `0x5357` when on, so a world without it hashes as
today and two clients with different rules refuse to start. `World` builds a `SandwormPack` only with the flag;
`BuildSystems` adds `"sandworms"` **after `"machines"`**, so that the step's events from projectiles, tanks and
machines are all recorded before the pack reads them. Nothing that counts as noise is recorded later in the step;
a test pins that (§9).

This changes the contract of `StepEventLog`. Its summary says nothing in the simulation reads it back and its
`[NotSaved]` reason says "output only"; both are already untrue (`EmberiteSwarm.WatchForThieves` reads
`OreCollected`), and with worms the log is input to the step. The reason becomes "scratch: cleared before each step
and filled before the systems that read it (emberites, worms)", with its entry in `SnapshotCoverageTests` changed to
match. And every noise event must always be recorded: today each site records through `?.Record`, so a context
built without a log records nothing. With the rule on the log is always passed, and a test checks it.

State lives in the pack, not on `Tank`: which tank a worm holds and each tank's last-noise tick are arrays in the
pack, and the tank's step asks `SimulationContext.Sandworms?.Holds(tank)`. A hold made in one step stops the tank
in the next, the same on every client.

### 7.2 Determinism and replays

Integer unrest, a `FastRandom` seeded from the simulation seed mixed with the system's constant, list order for
every tie, no clock, no dictionary, no `Math` transcendental. Events are read in the order they were recorded.
Inputs are untouched: no new key in phases 1 to 5, so replays and the input encoding do not change. The thumper
(phase 6) needs a sixth build key, and bit 31 of the encoded input was the last free one: that phase bumps the
protocol and is kept apart for it.

### 7.3 Snapshot and sync hash

Saved: lairs (`List<Lair>`), worms out (`List<Sandworm>`, each with its `PositionF[]`), the noise map's three
arrays and its list of noisy blocks, the per-tank arrays, the RNG, the tick. `[NotSaved]` with a reason and an
entry in `SnapshotCoverageTests`: the segment sector index (scratch, rebuilt each step) and the world hook for
occupied cells (fixed). `WorldStateHash` gets `MixState` (worms: positions, health, heat, state; lairs) and
`MixLatent` (RNG, timers, goals, the noisy blocks). A late joiner needs nothing new, and normal play pays nothing
for it.

### 7.4 Performance budget

The rule is the swarm's: cost follows what is out and what is loud, never the map.

| When | Work per step |
| --- | --- |
| Rule off | none: no pack, no system |
| Rule on, map quiet, nothing out | the walk over the step's events; one count check |
| Somewhere loud, nothing in reach | one block write per loud event; every 12 ticks each lair walks the noisy-block list (tens of entries at most) |
| A worm out | 3 feeler reads, 10 segment moves, a distance test per tank; hearing every 12 ticks; pixel writes only at a breach or a vent chamber |

`SandwormPack.WormSteps` stays 0 while nothing is out and `NoiseMap.BlockWrites` stays 0 in a silent step; tests
check both, as `EmberiteSwarmTests` checks `CreatureSteps`. Target with six worms out: under 0.05 ms a step on a
slow machine, read from the `"sandworms"` profile label. The noise map is under 20 kB on the standard map and grows with
area only as memory, not as work.

### 7.5 `Tweaks.Sandworms` (`Config/Tweaks.Sandworms.cs`)

| Constant | Value | Constant | Value |
| --- | --- | --- | --- |
| `BlockCells` | 32 | `SoilSpeed` / `OpenSpeed` / `LungeSpeed` | 0.8 / 0.55 / 2.5 |
| `UnrestCap` | 6400 | `LungeRange` / `LungeTicks` | 14 / 8 |
| `WakeUnrest` | 160 | `WindUpTicks` | 12 |
| `UnrestPerCell` | 16 | `RecoverTicks` | 48 |
| `QuietGraceTicks` | 72 | `BiteRange` | 4.5 |
| `DecayPerTick` | 3 | `BiteDamage` / `ChewDamage` | 250 / 90 |
| `MachineUnrestCap` | 1600 | `ChewTicks` / `HoldMaxTicks` | 12 / 120 |
| `MoveNoise` / `ShotNoise` / `BurstNoise` | 1 / 3 / 12 | `HoldBreakDamage` | 600 |
| `TurretNoise` / `HarvestNoise` / `PlantNoise` | 2 / 6 / 20 | `ChewEnergy` | 30 |
| `DigCellsPerNoise` (torch: 1) | 2 | `Health` / `GenerationFactor` / `MaxGeneration` | 4000 / 1.25 / 3 |
| `HearTicks` | 12 | `HeadArmour` / `MawFactor` | 0.25 / 1.5 |
| `FeelRange` / `FeelMemoryTicks` | 40 / 24 | `HeadRadius` / `SegmentRadius` | 3 / 2.5 |
| `ProwlTicks` / `ProwlRadius` | 144 / 12 | `BodySegments` / `SegmentSpacing` | 9 / 4 |
| `IgnoreTicks` | 720 | `BreachRadius` / `VentRadius` | 3 / 4 |
| `LoseInterestTicks` / `StuckTicks` | 192 / 72 | `HeatCapacity` / `BurrowEnergyPerCell` | 400 / 40 |
| `CellsPerLair` | 100 000 | `VentHeat` / `RestHeat` / `VentTicks` | 30 / 10 / 72 |
| `MinLairDistanceFromBase` / `MinLairSpacing` | 150 / 120 | `OpenShedDegreesPerSecond` | 0.5 |
| `GraceSeconds` | 240 | `RebirthTicks` | 5760 |
| `MaxOut` | 6 | `FeelerCells` | 8 |

Thermal code reads the heat numbers through `SimulationSettings`, as the guard test requires.

## 8. What the player sees and hears

### 8.1 Fog and radar

Vision stays output only: worms never read it, and they run the same in and out of sight. A segment is drawn only
where the side's `VisionMap` sees its cell. Soil blocks sight, so a burrowing worm is hidden; what shows is the
breach and whatever is in open ground in view. On radar each worm out returns a life echo at its head and at
every third segment (`CollectRadarTargets`, as the swarm adds its creatures), so a pulse shows a short line of
blips: a worm, and which way it lies.

### 8.2 Drawing

- Core: `SandwormRenderState` per segment in `Rendering/RenderSnapshots.cs` (worm id, segment index, position,
  motion, flags: burrowing, maw open, venting, holding; heat and health as fractions) and
  `World.CopySandwormRenderStates`.
- Shared rendering: `Rendering/Entities/SandwormQuads.cs` (in both csproj lists), one quad per visible segment
  with its motion, so `EntityVs.hlsl` extrapolates it like everything else.
- Shader: `EntityShape.KindSandwormHead` = 27 and `KindSandwormSegment` = 28, the same constants in
  `Shaders/Entity/Layout.hlsli`, a new `Shaders/Entity/Sandworms.hlsli` and two branches in `EntityPs.hlsl`
  (`EntityLayoutGuardTests`). Ringed plates on the body, a three-part maw on the head that opens with the flag,
  and a glow from `HeatScale` as the worm nears `VentHeat`, which is how the player reads that a vent is coming.
  No early returns.
- Dust: the particles the tank's fire uses (`KindFireParticle`, `FireParticles`) with a dust kind added, thrown at
  a breach, along a lunge, at each chew, and as a slow fall from the tunnel roof while a worm is near and unseen.
- Browser: the emitters are shared; `ENTITY_ATTRIBUTE_FLOATS` does not change because no vertex field is added.

### 8.3 HUD and warnings

Core answers, Desktop draws: `World.TremorFor(tank)` returns the unrest of the tank's block as a fraction of the
cap, its reach in cells, a `StatusLevel`, whether the tank is held, and how near the nearest worm that is out is
(as a strength from 0 to 1 inside 80 cells, no direction). Output only.

- **Tremor gauge** beside the heat gauge: fills with the block's unrest. Calm below the wake threshold, *Stirring*
  above it, *Calling* once the reach passes 150 cells, with the reach as the label ("heard from 220"). It visibly
  holds while the tank is loud and drains when it is quiet, which teaches the rule without text.
- **Near.** Screen tremble and roof dust scaled by the proximity strength; a warning line, "WORM NEAR: hold still
  or run".
- **Held.** "HELD: shoot the maw", the gauge flashing.
- **Toasts** through `GameEventWatcher`, once each: the first *Stirring* ("The ground is restless. Move on or go
  quiet."), the first worm woken by this side's noise, the first kill. A tutorial card when the rule is on.
- **Death** reads `DamageCause.Sandworm`. Colours from `HudPalette`.

### 8.4 Sound

New `StepEventKind` values, each mapped in `StepEventSounds` (desktop and browser, `StepEventSoundsTests`):
`WormWoke` (a distant groan, heard anywhere, quiet), `WormBreached` (a thud and falling earth), `WormLunging` (the
tell: a rising hiss), `WormBit`, `WormChewed`, `WormReleased`, `WormHit` (`Count` 0 for plate, 1 body, 2 maw: a
clang or a wet hit, so the ear says whether the shot counted), `WormVenting`, `WormKilled`. The rumble of a near
worm is a loop whose gain is the proximity strength; `SoundDirector` needs a looped voice for it if it has none.

## 9. Tests

- `NoiseMapTests`: each event adds its amount to the right block; unrest holds while noisy and falls after the
  grace; the cap; machine noise stops at its cap; base cells are silent; the per-second rates of driving, digging,
  torching and firing; a silent step writes no block.
- `SandwormCallTests`: the ramp table of §2.3 (torch for 10 s: the lair at 100 cells wakes and the one at 250
  does not); a second lair wakes later; a quiet place stops calling; a moved tank leaves the call behind; a shell
  burst far away outcalls the gun; nothing wakes in the grace period; no system stepped after `"sandworms"`
  records an event that counts as noise.
- `SandwormMovementTests`: the chain keeps its spacing; soil and open speeds; rock and bases stop it; a stuck
  worm goes home; burrowing writes no pixel; a breach clears soil and leaves rock; a worm home is counters again.
- `SandwormAttackTests`: a silent tank is not lunged at, even while its own gatling and harvester work beside it; a wind-up fixes the line and a sidestep avoids it; bite
  and chew amounts through the shield; a held tank cannot move; the hold breaks on damage, on time and on death;
  a docked tank is safe.
- `SandwormCombatTests`: the three damage factors; shells stop on segments; a segment blocks a tank and two
  overlapping bodies can part; kill, spent lair, rebirth with the next generation's health.
- `SandwormHeatTests`: burrow heat is booked; a chew moves energy, not degrees (the sum of both stores is unchanged); a worm from cold vents after 300 cells of soil; the vent empties to `RestHeat`; a kill spills; `ThermalLedgerTests`
  with a worm out for a whole fight.
- `SandwormCostTests`: `WormSteps` and `BlockWrites` stay 0 on a quiet map.
- Existing suites that must grow: `DeterminismTests` and `ReplayTests` with the rule on; `SnapshotRoundTripTests`
  mid-hold and mid-vent; `SnapshotCoverageTests`; `StepEventTests` for each new kind; `StepEventSoundsTests`;
  `EntityLayoutGuardTests`; `RadarLifeTests`.
- **Fingerprints.** The two recorded runs in `SimulationFingerprintTests` have the rule off and their constants
  must not change in any phase: lairs use their own RNG, and recording events changes nothing. A third recorded
  run with the rule on is added in phase 3, and its constant changes, with a note in the commit message, whenever
  worm behaviour changes on purpose.

## 10. Implementation plan

Six pull requests. Each leaves the game as it is for players until phase 5 switches the rule on.

**1. Noise and unrest (no worms).** `Entities/Sandworms/NoiseMap.cs`, `SandwormPack.cs` (noise pass only),
`Config/Tweaks.Sandworms.cs`, `MatchRules.Sandworms`, `World.Sandworms.cs` (`TremorFor`), `World.cs` (build, the
system entry), `WorldStateHash.cs`, snapshot coverage, a debug overlay of unrest. Tests: `NoiseMapTests`, cost,
snapshot. Risk: the per-second rates are estimates until measured; this phase exists to measure and tune the ramp
with a gauge on screen before anything bites.

**2. Lairs, body, movement.** `Sandworm.cs`, `SandwormPack.Lairs.cs`, `.Movement.cs`, `.Hearing.cs`; lair
placement in `World.Initialize`; `SandwormRenderState`; a plain placeholder quad. Worms wake, come, prowl, go
home and breach; they do not attack. Risk: feeler steering against concave rock (the stuck timer is the backstop;
a coarse map-wide cost field, built only while a worm is out, is the fallback if worms are often lost).

**3. Combat and heat.** `SandwormPack.Combat.cs`, `.Attack.cs`, `.Heat.cs`; `CollisionSolver` (index, `TestPoint`
callback, `BlocksTank` loop); `ProjectileList` (hits); `DamageCause.Sandworm`; `SimulationContext` and
`TankDigger` (the hold); the new `StepEventKind` values; `World.SumSystemThermalEnergy`. Tests: attack, combat,
heat, the third fingerprint run. Risks: a shell fired by a held tank must meet the head on its first step (the
barrel tip is 4 cells out, the head centre 4.5 or less: place the holding head so that the tip is inside it, and
test it); a body across a tunnel must never trap a tank for good (the overlap exception, and a worm always
leaves).

**4. Presentation.** `SandwormQuads.cs`, `EntityShape.cs`, `Layout.hlsli`, `Sandworms.hlsli`, `EntityPs.hlsl`,
`EntityLayerBuilder.cs`, dust particles, `StepEventSounds.cs` and `AudioTweaks.cs`, the tremor gauge and warnings
(`MatchOverlay`, `GameEventWatcher`, `HudPalette`), radar targets in `SideSensing`, and the browser's `hud.js`,
`audio.js` and `guidance.js`. Risks: three shader targets plus the web one; a looped rumble voice.

**5. Bots, guns, switching it on.** `BotTankAI` and `BotGunner`: a held bot aims at the maw and fires; a bot whose
block is *Calling* finishes what it is doing and moves two blocks before it digs or sieges again (it asks
`World.TremorFor`; it does not read the noise map). Gatlings and turrets shoot a worm segment in a clear line
(`TryFindTarget` on the pack, asked only while one is out). `Sandworms = true` in `WorldFactory` and
`BrowserSession`. DESIGN.md (a new §6.8 and the decision log), both architecture files and a line in AGENTS.md
("The worms cost what is out and what is loud"). Then a balance pass. Risk: bots that dig and siege without pause
are the loudest things on the map; without this phase's bot rule they feed themselves to the worms, which is why
the rule is not switched on earlier.

**6. The thumper (optional, protocol bump).** A cheap machine that beats every half second while charged (72
unrest a second at its own position, exempt from the machine cap) and is wrecked by the first worm that reaches
it: the planted decoy, and the bait for a kill box of turrets in a cavern. The full machine recipe, a sixth build
key and a wider input encoding. Kept last because it is the only part that touches the protocol.

## 11. Open questions

1. **Reward for a kill.** The plan gives four quiet minutes around the lair and nothing else. Should a dead worm
   also leave something? Loose ore would need a source, since ore is never made from nothing.
2. **Machines.** Should a worm that reaches a noisy harvester or gatling and finds no tank wreck it? It makes
   outposts something to defend and gives noise a price for builders, but it is a second front on top of the
   emberites' planned attacks on relays. Left out of phases 1 to 5.
3. **Permanent kills.** Lairs refill after four minutes, up to generation 3 and then for ever at that size. Should
   a lair be destroyable (dig to it and torch it), so that a region can be made safe for good?
4. **Mines.** The brief mentions them. The plan has none: the gun is the decoy and the thumper the planted one. A
   mine is a new projectile or machine and another key. Wanted?
5. **How much to tell.** The gauge shows unrest and its reach, but not where the lairs are or which way a worm
   comes. Should a discovered lair be marked like a nest, or is not knowing the point?
6. **No tunnels left behind.** The plan has worms pass through soil without opening it (only breaches and vent
   chambers are dug), so that narrow tunnels stay narrow and the map keeps its shape. Open worm tunnels would be
   more spectacular and would change the map over a match, at a cost per cell written. Confirm.
7. **Both tanks share the ground.** One side's noise calls worms that may meet the other side first. Is noise as
   a weapon wanted (shelling the ground beside the enemy to call worms onto him), or should a worm prefer the
   tank whose colour made the noise?
8. **Worms and emberites.** Does either hurt or block the other? The plan has them pass through each other (§4),
   and a worm's vent feeds emberites (§5.4); nothing else is decided.
9. **Machines in the way.** Do machines block a worm? They are not in its obstacle list (§4) now, so it passes
   under them.
10. **More calls than `MaxOut`.** Which lairs wake once `MaxOut` worms are out: are calls queued, the nearest lair
    first, or dropped?
11. **Whose burst.** `Explosion` is recorded without a colour, so a burst cannot be put down to a side. That matters
    for question 7.
12. **Where `IgnoreTicks` lives.** Per worm or per lair, kept as a list of block and expiry tick; either way it is
    saved in the snapshot and mixed in the hash.
