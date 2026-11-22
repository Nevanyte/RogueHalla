using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-NET-STEER-1 oracle. A scripted 30 s kite (keys held, heading changes every 600 ms, a click every 250 ms, aim changing
    /// every frame) is run through <see cref="SteerPacer"/> and <see cref="InputSender"/> exactly as MatchClient wires them, and
    /// checked against the server's order budget (crates/rh-sim/src/input.rs OrderBudget::allow: 30 tokens, 15/s) and its frame
    /// limit (crates/rh-server/src/conn.rs: 60 frames/s). Pure client arithmetic; no map, no network.
    /// </summary>
    public class SteerPacingTests
    {
        // The server's per-seat budget: starts full at 30, refills 15/s on elapsed time, one token per non-Aim command.
        sealed class ServerBudget
        {
            double _tokens = 30, _last;
            public bool Allow(double nowMs)
            {
                _tokens = Math.Min(30, _tokens + (nowMs - _last) * 15 / 1000.0); _last = nowMs;
                if (_tokens < 1) return false;
                _tokens -= 1; return true;
            }
        }

        // MatchClient's steering block: the open-field goal is pos + unit(keys) * AheadMax; Turn is essential (Spend), Refresh asks TryTake.
        sealed class Steerer
        {
            public readonly SteerPacer Pacer = new SteerPacer();
            public readonly OrderBudget Budget = new OrderBudget();
            public double GoalX, GoalY; // last SENT goal
            public bool HasGoal;

            public bool Step(InputSender input, double now, int kx, int ky, double px, double py)
            {
                double len = Math.Sqrt(kx * kx + ky * ky);
                double gx = px + kx / len * KeyboardSteer.AheadMax, gy = py + ky / len * KeyboardSteer.AheadMax;
                var d = Pacer.Decide(now, kx, ky, px, py, gx, gy);
                if (d == SteerDecision.None) return false;
                if (d == SteerDecision.Refresh && !Budget.TryTake(now)) return false;
                if (d == SteerDecision.Turn) Budget.Spend(now);
                input.Move((float)gx, (float)gy);
                Pacer.Sent(now, kx, ky, gx, gy);
                GoalX = gx; GoalY = gy; HasGoal = true;
                return true;
            }
        }

        // E, NE, N, NW, W, SW, S, SE in server axes (+y down).
        static readonly int[][] Heads =
        {
            new[] { 1, 0 }, new[] { 1, -1 }, new[] { 0, -1 }, new[] { -1, -1 },
            new[] { -1, 0 }, new[] { -1, 1 }, new[] { 0, 1 }, new[] { 1, 1 },
        };

        [TestCase(60)]
        [TestCase(144)]
        [TestCase(240)]
        public void ThirtySecondKiteWithAttacksStaysUnderTheServerBudget(int fps)
        {
            const double Speed = 170.0, RunMs = 30000;
            double dt = 1000.0 / fps;
            var input = new InputSender();
            var steer = new Steerer();
            var server = new ServerBudget();
            double px = 0, py = 0;
            int orders = 0, drops = 0;
            double firstDrop = -1, nextClick = 0, releaseAt = -1;
            var times = new List<double>();
            int peak = 0, lo = 0;

            long frames = (long)(RunMs * fps / 1000.0);
            for (long i = 0; i < frames; i++)
            {
                double now = i * dt;
                if (steer.HasGoal)
                {
                    double dx = steer.GoalX - px, dy = steer.GoalY - py, dist = Math.Sqrt(dx * dx + dy * dy);
                    double step = Speed * dt / 1000.0;
                    if (dist <= step) { px = steer.GoalX; py = steer.GoalY; }
                    else if (dist > 0) { px += dx / dist * step; py += dy / dist * step; }
                }
                var h = Heads[(int)(now / 600) % 8];
                steer.Step(input, now, h[0], h[1], px, py);
                if (now >= nextClick) { input.Attack(0.5f, now); releaseAt = now + 150; nextClick += 250; }
                if (releaseAt >= 0 && now >= releaseAt) { input.Release(0.5f, now); releaseAt = -1; }
                input.Aim((float)(now / 300));
                foreach (var f in input.Flush(now))
                {
                    times.Add(now);
                    foreach (var c in f.Commands)
                    {
                        if (c.Kind == CommandKind.Aim) continue;
                        orders++;
                        if (!server.Allow(now)) { drops++; if (firstDrop < 0) firstDrop = now; }
                    }
                }
                while (lo < times.Count && times[lo] <= now - 1000) lo++;
                peak = Math.Max(peak, times.Count - lo);
            }

            double perSecond = orders / (RunMs / 1000.0);
            TestContext.Progress.WriteLine(
                $"steer-kite {fps} fps: {perSecond:F1} orders/s, {drops} dropped (first at {(firstDrop < 0 ? "never" : firstDrop.ToString("F0") + " ms")}), peak {peak} frames in 1 s");
            Assert.AreEqual(0, drops, "orders the server would drop (first at " + firstDrop + " ms) at " + fps + " fps");
            Assert.LessOrEqual(perSecond, 14.0, "mean non-Aim orders per second at " + fps + " fps");
            Assert.LessOrEqual(peak, 45, "most WebSocket frames in any 1000 ms at " + fps + " fps (server limit 60)");
        }

        [Test]
        public void ADirectionChangeSendsOnTheSameFrame()
        {
            var input = new InputSender();
            var steer = new Steerer();
            double px = 0, py = 0;
            for (double now = 0; now < 1000; now += 1000.0 / 60)
            {
                steer.Step(input, now, 1, 0, px, py);
                input.Flush(now);
                if (steer.HasGoal) px = Math.Min(steer.GoalX, px + 170.0 / 60);
            }
            double t = 1000;
            Assert.IsTrue(steer.Step(input, t, 0, -1, px, py), "the key change must send a Move at once");
            var frames = input.Flush(t);
            var move = frames.SelectMany(f => f.Commands).Single(c => c.Kind == CommandKind.Move);
            Assert.AreEqual((float)px, move.X, 0.01);
            Assert.AreEqual((float)(py - KeyboardSteer.AheadMax), move.Y, 0.01);
        }

        [Test]
        public void ARootedWalkerIsResentOnlyByTheSafetyRepeat()
        {
            var pacer = new SteerPacer();
            int sends = 0;
            for (double now = 0; now < 2000; now += 1000.0 / 60)
            {
                var d = pacer.Decide(now, 1, 0, 0, 0, 64, 0); // the walker never moves; the goal stays 64 u ahead
                if (d == SteerDecision.None) continue;
                pacer.Sent(now, 1, 0, 64, 0);
                sends++;
            }
            Assert.LessOrEqual(sends, 5, "Moves sent in 2 s to a walker that is not moving (today: every 100 ms)");
            Assert.GreaterOrEqual(sends, 2, "the safety repeat must still heal a dropped order");
        }
    }
}
