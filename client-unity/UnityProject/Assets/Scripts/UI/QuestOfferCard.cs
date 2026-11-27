using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Roguehalla.Client
{
    /// <summary>The quest offer card: one offer the player can accept where they stand. Opened and closed by the HUD;
    /// it sends nothing itself.</summary>
    public sealed class QuestOfferCard
    {
        public const float GraceSeconds = .5f;
        const float Pad = 14f, Gap = 6f, FooterHeight = 34f, InnerWidth = HudRegions.OfferWidth - Pad * 2f;
        const float ScrollGutter = 10f, TextWidth = InnerWidth - ScrollGutter;
        const string AcceptCaption = "ACCEPT · Enter", AcceptingCaption = "ACCEPTING…";

        readonly RectTransform _root, _footer, _body, _content;
        readonly ScrollRect _scroller;
        readonly ScrollIndicator _scrollIndicator;
        readonly HudFade _fade;
        readonly System.Action _onAccept, _onClose;
        readonly QuestAcceptLatch _latch;
        readonly RectTransform[] _boxes = new RectTransform[6];
        readonly TMP_Text[] _labels = new TMP_Text[6];
        readonly float[] _sizes = new float[6];
        readonly float[] _heights = new float[6];
        readonly Button _accept, _close;
        readonly TMP_Text _acceptLabel;
        const int Giver = 0, Name = 1, Objective = 2, Reward = 3, Ends = 4, More = 5;

        bool _open, _reachOk = true, _laidOut, _dirty;
        QuestHandle? _shown;
        float _lost, _wanted, _scale = float.NaN;
        string _giverText, _nameText, _objectiveText, _rewardSource, _rewardText, _endsText, _moreText;
        int _endsSeconds = -2, _moreReach = -1;
        string _caption;
        bool _captionInteractable;
        HudRegion _placed;

        public QuestOfferCard(Transform parent, QuestAcceptLatch latch, System.Action onAccept, System.Action onClose)
        {
            _latch = latch;
            _onAccept = onAccept; _onClose = onClose;
            var theme = UiTheme.Current;
            _root = HudUi.Box(parent, "QuestOffer", HudAnchor.TopLeft, HudRegions.Margin, 0f, HudRegions.OfferWidth, HudRegions.OfferMinHeight);
            HudUi.Surface(_root, UiTheme.With(theme.Surface, HudUi.PlateAlpha), theme.CornerRadiusLarge, theme.BorderStrong, true);
            _body = HudUi.At(_root, "Description", Pad, Pad, InnerWidth, 40f);
            var viewport = HudUi.At(_body, "Viewport", 0f, 0f, InnerWidth, 40f);
            HudUi.Fill(viewport, "ScrollTarget", Color.clear, true);
            viewport.gameObject.AddComponent<RectMask2D>();
            _content = HudUi.At(viewport, "Content", 0f, 0f, InnerWidth, 40f);
            _scroller = _body.gameObject.AddComponent<ScrollRect>();
            _scroller.viewport = viewport;
            _scroller.content = _content;
            _scroller.horizontal = false;
            _scroller.movementType = ScrollRect.MovementType.Clamped;
            _scroller.scrollSensitivity = 35f;
            _body.gameObject.AddComponent<KeyboardScrollPane>().Initialize(_scroller);
            _scrollIndicator = new ScrollIndicator(_scroller, _body);
            _sizes[Giver] = 13f; _sizes[Name] = 21f; _sizes[Objective] = 16f;
            _sizes[Reward] = 15f; _sizes[Ends] = 13f; _sizes[More] = 13f;
            Build(Giver, "Giver", theme.Accent, true, true);
            Build(Name, "Name", theme.TextPrimary, true, true);
            Build(Objective, "Objective", theme.TextPrimary, false, true);
            Build(Reward, "Reward", theme.Accent, false, true);
            Build(Ends, "Ends", theme.TextSecondary, false, true);
            Build(More, "More", theme.TextSecondary, false, true);
            _footer = HudUi.At(_root, "Footer", Pad, 0f, InnerWidth, FooterHeight);
            _accept = HudUi.Button(_footer, "Accept", AcceptCaption, HudAnchor.TopLeft, 0f, 0f, 186f, FooterHeight, () => { if (_onAccept != null) _onAccept(); }, 15f);
            HudUi.ApplyColors(_accept, theme.AccentDim);
            _acceptLabel = _accept.GetComponentInChildren<TMP_Text>(true);
            _caption = AcceptCaption; _captionInteractable = true;
            _close = HudUi.Button(_footer, "Close", "Esc · close", HudAnchor.TopLeft, 194f, 0f, InnerWidth - 194f, FooterHeight, () => { if (_onClose != null) _onClose(); }, 13f);
            _fade = new HudFade(_root, true);
            _fade.Snap(false);
        }

        void Build(int index, string box, Color color, bool bold, bool wrap)
        {
            _boxes[index] = HudUi.At(_content, box, 0f, 0f, TextWidth, 14f);
            _labels[index] = HudUi.Unfit(HudUi.Label(_boxes[index], "Label", "", _sizes[index], color,
                TextAlignmentOptions.TopLeft, bold, UiFontRole.Body, wrap));
        }

        public RectTransform Root { get { return _root; } }
        /// <summary>The wanted state: false the moment Close is called.</summary>
        public bool IsOpen { get { return _open; } }
        public QuestHandle? Shown { get { return _shown; } }
        /// <summary>The height its text needs.</summary>
        public float WantedHeight { get { return _wanted; } }
        public bool Accepting { get { return _open && _shown.HasValue && _latch.IsHeld(_shown.Value); } }
        public bool Lapsed { get { return !_reachOk && _lost >= GraceSeconds; } }

        public void Open(QuestRowView offer, int inReach, double serverMs)
        {
            if (offer == null) return;
            _open = true; _shown = offer.Handle; _reachOk = true; _lost = 0f;
            _fade.Set(true);
            _scroller.StopMovement();
            _scroller.verticalNormalizedPosition = 1f;
            Refresh(offer, inReach, serverMs);
        }

        /// <summary>Same offer, new facts. Writes a label only when its string differs.</summary>
        public void Refresh(QuestRowView offer, int inReach, double serverMs)
        {
            if (offer == null) return;
            bool changed = false;
            changed |= Write(Giver, ref _giverText, offer.GiverLabel);
            changed |= Write(Name, ref _nameText, offer.Name);
            changed |= Write(Objective, ref _objectiveText, offer.Objective);
            if (!string.Equals(_rewardSource, offer.Reward) || _rewardText == null)
            {
                _rewardSource = offer.Reward;
                changed |= Write(Reward, ref _rewardText, "Reward · " + offer.Reward);
            }
            int seconds = offer.ExpiresAt > 0 ? SecondsLeft(offer.ExpiresAt, serverMs) : -1;
            if (seconds != _endsSeconds)
            {
                _endsSeconds = seconds;
                string text = seconds < 0 ? "" : "Ends in " + QuestTracker.Clock(seconds);
                bool showHide = (seconds < 0) != string.IsNullOrEmpty(_endsText);
                Write(Ends, ref _endsText, text);
                changed |= showHide;
            }
            int more = inReach > 1 ? inReach : 0;
            if (more != _moreReach)
            {
                _moreReach = more;
                bool showHide = (more == 0) != string.IsNullOrEmpty(_moreText);
                Write(More, ref _moreText, more == 0 ? "" : more + " offers here · J for all");
                changed |= showHide;
            }
            float scale = ShowcaseSettings.Current != null ? ShowcaseSettings.Current.TextScale : 1f;
            if (scale != _scale) { _scale = scale; changed = true; }
            if (changed) Measure();
            UpdateAccept();
        }

        /// <summary>True zeroes the grace; false lets Tick count it.</summary>
        public void NoteReach(bool canAccept)
        {
            if (canAccept) { _reachOk = true; _lost = 0f; }
            else _reachOk = false;
            if (_open) UpdateAccept();
        }

        public void Close()
        {
            _open = false; _shown = null;
            _fade.Set(false);
            UpdateAccept();
        }

        /// <summary>Gives the card its region; lays the labels out only when the region or a text changed.</summary>
        public void Place(HudRegion region)
        {
            bool same = _laidOut && region.Anchor == _placed.Anchor && region.X == _placed.X && region.Y == _placed.Y
                && region.Width == _placed.Width && region.Height == _placed.Height;
            if (same && !_dirty) return;
            _placed = region; _laidOut = true; _dirty = false;
            HudUi.Place(_root, region);
            Layout(region.Height);
        }

        public void MarkAccepting()
        {
            UpdateAccept();
        }

        /// <summary>The fade and the grace; the accept hold is aged by the shared latch.</summary>
        public void Tick(float dt)
        {
            if (!_reachOk) _lost += dt;
            _fade.Tick(dt);
            UpdateAccept();
        }

        // ---------------------------------------------------------------- internals

        static int SecondsLeft(double expiresAt, double serverMs)
        {
            double s = System.Math.Ceiling((expiresAt - serverMs) / 1000.0);
            if (double.IsNaN(s) || double.IsInfinity(s) || s < 0) return 0;
            return s > 1000000000.0 ? 1000000000 : (int)s;
        }

        bool Write(int index, ref string cache, string value)
        {
            if (value == null) value = "";
            if (string.Equals(cache, value)) return false;
            cache = value;
            _labels[index].text = value;
            return true;
        }

        void UpdateAccept()
        {
            string caption = Accepting ? AcceptingCaption : AcceptCaption;
            bool interactable = _open && !Accepting && _reachOk;
            if (!string.Equals(caption, _caption))
            {
                _caption = caption;
                _acceptLabel.text = caption;
                var fit = _acceptLabel.GetComponent<MythicTextScale>();
                if (fit != null) fit.Refit();
            }
            if (interactable != _captionInteractable || _accept.interactable != interactable)
            {
                _captionInteractable = interactable;
                _accept.interactable = interactable;
            }
        }

        bool Shows(int index)
        {
            if (index == Ends) return !string.IsNullOrEmpty(_endsText);
            if (index == More) return !string.IsNullOrEmpty(_moreText);
            return true;
        }

        /// <summary>The height each label needs at the wanted size, and the card's total. Runs when a text, the More line or the scale changes.</summary>
        void Measure()
        {
            float total = Pad * 2f + FooterHeight + Gap;
            for (int i = 0; i < _labels.Length; i++)
            {
                if (!Shows(i)) { _heights[i] = 0f; continue; }
                var label = _labels[i];
                label.GetComponent<MythicTextScale>().Apply(_scale);
                _heights[i] = Mathf.Ceil(label.GetPreferredValues(label.text, TextWidth, 0f).y);
                total += _heights[i] + Gap;
            }
            _wanted = total;
            _dirty = true;
        }

        void Layout(float height)
        {
            float visible = Mathf.Max(1f, height - Pad * 2f - Gap - FooterHeight);
            HudUi.Place(_body, HudAnchor.TopLeft, Pad, Pad, InnerWidth, visible);
            HudUi.Place((RectTransform)_scroller.viewport, HudAnchor.TopLeft, 0f, 0f, InnerWidth, visible);
            float y = 0f;
            for (int i = 0; i < _boxes.Length; i++)
            {
                bool on = Shows(i);
                _boxes[i].gameObject.SetActive(on);
                if (!on) continue;
                HudUi.Place(_boxes[i], HudAnchor.TopLeft, 0f, y, TextWidth, _heights[i]);
                y += _heights[i] + Gap;
            }
            y = Mathf.Max(0f, y - Gap);
            if (_content.rect.height != y) _content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, y);
            _scroller.vertical = y > visible;
            if (!_scroller.vertical) _content.anchoredPosition = Vector2.zero;
            _scrollIndicator.Refresh();
            HudUi.Place(_footer, HudAnchor.TopLeft, Pad, height - Pad - FooterHeight, InnerWidth, FooterHeight);
        }
    }
}
