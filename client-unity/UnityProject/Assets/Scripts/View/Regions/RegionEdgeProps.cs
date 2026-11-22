using System.Collections.Generic;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Deterministic, non-colliding foreground dressing at public regional seams.</summary>
    public static class RegionEdgeProps
    {
        const int Fire = (int)RegionId.Fire;
        const int Plague = (int)RegionId.Plague;
        const int Frost = (int)RegionId.Frost;
        const int Holy = (int)RegionId.Holy;
        const int SeedSalt = 0x2E19;

        public static List<RegionProps.Placement> Generate(MapBlob map, RegionField field,
            RegionTransitionArt art, IList<RegionProps.Placement> existing, TempleLayout temple = null, PlagueGround plagueGround = null)
        {
            var result = new List<RegionProps.Placement>();
            if (map == null || map.Tiles == null || map.Regions == null || map.Tiles.Length != map.W * map.H ||
                map.Regions.Length != map.W * map.H || art == null || field == null || !field.Ready)
                return result;

            float grass = Mathf.Clamp01(art.GrassWeathering), rocks = Mathf.Clamp01(art.RockBreakup);
            if (grass <= 0f && rocks <= 0f) return result;
            var weights = new float[RegionField.Regions];
            var nave = ChurchLandmark.DeliveredNave(map);
            temple ??= TempleLayout.For(map);
            plagueGround ??= PlagueGround.Build(map);
            int seed = unchecked((int)(map.LayerHash ^ (map.SemanticHash * 2654435761u))) + SeedSalt;
            byte[] terrain = map.Terrain, bushes = map.Bushes;
            for (int y = 2; y < map.H - 2; y++) for (int x = 2; x < map.W - 2; x++)
            {
                int i = y * map.W + x;
                if (map.Tiles[i] != MapBlob.Floor || (terrain != null && i < terrain.Length && terrain[i] != 0) ||
                    (bushes != null && bushes.Length == map.W * map.H && bushes[i] != 0)) continue;
                if (nave.Contains(x, y) || temple.Contains(x, y) || plagueGround.KindAt(x, y) == PlagueGround.Water ||
                    InsideLandmark(map, x, y)) continue;
                byte region = map.Regions[i];
                if (region < 1 || region > 5) continue;

                field.Sample(x + .5f, y + .5f, weights);
                float frostMix = weights[Frost - 1], fireMix = weights[Fire - 1];
                string key = null;
                float scale = 0f;
                Color tint = Color.white;
                float roll = RegionNoise.Hash01(x, y, seed);

                // Keep the grass rooted on its real green-side region. Frost only changes its colour and density;
                // actual concealment cells and reeds in ice remain owned by their original systems.
                if (grass > 0f && (region == Plague || region == Holy) && frostMix >= .035f && frostMix < .88f &&
                    Touches(map.Regions, map.W, map.H, x, y, Frost, 2))
                {
                    float density = Mathf.Lerp(.24f, .075f, frostMix) * grass;
                    if (roll < density)
                    {
                        float dryChance = Mathf.SmoothStep(.26f, .70f, frostMix) * grass;
                        bool dryTuft = RegionNoise.Hash01(x, y, seed + 9) < dryChance;
                        key = dryTuft ? "sent.deadgrass" : region == Plague ? "plague.fern" : "holy.grass";
                        scale = Mathf.Lerp(dryTuft ? .27f : .30f, dryTuft ? .35f : .39f,
                            RegionNoise.Hash01(x, y, seed + 3));
                        var dry = new Color(.91f, .86f, .72f, 1f);
                        tint = dryTuft ? Color.white : Color.Lerp(Color.white, dry, Mathf.Clamp01(frostMix * grass * .22f));
                    }
                }

                // Scatter small, low scree around the Fire boundary. It uses the same authored stones and matte
                // pigment as the Fire rocks; only presentation scale changes, never terrain or walkability.
                if (key == null && rocks > 0f && fireMix >= .05f && fireMix < .96f &&
                    Touches(map.Regions, map.W, map.H, x, y, Fire, 2))
                {
                    float density = Mathf.Lerp(.12f, .20f, fireMix) * rocks;
                    if (roll < density)
                    {
                        key = "fire.scree.cobbles";
                        scale = Mathf.Lerp(.12f, .34f, fireMix) * Mathf.Lerp(.92f, 1.04f, RegionNoise.Hash01(x, y, seed + 5));
                        tint = Color.Lerp(new Color(.78f, .76f, .75f, 1f), Color.white, fireMix * .3f);
                    }
                }

                if (key == null || TooClose(existing, result, x + .5f, y + .5f)) continue;
                result.Add(new RegionProps.Placement
                {
                    Art = key, X = x + .5f, Y = y + .5f,
                    Scale = scale, Flip = RegionNoise.Hash01(x, y, seed + 7) < .5f,
                    Tx = x, Ty = y, TransitionDetail = true, TransitionTint = tint,
                    TransitionWidth = key == "sent.deadgrass"
                        ? Mathf.Lerp(.82f, .58f, frostMix) : key == "fire.scree.cobbles" ? 1f : Mathf.Lerp(.84f, .58f, frostMix),
                    TransitionHeight = key == "sent.deadgrass"
                        ? Mathf.Lerp(.86f, .57f, frostMix) : key == "fire.scree.cobbles" ? 1f : Mathf.Lerp(.94f, .62f, frostMix)
                });
            }
            return result;
        }

        public static bool WeatherExisting(MapBlob map, RegionField field, RegionTransitionArt art,
            RegionProps.Placement placement, string artKey, float[] weights, ref float scaleX, ref float scaleY, ref Color tint)
        {
            if (map == null || field == null || !field.Ready || art == null || map.Regions == null ||
                weights == null || weights.Length < RegionField.Regions ||
                (uint)placement.Tx >= (uint)map.W || (uint)placement.Ty >= (uint)map.H) return false;
            float grass = Mathf.Clamp01(art.GrassWeathering), rocks = Mathf.Clamp01(art.RockBreakup);
            if (grass <= 0f && rocks <= 0f) return false;
            byte region = map.Regions[placement.Ty * map.W + placement.Tx];
            if (grass > 0f && (artKey == "plague.fern" || artKey == "holy.grass" || artKey == "sent.deadgrass") &&
                region != Frost && Touches(map.Regions, map.W, map.H, placement.Tx, placement.Ty, Frost, 2))
            {
                field.Sample(placement.X, placement.Y, weights);
                float frostMix = weights[Frost - 1];
                if (frostMix < .035f || frostMix >= .9f) return false;
                var dry = new Color(.83f, .79f, .66f, 1f);
                tint *= Color.Lerp(Color.white, dry, Mathf.Clamp01(frostMix * grass * .78f));
                float thinning = artKey == "sent.deadgrass" ? .035f : .07f;
                scaleY *= 1f - frostMix * grass * thinning;
                return true;
            }

            if (rocks > 0f && (artKey == "fire.scree" || artKey == "fire.scree.cobbles") &&
                region == Fire && TouchesOtherRegion(map.Regions, map.W, map.H, placement.Tx, placement.Ty, Fire, 2))
            {
                field.Sample(placement.X, placement.Y, weights);
                float fireMix = weights[Fire - 1];
                float breakup = Mathf.Clamp01((1f - fireMix) * rocks);
                scaleX *= 1f - breakup * .18f;
                scaleY *= 1f - breakup * .18f;
                var weathered = Color.Lerp(new Color(.82f, .80f, .79f, 1f), Color.white, fireMix * .25f);
                tint *= Color.Lerp(Color.white, weathered, rocks);
                return breakup > 0f;
            }
            return false;
        }

        static bool Touches(byte[] regions, int width, int height, int x, int y, int target, int radius)
        {
            for (int dy = -radius; dy <= radius; dy++) for (int dx = -radius; dx <= radius; dx++)
            {
                if (Mathf.Abs(dx) + Mathf.Abs(dy) > radius) continue;
                int nx = x + dx, ny = y + dy;
                if ((uint)nx < (uint)width && (uint)ny < (uint)height && regions[ny * width + nx] == target) return true;
            }
            return false;
        }

        static bool TouchesOtherRegion(byte[] regions, int width, int height, int x, int y, int source, int radius)
        {
            for (int dy = -radius; dy <= radius; dy++) for (int dx = -radius; dx <= radius; dx++)
            {
                if (Mathf.Abs(dx) + Mathf.Abs(dy) > radius) continue;
                int nx = x + dx, ny = y + dy;
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height) continue;
                byte region = regions[ny * width + nx];
                if (region > 0 && region <= 5 && region != source) return true;
            }
            return false;
        }

        static bool InsideLandmark(MapBlob map, int x, int y)
        {
            var anchors = map.Anchors;
            if (anchors == null) return false;
            foreach (var anchor in anchors)
            {
                if (anchor == null) continue;
                var f = anchor.Footprint;
                // Leave a one-tile breathing margin around authored landmarks so edge tufts do not crowd their silhouettes.
                if (x >= f.X - 1 && x < f.X + f.W + 1 && y >= f.Y - 1 && y < f.Y + f.H + 1) return true;
            }
            return false;
        }

        static bool TooClose(IList<RegionProps.Placement> existing, List<RegionProps.Placement> added, float x, float y)
        {
            const float minSpacingSquared = .31f * .31f;
            if (existing != null) for (int i = 0; i < existing.Count; i++)
            {
                float dx = existing[i].X - x, dy = existing[i].Y - y;
                if (dx * dx + dy * dy < minSpacingSquared) return true;
            }
            for (int i = 0; i < added.Count; i++)
            {
                float dx = added[i].X - x, dy = added[i].Y - y;
                if (dx * dx + dy * dy < minSpacingSquared) return true;
            }
            return false;
        }
    }
}
