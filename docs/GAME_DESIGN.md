# Oduncu — Game Design Document (v0.2)

*Working title. "Oduncu" is Turkish for woodcutter.*

A real-time strategy game for phones and tablets with the core loop of Age of Empires II:
gather four resources with villagers, advance through four ages, build a counter-based army,
and destroy the enemy. Every mechanic below is either kept from AoE2, simplified for touch,
or cut, and the reason is stated each time.

---

## 1. Vision and pillars

**One-line pitch.** Age of Empires II's economy-to-army loop, playable in a 15-minute match
with one thumb.

**Pillars** (used to settle every design argument):

1. **Macro over micro.** The player's skill is expressed through economy, timing and army
   composition, not through click speed. Any mechanic that rewards fast fingers on a
   touchscreen is redesigned or cut.
2. **A match fits in a commute.** Target match length is 12 to 18 minutes. Single player is
   pausable and saveable at any moment.
3. **Readable at phone size.** Every unit, building and resource must be identifiable at a
   glance on a 6-inch screen. If it isn't, it doesn't ship.
4. **No pay-to-win.** Nothing sold for money affects match outcomes.

**Not goals for v1:** naval combat, more than 4 players per match, modding, cross-play with
desktop, user-generated maps.

---

## 2. What is kept, simplified, or cut from AoE2

| AoE2 feature | Decision | Mobile adaptation |
|---|---|---|
| Four resources (food, wood, gold, stone) | Keep | Unchanged. It is the identity of the game. |
| Villagers gather and build | Keep | Auto-reseeding farms, auto-return to work after building, "economy ratio" planner (section 4.3). |
| Four ages | Keep | Age-up costs and research times cut by roughly half to fit the match length. |
| Tech tree (~100 techs) | Simplify | About 30 upgrades. Blacksmith, University and Castle-age unique techs merged into one "Forge" building. |
| Unit counter system | Keep | Same rock-paper-scissors (section 5.1), 11 unit lines instead of ~40. |
| Civilizations with bonuses and unique units | Keep | Launch with 4 civs; more added post-launch. |
| 200 population cap | Simplify | Cap of 75 per player. Keeps battles legible and the CPU budget sane. |
| Large maps (120 to 240 tiles) | Simplify | 64x64 to 96x96 tiles. Enemies are reachable within a minute of walking. |
| Walls, gates, towers, castles | Keep | Walls are placed as straight drags with snap-to-grid; gates auto-insert where a wall crosses a road. |
| Monks and relics | Simplify | Monks convert units; relics are cut from v1. |
| Market and trade | Simplify | Resource exchange at the market stays; trade carts are cut. |
| Siege (ram, mangonel, trebuchet) | Keep | Three siege units. |
| Navy, docks, fishing | Cut | Land-only maps in v1. Removes a whole UI layer and pathfinding domain. |
| Formations, stances, patrol | Simplify | Units default to a sensible formation. Stance is one toggle: "hold ground" on or off. |
| Hotkeys and control groups | Replace | On-screen quick-select buttons and three control-group slots (section 7). |
| Game speed 1.7x | Replace | Fixed single speed tuned for touch; "Fast" is a single-player-only option. |

---

## 3. Core loop

```
 explore  →  gather  →  build / research  →  age up  →  train army  →  fight
    ↑                                                                   │
    └─────────────────── expand (new camps, more villagers) ◄───────────┘
```

A standard skirmish:

| Phase | Clock | What the player is doing |
|---|---|---|
| Dark Age | 0:00 to 3:00 | 6 starting villagers, scout the map, queue villagers, first lumber camp and mill. |
| Feudal Age | 3:00 to 7:00 | Barracks, range, stable. First skirmishes, walls, scouting raids. |
| Castle Age | 7:00 to 12:00 | Knights, crossbows, siege, a castle. Main army fights. |
| Imperial Age | 12:00 to end | Final upgrades, trebuchets, push to destroy. |

Win condition: destroy all enemy Town Centers, Castles and military production buildings
(the AoE2 "conquest" rule, minus houses and walls to avoid hunt-the-last-building endings).
Alternative mode: hold the central Wonder site for 3 minutes ("King of the Hill") for shorter games.

---

## 4. Economy

### 4.1 Resources

| Resource | Sources | Drop-off | Notes |
|---|---|---|---|
| Food | Berries, sheep, deer, boar, farms | Mill, Town Center | Farms auto-reseed from wood; no manual reseeding. |
| Wood | Trees | Lumber Camp, Town Center | Most plentiful resource; defines the game's name. |
| Gold | Gold mines, market | Mining Camp, Town Center | Scarce; drives map control fights. |
| Stone | Stone mines | Mining Camp, Town Center | Walls, towers, castles only. |

### 4.2 Villagers

- Cost 50 food, train in 20 s at the Town Center. Town Center auto-queues villagers while an
  "auto-villager" toggle is on (default on at Dark Age, off once population is 40).
- Tap a resource with villagers selected to gather it. A villager that finishes a building
  returns to its previous job automatically.
- A newly trained villager goes to whatever the economy planner (4.3) says is most under-staffed.
- Idle villagers raise a badge on the Idle button; tapping it cycles through them.

### 4.3 Economy planner (the main mobile invention)

Instead of dragging individual villagers between resources, the player sets a target
distribution with a four-segment slider (e.g. Food 45 / Wood 35 / Gold 15 / Stone 5).
New villagers and idle villagers are assigned to close the largest gap. The player can
still micromanage any villager manually, which overrides the planner for that unit until
it goes idle again.

Presets per age ("Boom", "Rush", "Siege") give newcomers a build-order feel without
memorisation.

### 4.4 Housing

Each house supports 5 population; the Town Center supports 10. Population cap 75.
The build button for a house flashes when the player is within 5 population of the cap.

---

## 5. Military

### 5.1 Counter system

```
   Spearman  ──beats──►  Cavalry  ──beats──►  Archer  ──beats──►  Infantry
      ▲                                           │                   │
      └───────────────── beaten by ───────────────┘◄──────────────────┘

   Skirmisher beats Archer; loses to everything else in melee.
   Cavalry beats Siege and Monks.   Siege beats Buildings.   Infantry beats Spearmen and Buildings.
```

Each unit has one explicit "strong against" and "weak against" shown as icons on its card,
so the counter triangle is learnable without a wiki.

### 5.2 Unit roster

| Line | Feudal | Castle | Imperial | Building | Role |
|---|---|---|---|---|---|
| Infantry | Militia | Long Swordsman | Champion | Barracks | Beats spears and buildings |
| Spear | Spearman | Pikeman | Halberdier | Barracks | Beats cavalry |
| Archer | Archer | Crossbowman | Arbalester | Archery Range | Beats infantry |
| Skirmisher | Skirmisher | Elite Skirmisher | — | Archery Range | Beats archers |
| Light cavalry | Scout | Light Cavalry | Hussar | Stable | Scouting, raiding, kills siege and monks |
| Heavy cavalry | — | Knight | Cavalier | Stable | Beats archers, general purpose |
| Ram | — | Battering Ram | Capped Ram | Siege Workshop | Beats buildings |
| Mangonel | — | Mangonel | Onager | Siege Workshop | Area damage vs. massed units |
| Trebuchet | — | — | Trebuchet | Castle | Long-range vs. buildings |
| Monk | — | Monk | — | Monastery | Converts units, heals |
| Unique unit | — | Unique | Elite Unique | Castle | One per civilization |

Eleven lines, roughly a quarter of AoE2's roster. Enough for every counter relationship to
exist with no redundant choices.

### 5.3 Combat rules

- Damage = attack − armour, minimum 1, with separate melee and pierce armour (as in AoE2).
- Bonus damage via tags (anti-cavalry, anti-archer, anti-building) rather than per-unit tables.
- Units auto-engage enemies within line of sight unless "hold ground" is set.
- Ranged units auto-kite only if a civilization bonus grants it; otherwise no kiting.
  This is deliberate: kiting is a micro skill that does not transfer to touch.
- Arrows and projectiles always hit (no dodging) to make outcomes depend on composition.

### 5.4 Buildings

| Building | Age | Purpose |
|---|---|---|
| Town Center | Dark | Villagers, age-up, drop-off, garrison and arrows |
| House | Dark | +5 population |
| Mill, Lumber Camp, Mining Camp | Dark | Drop-off; gather-rate upgrades |
| Farm | Dark | Infinite food at slow rate; auto-reseeds |
| Barracks | Dark | Infantry, spears |
| Archery Range, Stable | Feudal | Archers and skirmishers; cavalry |
| Forge | Feudal | All attack, armour and economy upgrades (merged Blacksmith + University) |
| Market | Feudal | Buy and sell resources at a moving price |
| Palisade / Stone Wall, Gate, Tower | Feudal / Castle | Static defence |
| Monastery | Castle | Monks |
| Siege Workshop | Castle | Rams, mangonels |
| Castle | Castle | Unique unit, trebuchets, unique techs, strong defence |
| Wonder | Imperial | Victory timer in Wonder mode only |

Seventeen building types versus AoE2's ~30. Dock, Outpost, Feitoria, Krepost and similar are cut.

---

## 6. Ages and research

| Age | Cost | Requirement | Time |
|---|---|---|---|
| Feudal | 400 food | 2 Dark Age buildings | 60 s |
| Castle | 600 food, 150 gold | 2 Feudal buildings | 75 s |
| Imperial | 800 food, 500 gold | Castle or 2 Castle-age buildings | 90 s |

Roughly 30 researchable upgrades, all at the Forge, Town Center, economy camps or Castle:

- Forge: 3 tiers each of melee attack, archer attack, infantry armour, cavalry armour, archer armour (15).
- Economy: 2 tiers each of wood, food, gold, stone gather rate (8).
- Town Center: Loom, Wheelbarrow, Hand Cart, Town Watch (4).
- Castle: 2 unique techs per civilization (2).
- Monastery: Sanctity, Fervor (2).

Each upgrade card states its effect in one line of plain language ("+1 attack for all infantry").

---

## 7. Touch controls and UI

Landscape orientation only. The UI is split into fixed zones:

```
┌────────────────────────────────────────────────────────────┐
│ [resources: F W G S  pop]              [minimap] [menu]    │
│                                                            │
│                                                            │
│                   game view                                │
│  [Idle vil]                                                │
│  [Army]                                                    │
│  [TC]                                                      │
│  [G1][G2][G3]                                              │
│                                                            │
│ ┌──────────────┐  ┌──────────────────────────────────────┐ │
│ │ selection    │  │ command card (max 8 context buttons) │ │
│ └──────────────┘  └──────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────┘
```

**Gestures**

| Gesture | Action |
|---|---|
| Tap unit or building | Select it |
| Tap terrain with selection | Move (military) / nothing (buildings) |
| Tap enemy with selection | Attack |
| Tap resource with villagers | Gather |
| Tap friendly building with villagers | Repair or garrison |
| Double-tap unit | Select all of that type on screen |
| Long-press and drag on terrain | Box select (single-finger drag without hold pans the camera) |
| Two-finger drag | Pan camera |
| Pinch | Zoom (3 fixed levels: close, normal, overview) |
| Tap minimap | Jump camera |
| Long-press G1 to G3 | Assign current selection to that control group |

**Quick-select buttons** (left edge): Idle villager, All military, Town Center.
These replace the hotkeys a desktop player relies on.

**Command card**: context-sensitive, never more than 8 buttons, each at least 48 dp.
Build menu is a second page of the command card, grouped by Economy / Military / Defence.

**Alerts**: "Under attack" flashes the minimap region and offers a one-tap jump.
"Idle villagers", "Housed", "Research complete" appear as a dismissible stack top-left.

**Readability**: player colour outlines on all units, 1.5x unit scale relative to buildings
compared with AoE2, and a high-contrast ground palette per biome.

---

## 8. Civilizations (launch set of four)

| Civilization | Economic bonus | Military bonus | Unique unit | Unique techs |
|---|---|---|---|---|
| Woodlanders | Lumber camps 50% cheaper; wood gathered 15% faster | Archers +1 range in Castle Age | Longbowman (range) | Yeomen (+1 archer range), Warwolf (trebuchet splash) |
| Steppe Riders | Herdables give 50% more food | Light cavalry trains 30% faster | Mangudai (mounted archer) | Nomads (houses not needed), Drill (siege move faster) |
| Highland Clans | Stone mining 20% faster | Infantry +1 pierce armour per age | Woad Raider (fast infantry) | Stronghold (castles fire faster), Furor Celtica (siege +40% HP) |
| River Kingdoms | Farms 25% cheaper | Knights +2 attack vs. archers | Cataphract (anti-infantry cavalry) | Logistica (cataphract trample), Greek Fire (tower range) |

Names and bonuses are placeholders modelled on AoE2 archetypes and should be replaced with
original flavour before launch.

---

## 9. Maps

- Square grid, 64x64 (1v1) or 96x96 (2v2 and 4-player free-for-all).
- Procedurally generated from three templates in v1:
  - **Open** (Arabia-like): scattered forest, open centre, raiding favoured.
  - **Walled** (Arena-like): each player starts inside stone walls; booming favoured.
  - **Forest** (Black Forest-like): narrow forest lanes; siege and chokepoints favoured.
- Standard start for every player: Town Center, 6 villagers, 1 scout, 4 sheep nearby,
  2 boar, berries, a forest within 10 tiles, one gold and one stone pile within 15 tiles.
- Fog of war with explored / visible / hidden states, as in AoE2.

---

## 10. Game modes

| Mode | Players | Purpose |
|---|---|---|
| Skirmish vs AI | 1 vs 1 to 3 AI | Core offline mode; pausable, saveable |
| Campaign | 1 | 12 scripted missions of 5 to 10 minutes each; doubles as the tutorial |
| Ranked 1v1 | 2 | Real-time online, ELO ladder |
| Casual 2v2 / FFA | 4 | Real-time online, unranked |
| Daily challenge | 1 | Fixed seed and civ, leaderboard on time-to-win |

Campaign missions each introduce one mechanic: gathering, housing, ageing up, the counter
triangle, walls, siege, monks, trade, castles, trebuchets, a full match, and a final
"all tools" mission.

---

## 11. AI opponent

- Scripted build orders per civilization and difficulty, with a utility layer choosing
  between Boom / Rush / Defend / Siege every 30 seconds based on scouting information.
- Difficulties: Easy (slow build, no attacks before 8:00), Standard (fair, no cheats),
  Hard (fair, perfect build order), Brutal (+25% gather rate, explicitly labelled as a cheat).
- The AI uses the same command interface as the player (no direct state mutation), so
  replays and determinism hold.

---

## 12. Technical plan

### 12.1 Engine

**Decision: Unity 6 (LTS) with C#.** Chosen over Godot 4 and JavaScript stacks for four reasons:

1. **Photon Quantum** provides a fixed-point, deterministic simulation with prediction and
   rollback, built for exactly this kind of lockstep game. It removes the riskiest piece of
   custom engineering from the plan.
2. **Burst and the Jobs system** make flow-field pathfinding, fog-of-war updates and
   large-group movement cheap on mobile CPUs.
3. **Mature mobile tooling**: Profiler, Memory Profiler, Frame Debugger, Adaptive
   Performance for thermal throttling, Addressables for asset streaming, IL2CPP builds.
4. **Hiring and ecosystem**: the largest pool of mobile game engineers and RTS reference
   material.

Costs accepted: Unity is free below the Personal revenue threshold and paid per seat above
it; Quantum is free at low concurrency and paid beyond. Both are budgeted as known line
items rather than risks.

Options considered and rejected: Godot 4 (C# mobile export too immature for the
simulation), TypeScript with PixiJS or Phaser in a Capacitor WebView (viable only with 2D
rendering, loses thermal and performance APIs), Cocos Creator (native and capable, but
smaller English-language community and no deterministic networking framework).

### 12.2 Architecture

```
┌──────────────────────────────────────────────────────────┐
│  Presentation (Unity scenes, input, audio, UI Toolkit)   │
│  - reads sim state each frame, interpolates between ticks│
│  - turns gestures into Commands                          │
├──────────────────────────────────────────────────────────┤
│  Simulation (Quantum: pure C#, fixed-point, no UnityEngine)│
│  - fixed timestep 10 ticks/s                             │
│  - entities, pathfinding, combat, economy, fog, tech     │
│  - input: ordered list of Commands per tick              │
│  - output: deterministic state; hashable for desync check│
├──────────────────────────────────────────────────────────┤
│  Data (ScriptableObject assets generated from CSV/JSON:  │
│        units, buildings, techs, civs, map templates)     │
└──────────────────────────────────────────────────────────┘
```

Rules that keep the simulation portable and testable:

- The simulation assembly has no reference to `UnityEngine`. It is a plain C# class
  library compiled into the Unity project and into a separate .NET test project.
- All simulation maths uses the `FP` fixed-point type in the simulation assembly, whose
  API mirrors Quantum's `FP` so the two can be swapped at M3. `float` and `double` are
  banned in the simulation assembly by code review now and by an analyzer rule from M1.
- Presentation never mutates simulation state; it only submits Commands.
- The AI submits Commands through the same interface as a human player.

Quantum is not needed until multiplayer, so M0 to M2 run the hand-written simulation and
M3 adopts Quantum behind the same boundary. If Quantum is later dropped (cost, licensing),
the hand-written simulation stays in place without touching presentation.

### 12.3 Determinism and multiplayer

- Lockstep with prediction and rollback via Quantum: each client runs the full
  simulation; only Commands cross the network through Photon's relay. This is the same
  model AoE2 used, with rollback added so the local player's commands feel instant.
- State hash exchanged every second to detect desync; a desync ends the match for the
  offending client with a replay file attached to telemetry.
- Reconnect: Quantum snapshots let a dropped client rejoin mid-match within 60 seconds.
- Replays and the daily challenge come for free from the command log.
- Matchmaking, accounts and ladders: Unity Gaming Services (Authentication, Lobby,
  Leaderboards) or a small HTTP service, decided at M3. No custom game logic runs
  server-side in v1.

### 12.4 Pathfinding and performance

- Grid A* for single units; flow fields for groups of more than 8 units. Both run inside
  the simulation in fixed-point; Burst-compiled jobs are used only where Quantum allows
  deterministic parallelism (field generation, fog recomputation).
- Budget: 4 players × 75 units + ~400 buildings and trees at 10 ticks/s on a 2020
  mid-range phone with the simulation under 4 ms per tick.
- Rendering: URP, GPU instancing for units and trees, one draw call per unit type where
  possible, 60 fps on mid-range, 30 fps floor on 2018 low-end devices.
- Thermal: Adaptive Performance drops to 30 fps and halves shadow resolution under
  thermal warning; the simulation tick rate is never reduced.
- Memory: Addressables load one civilization's assets per match; target under 400 MB
  resident on iOS.

### 12.5 Art direction

Low-poly 3D models with a fixed 3/4 isometric camera and flat-shaded textures in URP.
One model and one animation set per unit serves all facings, which is far cheaper than
AoE2-style pre-rendered sprites that need 8 directions × every animation. Zoom levels
work without re-authoring. Buildings get three damage states. Shadows are a single
blob-shadow decal on low-end devices.

### 12.6 Repository layout (proposed)

```
oduncu/
  docs/                     design docs, this file
  Oduncu.Unity/             Unity project
    Assets/
      Sim/                  simulation assembly (asmdef with noEngineReferences)
      Sim.Tests/            NUnit tests, run by Unity's Test Runner and headless
      Game/                 presentation: scripts, editor bootstrap, build script, scenes
      Data/                 (M1) ScriptableObjects and CSV sources for units, techs, civs
      Art/                  (M1) models, animations, materials, audio
    Packages/
    ProjectSettings/
  Oduncu.Sim/               .NET library project compiling Assets/Sim sources headless
  Oduncu.Sim.Tests/         .NET test project compiling Assets/Sim.Tests sources headless
  tools/                    (M1) map generator CLI, balance spreadsheet exporter
  .github/workflows/        CI: headless tests on every push, Android build when licensed
```

Unity version is pinned in `ProjectSettings/ProjectVersion.txt`; the repo uses Git LFS
for binary assets and a `.gitignore` for `Library/`, `Temp/`, `Logs/` and `Build/`.

---

## 13. Monetization

**Recommendation: premium with a free start.** The download is free and includes the
campaign's first three missions, two civilizations and skirmish vs AI. A single one-time
purchase unlocks everything. Later civilization packs and cosmetic skins are optional
purchases. No ads, no energy, no boosts.

A free-to-play live-service model is possible but is a different game with different
retention mechanics, and it is incompatible with pillar 4.

---

## 14. Milestones

Estimates assume a small team: two engineers, one artist, part-time designer and audio.
They are rough and should be re-planned after milestone 0.

| Milestone | Target | Exit criteria |
|---|---|---|
| M0 Prototype | Week 8 | Unity project set up with Git LFS and CI build for Android. Headless sim with villagers, one resource, one building, one unit, A* pathing, combat. Tap-to-move on a phone. Determinism test passes 10,000 ticks on two devices with identical hashes. **Scaffolded: sim, tests, Unity shell and CI are in the repository; the on-device hash check is the remaining exit item.** |
| M1 Vertical slice | Week 20 | All four ages, full unit roster for one civ, one map template, Standard AI, complete touch UI. Internal playtests reach a 15-minute match. |
| M2 Content | Week 30 | Four civs, three map templates, 12 campaign missions, Easy to Brutal AI, save and load, tutorial, audio. |
| M3 Multiplayer | Week 42 | Photon Quantum adopted behind the simulation boundary. Relay, 1v1 and 2v2, reconnect, replays, desync telemetry. Closed beta of 200 players. |
| M4 Soft launch | Week 50 | Store listings, analytics, crash reporting, localisation (English, Turkish, plus 3). Release in two test countries. |

---

## 15. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Touch controls feel clumsy in battle | Fatal to the core experience | M0 and M1 are judged on touch feel first; auto-engage and formations reduce the need for micro. |
| Matches run too long for mobile | Retention | Tune costs and map size against a 15-minute target in every playtest; Wonder and King of the Hill modes as shorter alternatives. |
| Simulation desync in multiplayer | Unplayable online | Fixed-point maths, headless determinism tests from M0, hash checks every second, replays for repro. |
| Art cost of a full roster | Schedule | Low-poly 3D with shared rigs; 11 unit lines is a hard cap for v1. |
| Battery and heat | Reviews | Tick-rate and frame-rate caps, no per-frame allocations in the sim, profiling gate at each milestone. |
| AI too weak or too obviously cheating | Single-player retention | Scripted build orders plus a utility layer; only Brutal cheats and says so. |
| Dependence on Photon Quantum (pricing, licence changes) | Multiplayer cost and schedule | Simulation is isolated behind a Command interface with no `UnityEngine` reference, so a hand-written fixed-point sim can replace Quantum; the swap is budgeted at 6 weeks if ever needed. |

---

## 16. Open decisions

These need an owner's call before M0 starts. Defaults are what this document assumes.

1. ~~Engine~~: decided, Unity 6 with Photon Quantum (section 12.1).
2. **Art style**: low-poly 3D with fixed camera (default) or 2D pre-rendered sprites.
3. **Monetization**: premium unlock (default) or free-to-play with cosmetics only.
4. **Multiplayer timing**: at soft launch (default) or a later update to ship single player sooner.
5. **Theme**: original fantasy-medieval setting (default) or a historical setting with real civilizations.
6. **Team size**: the milestone plan assumes two engineers and one artist.
