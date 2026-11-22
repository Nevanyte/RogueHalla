// P-COLISEUM-GATES-1: the coliseum gates drawn closed while the arena reports GatesLocked (legacy MapView.drawGates /
// setGatesLocked, MapView.ts:572-618). Each gate tile is a wall block in the coliseum wall style, built once per map in the
// RegionWalls conventions (one mesh per tile row, sorting order 2000 + row * 16, front face where the tile below is not WALL
// on the closed map) and shown or hidden by the lock. Presentation only: collision and prediction never read this.
using System.Collections.Generic;
using UnityEngine;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;

namespace Roguehalla.Client
{
    /// <summary>Closed-gate wall rows over the coliseum gate tiles, visible only while the gates are locked.</summary>
    public sealed class ColiseumGateWalls : System.IDisposable
    {
        const float TopHeight = .78f;   // RegionWalls cap footprint
        public int TileCount { get; private set; }
        readonly List<Object> _owned = new List<Object>();
        GameObject _root;
        bool _shown;

        readonly List<Vector3> _v = new List<Vector3>(256);
        readonly List<Vector2> _uv = new List<Vector2>(256);
        readonly List<Color32> _c = new List<Color32>(256);
        readonly List<Vector2> _k = new List<Vector2>(256);   // uv2.x = surface kind (RegionWalls: 0 top, 1 south face, 4 unlit decal)
        readonly List<int> _t = new List<int>(512);
        float _kind;

        /// <summary>Builds the closed-gate rows of <paramref name="map"/> (none without coliseum gates) with the wall
        /// <paramref name="material"/> (owned by <see cref="RegionWalls"/>), hidden until <see cref="SetLocked"/>.</summary>
        public static ColiseumGateWalls Build(Transform parent, MapBlob map, Material material, HashSet<int> painted = null)
        {
            var g = new ColiseumGateWalls(); g.Create(parent, map, material, painted); return g;
        }

        /// <summary>Shows the closed gates while <paramref name="locked"/>; hides them when open.</summary>
        public void SetLocked(bool locked)
        {
            if (locked == _shown || _root == null) return;
            _shown = locked;
            _root.SetActive(locked);
        }

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uva, Vector2 uvb, Vector2 uvc, Vector2 uvd, Color32 col)
        {
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d); _uv.Add(uva); _uv.Add(uvb); _uv.Add(uvc); _uv.Add(uvd);
            _c.Add(col); _c.Add(col); _c.Add(col); _c.Add(col);
            var kd = new Vector2(_kind, 0); _k.Add(kd); _k.Add(kd); _k.Add(kd); _k.Add(kd);
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2); _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
        }

        void Rect(float x0, float y0, float x1, float y1, RegionWallAtlas.Cell cell, Color32 col)
        {
            Quad(new Vector3(x0, y0, 0), new Vector3(x1, y0, 0), new Vector3(x1, y1, 0), new Vector3(x0, y1, 0),
                new Vector2(cell.Min.x, cell.Min.y), new Vector2(cell.Max.x, cell.Min.y), new Vector2(cell.Max.x, cell.Max.y), new Vector2(cell.Min.x, cell.Max.y), col);
        }

        void Create(Transform parent, MapBlob map, Material material, HashSet<int> painted)
        {
            var gateTiles = new List<MapTile>();
            ColiseumGates.ClosedTiles(map, true, gateTiles);
            if (painted != null && map != null) gateTiles.RemoveAll(tile => painted.Contains(tile.Y * map.W + tile.X));
            if (gateTiles.Count == 0 || material == null) return;
            MapBlob closed = ColiseumGates.ClosedMap(map);
            if (closed == null) return;
            _root = new GameObject("Coliseum gates (closed)"); _root.transform.SetParent(parent, false);
            _owned.Add(_root);
            int w = closed.W, h = closed.H; var tiles = closed.Tiles;
            bool Solid(int x, int y) { return x < 0 || y < 0 || x >= w || y >= h || tiles[y * w + x] == MapBlob.Wall; }

            var set = RegionWallAtlas.SetFor(RegionThemes.Get(RegionId.Coliseum).Wall);
            var contact = RegionWallAtlas.Contact;
            var white = new Color32(255, 255, 255, 255);
            const float rimW = .085f;
            // One mesh per row that holds a gate tile, so each row sorts against actors as that row's walls do.
            var rows = new SortedDictionary<int, List<MapTile>>();
            foreach (var t in gateTiles)
            {
                if (t.X >= w || t.Y >= h) continue;
                if (!rows.TryGetValue(t.Y, out var row)) { row = new List<MapTile>(); rows.Add(t.Y, row); }
                row.Add(t);
            }
            foreach (var entry in rows)
            {
                int y = entry.Key;
                _v.Clear(); _uv.Clear(); _c.Clear(); _t.Clear(); _k.Clear(); _kind = 0f;
                foreach (var t in entry.Value)
                {
                    int x = t.X;
                    // sRGB tint, uploaded linear (SrgbMeshColors): about .79-.84 linear, a shade under the ring wall's raw .92-1 (a shut gate, not masonry).
                    float tint = .90f + RegionNoise.Hash01(x, y, 5) * .03f;
                    var col = new Color32((byte)(255 * tint), (byte)(255 * tint), (byte)(255 * tint), 255);
                    float py = -(y + 1) * ArenaView.GroundSquash, px0 = x, px1 = x + 1;
                    int v = (int)(RegionNoise.Hash(x, y, 9) % 97u);
                    TileCount++;
                    if (!Solid(x, y + 1))
                    {
                        _kind = 4f; Rect(px0 - .04f, py - .30f, px1 + .04f, py + .04f, contact, white);
                        _kind = 1f; Rect(px0 - .003f, py, px1 + .003f, py + RegionWalls.WallHeight + .02f, set.Face[v % RegionWallAtlas.FaceVariants], col); _kind = 0f;
                    }
                    float ty0 = py + RegionWalls.WallHeight, ty1 = ty0 + TopHeight;
                    Rect(px0 - .003f, ty0, px1 + .003f, ty1, set.Top[(v / 3) % RegionWallAtlas.TopVariants], col);
                    var rim = set.Rim;
                    if (!Solid(x, y - 1))
                        Quad(new Vector3(px0, ty1 - rimW, 0), new Vector3(px1, ty1 - rimW, 0), new Vector3(px1, ty1, 0), new Vector3(px0, ty1, 0),
                            new Vector2(rim.Max.x, rim.Max.y), new Vector2(rim.Min.x, rim.Max.y), new Vector2(rim.Min.x, rim.Min.y), new Vector2(rim.Max.x, rim.Min.y), white);
                    if (!Solid(x - 1, y))
                        Quad(new Vector3(px0, ty0, 0), new Vector3(px0 + rimW, ty0, 0), new Vector3(px0 + rimW, ty1, 0), new Vector3(px0, ty1, 0),
                            new Vector2(rim.Min.x, rim.Min.y), new Vector2(rim.Min.x, rim.Max.y), new Vector2(rim.Max.x, rim.Max.y), new Vector2(rim.Max.x, rim.Min.y), white);
                    if (!Solid(x + 1, y))
                        Quad(new Vector3(px1 - rimW, ty0, 0), new Vector3(px1, ty0, 0), new Vector3(px1, ty1, 0), new Vector3(px1 - rimW, ty1, 0),
                            new Vector2(rim.Min.x, rim.Max.y), new Vector2(rim.Min.x, rim.Min.y), new Vector2(rim.Max.x, rim.Min.y), new Vector2(rim.Max.x, rim.Max.y), white);
                }
                if (_v.Count == 0) continue;
                var mesh = new Mesh { name = "Coliseum gate row " + y, indexFormat = UnityEngine.Rendering.IndexFormat.UInt16 };
                var srgb = _c.ToArray();
                mesh.SetVertices(_v); mesh.SetUVs(0, _uv); SrgbMeshColors.Upload(mesh, srgb, new Color[srgb.Length]); mesh.SetUVs(2, _k); mesh.SetTriangles(_t, 0); mesh.RecalculateBounds();
                _owned.Add(mesh);
                var go = new GameObject("Gate row " + y); go.transform.SetParent(_root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material; mr.sortingOrder = 2000 + y * 16;   // RegionWalls row order
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }
            _root.SetActive(false);
        }

        public void Dispose()
        {
            foreach (var o in _owned) if (o != null) RegionUtil.Kill(o);
            _owned.Clear();
            _root = null;
        }
    }
}
