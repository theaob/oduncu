using System;
using System.Globalization;

namespace Oduncu.Sim
{
    /// <summary>
    /// Q47.16 fixed-point number. Every arithmetic operation in the simulation goes through
    /// this type so that results are bit-identical on every device and platform.
    /// float and double are banned inside the Oduncu.Sim assembly.
    /// The API is deliberately shaped like Photon Quantum's FP so it can be swapped later.
    /// </summary>
    public readonly struct FP : IEquatable<FP>, IComparable<FP>
    {
        public const int FractionBits = 16;
        public const long OneRaw = 1L << FractionBits;
        private const long FractionMask = OneRaw - 1;

        public readonly long Raw;

        private FP(long raw) { Raw = raw; }

        public static readonly FP Zero = new FP(0);
        public static readonly FP One = new FP(OneRaw);
        public static readonly FP Half = new FP(OneRaw >> 1);
        public static readonly FP MaxValue = new FP(long.MaxValue);
        public static readonly FP MinValue = new FP(long.MinValue);

        public static FP FromRaw(long raw) => new FP(raw);
        public static FP FromInt(int value) => new FP((long)value << FractionBits);

        /// <summary>Exact rational constant, e.g. FP.Ratio(8, 100) for 0.08.</summary>
        public static FP Ratio(int numerator, int denominator)
        {
            if (denominator == 0) throw new DivideByZeroException();
            return new FP(((long)numerator << FractionBits) / denominator);
        }

        public static implicit operator FP(int value) => FromInt(value);

        public static FP operator +(FP a, FP b) => new FP(a.Raw + b.Raw);
        public static FP operator -(FP a, FP b) => new FP(a.Raw - b.Raw);
        public static FP operator -(FP a) => new FP(-a.Raw);

        public static FP operator *(FP a, FP b)
        {
            // Split both operands into integer and fractional parts so the intermediate
            // products never overflow 64 bits for the value ranges the simulation uses.
            long ai = a.Raw >> FractionBits;
            long af = a.Raw & FractionMask;
            long bi = b.Raw >> FractionBits;
            long bf = b.Raw & FractionMask;
            long result = (ai * bi) << FractionBits;
            result += ai * bf + af * bi;
            result += (af * bf) >> FractionBits;
            return new FP(result);
        }

        public static FP operator /(FP a, FP b)
        {
            if (b.Raw == 0) throw new DivideByZeroException();
            long quotient = a.Raw / b.Raw;
            long remainder = a.Raw % b.Raw;
            return new FP((quotient << FractionBits) + ((remainder << FractionBits) / b.Raw));
        }

        public static FP operator *(FP a, int b) => new FP(a.Raw * b);
        public static FP operator /(FP a, int b) => new FP(a.Raw / b);

        public static bool operator ==(FP a, FP b) => a.Raw == b.Raw;
        public static bool operator !=(FP a, FP b) => a.Raw != b.Raw;
        public static bool operator <(FP a, FP b) => a.Raw < b.Raw;
        public static bool operator >(FP a, FP b) => a.Raw > b.Raw;
        public static bool operator <=(FP a, FP b) => a.Raw <= b.Raw;
        public static bool operator >=(FP a, FP b) => a.Raw >= b.Raw;

        public static FP Abs(FP a) => a.Raw < 0 ? new FP(-a.Raw) : a;
        public static FP Min(FP a, FP b) => a.Raw < b.Raw ? a : b;
        public static FP Max(FP a, FP b) => a.Raw > b.Raw ? a : b;
        public static FP Clamp(FP v, FP min, FP max) => v.Raw < min.Raw ? min : (v.Raw > max.Raw ? max : v);

        public int FloorToInt() => (int)(Raw >> FractionBits);
        public int CeilToInt() => (int)((Raw + FractionMask) >> FractionBits);
        public int RoundToInt() => (int)((Raw + Half.Raw) >> FractionBits);

        public static FP Sqrt(FP a)
        {
            if (a.Raw < 0) throw new ArgumentOutOfRangeException(nameof(a), "Sqrt of negative value");
            if (a.Raw == 0) return Zero;
            // sqrt(raw / 2^16) * 2^16 == sqrt(raw * 2^16). Shift first when it cannot overflow.
            if (a.Raw < (1L << 47))
            {
                return new FP((long)IntegerSqrt((ulong)a.Raw << FractionBits));
            }
            // Very large values: lose 8 bits of fractional precision rather than overflow.
            return new FP((long)IntegerSqrt((ulong)a.Raw) << (FractionBits / 2));
        }

        /// <summary>Floor of the square root, computed with integer Newton iteration.</summary>
        public static ulong IntegerSqrt(ulong n)
        {
            if (n == 0) return 0;
            int bits = 0;
            for (ulong t = n; t != 0; t >>= 1) bits++;
            ulong x = 1UL << ((bits + 1) / 2);
            while (true)
            {
                ulong y = (x + n / x) >> 1;
                if (y >= x) return x;
                x = y;
            }
        }

        public bool Equals(FP other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is FP other && Equals(other);
        public override int GetHashCode() => Raw.GetHashCode();
        public int CompareTo(FP other) => Raw.CompareTo(other.Raw);

        /// <summary>For presentation and debugging only. Never feed the result back into the simulation.</summary>
        public float AsFloat => (float)Raw / OneRaw;
        public double AsDouble => (double)Raw / OneRaw;

        public override string ToString() => AsDouble.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
