using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Roguehalla.Client
{
    /// <summary>
    /// Opt-in (<c>?perf=1</c>) per-system CPU sections for the frame-time recorder. Release WebGL players compile Unity's profiler
    /// markers out, so the few systems worth attributing are bracketed here with a Stopwatch timestamp instead. When the
    /// recorder is off (every normal session) <see cref="Begin"/> is one static bool test and <see cref="End"/> returns at once.
    /// No allocation: fixed arrays, indexed by <see cref="Sec"/>.
    /// </summary>
    public static class PerfMarks
    {
        public enum Sec
        {
            // engine phases (PlayerLoop brackets, PerfRecorder)
            Frame, PreUpdate, Scripts, Render, UiRebuild,
            // scripts we own
            NetPoll, Hud, Camera, Present, Actors, Collections, SurfaceFx, Vision, Props, WishFx, Effects, Atmosphere, Soundscape, MapUi,
            HudAccount, HudFlows, HudMap, HudEdge, HudDrawer, HudOwner, FxCues, Count
        }

        public static readonly string[] Names =
        {
            "frame", "preUpdate", "scripts", "render", "uiRebuild",
            "netPoll", "hud", "camera", "present", "actors", "collections", "surfaceFx", "vision", "props", "wishFx", "effects", "atmosphere", "soundscape", "mapUi",
            "hudAccount", "hudFlows", "hudMap", "hudEdge", "hudDrawer", "hudOwner", "fxCues",
        };

        public static bool On;
        static readonly double TickMs = 1000.0 / Stopwatch.Frequency;
        static readonly double[] _sum = new double[(int)Sec.Count];
        static readonly double[] _max = new double[(int)Sec.Count];
        static readonly double[] _frame = new double[(int)Sec.Count];
        static readonly int[] _calls = new int[(int)Sec.Count];
        static readonly double[] _alloc = new double[(int)Sec.Count];
        static readonly double[] _frameAlloc = new double[(int)Sec.Count];
        static int _frames;

        /// <summary>The collection counts read around one frame (FrameStart, ScriptsStart, ScriptsEnd, RenderStart, FrameEnd); -1 is "not read".</summary>
        public struct GcReads { public int FrameStart, ScriptsStart, ScriptsEnd, RenderStart, FrameEnd; }

        /// <summary>The session's state in one frame (five of these are 0 or 1 flags).</summary>
        public struct FrameContext { public int Actors, Walls, WallsChanged, WalkMapChanged, TerrainMapChanged, StateFrames, Snapshots, Dead, OrderPending, MapTiles; }

        /// <summary>A fixed row of one frame: every section's ms and heap growth, the five collection counts and the context.</summary>
        sealed class Row
        {
            public int Index; public double Seconds;
            public readonly double[] Ms = new double[(int)Sec.Count];
            public readonly double[] Bytes = new double[(int)Sec.Count];
            public readonly int[] Gc = new int[5];
            public FrameContext Ctx;
            public void CopyFrom(Row o)
            {
                Index = o.Index; Seconds = o.Seconds; Ctx = o.Ctx;
                System.Array.Copy(o.Ms, Ms, Ms.Length); System.Array.Copy(o.Bytes, Bytes, Bytes.Length); System.Array.Copy(o.Gc, Gc, Gc.Length);
            }
        }

        /// <summary>The worst frame of one section in the window, with a copy of the frame before and the frame after.</summary>
        sealed class Slot
        {
            public readonly Sec Section;
            public readonly Row Before = new Row(), At = new Row(), After = new Row();
            public bool HasBefore, HasAt, HasAfter, Waiting;
            public double Value;
            public Slot(Sec s) { Section = s; }
            public void Clear() { HasBefore = HasAt = HasAfter = Waiting = false; Value = 0; }
        }

        static Row _cur, _prev;
        static bool _hasPrev;
        static Slot[] _slots;
        static int _prevEnd = -1;
        static int _collectionsInFrames, _framesWithCollection, _stepBefore, _stepScripts, _stepBetween, _stepRender, _betweenFrames;
        static int _wallsMin, _wallsMax, _stateFrames, _snapshots, _mapTiles;
        static int _framesWallsChanged, _framesWalkMapChanged, _framesTerrainMapChanged, _framesDead, _framesOrderPending;

        /// <summary>A section start: timestamp and managed heap size. Bytes are heap growth in whole collector blocks, a lower bound (a collection or lazy sweeping lowers it).</summary>
        public struct Mark { public long T; public long Heap; }

        public static Mark Begin() { return On ? new Mark { T = Stopwatch.GetTimestamp(), Heap = System.GC.GetTotalMemory(false) } : default(Mark); }

        public static void End(Sec s, Mark start)
        {
            if (!On || start.T == 0L) return;
            double ms = (Stopwatch.GetTimestamp() - start.T) * TickMs;
            long grew = System.GC.GetTotalMemory(false) - start.Heap;
            Add(s, ms, grew);
        }

        static void EnsureRows()
        {
            if (_slots != null) return;
            _cur = new Row(); _prev = new Row();
            _slots = new[] { new Slot(Sec.Frame), new Slot(Sec.Actors), new Slot(Sec.Render) };
        }

        /// <summary>Adds one section's time and heap growth to the frame being recorded. No-op while the recorder is off.</summary>
        public static void Add(Sec s, double ms, long grewBytes)
        {
            if (!On) return;
            EnsureRows();
            int i = (int)s; _frame[i] += ms; _calls[i]++;
            if (grewBytes > 0) { _alloc[i] += grewBytes; _frameAlloc[i] += grewBytes; }
        }

        static int D(int a, int b) { return b > a ? b - a : 0; }

        /// <summary>Closes one frame: per-section totals go into the window sums, maxima, the frame row, the three worst-frame slots and the counters.</summary>
        public static void EndFrame(GcReads gc, FrameContext ctx, double seconds)
        {
            if (!On) return;
            EnsureRows();
            Row r = _cur;
            r.Index = _frames; r.Seconds = seconds; r.Ctx = ctx;
            _frames++;
            for (int i = 0; i < _frame.Length; i++)
            {
                double v = _frame[i]; _sum[i] += v; if (v > _max[i]) _max[i] = v;
                r.Ms[i] = v; r.Bytes[i] = _frameAlloc[i]; _frame[i] = 0; _frameAlloc[i] = 0;
            }
            r.Gc[0] = gc.FrameStart; r.Gc[1] = gc.ScriptsStart; r.Gc[2] = gc.ScriptsEnd; r.Gc[3] = gc.RenderStart; r.Gc[4] = gc.FrameEnd;

            for (int k = 0; k < _slots.Length; k++)
            {
                Slot s = _slots[k]; double v = r.Ms[(int)s.Section];
                if (!s.HasAt || v > s.Value)
                {
                    s.At.CopyFrom(r); s.HasAt = true; s.Value = v;
                    if (_hasPrev) { s.Before.CopyFrom(_prev); s.HasBefore = true; } else s.HasBefore = false;
                    s.HasAfter = false; s.Waiting = true;
                }
                else if (s.Waiting) { s.After.CopyFrom(r); s.HasAfter = true; s.Waiting = false; }
            }

            if (gc.FrameStart >= 0)
            {
                int inFrame = D(gc.FrameStart, gc.FrameEnd);
                _collectionsInFrames += inFrame; if (inFrame > 0) _framesWithCollection++;
                if (D(gc.FrameStart, gc.ScriptsStart) > 0) _stepBefore++;
                if (D(gc.ScriptsStart, gc.ScriptsEnd) > 0) _stepScripts++;
                if (D(gc.ScriptsEnd, gc.RenderStart) > 0) _stepBetween++;
                if (D(gc.RenderStart, gc.FrameEnd) > 0) _stepRender++;
                if (_prevEnd >= 0) _betweenFrames += D(_prevEnd, gc.FrameStart);
            }
            _prevEnd = gc.FrameEnd;

            if (_frames == 1) { _wallsMin = ctx.Walls; _wallsMax = ctx.Walls; }
            else { if (ctx.Walls < _wallsMin) _wallsMin = ctx.Walls; if (ctx.Walls > _wallsMax) _wallsMax = ctx.Walls; }
            _stateFrames += ctx.StateFrames; _snapshots += ctx.Snapshots; _mapTiles = ctx.MapTiles;
            if (ctx.WallsChanged == 1) _framesWallsChanged++;
            if (ctx.WalkMapChanged == 1) _framesWalkMapChanged++;
            if (ctx.TerrainMapChanged == 1) _framesTerrainMapChanged++;
            if (ctx.Dead == 1) _framesDead++;
            if (ctx.OrderPending == 1) _framesOrderPending++;

            _cur = _prev; _prev = r; _hasPrev = true;
        }

        public static void Reset() { Reset(-1); }

        /// <summary>Clears the window: sums, rows, slots, counters and the frame index. <paramref name="gcCount"/> is the collection count now (-1: unknown).</summary>
        public static void Reset(int gcCount)
        {
            EnsureRows();
            _frames = 0;
            for (int i = 0; i < _sum.Length; i++) { _sum[i] = 0; _max[i] = 0; _frame[i] = 0; _calls[i] = 0; _alloc[i] = 0; _frameAlloc[i] = 0; }
            for (int k = 0; k < _slots.Length; k++) _slots[k].Clear();
            _hasPrev = false; _prevEnd = gcCount;
            _collectionsInFrames = _framesWithCollection = _stepBefore = _stepScripts = _stepBetween = _stepRender = _betweenFrames = 0;
            _wallsMin = _wallsMax = _stateFrames = _snapshots = _mapTiles = 0;
            _framesWallsChanged = _framesWalkMapChanged = _framesTerrainMapChanged = _framesDead = _framesOrderPending = 0;
        }

        public static int Frames { get { return _frames; } }
        public static double MeanMs(int i) { return _frames > 0 ? _sum[i] / _frames : 0; }
        public static double MaxMs(int i) { return _max[i]; }
        public static int Calls(int i) { return _calls[i]; }
        /// <summary>Managed bytes allocated inside the section, per frame.</summary>
        public static double AllocPerFrame(int i) { return _frames > 0 ? _alloc[i] / _frames : 0; }

        // ------------------------------------------------------------------------------------------------ JSON

        static string Ms(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
        static string Num(double v) { return v.ToString("0", CultureInfo.InvariantCulture); }

        static void AppendRow(StringBuilder sb, Row r, bool has)
        {
            if (!has || r == null) { sb.Append("null"); return; }
            sb.Append("{\"i\":").Append(r.Index).Append(",\"t\":").Append(Ms(r.Seconds)).Append(",\"ms\":[");
            for (int i = 0; i < r.Ms.Length; i++) { if (i > 0) sb.Append(','); sb.Append(Ms(r.Ms[i])); }
            sb.Append("],\"bytes\":[");
            for (int i = 0; i < r.Bytes.Length; i++) { if (i > 0) sb.Append(','); sb.Append(Num(r.Bytes[i])); }
            sb.Append("],\"gc\":[");
            for (int i = 0; i < r.Gc.Length; i++) { if (i > 0) sb.Append(','); sb.Append(r.Gc[i]); }
            FrameContext c = r.Ctx;
            sb.Append("],\"ctx\":{\"actors\":").Append(c.Actors).Append(",\"walls\":").Append(c.Walls).Append(",\"wallsChanged\":").Append(c.WallsChanged)
              .Append(",\"walkMapChanged\":").Append(c.WalkMapChanged).Append(",\"terrainMapChanged\":").Append(c.TerrainMapChanged)
              .Append(",\"stateFrames\":").Append(c.StateFrames).Append(",\"snapshots\":").Append(c.Snapshots).Append(",\"dead\":").Append(c.Dead)
              .Append(",\"orderPending\":").Append(c.OrderPending).Append(",\"mapTiles\":").Append(c.MapTiles).Append("}}");
        }

        static void AppendSlot(StringBuilder sb, string name, Sec section)
        {
            Slot s = null;
            if (_slots != null) for (int k = 0; k < _slots.Length; k++) if (_slots[k].Section == section) s = _slots[k];
            bool at = s != null && s.HasAt;
            sb.Append('"').Append(name).Append("\":{\"section\":\"").Append(name).Append("\",\"maxMs\":").Append(at ? Ms(s.Value) : "0").Append(",\"before\":");
            AppendRow(sb, s != null ? s.Before : null, at && s.HasBefore);
            sb.Append(",\"at\":"); AppendRow(sb, s != null ? s.At : null, at);
            sb.Append(",\"after\":"); AppendRow(sb, s != null ? s.After : null, at && s.HasAfter);
            sb.Append('}');
        }

        /// <summary>Appends exactly one JSON object member, <c>"worstFrames":{...}</c>: no leading or trailing comma.</summary>
        public static void AppendWorstFrames(StringBuilder sb)
        {
            sb.Append("\"worstFrames\":{\"sections\":[");
            for (int i = 0; i < Names.Length; i++) { if (i > 0) sb.Append(','); sb.Append('"').Append(Names[i]).Append('"'); }
            sb.Append("],\"slots\":{");
            AppendSlot(sb, "frame", Sec.Frame); sb.Append(',');
            AppendSlot(sb, "actors", Sec.Actors); sb.Append(',');
            AppendSlot(sb, "render", Sec.Render);
            sb.Append("},\"gc\":{\"collectionsInFrames\":").Append(_collectionsInFrames).Append(",\"framesWithCollection\":").Append(_framesWithCollection)
              .Append(",\"framesStepBeforeScripts\":").Append(_stepBefore).Append(",\"framesStepInScripts\":").Append(_stepScripts)
              .Append(",\"framesStepBetweenScriptsAndRender\":").Append(_stepBetween).Append(",\"framesStepInRender\":").Append(_stepRender)
              .Append(",\"collectionsBetweenFrames\":").Append(_betweenFrames)
              .Append("},\"context\":{\"wallsMin\":").Append(_wallsMin).Append(",\"wallsMax\":").Append(_wallsMax).Append(",\"stateFrames\":").Append(_stateFrames)
              .Append(",\"snapshots\":").Append(_snapshots).Append(",\"mapTiles\":").Append(_mapTiles)
              .Append(",\"framesWallsChanged\":").Append(_framesWallsChanged).Append(",\"framesWalkMapChanged\":").Append(_framesWalkMapChanged)
              .Append(",\"framesTerrainMapChanged\":").Append(_framesTerrainMapChanged).Append(",\"framesDead\":").Append(_framesDead)
              .Append(",\"framesOrderPending\":").Append(_framesOrderPending).Append("}}");
        }
    }
}
