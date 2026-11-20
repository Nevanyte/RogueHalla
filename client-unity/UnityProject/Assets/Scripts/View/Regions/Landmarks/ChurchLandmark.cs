using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>
    /// P-UNITY-LANDMARK-3: the Sentenced church's art (kind 2), painted procedurally once per map and derived only from the tiles the
    /// framework classified (never from a copy of the drawing), so it is right in all eight orientations. With opposing doorways,
    /// the building's split wall halves are reunited from their shared bounds; every other wall group is a headstone plot. Pieces:
    ///  - roof: a slate gable over the building's box, the ridge along its long axis, the two slopes shaded differently, with an eave
    ///    shadow; one roof piece through Landmark.AddRoofRenderer. It starts above the south wall's face (the face stays visible) and
    ///    reaches over the wall caps RegionWalls draws, fully opaque over them, so no block shows at a soft edge;
    ///  - door: a pointed stone arch with a dark opening (door in the south wall), else a flagstone sill and two jamb posts; the roof has
    ///    a notch over the door tiles (and a head's height above), so an actor in the doorway is never hidden by it;
    ///  - gate: two stone posts with lanterns at the ends of the five entrance tiles and a flagstone threshold across them;
    ///  - gravestones: each headstone group is a raised plot (covering its wall blocks) with a standing stone, cross or slab per tile.
    /// Colours are sRGB, as the region atlas; the palette follows the Sentenced zone (cool violet-grey masonry, muted ground).
    /// </summary>
    public static class ChurchLandmark
    {
        const float Sq = ArenaView.GroundSquash, Wall = RegionWalls.WallHeight;
        static readonly Color SlateLit = new Color(.37f, .38f, .48f), SlateDark = new Color(.20f, .20f, .28f), Ridge = new Color(.58f, .58f, .68f),
            Ink = new Color(.06f, .06f, .10f), Stone = new Color(.55f, .55f, .63f), StoneLit = new Color(.70f, .70f, .78f), StoneDark = new Color(.32f, .32f, .41f),
            Moss = new Color(.33f, .42f, .30f), Earth = new Color(.23f, .20f, .21f), DeadGrass = new Color(.31f, .34f, .26f), Warm = new Color(1f, .72f, .32f);


        sealed class Group
        {
            public int MinX = int.MaxValue, MinY = int.MaxValue, MaxX = -1, MaxY = -1;
            public readonly List<Vector2Int> Tiles = new List<Vector2Int>();
        }

        struct Box
        {
            public float X0, Y0, X1, Y1; public bool Valid;
            public bool Contains(float x, float y) { return Valid && x >= X0 && x <= X1 && y >= Y0 && y <= Y1; }
            public float Dist(float x, float y)
            {
                if (!Valid) return 99f;
                float dx = Mathf.Max(Mathf.Max(X0 - x, x - X1), 0f), dy = Mathf.Max(Mathf.Max(Y0 - y, y - Y1), 0f);
                return Mathf.Sqrt(dx * dx + dy * dy);
            }
        }

        public static void Build(Landmark lm)
        {
            var groups = FindGroups(lm.FootX, lm.FootY, lm.FootW, lm.FootH, lm.IsWall);
            var building = MainBuilding(groups);
            if (building == null) return;
            var cover = new List<Roguehalla.Net.Protocol.MapTile>();                                            // the roof's cover: the building's interior, walls and pillars (no headstone, gate or yard)
            for (int y = building.MinY; y <= building.MaxY; y++) for (int x = building.MinX; x <= building.MaxX; x++)
                if (lm.IsWall(x, y) || lm.IsInterior(x, y)) cover.Add(new Roguehalla.Net.Protocol.MapTile((ushort)x, (ushort)y));
            var doors = DoorRuns(lm, building);
            // The rear cut occupies tiles of the original roof footprint. Both open doorways
            // stay out of the fade mask and retain their visual roof notches.
            lm.SetFadeTiles(cover);
            var centre = new Vector2((building.MinX + building.MaxX) * .5f, (building.MinY + building.MaxY) * .5f);
            var notches = new List<Box>(doors.Count);
            var directions = new List<Vector2Int>(doors.Count);
            foreach (var door in doors)
            {
                var centreOfRun = ((Vector2)door[0] + (Vector2)door[door.Count - 1]) * .5f;
                if (Vector2.Dot(centreOfRun - centre, lm.FrontVector) < 0f)
                    foreach (var tile in door)
                        cover.Add(new Roguehalla.Net.Protocol.MapTile((ushort)tile.x, (ushort)tile.y));
                notches.Add(DoorNotch(building, door, out var normal));
                directions.Add(normal);
            }
            lm.SetCover(cover);
            var naveFront = NaveFront(lm, building);
            BuildSlateFloor(lm, building, naveFront);
            BuildRoof(lm, building, notches);
            for (int i = 0; i < doors.Count; i++) BuildDoor(lm, doors[i], directions[i]);
            BuildInteriorFurnishings(lm, building, naveFront);
            BuildWindows(lm, building, naveFront);
            if (lm.ArchTiles.Length > 0) BuildGate(lm);
            foreach (var g in groups)                                                   // a plot lies outside the building's box; the pillar blocks inside it stay as RegionWalls draws them
                if (g != building && !(g.MinX >= building.MinX && g.MaxX <= building.MaxX && g.MinY >= building.MinY && g.MaxY <= building.MaxY)) BuildGravestones(lm, g);
        }

        internal sealed class NaveFloor
        {
            readonly Roguehalla.Net.Protocol.MapRect _footprint;
            readonly Group _building;
            readonly bool[] _interior;

            internal NaveFloor() { }

            internal NaveFloor(MapBlob map, MapAnchor anchor)
            {
                _footprint = anchor.Footprint;
                _building = MainBuilding(FindGroups(_footprint.X, _footprint.Y, _footprint.W, _footprint.H,
                    (x, y) => map.Tiles[y * map.W + x] == MapBlob.Wall));
                if (_building != null)
                    _interior = LandmarkInterior.Find(map.Tiles, map.W, map.H, _footprint, anchor.Entrance);
            }

            internal bool Contains(int x, int y) =>
                _interior != null && x >= _building.MinX && x <= _building.MaxX &&
                y >= _building.MinY && y <= _building.MaxY &&
                _interior[(y - _footprint.Y) * _footprint.W + x - _footprint.X];
        }

        static readonly NaveFloor NoNave = new NaveFloor();
        static readonly ConditionalWeakTable<MapBlob, NaveFloor> NaveFloors = new ConditionalWeakTable<MapBlob, NaveFloor>();

        /// <summary>Gets the delivered church's painted floor mask for ordinary prop placement and its light and shadow consumers.</summary>
        internal static NaveFloor DeliveredNave(MapBlob map) => NaveFloors.GetValue(map, FindNaveFloor);

        static NaveFloor FindNaveFloor(MapBlob map)
        {
            foreach (var anchor in map.Anchors)
            {
                if (anchor.Zone != (byte)RegionId.Sentenced || anchor.Kind != MapBlob.AnchorKindChurch) continue;
                var f = anchor.Footprint;
                if (f.W == 0 || f.H == 0 || f.X + f.W > map.W || f.Y + f.H > map.H) continue;
                return new NaveFloor(map, anchor);
            }
            return NoNave;
        }

        static Group MainBuilding(List<Group> groups)
        {
            MergeOpposingWallHalves(groups);
            Group building = null;
            foreach (var group in groups)
                if (building == null || group.Tiles.Count > building.Tiles.Count) building = group;
            return building;
        }

        /// <summary>Two wall halves with identical long bounds are one building when the matching door gap is narrow.</summary>
        static void MergeOpposingWallHalves(List<Group> groups)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                var a = groups[i];
                for (int j = i + 1; j < groups.Count; j++)
                {
                    var b = groups[j];
                    if (!OpposingHalves(a, b)) continue;
                    a.Tiles.AddRange(b.Tiles);
                    a.MinX = Mathf.Min(a.MinX, b.MinX); a.MaxX = Mathf.Max(a.MaxX, b.MaxX);
                    a.MinY = Mathf.Min(a.MinY, b.MinY); a.MaxY = Mathf.Max(a.MaxY, b.MaxY);
                    a.Tiles.Sort((x, y) => x.y != y.y ? x.y.CompareTo(y.y) : x.x.CompareTo(y.x));
                    groups.RemoveAt(j);
                    return;
                }
            }
        }

        static bool OpposingHalves(Group a, Group b)
        {
            if (a.MinY == b.MinY && a.MaxY == b.MaxY && (a.MaxX < b.MinX || b.MaxX < a.MinX))
            {
                int gap = a.MaxX < b.MinX ? b.MinX - a.MaxX - 1 : a.MinX - b.MaxX - 1;
                return a.MaxY - a.MinY >= 7 && gap > 0 && gap <= LandmarkInterior.MaxOpening;
            }
            if (a.MinX == b.MinX && a.MaxX == b.MaxX && (a.MaxY < b.MinY || b.MaxY < a.MinY))
            {
                int gap = a.MaxY < b.MinY ? b.MinY - a.MaxY - 1 : a.MinY - b.MaxY - 1;
                return a.MaxX - a.MinX >= 7 && gap > 0 && gap <= LandmarkInterior.MaxOpening;
            }
            return false;
        }

        // ---- reading the tiles --------------------------------------------------------------------------------------------------

        static List<Group> FindGroups(int minX, int minY, int width, int height, Func<int, int, bool> isWall)
        {
            var groups = new List<Group>(); var seen = new bool[width * height]; var stack = new Stack<Vector2Int>();
            for (int y = minY; y < minY + height; y++) for (int x = minX; x < minX + width; x++)
            {
                int i = (y - minY) * width + x - minX;
                if (seen[i] || !isWall(x, y)) continue;
                var g = new Group(); seen[i] = true; stack.Push(new Vector2Int(x, y));
                while (stack.Count > 0)
                {
                    var t = stack.Pop(); g.Tiles.Add(t);
                    g.MinX = Mathf.Min(g.MinX, t.x); g.MaxX = Mathf.Max(g.MaxX, t.x); g.MinY = Mathf.Min(g.MinY, t.y); g.MaxY = Mathf.Max(g.MaxY, t.y);
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = t.x + dx, ny = t.y + dy;
                        if (nx < minX || ny < minY || nx >= minX + width || ny >= minY + height || !isWall(nx, ny)) continue;
                        int ni = (ny - minY) * width + nx - minX;
                        if (seen[ni]) continue;
                        seen[ni] = true; stack.Push(new Vector2Int(nx, ny));
                    }
                }
                g.Tiles.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
                groups.Add(g);
            }
            return groups;
        }

        /// <summary>Every small, wall-bounded opening on the building edge is a threshold.</summary>
        static List<List<Vector2Int>> DoorRuns(Landmark lm, Group b)
        {
            var doors = new List<List<Vector2Int>>();
            AddEdgeRuns(lm, b, b.MinY, true, doors);
            AddEdgeRuns(lm, b, b.MaxY, true, doors);
            AddEdgeRuns(lm, b, b.MinX, false, doors);
            AddEdgeRuns(lm, b, b.MaxX, false, doors);
            return doors;
        }

        static void AddEdgeRuns(Landmark lm, Group b, int edge, bool horizontal, List<List<Vector2Int>> doors)
        {
            int start = horizontal ? b.MinX : b.MinY, end = horizontal ? b.MaxX : b.MaxY;
            int position = start;
            while (position <= end)
            {
                Vector2Int At(int n) { return horizontal ? new Vector2Int(n, edge) : new Vector2Int(edge, n); }
                if (lm.IsWall(At(position).x, At(position).y)) { position++; continue; }
                int runStart = position;
                while (position <= end && !lm.IsWall(At(position).x, At(position).y)) position++;
                int runLength = position - runStart;
                bool bounded = runStart > start && position <= end;
                if (bounded && runLength > 0 && runLength <= LandmarkInterior.MaxOpening)
                {
                    var door = new List<Vector2Int>(runLength);
                    for (int n = runStart; n < position; n++) door.Add(At(n));
                    doors.Add(door);
                }
            }
        }

        static void Extent(List<Vector2Int> tiles, out int x0, out int y0, out int x1, out int y1)
        {
            x0 = int.MaxValue; y0 = int.MaxValue; x1 = -1; y1 = -1;
            foreach (var t in tiles) { x0 = Mathf.Min(x0, t.x); x1 = Mathf.Max(x1, t.x); y0 = Mathf.Min(y0, t.y); y1 = Mathf.Max(y1, t.y); }
        }

        /// <summary>The roof opening over a doorway and a head's height above it, extended beyond its outside edge.</summary>
        static Box DoorNotch(Group b, List<Vector2Int> door, out Vector2Int outward)
        {
            Extent(door, out int x0, out int y0, out int x1, out int y1);
            if (y0 == y1)
            {
                bool north = y0 == b.MinY;
                outward = new Vector2Int(0, north ? 1 : -1);
                float y = north ? -y0 * Sq : -(y0 + 1) * Sq;
                return new Box { Valid = true, X0 = x0, X1 = x1 + 1, Y0 = y - (north ? .1f : 0f) - (north ? 0f : Wall), Y1 = y + (north ? Wall : .1f) + .5f };
            }
            bool west = x0 == b.MinX;
            outward = new Vector2Int(west ? -1 : 1, 0);
            float x = west ? x0 : x0 + 1;
            return new Box { Valid = true, X0 = x - (west ? Wall + .5f : .1f), X1 = x + (west ? .1f : Wall + .5f), Y0 = -(y1 + 1) * Sq, Y1 = -y0 * Sq };
        }

        // ---- the canvas ---------------------------------------------------------------------------------------------------------

        /// <summary>A world-space painting surface: pixels are composited "over" in call order; one texture and sprite at the end.</summary>
        sealed class Canvas
        {
            public readonly float X0, Y0; public readonly int W, H, Ppu; readonly Color[] _px;
            public Canvas(float x0, float y0, float w, float h, int ppu)
            {
                X0 = x0; Y0 = y0; Ppu = ppu; W = Mathf.CeilToInt(w * ppu); H = Mathf.CeilToInt(h * ppu); _px = new Color[W * H];
            }
            public void Paint(Func<float, float, Color> f) { PaintIn(X0, Y0, X0 + W / (float)Ppu, Y0 + H / (float)Ppu, f); }
            public void PaintIn(float xa, float ya, float xb, float yb, Func<float, float, Color> f)
            {
                int i0 = Mathf.Max(0, Mathf.FloorToInt((xa - X0) * Ppu)), i1 = Mathf.Min(W - 1, Mathf.CeilToInt((xb - X0) * Ppu));
                int j0 = Mathf.Max(0, Mathf.FloorToInt((ya - Y0) * Ppu)), j1 = Mathf.Min(H - 1, Mathf.CeilToInt((yb - Y0) * Ppu));
                for (int j = j0; j <= j1; j++) for (int i = i0; i <= i1; i++)
                {
                    var c = f(X0 + (i + .5f) / Ppu, Y0 + (j + .5f) / Ppu);
                    if (c.a <= 0f) continue;
                    int k = j * W + i; var d = _px[k]; float oa = c.a + d.a * (1f - c.a);
                    _px[k] = new Color((c.r * c.a + d.r * d.a * (1f - c.a)) / oa, (c.g * c.a + d.g * d.a * (1f - c.a)) / oa, (c.b * c.a + d.b * d.a * (1f - c.a)) / oa, oa);
                }
            }
            public SpriteRenderer ToSprite(Landmark lm, string name, string objectName, int order)
            {
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false, false) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                tex.SetPixels(_px); tex.Apply(false, true); lm.Own(tex);
                var sprite = Sprite.Create(tex, new Rect(0, 0, W, H), Vector2.zero, Ppu, 0, SpriteMeshType.FullRect); lm.Own(sprite);
                var go = new GameObject(objectName); go.transform.SetParent(lm.Root.transform, false);
                go.transform.localPosition = new Vector3(X0, Y0, 0);
                var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = sprite; sr.sharedMaterial = lm.Material; sr.sortingOrder = order;
                return sr;
            }
        }


        static Color Shade(Color c, float k) { return new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), 1f); }
        static Color WithAlpha(Color c, float a) { c.a = a; return c; }
        static float Noise(float x, float y, int seed) { return RegionNoise.Periodic(x, y, 61, seed); }

        // ---- the nave: a painted slate grid with faint inlaid runes ----------------------------------------------------------

        static Vector2Int NaveFront(Landmark lm, Group room)
        {
            var direction = lm.FrontVector;
            if (lm.ArchTiles.Length > 0)
            {
                // The delivered graveyard gate sits on the church axis. The arena bearing can be oblique.
                direction = new Vector2(lm.ArchCentre.x - (room.MinX + room.MaxX + 1) * .5f,
                    -lm.ArchCentre.y / Sq - (room.MinY + room.MaxY + 1) * .5f);
            }
            if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
                return direction.x > 0f ? Vector2Int.right : Vector2Int.left;
            return direction.y > 0f ? Vector2Int.up : Vector2Int.down;
        }

        static void BuildSlateFloor(Landmark lm, Group room, Vector2Int naveFront)
        {
            const int ppu = 24;
            bool acrossX = naveFront.y != 0;
            float centre = acrossX ? (room.MinX + room.MaxX + 1) * .5f : (room.MinY + room.MaxY + 1) * .5f;
            var wash = Resources.Load<Texture2D>("StormInk/Church/SlateWash");
            var pigment = wash != null ? wash.GetPixels32() : null;
            float x0 = room.MinX, y0 = -(room.MaxY + 1) * Sq;
            var cv = new Canvas(x0, y0, room.MaxX - room.MinX + 1, (room.MaxY - room.MinY + 1) * Sq, ppu);
            cv.Paint((wx, wy) =>
            {
                float mapY = -wy / Sq;
                int tx = Mathf.FloorToInt(wx), ty = Mathf.FloorToInt(mapY);
                if (!lm.IsInterior(tx, ty)) return default;
                Color stone = new Color(.44f, .42f, .42f);
                if (pigment != null)
                {
                    int px = Mathf.FloorToInt(Mathf.Repeat(wx / 6f, 1f) * wash.width);
                    int py = Mathf.FloorToInt(Mathf.Repeat(-mapY / 6f, 1f) * wash.height);
                    Color brush = pigment[py * wash.width + px];
                    stone = Color.Lerp(new Color(brush.grayscale, brush.grayscale, brush.grayscale * 1.05f), brush, .38f);
                }
                float across = (acrossX ? wx : mapY) - centre;
                float along = acrossX ? mapY - room.MinY : wx - room.MinX;
                float halfWidth = Mathf.Abs(across);
                float wear = 1f - Mathf.SmoothStep(0f, 1f, halfWidth / 2.25f);
                stone = Color.Lerp(stone * .91f, stone * 1.16f + new Color(.025f, .017f, .008f), wear);
                // One quiet line of carved lozenges follows the two doors. The middle crossing stays clear.
                float rhythm = Mathf.Abs(Mathf.Repeat(along + 1.25f, 4.5f) - 2.25f);
                float cut = Mathf.Abs(halfWidth + rhythm * .76f - .38f);
                if (halfWidth < .43f && rhythm < .58f && cut < .045f) stone = Shade(stone, .54f);
                stone.a = 1f;
                return stone;
            });
            cv.ToSprite(lm, "Church slate and runes", "Church slate floor", -88);
        }

        // Both banks have the same facing. Height on screen follows projection; upright props keep their painted vertical.
        static void BuildInteriorFurnishings(Landmark lm, Group room, Vector2Int naveFront)
        {
            bool aisleOnX = naveFront.x != 0;
            float alongMin = aisleOnX ? room.MinX : room.MinY, alongMax = aisleOnX ? room.MaxX : room.MaxY;
            float across = aisleOnX ? (room.MinY + room.MaxY + 1) * .5f : (room.MinX + room.MaxX + 1) * .5f;
            var pew = Resources.Load<Sprite>("StormInk/Church/OakPew");
            // The narrow back cap lies along the image's +Y edge. Point it toward the entrance.
            var pewAngle = Mathf.Atan2(-naveFront.x, -naveFront.y) * Mathf.Rad2Deg;
            float[] rows = { alongMin + 1.55f, alongMin + 2.65f, alongMax - 1.55f, alongMax - .45f };
            foreach (float row in rows)
            for (int bank = -1; bank <= 1; bank += 2)
            {
                float bankCentre = across + bank * 4.85f;
                float x = aisleOnX ? row : bankCentre, y = aisleOnX ? bankCentre : row;
                const float length = 4.55f, depth = .82f;
                int first = Mathf.FloorToInt(bankCentre - length * .5f), last = Mathf.FloorToInt(bankCentre + length * .5f);
                bool fits = true;
                for (int tile = first; tile <= last; tile++)
                {
                    if (lm.IsInterior(aisleOnX ? Mathf.FloorToInt(x) : tile, aisleOnX ? tile : Mathf.FloorToInt(y))) continue;
                    fits = false;
                    break;
                }
                if (fits)
                    AddAssetSprite(lm, pew, "Church pew", x, y, aisleOnX ? length * Sq : length,
                        aisleOnX ? depth : depth * Sq, pewAngle, 2);
            }
            var pulpit = Resources.Load<Sprite>("StormInk/Church/GothicPulpit");
            float front = aisleOnX ? naveFront.x : naveFront.y;
            float nearRear = front > 0 ? alongMin + 2.2f : alongMax - 1.2f;
            float onSide = across + (front > 0 ? 8.8f : -8.8f);
            float px = aisleOnX ? nearRear : onSide, py = aisleOnX ? onSide : nearRear;
            if (lm.IsInterior(Mathf.FloorToInt(px), Mathf.FloorToInt(py)))
                AddAssetSprite(lm, pulpit, "Church pulpit", px, py, 2.15f, 2.5f, 0f, 3);
        }

        static void AddAssetSprite(Landmark lm, Sprite sprite, string objectName, float x, float row,
            float width, float height, float rotation, int sortOffset, bool flame = false)
        {
            if (sprite == null) return;
            var go = new GameObject(objectName);
            go.transform.SetParent(lm.Root.transform, false);
            go.transform.localPosition = new Vector3(x, -row * Sq, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, rotation);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = lm.PaintedMaterial(flame);
            renderer.sortingOrder = 2000 + 16 * (Mathf.FloorToInt(row) + 1) + sortOffset;
            Vector2 size = sprite.bounds.size;
            go.transform.localScale = new Vector3(width / size.x, height / size.y, 1f);
            if (flame)
            {
                float pivot = sprite.pivot.y / sprite.rect.height;
                lm.CandleSockets.Add(new Vector3(x, row, (.70f - pivot) * height));
            }
        }

        static void BuildWindows(Landmark lm, Group room, Vector2Int naveFront)
        {
            var glass = Resources.Load<Sprite>("StormInk/Church/GarnetLancet");
            var candles = Resources.Load<Sprite>("StormInk/Church/Candelabrum");
            bool aisleOnX = naveFront.x != 0;
            float across = aisleOnX ? (room.MinY + room.MaxY + 1) * .5f : (room.MinX + room.MaxX + 1) * .5f;
            int first = aisleOnX ? room.MinX : room.MinY, last = aisleOnX ? room.MaxX : room.MaxY;
            float[] bays = { -8.2f, -4.2f, 4.2f, 8.2f };
            // Set the light in the reveals and plant its candelabrum on the floor inside it.
            foreach (int end in new[] { first, last })
            foreach (float bay in bays)
            {
                float wall = end + .5f;
                float x = aisleOnX ? wall : across + bay, y = aisleOnX ? across + bay : wall;
                if (!lm.IsWall(Mathf.FloorToInt(x), Mathf.FloorToInt(y))) continue;
                AddAssetSprite(lm, glass, "Church stained glass", x, y, 1.13f, 1.92f, 0f, 4);
                float inside = end == first ? 1.1f : -1.1f;
                // The inner pair brackets the three-tile doorway. The outer pair stands beyond each pew end.
                float candleBay = across + Mathf.Sign(bay) * (Mathf.Abs(bay) < 6f ? 1.95f : 9f);
                float cx = aisleOnX ? x + inside : candleBay;
                float cy = aisleOnX ? candleBay : y + inside;
                if (lm.IsInterior(Mathf.FloorToInt(cx), Mathf.FloorToInt(cy)))
                    // The transparent headroom holds the smoke. Its lower pivot anchors the candle base.
                    AddAssetSprite(lm, candles, "Church candle flame", cx, cy, .68f, 1.295f, 0f, 5, flame: true);
            }
        }

        // ---- the roof -----------------------------------------------------------------------------------------------------------

        static void BuildRoof(Landmark lm, Group b, List<Box> notches)
        {
            const int P = 16; const float Ov = .35f, Course = .32f;
            float xa = b.MinX - Ov, xb = b.MaxX + 1 + Ov;
            float ya = -(b.MaxY + 1) * Sq + Wall, yb = -b.MinY * Sq + Wall + .12f;   // the roof's lower edge is the wall-cap line: the south wall's whole face shows below it and an actor in front is not overlapped; the north caps are under yb
            bool ridgeX = b.MaxX - b.MinX >= b.MaxY - b.MinY;
            float xr = (xa + xb) / 2f, yr = (ya + yb) / 2f, half = ridgeX ? (yb - ya) / 2f : (xb - xa) / 2f;
            var cv = new Canvas(xa - .15f, ya, xb - xa + .3f, yb - ya + .15f, P);
            cv.Paint((wx, wy) =>
            {
                for (int i = 0; i < notches.Count; i++) if (notches[i].Contains(wx, wy)) return default;
                float outside = Mathf.Max(Mathf.Max(xa - wx, wx - xb), Mathf.Max(ya - wy, wy - yb));
                if (outside > 0f)
                {
                    return default;
                }
                float d = ridgeX ? Mathf.Abs(wy - yr) : Mathf.Abs(wx - xr);
                bool lit = ridgeX ? wy > yr : wx < xr;                                  // the north / west slope takes the light
                float u = ridgeX ? wx : wy / Sq;
                float k = d / Course; int idx = Mathf.FloorToInt(k); float fk = k - idx;
                float row = u * 2f + (idx & 1) * .5f; int col = Mathf.FloorToInt(row); float fc = row - col;
                float tint = .86f + .26f * RegionNoise.Hash01(col, idx + (lit ? 0 : 77), 31);
                var c = Shade(lit ? SlateLit : SlateDark, tint * (1.12f - .22f * d / half));
                if (fk > .8f) c = Shade(c, .7f); else if (fk < .1f && idx > 0) c = Shade(c, 1.12f);
                if (fc < .05f) c = Shade(c, .78f);
                float n = Noise(wx * .5f, wy * .5f, 3);
                if (n > .62f) c = Color.Lerp(c, Moss, .22f * Mathf.Clamp01((n - .62f) / .3f));
                if (d < .17f) { c = Color.Lerp(Ridge, StoneLit, d < .05f ? .5f : 0f); if (d > .13f) c = Shade(c, .8f); }
                else if (d < .26f && !lit) c = Shade(c, .72f);                         // the ridge cap's shadow on the dark slope
                if (-outside < .05f) c = Ink;
                else if (-outside < .11f && (ridgeX ? (wx < xa + .11f || wx > xb - .11f) : (wy < ya + .11f || wy > yb - .11f))) c = Shade(c, 1.25f);   // the gable edge board
                if (wy < ya + .3f && wx > xa + .05f && wx < xb - .05f) c = Shade(c, .62f + .38f * (wy - ya) / .3f);   // the eave's shadow, on the roof's own lower rows
                for (int i = 0; i < notches.Count; i++) if (notches[i].Dist(wx, wy) < .06f) { c = Ink; break; }
                c.a = 1f; return c;
            });
            lm.AddRoofRenderer(cv.ToSprite(lm, "Church roof", "Church roof (slate gable)", 0));
        }

        // ---- the door ------------------------------------------------------------------------------------------------------------

        static void BuildDoor(Landmark lm, List<Vector2Int> door, Vector2Int outward)
        {
            Extent(door, out int x0, out int y0, out int x1, out int y1);
            if (y0 == y1) { BuildArchDoor(lm, x0, x1, y0, outward.y); return; }
            Flagstones(lm, "Church door", x0, y0, x1, y1, -84);                                    // else: a worn sill and two jamb posts
            float cx = x0 + .5f;
            Post(lm, "Church door", cx, -y0 * Sq, .62f, 1.4f, false, 2000 + 16 * (y0 - 1) + 1);
            Post(lm, "Church door", cx, -(y1 + 1) * Sq, .62f, 1.4f, false, 2000 + 16 * y1 + 1);
        }

        /// <summary>A pointed stone arch on the door tiles with a dark opening (see-through at the floor), a keystone and a cross.</summary>
        static void BuildArchDoor(Landmark lm, int x0, int x1, int row, int outwardSign)
        {
            const int P = 24; const float OutA = 1.5f, OutSpring = .8f, OutRise = .9f, InA = 1.0f, InSpring = .72f, InRise = .55f;
            float half = (x1 - x0 + 1) / 2f, cx = (x0 + x1 + 1) / 2f, scale = half / OutA;
            float baseY = outwardSign > 0 ? -row * Sq : -(row + 1) * Sq;
            var cv = new Canvas(cx - half - .1f, baseY - .02f, 2f * half + .2f, 2.1f, P);
            cv.Paint((wx, wy) =>
            {
                float lx = (wx - cx) / scale, ly = (wy - baseY) * outwardSign, dx = Mathf.Abs(lx);
                float apex = OutSpring + OutRise;
                if (ly > apex - .03f)                                                  // the cross on the apex
                {
                    bool bar = dx < .05f && ly < 1.92f, arm = dx < .15f && ly > 1.78f && ly < 1.88f;
                    if (!bar && !arm) return default;
                    bool edge = bar && !arm ? dx > .035f : (dx > .135f || ly < 1.795f || ly > 1.865f) && !(dx < .035f);
                    return edge ? Ink : Shade(Stone, lx < 0f ? 1.1f : .85f);
                }
                float outTop = OutSpring + OutRise * Mathf.Pow(Mathf.Max(0f, 1f - dx / OutA), .7f);
                float outSd = Mathf.Min(outTop - ly, OutA - dx);
                bool sill = ly < .1f && dx < OutA + .06f;
                if (outSd < 0f && !sill) return default;
                float inTop = InSpring + InRise * Mathf.Pow(Mathf.Max(0f, 1f - dx / InA), .7f);
                float inSd = Mathf.Min(inTop - ly, InA - dx);
                if (inSd > 0f && ly > .1f)
                {
                    float a = Mathf.Lerp(.12f, .9f, Mathf.SmoothStep(0f, 1f, ly / 1.2f)) * Mathf.Lerp(1f, .8f, dx / InA);
                    var col = Color.Lerp(new Color(.04f, .03f, .06f), Warm * .5f, Mathf.Clamp01(.35f - ly) * .6f * (1f - dx / InA));
                    if (inSd < .05f) return Ink;
                    return new Color(col.r, col.g, col.b, a);
                }
                float t = Mathf.Clamp01(.55f + .35f * (-lx / OutA) - ly * .08f + (Noise(wx * 3f, wy * 3f, 17) - .5f) * .25f);
                var c = Color.Lerp(StoneDark, StoneLit, t);
                if (ly <= OutSpring) { float cr = (ly + ((int)(dx / .5f) & 1) * .15f) / .3f; if (cr - Mathf.Floor(cr) < .07f) c = Shade(c, .72f); }   // jamb courses
                else { float jc = Mathf.Atan2(ly - InSpring + .2f, lx) * Mathf.Rad2Deg / 14f; if (jc - Mathf.Floor(jc) < .08f) c = Shade(c, .7f); }          // arch ring joints
                if (dx < .2f && ly > 1.28f && ly < 1.66f) c = Shade(Stone, 1.15f);                                                                                   // keystone
                if (sill) c = Shade(Stone, .85f + .1f * Noise(wx * 4f, 1f, 9));
                if (!sill && outSd < .05f || inSd > -.05f && inSd <= 0f) c = Ink;
                return new Color(c.r, c.g, c.b, 1f);
            });
            cv.ToSprite(lm, "Church door arch", "Church door", 2000 + 16 * row);
        }

        // ---- the main gate -------------------------------------------------------------------------------------------------------

        static void BuildGate(Landmark lm)
        {
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            foreach (var t in lm.ArchTiles) { minX = Mathf.Min(minX, t.X); maxX = Mathf.Max(maxX, t.X); minY = Mathf.Min(minY, t.Y); maxY = Mathf.Max(maxY, t.Y); }
            bool vertical = maxY - minY > maxX - minX;                                     // taller than wide: the gate runs north-south
            Flagstones(lm, "Church gate", minX, minY, maxX, maxY, -84);
            if (!vertical)
            {
                float baseY = -(minY + .85f) * Sq;
                Post(lm, "Church gate", minX, baseY, .86f, 1.9f, true, 2000 + 16 * minY + 1);
                Post(lm, "Church gate", maxX + 1, baseY, .86f, 1.9f, true, 2000 + 16 * minY + 1);
            }
            else
            {
                float cx = minX + .5f;
                Post(lm, "Church gate", cx, -minY * Sq, .86f, 1.9f, true, 2000 + 16 * (minY - 1) + 1);
                Post(lm, "Church gate", cx, -(maxY + 1) * Sq, .86f, 1.9f, true, 2000 + 16 * maxY + 1);
            }
        }

        /// <summary>A stone post (plinth, courses, cap, a pyramid top) with an optional lantern and glow; base centre at (cx, baseY).</summary>
        static void Post(Landmark lm, string objectName, float cx, float baseY, float w, float h, bool lantern, int order)
        {
            const int P = 24; const float Pad = .55f;
            var cv = new Canvas(cx - w / 2f - Pad, baseY - .2f, w + 2f * Pad, h + .2f + (lantern ? .6f : .15f), P);
            cv.Paint((wx, wy) =>
            {
                float lx = wx - cx, ly = wy - baseY, hw = w / 2f;
                if (ly < 0f)                                                            // the contact shadow on the ground
                {
                    float e = (lx / (hw * 1.5f)) * (lx / (hw * 1.5f)) + ((ly + .02f) / .15f) * ((ly + .02f) / .15f);
                    return e < 1f ? new Color(0f, 0f, .03f, .38f * (1f - e)) : default;
                }
                float half = HalfWidth(ly, h, hw);
                if (lantern && ly >= h)
                {
                    float gy = ly - (h + .2f), r = Mathf.Sqrt(lx * lx + gy * gy), ax = Mathf.Abs(lx);
                    if (ly <= h + .31f && ax < .11f) return ax > .085f || ly < h + .03f || ly > h + .28f ? Ink : Color.Lerp(Warm, new Color(1f, .93f, .66f), 1f - r / .2f);
                    if (ly > h + .31f && ly < h + .4f && ax < .17f - (ly - h - .31f) * 1.8f) return Ink;
                    if (r < .55f) { float g = 1f - r / .55f; return WithAlpha(Warm, .3f * g * g); }
                    return default;
                }
                if (half < 0f || Mathf.Abs(lx) > half) return default;
                float s = lx / Mathf.Max(half, .01f);
                var c = Color.Lerp(StoneLit, StoneDark, Mathf.Clamp01(.5f + s * .45f));
                float cr = (ly + ((int)Mathf.Floor((lx + hw) / (hw * .9f)) & 1) * .16f) / .32f;
                if (ly < h - .5f && cr - Mathf.Floor(cr) < .06f) c = Shade(c, .72f);
                float n = Noise(wx * 5f, wy * 5f, 21);
                c = Shade(c, .92f + n * .16f);
                if (ly < .3f && n > .5f) c = Color.Lerp(c, Moss, .45f * Mathf.Clamp01((.3f - ly) / .3f));
                if (Mathf.Abs(lx) > half - .045f || ly < .02f) c = Ink;
                return new Color(c.r, c.g, c.b, 1f);
            });
            cv.ToSprite(lm, "Church post", objectName, order);
        }

        /// <summary>Half-width of a post at height ly (negative outside): plinth, shaft, cap slab, pyramid top.</summary>
        static float HalfWidth(float ly, float h, float hw)
        {
            if (ly < 0f || ly > h) return -1f;
            if (ly < .2f) return hw + .07f;
            float capTop = h - .34f;
            if (ly > capTop) return (hw + .02f) * Mathf.Clamp01(1f - (ly - capTop) / .34f) + .02f;
            if (ly > capTop - .16f) return hw + .1f;
            return hw;
        }

        /// <summary>Worn flagstone paving over a rectangle of tiles (the gate's threshold, a side door's sill), just above the ground.</summary>
        static void Flagstones(Landmark lm, string objectName, int tx0, int ty0, int tx1, int ty1, int order)
        {
            const int P = 16;
            float ex0 = tx0, ex1 = tx1 + 1, ey0 = -(ty1 + 1) * Sq, ey1 = -ty0 * Sq;
            var cv = new Canvas(ex0, ey0, ex1 - ex0, ey1 - ey0, P);
            cv.Paint((wx, wy) =>
            {
                float v = -wy / Sq, rowH = .5f; int row = Mathf.FloorToInt(v / rowH); float fr = v / rowH - row;
                float u2 = wx + (row & 1) * .4f, cw = .8f; int col = Mathf.FloorToInt(u2 / cw); float fc = u2 / cw - col;
                var c = Shade(new Color(.42f, .41f, .47f), .86f + .28f * RegionNoise.Hash01(col, row, 41));
                if (fr < .09f || fc * cw < .05f) c = Shade(c, .62f);
                if (Mathf.Min(Mathf.Min(wx - ex0, ex1 - wx), Mathf.Min(wy - ey0, ey1 - wy)) < .05f) c = Shade(c, .6f);
                c.a = .96f; return c;
            });
            cv.ToSprite(lm, "Church flagstones", objectName, order);
        }

        // ---- the graveyard ---------------------------------------------------------------------------------------------------------

        /// <summary>One plot per headstone group: a raised bed that covers the group's wall blocks, with a stone, cross or slab on each tile.</summary>
        static void BuildGravestones(Landmark lm, Group g)
        {
            const int P = 20;
            int gw = g.MaxX - g.MinX + 1, gh = g.MaxY - g.MinY + 1; var has = new bool[gw * gh];
            foreach (var t in g.Tiles) has[(t.y - g.MinY) * gw + t.x - g.MinX] = true;
            Func<int, int, bool> inGroup = (x, y) => x >= g.MinX && y >= g.MinY && x <= g.MaxX && y <= g.MaxY && has[(y - g.MinY) * gw + x - g.MinX];
            float xa = g.MinX - .12f, xb = g.MaxX + 1.12f, ya = -(g.MaxY + 1) * Sq - .1f, yb = -g.MinY * Sq + Wall + 1.2f;
            var cv = new Canvas(xa, ya, xb - xa, yb - ya, P);
            cv.Paint((wx, wy) => Bed(wx, wy, inGroup));
            foreach (var t in g.Tiles)                                                  // north to south, so a stone in front covers the one behind
            {
                int hash = (int)(RegionNoise.Hash(t.x, t.y, 53) % 1000u);
                float cx = t.x + .5f + ((hash % 7) - 3) * .025f, baseY = Wall - (t.y + .78f) * Sq, sw = .5f + (hash % 5) * .02f, sh = .72f + ((hash / 7) % 5) * .07f;
                DrawStone(cv, cx, baseY, sw, sh, hash % 4);
            }
            cv.ToSprite(lm, "Church gravestones", "Church gravestone", 2000 + 16 * g.MaxY + 1);
        }

        static Color Bed(float wx, float wy, Func<int, int, bool> inGroup)
        {
            float v = (Wall - wy) / Sq; int ty = Mathf.FloorToInt(v), tx = Mathf.FloorToInt(wx);
            if (inGroup(tx, ty))
            {
                float fu = wx - tx, fv = v - ty;
                float n = Noise(wx * 2.2f, wy * 2.2f, 5), n2 = Noise(wx * 7f, wy * 7f, 8);
                var c = Color.Lerp(Earth, DeadGrass, Mathf.Clamp01((n - .38f) * 2.5f)); c = Shade(c, .88f + .24f * n2);
                const float Lip = .1f;
                bool edge = !inGroup(tx - 1, ty) && fu < Lip || !inGroup(tx + 1, ty) && fu > 1f - Lip || !inGroup(tx, ty - 1) && fv < Lip * .8f || !inGroup(tx, ty + 1) && fv > 1f - Lip * .8f;
                if (edge) c = Color.Lerp(StoneDark, Stone, fv < .5f ? 1f : .6f);
                if (!inGroup(tx, ty - 1) && fv < .035f || !inGroup(tx - 1, ty) && fu < .03f || !inGroup(tx + 1, ty) && fu > .97f) c = Ink;
                return new Color(c.r, c.g, c.b, 1f);
            }
            float v2 = -wy / Sq; int t2 = Mathf.CeilToInt(v2) - 1;
            if (inGroup(tx, t2) && !inGroup(tx, t2 + 1))
            {
                float h = wy + (t2 + 1) * Sq;                                          // up the south face from the tile's bottom edge
                if (h >= 0f && h <= Wall + .005f)
                {
                    float k = h / Wall, course = h / .22f;
                    var c = Shade(Color.Lerp(StoneDark, Stone, .35f + .3f * Noise(wx * 3f, 1f, 12)), .75f + .4f * k);
                    float jx = (wx + ((int)Mathf.Floor(course) & 1) * .3f) / .6f;
                    if (course - Mathf.Floor(course) < .09f || jx - Mathf.Floor(jx) < .05f) c = Shade(c, .7f);
                    if (h < .02f || (!inGroup(tx - 1, t2) && wx - tx < .03f) || (!inGroup(tx + 1, t2) && tx + 1 - wx < .03f)) c = Ink;
                    return new Color(c.r, c.g, c.b, 1f);
                }
            }
            return default;
        }

        /// <summary>Type 0 round-topped slab, 1 pointed (gothic) slab, 2 cross, 3 chamfered slab; (cx, baseY) is the stone's foot.</summary>
        static void DrawStone(Canvas cv, float cx, float baseY, float w, float h, int type)
        {
            float hw = w / 2f;
            cv.PaintIn(cx - hw - .3f, baseY - .2f, cx + hw + .3f, baseY + h + .1f, (wx, wy) =>
            {
                float lx = wx - cx, ly = wy - baseY;
                if (!Inside(type, lx, ly, hw, h, 0f))
                {
                    if (Inside(type, lx, ly, hw, h, .04f)) return Ink;
                    float sh = (lx - .09f) / (hw * 1.5f), sv = (ly + .04f) / .1f;       // the shadow on the bed, to the right and slightly under
                    if (sh * sh + sv * sv < 1f && ly < .1f) return new Color(0f, 0f, .02f, .4f);
                    return default;
                }
                float s = Mathf.Clamp(lx / hw, -1f, 1f);
                var c = Color.Lerp(StoneLit, StoneDark, Mathf.Clamp01(.42f + s * .4f + (1f - ly / h) * .1f));
                c = Shade(c, .9f + .2f * Noise(wx * 6f, wy * 6f, 33));
                if (ly < .16f) { float n = Noise(wx * 8f, wy * 8f, 35); if (n > .45f) c = Color.Lerp(c, Moss, .55f * Mathf.Clamp01((.16f - ly) / .16f)); }
                if (type != 2)                                                          // carved lines and a small cross
                {
                    if (Mathf.Abs(lx) < hw * .55f && ((ly > h * .46f && ly < h * .46f + .028f) || (ly > h * .36f && ly < h * .36f + .028f))) c = Shade(c, .6f);
                    if (Mathf.Abs(lx) < .02f && ly > h * .6f && ly < h * .82f || Mathf.Abs(lx) < .075f && ly > h * .7f && ly < h * .7f + .03f) c = Shade(c, .55f);
                }
                else if (ly < .12f) c = Shade(c, .8f);
                return new Color(c.r, c.g, c.b, 1f);
            });
        }

        static bool Inside(int type, float lx, float ly, float hw, float h, float e)
        {
            float ax = Mathf.Abs(lx); if (ly < -e * .3f) return false;
            switch (type)
            {
                case 0:
                {
                    float ex = hw + e; if (ax > ex) return false;
                    float cy = h - hw; if (ly <= cy) return ly <= h + e;
                    return ax * ax + (ly - cy) * (ly - cy) <= ex * ex;
                }
                case 1:
                {
                    float ex = hw + e, spring = h * .68f; if (ax > ex) return false;
                    return ly <= spring + (h - spring + e) * Mathf.Pow(Mathf.Max(0f, 1f - ax / ex), .75f);
                }
                case 2:
                    return ax <= .045f + e && ly <= h + e || ax <= hw * .55f + e && ly >= h * .56f - e && ly <= h * .56f + .13f + e || ax <= hw * .7f + e && ly <= .1f + e;
                default:
                    return ax <= hw + e && ly <= h + e - Mathf.Max(0f, ax - (hw - .12f)) * 1.2f;
            }
        }
    }
}
