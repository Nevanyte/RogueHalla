// Fire zone props and authored Fire concealment scrub.
using System;
using System.Collections.Generic;
using UnityEngine;
using static Roguehalla.Client.RegionCanvas;

namespace Roguehalla.Client
{
    /// <summary>Fire zone prop definitions for <c>RegionPropArt</c>.</summary>
    public static partial class RegionPropArt
    {
        static void AddFire(List<Def> l)
        {
            Color baseC = P(1, 0), obs = P(1, 1), lava = P(1, 2), shadow = P(1, 5);
            Color rockDark = Shade(Mix(obs, shadow, .5f), .75f), rockMid = Mix(obs, baseC, .4f), rockLight = Mix(Mix(obs, baseC, .6f), lava, .12f);
            var scrubArt = Resources.Load<StormInkPropArtDefinition>("StormInk/Props/FireThornScrubArt");
            Add(l, "fire.spire", 80, 164, 10, (c, gy) =>
            {
                float cx = 40; c.SoftShadow(cx + 6, gy + 2, 32f, 7f, .5f, Alpha(shadow, 1f));
                c.Glow(cx, gy + 8, 30f, lava, .35f);
                var body = Poly(new Vector2(18, gy), new Vector2(24, gy + 52), new Vector2(30, gy + 98), new Vector2(40, gy + 142), new Vector2(50, gy + 96), new Vector2(60, gy + 50), new Vector2(64, gy));
                c.Fill(body, Split(41, Mix(rockMid, rockLight, .55f), rockDark, 10f), Ink, 1.6f);
                c.Fill(Intersect(body, Poly(new Vector2(41, gy + 130), new Vector2(46, gy + 60), new Vector2(40, gy + 4), new Vector2(34, gy + 60))), Flat(Alpha(rockLight, .45f)));
                // glowing seam
                var seam = Capsule(new Vector2(47, gy + 12), new Vector2(43, gy + 70), 1.4f, .6f);
                c.Fill(Intersect(body, seam), Flat(Mix(lava, Color.white, .25f)));
                c.Glow(46, gy + 30, 14f, lava, .35f);
                c.Fill(Capsule(new Vector2(30, gy + 60), new Vector2(26, gy + 22), .9f, .5f), Flat(Alpha(Shade(rockDark, .5f), .8f)));
            }, true);
            Action<RegionCanvas, int> rockFallback = (c, gy) =>
            {
                c.SoftShadow(56, gy + 2, 40f, 7f, .45f, Alpha(shadow, 1f)); c.Glow(52, gy + 6, 34f, lava, .22f);
                Rock(c, 40, gy, 24, 22, 4, rockDark, rockMid, rockLight, null, 0, Mix(lava, Color.white, .15f), .2f);
                Rock(c, 70, gy, 20, 17, 9, rockDark, rockMid, rockLight, null, 0, Mix(lava, Color.white, .15f), .2f);
                Rock(c, 56, gy + 1, 14, 11, 13, rockDark, rockMid, rockLight);
            };
            Add(l, "fire.rocks", 104, 70, 8, rockFallback);
            AddPaintedRock("fire.scree", "FireScreeArt", new Vector2(.5f, .12f));
            AddPaintedRock("fire.scree.cobbles", "FireScreeCobblesArt", new Vector2(.5f, .12f));
            AddPaintedRock("fire.basalt.ledge", "FireBasaltLedgeArt", new Vector2(.51f, .16f));

            void AddPaintedRock(string key, string resource, Vector2 fallbackFoot)
            {
                var art = Resources.Load<StormInkPropArtDefinition>("StormInk/Props/" + resource);
                int width = art != null ? art.WidthPixels(104) : 104;
                int height = art != null ? art.HeightPixels(70) : 70;
                int ground = art != null ? art.GroundPixels(8, height) : 8;
                Add(l, key, width, height, ground, rockFallback,
                    textureFoot: art != null ? art.ClampedTextureFoot(fallbackFoot) : null,
                    textureSource: art != null ? art.Pigment : null);
            }
            if (scrubArt != null && HasVisiblePigment(scrubArt.Pigment))
            {
                int scrubW = scrubArt.WidthPixels(99);
                int scrubH = scrubArt.HeightPixels(61);
                int scrubGround = scrubArt.GroundPixels(6, scrubH);
                Add(l, "fire.bush", scrubW, scrubH, scrubGround, (c, gy) => { },
                    textureFoot: scrubArt.ClampedTextureFoot(new Vector2(.5f, .06f)),
                    textureSource: scrubArt.Pigment);
            }
            Add(l, "fire.stump", 96, 124, 10, (c, gy) =>
            {
                float cx = 48; c.SoftShadow(cx + 6, gy + 2, 30f, 6f, .45f, Alpha(shadow, 1f)); c.Glow(cx, gy + 5, 26f, lava, .30f);
                Color wood = Shade(Mix(obs, baseC, .5f), .55f), woodLight = Mix(wood, lava, .20f);
                var trunk = Union(Capsule(new Vector2(cx, gy), new Vector2(cx - 4, gy + 52), 10, 6), Capsule(new Vector2(cx - 4, gy + 48), new Vector2(cx - 22, gy + 84), 5, 2.5f),
                                  Capsule(new Vector2(cx - 3, gy + 50), new Vector2(cx + 20, gy + 92), 4.5f, 2f), Capsule(new Vector2(cx + 8, gy + 70), new Vector2(cx + 30, gy + 80), 2.5f, 1.2f), Capsule(new Vector2(cx - 12, gy + 66), new Vector2(cx - 30, gy + 66), 2.6f, 1f));
                c.Fill(trunk, Toon(trunk, Shade(wood, .55f), wood, woodLight, 6f, .12f, 3, .2f), Ink, 1.5f);
                c.Fill(Intersect(trunk, Capsule(new Vector2(cx - 1, gy + 4), new Vector2(cx - 5, gy + 50), 1f, .6f)), Flat(Mix(lava, Color.white, .1f)));
                c.Glow(cx - 2, gy + 24, 12f, lava, .30f);
            }, true);
            Add(l, "fire.crystal", 80, 108, 8, (c, gy) =>
            {
                float cx = 40; c.SoftShadow(cx + 4, gy + 1, 28f, 6f, .4f, Alpha(shadow, 1f)); c.Glow(cx, gy + 28, 36f, lava, .38f);
                float[,] sp = { { -14, 0, 14, 52, -.15f }, { 14, 0, 12, 44, .20f }, { 0, 2, 17, 84, 0f } };
                foreach (int k in new[] { 0, 1, 2 })
                {
                    float x0 = cx + sp[k, 0], y0 = gy + sp[k, 1], w = sp[k, 2], h = sp[k, 3], lean = sp[k, 4] * h; var tip = new Vector2(x0 + lean, y0 + h);
                    var whole = Poly(new Vector2(x0 - w * .5f, y0), new Vector2(x0 - w * .45f + lean * .4f, y0 + h * .7f), tip, new Vector2(x0 + w * .45f + lean * .4f, y0 + h * .7f), new Vector2(x0 + w * .5f, y0));
                    c.Fill(whole, Split(x0 + lean * .3f, Mix(lava, Color.white, .38f), Mix(lava, Shade(obs, .6f), .45f), 3f), Alpha(Shade(obs, .5f), .95f), 1.5f);
                    c.Fill(Intersect(whole, Capsule(new Vector2(x0 - w * .2f, y0 + 4), new Vector2(x0 - w * .12f + lean * .4f, y0 + h * .6f), .9f, .5f)), Flat(Alpha(Color.white, .7f)));
                }
            });
            Add(l, "fire.vent", 104, 52, 8, (c, gy) =>
            {
                float cx = 52; c.SoftShadow(cx + 4, gy + 1, 40f, 6f, .45f, Alpha(shadow, 1f)); c.Glow(cx, gy + 10, 40f, lava, .35f);
                var mound = Ellipse(cx, gy + 8, 36, 16);
                c.Fill(mound, Toon(mound, rockDark, rockMid, rockLight, 12f, .1f, 6), Ink, 1.4f);
                var slit = Ellipse(cx, gy + 15, 17, 4.5f);
                c.Fill(slit, (x, y, d) => Color.Lerp(Shade(lava, .8f), Mix(lava, Color.white, .55f), Mathf.Clamp01(d / 3f)), Alpha(Shade(obs, .5f), .9f), 1.2f);
                for (int k = 0; k < 5; k++) c.Glow(cx - 12 + k * 6, gy + 24 + (k % 2) * 7, 3.4f, Mix(lava, Color.white, .35f), .8f);
            });
        }

        static bool HasVisiblePigment(Texture2D source)
        {
            if (source == null || !source.isReadable) return false;
            var pixels = source.GetPixels32();
            foreach (var pixel in pixels) if (pixel.a / 255f > EdgeAlpha) return true;
            return false;
        }
    }
}
