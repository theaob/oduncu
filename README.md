# Oduncu

A mobile real-time strategy game with the economy-to-army loop of Age of Empires II,
redesigned for touch and 15-minute matches. Built with Unity 6 and C#.

- Design document: [docs/GAME_DESIGN.md](docs/GAME_DESIGN.md)
- Current milestone: **M0 prototype** (see section 14 of the design document)

## Layout

| Path | What it is |
|---|---|
| `Oduncu.Unity/` | The Unity project. Open this folder in Unity Hub. |
| `Oduncu.Unity/Assets/Sim/` | The deterministic simulation. Pure C#, fixed-point maths, no `UnityEngine` reference. |
| `Oduncu.Unity/Assets/Sim.Tests/` | NUnit tests for the simulation. Run in Unity's Test Runner or headless (below). |
| `Oduncu.Unity/Assets/Game/` | Presentation: tick runner, primitive renderer, touch input, editor bootstrap and build script. |
| `Oduncu.Sim/`, `Oduncu.Sim.Tests/` | .NET projects that compile the same sources outside Unity for CI, servers and tools. |
| `.github/workflows/ci.yml` | Runs the headless tests on every push; builds an Android APK when Unity licence secrets are set. |

## Running the simulation tests without Unity

Requires the .NET 8 SDK.

```
dotnet test Oduncu.Sim.Tests/Oduncu.Sim.Tests.csproj
```

The suite includes the milestone 0 determinism check: the scripted scenario runs for
10,000 ticks twice and from a replayed command log, and every checkpoint hash must match.

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
DETERMINISM_HASH 8A5992FA1F434A07
```

Any change to simulation rules changes this value; update it in the same commit.

## Rules for simulation code

- No `float`, `double`, `System.Random`, `DateTime` or `UnityEngine` inside `Assets/Sim`.
- Iterate entities by index in id order; never iterate a `Dictionary` or `HashSet` where the order affects state.
- Every state change goes through a `Command`. The AI and the network use the same path as touch input.
