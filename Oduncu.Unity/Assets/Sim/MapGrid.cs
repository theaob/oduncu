namespace Oduncu.Sim
{
    /// <summary>
    /// Static occupancy of the map. A cell is blocked when a building or resource stands on it.
    /// Units do not block cells in milestone 0.
    /// </summary>
    public sealed class MapGrid
    {
        public readonly int Width;
        public readonly int Height;
        private readonly int[] _occupant;

        public MapGrid(int width, int height)
        {
            Width = width;
            Height = height;
            _occupant = new int[width * height];
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public bool InBounds(Cell c) => InBounds(c.X, c.Y);

        public int Index(int x, int y) => y * Width + x;
        public Cell CellAt(int index) => new Cell(index % Width, index / Width);

        /// <summary>Entity id occupying the cell, or 0.</summary>
        public int OccupantAt(int x, int y) => InBounds(x, y) ? _occupant[Index(x, y)] : -1;

        public bool IsFree(int x, int y) => InBounds(x, y) && _occupant[Index(x, y)] == 0;
        public bool IsFree(Cell c) => IsFree(c.X, c.Y);

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
            for (int y = r.Y; y <= r.MaxY; y++)
                for (int x = r.X; x <= r.MaxX; x++)
                    _occupant[Index(x, y)] = entityId;
        }

        public void Clear(CellRect r)
        {
            for (int y = r.Y; y <= r.MaxY; y++)
                for (int x = r.X; x <= r.MaxX; x++)
                    _occupant[Index(x, y)] = 0;
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
        }
    }
}
