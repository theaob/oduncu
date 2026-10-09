using System.Collections.Generic;

namespace Oduncu.Sim
{
    /// <summary>
    /// The Open map template for 1v1 (plan 3.5, design section 9): Arabia-like scattered forest
    /// around an open centre, with the standard start for each player. Player 0's half is
    /// generated from the seed and player 1's half is its point mirror, so starts are identical
    /// by construction. Every forest blob is checked with a flood fill and dropped if it would
    /// cut a resource or the enemy base off from a Town Center.
    /// </summary>
    public static class OpenMap
    {
        public const int Size = 64;
        public const int Villagers = 6;
        public const int Sheep = 4;
        public const int Boar = 2;
        public const int BerryBushes = 6;
        public const int GoldPile = 5;
        public const int StonePile = 4;

        /// <summary>Standard start distances from the Town Center footprint, in tiles (section 9).</summary>
        public const int ForestWithin = 10;
        public const int GoldStoneWithin = 15;
        public const int BerriesWithin = 10;
        public const int SheepWithin = 8;
        public const int BoarWithin = 16;

        /// <summary>No scattered forest this close to the map centre.</summary>
        private const int OpenCentreRadius = 12;
        private const int ScatterBlobs = 40;

        private const byte Free = 0, Tc = 1, Tree = 2, Berry = 3, Gold = 4, Stone = 5;

        private struct Plan
        {
            public byte[] Cells;
            public Cell TcOrigin;
            public List<Cell> SheepCells;
            public List<Cell> BoarCells;
        }

        /// <summary>Build the map and its two starts. Both players are Woodlanders.</summary>
        public static Simulation Create(int seed)
        {
            var rng = new DeterministicRandom((ulong)seed * 2654435761UL + 11UL);
            Plan plan = MakePlan(rng);
            var sim = new Simulation(Size, Size, 2, (ulong)seed);
            for (int p = 0; p < 2; p++) sim.SetCivilization(p, CivId.Woodlanders);

            Cell tc0 = plan.TcOrigin;
            Cell tc1 = MirrorOrigin(tc0, 3);
            sim.SpawnStructure(EntityKind.TownCenter, 0, tc0, false);
            sim.SpawnStructure(EntityKind.TownCenter, 1, tc1, false);

            // Resources in row order of player 0's half, each followed by its mirror.
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    byte k = plan.Cells[y * Size + x];
                    if (k < Tree || x + y >= Size - 1) continue;
                    EntityKind kind = k == Tree ? EntityKind.Tree : k == Berry ? EntityKind.Berries : k == Gold ? EntityKind.GoldMine : EntityKind.StoneMine;
                    sim.SpawnStructure(kind, SimConstants.NeutralOwner, new Cell(x, y), false);
                    sim.SpawnStructure(kind, SimConstants.NeutralOwner, Mirror(new Cell(x, y)), false);
                }
            }

            // Villagers and the scout stand on the ring around player 0's Town Center, in row
            // order; player 1 gets the mirrored cells, so both starts match exactly.
            var ring = new List<Cell>();
            var around = new CellRect(tc0.X, tc0.Y, 3);
            for (int r = 1; r <= 2 && ring.Count < Villagers + 1; r++)
                for (int y = around.Y - r; y <= around.MaxY + r && ring.Count < Villagers + 1; y++)
                    for (int x = around.X - r; x <= around.MaxX + r && ring.Count < Villagers + 1; x++)
                        if (around.DistanceTo(new Cell(x, y)) == r && sim.Map.IsFree(x, y) && !plan.SheepCells.Contains(new Cell(x, y))) ring.Add(new Cell(x, y));

            for (int p = 0; p < 2; p++)
            {
                for (int i = 0; i < ring.Count; i++)
                    sim.SpawnUnit(i < Villagers ? EntityKind.Villager : EntityKind.Scout, p, p == 0 ? ring[i] : Mirror(ring[i]));
                for (int i = 0; i < plan.SheepCells.Count; i++)
                    sim.SpawnUnit(EntityKind.Sheep, p, p == 0 ? plan.SheepCells[i] : Mirror(plan.SheepCells[i]));
                for (int i = 0; i < plan.BoarCells.Count; i++)
                    sim.SpawnUnit(EntityKind.Boar, SimConstants.NeutralOwner, p == 0 ? plan.BoarCells[i] : Mirror(plan.BoarCells[i]));
            }
            return sim;
        }

        public static Cell Mirror(Cell c) => new Cell(Size - 1 - c.X, Size - 1 - c.Y);

        private static Cell MirrorOrigin(Cell origin, int size) => new Cell(Size - origin.X - size, Size - origin.Y - size);

        // ------------------------------------------------------------------ planning

        private static Plan MakePlan(DeterministicRandom rng)
        {
            var plan = new Plan
            {
                Cells = new byte[Size * Size],
                TcOrigin = new Cell(rng.Next(8, 13), rng.Next(8, 13)),
                SheepCells = new List<Cell>(),
                BoarCells = new List<Cell>(),
            };
            var tc = new CellRect(plan.TcOrigin.X, plan.TcOrigin.Y, 3);
            Fill(plan.Cells, tc, Tc);
            Fill(plan.Cells, MirrorRect(tc), Tc);

            PlaceBlock(plan, rng, tc, Berry, 2, 3, 4, 7);
            PlaceBlock(plan, rng, tc, Gold, 0, 0, 8, 12, GoldPile);
            PlaceBlock(plan, rng, tc, Stone, 0, 0, 8, 12, StonePile);
            PlaceForest(plan, rng, tc);

            // Sheep near home, boar further out; on free cells away from the base ring.
            PlaceAnimals(plan, rng, tc, plan.SheepCells, Sheep, 3, 6);
            PlaceAnimals(plan, rng, tc, plan.BoarCells, Boar, 11, 15);

            var reach = new bool[Size * Size];
            for (int blob = 0; blob < ScatterBlobs; blob++) TryScatterBlob(plan, rng, tc, reach);
            return plan;
        }

        private static CellRect MirrorRect(CellRect r) => new CellRect(Size - r.X - r.Size, Size - r.Y - r.Size, r.Size);

        private static void Fill(byte[] cells, CellRect r, byte k)
        {
            for (int y = r.Y; y <= r.MaxY; y++)
                for (int x = r.X; x <= r.MaxX; x++)
                    cells[y * Size + x] = k;
        }

        private static bool InHalf(int x, int y) => x >= 1 && y >= 1 && x + y <= Size - 4;

        /// <summary>Free cell of player 0's half with a one-cell margin of free cells around it.</summary>
        private static bool Clear(byte[] cells, int x, int y)
        {
            if (!InHalf(x, y)) return false;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= Size || ny >= Size) return false;
                    if (cells[ny * Size + nx] != Free) return false;
                }
            return true;
        }

        /// <summary>
        /// A compact pile of resource cells whose nearest cell is minDist..maxDist from the Town
        /// Center: a w×h block, or (w = 0) a plus-shaped pile of count cells.
        /// </summary>
        private static void PlaceBlock(Plan plan, DeterministicRandom rng, CellRect tc, byte kind, int w, int h, int minDist, int maxDist, int count = 0)
        {
            var shape = new List<Cell>();
            if (w > 0)
            {
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) shape.Add(new Cell(x, y));
            }
            else
            {
                Cell[] plus = { new Cell(1, 1), new Cell(0, 1), new Cell(2, 1), new Cell(1, 0), new Cell(1, 2), new Cell(2, 2) };
                for (int i = 0; i < count && i < plus.Length; i++) shape.Add(plus[i]);
            }

            for (int attempt = 0; attempt < 400; attempt++)
            {
                int ox = rng.Next(1, Size / 2), oy = rng.Next(1, Size / 2);
                if (TryShape(plan.Cells, tc, shape, ox, oy, minDist, maxDist, kind)) return;
            }
            // Deterministic fallback: first fitting spot in row order.
            for (int oy = 1; oy < Size / 2; oy++)
                for (int ox = 1; ox < Size / 2; ox++)
                    if (TryShape(plan.Cells, tc, shape, ox, oy, minDist, maxDist, kind)) return;
            throw new System.InvalidOperationException("Open map: no room for resource " + kind);
        }

        private static bool TryShape(byte[] cells, CellRect tc, List<Cell> shape, int ox, int oy, int minDist, int maxDist, byte kind)
        {
            int nearest = int.MaxValue;
            for (int i = 0; i < shape.Count; i++)
            {
                int x = ox + shape[i].X, y = oy + shape[i].Y;
                if (!Clear(cells, x, y)) return false;
                int d = tc.DistanceTo(new Cell(x, y));
                if (d < nearest) nearest = d;
                if (d > maxDist + 2) return false;
            }
            if (nearest < minDist || nearest > maxDist) return false;
            for (int i = 0; i < shape.Count; i++) cells[(oy + shape[i].Y) * Size + ox + shape[i].X] = kind;
            return true;
        }

        /// <summary>The home forest: an irregular 6×4 grove whose nearest tree is 6..9 tiles from the Town Center.</summary>
        private static void PlaceForest(Plan plan, DeterministicRandom rng, CellRect tc)
        {
            var shape = new List<Cell>();
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 6; x++)
                    if (!((x == 0 || x == 5) && (y == 0 || y == 3)) || rng.Chance(50)) shape.Add(new Cell(x, y));
            for (int attempt = 0; attempt < 400; attempt++)
            {
                int ox = rng.Next(1, Size / 2), oy = rng.Next(1, Size / 2);
                if (TryShape(plan.Cells, tc, shape, ox, oy, 6, 9, Tree)) return;
            }
            for (int oy = 1; oy < Size / 2; oy++)
                for (int ox = 1; ox < Size / 2; ox++)
                    if (TryShape(plan.Cells, tc, shape, ox, oy, 6, ForestWithin, Tree)) return;
            throw new System.InvalidOperationException("Open map: no room for the home forest");
        }

        private static void PlaceAnimals(Plan plan, DeterministicRandom rng, CellRect tc, List<Cell> into, int count, int minDist, int maxDist)
        {
            for (int attempt = 0; attempt < 2000 && into.Count < count; attempt++)
            {
                var c = new Cell(rng.Next(1, Size / 2 + 4), rng.Next(1, Size / 2 + 4));
                int d = tc.DistanceTo(c);
                if (d < minDist || d > maxDist || !Clear(plan.Cells, c.X, c.Y) || into.Contains(c)) continue;
                into.Add(c);
            }
            if (into.Count < count) throw new System.InvalidOperationException("Open map: no room for animals");
        }

        /// <summary>A round blob of trees and its mirror, kept only if every start stays connected.</summary>
        private static void TryScatterBlob(Plan plan, DeterministicRandom rng, CellRect tc, bool[] reach)
        {
            int r = rng.Next(1, 5);
            int cx = rng.Next(2, Size - 2), cy = rng.Next(2, Size - 2);
            int density = rng.Next(55, 90);
            var centre = new Cell(Size / 2, Size / 2);
            if (Cell.Chebyshev(new Cell(cx, cy), centre) < OpenCentreRadius + r) return;
            if (tc.DistanceTo(new Cell(cx, cy)) < 7 + r) return;

            var added = new List<int>();
            for (int y = cy - r; y <= cy + r; y++)
            {
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > r * r + r) continue;
                    if (!ClearForTree(plan.Cells, x, y) || !rng.Chance(density)) continue;
                    if (plan.SheepCells.Contains(new Cell(x, y)) || plan.BoarCells.Contains(new Cell(x, y))) continue;
                    if (NearAnimal(plan, x, y)) continue;
                    Cell m = Mirror(new Cell(x, y));
                    plan.Cells[y * Size + x] = Tree;
                    plan.Cells[m.Y * Size + m.X] = Tree;
                    added.Add(y * Size + x);
                }
            }
            if (added.Count == 0 || Connected(plan, tc, reach)) return;
            for (int i = 0; i < added.Count; i++)
            {
                int idx = added[i];
                Cell m = Mirror(new Cell(idx % Size, idx / Size));
                plan.Cells[idx] = Free;
                plan.Cells[m.Y * Size + m.X] = Free;
            }
        }

        /// <summary>A free cell of player 0's half whose neighbours are free or trees, so forests can grow into each other.</summary>
        private static bool ClearForTree(byte[] cells, int x, int y)
        {
            if (!InHalf(x, y) || cells[y * Size + x] != Free) return false;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= Size || ny >= Size) return false;
                    byte k = cells[ny * Size + nx];
                    if (k != Free && k != Tree) return false;
                }
            return true;
        }

        private static bool NearAnimal(Plan plan, int x, int y)
        {
            for (int i = 0; i < plan.SheepCells.Count; i++) if (Cell.Chebyshev(plan.SheepCells[i], new Cell(x, y)) <= 1) return true;
            for (int i = 0; i < plan.BoarCells.Count; i++) if (Cell.Chebyshev(plan.BoarCells[i], new Cell(x, y)) <= 1) return true;
            return false;
        }

        /// <summary>Flood fill from around player 0's Town Center: reaches the other base, every animal, and touches every resource.</summary>
        private static bool Connected(Plan plan, CellRect tc, bool[] reach)
        {
            FloodFill(plan.Cells, tc, reach);
            // Trees inside a forest are reached by cutting the outer ones, so a tree counts when
            // its forest touches walkable ground anywhere.
            var trees = new Queue<int>();
            var cut = new bool[plan.Cells.Length];
            for (int i = 0; i < plan.Cells.Length; i++)
            {
                if (plan.Cells[i] == Tree && Touches(reach, i % Size, i / Size)) { cut[i] = true; trees.Enqueue(i); }
            }
            while (trees.Count > 0)
            {
                int i = trees.Dequeue();
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = i % Size + dx, ny = i / Size + dy;
                        if (nx < 0 || ny < 0 || nx >= Size || ny >= Size) continue;
                        int ni = ny * Size + nx;
                        if (cut[ni] || plan.Cells[ni] != Tree) continue;
                        cut[ni] = true;
                        trees.Enqueue(ni);
                    }
            }
            for (int i = 0; i < plan.Cells.Length; i++)
            {
                byte k = plan.Cells[i];
                if (k == Free || k == Tc || cut[i]) continue;
                if (!Touches(reach, i % Size, i / Size)) return false;
            }
            // The enemy base: its footprint's corner cell touches the ground around it.
            Cell enemy = MirrorOrigin(new Cell(tc.X, tc.Y), tc.Size);
            if (!Touches(reach, enemy.X, enemy.Y) && !Touches(reach, enemy.X + tc.Size - 1, enemy.Y + tc.Size - 1)) return false;
            for (int i = 0; i < plan.SheepCells.Count; i++) if (!reach[Index(plan.SheepCells[i])] || !reach[Index(Mirror(plan.SheepCells[i]))]) return false;
            for (int i = 0; i < plan.BoarCells.Count; i++) if (!reach[Index(plan.BoarCells[i])] || !reach[Index(Mirror(plan.BoarCells[i]))]) return false;
            return true;
        }

        private static int Index(Cell c) => c.Y * Size + c.X;

        /// <summary>Walkable cells reachable from the ring around a Town Center (8-way, no corner cutting).</summary>
        private static void FloodFill(byte[] cells, CellRect tc, bool[] reach)
        {
            for (int i = 0; i < reach.Length; i++) reach[i] = false;
            var queue = new Queue<int>();
            for (int y = tc.Y - 1; y <= tc.MaxY + 1; y++)
                for (int x = tc.X - 1; x <= tc.MaxX + 1; x++)
                    if (x >= 0 && y >= 0 && x < Size && y < Size && cells[y * Size + x] == Free && !reach[y * Size + x])
                    {
                        reach[y * Size + x] = true;
                        queue.Enqueue(y * Size + x);
                    }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int cx = i % Size, cy = i / Size;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= Size || ny >= Size) continue;
                        int ni = ny * Size + nx;
                        if (reach[ni] || cells[ni] != Free) continue;
                        if (dx != 0 && dy != 0 && (cells[cy * Size + nx] != Free || cells[ny * Size + cx] != Free)) continue;
                        reach[ni] = true;
                        queue.Enqueue(ni);
                    }
            }
        }

        private static bool Touches(bool[] reach, int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx >= 0 && ny >= 0 && nx < Size && ny < Size && reach[ny * Size + nx]) return true;
                }
            return false;
        }
    }
}
