using System;
using System.Collections.Generic;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>
    /// Original procedural textures for the Wish effects. White-and-alpha only: colour comes from the palette at draw time.
    /// Generated once on first use (about 1.5 MB of RGBA in all, including the Fire brush textures), shared by sprites and particle materials, nothing derived
    /// from any third-party artwork. Each sprite's pixels-per-unit equals its size, so a scale of 1 is one world unit.
    /// </summary>
    public static class WishFxArt
    {
        public const string Soft = "wish.soft", Disc = "wish.disc", Band = "wish.band", Hairline = "wish.hairline", Dashed = "wish.dashed",
            Shard = "wish.shard", Fly = "wish.fly", Cross = "wish.cross", Rays = "wish.rays", Column = "wish.column", Chain = "wish.chain",
            Beam = "wish.beam", Flame = "wish.flame", Gavel = "wish.gavel", Scales = "wish.scales", Star = "wish.star", Solid = "wish.solid",
            FireWash = "wish.fire-wash", FireTrail = "wish.fire-trail", FireFleck = "wish.fire-fleck";

        static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        static readonly List<UnityEngine.Object> Owned = new List<UnityEngine.Object>();

        public static Sprite Get(string key)
        {
            if (Sprites.TryGetValue(key, out var cached) && cached != null) return cached;
            int width = key == FireTrail ? 512 : key == FireWash ? 256 : key == Band || key == Hairline || key == Dashed ? 256 : key == Fly || key == Star ? 64 : key == FireFleck ? 128 : 128;
            int height = key == FireTrail ? 128 : key == FireWash ? 256 : width;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            { name = "Original / " + key, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                float u = (x + .5f) / width * 2f - 1f, v = (y + .5f) / height * 2f - 1f;
                float a = Alpha(key, u, v, Mathf.Sqrt(u * u + v * v));
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
            texture.SetPixels32(pixels); texture.Apply(false, !Application.isEditor);
            var sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f), width, 0, SpriteMeshType.FullRect);
            sprite.name = key; sprite.hideFlags = HideFlags.HideAndDontSave;
            Sprites[key] = sprite; Owned.Add(texture); Owned.Add(sprite);
            return sprite;
        }

        /// <summary>Alpha-blended unlit material for particles using the texture <paramref name="key"/> (cached per key).</summary>
        public static Material ParticleMaterial(string key)
        {
            if (Materials.TryGetValue(key, out var cached) && cached != null) return cached;
            ShowcaseSprites.Initialize();
            var m = new Material(ShowcaseSprites.Unlit) { name = "Roguehalla / wish particle / " + key, hideFlags = HideFlags.HideAndDontSave };
            m.mainTexture = Get(key).texture;
            Materials[key] = m; Owned.Add(m);
            return m;
        }

        public static void Release()
        {
            foreach (var o in Owned) if (o != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o); }
            Owned.Clear(); Sprites.Clear(); Materials.Clear();
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t);
        }
        static float Seg(float x, float y, float ax, float ay, float bx, float by)
        {
            float px = x - ax, py = y - ay, dx = bx - ax, dy = by - ay;
            float t = Mathf.Clamp01((px * dx + py * dy) / (dx * dx + dy * dy));
            float qx = px - dx * t, qy = py - dy * t; return Mathf.Sqrt(qx * qx + qy * qy);
        }
        static float Edge(float d, float width) { return Mathf.Clamp01(d / width); }

        static float PaintNoise(float x, float y, uint seed)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            uint key = unchecked((uint)(ix * 73856093) ^ (uint)(iy * 19349663) ^ seed);
            float a = WishFxModel.Hash01(key), b = WishFxModel.Hash01(key + 1u);
            float c = WishFxModel.Hash01(key + 0x9e3779b9u), d = WishFxModel.Hash01(key + 0x9e3779bau);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Alpha(string key, float u, float v, float r)
        {
            float ang = Mathf.Atan2(v, u);
            switch (key)
            {
                case FireTrail:
                {
                    float along = (u + 1f) * .5f;
                    float noise = PaintNoise(along * 17f, (v + 1f) * 2.2f, 0x51f15e1u);
                    float grit = PaintNoise(along * 83f, (v + 1f) * 18f, 0x9ad571u);
                    float centre = .075f * Mathf.Sin(along * 18f + .7f) + .035f * Mathf.Sin(along * 43f + 1.8f) + (noise - .5f) * .14f;
                    float half = .68f + (noise - .5f) * .18f + .055f * Mathf.Sin(along * 34f + .4f);
                    float tapered = Smooth(0f, .12f, along) * (1f - Smooth(.86f, 1f, along));
                    float edge = 1f - Smooth(half - .16f, half + .07f, Mathf.Abs(v - centre));
                    float grain = Smooth(.04f, .43f, grit);
                    float dryBreak = grit < .10f ? .06f : .56f + .44f * noise;
                    return edge * tapered * grain * dryBreak;
                }
                case FireWash:
                {
                    float n = PaintNoise((u + 1f) * 5.5f, (v + 1f) * 5.5f, 0x0bba5e5u);
                    float radius = Mathf.Sqrt(Mathf.Pow(u * (.94f + .12f * n), 2f) + Mathf.Pow(v * (1.04f - .08f * n), 2f));
                    float edge = 1f - Smooth(.78f + (n - .5f) * .16f, 1.08f + (n - .5f) * .13f, radius);
                    float pigment = .5f + .5f * PaintNoise((u + 1f) * 15f, (v + 1f) * 15f, 0x725e1u);
                    float dry = PaintNoise((u + 1f) * 58f, (v + 1f) * 58f, 0x68c0a1u) < .075f ? .05f : 1f;
                    return edge * (.22f + .78f * pigment) * dry;
                }
                case FireFleck:
                {
                    float t = (v + 1f) * .5f;
                    float grain = PaintNoise((u + 1f) * 27f, (v + 1f) * 13f, 0x43a9e1u);
                    float centre = .07f * Mathf.Sin(t * 10f + .8f) + (grain - .5f) * .08f;
                    float half = .56f * Mathf.Pow(Mathf.Max(0f, 1f - t), .72f) * (.72f + .28f * grain);
                    float body = 1f - Smooth(half - .09f, half + .08f, Mathf.Abs(u - centre));
                    float taper = Smooth(0f, .11f, t) * (1f - Smooth(.72f, 1f, t));
                    float grit = PaintNoise((u + 1f) * 71f, (v + 1f) * 25f, 0x6f1ecau);
                    return body * taper * (.35f + .65f * Smooth(.03f, .45f, grit));
                }
                case Soft: return Mathf.Pow(Mathf.Max(0f, 1f - r), 2f);
                case Disc: return Smooth(1f, .74f, r);
                case Solid: return 1f;
                case Band: return Smooth(.66f, .94f, r) * Smooth(1f, .965f, r);
                case Hairline: return Mathf.Clamp01((.03f - Mathf.Abs(r - .955f)) * 60f);
                case Dashed:
                {
                    float stroke = Mathf.Clamp01((.036f - Mathf.Abs(r - .955f)) * 60f);
                    float cell = Mathf.Repeat(ang / (2f * Mathf.PI) * 36f, 1f);
                    return stroke * Smooth(.0f, .08f, cell) * Smooth(.66f, .58f, cell);
                }
                case Shard:
                {
                    float t = (v + 1f) * .5f;
                    float half = .36f * Mathf.Min(t / .3f, (1f - t) / .7f);
                    return Mathf.Clamp01((half - Mathf.Abs(u)) * 16f) * (.55f + .45f * (1f - Mathf.Abs(u) / Mathf.Max(.001f, half)));
                }
                case Fly:
                {
                    float body = Smooth(1f, .75f, Mathf.Sqrt(Mathf.Pow(u / .2f, 2) + Mathf.Pow(v / .5f, 2)));
                    float wl = Smooth(1f, .7f, Mathf.Sqrt(Mathf.Pow((u + .55f) / .45f, 2) + Mathf.Pow((v - .15f) / .26f, 2)));
                    float wr = Smooth(1f, .7f, Mathf.Sqrt(Mathf.Pow((u - .55f) / .45f, 2) + Mathf.Pow((v - .15f) / .26f, 2)));
                    return Mathf.Max(body, Mathf.Max(wl, wr) * .55f);
                }
                case Cross:
                {
                    float sx = Mathf.Clamp01(1f - Mathf.Abs(u) * 9f) * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(v)), 2f);
                    float sy = Mathf.Clamp01(1f - Mathf.Abs(v) * 9f) * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(u)), 2f);
                    return Mathf.Min(1f, Mathf.Max(sx, sy) + Mathf.Pow(Mathf.Max(0f, 1f - r * 2.2f), 2f));
                }
                case Rays:
                {
                    float m = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 6f)), 7f);
                    return (m * .85f + .12f) * Mathf.Pow(Mathf.Max(0f, 1f - r), 1.3f) * Smooth(.05f, .22f, r);
                }
                case Column:
                {
                    float t = (v + 1f) * .5f;
                    return Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Abs(u)), 1.7f) * Mathf.Pow(1f - t, 1.15f) * Smooth(0f, .07f, t);
                }
                case Chain:
                {
                    float e = Mathf.Sqrt(Mathf.Pow(u / .5f, 2) + v * v);
                    return Mathf.Clamp01((.2f - Mathf.Abs(e - .78f)) * 10f);
                }
                case Beam: return Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Abs(v)), 2f) * Smooth(1f, .72f, Mathf.Abs(u));
                case Flame:
                {
                    float t = (v + 1f) * .5f;
                    float w = .56f * Mathf.Pow(Mathf.Max(0f, 1f - t), .8f) * (.78f + .22f * Mathf.Cos(t * 9f)) * Smooth(0f, .08f, t * 3f + .0f);
                    return Mathf.Clamp01((w - Mathf.Abs(u)) * 12f) * (1f - t * t * t) * (.7f + .3f * (1f - Mathf.Abs(u) / Mathf.Max(.01f, w)));
                }
                case Gavel:
                {
                    float head = Mathf.Min(Mathf.Abs(u) - .56f, Mathf.Abs(v - .45f) - .22f);
                    float handle = Mathf.Min(Mathf.Abs(u) - .09f, Mathf.Abs(v + .28f) - .58f);
                    float band = Mathf.Min(Mathf.Abs(u) - .13f, Mathf.Abs(v - .45f) - .26f);
                    float d = Mathf.Min(head, Mathf.Min(handle, band));
                    return Mathf.Clamp01(-d * 24f);
                }
                case Scales:
                {
                    float pillar = Seg(u, v, 0f, -.82f, 0f, .66f) - .055f;
                    float beam = Seg(u, v, -.82f, .62f, .82f, .62f) - .045f;
                    float panL = Mathf.Abs(Mathf.Sqrt(Mathf.Pow(u + .6f, 2) + Mathf.Pow(v - .22f, 2)) - .3f) - .045f;
                    float panR = Mathf.Abs(Mathf.Sqrt(Mathf.Pow(u - .6f, 2) + Mathf.Pow(v - .22f, 2)) - .3f) - .045f;
                    if (v > .22f) { panL = panR = 1f; }
                    float strings = Mathf.Min(Mathf.Min(Seg(u, v, -.82f, .62f, -.9f, .22f), Seg(u, v, -.82f, .62f, -.3f, .22f)),
                        Mathf.Min(Seg(u, v, .82f, .62f, .3f, .22f), Seg(u, v, .82f, .62f, .9f, .22f))) - .022f;
                    float foot = Seg(u, v, -.4f, -.82f, .4f, -.82f) - .06f;
                    return Mathf.Clamp01(-Mathf.Min(Mathf.Min(pillar, beam), Mathf.Min(Mathf.Min(panL, panR), Mathf.Min(strings, foot))) * 24f);
                }
                case Star:
                {
                    float thr = .34f + .62f * Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 2.5f)), 2.2f);
                    return Mathf.Clamp01((thr - r) * 8f);
                }
                default: return Smooth(1f, .9f, r);
            }
        }
    }
}
