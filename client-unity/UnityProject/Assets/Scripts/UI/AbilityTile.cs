using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>One 64-unit ability tile of the action row: key cap, name, countdown, cooldown wedge, charge badge.</summary>
    public sealed class AbilityTile
    {
        readonly RectTransform _root, _nameBox;
        readonly UnityEngine.UI.Button _button;
        readonly HudIconHint _hint;
        readonly TMP_Text _key, _name, _seconds, _badge;
        readonly UnityEngine.UI.Image _surface, _sweep, _cast, _icon;
        readonly MythicGlyph? _glyph;
        readonly UiTheme _theme;
        double _readyAtMs, _totalMs;

        // What the tile shows now; Show writes only what differs.
        bool _drawn;
        AbilityPhase _phase;
        float _sweepFill, _castFill;
        int _secondsShown, _badgeShown;
        string _nameShown = "", _hintShown = "";

        public AbilityTile(Transform parent, string name, HudRegion region, float raise, string keyCap, Transform hintOverlay,
            MythicGlyph? icon = null)
        {
            var theme = _theme = UiTheme.Current;
            var placed = new HudRegion(region.Anchor, region.X, region.Y + raise, region.Width, region.Height);
            _root = HudUi.Box(parent, name, placed);
            _surface = HudUi.Surface(_root, theme.SurfaceRaised, theme.CornerRadius, theme.BorderStrong, true, true);
            _button = _root.gameObject.AddComponent<UnityEngine.UI.Button>();
            _button.targetGraphic = _surface;
            _button.transition = UnityEngine.UI.Selectable.Transition.None;
            _hint = HudIconHint.Attach(_button, hintOverlay, "");

            _sweep = HudUi.Fill(_root, "CooldownSweep", UiTheme.With(theme.OutlineDark, .62f));
            _sweep.rectTransform.offsetMin = new Vector2(1f, 1f); _sweep.rectTransform.offsetMax = new Vector2(-1f, -1f);
            _sweep.sprite = UiSprites.Flat(); _sweep.type = UnityEngine.UI.Image.Type.Filled;
            _sweep.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360; _sweep.fillOrigin = 2; _sweep.fillClockwise = false;
            _sweep.fillAmount = 0f; _sweep.enabled = false;

            _glyph = icon;
            if (icon.HasValue)
            {
                var img = _icon = MythicUi.Icon(_root, "Icon", icon.Value, 16f);
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(1f, 1f);
                img.rectTransform.pivot = new Vector2(1f, 1f); img.rectTransform.anchoredPosition = new Vector2(-4f, -3f);
            }

            _key = HudUi.Label(HudUi.At(_root, "Key", 5, 4, 30, 12), "Label", keyCap, theme.SizeTiny, theme.TextSecondary);
            _nameBox = HudUi.Box(_root, "Name", HudAnchor.Center, 0, 0, 60, 26);
            _name = HudUi.Label(_nameBox, "Label", "", theme.SizeTiny, theme.TextPrimary, TextAlignmentOptions.Center, wrap: true);
            _seconds = HudUi.Label(HudUi.Box(_root, "Seconds", HudAnchor.Center, 0, 0, 40, 20), "Label", "", 15f, theme.TextPrimary,
                TextAlignmentOptions.Center, true);
            _badge = HudUi.Label(HudUi.Box(_root, "Charges", HudAnchor.BottomRight, 2, 2, 20, 14), "Label", "", theme.SizeSmall, theme.Accent,
                TextAlignmentOptions.Center, true);
            var castBox = HudUi.Box(_root, "Cast", HudAnchor.BottomLeft, 0, 0, HudRegions.TileSize, 3);
            _cast = castBox.gameObject.AddComponent<UnityEngine.UI.Image>();
            _cast.sprite = UiSprites.Flat(); _cast.color = theme.Accent; _cast.raycastTarget = false;
            _cast.type = UnityEngine.UI.Image.Type.Filled; _cast.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            _cast.fillOrigin = 0; _cast.fillAmount = 0f; _cast.enabled = false;
            Apply(AbilityFace.Empty, "", "");
        }

        public RectTransform Root { get { return _root; } }
        /// <summary>Carries the hover / keyboard hint; it sends nothing.</summary>
        public UnityEngine.UI.Button Button { get { return _button; } }
        public HudIconHint Hint { get { return _hint; } }
        public TMP_Text KeyLabel { get { return _key; } }
        public TMP_Text NameLabel { get { return _name; } }
        public TMP_Text SecondsLabel { get { return _seconds; } }
        public TMP_Text BadgeLabel { get { return _badge; } }
        public UnityEngine.UI.Image Sweep { get { return _sweep; } }
        /// <summary>The cast bar (child "Cast"): fills from the left while a cast is in flight.</summary>
        public UnityEngine.UI.Image CastBar { get { return _cast; } }
        /// <summary>The corner icon (child "Icon"); null for a tile built without a glyph.</summary>
        public UnityEngine.UI.Image Icon { get { return _icon; } }

        /// <summary>Draws a face. Writes only what differs from what the tile shows: a call with the same face, name and
        /// hint changes nothing and allocates nothing.</summary>
        public void Show(AbilityFace face, string name, string hint) { Apply(face, name ?? "", hint ?? ""); }

        /// <summary>Draws a face and a name; the hint is left as it is (its writer is elsewhere).</summary>
        public void Show(AbilityFace face, string name) { Apply(face, name ?? "", null); }

        void Apply(AbilityFace face, string name, string hint)
        {
            var phase = face.Phase == AbilityPhase.Casting ? AbilityPhase.Ready : face.Phase;
            bool first = !_drawn;
            if (first || phase != _phase)
            {
                var theme = _theme;
                bool empty = phase == AbilityPhase.Empty;
                _surface.color = empty ? UiTheme.With(theme.Surface, .35f) : (Color)theme.SurfaceRaised;
                HudUi.SetFrame(_root, empty ? UiTheme.With(theme.Border, .5f) : phase == AbilityPhase.Cooling ? (Color)theme.Border : (Color)theme.BorderStrong);
                _key.color = empty ? UiTheme.With(theme.TextSecondary, .6f) : (Color)theme.TextSecondary;
                _button.interactable = !empty;
                if (_icon != null) { var tint = MythicUi.GlyphColor(_glyph.Value); tint.a = phase == AbilityPhase.Ready ? 1f : .3f; _icon.color = tint; }
                _phase = phase;
            }
            bool cooling = phase == AbilityPhase.Cooling;
            float fill = cooling ? Mathf.Clamp01(face.Sweep) : 0f;
            if (first || _sweep.enabled != cooling) _sweep.enabled = cooling;
            if (first || _sweepFill != fill) { _sweepFill = fill; _sweep.fillAmount = fill; }

            int seconds = cooling && face.Seconds > 0 ? face.Seconds : 0;
            if (first || _secondsShown != seconds) { _secondsShown = seconds; _seconds.text = seconds > 0 ? seconds.ToString() : ""; }
            int badge = phase == AbilityPhase.Empty ? -1 : face.Badge;
            if (first || _badgeShown != badge) { _badgeShown = badge; _badge.text = badge >= 0 ? badge.ToString() : ""; }

            string shownName = phase == AbilityPhase.Cooling ? "" : name;
            if (first || !string.Equals(_nameShown, shownName))
            {
                _nameShown = shownName; _name.text = shownName;
                bool show = shownName.Length != 0;
                if (_nameBox.gameObject.activeSelf != show) _nameBox.gameObject.SetActive(show);
            }
            bool casting = face.Cast >= 0f;
            float castFill = casting ? Mathf.Clamp01(face.Cast) : 0f;
            if (first || _cast.enabled != casting) _cast.enabled = casting;
            if (first || _castFill != castFill) { _castFill = castFill; _cast.fillAmount = castFill; }
            if (hint != null && (first || !string.Equals(_hintShown, hint))) { _hintShown = hint; _hint.Text = hint; }
            _drawn = true;
        }

        /// <summary>Remembers the running cooldown (server ms it ends at, and its length; 0 and 0 when none) so that a later
        /// packet can move the wedge every frame. In this packet it only stores the two values.</summary>
        public void Cooldown(double readyAtMs, double totalMs) { _readyAtMs = readyAtMs; _totalMs = totalMs; }

        public double ReadyAtMs { get { return _readyAtMs; } }
        public double TotalMs { get { return _totalMs; } }
    }
}
