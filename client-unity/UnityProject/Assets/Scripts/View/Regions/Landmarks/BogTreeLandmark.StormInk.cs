using System.Collections.Generic;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Places the painted cypress over the rooted footprint, with a separate fade layer for its crown.</summary>
    public static partial class BogTreeLandmark
    {
        static bool PaintStormInk(Landmark lm, Grid grid, bool[] cover, List<MapTile> coverTiles)
        {
            if (!lm.IsDeliveredAnchor || lm.Anchor.Zone != (byte)RegionId.Plague) return false;

            var field = new Field(grid, 0);
            int rootWidth = field.MaxX - field.MinX + 1;
            int rootDepth = field.MaxY - field.MinY + 1;
            if (rootWidth < 7 || rootDepth < 7 || grid.W < 20 || grid.H < 20) return false;
            var trunk = Resources.Load<Sprite>("StormInk/Plague/CypressTrunk");
            var crown = Resources.Load<Sprite>("StormInk/Plague/CypressCrown");
            if (trunk == null && crown == null) return false;

            // Through the fork, bark and needles overlap the same fighter. Give that double layer a lighter wash.
            if (trunk != null && crown != null && lm.Material.HasProperty("_LayeredCanopy"))
                lm.Material.SetFloat("_LayeredCanopy", 1f);

            Vector2 trunkSpan;
            if (trunk == null)
            {
                trunkSpan = PaintBark(lm, grid, field, TrunkName, false);
            }
            else
            {
                float width = Mathf.Min(rootWidth * .97f, 2f * Mathf.Min(field.Cu, grid.W - field.Cu) - .5f);
                float height = Mathf.Min(11.5f, field.Cv * Sq * 1.22f);
                var contact = new Vector2(lm.FootX + field.Cu, -(lm.FootY + field.Cv) * Sq);
                const float RootContact = .27f;
                var trunkRenderer = StormInkLandmarkSprite.Place(lm, trunk, TrunkName, contact, new Vector2(width, height),
                    new Vector2(.49f, RootContact), StormInkLandmarkSprite.RowOrder(lm.FootY + field.Cv + 1.25f));
                // The artwork is a full-rect upright cutout. Stain only its lower root skirt where it meets silt.
                var barkProperties = new MaterialPropertyBlock();
                trunkRenderer.GetPropertyBlock(barkProperties);
                barkProperties.SetFloat("_WetRootContact", 1f);
                trunkRenderer.SetPropertyBlock(barkProperties);
                lm.AddFadeRenderer(trunkRenderer);

                float bottom = -field.Cv * Sq - height * RootContact;
                trunkSpan = new Vector2(bottom, bottom + height);
            }

            if (crown == null) PaintCanopy(lm, grid, cover, coverTiles, trunkSpan);
            else PaintStormCrown(lm, grid, cover, field, trunkSpan, crown);
            if (Resources.Load<Sprite>("StormInk/Plague/CypressKnees") is Sprite knees)
                PaintStormKnees(lm, grid, knees);
            else
                PaintProceduralKnees(lm, grid);
            lm.AddFadeTiles(coverTiles);
            return true;
        }

        static void PaintProceduralKnees(Landmark lm, Grid grid)
        {
            for (int group = 1; group < grid.Groups; group++)
                PaintBark(lm, grid, new Field(grid, group), StumpName, true);
        }

        static void PaintStormKnees(Landmark lm, Grid grid, Sprite knees)
        {
            const float OccupiedWidth = .9375f; // measured alpha bounds: x=12..371 of the 384px source
            const float OccupiedCentreX = .505f; // measured occupied-alpha centroid x=193.88/384
            const float GroundContactY = .12f;   // authored silhouette feet are near the lower 12% of the source
            var bounds = knees.bounds;
            float sourceAspect = bounds.size.y / bounds.size.x;
            for (int group = 1; group < grid.Groups; group++)
            {
                var field = new Field(grid, group);
                if (field.MaxX < field.MinX || field.MaxY < field.MinY) continue;
                float islandWidth = field.MaxX - field.MinX + 1f;
                // Scale by the measured occupied silhouette, not transparent sprite margins, so the knees follow
                // the original blocked island width. The source includes its own broad silt/contact wash.
                float visualWidth = islandWidth * 1.04f;
                float spriteWidth = visualWidth / OccupiedWidth;
                float spriteHeight = spriteWidth * sourceAspect;
                var contact = new Vector2(lm.FootX + field.Cu, -(lm.FootY + field.MaxY + 1f) * Sq);
                int order = StormInkLandmarkSprite.RowOrder(lm.FootY + field.MaxY + 1f);
                var root = StormInkLandmarkSprite.Place(lm, knees, StumpName, contact,
                    new Vector2(spriteWidth, spriteHeight), new Vector2(OccupiedCentreX, GroundContactY), order);
                // Wet bark and silt pick up the bank's grey-green pigment, especially at the feathered feet.
                root.color = new Color32(167, 178, 166, 235);
                root.flipX = field.Cu < grid.W * .5f;
            }
        }

        static void PaintStormCrown(Landmark lm, Grid grid, bool[] cover, Field trunk, Vector2 trunkSpan, Sprite crown)
        {
            // The fork and living branches show below a broad, open layer of needles and hanging moss.
            float bottom = Mathf.Lerp(trunkSpan.x, trunkSpan.y, .63f);
            float height = Mathf.Min(7f, -.35f - bottom);
            float centre = trunk.Cu + .35f;
            float width = Mathf.Min(grid.W * .75f, 2f * Mathf.Min(centre, grid.W - centre) - .75f);
            var lowerLeft = new Vector2(lm.FootX + centre - width * .5f, -lm.FootY * Sq + bottom);
            var span = new Vector2(width, height);
            lm.AddRoofRenderer(StormInkLandmarkSprite.Place(lm, crown, CrownName, lowerLeft, span, Vector2.zero, 0));
            SetStormCrownFade(lm, crown, lowerLeft, span);

            int x0 = grid.W, x1 = -1, y0 = grid.H, y1 = -1;
            for (int y = 0; y < grid.H; y++) for (int x = 0; x < grid.W; x++) if (cover[y * grid.W + x])
            {
                x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x);
                y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
            }
            if (x1 >= 0) PaintStormContact(lm, trunk, x0, x1, y0, y1);
        }

        /// <summary>A soft, non-cell-shaped cypress wash. Its outline follows the measured tree mass, not the rectangular wall pixels used for collision.</summary>
        static void PaintStormContact(Landmark lm, Field trunk, int coverMinX, int coverMaxX, int coverMinY, int coverMaxY)
        {
            const float ContactAlpha = .12f;
            float centreX = trunk.Cu;
            float centreY = (trunk.MinY + trunk.MaxY + 1f) * .5f + .25f;
            float radiusX = Mathf.Max(2.1f, (coverMaxX - coverMinX + 1f) * .46f);
            float radiusY = Mathf.Max(1.6f, (coverMaxY - coverMinY + 1f) * .36f);
            float u0 = centreX - radiusX - .35f, u1 = centreX + radiusX + .35f;
            float v0 = centreY - radiusY - .35f, v1 = centreY + radiusY + .35f;
            int width = Mathf.Max(1, Mathf.CeilToInt((u1 - u0) * ShadowPpt));
            int height = Mathf.Max(1, Mathf.CeilToInt((v1 - v0) * Sq * ShadowPpt));
            var px = new Color32[width * height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                float u = u0 + (x + .5f) / ShadowPpt;
                float v = v1 - (y + .5f) / (ShadowPpt * Sq);
                float nx = (u - centreX) / radiusX, ny = (v - centreY) / radiusY;
                float radial = Mathf.Sqrt(nx * nx + ny * ny);
                float edge = (RegionNoise.Periodic(u * 1.25f, v * 1.45f, 61, 83) - .5f) * .16f;
                float body = 1f - SmoothStep(.70f, 1.04f, radial + edge);
                float dapple = .84f + .16f * RegionNoise.Periodic(u * 2.1f, v * 2.0f, 61, 89);
                float core = 1f - SmoothStep(.02f, .82f, radial);
                float alpha = ContactAlpha * body * dapple + .035f * core;
                var colour = new Color32(14, 24, 17, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                px[y * width + x] = colour;
            }
            var texture = NewTexture(lm, width, height, ShadowName); texture.SetPixels32(px); texture.Apply(false, false);
            float bottom = -(lm.FootY + v1) * Sq;
            var renderer = NewSprite(lm, lm.Root.transform, texture, ShadowName,
                new Vector3(lm.FootX + u0, bottom, 0f), 0, ShadowPpt);
            lm.AddGroundPiece(renderer, 2000 + 16 * (lm.FootY + Mathf.FloorToInt(v0)) - 6);
        }

        static void SetStormCrownFade(Landmark lm, Sprite crown, Vector2 lowerLeft, Vector2 span)
        {
            var fade = new bool[lm.FootW * lm.FootH];

            int x0 = Mathf.FloorToInt(lowerLeft.x);
            int y0 = Mathf.FloorToInt(-(lowerLeft.y + span.y) / Sq);
            int x1 = Mathf.CeilToInt(lowerLeft.x + span.x);
            int y1 = Mathf.CeilToInt(-lowerLeft.y / Sq);

            // The cutout is a single full-rect sprite. A non-readable importer still fades the
            // projected area, so a visible character never sits behind an opaque crown.
            if (crown.texture.isReadable)
            {
                var texture = crown.texture;
                var pixels = texture.GetPixels32();
                var rect = crown.rect;
                int left = Mathf.RoundToInt(rect.x), bottom = Mathf.RoundToInt(rect.y);
                int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
                for (int v = 0; v < h; v++)
                {
                    int y = Mathf.FloorToInt(-(lowerLeft.y + (v + .5f) / h * span.y) / Sq);
                    for (int u = 0; u < w; u++)
                    {
                        if (pixels[(bottom + v) * texture.width + left + u].a < 128) continue;
                        int x = Mathf.FloorToInt(lowerLeft.x + (u + .5f) / w * span.x);
                        Mark(x, y);
                    }
                }
            }
            else
            {
                for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) Mark(x, y);
            }

            var tiles = new List<MapTile>();
            for (int y = 0; y < lm.FootH; y++) for (int x = 0; x < lm.FootW; x++)
                if (fade[y * lm.FootW + x]) tiles.Add(new MapTile((ushort)(lm.FootX + x), (ushort)(lm.FootY + y)));
            lm.SetFadeTiles(tiles);

            void Mark(int x, int y)
            {
                x -= lm.FootX;
                y -= lm.FootY;
                if ((uint)x >= (uint)lm.FootW) return;
                for (int r = 0; r <= CrownFadeSouthRows; r++)
                    if ((uint)(y + r) < (uint)lm.FootH) fade[(y + r) * lm.FootW + x] = true;
            }
        }
    }
}
