using System.Collections.Generic;
using NUnit.Framework;
using Oduncu.Sim;

namespace Oduncu.Sim.Tests
{
    public class GameplayTests
    {
        private static void Run(Simulation sim, int ticks, params Command[] firstTickCommands)
        {
            var cmds = new List<Command>(firstTickCommands);
            sim.Step(cmds);
            cmds.Clear();
            for (int i = 1; i < ticks; i++) sim.Step(cmds);
        }

        private static Simulation SmallMap(out Entity tc, out Entity villager, out Entity tree)
        {
            var sim = new Simulation(20, 20, 2, 1);
            tc = sim.SpawnStructure(EntityKind.TownCenter, 0, new Cell(2, 2), false);
            villager = sim.SpawnUnit(EntityKind.Villager, 0, new Cell(6, 3));
            tree = sim.SpawnStructure(EntityKind.Tree, SimConstants.NeutralOwner, new Cell(10, 3), false);
            return sim;
        }

        [Test]
        public void VillagerGathersAndDepositsWood()
        {
            Entity tc, v, tree;
            var sim = SmallMap(out tc, out v, out tree);
            int woodBefore = sim.Players[0].Wood;
            Run(sim, 600, Command.Gather(0, new[] { v.Id }, tree.Id));
            Assert.Greater(sim.Players[0].Wood, woodBefore, "wood should have been deposited");
            Assert.Less(tree.Amount, 100, "tree should have been chopped");
            Assert.IsTrue(v.State == UnitState.Gathering || v.State == UnitState.Returning);
        }

        [Test]
        public void VillagerBuildsBarracksAndTrainsMilitia()
        {
            Entity tc, v, tree;
            var sim = SmallMap(out tc, out v, out tree);
            sim.Players[0].Wood = 500;
            Run(sim, 1, Command.Build(0, new[] { v.Id }, EntityKind.Barracks, new Cell(6, 8)));
            Entity barracks = Scenarios.FindOwned(sim, 0, EntityKind.Barracks);
            Assert.IsNotNull(barracks);
            Assert.IsTrue(barracks.UnderConstruction);
            Assert.AreEqual(500 - 175, sim.Players[0].Wood);

            Run(sim, 500);
            Assert.IsFalse(barracks.UnderConstruction, "barracks should be complete");
            Assert.AreEqual(barracks.Def.MaxHp, barracks.Hp);
            Assert.AreEqual(UnitState.Idle, v.State);

            Run(sim, 1, Command.Train(0, barracks.Id, EntityKind.Militia));
            Assert.AreEqual(1, barracks.TrainQueue.Count);
            Run(sim, 250);
            Assert.IsNotNull(Scenarios.FindOwned(sim, 0, EntityKind.Militia));
            Assert.AreEqual(0, barracks.TrainQueue.Count);
        }

        [Test]
        public void BuilderReturnsToPreviousTree()
        {
            Entity tc, v, tree;
            var sim = SmallMap(out tc, out v, out tree);
            sim.Players[0].Wood = 500;
            Run(sim, 50, Command.Gather(0, new[] { v.Id }, tree.Id));
            Run(sim, 500, Command.Build(0, new[] { v.Id }, EntityKind.Barracks, new Cell(6, 8)));
            Assert.IsFalse(Scenarios.FindOwned(sim, 0, EntityKind.Barracks).UnderConstruction);
            Assert.IsTrue(v.State == UnitState.Gathering || v.State == UnitState.Returning, "state was " + v.State);
            Assert.AreEqual(tree.Id, v.GatherSourceId);
        }

        [Test]
        public void TownCenterTrainsVillagerAndChargesFood()
        {
            Entity tc, v, tree;
            var sim = SmallMap(out tc, out v, out tree);
            Run(sim, 1, Command.Train(0, tc.Id, EntityKind.Villager));
            Assert.AreEqual(SimConstants.StartingResources.Food - 50, sim.Players[0].Food);
            Assert.AreEqual(SimConstants.StartingResources.Wood, sim.Players[0].Wood);
            Run(sim, 210);
            Assert.AreEqual(2, sim.CountUnits(0));
        }

        [Test]
        public void TrainIsRejectedWithoutFood()
        {
            Entity tc, v, tree;
            var sim = SmallMap(out tc, out v, out tree);
            sim.Players[0].Food = 10;
            Run(sim, 1, Command.Train(0, tc.Id, EntityKind.Villager));
            Assert.AreEqual(0, tc.TrainQueue.Count);
            Assert.AreEqual("not enough food", sim.LastRejection);
        }

        [Test]
        public void MilitiaKillsEnemyVillager()
        {
            var sim = new Simulation(20, 20, 2, 1);
            Entity m = sim.SpawnUnit(EntityKind.Militia, 0, new Cell(2, 2));
            Entity enemy = sim.SpawnUnit(EntityKind.Villager, 1, new Cell(8, 2));
            Run(sim, 400, Command.Attack(0, new[] { m.Id }, enemy.Id));
            Assert.IsFalse(enemy.Alive);
            Assert.IsNull(sim.Find(enemy.Id));
            Assert.AreEqual(UnitState.Idle, m.State);
        }

        [Test]
        public void IdleMilitiaAutoEngagesNearbyEnemy()
        {
            var sim = new Simulation(20, 20, 2, 1);
            Entity m = sim.SpawnUnit(EntityKind.Militia, 0, new Cell(2, 2));
            Entity enemy = sim.SpawnUnit(EntityKind.Villager, 1, new Cell(5, 2));
            Run(sim, 20);
            Assert.AreEqual(UnitState.Attacking, m.State);
            Assert.AreEqual(enemy.Id, m.TargetId);
        }

        [Test]
        public void CannotCommandEnemyUnits()
        {
            var sim = new Simulation(20, 20, 2, 1);
            Entity enemy = sim.SpawnUnit(EntityKind.Villager, 1, new Cell(5, 5));
            Run(sim, 5, Command.Move(0, new[] { enemy.Id }, new Cell(1, 1)));
            Assert.AreEqual(UnitState.Idle, enemy.State);
        }

        [Test]
        public void BuildingDestroyedFreesCells()
        {
            var sim = new Simulation(20, 20, 2, 1);
            Entity b = sim.SpawnStructure(EntityKind.Barracks, 1, new Cell(5, 5), false);
            b.Hp = 3;
            Entity m = sim.SpawnUnit(EntityKind.Militia, 0, new Cell(2, 5));
            Run(sim, 200, Command.Attack(0, new[] { m.Id }, b.Id));
            Assert.IsFalse(b.Alive);
            Assert.IsTrue(sim.Map.IsFree(6, 6));
        }

        [Test]
        public void MoveArrivesAtTarget()
        {
            var sim = new Simulation(20, 20, 1, 1);
            Entity v = sim.SpawnUnit(EntityKind.Villager, 0, new Cell(1, 1));
            Run(sim, 300, Command.Move(0, new[] { v.Id }, new Cell(10, 10)));
            Assert.AreEqual(new Cell(10, 10), v.Cell);
            Assert.AreEqual(UnitState.Idle, v.State);
        }
    }
}
