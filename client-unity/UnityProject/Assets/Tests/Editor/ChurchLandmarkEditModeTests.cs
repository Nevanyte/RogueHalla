using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Roguehalla.Client;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using UnityEngine;
using MapRect = Roguehalla.Net.Protocol.MapRect;   // Net.Ui has a MapRect of its own (the map screen's)

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UNITY-LANDMARK-3 oracle, Unity half: a map whose anchors name a Church gets one landmark whose art is derived from the TILES
    /// inside the anchor's footprint (the approved drawing docs/landmarks/blueprints/church.txt is stamped into a test map in each of the
    /// eight orientations). Only structure is pinned: counts, which tiles the roof covers, where the gate and door art stand, when the roof
    /// fades, one object per headstone group. Never colours or shapes. The contract between these tests and the art is the object names
    /// "Church gate", "Church door" and "Church gravestone" (descendants of Landmark.Root) and Landmark.RoofRenderers
    /// (docs/backlog/packets/P-UNITY-LANDMARK-3.md, Established facts).
    /// </summary>
    public sealed class ChurchLandmarkEditModeTests
    {
        const string GateName = "Church gate", DoorName = "Church door", GraveName = "Church gravestone";
        const int N = 48, FootX = 9, FootY = 7, Size = 29;

        static string[] ChurchRows()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../docs/landmarks/blueprints/church.txt"));
            Assert.That(File.Exists(path), "blueprint missing: " + path);
            return File.ReadAllLines(path).Where(l => l.Length > 0 && l[0] != ';').ToArray();
        }

        /// <summary>One of the eight placements: swap x and y, then flip x, then flip y.</summary>
        static string[] Orient(string[] rows, int k)
        {
            bool swap = (k & 1) != 0, fx = (k & 2) != 0, fy = (k & 4) != 0; int n = rows.Length;
            var o = new string[n];
            for (int y = 0; y < n; y++)
            {
                var r = new char[n];
                for (int x = 0; x < n; x++) { int sx = fx ? n - 1 - x : x, sy = fy ? n - 1 - y : y; r[x] = swap ? rows[sx][sy] : rows[sy][sx]; }
                o[y] = new string(r);
            }
            return o;
        }

        /// <summary>Private marks, all plain floor in the map, put on the drawing before it is oriented: M main gate (row 28, columns
        /// 12-16), i building interior floor (rows 4-15, columns 4-24), L the lane tile in front of the door, Y two graveyard floor tiles.</summary>
        static string[] Marked()
        {
            var rows = ChurchRows().Select(r => r.ToCharArray()).ToArray();
            for (int x = 12; x <= 16; x++) rows[28][x] = 'M';
            for (int y = 4; y <= 15; y++) for (int x = 4; x <= 24; x++) if (rows[y][x] == '.') rows[y][x] = 'i';
            rows[17][14] = 'L'; rows[22][14] = 'Y'; rows[20][4] = 'Y';
            return rows.Select(r => new string(r)).ToArray();
        }

        sealed class Site
        {
            public MapBlob Map; public MapAnchor Anchor; public int K;
            public List<(int x, int y)> Gate = new List<(int, int)>(), Door = new List<(int, int)>(), RearDoor = new List<(int, int)>(), Interior = new List<(int, int)>(),
                BuildingWalls = new List<(int, int)>(), Headstones = new List<(int, int)>(), Lane = new List<(int, int)>(), Yard = new List<(int, int)>();
            public List<List<(int x, int y)>> Groups = new List<List<(int, int)>>();
            public (int x, int y) Camp;
        }

        static Site Church(int k)
        {
            var rows = Orient(Marked(), k);
            var tiles = new byte[N * N]; var terrain = new byte[N * N]; var regions = new byte[N * N];
            for (int i = 0; i < regions.Length; i++) regions[i] = 2;
            var s = new Site { K = k };
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            {
                char c = rows[y][x]; int i = (FootY + y) * N + FootX + x; var t = (FootX + x, FootY + y);
                if (c == '#' || c == 'P') { tiles[i] = MapBlob.Wall; s.BuildingWalls.Add(t); }
                if (c == 'G') { tiles[i] = MapBlob.Wall; s.Headstones.Add(t); }
                if (c == '+') { tiles[i] = MapBlob.Low; terrain[i] = (byte)TerrainKind.Fence; }
                if (c == 'M') s.Gate.Add(t);
                if (c == 'D') s.Door.Add(t);
                if (c == 'R') s.RearDoor.Add(t);
                if (c == 'i' || c == 'E') s.Interior.Add(t);
                if (c == 'E') s.Camp = t;
                if (c == 'L') s.Lane.Add(t);
                if (c == 'Y') s.Yard.Add(t);
            }
            // 8-connected groups of headstone tiles
            var left = new HashSet<(int, int)>(s.Headstones);
            while (left.Count > 0)
            {
                var first = left.First(); left.Remove(first); var group = new List<(int, int)> { first }; var stack = new Stack<(int, int)>(); stack.Push(first);
                while (stack.Count > 0)
                {
                    var (a, b) = stack.Pop();
                    for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
                        if (left.Remove((a + dx, b + dy))) { group.Add((a + dx, b + dy)); stack.Push((a + dx, b + dy)); }
                }
                s.Groups.Add(group);
            }
            double mx = s.Gate.Average(m => m.x) - (FootX + (Size - 1) / 2.0), my = s.Gate.Average(m => m.y) - (FootY + (Size - 1) / 2.0);
            byte front = Math.Abs(mx) > Math.Abs(my) ? (byte)(mx > 0 ? 0 : 8) : (byte)(my > 0 ? 4 : 12);
            s.Anchor = new MapAnchor(2, MapBlob.AnchorKindChurch, new MapTile((ushort)s.Camp.x, (ushort)s.Camp.y),
                new MapRect(FootX, FootY, Size, Size), front, s.Gate.Select(m => new MapTile((ushort)m.x, (ushort)m.y)).ToArray());
            s.Map = new MapBlob(N, N, tiles, terrain, regions, MapBlob.GroundTerrainAll);
            return s;
        }

        // ---- scene helpers ----------------------------------------------------------------------------------------------------

        sealed class Scene : IDisposable
        {
            public readonly GameObject Root = new GameObject("church test root"); public LandmarkView View;
            public Scene(MapBlob map, params MapAnchor[] anchors) { View = LandmarkView.Build(Root.transform, map, anchors); }
            public void Dispose() { if (View != null) View.Dispose(); UnityEngine.Object.DestroyImmediate(Root); }
        }

        sealed class Logs : IDisposable
        {
            public readonly List<string> All = new List<string>();
            void On(string text, string stack, LogType type) { All.Add(text); }
            public Logs() { Application.logMessageReceived += On; }
            public int Count(string fragment) { return All.Count(l => l.Contains(fragment)); }
            public void Dispose() { Application.logMessageReceived -= On; }
        }

        static Vector3 TileCentre(int x, int y) { return ArenaView.WorldPoint((x + .5f) * 32f, (y + .5f) * 32f); }
        static Vector3 TileCentre((int x, int y) t) { return TileCentre(t.x, t.y); }
        static IEnumerable<int> Orientations() { return Enumerable.Range(0, 8); }

        static Landmark Built(Scene scene, string why)
        {
            Assert.That(scene.View.Count, Is.EqualTo(1), why + ": one landmark for the Church anchor");
            return scene.View.Get(0);
        }

        /// <summary>Whether tile t's centre lies within the bounds, give or take half a tile (art is not pixel-exact on its tiles).</summary>
        static bool Reaches(Bounds b, (int x, int y) t)
        {
            var c = TileCentre(t); b.Expand(new Vector3(1f, ArenaView.GroundSquash, 100f));   // Expand grows each side by half the amount
            return b.Contains(new Vector3(c.x, c.y, b.center.z));
        }

        static IEnumerable<Transform> Named(Landmark lm, string name)
        {
            return lm.Root.GetComponentsInChildren<Transform>(true).Where(t => t.name == name);
        }

        static bool TryBounds(IEnumerable<Transform> objects, out Bounds b)
        {
            b = default; bool any = false;
            foreach (var t in objects) foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            return any;
        }

        static Bounds RoofBounds(Landmark lm)
        {
            Assert.That(lm.RoofRenderers.Length, Is.GreaterThan(0), "the roof is drawn");
            var all = lm.RoofRenderers[0].bounds; foreach (var r in lm.RoofRenderers) all.Encapsulate(r.bounds);
            return all;
        }

        // ---- the framework builds the church from the tiles ----------------------------------------------------------------------

        [TestCaseSource(nameof(Orientations))]
        public void AChurchAnchorBuildsOneLandmarkClassifiedFromTheTilesInEveryOrientation(int k)
        {
            var site = Church(k);
            Assert.That(LandmarkArts.Has(MapBlob.AnchorKindChurch), Is.True, "Church is registered in LandmarkArts");
            using (var logs = new Logs())
            using (var scene = new Scene(site.Map, site.Anchor))
            {
                var lm = Built(scene, "orientation " + k);
                Assert.That(scene.Root.transform.childCount, Is.EqualTo(1), "one landmark object under the parent");
                Assert.That(lm.Root.transform.parent, Is.EqualTo(scene.Root.transform));
                Assert.That(lm.WallCount, Is.EqualTo(136), "orientation " + k + ": 112 building (64 wall + 48 pillar) + 24 headstone tiles");
                Assert.That(lm.IceCount, Is.EqualTo(0));
                Assert.That(lm.InteriorCount, Is.EqualTo(204), "orientation " + k + ": the walled building's floor");
                Assert.That(logs.Count("LandmarkView: no art"), Is.EqualTo(0));
                Assert.That(site.Interior.Count, Is.EqualTo(204), "guard on the test map");
            }
        }

        // ---- the roof ---------------------------------------------------------------------------------------------------------------

        [TestCaseSource(nameof(Orientations))]
        public void TheRoofCoversTheBuildingsInteriorAndOwnWallsAndNeitherGraveyardNorYard(int k)
        {
            var site = Church(k);
            using (var scene = new Scene(site.Map, site.Anchor))
            {
                var lm = Built(scene, "orientation " + k);
                var roof = RoofBounds(lm);
                foreach (var t in site.Interior)
                {
                    Assert.That(lm.RoofCovers(t.x, t.y), Is.True, "orientation " + k + ": interior tile " + t + " is under the roof");
                    Assert.That(Reaches(roof, t), Is.True, "orientation " + k + ": the drawn roof reaches interior tile " + t);
                }
                foreach (var t in site.BuildingWalls)
                {
                    Assert.That(lm.RoofCovers(t.x, t.y), Is.True, "orientation " + k + ": the building's own wall/pillar " + t);
                    Assert.That(Reaches(roof, t), Is.True, "orientation " + k + ": the drawn roof reaches the building's wall/pillar " + t);
                }
                foreach (var t in site.Headstones.Concat(site.Gate).Concat(site.Yard))
                    Assert.That(Reaches(roof, t), Is.False, "orientation " + k + ": the drawn roof must not reach graveyard/gate tile " + t);
                // Seam note (packet): the framework's RoofCovers mask is interior + EVERY wall tile of the footprint, so it also reports
                // the headstones; the drawn roof is what is pinned above.
            }
        }

        [TestCaseSource(nameof(Orientations))]
        public void TheRoofFadesForAnActorInsideTheBuildingAndNotInTheGraveyardTheMainGateOrTheDoor(int k)
        {
            var site = Church(k);
            using (var scene = new Scene(site.Map, site.Anchor))
            {
                var lm = Built(scene, "orientation " + k); var v = scene.View;
                v.Tick(10f);
                Assert.That(lm.RoofAlpha, Is.EqualTo(LandmarkView.RoofOpaqueAlpha), "nobody inside: opaque");
                var notInside = new List<(string, (int x, int y))> { ("graveyard", site.Yard[0]), ("graveyard", site.Yard[1]), ("main gate", site.Gate[0]),
                    ("main gate middle", site.Gate[2]), ("front door", site.Door[0]), ("front door middle", site.Door[1]),
                    ("rear door", site.RearDoor[0]), ("rear door middle", site.RearDoor[1]), ("lane before the door", site.Lane[0]) };
                foreach (var (what, t) in notInside)
                {
                    v.NoteActor(TileCentre(t)); v.Tick(10f);
                    Assert.That(lm.RoofAlpha, Is.EqualTo(LandmarkView.RoofOpaqueAlpha), "orientation " + k + ": an actor at the " + what + " " + t + " does not fade the roof");
                }
                foreach (var t in new[] { site.Camp, site.Interior[0], site.Interior[site.Interior.Count - 1] })
                {
                    v.NoteActor(TileCentre(t)); v.Tick(10f);
                    Assert.That(lm.RoofAlpha, Is.EqualTo(LandmarkView.RoofFadedAlpha), "orientation " + k + ": an actor on interior tile " + t + " fades it");
                    v.Tick(10f);
                    Assert.That(lm.RoofAlpha, Is.EqualTo(LandmarkView.RoofOpaqueAlpha), "and it returns when the actor leaves");
                }
                v.NoteActor(TileCentre(site.Camp)); v.Tick(10f);
                foreach (var r in lm.RoofRenderers) Assert.That(r.color.a, Is.EqualTo(LandmarkView.RoofFadedAlpha).Within(.001f), "every drawn roof piece carries the alpha");
                // Owner ruling 2026-11-13: a visible actor inside makes it peek when the local player is in the graveyard.
                v.Tick(10f);
                v.NoteActor(TileCentre(site.Yard[0])); v.NoteOtherActor(TileCentre(site.Camp)); v.Tick(10f);
                Assert.That(lm.RoofAlpha, Is.EqualTo(LandmarkView.RoofPeekAlpha));
            }
        }

        [Test]
        public void TheRoofSortsAtTheFootprintsSouthEdgeAndUnderTheFog()
        {
            var site = Church(0);
            using (var scene = new Scene(site.Map, site.Anchor))
            {
                var lm = Built(scene, "orientation 0");
                int edge = 2000 + (FootY + Size) * 16;
                Assert.That(lm.RoofRenderers.Length, Is.GreaterThan(0), "the roof is drawn");
                Assert.That(lm.RoofOrder, Is.InRange(edge, edge + 15));
                Assert.That(lm.RoofRenderers.All(r => r.sortingOrder == lm.RoofOrder), Is.True, "every roof piece on that order");
                Assert.That(lm.RoofOrder, Is.LessThan(VisionFog.SortingOrder));
            }
        }

        // ---- the main gate and the door ----------------------------------------------------------------------------------------------

        [TestCaseSource(nameof(Orientations))]
        public void TheGateArtStandsOnTheFiveEntranceTilesOnTheSideTheFrontPointsTo(int k)
        {
            var site = Church(k);
            using (var scene = new Scene(site.Map, site.Anchor))
            {
                var lm = Built(scene, "orientation " + k);
                Assert.That(site.Gate.Count, Is.EqualTo(5));
                Assert.That(lm.ArchTiles.Select(t => ((int)t.X, (int)t.Y)).OrderBy(t => t), Is.EqualTo(site.Gate.OrderBy(t => t)), "the framework's arch is the main gate");
                Assert.That(TryBounds(Named(lm, GateName), out var gate), Is.True, "orientation " + k + ": gate art (objects named '" + GateName + "' with renderers)");
                foreach (var t in site.Gate) Assert.That(Reaches(gate, t), Is.True, "orientation " + k + ": the gate art spans entrance tile " + t);
                float tx = lm.ArchCentre.x, ty = -lm.ArchCentre.y / ArenaView.GroundSquash;
                var c = new Vector2(FootX + Size / 2f, FootY + Size / 2f);
                Assert.That(Vector2.Dot(new Vector2(tx, ty) - c, lm.FrontVector), Is.GreaterThan(Size / 2f - 1.5f), "orientation " + k + ": the gate is on the edge the front points to");
                // and the art itself is there, not just the arch point
                var centre = gate.center; float gx = centre.x, gy = -centre.y / ArenaView.GroundSquash;
                Assert.That(Vector2.Dot(new Vector2(gx, gy) - c, lm.FrontVector), Is.GreaterThan(Size / 2f - 2.5f), "orientation " + k + ": the gate art stands on that edge");
                foreach (var t in site.Door) Assert.That(Reaches(gate, t), Is.False, "orientation " + k + ": the gate art is not the door's: " + t);
            }
        }

        [TestCaseSource(nameof(Orientations))]
        public void TheDoorSurroundStandsOnTheThreeDoorTilesOfTheBuildingNotOnTheMainGate(int k)
        {
            var site = Church(k);
            using (var scene = new Scene(site.Map, site.Anchor))
            {
                var lm = Built(scene, "orientation " + k);
                Assert.That(site.Door.Count, Is.EqualTo(3));
                Assert.That(site.RearDoor.Count, Is.EqualTo(3));
                var doors = Named(lm, DoorName).Where(t => t.GetComponentsInChildren<Renderer>(true).Length > 0).ToArray();
                Assert.That(doors.Length, Is.GreaterThanOrEqualTo(2), "orientation " + k + ": both openings have distinct door artwork");
                foreach (var run in new[] { site.Door, site.RearDoor })
                {
                    float dx = run.Average(t => t.x + .5f), dy = run.Average(t => t.y + .5f);
                    var near = doors.Select(t => { TryBounds(new[] { t }, out var b); return (t, b, d: Vector2.Distance(new Vector2(b.center.x, -b.center.y / ArenaView.GroundSquash), new Vector2(dx, dy))); }).OrderBy(v => v.d).First();
                    Assert.That(near.d, Is.LessThan(2.5f), "orientation " + k + ": a separate surround is centred on doorway at " + dx + "," + dy);
                    foreach (var t in run) Assert.That(Reaches(near.b, t), Is.True, "orientation " + k + ": a door surround spans tile " + t);
                    foreach (var t in site.Gate) Assert.That(Reaches(near.b, t), Is.False, "orientation " + k + ": door surround is not on the graveyard gate: " + t);
                }
            }
        }

        [TestCaseSource(nameof(Orientations))]
        public void PewsAndPulpitArePaintedObjectsPlacedInsideTheWalkableChurch(int k)
        {
            var site = Church(k);
            using (var scene = new Scene(site.Map, site.Anchor))
            {
                var lm = Built(scene, "orientation " + k);
                var pews = Named(lm, "Church pew").ToArray();
                Assert.That(pews.Length, Is.GreaterThanOrEqualTo(6), "orientation " + k + ": both sides of the central aisle have pews");
                foreach (var pew in pews)
                {
                    Assert.That(pew.GetComponentsInChildren<Collider>(true), Is.Empty, "pews are presentation only and remain walk-through");
                    Assert.That(TryBounds(new[] { pew }, out var bounds), Is.True);
                    float x = bounds.center.x, y = -bounds.center.y / ArenaView.GroundSquash;
                    Assert.That(site.Interior.Any(t => Mathf.Abs(t.x + .5f - x) < 1.2f && Mathf.Abs(t.y + .5f - y) < 1.2f), Is.True,
                        "orientation " + k + ": pew remains within the room's floor");
                }
                Assert.That(Named(lm, "Church pulpit").Count(), Is.EqualTo(1), "one raised side pulpit");
                Assert.That(Named(lm, "Church stained glass").Count(), Is.GreaterThanOrEqualTo(2), "garnet lancets flank the nave");
                Assert.That(Named(lm, "Church candle flame").Count(), Is.GreaterThanOrEqualTo(2), "candles mark the side walls");
                Assert.That(Named(lm, "Church slate floor").Count(), Is.EqualTo(1), "one slate-and-rune finish over the interior floor");
            }
        }

        [TestCaseSource(nameof(Orientations))]
        public void OverheadPewBackCapFacesTheGraveyardEntrance(int k)
        {
            var site = Church(k);
            using (var scene = new Scene(site.Map, site.Anchor))
            {
                var lm = Built(scene, "orientation " + k);
                // The authored seat opens on sprite-local -Y. Use the two delivered door runs as the oracle.
                Vector2 front = new Vector2(site.Door.Average(t => (float)t.x), -site.Door.Average(t => (float)t.y));
                Vector2 rear = new Vector2(site.RearDoor.Average(t => (float)t.x), -site.RearDoor.Average(t => (float)t.y));
                var pews = Named(lm, "Church pew").ToArray();
                Assert.That(pews.Length, Is.GreaterThanOrEqualTo(6));
                foreach (var pew in pews)
                    Assert.That(Vector2.Dot(pew.up, (front - rear).normalized), Is.GreaterThan(.99f),
                        "the thin back cap is on the entrance side of every seat in both banks");
            }
        }

        [TestCaseSource(nameof(Orientations))]
        public void ObliqueMapBearingKeepsPewsSquareToTheDeliveredRoom(int k)
        {
            var site = Church(k);
            var a = site.Anchor;
            // Real maps report a 16-way bearing toward the arena centre even when doorways run due east/west.
            var oblique = new MapAnchor(a.Zone, a.Kind, a.Centre, a.Footprint, (byte)((a.Front + 1) % 16), a.Entrance);
            using (var scene = new Scene(site.Map, oblique))
            {
                var lm = Built(scene, "oblique front in orientation " + k);
                var rear = site.RearDoor[1];
                var front = site.Door[1];
                var back = new Vector2(front.x - rear.x, rear.y - front.y).normalized;
                var pews = Named(lm, "Church pew").ToArray();
                Assert.That(pews.Length, Is.GreaterThanOrEqualTo(6));
                foreach (var pew in pews)
                    Assert.That(Vector2.Dot(pew.up, back), Is.GreaterThan(.999f));
            }
        }

        [TestCaseSource(nameof(Orientations))]
        public void CandleBasesStandOnSlateClearOfThePewSeat(int k)
        {
            var site = Church(k);
            using (var scene = new Scene(site.Map, site.Anchor))
            {
                var lm = Built(scene, "orientation " + k);
                var candles = Named(lm, "Church candle flame").ToArray();
                Assert.That(candles.Length, Is.GreaterThanOrEqualTo(4));
                foreach (var pew in Named(lm, "Church pew"))
                {
                    var seat = pew.GetComponent<SpriteRenderer>().bounds;
                    foreach (var candle in candles)
                    {
                        var contact = candle.position;
                        bool touchesSeat = Mathf.Abs(contact.x - seat.center.x) < seat.extents.x + .16f
                            && Mathf.Abs(contact.y - seat.center.y) < seat.extents.y + .16f * ArenaView.GroundSquash;
                        Assert.That(touchesSeat, Is.False, "a standing candelabrum needs clear slate around its base");
                    }
                }
            }
        }

        [TestCaseSource(nameof(Orientations))]
        public void PewRowsCrossTheAisleAndThePulpitStandsUpright(int k)
        {
            var site = Church(k);
            using (var scene = new Scene(site.Map, site.Anchor))
            {
                var lm = Built(scene, "orientation " + k);
                bool aisleRunsOnX = site.Door[0].x == site.Door[1].x;
                foreach (var pew in Named(lm, "Church pew"))
                {
                    Assert.That(TryBounds(new[] { pew }, out var bounds), Is.True);
                    float across = aisleRunsOnX ? bounds.size.y / ArenaView.GroundSquash : bounds.size.x;
                    float along = aisleRunsOnX ? bounds.size.x : bounds.size.y / ArenaView.GroundSquash;
                    Assert.Greater(across, 3f * along, "the bench spans a bank of seats across the aisle");
                    Assert.Greater(across, 3f, "one bench seats a row of fighters");
                }
                var pulpit = Named(lm, "Church pulpit").Single();
                Assert.Greater(Vector3.Dot(pulpit.up, Vector3.up), .98f, "the raised pulpit stays upright in the camera");
            }
        }

        // ---- the graveyard ----------------------------------------------------------------------------------------------------------------

        [TestCaseSource(nameof(Orientations))]
        public void EveryHeadstoneGroupHasExactlyOneGravestoneObject(int k)
        {
            var site = Church(k);
            using (var scene = new Scene(site.Map, site.Anchor))
            {
                var lm = Built(scene, "orientation " + k);
                Assert.That(site.Groups.Count, Is.EqualTo(4), "guard on the test map: four headstone groups of six tiles");
                var stones = Named(lm, GraveName).ToArray();
                Assert.That(stones.Length, Is.EqualTo(site.Groups.Count), "orientation " + k + ": one '" + GraveName + "' per group");
                foreach (var s in stones) Assert.That(s.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThan(0), "a gravestone is drawn");
                foreach (var g in site.Groups)
                {
                    int x0 = g.Min(t => t.x), x1 = g.Max(t => t.x) + 1, y0 = g.Min(t => t.y), y1 = g.Max(t => t.y) + 1;
                    int here = 0;
                    foreach (var s in stones)
                    {
                        TryBounds(new[] { s }, out var b); float bx = b.center.x, by = -b.center.y / ArenaView.GroundSquash;
                        if (bx >= x0 - .5f && bx <= x1 + .5f && by >= y0 - .5f && by <= y1 + .5f) here++;
                    }
                    Assert.That(here, Is.EqualTo(1), "orientation " + k + ": the group at (" + x0 + ", " + y0 + ") has one gravestone object");
                }
            }
        }

        // ---- nothing extra ----------------------------------------------------------------------------------------------------------------

        [Test]
        public void AFrameWhereNothingChangedCreatesAndAllocatesNothing()
        {
            var site = Church(0);
            using (var scene = new Scene(site.Map, site.Anchor))
            {
                Built(scene, "orientation 0");
                var v = scene.View; var inside = TileCentre(site.Camp); var outside = TileCentre(site.Yard[0]);
                for (int i = 0; i < 20; i++) { v.NoteActor(inside); v.NoteActor(outside); v.Tick(1f / 60f); }
                int objects = scene.Root.GetComponentsInChildren<Transform>(true).Length;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 200; i++) { v.NoteActor(inside); v.NoteActor(outside); v.Tick(1f / 60f); }
                Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThanOrEqualTo(0), "200 unchanged frames allocate nothing");
                Assert.That(scene.Root.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(objects), "and create no object");
                Assert.That(objects, Is.GreaterThan(2), "the landmark has art objects");
                for (int i = 0; i < 20; i++) v.Tick(1f / 60f);
                before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 200; i++) v.Tick(1f / 60f);
                Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThanOrEqualTo(0), "200 idle frames at the opaque value");
            }
        }

        [Test]
        public void DisposingTheViewRemovesEverythingTheChurchDrew()
        {
            var site = Church(0);
            var scene = new Scene(site.Map, site.Anchor);
            try
            {
                var lm = Built(scene, "orientation 0");
                Assert.That(lm.Root.GetComponentsInChildren<Renderer>(true).Length, Is.GreaterThan(0), "it drew something");
                scene.View.Dispose();
                Assert.That(scene.Root.transform.childCount, Is.EqualTo(0), "no church object is left under the parent");
            }
            finally { scene.Dispose(); }
        }
    }
}
