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
            if (!StormCypressGeometry.For(lm.SourceMap).Matches(lm.Anchor)) return false;

            var field = new Field(grid, 0);
            var trunk = Resources.Load<Sprite>(StormCypressGeometry.TrunkResource);
            var crown = Resources.Load<Sprite>("StormInk/Plague/CypressCrown");
            if (trunk == null || crown == null) return false;

            // Both strips come from one upright painting. A single scale keeps the fork continuous
            // through its join even when the delivered trunk is off-centre or reflected.
            float sourceWidth = trunk.rect.width;
            float sourceHeight = trunk.rect.height + crown.rect.height;
            const float rootContact = .17f;
            float availableWidth = Mathf.Min(grid.W * .75f, 2f * Mathf.Min(field.Cu, grid.W - field.Cu) - .5f);
            float availableHeight = (field.Cv * Sq - .45f) / (1f - rootContact);
            float unit = Mathf.Min(availableWidth / sourceWidth, availableHeight / sourceHeight);
            float width = sourceWidth * unit;
            float trunkHeight = trunk.rect.height * unit;
            var lowerLeft = new Vector2(lm.FootX + field.Cu - .5f * width,
                -(lm.FootY + field.Cv) * Sq - sourceHeight * unit * rootContact);

            var bark = StormInkLandmarkSprite.Place(lm, trunk, TrunkName, lowerLeft,
                new Vector2(width, trunkHeight), Vector2.zero, StormInkLandmarkSprite.RowOrder(lm.FootY + field.Cv + 1.25f));
            var properties = new MaterialPropertyBlock();
            bark.GetPropertyBlock(properties);
            properties.SetFloat("_WetRootContact", 1f);
            bark.SetPropertyBlock(properties);
            lm.AddFadeRenderer(bark);
            var crownBase = lowerLeft + Vector2.up * trunkHeight;
            var crownSpan = new Vector2(width, crown.rect.height * unit);
            lm.AddRoofRenderer(StormInkLandmarkSprite.Place(lm, crown, CrownName, crownBase, crownSpan, Vector2.zero, 0));
            SetStormCrownFade(lm, crown, crownBase, crownSpan);
            PaintStormGroundContact(lm, grid, cover, field);
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

        static void PaintStormGroundContact(Landmark lm, Grid grid, bool[] cover, Field trunk)
        {
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
