using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-NET-DRAWAHEAD-1b oracle: the legacy client's bounded draw-ahead of remote fighters (packages/client/src/net/remoteDraw.ts,
    /// shared/src/interp.ts RenderClock <c>aheadMs</c>), driven headlessly. A server stream of one snapshot per 50 ms tick arrives
    /// 30 ms late and is stamped at the frame that polls it; each frame advances the clock and samples the remote fighters at
    /// <see cref="RenderClock.DrawMs"/>. "True position" is where the server had the fighter at the newest time the client can
    /// know of (local time minus the link's latency).
    /// </summary>
    public class DrawAheadTests
    {
        const double Speed = 170;           // PLAYER_SPEED, u/s
        const double Latency = 30;          // ms
        const double TickMs = 50;
        const double MaxAheadU = Speed * TickMs / 1000; // REMOTE_MAX_AHEAD_U: one tick's walk, 8.5 u
        const float X0 = 200, Y0 = 500;
        const uint Id = 7;

        static MapBlob Floor() { return new MapBlob(96, 40, new byte[96 * 40]); } // 3072 x 1280 u of open floor

        struct Frame
        {
            public double Local, Draw, Render, Lead;
            public InterpActor Fighter;
            public WireActor Newest;
        }

        static WireActor Player(double x, double y, byte dir, byte ticks)
        {
            return new WireActor { Id = Id, X = (float)x, Y = (float)y, HpPct = 100, Status = Wire.DefaultStatus, WalkDir = dir, WalkTicks = ticks };
        }

        /// <summary>Plays <paramref name="seconds"/> of the stream <paramref name="at"/> (server time to the fighter) at <paramref name="fps"/>.</summary>
        static List<Frame> Play(double fps, double seconds, Func<double, WireActor> at)
        {
            var buffer = new SnapshotBuffer();
            var clock = new RenderClock { DrawAhead = true };
            var draw = new RemoteDraw(buffer) { Map = Floor(), TickMs = TickMs };
            var frames = new List<Frame>();
            var list = new List<InterpActor>();
            double step = 1000.0 / fps;
            int next = 0;
            WireActor newest = default(WireActor);
            for (double t = 1000; t < 1000 + seconds * 1000; t += step)
            {
                for (double s = 1000 + next * TickMs; s + Latency <= t; s = 1000 + next * TickMs)
                {
                    newest = at(s);
                    buffer.Add(new ServerMsg.Snapshot { Tick = (uint)Math.Round(s / TickMs), Actors = new[] { newest } });
                    clock.OnSnapshot(t, s);
                    next++;
                }
                if (!clock.Ready) continue;
                clock.Update(t);
                Assert.IsTrue(draw.Sample(clock.DrawMs, list), "the fighter has data");
                Assert.AreEqual(1, list.Count);
                frames.Add(new Frame { Local = t, Draw = clock.DrawMs, Render = clock.RenderMs, Lead = clock.LeadMs, Fighter = list[0], Newest = newest });
            }
            return frames;
        }

        /// <summary>A player walking east at PLAYER_SPEED from server time 1000 ms, with the walk hint the server would send.</summary>
        static Func<double, WireActor> WalkingEast(byte ticks, double stopAtMs = double.PositiveInfinity)
        {
            return s =>
            {
                double walked = Math.Min(s, stopAtMs) - 1000;
                bool walking = s <= stopAtMs;
                return Player(X0 + Speed * walked / 1000, Y0, 0, walking ? ticks : (byte)0);
            };
        }

        static double TrueX(double localMs, double stopAtMs = double.PositiveInfinity)
        {
            return X0 + Speed * (Math.Min(localMs - Latency, stopAtMs) - 1000) / 1000;
        }

        [Test]
        public void AWalkingPlayerWithAHintIsDrawnNearItsTrueServerPositionAndNeverPastTheCap()
        {
            foreach (var fps in new[] { 60.0, 144.0 })
            {
                var frames = Play(fps, 8, WalkingEast(3)).Where(f => f.Local > 4000).ToList();
                var errors = frames.Select(f => Math.Abs(f.Fighter.X - TrueX(f.Local))).ToList();
                double mean = errors.Average(), worst = errors.Max();
                double pastNewest = frames.Max(f => f.Fighter.X - f.Newest.X);
                Console.WriteLine("fps " + fps + ": mean error " + mean.ToString("F2") + " u, worst " + worst.ToString("F2") + " u, most past newest " + pastNewest.ToString("F2") + " u");
                // Today (no draw-ahead) the fighter trails by the whole delay, 55-67 ms of walk: 9.4-11.4 u.
                Assert.LessOrEqual(mean, 3.5, "fps " + fps + ": a walking player with a hint is drawn within a few units of where the server has it");
                Assert.LessOrEqual(worst, 5.5, "fps " + fps + ": worst frame");
                Assert.LessOrEqual(pastNewest, MaxAheadU + 0.01, "fps " + fps + ": never more than one tick's walk past its newest sample");
                foreach (var f in frames) Assert.AreEqual(Y0, f.Fighter.Y, 0.01, "drawn along its walk only");
            }
        }

        [Test]
        public void APlayerWithoutAHintIsHeldAtItsNewestSample()
        {
            foreach (var fps in new[] { 60.0, 144.0 })
                foreach (var f in Play(fps, 6, WalkingEast(0)))
                    Assert.LessOrEqual(f.Fighter.X, f.Newest.X + 1e-3, "fps " + fps + " at " + f.Local + ": walk_ticks 0 holds the newest sample");
        }

        [Test]
        public void AnAbruptStopSettlesOnTheStopWithoutAJump()
        {
            // The newest walking samples still promise three ticks (a stun or a new order can break a promise): the drawn figure may
            // overshoot by at most the cap, then the offset decays back, speed-capped, never by a jump.
            const double stopAt = 5000;
            double xStop = X0 + Speed * (stopAt - 1000) / 1000;
            foreach (var fps in new[] { 60.0, 144.0 })
            {
                var frames = Play(fps, 8, WalkingEast(3, stopAt));
                for (int i = 1; i < frames.Count; i++)
                {
                    var a = frames[i - 1];
                    var b = frames[i];
                    if (b.Local < 3000) continue;
                    double dt = b.Local - a.Local;
                    double jump = Math.Abs(b.Fighter.X - a.Fighter.X);
                    // A walk moves Speed x dt (render time may run up to 5% fast while it catches up); a correction at rest is capped well
                    // under that (legacy REMOTE_CORRECT_SPEED_REST 44 u/s), so snapping the overshoot back fails here.
                    Assert.LessOrEqual(jump, 1.05 * Speed * dt / 1000 + 0.25, "fps " + fps + " at " + b.Local + ": a frame's move is a walk or a capped correction, never a jump");
                    Assert.LessOrEqual(b.Fighter.X, xStop + MaxAheadU + 0.01, "fps " + fps + " at " + b.Local + ": the overshoot is at most the cap");
                    if (b.Local >= stopAt + Latency + 1000) Assert.AreEqual(xStop, b.Fighter.X, 0.25, "fps " + fps + " at " + b.Local + ": settled on the stop");
                }
            }
        }

        [Test]
        public void AMovingMobIsDrawnAtMostTwentyFiveMillisecondsPastItsNewestSample()
        {
            const double mobSpeed = 100;
            const double cap = mobSpeed * RemoteDraw.MobExtrapolateMs / 1000; // 2.5 u
            Assert.AreEqual(25, RemoteDraw.MobExtrapolateMs, "legacy MOB_EXTRAPOLATE_MS");
            foreach (var fps in new[] { 60.0, 144.0 })
            {
                var frames = Play(fps, 6, s => new WireActor { Id = Id, Kind = (byte)ActorKind.RegionalMob, X = (float)(X0 + mobSpeed * (s - 1000) / 1000), Y = Y0, HpPct = 100, Status = Wire.DefaultStatus })
                    .Where(f => f.Local > 3000).ToList();
                double most = frames.Max(f => f.Fighter.X - f.Newest.X);
                Assert.LessOrEqual(most, cap + 0.01, "fps " + fps + ": a mob is drawn at most 25 ms of its walk past its newest sample");
                Assert.GreaterOrEqual(most, 1.0, "fps " + fps + ": a moving mob is drawn past its newest sample when render time runs past it");
            }
        }

        [Test]
        public void TheAttackStampIsRenderTimeMinusTheLeadAndMatchesTheClockWithoutDrawAhead()
        {
            // Two clocks on one calm stream: the draw-ahead one and a bare one (draw-ahead off, what P-NET-DELAY-1 ships). The time an
            // attack or cast is stamped with is RenderMs on both (MatchClient.cs, Session.Cast): it must not move, so the server's rewind
            // (rh-sim weapons.rs note_lag) is what it was. Remote fighters are drawn LeadMs later, at DrawMs.
            foreach (var fps in new[] { 60.0, 144.0 })
            {
                var on = new RenderClock { DrawAhead = true };
                var off = new RenderClock();
                Assert.AreEqual(TickMs, on.AheadMs, "the ahead amount is one tick");
                Assert.AreEqual(0, off.AheadMs);
                double step = 1000.0 / fps, lastDraw = double.NegativeInfinity, lastRender = double.NegativeInfinity;
                int next = 0;
                for (double t = 1000; t < 9000; t += step)
                {
                    for (double s = 1000 + next * TickMs; s + Latency <= t; s = 1000 + next * TickMs) { on.OnSnapshot(t, s); off.OnSnapshot(t, s); next++; }
                    if (!on.Ready) continue;
                    on.Update(t); off.Update(t);
                    Assert.AreEqual(on.RenderMs + on.LeadMs, on.DrawMs, 1e-9, "DrawMs is RenderMs plus the lead");
                    Assert.GreaterOrEqual(on.DrawMs, lastDraw, "draw time never runs backwards");
                    Assert.GreaterOrEqual(on.RenderMs, lastRender, "the stamp never runs backwards");
                    lastDraw = on.DrawMs; lastRender = on.RenderMs;
                    Assert.AreEqual(off.DrawMs, off.RenderMs, 1e-9, "without draw-ahead the two are one");
                    if (t > 5000)
                    {
                        Assert.AreEqual(TickMs, on.LeadMs, 0.5, "fps " + fps + " at " + t + ": on a calm link the draw runs one tick ahead of the stamp");
                        Assert.AreEqual(off.RenderMs, on.RenderMs, 2.0, "fps " + fps + " at " + t + ": the stamp is what the clock without draw-ahead gives");
                    }
                }
            }
        }
    }
}
