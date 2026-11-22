using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Fits the painted ice roof and wall caps to the delivered cave opening.</summary>
    public static partial class CaveLandmark
    {
        sealed class IcePainting
        {
            public Texture2D Source;
            public Color32[] Pixels;
            public Vector2 Door, Inward;

            // Keep procedural wall caps inside the irregular silhouette of the
            // painted roof, so the old rectangular cap cannot show through it.
            public float RoofAlpha(Landmark lm, float u, float v)
            {
                const float lip = 1.25f;
                float width = lm.FootW + 2f * lip, height = lm.FootH + 2f * lip;
                float tx = (u + lip) / width * Source.width - .5f;
                float ty = (lm.FootH + lip - v) / height * Source.height - .5f;
                if (tx < 0f || ty < 0f || tx >= Source.width - 1 || ty >= Source.height - 1) return 0f;
                int x = Mathf.FloorToInt(tx), y = Mathf.FloorToInt(ty);
                float fx = tx - x, fy = ty - y;
                float a00 = Pixels[y * Source.width + x].a / 255f;
                float a10 = Pixels[y * Source.width + x + 1].a / 255f;
                float a01 = Pixels[(y + 1) * Source.width + x].a / 255f;
                float a11 = Pixels[(y + 1) * Source.width + x + 1].a / 255f;
                return Mathf.Lerp(Mathf.Lerp(a00, a10, fx), Mathf.Lerp(a01, a11, fx), fy);
            }

            // Sample the middle of the same painting for the visible cliff and inner rock islands.
            // Mirror the sample at each edge so two neighbouring rows share their brush strokes.
            public Color WallPigment(float u, float v, bool face)
            {
                float sx = .20f + .64f * Mathf.PingPong(.54f + u * .087f + v * .019f, 1f);
                float sy = .19f + .63f * Mathf.PingPong(.79f - v * .089f - u * .009f, 1f);
                var sample = Pixels[(int)(sy * Source.height) * Source.width + (int)(sx * Source.width)];
                float value = Mathf.Clamp01((sample.r * .3f + sample.g * .55f + sample.b * .15f - 53f) / 189f);
                return face
                    ? Color.Lerp(new Color(.095f, .14f, .19f), new Color(.31f, .44f, .49f), value)
                    : Color.Lerp(new Color(.13f, .21f, .27f), new Color(.63f, .72f, .73f), value);
            }
        }

        // The painting stays upright. The cut follows the delivered floor cells.
        static IcePainting ReadIcePainting(Landmark lm)
        {
            if (!lm.IsDeliveredAnchor || !StormFrostGeometry.For(lm.SourceMap).Matches(lm.Anchor)) return null;
            var first = lm.ArchTiles[0];
            int minX = first.X, maxX = first.X, minY = first.Y, maxY = first.Y;
            foreach (var tile in lm.ArchTiles)
            {
                minX = Mathf.Min(minX, tile.X); maxX = Mathf.Max(maxX, tile.X);
                minY = Mathf.Min(minY, tile.Y); maxY = Mathf.Max(maxY, tile.Y);
            }
            bool onX = minX == maxX;
            var source = Resources.Load<Texture2D>(StormFrostGeometry.RoofResource);
            if (source == null || !source.isReadable) return null;

            Vector2 inward = onX ? new Vector2(minX == lm.FootX ? 1f : -1f, 0f)
                : new Vector2(0f, minY == lm.FootY ? 1f : -1f);
            return new IcePainting {
                Source = source, Pixels = source.GetPixels32(), Inward = inward,
                Door = new Vector2((minX + maxX + 1) * .5f, (minY + maxY + 1) * .5f)
            };
        }

        static void BuildIceRoof(Landmark lm, IcePainting painting)
        {
            var source = painting.Source;
            var inward = painting.Inward;
            var door = painting.Door;
            Vector2 across = new Vector2(-inward.y, inward.x);
            const float lip = 1.25f;
            float left = lm.FootX - lip, bottom = lm.FootY + lm.FootH + lip;
            float width = lm.FootW + 2f * lip, height = lm.FootH + 2f * lip;
            var pixels = painting.Pixels;
            // Only visit pixels around the cut. Keep the original ink everywhere else.
            int x0 = Mathf.Max(0, Mathf.FloorToInt((door.x - 4f - left) / width * source.width));
            int x1 = Mathf.Min(source.width, Mathf.CeilToInt((door.x + 4f - left) / width * source.width));
            int y0 = Mathf.Max(0, Mathf.FloorToInt((bottom - door.y - 4f) / height * source.height));
            int y1 = Mathf.Min(source.height, Mathf.CeilToInt((bottom - door.y + 4f) / height * source.height));
            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                var offset = new Vector2(left + (x + .5f) * width / source.width,
                    bottom - (y + .5f) * height / source.height) - door;
                float flank = Mathf.Abs(Vector2.Dot(offset, across)) / 2.18f;
                float depth = Vector2.Dot(offset, inward);
                if (flank >= 1f) continue;
                float opening = (1f - flank * flank) * 2.15f;
                float feather = Mathf.Max(Mathf.InverseLerp(opening - .19f, opening + .12f, depth),
                    Mathf.InverseLerp(.89f, 1f, flank));
                int index = y * source.width + x;
                pixels[index].a = (byte)Mathf.RoundToInt(pixels[index].a * feather);
            }
            var tex = NewTexture(lm, source.width, source.height, "Frost ice roof and portal");
            tex.SetPixels32(pixels);
            tex.Apply(false, !Application.isEditor);
            var centre = new Vector3(left + width * .5f, -(bottom - height * .5f) * ArenaView.GroundSquash);
            var scale = new Vector3(width * 32f / tex.width, height * ArenaView.GroundSquash * 32f / tex.height, 1f);
            var roof = NewSprite(lm, tex, "Frost painted ice roof", new Vector2(.5f, .5f), 32f, centre, scale, lm.RoofOrder);
            lm.AddRoofRenderer(roof);
        }
    }
}
