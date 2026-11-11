using System;
using System.Collections.Generic;

namespace Oduncu.Sim
{
    /// <summary>
    /// 8-connected A* on the map grid with no corner cutting. Tie-breaking is by insertion
    /// order so the result is identical on every platform. Buffers are reused between calls.
    /// Milestone 1 replaces group moves with flow fields; single-unit A* stays.
    /// </summary>
    public sealed class Pathfinder
    {
        private const int StraightCost = 10;
        private const int DiagonalCost = 14;

        private struct HeapItem
        {
            public int F;
            public int Serial;
            public int Node;
        }

        private readonly MapGrid _map;
        private readonly int[] _g;
        private readonly int[] _parent;
        private readonly int[] _closedStamp;
        private readonly int[] _openStamp;
        private HeapItem[] _heap = new HeapItem[256];
        private int _heapCount;
        private int _stamp;
        private int _serial;
        /// <summary>Whose gates count as open for the current search; -1 for nobody's.</summary>
        private int _player = -1;

        private static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] Dy = { 0, 0, 1, -1, 1, -1, 1, -1 };

        public Pathfinder(MapGrid map)
        {
            _map = map;
            int n = map.Width * map.Height;
            _g = new int[n];
            _parent = new int[n];
            _closedStamp = new int[n];
            _openStamp = new int[n];
        }

        /// <summary>Path to a cell. If the goal is blocked, paths to a free cell adjacent to it.</summary>
        /// <remarks>Player is whose gates are open to the walker; -1 treats every gate as a wall.</remarks>
        public bool FindPathToCell(Cell start, Cell goal, List<Cell> result, int player = -1)
        {
            _player = player;
            var rect = new CellRect(goal.X, goal.Y, 1);
            if (_map.IsPassable(goal, player)) return Search(start, rect, 0, result);
            return Search(start, rect, 1, result);
        }

        /// <summary>Path to any passable cell touching the rect (chessboard distance 1).</summary>
        public bool FindPathAdjacentToRect(Cell start, CellRect rect, List<Cell> result, int player = -1)
        {
            _player = player;
            return Search(start, rect, 1, result);
        }

        private bool Search(Cell start, CellRect goalRect, int goalDistance, List<Cell> result)
        {
            result.Clear();
            if (!_map.InBounds(start)) return false;
            if (goalRect.DistanceTo(start) <= goalDistance) return true;

            _stamp++;
            _heapCount = 0;
            int startIndex = _map.Index(start.X, start.Y);
            _g[startIndex] = 0;
            _parent[startIndex] = -1;
            Push(Heuristic(start, goalRect), startIndex);
            _openStamp[startIndex] = _stamp;

            int expansions = 0;
            int limit = _map.Width * _map.Height;
            while (_heapCount > 0)
            {
                int current = Pop();
                if (_closedStamp[current] == _stamp) continue;
                _closedStamp[current] = _stamp;
                Cell c = _map.CellAt(current);

                if (goalRect.DistanceTo(c) <= goalDistance)
                {
                    Reconstruct(current, startIndex, result);
                    return true;
                }

                if (++expansions > limit) break;

                for (int d = 0; d < 8; d++)
                {
                    int nx = c.X + Dx[d];
                    int ny = c.Y + Dy[d];
                    if (!_map.IsPassable(nx, ny, _player)) continue;
                    if (d >= 4)
                    {
                        // No corner cutting: both orthogonal neighbours must be passable.
                        if (!_map.IsPassable(c.X + Dx[d], c.Y, _player) || !_map.IsPassable(c.X, c.Y + Dy[d], _player)) continue;
                    }
                    int ni = _map.Index(nx, ny);
                    if (_closedStamp[ni] == _stamp) continue;
                    int ng = _g[current] + (d < 4 ? StraightCost : DiagonalCost);
                    if (_openStamp[ni] == _stamp && ng >= _g[ni]) continue;
                    _g[ni] = ng;
                    _parent[ni] = current;
                    _openStamp[ni] = _stamp;
                    Push(ng + Heuristic(new Cell(nx, ny), goalRect), ni);
                }
            }
            return false;
        }

        private static int Heuristic(Cell c, CellRect goal)
        {
            int dx = c.X < goal.X ? goal.X - c.X : (c.X > goal.MaxX ? c.X - goal.MaxX : 0);
            int dy = c.Y < goal.Y ? goal.Y - c.Y : (c.Y > goal.MaxY ? c.Y - goal.MaxY : 0);
            int diag = Math.Min(dx, dy);
            int straight = Math.Max(dx, dy) - diag;
            return diag * DiagonalCost + straight * StraightCost;
        }

        private void Reconstruct(int node, int startIndex, List<Cell> result)
        {
            while (node != startIndex && node != -1)
            {
                result.Add(_map.CellAt(node));
                node = _parent[node];
            }
            result.Reverse();
        }

        private void Push(int f, int node)
        {
            if (_heapCount == _heap.Length) Array.Resize(ref _heap, _heap.Length * 2);
            int i = _heapCount++;
            _heap[i] = new HeapItem { F = f, Serial = _serial++, Node = node };
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (!Less(_heap[i], _heap[p])) break;
                HeapItem t = _heap[i]; _heap[i] = _heap[p]; _heap[p] = t;
                i = p;
            }
        }

        private int Pop()
        {
            int node = _heap[0].Node;
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
            return node;
        }

        private static bool Less(HeapItem a, HeapItem b) => a.F < b.F || (a.F == b.F && a.Serial < b.Serial);
    }
}
