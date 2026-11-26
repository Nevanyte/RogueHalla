using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Client;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-TEXT-AWAKE-1: the trader's rows in the order of a real match (the window is built hidden, the trade is delivered and refreshed
    /// while it is hidden, then it is shown). A label that has never been active in the hierarchy has had no Awake and measures one tenth of
    /// its size, so a row must neither be measured nor cached while hidden, and the call that shows the window must make every row right.
    /// "Right" is derived from the awake label (never typed): a row is max(minimum, ceil(preferred height at row width minus 24, plus 16)).
    /// </summary>
    public sealed class TraderRowsRealOrderEditModeTests
    {
        // The view's private consts (TraderDialogView.MerchantRowHeight, ChoiceRowHeight, AppliedStatRowHeight), copied: the rows' creation heights.
        const float MerchantMin = 72f, ChoiceMin = 76f, AppliedMin = 40f;
        // The view's content estimate for rows kept hidden (RebuildChoices): 3 x (ChoiceRowHeight + 8) and 2 x AppliedStatRowHeight + 3 x (ChoiceRowHeight + 8).
        const float BarterEstimate = 252f, StatEstimate = 332f;

        ShowcaseSettings _settings; GameObject _settingsGo; float _savedScale; bool _createdSettings; UiTheme _savedTheme;
        static readonly FieldInfo ScaleField = typeof(ShowcaseSettings).GetField("_textScale", BindingFlags.Instance | BindingFlags.NonPublic);

        [SetUp]
        public void SetUp()
        {
            _savedTheme = UiTheme.Current;
            _settings = ShowcaseSettings.Current;
            if (_settings == null)
            {
                _settingsGo = new GameObject("trader rows settings"); _createdSettings = true;
                _settings = _settingsGo.AddComponent<ShowcaseSettings>();
                typeof(ShowcaseSettings).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_settings, null);
            }
            _savedScale = (float)ScaleField.GetValue(_settings);
        }

        [TearDown]
        public void TearDown()
        {
            if (_settings != null) ScaleField.SetValue(_settings, _savedScale);
            if (_createdSettings) UnityEngine.Object.DestroyImmediate(_settingsGo);
            UiTheme.Current = _savedTheme;
        }

        // ------------------------------------------------------------------------------------------------ 1
        [Test, Combinatorial]
        public void EveryRowIsRightOnTheFirstFrameOfARealMatch([Values("barter", "stat")] string kind, [Values(1f, 1.25f, 1.5f)] float scale, [Values(false, true)] bool narrow)
        {
            bool stat = kind == "stat";
            using (var rig = new Rig(Canvas(narrow), scale))
            {
                rig.Deliver(stat); rig.View.Refresh();
                rig.View.SetVisible(true);
                AssertRowsRight(rig.View, stat);
            }
        }

        // ------------------------------------------------------------------------------------------------ 2
        [Test, Combinatorial]
        public void TheRealOrderEqualsTheControlOrder([Values("barter", "stat")] string kind, [Values(1f, 1.25f, 1.5f)] float scale, [Values(false, true)] bool narrow)
        {
            bool stat = kind == "stat";
            Geometry real;
            using (var rig = new Rig(Canvas(narrow), scale))
            {
                rig.Deliver(stat); rig.View.Refresh();
                rig.View.SetVisible(true);
                real = Geometry.Of(Rows(rig.View, stat), rig.View);
            }
            using (var rig = new Rig(Canvas(narrow), scale))
            {
                rig.View.SetVisible(true);
                rig.Deliver(stat); rig.View.Refresh();
                AssertFormula(rig.View, stat);   // set-up: the control order satisfies the rule
                var control = Geometry.Of(Rows(rig.View, stat), rig.View);
                Assert.That(control.Heights.Length, Is.EqualTo(real.Heights.Length), "the same rows");
                for (int i = 0; i < real.Heights.Length; i++)
                {
                    Assert.That(real.Heights[i], Is.EqualTo(control.Heights[i]).Within(.01f), "row " + i + " height, real order against control order");
                }
                Assert.That(real.Content, Is.EqualTo(control.Content).Within(.01f), "content height, real order against control order");
            }
        }

        // ------------------------------------------------------------------------------------------------ 3
        [Test]
        public void AHiddenWindowMeasuresAndCachesNothing([Values("barter", "stat")] string kind)
        {
            bool stat = kind == "stat";
            using (var rig = new Rig(Canvas(false), 1.5f))
            {
                rig.Deliver(stat); rig.View.Refresh(); rig.LateUpdate(); rig.LateUpdate();
                var rows = Rows(rig.View, stat);
                foreach (var row in rows.All)
                {
                    Assert.That(row.Label.gameObject.activeInHierarchy, Is.False, row.Name + " is built under the hidden window (set-up)");
                    Assert.That(row.Label.isOrthographic, Is.False, row.Name + " has had no Awake (set-up)");
                }
                Assert.That(rig.View.IsVisible, Is.False, "the window is hidden (set-up)");
                Assert.That(Cache(rig.View).Count, Is.EqualTo(0), "no row was measured and cached while hidden");
                Assert.That(ContentRect(rig.View).sizeDelta.y, Is.EqualTo(stat ? StatEstimate : BarterEstimate).Within(.01f), "the content keeps the rebuild's estimate");
                foreach (var row in rows.All)
                {
                    Assert.That(row.Rect.sizeDelta.y, Is.EqualTo(row.Min).Within(.01f), row.Name + " keeps its creation height while hidden");
                }
                foreach (var row in rows.Merchants) row.Rect.sizeDelta = new Vector2(0, 55f);
                rig.LateUpdate();
                foreach (var row in rows.Merchants)
                {
                    Assert.That(row.Rect.sizeDelta.y, Is.EqualTo(55f).Within(.01f), row.Name + ": a hidden LateUpdate writes no rect");
                }
                foreach (var row in rows.Merchants) row.Rect.sizeDelta = new Vector2(0, MerchantMin);
                rig.View.SetVisible(true);
                Assert.That(Cache(rig.View).Count, Is.EqualTo(rows.All.Count), "showing the window measures every row");
            }
        }

        // ------------------------------------------------------------------------------------------------ 4
        [Test]
        public void LaterCallsChangeNoRowAndMeasureNothingAgain([Values("barter", "stat")] string kind)
        {
            bool stat = kind == "stat";
            using (var rig = new Rig(Canvas(false), 1.5f))
            {
                rig.Deliver(stat); rig.View.Refresh();
                rig.View.SetVisible(true);
                AssertRowsRight(rig.View, stat);
                var before = Entries(rig.View);
                var geometry = Geometry.Of(Rows(rig.View, stat), rig.View);
                Assert.That(before.Count, Is.EqualTo(geometry.Heights.Length), "every row has a cache entry (set-up)");

                rig.LateUpdate();
                AssertUnchanged(rig, stat, before, geometry, "LateUpdate");
                rig.View.Refresh();
                AssertUnchanged(rig, stat, before, geometry, "Refresh");
                rig.Deliver(stat); rig.View.Refresh();
                AssertUnchanged(rig, stat, before, geometry, "the same trade in a new view object, then Refresh");
                rig.View.SetVisible(false); rig.View.SetVisible(true);
                AssertUnchanged(rig, stat, before, geometry, "SetVisible(false) then SetVisible(true)");
                rig.LateUpdate();
                AssertUnchanged(rig, stat, before, geometry, "LateUpdate after the reopening");
            }
        }

        static void AssertUnchanged(Rig rig, bool stat, Dictionary<int, object> before, Geometry geometry, string after)
        {
            var now = Entries(rig.View);
            Assert.That(now.Count, Is.EqualTo(before.Count), "after " + after + ": the same number of cache entries");
            foreach (var pair in before)
            {
                object entry = null;
                Assert.That(now.TryGetValue(pair.Key, out entry), Is.True, "after " + after + ": the entry of row " + pair.Key + " is still there");
                Assert.That(ReferenceEquals(entry, pair.Value), Is.True, "after " + after + ": the entry of row " + pair.Key + " is the same object (nothing was measured again)");
            }
            var then = Geometry.Of(Rows(rig.View, stat), rig.View);
            Assert.That(then.Heights.Length, Is.EqualTo(geometry.Heights.Length), "after " + after + ": the same rows");
            for (int i = 0; i < geometry.Heights.Length; i++)
            {
                Assert.That(then.Heights[i], Is.EqualTo(geometry.Heights[i]).Within(.01f), "after " + after + ": row " + i + " height");
                Assert.That(then.Ys[i], Is.EqualTo(geometry.Ys[i]).Within(.01f), "after " + after + ": row " + i + " position");
            }
            Assert.That(then.Content, Is.EqualTo(geometry.Content).Within(.01f), "after " + after + ": content height");
        }

        // ------------------------------------------------------------------------------------------------ 5
        [Test]
        public void AReopenedWindowIsRightAfterARebuildWhileClosed([Values("barter", "stat")] string kind)
        {
            bool stat = kind == "stat";
            using (var rig = new Rig(Canvas(false), 1.5f))
            {
                rig.Deliver(stat); rig.View.Refresh();
                rig.View.SetVisible(true);
                var scroll = rig.View.Root.Find("OfferPanel/ChoiceScroll").GetComponent<UnityEngine.UI.ScrollRect>();
                if (stat)
                {
                    scroll.verticalNormalizedPosition = 1f;
                    float viewport = ((RectTransform)scroll.viewport).rect.height;
                    Assert.That(ContentRect(rig.View).sizeDelta.y, Is.GreaterThan(viewport), "the stat list is taller than its viewport (set-up)");
                    Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(1f).Within(.01f), "the list stands at the top (set-up)");
                }
                rig.View.SetVisible(false);
                var changed = Trade(stat, 0); changed.Offers[0].Label = "−30% movement speed → +99.9 Soul Offence";   // a content change: a moved merchant no longer rebuilds (P-UI-TRADER-STEADY-1)
                rig.T.Deliver(new ServerMsg.State { Tick = 99, Items = new StateItem[] { new TradeState { Available = true, Value = changed } } });
                rig.View.Refresh(); rig.LateUpdate();
                rig.View.SetVisible(true);
                AssertFormula(rig.View, stat);
                if (stat)
                {
                    Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(1f).Within(.01f), "the list reopens at the top, not at the bottom");
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ 6
        [Test]
        public void ATextSizeChangedWhileHiddenIsRightAtTheFirstOpen()
        {
            using (var rig = new Rig(Canvas(false), 1.25f))
            {
                rig.Deliver(false); rig.View.Refresh();
                rig.SetScale(1.5f);
                rig.LateUpdate(); rig.LateUpdate();
                rig.View.SetVisible(true);
                AssertRowsRight(rig.View, false);
            }
        }

        // ------------------------------------------------------------------------------------------------ 8
        [Test]
        public void FitHeightOnAHiddenRowReturnsTheMinimumAndCachesNothing()
        {
            using (var rig = new Rig(Canvas(false), 1.5f))
            {
                rig.Deliver(false); rig.View.Refresh();
                var row = rig.View.Root.Find("MerchantPanel/MerchantList/Merchant_0");
                var label = row.GetComponentInChildren<TMP_Text>(true);
                Assert.That(label.gameObject.activeInHierarchy, Is.False, "the row's label is not active in the hierarchy (set-up)");
                Assert.That(label.isOrthographic, Is.False, "the row's label has had no Awake (set-up)");
                Cache(rig.View).Clear();
                var fit = typeof(TraderDialogView).GetMethod("FitHeight", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(fit, Is.Not.Null, "FitHeight(GameObject, TMP_Text, RectTransform, float, bool) exists");
                float result = (float)fit.Invoke(rig.View, new object[] { row.gameObject, label, (RectTransform)row, MerchantMin, false });
                Assert.That(result, Is.EqualTo(MerchantMin).Within(.01f), "a row that cannot be measured keeps its minimum");
                Assert.That(Cache(rig.View).Count, Is.EqualTo(0), "and stores nothing");
            }
        }

        // ================================================================================================ rules, derived from the awake label
        static Vector2 Canvas(bool narrow) { return narrow ? new Vector2(980f, 2121f) : new Vector2(1920f, 1080f); }

        /// <summary>max(minimum, ceil(preferred height at the row width minus 24, plus 16)): the rule of FitHeight.</summary>
        static float Need(RowInfo row)
        {
            float preferred = row.Label.GetPreferredValues(row.Label.text, Mathf.Max(1f, row.Rect.rect.width - 24f), Mathf.Infinity).y;
            return Mathf.Max(row.Min, Mathf.Ceil(preferred + 16f));
        }

        /// <summary>What the text needs at the width it is drawn in.</summary>
        static float Seen(RowInfo row)
        {
            return row.Label.GetPreferredValues(row.Label.text, row.Label.rectTransform.rect.width, Mathf.Infinity).y;
        }

        static void AssertAwake(RowInfo row)
        {
            Assert.That(row.Label.gameObject.activeInHierarchy, Is.True, row.Name + "'s label is active in the hierarchy, so its measures are true");
            Assert.That(row.Label.isOrthographic, Is.True, row.Name + "'s label has woken");
        }

        /// <summary>Set-up (counts, awake) and the rule: each row is at least as tall as its text needs.</summary>
        static void AssertFormula(TraderDialogView view, bool stat)
        {
            AssertCounts(view, stat);
            var rows = Rows(view, stat);
            foreach (var row in rows.All) AssertAwake(row);
            foreach (var row in rows.All)
            {
                float need = Need(row);
                Assert.That(row.Rect.rect.height, Is.GreaterThanOrEqualTo(need - .01f), row.Name + " is as tall as its text needs (" + need + ")");
            }
        }

        internal static void AssertRowsRight(TraderDialogView view, bool stat)
        {
            AssertFormula(view, stat);
            var rows = Rows(view, stat);
            foreach (var row in rows.All)
            {
                if (row.Applied) continue;
                Assert.That(Seen(row), Is.LessThanOrEqualTo(row.Rect.rect.height + .01f), row.Name + ": its text does not stand out of its button");
            }
            // Layout: merchants stack 8 apart; applied rows 4 apart, choice rows 8 apart; the content is the heights and gaps minus 4.
            float y = 0;
            foreach (var row in rows.Merchants)
            {
                Assert.That(row.Rect.anchoredPosition.y, Is.EqualTo(-y).Within(.01f), row.Name + " position");
                y += row.Rect.rect.height + 8f;
            }
            y = 0;
            foreach (var row in rows.Content)
            {
                Assert.That(row.Rect.anchoredPosition.y, Is.EqualTo(-y).Within(.01f), row.Name + " position");
                y += row.Rect.rect.height + (row.Applied ? 4f : 8f);
            }
            Assert.That(ContentRect(view).sizeDelta.y, Is.EqualTo(y - 4f).Within(.01f), "the content is the heights and gaps minus 4");
        }

        static void AssertCounts(TraderDialogView view, bool stat)
        {
            Assert.That(view.IsVisible, Is.True, "the window is shown (set-up)");
            Assert.That(view.MerchantCount, Is.EqualTo(2), "two merchants (set-up)");
            Assert.That(view.ChoiceCount, Is.EqualTo(3), "three choices (set-up)");
            Assert.That(view.Root.Find("OfferPanel/ChoiceScroll/Viewport/Content/AppliedStat_1") != null, Is.EqualTo(stat), "two applied stat rows for the stat trader only (set-up)");
            Assert.That(view.Root.Find("MerchantPanel/MerchantList/Merchant_0").GetComponentInChildren<TMP_Text>(true).text, Does.Contain("\n"), "a merchant label has two lines (set-up)");
        }

        // ================================================================================================ rows and the cache
        sealed class RowInfo { public string Name; public RectTransform Rect; public TMP_Text Label; public float Min; public bool Applied; }

        sealed class RowSet
        {
            public readonly List<RowInfo> Merchants = new List<RowInfo>(), Content = new List<RowInfo>(), All = new List<RowInfo>();
        }

        static RectTransform ContentRect(TraderDialogView view)
        {
            return (RectTransform)view.Root.Find("OfferPanel/ChoiceScroll/Viewport/Content");
        }

        /// <summary>The rows in the order the view stacks them: merchants; then, in the content, the applied stat rows and the choices.</summary>
        static RowSet Rows(TraderDialogView view, bool stat)
        {
            var set = new RowSet();
            var list = view.Root.Find("MerchantPanel/MerchantList");
            for (int i = 0; i < 2; i++) Add(set, set.Merchants, list.Find("Merchant_" + i), MerchantMin, false);
            var content = ContentRect(view);
            if (stat) for (int i = 0; i < 2; i++) Add(set, set.Content, content.Find("AppliedStat_" + i), AppliedMin, true);
            for (int i = 0; i < 3; i++) Add(set, set.Content, content.Find("Choice_" + i), ChoiceMin, false);
            return set;
        }

        static void Add(RowSet set, List<RowInfo> into, Transform t, float min, bool applied)
        {
            var info = new RowInfo { Name = t.name, Rect = (RectTransform)t, Label = t.GetComponentInChildren<TMP_Text>(true), Min = min, Applied = applied };
            into.Add(info); set.All.Add(info);
        }

        sealed class Geometry
        {
            public float[] Heights, Ys; public float Content;
            public static Geometry Of(RowSet rows, TraderDialogView view)
            {
                var g = new Geometry { Heights = new float[rows.All.Count], Ys = new float[rows.All.Count], Content = ContentRect(view).sizeDelta.y };
                for (int i = 0; i < rows.All.Count; i++) { g.Heights[i] = rows.All[i].Rect.rect.height; g.Ys[i] = rows.All[i].Rect.anchoredPosition.y; }
                return g;
            }
        }

        static readonly FieldInfo CacheField = typeof(TraderDialogView).GetField("_fitCache", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>The view's private row-measure cache (FitCache is a private class: read through IDictionary).</summary>
        static IDictionary Cache(TraderDialogView view) { return (IDictionary)CacheField.GetValue(view); }

        static Dictionary<int, object> Entries(TraderDialogView view)
        {
            var copy = new Dictionary<int, object>();
            foreach (DictionaryEntry e in Cache(view)) copy[(int)e.Key] = e.Value;
            return copy;
        }

        // ================================================================================================ the rig: a hidden window, a real Session
        internal sealed class TestTransport : ITransport
        {
            public event Action Opened;
            public event Action<byte[]> Received;
            public event Action<string> Closed;
            public readonly List<byte[]> Sent = new List<byte[]>();
            public void Connect(string url) { Opened?.Invoke(); }
            public void Send(byte[] frame) { Sent.Add(frame); }
            public void Close() { }
            public void Poll() { }
            public void Deliver(ServerMsg msg) { Received?.Invoke(msg.Encode()); }
        }

        /// <summary>The trade of the two cases. barter: merchant 0 is a barter merchant, three different labels; stat: merchant 0 is a stat trader.</summary>
        internal static WireTradeView Trade(bool stat, double dx)
        {
            var view = new WireTradeView
            {
                Merchants = stat
                    ? new[] { new WireMerchant { Kind = TradeKind.Stats, X = 10 + dx, Y = 12, Name = "Soul Broker" }, new WireMerchant { Kind = TradeKind.Barter, X = 14, Y = 16, Name = "Wandering Villager" } }
                    : new[] { new WireMerchant { Kind = TradeKind.Barter, X = 10 + dx, Y = 12, Name = "Wandering Villager" }, new WireMerchant { Kind = TradeKind.Stats, X = 14, Y = 16, Name = "Soul Broker" } },
                Offers = new[]
                {
                    new WireTradeOffer { Cost = TradeStat.Ad, CostValue = .1, Gain = TradeStat.Ap, GainValue = .2, Label = "−24% movement speed → +150.4 Soul Offence" },
                    new WireTradeOffer { Cost = TradeStat.Ad, CostValue = .2, Gain = TradeStat.Ap, GainValue = .3, Label = "−24% movement speed → +150.4 Soul Offence" },
                    new WireTradeOffer { Cost = TradeStat.Ad, CostValue = .3, Gain = TradeStat.Ap, GainValue = .4, Label = "−24% movement speed → +150.4 Soul Offence" },
                },
                Stats = stat ? new[] { new WireTradeDelta { Stat = TradeStat.Ad, Value = -1.25 }, new WireTradeDelta { Stat = TradeStat.MaxHp, Value = -12 } } : new WireTradeDelta[0],
                BarterLabels = new[] { "Give held weapon → Sword", "Give held weapon → small healing potion", "Give a small healing potion → Bow" },
            };
            return view;
        }

        sealed class Rig : IDisposable
        {
            public readonly GameObject CanvasGo, Host; public readonly TraderDialogView View; public readonly TestTransport T; public readonly Session S;
            readonly float _savedScale; int _tick = 1;
            static readonly MethodInfo Late = typeof(TraderDialogView).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

            /// <summary>A WorldSpace canvas of a fixed size; the text scale is set before anything is built.</summary>
            public Rig(Vector2 size, float scale)
            {
                _savedScale = (float)ScaleField.GetValue(ShowcaseSettings.Current);
                ScaleField.SetValue(ShowcaseSettings.Current, scale);
                CanvasGo = new GameObject("Trader canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
                CanvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                ((RectTransform)CanvasGo.transform).sizeDelta = size;
                Host = new GameObject("Trader view host");
                View = Host.AddComponent<TraderDialogView>();
                View.Initialize(CanvasGo.transform);
                T = new TestTransport();
                S = new Session(T, new SessionOptions { Url = "ws://fixture/ws", Profile = "trader-test", Now = () => 1000, ReconnectBaseMs = 500 });
                S.Connect();
                var id = new byte[16]; id[0] = 1;
                T.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = id, Session = new byte[16], PlayerId = 1, TickRate = 20, Map = new MapBlob(4, 4, new byte[16]) });
                T.Sent.Clear();
                View.Bind(S);
            }

            /// <summary>Sets the scale as the settings screen does: the field, then every label under the canvas.</summary>
            public void SetScale(float scale)
            {
                ScaleField.SetValue(ShowcaseSettings.Current, scale);
                MythicUi.ApplyTextScale(CanvasGo.transform, scale);
            }

            /// <summary>One frame of the view (its LateUpdate never runs by itself in EditMode).</summary>
            public void LateUpdate() { Late.Invoke(View, null); }

            /// <summary>Delivers a trade in a NEW view object each call, as every state frame does.</summary>
            public void Deliver(bool stat, double dx = 0)
            {
                T.Deliver(new ServerMsg.State { Tick = (uint)(++_tick), Items = new StateItem[] { new TradeState { Available = true, Value = Trade(stat, dx) } } });
            }

            public void Dispose()
            {
                if (S != null) S.Leave();
                UnityEngine.Object.DestroyImmediate(Host); UnityEngine.Object.DestroyImmediate(CanvasGo);
                ScaleField.SetValue(ShowcaseSettings.Current, _savedScale);
            }
        }
    }

    /// <summary>P-UI-TEXT-AWAKE-1, test 7: the whole HUD and the real call chain (a delivery reaches MatchHud, which refreshes the hidden window).</summary>
    public sealed class TraderRowsWholeHudEditModeTests
    {
        sealed class HudTransport : ITransport
        {
            public event Action Opened; public event Action<byte[]> Received; public event Action<string> Closed;
            public void Connect(string url) { } public void Send(byte[] frame) { } public void Close() { } public void Poll() { }
            public void Open() { Opened?.Invoke(); } public void Deliver(ServerMsg message) { Received?.Invoke(message.Encode()); }
        }

        static readonly FieldInfo ScaleField = typeof(ShowcaseSettings).GetField("_textScale", BindingFlags.Instance | BindingFlags.NonPublic);
        GameObject _root, _events, _settingsGo; EventSystem _priorEvents; ShowcaseSettings _settings; float _savedScale; bool _createdSettings; UiTheme _savedTheme;

        [SetUp]
        public void SetUp()
        {
            _savedTheme = UiTheme.Current;
            _settings = ShowcaseSettings.Current;
            if (_settings == null)
            {
                _settingsGo = new GameObject("trader hud settings"); _createdSettings = true;
                _settings = _settingsGo.AddComponent<ShowcaseSettings>();
                typeof(ShowcaseSettings).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_settings, null);
            }
            _savedScale = (float)ScaleField.GetValue(_settings);
            _priorEvents = EventSystem.current;
            _events = new GameObject("Trader hud events", typeof(UiInputRoutingEventSystem));
            var system = _events.GetComponent<UiInputRoutingEventSystem>(); system.enabled = false; system.enabled = true;
            EventSystem.current = system;
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
            if (_events != null) UnityEngine.Object.DestroyImmediate(_events);
            if (_priorEvents != null) EventSystem.current = _priorEvents;
            if (_settings != null) ScaleField.SetValue(_settings, _savedScale);
            if (_createdSettings) UnityEngine.Object.DestroyImmediate(_settingsGo);
            UiTheme.Current = _savedTheme;
        }

        [Test]
        public void TheWholeHudRealChain([Values("barter", "stat")] string kind)
        {
            bool stat = kind == "stat";
            ScaleField.SetValue(_settings, 1.5f);   // before the HUD is built
            _root = new GameObject("Trader hud fixture");
            var client = _root.AddComponent<MatchClient>(); client.enabled = false;
            var transport = new HudTransport();
            var session = new Session(transport, new SessionOptions { Url = "ws://flows/ws", Profile = "flows", Now = () => 1000 });
            typeof(MatchClient).GetMethod("InitializeLayoutReview", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(client, new object[] { session });
            var hud = client.Hud;
            session.Connect(); transport.Open();
            transport.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16], Session = new byte[16], PlayerId = 1, TickRate = 20, ServerMs = 50, Map = new MapBlob(32, 32, new byte[1024]) });
            transport.Deliver(new ServerMsg.Snapshot { Tick = 1, ServerTimeOverrideMs = 50, Phase = 1, Me = new WireSelf { Id = 1, X = 64, Y = 64, Hp = 100, MaxHp = 100, Status = 1, Weapon = 0 } });
            // The HUD refreshes the hidden window itself on this delivery; the test calls no Refresh.
            transport.Deliver(new ServerMsg.State { Tick = 2, Items = new StateItem[] { new TradeState { Available = true, Value = TraderRowsRealOrderEditModeTests.Trade(stat, 0) } } });
            Assert.That(hud.TraderDialog.IsVisible, Is.False, "the window is hidden when the trade arrives (set-up)");
            hud.ShowPlayerFlow(1);
            Assert.That(hud.TraderDialog.IsVisible, Is.True, "the trader window is shown (set-up)");
            TraderRowsRealOrderEditModeTests.AssertRowsRight(hud.TraderDialog, stat);
        }
    }
}
