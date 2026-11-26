using System;

namespace Roguehalla.Net.Ui
{
    /// <summary>Easing and stepping for HUD motion; pure functions of time.</summary>
    public static class UiEase
    {
        public static float Clamp01(float t) { return t < 0f ? 0f : (t > 1f ? 1f : t); }

        /// <summary>1 - (1 - t)^3 on [0, 1], clamped.</summary>
        public static float OutCubic(float t) { float u = 1f - Clamp01(t); return 1f - u * u * u; }

        /// <summary>(1 - cos(pi t)) / 2 on [0, 1], clamped.</summary>
        public static float InOutSine(float t) { return (1f - (float)Math.Cos(Math.PI * Clamp01(t))) * .5f; }

        /// <summary>sin(pi t) on [0, 1]: 0 at both ends, 1 at t = .5; 0 outside.</summary>
        public static float Pop(float t) { return t <= 0f || t >= 1f ? 0f : (float)Math.Sin(Math.PI * t); }

        /// <summary>Moves current toward target by at most maxDelta (a negative one counts as 0); never overshoots.</summary>
        public static float Toward(float current, float target, float maxDelta)
        {
            if (maxDelta < 0f) maxDelta = 0f;
            float gap = target - current;
            if (gap <= maxDelta && gap >= -maxDelta) return target;
            return gap > 0f ? current + maxDelta : current - maxDelta;
        }

        /// <summary>Next alpha of a fade: toward 1 over inSeconds when show, toward 0 over outSeconds otherwise; with
        /// reduced, exactly 0 or 1. A duration of 0 or less is instant.</summary>
        public static float FadeStep(float alpha, bool show, float dt, float inSeconds, float outSeconds, bool reduced)
        {
            float target = show ? 1f : 0f;
            float seconds = show ? inSeconds : outSeconds;
            if (reduced || seconds <= 0f) return target;
            return Toward(alpha, target, dt / seconds);
        }

        /// <summary>Next value of a bar that eases toward its target: closes the gap in about seconds, never overshoots,
        /// jumps when the gap is under epsilon or reduced is on.</summary>
        public static float Approach(float current, float target, float dt, float seconds, bool reduced, float epsilon = .001f)
        {
            float gap = target - current;
            if (reduced || seconds <= 0f || gap < epsilon && gap > -epsilon) return target;
            if (dt <= 0f) return current;
            float k = 1f - (float)Math.Exp(-3.5 * dt / seconds);   // about 3 percent of the gap is left after `seconds`
            if (k >= .999f) return target;
            return current + gap * k;
        }
    }
}
