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
