using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Oduncu.Sim;

namespace Oduncu.Sim.Tests
{
    /// <summary>Milestone 1 step 1: multi-resource costs, per-player stats, entity pooling and the v2 command log.</summary>
    public class FoundationTests
    {
        private static void Run(Simulation sim, int ticks, params Command[] firstTickCommands)
        {
            var cmds = new List<Command>(firstTickCommands);
            sim.Step(cmds);
            cmds.Clear();
            for (int i = 1; i < ticks; i++) sim.Step(cmds);
        }

        private static Simulation Base(out Entity tc, out Entity barracks, out Entity villager)
        {
            var sim = new Simulation(24, 24, 2, 1);
            tc = sim.SpawnStructure(EntityKind.TownCenter, 0, new Cell(2, 2), false);
            barracks = sim.SpawnStructure(EntityKind.Barracks, 0, new Cell(10, 2), false);
            villager = sim.SpawnUnit(EntityKind.Villager, 0, new Cell(6, 8));
            return sim;
        }

        // ------------------------------------------------------------------ costs

        [TestCase(ResourceKind.Food, "not enough food")]
        [TestCase(ResourceKind.Gold, "not enough gold")]
        public void TrainIsRejectedWhenAnyOneResourceIsShort(ResourceKind shortOf, string message)
        {
            Entity tc, barracks, v;
            var sim = Base(out tc, out barracks, out v);
            sim.Players[0].Set(new Cost(1000, 1000, 1000, 1000));
            Cost militia = EntityDefs.Get(EntityKind.Militia).Cost;
            sim.Players[0].Add(shortOf, -(1000 - militia[shortOf] + 1));
            Run(sim, 1, Command.Train(0, barracks.Id, EntityKind.Militia));
            Assert.AreEqual(0, barracks.TrainQueue.Count);
            Assert.AreEqual(message, sim.LastRejection);
            Assert.AreEqual(militia[shortOf] - 1, sim.Players[0].Get(shortOf), "nothing charged on rejection");
        }

        [Test]
        public void TrainChargesExactlyTheCost()
        {
            Entity tc, barracks, v;
            var sim = Base(out tc, out barracks, out v);
            sim.Players[0].Set(new Cost(1000, 1000, 1000, 1000));
            Run(sim, 1, Command.Train(0, barracks.Id, EntityKind.Militia));
            Cost c = EntityDefs.Get(EntityKind.Militia).Cost;
            Assert.AreEqual(1000 - c.Food, sim.Players[0].Food);
            Assert.AreEqual(1000 - c.Wood, sim.Players[0].Wood);
            Assert.AreEqual(1000 - c.Gold, sim.Players[0].Gold);
            Assert.AreEqual(1000 - c.Stone, sim.Players[0].Stone);
        }

        [Test]
        public void BuildIsRejectedWhenShortAndChargesExactlyWhenAccepted()
        {
            Entity tc, barracks, v;
            var sim = Base(out tc, out barracks, out v);
            Cost price = EntityDefs.Get(EntityKind.Barracks).Cost;
            sim.Players[0].Set(new Cost(500, price.Wood - 1, 500, 500));
            Run(sim, 1, Command.Build(0, new[] { v.Id }, EntityKind.Barracks, new Cell(16, 16)));
            Assert.AreEqual("not enough wood", sim.LastRejection);
            Assert.AreEqual(1, CountKind(sim, EntityKind.Barracks));

            sim.Players[0].Set(new Cost(500, price.Wood, 500, 500));
            Run(sim, 1, Command.Build(0, new[] { v.Id }, EntityKind.Barracks, new Cell(16, 16)));
            Assert.AreEqual(2, CountKind(sim, EntityKind.Barracks));
            Assert.AreEqual(0, sim.Players[0].Wood);
            Assert.AreEqual(500, sim.Players[0].Food);
        }

        [Test]
        public void CancellingAQueuedUnitRefundsItInFull()
        {
            Entity tc, barracks, v;
            var sim = Base(out tc, out barracks, out v);
            var start = new Cost(1000, 1000, 1000, 1000);
            sim.Players[0].Set(start);
            Run(sim, 1, Command.Train(0, barracks.Id, EntityKind.Militia), Command.Train(0, barracks.Id, EntityKind.Militia));
            Assert.AreEqual(2, barracks.TrainQueue.Count);
            Run(sim, 50);
            Assert.Greater(barracks.TrainProgress, 0);

            Run(sim, 1, Command.CancelTrain(0, barracks.Id, 1));
            Assert.AreEqual(1, barracks.TrainQueue.Count);
            Assert.Greater(barracks.TrainProgress, 0, "cancelling a later slot keeps progress on the first");

            Run(sim, 1, Command.CancelTrain(0, barracks.Id, 0));
            Assert.AreEqual(0, barracks.TrainQueue.Count);
            Assert.AreEqual(0, barracks.TrainProgress);
            Assert.AreEqual(start.Food, sim.Players[0].Food);
            Assert.AreEqual(start.Gold, sim.Players[0].Gold);
        }

        [Test]
        public void CancelRejectsBadSlotsAndOtherPlayersBuildings()
        {
            Entity tc, barracks, v;
            var sim = Base(out tc, out barracks, out v);
            Run(sim, 1, Command.CancelTrain(0, barracks.Id, 0));
            Assert.AreEqual("no such queue slot", sim.LastRejection);
            Run(sim, 1, Command.CancelTrain(1, barracks.Id, 0));
            Assert.AreEqual("not your building", sim.LastRejection);
        }

        // ------------------------------------------------------------------ per-player stats

        [Test]
        public void MeleeAttackUpgradeChangesDamageOfThatPlayerOnly()
        {
            var sim = new Simulation(24, 24, 2, 1);
            Entity oldUnit = sim.SpawnUnit(EntityKind.Militia, 0, new Cell(2, 2));
            Entity enemyUnit = sim.SpawnUnit(EntityKind.Militia, 1, new Cell(2, 14));

            Assert.IsTrue(sim.CompleteResearch(0, TechId.MeleeAttack1));
            Assert.IsFalse(sim.CompleteResearch(0, TechId.MeleeAttack1), "researching twice is refused");
            Entity newUnit = sim.SpawnUnit(EntityKind.Militia, 0, new Cell(14, 2));

            int baseAttack = EntityDefs.Get(EntityKind.Militia).Attack;
            int armor = EntityDefs.Get(EntityKind.Villager).MeleeArmor;
            Assert.AreEqual(baseAttack + 1, sim.Players[0].Stats.Of(EntityKind.Militia).Attack);
            Assert.AreEqual(baseAttack, sim.Players[1].Stats.Of(EntityKind.Militia).Attack);

            Assert.AreEqual(baseAttack + 1 - armor, FirstHitDamage(sim, oldUnit, 1));
            Assert.AreEqual(baseAttack + 1 - armor, FirstHitDamage(sim, newUnit, 1));
            Assert.AreEqual(baseAttack - armor, FirstHitDamage(sim, enemyUnit, 0));
        }

        [Test]
        public void UpgradeDoesNotTouchUnrelatedKinds()
        {
            var sim = new Simulation(24, 24, 2, 1);
            sim.CompleteResearch(0, TechId.MeleeAttack1);
            Assert.AreEqual(EntityDefs.Get(EntityKind.Villager).Attack, sim.Players[0].Stats.Of(EntityKind.Villager).Attack);
        }

        /// <summary>Put a fresh villager of the given owner next to the attacker and measure the first hit on it.</summary>
        private static int FirstHitDamage(Simulation sim, Entity attacker, int targetOwner)
        {
            Entity target = sim.SpawnUnit(EntityKind.Villager, targetOwner, new Cell(attacker.Cell.X + 1, attacker.Cell.Y));
            int before = target.Hp;
            var none = new List<Command>();
            sim.Step(new List<Command> { Command.Attack(attacker.Owner, new[] { attacker.Id }, target.Id) });
            for (int i = 0; i < 40 && target.Hp == before; i++) sim.Step(none);
            return before - target.Hp;
        }

        // ------------------------------------------------------------------ pooling

        [Test]
        public void DeadEntitiesAreRecycledWithFreshIds()
        {
            var sim = new Simulation(24, 24, 2, 1);
            Entity tree = sim.SpawnStructure(EntityKind.Tree, SimConstants.NeutralOwner, new Cell(5, 5), false);
            int oldId = tree.Id;
            Entity m = sim.SpawnUnit(EntityKind.Militia, 0, new Cell(2, 2));
            Entity enemy = sim.SpawnUnit(EntityKind.Villager, 1, new Cell(3, 2));
            int enemyId = enemy.Id;
            Run(sim, 200, Command.Attack(0, new[] { m.Id }, enemyId));
            Assert.IsNull(sim.Find(enemyId));

            Entity spawned = sim.SpawnUnit(EntityKind.Villager, 1, new Cell(10, 10));
            Assert.Greater(spawned.Id, enemyId, "ids are never reused");
            Assert.IsNull(sim.Find(enemyId));
            Assert.AreEqual(oldId, sim.Find(oldId).Id);
            Assert.AreEqual(UnitState.Idle, spawned.State);
            Assert.AreEqual(spawned.Stats.MaxHp, spawned.Hp);
            Assert.AreEqual(0, spawned.Carry);
        }

        // ------------------------------------------------------------------ command log

        [Test]
        public void VersionOneLogsAreRejectedWithAClearError()
        {
            var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write(1);
                w.Write(4242);
                w.Write(0);
            }
            stream.Position = 0;
            var ex = Assert.Throws<InvalidDataException>(() => CommandLog.Load(stream));
            StringAssert.Contains("version 1", ex.Message);
            StringAssert.Contains("version " + CommandLog.FormatVersion, ex.Message);
        }

        [Test]
        public void VersionTwoRoundTripsNewFields()
        {
            var log = new CommandLog { Seed = 7 };
            var cmd = Command.CancelTrain(1, 42, 3);
            cmd.Cell2 = new Cell(9, 11);
            log.Record(5, new List<Command> { cmd });
            var stream = new MemoryStream();
            log.Save(stream);
            stream.Position = 0;
            CommandLog loaded = CommandLog.Load(stream);
            Command back = loaded.Entries[0].Command;
            Assert.AreEqual(CommandKind.CancelTrain, back.Kind);
            Assert.AreEqual(1, back.Player);
            Assert.AreEqual(42, back.Target);
            Assert.AreEqual(3, back.Arg);
            Assert.AreEqual(new Cell(9, 11), back.Cell2);
            Assert.AreEqual(5, loaded.Entries[0].Tick);
        }

        private static int CountKind(Simulation sim, EntityKind kind)
        {
            int n = 0;
            foreach (Entity e in sim.Entities) if (e.Alive && e.Kind == kind) n++;
            return n;
        }
    }
}
