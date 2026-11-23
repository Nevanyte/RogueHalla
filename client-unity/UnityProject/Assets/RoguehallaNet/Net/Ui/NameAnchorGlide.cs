namespace Roguehalla.Net.Ui
{
    /// <summary>Per layout key: a pack name's anchor glides when its token (carrier, singled, size) changes. Stub: Step returns the target and keeps nothing.</summary>
    public sealed class NameAnchorGlide
    {
        /// <summary>Seconds a step of any size takes to close; the largest good dt.</summary>
        public const float GlideSeconds = .15f, MaxDt = 1f;

        public void Begin() { }
        public void End() { }
        public void Clear() { }

        public void Step(long key, long token, float targetX, float targetY, float dt, bool reduced, out float x, out float y)
        {
            x = targetX; y = targetY;
        }

        public bool Carry(long oldKey, long newKey) { return false; }

        /// <summary>Entries now.</summary>
        public int Count { get { return 0; } }

        /// <summary>The residual of a key (drawn minus target); false when the key has no entry.</summary>
        public bool TryResidual(long key, out float x, out float y) { x = y = 0f; return false; }
    }
}
