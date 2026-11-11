using System.Collections.Generic;
using NUnit.Framework;
using Oduncu.Sim;

namespace Oduncu.Sim.Tests
{
    /// <summary>Milestone 1 step 5 (plan section 3.5): the Open map, fog of war, walls, gates and towers.</summary>
    public class MapTests
    {
        private static readonly List<Command> None = new List<Command>();

        private static void Step(Simulation sim, params Command[] commands) => sim.Step(new List<Command>(commands));

        // ------------------------------------------------------------------ Open map

        [Test]
        public void OpenMapStartsMeetSectionNineOn200Seeds()
        {
            for (int seed = 1; seed <= 200; seed++)
            {
                Simulation sim = OpenMap.Create(seed);
                var path = new List<Cell>();
                var astar = new Pathfinder(sim.Map);
                int[] totals = new int[2];
                for (int p = 0; p < 2; p++)
                {
                    Entity tc = Scenarios.FindOwned(sim, p, EntityKind.TownCenter);
                    Assert.IsNotNull(tc, "seed " + seed);
                    CellRect home = tc.Footprint;
                    Assert.AreEqual(OpenMap.Villagers, CountOwned(sim, p, EntityKind.Villager), "seed " + seed);
                    Assert.AreEqual(1, CountOwned(sim, p, EntityKind.Scout), "seed " + seed);
                    Assert.AreEqual(OpenMap.Sheep, CountOwned(sim, p, EntityKind.Sheep), "seed " + seed);

                    Assert.LessOrEqual(Nearest(sim, home, EntityKind.Tree), OpenMap.ForestWithin, "forest, seed " + seed);
                    Assert.LessOrEqual(Nearest(sim, home, EntityKind.Berries), OpenMap.BerriesWithin, "berries, seed " + seed);
                    Assert.LessOrEqual(Nearest(sim, home, EntityKind.GoldMine), OpenMap.GoldStoneWithin, "gold, seed " + seed);
                    Assert.LessOrEqual(Nearest(sim, home, EntityKind.StoneMine), OpenMap.GoldStoneWithin, "stone, seed " + seed);
                    Assert.AreEqual(OpenMap.Boar, CountWithin(sim, home, EntityKind.Boar, OpenMap.BoarWithin), "boar, seed " + seed);
                    Assert.AreEqual(OpenMap.Sheep, CountWithin(sim, home, EntityKind.Sheep, OpenMap.SheepWithin), "sheep, seed " + seed);

                    // Every resource can be reached from this Town Center. A tree inside a forest
                    // counts when its forest can be reached: the outer trees get cut first.
                    Cell start = FreeCellBeside(sim, home);
                    var forestReached = ReachableForests(sim, start);
                    foreach (Entity e in sim.Entities)
                    {
                        if (!e.IsResource && e.Kind != EntityKind.Boar && e.Kind != EntityKind.TownCenter) continue;
                        if (e == tc) continue;
                        if (e.Kind == EntityKind.Tree)
                        {
                            Assert.IsTrue(forestReached.Contains(e.Cell), "forest at " + e.Cell + " unreachable from player " + p + ", seed " + seed);
                            continue;
                        }
                        Assert.IsTrue(astar.FindPathAdjacentToRect(start, e.Footprint, path), e.Kind + " at " + e.Cell + " unreachable from player " + p + ", seed " + seed);
                    }

                    foreach (Entity e in sim.Entities)
                        if (e.IsResource && home.DistanceTo(e.Cell) <= 20) totals[p] += e.Amount;
                }
                int diff = System.Math.Abs(totals[0] - totals[1]);
                Assert.Less(diff * 100, 5 * System.Math.Max(totals[0], totals[1]), "resources within 20 tiles, seed " + seed);
            }
        }

        private static int CountOwned(Simulation sim, int player, EntityKind kind)
        {
            int n = 0;
            foreach (Entity e in sim.Entities) if (e.Owner == player && e.Kind == kind) n++;
            return n;
        }

        private static int Nearest(Simulation sim, CellRect home, EntityKind kind)
        {
            int best = int.MaxValue;
            foreach (Entity e in sim.Entities) if (e.Kind == kind) best = System.Math.Min(best, home.DistanceTo(e.Cell));
            return best;
        }

        private static int CountWithin(Simulation sim, CellRect home, EntityKind kind, int within)
        {
            int n = 0;
            foreach (Entity e in sim.Entities) if (e.Kind == kind && home.DistanceTo(e.Cell) <= within) n++;
            return n;
        }

        /// <summary>Trees connected (8-way, through trees) to a tree beside ground walkable from start.</summary>
        private static HashSet<Cell> ReachableForests(Simulation sim, Cell start)
        {
            var walk = new HashSet<Cell> { start };
            var queue = new Queue<Cell>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                Cell c = queue.Dequeue();
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var n = new Cell(c.X + dx, c.Y + dy);
                        if (!sim.Map.IsFree(n) || walk.Contains(n)) continue;
                        if (dx != 0 && dy != 0 && (!sim.Map.IsFree(c.X + dx, c.Y) || !sim.Map.IsFree(c.X, c.Y + dy))) continue;
                        walk.Add(n);
                        queue.Enqueue(n);
                    }
            }
            var trees = new HashSet<Cell>();
            foreach (Entity e in sim.Entities)
            {
                if (e.Kind != EntityKind.Tree) continue;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (walk.Contains(new Cell(e.Cell.X + dx, e.Cell.Y + dy)) && trees.Add(e.Cell)) queue.Enqueue(e.Cell);
            }
            while (queue.Count > 0)
            {
                Cell c = queue.Dequeue();
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var n = new Cell(c.X + dx, c.Y + dy);
                        Entity occupant = sim.Find(sim.Map.OccupantAt(n.X, n.Y));
                        if (occupant != null && occupant.Kind == EntityKind.Tree && trees.Add(n)) queue.Enqueue(n);
                    }
            }
            return trees;
        }

        private static Cell FreeCellBeside(Simulation sim, CellRect r)
        {
            for (int y = r.Y - 1; y <= r.MaxY + 1; y++)
                for (int x = r.X - 1; x <= r.MaxX + 1; x++)
                    if (sim.Map.IsFree(x, y)) return new Cell(x, y);
            Assert.Fail("Town Center walled in");
            return default;
        }

        [Test]
        public void OpenMapIsDeterministicPerSeed()
        {
            Assert.AreEqual(OpenMap.Create(7).ComputeHash(), OpenMap.Create(7).ComputeHash());
            Assert.AreNotEqual(OpenMap.Create(7).ComputeHash(), OpenMap.Create(8).ComputeHash());
        }

        // ------------------------------------------------------------------ fog

        [Test]
        public void UnitBecomesVisibleOnTheTickItEntersLineOfSight()
        {
            var sim = new Simulation(48, 16, 2, 1);
            Entity watcher = sim.SpawnUnit(EntityKind.Villager, 0, new Cell(5, 8));
            Entity walker = sim.SpawnUnit(EntityKind.Scout, 1, new Cell(30, 8));
            sim.Step(None);
            Assert.IsFalse(sim.CanSee(0, walker));
            Step(sim, Command.Move(1, new[] { walker.Id }, new Cell(5, 8)));
            int los = watcher.Stats.LineOfSight;
            while (true)
            {
                bool inSight = (walker.Cell.X - watcher.Cell.X) * (walker.Cell.X - watcher.Cell.X) + (walker.Cell.Y - watcher.Cell.Y) * (walker.Cell.Y - watcher.Cell.Y) <= los * los + los;
                Assert.AreEqual(inSight, sim.Fog.IsVisible(0, walker.Cell), "tick " + sim.CurrentTick);
                if (inSight) break;
                sim.Step(None);
                Assert.Less(sim.CurrentTick, 400);
            }
            Assert.IsTrue(sim.CanSee(0, walker));
        }

        [Test]
        public void ExploredCellsStayExplored()
        {
            var sim = new Simulation(48, 16, 1, 1);
            Entity scout = sim.SpawnUnit(EntityKind.Scout, 0, new Cell(4, 8));
            sim.Step(None);
            Assert.IsTrue(sim.Fog.IsVisible(0, new Cell(4, 8)));
            Step(sim, Command.Move(0, new[] { scout.Id }, new Cell(44, 8)));
            for (int t = 0; t < 400 && scout.State != UnitState.Idle; t++) sim.Step(None);
            Assert.IsFalse(sim.Fog.IsVisible(0, new Cell(4, 8)), "out of sight now");
            Assert.IsTrue(sim.Fog.IsExplored(0, new Cell(4, 8)), "but still explored");
        }

        [Test]
        public void CommandsOnHiddenEntitiesAreRejected()
        {
            var sim = new Simulation(48, 48, 2, 1);
            Entity militia = sim.SpawnUnit(EntityKind.Militia, 0, new Cell(4, 4));
            Entity villager = sim.SpawnUnit(EntityKind.Villager, 0, new Cell(5, 4));
            Entity enemy = sim.SpawnUnit(EntityKind.Villager, 1, new Cell(40, 40));
            Entity tree = sim.SpawnStructure(EntityKind.Tree, SimConstants.NeutralOwner, new Cell(30, 30), false);
            Step(sim, Command.Attack(0, new[] { militia.Id }, enemy.Id));
            Assert.AreEqual("you cannot see that", sim.LastRejection);
            Assert.AreEqual(UnitState.Idle, militia.State);
            Step(sim, Command.Gather(0, new[] { villager.Id }, tree.Id));
            Assert.AreEqual("you cannot see that", sim.LastRejection);

            // With the reveal-map setting, the same orders go through.
            var open = new Simulation(48, 48, 2, 1) { Reveal = RevealMode.AllVisible };
            Entity m2 = open.SpawnUnit(EntityKind.Militia, 0, new Cell(4, 4));
            Entity e2 = open.SpawnUnit(EntityKind.Villager, 1, new Cell(40, 40));
            Step(open, Command.Attack(0, new[] { m2.Id }, e2.Id));
            Assert.AreEqual(UnitState.Attacking, m2.State);
        }

        // ------------------------------------------------------------------ walls and gates

        [Test]
        public void WallLineChargesPerSegmentAndSkipsBlockedCells()
        {
            var sim = new Simulation(32, 32, 1, 1);
            sim.CompleteAge(0, AgeId.Feudal);
            sim.SpawnStructure(EntityKind.Tree, SimConstants.NeutralOwner, new Cell(12, 10), false);
            Entity v = sim.SpawnUnit(EntityKind.Villager, 0, new Cell(9, 12));
            int wood = sim.Players[0].Wood;
            Step(sim, Command.BuildWall(0, new[] { v.Id }, EntityKind.PalisadeWall, new Cell(10, 10), new Cell(16, 10)));
            int segments = 0;
            foreach (Entity e in sim.Entities) if (e.Kind == EntityKind.PalisadeWall) segments++;
            Assert.AreEqual(6, segments, "seven cells, one blocked by a tree");
            Assert.AreEqual(wood - 6 * sim.Players[0].Stats.Of(EntityKind.PalisadeWall).Cost.Wood, sim.Players[0].Wood);

            for (int t = 0; t < 2000; t++) sim.Step(None);
            foreach (Entity e in sim.Entities)
                if (e.Kind == EntityKind.PalisadeWall) Assert.IsFalse(e.UnderConstruction, "the villager walks down the line and builds every segment");
        }

        [Test]
        public void WallLineStopsWhenTheStockpileRunsOut()
        {
            var sim = new Simulation(32, 32, 1, 1);
            sim.CompleteAge(0, AgeId.Feudal);
            sim.Players[0].Set(new Cost(0, 10, 0, 0));
            Entity v = sim.SpawnUnit(EntityKind.Villager, 0, new Cell(9, 12));
            Step(sim, Command.BuildWall(0, new[] { v.Id }, EntityKind.PalisadeWall, new Cell(10, 10), new Cell(20, 10)));
            int segments = 0;
            foreach (Entity e in sim.Entities) if (e.Kind == EntityKind.PalisadeWall) segments++;
            Assert.AreEqual(3, segments);
            Assert.AreEqual("not enough wood", sim.LastRejection);
        }

        [Test]
        public void EnemiesRouteAroundAClosedWallAndTheOwnerUsesItsGate()
        {
            var sim = new Simulation(24, 24, 2, 1);
            // A wall across the whole map at y = 12, with a gate in the middle.
            for (int x = 0; x < 24; x++)
                sim.SpawnStructure(x == 12 ? EntityKind.Gate : EntityKind.StoneWall, 0, new Cell(x, 12), false);
            var path = new List<Cell>();
            var astar = new Pathfinder(sim.Map);
            Assert.IsTrue(astar.FindPathToCell(new Cell(12, 6), new Cell(12, 18), path, 0), "the owner walks through its gate");
            Assert.Contains(new Cell(12, 12), path);
            Assert.IsFalse(astar.FindPathToCell(new Cell(12, 6), new Cell(12, 18), path, 1), "the enemy cannot pass a closed wall");

            // Open a gap at the edge: the enemy goes around through it, never through the gate.
            var sim2 = new Simulation(24, 24, 2, 1);
            for (int x = 0; x < 23; x++)
                sim2.SpawnStructure(x == 12 ? EntityKind.Gate : EntityKind.StoneWall, 0, new Cell(x, 12), false);
            var astar2 = new Pathfinder(sim2.Map);
            Assert.IsTrue(astar2.FindPathToCell(new Cell(12, 6), new Cell(12, 18), path, 1));
            Assert.Contains(new Cell(23, 12), path);
            Assert.IsFalse(path.Contains(new Cell(12, 12)));

            // A unit actually walks through its own gate.
            Entity m = sim.SpawnUnit(EntityKind.Militia, 0, new Cell(12, 6));
            Step(sim, Command.Move(0, new[] { m.Id }, new Cell(12, 18)));
            for (int t = 0; t < 400 && m.State != UnitState.Idle; t++) sim.Step(None);
            Assert.AreEqual(new Cell(12, 18), m.Cell);
        }

        [Test]
        public void WallsNeedTheirAgeAndDoNotCountForAgeUp()
        {
            var sim = new Simulation(32, 32, 1, 1);
            Entity v = sim.SpawnUnit(EntityKind.Villager, 0, new Cell(9, 12));
            Step(sim, Command.BuildWall(0, new[] { v.Id }, EntityKind.PalisadeWall, new Cell(10, 10), new Cell(14, 10)));
            Assert.AreEqual("requires Feudal Age", sim.LastRejection);

            sim.CompleteAge(0, AgeId.Feudal);
            sim.SpawnStructure(EntityKind.PalisadeWall, 0, new Cell(2, 2), false);
            sim.SpawnStructure(EntityKind.Gate, 0, new Cell(4, 2), false);
            Assert.AreEqual("needs 2 different Feudal Age buildings", sim.AgeUpRejection(0));
        }

        // ------------------------------------------------------------------ towers

        [Test]
        public void TowerShootsEnemiesInRange()
        {
            var sim = new Simulation(32, 32, 2, 1);
            Entity tower = sim.SpawnStructure(EntityKind.Tower, 0, new Cell(10, 10), false);
            Entity enemy = sim.SpawnUnit(EntityKind.Villager, 1, new Cell(15, 10));
            for (int t = 0; t < 60 && enemy.Hp == enemy.Stats.MaxHp; t++) sim.Step(None);
            Assert.Less(enemy.Hp, enemy.Stats.MaxHp);
            Assert.IsTrue(tower.Alive);
        }
    }
}
