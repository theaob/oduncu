using System;
using NUnit.Framework;
using Oduncu.Sim;

namespace Oduncu.Sim.Tests
{
    /// <summary>
    /// Section 15 of the design asks for no per-frame allocations in the simulation. This
    /// measures the managed heap directly, so it only runs headless: Unity's Mono reports
    /// allocated bytes differently and the editor allocates on the same thread.
    /// </summary>
    public class AllocationTests
    {
        private const int WarmUpTicks = 500;
        private const int MeasuredTicks = 1000;

        [Test]
        public void SteadyStateTicksDoNotAllocate()
        {
#if UNITY_5_3_OR_NEWER
            Assert.Ignore("Measured by the headless .NET test run only.");
#else
            // Record the scripted match first (the script itself allocates), then replay it.
            var log = new CommandLog { Seed = Scenarios.DeterminismSeed };
            Scenarios.RunScripted(Scenarios.CreateDeterminismScenario(), WarmUpTicks + MeasuredTicks, log);

            Simulation sim = Scenarios.CreateDeterminismScenario();
            for (int t = 0; t < WarmUpTicks; t++) sim.Step(log.CommandsAt(sim.CurrentTick + 1));

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 0; t < MeasuredTicks; t++) sim.Step(log.CommandsAt(sim.CurrentTick + 1));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Greater(sim.CountUnits(0), 3, "the measured window should include training and fighting");
            Assert.AreEqual(0, allocated, "bytes allocated over " + MeasuredTicks + " ticks");
#endif
        }
    }
}
