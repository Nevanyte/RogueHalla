using System.Collections.Generic;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client
{
    public static partial class ChurchLandmark
    {
        static Group[] FindNavePiers(MapBlob map, Group building)
        {
            int width = building.MaxX - building.MinX - 1;
            int depth = building.MaxY - building.MinY - 1;
            if (width <= 0 || depth <= 0) return System.Array.Empty<Group>();

            // Strip the enclosing wall, including the portions connected to engaged piers.
            var groups = FindGroups(building.MinX + 1, building.MinY + 1, width, depth, (x, y) =>
            {
                int i = y * map.W + x;
                return map.Tiles[i] == MapBlob.Wall && (map.Terrain == null ||
                    (map.Terrain[i] != (byte)TerrainKind.Lava && map.Terrain[i] != (byte)TerrainKind.Crevasse));
            });
            var piers = new List<Group>();
            foreach (var g in groups)
            {
                int w = g.MaxX - g.MinX + 1, d = g.MaxY - g.MinY + 1;
                if (g.Tiles.Count == 6 && Mathf.Min(w, d) == 2 && Mathf.Max(w, d) == 3) piers.Add(g);
            }
            return piers.ToArray();
        }

        static void BuildNavePier(Landmark lm, Group group)
        {
            const int ppu = 44;
            float x = group.MinX;
            float y = -(group.MaxY + 1) * Sq;
            float width = group.MaxX - group.MinX + 1;
            float depth = (group.MaxY - group.MinY + 1) * Sq;
            float cx = x + width * .5f, cy = y + depth * .5f;
            float hw = width * .5f, hd = depth * .5f;
            var c = new Canvas(x, y, width, depth + .67f, ppu);
            PaintPierTier(c, cx, cy, hw * .995f, hd * .995f, .11f, .11f, .09f,
                new Color(.31f, .30f, .36f));
            PaintPierTier(c, cx, cy, hw * .94f, hd * .92f, .20f, .09f, .16f,
                new Color(.45f, .42f, .49f));
            // Two engaged shafts fit the long axis of the original rectangular block.
            // The narrow upper planes stay inside the old cap projection.
            bool across = width > depth / Sq;
            for (int shaft = 0; shaft < 2; shaft++)
            {
                float direction = 1 - 2 * shaft;
                float sx = cx + (across ? direction * hw * .39f : 0f);
                float sy = cy + (across ? 0f : direction * hd * .38f);
                float rw = hw * (across ? .39f : .62f), rd = hd * (across ? .62f : .36f);
                PaintPierTier(c, sx, sy, rw, rd, .29f, .12f, .24f,
                    new Color(.40f, .38f, .46f));
                PaintPierTier(c, sx, sy, rw * .81f, rd * .84f, .71f, .43f, .27f,
                    new Color(.39f, .36f, .45f));
                PaintPierTier(c, sx, sy, rw * .94f, rd * .96f, .79f, .08f, .24f,
                    new Color(.47f, .43f, .52f));
                PaintPierTier(c, sx, sy, rw, rd, .86f, .08f, .29f,
                    new Color(.43f, .40f, .50f), capital: true);
            }
            var renderer = c.ToSprite(lm, "Church pier stone", "Church nave pier", 2000 + 16 * group.MaxY + 1);
            renderer.sharedMaterial = lm.PaintedMaterial(false);
        }

        static void PaintPierTier(Canvas canvas, float cx, float cy, float hw, float hd,
            float top, float height, float chamfer, Color wash, bool capital = false)
        {
            canvas.PaintIn(cx - hw, cy - hd + top - height, cx + hw, cy + hd + top, (x, y) =>
            {
                float u = (x - cx) / hw, v = (y - cy - top) / hd;
                float ax = Mathf.Abs(u);
                if (ax >= 1f) return default;
                float corner = Mathf.Max(0f, ax - (1f - chamfer)) / chamfer;
                float north = 1f - chamfer * corner;
                float south = -north;
                bool topPlane = v >= south && v <= north;
                float face = (v - south) * hd / height;
                if (!topPlane && (face < -1f || face >= 0f)) return default;

                float grain = Noise(x * 11f, y * 12f, 122);
                float stain = Noise(x * 2.3f, y * 3.7f, 89);
                float mottling = .85f + .21f * stain + .10f * grain;
                if (!topPlane)
                {
                    float flute = Mathf.Cos(u * 26f) * .12f;
                    float cut = corner > 0 ? (u > 0 ? .86f : .64f) : .72f;
                    float ledge = Mathf.SmoothStep(.74f, 1.15f, face + 1f);
                    return Shade(wash, (cut + flute) * ledge * mottling);
                }

                float edge = Mathf.Min((1f - ax) * hw, (north - Mathf.Abs(v)) * hd);
                float bevel = edge < .035f ? .52f + .75f * Mathf.Clamp01((v + 1f) * .5f) : 1f;
                if (capital)
                {
                    // Two nested moldings and an inset quatrefoil, cut into the violet-grey capital.
                    float qx = u * 2.8f, qy = v * 2.8f;
                    float r = Mathf.Min(Mathf.Min(new Vector2(Mathf.Abs(qx) - .42f, qy).magnitude,
                        new Vector2(qx, Mathf.Abs(qy) - .42f).magnitude), .9f);
                    if (edge > .07f && edge < .13f) bevel *= .69f + .19f * grain;
                    else if (Mathf.Abs(r - .55f) < .048f) bevel *= .71f;
                    else if (r < .55f) bevel *= .91f;
                    float vein = v + u * .30f + .05f * (stain - .5f);
                    if (u < -.43f && Mathf.Abs(vein - .50f) < .025f) bevel *= .65f + .30f * grain;
                }
                return Shade(wash, bevel * mottling);
            });
        }
    }
}
