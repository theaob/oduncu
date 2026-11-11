using System;

namespace Oduncu.Sim
{
    /// <summary>
    /// Integration fields for large group moves (plan 3.4, design section 12.4). One Dijkstra
    /// pass from the goal gives every free cell its cost to the goal, so any number of units
    /// heading there just step downhill instead of each running A*. Fields are cached by goal
    /// and recomputed when the map changes, so the cache never changes what a unit does: a
    /// field is a pure function of the goal and the current occupancy. All buffers are
    /// allocated up front.
    /// </summary>
    public sealed class FlowFieldCache
    {
        public const int Unreachable = int.MaxValue;
        private const int StraightCost = 10;
        private const int DiagonalCost = 14;

        private static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] Dy = { 0, 0, 1, -1, 1, -1, 1, -1 };

        private sealed class Field
        {
            public bool Valid;
            public Cell Goal;
            public int Player;
            public int MapVersion;
            public long LastUsed;
            public int[] Cost;
        }

        private struct HeapItem
        {
            public int Cost;
            public int Serial;
            public int Node;
        }

        private readonly MapGrid _map;
        private readonly Field[] _fields;
        private HeapItem[] _heap;
        private int _heapCount;
        private int _serial;
        private long _useCounter;

        /// <summary>How many fields have been computed; for tests and profiling.</summary>
        public int Computations { get; private set; }

        public FlowFieldCache(MapGrid map, int poolSize)
        {
            _map = map;
            int n = map.Width * map.Height;
            _fields = new Field[poolSize];
            for (int i = 0; i < poolSize; i++) _fields[i] = new Field { Cost = new int[n] };
            _heap = new HeapItem[n * 2 + 16];
        }

        /// <summary>
        /// The next cell on a shortest path from a cell toward the goal. False when the cell is
        /// the goal (or touches a blocked goal) or the goal cannot be reached from it.
        /// </summary>
        public bool NextCell(Cell goal, Cell from, out Cell next) => NextCell(goal, from, -1, out next);

        /// <summary>As NextCell, for a walker whose own gates are open (player -1: nobody's).</summary>
        public bool NextCell(Cell goal, Cell from, int player, out Cell next)
        {
            next = from;
            if (!_map.InBounds(from)) return false;
            int[] cost = Get(goal, player);
            int here = cost[_map.Index(from.X, from.Y)];
            if (here == 0) return false;
            int best = Unreachable;
            for (int d = 0; d < 8; d++)
            {
                int nx = from.X + Dx[d], ny = from.Y + Dy[d];
                if (!CanStep(from.X, from.Y, d, player)) continue;
                int c = cost[_map.Index(nx, ny)];
                if (c == Unreachable) continue;
                int total = c + (d < 4 ? StraightCost : DiagonalCost);
                if (total < best)
                {
                    best = total;
                    next = new Cell(nx, ny);
                }
            }
            return best != Unreachable;
        }

        /// <summary>Cost to the goal from a cell in A* units (10 straight, 14 diagonal), or Unreachable.</summary>
        public int CostAt(Cell goal, Cell from)
        {
            if (!_map.InBounds(from)) return Unreachable;
            return Get(goal, -1)[_map.Index(from.X, from.Y)];
        }

        private int[] Get(Cell goal, int player)
        {
            _useCounter++;
            Field victim = null;
            for (int i = 0; i < _fields.Length; i++)
            {
                Field f = _fields[i];
                if (f.Valid && f.Goal == goal && f.Player == player && f.MapVersion == _map.Version)
                {
                    f.LastUsed = _useCounter;
                    return f.Cost;
                }
                // Reuse an empty slot first, otherwise the least recently used one.
                if (victim == null || (victim.Valid && (!f.Valid || f.LastUsed < victim.LastUsed))) victim = f;
            }
            Compute(victim, goal, player);
            victim.LastUsed = _useCounter;
            return victim.Cost;
        }

        private bool CanStep(int x, int y, int d, int player)
        {
            int nx = x + Dx[d], ny = y + Dy[d];
            if (!_map.IsPassable(nx, ny, player)) return false;
            // No corner cutting, the same rule as the A* pathfinder.
            return d < 4 || (_map.IsPassable(x + Dx[d], y, player) && _map.IsPassable(x, y + Dy[d], player));
        }

        private void Compute(Field f, Cell goal, int player)
        {
            Computations++;
            f.Valid = true;
            f.Goal = goal;
            f.Player = player;
            f.MapVersion = _map.Version;
            int[] cost = f.Cost;
            for (int i = 0; i < cost.Length; i++) cost[i] = Unreachable;
            _heapCount = 0;

            if (_map.IsPassable(goal, player)) Seed(cost, goal.X, goal.Y);
            else
            {
                // Blocked goal: every free cell touching it is a destination, as in A*.
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if ((dx != 0 || dy != 0) && _map.IsPassable(goal.X + dx, goal.Y + dy, player)) Seed(cost, goal.X + dx, goal.Y + dy);
            }

            while (_heapCount > 0)
            {
                HeapItem item = Pop();
                if (item.Cost != cost[item.Node]) continue;
                Cell c = _map.CellAt(item.Node);
                for (int d = 0; d < 8; d++)
                {
                    // Moves are symmetric, so stepping out from c is the same test as stepping into c.
                    if (!CanStep(c.X, c.Y, d, player)) continue;
                    int ni = _map.Index(c.X + Dx[d], c.Y + Dy[d]);
                    int nc = item.Cost + (d < 4 ? StraightCost : DiagonalCost);
                    if (nc >= cost[ni]) continue;
                    cost[ni] = nc;
                    Push(nc, ni);
                }
            }
        }

        private void Seed(int[] cost, int x, int y)
        {
            int i = _map.Index(x, y);
            cost[i] = 0;
            Push(0, i);
        }

        private void Push(int c, int node)
        {
            if (_heapCount == _heap.Length) Array.Resize(ref _heap, _heap.Length * 2);
            int i = _heapCount++;
            _heap[i] = new HeapItem { Cost = c, Serial = _serial++, Node = node };
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (!Less(_heap[i], _heap[p])) break;
                HeapItem t = _heap[i]; _heap[i] = _heap[p]; _heap[p] = t;
                i = p;
            }
        }

        private HeapItem Pop()
        {
            HeapItem top = _heap[0];
            _heap[0] = _heap[--_heapCount];
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, best = i;
                if (l < _heapCount && Less(_heap[l], _heap[best])) best = l;
                if (r < _heapCount && Less(_heap[r], _heap[best])) best = r;
                if (best == i) break;
                HeapItem t = _heap[i]; _heap[i] = _heap[best]; _heap[best] = t;
                i = best;
            }
            return top;
        }

        private static bool Less(HeapItem a, HeapItem b) => a.Cost < b.Cost || (a.Cost == b.Cost && a.Serial < b.Serial);
    }
}
