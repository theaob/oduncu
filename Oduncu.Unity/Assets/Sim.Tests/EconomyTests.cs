using System.Collections.Generic;
using NUnit.Framework;
using Oduncu.Sim;

namespace Oduncu.Sim.Tests
{
    /// <summary>Milestone 1 step 2 (plan section 3.2): resources, drop-offs, animals, farms, housing, rally, repair, auto-villager, planner.</summary>
    public class EconomyTests
    {
        private static readonly List<Command> None = new List<Command>();

        private static void Run(Simulation sim, int ticks, params Command[] firstTickCommands)
        {
            sim.Step(new List<Command>(firstTickCommands));
            for (int i = 1; i < ticks; i++) sim.Step(None);
        }

        /// <summary>Step until the condition holds; fails after maxTicks.</summary>
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

        private static Simulation Map(out Entity tc)
        {
            var sim = new Simulation(32, 32, 2, 1);
            tc = sim.SpawnStructure(EntityKind.TownCenter, 0, new Cell(2, 2), false);
            return sim;
        }

        // ------------------------------------------------------------------ resources and drop-off

        [TestCase(EntityKind.Tree, ResourceKind.Wood)]
        [TestCase(EntityKind.Berries, ResourceKind.Food)]
        [TestCase(EntityKind.GoldMine, ResourceKind.Gold)]
        [TestCase(EntityKind.StoneMine, ResourceKind.Stone)]
        public void EachKindDepositsIntoTheRightStockpile(EntityKind kind, ResourceKind into)
        {
            Entity tc;
            var sim = Map(out tc);
            Entity source = sim.SpawnStructure(kind, SimConstants.NeutralOwner, new Cell(9, 3), false);
            Entity v = sim.SpawnUnit(EntityKind.Villager, 0, new Cell(6, 3));
            var before = new int[4];
            for (int r = 0; r < 4; r++) before[r] = sim.Players[0].Get((ResourceKind)r);

            Run(sim, 1, Command.Gather(0, new[] { v.Id }, source.Id));
            RunUntil(sim, 1000, () => sim.Players[0].Get(into) > before[(int)into], "a deposit");
            for (int r = 0; r < 4; r++)
            {
                if ((ResourceKind)r == into) Assert.AreEqual(before[r] + SimConstants.VillagerCarryCapacity, sim.Players[0].Get((ResourceKind)r));
                else Assert.AreEqual(before[r], sim.Players[0].Get((ResourceKind)r), "nothing else changes");
            }
        }

        [Test]
        public void VillagerWalksToNearestDropOffThatAcceptsItsCarry()
        {
            Entity tc;
            var sim = Map(out tc);
            Entity gold = sim.SpawnStructure(EntityKind.GoldMine, SimConstants.NeutralOwner, new Cell(22, 22), false);
            Entity lumber = sim.SpawnStructure(EntityKind.LumberCamp, 0, new Cell(22, 19), false); // nearest, wrong kind
            Entity mining = sim.SpawnStructure(EntityKind.MiningCamp, 0, new Cell(26, 22), false);
            Entity v = sim.SpawnUnit(EntityKind.Villager, 0, new Cell(21, 23));
            int gold0 = sim.Players[0].Gold;

            Run(sim, 1, Command.Gather(0, new[] { v.Id }, gold.Id));
            RunUntil(sim, 1000, () => sim.Players[0].Gold > gold0, "a gold deposit");
            Assert.LessOrEqual(mining.Footprint.DistanceTo(v.Cell), 1, "deposited at the mining camp");
            Assert.Greater(lumber.Footprint.DistanceTo(v.Cell), 1);
        }

        [Test]
        public void SwitchingResourceDropsTheCarryInsteadOfConvertingIt()
        {
            Entity tc;
            var sim = Map(out tc);
            Entity tree = sim.SpawnStructure(EntityKind.Tree, SimConstants.NeutralOwner, new Cell(9, 3), false);
            Entity bush = sim.SpawnStructure(EntityKind.Berries, SimConstants.NeutralOwner, new Cell(9, 6), false);
            Entity v = sim.SpawnUnit(EntityKind.Villager, 0, new Cell(8, 4));
            int wood0 = sim.Players[0].Wood, food0 = sim.Players[0].Food;

            Run(sim, 1, Command.Gather(0, new[] { v.Id }, tree.Id));
            RunUntil(sim, 200, () => v.Carry >= 5, "five wood carried");
            Run(sim, 1, Command.Gather(0, new[] { v.Id }, bush.Id));
            RunUntil(sim, 200, () => v.CarryKind == ResourceKind.Food, "switch to food");
            Assert.LessOrEqual(v.Carry, 1, "the wood was dropped, not converted");
            RunUntil(sim, 1000, () => sim.Players[0].Food > food0, "a food deposit");
            Assert.AreEqual(wood0, sim.Players[0].Wood);
            Assert.AreEqual(food0 + SimConstants.VillagerCarryCapacity, sim.Players[0].Food);
        }

        // ------------------------------------------------------------------ animals

        [Test]
        public void SheepBelongToWhoeverIsNearAndAreStolenWhenOnlyTheEnemyIs()
        {
            var sim = new Simulation(32, 32, 2, 1);
            Entity sheep = sim.SpawnUnit(EntityKind.Sheep, SimConstants.NeutralOwner, new Cell(10, 10));
            Entity mine = sim.SpawnUnit(EntityKind.Villager, 0, new Cell(12, 10));
            Run(sim, 6);
            Assert.AreEqual(0, sheep.Owner, "claimed by the first player to come near");

            Entity theirs = sim.SpawnUnit(EntityKind.Villager, 1, new Cell(8, 10));
            Run(sim, 20);
            Assert.AreEqual(0, sheep.Owner, "not stolen while the owner is still near");

            Run(sim, 1, Command.Move(0, new[] { mine.Id }, new Cell(28, 28)));
            RunUntil(sim, 200, () => sheep.Owner == 1, "the steal");
            Assert.AreEqual(0, sim.CountUnits(1) - 1, "sheep do not count as population");
            Assert.AreEqual(theirs.Owner, sheep.Owner);
        }

        [Test]
        public void LoneVillagerKillsADeerAndBringsBackFood()
        {
            Entity tc;
            var sim = Map(out tc);
            Entity deer = sim.SpawnUnit(EntityKind.Deer, SimConstants.NeutralOwner, new Cell(14, 4));
            Entity v = sim.SpawnUnit(EntityKind.Villager, 0, new Cell(6, 4));
            int food0 = sim.Players[0].Food;
            Run(sim, 1, Command.Gather(0, new[] { v.Id }, deer.Id));
            RunUntil(sim, 600, () => deer.State == UnitState.Carcass, "the deer to die");
            Assert.AreEqual(SimConstants.NeutralOwner, deer.Owner);
            RunUntil(sim, 1500, () => sim.Players[0].Food > food0, "venison at the Town Center");
            Assert.AreEqual(v.Stats.MaxHp, v.Hp, "deer do not fight back");
        }

        [Test]
        public void BoarFightsBack()
        {
            Entity tc;
            var sim = Map(out tc);
            Entity boar = sim.SpawnUnit(EntityKind.Boar, SimConstants.NeutralOwner, new Cell(14, 4));
            Entity v = sim.SpawnUnit(EntityKind.Villager, 0, new Cell(6, 4));
            Run(sim, 1, Command.Gather(0, new[] { v.Id }, boar.Id));
            RunUntil(sim, 600, () => v.Hp < v.Stats.MaxHp, "the boar to hit back");
            Assert.AreEqual(UnitState.Attacking, boar.State);
            Assert.AreEqual(v.Id, boar.TargetId);
            RunUntil(sim, 600, () => boar.State == UnitState.Carcass, "the villager to win");
            Assert.Greater(v.Hp, 0);
        }

        [Test]
        public void MilitaryDoNotAutoAttackAnimals()
        {
            var sim = new Simulation(32, 32, 2, 1);
            Entity m = sim.SpawnUnit(EntityKind.Militia, 0, new Cell(5, 5));
            sim.SpawnUnit(EntityKind.Deer, SimConstants.NeutralOwner, new Cell(7, 5));
            Run(sim, 30);
            Assert.AreEqual(UnitState.Idle, m.State);
        }

        // ------------------------------------------------------------------ farms

        [Test]
        public void ExhaustedFarmReseedsOnceForWood()
        {
            Entity tc;
            var sim = Map(out tc);
            Entity farm = sim.SpawnStructure(EntityKind.Farm, 0, new Cell(6, 2), false);
            farm.Amount = 3;
            Entity v = sim.SpawnUnit(EntityKind.Villager, 0, new Cell(6, 5));
            int wood0 = sim.Players[0].Wood;
            int reseed = sim.Players[0].Stats.Of(EntityKind.Farm).Cost.Wood;

            Run(sim, 1, Command.Gather(0, new[] { v.Id }, farm.Id));
            RunUntil(sim, 400, () => sim.Players[0].Wood < wood0, "the reseed");
            Assert.AreEqual(wood0 - reseed, sim.Players[0].Wood, "charged once");
            Run(sim, 300);
            Assert.AreEqual(wood0 - reseed, sim.Players[0].Wood, "still only once");
            Assert.Greater(farm.Amount, 100);
            Assert.IsTrue(farm.Alive, "farms are never removed when empty");
        }

        [Test]
        public void FarmerIdlesWhenTheReseedCannotBePaid()
        {
            Entity tc;
            var sim = Map(out tc);
            Entity farm = sim.SpawnStructure(EntityKind.Farm, 0, new Cell(6, 2), false);
            farm.Amount = 3;
            Entity v = sim.SpawnUnit(EntityKind.Villager, 0, new Cell(6, 5));
            sim.Players[0].Wood = 0;
            Run(sim, 1, Command.Gather(0, new[] { v.Id }, farm.Id));
            RunUntil(sim, 400, () => v.State == UnitState.Idle, "the farmer to give up");
            Assert.AreEqual(0, farm.Amount);
            Assert.AreEqual(0, sim.Players[0].Wood);
        }

        // ------------------------------------------------------------------ housing

        [Test]
        public void TrainingIsHeldWhenHousedAndResumesWhenAHouseCompletes()
        {
            Entity tc;
            var sim = Map(out tc);
            var villagers = new List<Entity>();
            for (int i = 0; i < 10; i++) villagers.Add(sim.SpawnUnit(EntityKind.Villager, 0, new Cell(8 + i, 10)));
            Run(sim, 1, Command.Train(0, tc.Id, EntityKind.Villager));
            Assert.AreEqual(1, tc.TrainQueue.Count, "queued, not rejected");
            Assert.AreEqual(10, sim.Players[0].PopulationCap);

            Run(sim, 400);
            Assert.AreEqual(10, sim.CountUnits(0), "held while housed");
            Assert.AreEqual(1, tc.TrainQueue.Count);
            Assert.AreEqual(0, tc.TrainProgress);

            Run(sim, 1, Command.Build(0, new[] { villagers[0].Id, villagers[1].Id }, EntityKind.House, new Cell(12, 14)));
            Entity house = Scenarios.FindOwned(sim, 0, EntityKind.House);
            RunUntil(sim, 400, () => !house.UnderConstruction, "the house");
            Run(sim, 1);
            Assert.AreEqual(15, sim.Players[0].PopulationCap);
            RunUntil(sim, 300, () => sim.CountUnits(0) == 11, "the held villager");
        }

        [Test]
        public void PopulationCapStopsAt75()
        {
            Entity tc;
            var sim = Map(out tc);
            for (int i = 0; i < 20; i++) sim.SpawnStructure(EntityKind.House, 0, new Cell(2 + (i % 10) * 3, 20 + (i / 10) * 3), false);
            Run(sim, 1);
            Assert.AreEqual(SimConstants.MaxPopulation, sim.Players[0].PopulationCap);
        }

        // ------------------------------------------------------------------ rally points

        [Test]
        public void TrainedUnitWalksToTheRallyPoint()
        {
            var sim = new Simulation(32, 32, 2, 1);
            Entity barracks = sim.SpawnStructure(EntityKind.Barracks, 0, new Cell(4, 4), false);
            sim.SpawnStructure(EntityKind.House, 0, new Cell(1, 1), false);
            sim.Players[0].Set(new Cost(1000, 1000, 1000, 1000));
            Run(sim, 1, Command.SetRally(0, barracks.Id, new Cell(20, 18)), Command.Train(0, barracks.Id, EntityKind.Militia));
            RunUntil(sim, 300, () => Scenarios.FindOwned(sim, 0, EntityKind.Militia) != null, "the militia");
            Entity m = Scenarios.FindOwned(sim, 0, EntityKind.Militia);
            Assert.AreEqual(UnitState.Moving, m.State);
            RunUntil(sim, 400, () => m.Cell == new Cell(20, 18), "arrival at the rally point");
        }

        [Test]
        public void VillagerRalliedToAResourceStartsGathering()
        {
            Entity tc;
            var sim = Map(out tc);
            Entity tree = sim.SpawnStructure(EntityKind.Tree, SimConstants.NeutralOwner, new Cell(14, 4), false);
            Run(sim, 1, Command.SetRally(0, tc.Id, default(Cell), tree.Id), Command.Train(0, tc.Id, EntityKind.Villager));
            RunUntil(sim, 300, () => Scenarios.FindOwned(sim, 0, EntityKind.Villager) != null, "the villager");
            Entity v = Scenarios.FindOwned(sim, 0, EntityKind.Villager);
            Assert.AreEqual(UnitState.Gathering, v.State);
            Assert.AreEqual(tree.Id, v.GatherSourceId);
        }

        // ------------------------------------------------------------------ repair

        [Test]
        public void RepairRaisesHitPointsAndChargesHalfThePriceOverAFullRepair()
        {
            Entity tc;
            var sim = Map(out tc);
            Entity barracks = sim.SpawnStructure(EntityKind.Barracks, 0, new Cell(10, 2), false);
            int max = barracks.Stats.MaxHp;
            barracks.Hp = max / 2;
            Entity v = sim.SpawnUnit(EntityKind.Villager, 0, new Cell(9, 5));
            int wood0 = sim.Players[0].Wood;
            int perTick = (max + barracks.Stats.BuildTicks - 1) / barracks.Stats.BuildTicks;

            Run(sim, 1, Command.Repair(0, new[] { v.Id }, barracks.Id));
            RunUntil(sim, 50, () => barracks.Hp > max / 2, "repair to start");
            int hp = barracks.Hp;
            Run(sim, 10);
            Assert.AreEqual(hp + 10 * perTick, barracks.Hp, "hit points rise at the build rate");
            int repaired = barracks.Hp - max / 2;
            Assert.AreEqual(wood0 - barracks.Stats.Cost.Wood * repaired / (2 * max), sim.Players[0].Wood, "wood falls in step");

            RunUntil(sim, 1000, () => barracks.Hp == max, "full repair");
            Assert.AreEqual(wood0 - barracks.Stats.Cost.Wood * (max / 2) / (2 * max), sim.Players[0].Wood, "half the price for the missing half");
            Run(sim, 2);
            Assert.AreEqual(UnitState.Idle, v.State);
        }

        [Test]
        public void RepairStopsWhenTheOwnerCannotPay()
        {
            Entity tc;
            var sim = Map(out tc);
            Entity barracks = sim.SpawnStructure(EntityKind.Barracks, 0, new Cell(10, 2), false);
            barracks.Hp = 100;
            Entity v = sim.SpawnUnit(EntityKind.Villager, 0, new Cell(9, 5));
            sim.Players[0].Wood = 0;
            Run(sim, 1, Command.Repair(0, new[] { v.Id }, barracks.Id));
            RunUntil(sim, 200, () => v.State == UnitState.Idle, "repair to stop");
            Assert.Less(barracks.Hp, 140, "only the free first few points were repaired");
        }

        // ------------------------------------------------------------------ auto-villager

        [Test]
        public void AutoVillagerKeepsOneQueuedAndSwitchesOffAt40()
        {
            Entity tc;
            var sim = Map(out tc);
            for (int i = 0; i < 7; i++) sim.SpawnStructure(EntityKind.House, 0, new Cell(2 + i * 3, 28), false);
            for (int i = 0; i < 37; i++) sim.SpawnUnit(EntityKind.Villager, 0, new Cell(8 + (i % 20), 12 + i / 20));
            sim.Players[0].Food = 1000;

            Run(sim, 1, Command.SetAutoQueue(0, tc.Id, true));
            Assert.AreEqual(1, tc.TrainQueue.Count, "queued straight away");
            RunUntil(sim, 300, () => sim.CountUnits(0) == 38, "the first villager");
            Run(sim, 1);
            Assert.AreEqual(1, tc.TrainQueue.Count, "queue refilled");

            RunUntil(sim, 1000, () => !tc.AutoQueue, "the toggle to flip");
            Assert.GreaterOrEqual(sim.Players[0].Population, SimConstants.AutoVillagerStopPopulation);
            Run(sim, 500);
            Assert.AreEqual(0, tc.TrainQueue.Count);
            Assert.AreEqual(SimConstants.AutoVillagerStopPopulation, sim.CountUnits(0));
        }

        // ------------------------------------------------------------------ economy planner

        /// <summary>A Town Center with plenty of every resource around it and some idle villagers.</summary>
        private static Simulation PlannerMap(int seed, int villagers, out List<Entity> vs)
        {
            var sim = new Simulation(40, 40, 2, (ulong)seed);
            var rng = new DeterministicRandom((ulong)seed);
            sim.SpawnStructure(EntityKind.TownCenter, 0, new Cell(18, 18), false);
            for (int i = 0; i < 12; i++) sim.SpawnStructure(EntityKind.House, 0, new Cell(1 + i * 3, 37), false);
            EntityKind[] kinds = { EntityKind.Tree, EntityKind.Berries, EntityKind.GoldMine, EntityKind.StoneMine };
            foreach (EntityKind k in kinds)
            {
                int placed = 0;
                while (placed < 6)
                {
                    var c = new Cell(rng.Next(2, 36), rng.Next(2, 34));
                    if (Cell.Chebyshev(c, new Cell(19, 19)) < 4) continue;
                    if (sim.SpawnStructure(k, SimConstants.NeutralOwner, c, false) != null) placed++;
                }
            }
            vs = new List<Entity>();
            while (vs.Count < villagers)
            {
                var c = new Cell(rng.Next(14, 26), rng.Next(14, 26));
                if (!sim.Map.IsFree(c)) continue;
                vs.Add(sim.SpawnUnit(EntityKind.Villager, 0, c));
            }
            return sim;
        }

        private static int[] Assignment(Simulation sim, int player)
        {
            var counts = new int[4];
            foreach (Entity e in sim.Entities)
            {
                if (!e.Alive || e.Owner != player || e.Kind != EntityKind.Villager) continue;
                if (e.State != UnitState.Gathering && e.State != UnitState.Returning) continue;
                counts[(int)EntityDefs.Get(e.GatherKind).Yields]++;
            }
            return counts;
        }

        [Test]
        public void PlannerSplitsIdleVillagersByTargetShareOn10Maps()
        {
            var cases = new[]
            {
                new { T = new EconomyTargets(45, 35, 15, 5), Want = new[] { 9, 7, 3, 1 } },
                new { T = new EconomyTargets(25, 25, 25, 25), Want = new[] { 5, 5, 5, 5 } },
                new { T = new EconomyTargets(50, 50, 0, 0), Want = new[] { 10, 10, 0, 0 } },
            };
            for (int seed = 1; seed <= 10; seed++)
            {
                foreach (var c in cases)
                {
                    List<Entity> vs;
                    Simulation sim = PlannerMap(seed, 20, out vs);
                    Run(sim, SimConstants.PlannerInterval + 1, Command.SetEconomyTargets(0, c.T));
                    CollectionAssert.AreEqual(c.Want, Assignment(sim, 0), "seed " + seed + " targets " + c.T);
                }
            }
        }

        [Test]
        public void PlannerNeverMovesVillagersOnManualOrders()
        {
            List<Entity> vs;
            Simulation sim = PlannerMap(3, 20, out vs);
            Entity tree = Scenarios.FindOwned(sim, SimConstants.NeutralOwner, EntityKind.Tree);
            var manual = new[] { vs[0].Id, vs[1].Id, vs[2].Id, vs[3].Id, vs[4].Id, vs[5].Id, vs[6].Id, vs[7].Id, vs[8].Id, vs[9].Id };
            Run(sim, 1, Command.Gather(0, manual, tree.Id), Command.SetEconomyTargets(0, new EconomyTargets(45, 35, 15, 5)));
            Run(sim, 200);
            for (int i = 0; i < 10; i++) Assert.AreEqual(EntityKind.Tree, vs[i].GatherKind, "manual villager " + i + " kept on wood");
            // Ten on wood already exceed the 35% target, so the ten idle ones go to the biggest
            // remaining gaps: food until it ties gold at 300, then alternating food and gold.
            CollectionAssert.AreEqual(new[] { 8, 10, 2, 0 }, Assignment(sim, 0));
        }

        [Test]
        public void PlannerSendsNewVillagersToTheBiggestGap()
        {
            List<Entity> vs;
            Simulation sim = PlannerMap(5, 0, out vs);
            Entity tc = Scenarios.FindOwned(sim, 0, EntityKind.TownCenter);
            Run(sim, 1, Command.SetEconomyTargets(0, new EconomyTargets(0, 0, 100, 0)), Command.Train(0, tc.Id, EntityKind.Villager));
            RunUntil(sim, 300, () => Scenarios.FindOwned(sim, 0, EntityKind.Villager) != null, "the villager");
            Run(sim, SimConstants.PlannerInterval + 1);
            Entity v = Scenarios.FindOwned(sim, 0, EntityKind.Villager);
            Assert.AreEqual(EntityKind.GoldMine, v.GatherKind);
        }

        [Test]
        public void InvalidTargetsAreRejected()
        {
            Entity tc;
            var sim = Map(out tc);
            Run(sim, 1, Command.SetEconomyTargets(0, new EconomyTargets(50, 30, 10, 0)));
            Assert.AreEqual("economy targets must add up to 100", sim.LastRejection);
            Assert.IsTrue(sim.Players[0].EconomyTargets.IsOff);
        }
    }
}
