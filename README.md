# Oduncu

A mobile real-time strategy game with the economy-to-army loop of Age of Empires II,
redesigned for touch and 15-minute matches. Built with Unity 6 and C#.

- Design document: [docs/GAME_DESIGN.md](docs/GAME_DESIGN.md)
- Current milestone: **M1 vertical slice**, in progress (plan: [docs/MILESTONE_1_PLAN.md](docs/MILESTONE_1_PLAN.md))

## Layout

| Path | What it is |
|---|---|
| `Oduncu.Unity/` | The Unity project. Open this folder in Unity Hub. |
| `Oduncu.Unity/Assets/Sim/` | The deterministic simulation. Pure C#, fixed-point maths, no `UnityEngine` reference. |
| `Oduncu.Unity/Assets/Data/` | Balance tables (units, buildings, techs, ages) as CSV. The source of truth for every number. |
| `Oduncu.Unity/Assets/Sim/Generated/` | `Defs.g.cs`, generated from the CSV files. Never edit by hand. |
| `tools/DataGen/` | The generator that turns the CSV files into `Defs.g.cs`. |
| `Oduncu.Unity/Assets/Sim.Tests/` | NUnit tests for the simulation. Run in Unity's Test Runner or headless (below). |
| `Oduncu.Unity/Assets/Game/` | Presentation: tick runner, primitive renderer, touch input, editor bootstrap and build script. |
| `Oduncu.Sim/`, `Oduncu.Sim.Tests/` | .NET projects that compile the same sources outside Unity for CI, servers and tools. |
| `.github/workflows/ci.yml` | Runs the headless tests on every push; builds an Android APK when Unity licence secrets are set. |

## Running the simulation tests without Unity

Requires the .NET 8 SDK.

```
dotnet test Oduncu.Sim.Tests/Oduncu.Sim.Tests.csproj
```

The suite includes the determinism check (the scripted scenario runs for 10,000 ticks twice
and from a replayed command log, and every checkpoint hash must match), a check that the
README hash below is current, a scan of `Assets/Sim` for banned APIs, and a check that
steady-state ticks allocate nothing on the managed heap.

A tick-time benchmark at full 1v1 population runs as its own CI step and reports to the job
summary. To run it locally:

```
dotnet test Oduncu.Sim.Tests/Oduncu.Sim.Tests.csproj -c Release --filter TestCategory=Benchmark --logger "console;verbosity=detailed"
```

## Changing balance data

Edit the CSV files in `Oduncu.Unity/Assets/Data/` (each file's header comment explains its
units), then regenerate and commit both:

```
dotnet run --project tools/DataGen
```

CI runs the generator with `-check` and fails if `Defs.g.cs` does not match the CSV files.
Ids in the tables are stored in command logs, so never renumber or reuse one.

## Opening in Unity

1. Install any Unity 6000.0 LTS editor with the Android Build Support module.
2. Open `Oduncu.Unity/` from Unity Hub. On first open an editor script creates
   `Assets/Game/Scenes/Main.unity` and adds it to the build settings.
3. Press Play. Tap one of your blue villagers, then tap a tree to gather, the ground to
   move, or use the bottom buttons to build a barracks and train units.
4. Window > General > Test Runner > EditMode runs the same tests inside Unity.

To build for Android from the command line:

```
<Unity editor> -batchmode -quit -projectPath Oduncu.Unity -executeMethod Oduncu.Game.Editor.BuildScript.BuildAndroid
```

## Determinism probe on a device

Enable `Run On Start` on the `DeterminismProbe` component of the `Game` object (or launch
with `-determinism`) and read the `DETERMINISM_HASH` line from the device log. Two devices
and the headless test suite must print the same value. The reference value for the current
simulation code, seed 4242 and 10,000 ticks is:

```
DETERMINISM_HASH 7FCEA48251A5A53C
```

Any change to simulation rules changes this value; update it in the same commit (a test
fails until you do). The milestone 0 baseline, for the on-device check on commit `3f80d6d`,
was `8A5992FA1F434A07`.

## Rules for simulation code

- No `float`, `double`, `System.Random`, `DateTime` or `UnityEngine` inside `Assets/Sim`
  (enforced by `BannedApiTests`; a line that truly needs one carries a `banned-api-ok` comment).
- No allocations on the tick path: use `EntityFilter` queries instead of lambdas and reuse
  buffers. Entity objects are pooled, so keep ids, not `Entity` references, across ticks.
- Iterate entities by index in id order; never iterate a `Dictionary` or `HashSet` where the order affects state.
- Every state change goes through a `Command`. The AI and the network use the same path as touch input.
