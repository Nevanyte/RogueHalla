using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-TICK-4 oracle (C#, ADR 0029), the parts of the library that must follow the session's step beside the vector tests
    /// (TickRateVectorTests): the render clock's floor, the vision step counts, the remote-draw step gap, what a Welcome
    /// sets on a Session, and a source guard for the Unity-side fx due time (no test project there). The legacy 50 ms consts
    /// (<c>RenderClock.MinDelayMs</c>, <c>LocalMotion.TickMs</c>, <c>VisionRule.TickMs</c>) stay and are pinned by the old tests.
    /// </summary>
    public class TickRateClientTests
    {
        // ----------------------------------------------------------------------------------------------------------------
        // RenderClock: the delay floor is one step plus 5 ms (55 at 50 ms, 30 at 25 ms)

        static double SteadyTarget(double tickMs, bool drawAhead)
        {
            var c = new RenderClock { TickMs = tickMs, DrawAhead = drawAhead };
            // A calm link: a snapshot every step, each arriving 40 ms after its server time, no jitter; frames at 60 Hz.
            double next = 0;
            for (double t = 1000; t < 9000; t += 1000.0 / 60)
            {
                while (1000 + next * tickMs + 40 <= t) { c.OnSnapshot(1000 + next * tickMs + 40, 1000 + next * tickMs); next++; }
                if (c.Ready) c.Update(t);
            }
            return c.TargetDelayMs;
        }

        [TestCase(50.0, 55.0)]
        [TestCase(25.0, 30.0)]
        public void TheRenderClockFloorIsOneStepPlusFiveMilliseconds(double tickMs, double floor)
        {
            Assert.AreEqual(floor, new RenderClock { TickMs = tickMs }.MinDelay, "MinDelay of a clock at " + tickMs + " ms");
            Assert.AreEqual(floor, SteadyTarget(tickMs, false), 1e-6, "the target of a calm link settles on the floor");
        }

        [Test]
        public void TheDrawAheadFloorFollowsTheStepToo()
        {
            // Draw-ahead lowers the floor by one step: 55 - 50 = 5 at 20 Hz, 30 - 25 = 5 at 40 Hz.
            Assert.AreEqual(5.0, SteadyTarget(50.0, true), 1e-6);
            Assert.AreEqual(5.0, SteadyTarget(25.0, true), 1e-6);
        }

        [Test]
        public void TheLegacyFloorConstantStaysFiftyFive()
        {
            Assert.AreEqual(55.0, RenderClock.MinDelayMs);
            Assert.AreEqual(55.0, new RenderClock().MinDelay, "a default clock is the 50 ms clock");
        }

        // ----------------------------------------------------------------------------------------------------------------
        // Vision: the step counts of the refresh and the trail follow the step (rh-sim vision.rs refresh_ticks / trail_ticks)

        [TestCase(50.0, 2, 600)]
        [TestCase(25.0, 4, 1200)]
        public void VisionCountsItsRefreshAndTrailInTheSessionsSteps(double tickMs, int refresh, int trail)
        {
            var rule = new VisionRule(new MapBlob(4, 4, new byte[16])) { StepMs = tickMs };
            Assert.AreEqual(tickMs, rule.StepMs);
            Assert.AreEqual(VisionRule.DefaultUpdateMs, rule.UpdateMs, "the refresh period is milliseconds: it does not move with the rate");
            Assert.AreEqual(VisionRule.DefaultTrailMs, rule.TrailMs, "the trail is milliseconds too");
            Assert.AreEqual(refresh, rule.RefreshSteps, "VISION_UPDATE_MS in steps");
            Assert.AreEqual(trail, rule.TrailSteps, "VISION_TRAIL_MS in steps");
        }

        [Test]
        public void ADefaultVisionRuleIsTheFiftyMillisecondOne()
        {
            var rule = new VisionRule(new MapBlob(4, 4, new byte[16]));
            Assert.AreEqual(50.0, rule.StepMs);
            Assert.AreEqual(2, rule.RefreshSteps);
            Assert.AreEqual(600, rule.TrailSteps);
        }

        // ----------------------------------------------------------------------------------------------------------------
        // RemoteDraw: two samples more than two steps apart are not one step (a 60 ms gap is 2.4 steps at 25 ms, 1.2 at 50 ms)

        static WireActor Walker(double x)
        {
            return new WireActor { Id = 7, X = (float)x, Y = 500, HpPct = 100, Status = Wire.DefaultStatus, WalkDir = 0, WalkTicks = 3 };
        }

        static double DrawnPastNewest(double tickMs, double gapMs)
        {
            var buffer = new SnapshotBuffer();
            double speed = 170;
            buffer.Add(new ServerMsg.Snapshot { Tick = 1000, Actors = new[] { Walker(200) }, ServerTimeOverrideMs = 1000 });
            double newestX = 200 + speed * gapMs / 1000;
            buffer.Add(new ServerMsg.Snapshot { Tick = 1002, Actors = new[] { Walker(newestX) }, ServerTimeOverrideMs = 1000 + gapMs });
            var draw = new RemoteDraw(buffer) { Map = new MapBlob(96, 40, new byte[96 * 40]), TickMs = tickMs };
            var list = new List<InterpActor>();
            Assert.IsTrue(draw.Sample(1000 + gapMs + 10, list));
            return list[0].X - newestX;
        }

        [Test]
        public void ARemoteStepGapIsMeasuredInTheSessionsStep()
        {
            // At 25 ms a 60 ms gap is more than two steps (50 ms): no step is known, nothing is drawn past the newest sample.
            Assert.LessOrEqual(DrawnPastNewest(25.0, 60.0), 1e-3, "a 60 ms gap at 25 ms steps is not a step");
            // Controls: a 50 ms gap is two steps at 25 ms and one at 50 ms; a 60 ms gap is within two 50 ms steps.
            Assert.Greater(DrawnPastNewest(25.0, 50.0), 1.0, "two 25 ms steps still count");
            Assert.Greater(DrawnPastNewest(50.0, 60.0), 1.0, "at 20 Hz a 60 ms gap is a step");
        }

        // ----------------------------------------------------------------------------------------------------------------
        // Session: a Welcome's rate reaches every consumer

        static byte[] Token16() { var b = new byte[16]; for (int i = 0; i < 16; i++) b[i] = 0xAB; return b; }

        static Session InMatch(byte rate)
        {
            var t = new FakeTransport();
            var s = new Session(t, new SessionOptions { Url = "ws://x/ws", Profile = "p", Now = () => 1000 });
            s.Connect(); t.Open();
            t.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = Token16(), PlayerId = 3, Session = Token16(), TickRate = rate, Tick = 1, ServerMs = rate == 40 ? 25 : 50, Map = new MapBlob(4, 4, new byte[16]) });
            return s;
        }

        [TestCase((byte)20, 50.0)]
        [TestCase((byte)40, 25.0)]
        public void AWelcomeSetsTheStepOfTheClockTheRemoteDrawAndTheLocalWalk(byte rate, double tickMs)
        {
            var s = InMatch(rate);
            Assert.AreEqual(tickMs, s.Clock.TickMs, "render clock");
            Assert.AreEqual(tickMs, s.Remote.TickMs, "remote draw");
            Assert.AreEqual(tickMs, s.Motion.StepMs, "local walk prediction");
            Assert.AreEqual(tickMs + 5, s.Clock.MinDelay, "the clock's floor");
        }

        [Test]
        public void ANewWelcomeAtAnotherRateReplacesTheLocalWalksStep()
        {
            var t = new FakeTransport();
            var s = new Session(t, new SessionOptions { Url = "ws://x/ws", Profile = "p", Now = () => 1000 });
            s.Connect(); t.Open();
            foreach (byte rate in new byte[] { 40, 20 })
            {
                t.Deliver(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = Token16(), PlayerId = 3, Session = Token16(), TickRate = rate, Tick = 1, ServerMs = 1000.0 / rate, Map = new MapBlob(4, 4, new byte[16]) });
                Assert.AreEqual(1000.0 / rate, s.Motion.StepMs, "after a Welcome at " + rate + " Hz");
            }
        }

        // ----------------------------------------------------------------------------------------------------------------
        // Source guard: nothing the session's step reaches may read the fixed 50 ms wire constant

        [TestCase("client-unity/RoguehallaNet/Net/RemoteDraw.cs")]
        [TestCase("client-unity/RoguehallaNet/Net/RenderClock.cs")]
        [TestCase("client-unity/RoguehallaNet/Net/LocalMotion.cs")]
        [TestCase("client-unity/RoguehallaNet/Net/LocalMotion.AttackPause.cs")]
        [TestCase("client-unity/RoguehallaNet/Net/LocalMotion.Alignment.cs")]
        [TestCase("client-unity/UnityProject/Assets/Scripts/Net/MatchClient.cs")]
        public void NoStepMathReadsTheFixedWireTick(string path)
        {
            string text = File.ReadAllText(Path.Combine(Util.RepoRoot(), path));
            Assert.IsFalse(text.Contains("Wire.TickMs"), path + " reads Wire.TickMs (the 20 Hz step) instead of the session's rate");
        }

        [TestCase("client-unity/RoguehallaNet/Net/LocalMotion.AttackPause.cs")]
        [TestCase("client-unity/RoguehallaNet/Net/LocalMotion.Alignment.cs")]
        [TestCase("client-unity/RoguehallaNet/Net/LocalMotion.cs")]
        public void LocalMotionStepMathUsesTheInstanceStepNotTheConst(string path)
        {
            // `TickMs` (the const) may remain declared in LocalMotion.cs as the 50 ms default; no expression may read it.
            string text = File.ReadAllText(Path.Combine(Util.RepoRoot(), path));
            foreach (var line in text.Split('\n'))
            {
                string code = line.Trim();
                if (code.StartsWith("///") || code.StartsWith("//") || code.Contains("public const double TickMs")) continue;
                Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(code, @"(?<![A-Za-z.])TickMs\b"), path + ": " + code);
            }
        }
    }
}
