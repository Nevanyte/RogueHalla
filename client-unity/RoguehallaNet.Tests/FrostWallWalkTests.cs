using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NUnit.Framework;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-FROST-WALL-PREDICT-1: the local walk mirror plans, steers and re-plans on the map the server walks, the Welcome map with
    /// every standing Frost Wall's tiles LOW (rh-sim <c>barriers::body_map</c>). The oracle is
    /// <c>protocol/fixtures/frost_wall_walk_v1.json</c>, written from the real sim by
    /// <c>cargo run -p rh-parity --bin frost-wall-walk-fixtures</c>: per-tick rows of a hero walker (foot box 10 x 6) while walls
    /// rise, stand, are walked into, cut a gap and fall. The replay follows the real <see cref="Session"/> order: an order issued
    /// at <c>50 * (k - 1)</c> after row k - 1; then row k's walls (the State frame, sent before the snapshot each tick) through
    /// <see cref="LocalMotion.SetWalls"/>; then row k's self sample; then the frame's Step. A disagreement is a C# bug.
    /// </summary>
    public class FrostWallWalkTests
    {
        const double Tick = 50.0;
        /// <summary>Wire x/y are float: the v2 corner walks hold 0.01 u with the same harness.</summary>
        const double Tol = 0.01;
        /// <summary>The push case: the mirror drops its walk at the displacement (a hard cut) and trails the server walking on by the
        /// smoothing's steady lag (about 22 u at 8.5 u a tick); it must stay under a tile and never snap.</summary>
        const double PushLag = 32.0;

        static JsonDocument Read()
        {
            return JsonDocument.Parse(File.ReadAllText(Path.Combine(Util.RepoRoot(), "protocol", "fixtures", "frost_wall_walk_v1.json")));
        }

        static Dictionary<string, MapBlob> Maps(JsonElement root)
        {
            var maps = new Dictionary<string, MapBlob>();
            foreach (var m in root.GetProperty("maps").EnumerateArray())
            {
                var tiles = new List<byte>();
                foreach (var t in m.GetProperty("tiles").EnumerateArray()) tiles.Add((byte)t.GetInt32());
                maps[m.GetProperty("name").GetString()] = new MapBlob((ushort)m.GetProperty("w").GetInt32(), (ushort)m.GetProperty("h").GetInt32(), tiles.ToArray());
            }
            return maps;
        }

        static Footprint Foot(JsonElement root)
        {
            var f = root.GetProperty("footprint");
            Assert.AreEqual("box", f.GetProperty("kind").GetString());
            return Footprint.Box(f.GetProperty("hx").GetDouble(), f.GetProperty("hy").GetDouble());
        }

        /// <summary>A row's walls as <see cref="CollectionsView"/> builds them (map coordinates, key = start time and first tile).</summary>
        static AreaView[] Walls(JsonElement row, int w)
        {
            var rows = new List<AreaView>();
            foreach (var wall in row.GetProperty("walls").EnumerateArray())
            {
                var idx = new List<uint>();
                foreach (var t in wall.GetProperty("tiles").EnumerateArray()) idx.Add(t.GetUInt32());
                var tiles = new MapTile[idx.Count];
                for (int j = 0; j < tiles.Length; j++) tiles[j] = new MapTile((ushort)(idx[j] % (uint)w), (ushort)(idx[j] / (uint)w));
                double started = wall.GetProperty("startedAt").GetDouble();
                rows.Add(new AreaView
                {
                    Kind = AreaKind.Wall, Ability = AbilityId.Freeze, StartedAt = started, EndsAt = wall.GetProperty("endsAt").GetDouble(),
                    Tiles = tiles, Key = new AreaKey(started, idx[0]),
                });
            }
            return rows.ToArray();
        }

        static WireSelf Self(JsonElement row, byte archetype)
        {
            return new WireSelf
            {
                Status = Wire.DefaultStatus, Id = 1, X = (float)row.GetProperty("x").GetDouble(), Y = (float)row.GetProperty("y").GetDouble(),
                MoveSeq = (byte)row.GetProperty("moveSeq").GetInt32(), DisplaceSeq = (byte)row.GetProperty("displaceSeq").GetInt32(),
                Hp = 10, MaxHp = 10, Archetype = archetype,
            };
        }

        static JsonElement Case(JsonElement root, string name)
        {
            foreach (var c in root.GetProperty("cases").EnumerateArray())
                if (c.GetProperty("name").GetString() == name) return c;
            Assert.Fail("no case " + name);
            return default;
        }

        static bool InsideStandingWall(AreaView[] walls, double x, double y)
        {
            int tx = (int)Math.Floor(x / 32.0), ty = (int)Math.Floor(y / 32.0);
            foreach (var w in walls) foreach (var t in w.Tiles) if (t.X == tx && t.Y == ty) return true;
            return false;
        }

        /// <summary>Replays one case; returns |drawn - server| per row (index 0 is the reset row).</summary>
        static double[] Replay(JsonElement root, JsonElement one, out LocalMotion motion)
        {
            var maps = Maps(root);
            var map = maps[one.GetProperty("map").GetString()];
            byte archetype = (byte)root.GetProperty("archetype").GetInt32();
            var foot = Foot(root);
            string name = one.GetProperty("name").GetString();
            var ticks = one.GetProperty("ticks");
            var orders = one.GetProperty("orders");
            var pf = new Pathfinder();
            motion = new LocalMotion(map) { Foot = foot };
            motion.OnSnapshot(Self(ticks[0], archetype), 0.0);
            var err = new double[ticks.GetArrayLength()];
            for (int k = 1; k < ticks.GetArrayLength(); k++)
            {
                foreach (var o in orders.EnumerateArray())
                {
                    if (o.GetProperty("tick").GetInt32() != k) continue;
                    double gx = o.GetProperty("goal")[0].GetDouble(), gy = o.GetProperty("goal")[1].GetDouble();
                    if (o.TryGetProperty("keyboard", out var key))
                    {
                        // The steering goal the client sends for the held key, from the position it shows, on the map it walks.
                        Assert.IsTrue(KeyboardSteer.TryGoal(pf, motion.WalkMap, motion.SimX, motion.SimY, key[0].GetInt32(), key[1].GetInt32(), foot,
                            out double sx, out double sy), name + " tick " + k + ": a steering goal exists");
                        Assert.AreEqual(gx, sx, 1e-9, name + " tick " + k + ": steering goal x on the walk map (the server's map with the wall LOW)");
                        Assert.AreEqual(gy, sy, 1e-9, name + " tick " + k + ": steering goal y");
                    }
                    motion.Order((float)gx, (float)gy, (byte)o.GetProperty("seq").GetInt32(), Tick * (k - 1));
                }
                var row = ticks[k];
                double now = Tick * k;
                motion.SetWalls(Walls(row, map.W));
                motion.OnSnapshot(Self(row, archetype), now);
                motion.Step(now);
                double sx2 = row.GetProperty("x").GetDouble(), sy2 = row.GetProperty("y").GetDouble();
                err[k] = Math.Sqrt(Math.Pow(motion.DrawnX - sx2, 2) + Math.Pow(motion.DrawnY - sy2, 2));
            }
            return err;
        }

        static void AssertMatches(string name)
        {
            using var doc = Read();
            var root = doc.RootElement;
            Assert.AreEqual("rh-frost-wall-walk/1", root.GetProperty("format").GetString());
            var one = Case(root, name);
            var err = Replay(root, one, out var motion);
            var ticks = one.GetProperty("ticks");
            for (int k = 1; k < err.Length; k++)
                if (err[k] > Tol)
                    Assert.Fail(name + " tick " + k + ": drawn (" + motion.DrawnX + ", " + motion.DrawnY + ") at the end; at tick " + k
                        + " |drawn - server| = " + err[k].ToString("F3") + " u > " + Tol + " (server (" + ticks[k].GetProperty("x").GetDouble().ToString("F2")
                        + ", " + ticks[k].GetProperty("y").GetDouble().ToString("F2") + "), server path " + ticks[k].GetProperty("path").GetRawText() + ")");
            Assert.AreEqual(0, motion.Snaps, name + ": no snap");
            Assert.AreEqual(0, motion.ExpiredOrders, name + ": no expired order");
        }

        // ----------------------------------------------------------------------------------------------------------------
        // The Rust per-tick vectors

        [Test]
        public void AFreezeWorldWithNoWallWalksExactlyAsToday() { AssertMatches("freeze_world_no_wall"); }

        [Test]
        public void AClickPastAStandingWallTakesTheServersPathRoundIt() { AssertMatches("click_past_standing_wall"); }

        [Test]
        public void AClickOnAStandingWallGoesWhereTheServerSnapsTheGoal() { AssertMatches("click_into_standing_wall"); }

        [Test]
        public void TheHeldKeySteersOnTheWallAndStopsAgainstTheIce() { AssertMatches("keyboard_into_standing_wall"); }

        [Test]
        public void AWallRisingOnTheWalkReplansItOnTheLandingTick() { AssertMatches("wall_rises_on_walk"); }

        [Test]
        public void AFallingWallReplansNothingAndTheDetourIsWalkedToItsEnd() { AssertMatches("wall_falls_mid_detour"); }

        [Test]
        public void OrdersOnTheExpiryTickStillGoRoundAndTheNextTickGoesThrough() { AssertMatches("orders_across_expiry"); }

        [Test]
        public void AWalkerInTheGapOfAGappedWallStaysInsideTheGap() { AssertMatches("gap_walker"); }

        [Test]
        public void AWalkerTheWallLandsOnFollowsTheServersPushAndNeverFightsIt()
        {
            using var doc = Read();
            var root = doc.RootElement;
            var one = Case(root, "pushed_walker");
            int landing = one.GetProperty("landingTick").GetInt32();
            var ticks = one.GetProperty("ticks");
            Assert.AreEqual(ticks[landing - 1].GetProperty("displaceSeq").GetInt32() + 1, ticks[landing].GetProperty("displaceSeq").GetInt32(),
                "the fixture's server sets the walker down on the landing tick");
            var err = Replay(root, one, out var motion);
            for (int k = 1; k < landing; k++) Assert.LessOrEqual(err[k], Tol, "before the wall: the ordinary walk, tick " + k);
            Assert.LessOrEqual(err[landing], Tol, "the displacement is shown at once: no client-side push, no smoothing across it");
            for (int k = landing; k < err.Length; k++) Assert.LessOrEqual(err[k], PushLag, "after the push the mirror follows the server, tick " + k);
            Assert.AreEqual(0, motion.Snaps, "the correction is never fought into a snap");
            Assert.LessOrEqual(err[err.Length - 1], Tol, "and settles on the server");
        }

        // ----------------------------------------------------------------------------------------------------------------
        // The seam

        static AreaView[] OneWall(JsonElement root, string caseName, out MapBlob map, out int landing)
        {
            var one = Case(root, caseName);
            map = Maps(root)[one.GetProperty("map").GetString()];
            landing = one.GetProperty("landingTick").GetInt32();
            return Walls(one.GetProperty("ticks")[landing], map.W);
        }

        [Test]
        public void TheWalkMapHasTheWallTilesLowWhileTheyStandAndIsTheWelcomeMapItselfOtherwise()
        {
            using var doc = Read();
            var walls = OneWall(doc.RootElement, "click_past_standing_wall", out var map, out _);
            var motion = new LocalMotion(map);
            motion.SetWalls(Array.Empty<AreaView>());
            Assert.AreSame(map, motion.WalkMap, "no wall: the mirror plans on the Welcome map object, as today");
            var cloud = new AreaView { Kind = AreaKind.Cloud, X = 400, Y = 400, Radius = 64 };
            motion.SetWalls(new[] { cloud });
            Assert.AreSame(map, motion.WalkMap, "a field that is not a wall blocks nothing");
            motion.SetWalls(new[] { cloud, walls[0] });
            Assert.AreNotSame(map, motion.WalkMap);
            foreach (var t in walls[0].Tiles)
            {
                Assert.AreEqual(MapBlob.Low, motion.WalkMap.At(t.X, t.Y), "a wall tile is LOW while the wall stands (" + t.X + ", " + t.Y + ")");
                Assert.AreEqual(MapBlob.Floor, map.At(t.X, t.Y), "the Welcome map itself is never written");
            }
            Assert.AreEqual(MapBlob.Wall, motion.WalkMap.At(0, 0), "every other tile is the Welcome map's");
            Assert.AreEqual(MapBlob.Floor, motion.WalkMap.At(20, 5));
            motion.SetWalls(Array.Empty<AreaView>());
            Assert.AreSame(map, motion.WalkMap, "the wall fell: its tiles reopen");
        }

        [Test]
        public void ASteadyFrameWithAStandingWallAllocatesNothingInTheWalkMirror()
        {
            using var doc = Read();
            var root = doc.RootElement;
            var walls = OneWall(root, "click_past_standing_wall", out var map, out _);
            // Collections builds a new array per State message: the same wall set in a fresh array must cost nothing either.
            var again = (AreaView[])walls.Clone();
            var motion = new LocalMotion(map) { Foot = Foot(root) };
            motion.OnSnapshot(new WireSelf { Status = Wire.DefaultStatus, Id = 1, X = 592, Y = 400, Hp = 10, MaxHp = 10 }, 0.0);
            motion.SetWalls(walls);
            Assert.AreEqual(MapBlob.Low, motion.WalkMap.At(12, 12), "the measured frames run with the wall on the walk map");
            motion.Order(208, 400, 1, 0.0);
            double now = 0;
            double bytes = AllocationProbe.PerCall(i =>
            {
                motion.SetWalls((i & 1) == 0 ? walls : again);
                now += 0.001; // a long walk: every measured frame steps the model along the planned detour
                motion.Step(now);
            });
            Assert.AreEqual(0.0, bytes, "bytes per steady frame (SetWalls with the same walls, Step)");
            Assert.Less(motion.DrawnX, 592f, "the frames walked");
        }
    }
}
