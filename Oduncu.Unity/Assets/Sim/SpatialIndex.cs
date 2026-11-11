using System.Collections.Generic;

namespace Oduncu.Sim
{
    /// <summary>
    /// Uniform bucket grid over the map. Units are filed under the cell they stand in,
    /// buildings and resources under their footprint origin. Each bucket is kept sorted by
    /// entity id, so anything that walks buckets sees a deterministic order. Moving between
    /// buckets shifts list elements but never allocates once a bucket has grown to its peak.
    /// </summary>
    public sealed class SpatialIndex
    {
        public const int BucketShift = 3;
        public const int BucketCells = 1 << BucketShift;
        private const int InitialBucketCapacity = 32;

        public readonly int BucketsX;
        public readonly int BucketsY;
        private readonly int _width;
        private readonly int _height;
        private readonly List<Entity>[] _buckets;

        public SpatialIndex(int width, int height)
        {
            _width = width;
            _height = height;
            BucketsX = (width + BucketCells - 1) >> BucketShift;
            BucketsY = (height + BucketCells - 1) >> BucketShift;
            _buckets = new List<Entity>[BucketsX * BucketsY];
            for (int i = 0; i < _buckets.Length; i++) _buckets[i] = new List<Entity>(InitialBucketCapacity);
        }

        public List<Entity> Bucket(int bx, int by) => _buckets[by * BucketsX + bx];

        public int BucketXOf(int cellX) => Clamp(cellX, _width) >> BucketShift;
        public int BucketYOf(int cellY) => Clamp(cellY, _height) >> BucketShift;

        private int BucketOf(Cell c) => BucketYOf(c.Y) * BucketsX + BucketXOf(c.X);

        public void Add(Entity e)
        {
            int b = BucketOf(e.Cell);
            List<Entity> list = _buckets[b];
            list.Insert(LowerBound(list, e.Id), e);
            e.IndexBucket = b;
        }

        public void Remove(Entity e)
        {
            if (e.IndexBucket < 0) return;
            List<Entity> list = _buckets[e.IndexBucket];
            int i = LowerBound(list, e.Id);
            if (i < list.Count && list[i] == e) list.RemoveAt(i);
            e.IndexBucket = -1;
        }

        /// <summary>Call after a unit's Cell changes. Cheap when it stays in the same bucket.</summary>
        public void Update(Entity e)
        {
            if (e.IndexBucket < 0 || BucketOf(e.Cell) == e.IndexBucket) return;
            Remove(e);
            Add(e);
        }

        private static int LowerBound(List<Entity> list, int id)
        {
            int lo = 0, hi = list.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (list[mid].Id < id) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        private static int Clamp(int v, int size) => v < 0 ? 0 : (v >= size ? size - 1 : v);
    }

    /// <summary>A test applied to candidate entities by the spatial queries.</summary>
    public interface IEntityFilter
    {
        bool Matches(Entity e);
    }

    public enum OwnerMatch : byte
    {
        Any = 0,
        /// <summary>Owned by Player.</summary>
        Owned = 1,
        /// <summary>Owned by a player other than Player (never neutral).</summary>
        Enemy = 2,
        Neutral = 3,
    }

    /// <summary>
    /// Allocation-free description of what to look for. Every set field must match; defaults
    /// match everything. Used on the tick path instead of lambdas.
    /// </summary>
    public struct EntityFilter : IEntityFilter
    {
        public EntityKind Kind;
        public EntityCategory Category;
        public EntityTag Tags;
        /// <summary>Skip anything carrying any of these tags.</summary>
        public EntityTag ExcludeTags;
        public OwnerMatch Owner;
        public int Player;
        /// <summary>Only drop-off buildings that accept DropOffKind.</summary>
        public bool DropOff;
        public ResourceKind DropOffKind;
        /// <summary>Only resources with something left.</summary>
        public bool WithAmount;
        /// <summary>Only buildings that are not under construction.</summary>
        public bool Complete;

        public bool Matches(Entity e)
        {
            if (Kind != EntityKind.None && e.Kind != Kind) return false;
            if (Category != EntityCategory.None && e.Def.Category != Category) return false;
            if ((e.Def.Tags & Tags) != Tags) return false;
            if ((e.Def.Tags & ExcludeTags) != 0) return false;
            switch (Owner)
            {
                case OwnerMatch.Owned: if (e.Owner != Player) return false; break;
                case OwnerMatch.Enemy: if (e.Owner < 0 || e.Owner == Player) return false; break;
                case OwnerMatch.Neutral: if (e.Owner >= 0) return false; break;
            }
            if (DropOff && !(e.IsBuilding && e.Def.AcceptsDropOff(DropOffKind))) return false;
            if (WithAmount && e.Amount <= 0) return false;
            if (Complete && e.UnderConstruction) return false;
            return true;
        }
    }

    /// <summary>Adapter so tests and scripts can still query with a lambda.</summary>
    public struct PredicateFilter : IEntityFilter
    {
        public System.Func<Entity, bool> Predicate;
        public bool Matches(Entity e) => Predicate(e);
    }
}
