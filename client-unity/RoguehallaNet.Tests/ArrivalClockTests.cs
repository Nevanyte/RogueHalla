using System;
using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-NET-ARRIVAL-CLOCK-1 oracle: the render clock's jitter window covers 4 s at any tick rate, and the session times a
    /// snapshot at its socket arrival (now - IArrivalTimedTransport.ReceivedAgeMs), not at the frame drain.
    /// </summary>
    public class ArrivalClockTests
    {
        static double Feed(double tickMs, int count, int lateUntil, double lateMs)
        {
            var c = new RenderClock { TickMs = tickMs };
            for (int k = 0; k < count; k++)
            {
                double late = k < lateUntil && k % 5 == 0 ? lateMs : 0;
                c.OnSnapshot(10000 + tickMs * k + late, tickMs * k);
            }
            return c.TargetDelayMs;
        }

        [Test]
        public void JitterWindowCoversFourSecondsAt40Hz()
        {
            // 160 snapshots = 4 s at 25 ms. Late ones (30 ms, every fifth of the first 60) are 2.5 to 4 s old at the end.
            // With 160 samples the 98th percentile index is ceil(160*0.98)-1 = 156, so the 4 largest of 160 are above it:
            // 12 late samples put the percentile at 30 ms of excess; target = TickMs + 30 = 55. A 2 s window (80 samples)
            // holds none of them (k >= 80 is never late) and gives 25.
            Assert.GreaterOrEqual(Feed(25, 160, 60, 30), 25 + 30 - 1, "late arrivals 2.5-4 s ago still count at 40 Hz");
        }

        [Test]
        public void JitterWindowAt20HzIsUnchanged()
        {
            // 80 samples = 4 s at 50 ms. The percentile index is ceil(80*0.98)-1 = 78 (the second largest), and 6 samples
            // (k = 0, 5, .. 25) are late by 30 ms: excess 30, target 80. Green before and after.
            Assert.GreaterOrEqual(Feed(50, 80, 30, 30), 79.0);
        }

        sealed class ArrivalTransport : ITransport, IArrivalTimedTransport
        {
            public event Action Opened;
            public event Action<byte[]> Received;
            public event Action<string> Closed;
            readonly Queue<KeyValuePair<double, byte[]>> _q = new Queue<KeyValuePair<double, byte[]>>();
            public double Clock;
            public double ReceivedAgeMs { get; private set; }
            public void Connect(string url) { }
            public void Send(byte[] frame) { }
            public void Close() { if (Closed != null) Closed("closed"); }
            public void Open() { Opened(); }
            public void Enqueue(double arrival, ServerMsg m) { _q.Enqueue(new KeyValuePair<double, byte[]>(arrival, m.Encode())); }
            public void Poll()
            {
                while (_q.Count != 0 && _q.Peek().Key <= Clock)
                {
                    var one = _q.Dequeue();
                    ReceivedAgeMs = Clock - one.Key;
                    Received(one.Value);
                    ReceivedAgeMs = 0;
                }
            }
        }

        [Test]
        public void SnapshotsAreTimedAtSocketArrivalNotAtTheFrameDrain()
        {
            const double T0 = 1000, Frame = 1000.0 / 60;
            double now = 0;
            var t = new ArrivalTransport();
            var s = new Session(t, new SessionOptions { Url = "ws://x/ws", Profile = "p", Now = () => now });
            s.Connect(); t.Open();
            t.Enqueue(0, new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16], PlayerId = 3, Session = new byte[16], TickRate = 40, Tick = 1, ServerMs = 25, Map = new MapBlob(4, 4, new byte[16]) });
            for (int k = 0; k < 80; k++)
                t.Enqueue(T0 + 25 * k, new ServerMsg.Snapshot { Tick = (uint)(k + 2), Phase = 1 });
            for (int f = 0; now < T0 + 25 * 80 + 100; f++)
            {
                now = f * Frame;
                t.Clock = now;
                s.Poll();
            }
            Assert.AreEqual(SessionState.InMatch, s.State);
            Assert.AreEqual(80, s.SnapshotsReceived, "every snapshot was raised");
            Assert.AreEqual(25.0, s.Clock.TickMs);
            // Regular socket arrival means no jitter: the target sits on the clock's floor (the drain noise is up to one frame).
            double floor = Math.Max(s.Clock.MinDelay - s.Clock.AheadMs, 0);
            Assert.LessOrEqual(s.Clock.TargetDelayMs, floor + 1, "target delay (floor " + floor + ")");
        }
    }
}
