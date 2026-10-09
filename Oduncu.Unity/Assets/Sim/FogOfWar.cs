namespace Oduncu.Sim
{
    /// <summary>
    /// Per-player fog of war (design section 9, plan 3.5): hidden, explored, visible. Lives in
    /// the simulation so the AI reads exactly what a player sees. Visible cells are stamped
    /// with the update number, so a refresh never clears the grid; explored cells stay explored.
    /// </summary>
    public sealed class FogOfWar
    {
        /// <summary>Sight radii are clamped to this many tiles.</summary>
        public const int MaxRadius = 32;

        private static readonly int[][] Offsets = BuildOffsets();

        public readonly int Width;
        public readonly int Height;
        private readonly int[][] _visibleStamp;
        private readonly bool[][] _explored;
        private int _stamp = 1;

        public FogOfWar(int width, int height, int players)
        {
            Width = width;
            Height = height;
            _visibleStamp = new int[players][];
            _explored = new bool[players][];
            for (int p = 0; p < players; p++)
            {
                _visibleStamp[p] = new int[width * height];
                _explored[p] = new bool[width * height];
            }
        }

        public bool IsVisible(int player, int x, int y)
        {
            if (player < 0 || player >= _visibleStamp.Length || x < 0 || y < 0 || x >= Width || y >= Height) return false;
            return _visibleStamp[player][y * Width + x] == _stamp;
        }

        public bool IsVisible(int player, Cell c) => IsVisible(player, c.X, c.Y);

        public bool IsExplored(int player, int x, int y)
        {
            if (player < 0 || player >= _explored.Length || x < 0 || y < 0 || x >= Width || y >= Height) return false;
            return _explored[player][y * Width + x];
        }

        public bool IsExplored(int player, Cell c) => IsExplored(player, c.X, c.Y);

        /// <summary>Start a refresh: everything goes from visible to explored until revealed again.</summary>
        internal void BeginUpdate() => _stamp++;

        /// <summary>Mark a disc of cells visible and explored for a player.</summary>
        internal void Reveal(int player, Cell centre, int radius)
        {
            if (radius > MaxRadius) radius = MaxRadius;
            if (radius < 0) radius = 0;
            int[] offsets = Offsets[radius];
            int[] stamp = _visibleStamp[player];
            bool[] explored = _explored[player];
            for (int i = 0; i < offsets.Length; i += 2)
            {
                int x = centre.X + offsets[i], y = centre.Y + offsets[i + 1];
                if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
                int idx = y * Width + x;
                stamp[idx] = _stamp;
                explored[idx] = true;
            }
        }

        /// <summary>Explored cells only: visibility is recomputed from positions every tick.</summary>
        public void WriteState(StateHasher h)
        {
            for (int p = 0; p < _explored.Length; p++)
            {
                bool[] e = _explored[p];
                long word = 0;
                for (int i = 0; i < e.Length; i++)
                {
                    if (e[i]) word |= 1L << (i & 63);
                    if ((i & 63) == 63 || i == e.Length - 1)
                    {
                        h.Write(word);
                        word = 0;
                    }
                }
            }
        }

        /// <summary>For each radius, the (dx, dy) pairs of the disc dx² + dy² ≤ r² + r, flattened.</summary>
        private static int[][] BuildOffsets()
        {
            var table = new int[MaxRadius + 1][];
            for (int r = 0; r <= MaxRadius; r++)
            {
                int limit = r * r + r;
                int n = 0;
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                        if (dx * dx + dy * dy <= limit) n++;
                var offsets = new int[n * 2];
                int k = 0;
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (dx * dx + dy * dy > limit) continue;
                        offsets[k++] = dx;
                        offsets[k++] = dy;
                    }
                table[r] = offsets;
            }
            return table;
        }
    }
}
