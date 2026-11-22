# Pyrite deposit feedback: design

Status: **built** as variant A in three PRs (#118 Core, #119 world, #120 HUD). §0 lists the decisions taken when it
was built; where the sections below differ, §0 wins. The numbers are starting values to tune in play.
The concept sheet beside it, `docs/concepts-pyrite-deposit.html`, sketches three variants and marks the recommended
one (variant A). Times assume 24 ticks a second (`Tweaks.Perf.TargetFps`); distances are terrain cells (a tank is 7
by 7, the core a disc 5 cells across).

## 0. Decisions as built

- **Deposit heat is on**, 250 a unit, at the core and at a relay (`TankBase.BankFromHold`, booked with
  `RecordInjected`). Both heats exist: the cargo heats the tank while carried (DESIGN.md §6.3, already booked) and
  the deposit heats the reactor when put down. At the core the bank runs **after** `PumpHeatAtBase`, so the deposit
  takes only the reactor room the tank's dock cooling left, so it never cuts that tick's cooling. The heat it leaves
  does take room in the ticks after, so near a full reactor a deposit costs some later dock cooling, relay extraction
  and shield recharge; a full hold vents in under a second only when nothing else feeds the reactor. A reactor with no room
  takes the ore but none of its heat, and the glow then shows the pulse only (it reads the real `HeatStoreFill`,
  never the requested heat). None of the five existing `SimulationFingerprintTests` constants changed (none of those
  runs banks ore); a sixth run, `TankBanksAtCore`, now records a bank and its deposit heat.
- **Core sound before the HUD PR:** in the Core PR a bank at the core plays nothing of its own (the relay's pickup note
  would have sounded every tick); the HUD PR gives it the deposit sound.
- **Core or relay is told by `StepEvent.Target`** (`ImpactTarget.Base` at the core's centre, `ImpactTarget.Machine`
  at a relay), not by comparing positions. `Position` is the receiver, so readers find the tank by `Color`.
- **No store capacity anywhere** (user decision): no `CoreDropOff.StoreFull`, `StoreFill`, `StoreLevel`, `BASE FULL`
  line or floater, refused ring (core heat mode 2), hairline bar, colour by level, `/ capacity` or extra store-full
  toast. BASE is the number in one colour. The existing `store-full` toast (`GameEventWatcher`) and the base panel
  line are unchanged, so §1's "nothing is said" at a full store was wrong.
- **Rivals:** crystals and core glow for any base in sight (the old ore beam was the local player's only); the
  numbers (`+N`, `BASE N`) for the player's own side only. A relay's flash follows the crystals (in sight); the
  sounds stay the player's own.
- **Harvested ore does not pulse the core**; the BASE row's rate counts it.
- **Glyphs:** `GlyphText` gains `O` (for `BASE STORE`); the burn floater reads `+N RESTART` (units of ore), not
  `CHARGE`, which needs letters the set lacks.
- **Pulse:** 0.12 a landing, half-life **0.3 s**; the test is "under 0.05 within 1.5 s of the *last* landing".
- **The relay label** (added at the user's request): the player's nearest powered relay within 24 cells, out of the
  base, carries `BASE STORE`, the store and `+N/MIN` in the world, drawn like the nest labels. Core answers which
  relay (`World.NearestOreDropOff` / `Tank.NearestDropOff`, through `MachineList.CollectNear`); output only.
- **A hold** (pause, or a network tick with no step) freezes the pour where it was last drawn, as shots do.
- **HUD state:** `OreLedgerView` (count-up, drained hold, held UNLOADING, the deposit's units and age) is fed with the
  entity layer's steps; `MaterialsView` turns it and the tank into what MATERIALS shows, and the browser gets the same
  view in `PlayerStatus.Materials`. `BuildSlotView.OreLine` takes the pouring flag; `OrePileFrame` takes a hold count.
- **MATERIALS layout:** its plate's inside is 20 px (174 to 194), so `LedgerLayout` has its own rows (HOLD 179.2, BASE
  189), without bars, checked by a layout test; the readout text drops from 22 to 11 art pixels. The `+N` rises in
  the plate's middle, clear of both numbers.
- **Sound:** `SoundBank.Deposit.cs` is listed in `Emberward.Rendering.csproj` and removed from the desktop's compile
  list. A core bank is `Deposit` once per pour (`CueVoice.Pour`), a relay unit the pickup note, a burn `Deposit`
  pitched down; all `CueHeard.Player`. No refused sound.
- **Not done:** the base panel's ore line does not show the rate yet.

## 1. The problem

A tank puts its pyrite down by touching (DESIGN.md §6.3): within 2 cells of its own core (`Tank.TouchesCore`) the hold
empties at `Tweaks.Base.HomeAbsorbOre` = 10 units a tick, and within 2 cells of one of its own powered relays
(`Tank.TouchedRelay`) at `Tweaks.Ore.RelayUnloadPerSecond` = 4 units a second. Today the player sees:

| What | At the core | At a relay |
| --- | --- | --- |
| How long it lasts | a full hold of 40 empties in **4 ticks (0.17 s)** | 10 s for 40 units |
| In the world | the ore beam (`KindOreBeam`, `CoverageQuads.SubmitTether`) for those 4 ticks, about 5 cells long, then `BeamFader` fades it | the beam to the relay, then ore pulses along the links (`Link.OreFlowTicks`) |
| Step event | **none** but `BaseServiced` (also sent for repair and cooling) | `OreUnloaded`, one per unit, at the tank |
| Sound | none of its own (`BaseServiced` drives the recharge loop) | `Pickup` per unit |
| HUD | UNLOADING with bobbing arrows under the hold for 4 ticks; the hold pile and `23 / 40` drop to empty | UNLOADING while it lasts |
| Where it went | the base ore store, shown only in the base overlay panel (`NetworkPanel`: "Base ore store N") and as stock in a build slot's caption | same |
| Full store | the `store-full` toast and the base panel say so; the ore line goes quiet (`CoreDropOff.None`) | the tank just keeps its ore |

So at the core, where most ore is banked, the hold empties in a sixth of a second with nothing that says where it went
or how much the base has now. The MATERIALS panel deliberately leaves the store out (`BuildSlotView.OreLine`'s comment:
"it only changes at the base"), which is exactly the moment the player wants to see it.

## 2. What the player should read

1. **Where it went:** the pyrite visibly leaves the tank and goes *into* the core or relay.
2. **That the base took it:** the receiver reacts, and the core visibly warms, because pyrite is hot and the warmth
   is real (§3.2).
3. **How much:** a `+N` at the receiver, and the base's stock in the MATERIALS panel, with how fast it is growing.
4. **When it cannot:** a full store or a dark base says so at the core, not by silence.

Decisions (variant A, "Pour and glow", in the concept sheet):

- The simulation's unload rates stay as they are. The **presentation stretches** a 4-tick bank into a 0.9 s pour,
  driven by a step event, as `BaseShieldQuads` stretches a one-tick hit into a flash. Slowing `HomeAbsorbOre`
  (variant C) would make the existing beam readable for free but makes every trip home slower; we do not want that.
- The core **really warms**: each unit a tank banks delivers a small fixed amount of heat into the base reactor,
  booked as injected in the ledger. The glow reads the reactor's real heat store (`HeatStoreFill`) and cools as the
  radiator vents it. No visual heat exists that the simulation does not have.
- Feedback is in the world first (crystals, receiver glow, `+N`), the HUD second (MATERIALS base row), sound third.
  A toast appears only on the first deposit of a match and at a full store, never on every deposit.

## 3. Core

### 3.1 One event for every unit a tank banks

Redefine `StepEventKind.OreUnloaded`: "a tank moved `Count` units from its hold into its base's store this step;
`Position` is the **receiver** (the core's centre, or the relay's position); `Color` is the tank." It is recorded on
both paths:

- `Tank.AdvanceBaseInteraction`, when `banked > 0` (today nothing is recorded there): one event a tick with
  `Count: banked`, at `baseColl.Position`.
- `Tank.AdvanceRelayUnload`, as today, but at `relay.Position` instead of the tank's.

Readers tell the core from a relay by `StepEvent.Target`: `ImpactTarget.Base` at the core, `ImpactTarget.Machine` at
a relay. (`StepEvent.Machine` cannot carry it: `MachineType`'s default is `Harvester`.) The tank is found by `Color`.

`RestartOreBurned` moves its `Position` from the tank to the core's centre too, so the burn path can draw the same
pour (it becomes charge rather than store; §4.3). Its only reader today (`BasePowerTests`) checks `Count`.

Nothing in the simulation reads step events, so this changes no result. Readers of `OreUnloaded` today:
`StepEventSounds` (positional `Pickup`, heard by the player; the receiver is beside the player), `GuidanceTests` (sums
`Count`; the core path now adds to it, which is right) and `OreEconomyTests` (sums `Count` at a relay; gains a
`Position` check).

### 3.2 Deposit heat: the core really warms

New constant `Tweaks.Cargo.DepositHeatPerOre = 250f` (energy units). The reactor holds 500 a heat unit and 100 units
(50,000 in all), and the stock radiator vents 500 a tick. A full hold of 40 delivers 10,000: a fifth of the reactor,
vented in about 20 ticks (0.8 s) when nothing else feeds it. That is a clear step on the heat store and a brief rise of
`ReactorTemperature` (the cold side climbs up to `EngineColdSideRise` = 20 degrees at full, so about 4 here); the
engine's efficiency dips for under a second. A real but negligible cost, in the spirit of "pyrite is hot".

One owner for banking. Add to `TankBase`:

```csharp
/// Moves up to max units from a tank's hold into the store. Each unit moved brings the deposit heat into the
/// reactor, as much as it has room for, booked as injected. Returns the units moved.
internal int BankFromHold(OreStore hold, int max, ThermalLedger ledger)
```

Both unload paths call it instead of `Hold.MoveTo(store, …)`. It calls `AcceptHeat(units * depositHeatPerOre)` and
`ledger.RecordInjected(accepted)`; a full reactor accepts nothing, and nothing is created. Thermal code reads its
numbers through `SimulationSettings` (`SourceGuardTests`), so `BaseDepositHeatPerOre` is added there from `Tweaks`,
and the base keeps it from the settings it is built with (as it keeps the ambient temperature).

- Reactor energy is already in `World.SumSystemThermalEnergy`, so the ledger balances with one `RecordInjected`
  (`ThermalLedgerTests` checks every tick).
- Harvester ore (`OreHarvested`) brings no heat: harvesters work only ore below 45 degrees, while the hold's warmth is
  what DESIGN.md §6.3 calls the ore's own energy. The rule is "a tank's cargo warms the core"; DESIGN.md says so.
- The burn path (dark base) already turns units into charge (`BasePowerSystem.BurnHold`, `RecordFromRestartOre`);
  it gets no deposit heat.
- No new state: the heat lives in the reactor (`Reactor.Heat`, `_reactorHeatRemainder`), already saved and hashed
  (`MixReactorHeat`). `SnapshotCoverageTests` and `SnapshotRoundTripTests` are untouched.
- **This changes results on purpose**: every run where a tank banks ore (the bot goes home with every hot load) gets a
  different reactor curve. Update the constants in `SimulationFingerprintTests` and say so in the commit message (as
  built none changed: no recorded run banks ore).

The constant is the switch: at 0 the simulation is as today and the glow shows only the existing heat store and the
event pulse (variant B's honesty rule).

### 3.3 Queries

What the HUD needs mostly exists: `TankBase.Store.Ore`, `Store.Capacity`, `Store.IsFull`, `TankBase.HeatStoreFill`,
`Tank.IsUnloadingOre`, `Tank.UnloadNode`, `Tank.CoreDropOff`. Small additions so Desktop does not recompute rules:

- ~~`CoreDropOff.StoreFull`, `TankBase.StoreFill`, `TankBase.StoreLevel`~~: dropped (§0, no capacity handling).
- `World.NearestOreDropOff(tank, reach)`: the relay that carries the store label (§0).

The store's **rate** is a statistic of events, not a game rule, so it is counted on the client from `OreUnloaded` and
`OreHarvested` (§5.2), the way `GameEventWatcher` already counts "Harvesters delivered +N ore".

## 4. In the world (Rendering, shared by desktop and browser)

A new emitter, `Rendering/Entities/DepositQuads.cs`, captures each step after `World.Advance`
(`EntityLayerBuilder.CaptureStep`, beside `_baseShields.CaptureStep`): it reads that step's `OreUnloaded` and
`RestartOreBurned`, each base's `Position`, `HeatStoreFill` and the local tank's `CoreDropOff`, and keeps a small fixed
ring of **pours** (16) and **floaters** (8). What it draws is a pure function of a pour's start step, its count, its
hashes and the current step plus `StepAlpha`, with no clock or random source (as `FireParticles`). It is output only,
so it is not in the snapshot; a late joiner simply starts with no pours in flight.

Shown for every deposit the local player can see (`Sight`/`VisionMap`, as the other emitters); the `+N` numbers only
for the local player's own side.

### 4.1 The pour (crystals)

On an `OreUnloaded` at a core: a pour of `min(Count, 12)` crystals. Later events from the same tank within 6 steps join
the same pour (a 40-unit bank is four events of 10, a tick apart), up to 12 crystals in all. Each crystal:

- launches from the tank's hold (the interpolated tank position, 1.5 cells toward the receiver) at a stagger of
  `i * 0.04 s`; its launch point is frozen at launch, so a tank that drives off leaves the rest of the pour behind;
- flies 0.45 s on a short arc to the receiver's centre: a quadratic whose apex is 2.5 cells off the straight line,
  alternating sides by hash, so a 5-cell gap reads as a pour, not a smear;
- shrinks to 60% and brightens to white-gold as it lands; each landing adds to the receiver's pulse (§4.2).

A full hold pours in about 0.9 s. At a relay each event is one unit, one crystal every 6 steps, which with the existing
beam reads as a trickle. The ore beam stays as it is for relays; at the core it is replaced by the pour
(`CoverageQuads.SubmitTether` skips the beam when `UnloadNode` is the core): a 5-cell beam for 4 ticks is the thing
that does not read.

Entity kind **27 `KindOreCrystal`**: one quad per crystal, 1.4 cells square. `params` = (age 0..1, glint seed 0..1,
spin phase, landing brightness 0..1). The shader draws a faceted diamond in `kOreGold` with a pale glint and a soft
additive halo; the facet code of `OreBeam` (`Beams.hlsli`) is factored into a function both call. Its motion is the
arc's change over one step, passed to `EntityQuads.Add(..., motion)` so the vertex shader extrapolates it (AGENTS.md,
"Entities are drawn by extrapolation").

### 4.2 The receiver: core heat glow and pulse

Entity kind **28 `KindCoreHeat`**: one quad per visible base core, radius 9 cells, drawn over the terrain's core.
`BaseMaterial.hlsli` keeps drawing the reactor; no `PostParams` row is added, so no layout version bump. `params`:

- `.x` heat: the base's `HeatStoreFill`, eased toward Core's value each step (display only). This is the real reactor;
  relays delivering heat light it too, which is right: the core is the base's heat store.
- `.y` pulse 0..1: rises by 0.12 for each landing crystal (capped at 1) and halves every 0.3 s.
- `.z` phase (the shared 4 s cycle).
- `.w` mode: 0 store (gold), 1 burn (a dark base taking ore as charge: a cyan-white flash, no gold). (No refused mode.)
- `color.r` radius in cells (9 for a core, 4 for a relay's flash).

Look: the reactor's centre brightens along the heat ramp (`HeatScale` colours: amber, orange, yellow-white) with `.x`;
a soft additive halo whose reach grows with `.x`; on each pulse a thin bright ring expands from the core's rim to about
7 cells and fades, with a brief heat shimmer above it. With `.x < 0.01` and `.y < 0.01` no quad is emitted.

A relay receiving a unit gets a small gold flash through the same kind: radius 4, mode 0, for 0.3 s after each unit.

### 4.3 The `+N` floater

On the first crystal landing of a pour by the local player's side: a floater `+N` at the receiver, `N` the pour's units
(counting up as later events join it), drawn with `GlyphText` (kind 16, as the charge and nest labels; it already has
`+`) in gold, rising 3 cells over 1.2 s and fading over the last 0.4 s. Under it, smaller, for the same 1.2 s:
`BASE 1234`, the store after the pour. At a relay the floater shows the visit's running total (`+1`, `+2`, … while the
tank stays), so a relay drop-off reads as a counter, not a cloud of `+1`s.

- Full store: nothing extra in the world (§0).
- Dark base: the burn pour in cyan and `+N RESTART` (N in ore units: the restart is counted in ore on the HUD
  already). `GlyphText` has `0-9 . : / + - > s k g e n A B D E G I M N O R S T` and space; `O` was added.

### 4.4 Shader notes

- New file `Rendering/Shaders/Entity/Deposit.hlsli` with `OreCrystal(...)` and `CoreHeat(...)`, included by
  `EntityPs.hlsl`, whose dispatch gains two `else if (kind < kKind... + 0.5)` branches.
- Kinds 27 and 28 in `Entity/Layout.hlsli` (with their `params` rows in its header comment) and `EntityShape.cs`
  (`KindOreCrystal`, `KindCoreHeat`); `EntityLayoutGuardTests` checks that they match and are dispatched.
- **Single return**: each function builds `result` and returns once at its end (`if (inside) { ... }`, never
  `if (!inside) return 0;`); Windows browsers compile through FXC, which hung on early returns.
- No new textures or samplers, so no resource declarations. Should one ever be needed, declare it with the macros in
  `PostPs/Common/Registers.hlsli` and give it a `SamplerState` at the same index.
- The browser gets both kinds for free: it draws the same entity shader compiled to GLSL ES (`ShaderCompiler --web`)
  from the same `EntityVertex` stream. The vertex layout does not change, so `webgl.js`
  (`ENTITY_ATTRIBUTE_FLOATS`) does not either.

## 5. HUD

### 5.1 MATERIALS: hold and base, two rows

The readout plate (`MaterialsPlate`, 186 by 20 art pixels) becomes a two-row ledger like SHIELD/HULL (`LedgerLayout`):

```
HOLD            23 / 40
BASE   +18/m      1,234
```

- **HOLD**: today's readout, in the readout green; deep orange when full.
- **BASE**: the store, thousands separated, in one pale gold (§0: no capacity, bar, `FULL` or colour by level).
- **Rate** `+18/m`: units a minute into the store for the player's side over the last 30 s (§5.2), dim, hidden at 0.
- **Deposit tick**: when a pour lands, the BASE number counts up from the old value to the new over the pour's 0.9 s
  (display only: the store already holds it), a gold `+N` floats up out of the row and fades, and the row flashes gold
  once. The hold pile drains over the same 0.9 s instead of vanishing in 4 ticks (the pile frame eases toward
  `OrePileFrame`, as the gauges ease).
- The ore line under the hold window: `UNLOADING` as today, held for at least the pour's length rather than 4 ticks.

`BuildSlotView.OreLine`'s comment about the store not being here is rewritten. `NetworkPanel`'s "Base ore store N"
line stays (it is the docked detail view) and gains the rate.

### 5.2 Where the numbers come from

A small shared class `Gui/OreLedgerView.cs`, listed in `Emberward.Rendering.csproj` and left out of the desktop's
list so the browser shares it (AGENTS.md, Layout). Fed each step with `World.StepEvents` and the player, it keeps a
30 s ring of per-step deposits for the player's side (`OreUnloaded` of the player's colour, `OreHarvested` of the
base's) and gives `RatePerMinute`, `DisplayedStore` (the count-up), `LastDepositUnits` and `DepositAge`. Display state
only.

### 5.3 Browser parity (`strip.js`)

`PlayerStatus` (`BrowserFrame.cs`) already carries the store (`home.Store.Ore`). Add `baseOreCapacity`, `baseOreLevel`,
`baseOreShown` (the count-up), `oreRatePerMinute`, `depositUnits` and `depositAge`, filled in `BrowserFrameBuilder`
from `OreLedgerView`. `strip.js` draws the two rows at the desktop's coordinates (`MATERIALS` and a new
`MATERIALS_LEDGER` mirroring `LedgerLayout`), the rising `+N` and the gold flash, from `PlayerStatus.Materials` (the shared
`MaterialsView`, §0) rather than separate fields. The phone plates (`hud.js`) show `BASE 1,234` under the hold. `BrowserSessionTests`
checks the new fields.

### 5.4 Toasts

- First deposit of the match into the player's base: "Pyrite banked: +40 (base 70)", detail "The base store pays for
  what you build inside the network. Touch the core or a powered relay to unload." `NotificationKind.Good`, from
  `GameEventWatcher`, key `first-deposit`, once a match.
- No new store-full toast: `GameEventWatcher` already posts `store-full`.
- No toast per deposit: the floater and the HUD row say it where the player is looking.

## 6. Sound

New `SoundEffect.Deposit`: a short cascade of 3 to 6 crystalline clinks, rising in pitch, then a low warm thump as the
core takes it, about 0.6 s, synthesised in `SoundBank`. `SoundBank.cs` is at 648 of the 700-line cap, so the generator
goes in a partial, `SoundBank.Deposit.cs`, listed in `Emberward.Rendering.csproj`. In `StepEventSounds`:

- `OreUnloaded` at the core: `Deposit`, gain `AudioTweaks.DepositGain * (0.6 + 0.4 * min(units, 40) / 40)`, heard by
  the player, with a new `CueVoice.Pour` that plays once per pour (events within 6 steps join, as in §4.1) so a 4-tick
  bank is one sound, not four. `StepEventSounds` needs the base's position to tell core from relay; pass it in, or let
  `SoundDirector` decide (it already knows the tracked tank's base).
- `OreUnloaded` at a relay: the `Pickup` streak, as today.
- `RestartOreBurned`: `Deposit` pitched down, beside the recharge loop.
- No refused sound (§0).

`StepEventSoundsTests` gains the core and relay cases.

## 7. Determinism, snapshot, network

- **Determinism**: the only simulation change is deposit heat (§3.2): plain double arithmetic through `AcceptHeat`,
  no clock or randomness. The event changes are output only.
- **Snapshot**: no new field; the heat lives in the reactor, already saved. `CoreDropOff` is recomputed every tick
  (reset in `Tank.Advance`), so its new value needs no saving. The presentation classes are not part of the world; a
  late joiner starts with no pours and a heat glow that eases up to the reactor's value in a second.
- **Network**: every client records the same events at the same tick, so every client draws the same pour. No
  protocol or relay change. Deposit heat changes simulation results, so it needs the usual care for any gameplay
  change: mixed builds must not share a match, and replays recorded before it diverge.
- **Cost**: Core adds one event per banking tick (four per trip home) and one `AcceptHeat` call; nothing at all while
  nobody unloads. `DepositQuads` walks the step's events once and draws at most 16 x 12 crystal quads plus one quad
  per visible warm core.

## 8. Tests

Core:

- `OreEconomyTests`: touching the core records `OreUnloaded` whose counts sum to what was banked, at the core's
  centre, with the tank's colour; at a relay the event is at the relay's position.
- `DepositHeatTests` (new), each case against the same world with no deposit heat: banking 40 units injects
  `40 * DepositHeatPerOre` more and leaves the reactor warmer (by part of it: the radiator has started venting); a
  full reactor injects nothing more; the burn path injects nothing more; a relay injects its units' worth; the pump
  comes before the bank; the ledger balances every tick throughout.
- `BasePowerTests`: `RestartOreBurned` at the core's centre.
- `SimulationFingerprintTests`: a new run, `TankBanksAtCore`, with its recorded constant (said in the commit message).

Rendering and HUD:

- `DepositQuadsTests` (new): the four events of one bank make one pour of 12 crystals; a crystal is at the tank's hold
  at age 0 and at the receiver at age 1; the pulse falls under 0.05 within 1.5 s of the last landing; no pour at a base out of sight; the
  floater says `+40` for the player's side and nothing for a rival; the same steps give the same quads.
- `EntityLayoutGuardTests`: passes with kinds 27 and 28.
- `OreLedgerViewTests` (new): the 30 s rate; the count-up reaches the store at the end of the pour.
- `StepEventSoundsTests`: a core bank is one `Deposit`; a relay unit is `Pickup`.
- `BrowserSessionTests`: the new `PlayerStatus` fields.
- `RenderingSourceGuardTests`: the new shader file passes (no `register()`, no early return).

## 9. Implementation plan

Three PRs, each green on `dotnet test Emberward.slnx` on its own.

**PR 1, Core: the event, deposit heat** (changes results).
- `src/Emberward.Core/StepEvents.cs` (`OreUnloaded`, `RestartOreBurned` docs)
- `src/Emberward.Core/Entities/Tanks/Tank.cs` (record at the core; receiver position and target; `BankFromHold`)
- `src/Emberward.Core/Entities/Bases/TankBase.cs` (`BankFromHold`)
- `src/Emberward.Core/Config/Tweaks.Cargo.cs`, `src/Emberward.Core/Config/SimulationSettings.cs`
- tests: `OreEconomyTests`, `DepositHeatTests`, `BasePowerTests`, `SimulationFingerprintTests`, `TankHeatBehaviorTests`
- `DESIGN.md` §6.3 (deposit heat) and §13 (decision log); `src/Emberward.Core/ARCHITECTURE.md`

**PR 2, the world: pour, core heat, floater** (no result change).
- `src/Emberward.Desktop/Rendering/Entities/DepositQuads.cs` (new), `EntityShape.cs`, `CoverageQuads.cs` (no beam
  at the core)
- `src/Emberward.Desktop/Rendering/EntityLayerBuilder.cs` (capture and emit)
- `src/Emberward.Desktop/Rendering/Shaders/Entity/Deposit.hlsli` (new), `Entity/Layout.hlsli`, `EntityPs.hlsl`,
  `Entity/Beams.hlsli` (the shared crystal facet)
- tests: `DepositQuadsTests`; `EntityLayoutGuardTests` and `RenderingSourceGuardTests` pass
- `src/Emberward.Desktop/ARCHITECTURE.md` (the emitter list)

**PR 3, HUD, sound, toasts, browser.**
- `src/Emberward.Desktop/Gui/OreLedgerView.cs` (new; in `src/Emberward.Rendering/Emberward.Rendering.csproj`, out of
  `Emberward.Desktop.csproj`), `HudStrip.cs`, `LedgerLayout.cs`, `NetworkPanel.cs`, `Gui/Guidance/GameEventWatcher.cs`
- `src/Emberward.Desktop/Audio/SoundEffect.cs`, `StepEventSounds.cs`, `SoundDirector.cs`, `SoundBank.Deposit.cs`
  (new), `src/Emberward.Desktop/Config/AudioTweaks.cs`
- `src/Emberward.Browser.Client/BrowserFrame.cs`, `BrowserFrameBuilder.cs`;
  `src/Emberward.Browser/wwwroot/strip.js`, `hud.js`
- tests: `OreLedgerViewTests`, `StepEventSoundsTests`, `BrowserSessionTests`, `GuidanceTests` (the two toasts)
- `DESIGN.md` §6.3 (the MATERIALS paragraph)

## 10. Open questions

Questions 1 to 4 were settled when this was built (§0): deposit heat on; rival crystals and glow in sight, numbers only
for your own side; no capacity on screen at all; harvested ore does not pulse the core. Question 5 is still open.

1. **Deposit heat on or off.** Recommended on, at 250 a unit (a fifth of the reactor per full hold, vented in under a
   second): it makes the glow true and costs a negligible engine dip. At 0 there is no gameplay change and the glow
   shows only the existing heat store plus the event pulse.
2. **Rival deposits.** Recommended: crystals and glow for any base in sight, numbers only for your own. Should a
   rival's banking be hidden entirely (fog of war over economy)?
3. **Capacity on screen.** The stock capacity, 20,000, is far above a match's ore (about 2,500 on 1500x750), so it shows
   only as colour and a hairline bar. If a mode or upgrade lowers it, print `/ capacity` from 50% full.
4. **Harvester deliveries.** Should harvested ore pulse the core (no crystals, no heat)? Recommended no for now: the
   link flow shows it arriving, and the BASE row's rate counts it.
5. **Pour length.** 0.9 s for a full hold reads without slowing anyone; a tank that drives off mid-pour leaves the
   crystals flying from where it was. Check in play that this does not look detached.
