using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Client;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-TRADER-STEADY-1 oracle: a trader's step no longer rebuilds the Traders window. The server sends a new Trade view on every
    /// tick while a merchant in sight walks; a delivery that differs from the last built view only in merchants' positions must keep
    /// every row, focus, hover and scroll place, create and destroy no Unity object, and allocate nothing in Refresh. Every other
    /// change still rebuilds, as today. The rig is that of <c>TraderRowsRealOrderEditModeTests</c> (a real Session, a new view object
    /// for each delivery, the view's Refresh called by the test as the HUD does on a collections change).
    /// </summary>
    public sealed class TraderSteadyEditModeTests
    {
        // Bound of Unity object ids created by 100 moved-only deliveries after the fix (see the packet, "Notes from the oracle author").
        const int CreatedBound = 5;
        // Bound of GC.Alloc samples one moved-only Refresh may add to the delivery's own (after the fix: none).
        const int RefreshSampleBound = 0;

        static readonly FieldInfo ScaleField = typeof(ShowcaseSettings).GetField("_textScale", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly MethodInfo Late = typeof(TraderDialogView).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly Vector2 Wide = new Vector2(1920f, 1080f);
        static readonly Vector2 Short = new Vector2(1920f, 1000f);

        ShowcaseSettings _settings; GameObject _settingsGo; float _savedScale; bool _createdSettings; UiTheme _savedTheme;
        GameObject _eventsGo; EventSystem _priorEvents;

        [SetUp]
        public void SetUp()
        {
            _savedTheme = UiTheme.Current;
            _settings = ShowcaseSettings.Current;
            if (_settings == null)
            {
                _settingsGo = new GameObject("trader steady settings"); _createdSettings = true;
                _settings = _settingsGo.AddComponent<ShowcaseSettings>();
                typeof(ShowcaseSettings).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_settings, null);
            }
            _savedScale = (float)ScaleField.GetValue(_settings);
            _priorEvents = EventSystem.current;
        }

        [TearDown]
        public void TearDown()
        {
            if (_eventsGo != null) UnityEngine.Object.DestroyImmediate(_eventsGo);
            if (_priorEvents != null) EventSystem.current = _priorEvents;
            if (_settings != null) ScaleField.SetValue(_settings, _savedScale);
            if (_createdSettings) UnityEngine.Object.DestroyImmediate(_settingsGo);
            UiTheme.Current = _savedTheme;
        }

        // ------------------------------------------------------------------------------------------------ 1
        /// <summary>The proof of the cause. 100 deliveries, each moving a merchant by .5 and nothing else, with the window closed.</summary>
        [Test]
        public void AWalkingTraderRebuildsNoRow([Values("barter", "stat")] string kind)
        {
            bool stat = kind == "stat";
            using (var rig = new Rig(Wide, 1f))
            {
                rig.Deliver(TraderRowsRealOrderEditModeTests.Trade(stat, 0)); rig.View.Refresh();
                var names = RowNames(stat);
                var rows = names.Select(n => Row(rig.View, n)).ToList();
                Assert.That(rows.All(r => r != null), Is.True, "set-up: every row of the first delivery exists (" + string.Join(",", names) + ")");
                int merchantChildren = MerchantList(rig.View).childCount, contentChildren = Content(rig.View).childCount;
                Assert.That(merchantChildren, Is.EqualTo(2), "set-up: two merchant rows");
                Assert.That(contentChildren, Is.EqualTo(stat ? 5 : 3), "set-up: the content holds " + (stat ? "two applied stat rows and three choices" : "three choices"));
                int step = IdStep();
                int before = MeshId();

                for (int i = 1; i <= 100; i++) { rig.Deliver(TraderRowsRealOrderEditModeTests.Trade(stat, .5 * i)); rig.View.Refresh(); }

                int after = MeshId();
                for (int i = 0; i < names.Count; i++)
                {
                    Assert.That(rows[i] != null, Is.True, names[i] + " is not destroyed by 100 moved-only deliveries");
                    Assert.AreSame(rows[i], Row(rig.View, names[i]), names[i] + " is the same object after 100 moved-only deliveries");
                }
                Assert.That(MerchantList(rig.View).childCount, Is.EqualTo(merchantChildren), "the merchant list has the same child count");
                Assert.That(Content(rig.View).childCount, Is.EqualTo(contentChildren), "the choice content has the same child count");
                int created = Math.Abs(after - before) / step - 1;
                Assert.That(created, Is.LessThanOrEqualTo(CreatedBound), "Unity objects created by 100 moved-only deliveries (id step " + step + ", " + Math.Abs(after - before) + " ids between two fresh meshes)");
            }
        }

        // ------------------------------------------------------------------------------------------------ 2
        [Test]
        public void AWalkingTraderKeepsTheOpenWindowsFocusAndPlace()
        {
            _eventsGo = new GameObject("trader steady events", typeof(UiInputRoutingEventSystem));
            var system = _eventsGo.GetComponent<UiInputRoutingEventSystem>(); system.enabled = false; system.enabled = true;   // registers it (EditMode runs no OnEnable by itself)
            EventSystem.current = system;
            using (var rig = new Rig(Short, 1.5f))
            {
                rig.Deliver(LongBarter(0)); rig.View.Refresh();
                rig.View.SetVisible(true);
                Assert.That(rig.View.SelectedMerchantIndex, Is.EqualTo(0), "set-up: the barter merchant is selected");
                Assert.That(rig.View.ChoiceCount, Is.EqualTo(3), "set-up: three choices");
                var scroll = rig.View.Root.Find("OfferPanel/ChoiceScroll").GetComponent<ScrollRect>();
                Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height + .5f), "set-up: the list is taller than its viewport");
                scroll.verticalNormalizedPosition = .4f;
                float placed = scroll.verticalNormalizedPosition;
                Assert.That(placed, Is.EqualTo(.4f).Within(.02f), "set-up: the list is scrolled to the middle");
                var row = Row(rig.View, "Choice_1");
                var label = row.GetComponentInChildren<TMP_Text>(true);
                EventSystem.current.SetSelectedGameObject(row);
                Assert.That(ReferenceEquals(EventSystem.current.currentSelectedGameObject, row), Is.True, "set-up: the choice row has the keyboard focus");

                rig.Deliver(LongBarter(.5)); rig.View.Refresh(); rig.LateUpdate();

                var selected = EventSystem.current.currentSelectedGameObject;
                Assert.That(selected != null, Is.True, "the focused row is still alive after a trader's step");
                Assert.AreSame(row, selected, "the focused row is the same object after a trader's step");
                Assert.AreSame(row, Row(rig.View, "Choice_1"), "the row under its name is the same object");
                Assert.AreSame(label, row == null ? null : row.GetComponentInChildren<TMP_Text>(true), "the row's label text object is the same");
                Assert.That(scroll.verticalNormalizedPosition, Is.EqualTo(placed).Within(1e-4f), "the scroll position is unchanged");
                Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height + .5f), "the list is still taller than its viewport");
            }
        }

        // ------------------------------------------------------------------------------------------------ 3
        /// <summary>A standing guard (green before and after): the rows act through the CURRENT view after a moved-only delivery.</summary>
        [Test]
        public void AMovedMerchantStillSendsThroughTheNewView()
        {
            using (var rig = new Rig(Wide, 1f))
            {
                rig.Deliver(TraderRowsRealOrderEditModeTests.Trade(false, 0)); rig.View.Refresh();
                rig.View.SetVisible(true);
                Assert.That(rig.View.SelectedMerchantIndex, Is.EqualTo(0), "set-up: the barter merchant is selected");
                for (int i = 1; i <= 3; i++) { rig.Deliver(TraderRowsRealOrderEditModeTests.Trade(false, .5 * i)); rig.View.Refresh(); }
                var button = Row(rig.View, "Choice_1").GetComponent<Button>();
                Assert.That(button.interactable, Is.True, "set-up: the choice can be clicked");

                button.onClick.Invoke();
                rig.S.FlushInput();

                var trades = rig.Trades();
                Assert.That(trades.Count, Is.EqualTo(1), "one trade command was sent by the click after a moved merchant");
                Assert.That(trades.Count == 1 && trades[0].TradeKind == TradeKind.Barter && trades[0].Pick == 1, Is.True, "it is the barter choice 1");
                Assert.That(Status(rig.View), Is.EqualTo("Trading…"), "the window says it is trading");
            }
        }

        // ------------------------------------------------------------------------------------------------ 4
        /// <summary>Standing guards (green before and after): every other change of the view still rebuilds every row, as today.</summary>
        [Test, Combinatorial]
        public void EveryOtherChangeStillRebuilds(
            [Values("offerLabel", "merchantName", "merchantAdded", "merchantRemoved", "tradeUsed", "barterReadyAt", "statValue", "barterLabel", "labelAndMoved")] string change,
            [Values(false, true)] bool open)
        {
            using (var rig = new Rig(Wide, 1f))
            {
                // The wire carries at most two merchants: "added" starts from one.
                var first = TraderRowsRealOrderEditModeTests.Trade(true, 0);
                if (change == "merchantAdded") first.Merchants = new[] { first.Merchants[0] };
                rig.Deliver(first); rig.View.Refresh();
                if (open) rig.View.SetVisible(true);
                var names = RowNames(true);
                if (change == "merchantAdded") names.Remove("Merchant_1");
                var old = names.Select(n => Row(rig.View, n)).ToList();
                Assert.That(old.All(r => r != null), Is.True, "set-up: every row exists");

                var next = TraderRowsRealOrderEditModeTests.Trade(true, 0);
                switch (change)
                {
                    case "offerLabel": next.Offers[0].Label = "−30% movement speed → +99.9 Soul Offence"; break;
                    case "merchantName": next.Merchants[0].Name = "Soul Broker the Younger"; break;
                    case "merchantAdded": break;   // the full two-merchant trade, after a one-merchant first delivery
                    case "merchantRemoved": next.Merchants = new[] { next.Merchants[0] }; break;
                    case "tradeUsed": next.TradeUsed = true; break;
                    case "barterReadyAt": next.BarterReadyAt = 5000; break;
                    case "statValue": next.Stats[0].Value = -2.5; break;
                    case "barterLabel": next.BarterLabels[2] = "Give a small healing potion → Axe"; break;
                    case "labelAndMoved": next.Offers[1].Label = "−10% attack speed → +5.1 Defense"; next.Merchants[0].X += .5; break;
                    default: throw new ArgumentException(change);
                }
                rig.Deliver(next); rig.View.Refresh();

                for (int i = 0; i < names.Count; i++)
                {
                    Assert.That(old[i] == null, Is.True, names[i] + " was destroyed by the change '" + change + "'");
                    var now = Row(rig.View, names[i]);
                    if (change == "merchantRemoved" && names[i] == "Merchant_1")
                    {
                        Assert.That(now == null, Is.True, "the removed merchant has no row");
                        continue;
                    }
                    Assert.That(now != null && !ReferenceEquals(now, old[i]), Is.True, names[i] + " is a new object after '" + change + "'");
                }
                switch (change)
                {
                    case "offerLabel": Assert.That(Text(Row(rig.View, "Choice_0")), Does.Contain("99.9"), "the new offer label is shown"); break;
                    case "merchantName": Assert.That(Text(Row(rig.View, "Merchant_0")), Does.Contain("the Younger"), "the new name is shown"); break;
                    case "merchantAdded": Assert.That(rig.View.MerchantCount, Is.EqualTo(2), "the second merchant has a row"); break;
                    case "merchantRemoved": Assert.That(rig.View.MerchantCount, Is.EqualTo(1), "one merchant row is left"); break;
                    case "tradeUsed": Assert.That(Row(rig.View, "Choice_0").GetComponent<Button>().interactable, Is.False, "a used stat trade blocks the choice"); break;
                    case "statValue": Assert.That(Text(Row(rig.View, "AppliedStat_0")), Does.Contain("-2.5"), "the new stat value is shown"); break;
                    case "barterLabel": Assert.That(rig.View.MerchantCount, Is.EqualTo(2), "the merchants are the same two"); break;
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ 5
        /// <summary>Two measures of the same moved-only delivery, with the window closed: the delivery alone (the net library builds a new
        /// TradeView and its lists, which this packet does not own) and the delivery with the view's Refresh. The difference is Refresh.</summary>
        [Test]
        public void AMovedOnlyDeliveryAllocatesNothingInRefresh()
        {
            using (var rig = new Rig(Wide, 1f))
            {
                rig.Deliver(TraderRowsRealOrderEditModeTests.Trade(false, 0)); rig.View.Refresh();
                Assert.That(rig.View.IsVisible, Is.False, "set-up: the window is closed");
                Assert.That(rig.View.MerchantCount, Is.EqualTo(2), "set-up: two merchants (the status says '2 traders in sight')");
                Assert.That(AllocationMeter.Samples(AllocationMeter.AllocatingControl, AllocationMeter.ControlCalls), Is.GreaterThan(0), "the meter sees a known allocation (set-up)");
                Assert.That(AllocationMeter.Samples(AllocationMeter.CleanControl, AllocationMeter.ControlCalls), Is.EqualTo(0), "the meter reads a known clean control as clean (set-up)");

                // The frames are encoded once, before any measure: encoding is the server's work, not the client's.
                var frames = new List<byte[]>();
                for (int i = 1; i <= 1000; i++) frames.Add(rig.Frame(TraderRowsRealOrderEditModeTests.Trade(false, .5 * i)));
                int next = 0; var view = rig.View; var transport = rig.T;
                Action deliveryOnly = () => { transport.DeliverBytes(frames[next++]); };
                Action deliveryAndRefresh = () => { transport.DeliverBytes(frames[next++]); view.Refresh(); };

                int a = AllocationMeter.Samples(deliveryOnly, 50);
                int b = AllocationMeter.Samples(deliveryAndRefresh, 50);

                Assert.That(a, Is.GreaterThan(0), "set-up: the delivery itself allocates (new TradeView), so the meter is awake (a = " + a + ")");
                Assert.That(next, Is.LessThan(frames.Count), "set-up: the frames were enough");
                Assert.That(view.MerchantCount, Is.EqualTo(2), "set-up: still two merchants");
                Assert.That(b - a, Is.LessThanOrEqualTo(RefreshSampleBound), "GC.Alloc samples that Refresh adds to a moved-only delivery (delivery alone a = " + a + ", with Refresh b = " + b + ")");
            }
        }

        // ================================================================================================ helpers
        static List<string> RowNames(bool stat)
        {
            var names = new List<string> { "Merchant_0", "Merchant_1" };
            if (stat) { names.Add("AppliedStat_0"); names.Add("AppliedStat_1"); }
            for (int i = 0; i < 3; i++) names.Add("Choice_" + i);
            return names;
        }

        static Transform MerchantList(TraderDialogView view) { return view.Root.Find("MerchantPanel/MerchantList"); }
        static Transform Content(TraderDialogView view) { return view.Root.Find("OfferPanel/ChoiceScroll/Viewport/Content"); }

        static GameObject Row(TraderDialogView view, string name)
        {
            var t = (name.StartsWith("Merchant_") ? MerchantList(view) : Content(view)).Find(name);
            return t == null ? null : t.gameObject;
        }

        static string Text(GameObject row) { return row.GetComponentInChildren<TMP_Text>(true).text; }
        static string Status(TraderDialogView view) { return view.Root.Find("Status").GetComponent<TMP_Text>().text; }

        /// <summary>A barter trade whose labels are long enough that the list is taller than its viewport at text size 1.5 (as FlowsLeftovers' tests).</summary>
        static WireTradeView LongBarter(double dx)
        {
            var v = TraderRowsRealOrderEditModeTests.Trade(false, dx);
            v.BarterLabels = new[] { Long("weapon", "a finely balanced sword"), Long("potion", "a bow of the northern halls"), Long("coin", "a heavy axe") };
            return v;
        }

        static string Long(string give, string get)
        {
            return "Give the " + give + " you carry to the traveller of the road and receive, in exchange for it, " + get +
                   " that was forged long ago in the halls of the north, kept dry through every winter, and carried here for you";
        }

        /// <summary>The way the recorder counts Unity objects: the instance id of a fresh Mesh (destroyed at once).</summary>
        static int MeshId()
        {
            var mesh = new Mesh(); int id = mesh.GetInstanceID(); UnityEngine.Object.DestroyImmediate(mesh); return id;
        }

        /// <summary>The distance between the ids of two Unity objects created one after the other, measured as the recorder does, with fresh
        /// Meshes (a GameObject takes two objects, itself and its Transform).</summary>
        static int IdStep()
        {
            var a = new Mesh(); var b = new Mesh(); var c = new Mesh();
            int ia = a.GetInstanceID(), ib = b.GetInstanceID(), ic = c.GetInstanceID();
            UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); UnityEngine.Object.DestroyImmediate(c);
            int step = Math.Abs(ib - ia);
            Assert.That(step, Is.GreaterThan(0), "set-up: ids of new objects differ");
            Assert.That(Math.Abs(ic - ib), Is.EqualTo(step), "set-up: ids of new objects step by a constant");
            return step;
        }

        sealed class RawTransport : ITransport
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
            public void DeliverBytes(byte[] frame) { Received?.Invoke(frame); }
        }

        /// <summary>A hidden window on a world-space canvas and a real Session (the rig of TraderRowsRealOrderEditModeTests).</summary>
        sealed class Rig : IDisposable
        {
            public readonly GameObject CanvasGo, Host; public readonly TraderDialogView View; public readonly RawTransport T; public readonly Session S;
            readonly float _savedScale; int _tick = 1;

            public Rig(Vector2 size, float scale)
            {
                _savedScale = (float)ScaleField.GetValue(ShowcaseSettings.Current);
                ScaleField.SetValue(ShowcaseSettings.Current, scale);
                CanvasGo = new GameObject("Trader steady canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                CanvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                ((RectTransform)CanvasGo.transform).sizeDelta = size;
                Host = new GameObject("Trader steady host");
                View = Host.AddComponent<TraderDialogView>();
                View.Initialize(CanvasGo.transform);
                T = new RawTransport();
                S = new Session(T, new SessionOptions { Url = "ws://fixture/ws", Profile = "trader-steady", Now = () => 1000, ReconnectBaseMs = 500 });
                S.Connect();
                var id = new byte[16]; id[0] = 1;
                T.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = id, Session = new byte[16], PlayerId = 1, TickRate = 20, Map = new MapBlob(4, 4, new byte[16]) });
                T.Sent.Clear();
                View.Bind(S);
            }

            public void LateUpdate() { Late.Invoke(View, null); }

            /// <summary>The encoded State message of one trade, with the next tick.</summary>
            public byte[] Frame(WireTradeView view)
            {
                return new ServerMsg.State { Tick = (uint)(++_tick), Items = new StateItem[] { new TradeState { Available = true, Value = view } } }.Encode();
            }

            /// <summary>Delivers a trade in a NEW view object, as every state frame does.</summary>
            public void Deliver(WireTradeView view) { T.DeliverBytes(Frame(view)); }

            /// <summary>The Trade commands sent so far.</summary>
            public List<WireCommand> Trades()
            {
                var list = new List<WireCommand>();
                foreach (var frame in T.Sent)
                {
                    ClientMsg decoded;
                    try { decoded = ClientMsg.Decode(frame); }
                    catch (DecodeException) { continue; }
                    var input = decoded as ClientMsg.Input;
                    if (input == null) continue;
                    foreach (var c in input.Commands) if (c.Kind == CommandKind.Trade) list.Add(c);
                }
                return list;
            }

            public void Dispose()
            {
                if (S != null) S.Leave();
                UnityEngine.Object.DestroyImmediate(Host); UnityEngine.Object.DestroyImmediate(CanvasGo);
                ScaleField.SetValue(ShowcaseSettings.Current, _savedScale);
            }
        }
    }
}
