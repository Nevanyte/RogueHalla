using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Sec = Roguehalla.Client.PerfMarks.Sec;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-PERF-WORST-FRAME-1: the frame recorder's window keeps the worst frame of the sections frame, actors and render with the
    /// frame before and after it, counts collections by phase, carries the session's context, and publishes all of it as one JSON member.
    /// Everything here is driven through PerfMarks.Add and PerfMarks.EndFrame with the milliseconds, the five collection counts, the
    /// context and the window time passed in: nothing reads a clock, a session or the engine. The JSON is parsed with JsonUtility; it
    /// turns a null object into a default one, so a row counts as present only when its ms array has Sec.Count entries, and the nulls
    /// themselves are counted on the text.
    /// </summary>
    public sealed class PerfWorstFrameEditModeTests
    {
        const int N = (int)Sec.Count;

        [Serializable] public sealed class CtxDto { public int actors, walls, wallsChanged, walkMapChanged, terrainMapChanged, stateFrames, snapshots, dead, orderPending, mapTiles; }
        [Serializable] public sealed class RowDto { public int i; public double t; public double[] ms; public double[] bytes; public int[] gc; public CtxDto ctx; }
        [Serializable] public sealed class SlotDto { public string section; public double maxMs; public RowDto before, at, after; }
        [Serializable] public sealed class SlotsDto { public SlotDto frame, actors, render; }
        [Serializable] public sealed class GcDto
        {
            public int collectionsInFrames, framesWithCollection, framesStepBeforeScripts, framesStepInScripts, framesStepBetweenScriptsAndRender,
                framesStepInRender, collectionsBetweenFrames;
        }
        [Serializable] public sealed class ContextDto
        {
            public int wallsMin, wallsMax, stateFrames, snapshots, mapTiles, framesWallsChanged, framesWalkMapChanged, framesTerrainMapChanged, framesDead, framesOrderPending;
        }
        [Serializable] public sealed class WorstDto { public string[] sections; public SlotsDto slots; public GcDto gc; public ContextDto context; }
        [Serializable] public sealed class WrapperDto { public WorstDto worstFrames; }

        bool _savedOn;
        CultureInfo _savedCulture;
        int _n;

        static PerfMarks.GcReads s_gc;
        static PerfMarks.FrameContext s_ctx;
        static double s_seconds;
        static int s_n;

        [SetUp]
        public void SetUp()
        {
            _savedOn = PerfMarks.On; _savedCulture = CultureInfo.CurrentCulture;
            PerfMarks.On = true; _n = 0;
            PerfMarks.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            CultureInfo.CurrentCulture = _savedCulture;
            PerfMarks.Reset();
            PerfMarks.On = _savedOn;
        }

        // ------------------------------------------------------------------------------------------------ helpers

        static PerfMarks.GcReads NoGc() { return new PerfMarks.GcReads { FrameStart = -1, ScriptsStart = -1, ScriptsEnd = -1, RenderStart = -1, FrameEnd = -1 }; }
        static PerfMarks.GcReads Gc(int[] a) { return new PerfMarks.GcReads { FrameStart = a[0], ScriptsStart = a[1], ScriptsEnd = a[2], RenderStart = a[3], FrameEnd = a[4] }; }

        static PerfMarks.FrameContext Ctx(int actors, int walls, int wallsChanged, int walkMapChanged, int terrainMapChanged, int stateFrames, int snapshots, int dead, int orderPending, int mapTiles)
        {
            return new PerfMarks.FrameContext { Actors = actors, Walls = walls, WallsChanged = wallsChanged, WalkMapChanged = walkMapChanged, TerrainMapChanged = terrainMapChanged,
                StateFrames = stateFrames, Snapshots = snapshots, Dead = dead, OrderPending = orderPending, MapTiles = mapTiles };
        }

        /// <summary>One recorded frame: ms for the three sections (frame, actors, render), growth for actors, then the frame closes at 0.05 s per frame.</summary>
        void Feed(double frameMs, double actorsMs, double renderMs, long actorsBytes = 0, PerfMarks.GcReads? gc = null, PerfMarks.FrameContext? ctx = null)
        {
            PerfMarks.Add(Sec.Frame, frameMs, 0);
            PerfMarks.Add(Sec.Actors, actorsMs, actorsBytes);
            PerfMarks.Add(Sec.Render, renderMs, 0);
            PerfMarks.EndFrame(gc ?? NoGc(), ctx ?? default(PerfMarks.FrameContext), _n * .05);
            _n++;
        }

        void Fresh() { _n = 0; PerfMarks.Reset(); }
        void Fresh(int gcCount) { _n = 0; PerfMarks.Reset(gcCount); }

        static string Json()
        {
            var sb = new StringBuilder();
            PerfMarks.AppendWorstFrames(sb);
            return "{" + sb + "}";
        }

        static WorstDto Parse(string json)
        {
            var w = JsonUtility.FromJson<WrapperDto>(json);
            return w != null && w.worstFrames != null ? w.worstFrames : new WorstDto();
        }

        static WorstDto Parse() { return Parse(Json()); }

        static int Count(string json, string needle) { return Regex.Matches(json, Regex.Escape(needle)).Count; }

        static bool Present(RowDto r) { return r != null && r.ms != null && r.ms.Length == N; }

        /// <summary>The row's frame index, asserting the row is there; -999 when it is not, so a later line fails on a value, never on a null.</summary>
        static int RowI(RowDto r, string what)
        {
            Assert.IsTrue(Present(r), what + " is a row of " + N + " sections");
            return Present(r) ? r.i : -999;
        }

        static double RowT(RowDto r) { return Present(r) ? r.t : double.NaN; }
        static double RowMs(RowDto r, Sec s) { return Present(r) ? r.ms[(int)s] : double.NaN; }
        static double RowBytes(RowDto r, Sec s) { return Present(r) && r.bytes != null && r.bytes.Length == N ? r.bytes[(int)s] : double.NaN; }
        static int[] RowGc(RowDto r) { return Present(r) && r.gc != null ? r.gc : new int[0]; }

        static SlotDto Slot(WorstDto w, string name)
        {
            SlotDto s = null;
            if (w != null && w.slots != null) s = name == "frame" ? w.slots.frame : name == "actors" ? w.slots.actors : w.slots.render;
            return s ?? new SlotDto();
        }

        static void AssertNeighbour(RowDto r, int expected, string what)
        {
            if (expected < 0) Assert.IsFalse(Present(r), what + " is null");
            else Assert.AreEqual(expected, RowI(r, what), what + " frame index");
        }

        static void AssertSlot(WorstDto w, string name, int at, int before, int after, double maxMs)
        {
            var s = Slot(w, name);
            Assert.AreEqual(name, s.section, name + " slot names its section");
            Assert.AreEqual(at, RowI(s.at, name + ".at"), name + ".at frame index");
            AssertNeighbour(s.before, before, name + ".before");
            AssertNeighbour(s.after, after, name + ".after");
            Assert.AreEqual(maxMs, s.maxMs, 1e-6, name + ".maxMs");
        }

        static void AssertCtx(PerfMarks.FrameContext e, RowDto r, string what)
        {
            Assert.IsTrue(Present(r), what + " is a row of " + N + " sections");
            var a = r != null && r.ctx != null ? r.ctx : new CtxDto();
            Assert.AreEqual(e.Actors, a.actors, what + " actors");
            Assert.AreEqual(e.Walls, a.walls, what + " walls");
            Assert.AreEqual(e.WallsChanged, a.wallsChanged, what + " wallsChanged");
            Assert.AreEqual(e.WalkMapChanged, a.walkMapChanged, what + " walkMapChanged");
            Assert.AreEqual(e.TerrainMapChanged, a.terrainMapChanged, what + " terrainMapChanged");
            Assert.AreEqual(e.StateFrames, a.stateFrames, what + " stateFrames");
            Assert.AreEqual(e.Snapshots, a.snapshots, what + " snapshots");
            Assert.AreEqual(e.Dead, a.dead, what + " dead");
            Assert.AreEqual(e.OrderPending, a.orderPending, what + " orderPending");
            Assert.AreEqual(e.MapTiles, a.mapTiles, what + " mapTiles");
        }

        static void AssertGcCounters(GcDto g, int inFrames, int withCollection, int before, int inScripts, int between, int inRender, int betweenFrames, string what)
        {
            g = g ?? new GcDto();
            Assert.AreEqual(withCollection, g.framesWithCollection, what + " framesWithCollection");
            Assert.AreEqual(before, g.framesStepBeforeScripts, what + " framesStepBeforeScripts");
            Assert.AreEqual(inScripts, g.framesStepInScripts, what + " framesStepInScripts");
            Assert.AreEqual(between, g.framesStepBetweenScriptsAndRender, what + " framesStepBetweenScriptsAndRender");
            Assert.AreEqual(inRender, g.framesStepInRender, what + " framesStepInRender");
            Assert.AreEqual(inFrames, g.collectionsInFrames, what + " collectionsInFrames");
            Assert.AreEqual(betweenFrames, g.collectionsBetweenFrames, what + " collectionsBetweenFrames");
        }

        /// <summary>Eight frames of collection counts: FrameStart, ScriptsStart, ScriptsEnd, RenderStart, FrameEnd.</summary>
        static readonly int[][] GcTable =
        {
            new[] { 10, 10, 10, 10, 10 }, new[] { 10, 10, 11, 11, 11 }, new[] { 12, 12, 12, 12, 12 }, new[] { 12, 13, 13, 13, 13 },
            new[] { 13, 13, 13, 14, 14 }, new[] { 14, 14, 14, 14, 15 }, new[] { 17, 17, 17, 17, 17 }, new[] { 17, 18, 19, 19, 19 },
        };

        // ------------------------------------------------------------------------------------------------ slots

        [Test]
        public void EachSlotKeepsItsWorstFrameWithNeighbours()
        {
            double[] frame = { 5, 6, 64, 7, 5, 5 }, actors = { 1, 1, 2, 1, 9, 1 }, render = { 1, 12, 1, 1, 1, 1 };
            long[] bytes = { 0, 0, 0, 0, 2000000, 0 };
            for (int i = 0; i < 6; i++) Feed(frame[i], actors[i], render[i], bytes[i]);
            var w = Parse();
            AssertSlot(w, "frame", 2, 1, 3, 64);
            AssertSlot(w, "actors", 4, 3, 5, 9);
            AssertSlot(w, "render", 1, 0, 2, 12);
            Assert.AreEqual(PerfMarks.MaxMs((int)Sec.Frame), Slot(w, "frame").maxMs, 1e-6, "the frame slot's maxMs is the existing cpu.frame.max");
            Assert.AreEqual(PerfMarks.MaxMs((int)Sec.Actors), Slot(w, "actors").maxMs, 1e-6, "the actors slot's maxMs is the existing cpu.actors.max");
            Assert.AreEqual(PerfMarks.MaxMs((int)Sec.Render), Slot(w, "render").maxMs, 1e-6, "the render slot's maxMs is the existing cpu.render.max");
            Assert.AreEqual(0.1, RowT(Slot(w, "frame").at), 1e-6, "frame 2 closed at 2 * 0.05 s");
            Assert.AreEqual(2000000, RowBytes(Slot(w, "actors").at, Sec.Actors), 0.5, "the actors row holds the growth fed in that frame");
            Assert.AreEqual(9, RowMs(Slot(w, "actors").at, Sec.Actors), 1e-6, "the actors row holds the actors ms of that frame");
            Assert.AreEqual(2, RowMs(Slot(w, "frame").at, Sec.Actors), 1e-6, "the worst frame's row holds every section of that frame");
            Assert.AreEqual(64, RowMs(Slot(w, "frame").at, Sec.Frame), 1e-6, "the worst frame's row holds its own frame ms");
            Assert.AreEqual(0, RowBytes(Slot(w, "frame").at, Sec.Actors), 0.5, "no growth was fed in the worst frame");
        }

        [Test]
        public void ALargerFrameReplacesAndATieDoesNot()
        {
            double[] v = { 10, 20, 20, 15, 30 };
            int[] at = { 0, 1, 1, 1, 4 }, before = { -1, 0, 0, 0, 3 }, after = { -1, -1, 2, 2, -1 };
            for (int i = 0; i < v.Length; i++)
            {
                Feed(v[i], 1, 1);
                var s = Slot(Parse(), "frame");
                Assert.AreEqual(at[i], RowI(s.at, "after frame " + i + ": at"), "after frame " + i + ": at");
                AssertNeighbour(s.before, before[i], "after frame " + i + ": before");
                AssertNeighbour(s.after, after[i], "after frame " + i + ": after");
            }
            Assert.AreEqual(30, Slot(Parse(), "frame").maxMs, 1e-6, "maxMs is the largest value");
        }

        [Test]
        public void TheFirstAndTheNewestFrameHaveNoNeighbour()
        {
            Feed(5, 1, 1);
            string one = Json(); var w = Parse(one);
            foreach (var name in new[] { "frame", "actors", "render" }) Assert.AreEqual(0, RowI(Slot(w, name).at, name + ".at after one frame"), name + ".at after one frame");
            Assert.AreEqual(3, Count(one, "\"before\":null"), "one frame: no slot has a before");
            Assert.AreEqual(3, Count(one, "\"after\":null"), "one frame: no slot has an after");

            Feed(6, 2, 2);                                  // strictly worse in all three sections
            string two = Json(); w = Parse(two);
            Assert.AreEqual(0, Count(two, "\"before\":null"), "a strictly worse second frame has the first as its before");
            Assert.AreEqual(3, Count(two, "\"after\":null"), "the newest frame has no after");
            foreach (var name in new[] { "frame", "actors", "render" })
            {
                Assert.AreEqual(1, RowI(Slot(w, name).at, name + ".at"), name + ".at after two frames");
                Assert.AreEqual(0, RowI(Slot(w, name).before, name + ".before"), name + ".before after two frames");
            }

            Feed(1, 0.5, 0.5);                              // smaller in all three
            string three = Json(); w = Parse(three);
            Assert.AreEqual(0, Count(three, "\"after\":null"), "a smaller third frame is the after of every slot");
            foreach (var name in new[] { "frame", "actors", "render" })
            {
                Assert.AreEqual(1, RowI(Slot(w, name).at, name + ".at"), name + ".at stays after a smaller frame");
                Assert.AreEqual(2, RowI(Slot(w, name).after, name + ".after"), name + ".after after three frames");
            }
        }

        [Test]
        public void AFirstFrameOfZeroStillFillsTheSlots()
        {
            Feed(0, 0, 0);
            var w = Parse();
            foreach (var name in new[] { "frame", "actors", "render" })
            {
                Assert.AreEqual(0, RowI(Slot(w, name).at, name + ".at"), name + ".at holds the first frame even at 0 ms");
                Assert.AreEqual(0, Slot(w, name).maxMs, 1e-9, name + ".maxMs");
            }
        }

        [Test]
        public void WithNoFrameEveryRowIsNullAndTheMaximumIsZero()
        {
            string json = Json(); var w = Parse(json);
            Assert.AreEqual(3, Count(json, "\"at\":null"), "no frame: three null at rows");
            Assert.AreEqual(3, Count(json, "\"before\":null"), "no frame: three null before rows");
            Assert.AreEqual(3, Count(json, "\"after\":null"), "no frame: three null after rows");
            foreach (var name in new[] { "frame", "actors", "render" })
            {
                Assert.AreEqual(name, Slot(w, name).section, name + " slot still names its section");
                Assert.AreEqual(0, Slot(w, name).maxMs, 1e-9, name + ".maxMs");
            }
        }

        // ------------------------------------------------------------------------------------------------ collection counters

        [Test]
        public void CollectionCountersCountByPhaseAndBetweenFrames()
        {
            Fresh(10);
            foreach (var row in GcTable) Feed(1, 1, 1, 0, Gc(row));
            var g = Parse().gc;
            AssertGcCounters(g, 6, 5, 2, 2, 1, 1, 3, "eight frames");
        }

        [Test]
        public void AFrameWithoutAStartReadCountsNowhere()
        {
            Fresh(10);
            Feed(1, 1, 1, 0, new PerfMarks.GcReads { FrameStart = -1, ScriptsStart = 10, ScriptsEnd = 12, RenderStart = 12, FrameEnd = 13 });
            var w = Parse();
            AssertGcCounters(w.gc, 0, 0, 0, 0, 0, 0, 0, "a frame without a start read");
            var at = Slot(w, "frame").at;
            Assert.AreEqual(-1, RowGc(at).Length == 5 ? RowGc(at)[0] : -999, "its row keeps the -1 start read");
            Assert.AreEqual(13, RowGc(at).Length == 5 ? RowGc(at)[4] : -999, "its row keeps the end read");
        }

        [Test]
        public void TheFrameEndOfASkippedFrameStillCountsAsThePreviousEnd()
        {
            Fresh(10);
            Feed(1, 1, 1, 0, new PerfMarks.GcReads { FrameStart = -1, ScriptsStart = 10, ScriptsEnd = 12, RenderStart = 12, FrameEnd = 13 });
            Feed(1, 1, 1, 0, Gc(new[] { 15, 15, 15, 15, 15 }));
            AssertGcCounters(Parse().gc, 0, 0, 0, 0, 0, 0, 2, "the frame after a skipped one");
        }

        [Test]
        public void ARowCarriesItsFiveCounts()
        {
            Fresh(10);
            for (int i = 0; i < GcTable.Length; i++) Feed(i == 5 ? 9 : 1, 1, 1, 0, Gc(GcTable[i]));
            var s = Slot(Parse(), "frame");
            Assert.AreEqual(5, RowI(s.at, "frame.at"), "the sixth frame is the worst");
            CollectionAssert.AreEqual(new[] { 14, 14, 14, 14, 15 }, RowGc(s.at), "the worst frame's five counts");
            CollectionAssert.AreEqual(new[] { 13, 13, 13, 14, 14 }, RowGc(s.before), "the frame before's five counts");
        }

        [Test]
        public void TheBaselineOfAResetDecidesTheFirstGap()
        {
            Fresh(10);
            Feed(1, 1, 1, 0, Gc(new[] { 12, 12, 12, 12, 12 }));
            AssertGcCounters(Parse().gc, 0, 0, 0, 0, 0, 0, 2, "Reset(10), first frame at 12");

            Fresh();
            Feed(1, 1, 1, 0, Gc(new[] { 12, 12, 12, 12, 12 }));
            AssertGcCounters(Parse().gc, 0, 0, 0, 0, 0, 0, 0, "Reset(), first frame at 12");

            Fresh(5);
            Feed(1, 1, 1, 0, Gc(new[] { 3, 3, 3, 3, 3 }));
            AssertGcCounters(Parse().gc, 0, 0, 0, 0, 0, 0, 0, "Reset(5), a count that went down to 3");
        }

        // ------------------------------------------------------------------------------------------------ context

        [Test]
        public void TheRowsCarryTheFrameContext()
        {
            var c0 = Ctx(30, 1, 0, 1, 0, 2, 3, 0, 1, 100);
            var c1 = Ctx(31, 2, 1, 0, 1, 5, 4, 1, 0, 200);
            var c2 = Ctx(32, 3, 1, 1, 0, 7, 6, 0, 1, 300);
            Feed(5, 1, 1, 0, null, c0);
            Feed(9, 1, 1, 0, null, c1);
            Feed(5, 1, 1, 0, null, c2);
            var s = Slot(Parse(), "frame");
            Assert.AreEqual(1, RowI(s.at, "frame.at"), "the middle frame is the worst");
            AssertCtx(c1, s.at, "at");
            AssertCtx(c0, s.before, "before");
            AssertCtx(c2, s.after, "after");
        }

        [Test]
        public void TheWindowCountsTheContext()
        {
            int[] walls = { 0, 2, 3, 1 }, wallsChanged = { 0, 1, 1, 1 }, walkMap = { 1, 0, 1, 0 }, terrain = { 0, 0, 1, 0 }, state = { 1, 0, 2, 1 },
                snaps = { 2, 3, 2, 3 }, dead = { 0, 0, 1, 1 }, order = { 1, 1, 0, 0 }, tiles = { 0, 0, 65536, 65536 };
            for (int i = 0; i < 4; i++) Feed(1, 1, 1, 0, null, Ctx(10 + i, walls[i], wallsChanged[i], walkMap[i], terrain[i], state[i], snaps[i], dead[i], order[i], tiles[i]));
            var c = Parse().context ?? new ContextDto();
            Assert.AreEqual(0, c.wallsMin, "wallsMin");
            Assert.AreEqual(3, c.wallsMax, "wallsMax");
            Assert.AreEqual(3, c.framesWallsChanged, "framesWallsChanged");
            Assert.AreEqual(2, c.framesWalkMapChanged, "framesWalkMapChanged");
            Assert.AreEqual(1, c.framesTerrainMapChanged, "framesTerrainMapChanged");
            Assert.AreEqual(4, c.stateFrames, "stateFrames");
            Assert.AreEqual(10, c.snapshots, "snapshots");
            Assert.AreEqual(65536, c.mapTiles, "mapTiles is the last frame's");
            Assert.AreEqual(2, c.framesDead, "framesDead");
            Assert.AreEqual(2, c.framesOrderPending, "framesOrderPending");
        }

        // ------------------------------------------------------------------------------------------------ reset, off, old statistics

        [Test]
        public void ResetClearsEverything()
        {
            Fresh(10);
            Feed(5, 4, 1, 4096, Gc(new[] { 10, 10, 11, 11, 11 }), Ctx(30, 2, 1, 1, 1, 2, 3, 1, 1, 100));
            Feed(9, 4, 1, 4096, Gc(new[] { 12, 13, 13, 13, 13 }), Ctx(31, 3, 1, 1, 1, 2, 3, 1, 1, 100));
            Feed(5, 4, 1, 4096, Gc(new[] { 13, 13, 13, 13, 14 }), Ctx(32, 1, 1, 1, 1, 2, 3, 1, 1, 100));
            Assert.AreEqual(3, PerfMarks.Frames, "three frames before the reset");
            Assert.Greater((Parse().gc ?? new GcDto()).collectionsInFrames, 0, "collections were counted before the reset");
            Assert.Greater((Parse().context ?? new ContextDto()).framesDead, 0, "context was counted before the reset");
            Assert.Greater(PerfMarks.AllocPerFrame((int)Sec.Actors), 0, "growth was counted before the reset");

            PerfMarks.Reset();
            string json = Json(); var w = Parse(json);
            Assert.AreEqual(3, Count(json, "\"at\":null"), "after Reset(): three null at rows");
            Assert.AreEqual(3, Count(json, "\"before\":null"), "after Reset(): three null before rows");
            Assert.AreEqual(3, Count(json, "\"after\":null"), "after Reset(): three null after rows");
            AssertGcCounters(w.gc, 0, 0, 0, 0, 0, 0, 0, "after Reset()");
            var c = w.context ?? new ContextDto();
            Assert.AreEqual(0, c.wallsMin + c.wallsMax + c.stateFrames + c.snapshots + c.mapTiles, "context sums after Reset()");
            Assert.AreEqual(0, c.framesWallsChanged + c.framesWalkMapChanged + c.framesTerrainMapChanged + c.framesDead + c.framesOrderPending, "context frame counts after Reset()");
            Assert.AreEqual(0, PerfMarks.Frames, "Frames after Reset()");
            Assert.AreEqual(0, PerfMarks.MaxMs((int)Sec.Actors), 1e-9, "MaxMs after Reset()");
            Assert.AreEqual(0, PerfMarks.AllocPerFrame((int)Sec.Actors), 1e-9, "AllocPerFrame after Reset()");

            _n = 0;
            Feed(5, 4, 1);
            json = Json(); w = Parse(json);
            Assert.AreEqual(0, RowI(Slot(w, "frame").at, "frame.at"), "the first frame after a reset has index 0");
            Assert.AreEqual(3, Count(json, "\"before\":null"), "the first frame after a reset has no before");
        }

        [Test]
        public void NothingIsRecordedWhileOff()
        {
            PerfMarks.On = false;
            PerfMarks.Add(Sec.Actors, 5, 100);
            PerfMarks.EndFrame(Gc(new[] { 1, 1, 2, 2, 3 }), Ctx(9, 9, 1, 1, 1, 9, 9, 1, 1, 9), 0.5);
            PerfMarks.On = true;
            _n = 0;
            Feed(8, 7, 3);
            var s = Slot(Parse(), "actors");
            Assert.AreEqual(0, RowI(s.at, "actors.at"), "the frame fed while on is the first frame");
            Assert.AreEqual(7, s.maxMs, 1e-6, "the slot holds only what was fed while on");
            Assert.AreEqual(1, PerfMarks.Frames, "one frame");
            Assert.AreEqual(7, PerfMarks.MeanMs((int)Sec.Actors), 1e-6, "the ms added while off did not count");
            Assert.AreEqual(1, PerfMarks.Calls((int)Sec.Actors), "one call");
            Assert.AreEqual(0, PerfMarks.AllocPerFrame((int)Sec.Actors), 1e-9, "the growth added while off did not count");
            AssertGcCounters(Parse().gc, 0, 0, 0, 0, 0, 0, 0, "counters after an off frame and a frame without a start read");
        }

        [Test]
        public void ExistingStatisticsKeepTheirMeaning()
        {
            PerfMarks.Add(Sec.Actors, 4, 100);
            PerfMarks.Add(Sec.Actors, 4, 100);
            PerfMarks.EndFrame(NoGc(), default(PerfMarks.FrameContext), 0.05);
            Assert.AreEqual(2, PerfMarks.Calls((int)Sec.Actors), "Calls after frame 1");
            Assert.AreEqual(8, PerfMarks.MeanMs((int)Sec.Actors), 1e-6, "MeanMs after frame 1");
            Assert.AreEqual(8, PerfMarks.MaxMs((int)Sec.Actors), 1e-6, "MaxMs after frame 1");
            Assert.AreEqual(200, PerfMarks.AllocPerFrame((int)Sec.Actors), 1e-6, "AllocPerFrame after frame 1");

            PerfMarks.Add(Sec.Actors, 4, -50);            // a shrinking heap adds no growth
            PerfMarks.EndFrame(NoGc(), default(PerfMarks.FrameContext), 0.10);
            Assert.AreEqual(3, PerfMarks.Calls((int)Sec.Actors), "Calls after frame 2");
            Assert.AreEqual(2, PerfMarks.Frames, "Frames after frame 2");
            Assert.AreEqual(6, PerfMarks.MeanMs((int)Sec.Actors), 1e-6, "MeanMs after frame 2: (8 + 4) / 2 frames");
            Assert.AreEqual(8, PerfMarks.MaxMs((int)Sec.Actors), 1e-6, "MaxMs after frame 2");
            Assert.AreEqual(100, PerfMarks.AllocPerFrame((int)Sec.Actors), 1e-6, "AllocPerFrame after frame 2: 200 / 2 frames");
        }

        // ------------------------------------------------------------------------------------------------ JSON

        [Test]
        public void TheJsonHoldsTheNamedFields()
        {
            Feed(12.5, 4.25, 3, 0, Gc(new[] { 10, 10, 10, 10, 10 }), Ctx(31, 2, 0, 0, 0, 1, 2, 0, 1, 100));
            string json = Json(); var w = Parse(json);
            CollectionAssert.AreEqual(PerfMarks.Names, w.sections ?? new string[0], "sections are PerfMarks.Names");
            Assert.AreEqual(N, PerfMarks.Names.Length, "26 sections");
            foreach (var name in new[] { "frame", "actors", "render" })
            {
                var s = Slot(w, name);
                Assert.AreEqual(name, s.section, name + " slot section");
                Assert.IsTrue(Present(s.at), name + ".at is present");
                Assert.AreEqual(N, s.at != null && s.at.bytes != null ? s.at.bytes.Length : -1, name + ".at.bytes length");
                Assert.AreEqual(5, s.at != null && s.at.gc != null ? s.at.gc.Length : -1, name + ".at.gc length");
            }
            string fragment = AppendedText();
            Assert.IsTrue(fragment.StartsWith("\"worstFrames\":"), "the fragment starts with the member name: " + fragment);
            Assert.IsTrue(fragment.Contains("\"gc\":{"), "the fragment holds the gc object");
            Assert.IsTrue(fragment.Contains("\"context\":{"), "the fragment holds the context object");
            Assert.IsFalse(fragment.StartsWith(",") || fragment.EndsWith(","), "no leading or trailing comma");
            Assert.AreEqual(12.5, RowMs(Slot(w, "frame").at, Sec.Frame), 1e-6, "ms by value");
        }

        [Test]
        public void TheJsonIsTheSameUnderAGermanCulture()
        {
            Feed(12.5, 4.25, 3, 0, Gc(new[] { 10, 10, 10, 10, 10 }), Ctx(31, 2, 0, 0, 0, 1, 2, 0, 1, 100));
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                string json = Json(); var w = Parse(json);
                Assert.AreEqual(12.5, RowMs(Slot(w, "frame").at, Sec.Frame), 1e-6, "the frame ms parses back under de-DE");
                Assert.IsTrue(json.Contains("12.5"), "a decimal point, not a comma: " + json);
                Assert.IsFalse(json.Contains("12,5"), "no decimal comma");
            }
            finally { CultureInfo.CurrentCulture = _savedCulture; }
        }

        static string AppendedText()
        {
            var sb = new StringBuilder();
            PerfMarks.AppendWorstFrames(sb);
            return sb.ToString();
        }

        // ------------------------------------------------------------------------------------------------ no garbage

        static readonly Action OneFrame = () =>
        {
            s_n++;
            double v = (s_n & 1) == 1 ? s_n : 0.5;   // odd: a rising value replaces every slot; even: the waiting slots take the after row
            PerfMarks.Add(Sec.Frame, v, 0);
            PerfMarks.Add(Sec.Actors, v, 4096);
            PerfMarks.Add(Sec.Render, v, 0);
            PerfMarks.EndFrame(s_gc, s_ctx, s_seconds);
        };

        static readonly Action FourMarks = () =>
        {
            var a = PerfMarks.Begin(); PerfMarks.End(Sec.Frame, a);
            var b = PerfMarks.Begin(); PerfMarks.End(Sec.Actors, b);
            var c = PerfMarks.Begin(); PerfMarks.End(Sec.Render, c);
            var d = PerfMarks.Begin(); PerfMarks.End(Sec.NetPoll, d);
            PerfMarks.EndFrame(s_gc, s_ctx, s_seconds);
        };

        [Test]
        public void ASteadyFrameAllocatesNothing()
        {
            s_gc = new PerfMarks.GcReads { FrameStart = 0, ScriptsStart = 0, ScriptsEnd = 0, RenderStart = 0, FrameEnd = 0 };
            s_ctx = Ctx(31, 2, 1, 1, 1, 1, 2, 0, 1, 100);
            s_seconds = 1.5; s_n = 0;
            OneFrame();
            Assert.AreEqual(1, PerfMarks.Frames, "one call of the frame delegate recorded one frame");
            AllocationMeter.AssertNone(OneFrame, 200, "slots replaced and filled");
            AllocationMeter.AssertNone(FourMarks, 200, "End");
        }
    }
}
