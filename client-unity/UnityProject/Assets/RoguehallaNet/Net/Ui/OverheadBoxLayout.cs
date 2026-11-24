using System;

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
        const int MaxPasses = 32;

        public static long SerialKey(int serial) { return serial; }
        public static long CampKey(int camp) { return CampKeyBase + camp; }
        public static long FighterKey(uint actorId) { return FighterKeyBase + actorId; }

        // Boxes (parallel arrays), their parts (flat, contiguous per box), fixed rectangles, memory entries.
        long[] _key = new long[32]; byte[] _cls = new byte[32]; float[] _x = new float[32], _y = new float[32];
        bool[] _avoid = new bool[32]; int[] _partStart = new int[32], _partCount = new int[32];
        float[] _lift = new float[32], _target = new float[32]; int[] _order = new int[32]; bool[] _done = new bool[32];
        int _boxes;
        float[] _px0 = new float[128], _py0 = new float[128], _px1 = new float[128], _py1 = new float[128];
        int _parts;
        float[] _fx0 = new float[32], _fy0 = new float[32], _fx1 = new float[32], _fy1 = new float[32];
        int _fixed;
        long[] _mKey = new long[32]; LabelLift[] _mLift = new LabelLift[32]; float[] _mAge = new float[32]; bool[] _mSeen = new bool[32];
        int _memory;

        /// <summary>Starts a frame: clears boxes, parts and fixed rectangles; keeps the memory.</summary>
        public void Begin() { _boxes = 0; _parts = 0; _fixed = 0; }

        /// <summary>Adds a box at a world origin and returns its index.</summary>
        public int AddBox(long key, byte cls, float x, float y, bool avoidFixed)
        {
            if (_boxes == _key.Length)
            {
                int n = _boxes * 2;
                Array.Resize(ref _key, n); Array.Resize(ref _cls, n); Array.Resize(ref _x, n); Array.Resize(ref _y, n);
                Array.Resize(ref _avoid, n); Array.Resize(ref _partStart, n); Array.Resize(ref _partCount, n);
                Array.Resize(ref _lift, n); Array.Resize(ref _target, n); Array.Resize(ref _order, n); Array.Resize(ref _done, n);
            }
            int i = _boxes++;
            _key[i] = key; _cls[i] = cls; _x[i] = x; _y[i] = y; _avoid[i] = avoidFixed;
            _partStart[i] = _parts; _partCount[i] = 0; _lift[i] = 0f; _target[i] = 0f;
            return i;
        }

        /// <summary>Adds a part (offsets from the origin) to the latest box (with no box, nothing).</summary>
        public void AddPart(float x0, float y0, float x1, float y1)
        {
            if (_boxes == 0) return;
            if (_parts == _px0.Length)
            {
                int n = _parts * 2;
                Array.Resize(ref _px0, n); Array.Resize(ref _py0, n); Array.Resize(ref _px1, n); Array.Resize(ref _py1, n);
            }
            _px0[_parts] = x0; _py0[_parts] = y0; _px1[_parts] = x1; _py1[_parts] = y1; _parts++;
            _partCount[_boxes - 1]++;
        }

        /// <summary>Adds a world-unit rectangle that never moves in this solve.</summary>
        public void AddFixed(float x0, float y0, float x1, float y1)
        {
            if (_fixed == _fx0.Length)
            {
                int n = _fixed * 2;
                Array.Resize(ref _fx0, n); Array.Resize(ref _fy0, n); Array.Resize(ref _fx1, n); Array.Resize(ref _fy1, n);
            }
            _fx0[_fixed] = x0; _fy0[_fixed] = y0; _fx1[_fixed] = x1; _fy1[_fixed] = y1; _fixed++;
        }

        /// <summary>Solves every box. Nothing here reads a clock.</summary>
        public void Solve(float dt, bool reducedMotion)
        {
            bool good = dt > 0f && dt <= ForgetSeconds;
            if (!good) _memory = 0;
            for (int i = 0; i < _boxes; i++) _done[i] = false;
            for (int i = 0; i < _memory; i++) _mSeen[i] = false;
            for (int pos = 0; pos < _boxes; pos++)
            {
                int b = -1;
                for (int i = 0; i < _boxes; i++)
                {
                    if (_done[i]) continue;
                    if (b < 0 || _cls[i] < _cls[b] || (_cls[i] == _cls[b] && _key[i] < _key[b])) b = i;
                }
                _done[b] = true; _order[pos] = b;
                float w = Place(b, pos, PadEnter);
                if (!good)
                {
                    var s = LabelLift.Start(w);
                    _lift[b] = s.Value; _target[b] = s.Target;
                    continue;
                }
                int e = Find(_key[b]);
                LabelLift next;
                if (e < 0) next = LabelLift.Start(w);
                else
                {
                    float t = _mLift[e].Target;
                    if (t > LabelLift.Deadband && w < t - LabelLift.Deadband)
                    {
                        float wk = Place(b, pos, PadKeep);
                        if (wk > w) w = Math.Min(wk, t);
                    }
                    next = _mLift[e].Step(w, dt, reducedMotion);
                }
                _lift[b] = next.Value; _target[b] = next.Target;
                if (e < 0) e = Add(_key[b]);
                _mLift[e] = next; _mAge[e] = 0f; _mSeen[e] = true;
            }
            if (!good) return;
            for (int i = _memory - 1; i >= 0; i--)
            {
                if (_mSeen[i]) continue;
                _mAge[i] += dt;
                if (_mAge[i] > ForgetSeconds)
                {
                    int last = --_memory;
                    _mKey[i] = _mKey[last]; _mLift[i] = _mLift[last]; _mAge[i] = _mAge[last]; _mSeen[i] = _mSeen[last];
                }
            }
        }

        int Find(long key)
        {
            for (int i = 0; i < _memory; i++) if (_mKey[i] == key) return i;
            return -1;
        }

        int Add(long key)
        {
            if (_memory == _mKey.Length)
            {
                int n = _memory * 2;
                Array.Resize(ref _mKey, n); Array.Resize(ref _mLift, n); Array.Resize(ref _mAge, n); Array.Resize(ref _mSeen, n);
            }
            _mKey[_memory] = key;
            return _memory++;
        }

        // The lift box b needs to clear the boxes visited before it (at their targets) and the fixed rectangles.
        float Place(int b, int pos, float padX)
        {
            float lift = 0f, bx = _x[b], by = _y[b];
            int p0 = _partStart[b], pn = _partCount[b];
            for (int pass = 0; pass < MaxPasses; pass++)
            {
                float before = lift;
                for (int p = p0; p < p0 + pn; p++)
                {
                    float rx0 = bx + _px0[p], rx1 = bx + _px1[p], ry0 = by + _py0[p], ry1 = by + _py1[p];
                    for (int o = 0; o < pos; o++)
                    {
                        int ob = _order[o], ot = _partStart[ob], oc = _partCount[ob];
                        float ox = _x[ob], oy = _y[ob] + _target[ob];
                        for (int q = ot; q < ot + oc; q++)
                            lift = Raise(lift, rx0, rx1, ry0, ry1, ox + _px0[q], oy + _py0[q], ox + _px1[q], oy + _py1[q], padX);
                    }
                    if (_avoid[b])
                        for (int q = 0; q < _fixed; q++)
                            lift = Raise(lift, rx0, rx1, ry0, ry1, _fx0[q], _fy0[q], _fx1[q], _fy1[q], padX);
                }
                if (!(lift > before)) break;
            }
            return Math.Min(lift, MaxLift);
        }

        static float Raise(float lift, float rx0, float rx1, float ry0, float ry1, float qx0, float qy0, float qx1, float qy1, float padX)
        {
            if (rx0 - padX < qx1 && qx0 < rx1 + padX && ry0 + lift < qy1 + Gap && qy0 < ry1 + lift)
                return Math.Max(lift, qy1 + Gap - ry0);
            return lift;
        }

        /// <summary>Boxes added since Begin.</summary>
        public int BoxCount { get { return _boxes; } }
        /// <summary>The drawn lift of a box, world units up.</summary>
        public float Lift(int box) { return _lift[box]; }
        /// <summary>The lift a box moves toward, which later boxes avoid.</summary>
        public float Target(int box) { return _target[box]; }
        /// <summary>Parts of a box.</summary>
        public int PartCount(int box) { return _partCount[box]; }
        /// <summary>A part in world units (origin plus offset), raised by Target if atTarget, else by Lift.</summary>
        public void PartRect(int box, int part, bool atTarget, out float x0, out float y0, out float x1, out float y1)
        {
            int p = _partStart[box] + part;
            float up = atTarget ? _target[box] : _lift[box];
            x0 = _x[box] + _px0[p]; x1 = _x[box] + _px1[p];
            y0 = _y[box] + _py0[p] + up; y1 = _y[box] + _py1[p] + up;
        }
        /// <summary>The remembered lift of a key; false when it has none.</summary>
        public bool TryMemory(long key, out float value, out float target)
        {
            int e = Find(key);
            if (e < 0) { value = target = 0f; return false; }
            value = _mLift[e].Value; target = _mLift[e].Target; return true;
        }
        /// <summary>Copies the memory of oldKey to newKey when oldKey has one and newKey has none; true when it copied (age 0); the old entry stays.</summary>
        public bool Carry(long oldKey, long newKey)
        {
            int o = Find(oldKey);
            if (o < 0 || Find(newKey) >= 0) return false;
            int n = Add(newKey);
            _mLift[n] = _mLift[o]; _mAge[n] = 0f; _mSeen[n] = false;
            return true;
        }
        /// <summary>Keys with a memory.</summary>
        public int MemoryCount { get { return _memory; } }
        /// <summary>Forgets the whole memory (the Tab map, a clock cut).</summary>
        public void Clear() { _memory = 0; }
    }
}
