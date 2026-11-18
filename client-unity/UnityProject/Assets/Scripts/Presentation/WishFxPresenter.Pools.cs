using System.Collections.Generic;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>
    /// The pooled sprite pen and the capped particle systems the Wish presenter draws with.
    /// </summary>
    public sealed partial class WishFxPresenter
    {
        // ---------------------------------------------------------------- pooled sprite pen
        sealed class Pen
        {
            readonly Transform _root; readonly List<SpriteRenderer> _list = new List<SpriteRenderer>(); readonly int _cap;
            int _used, _previous;
            public Pen(Transform parent, int cap) { _root = new GameObject("Wish sprites").transform; _root.SetParent(parent, false); _cap = cap; }
            public int Used { get { return _used; } }
            public int PoolSize { get { return _list.Count; } }
            public void Begin() { _used = 0; }
            public void End() { for (int i = _used; i < _previous && i < _list.Count; i++) _list[i].enabled = false; _previous = _used; }
            public void Draw(string key, Vector3 pos, Vector2 size, float rotDeg, Color color, int order)
            {
                if (color.a <= .004f || _used >= _cap) return;
                SpriteRenderer r;
                if (_used == _list.Count)
                {
                    var go = new GameObject("Wish fx"); go.transform.SetParent(_root, false);
                    r = go.AddComponent<SpriteRenderer>(); ShowcaseSprites.Initialize(); r.sharedMaterial = ShowcaseSprites.Unlit;
                    _list.Add(r);
                }
                else r = _list[_used];
                _used++;
                r.enabled = true;
                r.sprite = key[0] == 's' ? ShowcaseSprites.Get(key) : WishFxArt.Get(key);
                r.color = color; r.sortingOrder = order;
                r.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, 0, rotDeg));
                r.transform.localScale = new Vector3(size.x, size.y, 1f);
            }
        }

        // ---------------------------------------------------------------- capped particle systems
        sealed class Particles
        {
            readonly ParticleSystem _rise, _float, _fall, _spark;
            readonly ParticleSystem[] _all;
            public Particles(Transform parent)
            {
                _rise = Make(parent, "Wish embers and motes (rise)", WishFxArt.Soft, 240, -.12f);
                _float = Make(parent, "Wish motes (drift)", WishFxArt.Soft, 160, 0f);
                _fall = Make(parent, "Wish ice shards", WishFxArt.Shard, 160, .35f);
                _spark = Make(parent, "Wish sparkles", WishFxArt.Cross, 160, 0f);
                _all = new[] { _rise, _float, _fall, _spark };
            }
            public int Capacity { get { int n = 0; foreach (var s in _all) n += s.main.maxParticles; return n; } }
            public int Live { get { int n = 0; foreach (var s in _all) n += s.particleCount; return n; } }
            public void Configure(bool reduced)
            {
                // Reduced motion and the Low preset draw no particles at all; whatever was alive is cleared at once.
                bool off = reduced || Quality.Particles <= 0f;
                foreach (var s in _all) if (off && s.particleCount > 0) s.Clear(false);
            }
            public void Clear() { foreach (var s in _all) s.Clear(false); }
            static bool Off { get { return Quality.Particles <= 0f || Reduced; } }

            public void Rise(Vector3 p, Vector3 v, float size, float life, Color c) { Emit(_rise, p, v, size, life, c, 0); }
            public void Float(Vector3 p, Vector3 v, float size, float life, Color c) { Emit(_float, p, v, size, life, c, 0); }
            public void Fall(Vector3 p, Vector3 v, float size, float life, Color c, float rotDeg) { Emit(_fall, p, v, size, life, c, rotDeg); }
            public void Spark(Vector3 p, Vector3 v, float size, float life, Color c) { Emit(_spark, p, v, size, life, c, 0); }

            static void Emit(ParticleSystem ps, Vector3 p, Vector3 v, float size, float life, Color c, float rotDeg)
            {
                if (Off) return;
                var ep = new ParticleSystem.EmitParams
                {
                    position = p, velocity = v, startSize = size, startLifetime = life, startColor = c, rotation = rotDeg
                };
                ps.Emit(ep, 1);
            }

            static ParticleSystem Make(Transform parent, string name, string textureKey, int max, float gravity)
            {
                var go = new GameObject(name); go.transform.SetParent(parent, false);
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = true; main.playOnAwake = false; main.maxParticles = max; main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = 1f; main.startSpeed = 0f; main.startSize = .12f; main.gravityModifier = gravity; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                var emission = ps.emission; emission.enabled = false; emission.rateOverTime = 0f;
                var shape = ps.shape; shape.enabled = false;
                var col = ps.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .12f), new GradientAlphaKey(.8f, .6f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(g);
                var size = ps.sizeOverLifetime; size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, .6f), new Keyframe(.2f, 1f), new Keyframe(1, .25f)));
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Billboard; r.sortingOrder = ParticleOrder; r.sharedMaterial = WishFxArt.ParticleMaterial(textureKey);
                ps.Play();
                return ps;
            }
        }
    }
}
