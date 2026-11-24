using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-MAP-PAINT-1, the cover half: every consumer of "a contact stands within CampDropPx (8) layer units of this camp or ward" reads
    /// one flag per row, computed once per Present (and again only when the layer rect or the map's size changed), instead of looping the
    /// actors at six call sites. The marks are built as <see cref="DeclutterEditModeTests"/> builds them, on a 16 x 16 tile map with the
    /// layer rect set (a bare object's anchors are at its centre, so the rect is the size delta): at rect 512 a camp's world x is its
    /// layer x. The packet (docs/backlog/packets/P-UI-MAP-PAINT-1.md) has the rules and the numbers.
    /// </summary>
    public sealed class MapCoverEditModeTests
    {
        const int Tiles = 16;
        GameObject _root;
        Session _session;
        FakeTransport _transport;
        TacticalMapMarks _marks;
        uint _tick;
        uint _nextActor;

        [SetUp]
        public void SetUp()
        {
            _tick = 10; _nextActor = 100;   // NUnit shares one fixture instance between tests
            _root = new GameObject("Map cover fixture", typeof(RectTransform));
            _transport = new FakeTransport();
            _session = new Session(_transport, new SessionOptions { Url = "ws://localhost/ws", Profile = "fixture", Now = () => 1000 });
            _session.Connect(); _transport.Open();
            _transport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16],
                PlayerId = 1, TickRate = 20, Map = new MapBlob(Tiles, Tiles, new byte[Tiles * Tiles]) });
        }

        [TearDown]
        public void TearDown()
        {
            if (_session != null) _session.Leave();
            UnityEngine.Object.DestroyImmediate(_root);
        }

        // ---- fixtures ----------------------------------------------------------------------------------------------------------------

        static WireCamp Camp(byte state = 0, bool reward = true, bool ward = false) => new WireCamp
        {
            MobType = 0, Elite = 0, Alive = state == 2 ? 0u : 3u, State = state,
            ClearedAt = state == 2 ? 1000 : 0, RespawnAt = state == 2 ? 181000 : 0,
            Reward = reward ? new WireCampReward { Stat = 2, Amount = 100 } : (WireCampReward?)null,
            Ward = ward ? new WireWard { Yours = true, ExpiresAt = 1e12 } : null,
        };

        /// <summary>The marks on a 16 x 16 map with the given camps, the layer rect <paramref name="rect"/> across, and the camps' rows delivered.</summary>
        TacticalMapMarks Build(bool large, float rect, MapCamp[] camps, params WireCamp[] rows)
        {
            var map = new MapBlob(Tiles, Tiles, new byte[Tiles * Tiles]).WithPublic(camps, null, null, null, null);
            _marks = _root.AddComponent<TacticalMapMarks>();
            _marks.Large = large;
            _marks.rectTransform.sizeDelta = new Vector2(rect, rect);
            if (large) _marks.transform.localScale = new Vector3(1.3f, 1.3f, 1f);
            _marks.SetMap(map);
            _transport.Deliver(new ServerMsg.State { Tick = ++_tick, Items = new StateItem[] { new CampsState { Rows = rows } } });
            return _marks;
        }

        static WireActor Actor(uint id, float x, float y) => new WireActor { Id = id, X = x, Y = y, Faction = 2, HpPct = 100, Status = 1 };

        /// <summary>One step of a match: a snapshot of these actors (the own seat is id 1), then Present and a canvas pass (the mesh).</summary>
        void Step(params WireActor[] actors)
        {
            _transport.Deliver(new ServerMsg.Snapshot
            {
                Tick = ++_tick, Phase = 1, Actors = actors,
                Me = new WireSelf { Id = 1, X = 48, Y = 48, Hp = 100, MaxHp = 100, Status = 1 }
            });
            _marks.Present(_session, null);
            Canvas.ForceUpdateCanvases();
            // Premise: Sample returns exactly the chosen actors, where they were put.
            var sampled = SampledActors();
            Assert.That(sampled.Count, Is.EqualTo(actors.Length), "premise: the sample holds the delivered actors");
            for (int i = 0; i < actors.Length && i < sampled.Count; i++)
            {
                Assert.That(sampled[i].Id, Is.EqualTo(actors[i].Id), "premise: sample order is delivery order, actor " + i);
                Assert.That(sampled[i].X, Is.EqualTo(actors[i].X).Within(.05f), "premise: actor " + i + " x");
                Assert.That(sampled[i].Y, Is.EqualTo(actors[i].Y).Within(.05f), "premise: actor " + i + " y");
            }
        }

        List<InterpActor> SampledActors()
        {
            var field = typeof(TacticalMapMarks).GetField("_actors", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "the marks keep their sampled actors in _actors");
            return (List<InterpActor>)field.GetValue(_marks);
        }

        /// <summary>The rule, from scratch: the first non-self sampled actor whose layer point is under CampDropPx from the world point.</summary>
        bool Brute(float worldX, float worldY)
        {
            var r = _marks.rectTransform.rect;
            Vector2 At(float x, float y) => new Vector2(r.xMin + x / (Tiles * 32f) * r.width, r.yMax - y / (Tiles * 32f) * r.height);
            var p = At(worldX, worldY);
            foreach (var a in SampledActors())
                if (a.Id != 1 && (At(a.X, a.Y) - p).sqrMagnitude < TacticalMapMarks.CampDropPx * TacticalMapMarks.CampDropPx) return true;
            return false;
        }

        int Active(string prefix)
        {
            int n = 0;
            foreach (Transform child in _marks.transform)
                if (child.name.StartsWith(prefix, StringComparison.Ordinal) && child.gameObject.activeSelf) n++;
            return n;
        }

        // ---- the tests ---------------------------------------------------------------------------------------------------------------

        /// <summary>Guard (green on the stub): the flag and every consumer of it agree, on both maps, either side of the 8 unit line.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void TheFlagDecidesEveryConsumer(bool large)
        {
            var camps = new[] { new MapCamp(200, 256, 8, 1), new MapCamp(214, 256, 8, 1) };
            // Camp 0 is cleared (clock, reward); camp 1 idle with a reward and a ward on it (14 units right of camp 0).
            Build(large, 512f, camps, Camp(2), Camp(0, true, true));
            bool sawCovered0 = false, sawOpen0 = false, sawCovered1 = false, sawOpen1 = false;
            foreach (float d in new[] { 2f, 5.5f, 6.5f, 7.5f, 8.5f, 9.5f, 21.5f, 22.5f })
            {
                Step(Actor(_nextActor++, 200f + d, 256f));
                bool c0 = d < 8f, c1 = Mathf.Abs(14f - d) < 8f;
                string at = " (actor " + d + " right of camp 0)";
                Assert.That(_marks.CampMarkerCount, Is.EqualTo(2), "premise: two camp rows" + at);
                Assert.That(_marks.WardMarkCount, Is.EqualTo(1), "premise: one ward row" + at);
                Assert.That(_marks.CampCovered(0), Is.EqualTo(c0), "camp 0 flag" + at);
                Assert.That(_marks.CampCovered(1), Is.EqualTo(c1), "camp 1 flag" + at);
                Assert.That(_marks.WardCovered(0), Is.EqualTo(c1), "ward 0 flag (the ward stands on camp 1)" + at);
                int uncovered = (c0 ? 0 : 1) + (c1 ? 0 : 1);
                var clock = _marks.transform.Find("CampClock0");
                Assert.That(_marks.CampClocksShown, Is.EqualTo(c0 ? 0 : 1), "clocks shown" + at);
                Assert.That(clock != null && clock.gameObject.activeSelf, Is.EqualTo(!c0), "CampClock0 active" + at);
                Assert.That(Active("CampBadgeIcon"), Is.EqualTo(uncovered), "active badge icons" + at);
                Assert.That(_marks.CampBuffGlyphsDrawn, Is.EqualTo(uncovered), "buff glyphs" + at);
                Assert.That(_marks.CampMarkersDrawn, Is.EqualTo(uncovered), "camp markers drawn" + at);
                Assert.That(_marks.BadgesDrawn, Is.EqualTo(uncovered), "badges drawn" + at);
                Assert.That(_marks.WardsDrawn, Is.EqualTo(c1 ? 0 : 1), "wards drawn" + at);
                if (large)
                {
                    Assert.That(_marks.CampTextRect(0).HasValue && _marks.CampTextRect(1).HasValue, Is.True, "premise: both camps have a text block on the Tab map" + at);
                    Assert.That(Active("CampBadgeText"), Is.EqualTo(uncovered + (c1 ? 0 : 1)), "amount per uncovered camp plus the ward count" + at);
                }
                else Assert.That(Active("CampBadgeText"), Is.EqualTo(0), "the minimap shows no camp or ward text" + at);
                if (c0) sawCovered0 = true; else sawOpen0 = true;
                if (c1) sawCovered1 = true; else sawOpen1 = true;
            }
            Assert.That(sawCovered0 && sawOpen0 && sawCovered1 && sawOpen1, Is.True, "premise: both values of both flags occur");
        }

        /// <summary>Guard: the rule is in layer units. A rect of 256 halves the world distances; a rect of 512 leaves them.</summary>
        [Test]
        public void TheRuleIsInLayerUnits()
        {
            Build(false, 256f, new[] { new MapCamp(256, 256, 8, 1) }, Camp());
            Step(Actor(_nextActor++, 270f, 256f));
            Assert.That(_marks.CampCovered(0), Is.True, "14 world units are 7 layer units on a 256 rect: covered");
            Step(Actor(_nextActor++, 273f, 256f));
            Assert.That(_marks.CampCovered(0), Is.False, "17 world units are 8.5 layer units: not covered");
            _marks.rectTransform.sizeDelta = new Vector2(512f, 512f);
            Step(Actor(_nextActor++, 270f, 256f));
            Assert.That(_marks.CampCovered(0), Is.False, "14 world units are 14 layer units on a 512 rect: not covered");
        }

        /// <summary>Twelve idle camps (a 6 x 2 grid), wards on camps 0 to 4, twenty other actors in a row far from every mark, the own actor on camp 0.</summary>
        void BuildBusy(float rect, bool withNearActor)
        {
            var camps = new MapCamp[12];
            var rows = new WireCamp[12];
            for (int c = 0; c < 6; c++)
                for (int r = 0; r < 2; r++)
                {
                    int i = r * 6 + c;
                    camps[i] = new MapCamp((ushort)(64 + 80 * c), (ushort)(64 + 80 * r), 8, 1);
                    rows[i] = Camp(0, true, i < 5);
                }
            Build(false, rect, camps, rows);
            var actors = new List<WireActor> { Actor(1, 64f, 64f) };   // the own actor: a self test would cover camp 0
            for (int k = 0; k < 20; k++) actors.Add(Actor(10 + (uint)k, 20f + 24f * k, 400f));
            if (withNearActor) actors.Add(Actor(90, 73f, 64f));      // 9 world units right of camp 0: covers at rect 400 only
            _busy = actors.ToArray();
            Step(_busy);
            Assert.That(_marks.CampMarkerCount, Is.EqualTo(12), "premise: twelve camp rows");
            Assert.That(_marks.WardMarkCount, Is.EqualTo(5), "premise: five ward rows");
        }
        WireActor[] _busy;

        int RowTests() => _marks.CampMarkerCount + _marks.WardMarkCount;

        /// <summary>Red on the stub (46 tests per actor): one cover test per row and non-self actor, nothing at the six call sites or in the mesh pass.</summary>
        [Test]
        public void OneCoverTestPerCampAndActorPerPaint()
        {
            BuildBusy(512f, false);
            int before = _marks.CoverTests;
            _marks.Present(_session, null);
            Canvas.ForceUpdateCanvases();
            Assert.That(_marks.CoverTests - before, Is.EqualTo(RowTests() * 20), "one test per camp row and ward row for each of the twenty other actors");
            Assert.That(_marks.CampCovered(0), Is.False, "premise: nothing is covered (the own actor does not count)");
        }

        /// <summary>Red on the stub: the own marker moving redraws the mesh, and a redraw with the same rect and map makes no cover test.</summary>
        [Test]
        public void AMeshRebuildMakesNoCoverTest()
        {
            BuildBusy(512f, false);
            int before = _marks.CoverTests;
            _marks.FollowSelf(70f, 70f);
            Canvas.ForceUpdateCanvases();
            Assert.That(_marks.CoverTests - before, Is.EqualTo(0), "a mesh rebuild with an unchanged rect and map reuses the flags");
        }

        /// <summary>Guard: a new layer size recomputes the flags once, and they are right for the new size.</summary>
        [Test]
        public void ARectChangeRecomputesOnce()
        {
            BuildBusy(512f, true);
            Assert.That(_marks.CampCovered(0), Is.False, "premise: at 512 the near actor is 9 layer units away");
            int before = _marks.CoverTests;
            _marks.rectTransform.sizeDelta = new Vector2(400f, 400f);
            Canvas.ForceUpdateCanvases();
            Assert.That(_marks.CoverTests - before, Is.EqualTo(RowTests() * 21), "one recompute: each row against the twenty-one other actors (the near one is last)");
            Assert.That(_marks.CampCovered(0), Is.True, "at 400 the near actor is 7.03 units away: camp 0 is covered");
            Assert.That(_marks.WardCovered(0), Is.True, "and so is the ward on it");
            for (int i = 0; i < 12; i++)
            {
                var camp = _marks.CampCovered(i);
                Assert.That(camp, Is.EqualTo(Brute(64 + 80 * (i % 6), 64 + 80 * (i / 6))), "camp " + i + " equals the brute force on the new rect");
            }
            for (int j = 0; j < 5; j++)
                Assert.That(_marks.WardCovered(j), Is.EqualTo(Brute(64 + 80 * j, 64)), "ward " + j + " equals the brute force on the new rect");
        }

        [Test]
        public void TheFlagsLeaveWithTheMatch()
        {
            Build(false, 512f, new[] { new MapCamp(200, 256, 8, 1), new MapCamp(214, 256, 8, 1) }, Camp(2), Camp(0, true, true));
            Step(Actor(_nextActor++, 207.5f, 256f));
            Assert.That(_marks.CampCovered(0), Is.True, "premise: a flag is true");
            Assert.That(_marks.WardCovered(0), Is.True, "premise: the ward's flag is true");
            _marks.SetMap(null);
            Assert.That(_marks.CampCovered(0), Is.False, "no map: no camp flag");
            Assert.That(_marks.WardCovered(0), Is.False, "no map: no ward flag");
            Assert.That(_marks.CampCovered(-1) || _marks.CampCovered(99) || _marks.WardCovered(-1) || _marks.WardCovered(99), Is.False, "out of range reads false");
        }

        [Test]
        public void ANullMapOrNoCampMakesNoCoverTest()
        {
            _marks = _root.AddComponent<TacticalMapMarks>();
            _marks.rectTransform.sizeDelta = new Vector2(512f, 512f);
            _marks.SetMap(null);
            int before = _marks.CoverTests;
            Step();   // premise-free: a snapshot without actors
            _transport.Deliver(new ServerMsg.Snapshot { Tick = ++_tick, Phase = 1, Actors = new[] { Actor(_nextActor++, 100f, 100f), Actor(_nextActor++, 120f, 100f) },
                Me = new WireSelf { Id = 1, X = 48, Y = 48, Hp = 100, MaxHp = 100, Status = 1 } });
            Assert.DoesNotThrow(() => _marks.Present(_session, null), "a present with no map");
            Canvas.ForceUpdateCanvases();
            Assert.That(_marks.CoverTests - before, Is.EqualTo(0), "no map: no actor is converted");
            _marks.SetMap(new MapBlob(Tiles, Tiles, new byte[Tiles * Tiles]));
            _transport.Deliver(new ServerMsg.Snapshot { Tick = ++_tick, Phase = 1, Actors = new[] { Actor(_nextActor++, 100f, 100f), Actor(_nextActor++, 120f, 100f) },
                Me = new WireSelf { Id = 1, X = 48, Y = 48, Hp = 100, MaxHp = 100, Status = 1 } });
            Assert.DoesNotThrow(() => _marks.Present(_session, null), "a present with a map and no camp row");
            Canvas.ForceUpdateCanvases();
            Assert.That(_marks.CoverTests - before, Is.EqualTo(0), "no camp and no ward: no actor is converted");
        }

        [Test]
        public void ComputeCoverAllocatesNothingInASteadyPresent()
        {
            BuildBusy(512f, false);
            _marks.Present(_session, null);
            var method = typeof(TacticalMapMarks).GetMethod("ComputeCover", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "the marks have a ComputeCover method");
            Assert.That(method.GetParameters().Length, Is.EqualTo(0), "ComputeCover takes no parameters");
            var compute = (Action)Delegate.CreateDelegate(typeof(Action), _marks, method);
            for (int i = 0; i < 5; i++) compute();   // warm up
            AllocationMeter.AssertNone(compute, 200, "ComputeCover in a steady Present");
        }

        sealed class FakeTransport : ITransport
        {
            public event Action Opened;
            public event Action<byte[]> Received;
            public event Action<string> Closed;
            public void Open() => Opened?.Invoke();
            public void Deliver(ServerMsg msg) => Received?.Invoke(msg.Encode());
            public void Connect(string url) { }
            public void Send(byte[] frame) { }
            public void Close() { Closed?.Invoke("closed"); }
            public void Poll() { }
        }
    }
}
