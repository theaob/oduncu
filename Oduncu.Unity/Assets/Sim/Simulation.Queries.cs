using System;

namespace Oduncu.Sim
{
    public sealed partial class Simulation
    {
        /// <summary>Largest building or resource footprint. Structures are indexed by origin, so a footprint can reach
        /// this many cells past its bucket and the low side of every query pads by it.</summary>
        private static readonly int MaxStructureSize = ComputeMaxStructureSize();

        private static int ComputeMaxStructureSize()
        {
            int max = 1;
            EntityDef[] defs = GameData.Entities;
            for (int i = 0; i < defs.Length; i++) if (defs[i].Size > max) max = defs[i].Size;
            return max;
        }

        /// <summary>
        /// Nearest entity matching the predicate within radius tiles of a position. Ties go to the
        /// lower id. Backed by the spatial index; the lambda allocates at the call site, so the
        /// tick path uses the EntityFilter overload instead.
        /// </summary>
        public Entity FindNearest(FPVector2 from, int radius, Func<Entity, bool> predicate)
        {
            var filter = new PredicateFilter { Predicate = predicate };
            return FindNearest(from, radius, ref filter);
        }

        public Entity FindNearest(FPVector2 from, int radius, EntityFilter filter)
        {
            return FindNearest(from, radius, ref filter);
        }

        /// <summary>The same query by scanning every entity. Reference for the index property tests.</summary>
        public Entity FindNearestBruteForce(FPVector2 from, int radius, Func<Entity, bool> predicate)
        {
            Entity best = null;
            FP bestDist = FP.FromInt(radius * radius);
            for (int i = 0; i < _entities.Count; i++)
            {
                Entity e = _entities[i];
                if (!e.Alive || !predicate(e)) continue;
                FP d = FPVector2.SqrDistance(from, NearestPointOf(e, from));
                if (d < bestDist || (best == null && d == bestDist))
                {
                    best = e;
                    bestDist = d;
                }
            }
            return best;
        }

        private Entity FindNearest<TFilter>(FPVector2 from, int radius, ref TFilter filter) where TFilter : struct, IEntityFilter
        {
            // Smallest (distance, id) among matches with distance <= radius. The result does not
            // depend on the order buckets are visited, so it equals the brute-force scan.
            Entity best = null;
            FP bestDist = FP.FromInt(radius * radius);
            int minX = (from.X - radius).FloorToInt() - MaxStructureSize;
            int minY = (from.Y - radius).FloorToInt() - MaxStructureSize;
            int maxX = (from.X + radius).FloorToInt();
            int maxY = (from.Y + radius).FloorToInt();
            if (maxX < 0 || maxY < 0 || minX >= Map.Width || minY >= Map.Height) return null;
            int bx0 = Index.BucketXOf(minX), bx1 = Index.BucketXOf(maxX);
            int by0 = Index.BucketYOf(minY), by1 = Index.BucketYOf(maxY);
            for (int by = by0; by <= by1; by++)
            {
                for (int bx = bx0; bx <= bx1; bx++)
                {
                    var bucket = Index.Bucket(bx, by);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        Entity e = bucket[i];
                        if (!e.Alive || !filter.Matches(e)) continue;
                        FP d = FPVector2.SqrDistance(from, NearestPointOf(e, from));
                        if (d < bestDist || (d == bestDist && (best == null || e.Id < best.Id)))
                        {
                            best = e;
                            bestDist = d;
                        }
                    }
                }
            }
            return best;
        }

        private bool AnyUnitInside(CellRect rect)
        {
            int bx0 = Index.BucketXOf(rect.X), bx1 = Index.BucketXOf(rect.MaxX);
            int by0 = Index.BucketYOf(rect.Y), by1 = Index.BucketYOf(rect.MaxY);
            for (int by = by0; by <= by1; by++)
            {
                for (int bx = bx0; bx <= bx1; bx++)
                {
                    var bucket = Index.Bucket(bx, by);
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        Entity e = bucket[i];
                        if (e.Alive && e.IsUnit && rect.Contains(e.Cell)) return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Move a unit to a new cell and keep the spatial index in step.</summary>
        private void SetUnitCell(Entity u, Cell cell)
        {
            if (u.Cell == cell) return;
            u.Cell = cell;
            Index.Update(u);
        }
    }
}
