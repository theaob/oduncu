namespace Oduncu.Sim
{
    /// <summary>
    /// FNV-1a 64-bit hash over simulation state. Clients exchange the result every second
    /// to detect desyncs, and tests compare it between runs.
    /// </summary>
    public sealed class StateHasher
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        private ulong _hash = Offset;

        public ulong Value => _hash;

        public void Reset() => _hash = Offset;

        public void Write(long value)
        {
            ulong v = (ulong)value;
            for (int i = 0; i < 8; i++)
            {
                _hash ^= v & 0xFF;
                _hash *= Prime;
                v >>= 8;
            }
        }

        public void Write(ulong value) => Write((long)value);
        public void Write(int value) => Write((long)value);
        public void Write(bool value) => Write(value ? 1L : 0L);
        public void Write(FP value) => Write(value.Raw);
        public void Write(FPVector2 value) { Write(value.X); Write(value.Y); }
        public void Write(Cell value) { Write(value.X); Write(value.Y); }
    }
}
