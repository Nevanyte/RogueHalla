namespace Roguehalla.Net.Ui
{
    /// <summary>The shown fraction of a bar and the ghost of lost value behind it, eased toward a target.</summary>
    public struct BarEase
    {
        public const float FallPerSecond = 6f, RisePerSecond = 3f, GhostHoldSeconds = .25f, GhostFallPerSecond = 2f,
            SnapJump = .6f;
        public float Shown, Ghost, GhostHold;
        public bool Has { get; private set; }
        public float Target { get; private set; }

        static float Clamp(float v) { return float.IsNaN(v) || v < 0f ? 0f : (v > 1f ? 1f : v); }

        public void Snap(float target)
        {
            float t = Clamp(target);
            Shown = Ghost = Target = t; GhostHold = 0f; Has = true;
        }

        public void Step(float target, float dt, bool reduced) { Step(target, dt, reduced, true); }

        /// <summary>ghost false: not damage (a barrier move, a health fall that Hp did not cause): Ghost equals Shown
        /// after the step and the fall rate is not used.</summary>
        public void Step(float target, float dt, bool reduced, bool ghost)
        {
            float t = Clamp(target);
            if (!Has || reduced || t - Target > SnapJump) { Snap(t); return; }
            if (float.IsNaN(dt) || !(dt > 0f)) dt = 0f;
            bool fell = t < Target;
            Target = t;
            float from = Shown;
            float rate = t < from && ghost ? FallPerSecond : RisePerSecond;
            Shown = UiEase.Toward(from, t, rate * dt);
            if (!ghost) { Ghost = Shown; GhostHold = 0f; return; }
            if (fell) { if (from > Ghost) Ghost = from; GhostHold = GhostHoldSeconds; }
            if (Shown > Ghost) Ghost = Shown;
            float rest = dt;
            float spend = GhostHold < rest ? GhostHold : rest;
            GhostHold -= spend; rest -= spend;
            if (rest > 0f && Ghost > Shown)
            {
                float g = Ghost - GhostFallPerSecond * rest;
                Ghost = g > Shown ? g : Shown;
            }
        }

        public void Clear() { this = default(BarEase); }
    }
}
