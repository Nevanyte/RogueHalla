using Roguehalla.Net.Ui;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Roguehalla.Client
{
    /// <summary>A thin, non-interactive position marker at the right edge of a scroll pane: hidden while the content fits, otherwise a
    /// handle whose length is viewport over content. It follows the scroll's value-changed event; nothing polls.</summary>
    public sealed class ScrollIndicator
    {
        public const float Width = 4f, Inset = 3f;

        readonly ScrollRect _scroll;
        readonly RectTransform _track, _handle;
        readonly UnityAction<Vector2> _listener;

        public bool Visible => _track.gameObject.activeSelf;

        public ScrollIndicator(ScrollRect scroll, RectTransform frame)
        {
            _scroll = scroll;
            var theme = UiTheme.Current;
            var track = HudUi.Fill(frame, "ScrollIndicator", UiTheme.With(theme.Border, .5f));
            _track = track.rectTransform;
            _track.anchorMin = new Vector2(1f, 0f); _track.anchorMax = new Vector2(1f, 1f); _track.pivot = new Vector2(1f, .5f);
            _track.sizeDelta = new Vector2(Width, 0f); _track.anchoredPosition = new Vector2(-Inset, 0f);
            var handle = HudUi.Fill(_track, "Handle", UiTheme.With(theme.TextSecondary, .85f));
            _handle = handle.rectTransform;
            _track.gameObject.SetActive(false);
            _listener = _ => Refresh();
            scroll.onValueChanged.AddListener(_listener);
        }

        public void Refresh()
        {
            if (_scroll == null || _scroll.viewport == null || _scroll.content == null) return;
            bool show = ScrollThumb.Compute(_scroll.viewport.rect.height, _scroll.content.rect.height,
                _scroll.verticalNormalizedPosition, out float top, out float bottom);
            if (_track.gameObject.activeSelf != show) _track.gameObject.SetActive(show);
            if (!show) return;
            float low = 1f - bottom, high = 1f - top;
            if (Mathf.Abs(_handle.anchorMin.y - low) > .0005f) _handle.anchorMin = new Vector2(0f, low);
            if (Mathf.Abs(_handle.anchorMax.y - high) > .0005f) _handle.anchorMax = new Vector2(1f, high);
        }
    }
}
