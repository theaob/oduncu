# Milestone 1 plan: vertical slice

*Status: proposed. Written against `main` at the milestone 0 scaffold (commit `3d4ca4c`).*

The exit criteria from the design document (section 14) are:

> All four ages, full unit roster for one civ, one map template, Standard AI, complete
> touch UI. Internal playtests reach a 15-minute match.

This plan turns that sentence into a list of simulation features, the tests that prove
each one, the order to build them in, and the checks that can only be done in Unity or on a
phone. Everything in the simulation half can be built and tested headless in CI; the Unity
half cannot, and is listed separately so nobody mistakes a green CI run for a finished slice.

---

## 1. Where milestone 0 leaves us

What exists and works headless:

| Area | State on `main` |
|---|---|
| Simulation core | Fixed-point `FP`, 10 ticks/s, entities in id order, state hash, command log with replay. |
| Entities | Villager, Militia, Town Center, Barracks, Tree. Stats hard-coded in `EntityDefs`. |
| Economy | Wood only. One drop-off (Town Center). Fixed population cap of 20. |
| Movement | 8-way grid A* per unit. Units do not block each other and do not separate. |
| Combat | Single `Attack − Armor` value, melee range only, idle military auto-engages. |
| Map | 48×48, two bases, seeded forest blobs. |
| Tests | 29 NUnit tests: fixed-point maths, A*, gameplay rules, 10,000-tick determinism and replay. |
| Unity | Tick runner, primitive renderer with interpolation, IMGUI buttons, mouse/touch tap input. |

Gaps that matter for milestone 1, found while reading the code:

- **Costs are one integer of wood** (`EntityDef.CostWood`, `PlayerState.Wood`). Four resources
  need a `Cost` struct everywhere a price is checked or paid.
- **Stats are global and immutable.** Upgrades change stats per player, so a unit must read its
  stats from its owner's resolved table, not from a shared `EntityDef`.
- **`FindNearest` scans every entity and takes a lambda.** That is fine for 30 entities and
  too slow and too allocation-heavy for the budget in section 12.4 (2 × 75 units plus about 400
  trees and buildings, under 4 ms per tick). A spatial index is needed before the roster grows.
- **`RemoveDead` uses `List.RemoveAll` with a lambda**, and paths are fresh `List<Cell>` per
  unit. Section 15 asks for no per-frame allocations in the sim.
- **No unit collision.** Large armies will stack on one cell, which breaks readability
  (pillar 3) and makes melee fights look wrong.
- **The on-device determinism check (milestone 0's last exit item) has not been run.**
  Milestone 1 should not start changing rules before the baseline hash is confirmed on two
  phones, otherwise a platform desync cannot be told apart from a rules change.

---

## 2. Scope

### In

- One civilization, played by both sides: **Woodlanders** (default, see open decision 1).
- All four ages with their costs, requirements and research times from section 6.
- The full Woodlanders roster: 10 shared lines plus the Longbowman (section 5.2).
- Every building in section 5.4 except the Wonder.
- About 30 upgrades from section 6, including both Woodlanders unique techs.
- Four resources with every source in section 4.1, farms with auto-reseed, houses, cap 75.
- The economy planner (section 4.3) with the three presets.
- Walls, gates and towers.
- The **Open** map template at 64×64 for 1v1, with the standard start from section 9.
- Fog of war (hidden, explored, visible) computed in the simulation.
- Conquest victory (section 3).
- Standard AI.
- The complete touch UI from section 7, with placeholder art.

### Out (moved to milestone 2 or later, as in section 14)

- Other three civilizations, Walled and Forest templates, Easy/Hard/Brutal AI.
- Save and load, campaign, tutorial, audio.
- Wonder and King of the Hill modes.
- Final art. Milestone 1 uses low-poly placeholders that are readable at phone size; the
  artist's models replace them without simulation changes.
- Multiplayer and Photon Quantum (milestone 3). The command boundary stays as it is so the
  swap remains possible.

---

## 3. Simulation work

Each item lists what it adds and the tests that close it. "Scenario test" means a small
hand-built map like `GameplayTests.SmallMap`; "property test" means many seeds or many ticks
checked against an invariant.

### 3.1 Foundations (do first)

These change no gameplay but everything after depends on them.

1. **Data tables.** Unit, building, resource, tech and age data moves to CSV files under
   `Oduncu.Unity/Assets/Data/`. A small generator in `tools/` turns the CSVs into a checked-in
   `Assets/Sim/Generated/Defs.g.cs` of plain C# initialisers. The simulation never reads files,
   so it stays free of IO and identical on every platform. *Decision taken here:* code
   generation over runtime parsing, because it keeps the sim free of string parsing and makes
   every balance change a reviewable diff.
   - Test: generator output for the committed CSVs equals the committed `.g.cs` (CI fails on a
     stale file).
   - Test: every unit and building referenced by a `Trains`, tech or age row exists.
2. **Multi-resource costs.** `Cost { Food, Wood, Gold, Stone }` on every def; `PlayerState`
   holds four stockpiles. Villagers cost 50 food as in section 4.2.
   - Tests: train and build are rejected when any one resource is short and charge exactly the
     cost when accepted; cancelling a queued unit refunds it in full.
3. **Per-player resolved stats.** A `PlayerStats` table per player, built from base defs plus
   researched modifiers, rebuilt only when a tech completes. Entities hold their kind and
   owner and look stats up there.
   - Test: researching +1 melee attack changes damage dealt by existing and newly trained
     units of that player only.
4. **Spatial index.** A uniform bucket grid (for example 8×8-cell buckets) updated when units
   change cell, used by every nearest-entity and in-range query. Iteration order inside a
   bucket is by id, so results stay deterministic. `FindNearest` keeps its signature for tests
   but stops allocating.
   - Property test: for 50 seeds, every query returns the same entity as the current brute
     force scan.
5. **No per-tick allocations.** Pooled path buffers, an in-place dead-entity sweep, no lambdas
   on the tick path.
   - Test (headless only): after a 500-tick warm-up, 1,000 further ticks of the determinism
     scenario allocate 0 bytes on the managed heap (`GC.GetAllocatedBytesForCurrentThread`).
6. **Banned-API check.** Section 12.2 promises an analyzer rule from milestone 1. A Roslyn
   analyzer is the end state; the first step is a test that scans `Assets/Sim/**/*.cs` and
   fails on `float`, `double`, `System.Random`, `DateTime`, `UnityEngine`, or `foreach` over a
   `Dictionary`/`HashSet`.
7. **Performance benchmark.** A headless benchmark that runs the 1v1 standard start at full
   population (2 × 75 units, about 400 trees and buildings) and reports mean and worst tick
   time. It runs in CI as a report, not a gate, because CI machines vary. The gate is the
   on-device number in section 5.
8. **Command log version 2.** New command kinds and fields bump `CommandLog.FormatVersion`.
   Old logs are rejected with a clear error rather than misread.

### 3.2 Economy

| Feature | Notes | Tests |
|---|---|---|
| Resource kinds | Berries, sheep, deer, boar, gold mine, stone mine, trees. Each with its own amount, gather rate and carry type. | Each kind deposits into the right stockpile. |
| Drop-off buildings | Mill (food), Lumber Camp (wood), Mining Camp (gold, stone); Town Center takes all. | Villager walks to the nearest drop-off that accepts its carry type. |
| Carry switching | A villager carrying wood that is sent to gold drops the wood (as in AoE2). | Carry is lost, not converted. |
| Herdables | Sheep are owned by whoever sees them first and can be stolen by line of sight. | Ownership changes when only the enemy is in range. |
| Hunting | Deer flee, boar fight back. Villagers use their ranged hunting attack on animals only. | A lone villager kills a deer; a boar damages a villager. |
| Farms | Infinite food at a slow rate. Auto-reseed charges wood when the farm runs out; if the player cannot pay, the villager goes idle. | Reseed charges wood once; idles without wood. |
| Houses and population | House +5, Town Center +10, hard cap 75. Population is counted from units plus queued units. | Training is held (not rejected) when housed, and resumes when a house completes. |
| Rally points | Per production building, a cell or a resource. New villagers sent to a resource start gathering. | Trained unit walks to the rally point. |
| Repair | Villagers repair damaged buildings for a share of the cost. | HP rises and resources fall at the expected rate. |
| Auto-villager | Town Center keeps one villager queued while the toggle is on; turns off at population 40. | Queue is refilled; toggle flips at 40. |
| Economy planner | `SetEconomyTargets(food, wood, gold, stone)` command. Each tick (staggered) idle and newly trained villagers are assigned to the resource with the biggest gap between target and actual share. A manual gather order exempts that villager until it goes idle. Presets are data, not code. | Property test: with 20 idle villagers and targets 45/35/15/5 the assignment reaches 9/7/3/1. Manual orders are not overridden. |

### 3.3 Ages and research

- **Research as a building queue item.** The Town Center, Forge, camps, Castle and Monastery
  queue research the same way they queue units. One research per building at a time, as in
  AoE2.
- **Age-up** is a Town Center research with the costs, times and building requirements from
  section 6. Buildings, units and techs carry a minimum age in the data tables.
- **Tech effects** are data rows: target (unit line, tag or all), stat, operation (add,
  multiply by a fixed-point ratio), value. About 30 rows as listed in section 6. Effects of
  the Woodlanders civ bonuses use the same mechanism, applied at match start or at an age.
- **Unit line upgrades** (Militia to Long Swordsman and so on) happen **automatically on
  age-up** in this plan, so the upgrade count stays at about 30 (see open decision 2).
- Tests: age-up is rejected without the two buildings; completes after exactly the listed
  time; unlocks the next tier. Each tech row gets a generated test that the stat changes by
  the listed amount. Researching the same tech twice is rejected.

### 3.4 Combat

| Feature | Notes | Tests |
|---|---|---|
| Melee and pierce armour | `damage = max(1, attack − armour of the attack's type)` plus bonus damage by tag. | Table-driven test of every counter pair in section 5.1: the counter wins a 5 vs 5 fight on open ground. |
| Bonus tags | Anti-cavalry, anti-archer, anti-building, anti-siege, as data. | Spearman vs Knight and Ram vs building damage match the tables. |
| Ranged attacks | Projectiles always hit (section 5.3). Damage lands after a flight delay computed from distance at fire time, so the target can die first and the shot is wasted but never misses a living target. | A shot fired at a target that dies in flight does no damage to anyone else. |
| Minimum range | Mangonel and trebuchet. | Units inside minimum range are not targeted. |
| Area damage | Mangonel splash with friendly fire; radius in fixed point. | Splash hits every unit in radius once, in id order. |
| Unit separation | Soft collision: units push apart by a fixed-point amount per tick, never into blocked cells. Units still do not block pathfinding. | No two units share a position after 50 ticks of a 30-unit clump; separation never moves a unit into a building. |
| Formations | A group move assigns each unit an offset slot around the target; slowest unit sets the group speed. | Group arrives with no unit more than N cells from its slot. |
| Flow fields | Groups of more than 8 units moving to one goal share a flow field instead of running 9+ A* searches (section 12.4). Single units keep A*. | Flow field path length is within 10% of A* for the same start and goal on 50 seeds. |
| Hold-ground stance | One toggle per unit (section 5.3). | Units on hold ground attack only targets in range. |
| Auto-engage priorities | Military targets first, then villagers, then buildings; ties by distance then id. | Mixed enemy group: target order matches the rule. |
| Buildings that shoot | Town Center, towers and castle fire arrows; more arrows per garrisoned unit. | Garrisoning 5 villagers raises arrow count by the listed amount. |
| Garrison | Town Center, towers, castle. Garrisoned villagers are safe; ungarrison puts them on free cells. | Garrisoned units are removed from the map and come back on eject. |
| Siege | Battering Ram (melee vs buildings, pierce-immune), Mangonel, Trebuchet. Trebuchets do not pack or unpack in this plan (open decision 3). | Ram takes 1 damage from arrows; trebuchet outranges a castle. |
| Monks | Conversion: a channel of fixed length, then ownership changes. Healing of friendly units. Buildings cannot be converted in milestone 1. | Conversion completes after the listed ticks and the unit switches owner and goes idle. |
| Conquest victory | A player loses when they have no Town Center, Castle or military production building (section 3). Houses and walls do not count. | Match ends on the tick the last qualifying building dies. |

### 3.5 Map, fog and defences

- **Open template, 64×64, 1v1.** Scattered forest, open centre, mirrored resource placement
  for fairness. Each player gets the standard start from section 9: Town Center, 6 villagers,
  1 scout, 4 sheep, 2 boar, berries, a forest within 10 tiles, one gold and one stone pile
  within 15 tiles.
  - Property test over 200 seeds: every start meets the section 9 distances, every resource is
    reachable from its Town Center, and the two players' resource totals within 20 tiles
    differ by under 5%.
- **Fog of war in the simulation.** A per-player visibility grid updated from line of sight
  every few ticks, with explored and visible bits. It lives in the sim (not in Unity) because
  the AI must read the same fog the player does, and because Quantum will need it in
  milestone 3. Commands that target an entity a player cannot see are rejected.
  - Tests: a unit becomes visible on the tick it enters line of sight; explored cells stay
    explored; an attack command on a hidden unit is rejected.
- **Walls and gates.** A `BuildWall(from, to)` command places a straight line of segments
  (section 7's drag gesture), charging per segment and skipping blocked cells. Gates are
  wall segments that let the owner's units through: they occupy the cell for pathfinding of
  other players only, which needs a per-player passability check in the pathfinder.
  - Tests: enemy A* routes around a closed wall; the owner routes through its gate.
- **Towers.** Built like any building, shoot like a Town Center.

### 3.6 Standard AI

- Lives in a new `Oduncu.Sim.AI` folder inside the simulation assembly, with no access to
  anything a player cannot see: it reads its own state and its fogged view, and outputs
  `Command`s through the same queue as touch input (section 11).
- Two layers, as in section 11: a scripted build order per strategy, and a utility layer that
  re-chooses between Boom, Rush, Defend and Siege every 30 seconds from what it has scouted.
- Standard difficulty is fair: no extra resources, no map knowledge.
- Tests:
  - AI vs AI on 20 seeds finishes with a conquest victory, without exceptions, inside 25
    minutes of simulated time. Median match length is reported, which doubles as the
    15-minute balance check before any human plays.
  - AI vs AI is deterministic: the same seed gives the same winner and final hash, and a
    replay of the recorded commands reproduces it.
  - The AI never issues a command the simulation rejects for using hidden information.
  - An idle AI (no opponent pressure) reaches Imperial Age within the timings of section 3.

### 3.7 Determinism

- The scripted scenario in `Scenarios` is replaced by an AI vs AI match on the Open map, so
  the 10,000-tick determinism and replay tests exercise every new system.
- The reference hash in the README changes in every PR that changes rules, as it does now.
- A new test runs the determinism match from 10 seeds, not one.

---

## 4. Unity work

All of this sits in `Assets/Game` and only reads the simulation and submits commands.

- **Touch input**, replacing the IMGUI buttons: every gesture in the section 7 table, built on
  the Input System package with `EnhancedTouch` (adds a package dependency). One-finger drag
  pans, long-press-and-drag box-selects, double-tap selects all of a type on screen, pinch
  snaps between three zoom levels.
- **HUD in UI Toolkit**: resource bar with population, minimap with fog, selection panel,
  command card (8 buttons, 48 dp minimum, second page for the build menu grouped Economy /
  Military / Defence), quick-select buttons (Idle villager, Army, Town Center), three control
  groups, the alert stack, and the economy planner slider with presets.
- **Placement previews** for buildings and the wall drag, with valid and invalid tinting from
  the same footprint check the simulation uses.
- **Fog rendering** from the simulation's visibility grid.
- **Placeholder art**: low-poly primitives per unit line and building, player-colour outlines,
  1.5× unit scale (section 7), simple projectile and death effects. URP and GPU instancing as
  in section 12.4.
- **Pause** for single player (the runner stops stepping). Fast speed is out of scope.
- **Match flow**: main menu with "Skirmish vs Standard AI", in-match menu, victory and defeat
  screens.
- **Debug overlay** (development builds): tick time, entity count, state hash.

---

## 5. What only Unity or a phone can verify

CI proves the rules are right and deterministic. It cannot prove any of these:

| Check | How | Pass mark |
|---|---|---|
| Milestone 0 baseline hash on two phones | Run `DeterminismProbe` on two Android devices of different chipsets. | Both print the README hash. **Entry gate for milestone 1.** |
| Game scripts compile against real Unity | Open the project in Unity 6000.0.58f1. | No compile errors. (Tracked in the *Verify Unity scripts compile* thread.) |
| Determinism on IL2CPP / ARM64 after the new systems | Re-run the probe with the milestone 1 determinism match on two devices. | Hashes match the headless suite. |
| Simulation tick time | Debug overlay on a 2020 mid-range phone at full population. | Under 4 ms per tick (section 12.4). |
| Frame rate and heat | 15-minute match on a mid-range and a 2018 low-end phone. | 60 fps mid-range, 30 fps floor low-end, Adaptive Performance kicks in under thermal warning. |
| Touch feel | Internal playtests of every gesture in section 7, especially box-select vs pan and tapping small units in a crowd. | Testers can do each without mis-taps after five minutes of play. |
| Readability | Screenshots at each zoom level on a 6-inch phone. | Every unit line and building identifiable at a glance (pillar 3). |
| UI layout | Phones with notches, 16:9 and 20:9 aspect ratios, and a tablet. | Nothing under a notch or outside the safe area; buttons at least 48 dp. |
| Match length | Internal playtests vs Standard AI. | Median match 12 to 18 minutes (milestone 1 exit criterion). |
| Memory | Memory Profiler after a full match. | Under 400 MB resident; no growth over the match. |

---

## 6. Order of work

Each step is one or a few PRs, each with tests, the README hash updated when rules change,
and CI green. Steps 2 to 6 can overlap once step 1 is in.

| Step | Contents | Gate to move on |
|---|---|---|
| 0 | On-device baseline hash; Unity compile check. | Both rows of section 5 marked as entry gates pass. |
| 1 | Foundations (3.1): data tables, costs, per-player stats, spatial index, zero allocations, banned-API test, benchmark, log v2. | Existing 29 tests pass unchanged in meaning; benchmark numbers recorded. |
| 2 | Economy (3.2). | Economy tests pass; Unity build shows four resources and houses with the old UI. |
| 3 | Combat (3.4) for Feudal units, then Castle and Imperial lines, siege, monks. | Counter-pair table test passes for every pair. |
| 4 | Ages and research (3.3). | Every tech row test passes. |
| 5 | Map, fog, walls (3.5). | 200-seed map fairness test passes. |
| 6 | Standard AI (3.6), then determinism switched to AI vs AI (3.7). | 20-seed AI vs AI test passes; median length reported. |
| 7 | Touch UI, HUD, placeholder art, match flow (section 4). Starts in parallel with step 2. | Section 5 checks pass on device. |
| 8 | Playtests and tuning of the data tables. | Milestone 1 exit criteria met. |

Section 14 of the design document gives milestone 1 twelve weeks (weeks 8 to 20) for two
engineers. One reasonable split is one engineer on steps 1 to 6 and one on step 7, meeting at
step 8; the steps are listed by dependency, not with dates, because the design document asks
for a re-plan after milestone 0 and that re-plan should use the benchmark from step 1.

---

## 7. Open decisions

Defaults are what this plan assumes; each can change without reworking the rest.

1. **Which civilization for the slice.** Default: Woodlanders, because its bonuses (cheaper
   lumber camps, faster wood, archer range) exercise the economy and the tech-effect system
   without needing mechanics that are out of scope.
2. **Unit line upgrades.** Default: automatic on age-up. The alternative is AoE2-style paid
   research, which adds about 15 upgrades and a lot of command-card buttons.
3. **Trebuchet packing.** Default: no packing. Packing is micro, and pillar 1 argues against it.
4. **Fog for the AI.** Default: Standard AI reads only its fogged view. Reading full state is
   simpler to build but breaks the "fair, no cheats" promise of section 11.
5. **Input System package.** Default: adopt it now for multi-touch gestures, instead of
   extending the legacy `Input` calls in `TouchInput.cs`.
