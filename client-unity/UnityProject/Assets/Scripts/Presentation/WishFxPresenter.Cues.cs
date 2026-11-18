using System.Collections.Generic;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>
    /// Drawing of the one-shot Wish cues: cast windup telegraph, landing, shatter, radiant and break.
    /// </summary>
    public sealed partial class WishFxPresenter
    {
        // ---------------------------------------------------------------- cues
        void DrawCues(double now, float t, bool reduced)
        {
            for (int i = _cueCount - 1; i >= 0; i--)
            {
                var c = _cues[i]; double age = now - c.Start;
                if (age >= c.Dur) { RemoveCue(i); continue; }
                if (age < 0) continue;
                float p = (float)(age / c.Dur);
                bool fire = !c.Fired; if (fire) _cues[i].Fired = true;
                switch (c.Type)
                {
                    case CueType.Windup: DrawWindup(c, p, t, reduced); break;
                    case CueType.Landing: DrawLanding(c, p, t, reduced, fire); break;
                    case CueType.Shatter: DrawShatter(c, p, reduced, fire); break;
                    case CueType.Radiant: DrawRadiant(c, p, reduced, fire); break;
                    case CueType.Break: DrawBreak(c, p, reduced, fire); break;
                }
                if (LightsOn) CueLight(c, p, fire);
            }
        }

        void DrawWindup(in Cue c, float p, float t, bool reduced)
        {
            if (!Pos(c.Caster, out var P)) return;
            var ab = (AbilityId)c.Ability; int fi = WishFxModel.FactionIndex(ab); Color col = Fac(fi);
            int order = OrderOf(P);
            float e = reduced ? 1f : WishFxModel.EaseOut(p);
            Vector3 T = P; bool hasT = false;
            if (c.Target != 0 && Pos(c.Target, out var tp)) { T = tp; hasT = true; }
            else if (c.HasAnchor) { T = W(c.X, c.Y); hasT = true; }
            switch (ab)
            {
                case AbilityId.Freeze:
                {
                    float rr = Mathf.Lerp(1.9f, .62f, e);
                    SafeRing(WishFxArt.Band, P, rr, A(Mix(col, Color.white, .15f), .25f + .45f * p), RingOrder);
                    SafeRing(WishFxArt.Hairline, P, .62f, A(Color.white, .5f * p), RingOrder + 1);
                    if (hasT && FrostWish != FrostWishKind.Wall)
                    {
                        Vector3 up = new Vector3(0, .75f, 0);
                        Vector3 head = Vector3.Lerp(P, T, reduced ? 1f : WishFxModel.EaseOut(Mathf.Min(1f, p * 1.7f)));
                        Line(WishFxArt.Beam, P + up, head + up, .5f * (.4f + .6f * p), A(Deep(col, 1f), .45f), order + 1);
                        Line(WishFxArt.Beam, P + up, head + up, .3f * (.4f + .6f * p), A(Mix(col, Color.white, .55f), .9f), order + 2);
                        Ring(WishFxArt.Band, T, Mathf.Lerp(1.5f, .7f, e), A(Mix(col, Color.white, .3f), .4f + .4f * p), RingOrder);
                        _pen.Draw(WishFxArt.Cross, head + up, Vector2.one * (.35f + .2f * p), 0, A(Color.white, .9f), order + 3);
                    }
                    break;
                }
                case AbilityId.Swarm:
                {
                    SafeRing(WishFxArt.Band, P, Mathf.Lerp(1.8f, .55f, e), A(Mix(col, Color.white, .1f), .22f + .4f * p), RingOrder);
                    int n = reduced ? 6 : 16;
                    for (int i = 0; i < n; i++)
                    {
                        float h1 = WishFxModel.Hash01((uint)(i * 17 + 5)), h2 = WishFxModel.Hash01((uint)(i * 17 + 6));
                        float ang = h1 * 6.2832f + (reduced ? 0f : p * 4f);
                        float dist = Mathf.Lerp(2.1f, .35f, reduced ? .5f : WishFxModel.EaseIn(p)) * (.55f + .45f * h2);
                        Fly(P + new Vector3(Mathf.Cos(ang) * dist, Mathf.Sin(ang) * dist * G + .55f, 0), ang + 1.57f, .17f, Mix(col, Color.white, .25f), order + 3, .5f + .5f * p, t, i);
                    }
                    break;
                }
                case AbilityId.Trial:
                {
                    SafeRing(WishFxArt.Band, P, Mathf.Lerp(1.8f, .6f, e), A(Mix(col, Color.white, .2f), .25f + .4f * p), RingOrder);
                    if (hasT)
                    {
                        // Chains pay out from the caster toward the judged; the lock-on ring narrows. No area is shown yet:
                        // the ring's radius is only delivered with the zone itself, on landing.
                        int links = Mathf.Clamp(Mathf.CeilToInt((T - P).magnitude / .42f), 2, 16);
                        float reach = reduced ? 1f : WishFxModel.EaseOut(Mathf.Min(1f, p * 1.6f));
                        Vector3 up = new Vector3(0, .6f, 0);
                        for (int k = 0; k < links; k++)
                        {
                            float u = (k + .5f) / links; if (u > reach) break;
                            Vector3 q = Vector3.Lerp(P, T, u) + up; Vector3 dd = T - P;
                            float rot = Mathf.Atan2(dd.y, dd.x) * Mathf.Rad2Deg + (k % 2 == 0 ? 0f : 90f);
                            _pen.Draw(WishFxArt.Chain, q, new Vector2(.36f, .22f), rot, A(Deep(col, 1f), .6f), order + 1);
                            _pen.Draw(WishFxArt.Chain, q, new Vector2(.3f, .18f), rot, A(Mix(col, Color.white, .45f), .95f), order + 2);
                        }
                        SafeRing(WishFxArt.Dashed, T, Mathf.Lerp(1.7f, .75f, e), A(Mix(col, Color.white, .3f), .9f), RingOrder + 1);
                        float swing = Mathf.Lerp(-55f, 8f, WishFxModel.EaseIn(p));
                        Vector3 gp = T + new Vector3(.95f, 1.6f + (reduced ? 0f : .25f * (1f - p)), 0);
                        _pen.Draw(WishFxArt.Gavel, gp, Vector2.one * .95f, swing, A(Deep(col, 1f), .7f), order + 2);
                        _pen.Draw(WishFxArt.Gavel, gp, Vector2.one * .8f, swing, A(Mix(col, Color.white, .55f), .95f), order + 3);
                    }
                    break;
                }
            }
        }

        void DrawLanding(in Cue c, float p, float t, bool reduced, bool fire)
        {
            var ab = (AbilityId)c.Ability; Color col = Fac(WishFxModel.FactionIndex(ab)); Vector3 A0 = W(c.X, c.Y);
            float e = reduced ? 1f : WishFxModel.EaseOut(p); float fade = 1f - p;
            int order = BurstOrder;
            int pn = WishFxModel.Budget(14, Quality.Particles, reduced, 28);
            switch (ab)
            {
                case AbilityId.Freeze:
                {
                    SafeRing(WishFxArt.Band, A0, Mathf.Lerp(.3f, 1.45f, e), A(Mix(col, Color.white, .4f), .85f * fade), order);
                    _pen.Draw(WishFxArt.Soft, A0 + new Vector3(0, .6f, 0), Vector2.one * 2.6f * (.6f + .4f * e), 0, A(col, .6f * fade), order - 1);
                    _pen.Draw(WishFxArt.Cross, A0 + new Vector3(0, .6f, 0), Vector2.one * (1.1f + .5f * e), 45f * e, A(Color.white, .9f * fade), order + 1);
                    if (fire) for (int i = 0; i < pn; i++) { float a = i * 6.2832f / pn + Random01(ref _rng); _particles.Fall(A0 + new Vector3(0, .5f, 0), new Vector3(Mathf.Cos(a), Mathf.Sin(a) * .7f + .4f) * (1.4f + Random01(ref _rng)), .16f, .7f, A(Mix(col, Color.white, .5f), .95f), a * Mathf.Rad2Deg); }
                    break;
                }
                case AbilityId.Swarm:
                {
                    SafeRing(WishFxArt.Band, A0, Mathf.Lerp(.25f, 1.25f, e), A(Mix(col, Color.white, .15f), .7f * fade), order);
                    _pen.Draw(WishFxArt.Soft, A0 + new Vector3(0, .5f, 0), Vector2.one * 2.2f, 0, A(Deep(col, 1f), .4f * fade), order - 1);
                    if (fire) for (int i = 0; i < pn; i++) { float a = i * 6.2832f / pn; _particles.Float(A0 + new Vector3(0, .5f, 0), new Vector3(Mathf.Cos(a), Mathf.Sin(a) * .7f) * (.8f + Random01(ref _rng)), .13f, .9f, A(Mix(col, Color.white, .2f), .85f)); }
                    break;
                }
                case AbilityId.Trial:
                {
                    // The gavel comes down: a violet flash and shock ring at the judged, then the zone ring (an area row)
                    // takes over and carries the true extent.
                    SafeRing(WishFxArt.Band, A0, Mathf.Lerp(.35f, 1.9f, e), A(Mix(col, Color.white, .35f), .9f * fade), order);
                    _pen.Draw(WishFxArt.Soft, A0 + new Vector3(0, .4f, 0), Vector2.one * 3.1f * (.7f + .3f * e), 0, A(col, .45f * fade * fade), order - 1);
                    Vector3 gp = A0 + new Vector3(.95f, 1.45f, 0);
                    _pen.Draw(WishFxArt.Gavel, gp, Vector2.one * .95f, Mathf.Lerp(8f, -4f, e), A(Deep(col, 1f), .7f * fade), order + 1);
                    _pen.Draw(WishFxArt.Gavel, gp, Vector2.one * .8f, Mathf.Lerp(8f, -4f, e), A(Mix(col, Color.white, .6f), .95f * fade), order + 2);
                    _pen.Draw(WishFxArt.Scales, A0 + new Vector3(-.95f, 1.55f + .1f * e, 0), Vector2.one * .8f, 0, A(Mix(col, Color.white, .55f), .95f * fade), order + 2);
                    _pen.Draw(WishFxArt.Cross, A0 + new Vector3(0, .4f, 0), Vector2.one * (.9f + .4f * e), 0, A(Color.white, .85f * fade), order + 3);
                    if (fire) for (int i = 0; i < pn; i++) { float a = i * 6.2832f / pn; _particles.Spark(A0 + new Vector3(0, .4f, 0), new Vector3(Mathf.Cos(a), Mathf.Sin(a) * .7f) * (1.6f + Random01(ref _rng)), .17f, .6f, A(Mix(col, Color.white, .5f), .95f)); }
                    break;
                }
                case AbilityId.Blaze:
                {
                    Vector3 D = W(c.X2, c.Y2);
                    float streak = Mathf.Clamp01(1f - p * 1.8f);
                    Line(WishFxArt.Beam, A0 + new Vector3(0, .5f, 0), D + new Vector3(0, .5f, 0), .85f * streak, A(Deep(col, 1f), .45f * streak), order - 1);
                    Line(WishFxArt.Beam, A0 + new Vector3(0, .5f, 0), D + new Vector3(0, .5f, 0), .5f * streak, A(Mix(col, new Color(1f, .85f, .45f), .55f), .95f * streak), order);
                    Line(WishFxArt.Beam, A0 + new Vector3(0, .5f, 0), D + new Vector3(0, .5f, 0), .18f * streak, A(Color.white, .95f * streak), order + 1);
                    SafeRing(WishFxArt.Band, A0, Mathf.Lerp(.2f, .95f, e), A(Mix(col, Color.white, .3f), .75f * fade), order);
                    SafeRing(WishFxArt.Band, D, Mathf.Lerp(.25f, 1.75f, e), A(Mix(col, Color.white, .25f), .85f * fade), order);
                    _pen.Draw(WishFxArt.Soft, D + new Vector3(0, .5f, 0), Vector2.one * 3f * (.6f + .4f * e), 0, A(col, .42f * fade * fade), order - 2);
                    _pen.Draw(WishFxArt.Cross, D + new Vector3(0, .5f, 0), Vector2.one * (1.0f + .6f * e), 0, A(Color.white, .85f * fade), order + 2);
                    _pen.Draw(WishFxArt.Cross, A0 + new Vector3(0, .5f, 0), Vector2.one * (.7f + .3f * e), 0, A(Color.white, .75f * fade), order + 2);
                    if (fire)
                    {
                        for (int i = 0; i < pn; i++) _particles.Rise(Vector3.Lerp(A0, D, Random01(ref _rng)) + new Vector3(0, .5f, 0), new Vector3((Random01(ref _rng) - .5f) * 1.2f, .4f + Random01(ref _rng) * .9f), .11f, .9f, A(Mix(col, new Color(1f, .8f, .3f), .5f), .95f));
                        for (int i = 0; i < pn; i++) { float a = i * 6.2832f / pn; _particles.Rise(D + new Vector3(0, .5f, 0), new Vector3(Mathf.Cos(a), Mathf.Sin(a) * .7f + .5f) * (1.2f + Random01(ref _rng)), .12f, .8f, A(new Color(1f, .75f, .3f), .95f)); }
                    }
                    break;
                }
                case AbilityId.LastStand:
                {
                    SafeRing(WishFxArt.Band, A0, Mathf.Lerp(.3f, 2.1f, e), A(Mix(col, Color.white, .35f), .85f * fade), order);
                    _pen.Draw(WishFxArt.Rays, A0 + new Vector3(0, .7f, 0), new Vector2(3.4f, 3.4f * G) * (.6f + .5f * e), reduced ? 0f : 18f * p, A(Mix(col, Color.white, .45f), .55f * fade), order - 1);
                    _pen.Draw(WishFxArt.Column, A0 + new Vector3(0, 2.2f, 0), new Vector2(1.7f * (1.2f - .3f * e), 4.8f), 0, A(col, .55f * fade), order - 2);
                    _pen.Draw(WishFxArt.Cross, A0 + new Vector3(0, .8f, 0), Vector2.one * (1.1f + .6f * e), 0, A(Color.white, .8f * fade), order + 1);
                    if (fire) for (int i = 0; i < pn; i++) { float a = i * 6.2832f / pn; _particles.Spark(A0 + new Vector3(0, .6f, 0), new Vector3(Mathf.Cos(a) * 1.2f, .5f + Random01(ref _rng) * 1.2f), .17f, .9f, A(Mix(col, Color.white, .35f), .95f)); }
                    break;
                }
            }
        }

        void DrawShatter(in Cue c, float p, bool reduced, bool fire)
        {
            Color col = Fac(3); float fade = 1f - p; float e = reduced ? 1f : WishFxModel.EaseOut(p);
            SafeRing(WishFxArt.Band, c.Pos, Mathf.Lerp(.3f, 1.3f, e), A(Mix(col, Color.white, .5f), .8f * fade), BurstOrder);
            _pen.Draw(WishFxArt.Cross, c.Pos + new Vector3(0, .8f, 0), Vector2.one * (1f + .5f * e), 30f * e, A(Color.white, .9f * fade), BurstOrder + 1);
            int n = reduced ? 0 : 7;
            for (int i = 0; i < n; i++)
            {
                float a = i * 6.2832f / n + c.Seed * .1f; float d = .35f + 1.1f * e;
                _pen.Draw(WishFxArt.Shard, c.Pos + new Vector3(Mathf.Cos(a) * d, .6f + Mathf.Sin(a) * d * .6f - .4f * p * p, 0), new Vector2(.2f, .5f), a * Mathf.Rad2Deg - 90f + 140f * p, A(Mix(col, Color.white, .6f), .95f * fade), BurstOrder + 2);
            }
            if (fire) { int pn = WishFxModel.Budget(16, Quality.Particles, reduced, 28); for (int i = 0; i < pn; i++) { float a = i * 6.2832f / pn; _particles.Fall(c.Pos + new Vector3(0, .7f, 0), new Vector3(Mathf.Cos(a), Mathf.Sin(a) * .8f + .5f) * (1.5f + Random01(ref _rng) * 1.2f), .15f, .8f, A(Mix(col, Color.white, .55f), .95f), a * Mathf.Rad2Deg); } }
        }

        void DrawRadiant(in Cue c, float p, bool reduced, bool fire)
        {
            Color col = Fac(4); float fade = 1f - p; float e = reduced ? 1f : WishFxModel.EaseOut(p);
            SafeRing(WishFxArt.Band, c.Pos, Mathf.Lerp(.4f, 2.4f, e), A(Mix(col, Color.white, .4f), .8f * fade), BurstOrder);
            _pen.Draw(WishFxArt.Rays, c.Pos + new Vector3(0, .8f, 0), new Vector2(3.8f, 3.8f * G) * (.6f + .4f * e), reduced ? 0f : 25f * p, A(Mix(col, Color.white, .5f), .5f * fade), BurstOrder - 1);
            _pen.Draw(WishFxArt.Cross, c.Pos + new Vector3(0, .9f, 0), Vector2.one * (1.2f + .7f * e), 0, A(Color.white, .85f * fade), BurstOrder + 1);
            if (fire) { int pn = WishFxModel.Budget(20, Quality.Particles, reduced, 30); for (int i = 0; i < pn; i++) { float a = i * 6.2832f / pn; _particles.Spark(c.Pos + new Vector3(0, .8f, 0), new Vector3(Mathf.Cos(a) * 1.6f, Mathf.Sin(a) * 1f + .4f), .16f, .9f, A(Mix(col, Color.white, .4f), .95f)); } }
        }

        void DrawBreak(in Cue c, float p, bool reduced, bool fire)
        {
            Color col = Fac(1); float fade = 1f - p; float e = reduced ? 1f : WishFxModel.EaseOut(p);
            _pen.Draw(WishFxArt.Cross, c.Pos + new Vector3(0, .9f, 0), Vector2.one * (.9f + .5f * e), 0, A(Color.white, .9f * fade), BurstOrder + 1);
            int n = reduced ? 0 : 8;
            for (int i = 0; i < n; i++)
            {
                float a = i * 6.2832f / n + c.Seed * .07f; float d = .3f + .95f * e;
                _pen.Draw(WishFxArt.Chain, c.Pos + new Vector3(Mathf.Cos(a) * d, .9f + Mathf.Sin(a) * d * .7f - .5f * p * p, 0), new Vector2(.3f, .18f), a * Mathf.Rad2Deg + 200f * p, A(Mix(col, Color.white, .5f), .95f * fade), BurstOrder + 2);
            }
            if (fire) { int pn = WishFxModel.Budget(10, Quality.Particles, reduced, 20); for (int i = 0; i < pn; i++) { float a = i * 6.2832f / pn; _particles.Spark(c.Pos + new Vector3(0, .8f, 0), new Vector3(Mathf.Cos(a), Mathf.Sin(a)) * 1.3f, .14f, .6f, A(Mix(col, Color.white, .5f), .9f)); } }
        }
    }
}
