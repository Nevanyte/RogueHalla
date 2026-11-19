using System;
using System.Collections.Generic;
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
        static readonly Color WineGlass = new Color(.34f, .055f, .11f), WineLight = new Color(.62f, .13f, .20f), Wood = new Color(.34f, .19f, .11f),
            WoodLight = new Color(.58f, .36f, .21f), WoodDark = new Color(.14f, .09f, .08f), Rune = new Color(.77f, .67f, .51f);

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
            var groups = FindGroups(lm);
            if (groups.Count == 0) return;
            MergeOpposingWallHalves(groups);
            var building = groups[0];
            foreach (var g in groups) if (g.Tiles.Count > building.Tiles.Count) building = g;
            var cover = new List<Roguehalla.Net.Protocol.MapTile>();                                            // the roof's cover: the building's interior, walls and pillars (no headstone, gate or yard)
            for (int y = building.MinY; y <= building.MaxY; y++) for (int x = building.MinX; x <= building.MaxX; x++)
                if (lm.IsWall(x, y) || lm.IsInterior(x, y)) cover.Add(new Roguehalla.Net.Protocol.MapTile((ushort)x, (ushort)y));
            lm.SetCover(cover);
            var doors = DoorRuns(lm, building);
            var notches = new List<Box>(doors.Count);
            var directions = new List<Vector2Int>(doors.Count);
            foreach (var door in doors)
            {
                notches.Add(DoorNotch(building, door, out var normal));
                directions.Add(normal);
            }
            BuildSlateFloor(lm, building);
            BuildRoof(lm, building, notches);
            for (int i = 0; i < doors.Count; i++) BuildDoor(lm, doors[i], directions[i]);
            BuildInteriorFurnishings(lm, building);
            BuildWindows(lm, building);
            if (lm.ArchTiles.Length > 0) BuildGate(lm);
            foreach (var g in groups)                                                   // a plot lies outside the building's box; the pillar blocks inside it stay as RegionWalls draws them
                if (g != building && !(g.MinX >= building.MinX && g.MaxX <= building.MaxX && g.MinY >= building.MinY && g.MaxY <= building.MaxY)) BuildGravestones(lm, g);
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

        static List<Group> FindGroups(Landmark lm)
        {
            var groups = new List<Group>(); var seen = new bool[lm.FootW * lm.FootH]; var stack = new Stack<Vector2Int>();
            for (int y = lm.FootY; y < lm.FootY + lm.FootH; y++) for (int x = lm.FootX; x < lm.FootX + lm.FootW; x++)
            {
                int i = (y - lm.FootY) * lm.FootW + x - lm.FootX;
                if (seen[i] || !lm.IsWall(x, y)) continue;
                var g = new Group(); seen[i] = true; stack.Push(new Vector2Int(x, y));
                while (stack.Count > 0)
                {
                    var t = stack.Pop(); g.Tiles.Add(t);
                    g.MinX = Mathf.Min(g.MinX, t.x); g.MaxX = Mathf.Max(g.MaxX, t.x); g.MinY = Mathf.Min(g.MinY, t.y); g.MaxY = Mathf.Max(g.MaxY, t.y);
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = t.x + dx, ny = t.y + dy;
                        if (nx < lm.FootX || ny < lm.FootY || nx >= lm.FootX + lm.FootW || ny >= lm.FootY + lm.FootH || !lm.IsWall(nx, ny)) continue;
                        int ni = (ny - lm.FootY) * lm.FootW + nx - lm.FootX;
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

        static void BuildSlateFloor(Landmark lm, Group b)
        {
            const int P = 12;
            float x0 = b.MinX, y0 = -(b.MaxY + 1) * Sq, width = b.MaxX - b.MinX + 1, height = (b.MaxY - b.MinY + 1) * Sq;
            float centre = (b.MinX + b.MaxX + 1) * .5f;
            var cv = new Canvas(x0, y0, width, height, P);
            cv.Paint((wx, wy) =>
            {
                int tx = Mathf.FloorToInt(wx), ty = Mathf.FloorToInt(-wy / Sq);
                if (!lm.IsInterior(tx, ty)) return default;
                float u = wx - tx, v = -wy / Sq - ty;
                float grain = RegionNoise.Hash01(tx, ty, 57);
                Color color = Color.Lerp(new Color(.19f, .19f, .25f), new Color(.34f, .34f, .40f), .2f + grain * .35f);
                float aisle = Mathf.Abs(wx - centre);
                if (aisle < 2.15f) color = Color.Lerp(color, new Color(.38f, .34f, .35f), .3f + .16f * (1f - aisle / 2.15f));
                float grout = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                if (grout < .035f) color = Color.Lerp(new Color(.08f, .08f, .12f), color, .62f);
                float tileMark = (ty % 3 + 3) % 3;
                if (aisle < .24f && tileMark == 1f && Mathf.Abs(u - .5f) < .055f) color = Color.Lerp(color, Rune, .42f);
                if (aisle < .8f && tileMark == 1f && Mathf.Abs(v - .5f) < .035f && u > .32f && u < .68f) color = Color.Lerp(color, Rune, .28f);
                float crack = Mathf.Abs((u - .18f) - (v - .34f) * .24f);
                if (grain > .88f && v > .2f && v < .7f && crack < .014f) color = Shade(color, .69f);
                return color;
            });
            cv.ToSprite(lm, "Church slate and runes", "Church slate floor", -88);
        }

        // ---- light pews and a carved pulpit. These drawings have no collider or map footprint. --------------------------------

        static void BuildInteriorFurnishings(Landmark lm, Group b)
        {
            // The nave runs from the rear threshold to the front threshold. In this rectangular footprint that is its shorter
            // dimension; the broader dimension holds two banks of pews with an open centre aisle.
            bool naveAlongX = b.MaxX - b.MinX < b.MaxY - b.MinY;
            float alongMin = naveAlongX ? b.MinX : b.MinY, alongMax = naveAlongX ? b.MaxX : b.MaxY;
            float across = naveAlongX ? (b.MinY + b.MaxY + 1) * .5f : (b.MinX + b.MaxX + 1) * .5f;
            float[] stations = { alongMin + 1.3f, alongMin + 6.7f, alongMax - 2.2f };
            float[] banks = { -7.5f, -4.5f, 4.5f, 7.5f };
            for (int r = 0; r < stations.Length; r++) for (int c = 0; c < banks.Length; c++)
            {
                float x = naveAlongX ? stations[r] : across + banks[c];
                float y = naveAlongX ? across + banks[c] : stations[r];
                if (!PewFits(lm, x, y, naveAlongX)) continue;
                bool positiveSide = banks[c] > 0f;
                BuildPew(lm, x, y, naveAlongX ? (positiveSide ? 0f : 180f) : (positiveSide ? 90f : -90f));
            }
            float frontX = lm.FrontVector.x, frontY = lm.FrontVector.y;
            float back = frontX != 0f ? (frontX > 0f ? b.MinX : b.MaxX) + (frontX > 0f ? 6f : -6f) :
                         frontY > 0f ? b.MinY + 6f : b.MaxY - 6f;
            float side = 6.8f;
            float pulpitX = naveAlongX ? back : across + side * (frontY >= 0f ? 1f : -1f);
            float pulpitY = naveAlongX ? across + side * (frontX >= 0f ? 1f : -1f) : back;
            if (!lm.IsInterior(Mathf.FloorToInt(pulpitX), Mathf.FloorToInt(pulpitY)))
            {
                float candidateX = (b.MinX + b.MaxX + 1) * .5f, candidateY = (b.MinY + b.MaxY + 1) * .5f;
                for (int r = 0; r < stations.Length && !lm.IsInterior(Mathf.FloorToInt(pulpitX), Mathf.FloorToInt(pulpitY)); r++)
                {
                    pulpitX = naveAlongX ? stations[r] : candidateX + side * (frontY >= 0f ? 1f : -1f);
                    pulpitY = naveAlongX ? candidateY + side * (frontX >= 0f ? 1f : -1f) : stations[r];
                }
            }
            if (lm.IsInterior(Mathf.FloorToInt(pulpitX), Mathf.FloorToInt(pulpitY))) BuildPulpit(lm, pulpitX, pulpitY, naveAlongX ? (frontX >= 0f ? 0f : 180f) : (frontY >= 0f ? 90f : -90f));
        }

        static bool PewFits(Landmark lm, float cx, float cy, bool naveAlongX)
        {
            if (naveAlongX)
            {
                int x0 = Mathf.FloorToInt(cx - .82f), x1 = Mathf.FloorToInt(cx + .82f), y = Mathf.FloorToInt(cy);
                for (int x = x0; x <= x1; x++) if (!lm.IsInterior(x, y)) return false;
            }
            else
            {
                int y0 = Mathf.FloorToInt(cy - .82f), y1 = Mathf.FloorToInt(cy + .82f), x = Mathf.FloorToInt(cx);
                for (int y = y0; y <= y1; y++) if (!lm.IsInterior(x, y)) return false;
            }
            return true;
        }

        static void BuildPew(Landmark lm, float cx, float row, float rotation)
        {
            var sprite = Resources.Load<Sprite>("StormInk/Church/OakPew");
            if (sprite != null)
            {
                AddAssetSprite(lm, sprite, "Church oak pew", "Church pew", cx, -row * Sq, 1.82f, .68f * Sq, 2000 + 16 * (Mathf.FloorToInt(row) + 1) + 2, rotation);
                return;
            }
            const int P = 20;
            float width = 1.82f, depth = .68f, baseY = -(row + .5f) * Sq;
            var cv = new Canvas(cx - width / 2f - .16f, baseY - .14f, width + .32f, depth + .32f, P);
            cv.Paint((wx, wy) =>
            {
                float x = wx - cx, y = wy - baseY;
                float shadow = x * x / (width * width * .48f) + y * y / (.18f * .18f);
                if (y < .04f && shadow < 1f) return new Color(.015f, .012f, .022f, .26f * (1f - shadow));
                if (Mathf.Abs(x) > width * .5f || y < 0f || y > depth) return default;
                float left = Mathf.Abs(x), edge = Mathf.Min(width * .5f - left, y, depth - y);
                Color wood = Color.Lerp(Wood, WoodLight, .18f + .2f * RegionNoise.Hash01(Mathf.FloorToInt(x * 17f), Mathf.FloorToInt(y * 31f), 71));
                if (y < .15f)
                {
                    if (edge < .035f) return WoodDark;
                    bool carving = left > .68f && left < .78f && (y > .04f && y < .12f);
                    return carving ? Rune : wood;
                }
                if (y < .5f)
                {
                    if (edge < .04f || y < .2f) return WoodDark;
                    float seam = Mathf.Abs(x / .38f - Mathf.Round(x / .38f));
                    return seam < .045f ? Shade(wood, .68f) : wood;
                }
                if (y < .61f && left < width * .47f)
                {
                    if (edge < .035f) return Ink;
                    return Shade(WoodLight, y > .57f ? .72f : 1.08f);
                }
                return default;
            });
            cv.ToSprite(lm, "Church oak pew", "Church pew", 2000 + 16 * (Mathf.FloorToInt(row) + 1) + 2);
        }

        static void BuildPulpit(Landmark lm, float cx, float row, float rotation)
        {
            var sprite = Resources.Load<Sprite>("StormInk/Church/GothicPulpit");
            if (sprite != null)
            {
                AddAssetSprite(lm, sprite, "Church carved pulpit", "Church pulpit", cx, -row * Sq, 1.38f, 1.38f * Sq, 2000 + 16 * (Mathf.FloorToInt(row) + 1) + 3, rotation);
                return;
            }
            const int P = 24;
            float baseY = -(row + 1f) * Sq, width = 1.35f, height = 1.15f;
            var cv = new Canvas(cx - 1.05f, baseY - .28f, 2.1f, 1.92f, P);
            cv.Paint((wx, wy) =>
            {
                float x = wx - cx, y = wy - baseY;
                float shadow = x * x / .72f + (y + .04f) * (y + .04f) / .11f;
                if (y < .05f && shadow < 1f) return new Color(.01f, .008f, .014f, .32f * (1f - shadow));
                if (Mathf.Abs(x) < .72f && y >= .02f && y < .23f) return y < .08f || Mathf.Abs(x) > .66f ? StoneDark : Shade(Stone, .75f);
                float topY = .36f - Mathf.Abs(x) * .35f;
                if (Mathf.Abs(x) < .64f && y > .2f && y < topY)
                {
                    if (Mathf.Abs(x) > .52f || y < .24f) return Ink;
                    if (Mathf.Abs(x) < .07f && y > .29f && y < .35f || Mathf.Abs(x) < .20f && y > .31f && y < .34f) return Rune;
                    return Color.Lerp(WoodDark, Wood, Mathf.Clamp01(.5f + x * .28f));
                }
                if (y >= topY && y < .72f && Mathf.Abs(x) < width * .5f - Mathf.Max(0f, y - .48f) * .8f)
                {
                    if (Mathf.Abs(x) > width * .5f - .07f || Mathf.Abs(y - .61f) < .04f) return WoodDark;
                    return Color.Lerp(Wood, WoodLight, .4f + .2f * Mathf.Sin(x * 17f));
                }
                if (y > .71f && y < height && Mathf.Abs(x) < width * .5f - (y - .71f) * .5f)
                {
                    if (Mathf.Abs(x) < .05f || Mathf.Abs(x) < .24f && y > .9f) return Rune;
                    return WoodDark;
                }
                return default;
            });
            cv.ToSprite(lm, "Church carved pulpit", "Church pulpit", 2000 + 16 * (Mathf.FloorToInt(row) + 1) + 3);
        }

        static void AddAssetSprite(Landmark lm, Sprite sprite, string textureName, string objectName, float x, float y, float width, float height, int order, float rotation)
        {
            var go = new GameObject(objectName); go.transform.SetParent(lm.Root.transform, false);
            go.transform.localPosition = new Vector3(x, y, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, rotation);
            var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.sharedMaterial = lm.Material; renderer.sortingOrder = order;
            Vector2 dimensions = sprite.bounds.size;
            renderer.transform.localScale = new Vector3(width / Mathf.Max(dimensions.x, .01f), height / Mathf.Max(dimensions.y, .01f), 1f);
        }

        // ---- narrow garnet lancets and candlelight painted into the inner church walls ------------------------------------------

        static void BuildWindows(Landmark lm, Group b)
        {
            bool sideWallsRunAlongY = b.MaxY - b.MinY > b.MaxX - b.MinX;
            float[] along = sideWallsRunAlongY ? new[] { b.MinY + 3.2f, b.MaxY - 3.2f } : new[] { b.MinX + 3.2f, b.MaxX - 3.2f };
            float[] sides = sideWallsRunAlongY ? new[] { b.MinX + 1.25f, b.MaxX - 1.25f } : new[] { b.MinY + 1.25f, b.MaxY - 1.25f };
            foreach (float a in along) foreach (float s in sides)
            {
                float x = sideWallsRunAlongY ? s : a, y = sideWallsRunAlongY ? a : s;
                if (lm.IsInterior(Mathf.FloorToInt(x), Mathf.FloorToInt(y)))
                {
                    BuildWindow(lm, x, y, sideWallsRunAlongY ? 90f : 0f);
                    BuildCandle(lm, x + (sideWallsRunAlongY ? .45f : 0f), y + (sideWallsRunAlongY ? 0f : .45f));
                }
            }
        }

        static void BuildWindow(Landmark lm, float cx, float cy, float rotation)
        {
            const int P = 20;
            float baseY = -(cy + .7f) * Sq;
            var cv = new Canvas(cx - .52f, baseY, 1.04f, 1.55f * Sq, P);
            cv.Paint((wx, wy) =>
            {
                float x = (wx - cx) / .46f, y = (-wy / Sq) - (cy + .7f);
                float top = .78f - Mathf.Abs(x) * .65f;
                if (Mathf.Abs(x) >= 1f || y < .04f || y > top) return default;
                bool rim = Mathf.Abs(x) > .83f || y < .11f || y > top - .06f;
                if (rim) return Ink;
                float tint = .82f + .26f * RegionNoise.Hash01(Mathf.FloorToInt(wx * 19f), Mathf.FloorToInt(wy * 23f), 97);
                Color glass = Color.Lerp(WineGlass, WineLight, .2f + .4f * (1f - Mathf.Abs(x)));
                glass = Shade(glass, tint);
                if (Mathf.Abs(x) < .045f || Mathf.Abs(y - .35f) < .035f) return Rune;
                if (Mathf.Abs(x) > .65f && y > .16f && y < .46f) glass = Shade(glass, .6f);
                glass.a = .92f; return glass;
            });
            var renderer = cv.ToSprite(lm, "Church garnet window", "Church stained glass", 2000 + 16 * Mathf.FloorToInt(cy) + 4);
            if (rotation != 0f)
            {
                Vector2 size = renderer.sprite.bounds.size;
                Vector3 position = renderer.transform.localPosition;
                var pivot = new GameObject("Church lancet alignment"); pivot.transform.SetParent(lm.Root.transform, false);
                pivot.transform.localPosition = position + new Vector3(size.x * .5f, size.y * .5f, 0f);
                renderer.transform.SetParent(pivot.transform, false);
                renderer.transform.localPosition = new Vector3(-size.x * .5f, -size.y * .5f, 0f);
                pivot.transform.localRotation = Quaternion.Euler(0f, 0f, rotation);
            }
        }

        static void BuildCandle(Landmark lm, float x, float y)
        {
            const int P = 16;
            float baseY = -(y + .5f) * Sq;
            var cv = new Canvas(x - .48f, baseY - .35f, .96f, .82f, P);
            cv.Paint((wx, wy) =>
            {
                float dx = wx - x, dy = wy - baseY;
                float glow = dx * dx / .18f + dy * dy / .22f;
                if (glow < 1f) return new Color(1f, .51f, .19f, .25f * (1f - glow));
                if (dy > .08f && dy < .36f && Mathf.Abs(dx) < .07f) return dx > .05f || dy < .11f || dy > .33f ? Ink : WoodLight;
                if (dy >= .36f && dy < .53f && Mathf.Abs(dx) < .045f) return Ink;
                if (dy > .48f && dy < .67f && Mathf.Abs(dx) < .075f - (dy - .48f) * .35f) return Color.Lerp(Warm, new Color(1f, .95f, .72f), Mathf.Clamp01((dy - .48f) * 5f));
                return default;
            });
            cv.ToSprite(lm, "Church candle", "Church candle flame", 2000 + 16 * (Mathf.FloorToInt(y) + 1) + 5);
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
