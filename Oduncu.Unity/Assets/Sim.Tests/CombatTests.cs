using System.Collections.Generic;
using NUnit.Framework;
using Oduncu.Sim;

namespace Oduncu.Sim.Tests
{
    /// <summary>Milestone 1 step 3 (plan section 3.4): armour, bonuses, projectiles, siege, monks, garrison, movement, conquest.</summary>
    public class CombatTests
    {
        private static readonly List<Command> None = new List<Command>();

        private static void Run(Simulation sim, int ticks, params Command[] firstTickCommands)
        {
            sim.Step(new List<Command>(firstTickCommands));
            for (int i = 1; i < ticks; i++) sim.Step(None);
        }

        private static int RunUntil(Simulation sim, int maxTicks, System.Func<bool> condition, string what)
        {
            for (int t = 0; t < maxTicks; t++)
            {
                if (condition()) return t;
                sim.Step(None);
            }
            Assert.Fail("timed out waiting for " + what);
            return -1;
        }

        private static int Count(Simulation sim, int owner)
        {
            int n = 0;
            foreach (Entity e in sim.Entities) if (e.Alive && e.Owner == owner && e.IsUnit) n++;
            return n;
        }

        // ------------------------------------------------------------------ counters

        /// <summary>Design section 5.1: the counter wins a 5 vs 5 fight on open ground.</summary>
        [TestCase(EntityKind.Spearman, EntityKind.Scout)]
        [TestCase(EntityKind.Pikeman, EntityKind.Knight)]
        [TestCase(EntityKind.Knight, EntityKind.Crossbowman)]
        [TestCase(EntityKind.Scout, EntityKind.Archer)]
        [TestCase(EntityKind.Archer, EntityKind.Militia)]
        [TestCase(EntityKind.Crossbowman, EntityKind.LongSwordsman)]
        [TestCase(EntityKind.Skirmisher, EntityKind.Archer)]
        [TestCase(EntityKind.Militia, EntityKind.Spearman)]
        [TestCase(EntityKind.LightCavalry, EntityKind.Mangonel)]
        [TestCase(EntityKind.LightCavalry, EntityKind.Monk)]
        public void CounterWinsFiveVersusFive(EntityKind counter, EntityKind countered)
        {
            for (int side = 0; side < 2; side++)
            {
                // Both placements, so the result does not depend on who stands where or has lower ids.
                var sim = new Simulation(40, 40, 2, 1);
                int counterOwner = side, otherOwner = 1 - side;
                for (int i = 0; i < 5; i++)
                {
                    sim.SpawnUnit(counter, counterOwner, new Cell(16 + i, side == 0 ? 17 : 22));
                    sim.SpawnUnit(countered, otherOwner, new Cell(16 + i, side == 0 ? 22 : 17));
                }
                RunUntil(sim, 3000, () => Count(sim, 0) == 0 || Count(sim, 1) == 0, counter + " vs " + countered + " to finish");
                Assert.Greater(Count(sim, counterOwner), 0, counter + " should beat " + countered + " (counter as player " + counterOwner + ")");
                Assert.AreEqual(0, Count(sim, otherOwner));
            }
        }

        [Test]
        public void BonusDamageMatchesTheTables()
        {
            var sim = new Simulation(32, 32, 2, 1);
            Entity knight = sim.SpawnUnit(EntityKind.Knight, 1, new Cell(5, 5));
            Entity barracks = sim.SpawnStructure(EntityKind.Barracks, 1, new Cell(10, 10), false);
            Entity ram = sim.SpawnUnit(EntityKind.BatteringRam, 1, new Cell(20, 20));

            // Spearman: 3 attack - 2 melee armour + 15 against cavalry.
            Assert.AreEqual(3 - 2 + 15, sim.DamageAgainst(EntityKind.Spearman, 3, AttackType.Melee, knight));
            // Ram: 2 attack - 2 melee armour is below the minimum of 1, plus 125 against buildings.
            Assert.AreEqual(1 + 125, sim.DamageAgainst(EntityKind.BatteringRam, 2, AttackType.Melee, barracks));
            // Arrows cannot get through a ram's pierce armour: always the minimum.
            Assert.AreEqual(1, sim.DamageAgainst(EntityKind.Castle, 11, AttackType.Pierce, ram));
        }

        [Test]
        public void RamTakesOneDamageFromTownCenterArrows()
        {
            var sim = new Simulation(32, 32, 2, 1);
            Entity tc = sim.SpawnStructure(EntityKind.TownCenter, 0, new Cell(10, 10), false);
            Entity ram = sim.SpawnUnit(EntityKind.BatteringRam, 1, new Cell(15, 11));
            int max = ram.Hp;
            RunUntil(sim, 200, () => ram.Hp < max, "the first arrow");
            Assert.AreEqual(max - sim.ArrowsFor(tc), ram.Hp);
        }

        [Test]
        public void TrebuchetOutrangesCastle()
        {
            var sim = new Simulation(48, 32, 2, 1);
            Entity castle = sim.SpawnStructure(EntityKind.Castle, 1, new Cell(10, 10), false);
            Entity treb = sim.SpawnUnit(EntityKind.Trebuchet, 0, new Cell(28, 12));
            int castleMax = castle.Hp, trebMax = treb.Hp;
            Run(sim, 400, Command.Attack(0, new[] { treb.Id }, castle.Id));
            Assert.Less(castle.Hp, castleMax, "trebuchet hits the castle");
            Assert.AreEqual(trebMax, treb.Hp, "castle cannot reach the trebuchet");
            Assert.AreEqual(new Cell(28, 12), treb.Cell, "trebuchet fires from where it stands");
        }

        // ------------------------------------------------------------------ projectiles

        [Test]
        public void ShotAtTargetThatDiesInFlightIsWasted()
        {
            var sim = new Simulation(32, 32, 2, 1);
            Entity archer = sim.SpawnUnit(EntityKind.Archer, 0, new Cell(10, 10));
            Entity target = sim.SpawnUnit(EntityKind.Villager, 1, new Cell(14, 10));
            Entity militia = sim.SpawnUnit(EntityKind.Militia, 0, new Cell(15, 10));
            Entity bystander = sim.SpawnUnit(EntityKind.Villager, 0, new Cell(14, 11));

            sim.Step(new List<Command> { Command.Attack(0, new[] { archer.Id }, target.Id), Command.Stop(0, new[] { militia.Id }) });
            RunUntil(sim, 20, () => sim.Projectiles.Count == 1, "the archer to fire");
            int landTick = sim.Projectiles[0].LandTick;
            Assert.Greater(landTick, sim.CurrentTick + 1, "the arrow is still in the air");

            target.Hp = 1;
            sim.Step(new List<Command> { Command.Attack(0, new[] { militia.Id }, target.Id), Command.Stop(0, new[] { archer.Id }) });
            Assert.IsFalse(target.Alive, "the militia kills the target first");
            int bystanderHp = bystander.Hp;
            while (sim.CurrentTick < landTick) sim.Step(new List<Command> { Command.Stop(0, new[] { militia.Id, archer.Id }) });
            Assert.AreEqual(0, sim.Projectiles.Count, "the arrow has landed");
            Assert.AreEqual(bystanderHp, bystander.Hp, "the wasted arrow hurts nobody else");
            Assert.AreEqual(militia.Stats.MaxHp, militia.Hp);
        }

        [Test]
        public void MinimumRangeTargetsAreIgnored()
        {
            var sim = new Simulation(32, 32, 2, 1);
            Entity mangonel = sim.SpawnUnit(EntityKind.Mangonel, 0, new Cell(10, 10));
            Entity close = sim.SpawnUnit(EntityKind.Militia, 1, new Cell(12, 10));
            Assert.IsNull(sim.FindAttackTarget(mangonel, mangonel.Stats.Range), "2 tiles is inside the 3-tile minimum range");
            Entity far = sim.SpawnUnit(EntityKind.Militia, 1, new Cell(15, 10));
            Assert.AreEqual(far.Id, sim.FindAttackTarget(mangonel, mangonel.Stats.Range).Id);

            // Ordered onto a target inside minimum range, the mangonel gives up rather than firing.
            Run(sim, 2, Command.Attack(0, new[] { mangonel.Id }, close.Id));
            Assert.AreEqual(0, sim.Projectiles.Count);
            Assert.AreNotEqual(close.Id, mangonel.TargetId);
        }

        [Test]
        public void SplashHitsEveryUnitInRadiusOnceIncludingFriends()
        {
            var sim = new Simulation(32, 32, 2, 1);
            Entity mangonel = sim.SpawnUnit(EntityKind.Mangonel, 0, new Cell(10, 10));
            Entity target = sim.SpawnUnit(EntityKind.Villager, 1, new Cell(16, 10));
            Entity enemyNear = sim.SpawnUnit(EntityKind.Villager, 1, new Cell(17, 10));
            Entity friendNear = sim.SpawnUnit(EntityKind.Villager, 0, new Cell(16, 11));
            Entity outside = sim.SpawnUnit(EntityKind.Villager, 1, new Cell(18, 10));
            var hit = new[] { target, enemyNear, friendNear };
            foreach (Entity e in hit) e.Hp = 500;
            outside.Hp = 500;

            sim.Step(new List<Command> { Command.Attack(0, new[] { mangonel.Id }, target.Id) });
            RunUntil(sim, 40, () => sim.Projectiles.Count == 1, "the mangonel to fire");
            sim.Step(new List<Command> { Command.Stop(0, new[] { mangonel.Id }) });
            RunUntil(sim, 40, () => sim.Projectiles.Count == 0, "the shot to land");

            foreach (Entity e in hit)
            {
                int expected = 500 - sim.DamageAgainst(EntityKind.Mangonel, mangonel.Stats.Attack, AttackType.Melee, e);
                Assert.AreEqual(expected, e.Hp, "entity " + e.Id + " is hit exactly once");
            }
            Assert.AreEqual(500, outside.Hp, "two tiles away is outside the splash");
        }

        // ------------------------------------------------------------------ targeting and stance

        [Test]
        public void AutoTargetPrefersMilitaryThenUnitsThenBuildings()
        {
            var sim = new Simulation(32, 32, 2, 1);
            Entity archer = sim.SpawnUnit(EntityKind.Archer, 0, new Cell(10, 10));
            Entity house = sim.SpawnStructure(EntityKind.House, 1, new Cell(11, 11), false);
            Entity villager = sim.SpawnUnit(EntityKind.Villager, 1, new Cell(12, 10));
            Entity far = sim.SpawnUnit(EntityKind.Militia, 1, new Cell(14, 10));
            Entity near = sim.SpawnUnit(EntityKind.Militia, 1, new Cell(10, 13));
            FP reach = FP.FromInt(archer.Stats.LineOfSight);

            Assert.AreEqual(near.Id, sim.FindAttackTarget(archer, reach).Id, "military first, then the closer one");
            Assert.AreNotEqual(far.Id, near.Id);

            var sim2 = new Simulation(32, 32, 2, 1);
            Entity archer2 = sim2.SpawnUnit(EntityKind.Archer, 0, new Cell(10, 10));
            Entity house2 = sim2.SpawnStructure(EntityKind.House, 1, new Cell(11, 11), false);
            Entity villager2 = sim2.SpawnUnit(EntityKind.Villager, 1, new Cell(13, 10));
            Assert.AreEqual(villager2.Id, sim2.FindAttackTarget(archer2, reach).Id, "units before a closer building");

            var sim3 = new Simulation(32, 32, 2, 1);
            Entity archer3 = sim3.SpawnUnit(EntityKind.Archer, 0, new Cell(10, 10));
            Entity house3 = sim3.SpawnStructure(EntityKind.House, 1, new Cell(11, 11), false);
            Assert.AreEqual(house3.Id, sim3.FindAttackTarget(archer3, reach).Id, "buildings when nothing else is near");
            Assert.IsNotNull(house);
            Assert.IsNotNull(villager);
            Assert.IsNotNull(house2);
        }

        [Test]
        public void HoldGroundOnlyAttacksTargetsInRange()
        {
            var sim = new Simulation(32, 32, 2, 1);
            Entity archer = sim.SpawnUnit(EntityKind.Archer, 0, new Cell(10, 10));
            Entity outOfRange = sim.SpawnUnit(EntityKind.Villager, 1, new Cell(15, 10));
            Run(sim, 60, Command.SetStance(0, new[] { archer.Id }, Stance.HoldGround));
            Assert.AreEqual(new Cell(10, 10), archer.Cell, "does not chase");
            Assert.AreEqual(outOfRange.Stats.MaxHp, outOfRange.Hp);

            Entity inRange = sim.SpawnUnit(EntityKind.Villager, 1, new Cell(13, 10));
            Run(sim, 40);
            Assert.Less(inRange.Hp, inRange.Stats.MaxHp, "attacks what is in range");
            Assert.AreEqual(new Cell(10, 10), archer.Cell);

            // The aggressive default chases the same target.
            var sim2 = new Simulation(32, 32, 2, 1);
            Entity archer2 = sim2.SpawnUnit(EntityKind.Archer, 0, new Cell(10, 10));
            sim2.SpawnUnit(EntityKind.Villager, 1, new Cell(15, 10));
            Run(sim2, 60);
            Assert.AreNotEqual(new Cell(10, 10), archer2.Cell);
        }

        // ------------------------------------------------------------------ garrison

        [Test]
        public void GarrisonAddsArrowsAndEjectReturnsUnits()
        {
            var sim = new Simulation(32, 32, 2, 1);
            Entity tc = sim.SpawnStructure(EntityKind.TownCenter, 0, new Cell(10, 10), false);
            var ids = new List<int>();
            for (int i = 0; i < 5; i++) ids.Add(sim.SpawnUnit(EntityKind.Villager, 0, new Cell(16, 8 + i)).Id);
            int baseArrows = sim.ArrowsFor(tc);

            Run(sim, 1, Command.Garrison(0, ids, tc.Id));
            RunUntil(sim, 200, () => tc.GarrisonCount == 5, "all five inside");
            Assert.AreEqual(baseArrows + 5, sim.ArrowsFor(tc));
            foreach (int id in ids)
            {
                Entity v = sim.Find(id);
                Assert.AreEqual(UnitState.Garrisoned, v.State);
            }
            Assert.IsNull(sim.FindNearest(tc.Position, 10, e => e.IsUnit), "garrisoned units are off the map");
            Assert.AreEqual(5, sim.Players[0].Population, "still counted for population");

            Run(sim, 1, Command.Ungarrison(0, tc.Id));
            Assert.AreEqual(0, tc.GarrisonCount);
            var cells = new HashSet<Cell>();
            foreach (int id in ids)
            {
                Entity v = sim.Find(id);
                Assert.AreNotEqual(UnitState.Garrisoned, v.State);
                Assert.IsTrue(sim.Map.IsFree(v.Cell), "ejected onto a free cell");
                cells.Add(v.Cell);
            }
            Assert.AreEqual(5, cells.Count, "each on its own cell");
        }

        [Test]
        public void DestroyedBuildingEjectsGarrison()
        {
            var sim = new Simulation(32, 32, 2, 1);
            Entity tower = sim.SpawnStructure(EntityKind.Tower, 0, new Cell(10, 10), false);
            Entity v = sim.SpawnUnit(EntityKind.Villager, 0, new Cell(12, 10));
            Run(sim, 1, Command.Garrison(0, new[] { v.Id }, tower.Id));
            RunUntil(sim, 100, () => v.State == UnitState.Garrisoned, "the villager inside");
            Entity ram = sim.SpawnUnit(EntityKind.BatteringRam, 1, new Cell(10, 12));
            tower.Hp = 1;
            Run(sim, 1, Command.Attack(1, new[] { ram.Id }, tower.Id));
            RunUntil(sim, 100, () => !tower.Alive, "the tower to fall");
            Assert.IsTrue(v.Alive);
            Assert.AreEqual(UnitState.Idle, v.State);
        }

        // ------------------------------------------------------------------ monks

        [Test]
        public void ConversionCompletesAfterTheChannel()
        {
            var sim = new Simulation(32, 32, 2, 1);
            Entity monk = sim.SpawnUnit(EntityKind.Monk, 0, new Cell(10, 10));
            Entity knight = sim.SpawnUnit(EntityKind.Knight, 1, new Cell(18, 10));
            Run(sim, SimConstants.ConversionTicks - 1, Command.Attack(0, new[] { monk.Id }, knight.Id));
            Assert.AreEqual(1, knight.Owner, "still channelling");
            sim.Step(None);
            Assert.AreEqual(0, knight.Owner, "converted on the last channel tick");
            Assert.AreEqual(UnitState.Idle, knight.State);
            Assert.AreEqual(UnitState.Idle, monk.State);
        }

        [Test]
        public void MonksCannotConvertBuildings()
        {
            var sim = new Simulation(32, 32, 2, 1);
            Entity monk = sim.SpawnUnit(EntityKind.Monk, 0, new Cell(10, 10));
            Entity house = sim.SpawnStructure(EntityKind.House, 1, new Cell(13, 10), false);
            Run(sim, 1, Command.Attack(0, new[] { monk.Id }, house.Id));
            Assert.AreEqual("monks convert units only", sim.LastRejection);
            Assert.AreEqual(UnitState.Idle, monk.State);
        }

        [Test]
        public void IdleMonkHealsDamagedFriend()
        {
            var sim = new Simulation(32, 32, 2, 1);
            sim.SpawnUnit(EntityKind.Monk, 0, new Cell(10, 10));
            Entity hurt = sim.SpawnUnit(EntityKind.Militia, 0, new Cell(14, 10));
            hurt.Hp = 10;
            RunUntil(sim, 600, () => hurt.Hp == hurt.Stats.MaxHp, "full health");
        }

        // ------------------------------------------------------------------ movement

        [Test]
        public void SeparationSpreadsAClumpWithoutEnteringBuildings()
        {
            var sim = new Simulation(32, 32, 1, 1);
            sim.SpawnStructure(EntityKind.House, 0, new Cell(16, 14), false);
            sim.SpawnStructure(EntityKind.House, 0, new Cell(13, 16), false);
            var units = new List<Entity>();
            for (int i = 0; i < 30; i++) units.Add(sim.SpawnUnit(EntityKind.Militia, 0, new Cell(15, 15)));
            for (int t = 0; t < 50; t++)
            {
                sim.Step(None);
                foreach (Entity u in units) Assert.IsTrue(sim.Map.IsFree(u.Cell), "unit " + u.Id + " pushed into a building at tick " + t);
            }
            var positions = new HashSet<FPVector2>();
            foreach (Entity u in units) Assert.IsTrue(positions.Add(u.Position), "two units share " + u.Position);
        }

        [TestCase(6)]
        [TestCase(16)]
        public void GroupArrivesInFormation(int size)
        {
            var sim = new Simulation(48, 48, 1, 1);
            var ids = new List<int>();
            var units = new List<Entity>();
            for (int i = 0; i < size; i++)
            {
                Entity u = sim.SpawnUnit(i % 2 == 0 ? EntityKind.Militia : EntityKind.Knight, 0, new Cell(4 + i % 4, 4 + i / 4));
                units.Add(u);
                ids.Add(u.Id);
            }
            var target = new Cell(36, 36);
            Run(sim, 1, Command.Move(0, ids, target));
            Assert.AreEqual(units[1].SpeedCap, units[0].Stats.Speed, "group moves at the slowest unit's speed");
            RunUntil(sim, 2000, () => units.TrueForAll(u => u.State == UnitState.Idle), "the group to arrive");
            foreach (Entity u in units)
            {
                Assert.LessOrEqual(Cell.Chebyshev(u.Cell, u.MoveTarget), 1, "unit " + u.Id + " near its slot");
                Assert.LessOrEqual(Cell.Chebyshev(u.Cell, target), 3);
            }
        }

        [Test]
        public void FlowFieldPathsMatchAStarWithinTenPercent()
        {
            for (int seed = 1; seed <= 50; seed++)
            {
                var sim = new Simulation(40, 40, 1, (ulong)seed);
                var rng = new DeterministicRandom((ulong)seed * 7919);
                for (int i = 0; i < 120; i++) sim.SpawnStructure(EntityKind.Tree, SimConstants.NeutralOwner, new Cell(rng.Next(40), rng.Next(40)), false);
                Cell start = FreeCell(sim, rng), goal = FreeCell(sim, rng);

                var path = new List<Cell>();
                var astar = new Pathfinder(sim.Map);
                if (!astar.FindPathToCell(start, goal, path)) continue;
                int astarCost = PathCost(start, path);

                var flow = new FlowFieldCache(sim.Map, 2);
                int flowCost = 0, steps = 0;
                Cell at = start, next;
                while (flow.NextCell(goal, at, out next))
                {
                    flowCost += next.X != at.X && next.Y != at.Y ? 14 : 10;
                    at = next;
                    Assert.Less(++steps, 2000, "flow field loops (seed " + seed + ")");
                }
                Assert.AreEqual(goal, at, "flow field reaches the goal (seed " + seed + ")");
                Assert.LessOrEqual(flowCost * 10, astarCost * 11, "seed " + seed);
            }
        }

        private static Cell FreeCell(Simulation sim, DeterministicRandom rng)
        {
            while (true)
            {
                var c = new Cell(rng.Next(40), rng.Next(40));
                if (sim.Map.IsFree(c)) return c;
            }
        }

        private static int PathCost(Cell start, List<Cell> path)
        {
            int cost = 0;
            Cell at = start;
            foreach (Cell c in path)
            {
                cost += c.X != at.X && c.Y != at.Y ? 14 : 10;
                at = c;
            }
            return cost;
        }

        // ------------------------------------------------------------------ conquest

        [Test]
        public void MatchEndsOnTheTickTheLastQualifyingBuildingDies()
        {
            var sim = new Simulation(32, 32, 2, 1);
            sim.SpawnStructure(EntityKind.Barracks, 0, new Cell(4, 4), false);
            Entity enemyBarracks = sim.SpawnStructure(EntityKind.Barracks, 1, new Cell(20, 20), false);
            sim.SpawnStructure(EntityKind.House, 1, new Cell(26, 26), false);
            sim.SpawnUnit(EntityKind.Villager, 1, new Cell(28, 4));
            Entity militia = sim.SpawnUnit(EntityKind.Militia, 0, new Cell(18, 20));
            sim.Step(None);
            Assert.IsFalse(sim.MatchOver);

            enemyBarracks.Hp = 5;
            sim.Step(new List<Command> { Command.Attack(0, new[] { militia.Id }, enemyBarracks.Id) });
            RunUntil(sim, 200, () => !enemyBarracks.Alive, "the barracks to fall");
            Assert.IsTrue(sim.MatchOver, "a house and a villager do not keep a player in the game");
            Assert.AreEqual(sim.CurrentTick, sim.MatchEndTick);
            Assert.AreEqual(0, sim.Winner);
            Assert.IsFalse(sim.Players[1].Alive);
        }
    }
}
