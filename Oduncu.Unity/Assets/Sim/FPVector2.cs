using System;

namespace Oduncu.Sim
{
    /// <summary>Fixed-point 2D vector in map tile units. X runs east, Y runs north.</summary>
    public readonly struct FPVector2 : IEquatable<FPVector2>
    {
        public readonly FP X;
        public readonly FP Y;

        public FPVector2(FP x, FP y) { X = x; Y = y; }

        public static readonly FPVector2 Zero = new FPVector2(FP.Zero, FP.Zero);

        /// <summary>Centre of a grid cell.</summary>
        public static FPVector2 CellCentre(int x, int y) => new FPVector2(FP.FromInt(x) + FP.Half, FP.FromInt(y) + FP.Half);
        public static FPVector2 CellCentre(Cell c) => CellCentre(c.X, c.Y);

        public Cell ToCell() => new Cell(X.FloorToInt(), Y.FloorToInt());

        public static FPVector2 operator +(FPVector2 a, FPVector2 b) => new FPVector2(a.X + b.X, a.Y + b.Y);
        public static FPVector2 operator -(FPVector2 a, FPVector2 b) => new FPVector2(a.X - b.X, a.Y - b.Y);
        public static FPVector2 operator *(FPVector2 a, FP s) => new FPVector2(a.X * s, a.Y * s);
        public static bool operator ==(FPVector2 a, FPVector2 b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(FPVector2 a, FPVector2 b) => !(a == b);

        public FP SqrMagnitude => X * X + Y * Y;
        public FP Magnitude => FP.Sqrt(SqrMagnitude);

        public FPVector2 Normalized
        {
            get
            {
                FP m = Magnitude;
                if (m == FP.Zero) return Zero;
                return new FPVector2(X / m, Y / m);
            }
        }

        public static FP Distance(FPVector2 a, FPVector2 b) => (a - b).Magnitude;
        public static FP SqrDistance(FPVector2 a, FPVector2 b) => (a - b).SqrMagnitude;
        public static FP Dot(FPVector2 a, FPVector2 b) => a.X * b.X + a.Y * b.Y;

        public bool Equals(FPVector2 other) => this == other;
        public override bool Equals(object obj) => obj is FPVector2 o && Equals(o);
        public override int GetHashCode() => unchecked(X.GetHashCode() * 397 ^ Y.GetHashCode());
        public override string ToString() => "(" + X + ", " + Y + ")";
    }

    /// <summary>Integer grid coordinate.</summary>
    public readonly struct Cell : IEquatable<Cell>
    {
        public readonly int X;
        public readonly int Y;

        public Cell(int x, int y) { X = x; Y = y; }

        public static bool operator ==(Cell a, Cell b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Cell a, Cell b) => !(a == b);

        /// <summary>Chessboard distance: the number of 8-way steps between two cells.</summary>
        public static int Chebyshev(Cell a, Cell b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

        public bool Equals(Cell other) => this == other;
        public override bool Equals(object obj) => obj is Cell o && Equals(o);
        public override int GetHashCode() => unchecked(X * 73856093 ^ Y * 19349663);
        public override string ToString() => "[" + X + "," + Y + "]";
    }

    /// <summary>Axis-aligned footprint of a building or resource on the grid.</summary>
    public readonly struct CellRect
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Size;

        public CellRect(int x, int y, int size) { X = x; Y = y; Size = size; }

        public int MaxX => X + Size - 1;
        public int MaxY => Y + Size - 1;

        public bool Contains(Cell c) => c.X >= X && c.X <= MaxX && c.Y >= Y && c.Y <= MaxY;

        /// <summary>Chessboard distance from a cell to the nearest cell of the rect. 0 when inside.</summary>
        public int DistanceTo(Cell c)
        {
            int dx = c.X < X ? X - c.X : (c.X > MaxX ? c.X - MaxX : 0);
            int dy = c.Y < Y ? Y - c.Y : (c.Y > MaxY ? c.Y - MaxY : 0);
            return Math.Max(dx, dy);
        }

        /// <summary>Nearest point on the rect's outer boundary to a position, in tile units.</summary>
        public FPVector2 NearestPoint(FPVector2 p)
        {
            FP minX = FP.FromInt(X), maxX = FP.FromInt(X + Size);
            FP minY = FP.FromInt(Y), maxY = FP.FromInt(Y + Size);
            return new FPVector2(FP.Clamp(p.X, minX, maxX), FP.Clamp(p.Y, minY, maxY));
        }

        public FPVector2 Centre => new FPVector2(FP.FromInt(X) + FP.Ratio(Size, 2), FP.FromInt(Y) + FP.Ratio(Size, 2));
    }
}
