using System.Collections.Generic;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Groups the delivered interior pier tiles and paints decorated nave walls over each existing collision footprint.</summary>
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

        static void BuildNavePier(Landmark lm, Group group, ChurchMasonryArt finish)
        {
            const int ppu = 50;
            float x0 = group.MinX, y0 = -(group.MaxY + 1) * Sq;
            float width = group.MaxX - group.MinX + 1, depth = (group.MaxY - group.MinY + 1) * Sq;
            float height = finish.InteriorHeight;
            bool alongX = width > depth / Sq;
            float insetX = alongX ? .09f : width * .27f;
            float insetY = alongX ? depth * .27f : .07f;
            var c = new Canvas(x0, y0, width, depth + height + .08f, ppu);
            // Plinth covers all six solid cells. The raised wall has ordinary hand-sized stone courses.
            PaintWallSection(c, finish, x0, y0, width, depth, 0f, .17f, false);
            PaintWallSection(c, finish, x0 + insetX, y0 + insetY, width - insetX * 2f, depth - insetY * 2f,
                .17f, height - .17f, true);
            // Short stone returns brace the raised screen. Their feet stay on the existing plinth.
            // They stop below the coping so the screen still reads as one continuous length of wall.
            for (int end = 0; end < 2; end++)
            {
                if (alongX)
                {
                    float u = x0 + (end == 0 ? .24f : width - .50f);
                    PaintWallSection(c, finish, u, y0 + .04f, .26f, insetY - .035f, .17f, height * .39f, false);
                }
                else
                {
                    float v = y0 + depth * (end == 0 ? .19f : .76f);
                    PaintWallSection(c, finish, x0 + .05f, v, insetX - .045f, .20f, .17f, height * .39f, false);
                    PaintWallSection(c, finish, x0 + width - insetX, v, insetX - .05f, .20f, .17f, height * .39f, false);
                }
            }
            var renderer = c.ToSprite(lm, "Church pier stone", "Church nave pier", 2000 + 16 * group.MaxY + 1);
            renderer.sharedMaterial = lm.PaintedMaterial(false);
        }

        static void PaintWallSection(Canvas canvas, ChurchMasonryArt finish, float x0, float y0,
            float width, float depth, float sill, float rise, bool carved)
        {
            float edgeY = y0 + sill + rise;
            canvas.PaintIn(x0, y0 + sill, x0 + width, edgeY + depth, (x, y) =>
            {
                float u = x - x0, v = y - edgeY;
                if (v < 0f)
                {
                    float elev = v + rise;
                    Color face = finish.Masonry(x, elev + sill, false);
                    if (!carved) return Color.Lerp(face, finish.Ink, .38f);
                    face = RegionCanvas.Shade(face, .70f);
                    if (u < .14f || width - u < .14f)
                        return Color.Lerp(face, finish.CutEdge, .30f);
                    if (elev < .055f || -v < .065f)
                        return Color.Lerp(face, -v < .024f ? finish.Ink : finish.CutEdge, .43f);
                    return finish.Arcade(face, u * Mathf.Max(1, Mathf.RoundToInt(width / .85f)) / width, elev / rise);
                }
                float margin = Mathf.Min(Mathf.Min(u, width - u), Mathf.Min(v, depth - v));
                Color coping = carved ? finish.Cap(x, y / Sq, width > depth / Sq) :
                    RegionCanvas.Shade(finish.Masonry(x, y / Sq, true), .80f);
                if (margin < .045f) return Color.Lerp(coping, finish.Ink, .79f);
                if (margin < .090f) return Color.Lerp(coping, finish.CutEdge, .64f);
                if (margin < .13f) return Color.Lerp(coping, finish.Ink, .32f);
                return coping;
            });
        }
    }
}
