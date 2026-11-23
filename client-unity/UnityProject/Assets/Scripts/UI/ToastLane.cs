using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Roguehalla.Client
{
    /// <summary>
    /// One toast lane of the match HUD: a column of small pooled pills (event lane at the top centre, feedback lane at the bottom centre)
    /// drawn from a <see cref="ToastLaneModel"/>. Pills are built once, never take the pointer, and are placed from <see cref="HudRegions"/>.
    /// </summary>
    public sealed class ToastLane
    {
        sealed class Pill
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public TMP_Text Text;
            public MythicTextScale Scale;
            public Image Strip;
            public string Shown;
            public NoticeTone Tone = (NoticeTone)255;
            public float Width, TextScale = -1f;
        }

        const float StripWidth = 3f, Inset = 14f, Padding = 28f;
        readonly RectTransform _root;
        readonly bool _event;
        readonly Pill[] _pills;
        bool _visible = true;

        /// <summary>The lane's container under the stage.</summary>
        public RectTransform Root => _root;

        public ToastLane(RectTransform stage, string name, bool eventLane, int capacity)
        {
            _event = eventLane;
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(stage, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            _root = rect;
            var theme = UiTheme.Current;
            _pills = new Pill[capacity + 1];
            for (int i = 0; i < _pills.Length; i++)
            {
                var pill = new Pill();
                pill.Rect = HudUi.Box(_root, "Toast" + i, _event ? HudAnchor.TopCenter : HudAnchor.BottomCenter, 0, 0, HudRegions.LaneMaxWidth, HudRegions.PillHeight);
                HudUi.Surface(pill.Rect, UiTheme.With(theme.Surface, HudUi.PlateAlpha), theme.CornerRadius, theme.BorderStrong, false, true);
                pill.Group = pill.Rect.gameObject.AddComponent<CanvasGroup>();
                pill.Group.blocksRaycasts = false; pill.Group.interactable = false;
                var strip = HudUi.Fill(pill.Rect, "Tone", theme.Accent);
                var sr = strip.rectTransform;
                sr.anchorMin = new Vector2(0f, 0f); sr.anchorMax = new Vector2(0f, 1f); sr.pivot = new Vector2(0f, .5f);
                sr.offsetMin = new Vector2(5f, 4f); sr.offsetMax = new Vector2(5f + StripWidth, -4f);
                pill.Strip = strip;
                pill.Text = HudUi.Label(pill.Rect, "Text", "", theme.SizeBody, theme.TextPrimary, TextAlignmentOptions.Center, false);
                pill.Text.rectTransform.offsetMin = new Vector2(Inset, pill.Text.rectTransform.offsetMin.y);
                pill.Text.rectTransform.offsetMax = new Vector2(-Inset, pill.Text.rectTransform.offsetMax.y);
                pill.Scale = pill.Text.GetComponent<MythicTextScale>(); pill.Scale.MinSize = 9f;
                pill.Width = HudRegions.LaneMaxWidth;
                pill.Rect.gameObject.SetActive(false);
                _pills[i] = pill;
            }
        }

        /// <summary>The lane is drawn only while visible; its pills keep their state meanwhile.</summary>
        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (!visible) for (int i = 0; i < _pills.Length; i++) if (_pills[i].Rect.gameObject.activeSelf) _pills[i].Rect.gameObject.SetActive(false);
        }

        /// <summary>Places and paints the pills for the model's lines. minLeft is the least left edge an event pill may have (0 for none).</summary>
        public void Present(ToastLaneModel model, float stageWidth, float topCentreBottom, float minLeft)
        {
            int count = _visible && model != null ? Mathf.Min(model.Count, _pills.Length) : 0;
            for (int i = 0; i < _pills.Length; i++)
            {
                var pill = _pills[i];
                if (i >= count) { if (pill.Rect.gameObject.activeSelf) pill.Rect.gameObject.SetActive(false); continue; }
                var line = model.Line(i);
                if (!pill.Rect.gameObject.activeSelf) pill.Rect.gameObject.SetActive(true);
                Paint(pill, line, stageWidth);
                Position(pill, line.Slot, stageWidth, topCentreBottom, minLeft);
                pill.Group.alpha = line.Alpha;
            }
        }

        void Paint(Pill pill, ToastLine line, float stageWidth)
        {
            float scale = ShowcaseSettings.Current != null ? ShowcaseSettings.Current.TextScale : 1f;
            float room = HudRegions.LaneWidth(stageWidth);
            bool textChanged = !ReferenceEquals(pill.Shown, line.Text) && pill.Shown != line.Text;
            if (textChanged || pill.TextScale != scale || pill.Width > room + .01f)
            {
                var label = pill.Text;
                float keep = label.fontSize;
                label.fontSize = pill.Scale.BaseSize * Mathf.Clamp(scale, .85f, 1.5f);
                float wanted = label.GetPreferredValues(line.Text, 0f, 0f).x;
                label.fontSize = keep;
                pill.Width = Mathf.Min(room, Mathf.Ceil(wanted / MythicTextScale.WidthShare) + Padding);
                pill.Rect.sizeDelta = new Vector2(pill.Width, HudRegions.PillHeight);
                pill.Shown = line.Text; pill.TextScale = scale;
                label.text = line.Text;
                pill.Scale.Refit();
            }
            if (pill.Tone != line.Tone)
            {
                pill.Tone = line.Tone;
                var theme = UiTheme.Current;
                pill.Strip.color = line.Tone == NoticeTone.Good ? (Color)theme.Success : line.Tone == NoticeTone.Warn ? (Color)theme.Warning
                    : line.Tone == NoticeTone.Bad ? (Color)theme.Danger : (Color)theme.Accent;
            }
        }

        void Position(Pill pill, float slot, float stageWidth, float topCentreBottom, float minLeft)
        {
            int low = Mathf.FloorToInt(slot);
            float frac = slot - low;
            var a = Region(low, pill.Width, topCentreBottom);
            float y = a.Y;
            if (frac > 0f) y += frac * (Region(low + 1, pill.Width, topCentreBottom).Y - a.Y);
            float x = 0f;
            if (_event && minLeft > 0f)
            {
                float left = stageWidth * .5f - pill.Width * .5f;
                if (left < minLeft) x = Mathf.Min(minLeft - left, Mathf.Max(0f, stageWidth * .5f - pill.Width * .5f));
            }
            HudUi.Place(pill.Rect, _event ? HudAnchor.TopCenter : HudAnchor.BottomCenter, x, y, pill.Width, HudRegions.PillHeight);
        }

        HudRegion Region(int slot, float width, float topCentreBottom) =>
            _event ? HudRegions.EventPill(slot, width, topCentreBottom) : HudRegions.FeedbackPill(slot, width);
    }
}
