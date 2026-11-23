namespace Roguehalla.Net.Ui
{
    /// <summary>Easing and stepping for HUD motion; pure functions of time.</summary>
    public static class UiEase
    {
        public static float Clamp01(float t) { return 0f; }
        public static float OutCubic(float t) { return 0f; }
        public static float InOutSine(float t) { return 0f; }
        public static float Pop(float t) { return 0f; }
        public static float Toward(float current, float target, float maxDelta) { return current; }

        /// <summary>Next alpha of a fade: toward 1 over inSeconds when show, toward 0 over outSeconds otherwise; with
        /// reduced, exactly 0 or 1. A duration of 0 or less is instant.</summary>
        public static float FadeStep(float alpha, bool show, float dt, float inSeconds, float outSeconds, bool reduced) { return alpha; }

        /// <summary>Next value of a bar that eases toward its target: closes the gap in about seconds, never overshoots,
        /// jumps when the gap is under epsilon or reduced is on.</summary>
        public static float Approach(float current, float target, float dt, float seconds, bool reduced, float epsilon = .001f) { return current; }
    }
}
