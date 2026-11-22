using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-ALPHA-CLIENT-1: the reconnect budget (the server holds a seat for 60 s after a 30 s idle timeout) and the connect
    /// timeout. These tests use the Session's default options on purpose: the defaults are the behaviour.
    /// </summary>
    public class SessionResilienceTests
    {
        double _now;
        FakeTransport _t;
        Session _s;
        static readonly byte[] Token = Fill(0xAB);
        static byte[] Fill(byte v) { var b = new byte[16]; for (int i = 0; i < 16; i++) b[i] = v; return b; }

        [SetUp]
        public void Setup()
        {
            _now = 1000; _t = new FakeTransport();
            _s = new Session(_t, new SessionOptions { Url = "ws://x/ws", Profile = "p", Ticket = new byte[] { 1 }, Now = () => _now });
        }

        static ServerMsg.Welcome Welcome()
        {
            return new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = Fill(1), PlayerId = 3, Session = Token, TickRate = 20, Tick = 1, ServerMs = 50, Map = new MapBlob(4, 4, new byte[16]) };
        }

        void ToMatch() { _s.Connect(); _t.Open(); _t.Deliver(Welcome()); }

        /// <summary>Drops the link, then answers every reconnect attempt with another drop, stepping the clock 50 ms at a time.
        /// Returns each attempt's start time (ms after the first drop); stops at Failed or after limitMs.</summary>
        List<double> RefuseEveryAttempt(double limitMs, out double endedAt) { return RefuseEveryAttempt(limitMs, out endedAt, out _); }
        List<double> RefuseEveryAttempt(double limitMs, out double endedAt, out int maxAfterDrop)
        {
            var starts = new List<double>();
            double t0 = _now;
            _t.Drop("down");
            endedAt = -1;
            maxAfterDrop = _s.ReconnectAttemptMax;
            while (_s.State != SessionState.Failed && _now - t0 <= limitMs)
            {
                _now += 50;
                int c = _t.Connects;
                _s.Update();
                if (_t.Connects > c) { starts.Add(_now - t0); _t.Drop("still down"); }
            }
            if (_s.State == SessionState.Failed) endedAt = _now - t0;
            return starts;
        }

        [Test]
        public void ADroppedSeatIsRetriedForAboutFiftyFiveSecondsThenFailsAsLostConnection()
        {
            ToMatch();
            var starts = RefuseEveryAttempt(80000, out double endedAt);
            Assert.AreEqual(SessionState.Failed, _s.State, "gave up inside 80 s");
            Assert.That(endedAt, Is.InRange(50000, 60000), "ends near the 55 s budget, not the old 15 s");
            Assert.That(starts.Count, Is.InRange(10, 16));
            Assert.AreEqual(FailureKind.LostConnection, _s.FailureKind);
        }

        [Test]
        public void BackoffDoublesFromHalfASecondAndIsCappedAtFiveSeconds()
        {
            ToMatch();
            var starts = RefuseEveryAttempt(80000, out _);
            double[] firstGaps = { 500, 1000, 2000, 4000 };
            double prev = 0;
            for (int i = 0; i < starts.Count; i++)
            {
                double gap = starts[i] - prev; prev = starts[i];
                if (i < firstGaps.Length) Assert.AreEqual(firstGaps[i], gap, "gap " + i);
                Assert.That(gap, Is.LessThanOrEqualTo(5000), "gap " + i + " above the cap");
            }
            Assert.AreEqual(5000, starts[starts.Count - 1] - starts[starts.Count - 2], "settles at the cap");
        }

        [Test]
        public void AttemptNumberAndMaxAreExposedForTheBanner()
        {
            ToMatch();
            _t.Drop("down");
            Assert.AreEqual(SessionState.Reconnecting, _s.State);
            Assert.AreEqual(1, _s.ReconnectAttempt);
            int max = _s.ReconnectAttemptMax;
            Assert.That(max, Is.InRange(10, 16));
            _now += 500; _s.Update(); _t.Drop("down");
            Assert.AreEqual(2, _s.ReconnectAttempt);
            Assert.AreEqual(max, _s.ReconnectAttemptMax, "max is fixed for the drop");
        }

        [Test]
        public void TheAttemptsMadeEqualTheAdvertisedMax()
        {
            ToMatch();
            var starts = RefuseEveryAttempt(80000, out _, out int max);
            Assert.AreEqual(max, starts.Count, "the banner's denominator is the real number of tries");
        }

        [Test]
        public void ASeatThatComesBackAfterThirtySecondsResumes()
        {
            ToMatch();
            double t0 = _now;
            _t.Drop("down");
            while (_now - t0 < 30000)
            {
                _now += 50;
                int c = _t.Connects;
                _s.Update();
                if (_t.Connects > c) _t.Drop("still down");
            }
            Assert.AreEqual(SessionState.Reconnecting, _s.State, "still trying at 30 s");
            while (_t.Connects == 0 || _s.State == SessionState.Reconnecting)
            {
                _now += 50;
                int c = _t.Connects;
                _s.Update();
                if (_t.Connects > c) { _t.Open(); break; }
            }
            CollectionAssert.AreEqual(Token, _t.LastSent<ClientMsg.Hello>().Resume);
            _t.Deliver(Welcome());
            Assert.AreEqual(SessionState.InMatch, _s.State);
            Assert.AreEqual(0, _s.ReconnectAttempt);
        }

        [Test]
        public void AReconnectAttemptThatNeverOpensTimesOutAndTheBudgetStillEnds()
        {
            ToMatch();
            double t0 = _now;
            _t.Drop("down");
            while (_s.State != SessionState.Failed && _now - t0 <= 90000) { _now += 100; _s.Update(); } // attempts start, nothing ever answers
            Assert.AreEqual(SessionState.Failed, _s.State, "a hung attempt must not hang the client forever");
            Assert.That(_now - t0, Is.LessThanOrEqualTo(80000));
            Assert.That(_t.Connects, Is.GreaterThanOrEqualTo(3), "later attempts were made after the hung one timed out");
            Assert.AreEqual(FailureKind.LostConnection, _s.FailureKind);
        }

        [Test]
        public void AConnectThatNeverOpensFailsAfterTenSecondsAndClosesTheSocket()
        {
            _s.Connect();
            _now += 9950; _s.Update();
            Assert.AreEqual(SessionState.Connecting, _s.State, "not yet at 9.95 s");
            _now += 50; _s.Update();
            Assert.AreEqual(SessionState.Failed, _s.State);
            Assert.AreEqual(FailureKind.ConnectTimeout, _s.FailureKind);
            Assert.IsTrue(_t.CloseCalled);
            Assert.That(ConnectionCopy.ForFailure(_s.FailureKind), Does.StartWith("Couldn't reach"));
        }

        [Test]
        public void AHelloThatNeverGetsAWelcomeFailsTenSecondsAfterTheConnectStarted()
        {
            _s.Connect();
            _now += 4000; _s.Update(); _t.Open();
            Assert.AreEqual(SessionState.HelloSent, _s.State);
            _now += 5950; _s.Update();
            Assert.AreEqual(SessionState.HelloSent, _s.State, "9.95 s after Connect");
            _now += 50; _s.Update();
            Assert.AreEqual(SessionState.Failed, _s.State);
            Assert.AreEqual(FailureKind.ConnectTimeout, _s.FailureKind);
        }

        [Test]
        public void AWelcomeStopsTheConnectClockAndAnEstablishedMatchNeverTimesOut()
        {
            _s.Connect(); _now += 9000; _s.Update(); _t.Open(); _t.Deliver(Welcome());
            for (int i = 0; i < 20; i++) { _now += 5000; _s.Update(); }
            Assert.AreEqual(SessionState.InMatch, _s.State);
            Assert.AreEqual(FailureKind.None, _s.FailureKind);
        }

        [Test]
        public void ClosedBeforeWelcomeIsAFailureOfThatKindAndAnEndedMatchIsNotAFailure()
        {
            _s.Connect(); _t.Open(); _t.Drop("refused");
            Assert.AreEqual(SessionState.Failed, _s.State);
            Assert.AreEqual(FailureKind.ClosedBeforeWelcome, _s.FailureKind);
            StringAssert.Contains("refused", _s.Error, "the raw text stays for logs");

            var t2 = new FakeTransport();
            var s2 = new Session(t2, new SessionOptions { Url = "ws://x/ws", Profile = "p", Now = () => _now });
            s2.Connect(); t2.Open(); t2.Deliver(Welcome()); s2.Leave();
            Assert.AreEqual(SessionState.Ended, s2.State);
            Assert.AreEqual(FailureKind.None, s2.FailureKind);
        }
    }
}
