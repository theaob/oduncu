using System;
using System.Collections.Generic;
using NUnit.Framework;
using Oduncu.Sim;

namespace Oduncu.Sim.Tests
{
    public class SpatialIndexTests
    {
        /// <summary>
        /// For 50 seeds, play part of the scripted match so units have moved, chopped trees and
        /// died, then compare the indexed query with a scan of every entity.
        /// </summary>
        [Test]
        public void IndexedQueriesMatchBruteForceOn50Seeds()
        {
            for (int seed = 1; seed <= 50; seed++)
            {
                Simulation sim = MapGenerator.CreateDefault(seed);
                Scenarios.RunScripted(sim, 300 + seed * 20);
                var rng = new DeterministicRandom((ulong)seed * 31UL + 7UL);
                var predicates = Predicates();

                for (int q = 0; q < 200; q++)
                {
                    // Points on and off the map, on cell corners and in between.
                    var from = new FPVector2(
                        FP.Ratio(rng.Next(-8 * 4, (sim.Map.Width + 8) * 4), 4),
                        FP.Ratio(rng.Next(-8 * 4, (sim.Map.Height + 8) * 4), 4));
                    int radius = q % 10 == 0 ? sim.Map.Width + sim.Map.Height : rng.Next(0, 25);
                    var p = predicates[q % predicates.Length];
                    Entity expected = sim.FindNearestBruteForce(from, radius, p);
                    Entity actual = sim.FindNearest(from, radius, p);
                    Assert.AreEqual(expected?.Id ?? 0, actual?.Id ?? 0,
                        "seed " + seed + " query " + q + " from " + from + " radius " + radius);
                }
            }
        }

        [Test]
        public void FilterStructMatchesEquivalentLambda()
        {
            Simulation sim = MapGenerator.CreateDefault(9);
            Scenarios.RunScripted(sim, 1500);
            var rng = new DeterministicRandom(3);
            for (int q = 0; q < 300; q++)
            {
                var from = new FPVector2(FP.Ratio(rng.Next(0, 48 * 2), 2), FP.Ratio(rng.Next(0, 48 * 2), 2));
                int radius = rng.Next(1, 30);
                int player = q % 2;

                var trees = new EntityFilter { Kind = EntityKind.Tree, WithAmount = true };
                Assert.AreEqual(Id(sim.FindNearestBruteForce(from, radius, e => e.Kind == EntityKind.Tree && e.Amount > 0)),
                    Id(sim.FindNearest(from, radius, trees)));

                var enemies = new EntityFilter { Owner = OwnerMatch.Enemy, Player = player };
                Assert.AreEqual(Id(sim.FindNearestBruteForce(from, radius, e => e.Owner >= 0 && e.Owner != player)),
                    Id(sim.FindNearest(from, radius, enemies)));

                var dropOffs = new EntityFilter { Owner = OwnerMatch.Owned, Player = player, Complete = true, DropOff = true, DropOffKind = ResourceKind.Wood };
                Assert.AreEqual(Id(sim.FindNearestBruteForce(from, radius, e => e.Owner == player && e.IsBuilding && e.Def.AcceptsDropOff(ResourceKind.Wood) && !e.UnderConstruction)),
                    Id(sim.FindNearest(from, radius, dropOffs)));
            }
        }

        [Test]
        public void EveryLiveEntityIsFiledUnderItsOwnBucketInIdOrder()
        {
            Simulation sim = MapGenerator.CreateDefault(17);
            Scenarios.RunScripted(sim, 4000);
            var seen = new HashSet<int>();
            for (int by = 0; by < sim.Index.BucketsY; by++)
            {
                for (int bx = 0; bx < sim.Index.BucketsX; bx++)
                {
                    List<Entity> bucket = sim.Index.Bucket(bx, by);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        Entity e = bucket[i];
                        Assert.IsTrue(e.Alive, "dead entity left in the index");
                        Assert.AreEqual(bx, sim.Index.BucketXOf(e.Cell.X));
                        Assert.AreEqual(by, sim.Index.BucketYOf(e.Cell.Y));
                        if (i > 0) Assert.Less(bucket[i - 1].Id, e.Id, "bucket not in id order");
                        Assert.IsTrue(seen.Add(e.Id), "entity filed twice");
                    }
                }
            }
            Assert.AreEqual(sim.Entities.Count, seen.Count, "every live entity is indexed");
        }

        private static Func<Entity, bool>[] Predicates()
        {
            return new Func<Entity, bool>[]
            {
                e => true,
                e => e.Kind == EntityKind.Tree && e.Amount > 0,
                e => e.IsUnit,
                e => e.IsBuilding && e.Owner == 0,
                e => e.Owner == 1 && (e.IsUnit || e.IsBuilding),
                e => e.Kind == EntityKind.Villager,
                e => e.Owner >= 0 && e.Owner != 0,
            };
        }

        private static int Id(Entity e) => e == null ? 0 : e.Id;
    }
}
