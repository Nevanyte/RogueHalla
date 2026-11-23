using System.Collections.Generic;
using Roguehalla.Net.Ui;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>The HUD's shared motion: whether motion is reduced, a fade for a panel, and a pop that scales about the centre.</summary>
    public static class UiMotion
    {
        /// <summary>Tests set it; null in play.</summary>
        public static bool? ReducedOverride;
        public static bool Reduced { get { return ReducedOverride ?? (ShowcaseSettings.Current != null && ShowcaseSettings.Current.ReducedMotion); } }
    }

    /// <summary>A fade for one panel: alpha through a CanvasGroup, pointer blocked while it fades out. The owner ticks it. The owner shows and hides the root through Set and Snap only (a direct SetActive leaves the fade's state stale).</summary>
    public sealed class HudFade
    {
        public const float InSeconds = .12f, OutSeconds = .25f;
        readonly RectTransform _root;
        readonly CanvasGroup _group;
        readonly bool _interactive;
        readonly float _in, _out;
        bool _shown;
        float _alpha;

        public HudFade(RectTransform root, bool interactive, float inSeconds = InSeconds, float outSeconds = OutSeconds)
        {
            _root = root; _interactive = interactive; _in = inSeconds; _out = outSeconds;
            if (_root == null) return;
            _group = _root.GetComponent<CanvasGroup>();
            if (_group == null) _group = _root.gameObject.AddComponent<CanvasGroup>();
            _shown = _root.gameObject.activeSelf;
            _alpha = _shown ? _group.alpha : 0f;
            bool takes = _shown && _interactive;
            _group.blocksRaycasts = takes; _group.interactable = takes;
        }

        /// <summary>The wanted state.</summary>
        public bool Shown { get { return _shown; } }
        /// <summary>What is drawn.</summary>
        public float Alpha { get { return _alpha; } }
        public bool Active { get { return _root != null && _root.gameObject.activeSelf; } }

        /// <summary>Starts the fade; activates the object at once when shown.</summary>
        public void Set(bool shown)
        {
            if (_root == null || shown == _shown) return;
            if (UiMotion.Reduced) { Snap(shown); return; }
            _shown = shown;
            bool takes = shown && _interactive;
            _group.blocksRaycasts = takes; _group.interactable = takes;
            if (shown && !_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
        }

        /// <summary>No fade.</summary>
        public void Snap(bool shown)
        {
            if (_root == null) return;
            _shown = shown;
            _alpha = shown ? 1f : 0f;
            bool takes = shown && _interactive;
            _group.alpha = _alpha; _group.blocksRaycasts = takes; _group.interactable = takes;
            if (_root.gameObject.activeSelf != shown) _root.gameObject.SetActive(shown);
        }

        /// <summary>Once per frame while Active or Shown; does nothing otherwise.</summary>
        public void Tick(float dt)
        {
            if (_root == null || !_root.gameObject.activeSelf && !_shown) return;
            float next = UiEase.FadeStep(_alpha, _shown, dt, _in, _out, UiMotion.Reduced);
            if (next != _alpha) { _alpha = next; _group.alpha = next; }
            if (!_shown && _alpha <= 0f && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
        }
    }

    /// <summary>Press pulses that scale a rect about its centre, whatever its pivot. Needs point-anchored rects.</summary>
    public sealed class HudPulses
    {
        public const float Seconds = .15f, Pop = .08f;

        struct Entry { public RectTransform Rect; public float Start; public Vector2 Origin, Last; }
        readonly List<Entry> _entries = new List<Entry>();

        public int Count { get { return _entries.Count; } }

        /// <summary>A rect already pulsing restarts (keeping its first origin); null is ignored.</summary>
        public void Play(RectTransform rect, float now)
        {
            if (rect == null || UiMotion.Reduced) return;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Rect != rect) continue;
                var e = _entries[i]; e.Start = now; _entries[i] = e;
                return;
            }
            _entries.Add(new Entry { Rect = rect, Start = now, Origin = rect.anchoredPosition, Last = rect.anchoredPosition });
        }

        /// <summary>Unscaled time.</summary>
        public void Tick(float now)
        {
            bool reduced = _entries.Count > 0 && UiMotion.Reduced;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (e.Rect == null) { _entries.RemoveAt(i); continue; }
                // Something else placed the rect during the pulse: its position is the new origin.
                if ((e.Rect.anchoredPosition - e.Last).sqrMagnitude > MovedSq) { e.Origin = e.Last = e.Rect.anchoredPosition; }
                float t = (now - e.Start) / Seconds;
                if (reduced || t >= 1f) { Restore(e); _entries.RemoveAt(i); continue; }
                float s = 1f + Pop * UiEase.Pop(t);
                var r = e.Rect;
                r.localScale = new Vector3(s, s, 1f);
                Vector2 off = (new Vector2(.5f, .5f) - r.pivot);
                off.x *= r.rect.width; off.y *= r.rect.height;
                var placed = e.Origin + off * (1f - s);
                r.anchoredPosition = placed;
                e.Last = placed; _entries[i] = e;
            }
        }

        /// <summary>Every rect back to scale 1 and its own position.</summary>
        public void Clear()
        {
            for (int i = 0; i < _entries.Count; i++) if (_entries[i].Rect != null) Restore(_entries[i]);
            _entries.Clear();
        }

        const float MovedSq = .001f * .001f;

        static void Restore(Entry e)
        {
            e.Rect.localScale = Vector3.one;
            if ((e.Rect.anchoredPosition - e.Last).sqrMagnitude <= MovedSq) e.Rect.anchoredPosition = e.Origin;
        }
    }
}
