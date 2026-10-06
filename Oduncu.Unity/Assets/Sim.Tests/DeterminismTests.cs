using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Oduncu.Sim;

namespace Oduncu.Sim.Tests
{
    public class DeterminismTests
    {
        [Test]
        public void TwoRunsProduceIdenticalHashes()
        {
            var hashesA = new List<ulong>();
            var hashesB = new List<ulong>();
            ulong a = Scenarios.RunScripted(Scenarios.CreateDeterminismScenario(), Scenarios.DeterminismTicks, null, hashesA);
            ulong b = Scenarios.RunScripted(Scenarios.CreateDeterminismScenario(), Scenarios.DeterminismTicks, null, hashesB);
            Assert.AreEqual(hashesA.Count, hashesB.Count);
            for (int i = 0; i < hashesA.Count; i++) Assert.AreEqual(hashesA[i], hashesB[i], "hash diverged at checkpoint " + i);
            Assert.AreEqual(a, b);
        }

        [Test]
        public void ReplayOfSavedLogMatchesLiveRun()
        {
            var log = new CommandLog { Seed = Scenarios.DeterminismSeed };
            var live = new List<ulong>();
            ulong liveHash = Scenarios.RunScripted(Scenarios.CreateDeterminismScenario(), Scenarios.DeterminismTicks, log, live);
            Assert.Greater(log.Entries.Count, 50, "scenario should issue plenty of commands");

            var stream = new MemoryStream();
            log.Save(stream);
            stream.Position = 0;
            CommandLog loaded = CommandLog.Load(stream);
            Assert.AreEqual(log.Entries.Count, loaded.Entries.Count);

            var replayed = new List<ulong>();
            ulong replayHash = Scenarios.Replay(MapGenerator.CreateDefault(loaded.Seed), loaded, Scenarios.DeterminismTicks, replayed);
            for (int i = 0; i < live.Count; i++) Assert.AreEqual(live[i], replayed[i], "replay diverged at checkpoint " + i);
            Assert.AreEqual(liveHash, replayHash);
        }

        [Test]
        public void ScenarioActuallyPlaysOut()
        {
            var sim = Scenarios.CreateDeterminismScenario();
            Scenarios.RunScripted(sim, Scenarios.DeterminismTicks);
            Assert.IsNotNull(Scenarios.FindOwned(sim, 0, EntityKind.Barracks), "player 0 should have built a barracks");
            Assert.IsNotNull(Scenarios.FindOwned(sim, 1, EntityKind.Barracks), "player 1 should have built a barracks");
            Assert.Greater(sim.CountUnits(0), 3, "player 0 should have trained units");
            Assert.Greater(sim.CountUnits(1), 3, "player 1 should have trained units");
        }

        [Test]
        public void HashIsSensitiveToOneDifferentCommand()
        {
            var a = Scenarios.CreateDeterminismScenario();
            var b = Scenarios.CreateDeterminismScenario();
            var none = new List<Command>();
            Entity v = Scenarios.FindOwned(b, 0, EntityKind.Villager);
            var moveOne = new List<Command> { Command.Move(0, new[] { v.Id }, new Cell(20, 20)) };
            a.Step(none);
            b.Step(moveOne);
            for (int i = 0; i < 50; i++) { a.Step(none); b.Step(none); }
            Assert.AreNotEqual(a.ComputeHash(), b.ComputeHash());
        }

        [Test]
        public void CommandOrderInsideUnitListDoesNotMatter()
        {
            var a = Scenarios.CreateDeterminismScenario();
            var b = Scenarios.CreateDeterminismScenario();
            List<Entity> vs = Scenarios.CollectOwned(a, 0, EntityKind.Villager);
            var ca = new List<Command> { Command.Move(0, new[] { vs[0].Id, vs[1].Id, vs[2].Id }, new Cell(20, 20)) };
            var cb = new List<Command> { Command.Move(0, new[] { vs[2].Id, vs[0].Id, vs[1].Id, vs[1].Id }, new Cell(20, 20)) };
            a.Step(ca);
            b.Step(cb);
            Assert.AreEqual(a.ComputeHash(), b.ComputeHash());
        }
    }
}
