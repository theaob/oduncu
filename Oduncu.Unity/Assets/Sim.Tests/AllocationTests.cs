using System;
using System.Collections.Generic;
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

        [Test]
        public void CombatTicksDoNotAllocate()
        {
#if UNITY_5_3_OR_NEWER
            Assert.Ignore("Measured by the headless .NET test run only.");
#else
            // Two mixed armies meet under a Town Center with a garrison: projectiles, splash,
            // flow-field group moves, separation, conversion and building arrows all run.
            var sim = new Simulation(64, 64, 2, 3);
            Entity tc = sim.SpawnStructure(EntityKind.TownCenter, 1, new Cell(40, 30), false);
            var kinds = new[] { EntityKind.Archer, EntityKind.Knight, EntityKind.Mangonel, EntityKind.Spearman, EntityKind.Monk, EntityKind.Skirmisher };
            var army0 = new List<int>();
            var army1 = new List<int>();
            for (int i = 0; i < 24; i++)
            {
                army0.Add(sim.SpawnUnit(kinds[i % kinds.Length], 0, new Cell(4 + i % 6, 28 + i / 6)).Id);
                army1.Add(sim.SpawnUnit(kinds[(i + 3) % kinds.Length], 1, new Cell(34 + i % 6, 28 + i / 6)).Id);
            }
            var garrison = new List<int>();
            for (int i = 0; i < 5; i++) garrison.Add(sim.SpawnUnit(EntityKind.Villager, 1, new Cell(45, 28 + i)).Id);

            var none = new List<Command>();
            sim.Step(new List<Command>
            {
                Command.Move(0, army0, new Cell(30, 30)),
                Command.Garrison(1, garrison, tc.Id),
            });
            for (int t = 0; t < 60; t++) sim.Step(none);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 0; t < 400; t++) sim.Step(none);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Greater(tc.GarrisonCount, 0, "the garrison went in");
            Assert.Less(sim.CountUnits(0) + sim.CountUnits(1), 53, "the armies fought");
            Assert.AreEqual(0, allocated, "bytes allocated over 400 combat ticks");
#endif
        }
    }
}
