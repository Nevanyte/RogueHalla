using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Client;
using Roguehalla.Net.Protocol;
using UnityEngine;
using MapRect = Roguehalla.Net.Protocol.MapRect;

namespace Roguehalla.Client.Tests
{
    /// <summary>Source-driven oracle for the delivered Holy Hall: its 37x37 blueprint, public Hall anchor and all eight poses.</summary>
    public sealed class StormInkHolyHallEditModeTests
    {
        const int N = 56, Ox = 9, Oy = 7, Size = 37;

        [Serializable] sealed class MapJson { public int w, h; public int[] tiles, terrain, regionGrid; }
        [Serializable] sealed class FileJson { public MapJson map; }

        static string[] Blueprint()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../docs/landmarks/blueprints/hall.txt"));
            Assert.That(File.Exists(path), "Hall blueprint missing: " + path);
            return File.ReadAllLines(path).Where(l => l.Length > 0 && l[0] != ';').ToArray();
        }

        static string[] Orient(string[] rows, int k)
        {
            bool swap = (k & 1) != 0, fx = (k & 2) != 0, fy = (k & 4) != 0;
            var o = new string[rows.Length];
            for (int y = 0; y < rows.Length; y++)
            {
                var r = new char[rows.Length];
                for (int x = 0; x < rows.Length; x++)
                {
                    int sx = fx ? rows.Length - 1 - x : x, sy = fy ? rows.Length - 1 - y : y;
                    r[x] = swap ? rows[sx][sy] : rows[sy][sx];
                }
                o[y] = new string(r);
            }
            return o;
        }

        static MapBlob DeliveredHall(int k, bool withAnchor = true)
        {
            var rows = Orient(Blueprint(), k); var tiles = new byte[N * N]; var terrain = new byte[N * N]; var regions = new byte[N * N];
            var entrance = new System.Collections.Generic.List<MapTile>();
            var marked = Blueprint().Select(r => r.ToCharArray()).ToArray();
            for (int x = 15; x <= 21; x++) marked[36][x] = 'M';
            var orientedMarks = Orient(marked.Select(r => new string(r)).ToArray(), k);
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            {
                int i = (Oy + y) * N + Ox + x; char c = rows[y][x];
                if (c == '#') tiles[i] = MapBlob.Wall;
                if (c == 'L') terrain[i] = (byte)TerrainKind.Light;
                if (orientedMarks[y][x] == 'M') entrance.Add(new MapTile((ushort)(Ox + x), (ushort)(Oy + y)));
                regions[i] = 5; // delivered Holy zone id
            }
            var anchor = new MapAnchor(5, MapBlob.AnchorKindHall,
                new MapTile((ushort)(Ox + 18), (ushort)(Oy + 18)),
                new MapRect(Ox, Oy, Size, Size), Front(entrance), entrance.ToArray());
            var map = new MapBlob(N, N, tiles, terrain, regions, MapBlob.GroundTerrainAll);
            return map.WithPublic(null, null, null, null, null, null, withAnchor ? new[] { anchor } : null);
        }

        static MapBlob ExistingOptionOffMap()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../crates/rh-sim/src/mapdata/fixtures/seed1.map.json"));
            Assert.That(File.Exists(path), "existing option-off map fixture missing: " + path);
            var source = JsonUtility.FromJson<FileJson>(File.ReadAllText(path)).map;
            byte[] Bytes(int[] values) { var bytes = new byte[values.Length]; for (int i = 0; i < values.Length; i++) bytes[i] = (byte)values[i]; return bytes; }
            return new MapBlob((ushort)source.w, (ushort)source.h, Bytes(source.tiles), Bytes(source.terrain), Bytes(source.regionGrid), MapBlob.GroundTerrainAll);
        }

        static byte Front(System.Collections.Generic.List<MapTile> entrance)
        {
            double mx = entrance.Average(t => t.X) - (Ox + 18), my = entrance.Average(t => t.Y) - (Oy + 18);
            return Math.Abs(mx) > Math.Abs(my) ? (byte)(mx > 0 ? 0 : 8) : (byte)(my > 0 ? 4 : 12);
        }

        static object ReadApi(TempleLayout layout, string name)
        {
            var p = typeof(TempleLayout).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null) return p.GetValue(layout);
            var f = typeof(TempleLayout).GetField(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.That(f, Is.Not.Null, "TempleLayout public API " + name);
            return f.GetValue(layout);
        }

        [TestCase(0), TestCase(1), TestCase(2), TestCase(3), TestCase(4), TestCase(5), TestCase(6), TestCase(7)]
        public void DeliveredHallProducesSignatureTempleAxisPiersAndLandmark(int pose)
        {
            var map = DeliveredHall(pose);
            var temple = TempleLayout.For(map);
            Assert.That(temple.Found, Is.True, temple.Describe());
            var signature = typeof(TempleLayout).GetProperty("IsSignature", BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            Assert.That(signature, Is.Not.Null, "signature detection API exists");
            Assert.That((bool)signature.GetValue(signature.GetMethod.IsStatic ? null : temple), Is.True, "recognizes the delivered Hall layout");

            var axis = pose % 2 == 0 ? 'x' : 'y';
            float axisValue = axis == 'x' ? Ox + 18.5f : Oy + 18.5f;
            Assert.That(temple.Pillars.Count, Is.EqualTo(32), "only the 32 source-defined isolated 3x3 piers count");
            foreach (var p in temple.Pillars)
            {
                Assert.That(map.Tiles[p.y * N + p.x], Is.EqualTo(MapBlob.Wall));
                Assert.That(map.Tiles[(p.y + 2) * N + p.x + 2], Is.EqualTo(MapBlob.Wall));
            }
            var entranceApi = (Vector2)ReadApi(temple, "Entrance");
            var shrineApi = (Vector2)ReadApi(temple, "Shrine");
            var doorApi = (Vector2)ReadApi(temple, "Door");
            foreach (var p in new[] { entranceApi, shrineApi, doorApi })
            {
            Assert.That(axis == 'x' ? p.x : p.y, Is.EqualTo(axisValue).Within(.25f), "landmark group lies on exact delivered nave axis");
            }
            Assert.That(temple.Lamps.Count, Is.EqualTo(6), "three source-defined twin lamp pairs");
            foreach (var lamp in temple.Lamps) Assert.That(map.Tiles[Mathf.FloorToInt(lamp.y) * N + Mathf.FloorToInt(lamp.x)], Is.EqualTo(MapBlob.Floor), "lamp stands on delivered floor");
            var unmatched = temple.Lamps.ToList();
            while (unmatched.Count > 0)
            {
                var a = unmatched[0]; unmatched.RemoveAt(0);
                int pair = unmatched.FindIndex(b => Mathf.Abs((axis == 'x' ? a.x + b.x : a.y + b.y) - 2f * axisValue) < .05f
                    && Mathf.Abs((axis == 'x' ? a.y - b.y : a.x - b.x)) < .05f);
                Assert.That(pair, Is.GreaterThanOrEqualTo(0), "every lamp has its reflected twin across the nave axis");
                unmatched.RemoveAt(pair);
            }
            Assert.That(temple.Props.Any(p => p.Art.StartsWith("temple.")), Is.False, "signature Hall uses anchor landmark art, no procedural miniature-temple props");

            var root = new GameObject("storm marsh temple oracle"); LandmarkView view = null;
            try
            {
                view = LandmarkView.Build(root.transform, map);
                Assert.That(view.Count, Is.EqualTo(1), "normal anchor-driven landmark build");
                var landmark = view.Get(0);
                Assert.That(landmark.RoofCovers(Mathf.FloorToInt(temple.Shrine.x), Mathf.FloorToInt(temple.Shrine.y)), Is.True, "the shrine's interior remains under the Holy roof/cover");
                Assert.That(landmark.RoofCovers(Mathf.FloorToInt(temple.Door.x), Mathf.FloorToInt(temple.Door.y)), Is.False, "the connected sanctuary doorway stays uncovered");
                bool coveredShrineWall = false;
                for (int y = 0; y < map.H && !coveredShrineWall; y++) for (int x = 0; x < map.W; x++)
                    if (map.Tiles[y * map.W + x] == MapBlob.Wall && !temple.TryBlock(x, y, out _, out _) && landmark.RoofCovers(x, y)) { coveredShrineWall = true; break; }
                Assert.That(coveredShrineWall, Is.True, "the Holy roof covers the shrine's connected enclosure walls");
                var transforms = view.Get(0).Root.GetComponentsInChildren<Transform>(true);
                Assert.That(transforms.Count(t => t.name == "Hall column"), Is.EqualTo(32), "one Hall column GameObject per delivered pier group");
                if (view.Get(0).Root.GetComponentsInChildren<SpriteRenderer>(true).Length > 0)
                {
                    Assert.That(transforms.Any(t => t.name == "Holy aisle lamp"), Is.True, "sprite resources present: aisle lamp renderer is built");
                    Assert.That(transforms.Any(t => t.name == "Holy sun shrine"), Is.True, "sprite resources present: sun shrine renderer is built");
                }
            }
            finally { if (view != null) view.Dispose(); UnityEngine.Object.DestroyImmediate(root); }
        }

        [TestCase(0), TestCase(3)]
        public void SignatureHallExcludesOutdoorClutterAndKeepsPaversAndPublicCover(int pose)
        {
            var hall = DeliveredHall(pose);
            var rooms = TempleLayout.For(hall);
            Assert.That(rooms.IsSignature, Is.True);
            var floor = Enumerable.Range(Oy, Size).SelectMany(y => Enumerable.Range(Ox, Size).Select(x => (x, y)))
                .Where(p => hall.At(p.x, p.y) == MapBlob.Floor && hall.Terrain[p.y * N + p.x] == 0)
                .Take(5).ToArray();
            MapProp Prop(byte kind, (int x, int y) cell) =>
                new MapProp(kind, (ushort)(cell.x * 32 + 16), (ushort)(cell.y * 32 + 16), 0, 0);
            var rows = new[]
            {
                Prop(42, floor[0]), Prop(45, floor[1]), Prop(46, floor[2]),
                Prop(47, floor[3]), Prop(42, (Ox - 1, floor[0].y)),
                Prop(45, (Ox - 1, floor[1].y + 1))
            }.OrderBy(p => p.Y).ThenBy(p => p.X).ToArray();
            var bushes = new byte[N * N];
            bushes[floor[4].y * N + floor[4].x] = 1;

            var delivered = hall.WithPublic(null, null, bushes, rows, null, null, hall.Anchors);
            var placed = RegionProps.Scatter(delivered);
            Assert.That(placed.Where(p => p.Shipped && rooms.Contains(p.Tx, p.Ty)).Select(p => p.Art),
                Is.EqualTo(new[] { "holy.paver" }));
            Assert.That(placed.Where(p => p.Shipped && !rooms.Contains(p.Tx, p.Ty)).Select(p => p.Art),
                Is.EquivalentTo(new[] { "holy.rock", "holy.flowers" }));
            Assert.That(placed.Any(p => p.ConcealmentCell && p.Tx == floor[4].x && p.Ty == floor[4].y), Is.True);

            var fallback = hall.WithPublic(null, null, bushes, rows, null, null);
            Assert.That(RegionProps.Scatter(fallback).Count(p => p.Shipped), Is.EqualTo(rows.Length));
        }

        [Test]
        public void WithoutDeliveredHallAnchorLegacyTemplePropsRemainEnabled()
        {
            var map = ExistingOptionOffMap();
            Assert.That(map.Anchors, Is.Empty, "option-off fixture has no delivered Hall anchor");
            var temple = TempleLayout.For(map);
            Assert.That(temple.Found, Is.True, temple.Describe());
            Assert.That(temple.Pillars.Count, Is.GreaterThan(0), "option-off map retains its existing generated piers");
            Assert.That(temple.Props.Count, Is.GreaterThan(0), "without the opt-in Hall anchor, existing procedural props remain");
            Assert.That(temple.Props.Any(p => p.Art.StartsWith("temple.")), Is.True);
        }
    }
}
