using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-NET-POLL-ALLOC-1: bytes allocated by <c>Session.Poll()</c> (the Unity <c>netPoll</c> section wraps exactly that call) in a
    /// running match, measured per poll, per scenario, on the real 160 x 160 tile map size, at 60 frames a second with a 20 Hz delta
    /// stream. A poll that drains no message allocates nothing; one that drains a delta allocates nothing once the buffer holds
    /// its spare (the first 40 snapshots after any clear do not); a poll that sends a Ping allocates the Ping.
    /// <para>
    /// Measuring: the library's <see cref="AllocationProbe"/> reports a MEAN over thousands of calls of one repeated body, which
    /// hides a burst (the evidence is a few polls, or a window of polls, that allocate tens of KB). So each scenario here is
    /// replayed <see cref="Attempts"/> times from scratch on one thread and the figure for each poll is its MINIMUM over the
    /// attempts: the same defence as the probe's best-of (collector noise only adds bytes, at a random poll; a real allocation is
    /// in every attempt). Assertions are "at most" and never an exact figure.
    /// </para>
    /// </summary>
    public class PollAllocationTests
    {
        const int Attempts = 3;
        const double FrameMs = 1000.0 / 60.0;
        const double StartMs = 1000.0;
        const int Floor = MapBlob.Floor;

        /// <summary>
        /// The warm budget per poll, bytes (max over every poll after the warm-up). The clean scenarios measure 0 on a poll with a
        /// delta and the Ping poll (once a second: MakePing, Encode, Send) the largest; the budget is that Ping poll with headroom
        /// for a re-plan's waypoint array (24 B per waypoint, a dozen at most): 512 B. Chosen from the measurements recorded in the
        /// packet, not from a guess.
        /// </summary>
        const double WarmBudget = 512;
        /// <summary>
        /// The budget for a poll that also applies a State frame (collections rows are rebuilt per frame by design: measured 440 B
        /// with a standing wall, 544 B with the Ping) or re-plans a walk (a 21-waypoint path array is 504 B): 2 KB. The point of the
        /// red tests below is that a change of the WALL SET costs 27 KB standing and 259 to 669 KB walking, on one poll.
        /// </summary>
        const double StateBudget = 2048;
        /// <summary>Polls to skip as warm-up: the spare exists after 40 snapshots (2 s at 20 Hz); 3 s leaves a second of margin.</summary>
        const int WarmFrames = 180;
        const int RunFrames = 600;

        // ------------------------------------------------------------------------------------------------------------------
        // Rig

        sealed class Wire : ITransport, IArrivalTimedTransport
        {
            public event Action Opened;
            public event Action<byte[]> Received;
            public event Action<string> Closed;
            readonly Queue<byte[]> _in = new Queue<byte[]>(64);
            public readonly List<byte[]> Sent = new List<byte[]>(8192);
            public int Drained;
            public double ReceivedAgeMs { get { return 0; } }
            public void Connect(string url) { }
            public void Send(byte[] frame) { Sent.Add(frame); }
            public void Close() { if (Closed != null) Closed("closed"); }
            public void Poll() { while (_in.Count != 0) { Drained++; Received(_in.Dequeue()); } }
            public void Open() { Opened(); }
            public void Enqueue(byte[] frame) { _in.Enqueue(frame); }
            public void Drop(string why) { Closed(why); }
            public uint LastInputSeq()
            {
                for (int i = Sent.Count - 1; i >= 0; i--)
                    if (ClientMsg.Decode(Sent[i]) is ClientMsg.Input input) return input.Seq;
                return 0;
            }
        }

        /// <summary>An open 160 x 160 map (the size of the real arena, <c>MAP_TILES_W</c> x <c>MAP_TILES_H</c>) with a wall border.</summary>
        static byte[] OpenTiles()
        {
            var t = new byte[160 * 160];
            for (int i = 0; i < 160; i++) { t[i] = MapBlob.Wall; t[159 * 160 + i] = MapBlob.Wall; t[i * 160] = MapBlob.Wall; t[i * 160 + 159] = MapBlob.Wall; }
            return t;
        }

        /// <summary>Open map plus a serpentine: ten full-width walls four tiles apart, each with a one-tile-wide... gap alternating
        /// at the two ends, so a walk from the top corridor to the bottom one is about 1500 tiles and a few dozen waypoints.</summary>
        static MapBlob Serpentine()
        {
            var t = OpenTiles();
            for (int k = 0; k < 10; k++)
            {
                int y = 8 + 4 * k;
                for (int x = 1; x < 159; x++) t[y * 160 + x] = MapBlob.Wall;
                int gap = k % 2 == 0 ? 156 : 2;
                for (int g = 0; g < 3; g++) t[y * 160 + gap + g] = (byte)Floor;
            }
            return new MapBlob(160, 160, t);
        }

        static MapBlob Open() { return new MapBlob(160, 160, OpenTiles()); }

        /// <summary>A sealed 6 x 6 room in the open map: a goal in it is cut off from a walker outside (a different component).</summary>
        static MapBlob WithSealedRoom()
        {
            var t = OpenTiles();
            for (int i = 100; i <= 107; i++) { t[100 * 160 + i] = MapBlob.Wall; t[107 * 160 + i] = MapBlob.Wall; t[i * 160 + 100] = MapBlob.Wall; t[i * 160 + 107] = MapBlob.Wall; }
            return new MapBlob(160, 160, t);
        }

        static double Centre(int tile) { return tile * 32.0 + 16.0; }

        /// <summary>The fake server: it walks the same A* path the client plans (a separate pathfinder, the same code) at 170 u/s on
        /// the 50 ms grid, applies an order at the next tick and echoes its sequence, so the client's prediction never diverges in
        /// the clean scenarios. Other fighters walk on circles. Frames are built lazily, outside the measured poll.</summary>
        sealed class Server
        {
            public readonly MapBlob Map;
            readonly Pathfinder _pf = new Pathfinder();
            public double X, Y;
            PathPoint[] _path = new PathPoint[0];
            int _at;
            public uint Tick = 1;
            public int Actors;
            public byte MoveSeq;
            public uint Ack;
            bool _hasOrder; double _gx, _gy; byte _oseq; uint _oack;
            /// <summary>Extra faults a scenario injects: self position offset in u added to what is sent (a server that disagrees).</summary>
            public double Lie;

            public Server(MapBlob map, double x, double y, int actors) { Map = map; X = x; Y = y; Actors = actors; }

            public void Order(double gx, double gy, byte seq, uint ack) { _hasOrder = true; _gx = gx; _gy = gy; _oseq = seq; _oack = ack; }

            WireSelf Self()
            {
                return new WireSelf { Id = 3, X = (float)(X + Lie), Y = (float)Y, Hp = 100, MaxHp = 100, Status = 1, MoveSeq = MoveSeq };
            }

            WireActor[] Others(uint tick)
            {
                var a = new WireActor[Actors];
                for (int k = 0; k < Actors; k++)
                    a[k] = new WireActor { Id = (uint)(100 + k), Faction = (byte)(k % 5), Status = 1, X = (float)(1000 + 40 * k + 30 * Math.Cos(tick * 0.1 + k)), Y = (float)(1000 + 40 * k + 30 * Math.Sin(tick * 0.1 + k)), HpPct = 100 };
                return a;
            }

            void Advance()
            {
                Tick++;
                if (_hasOrder)
                {
                    _hasOrder = false;
                    _path = _pf.FindPath(Map, X, Y, _gx, _gy, Footprint.Legacy);
                    _at = 0; MoveSeq = _oseq; Ack = _oack;
                }
                double budget = 170.0 * 0.05;
                while (budget > 0 && _at < _path.Length)
                {
                    double dx = _path[_at].X - X, dy = _path[_at].Y - Y, d = Math.Sqrt(dx * dx + dy * dy);
                    if (d <= budget) { X = _path[_at].X; Y = _path[_at].Y; budget -= d; _at++; }
                    else { X += dx / d * budget; Y += dy / d * budget; budget = 0; }
                }
            }

            public byte[] Full()
            {
                return new ServerMsg.Snapshot { Tick = Tick, Phase = 1, AckInput = Ack, Me = Self(), Actors = Others(Tick) }.Encode();
            }

            /// <summary>The next tick as a delta against the previous one: the self's position, the sequence echo and every other
            /// fighter moved. <paramref name="swing"/> adds or removes one fighter (a new fighter carries a full row).</summary>
            public byte[] NextDelta(bool swing = false)
            {
                Advance();
                var d = new ServerMsg.SnapshotDelta { Tick = Tick, BaseTick = Tick - 1, AckInput = Ack, HasSelf = true };
                var me = Self();
                d.Me = new SelfDelta { Mask = SelfDelta.FX | SelfDelta.FY | SelfDelta.FMoveSeq, X = me.X, Y = me.Y, MoveSeq = MoveSeq };
                int n = Actors + (swing ? 1 : 0);
                d.Changed = new ActorDelta[n];
                d.ChangedCount = n;
                var o = Others(Tick);
                for (int k = 0; k < Actors; k++)
                    d.Changed[k] = new ActorDelta { Id = o[k].Id, Mask = ActorDelta.MX | ActorDelta.MY, X = (ushort)o[k].X, Y = (ushort)o[k].Y, Status = 1 };
                if (swing)
                    d.Changed[Actors] = new ActorDelta { Id = 5000, Mask = ActorDelta.MNew, X = 900, Y = 900, Status = 1 };
                return d.Encode();
            }

            /// <summary>The delta that removes the fighter a swing added (so the count alternates on every snapshot).</summary>
            public byte[] NextDeltaDeparting()
            {
                Advance();
                var d = new ServerMsg.SnapshotDelta { Tick = Tick, BaseTick = Tick - 1, AckInput = Ack, HasSelf = true };
                var me = Self();
                d.Me = new SelfDelta { Mask = SelfDelta.FX | SelfDelta.FY | SelfDelta.FMoveSeq, X = me.X, Y = me.Y, MoveSeq = MoveSeq };
                d.Changed = new ActorDelta[Actors];
                d.ChangedCount = Actors;
                var o = Others(Tick);
                for (int k = 0; k < Actors; k++)
                    d.Changed[k] = new ActorDelta { Id = o[k].Id, Mask = ActorDelta.MX | ActorDelta.MY, X = (ushort)o[k].X, Y = (ushort)o[k].Y, Status = 1 };
                d.Departed = new uint[] { 5000 };
                d.DepartedCount = 1;
                return d.Encode();
            }
        }

        sealed class Rig
        {
            public readonly Wire T = new Wire();
            public readonly Session S;
            public readonly Server Srv;
            public double Now = StartMs;
            readonly MapBlob _map;

            public Rig(MapBlob map, double x, double y, int actors)
            {
                _map = map;
                Srv = new Server(map, x, y, actors);
                S = new Session(T, new SessionOptions { Url = "ws://x/ws", Profile = "p", Now = () => Now });
                S.Connect(); T.Open();
                Welcome();
            }

            public void Welcome()
            {
                T.Enqueue(new ServerMsg.Welcome { Protocol = Protocol.Wire.ProtocolVersion, MatchId = new byte[16], PlayerId = 3, Session = new byte[16], TickRate = 20, Tick = Srv.Tick, ServerMs = Srv.Tick * 50.0, Map = _map }.Encode());
                T.Enqueue(Srv.Full());
                S.Poll();
            }

            /// <summary>Sends an order the way the Unity input does after the poll: queue it, flush it, and tell the fake server which
            /// sequence and frame to echo at its next tick.</summary>
            public void Move(double gx, double gy)
            {
                byte seq = S.OrderMove((float)gx, (float)gy);
                S.FlushInput();
                Srv.Order(gx, gy, seq, T.LastInputSeq());
            }
        }

        /// <summary>One scenario: its per-frame hook runs BEFORE the poll (feeds frames, unmeasured) and AFTER it (input, unmeasured).</summary>
        sealed class Scenario
        {
            public Func<Rig> Build;
            public Action<Rig, int> Feed;
            public Action<Rig, int> Act;
            public int Frames = RunFrames;
        }

        /// <summary>The standard feed: a delta every third frame (20 Hz at 60 fps).</summary>
        static void SteadyFeed(Rig r, int f) { if (f % 3 == 0) r.T.Enqueue(r.Srv.NextDelta()); }

        sealed class Result
        {
            public double[] Bytes;
            public int[] Drained;
            public double WarmMax, WarmMean, AllMax;
            public int WarmMaxFrame;
            public int OverBudget;
            public string Describe(string name)
            {
                return string.Format("{0}: warm mean {1:F1} B/poll, warm max {2:F0} B (poll {3}), polls over {4} B: {5}, whole-run max {6:F0} B",
                    name, WarmMean, WarmMax, WarmMaxFrame, WarmBudget, OverBudget, AllMax);
            }
        }

        /// <summary>Replays the scenario <see cref="Attempts"/> times and keeps each poll's minimum.</summary>
        static Result Measure(Scenario sc, int warmFrames = WarmFrames, double budget = WarmBudget)
        {
            double[] best = null; int[] drained = null;
            for (int a = 0; a < Attempts; a++)
            {
                var r = sc.Build();
                var bytes = new double[sc.Frames]; var dr = new int[sc.Frames];
                GC.GetAllocatedBytesForCurrentThread();
                GC.Collect();
                for (int f = 0; f < sc.Frames; f++)
                {
                    r.Now = StartMs + 50 + f * FrameMs;
                    if (sc.Feed != null) sc.Feed(r, f);
                    int before = r.T.Drained;
                    long b0 = GC.GetAllocatedBytesForCurrentThread();
                    r.S.Poll();
                    long b1 = GC.GetAllocatedBytesForCurrentThread();
                    bytes[f] = b1 - b0; dr[f] = r.T.Drained - before;
                    if (sc.Act != null) sc.Act(r, f);
                }
                if (best == null) { best = bytes; drained = dr; }
                else for (int f = 0; f < bytes.Length; f++) best[f] = Math.Min(best[f], bytes[f]);
            }
            var res = new Result { Bytes = best, Drained = drained };
            double sum = 0; int n = 0;
            for (int f = 0; f < best.Length; f++)
            {
                res.AllMax = Math.Max(res.AllMax, best[f]);
                if (f < warmFrames) continue;
                sum += best[f]; n++;
                if (best[f] > res.WarmMax) { res.WarmMax = best[f]; res.WarmMaxFrame = f; }
                if (best[f] > budget) res.OverBudget++;
            }
            res.WarmMean = n == 0 ? 0 : sum / n;
            return res;
        }

        static void Report(string name, Result r)
        {
            TestContext.Progress.WriteLine("POLLALLOC " + r.Describe(name));
            var big = new System.Text.StringBuilder();
            for (int f = 0; f < r.Bytes.Length; f++) if (r.Bytes[f] > 200) big.Append(f).Append(":").Append(r.Bytes[f]).Append("/d").Append(r.Drained[f]).Append(" ");
            TestContext.Progress.WriteLine("POLLALLOC   polls over 200 B: " + big);
        }

        static void AssertWarm(string name, Result r, double budget = WarmBudget)
        {
            Report(name, r);
            Assert.LessOrEqual(r.WarmMax, budget, name + ": a warm poll allocated more than " + budget + " B; " + r.Describe(name));
        }

        // ------------------------------------------------------------------------------------------------------------------
        // Scenarios

        [Test]
        public void GuardAnIdleSeatReceivingSteadyDeltasAllocatesNothingBeyondThePing()
        {
            var r = Measure(new Scenario { Build = () => new Rig(Open(), 800, 800, 40), Feed = SteadyFeed });
            AssertWarm("idle seat, 40 fighters, deltas at 20 Hz", r);
            // A poll that drains no message and sends no Ping allocates nothing at all.
            int quiet = 0;
            for (int f = WarmFrames; f < r.Bytes.Length; f++) if (r.Drained[f] == 0 && r.Bytes[f] <= 16) quiet++;
            Assert.Greater(quiet, 100, "most polls drain nothing and allocate nothing");
        }

        [Test]
        public void GuardAWalkAlongAPathOfManyTilesAllocatesWithinTheBudget()
        {
            var sc = new Scenario
            {
                Build = () => new Rig(Serpentine(), Centre(4), Centre(5), 40),
                Feed = SteadyFeed,
                Frames = 900,
                Act = (r, f) => { if (f == 10) r.Move(Centre(4), Centre(55)); }
            };
            var r0 = sc.Build();
            var path = new Pathfinder().FindPath(r0.Srv.Map, Centre(4), Centre(5), Centre(4), Centre(55), Footprint.Legacy);
            Assert.GreaterOrEqual(path.Length, 12, "the serpentine makes a walk of many waypoints");
            var r = Measure(sc, 150);
            AssertWarm("walk along a serpentine, 40 fighters", r);
        }

        [Test]
        public void GuardAnOrderToAPointInsideAWallHeldForSecondsAllocatesWithinTheBudget()
        {
            // The goal snaps to the nearest floor tile; the walker arrives and then the order stays "held" (the click is repeated).
            var sc = new Scenario
            {
                Build = () => new Rig(Open(), 800, 800, 40),
                Feed = SteadyFeed,
                Act = (r, f) => { if (f == 10 || f > 10 && f % 30 == 0) r.Move(Centre(0) + 4, Centre(80)); } // tile 0 of the border is wall
            };
            AssertWarm("goal inside a wall, re-sent every 0.5 s", Measure(sc, 150));
        }

        [Test]
        public void GuardAnOrderToAPointCutOffFromTheWalkerHeldForSecondsAllocatesWithinTheBudget()
        {
            var sc = new Scenario
            {
                Build = () => new Rig(WithSealedRoom(), 800, 800, 40),
                Feed = SteadyFeed,
                Act = (r, f) => { if (f == 10 || f > 10 && f % 30 == 0) r.Move(Centre(103), Centre(103)); }
            };
            AssertWarm("goal in a sealed room, re-sent every 0.5 s", Measure(sc, 150));
        }

        [Test]
        public void GuardAServerThatDisagreesWithThePredictionForcesNoUnboundedPollGarbage()
        {
            // Every snapshot is 200 u from the model: reconciliation snaps and re-plans on each one, the heaviest thing Poll can do.
            var sc = new Scenario
            {
                Build = () => new Rig(Serpentine(), Centre(4), Centre(5), 40),
                Feed = (r, f) => { r.Srv.Lie = f % 6 < 3 ? 200 : 0; SteadyFeed(r, f); },
                Frames = 900,
                Act = (r, f) => { if (f == 10) r.Move(Centre(4), Centre(55)); }
            };
            AssertWarm("walk while every snapshot snaps the model and re-plans", Measure(sc, 150), StateBudget);
        }

        [Test]
        public void GuardKeyboardSteeringReSentEveryHundredMillisecondsAllocatesWithinTheBudget()
        {
            // P-NET-STEER-1 / MatchClient SteerRepeatMs: a held key re-sends a Move 64 u ahead every 100 ms (6 frames).
            var sc = new Scenario
            {
                Build = () => new Rig(Open(), 800, 800, 40),
                Feed = SteadyFeed,
                Act = (r, f) =>
                {
                    if (f < 10 || f % 6 != 0) return;
                    double heading = (f / 120) * 0.7;
                    r.Move(r.S.Motion.DrawnX + 64 * Math.Cos(heading), r.S.Motion.DrawnY + 64 * Math.Sin(heading));
                }
            };
            AssertWarm("held key re-sent every 100 ms", Measure(sc, 150));
        }

        [Test]
        public void GuardAFollowOrderOnATargetThatKeepsMovingAllocatesWithinTheBudget()
        {
            // A follow / attack-chase re-targets the walker's goal to the moving target's position every 200 ms (12 frames).
            var sc = new Scenario
            {
                Build = () => new Rig(Open(), 800, 800, 40),
                Feed = SteadyFeed,
                Act = (r, f) =>
                {
                    if (f < 10 || f % 12 != 0) return;
                    double t = f / 60.0;
                    r.Move(1400 + 300 * Math.Cos(t), 1400 + 300 * Math.Sin(t));
                }
            };
            AssertWarm("follow a moving target, re-sent every 200 ms", Measure(sc, 150));
        }

        [Test]
        public void GuardFiveSnapshotsDrainedInOnePollAllocateWithinTheBudget()
        {
            // A hitch of 250 ms every second: five deltas are queued and one poll drains them all.
            var sc = new Scenario
            {
                Build = () => new Rig(Open(), 800, 800, 40),
                Feed = (r, f) =>
                {
                    int s = f % 60;
                    if (s == 59) for (int i = 0; i < 5; i++) r.T.Enqueue(r.Srv.NextDelta());
                    else if (s >= 45 && s < 59) { /* the stall: nothing arrives, the poll still runs */ }
                    else if (f % 3 == 0 && s < 45) r.T.Enqueue(r.Srv.NextDelta());
                }
            };
            var r = Measure(sc);
            int bursts = 0; for (int f = WarmFrames; f < r.Bytes.Length; f++) if (r.Drained[f] >= 5) bursts++;
            Assert.GreaterOrEqual(bursts, 5, "the scenario drains five messages in one poll");
            AssertWarm("five deltas drained in one poll, once a second", r);
        }

        [Test]
        public void GuardAnActorCountThatChangesOnEverySnapshotAllocatesWithinTheBudget()
        {
            // One fighter enters on one snapshot and leaves on the next, so the actor array length differs every time.
            int n = 0;
            var sc = new Scenario
            {
                Build = () => { n = 0; return new Rig(Open(), 800, 800, 40); },
                Feed = (r, f) => { if (f % 3 == 0) r.T.Enqueue((n++ & 1) == 0 ? r.Srv.NextDelta(true) : r.Srv.NextDeltaDeparting()); }
            };
            AssertWarm("fighter count changes on every snapshot, 40 fighters", Measure(sc, WarmFrames, StateBudget), StateBudget);
        }

        /// <summary>The two seconds after a Welcome (a resume, or a first join): the buffer has no spare for 40 snapshots. A bounded
        /// burst is allowed there: at most <see cref="BurstPerPoll"/> per poll for at most <see cref="BurstFrames"/> frames after the
        /// Welcome (40 snapshots at 20 Hz = 2 s = 120 frames, plus 12 frames of margin), then the warm budget.</summary>
        const double BurstPerPoll = 8192;
        const int BurstFrames = 132;

        [Test]
        public void GuardTheSecondsAfterAWelcomeAreABoundedBurstThenQuiet()
        {
            var sc = new Scenario
            {
                Build = () =>
                {
                    var r = new Rig(Open(), 800, 800, 40);
                    for (int i = 0; i < 100; i++) { r.Now += 50; r.T.Enqueue(r.Srv.NextDelta()); r.S.Poll(); }
                    r.T.Drop("lost");                       // the connection drops: Reconnecting
                    r.S.RetryNow();
                    return r;
                },
                Feed = (r, f) =>
                {
                    if (f == 3) { r.T.Open(); }
                    if (f == 4) { r.Welcome(); }
                    if (f > 4 && f % 3 == 0) r.T.Enqueue(r.Srv.NextDelta());
                },
                Frames = 480
            };
            var r0 = sc.Build();
            Assert.AreEqual(SessionState.Reconnecting, r0.S.State, "the scenario resumes after a drop");
            var r = Measure(sc, 4 + BurstFrames + 1);
            Report("after a Welcome (warm part starts after the burst)", r);
            double burstMax = 0;
            for (int f = 5; f < 4 + BurstFrames; f++) burstMax = Math.Max(burstMax, r.Bytes[f]);
            TestContext.Progress.WriteLine("POLLALLOC burst after a Welcome: max {0:F0} B per poll in the first {1} frames", burstMax, BurstFrames);
            Assert.LessOrEqual(burstMax, BurstPerPoll, "the burst after a Welcome is bounded");
            Assert.LessOrEqual(r.WarmMax, WarmBudget, "quiet again after the burst; " + r.Describe("resume"));
        }

        static WireField Wall(double startedAt, uint firstTile)
        {
            return new WireField { Kind = FieldsState.KindWall, Ability = 0, Owner = 7, Subject = 7, StartedAt = startedAt, EndsAt = startedAt + 4000, Tiles = new uint[] { firstTile, firstTile + 1, firstTile + 2 } };
        }

        static byte[] StateWalls(uint tick, params WireField[] walls)
        {
            return new ServerMsg.State { Tick = tick, Items = new StateItem[] { new FieldsState { Rows = walls } } }.Encode();
        }

        [Test]
        public void GuardStateMessagesWithTheSameWallsEveryTickAllocateWithinTheBudget()
        {
            // A fight with a standing Frost Wall: a State frame (fields) arrives with every snapshot, carrying the same wall.
            // SetWalls documents that the same set again allocates nothing; Collections.Apply still builds its rows.
            var sc = new Scenario
            {
                Build = () => new Rig(Open(), 800, 800, 40),
                Feed = (r, f) =>
                {
                    if (f % 3 != 0) return;
                    r.T.Enqueue(StateWalls(r.Srv.Tick + 1, Wall(5000, 80 * 160 + 80)));
                    r.T.Enqueue(r.Srv.NextDelta());
                }
            };
            AssertWarm("a State frame with the same standing wall at 20 Hz", Measure(sc, WarmFrames, StateBudget), StateBudget);
        }

        [Test]
        public void TheWallSetChangingEveryTickWhileStandingAllocatesWithinTheBudget()
        {
            // Walls rise and fall: the set differs on every State, so SetWalls rebuilds the walk map (clone of the 25,600 tile map and a
            // new MapBlob with its hashes) on each, and the pathfinder rebuilds its tables for the new map object on its next query.
            var sc = new Scenario
            {
                Build = () => new Rig(Open(), 800, 800, 40),
                Feed = (r, f) =>
                {
                    if (f % 3 != 0) return;
                    int k = (int)r.Srv.Tick;
                    r.T.Enqueue(StateWalls(r.Srv.Tick + 1, Wall(5000 + k, (uint)(80 * 160 + 40 + k % 40)), Wall(5000 + k + 1, (uint)(90 * 160 + 40 + k % 40))));
                    r.T.Enqueue(r.Srv.NextDelta());
                }
            };
            AssertWarm("the wall set changes on every State, standing", Measure(sc, WarmFrames, StateBudget), StateBudget);
        }

        [Test]
        public void TheWallSetChangingEveryTickWhileWalkingAllocatesWithinTheBudget()
        {
            var sc = new Scenario
            {
                Build = () => new Rig(Open(), 800, 800, 40),
                Feed = (r, f) =>
                {
                    if (f % 3 != 0) return;
                    int k = (int)r.Srv.Tick;
                    r.T.Enqueue(StateWalls(r.Srv.Tick + 1, Wall(5000 + k, (uint)(25 * 160 + 40 + k % 40))));
                    r.T.Enqueue(r.Srv.NextDelta());
                },
                Frames = 900,
                Act = (r, f) => { if (f % 240 == 10) r.Move(800 + (f / 240 % 2 == 0 ? 1 : -1) * 1500, 800); }
            };
            AssertWarm("the wall set changes on every State, walking a long straight line", Measure(sc, WarmFrames, StateBudget), StateBudget);
        }

        /// <summary>The shape of a fight with Frost Walls: one wall rises every 500 ms and stands for 4 s, so the set of standing walls
        /// changes about four times a second (a rise, then a fall), with a State frame carrying the set on every snapshot.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void FrostWallsRisingAndFallingEveryHalfSecondAllocateWithinTheBudget(bool walking)
        {
            var sc = new Scenario
            {
                Build = () => new Rig(Open(), 800, 800, 40),
                Feed = (r, f) =>
                {
                    if (f % 3 != 0) return;
                    int born = f / 30;                      // the newest wall's index: a new one every 30 frames
                    var rows = new List<WireField>();
                    for (int k = Math.Max(0, born - 7); k <= born; k++) rows.Add(Wall(5000 + k * 500.0, (uint)(25 * 160 + 20 + (k * 7) % 100)));
                    r.T.Enqueue(StateWalls(r.Srv.Tick + 1, rows.ToArray()));
                    r.T.Enqueue(r.Srv.NextDelta());
                },
                Frames = 900,
                Act = (r, f) => { if (walking && f % 240 == 10) r.Move(800 + (f / 240 % 2 == 0 ? 1 : -1) * 1500, 800); }
            };
            AssertWarm(walking ? "walls rise and fall four times a second, walking" : "walls rise and fall four times a second, standing",
                Measure(sc, WarmFrames, StateBudget), StateBudget);
        }

        [Test]
        public void GuardDestroyedTerrainChangingEveryTickAllocatesWithinTheBudget()
        {
            var sc = new Scenario
            {
                Build = () => new Rig(Serpentine(), Centre(4), Centre(5), 40),
                Feed = (r, f) =>
                {
                    if (f % 3 != 0) return;
                    int k = (int)r.Srv.Tick % 100;
                    var cells = new uint[k];
                    for (int i = 0; i < k; i++) cells[i] = (uint)(8 * 160 + 10 + i); // the first serpentine wall, ascending
                    r.T.Enqueue(new ServerMsg.State { Tick = r.Srv.Tick + 1, Items = new StateItem[] { new DestroyedTerrainState { Cells = cells } } }.Encode());
                    r.T.Enqueue(r.Srv.NextDelta());
                },
                Frames = 900,
                Act = (r, f) => { if (f == 10) r.Move(Centre(4), Centre(55)); }
            };
            // A terrain change is a rare event (a wall-buster), not a per-frame cost: pinned at today's 34 KB, never more than 64 KB.
            AssertWarm("the destroyed terrain changes on every State, walking", Measure(sc, WarmFrames, 65536), 65536);
        }

        // ------------------------------------------------------------------------------------------------------------------
        // The other triggers of a walk-map rebuild

        static MapBlob GatedOpen()
        {
            var gate = new[] { new MapTile(80, 70), new MapTile(80, 71), new MapTile(80, 72) };
            return Open().WithPublic(null, new MapColiseum(81, 0, 70, 160, new[] { new MapGate(3, gate) }), null, null, null);
        }

        static byte[] StateGatesAndWalls(uint tick, bool locked, params WireField[] walls)
        {
            return new ServerMsg.State
            {
                Tick = tick,
                Items = new StateItem[] { new FieldsState { Rows = walls }, new ArenaState { Value = new WireArena { InColiseum = locked, GatesLocked = locked } } }
            }.Encode();
        }

        /// <summary>Gates closing and opening every 0.5 s with no wall standing: SetCollisionMap swaps between two cached maps
        /// (the Welcome map and its closed-gate copy), so the poll itself allocates nothing large; the pathfinder's rebuild is
        /// deferred to the next Order, which the Unity input makes after the poll (measured 0.6 MB there, outside netPoll).</summary>
        [Test]
        public void GuardGatesTogglingWithoutWallsAllocateWithinTheBudget()
        {
            var sc = new Scenario
            {
                Build = () => new Rig(GatedOpen(), 800, 800, 40),
                Feed = (r, f) =>
                {
                    if (f % 3 != 0) return;
                    r.T.Enqueue(StateGatesAndWalls(r.Srv.Tick + 1, f / 30 % 2 == 1));
                    r.T.Enqueue(r.Srv.NextDelta());
                },
                Frames = 900,
                Act = (r, f) => { if (f % 240 == 10) r.Move(800 + (f / 240 % 2 == 0 ? 1 : -1) * 1500, 800); }
            };
            AssertWarm("gates toggle every 0.5 s, no walls", Measure(sc, WarmFrames, StateBudget), StateBudget);
        }

        /// <summary>The same toggle with a Frost Wall standing: SetCollisionMap calls RebuildWalkMap, which clones the 25.6 KB tile map
        /// again on every toggle: the same allocation as a wall-set change, so the same budget.</summary>
        [Test]
        public void GatesTogglingWithAWallStandingAllocateWithinTheBudget()
        {
            var sc = new Scenario
            {
                Build = () => new Rig(GatedOpen(), 800, 800, 40),
                Feed = (r, f) =>
                {
                    if (f % 3 != 0) return;
                    r.T.Enqueue(StateGatesAndWalls(r.Srv.Tick + 1, f / 30 % 2 == 1, Wall(5000, 25 * 160 + 20)));
                    r.T.Enqueue(r.Srv.NextDelta());
                },
                Frames = 900
            };
            AssertWarm("gates toggle every 0.5 s, a wall standing", Measure(sc, WarmFrames, StateBudget), StateBudget);
        }

        /// <summary>The same destroyed cells again on every State (the usual case after a wall-buster): <c>Collections.Apply</c> compares
        /// the cell list and keeps the terrain map, so only the rows are rebuilt.</summary>
        [Test]
        public void GuardTheSameDestroyedTerrainOnEveryStateAllocatesWithinTheBudget()
        {
            var sc = new Scenario
            {
                Build = () => new Rig(Serpentine(), Centre(4), Centre(5), 40),
                Feed = (r, f) =>
                {
                    if (f % 3 != 0) return;
                    var cells = new uint[40];
                    for (int i = 0; i < 40; i++) cells[i] = (uint)(8 * 160 + 10 + i);
                    r.T.Enqueue(new ServerMsg.State { Tick = r.Srv.Tick + 1, Items = new StateItem[] { new DestroyedTerrainState { Cells = cells } } }.Encode());
                    r.T.Enqueue(r.Srv.NextDelta());
                }
            };
            AssertWarm("the same destroyed cells on every State", Measure(sc, WarmFrames, StateBudget), StateBudget);
        }

        // ------------------------------------------------------------------------------------------------------------------
        // Stale retained state

        static MapTile[] Column(int x, int y0, int count)
        {
            var t = new MapTile[count];
            for (int i = 0; i < count; i++) t[i] = new MapTile((ushort)x, (ushort)(y0 + i));
            return t;
        }

        static MapTile[] Row(int y, int x0, int count)
        {
            var t = new MapTile[count];
            for (int i = 0; i < count; i++) t[i] = new MapTile((ushort)(x0 + i), (ushort)y);
            return t;
        }

        static AreaView[] Fields(int id, params MapTile[][] walls)
        {
            var rows = new AreaView[walls.Length];
            for (int i = 0; i < walls.Length; i++)
                rows[i] = new AreaView { Kind = AreaKind.Wall, Tiles = walls[i], Key = new AreaKey(1000.0 * id + i, (uint)(walls[i][0].Y * 160 + walls[i][0].X)) };
            return rows;
        }

        /// <summary>The expected walk map, built here and independently of the library: the base tiles with every wall tile that is
        /// FLOOR turned LOW (rh-sim <c>with_low</c>).</summary>
        static MapBlob Expected(MapBlob map, AreaView[] walls)
        {
            var tiles = (byte[])map.Tiles.Clone();
            foreach (var w in walls)
                foreach (var c in w.Tiles)
                    if (tiles[c.Y * map.W + c.X] == MapBlob.Floor) tiles[c.Y * map.W + c.X] = MapBlob.Low;
            return new MapBlob(map.W, map.H, tiles);
        }

        /// <summary>One replay: set <paramref name="before"/>, cut to a start pose, order a walk, step half a second, set
        /// <paramref name="after"/> (a rising wall may block the remaining walk, which re-plans from the model), step half a second more.
        /// Returns the pose at the middle and the end. The same code runs on a long-lived and on a fresh instance.</summary>
        static double[] Replay(LocalMotion m, AreaView[] before, AreaView[] after, byte cut, byte seq, double now, double goalX, double goalY)
        {
            m.SetWalls(before);
            m.OnSnapshot(new WireSelf { Id = 3, X = 400, Y = 800, Hp = 10, MaxHp = 10, Status = 1, DisplaceSeq = cut }, now);
            m.Order((float)goalX, (float)goalY, seq, now);
            for (int k = 1; k <= 10; k++) m.Step(now + 50.0 * k);
            double mx = m.DrawnX, my = m.DrawnY;
            m.SetWalls(after);
            for (int k = 11; k <= 20; k++) m.Step(now + 50.0 * k);
            return new double[] { mx, my, m.DrawnX, m.DrawnY, m.VisualX, m.VisualY };
        }

        /// <summary>
        /// Guard against a fix that keeps one tile array or the pathfinder's tables across wall changes and then serves stale answers.
        /// ONE long-lived <see cref="LocalMotion"/> and one long-lived <see cref="Pathfinder"/> (the way Unity steering holds its own, reading
        /// <c>WalkMap</c>) go through 60 changes of the standing-wall set (none, a column that cuts the direct route, another column, two
        /// walls, a row, none again), and after every change are compared EXACTLY with instances built fresh for the same map and set:
        /// the walk map tiles (against an independent construction), every waypoint of several planned paths, <c>SegmentClear</c> for
        /// probes across where walls rise and fall, and the predicted pose after a second of walking, with a wall rising or falling
        /// mid-walk. <c>WalkBlocked</c> and the motion's own path are private, so they are compared through what they decide: the pose
        /// after the re-plan. A pathfinder that holds tables for an earlier tile array fails the path and probe comparisons; a
        /// walk map mutated in place under the same object fails the long-lived pathfinder's answers.
        /// </summary>
        [Test]
        public void GuardAWallSetThatChangesAgainAndAgainNeverServesAnAnswerFromAnEarlierSet()
        {
            var map = Open();
            var sets = new[]
            {
                new AreaView[0],
                Fields(1, Column(25, 15, 30)),                       // cuts the direct route along row 25 (y 800)
                Fields(2, Column(30, 10, 30)),                       // a different column further on
                Fields(3, Column(25, 15, 30), Row(45, 20, 25)),      // two walls
                Fields(4, Row(25, 20, 20)),                          // a row ON the direct route, no cut
                new AreaView[0],
                Fields(5, Column(25, 22, 6)),                        // a short column the walk can pass round closely
            };
            var probes = new[] { new[] { 400.0, 800, 1200, 800 }, new[] { 400.0, 700, 1200, 1000 }, new[] { 700.0, 400, 700, 1500 }, new[] { 500.0, 1450, 1500, 1450 }, new[] { 850.0, 810, 1000, 810 } };
            var goals = new[] { new[] { 1200.0, 800 }, new[] { 900.0, 1100 }, new[] { 1500.0, 400 } };

            var longMotion = new LocalMotion(map);
            var longPf = new Pathfinder();
            double now = 0;
            int checks = 0;
            for (int round = 0; round < 60; round++)
            {
                var before = sets[round % sets.Length];
                var after = sets[(round * 3 + 1) % sets.Length];
                byte cut = (byte)(round + 1), seq = (byte)(round % 250 + 1);
                double[] goal = goals[round % goals.Length];
                now += 5000;
                var fresh = new LocalMotion(map);
                var got = Replay(longMotion, before, after, cut, seq, now, goal[0], goal[1]);
                var want = Replay(fresh, before, after, cut, seq, now, goal[0], goal[1]);
                for (int i = 0; i < got.Length; i++) Assert.AreEqual(want[i], got[i], "round " + round + ": walked pose component " + i);

                // The walk map of the long-lived instance (it now holds `after`) against an independent build.
                var expectedMap = Expected(map, after);
                Assert.AreEqual(expectedMap.Tiles, longMotion.WalkMap.Tiles, "round " + round + ": walk map tiles");
                Assert.AreEqual(expectedMap.Tiles, fresh.WalkMap.Tiles, "round " + round + ": fresh walk map tiles");

                var freshPf = new Pathfinder();
                foreach (var p in probes)
                {
                    Assert.AreEqual(freshPf.SegmentClear(expectedMap, p[0], p[1], p[2], p[3], 12.0), longPf.SegmentClear(longMotion.WalkMap, p[0], p[1], p[2], p[3], 12.0),
                        "round " + round + ": SegmentClear probe");
                    checks++;
                }
                foreach (var g in goals)
                {
                    var wantPath = freshPf.FindPath(expectedMap, 400, 800, g[0], g[1], Footprint.Legacy);
                    var gotPath = longPf.FindPath(longMotion.WalkMap, 400, 800, g[0], g[1], Footprint.Legacy);
                    Assert.AreEqual(wantPath.Length, gotPath.Length, "round " + round + ": path length to " + g[0] + "," + g[1]);
                    for (int i = 0; i < wantPath.Length; i++)
                    {
                        Assert.AreEqual(wantPath[i].X, gotPath[i].X, "round " + round + ": waypoint " + i + " x");
                        Assert.AreEqual(wantPath[i].Y, gotPath[i].Y, "round " + round + ": waypoint " + i + " y");
                    }
                    checks++;
                }
            }
            // The sets really differ in what they decide: the cut column makes a detour, the empty set a straight line.
            var cutPath = new Pathfinder().FindPath(Expected(map, sets[1]), 400, 800, 1200, 800, Footprint.Legacy);
            var openPath = new Pathfinder().FindPath(Expected(map, sets[0]), 400, 800, 1200, 800, Footprint.Legacy);
            Assert.Greater(cutPath.Length, openPath.Length, "a wall across the route forces a detour; no wall is a straight line");
            Assert.AreEqual(1, openPath.Length);
            Assert.GreaterOrEqual(checks, 60 * (probes.Length + goals.Length));
        }

        // ------------------------------------------------------------------------------------------------------------------
        // The parts of a poll, called separately

        [Test]
        public void GuardThePartsOfAPollAreMeasuredSeparately()
        {
            var r = new Rig(Serpentine(), Centre(4), Centre(5), 40);
            r.Move(Centre(4), Centre(55));
            for (int f = 0; f < 240; f++) { r.Now += FrameMs; if (f % 3 == 0) r.T.Enqueue(r.Srv.NextDelta()); r.S.Poll(); }
            Assert.IsTrue(r.S.Clock.Ready);
            double clock = Min(() => { r.Now += FrameMs; r.S.Clock.Update(r.Now); });
            double motion = Min(() => { r.Now += FrameMs; r.S.Motion.Step(r.Now); });
            double nothing = Min(() => { r.Now += FrameMs; r.S.Poll(); });
            TestContext.Progress.WriteLine("POLLALLOC parts: Clock.Update {0} B, Motion.Step {1} B, a whole poll draining nothing {2} B", clock, motion, nothing);
            Assert.AreEqual(0.0, clock, "Clock.Update allocates nothing");
            Assert.AreEqual(0.0, motion, "Motion.Step allocates nothing");
            Assert.LessOrEqual(nothing, 128, "a poll that drains nothing allocates at most one Ping");
        }

        /// <summary>Best of five windows of 200 calls on this thread: bytes per call.</summary>
        static double Min(Action body)
        {
            double best = double.MaxValue;
            for (int run = 0; run < 5; run++)
            {
                for (int i = 0; i < 50; i++) body();
                GC.Collect();
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 200; i++) body();
                best = Math.Min(best, (GC.GetAllocatedBytesForCurrentThread() - b0) / 200.0);
            }
            return best;
        }
    }
}
