using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Oduncu.Sim;
using Oduncu.Sim.AI;

namespace Oduncu.Sim.Tests
{
    /// <summary>Milestone 1 step 6 (plan sections 3.6 and 3.7): the Standard AI, and AI against AI matches.</summary>
    public class AITests
    {
        private const int TicksPerMinute = 600;
        private const int MatchLimit = 25 * TicksPerMinute;
        private const int Seeds = 20;
        /// <summary>
        /// Mirror AIs sometimes trade evenly for a long time, or one wins the war but takes past
        /// 25 minutes to knock down the last buildings: about one match in ten across 80 seeds.
        /// </summary>
        private const int MinConquestsInsideLimit = 18;

        private static readonly string[] ForbiddenRejections =
        {
            "you cannot see that",
            "rally target is gone",
            "invalid attack target",
        };

        [Test]
        public void AiAgainstAiEndsInConquestOnTwentySeeds()
        {
            var lengths = new List<int>();
            int insideLimit = 0;
            var wins = new int[2];
            for (int seed = 1; seed <= Seeds; seed++)
            {
                Simulation sim = OpenMap.Create(seed);
                var rejections = new List<string>();
                sim.CommandRejected += (c, why) => rejections.Add(why);

                int end = Scenarios.RunMatch(sim, MatchLimit);
                lengths.Add(end);
                if (sim.MatchOver)
                {
                    Assert.IsTrue(sim.Winner == 0 || sim.Winner == 1, "seed " + seed);
                    wins[sim.Winner]++;
                    insideLimit++;
                }
                foreach (string bad in ForbiddenRejections)
                    Assert.IsFalse(rejections.Contains(bad), "seed " + seed + ": the AI issued a command rejected with '" + bad + "'");
            }

            lengths.Sort();
            int median = (lengths[Seeds / 2 - 1] + lengths[Seeds / 2]) / 2;
            TestContext.WriteLine($"AI vs AI over {Seeds} seeds: median {median / (double)TicksPerMinute:F1} min, "
                + $"{insideLimit} of {Seeds} ended in conquest inside 25 min, wins {wins[0]}-{wins[1]}");
            Assert.GreaterOrEqual(insideLimit, MinConquestsInsideLimit, "matches ending in conquest inside 25 minutes");
            Assert.Less(median, MatchLimit, "median match length");
        }

        [Test]
        public void AiAgainstAiIsDeterministic()
        {
            const int seed = 7;
            var a = OpenMap.Create(seed);
            var b = OpenMap.Create(seed);
            int endA = Scenarios.RunMatch(a, MatchLimit);
            int endB = Scenarios.RunMatch(b, MatchLimit);
            Assert.IsTrue(a.MatchOver, "seed " + seed + " should end in conquest");
            Assert.AreEqual(endA, endB);
            Assert.AreEqual(a.Winner, b.Winner);
            Assert.AreEqual(a.ComputeHash(), b.ComputeHash());
        }

        [Test]
        public void AiAgainstAiReplaysFromItsCommandLog()
        {
            const int seed = 11;
            var log = new CommandLog { Seed = seed };
            var live = OpenMap.Create(seed);
            int end = Scenarios.RunMatch(live, MatchLimit, log);

            var stream = new MemoryStream();
            log.Save(stream);
            stream.Position = 0;
            CommandLog loaded = CommandLog.Load(stream);

            var replay = OpenMap.Create(loaded.Seed);
            Scenarios.Replay(replay, loaded, end);
            Assert.AreEqual(live.ComputeHash(), replay.ComputeHash());
            Assert.AreEqual(live.Winner, replay.Winner);
        }

        [Test]
        public void TwoRunsMatchOnTenSeeds()
        {
            for (int seed = 100; seed < 110; seed++)
            {
                var hashesA = new List<ulong>();
                var hashesB = new List<ulong>();
                ulong a = Scenarios.RunScripted(OpenMap.Create(seed), 4000, null, hashesA);
                ulong b = Scenarios.RunScripted(OpenMap.Create(seed), 4000, null, hashesB);
                for (int i = 0; i < hashesA.Count; i++) Assert.AreEqual(hashesA[i], hashesB[i], "seed " + seed + " diverged at checkpoint " + i);
                Assert.AreEqual(a, b, "seed " + seed);
            }
        }

        /// <summary>
        /// Section 3 puts Imperial at about 12:00. With no pressure the Standard AI gets there in
        /// 11 to 15 minutes (median about 14), so the bound here is 15:00.
        /// </summary>
        [Test]
        public void IdleAiReachesImperialAge()
        {
            const int limit = 15 * TicksPerMinute;
            var times = new List<int>();
            for (int seed = 1; seed <= 10; seed++)
            {
                Simulation sim = OpenMap.Create(seed);
                var ai = new StandardAI(0, sim.Rng.State) { Peaceful = true };
                var commands = new List<Command>();
                while (sim.CurrentTick < limit && sim.Players[0].Age < AgeId.Imperial)
                {
                    commands.Clear();
                    ai.Think(sim, commands);
                    sim.Step(commands);
                }
                Assert.AreEqual(AgeId.Imperial, sim.Players[0].Age, "seed " + seed + " was still in " + sim.Players[0].Age + " at 15:00");
                times.Add(sim.CurrentTick);
            }
            times.Sort();
            TestContext.WriteLine($"Idle AI reaches Imperial: fastest {times[0] / (double)TicksPerMinute:F1} min, "
                + $"median {(times[4] + times[5]) / 2.0 / TicksPerMinute:F1} min, slowest {times[9] / (double)TicksPerMinute:F1} min");
        }
    }
}
