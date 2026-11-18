using System.Collections.Generic;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>
    /// Drawing of the live Wish areas (zone, cloud, trail) and of the ghosts of areas that just ended, with the scene lights they emit.
    /// </summary>
    public sealed partial class WishFxPresenter
    {
        // ---------------------------------------------------------------- areas
        void DrawAreas(double now, float t, bool reduced, float dt)
        {
            _areaTracker.BeginFrame();
            bool blazeLive = false;
            for (int i = 0; i < _areas.Count; i++)
            {
                var row = _areas[i]; var f = new WishAreaFacts(row);
                _areaTracker.Observe(f, now);
                switch (f.Kind)
                {
                    case AreaKind.Cloud: DrawCloud(f, now, t, reduced, dt); break;
                    case AreaKind.Zone: DrawZone(f, now, t, reduced, dt); break;
                    case AreaKind.Trail: DrawTrail(f, now, t, reduced, dt); blazeLive = true; break;
                }
            }
            if (blazeLive) _lastBlazeMs = now;
            _ended.Clear(); _areaTracker.EndFrame(_ended);
            for (int i = 0; i < _ended.Count; i++)
            {
                if (_ghostCount == GhostCapacity) { for (int j = 1; j < _ghostCount; j++) _ghosts[j - 1] = _ghosts[j]; _ghostCount--; }
                _ghosts[_ghostCount++] = new Ghost { Ended = _ended[i], At = now };
            }
        }

        void Drawn(in WishAreaFacts f, Vector3 c, Vector3 end, Vector2 size)
        {
            _drawn.Add(new WishDrawnArea { Kind = f.Kind, Ability = f.Ability, Center = c, End = end, Size = size });
        }

        Vector3 CloudCenter(in WishAreaFacts f)
        {
            // A cloud follows its owner. Only the owner's own cloud knows that owner; for everyone else the owner is hidden
            // and the row is anonymous, so the cast origin it was delivered with is the only place there is.
            if (f.Owner != 0 && f.Owner == _owner && Pos(f.Owner, out var p)) return p;
            return W(f.X, f.Y);
        }

        void DrawCloud(in WishAreaFacts f, double now, float t, bool reduced, float dt)
        {
            Color g = Fac(2); Vector3 c = CloudCenter(f);
            float r = f.CircleRadius * S; float el = f.Elapsed(now), rem = 1f - el;
            float born = reduced ? 1f : WishFxModel.EaseOut(WishFxModel.Phase(now, f.StartedAt, 300));
            float fade = Mathf.Clamp01(rem * 8f);
            float pulse = WishFxModel.Pulse(t, 1.6f, reduced);
            WishLight(c + new Vector3(0, .3f, 0), .9f, r * born * 1.3f + 1.4f, Mix(g, Color.white, .2f), (.6f + .15f * pulse) * fade);
            Ring(WishFxArt.Disc, c, r * born, A(Mix(g, Color.black, .45f), (.24f + .06f * pulse) * fade), GroundOrder);
            SafeRing(WishFxArt.Dashed, c, r * born, A(Mix(g, Color.white, .2f), .85f * fade), RingOrder);
            Ring(WishFxArt.Hairline, c, r * born, A(g, .55f * fade), RingOrder + 1);
            Drawn(f, c, c, Ell(r));
            TimeSweep(c, r, el, g, RingOrder + 2, fade, reduced);
            int flies = reduced ? 5 : System.Math.Max(5, WishFxModel.Budget(16, Quality.Particles, false, 16));
            for (int i = 0; i < flies; i++)
            {
                uint seed = (uint)(i * 7919 + 13);
                float h1 = WishFxModel.Hash01(seed), h2 = WishFxModel.Hash01(seed + 1), h3 = WishFxModel.Hash01(seed + 2);
                float orbit = r * (.22f + .62f * h1);
                float ang = h2 * 6.283f + (reduced ? 0f : (float)(t * (.7f + h3 * 1.6f)) * (i % 2 == 0 ? 1f : -1f));
                float bob = reduced ? 0f : Mathf.Sin(t * 5f + h1 * 9f) * .06f;
                Vector3 p = c + new Vector3(Mathf.Cos(ang) * orbit, Mathf.Sin(ang) * orbit * G + .35f + bob, 0);
                Fly(p, ang + 1.57f, .15f + .05f * h3, Mix(g, Color.white, .25f), OrderAt(c, i), fade, t, i);
            }
            if (!reduced) for (int q = EmitCount(ref _acc[0], dt, 6f * Mathf.Min(1.5f, Quality.Particles)); q > 0; q--) _particles.Rise(c + Rand2(r * .85f), new Vector3(0, .3f), .1f, 1.3f, A(Mix(g, Color.white, .15f), .7f));
        }

        int OrderAt(Vector3 c, int i) { return OrderOf(c) + 2 + (i % 3); }

        // ---- scene light (render pass, switch fx.fxlights): every Wish emits real light into the analytic local-light field, in its own colour ----
        static bool LightsOn { get { return AtmosphereQuality.AnalyticLights && AtmosphereQuality.FxLights; } }
        static void WishLight(Vector3 world, float height, float radius, Color col, float intensity)
        {
            if (LightsOn && intensity > .01f) LocalLightField.StageWorld(world, height, radius, col, intensity);
        }
        void CueLight(in Cue c, float p, bool fire)
        {
            var ab = (AbilityId)c.Ability; Color col = Mix(Fac(WishFxModel.FactionIndex(ab)), Color.white, .25f);
            Vector3 pos;
            if (c.Type == CueType.Windup) { if (!Pos(c.Caster, out pos)) return; WishLight(pos, .9f, 2.6f, col, .45f + 1.1f * p); return; }
            if (c.Type == CueType.Landing)
            {
                if (!c.HasAnchor) return;
                pos = W(c.X, c.Y);
                if (fire) LocalLightField.FlashWorld(pos, 1.0f, 5.2f, col, 2.6f, .5f);
                WishLight(pos, .9f, 3.4f, col, 1.1f * (1f - p)); return;
            }
            if (!Pos(c.Target, out pos) && !Pos(c.Caster, out pos)) return;      // shatter, radiant and break end effects sit on an actor the client is drawing
            if (fire) LocalLightField.FlashWorld(pos, .9f, 3.6f, col, 2.1f, .4f);
        }

        void Fly(Vector3 p, float rotRad, float size, Color col, int order, float fade, float t, int i)
        {
            float flap = 1f + .35f * Mathf.Sin(t * 38f + i * 2.1f) * (Reduced ? 0f : 1f);
            _pen.Draw(WishFxArt.Fly, p, new Vector2(size * flap, size), rotRad * Mathf.Rad2Deg, A(col, .95f * fade), order);
        }

        void TimeSweep(Vector3 c, float rWorld, float elapsed, Color col, int order, float fade, bool reduced)
        {
            // A bright bead rides the ring once round over the delivered lifetime; its tail shows how much has gone.
            float head = elapsed * 6.2832f - 1.5708f;
            int tail = reduced ? 0 : 6;
            for (int k = tail; k >= 0; k--)
            {
                float a = head - k * .09f;
                Vector3 p = c + new Vector3(Mathf.Cos(a) * rWorld, Mathf.Sin(a) * rWorld * G, 0);
                float s = k == 0 ? .34f : .2f - k * .02f;
                _pen.Draw(WishFxArt.Soft, p, Vector2.one * s * 1.6f, 0, A(Mix(col, Color.white, .5f), (k == 0 ? .9f : .5f - k * .06f) * fade), order);
                if (k == 0) _pen.Draw(WishFxArt.Cross, p, Vector2.one * s, 0, A(Color.white, .9f * fade), order + 1);
            }
        }

        void DrawZone(in WishAreaFacts f, double now, float t, bool reduced, float dt)
        {
            Color v = Fac(1); Vector3 c = W(f.X, f.Y);
            float full = f.CircleRadius * S;
            float born = reduced ? 1f : WishFxModel.EaseOut(WishFxModel.Phase(now, f.StartedAt, 350));
            float r = full * born; float el = f.Elapsed(now); float fade = Mathf.Clamp01((1f - el) * 8f);
            WishLight(c, .9f, r * 1.3f + 1.2f, Mix(v, Color.white, .25f), .6f * fade);
            Ring(WishFxArt.Disc, c, r, A(Deep(v, 1f), .24f * fade), GroundOrder);
            Ring(WishFxArt.Disc, c, r, A(v, .05f * fade), GroundOrder + 1);
            SafeRing(WishFxArt.Dashed, c, r, A(Mix(v, Color.white, .3f), .95f * fade), RingOrder, .7f);
            Ring(WishFxArt.Hairline, c, r, A(v, .5f * fade), RingOrder + 1);
            Drawn(f, c, c, Ell(r));
            if (born >= 1f) TimeSweep(c, r, el, v, RingOrder + 2, fade, reduced);
            // Caster sigil: the judgment rests on whoever cast it, if that caster is observed.
            if (Pos(f.Owner, out var cp))
                Ring("sigil1", cp, .62f, A(Mix(v, Color.white, .35f), (.35f + .15f * WishFxModel.Pulse(t, 1.8f, reduced)) * fade), GroundOrder + 2);
        }

        void DrawTrail(in WishAreaFacts f, double now, float t, bool reduced, float dt)
        {
            Color fire = Fac(0); Vector3 a = W(f.X, f.Y), b = W(f.X2, f.Y2);
            float el = f.Elapsed(now); float life = Mathf.Clamp01((1f - el) * 6f);
            float band = TrailBandThickness(f);
            DrawTrailBand(a, b, band, f.TrailHalfWidth * S, Deep(fire, 1f), .85f * life, GroundOrder + 4);
            Vector3 d = b - a; float len = d.magnitude;
            if (LightsOn)      // a fire path lights its length: the ends and the middle, flickering
            {
                Color fl = Mix(fire, new Color(1f, .8f, .4f), .4f); float fk = (1.05f + .3f * WishFxModel.Pulse(t, 4.3f, reduced)) * life;
                WishLight(Vector3.Lerp(a, b, .0f) + new Vector3(0, .25f, 0), .6f, 2.8f, fl, fk); WishLight(Vector3.Lerp(a, b, .5f) + new Vector3(0, .25f, 0), .6f, 2.8f + len * .3f, fl, fk);
                WishLight(Vector3.Lerp(a, b, 1f) + new Vector3(0, .25f, 0), .6f, 2.8f, fl, fk);
            }
            Drawn(f, (a + b) * .5f, b, new Vector2(len, band));
            if (len < .01f) return;
            Line(WishFxArt.Beam, a, b, band * 1.5f, A(Mix(fire, new Color(1f, .85f, .4f), .2f), (.6f + .2f * WishFxModel.Pulse(t, .5f, reduced)) * life), GroundOrder + 6);
            int tongues = Mathf.Clamp(Mathf.CeilToInt(len / (band * 1.1f)), 2, 14);
            for (int i = 0; i < tongues; i++)
            {
                float u = (i + .5f) / tongues; uint seed = (uint)(i * 31 + (int)f.StartedAt);
                float h = WishFxModel.Hash01(seed);
                float flick = reduced ? .85f : .75f + .25f * Mathf.Sin(t * (9f + h * 5f) + h * 17f);
                Vector3 p = Vector3.Lerp(a, b, u) + new Vector3((h - .5f) * band * .5f, .02f, 0);
                float hgt = (.55f + .4f * h) * flick * (.5f + .5f * life);
                _pen.Draw(WishFxArt.Flame, p + new Vector3(0, hgt * .5f, 0), new Vector2(band * 1.6f, hgt * 1.25f), 0,
                    A(Mix(fire, new Color(1f, .9f, .5f), .25f + .3f * h), .85f * life), OrderOf(p) + 1);
                _pen.Draw(WishFxArt.Flame, p + new Vector3(0, hgt * .32f, 0), new Vector2(band * .6f, hgt * .6f), 0,
                    A(new Color(1f, .95f, .7f), .8f * life), OrderOf(p) + 2);
            }
            if (!reduced) for (int q = EmitCount(ref _acc[1], dt, Mathf.Min(24f, len * 3f) * Mathf.Min(1.5f, Quality.Particles)); q > 0; q--) _particles.Rise(Vector3.Lerp(a, b, Random01(ref _rng)) + new Vector3(0, .1f, 0), new Vector3((Random01(ref _rng) - .5f) * .3f, .5f + Random01(ref _rng) * .5f), .09f, 1.1f, A(Mix(fire, new Color(1f, .8f, .3f), .5f), .9f));
        }

        // The band's world thickness: the perpendicular distance between the two edges the server's half-width describes,
        // measured after the ground squash so the drawn band really covers 2 x half-width of floor.
        static float TrailBandThickness(in WishAreaFacts f)
        {
            float dx = f.X2 - f.X, dy = f.Y2 - f.Y, len = Mathf.Sqrt(dx * dx + dy * dy);
            float hw = f.TrailHalfWidth;
            if (len < .001f) return hw * 2f * S;
            float nx = -dy / len * hw, ny = dx / len * hw;               // server-space edge offset
            Vector2 o = new Vector2(nx * S, -ny * S * G);                // as drawn
            Vector2 wd = new Vector2(dx * S, -dy * S * G).normalized;    // drawn direction
            return Mathf.Abs(wd.x * o.y - wd.y * o.x) * 2f;
        }

        void DrawTrailBand(Vector3 a, Vector3 b, float thickness, float capRadiusWorldX, Color col, float alpha, int order)
        {
            _pen.Draw(WishFxArt.Solid, (a + b) * .5f, new Vector2((b - a).magnitude, thickness), Mathf.Atan2((b - a).y, (b - a).x) * Mathf.Rad2Deg, A(col, alpha), order);
            Vector2 cap = new Vector2(capRadiusWorldX * 2f, capRadiusWorldX * 2f * G);
            _pen.Draw(WishFxArt.Disc, a, cap, 0, A(col, alpha), order); _pen.Draw(WishFxArt.Disc, b, cap, 0, A(col, alpha), order);
        }

        // ---------------------------------------------------------------- ended areas
        void DrawGhosts(double now, float t, bool reduced)
        {
            for (int i = _ghostCount - 1; i >= 0; i--)
            {
                var g = _ghosts[i]; var f = g.Ended.Facts; double age = now - g.At;
                double life = f.Kind == AreaKind.Trail ? TrailScorchMs : f.Kind == AreaKind.Zone ? 600 : 650;
                if (age >= life || age < 0) { if (age >= life) RemoveGhost(i); continue; }
                float p = (float)(age / life);
                if (f.Kind == AreaKind.Trail)
                {
                    Vector3 a = W(f.X, f.Y), b = W(f.X2, f.Y2);
                    DrawTrailBand(a, b, TrailBandThickness(f), f.TrailHalfWidth * S, Deep(Fac(0), 1f), .8f * (1f - p), GroundOrder + 4);
                    continue;
                }
                if (f.Kind == AreaKind.Zone)
                {
                    Vector3 c = W(f.X, f.Y); float r = f.CircleRadius * S; Color v = Fac(1);
                    if (g.Ended.Natural || reduced)
                        SafeRing(WishFxArt.Dashed, c, r * (reduced ? 1f : 1f - .55f * WishFxModel.EaseOut(p)), A(Mix(v, Color.white, .3f), .9f * (1f - p)), RingOrder, .7f);
                    else
                    {
                        // The judged left the ring: it breaks into its dashes, which fly outward and fade.
                        int n = 18;
                        for (int k = 0; k < n; k++)
                        {
                            float ang = k * 6.2832f / n; float push = 1f + .22f * WishFxModel.EaseOut(p);
                            Vector3 q = c + new Vector3(Mathf.Cos(ang) * r * push, Mathf.Sin(ang) * r * push * G, 0);
                            _pen.Draw(WishFxArt.Shard, q, new Vector2(.14f, .55f), ang * Mathf.Rad2Deg - 90f, A(Mix(v, Color.white, .4f), .9f * (1f - p)), RingOrder + 1);
                        }
                    }
                    continue;
                }
                // Cloud: the swarm disperses outward from where it last stood.
                Vector3 cc = W(f.X, f.Y); if (f.Owner != 0 && f.Owner == _owner && Pos(f.Owner, out var op)) cc = op;
                float rr = f.CircleRadius * S; Color gcol = Fac(2);
                Ring(WishFxArt.Hairline, cc, rr * (1f + .25f * p), A(gcol, .5f * (1f - p)), RingOrder);
                int fl = reduced ? 0 : 10;
                for (int k = 0; k < fl; k++)
                {
                    float h = WishFxModel.Hash01((uint)(k * 53 + 7)); float ang = h * 6.2832f;
                    float dist = rr * (.3f + 1.4f * WishFxModel.EaseOut(p)) * (.6f + .4f * WishFxModel.Hash01((uint)k + 99));
                    Fly(cc + new Vector3(Mathf.Cos(ang) * dist, Mathf.Sin(ang) * dist * G + .35f, 0), ang, .16f, Mix(gcol, Color.white, .25f), OrderOf(cc) + 3, 1f - p, t, k);
                }
            }
        }
        void RemoveGhost(int i) { for (int j = i + 1; j < _ghostCount; j++) _ghosts[j - 1] = _ghosts[j]; _ghostCount--; }
    }
}
