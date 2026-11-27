using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using static Roguehalla.Client.Tests.WorldNamesRig;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-WORLD-LABELS-1: a quest's name stands on one point only, and a text label of the world (a giver's or trader's name, "Turn in here", a
    /// prop's label) fades out while it lies on the local fighter's own overhead cluster (name, bar, badges) and returns when it has left it. The
    /// arithmetic is in <c>LabelClearanceTests</c> and <c>QuestNameOnceTests</c> (dotnet); here are the presenters: <see cref="WorldMarks"/>,
    /// <see cref="WorldIndicators"/> and <see cref="SpriteActorPresenter.TryLocalClusterRect"/>. Time is a parameter (the <c>dt</c> of
    /// <see cref="WorldMarks.Update(CollectionsView, Func{float, float, bool}, float)"/>, the <c>now</c> of <see cref="WorldIndicators.Update"/>).
    /// The cluster is a <see cref="Rect"/> the test holds, or the real presenter's. A gap is the distance of a label's plate from the cluster along x
    /// (the plate level with the cluster), negative when they overlap; one step of .05 s moves an alpha .3333. Every assertion is
    /// <c>Assert.That(actual, constraint, message)</c>. The packet (docs/backlog/packets/P-UI-WORLD-LABELS-1.md) has the rules.
    /// </summary>
    public sealed class WorldLabelsEditModeTests
    {
        const string MarkLabel = "World marker label", MarkPlate = "World marker label plate", PropLabel = "World label", PropPlate = "World label plate";
        const float Step = .05f, Tol = .002f;

        UiTheme _savedTheme; bool? _savedReduced; bool _savedHidden; Func<float> _savedClock; bool _savedContinuity;
        GameObject _root, _propsRoot; WorldMarks _marks; WorldIndicators _props; Stage _stage;
        readonly List<GameObject> _extra = new List<GameObject>();
        Rect _cluster; bool _answer = true; uint _tick; float _now = 1f;

        SpriteActorPresenter P { get { return _stage.P; } }
        bool Read(out Rect r) { r = _cluster; return _answer; }

        [SetUp]
        public void SetUp()
        {
            _savedTheme = UiTheme.Current; _savedReduced = UiMotion.ReducedOverride; _savedHidden = WorldLabelVisibility.Hidden;
            _savedClock = SpriteActorPresenter.FadeClock; _savedContinuity = SpriteActorPresenter.ContinuityEnabled;
            UiTheme.Current = UiTheme.Skin();
            ShowcaseSprites.Initialize();
            WorldLabelVisibility.Hidden = false; UiMotion.ReducedOverride = false; SpriteActorPresenter.FadeClock = null;
            _root = new GameObject("world labels marks"); _marks = new WorldMarks(_root.transform);
            _propsRoot = new GameObject("world labels props"); _props = new WorldIndicators(_propsRoot.transform);
            _stage = new Stage("world labels arena");
            _cluster = default; _answer = true; _tick = 0; _now = 1f;
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
                if (_propsRoot != null) UnityEngine.Object.DestroyImmediate(_propsRoot);
                foreach (var g in _extra) if (g != null) UnityEngine.Object.DestroyImmediate(g);
                _extra.Clear();
                if (_stage != null) _stage.Dispose();
            }
            finally
            {
                UiMotion.ReducedOverride = _savedReduced; WorldLabelVisibility.Hidden = _savedHidden;
                SpriteActorPresenter.FadeClock = _savedClock; SpriteActorPresenter.ContinuityEnabled = _savedContinuity;
                UiTheme.Current = _savedTheme;
            }
        }

        // ---- fixtures: delivered quests, traders and props ----

        CollectionsView Deliver(CollectionsView view, params StateItem[] items)
        {
            var state = new ServerMsg.State { Tick = ++_tick, Items = items };
            typeof(CollectionsView).GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { state });
            return view;
        }

        static WireQuestRow Row(uint handle, string name, string giver, double gx, double gy, string def = "ember_walk", bool accepted = true, bool ready = false, uint carrier = 0)
        {
            return new WireQuestRow
            {
                Handle = new QuestHandle(handle), Def = def, State = QuestRowState.Open, OwnerAccepted = accepted, OwnerReady = ready,
                Name = name, GiverLabel = giver, GiverX = gx, GiverY = gy, Carrier = carrier == 0 ? (RosterId?)null : new RosterId(carrier),
            };
        }

        static WireQuestObjective Pt(uint handle, double x, double y) { return new WireQuestObjective { Quest = new QuestHandle(handle), X = x, Y = y }; }

        static QuestsState Quests(WireQuestRow[] rows, WireQuestObjective[] objectives, WireQuestDrop[] drops = null)
        {
            return new QuestsState { Available = true, View = new WireQuestView { Rows = rows, Objectives = objectives, Drops = drops ?? Array.Empty<WireQuestDrop>() } };
        }

        static TradeState Trade(params WireMerchant[] merchants) { return new TradeState { Available = true, Value = new WireTradeView { Merchants = merchants } }; }

        static WireMerchant Merchant(string name, TradeKind kind, double x, double y) { return new WireMerchant { Kind = kind, X = x, Y = y, Name = name }; }

        /// <summary>The standard quest: "Ember Walk" (handle 71), accepted, its giver "Scorch quest giver" at (64, 80), its points at x = 200, 320, 440 (y 220).</summary>
        CollectionsView Ember(params double[] xs)
        {
            if (xs.Length == 0) xs = new double[] { 200, 320, 440 };
            var points = new WireQuestObjective[xs.Length];
            for (int i = 0; i < xs.Length; i++) points[i] = Pt(71, xs[i], 220);
            return Deliver(new CollectionsView(), Quests(new[] { Row(71, "Ember Walk", "Scorch quest giver", 64, 80) }, points));
        }

        static CollectionsView Props(params PropView[] props)
        {
            var c = new CollectionsView();
            typeof(CollectionsView).GetProperty("Props", BindingFlags.Instance | BindingFlags.Public).SetValue(c, props);
            typeof(CollectionsView).GetProperty("RewardChests", BindingFlags.Instance | BindingFlags.Public).SetValue(c, new RewardChestView[0]);
            return c;
        }

        static PropView Body(string label, float x, float y) { return new PropView { Kind = PropAppearance.Body, BodyLabel = label, X = x, Y = y }; }

        static RewardChestView RewardChest(float x, float y, double expiresAt)
        {
            var r = new RewardChestView();
            typeof(RewardChestView).GetProperty("X").SetValue(r, x); typeof(RewardChestView).GetProperty("Y").SetValue(r, y);
            typeof(RewardChestView).GetProperty("ExpiresAt").SetValue(r, expiresAt);
            typeof(RewardChestView).GetProperty("Handle").SetValue(r, new RewardChestHandle(7));
            return r;
        }

        // ---- readers ----

        static List<TextMeshPro> Labels(Transform root, string objectName, string text = null, bool inHierarchy = true)
        {
            var list = new List<TextMeshPro>();
            foreach (var t in root.GetComponentsInChildren<TextMeshPro>(true))
            {
                if (t.name != objectName) continue;
                if (!(inHierarchy ? t.gameObject.activeInHierarchy : t.gameObject.activeSelf)) continue;
                if (text != null && t.text != text) continue;
                list.Add(t);
            }
            return list;
        }

        /// <summary>The one active label of that text (the test fails when there is not exactly one); null when there is none.</summary>
        static TextMeshPro One(Transform root, string objectName, string text, bool inHierarchy = true)
        {
            var l = Labels(root, objectName, text, inHierarchy);
            Assert.That(l.Count, Is.EqualTo(1), "exactly one active label \"" + text + "\"");
            return l.Count > 0 ? l[0] : null;
        }

        static SpriteRenderer PlateOf(Transform root, string plateName, TextMeshPro label)
        {
            if (label == null) return null;
            foreach (var s in root.GetComponentsInChildren<SpriteRenderer>(true))
                if (s.name == plateName && ((s.transform.position - label.transform.position) - new Vector3(0, .02f, 0)).sqrMagnitude < 1e-6f) return s;
            return null;
        }

        static float TextAlpha(Transform root, string objectName, string text, bool inHierarchy = true)
        {
            var l = One(root, objectName, text, inHierarchy);
            return l == null ? -1f : l.color.a;
        }

        static float PlateAlpha(Transform root, string objectName, string plateName, string text, bool inHierarchy = true)
        {
            var p = PlateOf(root, plateName, One(root, objectName, text, inHierarchy));
            Assert.That(p, Is.Not.Null, "the plate of \"" + text + "\" is drawn");
            return p == null ? -1f : p.color.a;
        }

        static Rect PlateRect(Transform root, string objectName, string plateName, string text)
        {
            var p = PlateOf(root, plateName, One(root, objectName, text));
            Assert.That(p, Is.Not.Null, "the plate of \"" + text + "\" is drawn");
            return p == null ? default : XY(p.bounds);
        }

        static SpriteRenderer SpriteAt(Transform root, string name, Vector3 at)
        {
            foreach (var s in root.GetComponentsInChildren<SpriteRenderer>(true))
                if (s.name == name && s.enabled && (s.transform.position - at).sqrMagnitude < 1e-4f) return s;
            return null;
        }

        static int Enabled(Transform root, string name)
        {
            int n = 0;
            foreach (var s in root.GetComponentsInChildren<SpriteRenderer>(true)) if (s.name == name && s.enabled) n++;
            return n;
        }

        /// <summary>A cluster rectangle of width 1 level with the plate, its right edge <paramref name="gap"/> left of the plate's left edge (negative: overlapping).</summary>
        static Rect LeftOf(Rect plate, float gap) { return new Rect(plate.xMin - gap - 1f, plate.yMin - .05f, 1f, plate.height + .1f); }

        static Rect Union(Rect a, Rect b) { return Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax)); }

        // ---- drives ----

        void Run(CollectionsView view, int times, Func<float, float, bool> canSee = null)
        {
            for (int i = 0; i < times; i++) _marks.Update(view, canSee, Step);
        }

        /// <summary>One <see cref="WorldIndicators.Update"/> at the next .05 s (the first call of a rig is at 1.0 s with no step).</summary>
        void Up(CollectionsView view, bool first = false, bool haveLocal = false, float localX = 0, float localY = 0, double serverMs = 0)
        {
            if (!first) _now += Step;
            _props.Update(view, serverMs, haveLocal, localX, localY, _now, null);
        }

        void UpTimes(CollectionsView view, int times) { for (int i = 0; i < times; i++) Up(view); }

        /// <summary>The plate of a mark label, measured on a throwaway presenter (so the real one has not seen the label yet).</summary>
        Rect MeasureMark(CollectionsView view, string text)
        {
            var go = new GameObject("measure marks"); _extra.Add(go);
            var m = new WorldMarks(go.transform);
            m.Update(view, null, Step);
            return PlateRect(go.transform, MarkLabel, MarkPlate, text);
        }

        Rect MeasureProp(CollectionsView view, string text)
        {
            var go = new GameObject("measure props"); _extra.Add(go);
            var w = new WorldIndicators(go.transform);
            w.Update(view, 0, false, 0, 0, 1f, null);
            return PlateRect(go.transform, PropLabel, PropPlate, text);
        }

        // ---- 1 to 3: a quest's name stands once ----

        [Test]
        public void ThreeObjectivesShowTheQuestNameOnce()
        {
            var view = Ember();
            _marks.Update(view, null, Step);
            var names = Labels(_root.transform, MarkLabel, "Ember Walk");
            Assert.That(names.Count, Is.EqualTo(1), "three drawn points of one quest show its name once");
            if (names.Count > 0) Assert.That(names[0].transform.position.x, Is.EqualTo(ArenaView.WorldPoint(200, 220).x).Within(1e-3f), "on the first point");
            Assert.That(_marks.Used, Is.EqualTo(4), "Used still counts marks: the giver and three points");
            Assert.That(Enabled(_root.transform, "World marker"), Is.EqualTo(4), "all four glyphs stay");
            Assert.That(Labels(_root.transform, MarkLabel, "Scorch quest giver").Count, Is.EqualTo(1), "the giver's name stands once (guard)");
        }

        [Test]
        public void TheNameIsStickyAndAFoggedCarrierPassesItOn()
        {
            var view = Ember();
            float x200 = ArenaView.WorldPoint(200, 220).x, x320 = ArenaView.WorldPoint(320, 220).x;
            _marks.Update(view, null, Step);
            var one = Labels(_root.transform, MarkLabel, "Ember Walk");
            Assert.That(one.Count, Is.EqualTo(1), "set-up: one name");
            if (one.Count > 0) Assert.That(one[0].transform.position.x, Is.EqualTo(x200).Within(1e-3f), "at the first point");

            _marks.Update(view, (x, y) => x != 200, Step);
            one = Labels(_root.transform, MarkLabel, "Ember Walk");
            Assert.That(one.Count, Is.EqualTo(1), "the first point fogged: still one name");
            if (one.Count > 0) Assert.That(one[0].transform.position.x, Is.EqualTo(x320).Within(1e-3f), "it passed to the second point");
            Assert.That(_marks.Used, Is.EqualTo(3), "the giver and two points are drawn");

            _marks.Update(view, null, Step);
            one = Labels(_root.transform, MarkLabel, "Ember Walk");
            Assert.That(one.Count, Is.EqualTo(1), "the first point is back: one name");
            if (one.Count > 0) Assert.That(one[0].transform.position.x, Is.EqualTo(x320).Within(1e-3f), "sticky: it stays on the second point");
            Assert.That(_marks.Used, Is.EqualTo(4), "four marks again");

            Deliver(view, Quests(new[] { Row(71, "Ember Walk", "Scorch quest giver", 64, 80) }, new[] { Pt(71, 200, 220), Pt(71, 440, 220) }));
            _marks.Update(view, null, Step);
            one = Labels(_root.transform, MarkLabel, "Ember Walk");
            Assert.That(one.Count, Is.EqualTo(1), "the second point is gone: one name");
            if (one.Count > 0) Assert.That(one[0].transform.position.x, Is.EqualTo(x200).Within(1e-3f), "the first drawn point takes it");
        }

        [Test]
        public void EachQuestAndDropKeepsItsText()
        {
            var rows = new[]
            {
                Row(71, "Ember Walk", "Scorch quest giver", 64, 80),
                Row(72, "Iron Trail", "Smith quest giver", 96, 80, def: "iron_trail"),
                Row(73, "Marked Run", "Dash quest giver", 128, 80, def: "marked_run", accepted: false, carrier: 9),
            };
            var points = new[] { Pt(71, 200, 220), Pt(71, 320, 220), Pt(72, 200, 300), Pt(72, 320, 300), Pt(73, 200, 380), Pt(73, 320, 380) };
            var drops = new[] { new WireQuestDrop { Index = 0, Reward = 0, Charges = 1, X = 500, Y = 500 }, new WireQuestDrop { Index = 1, Reward = 0, Charges = 1, X = 560, Y = 500 } };
            var view = Deliver(new CollectionsView(), Quests(rows, points, drops));
            _marks.Update(view, null, Step);
            Assert.That(Labels(_root.transform, MarkLabel, "Ember Walk").Count, Is.EqualTo(1), "quest 71: one name for two points");
            Assert.That(Labels(_root.transform, MarkLabel, "Iron Trail").Count, Is.EqualTo(1), "quest 72: one name for two points");
            Assert.That(Labels(_root.transform, MarkLabel, "Marked Run").Count, Is.EqualTo(1), "a public carrier mark: one name for two points");
            Assert.That(Labels(_root.transform, MarkLabel, "Warden's Cage").Count, Is.EqualTo(2), "two drops of one reward keep their own text");
            Assert.That(_marks.Used, Is.EqualTo(3 + 6 + 2), "three givers, six points and two drops are all marks");
        }

        // ---- 4: a label under the cluster fades, its glyph stays ----

        [Test]
        public void AGiverNameUnderTheClusterFadesAndItsGlyphStays()
        {
            var view = Ember();
            _marks.Update(view, null, Step);
            var plate = PlateRect(_root.transform, MarkLabel, MarkPlate, "Scorch quest giver");
            _cluster = plate; _marks.LocalCluster = Read;
            float[] want = { .667f, .333f, 0f, 0f };
            var glyphAt = ArenaView.WorldPoint(64, 80) + new Vector3(0, 2.55f, 0);
            for (int i = 0; i < want.Length; i++)
            {
                _marks.Update(view, null, Step);
                Assert.That(TextAlpha(_root.transform, MarkLabel, "Scorch quest giver"), Is.EqualTo(want[i]).Within(Tol), "text alpha, step " + (i + 1));
                Assert.That(PlateAlpha(_root.transform, MarkLabel, MarkPlate, "Scorch quest giver"), Is.EqualTo(want[i]).Within(Tol), "plate alpha, step " + (i + 1));
            }
            Assert.That(Labels(_root.transform, MarkLabel, "Scorch quest giver").Count, Is.EqualTo(1), "the faded label stays active");
            var glyph = SpriteAt(_root.transform, "World marker", glyphAt); var back = SpriteAt(_root.transform, "World marker plate", glyphAt);
            Assert.That(glyph, Is.Not.Null, "the glyph stays enabled");
            Assert.That(back, Is.Not.Null, "the glyph plate stays enabled");
            if (glyph != null) Assert.That(glyph.color.a, Is.EqualTo(.6f).Within(1e-3f), "the glyph keeps its own alpha (accepted: .6)");
            if (back != null) Assert.That(back.color.a, Is.EqualTo(1f).Within(1e-3f), "the glyph plate keeps its own alpha");
            Assert.That(_marks.Used, Is.EqualTo(4), "Used does not change with the fade");

            var go = new GameObject("clear marks"); _extra.Add(go);
            var clear = new WorldMarks(go.transform);
            clear.Update(view, null, Step);
            _cluster = LeftOf(PlateRect(go.transform, MarkLabel, MarkPlate, "Scorch quest giver"), .45f); clear.LocalCluster = Read;
            for (int i = 0; i < 4; i++)
            {
                clear.Update(view, null, Step);
                Assert.That(TextAlpha(go.transform, MarkLabel, "Scorch quest giver"), Is.EqualTo(1f).Within(Tol), "a cluster .45 away leaves the label at 1, step " + (i + 1));
            }
        }

        [Test]
        public void TwoGiversInOneZoneShowTheTwoQuestNamesBeforeAcceptance()
        {
            var view = Deliver(new CollectionsView(), Quests(new[] {
                Row(71, "Ember Walk", "Scorch quest giver", 64, 80, accepted: false),
                Row(72, "Elite Slayer", "Scorch quest giver", 960, 480, "elite_slayer", accepted: false)
            }, Array.Empty<WireQuestObjective>()));
            _marks.Update(view, null, Step);
            Assert.That(Labels(_root.transform, MarkLabel, "Ember Walk").Count, Is.EqualTo(1));
            Assert.That(Labels(_root.transform, MarkLabel, "Elite Slayer").Count, Is.EqualTo(1));
        }

        [Test]
        public void RemoteTurnInUsesTheDeliveredEndpointAndRespectsFog()
        {
            var view = Deliver(new CollectionsView(), Quests(new[] { Row(71, "Letters", "Archivist", 64, 80, "letters", ready: true) }, new[] { Pt(71, 320, 240) }));
            _marks.Update(view, null, Step);
            Assert.That(Labels(_root.transform, MarkLabel, "Turn in here").Count, Is.EqualTo(1));
            Assert.That(Labels(_root.transform, MarkLabel, "Archivist").Count, Is.EqualTo(1));
            _marks.Update(view, (x, y) => x < 200, Step);
            Assert.That(Labels(_root.transform, MarkLabel, "Turn in here").Count, Is.Zero);
        }

        [Test]
        public void TurnInNamesFadeLikeAnyOther()
        {
            var view = Deliver(new CollectionsView(), Quests(new[] { Row(71, "Ember Walk", "Scorch quest giver", 64, 80, ready: true) }, new WireQuestObjective[0]));
            _marks.Update(view, null, Step);
            Assert.That(Labels(_root.transform, MarkLabel, "Turn in here").Count, Is.EqualTo(1), "set-up: a ready quest says \"Turn in here\"");
            _cluster = PlateRect(_root.transform, MarkLabel, MarkPlate, "Turn in here"); _marks.LocalCluster = Read;
            float[] want = { .667f, .333f, 0f, 0f };
            for (int i = 0; i < want.Length; i++)
            {
                _marks.Update(view, null, Step);
                Assert.That(TextAlpha(_root.transform, MarkLabel, "Turn in here"), Is.EqualTo(want[i]).Within(Tol), "text alpha, step " + (i + 1));
                Assert.That(PlateAlpha(_root.transform, MarkLabel, MarkPlate, "Turn in here"), Is.EqualTo(want[i]).Within(Tol), "plate alpha, step " + (i + 1));
            }
            var glyph = SpriteAt(_root.transform, "World marker", ArenaView.WorldPoint(64, 80) + new Vector3(0, 2.55f, 0));
            Assert.That(glyph, Is.Not.Null, "the glyph stays");
        }

        [Test]
        public void ATraderNameFadesLikeAnyOtherAndTheFigureStays()
        {
            var view = Deliver(new CollectionsView(), Trade(Merchant("Orrin", TradeKind.Barter, 300, 300)));
            _marks.Update(view, null, Step);
            Assert.That(Labels(_root.transform, MarkLabel, "Orrin").Count, Is.EqualTo(1), "set-up: the trader's name");
            _cluster = PlateRect(_root.transform, MarkLabel, MarkPlate, "Orrin"); _marks.LocalCluster = Read;
            float[] want = { .667f, .333f, 0f, 0f };
            for (int i = 0; i < want.Length; i++)
            {
                _marks.Update(view, null, Step);
                Assert.That(TextAlpha(_root.transform, MarkLabel, "Orrin"), Is.EqualTo(want[i]).Within(Tol), "text alpha, step " + (i + 1));
                Assert.That(PlateAlpha(_root.transform, MarkLabel, MarkPlate, "Orrin"), Is.EqualTo(want[i]).Within(Tol), "plate alpha, step " + (i + 1));
            }
            var figure = _root.transform.Find("Trader/Trader figure");
            Assert.That(figure != null && figure.GetComponent<SpriteRenderer>().enabled, Is.True, "the trader figure stays enabled");
            Assert.That(Enabled(_root.transform, "World marker"), Is.EqualTo(1), "and its diamond");
        }

        // ---- 5: a hidden carrier does not pass the name on ----

        [Test]
        public void AHiddenCarrierDoesNotPassTheNameOnBecauseOfTheFade()
        {
            var view = Ember();
            _marks.Update(view, null, Step);
            var plate = PlateRect(_root.transform, MarkLabel, MarkPlate, "Ember Walk");
            _cluster = plate; _marks.LocalCluster = Read;
            Run(view, 6);
            var names = Labels(_root.transform, MarkLabel, "Ember Walk");
            Assert.That(names.Count, Is.EqualTo(1), "exactly one \"Ember Walk\" label is active, the carrier's");
            if (names.Count > 0)
            {
                Assert.That(names[0].transform.position.x, Is.EqualTo(ArenaView.WorldPoint(200, 220).x).Within(1e-3f), "it is still on the first point");
                Assert.That(names[0].color.a, Is.EqualTo(0f).Within(Tol), "and it is faded to 0");
            }
            int active = 0;
            foreach (var l in Labels(_root.transform, MarkLabel)) if (l.text != "Scorch quest giver") active++;
            Assert.That(active, Is.EqualTo(1), "the other two points' labels are inactive: no label but the carrier's and the giver's");
        }

        // ---- 6: skipped, not guessed ----

        [TestCase(false)]
        [TestCase(true)]
        public void AnInactiveRootKeepsItsStateAndResumes(bool viaHiddenFlag)
        {
            var view = Ember();
            _marks.Update(view, null, Step);
            var plate = PlateRect(_root.transform, MarkLabel, MarkPlate, "Scorch quest giver");
            _cluster = plate; _marks.LocalCluster = Read;
            Run(view, 4);
            Assert.That(TextAlpha(_root.transform, MarkLabel, "Scorch quest giver"), Is.EqualTo(0f).Within(Tol), "set-up: held hidden at 0");

            _cluster = LeftOf(plate, .45f);
            if (viaHiddenFlag) WorldLabelVisibility.Hidden = true; else _root.SetActive(false);
            Run(view, 4);
            Assert.That(TextAlpha(_root.transform, MarkLabel, "Scorch quest giver", inHierarchy: false), Is.EqualTo(0f).Within(Tol), "no state steps while skipped: still 0 (a step would reach 1)");
            if (viaHiddenFlag) WorldLabelVisibility.Hidden = false; else _root.SetActive(true);
            _marks.Update(view, null, Step);
            Assert.That(TextAlpha(_root.transform, MarkLabel, "Scorch quest giver"), Is.EqualTo(.333f).Within(Tol), "one Update after the skip steps once");
        }

        // ---- 7: nothing else moved (guard) ----

        [Test]
        public void NothingElseMoved()
        {
            var view = Ember(200);
            _marks.Update(view, null, Step);
            var giverGlyph = ArenaView.WorldPoint(64, 80) + new Vector3(0, 2.55f, 0);
            var giver = One(_root.transform, MarkLabel, "Scorch quest giver");
            Assert.That(giver.transform.position.x, Is.EqualTo(giverGlyph.x).Within(1e-4f), "giver label x");
            Assert.That(giver.transform.position.y, Is.EqualTo(giverGlyph.y - .45f).Within(1e-4f), "giver label sits .45 below its glyph (.2 and .25)");
            Assert.That(giver.fontSize, Is.EqualTo(2.3f).Within(1e-4f), "giver label size");
            Assert.That(giver.GetComponent<MeshRenderer>().sortingOrder, Is.EqualTo(29001), "mark text order");
            var plate = PlateOf(_root.transform, MarkPlate, giver);
            Assert.That(plate.sortingOrder, Is.EqualTo(28999), "mark label plate order");
            Assert.That(plate.transform.localScale.x, Is.EqualTo(giver.preferredWidth + .2f).Within(1e-3f), "plate width: preferred width + .2");
            Assert.That(plate.transform.localScale.y, Is.EqualTo(2.3f * .12f + .02f).Within(1e-4f), "plate height: size * .12 + .02");
            var glyph = SpriteAt(_root.transform, "World marker", giverGlyph);
            Assert.That(glyph.sortingOrder, Is.EqualTo(29000), "glyph order");
            Assert.That(glyph.transform.localScale.x, Is.EqualTo(.5f).Within(1e-4f), "giver glyph size");
            var objGlyph = ArenaView.WorldPoint(200, 220) + new Vector3(0, .9f, 0);
            var name = One(_root.transform, MarkLabel, "Ember Walk");
            Assert.That(name.transform.position.y, Is.EqualTo(objGlyph.y - .37f).Within(1e-4f), "objective label sits .37 below its glyph (.12 and .25)");
            Assert.That(name.fontSize, Is.EqualTo(2f).Within(1e-4f), "objective label size");
            Assert.That(SpriteAt(_root.transform, "World marker", objGlyph).transform.localScale.x, Is.EqualTo(.42f).Within(1e-4f), "objective glyph size");

            var props = Props(Body("Ash", 100, 100),
                new PropView { Kind = PropAppearance.Chest, X = 300, Y = 100, Focused = true },
                new PropView { Kind = PropAppearance.QuestItem, X = 500, Y = 100, Focused = true, QuestFetch = true },
                new PropView { Kind = PropAppearance.Cannon, X = 700, Y = 100, Focused = true });
            Up(props, first: true);
            float[] lifts = { .85f, 1.15f, 1.2f, 1.4f }; string[] texts = { "Ash", "Chest", "Quest item", "Travel cannon · F to blast" }; float[] xs = { 100, 300, 500, 700 };
            for (int i = 0; i < texts.Length; i++)
            {
                var l = One(_propsRoot.transform, PropLabel, texts[i]);
                var at = ArenaView.WorldPoint(xs[i], 100);
                Assert.That(l.transform.position.y, Is.EqualTo(at.y + lifts[i]).Within(1e-4f), texts[i] + ": lift");
                Assert.That(l.fontSize, Is.EqualTo(2.4f).Within(1e-4f), texts[i] + ": size");
                Assert.That(l.GetComponent<MeshRenderer>().sortingOrder, Is.EqualTo(29101), texts[i] + ": text order");
                var pl = PlateOf(_propsRoot.transform, PropPlate, l);
                Assert.That(pl.sortingOrder, Is.EqualTo(29100), texts[i] + ": plate order");
                Assert.That(pl.color.a, Is.EqualTo(.92f).Within(1e-3f), texts[i] + ": plate alpha");
                Assert.That(pl.transform.localScale.x, Is.EqualTo(l.preferredWidth + .16f).Within(1e-3f), texts[i] + ": plate width");
                Assert.That(pl.transform.localScale.y, Is.EqualTo(2.4f * .1f + .08f).Within(1e-4f), texts[i] + ": plate height");
            }
        }

        // ---- 8 and 9: no allocation in a steady frame ----

        [Test]
        public void ASteadyMarksFrameAllocatesNothing()
        {
            var rows = new[] { Row(71, "Ember Walk", "Scorch quest giver", 64, 80) };
            var points = new[] { Pt(71, 200, 220), Pt(71, 320, 220), Pt(71, 440, 220) };
            var drops = new[] { new WireQuestDrop { Index = 0, Reward = 0, Charges = 1, X = 560, Y = 500 } };
            var view = Deliver(new CollectionsView(), Quests(rows, points, drops), Trade(Merchant("Orrin", TradeKind.Barter, 700, 300)));
            _marks.Update(view, null, Step);
            _cluster = PlateRect(_root.transform, MarkLabel, MarkPlate, "Scorch quest giver"); _marks.LocalCluster = Read;
            Run(view, 8);
            Assert.That(TextAlpha(_root.transform, MarkLabel, "Scorch quest giver"), Is.EqualTo(0f).Within(Tol), "set-up: the giver's text has faded to 0");
            Assert.That(Labels(_root.transform, MarkLabel, "Ember Walk").Count, Is.EqualTo(1), "set-up: exactly one \"Ember Walk\" label is active");
            Assert.That(Labels(_root.transform, MarkLabel, "Warden's Cage").Count, Is.EqualTo(1), "set-up: the drop's text");
            Assert.That(Labels(_root.transform, MarkLabel, "Orrin").Count, Is.EqualTo(1), "set-up: the trader's name");
            Action step = () => _marks.Update(view, null, Step);
            AllocationMeter.AssertNone(step, 200, "a steady WorldMarks.Update with a giver, three points, a drop and a trader under a cluster");
        }

        [Test]
        public void ASteadyPropsFrameAllocatesNothing()
        {
            var view = Props(Body("Ash", 100, 100),
                new PropView { Kind = PropAppearance.Chest, X = 400, Y = 100, Focused = true },
                new PropView { Kind = PropAppearance.Ground, Weapon = WeaponId.Bow, X = 700, Y = 100, Focused = true },
                new PropView { Kind = PropAppearance.Corpse, CorpseLabel = "Kora", X = 1000, Y = 100, Focused = true },
                new PropView { Kind = PropAppearance.QuestItem, X = 1300, Y = 100, Focused = true, QuestFetch = true });
            Up(view, first: true);
            _cluster = PlateRect(_propsRoot.transform, PropLabel, PropPlate, "Ash"); _props.LocalCluster = Read;
            UpTimes(view, 8);
            Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Ash"), Is.EqualTo(0f).Within(Tol), "set-up: the body label has faded to 0");
            foreach (string other in new[] { "Chest", WorldIndicatorRules.WeaponNames[(int)WeaponId.Bow], "Kora", "Quest item" })
                Assert.That(TextAlpha(_propsRoot.transform, PropLabel, other), Is.EqualTo(1f).Within(Tol), "set-up: \"" + other + "\" is drawn at alpha 1 (ShowProp runs for it)");
            Action step = () => _props.Update(view, 0, false, 0, 0, 5f, null);
            AllocationMeter.AssertNone(step, 200, "a steady WorldIndicators.Update with a body, chest, weapon, corpse and quest item");
        }

        // ---- 10 to 14: prop labels ----

        [Test]
        public void AFacedPropLabelFadesAndBack()
        {
            var view = Props(Body("Ash", 100, 100));
            Up(view, first: true);
            var plate = PlateRect(_propsRoot.transform, PropLabel, PropPlate, "Ash");
            Assert.That(PlateAlpha(_propsRoot.transform, PropLabel, PropPlate, "Ash"), Is.EqualTo(.92f).Within(1e-3f), "set-up: a clear prop plate is .92");
            _cluster = plate; _props.LocalCluster = Read;
            float[] down = { .667f, .333f, 0f, 0f };
            foreach (float a in down)
            {
                Up(view);
                Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Ash"), Is.EqualTo(a).Within(Tol), "text alpha down");
                Assert.That(PlateAlpha(_propsRoot.transform, PropLabel, PropPlate, "Ash"), Is.EqualTo(.92f * a).Within(Tol), "plate alpha is .92 times it");
            }
            _cluster = LeftOf(plate, .45f);
            float[] up = { .333f, .667f, 1f, 1f };
            foreach (float a in up)
            {
                Up(view);
                Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Ash"), Is.EqualTo(a).Within(Tol), "text alpha up");
                Assert.That(PlateAlpha(_propsRoot.transform, PropLabel, PropPlate, "Ash"), Is.EqualTo(.92f * a).Within(Tol), "plate alpha up: the plate peaks at .92 times alpha");
            }
            Assert.That(_props.LabelsShown, Is.EqualTo(1), "LabelsShown did not change with the fade");
        }

        [Test]
        public void BetweenThePadsNothingFlickers()
        {
            var view = Props(Body("Ash", 100, 100));
            Up(view, first: true);
            var plate = PlateRect(_propsRoot.transform, PropLabel, PropPlate, "Ash");
            _props.LocalCluster = Read;
            _cluster = LeftOf(plate, .25f);
            for (int i = 0; i < 6; i++) { Up(view); Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Ash"), Is.EqualTo(1f).Within(Tol), "gap .25 does not hide a visible label, frame " + i); }
            _cluster = LeftOf(plate, -.05f);
            UpTimes(view, 4);
            Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Ash"), Is.EqualTo(0f).Within(Tol), "an overlap hides it");
            _cluster = LeftOf(plate, .25f);
            for (int i = 0; i < 6; i++) { Up(view); Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Ash"), Is.EqualTo(0f).Within(Tol), "gap .25 does not bring a hidden label back, frame " + i); }
            _cluster = LeftOf(plate, .45f);
            UpTimes(view, 4);
            Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Ash"), Is.EqualTo(1f).Within(Tol), "gap .45 brings it back");
        }

        [Test]
        public void TheQuestItemMarkerAndChestSecondsAreNeverHidden()
        {
            var props = new[] { new PropView { Kind = PropAppearance.QuestItem, X = 100, Y = 100, Focused = true, QuestFetch = true } };
            var view = new CollectionsView();
            typeof(CollectionsView).GetProperty("Props", BindingFlags.Instance | BindingFlags.Public).SetValue(view, props);
            typeof(CollectionsView).GetProperty("RewardChests", BindingFlags.Instance | BindingFlags.Public).SetValue(view, new[] { RewardChest(140, 100, 12000) });
            Up(view, first: true, haveLocal: true, localX: 140, localY: 100);
            Assert.That(_props.SecondsShownForTest(0), Is.EqualTo("12s"), "set-up: the near chest shows its seconds");
            var marker = _propsRoot.transform.Find("Quest item marker").GetComponent<SpriteRenderer>();
            var markerPlate = _propsRoot.transform.Find("Quest item marker plate").GetComponent<SpriteRenderer>();
            var seconds = One(_propsRoot.transform, PropLabel, "12s");
            var secondsPlate = PlateOf(_propsRoot.transform, PropPlate, seconds);
            Assert.That(marker.enabled && markerPlate.enabled && seconds != null && secondsPlate != null, Is.True, "set-up: marker, plate and seconds are drawn");
            float markerAlpha = marker.color.a, secondsAlpha = seconds.color.a, secondsPlateAlpha = secondsPlate.color.a;
            var labelPlate = PlateRect(_propsRoot.transform, PropLabel, PropPlate, "Quest item");
            var all = Union(Union(XY(marker.bounds), XY(markerPlate.bounds)), Union(labelPlate, Union(XY(seconds.GetComponent<MeshRenderer>().bounds), XY(secondsPlate.bounds))));
            _cluster = all; _props.LocalCluster = Read;
            for (int i = 0; i < 4; i++) Up(view, haveLocal: true, localX: 140, localY: 100);
            Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Quest item"), Is.EqualTo(0f).Within(Tol), "set-up: the quest item's text fades under the cluster");
            Assert.That(marker.enabled && markerPlate.enabled, Is.True, "the marker and its plate stay enabled");
            Assert.That(marker.color.a, Is.EqualTo(markerAlpha).Within(1e-4f), "the marker keeps its alpha");
            Assert.That(_props.SecondsShownForTest(0), Is.EqualTo("12s"), "the chest's seconds text stays shown");
            Assert.That(seconds.color.a, Is.EqualTo(secondsAlpha).Within(1e-4f), "the seconds text keeps its alpha");
            Assert.That(secondsPlate.color.a, Is.EqualTo(secondsPlateAlpha).Within(1e-4f), "the seconds plate keeps its alpha");
            Assert.That(_props.RingsShown, Is.EqualTo(1), "the ring stays");
        }

        [Test]
        public void AFreshLabelStartsHiddenAndReducedSwitchesAtOnce()
        {
            var view = Props(Body("Ash", 100, 100));
            var plate = MeasureProp(view, "Ash");
            _cluster = plate; _props.LocalCluster = Read;
            Up(view, first: true);
            Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Ash"), Is.EqualTo(0f).Within(1e-4f), "a label first drawn under the cluster is at 0 on its first Update (no ramp)");
            Assert.That(PlateAlpha(_propsRoot.transform, PropLabel, PropPlate, "Ash"), Is.EqualTo(0f).Within(1e-4f), "and so is its plate");

            UiMotion.ReducedOverride = true;
            _cluster = LeftOf(plate, .45f);
            Up(view);
            Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Ash"), Is.EqualTo(1f).Within(1e-4f), "reduced motion: 1 on the next Update");
            _cluster = plate;
            Up(view);
            Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Ash"), Is.EqualTo(0f).Within(1e-4f), "reduced motion: 0 on the next Update");
        }

        [Test]
        public void AnEarlierMarkLeavingResetsNothing()
        {
            var both = Props(Body("Ash", 100, 100), Body("Bran", 400, 100));
            Up(both, first: true);
            var plate = PlateRect(_propsRoot.transform, PropLabel, PropPlate, "Bran");
            _cluster = plate; _props.LocalCluster = Read;
            UpTimes(both, 4);
            Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Bran"), Is.EqualTo(0f).Within(Tol), "set-up: the second body is hidden");
            Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Ash"), Is.EqualTo(1f).Within(Tol), "set-up: the first is clear");
            _cluster = LeftOf(plate, .25f);
            Up(both);
            Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Bran"), Is.EqualTo(0f).Within(Tol), "set-up: held hidden in the band");
            var second = Props(Body("Bran", 400, 100));
            Up(second);
            Assert.That(_props.LabelsShown, Is.EqualTo(1), "set-up: one label is drawn, in slot 0");
            Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Bran"), Is.EqualTo(0f).Within(Tol), "the second body, now in slot 0, is still 0");
        }

        // ---- 15 to 18: the local cluster of the presenter ----

        static bool Holds(Rect outer, Rect inner, float tol) { return outer.xMin <= inner.xMin + tol && outer.xMax >= inner.xMax - tol && outer.yMin <= inner.yMin + tol && outer.yMax >= inner.yMax - tol; }

        static bool Equal(Rect a, Rect b, float tol) { return Mathf.Abs(a.xMin - b.xMin) <= tol && Mathf.Abs(a.xMax - b.xMax) <= tol && Mathf.Abs(a.yMin - b.yMin) <= tol && Mathf.Abs(a.yMax - b.yMax) <= tol; }

        static Actor Hero(bool badge = true, float x = 640f, float y = 640f) { return Actor.Fighter(1, x, "Hero", local: true, y: y).Marks(0, badge, 0, 0, 0); }

        static readonly Actor Far = Actor.Fighter(2, 4000f, "Other");

        [Test]
        public void TheLocalClusterBoundsNameBarAndBadge()
        {
            var scene = Scene.Of(Hero(), Far);
            Frame(P, scene);
            Assert.That(P.TryLocalClusterRect(out var r), Is.True, "a local fighter is drawn: a rectangle");
            Assert.That(BarShown(P, 1), Is.True, "set-up: the bar track is drawn");
            var name = NameOf(P, 1); name.ForceMeshUpdate();
            Rect nameBox = XY(name.GetComponent<MeshRenderer>().bounds), bar = XY(SpriteOf(P, 1, "Health and barrier track").bounds), badge = XY(SpriteOf(P, 1, "Secondary weapon backer").bounds);
            Assert.That(SpriteOf(P, 1, "Secondary weapon backer").enabled, Is.True, "set-up: the off-hand badge is drawn");
            Assert.That(Holds(r, nameBox, .02f), Is.True, "the rectangle holds the name's drawn bounds");
            Assert.That(Holds(r, bar, .02f), Is.True, "the rectangle holds the bar track");
            Assert.That(Holds(r, badge, .02f), Is.True, "the rectangle holds the off-hand badge");
            Assert.That(r.Overlaps(LineBoxOf(P, 2)), Is.False, "the other fighter's name is not in it");

            Frame(P, Scene.Of(Hero().Moved(400f, 0f), Far));
            Assert.That(P.TryLocalClusterRect(out var moved), Is.True, "still a rectangle after the fighter moved");
            Assert.That(moved.center.x - r.center.x, Is.EqualTo(12.5f).Within(.01f), "moved 400 px: the centre moves 12.5 units");
            Assert.That(moved.center.y, Is.EqualTo(r.center.y).Within(.01f), "and not in y");

            Action read = () => { P.TryLocalClusterRect(out _); };
            AllocationMeter.AssertNone(read, 200, "TryLocalClusterRect");
        }

        [Test]
        public void NoLocalNoRect()
        {
            Frame(P, Scene.Of(Hero(), Far));
            Assert.That(P.TryLocalClusterRect(out _), Is.True, "set-up (absent): a local fighter, alive");
            Frame(P, Scene.Of(Far));
            Assert.That(P.TryLocalClusterRect(out _), Is.False, "the local art absent from the next frame: no rectangle");

            Frame(P, Scene.Of(Hero(), Far));
            Assert.That(P.TryLocalClusterRect(out _), Is.True, "set-up (hidden): a local fighter, alive, labels shown");
            WorldLabelVisibility.Hidden = true;
            Assert.That(P.TryLocalClusterRect(out _), Is.False, "WorldLabelVisibility.Hidden: no rectangle");
            Frame(P, Scene.Of(Hero(), Far));
            Assert.That(P.TryLocalClusterRect(out _), Is.False, "and after a frame drawn while it is true");
            WorldLabelVisibility.Hidden = false;

            Frame(P, Scene.Of(Hero(), Far));
            Assert.That(P.TryLocalClusterRect(out _), Is.True, "set-up (dead): a local fighter, alive");
            P.Begin();
            P.Present(1, 640f, 640f, 0, 0f, FactionId.Holy, ActorStatus.Dead, WeaponId.Fists, ActorKind.Player, null, 0, 0, true);
            P.PresentIndicators(1, 0, false, WeaponId.Fists, false);
            P.End(Dt);
            Assert.That(P.TryLocalClusterRect(out _), Is.False, "the local fighter dead: no rectangle");
        }

        [Test]
        public void OnlyTheLocalClusterHidesALabel()
        {
            var scene = Scene.Of(Hero(), Far);
            Frame(P, scene);
            var mine = LineBoxOf(P, 1).center; var theirs = LineBoxOf(P, 2).center;
            var a = WorldToPx(new Vector2(mine.x, mine.y - .85f)); var b = WorldToPx(new Vector2(theirs.x, theirs.y - .85f));
            var view = Props(Body("Ash", a.x, a.y), Body("Bran", b.x, b.y));
            Up(view, first: true);
            Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Ash"), Is.EqualTo(1f).Within(Tol), "set-up: no source, the local one is clear");
            _props.LocalCluster = P.TryLocalClusterRect;
            for (int i = 0; i < 4; i++) { Frame(P, scene); Up(view); }
            Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Ash"), Is.EqualTo(0f).Within(Tol), "a body label over the local fighter's name fades");
            Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Bran"), Is.EqualTo(1f).Within(Tol), "one over the other fighter's name stays 1");
        }

        [Test]
        public void AStatusEffectDoesNotGrowTheCluster()
        {
            Frame(P, Scene.Of(Hero()));
            Assert.That(P.TryLocalClusterRect(out var plain), Is.True, "set-up: a rectangle");
            var casting = Hero(); casting.Effects = Indicators.Casting;
            Frame(P, Scene.Of(casting));
            var glyph = SpriteOf(P, 1, "Status cast");
            Assert.That(glyph.enabled, Is.True, "set-up: \"Status cast\" is enabled");
            Assert.That(Holds(plain, XY(glyph.bounds), 0f), Is.False, "set-up: the cast glyph lies outside the plain rectangle (adding it would grow it)");
            Assert.That(P.TryLocalClusterRect(out var withCast), Is.True, "a rectangle while casting");
            Assert.That(Equal(plain, withCast, 1e-4f), Is.True, "the rectangle is the same: " + plain + " vs " + withCast);
        }

        // ---- 19: a released source ----

        [Test]
        public void AReleasedSourceWaitsOneFrame()
        {
            UiMotion.ReducedOverride = true;
            var view = Props(Body("Ash", 100, 100));
            var plate = MeasureProp(view, "Ash");
            _cluster = plate; _answer = true; _props.LocalCluster = Read;
            Up(view, first: true);
            Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Ash"), Is.EqualTo(0f).Within(1e-4f), "set-up: a label hidden under the cluster");
            _answer = false;
            Up(view);
            Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Ash"), Is.EqualTo(0f).Within(1e-4f), "the source answers false once: the last rectangle is kept, still 0");
            _answer = true;
            Up(view);
            Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Ash"), Is.EqualTo(0f).Within(1e-4f), "true again: 0");
            _answer = false;
            Up(view);
            Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Ash"), Is.EqualTo(0f).Within(1e-4f), "false once after a true: still kept");
            Up(view);
            Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Ash"), Is.EqualTo(1f).Within(1e-4f), "false twice running: released, 1 on the second");
        }

        // ---- 20: a walking trader ----

        [Test]
        public void AWalkingTraderKeepsItsLabelState()
        {
            var view = Deliver(new CollectionsView(), Trade(Merchant("Orrin", TradeKind.Barter, 200, 300)));
            var plate = MeasureMark(view, "Orrin");
            _cluster = plate; _marks.LocalCluster = Read;
            _marks.Update(view, null, Step);
            Assert.That(TextAlpha(_root.transform, MarkLabel, "Orrin"), Is.EqualTo(0f).Within(1e-4f), "born under the cluster: 0 (no ramp)");

            _cluster = LeftOf(plate, .25f);
            Deliver(view, Trade(Merchant("Orrin", TradeKind.Barter, 202, 300)));
            Run(view, 4);
            Assert.That(TextAlpha(_root.transform, MarkLabel, "Orrin"), Is.EqualTo(0f).Within(Tol), "delivered 2 units along x, still in the band: stays 0 (a state keyed by place would be born fresh and read 1)");

            _cluster = LeftOf(plate, .45f);
            Deliver(view, Trade(Merchant("Orrin", TradeKind.Barter, 204, 300)));
            _marks.Update(view, null, Step);
            Assert.That(TextAlpha(_root.transform, MarkLabel, "Orrin"), Is.EqualTo(.333f).Within(Tol), "cluster .45 away, delivered again: one Update gives .333, it ramps (not 1)");
        }

        [Test]
        public void TwoTradersDoNotShareAState()
        {
            var view = Deliver(new CollectionsView(), Trade(Merchant("Orrin", TradeKind.Barter, 200, 300), Merchant("Vesk", TradeKind.Stats, 900, 300)));
            _cluster = MeasureMark(view, "Orrin"); _marks.LocalCluster = Read;
            for (int i = 0; i < 4; i++)
            {
                _marks.Update(view, null, Step);
                Assert.That(TextAlpha(_root.transform, MarkLabel, "Orrin"), Is.EqualTo(0f).Within(Tol), "Orrin stays hidden under the cluster, update " + (i + 1));
                Assert.That(TextAlpha(_root.transform, MarkLabel, "Vesk"), Is.EqualTo(1f).Within(Tol), "the other trader, far from it, stays 1, update " + (i + 1));
            }
        }

        // ---- 21 and 22: a part of the cluster that grows the rectangle ----

        enum Edge { Left, Right, Top, Bottom }

        static float EdgeOf(Rect r, Edge e) { return e == Edge.Left ? r.xMin : e == Edge.Right ? r.xMax : e == Edge.Top ? r.yMax : r.yMin; }

        /// <summary>How far the rectangle <paramref name="grown"/> reaches past <paramref name="plain"/> on one edge.</summary>
        static float Growth(Rect plain, Rect grown, Edge e)
        {
            return e == Edge.Left ? plain.xMin - grown.xMin : e == Edge.Right ? grown.xMax - plain.xMax : e == Edge.Top ? grown.yMax - plain.yMax : plain.yMin - grown.yMin;
        }

        /// <summary>The world move of a rectangle that puts the plate <paramref name="gap"/> beyond its edge <paramref name="e"/>, level with its middle on the other axis.</summary>
        static Vector2 MoveToGap(Rect r, Edge e, Rect plate, float gap)
        {
            switch (e)
            {
                case Edge.Left: return new Vector2(plate.xMax + gap - r.xMin, plate.center.y - r.center.y);
                case Edge.Right: return new Vector2(plate.xMin - gap - r.xMax, plate.center.y - r.center.y);
                case Edge.Top: return new Vector2(plate.center.x - r.center.x, plate.yMin - gap - r.yMax);
                default: return new Vector2(plate.center.x - r.center.x, plate.yMax + gap - r.yMin);
            }
        }

        /// <summary>The distance of the plate beyond the cluster's edge <paramref name="e"/> (negative: overlapping).</summary>
        static float GapTo(Rect cluster, Edge e, Rect plate)
        {
            return e == Edge.Left ? cluster.xMin - plate.xMax : e == Edge.Right ? plate.xMin - cluster.xMax : e == Edge.Top ? plate.yMin - cluster.yMax : cluster.yMin - plate.yMax;
        }

        /// <summary>Legacy pixels of a world move (dx / Scale, dy / (-Scale * GroundSquash)).</summary>
        static Vector2 MovePx(Vector2 world) { return WorldToPx(world); }

        /// <summary>The plain rectangle R0, the badge one R1, the edge E R1 grows by at least .1 and an edge F both share (1e-3); the test fails when there is none.</summary>
        void Badge(out Rect r0, out Rect r1, out Edge grown, out Edge shared)
        {
            Frame(P, Scene.Of(Hero(badge: false)));
            Assert.That(P.TryLocalClusterRect(out r0), Is.True, "set-up: the plain rectangle R0");
            Frame(P, Scene.Of(Hero(badge: true)));
            Assert.That(P.TryLocalClusterRect(out r1), Is.True, "set-up: the badge rectangle R1");
            Assert.That(Holds(r1, r0, 1e-4f), Is.True, "set-up: R1 contains R0");
            grown = Edge.Left; shared = Edge.Left;
            float best = float.MinValue;
            foreach (Edge e in Enum.GetValues(typeof(Edge))) { float g = Growth(r0, r1, e); if (g > best) { best = g; grown = e; } }
            Assert.That(best, Is.GreaterThanOrEqualTo(.1f), "set-up: the badge grows one edge by at least .1 (measured " + best + " on " + grown + ")");
            bool found = false;
            foreach (Edge e in Enum.GetValues(typeof(Edge))) if (Mathf.Abs(Growth(r0, r1, e)) < 1e-3f) { shared = e; found = true; if (e != grown) break; }
            Assert.That(found, Is.True, "set-up: R1 shares an edge with R0");
        }

        /// <summary>Draws the fighter moved so the cluster of the given kind stands where the plate lies <paramref name="gap"/> beyond edge <paramref name="e"/> of R0.</summary>
        void PlaceFighter(Rect r0, Edge e, Rect plate, float gap, bool badge)
        {
            var move = MovePx(MoveToGap(r0, e, plate, gap));
            Frame(P, Scene.Of(Hero(badge, 640f + move.x, 640f + move.y)));
        }

        [Test]
        public void AHeldLabelIgnoresAPartThatDoesNotReachIt()
        {
            Badge(out var r0, out var r1, out _, out var shared);
            var view = Props(Body("Ash", 100, 100));
            var plate = MeasureProp(view, "Ash");
            _props.LocalCluster = P.TryLocalClusterRect;
            Frame(P, Scene.Of(Hero(badge: false)));
            var onto = MovePx(MoveToGap(r0, shared, plate, -.5f));
            Frame(P, Scene.Of(Hero(false, 640f + onto.x, 640f + onto.y)));
            Up(view, first: true);
            Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Ash"), Is.EqualTo(0f).Within(1e-4f), "set-up: the plate is born under the cluster, alpha 0");
            PlaceFighter(r0, shared, plate, .25f, false);
            Up(view);
            Assert.That(P.TryLocalClusterRect(out var held), Is.True, "set-up: a rectangle");
            Assert.That(GapTo(held, shared, plate), Is.EqualTo(.25f).Within(1e-3f), "set-up: the plate lies .25 from the shared edge F");
            Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Ash"), Is.EqualTo(0f).Within(Tol), "set-up: held at 0 in the band");
            PlaceFighter(r0, shared, plate, .25f, true);
            Assert.That(P.TryLocalClusterRect(out var withBadge), Is.True, "the badge fighter is presented");
            Assert.That(Mathf.Abs(EdgeOf(withBadge, shared) - EdgeOf(held, shared)), Is.LessThan(1e-3f), "the shared edge F did not move");
            for (int i = 0; i < 4; i++)
            {
                Up(view);
                Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Ash"), Is.EqualTo(0f).Within(Tol), "the part does not reach the plate: it stays 0, update " + (i + 1));
            }
        }

        [Test]
        public void AFadingLabelMeetsAPartThatGrowsTheRect()
        {
            Badge(out var r0, out var r1, out var grown, out _);
            var view = Props(Body("Ash", 100, 100));
            var plate = MeasureProp(view, "Ash");
            _props.LocalCluster = P.TryLocalClusterRect;
            PlaceFighter(r0, grown, plate, .12f, false);
            Up(view, first: true);
            Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Ash"), Is.EqualTo(1f).Within(Tol), "set-up: the plate born at gap .12 from the edge E of R0 is clear (1)");
            PlaceFighter(r0, grown, plate, .12f, true);
            Assert.That(P.TryLocalClusterRect(out var withBadge), Is.True, "the badge fighter is presented");
            Assert.That(Growth(r0, withBadge, grown), Is.GreaterThanOrEqualTo(.1f), "set-up: the edge E grew by at least .1 under the plate");
            float[] want = { .667f, .333f, 0f };
            for (int i = 0; i < want.Length; i++)
            {
                Up(view);
                Assert.That(TextAlpha(_propsRoot.transform, PropLabel, "Ash"), Is.EqualTo(want[i]).Within(Tol), "the grown rectangle meets the plate: alpha " + want[i] + ", update " + (i + 1));
            }
        }
    }
}
