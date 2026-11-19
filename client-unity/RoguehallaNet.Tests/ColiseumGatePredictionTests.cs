// P-COLISEUM-GATES-1 oracle: while the coliseum gates are locked the self walk keeps predicting on the server's body map
// (rh-sim MatchStatic::locked_map = TileMap::closed_gates, map.rs:168-194; with standing walls barriers::sync/body_map,
// barriers.rs:337-358, = closed_gates().with_low(wall cells)), as legacy predicts on withGates(map) (RaidScene.ts:985-989,
// 1045; shared/src/map.ts:324-336). A map without coliseum geometry keeps the suspend path
// (PredictionPollTests.LockedGatesSuspendOnlyPredictionUntilOpenMapIsAuthoritativeAgain).
// The closed-gate drawing reads an engine-free helper, Roguehalla.Net.Ui.ColiseumGates.ClosedTiles (legacy MapView.drawGates,
// MapView.ts:572-618); it is located by reflection so this file compiles before the helper exists.
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    public class ColiseumGatePredictionTests
    {
        sealed class QueuedTransport : ITransport
        {
            public event Action Opened;
            public event Action<byte[]> Received;
            public event Action<string> Closed;
            readonly Queue<byte[]> incoming = new Queue<byte[]>();
            public void Connect(string url) { }
            public void Send(byte[] frame) { }
            public void Close() { Closed?.Invoke("closed"); }
            public void Poll() { while (incoming.Count != 0) Received(incoming.Dequeue()); }
            public void Open() { Opened(); }
            public void Enqueue(ServerMsg msg) { incoming.Enqueue(msg.Encode()); }
        }

        const ushort W = 32, H = 32;
        const int WallX = 10; // a wall column at x 320..352
        static readonly MapTile[] GateTiles = { new MapTile(WallX, 15), new MapTile(WallX, 16) }; // y 480..544
        const double StartX = 100, StartY = 496, Radius = 12; // the default self disc (rh-sim PLAYER_RADIUS)

        /// <summary>The open map: the wall column has the gate gap (rows 15-16) and an ordinary gap (rows 2-3) that is no gate.</summary>
        static MapBlob GatedMap()
        {
            var tiles = new byte[W * H];
            for (int y = 0; y < H; y++) if (y != 2 && y != 3 && y != 15 && y != 16) tiles[y * W + WallX] = MapBlob.Wall;
            return new MapBlob(W, H, tiles)
                .WithPublic(null, new MapColiseum(WallX + 1, 0, W - WallX - 1, H, new[] { new MapGate(3, GateTiles) }), null, null, null);
        }

        static WireSelf Self(double x, double y) =>
            new WireSelf { Id = 3, X = (float)x, Y = (float)y, Hp = 10, MaxHp = 10, Status = 1 };

        static Session Start(QueuedTransport t, MapBlob map, Func<double> now)
        {
            var s = new Session(t, new SessionOptions { Url = "ws://x/ws", Profile = "p", Now = now });
            s.Connect(); t.Open();
            t.Enqueue(new ServerMsg.Welcome { Protocol = Wire.ProtocolVersion, MatchId = new byte[16], PlayerId = 3, Session = new byte[16], TickRate = 20, Tick = 1, ServerMs = 50, Map = map });
            t.Enqueue(new ServerMsg.Snapshot { Tick = 1, Phase = 1, Me = Self(StartX, StartY) });
            s.Poll();
            return s;
        }

        static ServerMsg.State Gates(uint tick, bool locked) =>
            new ServerMsg.State { Tick = tick, Items = new StateItem[] { new ArenaState { Value = new WireArena { InColiseum = locked, GatesLocked = locked } } } };

        static ServerMsg.State FrostWall(uint tick, params uint[] tiles) =>
            new ServerMsg.State
            {
                Tick = tick,
                Items = new StateItem[]
                {
                    new FieldsState
                    {
                        Rows = new[]
                        {
                            new WireField { Kind = FieldsState.KindWall, Ability = 0, Owner = 4, X = 100, Y = 100, X2 = 140, Y2 = 100, StartedAt = 0, EndsAt = 60000, Tiles = tiles },
                        },
                    },
                },
            };

        static byte At(MapBlob m, int x, int y) => m.Tiles[y * m.W + x];

        [Test]
        public void LockedGatesMakeTheWalkMapTheServersClosedGateMap()
        {
            double now = 0; var t = new QueuedTransport(); var map = GatedMap();
            var s = Start(t, map, () => now);
            now = 50; t.Enqueue(Gates(2, true)); s.Poll();
            Assert.IsNotNull(s.Motion.WalkMap, "prediction keeps a walk map while the gates are locked");
            CollectionAssert.AreEqual(FrostWallAim.PlanMap(map, true, Array.Empty<AreaView>(), null), s.Motion.WalkMap.Tiles,
                "locked: the Welcome map with every Coliseum.Gates tile WALL (rh-sim closed_gates)");
            Assert.AreEqual(MapBlob.Floor, At(map, WallX, 15), "the Welcome map itself is not edited");
        }

        [Test]
        public void APredictedWalkThroughALockedGateDetoursAroundItBeforeAnySnapshot()
        {
            double now = 0; var t = new QueuedTransport(); var map = GatedMap();
            var s = Start(t, map, () => now);
            now = 50; t.Enqueue(Gates(2, true)); s.Poll();
            s.OrderMove(600, (float)StartY); s.FlushInput();
            // 600 ms of frames, under LocalMotion.OrderExpireMs, with no snapshot: only prediction can move the drawn self.
            for (int k = 1; k <= 12; k++)
            {
                now = 50 + 50 * k; s.Poll();
                double x = s.Motion.DrawnX, y = s.Motion.DrawnY;
                bool inGate = x + Radius > WallX * 32 && x - Radius < (WallX + 1) * 32 && y + Radius > 15 * 32 && y - Radius < 17 * 32;
                Assert.IsFalse(inGate, "frame " + k + ": the predicted disc entered a locked gate at (" + x + ", " + y + ")");
                Assert.LessOrEqual(x + Radius, WallX * 32 + 0.01, "frame " + k + ": nothing crosses the wall column before the detour gap");
            }
            Assert.Less(s.Motion.DrawnY, StartY - 20, "the predicted walk heads north to the open gap: prediction is live while locked");
        }

        [Test]
        public void UnlockingReturnsTheWalkToTheOpenMap()
        {
            double now = 0; var t = new QueuedTransport(); var map = GatedMap();
            var s = Start(t, map, () => now);
            now = 50; t.Enqueue(Gates(2, true)); s.Poll();
            Assert.AreEqual(MapBlob.Wall, At(s.Motion.WalkMap, WallX, 15), "locked gate tile is WALL on the walk map");
            now = 100; t.Enqueue(Gates(3, false)); s.Poll();
            CollectionAssert.AreEqual(map.Tiles, s.Motion.WalkMap.Tiles, "unlocked: the open Welcome map again");
            s.OrderMove(600, (float)StartY); s.FlushInput();
            now = 400; s.Poll();
            Assert.AreEqual(StartY, s.Motion.DrawnY, 0.01, "the open gate is walked straight through");
            Assert.Greater(s.Motion.DrawnX, StartX + 30, "prediction runs on the open map");
        }

        [Test]
        public void FrostWallsStayLowOnTopOfTheLockedGatesAsRustsOverlay()
        {
            double now = 0; var t = new QueuedTransport(); var map = GatedMap();
            var s = Start(t, map, () => now);
            uint gate = 15u * W + WallX, floor = 20u * W + 5;
            now = 50; t.Enqueue(FrostWall(2, gate, floor)); s.Poll();
            now = 100; t.Enqueue(Gates(3, true)); s.Poll();
            // barriers.rs:337-343: locked = closed_gates().with_low(cells); with_low (map.rs:154-160) lowers FLOOR only.
            var locked = (byte[])map.Tiles.Clone();
            foreach (var g in GateTiles) locked[g.Y * W + g.X] = MapBlob.Wall;
            locked[floor] = MapBlob.Low;
            CollectionAssert.AreEqual(locked, s.Motion.WalkMap.Tiles, "locked with a wall: gate tiles WALL (a wall cell on a gate stays WALL), wall cells LOW");
            now = 150; t.Enqueue(Gates(4, false)); s.Poll();
            var open = (byte[])map.Tiles.Clone();
            open[gate] = MapBlob.Low; open[floor] = MapBlob.Low;
            CollectionAssert.AreEqual(open, s.Motion.WalkMap.Tiles, "unlocked with a wall: open map, wall cells LOW");
        }

        // ---- the closed-gate drawing's tile list (engine-free; UnityProject draws what it returns) ----

        static MethodInfo ClosedTilesMethod()
        {
            var type = typeof(MapBlob).Assembly.GetType("Roguehalla.Net.Ui.ColiseumGates");
            Assert.IsNotNull(type, "Roguehalla.Net.Ui.ColiseumGates (client-unity/RoguehallaNet/Net/Ui/ColiseumGates.cs) exists");
            var method = type.GetMethod("ClosedTiles", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(MapBlob), typeof(bool), typeof(List<MapTile>) }, null);
            Assert.IsNotNull(method, "public static void ClosedTiles(MapBlob map, bool gatesLocked, List<MapTile> into)");
            return method;
        }

        static List<MapTile> Closed(MethodInfo m, MapBlob map, bool locked)
        {
            var into = new List<MapTile> { new MapTile(31, 31) }; // stale content the helper must clear
            m.Invoke(null, new object[] { map, locked, into });
            return into;
        }

        [Test]
        public void ClosedGateTilesAreEveryGateTileWhileLockedAndNoneWhenOpen()
        {
            var m = ClosedTilesMethod();
            // MaxColiseumGates (8) gates, every side, tiles in delivered order.
            var gates = new MapGate[MapBlob.MaxColiseumGates];
            var want = new List<MapTile>();
            for (int g = 0; g < gates.Length; g++)
            {
                var tiles = new[] { new MapTile((ushort)(2 + 3 * g), 5), new MapTile((ushort)(2 + 3 * g), 6), new MapTile((ushort)(3 + 3 * g), 5) };
                gates[g] = new MapGate((byte)(g % 4), tiles);
                want.AddRange(tiles);
            }
            var map = new MapBlob(W, H, new byte[W * H]).WithPublic(null, new MapColiseum(1, 1, 30, 30, gates), null, null, null);
            CollectionAssert.AreEqual(want, Closed(m, map, true), "locked: every gate's tiles, gate by gate, in delivered order");
            CollectionAssert.IsEmpty(Closed(m, map, false), "open: nothing drawn closed");
            var plain = new MapBlob(W, H, new byte[W * H]);
            CollectionAssert.IsEmpty(Closed(m, plain, true), "a map without coliseum geometry draws no gate");
            CollectionAssert.IsEmpty(Closed(m, null, true), "no map yet: nothing");
        }
    }
}
