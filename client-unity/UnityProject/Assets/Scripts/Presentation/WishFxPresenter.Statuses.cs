using System.Collections.Generic;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>
    /// Drawing of the status looks on observed actors (stun, judged, holy, speed streaks, hidden self, burning) from the status-edge tracker.
    /// </summary>
    public sealed partial class WishFxPresenter
    {
        // ---------------------------------------------------------------- statuses
        void DrawStatuses(double now, float t, bool reduced, float dt)
        {
            _statusTracker.BeginFrame();
            bool blazeRecent = now - _lastBlazeMs < TrailScorchMs;
            for (int i = 0; i < _noteCount; i++)
            {
                var n = _notes[i];
                if (n.Kind == ActorKind.Player && _statusTracker.Note(n.Id, n.Effects, n.Alive, out var edge)) OnEdge(edge, now);
                if (!n.Alive || n.Effects == 0 || !Pos(n.Id, out var P)) continue;
                int order = OrderOf(P); uint seed = n.Id * 2654435761u;
                if ((n.Effects & WishEffectBits.Stunned) != 0) DrawStun(P, order, seed, now, t, reduced, dt, n.Id);
                if ((n.Effects & WishEffectBits.Zoned) != 0) DrawJudged(P, order, seed, t, reduced, n.Id);
                if ((n.Effects & WishEffectBits.LastStand) != 0) DrawHoly(P, order, seed, t, reduced, dt, n);
                if ((n.Effects & WishEffectBits.Hidden) != 0 && n.Id == _owner) DrawHiddenSelf(P, order, seed, t, reduced, dt, n.Id);
                if ((n.Effects & WishEffectBits.Dot) != 0 && blazeRecent) DrawBurning(P, order, seed, t, reduced, dt);
            }
            _statusTracker.EndFrame();
        }

        void OnEdge(in WishStatusEdge e, double now)
        {
            if (!Pos(e.Id, out var P)) return;
            if ((e.Ended & WishEffectBits.Stunned) != 0 && IsIceStun(P)) AddCue(new Cue { Type = CueType.Shatter, Start = now, Dur = 650, Pos = P, Seed = e.Id });
            if ((e.Ended & WishEffectBits.LastStand) != 0) AddCue(new Cue { Type = CueType.Radiant, Start = now, Dur = 800, Pos = P, Seed = e.Id });
            if ((e.Ended & WishEffectBits.Zoned) != 0) AddCue(new Cue { Type = CueType.Break, Start = now, Dur = 550, Pos = P, Seed = e.Id });
        }

        bool IsIceStun(Vector3 p)
        {
            // Styled as ice when a Freeze landing was delivered at this spot a moment ago; any other stun is plain.
            if (_smoothMs - _freezeAt > 2200) return false;
            return (p - W(_freezeX, _freezeY)).sqrMagnitude < 1f;
        }

        void DrawStun(Vector3 P, int order, uint seed, double now, float t, bool reduced, float dt, uint id)
        {
            bool ice = IsIceStun(P);
            Color ic = Fac(3);
            float formed = ice ? (reduced ? 1f : WishFxModel.EaseOut(WishFxModel.Phase(now, _freezeAt, 250))) : 0f;
            if (ice)
            {
                _actors.SetWishTint(id, ic, .6f * formed);
                SafeRing(WishFxArt.Band, P, 1.0f * (.7f + .3f * formed), A(Mix(ic, Color.white, .15f), .9f * formed), RingOrder);
                for (int k = 0; k < 7; k++)
                {
                    float h = WishFxModel.Hash01(seed + (uint)k * 7);
                    float ang = (k / 7f + h * .06f) * 6.2832f; float sy = Mathf.Sin(ang);
                    float height = (.85f + .6f * h) * formed;
                    Vector3 b = P + new Vector3(Mathf.Cos(ang) * .62f, sy * .2f + .08f, 0);
                    float lean = Mathf.Cos(ang) * -18f;
                    int o = sy < 0 ? order + 1 : order - 2;
                    _pen.Draw(WishFxArt.Shard, b + new Vector3(0, height * .5f, 0), new Vector2(.58f, height * 1.1f), lean, A(Deep(ic, 1f), .85f), o - 1);
                    _pen.Draw(WishFxArt.Shard, b + new Vector3(0, height * .5f, 0), new Vector2(.46f, height), lean, A(Mix(ic, Color.white, .3f), .97f), o);
                }
                // An ice cap over the shoulders reads as the frozen pose.
                _pen.Draw(WishFxArt.Soft, P + new Vector3(0, 1f, 0), new Vector2(1.5f, 2.1f), 0, A(Mix(ic, Color.white, .3f), .22f * formed), order + 1);
            }
            else _actors.SetWishTint(id, Mix(Fac(4), Color.white, .4f), .08f);
            // Stars circle over the head (above the bars) while the stun lasts, ice or not.
            for (int k = 0; k < 3; k++)
            {
                float a = (reduced ? 0f : t * 2.2f) + k * 2.094f;
                Vector3 sp = P + new Vector3(Mathf.Cos(a) * .55f, 3.15f + Mathf.Sin(a) * .1f, 0);
                _pen.Draw(WishFxArt.Star, sp, Vector2.one * .42f, reduced ? 0f : t * 90f, A(Deep(ic, 1f), .9f), order + 12);
                _pen.Draw(WishFxArt.Star, sp, Vector2.one * .32f, reduced ? 0f : t * 90f, A(new Color(1f, .98f, .8f), .98f), order + 13);
            }
            if (ice && !reduced) for (int q = EmitCount(ref _acc[2], dt, 5f * Mathf.Min(1.5f, Quality.Particles)); q > 0; q--) _particles.Fall(P + new Vector3((Random01(ref _rng) - .5f) * 1.1f, .4f + Random01(ref _rng) * 1.2f, 0), new Vector3(0, -.15f, 0), .1f, 1.1f, A(Mix(ic, Color.white, .7f), .95f), 0);
        }

        void DrawJudged(Vector3 P, int order, uint seed, float t, bool reduced, uint id)
        {
            Color v = Fac(1);
            _actors.SetWishTint(id, v, .16f);
            float pulse = WishFxModel.Pulse(t, 2f, reduced);
            SafeRing(WishFxArt.Dashed, P, .8f, A(Mix(v, Color.white, .35f), .9f), RingOrder + 2, .7f);
            Ring("sigil1", P, .5f, A(Mix(v, Color.white, .4f), .55f + .2f * pulse), RingOrder + 3);
            // Two chains cross the torso.
            for (int s = 0; s < 2; s++)
            {
                Vector3 a = P + new Vector3(s == 0 ? -.5f : .5f, 1.75f, 0), b = P + new Vector3(s == 0 ? .5f : -.5f, .3f, 0);
                for (int k = 0; k < 5; k++)
                {
                    Vector3 q = Vector3.Lerp(a, b, (k + .5f) / 5f); Vector3 d = b - a;
                    float rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg + (k % 2 == 0 ? 0f : 90f);
                    _pen.Draw(WishFxArt.Chain, q, new Vector2(.3f, .19f), rot, A(Deep(v, 1f), .65f), order);
                    _pen.Draw(WishFxArt.Chain, q, new Vector2(.25f, .15f), rot, A(Mix(v, Color.white, .5f), .95f), order + 1);
                }
            }
            Vector3 mp = P + new Vector3(1.05f, 1.9f + (reduced ? 0f : .06f * Mathf.Sin(t * 2f)), 0);
            _pen.Draw(WishFxArt.Scales, mp, Vector2.one * .56f, 0, A(Deep(v, 1f), .75f), order + 9);
            _pen.Draw(WishFxArt.Scales, mp, Vector2.one * .46f, 0, A(Mix(v, Color.white, .55f), .98f), order + 10);
        }

        void DrawHoly(Vector3 P, int order, uint seed, float t, bool reduced, float dt, in ActorNote n)
        {
            Color h = Fac(4); float pulse = WishFxModel.Pulse(t + seed * .001f, 1.0f, reduced);
            _actors.SetWishTint(n.Id, Mix(h, Color.white, .5f), .14f + .06f * pulse);
            // Behind the body: a column of light, a bubble and its rim (the shield of light).
            _pen.Draw(WishFxArt.Column, P + new Vector3(0, 2.1f, 0), new Vector2(1.75f, 4.4f), 0, A(Deep(h, 1f), .3f), order - 5);
            _pen.Draw(WishFxArt.Column, P + new Vector3(0, 2.1f, 0), new Vector2(1.55f, 4.4f), 0, A(h, .55f + .15f * pulse), order - 4);
            _pen.Draw(WishFxArt.Disc, P + new Vector3(0, 1f, 0), new Vector2(2.0f, 2.35f), 0, A(h, .34f + .08f * pulse), order - 3);
            _pen.Draw(WishFxArt.Hairline, P + new Vector3(0, 1f, 0), new Vector2(2.06f, 2.42f), 0, A(Deep(h, 1f), .8f), order - 3);
            _pen.Draw(WishFxArt.Hairline, P + new Vector3(0, 1f, 0), new Vector2(2.0f, 2.35f), 0, A(Mix(h, Color.white, .35f), 1f), order - 2);
            // In front: a thin rim at the feet and the halo above the bars.
            SafeRing(WishFxArt.Band, P, .95f, A(h, .75f + .2f * pulse), RingOrder + 4);
            Vector3 hp = P + new Vector3(0, 3.35f, 0);
            _pen.Draw(WishFxArt.Hairline, hp, new Vector2(1.4f, .46f), 0, A(Deep(h, 1f), .9f), order + 11);
            _pen.Draw(WishFxArt.Hairline, hp, new Vector2(1.15f, .36f), 0, A(Mix(h, Color.white, .6f), .95f), order + 12);
            _pen.Draw(WishFxArt.Soft, hp, new Vector2(2.2f, 1f), 0, A(h, .55f + .15f * pulse), order + 11);
            if ((n.Effects & WishEffectBits.Haste) != 0 && n.Kind == ActorKind.Player && !reduced) DrawSpeedStreaks(P, order, n.Id, h, dt);
            if (!reduced) for (int q = EmitCount(ref _acc[3], dt, 7f * Mathf.Min(1.5f, Quality.Particles)); q > 0; q--) _particles.Spark(P + new Vector3((Random01(ref _rng) - .5f) * 1.2f, .2f, 0), new Vector3(0, .5f + Random01(ref _rng) * .5f), .12f, 1.2f, A(Mix(h, Color.white, .4f), .9f));
        }

        void DrawSpeedStreaks(Vector3 P, int order, uint id, Color h, float dt)
        {
            if (_lastPos.TryGetValue(id, out var prev) && dt > .0001f)
            {
                Vector3 d = P - prev; float speed = d.magnitude / dt;
                if (speed > .6f)
                {
                    Vector3 dir = -d.normalized; float len = Mathf.Clamp(speed * .12f, .5f, 1.4f);
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 o = new Vector3(-dir.y, dir.x, 0) * (k - 1) * .22f + new Vector3(0, .35f + k * .22f, 0);
                        Line(WishFxArt.Beam, P + o + dir * .25f, P + o + dir * (.25f + len), .12f, A(Mix(h, Color.white, .6f), .55f - k * .08f), order - 1);
                    }
                }
            }
            _lastPos[id] = P;
        }

        void DrawHiddenSelf(Vector3 P, int order, uint seed, float t, bool reduced, float dt, uint id)
        {
            Color g = Fac(2);
            _actors.SetWishTint(id, g, .22f);
            int n = reduced ? 4 : 8;
            for (int i = 0; i < n; i++)
            {
                float h1 = WishFxModel.Hash01(seed + (uint)i), h2 = WishFxModel.Hash01(seed + (uint)i + 40);
                float ang = h1 * 6.2832f + (reduced ? 0f : t * (1.4f + h2 * 1.8f) * (i % 2 == 0 ? 1f : -1f));
                Vector3 p = P + new Vector3(Mathf.Cos(ang) * (.35f + .35f * h2), .35f + h2 * 1.4f + (reduced ? 0f : .1f * Mathf.Sin(t * 4f + i)), 0);
                Fly(p, ang + 1.57f, .14f, Mix(g, Color.white, .3f), order + 1, .9f, t, i);
            }
            if (!reduced) for (int q = EmitCount(ref _acc[4], dt, 6f * Mathf.Min(1.5f, Quality.Particles)); q > 0; q--) _particles.Float(P + new Vector3((Random01(ref _rng) - .5f) * .9f, .3f + Random01(ref _rng), 0), new Vector3(0, .25f), .1f, 1.0f, A(Mix(g, Color.white, .2f), .75f));
        }

        void DrawBurning(Vector3 P, int order, uint seed, float t, bool reduced, float dt)
        {
            Color f = Fac(0);
            for (int k = 0; k < 3; k++)
            {
                float h = WishFxModel.Hash01(seed + (uint)k * 3);
                float flick = reduced ? .85f : .7f + .3f * Mathf.Sin(t * (10f + h * 4f) + h * 9f);
                Vector3 p = P + new Vector3((k - 1) * .32f, .15f, 0);
                _pen.Draw(WishFxArt.Flame, p + new Vector3(0, .3f * flick, 0), new Vector2(.42f, .6f * flick), 0, A(Mix(f, new Color(1f, .85f, .4f), .3f), .9f), order + 2 + k);
            }
            if (!reduced) for (int q = EmitCount(ref _acc[5], dt, 6f * Mathf.Min(1.5f, Quality.Particles)); q > 0; q--) _particles.Rise(P + new Vector3((Random01(ref _rng) - .5f) * .7f, .6f, 0), new Vector3(0, .6f), .09f, .8f, A(new Color(1f, .75f, .3f), .9f));
        }
    }
}
