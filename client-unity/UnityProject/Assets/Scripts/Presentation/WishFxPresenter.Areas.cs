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
        static readonly float[] TrailFrom = { 0f, .27f, .68f }, TrailTo = { .46f, .81f, 1f },
            TrailWidth = { .64f, .87f, .74f }, TrailSoot = { .42f, .33f, .45f }, TrailHeat = { .65f, .74f, .84f }, TrailLateral = { -.09f, .065f, -.025f };

        // ---------------------------------------------------------------- areas
        void DrawAreas(double now, float t, bool reduced, float dt)
        {
            _areaTracker.BeginFrame();
            for (int i = 0; i < _areas.Count; i++)
            {
                var row = _areas[i]; var f = new WishAreaFacts(row);
                _areaTracker.Observe(f, now);
                switch (f.Kind)
                {
                    case AreaKind.Cloud: DrawCloud(f, now, t, reduced, dt); break;
                    case AreaKind.Zone: DrawZone(f, now, t, reduced, dt); break;
                    case AreaKind.Trail: DrawTrail(f, now, t, reduced, dt); break;
                }
            }
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
                if (ab == AbilityId.Blaze)
                {
                    pos = c.HasDest ? W(c.X2, c.Y2) : W(c.X, c.Y);
                    if (fire) LocalLightField.FlashWorld(pos, .7f, 1.7f, col, 2.0f, .28f);
                    WishLight(pos, .45f, 1.15f, col, .8f * (1f - p));
                    return;
                }
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
            Vector3 d = b - a; float len = d.magnitude;
            DrawInkTrail(a, b, band, life, GroundOrder + 4, t, reduced, (uint)f.StartedAt);
            if (LightsOn && len > .01f) // small lights stay within the delivered painted strip
            {
                Color fl = Mix(fire, new Color(1f, .8f, .4f), .4f); float fk = (1.05f + .3f * WishFxModel.Pulse(t, 4.3f, reduced)) * life;
                float lightRadius = Mathf.Min(band * .42f, len * .16f);
                if (lightRadius > .01f)
                {
                    WishLight(Vector3.Lerp(a, b, .25f), .25f, lightRadius, fl, fk * .65f);
                    WishLight(Vector3.Lerp(a, b, .5f), .25f, lightRadius, fl, fk * .8f);
                    WishLight(Vector3.Lerp(a, b, .75f), .25f, lightRadius, fl, fk * .65f);
                }
            }
            Drawn(f, (a + b) * .5f, b, new Vector2(len, band));
            if (len < .01f) return;
            if (!reduced) for (int q = EmitCount(ref _acc[1], dt, Mathf.Min(8f, len * 1.15f) * Mathf.Min(1.5f, Quality.Particles)); q > 0; q--)
                _particles.Rise(Vector3.Lerp(a, b, Random01(ref _rng)), new Vector3((Random01(ref _rng) - .5f) * .12f, .16f + Random01(ref _rng) * .18f), .055f, .65f, A(Mix(fire, new Color(1f, .8f, .3f), .38f), .55f));
        }

        void DrawInkTrail(Vector3 a, Vector3 b, float thickness, float alpha, int order, float t, bool reduced, uint seed)
        {
            Vector3 delta = b - a; float length = delta.magnitude; if (length < .01f || thickness < .01f) return;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            for (int i = 0; i < 3; i++)
            {
                float lo = TrailFrom[i], hi = TrailTo[i];
                float h = WishFxModel.Hash01(seed + (uint)(i * 41 + 5));
                float rot = angle + (h - .5f) * 5f;
                float radians = angle * Mathf.Deg2Rad;
                Vector2 sideOffset = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians)) * (thickness * TrailLateral[i]);
                Vector3 start = Vector3.Lerp(a, b, lo) + (Vector3)sideOffset;
                Vector3 end = Vector3.Lerp(a, b, hi) + (Vector3)sideOffset;
                Vector3 mid = (start + end) * .5f;
                float span = (end - start).magnitude;
                _pen.Draw(WishFxArt.FireTrail, mid + (Vector3)(sideOffset * .18f), new Vector2(span * 1.02f, thickness * TrailWidth[i]), rot,
                    A(new Color(.19f, .09f, .055f), TrailSoot[i] * alpha), order + i);
                _pen.Draw(WishFxArt.FireTrail, mid, new Vector2(span, thickness * TrailWidth[i] * .72f), rot,
                    A(Mix(new Color(1f, .34f, .08f), new Color(1f, .69f, .18f), .18f + .12f * (reduced ? 0f : Mathf.Sin(t * 4.2f + i))), TrailHeat[i] * alpha), order + 3 + i);
            }
            float angleRad = angle * Mathf.Deg2Rad;
            Vector2 side = new Vector2(-Mathf.Sin(angleRad), Mathf.Cos(angleRad));
            int flecks = length > 5f ? 3 : 2;
            for (int i = 0; i < flecks; i++)
            {
                float h = WishFxModel.Hash01(seed + (uint)(i * 37 + 13));
                float u = Mathf.Lerp(.17f, .84f, (i + .2f + .6f * h) / flecks);
                float spread = (h - .5f) * thickness * .72f;
                Vector3 p = Vector3.Lerp(a, b, u) + (Vector3)(side * spread);
                float size = thickness * (.22f + .1f * h);
                float rotation = angle - 90f + (h - .5f) * 38f;
                _pen.Draw(WishFxArt.FireFleck, p + new Vector3(0, size * .16f, 0), new Vector2(size * .52f, size * 1.35f), rotation,
                    A(Mix(new Color(.63f, .24f, .08f), new Color(1f, .69f, .22f), .2f + .18f * h), .52f * alpha), order + 7);
            }
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
                    DrawInkTrail(a, b, TrailBandThickness(f), 1f - p, GroundOrder + 4, t, reduced, (uint)f.StartedAt);
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
