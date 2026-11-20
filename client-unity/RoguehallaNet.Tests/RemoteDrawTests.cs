using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-NET-DRAWAHEAD-1b, beside the oracle (DrawAheadTests): what the oracle's open floor and steady walks do not reach. Walls clip
    /// the draw-ahead, every hold (stun, root, cast, death, a windup window, an engaged mob) keeps a fighter at its newest sample, a
    /// cut is drawn where it lands, a 40 Hz Welcome converts walk ticks with its own tick, the stamp never runs backwards near the
    /// delay ceiling, and sampling makes no garbage.
    /// </summary>
    public class RemoteDrawTests
    {
        const double Speed = 170, Latency = 30;
        const uint Id = 7;

        struct Frame { public double Local, Draw; public InterpActor Fighter; public WireActor Newest; public double NewestMs; }

        static MapBlob Floor() { return new MapBlob(96, 40, new byte[96 * 40]); }

        /// <summary>Open floor with a wall column at tile x = 60 (world x 1920..1952).</summary>
        static MapBlob WallAt60()
        {
            var tiles = new byte[96 * 40];
            for (int y = 0; y < 40; y++) tiles[y * 96 + 60] = MapBlob.Wall;
            return new MapBlob(96, 40, tiles);
        }

        static WireActor Player(double x, double y, byte dir, byte ticks)
        {
            return new WireActor { Id = Id, X = (float)x, Y = (float)y, HpPct = 100, Status = Wire.DefaultStatus, WalkDir = dir, WalkTicks = ticks };
        }

        static List<Frame> Play(double seconds, Func<double, WireActor> at, MapBlob map = null, double tickMs = 50, Action<RemoteDraw> setup = null, double fps = 60)
        {
            var buffer = new SnapshotBuffer();
            var clock = new RenderClock { DrawAhead = true, TickMs = tickMs };
            var draw = new RemoteDraw(buffer) { Map = map ?? Floor(), TickMs = tickMs };
            if (setup != null) setup(draw);
            var frames = new List<Frame>();
            var list = new List<InterpActor>();
            double step = 1000.0 / fps;
            int next = 0;
            WireActor newest = default(WireActor);
            double newestMs = 0;
            for (double t = 1000; t < 1000 + seconds * 1000; t += step)
            {
                for (double s = 1000 + next * tickMs; s + Latency <= t; s = 1000 + next * tickMs)
                {
                    newest = at(s);
                    newestMs = s;
                    var snap = new ServerMsg.Snapshot { Tick = (uint)(1000 + next), Actors = new[] { newest }, ServerTimeOverrideMs = s };
                    Assert.IsTrue(buffer.Add(snap));
                    clock.OnSnapshot(t, s);
                    next++;
                }
                if (!clock.Ready) continue;
                clock.Update(t);
                Assert.IsTrue(draw.Sample(clock.DrawMs, list));
                frames.Add(new Frame { Local = t, Draw = clock.DrawMs, Fighter = list[0], Newest = newest, NewestMs = newestMs });
            }
            return frames;
        }

        static Func<double, WireActor> East(double x0, byte ticks, Func<WireActor, double, WireActor> edit = null)
        {
            return s =>
            {
                var a = Player(x0 + Speed * (s - 1000) / 1000, 500, 0, ticks);
                return edit != null ? edit(a, s) : a;
            };
        }

        [Test]
        public void TheDrawAheadIsClippedAtAWall()
        {
            // The server stops the body at 1920 - 12 = 1908 (about 9.5 s in), yet its hint still promises three ticks (the worst case
            // for the client): the drawn disc (PLAYER_RADIUS - 1 = 11) never overlaps the wall, and it settles on the stop.
            var frames = Play(12, s =>
            {
                double x = Math.Min(1908, 300 + Speed * (s - 1000) / 1000);
                return Player(x, 500, 0, 3);
            }, WallAt60());
            Assert.Greater(frames.Max(f => f.Fighter.X - f.Newest.X), 3.5, "drawn ahead while the walk is clear");
            Assert.Greater(frames.Where(f => f.Newest.X > 1900).Max(f => f.Fighter.X), 1908.5, "drawn ahead up to the wall");
            foreach (var f in frames) Assert.LessOrEqual(f.Fighter.X, 1920 - 11 + 0.01, "at " + f.Local + ": the drawn body stays out of the wall");
            Assert.AreEqual(1908, frames.Last().Fighter.X, 0.25, "settled on the stop");
        }

        [Test]
        public void StunRootCastAndDeathHoldAWalkerWithAHint()
        {
            foreach (ushort bits in new ushort[] { Ui.Indicators.Stunned, Ui.Indicators.Rooted, Ui.Indicators.Casting })
                foreach (var f in Play(4, East(200, 3, (a, s) => { a.Effects = bits; return a; })))
                    Assert.LessOrEqual(f.Fighter.X, f.Newest.X + 1e-3, "effects " + bits + " at " + f.Local + ": held at the newest sample");
            foreach (var f in Play(4, East(200, 3, (a, s) => { a.Status = (byte)ActorStatus.Dead; return a; })))
                Assert.LessOrEqual(f.Fighter.X, f.Newest.X + 1e-3, "dead at " + f.Local + ": held at the newest sample");
        }

        [Test]
        public void AWindupWindowHoldsFromItsStartWithoutAJump()
        {
            // A sword windup noted at 6000 for 1000 ms: from 6000 to 7000 the walker is drawn at its newest sample (the stream here
            // keeps walking, so only the window holds it); before it, the budget shrinks to the window's start, so nothing jumps.
            var frames = Play(9, East(200, 3), setup: d => d.NoteWindup(Id, 6000, 1000, (byte)WeaponId.Sword));
            Assert.Greater(frames.Where(f => f.NewestMs > 5000 && f.NewestMs < 5900).Max(f => f.Fighter.X - f.Newest.X), 3.5, "drawn ahead before the window");
            for (int i = 1; i < frames.Count; i++)
            {
                var f = frames[i];
                if (f.NewestMs >= 6100 && f.NewestMs < 6950) Assert.LessOrEqual(f.Fighter.X, f.Newest.X + 0.5, "at " + f.Local + ": held by the windup");
                // Held while the stream walks on, each new sample leaves the figure behind and it catches up at up to its walk speed on
                // top of the walk (legacy CATCHUP_SHARE 1.0): a frame moves at most twice the walk, never a snap.
                if (f.Local > 5000) Assert.LessOrEqual(Math.Abs(f.Fighter.X - frames[i - 1].Fighter.X), 2.05 * Speed * (f.Local - frames[i - 1].Local) / 1000 + 0.25, "at " + f.Local + ": no jump");
            }
            Assert.Greater(frames.Where(f => f.NewestMs > 7500).Max(f => f.Fighter.X - f.Newest.X), 3.5, "drawn ahead again once the window is over");
            // A cancel drops the window: drawn ahead through it.
            var cancelled = Play(9, East(200, 3), setup: d => { d.NoteWindup(Id, 6000, 1000, (byte)WeaponId.Sword); d.NoteAttackEnd(Id); });
            Assert.Greater(cancelled.Where(f => f.NewestMs >= 6100 && f.NewestMs < 6950).Max(f => f.Fighter.X - f.Newest.X), 3.5, "a cancelled windup holds nothing");
        }

        [Test]
        public void ARangedWindupHoldsOnlyItsReleasePause()
        {
            // A bow shot noted at 6000 landing 1000 ms later holds from 6800 (RANGED_RELEASE_PAUSE_MS 200 before it lands).
            var frames = Play(9, East(200, 3), setup: d => d.NoteWindup(Id, 6000, 1000, (byte)WeaponId.Bow));
            Assert.Greater(frames.Where(f => f.NewestMs >= 6100 && f.NewestMs < 6700).Max(f => f.Fighter.X - f.Newest.X), 3.5, "walks on before the release pause");
            var held = frames.Where(f => f.NewestMs >= 6850 && f.NewestMs < 6950).ToList();
            Assert.IsNotEmpty(held);
            foreach (var f in held) Assert.LessOrEqual(f.Fighter.X, f.Newest.X + 0.5, "at " + f.Local + ": held in the release pause");
        }

        [Test]
        public void AnEngagedMobIsHeld()
        {
            foreach (var f in Play(4, s => new WireActor { Id = Id, Kind = (byte)ActorKind.RegionalMob, X = (float)(200 + 100 * (s - 1000) / 1000), Y = 500, HpPct = 100, Status = Wire.DefaultStatus, Flags = WireIndicators.FlagEngaged }))
                Assert.LessOrEqual(f.Fighter.X, f.Newest.X + 1e-3, "at " + f.Local + ": an engaged mob is held");
        }

        [Test]
        public void ACutIsDrawnWhereItLandsNeverSlidInto()
        {
            // At 4000 the walker is displaced 20 u back (under the snap distance, so only the cut keeps it from sliding).
            const double cutAt = 4000;
            var frames = Play(6, East(200, 3, (a, s) =>
            {
                if (s >= cutAt) { a.X -= 20; a.DisplaceSeq = 1; }
                return a;
            }));
            double before = 200 + Speed * (cutAt - 50 - 1000) / 1000, landing = 200 + Speed * (cutAt - 1000) / 1000 - 20;
            var after = frames.Where(f => f.Fighter.DisplaceSeq == 1).ToList();
            Assert.IsNotEmpty(after);
            Assert.AreEqual(landing, after[0].Fighter.X, 0.01, "the first frame past the cut draws where it lands");
            foreach (var f in frames.Where(f => f.Fighter.DisplaceSeq == 0)) Assert.LessOrEqual(f.Fighter.X, before + Speed * 0.05 + 0.01, "before the cut");
        }

        [Test]
        public void AFortyHertzWelcomeConvertsWalkTicksWithItsOwnTick()
        {
            // 25 ms ticks, hints up to 6 ticks: drawn 40 ms past the newest sample, a walker goes one 25 ms tick's walk (4.25 u), never
            // 40 ms of it or 6 x 50 ms; drawn 10 ms past, 10 ms of it. (A fresh draw each time: no earlier frame to carry an offset.)
            var clock = new RenderClock { DrawAhead = true, TickMs = 25 };
            Assert.AreEqual(25, clock.AheadMs, "the clock's ahead amount is the Welcome's tick");
            var buffer = new SnapshotBuffer();
            var list = new List<InterpActor>();
            double s = 0, newestX = 0;
            for (int k = 0; k < 3; k++)
            {
                s = 1000 + 25 * k;
                newestX = 200 + Speed * (s - 1000) / 1000;
                buffer.Add(new ServerMsg.Snapshot { Tick = (uint)(1000 + k), Actors = new[] { Player(newestX, 500, 0, 6) }, ServerTimeOverrideMs = s });
            }
            foreach (var past in new[] { 40.0, 10.0, 25.0 })
            {
                var draw = new RemoteDraw(buffer) { Map = Floor(), TickMs = 25 };
                Assert.IsTrue(draw.Sample(s + past, list));
                Assert.AreEqual(newestX + Speed * Math.Min(past, 25) / 1000, list[0].X, 0.01, past + " ms past the newest sample");
            }
            var at50 = new RemoteDraw(buffer) { Map = Floor(), TickMs = 50 };
            Assert.IsTrue(at50.Sample(s + 40, list));
            Assert.AreEqual(newestX + Speed * 0.040, list[0].X, 0.01, "the same hint at a 20 Hz Welcome goes on for 40 ms");
        }

        [Test]
        public void TheStampNeverRunsBackwardsNearTheDelayCeiling()
        {
            // A link that stalls for 160 ms every 2 s (what was sent in the stall arrives at once, in order) drives the delay to its
            // 150 ms ceiling, where the lead shrinks.
            var on = new RenderClock { DrawAhead = true };
            double lastRender = double.NegativeInfinity, lastDraw = double.NegativeInfinity;
            bool shrank = false;
            int next = 0;
            double newest = double.NegativeInfinity;
            var pending = new List<(double at, double s)>();
            for (double t = 1000; t < 20000; t += 1000.0 / 60)
            {
                for (double s = 1000 + next * 50; s <= t; s = 1000 + next * 50) { double stall = Math.Floor(s / 2000) * 2000; pending.Add((Math.Max(s, s - stall < 160 ? stall + 160 : s) + 30, s)); next++; }
                pending.Sort((a, b) => a.s.CompareTo(b.s));
                for (int i = 0; i < pending.Count;)
                {
                    if (pending[i].at <= t)
                    {
                        // As Session: only a snapshot newer than the newest reaches the clock (an older one would be a source-clock cut).
                        if (pending[i].s > newest) { on.OnSnapshot(t, pending[i].s); newest = pending[i].s; }
                        pending.RemoveAt(i);
                    }
                    else i++;
                }
                if (!on.Ready) continue;
                on.Update(t);
                Assert.AreEqual(on.DrawMs, on.RenderMs + on.LeadMs, 1e-9);
                Assert.That(on.LeadMs, Is.InRange(0.0, on.AheadMs + 1e-9));
                Assert.GreaterOrEqual(on.RenderMs, lastRender, "the stamp never runs backwards");
                Assert.GreaterOrEqual(on.DrawMs, lastDraw);
                if (on.LeadMs < on.AheadMs - 1) shrank = true;
                lastRender = on.RenderMs; lastDraw = on.DrawMs;
            }
            Assert.IsTrue(shrank, "the run reached the ceiling");
        }

        [Test]
        public void SamplingMakesNoGarbage()
        {
            var buffer = new SnapshotBuffer();
            var draw = new RemoteDraw(buffer) { Map = WallAt60() };
            for (uint k = 0; k < 40; k++)
            {
                var actors = new WireActor[14];
                for (int i = 0; i < actors.Length; i++)
                {
                    actors[i] = Player(100 + 3.4 * k + i * 5, 100 + 60 * i, 0, 3);
                    actors[i].Id = (uint)(i + 1);
                    if (i % 3 == 1) { actors[i].Kind = (byte)ActorKind.RegionalMob; actors[i].WalkTicks = 0; }
                }
                buffer.Add(new ServerMsg.Snapshot { Tick = k, Actors = actors });
            }
            var list = new List<InterpActor>();
            draw.Sample(1950, list);
            double b = AllocationProbe.PerCall(i => { draw.NoteWindup(1, 1900, 100, 0); draw.NoteAttackEnd(1); draw.Sample(1950 + (i % 40), list); });
            Assert.AreEqual(0.0, b);
            Assert.AreEqual(14, list.Count);
        }
    
        [Test]
        public void RenewedMobHandlesWithWindupsDoNotAccumulateFighterState()
        {
            // The server renews NPC observer handles; each windup under a handle that is never drawn again must not pin
            // that fighter's state for the rest of the match.
            var buffer = new SnapshotBuffer();
            var draw = new RemoteDraw(buffer) { Map = WallAt60() };
            var list = new List<InterpActor>();
            var count = typeof(RemoteDraw).GetField("_fighters", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            int most = 0;
            for (uint k = 0; k < 2000; k++)
            {
                var mob = Player(100 + k % 50, 300, 0, 0);
                mob.Id = 1000 + k;
                mob.Kind = (byte)ActorKind.RegionalMob;
                buffer.Add(new ServerMsg.Snapshot { Tick = k, Actors = new[] { mob } });
                draw.NoteWindup(mob.Id, k * 50.0, 400, (byte)WeaponId.Sword);
                draw.Sample(k * 50.0 - 60, list);
                most = System.Math.Max(most, ((System.Collections.IDictionary)count.GetValue(draw)).Count);
            }
            Assert.Less(most, 700, "fighter state stays bounded (about ForgetFrames + one forget period) under renewed handles");
        }
}
}
