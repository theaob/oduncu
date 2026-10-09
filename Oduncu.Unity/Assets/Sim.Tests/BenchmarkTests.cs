using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using Oduncu.Sim;

namespace Oduncu.Sim.Tests
{
    /// <summary>
    /// Headless tick-time benchmark at full 1v1 population (plan step 1, item 7). It reports and
    /// never fails on time: CI machines vary, and the gate is the on-device number (under 4 ms
    /// per tick on a 2020 mid-range phone). CI runs it as its own step and writes the numbers to
    /// the job summary.
    /// </summary>
    [Category("Benchmark")]
    public class BenchmarkTests
    {
        private const int Ticks = 2000;
        private const int WarmUpTicks = 100;

        [Test]
        public void FullPopulationTickTime()
        {
            // Warm up the JIT on a throwaway copy so the first timed tick is not a compile.
            Run(Scenarios.CreateFullPopulationBenchmark(1), WarmUpTicks, null);

            Simulation sim = Scenarios.CreateFullPopulationBenchmark(1);
            int startEntities = sim.Entities.Count;
            var times = new double[Ticks];
            Run(sim, Ticks, times);

            double sum = 0, worst = 0;
            for (int i = 0; i < times.Length; i++)
            {
                sum += times[i];
                if (times[i] > worst) worst = times[i];
            }
            var sorted = (double[])times.Clone();
            Array.Sort(sorted);
            double p95 = sorted[(int)(sorted.Length * 0.95)];
            double mean = sum / times.Length;

            string report = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Full-population benchmark: {0} entities at start ({1} units), {2} ticks. Mean {3:0.000} ms, p95 {4:0.000} ms, worst {5:0.000} ms per tick. {6} entities at end.",
                startEntities, 2 * Scenarios.BenchmarkUnitsPerPlayer, Ticks, mean, p95, worst, sim.Entities.Count);
            TestContext.WriteLine(report);
            Console.WriteLine(report);

            string summary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
            if (!string.IsNullOrEmpty(summary))
            {
                File.AppendAllText(summary, "### Simulation benchmark\n\n" + report + "\n\nBudget on a 2020 mid-range phone: 4 ms per tick (design section 12.4). CI hardware is not that phone; this is a trend line, not a gate.\n");
            }

            Assert.GreaterOrEqual(startEntities, 2 * Scenarios.BenchmarkUnitsPerPlayer + 350, "benchmark should carry the full section 12.4 load");
            Assert.Less(sim.CountUnits(0) + sim.CountUnits(1), 2 * Scenarios.BenchmarkUnitsPerPlayer, "the armies should have fought");
        }

        private static void Run(Simulation sim, int ticks, double[] times)
        {
            var commands = new List<Command>();
            var watch = new Stopwatch();
            for (int t = 0; t < ticks; t++)
            {
                commands.Clear();
                Scenarios.FillBenchmarkCommands(sim, sim.CurrentTick + 1, commands);
                watch.Restart();
                sim.Step(commands);
                watch.Stop();
                if (times != null) times[t] = watch.Elapsed.TotalMilliseconds;
            }
        }
    }
}
