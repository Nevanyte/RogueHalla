using System;
using Roguehalla.Net;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Roguehalla.Client
{
    /// <summary>
    /// Presentation of the tutorial (P-TUTORIAL-3): a welcome card, a compact step card and a guide marker. It holds no rules: text,
    /// visibility and marker placement come from <see cref="TutorialMarkerRules"/> and <see cref="TutorialFlow"/>. Only the card
    /// buttons take pointer input.
    /// </summary>
    public sealed class TutorialView : MonoBehaviour
    {
        const float EdgeMargin = 56f, CardWidth = 300f, WelcomeWidth = 340f;
        static readonly Color Gold = new Color(1f, .82f, .3f, .95f);
        static Sprite _triangle;
        RectTransform _stage, _welcome, _card, _edge;
        TMP_Text _welcomeBody, _startLabel, _cardText;
        Transform _ring;
        Image _edgeImage;
        TMP_Text _cardButtonLabel;
        bool _cardDone;

        public void Initialize(RectTransform stage, RectTransform chrome, Action start, Action skipWelcome, Action skipStep, Action closeDone)
        {
            _stage = stage;
            var theme = UiTheme.Current;
            _welcome = HudUi.Box(chrome, "TutorialWelcome", HudAnchor.Center, 0, 0, WelcomeWidth, 150f);
            HudUi.Surface(_welcome, theme.Surface, theme.CornerRadiusLarge, theme.AccentDim, true, true);
            HudUi.Label(HudUi.At(_welcome, "Title", 16f, 10f, WelcomeWidth - 32f, 24f), "Label", "NEW HERE?", 16f, theme.Accent, TextAlignmentOptions.MidlineLeft, true).characterSpacing = 2f;
            _welcomeBody = HudUi.Label(HudUi.At(_welcome, "Body", 16f, 40f, WelcomeWidth - 32f, 54f), "Label", "", theme.SizeBody, theme.TextPrimary, TextAlignmentOptions.TopLeft, false, UiFontRole.Body, true);
            _startLabel = HudUi.Button(_welcome, "Start", "Start", HudAnchor.BottomLeft, 16f, 14f, 120f, 28f, () => start(), theme.SizeBody).GetComponentInChildren<TMP_Text>();
            HudUi.Button(_welcome, "Skip", "Skip", HudAnchor.BottomRight, 16f, 14f, 80f, 28f, () => skipWelcome(), theme.SizeBody);
            _welcome.gameObject.SetActive(false);

            // Legacy training-bar spot (bottom-left); above the quest launcher so the two do not overlap. Only its Skip button takes pointer input.
            _card = HudUi.Box(stage, "TutorialCard", HudAnchor.BottomLeft, 12f, 44f, CardWidth, 58f);
            HudUi.Surface(_card, theme.Surface, theme.CornerRadius, theme.AccentDim, false, true);
            _cardText = HudUi.Label(HudUi.At(_card, "Copy", 10f, 6f, CardWidth - 76f, 46f), "Label", "", theme.SizeBody, theme.TextPrimary, TextAlignmentOptions.MidlineLeft, false, UiFontRole.Body, true);
            _cardButtonLabel = HudUi.Button(_card, "Skip", "Skip", HudAnchor.MiddleRight, 8f, 0, 56f, 24f, () => { if (_cardDone) closeDone(); else skipStep(); }, theme.SizeSmall, theme.CornerRadiusSmall).GetComponentInChildren<TMP_Text>();
            _card.gameObject.SetActive(false);

            var ring = new GameObject("TutorialRing");
            var sr = ring.AddComponent<SpriteRenderer>();
            sr.sprite = GroundMarkArt.Ring(new Color32(255, 209, 77, 255)); sr.sortingOrder = 5000;
            ring.SetActive(false); _ring = ring.transform;

            _edge = HudUi.Box(stage, "TutorialEdge", HudAnchor.Center, 0, 0, 28f, 28f);
            _edgeImage = _edge.gameObject.AddComponent<Image>();
            _edgeImage.sprite = Triangle(); _edgeImage.color = Gold; _edgeImage.raycastTarget = false;
            _edge.gameObject.SetActive(false);
        }

        public bool WelcomeShown { get { return _welcome != null && _welcome.gameObject.activeSelf; } }

        public void SetWelcome(bool visible, string action, string body)
        {
            if (visible)
            {
                _startLabel.text = action; _welcomeBody.text = body;
                if (!WelcomeShown) _welcome.SetAsLastSibling();
            }
            _welcome.gameObject.SetActive(visible);
        }

        public void SetCard(bool visible, string label, bool done = false)
        {
            _cardDone = done;
            if (visible) { _cardText.text = label; _cardButtonLabel.text = done ? "Close" : "Skip"; }
            _card.gameObject.SetActive(visible);
            if (!visible) HideMarker();
        }

        public void HideMarker() { _ring.gameObject.SetActive(false); _edge.gameObject.SetActive(false); }

        /// <summary>Places the guide marker for a target observed in the current snapshot; hides it when there is none.</summary>
        public void ShowMarker(Camera camera, TutorialTarget target)
        {
            if (camera == null || target.Kind == TutorialTargetKind.None) { HideMarker(); return; }
            Vector3 world = ArenaView.WorldPoint(target.X, target.Y);
            Vector3 screen = camera.WorldToScreenPoint(world);
            float w = Screen.width, h = Screen.height;
            // The rules work top-left, y down; Unity screen space is bottom-left, y up.
            var m = TutorialMarkerRules.Place(screen.x, h - screen.y, screen.z >= 0f, w, h, EdgeMargin);
            bool reduced = ShowcaseSettings.Current != null && ShowcaseSettings.Current.ReducedMotion;
            _ring.gameObject.SetActive(m.OnScreen); _edge.gameObject.SetActive(!m.OnScreen);
            if (m.OnScreen)
            {
                float pulse = reduced ? 1f : 1f + Mathf.Sin(Time.unscaledTime * 3f) * .06f;
                float d = (target.Radius > 0f ? target.Radius : 28f) * ArenaView.Scale * 2f * pulse;
                _ring.position = world; _ring.localScale = new Vector3(d, d * ArenaView.GroundSquash, 1f);
                return;
            }
            // The canvas is ScreenSpaceOverlay: a null camera. The edge box is centre-anchored with a centre pivot.
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_stage, new Vector2(m.X, h - m.Y), null, out Vector2 local);
            _edge.anchoredPosition = local;
            _edge.localRotation = Quaternion.Euler(0f, 0f, -m.Angle * Mathf.Rad2Deg - 90f);
            float a = reduced ? Gold.a : .6f + .4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f));
            _edgeImage.color = new Color(Gold.r, Gold.g, Gold.b, a);
        }

        void OnDestroy() { if (_ring != null) Destroy(_ring.gameObject); }

        static Sprite Triangle()
        {
            if (_triangle != null) return _triangle;
            const int n = 64;
            var pixels = new Color32[n * n];
            for (int py = 0; py < n; py++)
                for (int px = 0; px < n; px++)
                {
                    float x = (px + .5f) / n * 2f - 1f, y = (py + .5f) / n * 2f - 1f;
                    pixels[py * n + px] = y >= -.9f && y <= .9f && Mathf.Abs(x) <= (.9f - y) * .5f ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                }
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixels32(pixels); texture.Apply(false, true);
            _triangle = Sprite.Create(texture, new Rect(0, 0, n, n), new Vector2(.5f, .5f), n);
            _triangle.hideFlags = HideFlags.HideAndDontSave;
            return _triangle;
        }
    }
}
