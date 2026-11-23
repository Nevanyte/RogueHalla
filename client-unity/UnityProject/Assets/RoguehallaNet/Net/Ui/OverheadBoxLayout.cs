namespace Roguehalla.Net.Ui
{
    /// <summary>Lifts boxes (a name and its marks) clear of each other, with one memory per key.</summary>
    public sealed class OverheadBoxLayout
    {
        /// <summary>World units. Gap: clearance above what a box rises over. PadEnter, PadKeep (each side, PadKeep
        /// larger): a box starts to yield below the first and keeps its lift below the second. MaxLift: the cap.
        /// ForgetSeconds: how long a key may stay unseen before its memory goes; also the largest good dt.</summary>
        public const float Gap = .10f, PadEnter = .16f, PadKeep = .40f, MaxLift = 2.5f, ForgetSeconds = 1f;
        /// <summary>Visiting order. Prompt, Giver and Prop are reserved for later packets.</summary>
        public const byte ClassLocal = 10, ClassPrompt = 15, ClassFighter = 20, ClassGiver = 25, ClassElite = 30,
            ClassMob = 40, ClassProp = 50;
        /// <summary>Keys: a serial key is the art's layout serial; a camp key is CampKeyBase plus the one-based camp
        /// number (above every serial key); a fighter key is FighterKeyBase plus the fighter's actor id (above every
        /// camp key and every serial key).</summary>
        public const long CampKeyBase = 1L << 32, FighterKeyBase = 1L << 33;

        public static long SerialKey(int serial) { return 0; }
        public static long CampKey(int camp) { return 0; }
        public static long FighterKey(uint actorId) { return 0; }

        /// <summary>Starts a frame: clears boxes, parts and fixed rectangles; keeps the memory.</summary>
        public void Begin() { }
        /// <summary>Adds a box at a world origin and returns its index.</summary>
        public int AddBox(long key, byte cls, float x, float y, bool avoidFixed) { return 0; }
        /// <summary>Adds a part (offsets from the origin) to the latest box (with no box, nothing).</summary>
        public void AddPart(float x0, float y0, float x1, float y1) { }
        /// <summary>Adds a world-unit rectangle that never moves in this solve.</summary>
        public void AddFixed(float x0, float y0, float x1, float y1) { }
        /// <summary>Solves every box. Nothing here reads a clock.</summary>
        public void Solve(float dt, bool reducedMotion) { }
        /// <summary>Boxes added since Begin.</summary>
        public int BoxCount { get { return 0; } }
        /// <summary>The drawn lift of a box, world units up.</summary>
        public float Lift(int box) { return 0f; }
        /// <summary>The lift a box moves toward, which later boxes avoid.</summary>
        public float Target(int box) { return 0f; }
        /// <summary>Parts of a box.</summary>
        public int PartCount(int box) { return 0; }
        /// <summary>A part in world units (origin plus offset), raised by Target if atTarget, else by Lift.</summary>
        public void PartRect(int box, int part, bool atTarget, out float x0, out float y0, out float x1, out float y1)
        {
            x0 = y0 = x1 = y1 = 0f;
        }
        /// <summary>The remembered lift of a key; false when it has none.</summary>
        public bool TryMemory(long key, out float value, out float target) { value = target = 0f; return false; }
        /// <summary>Keys with a memory.</summary>
        public int MemoryCount { get { return 0; } }
        /// <summary>Forgets the whole memory (the Tab map, a clock cut).</summary>
        public void Clear() { }
    }
}
