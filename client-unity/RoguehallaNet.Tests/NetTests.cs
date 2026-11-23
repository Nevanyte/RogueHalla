using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Tests
{
    public class SnapshotAndClockTests
    {
        static ServerMsg.Snapshot Snap(double ms, params WireActor[] a) { return new ServerMsg.Snapshot { Tick = (uint)(ms / 50), Actors = a }; }
        static WireActor Act(uint id, float x, float y, float facing = 0) { return new WireActor { Id = id, X = x, Y = y, Facing = facing, HpPct = 100 }; }

        [Test]
        public void InterpolatesBetweenTheTwoSnapshotsAroundRenderTime()
        {
            var b = new SnapshotBuffer();
            b.Add(Snap(1000, Act(1, 0, 10))); b.Add(Snap(1050, Act(1, 100, 30)));
            var o = new List<InterpActor>();
            Assert.IsTrue(b.Sample(1025, o));
            Assert.AreEqual(50f, o[0].X, 1e-3); Assert.AreEqual(20f, o[0].Y, 1e-3);
            b.Sample(1010, o); Assert.AreEqual(20f, o[0].X, 1e-3);
        }

        [Test]
        public void HoldsNewestWhenStarvedAndOldestBeforeTheBuffer()
        {
            var b = new SnapshotBuffer();
            b.Add(Snap(1000, Act(1, 0, 0))); b.Add(Snap(1050, Act(1, 100, 0)));
            var o = new List<InterpActor>();
            b.Sample(5000, o); Assert.AreEqual(100f, o[0].X);
            b.Sample(10, o); Assert.AreEqual(0f, o[0].X);
        }

        [Test]
        public void FacingTakesTheShortWayAndTeleportsAreNotSlid()
        {
            var b = new SnapshotBuffer();
            b.Add(Snap(0, Act(1, 0, 0, 3.0f), Act(2, 0, 0))); b.Add(Snap(50, Act(1, 0, 0, -3.0f), Act(2, 1000, 0), Act(3, 5, 5)));
            var o = new List<InterpActor>();
            b.Sample(25, o);
            Assert.Greater(Math.Abs(o[0].Facing), 3.0f, "went through pi, not through 0");
            Assert.AreEqual(1000f, o[1].X, "teleport shown at once");
            Assert.AreEqual(3, o.Count); Assert.AreEqual(5f, o[2].X, "new actor appears where it is");
        }

        [Test]
        public void IgnoresDuplicateAndReorderedSnapshots()
        {
            var b = new SnapshotBuffer();
            Assert.IsTrue(b.Add(Snap(100))); Assert.IsFalse(b.Add(Snap(100))); Assert.IsFalse(b.Add(Snap(50)));
            Assert.AreEqual(1, b.Count);
        }

        // Snapshots every 50 ms arrive 30 ms late plus up to 40 ms of jitter; frames at 8 ms; a 1 s stall at 5 s.
        static void Drive(RenderClock c, Func<int, double> jitter, double stallFrom, double stallTo, double seconds, Action<double, double> frame)
        {
            var arrivals = new List<(double local, double server)>();
            for (int i = 0; i * 50.0 < seconds * 1000; i++)
            {
                double s = 1000 + i * 50.0, l = s + 30 + jitter(i);
                if (l >= stallFrom && l < stallTo) l = stallTo + (l - stallFrom) * 0.01; // held back, then a burst
                arrivals.Add((l, s));
            }
            arrivals.Sort((a, b) => a.local.CompareTo(b.local));
            int next = 0;
            for (double t = 1000; t < 1000 + seconds * 1000; t += 8)
            {
                while (next < arrivals.Count && arrivals[next].local <= t) { c.OnSnapshot(arrivals[next].local, arrivals[next].server); next++; }
                if (c.Ready) frame(t, c.Update(t));
            }
        }

        [Test]
        public void RenderTimeNeverRunsBackwardsUnderJitterAndAStall()
        {
            var rnd = new Random(7);
            var c = new RenderClock();
            double last = double.NegativeInfinity; int frames = 0;
            Drive(c, i => rnd.NextDouble() * 40, 6000, 7000, 12, (t, r) =>
            {
                Assert.GreaterOrEqual(r, last, "render time went backwards at local " + t);
                last = r; frames++;
                if (t > 3000) Assert.That(c.DelayMs, Is.InRange(RenderClock.MinDelayMs - 1, RenderClock.MaxDelayMs + 1), "delay at " + t);
            });
            Assert.Greater(frames, 1000);
        }

        [Test]
        public void DelayConvergesTowardTheFloorOnAPerfectLinkAndGrowsWithJitter()
        {
            var calm = new RenderClock();
            Drive(calm, i => 0, double.MaxValue, double.MaxValue, 15, (t, r) => { });
            Assert.That(calm.DelayMs, Is.InRange(RenderClock.MinDelayMs, RenderClock.MinDelayMs + 3), "delay " + calm.DelayMs);

            var rnd = new Random(3);
            var noisy = new RenderClock();
            Drive(noisy, i => rnd.NextDouble() * 80, double.MaxValue, double.MaxValue, 15, (t, r) => { });
            Assert.Greater(noisy.TargetDelayMs, 110);
            Assert.LessOrEqual(noisy.TargetDelayMs, RenderClock.MaxDelayMs);
        }


        // Snapshots arrive up to 140 ms late at local 3.0 s (a spike, order kept) which drives the target to the 150 ms ceiling. Returns
        // the ms from the moment the target is back at the floor (the 4 s window has aged the spike out) until the slewing delay
        // is within 15 ms of it: the part of the recovery this change speeds up.
        static double RecoveryMs(double maxCatchUp, out double minDelay, out int reversals, out double peakDelay)
        {
            var c = new RenderClock { MaxCatchUpShare = maxCatchUp };
            double targetLow = -1, recovered = -1, last = double.NegativeInfinity, prev = double.NaN, lo = double.MaxValue, peak = 0; int flips = 0, dir = 0;
            Drive(c, i => i == 40 ? 140 : i == 41 || i == 42 ? 100 : i == 43 ? 60 : i == 44 ? 20 : 0, double.MaxValue, double.MaxValue, 40, (t, r) =>
            {
                Assert.GreaterOrEqual(r, last, "render time went backwards"); last = r;
                if (t > 3000) lo = Math.Min(lo, c.DelayMs);
                peak = Math.Max(peak, c.DelayMs);
                if (peak > 140 && targetLow < 0 && c.TargetDelayMs <= RenderClock.MinDelayMs + 5) targetLow = t;
                if (targetLow >= 0 && recovered < 0 && c.DelayMs <= RenderClock.MinDelayMs + 15) recovered = t - targetLow;
                if (t > 25000 && !double.IsNaN(prev)) { int d = Math.Sign(Math.Round(c.DelayMs - prev, 3)); if (d != 0) { if (dir != 0 && d != dir) flips++; dir = d; } }
                prev = c.DelayMs;
            });
            minDelay = lo; reversals = flips; peakDelay = peak;
            return recovered;
        }

        [Test]
        public void DelayRecoversFasterAfterAStallWithoutDippingBelowTheFloorOrOscillating()
        {
            double minOld, minNew, peakOld, peakNew; int revOld, revNew;
            double oldMs = RecoveryMs(0.01, out minOld, out revOld, out peakOld), newMs = RecoveryMs(0.04, out minNew, out revNew, out peakNew);
            Console.WriteLine("recovery ms: constant 1% = " + oldMs + ", adaptive = " + newMs);
            Assert.Greater(oldMs, 0); Assert.Greater(newMs, 0);
            Assert.Greater(peakNew, 140); Assert.Greater(peakOld, 140);
            Assert.Less(newMs, oldMs * 0.6, "old " + oldMs + " new " + newMs);
            Assert.GreaterOrEqual(minNew, RenderClock.MinDelayMs - 1e-6, "delay below the floor");
            Assert.LessOrEqual(revNew, 0, "delay oscillates on a steady link");
        }

        [Test]
        public void SteadyJitterDoesNotMakeTheDelayOscillate()
        {
            var rnd = new Random(11);
            var c = new RenderClock();
            double prev = double.NaN, last = double.NegativeInfinity; int flips = 0, dir = 0, frames = 0;
            Drive(c, i => rnd.NextDouble() * 30, double.MaxValue, double.MaxValue, 40, (t, r) =>
            {
                Assert.GreaterOrEqual(r, last); last = r;
                Assert.GreaterOrEqual(c.DelayMs, RenderClock.MinDelayMs - 1e-6);
                if (t > 15000) { frames++; if (!double.IsNaN(prev)) { int d = Math.Sign(Math.Round(c.DelayMs - prev, 3)); if (d != 0) { if (dir != 0 && d != dir) flips++; dir = d; } } prev = c.DelayMs; }
            });
            // Arrival-phase noise moves the measured delay by a few ms either way, but never in slews of tens of ms.
            Assert.Less(flips, frames / 20, "flips " + flips + " of " + frames);
        }

        // A frame-driven client stamps each snapshot at the frame that polls it, so arrival jitter includes frame quantization.
        // Plays a link with base latency 30 ms plus `jitterMs` of uniform jitter, polled at `fps`, and reports the settled delay,
        // the most render time ever ran past the newest received sample (ms; > 0 is a stall: remote fighters freeze), and the
        // number of frames that ran past it.
        static void FramePolled(double fps, double jitterMs, int seed, out double settled, out double worstStarveMs, out int starvedFrames)
        {
            var rnd = new Random(seed);
            var c = new RenderClock();
            double frame = 1000.0 / fps, newest = double.NegativeInfinity, worst = 0; int starved = 0, next = 0;
            var arrivals = new List<(double local, double server)>();
            for (int i = 0; i < 20 * 30; i++) { double s = 1000 + i * 50.0; arrivals.Add((s + 30 + rnd.NextDouble() * jitterMs, s)); }
            arrivals.Sort((a, b) => a.local.CompareTo(b.local));
            for (double t = 1000; t < 1000 + 30000; t += frame)
            {
                while (next < arrivals.Count && arrivals[next].local <= t) { c.OnSnapshot(t, arrivals[next].server); newest = Math.Max(newest, arrivals[next].server); next++; }
                if (!c.Ready) continue;
                double r = c.Update(t);
                if (t > 6000 && r > newest) { starved++; worst = Math.Max(worst, r - newest); }
            }
            settled = c.DelayMs; worstStarveMs = worst; starvedFrames = starved;
        }

        [Test]
        public void CalmLinksDrawRemoteFightersCloserToNowWithoutStalling()
        {
            Assert.AreEqual(55, RenderClock.MinDelayMs, "the floor: one 50 ms tick plus 5 ms of margin");
            foreach (var fps in new[] { 60.0, 144.0 })
                foreach (var jitter in new[] { 0.0, 5.0, 15.0, 30.0 })
                {
                    double settled, worst; int starved;
                    FramePolled(fps, jitter, 5, out settled, out worst, out starved);
                    Console.WriteLine("fps " + fps + " jitter " + jitter + ": delay " + settled.ToString("F1") + " ms, worst stall " + worst.ToString("F1") + " ms over " + starved + " frames");
                    Assert.LessOrEqual(starved, 2, "fps " + fps + " jitter " + jitter + ": frames past the newest sample");
                    Assert.Less(worst, 5, "fps " + fps + " jitter " + jitter + ": stall length");
                    if (fps >= 144 && jitter <= 5) Assert.Less(settled, 66, "a calm link at 144 fps should sit near the floor, not at the old 70");
                }
        }

        [Test]
        public void StartsAtAHundredMilliseconds()
        {
            var c = new RenderClock();
            c.OnSnapshot(2000, 1000);
            Assert.AreEqual(100, c.DelayMs, 1e-6);
            Assert.AreEqual(900, c.RenderMs, 1e-6);
        }

        [Test]
        public void ClockSyncPicksTheMinimumRttSample()
        {
            var s = new ClockSync();
            // true offset +1000: server = local + 1000. Sample 1 has a slow return path (asymmetric), sample 2 is clean.
            s.OnPong(new ServerMsg.Pong { ClientMs = 0, ServerMs = 1010 }, 100);   // rtt 100, offset 960
            s.OnPong(new ServerMsg.Pong { ClientMs = 200, ServerMs = 1210 }, 220); // rtt 20, offset 1000
            Assert.AreEqual(20, s.RttMs, 1e-9);
            Assert.AreEqual(1000, s.OffsetMs, 1e-9);
            Assert.AreEqual(1500, s.ServerNowMs(500), 1e-9);
            s.OnPong(new ServerMsg.Pong { ClientMs = 900, ServerMs = 0 }, 800); // negative rtt: ignored
            Assert.AreEqual(20, s.RttMs);
        }
    }

    public class InputSenderTests
    {
        static string[] s_counterControl;

        /// <summary>Guard (P-UI-ALLOC-PROOF-1): the allocation counter must register a known allocation, so a test built on it can never go blind silently.</summary>
        static void AssertCounterSees()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var kept = new string[1000];
            for (int i = 0; i < kept.Length; i++) kept[i] = "control" + i;
            s_counterControl = kept;
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.GreaterThan(0L), "the allocation counter did not see 1,000 kept string concatenations: it is blind here");
        }

        [Test]
        public void WeaponAndPickupIntentsKeepQueueOrderAcrossFrames()
        {
            var s = new InputSender();
            for (int i = 0; i < 15; i++) s.Attack(0, null);
            s.Swap(); s.Pickup(new PickupHandle(42)); s.Swap(1); s.Release(0, null);
            s.Swap(2); s.Pickup(default(PickupHandle));
            var frames = s.Flush(0);
            CollectionAssert.AreEqual(new[] { 16, 3 }, frames.Select(f => f.Commands.Count).ToArray());
            Assert.AreEqual(CommandKind.Swap, frames[0].Commands[15].Kind); Assert.IsNull(frames[0].Commands[15].Slot);
            Assert.AreEqual(CommandKind.Pickup, frames[1].Commands[0].Kind); Assert.AreEqual(42u, frames[1].Commands[0].PickupRef.Value);
            Assert.AreEqual(CommandKind.Swap, frames[1].Commands[1].Kind); Assert.AreEqual((byte)1, frames[1].Commands[1].Slot);
            Assert.AreEqual(CommandKind.Release, frames[1].Commands[2].Kind); Assert.AreEqual(2, s.Dropped);
            foreach (var frame in frames) Assert.DoesNotThrow(() => ClientMsg.Decode(frame.Encode()));
            Assert.AreEqual(0, s.Flush(1).Count);
        }

        [Test]
        public void AimIsRateLimitedToFortyPerSecond()
        {
            var s = new InputSender();
            int aims = 0;
            for (double t = 0; t < 1000; t += 5)
            {
                s.Aim((float)(t / 100));
                foreach (var f in s.Flush(t)) aims += f.Commands.Count(c => c.Kind == CommandKind.Aim);
            }
            Assert.That(aims, Is.InRange(38, 40));
        }


        [Test]
        public void UnchangedAimIsNotResentExceptByTheSlowRepeat()
        {
            var s = new InputSender();
            int aims = 0;
            for (double t = 0; t < 2000; t += 5)
            {
                s.Aim(1.25f);
                foreach (var f in s.Flush(t)) aims += f.Commands.Count(c => c.Kind == CommandKind.Aim);
            }
            Assert.That(aims, Is.InRange(20, 21)); // first send plus the 100 ms repeat (server drops aim while stunned or locked), not 40/s
        }

        [Test]
        public void ChangedAimIsNeverLostWhileRateLimited()
        {
            var s = new InputSender();
            s.Aim(1f); Assert.AreEqual(1f, s.Flush(0)[0].Commands[0].A);
            s.Aim(2f); Assert.AreEqual(0, s.Flush(10).Count);
            s.Aim(1f); // back to the sent value before the gate opened: nothing left to say
            Assert.AreEqual(0, s.Flush(30).Count);
            s.Aim(3f);
            var frames = s.Flush(40);
            Assert.AreEqual(1, frames.Count); Assert.AreEqual(3f, frames[0].Commands[0].A);
            s.Aim(4f); Assert.AreEqual(0, s.Flush(50).Count); // gated, still pending
            frames = s.Flush(65);
            Assert.AreEqual(4f, frames[0].Commands[0].A);
            Assert.AreEqual(0, s.Flush(90).Count);
        }

        [Test]
        public void IdleFlushAllocatesNothing()
        {
            AssertCounterSees();
            var s = new InputSender();
            s.Aim(1f); s.Flush(0);
            for (int i = 0; i < 100; i++) s.Flush(i);
            s.Aim(1f); s.Flush(1000); // the slow repeat is due here; measure the quiet window that follows it
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 1; i < 200; i++) { s.Aim(1f); s.Flush(1000 + i * 0.4); }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(1024));
        }

        [Test]
        public void LatestAimWinsAndNonAimLeavesImmediately()
        {
            var s = new InputSender();
            s.Aim(1f); s.Flush(0);
            s.Aim(2f); s.Aim(3f);
            var seq = s.Move(10, 20);
            var frames = s.Flush(10); // aim not due yet
            Assert.AreEqual(1, frames.Count);
            Assert.AreEqual(1, frames[0].Commands.Count);
            Assert.AreEqual(CommandKind.Move, frames[0].Commands[0].Kind);
            Assert.AreEqual(seq, frames[0].Commands[0].Seq);
            frames = s.Flush(30);
            Assert.AreEqual(3f, frames[0].Commands[0].A);
        }

        [Test]
        public void FramesCarryAtMostSixteenCommandsAndSequencesIncrease()
        {
            var s = new InputSender();
            for (int i = 0; i < 40; i++) s.Attack(0.5f, 1000 + i);
            var frames = s.Flush(0);
            CollectionAssert.AreEqual(new[] { 16, 16, 8 }, frames.Select(f => f.Commands.Count).ToArray());
            CollectionAssert.AreEqual(new uint[] { 1, 2, 3 }, frames.Select(f => f.Seq).ToArray());
            foreach (var f in frames) Assert.DoesNotThrow(() => ClientMsg.Decode(f.Encode()));
            Assert.AreEqual(4u, new InputSender().Also(x => { x.Stop(); x.Flush(0); x.Stop(); x.Flush(0); x.Stop(); x.Flush(0); x.Stop(); }).Flush(0)[0].Seq);
        }

        [Test]
        public void OrderSequencesWrapSkippingZeroAndNonFiniteInputIsDropped()
        {
            var s = new InputSender();
            byte last = 0;
            for (int i = 0; i < 300; i++) { last = s.Stop(); Assert.AreNotEqual(0, last); }
            Assert.AreEqual(0, s.Move(float.NaN, 0));
            s.Aim(float.PositiveInfinity); s.Attack(float.NaN, null);
            Assert.AreEqual(3, s.Dropped);
            foreach (var f in s.Flush(0)) Assert.DoesNotThrow(() => ClientMsg.Decode(f.Encode()));
        }

        [Test]
        public void TrainPreservesWireFieldsForAllFiveCommandsWithoutSemanticClamps()
        {
            var sender = new InputSender();
            // These intentionally unvalidated catalog/count fields are wire-shape examples, not server-valid actions.
            for (byte command = 1; command <= 5; command++)
                sender.Train(command, uint.MaxValue, ulong.MaxValue);

            var frames = sender.Flush(0);
            Assert.AreEqual(1, frames.Count);
            Assert.AreEqual(5, frames[0].Commands.Count);
            var decoded = (ClientMsg.Input)ClientMsg.Decode(frames[0].Encode());
            Assert.AreEqual(5, decoded.Commands.Count);
            for (int i = 0; i < 5; i++)
            {
                var command = decoded.Commands[i];
                Assert.AreEqual(CommandKind.Train, command.Kind);
                Assert.AreEqual((byte)(i + 1), command.TrainCommand);
                Assert.AreEqual(uint.MaxValue, command.TrainId);
                Assert.AreEqual(ulong.MaxValue, command.TrainCount);
                Assert.AreEqual(0, command.TrainOptions.Length);
            }
            Assert.AreEqual(0, sender.Dropped);
        }

        [Test]
        public void TrainCopiesThreeOptionsAndKeepsItsPositionAcrossFlushFrames()
        {
            var sender = new InputSender();
            for (int i = 0; i < 15; i++) sender.Attack(0, null);
            // Duplicate indices are forwarded for the server validator to refuse; this API only bounds wire shape.
            var options = new byte[] { 2, 2, 2 };
            sender.Train(4, 0, ulong.MaxValue, options);
            options[0] = 99;
            options[1] = 99;
            options[2] = 99;
            sender.Stop();

            var frames = sender.Flush(0);
            CollectionAssert.AreEqual(new[] { 16, 1 }, frames.Select(f => f.Commands.Count).ToArray());
            var decodedFirst = (ClientMsg.Input)ClientMsg.Decode(frames[0].Encode());
            var train = decodedFirst.Commands[15];
            Assert.AreEqual(CommandKind.Train, train.Kind);
            Assert.AreEqual((byte)4, train.TrainCommand);
            CollectionAssert.AreEqual(new byte[] { 2, 2, 2 }, train.TrainOptions);
            Assert.AreEqual(CommandKind.Stop, frames[1].Commands[0].Kind);
            Assert.DoesNotThrow(() => ClientMsg.Decode(frames[1].Encode()));
        }

        [Test]
        public void TrainRefusesOnlyInvalidCommandBytesOrMoreThanThreeOptions()
        {
            var sender = new InputSender();
            sender.Train(0, 0, 0);
            sender.Train(6, 0, 0);
            sender.Train(4, 0, 0, new byte[] { 0, 1, 2, 3 });

            Assert.AreEqual(3, sender.Dropped);
            Assert.AreEqual(0, sender.Flush(0).Count);

            sender.Train(4, 0, 0, new byte[] { 0, 1, 2 });
            var frames = sender.Flush(1);
            Assert.AreEqual(1, frames.Count);
            Assert.AreEqual((byte)4, frames[0].Commands[0].TrainCommand);
            CollectionAssert.AreEqual(new byte[] { 0, 1, 2 }, frames[0].Commands[0].TrainOptions);
            Assert.AreEqual(3, sender.Dropped);
        }
    }

    static class Ext { public static T Also<T>(this T t, Action<T> a) { a(t); return t; } }

}
