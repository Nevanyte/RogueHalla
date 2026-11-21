using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using TMPro;
using Roguehalla.Net.Ui;

namespace Roguehalla.Client
{
    /// <summary>
    /// Building the arena from the map blob: ground chunks, region map, temple emblem and lanterns.
    /// </summary>
    public sealed partial class ArenaView
    {
        public void Build(MapBlob map, WireVision vision = null)
        {
            if (HasMap) return;
            Regions.Load(map);
            if (_atmos != null) _atmos.OnMapBuilt(map);
            _visionFog.Configure(map, vision);
            if (map.Regions != null && map.Regions.Length == map.W * map.H) { BuildRegionMap(map); return; }
            float w = map.W, h = map.H * GroundSquash;
            for (int y = 0; y < map.H; y += 4) for (int x = 0; x < map.W; x += 4)
            {
                float cw = Mathf.Min(4, map.W - x), ch = Mathf.Min(4, map.H - y);
                var ground = ShowcaseSprites.Create(_mapRoot, "Neutral carved flagstone", "bar", -100, Color.white,
                    new Vector2(cw, ch * GroundSquash), new Vector3(x + cw / 2, -(y + ch / 2) * GroundSquash));
                // Collision-only maps identify no faction or terrain. Neutral artwork is deliberate;
                // lava, ice, healing and zone treatments wait for public static-map facts.
                ground.sprite = ShowcaseBiomeArt.Floor(ShowcaseBiome.Neutral, (x/4 + y/4) % 3);
            }
            for (int y = 0; y < map.H; y++) for (int x = 0; x < map.W; x++)
            {
                byte tile = map.At(x, y); if (tile == MapBlob.Floor) continue;
                Vector3 p = WorldPoint((x + .5f) * 32, (y + .5f) * 32);
                bool wall = tile == MapBlob.Wall;
                int order = 2000 + y * 16;
                float height = wall ? .67f : .25f;
                var tint = wall ? new Color(.88f, .80f, .65f) : new Color(.48f, .56f, .60f);
                ShowcaseSprites.Create(_mapRoot, "Wall contact", "shadow", order - 5, Color.white, new Vector2(1.5f, .65f), p + new Vector3(.16f, -.26f), false);
                ShowcaseSprites.Create(_mapRoot, "Painted wall face", "stone", order, tint * .58f, new Vector2(1.02f, height + .32f), p + new Vector3(0, height / 2));
                var crown = ShowcaseSprites.Create(_mapRoot, "Beveled stone crown", "stone", order + 1, tint, new Vector2(1.02f, .7f), p + new Vector3(0, height));
                if (wall && (x+y)%4==0) { var caster = crown.gameObject.AddComponent<ShadowCaster2D>(); caster.selfShadows = false; caster.useRendererSilhouette = true; }
                bool edge = y + 1 < map.H && map.At(x, y + 1) == MapBlob.Floor || x > 0 && map.At(x - 1, y) == MapBlob.Floor || x + 1 < map.W && map.At(x + 1, y) == MapBlob.Floor;
                if (wall && edge && ((x + y * 3) % 7 == 0))
                    AddLantern(p + new Vector3(0, .32f, -1), order + 2, _lamps.Count);
                if (wall && edge && (x*7+y*3)%29==0 && _catalog!=null && _catalog.Statue!=null)
                {
                    var ornament=ShowcaseSprites.Create(_mapRoot,"Authored wall statue","bar",order+2,Color.white,Vector2.one,p);
                    ornament.sprite=_catalog.Statue; ornament.transform.localScale=Vector3.one*.85f;
                }
            }
            HasMap = true;
            ApplyQuality();
        }
        /// <summary>Region-aware map: one ground quad, row-batched walls and deterministic props. Used when the map carries the region plane.</summary>
        void BuildRegionMap(MapBlob map)
        {
            var temple = TempleLayout.For(map); var plague = PlagueGround.Build(map);
            Debug.Log("[regions] " + temple.Describe() + "; plague ground: " + plague.MudTiles + " mud, " + plague.GrassTiles + " grass, " + plague.WaterTiles + " shallow water tiles; terrain mode " + map.TerrainMode);
            Surfaces.ClearLayers(); Surfaces.AddLayer(temple); Surfaces.AddLayer(plague);          // decorative surfaces for SurfaceMap (never override real terrain)
            _regionGround = RegionGround.Create(_mapRoot, map, RegionExposure, temple, plague);
            if (temple.Found && !temple.IsSignature) AddTempleEmblem(temple);
            _regionWalls = RegionWalls.Build(_mapRoot, map, RegionExposure);
            _regionProps = RegionProps.Build(_mapRoot, map);
            _coliseumGates = ColiseumGatePresenter.Build(_mapRoot, map);
            _landmarks = LandmarkView.Build(_mapRoot, map);
            if (_atmos != null) _atmos.Lights.RegisterLandmarkCandles(_landmarks);
            HasMap = true;
            ApplyQuality();
        }
        /// <summary>The Holy faction's own icon inlaid at the centre of the temple medallion (flat on the floor, under actors).</summary>
        void AddTempleEmblem(TempleLayout temple)
        {
            var glyph = ShowcaseIdentityAssets.GetFactionGlyph((int)FactionId.Holy);
            if (glyph == null) return;
            var c = temple.Center;
            var r = ShowcaseSprites.Create(_mapRoot, "Temple emblem (Holy faction icon)", "bar", -90, new Color(1f, .93f, .62f, .95f), Vector2.one, new Vector3(c.x, -c.y * GroundSquash, 0), false);
            r.sprite = glyph;
            float side = Mathf.Max(glyph.bounds.size.x, glyph.bounds.size.y, .01f);
            r.transform.localScale = new Vector3(1.7f / side, 1.7f / side * GroundSquash, 1f);
        }
        static float Hash(int x, int y) { unchecked { uint h = (uint)(x * 73856093 ^ y * 19349663); return (h & 255) / 255f; } }

        void AddLantern(Vector3 p, int order, int index)
        {
            var color = new Color(1, .57f, .22f);
            ShowcaseSprites.Create(_mapRoot, "Iron brazier", "stone", order, new Color(.28f, .22f, .17f), new Vector2(.3f, .38f), p);
            ShowcaseSprites.Create(_mapRoot, "Flame", "spark", order + 1, new Color(2, 1.1f, .4f), new Vector2(.22f, .4f), p + new Vector3(0, .17f), false);
            var glow = ShowcaseSprites.Create(_mapRoot, "Ember haze", "glow", order + 2, new Color(1, .4f, .1f, .35f), new Vector2(1.8f, 1.8f), p, false);
            _lampGlows.Add(glow);
            var light = ShowcaseSprites.CreateLight(_mapRoot, "Static torch light", p);
            light.Configure(color, 2.3f, .3f, 4.8f, .6f, true);
            _lamps.Add(light);
        }
    }
}
