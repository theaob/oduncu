using System.Collections.Generic;

namespace Oduncu.Sim
{
    /// <summary>
    /// Scripted command streams used by the determinism tests and by the on-device probe.
    /// The script only reads simulation state, so two identical simulations receive
    /// identical commands.
    /// </summary>
    public static class Scenarios
    {
        public const int DeterminismSeed = 4242;
        public const int DeterminismTicks = 10000;

        public static Simulation CreateDeterminismScenario() => MapGenerator.CreateDefault(DeterminismSeed);

        /// <summary>Run the scripted scenario and return the final hash. Optionally records every command.</summary>
        public static ulong RunScripted(Simulation sim, int ticks, CommandLog log = null, List<ulong> hashEvery1000 = null)
        {
            var script = new DeterministicRandom(99);
            var commands = new List<Command>();
            for (int t = 0; t < ticks; t++)
            {
                commands.Clear();
                FillCommands(sim, sim.CurrentTick + 1, script, commands);
                if (log != null) log.Record(sim.CurrentTick + 1, commands);
                sim.Step(commands);
                if (hashEvery1000 != null && sim.CurrentTick % 1000 == 0) hashEvery1000.Add(sim.ComputeHash());
            }
            return sim.ComputeHash();
        }

        /// <summary>Replay a recorded log into a fresh simulation and return the final hash.</summary>
        public static ulong Replay(Simulation sim, CommandLog log, int ticks, List<ulong> hashEvery1000 = null)
        {
            for (int t = 0; t < ticks; t++)
            {
                sim.Step(log.CommandsAt(sim.CurrentTick + 1));
                if (hashEvery1000 != null && sim.CurrentTick % 1000 == 0) hashEvery1000.Add(sim.ComputeHash());
            }
            return sim.ComputeHash();
        }

        public static void FillCommands(Simulation sim, int tick, DeterministicRandom script, List<Command> output)
        {
            for (int p = 0; p < sim.Players.Length; p++)
            {
                Entity tc = FindOwned(sim, p, EntityKind.TownCenter);
                var villagers = CollectOwned(sim, p, EntityKind.Villager);
                var militia = CollectOwned(sim, p, EntityKind.Militia);

                if (tick == 1)
                {
                    foreach (Entity v in villagers)
                    {
                        Entity tree = sim.FindNearest(v.Position, 20, e => e.Kind == EntityKind.Tree && e.Amount > 0);
                        if (tree != null) output.Add(Command.Gather(p, new[] { v.Id }, tree.Id));
                    }
                }

                if (tick == 200 && villagers.Count > 0)
                {
                    Cell site = p == 0 ? new Cell(12, 4) : new Cell(MapGenerator.DefaultSize - 15, MapGenerator.DefaultSize - 7);
                    output.Add(Command.Build(p, new[] { villagers[0].Id }, EntityKind.Barracks, site));
                }

                if (tick >= 300 && tick % 250 == 0 && tc != null)
                {
                    output.Add(Command.Train(p, tc.Id, EntityKind.Villager));
                }

                if (tick >= 1000 && tick % 300 == 0)
                {
                    Entity barracks = FindOwned(sim, p, EntityKind.Barracks);
                    if (barracks != null) output.Add(Command.Train(p, barracks.Id, EntityKind.Militia));
                }

                if (tick >= 3000 && tick % 500 == 0 && militia.Count > 0)
                {
                    Entity enemy = sim.FindNearest(militia[0].Position, 200, e => e.Owner >= 0 && e.Owner != p && (e.IsUnit || e.IsBuilding));
                    if (enemy != null) output.Add(Command.Attack(p, Ids(militia), enemy.Id));
                }

                if (tick % 700 == 350 && villagers.Count > 1)
                {
                    Entity v = villagers[script.Next(villagers.Count)];
                    Cell to = new Cell(script.Next(sim.Map.Width), script.Next(sim.Map.Height));
                    output.Add(Command.Move(p, new[] { v.Id }, to));
                }

                if (tick % 700 == 400 && villagers.Count > 0)
                {
                    // Send idle villagers back to work, exercising the gather path again.
                    foreach (Entity v in villagers)
                    {
                        if (v.State != UnitState.Idle) continue;
                        Entity tree = sim.FindNearest(v.Position, 20, e => e.Kind == EntityKind.Tree && e.Amount > 0);
                        if (tree != null) output.Add(Command.Gather(p, new[] { v.Id }, tree.Id));
                    }
                }
            }
        }

        // ------------------------------------------------------------------ benchmark

        public const int BenchmarkMapSize = 64;
        public const int BenchmarkUnitsPerPlayer = 75;

        /// <summary>
        /// The section 12.4 load for a 1v1: a 64x64 map, 75 units a side (45 villagers, 30
        /// militia), about 400 trees and a handful of buildings. Spawned directly, ignoring the
        /// population cap, so the benchmark measures the full-population tick cost.
        /// </summary>
        public static Simulation CreateFullPopulationBenchmark(int seed)
        {
            int size = BenchmarkMapSize;
            var sim = new Simulation(size, size, 2, (ulong)seed);
            var rng = new DeterministicRandom((ulong)seed * 104729UL + 3UL);
            sim.SpawnStructure(EntityKind.TownCenter, 0, new Cell(4, 4), false);
            sim.SpawnStructure(EntityKind.TownCenter, 1, new Cell(size - 7, size - 7), false);
            sim.SpawnStructure(EntityKind.Barracks, 0, new Cell(10, 4), false);
            sim.SpawnStructure(EntityKind.Barracks, 1, new Cell(size - 13, size - 7), false);
            sim.SpawnStructure(EntityKind.Forge, 0, new Cell(4, 10), false);
            sim.SpawnStructure(EntityKind.Forge, 1, new Cell(size - 7, size - 13), false);

            int trees = 0;
            for (int attempt = 0; attempt < 400 && trees < 400; attempt++)
            {
                int cx = rng.Next(2, size - 2), cy = rng.Next(2, size - 2);
                if (cx + cy < 24 || cx + cy > 2 * size - 26) continue; // keep the bases clear
                int radius = rng.Next(2, 4);
                for (int y = cy - radius; y <= cy + radius && trees < 400; y++)
                    for (int x = cx - radius; x <= cx + radius && trees < 400; x++)
                        if (rng.Chance(70) && sim.SpawnStructure(EntityKind.Tree, SimConstants.NeutralOwner, new Cell(x, y), false) != null) trees++;
            }

            for (int p = 0; p < 2; p++)
            {
                for (int i = 0; i < BenchmarkUnitsPerPlayer; i++)
                {
                    EntityKind kind = i < 45 ? EntityKind.Villager : EntityKind.Militia;
                    int x = 3 + (i % 15), y = 15 + i / 15;
                    var cell = p == 0 ? new Cell(x, y) : new Cell(size - 1 - x, size - 1 - y);
                    sim.SpawnUnit(kind, p, cell);
                }
            }
            return sim;
        }

        /// <summary>Villagers gather and both armies march to the map centre on tick 1, where they meet and fight.</summary>
        public static void FillBenchmarkCommands(Simulation sim, int tick, List<Command> output)
        {
            if (tick != 1) return;
            var centre = new Cell(sim.Map.Width / 2, sim.Map.Height / 2);
            for (int p = 0; p < sim.Players.Length; p++)
            {
                foreach (Entity v in CollectOwned(sim, p, EntityKind.Villager))
                {
                    Entity tree = sim.FindNearest(v.Position, 40, e => e.Kind == EntityKind.Tree && e.Amount > 0);
                    if (tree != null) output.Add(Command.Gather(p, new[] { v.Id }, tree.Id));
                }
                var militia = CollectOwned(sim, p, EntityKind.Militia);
                if (militia.Count > 0) output.Add(Command.Move(p, Ids(militia), centre));
            }
        }

        public static Entity FindOwned(Simulation sim, int owner, EntityKind kind)
        {
            var list = sim.Entities;
            for (int i = 0; i < list.Count; i++)
            {
                Entity e = list[i];
                if (e.Alive && e.Owner == owner && e.Kind == kind) return e;
            }
            return null;
        }

        public static List<Entity> CollectOwned(Simulation sim, int owner, EntityKind kind)
        {
            var result = new List<Entity>();
            var list = sim.Entities;
            for (int i = 0; i < list.Count; i++)
            {
                Entity e = list[i];
                if (e.Alive && e.Owner == owner && e.Kind == kind) result.Add(e);
            }
            return result;
        }

        private static int[] Ids(List<Entity> entities)
        {
            var ids = new int[entities.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = entities[i].Id;
            return ids;
        }
    }
}
