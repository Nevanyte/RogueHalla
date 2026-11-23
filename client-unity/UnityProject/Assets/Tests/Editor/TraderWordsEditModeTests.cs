using System;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using TMPro;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-PLAYER-WORDS-2, trader words: the detail heading names the kind of trade, the status line reads the connection state in words, and the
    /// applied stat lines use the game's stat names. The rig is that of TraderCopyEditModeTests (a trader view bound to a session on a fake
    /// transport that opens at once).
    /// </summary>
    public sealed class TraderWordsEditModeTests
    {
        GameObject _canvas, _host;
        TraderDialogView _view;
        TestTransport _transport;
        Session _session;
        double _now = 1000;

        [SetUp]
        public void SetUp()
        {
            _canvas = new GameObject("Trader words canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            _host = new GameObject("Trader words host");
            _view = _host.AddComponent<TraderDialogView>();
            _view.Initialize(_canvas.transform);
            _transport = new TestTransport();
            _session = new Session(_transport, new SessionOptions { Url = "ws://fixture/ws", Profile = "trader-words", Now = () => _now, ReconnectBaseMs = 500 });
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
        public void TheDetailHeadingNamesTheTrade()
        {
            Deliver(Available(Barter("The Villager"), Stats("The Ledger"))); _view.Refresh();
            Assert.IsTrue(_view.SelectMerchant(0)); _view.Refresh();
            Assert.That(Text("TraderDialog/OfferPanel/DetailHeading"), Is.EqualTo("The Villager · Weapon barter"));
            Assert.IsTrue(_view.SelectMerchant(1)); _view.Refresh();
            Assert.That(Text("TraderDialog/OfferPanel/DetailHeading"), Is.EqualTo("The Ledger · Stat trader"));
        }

        [Test]
        public void TheStatusDefaultReadsInWords()
        {
            var other = new TestTransport();
            var idle = new Session(other, new SessionOptions { Url = "ws://fixture/ws", Profile = "trader-words-2", Now = () => _now });
            try
            {
                Assert.That(idle.State, Is.EqualTo(SessionState.Idle), "set-up: a new session is idle");
                _view.Bind(idle); _view.Refresh();
                Assert.That(Text("TraderDialog/Status"), Is.EqualTo("Not connected"));
                idle.Connect();
                Assert.That(idle.State, Is.EqualTo(SessionState.HelloSent), "set-up: opened, no Welcome");
                _view.Refresh();
                Assert.That(Text("TraderDialog/Status"), Is.EqualTo("Connecting…"));
                idle.Detach();
                Assert.That(idle.State, Is.EqualTo(SessionState.Detached), "set-up: detached");
                _view.Refresh();
                Assert.That(Text("TraderDialog/Status"), Is.EqualTo("Disconnected"));
            }
            finally { _view.Bind(_session); idle.Leave(); }
        }

        [Test]
        public void TheStatLinesUseTheTradeNames()
        {
            var view = Available(Stats("The Ledger"));
            var order = new[] { TradeStat.MaxHp, TradeStat.Ad, TradeStat.Ap, TradeStat.AttackSpeed, TradeStat.MoveSpeed, TradeStat.Armor, TradeStat.Shield };
            var words = new[] { "max HP", "Offense", "Soul Offence", "attack speed", "movement speed", "Defense", "Soul Defense" };
            var deltas = new WireTradeDelta[order.Length];
            for (int i = 0; i < order.Length; i++) deltas[i] = new WireTradeDelta { Stat = order[i], Value = 1 };
            view.Stats = deltas;
            Deliver(view); _view.Refresh();
            Assert.IsTrue(_view.SelectMerchant(0)); _view.Refresh();
            for (int i = 0; i < words.Length; i++)
                Assert.That(Text("TraderDialog/OfferPanel/ChoiceScroll/Viewport/Content/AppliedStat_" + i), Is.EqualTo(words[i] + "  1"), "stat line " + i);
        }

        string Text(string path) => _canvas.transform.Find(path).GetComponent<TMP_Text>().text;

        void Deliver(WireTradeView view) { _transport.Deliver(new ServerMsg.State { Tick = 1, Items = new StateItem[] { new TradeState { Available = true, Value = view } } }); }

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
