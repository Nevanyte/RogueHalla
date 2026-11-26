using System;
using System.Collections.Generic;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Roguehalla.Client
{
    /// <summary>One public map projection shared by planning, the tactical map and minimap. No visibility inference.</summary>
    public sealed class TacticalMapSurface : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        public Action<Vector2, PointerEventData.InputButton> Pressed;
        public Action<Vector2, PointerEventData.InputButton> Dragged;
        public Action<Vector2, PointerEventData.InputButton> PointerReleased;
        public Action Aborted;
        RawImage _terrain, _fog;
        TacticalMapMarks _marks;
        TerrainRaster.Entry _raster;
        MapBlob _map;
        public MapBlob Map => _map;
        public RectTransform Rect => (RectTransform)transform;
        public TacticalMapMarks Marks => _marks;
        /// <summary>True when the map carried a region plane and the terrain is tinted by zone.</summary>
        /// <summary>P-UI-TABMAP-SHEET-1: true when the layout owns this surface's rect, so <see cref="Rebuild"/> adds no aspect fitter (the Tab map sets it; the minimap does not).</summary>
        public bool LayoutOwnsRect;
        public bool HasZoneTint => _raster != null && _raster.Tinted;

        /// <summary>How much larger than its base size the marks layer is drawn (1 on the minimap).</summary>
        public float MarkScale { get; private set; } = 1f;
        /// <summary>Draws the marks layer k times larger while it still covers the surface exactly (a value under 1 or not finite is 1).</summary>
        public void SetMarkScale(float k)
        {
            if (float.IsNaN(k) || float.IsInfinity(k) || k < 1f) k = 1f;
            if (k == MarkScale || _marks == null) return;
            MarkScale = k;
            var r = _marks.rectTransform;
            float lo = .5f - .5f / k, hi = .5f + .5f / k;
            r.anchorMin = new Vector2(lo, lo); r.anchorMax = new Vector2(hi, hi);
            r.offsetMin = r.offsetMax = Vector2.zero; r.pivot = new Vector2(.5f, .5f);
            r.localScale = new Vector3(k, k, 1f);
            _marks.LayerChanged();
        }

        public void Initialize()
        {
            _terrain = gameObject.AddComponent<RawImage>();
            _terrain.color = Color.white;
            _terrain.raycastTarget = true;
            // Fog of war: the same texture the world layer uses (VisionFog), over the terrain and under every contact. It dims the part of
            // the map the server is not showing the player and reveals nothing; off outside a running match.
            var fog = new GameObject("Vision fog", typeof(RectTransform));
            fog.transform.SetParent(transform, false);
            var fr = (RectTransform)fog.transform;
            fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one; fr.offsetMin = fr.offsetMax = Vector2.zero;
            _fog = fog.AddComponent<RawImage>();
            _fog.raycastTarget = false; _fog.uvRect = new Rect(0, 1, 1, -1); // the fog texture's row 0 is the map's top row
            _fog.color = new Color(1, 1, 1, FogUiOpacity); _fog.enabled = false;
            var fogMaterial = VisionFog.UiMaterial; if (fogMaterial != null) _fog.material = fogMaterial;
            var marks = new GameObject("Delivered contacts and personal route", typeof(RectTransform));
            marks.transform.SetParent(transform, false);
            var r = (RectTransform)marks.transform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            _marks = marks.AddComponent<TacticalMapMarks>();
            _marks.raycastTarget = false;
        }

        void OnEnable() { UiTheme.Changed += Rebuild; }

        public void SetMap(MapBlob map)
        {
            if (ReferenceEquals(map, _map)) return;
            _map = map;
            Rebuild();
            _marks.SetMap(map);
        }

        void Rebuild()
        {
            if (_terrain == null) return;
            if (_raster != null) { TerrainRaster.Release(_raster); _raster = null; }
            _terrain.texture = null;
            if (_map == null) return;
            _raster = TerrainRaster.Acquire(_map, UiTheme.Current);
            _terrain.texture = _raster.Texture;
            if (LayoutOwnsRect) return;
            var fit = GetComponent<AspectRatioFitter>() ?? gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent; fit.aspectRatio = (float)_map.W / _map.H;
        }

        const float FogUiOpacity = 1f;

        public void Present(Session session, IReadOnlyList<RoutePoint> route)
        {
            var vision = VisionFog.Current;
            bool fogged = vision != null && vision.Active && _map != null && vision.Width == _map.W && vision.Height == _map.H;
            if (fogged && _fog.texture != vision.Texture) _fog.texture = vision.Texture;
            if (_fog.enabled != fogged) _fog.enabled = fogged;
            _marks.Present(session, route);
        }

        public bool PointerWorld(PointerEventData e, out Vector2 point)
        {
            return ScreenWorld(e.position, e.pressEventCamera, out point);
        }

        public bool ScreenWorld(Vector2 screen, Camera eventCamera, out Vector2 point)
        {
            point = default;
            if (_map == null || Rect.rect.width <= 0 || Rect.rect.height <= 0 || !RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, screen, eventCamera, out var p)) return false;
            var rect = Rect.rect;
            // Local y points up; the map's y points down the screen. The projection clamps to the map.
            if (!MapProjection.PixelToWorld(p.x - rect.xMin, rect.yMax - p.y, rect.width, rect.height, _map.W, _map.H, out var world)) return false;
            point = new Vector2((float)world.X, (float)world.Y);
            return true;
        }

        public void OnPointerDown(PointerEventData e) { if (PointerWorld(e, out var point)) Pressed?.Invoke(point, e.button); }
        public void OnDrag(PointerEventData e) { if (PointerWorld(e, out var point)) Dragged?.Invoke(point, e.button); }
        public void OnPointerUp(PointerEventData e) { if (PointerWorld(e, out var point)) PointerReleased?.Invoke(point, e.button); }
        void OnDisable() { UiTheme.Changed -= Rebuild; Aborted?.Invoke(); }
        void OnApplicationFocus(bool focus) { if (!focus) Aborted?.Invoke(); }
        void OnDestroy() { if (_raster != null) { TerrainRaster.Release(_raster); _raster = null; } }
    }

    /// <summary>
    /// Builds the smooth map terrain texture once per map and theme and shares it between the minimap and the Tab map. Walls and low
    /// obstacles are drawn as anti-aliased filled shapes with a lighter stroke from a bilinear coverage field (see
    /// <see cref="MapSmoothing"/>), at several texels per tile, so the map is never a one-texel-per-tile pixel grid. When the map carries
    /// a region plane each zone is tinted with its theme hue, softly blended across the borders; without one the look stays neutral.
    /// A picture whose last holder lets go is parked (one spare) when it is of the current theme, so a rebind finds it again; a map
    /// that differs only in some tiles is derived from a picture of the same shape by shading the texels those tiles reach.
    /// </summary>
    public static class TerrainRaster
    {
        public sealed class Entry
        {
            internal MapBlob Map; internal UiTheme Theme; internal int Refs; internal float[][] Weights;
            public Texture2D Texture; public bool Tinted; public int TexelsPerTile;
        }

        static readonly List<Entry> Cache = new List<Entry>();
        static Entry _spare;
        const int TargetTexels = 1024;

        /// <summary>Whole picture builds so far (a counter for the tests).</summary>
        public static int FullBuilds { get; private set; }
        /// <summary>Pictures derived from a kin so far (a counter for the tests).</summary>
        public static int DerivedBuilds { get; private set; }
        /// <summary>Texels computed by the last derive.</summary>
        public static int LastDerivedTexels { get; private set; }
        /// <summary>True when a picture nobody holds is parked with a live texture.</summary>
        public static bool HasSpare { get { return _spare != null && _spare.Texture != null; } }
        /// <summary>Destroys the parked picture, if any.</summary>
        public static void DropSpare()
        {
            var e = _spare; _spare = null;
            if (e != null) DestroyTexture(e);
        }

        static void DestroyTexture(Entry e)
        {
            var t = e.Texture; e.Texture = null;
            if (t == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(t); else UnityEngine.Object.DestroyImmediate(t);
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static bool Kin(Entry e, MapBlob map, UiTheme theme)
        {
            return e != null && e.Texture != null && ReferenceEquals(e.Theme, theme) && e.Map.W == map.W && e.Map.H == map.H && SameBytes(e.Map.Regions, map.Regions);
        }

        static bool Same(Entry e, MapBlob map, UiTheme theme)
        {
            return Kin(e, map, theme) && (ReferenceEquals(e.Map, map) || SameBytes(e.Map.Tiles, map.Tiles));
        }

        public static Entry Acquire(MapBlob map, UiTheme theme)
        {
            if (_spare != null && _spare.Texture == null) _spare = null;
            foreach (var e in Cache)
                if (Same(e, map, theme)) { e.Refs++; e.Map = map; return e; }
            if (Same(_spare, map, theme))
            {
                var e = _spare; _spare = null;
                e.Refs = 1; e.Map = map; Cache.Add(e);
                return e;
            }
            Entry kin = null;
            foreach (var e in Cache) if (Kin(e, map, theme)) { kin = e; break; }
            if (kin == null && Kin(_spare, map, theme)) kin = _spare;
            Entry entry;
            if (kin != null)
            {
                entry = Derive(kin, map);
                if (ReferenceEquals(kin, _spare)) { _spare = null; DestroyTexture(kin); }
            }
            else
            {
                DropSpare();
                entry = new Entry { Map = map, Theme = theme, Refs = 1 };
                FullBuilds++;
                Build(entry);
            }
            Cache.Add(entry);
            return entry;
        }

        public static void Release(Entry entry)
        {
            if (entry == null || entry.Refs <= 0 || --entry.Refs > 0) return;
            Cache.Remove(entry);
            if (entry.Texture == null) return;
            bool park = ReferenceEquals(entry.Theme, UiTheme.Current);
            if (park) foreach (var e in Cache) if (Kin(e, entry.Map, entry.Theme)) { park = false; break; }
            if (!park) { DestroyTexture(entry); return; }
            var old = _spare; _spare = entry;
            if (old != null && !ReferenceEquals(old, entry)) DestroyTexture(old);
        }

        /// <summary>Texels per tile for a map: enough for crisp edges at the largest map size, never more than 8.</summary>
        public static int TexelsFor(int w, int h) { return Mathf.Clamp(TargetTexels / Mathf.Max(1, Mathf.Max(w, h)), 2, 8); }

        static Texture2D NewTexture(int tw, int th)
        {
            return new Texture2D(tw, th, TextureFormat.RGBA32, true)
            {
                name = "Public map terrain", filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp, anisoLevel = 2, hideFlags = HideFlags.HideAndDontSave
            };
        }

        /// <summary>The one texel function of the whole build and the derive.</summary>
        sealed class Shade
        {
            readonly int _w, _h, _k;
            readonly bool _hasRegions;
            readonly float[][] _weights;
            readonly Func<int, int, float> _wall, _low;
            readonly Color _backing, _wallFill, _wallStroke, _lowFill;
            readonly Color[] _zoneColor = new Color[MapZones.Count + 1];
            readonly float[] _zoneAlpha = new float[MapZones.Count + 1];
            readonly float _edge;

            public Shade(MapBlob map, UiTheme theme, float[][] weights, bool hasRegions)
            {
                _w = map.W; _h = map.H; _k = TexelsFor(_w, _h);
                _hasRegions = hasRegions; _weights = weights;
                var tiles = map.Tiles; int w = _w;
                _wall = (x, y) => tiles[y * w + x] == MapBlob.Wall ? 1f : 0f;
                _low = (x, y) => tiles[y * w + x] == MapBlob.Low ? 1f : 0f;
                _backing = theme.SurfaceSunken;
                _wallFill = Color.Lerp(_backing, theme.Border, .5f); _wallStroke = theme.TextSecondary;
                _lowFill = Color.Lerp(_backing, theme.AccentDim, .5f);
                for (int r = 1; r <= MapZones.Count; r++) { _zoneColor[r] = theme.ZoneColor(r); _zoneAlpha[r] = MapZones.TintAlpha(r, true); }
                _edge = .85f / _k;
            }

            /// <summary>The colour of texel (col, row), row 0 at the map's top.</summary>
            public Color32 Texel(int col, int row)
            {
                double ty = (row + .5) / _k, tx = (col + .5) / _k;
                Color c = _backing;
                if (_hasRegions)
                {
                    // The same blurred zone weights the world ground shader bakes (ZoneWeights): borders are a soft gradient about 1.5 tiles
                    // wide at any angle, not a one-tile staircase.
                    float aSum = 0f, rr = 0f, gg = 0f, bb = 0f;
                    for (int region = 1; region <= MapZones.Count; region++)
                    {
                        float wgt = ZoneWeights.Sample(_weights[region], _w, _h, tx, ty);
                        if (wgt <= 0.002f) continue;
                        float a = wgt * _zoneAlpha[region];
                        aSum += a; rr += _zoneColor[region].r * a; gg += _zoneColor[region].g * a; bb += _zoneColor[region].b * a;
                    }
                    if (aSum > 0f) c = Color.Lerp(c, new Color(rr / aSum, gg / aSum, bb / aSum, 1f), aSum);
                }
                float cl = MapSmoothing.Coverage(_low, _w, _h, tx, ty);
                if (cl > .05f) c = Color.Lerp(c, _lowFill, MapSmoothing.Inside(cl, _edge) * .9f);
                float cw = MapSmoothing.Coverage(_wall, _w, _h, tx, ty);
                if (cw > .05f)
                {
                    c = Color.Lerp(c, _wallFill, MapSmoothing.Inside(cw, _edge));
                    c = Color.Lerp(c, _wallStroke, MapSmoothing.Stroke(cw, .17f, _edge) * .85f);
                }
                return new Color32((byte)Mathf.RoundToInt(c.r * 255f), (byte)Mathf.RoundToInt(c.g * 255f), (byte)Mathf.RoundToInt(c.b * 255f), 255);
            }
        }

        static void Build(Entry entry)
        {
            var map = entry.Map;
            int w = map.W, h = map.H, k = TexelsFor(w, h);
            entry.TexelsPerTile = k;
            bool hasRegions = map.Regions != null && map.Regions.Length == w * h;
            entry.Tinted = hasRegions;
            entry.Weights = hasRegions ? ZoneWeights.Bake(map.Regions, w, h) : null;
            var shade = new Shade(map, entry.Theme, entry.Weights, hasRegions);
            int tw = w * k, th = h * k;
            var texture = NewTexture(tw, th);
            var data = texture.GetPixelData<Color32>(0);
            for (int row = 0; row < th; row++)
            {
                int outRow = th - 1 - row;
                for (int col = 0; col < tw; col++) data[outRow * tw + col] = shade.Texel(col, row);
            }
            texture.Apply(true, false);
            entry.Texture = texture;
        }

        /// <summary>A picture of <paramref name="map"/> made from a kin: its mip 0 is copied and only the texels within one tile of a changed tile are shaded again.</summary>
        static Entry Derive(Entry kin, MapBlob map)
        {
            int w = map.W, h = map.H, k = kin.TexelsPerTile, tw = w * k, th = h * k;
            var entry = new Entry { Map = map, Theme = kin.Theme, Refs = 1, Weights = kin.Weights, Tinted = kin.Tinted, TexelsPerTile = k };
            var dirty = new bool[w * h];
            var a = kin.Map.Tiles; var b = map.Tiles;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (a[y * w + x] == b[y * w + x]) continue;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && ny >= 0 && nx < w && ny < h) dirty[ny * w + nx] = true;
                        }
                }
            var shade = new Shade(map, kin.Theme, kin.Weights, kin.Tinted);
            var texture = NewTexture(tw, th);
            var data = texture.GetPixelData<Color32>(0);
            data.CopyFrom(kin.Texture.GetPixelData<Color32>(0));
            int tiles = 0;
            for (int ty = 0; ty < h; ty++)
                for (int tx = 0; tx < w; tx++)
                {
                    if (!dirty[ty * w + tx]) continue;
                    tiles++;
                    for (int row = ty * k; row < ty * k + k; row++)
                    {
                        int outRow = th - 1 - row;
                        for (int col = tx * k; col < tx * k + k; col++) data[outRow * tw + col] = shade.Texel(col, row);
                    }
                }
            texture.Apply(true, false);
            entry.Texture = texture;
            DerivedBuilds++;
            LastDerivedTexels = tiles * k * k;
            return entry;
        }
    }
}
