using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Roguehalla.Client;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using TMPro;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>The trader window's player-facing copy (P-UI-TRADER-COPY-1): traders, not the server; buttons carry the offer alone.</summary>
    public sealed class TraderCopyEditModeTests
    {
        GameObject _canvas, _host;
        TraderDialogView _view;
        TestTransport _transport;
        Session _session;
        double _now = 1000;

        [SetUp]
        public void SetUp()
        {
            _canvas = new GameObject("Trader copy canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            _host = new GameObject("Trader copy host");
            _view = _host.AddComponent<TraderDialogView>();
            _view.Initialize(_canvas.transform);
            _transport = new TestTransport();
            _session = new Session(_transport, new SessionOptions { Url = "ws://fixture/ws", Profile = "trader-copy", Now = () => _now, ReconnectBaseMs = 500 });
            _session.Connect();
            _transport.Deliver(Welcome());
            _view.Bind(_session);
            _view.SetVisible(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_session != null) _session.Leave();
            UnityEngine.Object.DestroyImmediate(_host);
            UnityEngine.Object.DestroyImmediate(_canvas);
        }

        [Test]
        public void TheStatusLineOfEachCase()
        {
            Assert.AreEqual(SessionState.InMatch, _session.State, "set-up");
            Assert.AreEqual("Looking for traders…", Status(), "no trade view yet");

            Deliver(new TradeState { Available = false, Value = null }); _view.Refresh();
            Assert.AreEqual("Traders are unavailable in this match.", Status(), "view not available");

            Deliver(Available()); _view.Refresh();
            Assert.AreEqual("No trader in sight.", Status(), "no merchants");

            Deliver(Available(Barter("The Villager"))); _view.Refresh();
            Assert.AreEqual("1 trader in sight", Status(), "one trader");
            Deliver(Available(Barter("The Villager"), Stats("The Ledger"))); _view.Refresh();
            Assert.AreEqual("2 traders in sight", Status(), "two traders");

            Assert.IsTrue(_view.SelectMerchant(0)); _view.Refresh();
            Assert.IsTrue(_view.TryTrade(TradeKind.Barter, 0));
            Assert.AreEqual("Trading…", Status(), "a trade is pending");

            var used = Available(Barter("The Villager"), Stats("The Ledger")); used.TradeUsed = true;
            Deliver(used); _view.Refresh();
            Assert.IsTrue(_view.SelectMerchant(1)); _view.Refresh();
            Assert.AreEqual("Stat trade used this run.", Status(), "stat used, stat trader selected");
            Assert.IsTrue(_view.SelectMerchant(0)); _view.Refresh();
            Assert.AreEqual("Stat trade used this run. Barter is still open.", Status(), "stat used, barter trader selected");
        }

        [Test]
        public void ASessionNotInAMatchShowsTheStateCopyAlone()
        {
            Deliver(Available(Barter("The Villager"))); _view.Refresh();
            _transport.Drop(); _view.Refresh();
            Assert.AreEqual(SessionState.Reconnecting, _session.State, "set-up");
            Assert.AreEqual("Connection interrupted · resuming", Status());

            var other = new TestTransport();
            var ended = new Session(other, new SessionOptions { Url = "ws://fixture/ws", Profile = "trader-copy-2", Now = () => _now });
            try
            {
                ended.Connect(); other.Deliver(Welcome());
                other.Deliver(new ServerMsg.State { Tick = 1, Items = new StateItem[] { new TradeState { Available = true, Value = Available(Barter("The Villager")) } } });
                _view.Bind(ended); _view.Refresh();
                Assert.AreEqual("1 trader in sight", Status(), "set-up: in a match");
                other.Deliver(new ServerMsg.Events { Tick = 2, List = new List<WireEvent> { new WireEvent { Kind = EventKind.MatchEnded } } });
                Assert.AreEqual(SessionState.Ended, ended.State, "set-up");
                _view.Refresh();
                Assert.AreEqual("Match ended", Status(), "whatever the view says");
            }
            finally { _view.Bind(_session); ended.Leave(); }
        }

        [Test]
        public void NoPlayerFacingTextMentionsTheServer()
        {
            var seen = new List<string>();
            void Check(string state)
            {
                foreach (var t in _canvas.transform.Find("TraderDialog").GetComponentsInChildren<TMP_Text>(false))
                    foreach (var banned in new[] { "server", "authoritative", "delivered", "session" })
                        if ((t.text ?? "").IndexOf(banned, StringComparison.OrdinalIgnoreCase) >= 0)
                            seen.Add(state + ": " + t.gameObject.name + " \"" + t.text + "\"");
            }
            Check("no view");
            Deliver(new TradeState { Available = false, Value = null }); _view.Refresh(); Check("unavailable");
            Deliver(Available()); _view.Refresh(); Check("no merchants");
            var both = Available(Barter("The Villager"), Stats("The Ledger"));
            both.Offers = new[] { new WireTradeOffer { Cost = TradeStat.Ad, CostValue = .1, Gain = TradeStat.Ap, GainValue = .2, Label = "One" } };
            Deliver(both); _view.Refresh();
            Assert.IsTrue(_view.SelectMerchant(0)); _view.Refresh(); Check("barter selected");
            Assert.IsTrue(_view.SelectMerchant(1)); _view.Refresh(); Check("stat selected");
            Assert.IsTrue(_view.TryTrade(TradeKind.Stats, 0)); Check("trade sent");
            var noOffers = Available(Stats("The Ledger"));
            Deliver(noOffers); _view.Refresh(); Check("stat trader without offers");
            _transport.Drop(); _view.Refresh(); Check("reconnecting");
            _view.Bind(null); Check("no session");
            var other = new TestTransport();
            var ended = new Session(other, new SessionOptions { Url = "ws://fixture/ws", Profile = "trader-copy-3", Now = () => _now });
            try
            {
                ended.Connect(); other.Deliver(Welcome());
                other.Deliver(new ServerMsg.Events { Tick = 2, List = new List<WireEvent> { new WireEvent { Kind = EventKind.MatchEnded } } });
                _view.Bind(ended); _view.Refresh(); Check("ended");
            }
            finally { _view.Bind(_session); ended.Leave(); }
            Assert.That(seen, Is.Empty, "player-facing text that talks about the server");
        }

        [Test]
        public void ChoiceButtonsCarryTheOfferAlone()
        {
            var view = Available(Barter("The Villager"), Stats("The Ledger"));
            view.BarterLabels = new[] { "Small potion for a sword", "Trade your axe", "Leave empty handed" };
            view.Offers = new[]
            {
                new WireTradeOffer { Cost = TradeStat.Ad, CostValue = .1, Gain = TradeStat.Ap, GainValue = .2, Label = "-12% attack speed -> +13.9 Defense" },
                new WireTradeOffer { Cost = TradeStat.Ad, CostValue = .2, Gain = TradeStat.Ap, GainValue = .3, Label = "Second offer" }
            };
            Deliver(view); _view.Refresh();
            Assert.IsTrue(_view.SelectMerchant(0)); _view.Refresh();
            Assert.AreEqual(3, _view.ChoiceCount);
            for (int i = 0; i < 3; i++) Assert.AreEqual(view.BarterLabels[i], ChoiceLabel(i).text, "barter button " + i);
            Assert.IsTrue(_view.SelectMerchant(1)); _view.Refresh();
            Assert.AreEqual(2, _view.ChoiceCount);
            for (int i = 0; i < 2; i++) Assert.AreEqual(view.Offers[i].Label, ChoiceLabel(i).text, "stat button " + i);
        }

        [Test]
        public void SendingATradeSaysTrading()
        {
            Deliver(Available(Barter("The Villager"))); _view.Refresh();
            Assert.AreNotEqual("Trading…", Status(), "set-up: nothing pending yet");
            Assert.IsTrue(_view.TryTrade(TradeKind.Barter, 1));
            Assert.AreEqual("Trading…", Status(), "at once");
            _view.Refresh();
            Assert.AreEqual("Trading…", Status(), "until the next trade view");
            Deliver(Available(Barter("The Villager"))); _view.Refresh();
            Assert.AreEqual("1 trader in sight", Status(), "the next trade view ends it");
        }

        [Test]
        public void TheEmptyStatesSayItOnce()
        {
            AssertEmptyState("Looking for traders…", "no view");
            Deliver(new TradeState { Available = false, Value = null }); _view.Refresh();
            AssertEmptyState("Traders are unavailable in this match.", "unavailable view");
            Deliver(Available()); _view.Refresh();
            AssertEmptyState("No trader in sight.", "no merchants");
        }

        void AssertEmptyState(string centre, string label)
        {
            Assert.AreEqual(centre, Text("TraderDialog/OfferPanel/Empty"), label + ": centre text");
            Assert.AreEqual("Traders", Text("TraderDialog/OfferPanel/DetailHeading"), label + ": detail heading");
            Assert.AreEqual("", Text("TraderDialog/OfferPanel/DetailSummary"), label + ": detail summary");
        }

        string Status() => Text("TraderDialog/Status");
        string Text(string path) => _canvas.transform.Find(path).GetComponent<TMP_Text>().text;
        TMP_Text ChoiceLabel(int index) => _canvas.transform.Find("TraderDialog/OfferPanel/ChoiceScroll/Viewport/Content/Choice_" + index + "/Label").GetComponent<TMP_Text>();

        void Deliver(WireTradeView view) { Deliver(new TradeState { Available = true, Value = view }); }
        void Deliver(TradeState state) { _transport.Deliver(new ServerMsg.State { Tick = 1, Items = new StateItem[] { state } }); }

        static WireMerchant Barter(string name) => new WireMerchant { Kind = TradeKind.Barter, X = 10, Y = 12, Name = name };
        static WireMerchant Stats(string name) => new WireMerchant { Kind = TradeKind.Stats, X = 14, Y = 16, Name = name };
        static WireTradeView Available(params WireMerchant[] merchants)
        {
            return new WireTradeView
            {
                Merchants = merchants ?? Array.Empty<WireMerchant>(), Offers = Array.Empty<WireTradeOffer>(),
                Stats = Array.Empty<WireTradeDelta>(), BarterLabels = new[] { "One", "Two", "Three" }
            };
        }

        static ServerMsg.Welcome Welcome()
        {
            var id = new byte[16]; id[0] = 1;
            return new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = id, Session = new byte[16], PlayerId = 1,
                TickRate = 20, Map = new MapBlob(4, 4, new byte[16]) };
        }

        sealed class TestTransport : ITransport
        {
            public event Action Opened;
            public event Action<byte[]> Received;
            public event Action<string> Closed;
            public void Connect(string url) { Opened?.Invoke(); }
            public void Send(byte[] frame) { }
            public void Close() { }
            public void Poll() { }
            public void Deliver(ServerMsg msg) { Received?.Invoke(msg.Encode()); }
            public void Drop() { Closed?.Invoke("fixture disconnect"); }
        }
    }
}
