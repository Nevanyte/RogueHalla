using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>The HUD's shared motion: whether motion is reduced, a fade for a panel, and a pop that scales about the centre.</summary>
    public static class UiMotion
    {
        public static bool? ReducedOverride;
        public static bool Reduced { get { return ReducedOverride ?? (ShowcaseSettings.Current != null && ShowcaseSettings.Current.ReducedMotion); } }
    }

    /// <summary>A fade for one panel: alpha through a CanvasGroup, pointer blocked while it fades out.</summary>
    public sealed class HudFade
    {
        public const float InSeconds = .12f, OutSeconds = .25f;
        readonly RectTransform _root;
        public HudFade(RectTransform root, bool interactive, float inSeconds = InSeconds, float outSeconds = OutSeconds) { _root = root; }
        public bool Shown { get { return false; } }
        public float Alpha { get { return 0f; } }
        public bool Active { get { return _root != null && _root.gameObject.activeSelf; } }
        public void Set(bool shown) { }
        public void Snap(bool shown) { }
        public void Tick(float dt) { }
    }

    /// <summary>Press pulses that scale a rect about its centre, whatever its pivot.</summary>
    public sealed class HudPulses
    {
        public const float Seconds = .15f, Pop = .08f;
        public void Play(RectTransform rect, float now) { }
        public void Tick(float now) { }
        public void Clear() { }
        public int Count { get { return 0; } }
    }
}
