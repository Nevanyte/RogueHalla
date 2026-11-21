using System.IO;
using System.Reflection;
using System.Text.Json;
using NUnit.Framework;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Tests
{
    public sealed class DestroyedTerrainTests
    {
        [Test]
        public void RustTerrainVectorRoundTrips()
        {
            using var fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(Util.RepoRoot(), "protocol", "fixtures", Util.FixtureName)));
            foreach (var vector in fixtures.RootElement.GetProperty("server").EnumerateArray())
            {
                if (vector.GetProperty("name").GetString() != "state_destroyed_terrain") continue;
                var frame = Util.FromHex(vector.GetProperty("hex").GetString());
                var msg = (ServerMsg.State)ServerMsg.Decode(frame);
                CollectionAssert.AreEqual(new uint[] { 52, 1057, 90003 }, ((DestroyedTerrainState)msg.Items[0]).Cells);
                CollectionAssert.AreEqual(frame, msg.Encode());
                return;
            }
            Assert.Fail("Missing state_destroyed_terrain in Rust fixture");
        }

        static void Apply(CollectionsView view, ServerMsg.State state)
        {
            typeof(CollectionsView).GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(view, new object[] { state });
        }

        [Test]
        public void PartialBarrierKeepsItsKeyButUpdatesTheWalkMap()
        {
            var motion = new LocalMotion(new MapBlob(5, 3, new byte[15]));
            var wall = new AreaView { Kind = AreaKind.Wall, X = 80, Y = 48, StartedAt = 10, EndsAt = 4010, Key = new AreaKey(10, 2) };
            wall.Tiles = new[] { new MapTile(2, 0), new MapTile(2, 1), new MapTile(2, 2) };
            motion.SetWalls(new[] { wall });
            Assert.AreEqual(MapBlob.Low, motion.WalkMap.At(2, 1));
            wall.Tiles = new[] { new MapTile(2, 0), new MapTile(2, 2) };
            motion.SetWalls(new[] { wall });
            Assert.AreEqual(MapBlob.Floor, motion.WalkMap.At(2, 1));
            Assert.AreEqual(MapBlob.Low, motion.WalkMap.At(2, 2));
        }

        [Test]
        public void ReplacementOpensNavigationAndRetainsTheCanonicalMap()
        {
            var map = new MapBlob(5, 3, new byte[]
            {
                1, 1, 1, 1, 1,
                1, 0, 1, 0, 1,
                1, 1, 1, 1, 1,
            });
            var view = new CollectionsView();
            view.BindMap(map);
            var motion = new LocalMotion(map);
            var paths = new Pathfinder();
            Assert.IsFalse(paths.SegmentClear(motion.WalkMap, 48, 48, 112, 48, 0));
            var state = new ServerMsg.State { Tick = 2, Items = new StateItem[] { new DestroyedTerrainState { Cells = new uint[] { 7 } } } };
            Apply(view, (ServerMsg.State)ServerMsg.Decode(state.Encode()));
            motion.SetCollisionMap(view.TerrainMap);
            Assert.AreEqual(MapBlob.Wall, map.At(2, 1));
            Assert.AreEqual(MapBlob.Floor, view.TerrainMap.At(2, 1));
            Assert.IsTrue(paths.SegmentClear(motion.WalkMap, 48, 48, 112, 48, 0));

            var updated = view.TerrainMap;
            Apply(view, state);
            Assert.AreSame(updated, view.TerrainMap, "Resync of the same overlay reuses the effective map");
            state.Items = new StateItem[] { new DestroyedTerrainState { Cells = new uint[] { 99 } } };
            var error = Assert.Throws<TargetInvocationException>(() => Apply(view, state));
            StringAssert.Contains("off the Welcome map", error.InnerException.Message);
            Assert.AreSame(updated, view.TerrainMap);
        }
    }
}
