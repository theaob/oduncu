namespace Oduncu.Sim
{
    /// <summary>
    /// Static occupancy of the map. A cell is blocked when a building or resource stands on it.
    /// Units never block cells; they push apart instead (soft separation). Gates block every player but their owner.
    /// </summary>
    public sealed class MapGrid
    {
        public readonly int Width;
        public readonly int Height;
        private readonly int[] _occupant;
        /// <summary>Owner + 1 of the gate on a cell, or 0. A gate blocks everyone but its owner.</summary>
        private readonly int[] _gateOwner;

        /// <summary>Bumped on every occupancy change so cached flow fields know they are stale.</summary>
        public int Version { get; private set; }

        public MapGrid(int width, int height)
        {
            Width = width;
            Height = height;
            _occupant = new int[width * height];
            _gateOwner = new int[width * height];
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public bool InBounds(Cell c) => InBounds(c.X, c.Y);

        public int Index(int x, int y) => y * Width + x;
        public Cell CellAt(int index) => new Cell(index % Width, index / Width);

        /// <summary>Entity id occupying the cell, or 0.</summary>
        public int OccupantAt(int x, int y) => InBounds(x, y) ? _occupant[Index(x, y)] : -1;

        public bool IsFree(int x, int y) => InBounds(x, y) && _occupant[Index(x, y)] == 0;
        public bool IsFree(Cell c) => IsFree(c.X, c.Y);

        /// <summary>Whether a player's units may walk on a cell: free, or the player's own gate. Player -1 means nobody's gates.</summary>
        public bool IsPassable(int x, int y, int player)
        {
            if (!InBounds(x, y)) return false;
            int i = Index(x, y);
            return _occupant[i] == 0 || (player >= 0 && _gateOwner[i] == player + 1);
        }

        public bool IsPassable(Cell c, int player) => IsPassable(c.X, c.Y, player);

        public void SetGate(CellRect r, int owner)
        {
            Version++;
            for (int y = r.Y; y <= r.MaxY; y++)
                for (int x = r.X; x <= r.MaxX; x++)
                    _gateOwner[Index(x, y)] = owner + 1;
        }

        public bool IsRectFree(CellRect r)
        {
            for (int y = r.Y; y <= r.MaxY; y++)
                for (int x = r.X; x <= r.MaxX; x++)
                    if (!IsFree(x, y)) return false;
            return true;
        }

        public bool IsRectInBounds(CellRect r) => InBounds(r.X, r.Y) && InBounds(r.MaxX, r.MaxY);

        public void Occupy(CellRect r, int entityId)
        {
            Version++;
            for (int y = r.Y; y <= r.MaxY; y++)
                for (int x = r.X; x <= r.MaxX; x++)
                    _occupant[Index(x, y)] = entityId;
        }

        public void Clear(CellRect r)
        {
            Version++;
            for (int y = r.Y; y <= r.MaxY; y++)
                for (int x = r.X; x <= r.MaxX; x++)
                {
                    _occupant[Index(x, y)] = 0;
                    _gateOwner[Index(x, y)] = 0;
                }
        }

        public Cell Clamp(Cell c)
        {
            int x = c.X < 0 ? 0 : (c.X >= Width ? Width - 1 : c.X);
            int y = c.Y < 0 ? 0 : (c.Y >= Height ? Height - 1 : c.Y);
            return new Cell(x, y);
        }

        public void WriteState(StateHasher h)
        {
            h.Write(Width);
            h.Write(Height);
            for (int i = 0; i < _occupant.Length; i++) h.Write(_occupant[i]);
            for (int i = 0; i < _gateOwner.Length; i++) if (_gateOwner[i] != 0) { h.Write(i); h.Write(_gateOwner[i]); }
        }
    }
}
