using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client
{
    public sealed partial class RegionWalls
    {
        // Abrasion cuts only exposed corners. Joined edges keep their full span and the painted
        // cap and face share the very same front edge. Everything stays in the original row mesh.
        void DrawParapet(int x, int y, MapColiseum arena, float foot, bool west, bool east,
            bool north, bool south, Color32 tint)
        {
            var finish = RegionWallAtlas.Parapet;
            var face = finish.Face[(x - arena.X) % RegionWallAtlas.FaceVariants];
            bool recedes = west && east && (!north || !south);
            int along = recedes ? y - arena.Y : x - arena.X;
            var cap = finish.Top[along % RegionWallAtlas.TopVariants];
            float wear = .095f + .055f * RegionNoise.Hash01(x, y, 37);
            float leftCut = west && (north || south) ? wear : 0f;
            float rightCut = east && (north || south) ? wear * .82f : 0f;
            float leftFront = south ? leftCut : 0f, rightFront = south ? rightCut : 0f;
            float leftBack = 1f - (north ? leftCut : 0f), rightBack = 1f - (north ? rightCut : 0f);

            Vector2 Surface(float u, float v) => new Vector2(x + u, foot + WallHeight + v * TopHeight);
            Vector2 CapUv(float u, float v) => new Vector2(
                Mathf.Lerp(cap.Min.x, cap.Max.x, recedes ? 1f - v : u),
                Mathf.Lerp(cap.Min.y, cap.Max.y, recedes ? u : v));

            void StoneStrip(float start, float end, float frontA, float frontB, float backA, float backB, float faceKind)
            {
                Vector2 a = Surface(start, frontA), b = Surface(end, frontB);
                Vector2 c = Surface(end, backB), d = Surface(start, backA);
                if (south)
                {
                    _kind = faceKind;
                    float u0 = Mathf.Lerp(face.Min.x, face.Max.x, start), u1 = Mathf.Lerp(face.Min.x, face.Max.x, end);
                    Quad(a - Vector2.up * WallHeight, b - Vector2.up * WallHeight, b, a,
                        new Vector2(u0, face.Min.y), new Vector2(u1, face.Min.y),
                        new Vector2(u1, face.Max.y), new Vector2(u0, face.Max.y), tint);
                }
                _kind = 0f;
                Quad(a, b, c, d, CapUv(start, frontA), CapUv(end, frontB),
                    CapUv(end, backB), CapUv(start, backA), tint);
                if (north) ParapetRim(c, d, finish.Rim);
            }

            if (south)
            {
                _kind = 4f;
                Rect(x - .04f, foot - .30f, x + 1.04f, foot + .04f,
                    RegionWallAtlas.Contact, new Color32(255, 255, 255, 255));
            }
            if (leftCut > 0f) StoneStrip(0f, leftCut, leftFront, 0f, leftBack, 1f, 2f);
            StoneStrip(leftCut, 1f - rightCut, 0f, 0f, 1f, 1f, 1f);
            if (rightCut > 0f) StoneStrip(1f - rightCut, 1f, 0f, rightFront, 1f, rightBack, 3f);
            if (west) ParapetRim(Surface(0f, leftBack), Surface(0f, leftFront), finish.Rim);
            if (east) ParapetRim(Surface(1f, rightFront), Surface(1f, rightBack), finish.Rim);
        }

        void ParapetRim(Vector2 start, Vector2 end, RegionWallAtlas.Cell rim)
        {
            Vector2 tangent = (end - start).normalized;
            var inward = new Vector2(-tangent.y, tangent.x) * .043f;
            Quad(start, end, end + inward, start + inward,
                rim.Min, new Vector2(rim.Max.x, rim.Min.y), rim.Max,
                new Vector2(rim.Min.x, rim.Max.y), new Color32(255, 255, 255, 255));
        }
    }
}
