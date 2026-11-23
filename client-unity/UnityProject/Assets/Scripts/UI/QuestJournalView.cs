using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Presentation-only journal for the reliable, owner-scoped quest collection.</summary>
    public sealed class QuestJournalView : MonoBehaviour
    {
        sealed class Row
        {
            public RectTransform Root;
            public UnityEngine.UI.Button Button;
            public TMP_Text Label;
            public QuestHandle Handle;
        }

        RectTransform _root, _card, _content;
        UnityEngine.UI.ScrollRect _scroll, _detailScroll;
        RectTransform _detailContent;
        TMP_Text _state, _detailName, _giver, _brief, _objective, _reward, _participants, _status, _acceptHint, _acceptLabel;
        UnityEngine.UI.Button _close, _accept, _track;
        readonly List<Row> _rows = new List<Row>();
        readonly List<UnityEngine.UI.Button> _actions = new List<UnityEngine.UI.Button>();
        QuestView _actionView;
        QuestRowView _actionRow;
        int _visibleRowCount;
        Session _session;
        Action _onClose;
        Action<QuestHandle?> _onTracked;
        QuestView _view;
        QuestView _participantCacheView;
        QuestRowView _participantCacheRow;
        string _participantCacheText;
        QuestHandle? _selected;
        QuestHandle? _tracked, _acceptPendingFor;
        uint _boundEpoch, _acceptBoundEpoch;
        Session _acceptBoundSession;
        QuestView _acceptBoundView;
        QuestHandle _acceptBoundHandle;
        bool _acceptBindingValid;
        bool _initialized;
        QuestRowView _statusRow; bool _statusPending, _statusReward; int _statusSeconds; string _statusText = "";
        QuestRowView _hintRow; bool _hintFar; string _hintText = "";
        QuestHandle? _markedSelected; bool _rowsNeedMark = true;
        bool _acceptShowsPending;
        RectTransform _textFrame, _strip, _stripContent;
        UnityEngine.UI.ScrollRect _stripScroll;
        ScrollIndicator _textIndicator, _stripIndicator;
        UnityEngine.UI.Button _abandon;
        TMP_Text _abandonLabel, _trackLabel;
        GameObject[] _emptyHidden;
        int _hasState = -1, _stripRows;
        QuestHandle? _stripQuest, _renderedHandle, _abandonQuest;
        bool _abandonArmed;
        string _abandonDelivered = "Abandon", _stateText;
        int _stateTextCount = -1;
        const string ArmedText = "Confirm abandon";

        public RectTransform Root => _root;
        public bool IsVisible => _root != null && _root.gameObject.activeSelf;
        public QuestHandle? TrackedQuest => _tracked;

        public void Initialize(Transform canvasRoot, Action onClose = null, Action<QuestHandle?> onTracked = null)
        {
            if (_initialized) throw new InvalidOperationException("QuestJournalView is already initialized.");
            if (canvasRoot == null) throw new ArgumentNullException(nameof(canvasRoot));
            _initialized = true; _onClose = onClose; _onTracked = onTracked;
            _root = MythicUi.Panel(canvasRoot, "QuestJournal", new Vector2(.12f, .12f), new Vector2(.88f, .915f), ShowcasePalette.Panel);
            _card = _root;
            MythicUi.Text(_card, "JournalTitle", "QUEST JOURNAL", new Vector2(.035f, .925f), new Vector2(.66f, .995f), 27, ShowcasePalette.Gold, heading: true);
            _close = MythicUi.Button(_card, "CloseJournal", "CLOSE", new Vector2(.79f, .925f), new Vector2(.965f, .99f), Close, MythicUi.ButtonStyle.Quiet);
            _state = MythicUi.Text(_card, "JournalState", "Waiting for quest data…", new Vector2(.035f, .75f), new Vector2(.965f, .80f), 16, ShowcasePalette.MutedText);
            _scroll = CreateScroll(_card, new Vector2(.035f, .05f), new Vector2(.43f, .73f), out _content);
            var detail = MythicUi.Panel(_card, "QuestDetail", new Vector2(.455f, .05f), new Vector2(.965f, .73f), ShowcasePalette.PanelRaised); // a lifted slate well, not the near-black sunken token
            MythicUi.Text(detail, "DetailHeading", "QUEST DETAILS", new Vector2(.025f, .885f), new Vector2(.975f, .99f), 19, ShowcasePalette.Text);
            _detailScroll = CreateReadingScroll(detail, new Vector2(.025f, .20f), new Vector2(.975f, .87f), out _detailContent);
            _textFrame = (RectTransform)_detailScroll.transform;
            _detailName = CreateQuestField(_detailContent, "QuestName", "QUEST NAME", 18, ShowcasePalette.Text);
            _objective = CreateQuestField(_detailContent, "QuestObjective", "OBJECTIVE", 17, ShowcasePalette.Text);
            _giver = CreateQuestField(_detailContent, "QuestGiver", "GIVER", 16, ShowcasePalette.Gold);
            _brief = CreateQuestField(_detailContent, "QuestBrief", "BRIEF", 17, ShowcasePalette.MutedText);
            _reward = CreateQuestField(_detailContent, "QuestReward", "REWARD", 16, ShowcasePalette.Text);
            _participants = CreateQuestField(_detailContent, "QuestParticipants", "PARTICIPANTS", 15, ShowcasePalette.Text);
            _status = MythicUi.Text(detail, "QuestStatus", "", new Vector2(.025f, .115f), new Vector2(.975f, .20f), 15, ShowcasePalette.MutedText);
            _track = MythicUi.Button(detail, "TrackQuest", "TRACK OBJECTIVE", new Vector2(.035f, .025f), new Vector2(.48f, .12f), ToggleTrack, MythicUi.ButtonStyle.Secondary);
            _acceptHint = MythicUi.Text(detail, "AcceptHint", "", new Vector2(.52f, .025f), new Vector2(.965f, .12f), 13, ShowcasePalette.MutedText, TextAlignmentOptions.Center);
            _acceptHint.enableWordWrapping = true;
            _acceptHint.gameObject.SetActive(false);
            _accept = MythicUi.Button(detail, "AcceptQuest", "ACCEPT QUEST", new Vector2(.52f, .025f), new Vector2(.965f, .12f), () => { }, MythicUi.ButtonStyle.Primary);
            _acceptLabel = _accept.GetComponentInChildren<TMP_Text>(true);
            _abandon = MythicUi.Button(detail, "AbandonQuest", _abandonDelivered, new Vector2(.52f, .025f), new Vector2(.965f, .12f), () => { }, MythicUi.ButtonStyle.Secondary);
            _abandonLabel = _abandon.GetComponentInChildren<TMP_Text>(true);
            _abandon.gameObject.SetActive(false);
            _trackLabel = _track.GetComponentInChildren<TMP_Text>(true);
            _strip = CreateStrip(detail, out _stripScroll, out _stripContent);
            _textIndicator = new ScrollIndicator(_detailScroll, _textFrame);
            _stripIndicator = new ScrollIndicator(_stripScroll, _strip);
            _emptyHidden = new[]
            {
                _detailName.transform.parent.Find("FieldLabel").gameObject, _objective.transform.parent.gameObject, _giver.transform.parent.gameObject,
                _reward.transform.parent.gameObject, _participants.transform.parent.gameObject,
            };
            _root.gameObject.SetActive(false);
            RenderSelection();
        }

        internal static UnityEngine.UI.ScrollRect CreateScroll(Transform parent, Vector2 min, Vector2 max,
            out RectTransform content, string name = "QuestRows")
        {
            var frame = MythicUi.Rect(parent, name, min, max);
            var scroll = frame.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true; scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36; scroll.inertia = true;
            var viewport = MythicUi.Rect(frame, "Viewport", Vector2.zero, Vector2.one);
            var image = viewport.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = Color.clear; image.raycastTarget = true;
            viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            content = MythicUi.Rect(viewport, "Content", new Vector2(0, 1), Vector2.one, Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(.5f, 1);
            var layout = content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.padding = new UnityEngine.RectOffset(4, 4, 4, 4); layout.spacing = 8; layout.childAlignment = UnityEngine.TextAnchor.UpperCenter;
            layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = content;
            frame.gameObject.AddComponent<KeyboardScrollPane>().Initialize(scroll);
            return scroll;
        }

        static UnityEngine.UI.ScrollRect CreateReadingScroll(Transform parent, Vector2 min, Vector2 max, out RectTransform content)
        {
            var frame = MythicUi.Rect(parent, "QuestTextScroll", min, max);
            var scroll = frame.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true; scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36; scroll.inertia = true;
            var viewport = MythicUi.Rect(frame, "Viewport", Vector2.zero, Vector2.one);
            var image = viewport.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = Color.clear; image.raycastTarget = true;
            viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            content = MythicUi.Rect(viewport, "Content", new Vector2(0, 1), Vector2.one, Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(.5f, 1f);
            var stack = content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            stack.padding = new UnityEngine.RectOffset(8, 8, 6, 8); stack.spacing = 10;
            stack.childAlignment = UnityEngine.TextAnchor.UpperLeft; stack.childControlWidth = true; stack.childControlHeight = true;
            stack.childForceExpandWidth = true; stack.childForceExpandHeight = false;
            content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = content;
            frame.gameObject.AddComponent<KeyboardScrollPane>().Initialize(scroll);
            return scroll;
        }

        static TMP_Text CreateQuestField(Transform parent, string name, string labelText, float size, Color color)
        {
            var field = MythicUi.Rect(parent, name, new Vector2(0, 1), Vector2.one, Vector2.zero, Vector2.zero);
            field.pivot = new Vector2(.5f, 1f);
            var stack = field.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            stack.spacing = 2; stack.childAlignment = UnityEngine.TextAnchor.UpperLeft; stack.childControlWidth = true;
            stack.childControlHeight = true; stack.childForceExpandWidth = true; stack.childForceExpandHeight = false;
            field.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            var label = MythicUi.Text(field, "FieldLabel", labelText, Vector2.zero, Vector2.one, 12, ShowcasePalette.Gold);
            label.enableAutoSizing = false; label.enableWordWrapping = false; label.fontSizeMin = label.fontSize; label.fontSizeMax = label.fontSize;
            label.rectTransform.anchorMin = new Vector2(0, 1); label.rectTransform.anchorMax = Vector2.one; label.rectTransform.pivot = new Vector2(.5f, 1f);

            var value = MythicUi.Text(field, "Value", "", Vector2.zero, Vector2.one, size, color);
            value.enableAutoSizing = false; value.enableWordWrapping = true; value.alignment = TextAlignmentOptions.TopLeft;
            value.fontSizeMin = value.fontSize; value.fontSizeMax = value.fontSize; value.margin = Vector4.zero;
            value.rectTransform.anchorMin = new Vector2(0, 1); value.rectTransform.anchorMax = Vector2.one; value.rectTransform.pivot = new Vector2(.5f, 1f);
            value.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            return value;
        }

        /// <summary>The quest's own actions: a window of at most two rows onto a list, outside the details scroll.</summary>
        static RectTransform CreateStrip(Transform parent, out UnityEngine.UI.ScrollRect scroll, out RectTransform content)
        {
            var frame = MythicUi.Rect(parent, "QuestActions", new Vector2(.025f, .20f), new Vector2(.975f, .20f));
            frame.pivot = new Vector2(.5f, 0f); frame.anchoredPosition = Vector2.zero; frame.sizeDelta = Vector2.zero;
            scroll = frame.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            scroll.horizontal = false; scroll.vertical = true; scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36; scroll.inertia = true;
            var viewport = MythicUi.Rect(frame, "Viewport", Vector2.zero, Vector2.one);
            var image = viewport.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = Color.clear; image.raycastTarget = true;
            viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            content = MythicUi.Rect(viewport, "Content", new Vector2(0, 1), Vector2.one, Vector2.zero, Vector2.zero);
            content.pivot = new Vector2(.5f, 1f);
            var stack = content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            stack.padding = new RectOffset(8, 8, 0, 0); stack.spacing = QuestActionStrip.RowGap; stack.childAlignment = TextAnchor.UpperCenter;
            stack.childControlWidth = true; stack.childControlHeight = true; stack.childForceExpandWidth = true; stack.childForceExpandHeight = false;
            content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = content;
            frame.gameObject.SetActive(false);
            return frame;
        }

        internal static UnityEngine.UI.Button ActionButton(List<UnityEngine.UI.Button> buttons, RectTransform parent, int index)
        {
            if (index < buttons.Count) return buttons[index];
            var button = MythicUi.Button(parent, "Action" + index, "", Vector2.zero, Vector2.one, () => { }, fontSize: 14);
            var layout = button.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            layout.minHeight = 38;
            buttons.Add(button);
            return button;
        }

        public void Bind(Session session)
        {
            if (ReferenceEquals(_session, session)) { Refresh(); return; }
            _session = session; _boundEpoch = session != null ? session.TimelineEpoch : 0;
            _selected = null; ClearTracked(); _acceptPendingFor = null;
            Refresh();
        }

        public void Refresh()
        {
            if (_root == null) return;
            if (_session == null || _session.State != SessionState.InMatch)
            {
                if (_view != null) { _view = null; _selected = null; ClearTracked(); SetRows(0); }
                _acceptPendingFor = null;
                _state.text = _session == null ? "Quest data is not connected." : _session.State == SessionState.Reconnecting ? "Reconnecting · quest actions paused" : "Quest journal unavailable while disconnected.";
                RenderSelection(); return;
            }
            if (_boundEpoch != _session.TimelineEpoch)
            {
                _boundEpoch = _session.TimelineEpoch; _view = null; _selected = null;
                ClearTracked(); _acceptPendingFor = null; SetRows(0);
            }
            QuestView next = _session.Collections.Quests;
            if (next == null)
            {
                if (_view != null) { _view = null; _selected = null; ClearTracked(); SetRows(0); }
                _acceptPendingFor = null;
                _state.text = "Waiting for quest data…"; RenderSelection(); return;
            }
            if (!next.Available)
            {
                if (!ReferenceEquals(next, _view))
                {
                    _view = next; _selected = null; ClearTracked(); _acceptPendingFor = null; SetRows(0);
                }
                _state.text = "Quest journal unavailable."; RenderSelection(); return;
            }
            bool collectionChanged = !ReferenceEquals(next, _view);
            if (collectionChanged)
            {
                float scrollPosition = _scroll.verticalNormalizedPosition;
                QuestHandle? selected = _selected;
                _view = next;
                _acceptPendingFor = null;
                if (selected.HasValue && Find(selected.Value) == null) selected = null;
                _selected = selected;
                if (_tracked.HasValue && Find(_tracked.Value) == null) ClearTracked();
                _visibleRowCount = 0;
                foreach (var quest in next.Rows)
                {
                    if (!QuestPresentation.ShowsOffer(quest)) continue;
                    Row row = GetRow(_visibleRowCount++); row.Handle = quest.Handle;
                    row.Label.text = QuestRowLabel(quest);
                    QuestHandle capturedHandle = quest.Handle; Session capturedSession = _session; uint capturedEpoch = _boundEpoch;
                    row.Button.onClick.RemoveAllListeners(); row.Button.onClick.AddListener(() => SelectCurrent(capturedSession, capturedEpoch, next, capturedHandle));
                    row.Root.gameObject.SetActive(true);
                }
                SetRows(_visibleRowCount); _rowsNeedMark = true;
                _scroll.verticalNormalizedPosition = scrollPosition;
            }
            if (_stateTextCount != _visibleRowCount)
            {
                _stateTextCount = _visibleRowCount;
                _stateText = _visibleRowCount == 0 ? "No quests are currently available." : _visibleRowCount + " quest" + (_visibleRowCount == 1 ? "" : "s");
            }
            SetText(_state, _stateText);
            RenderSelection();
        }

        static string QuestRowLabel(QuestRowView q)
        {
            return (q.Name ?? string.Empty) + "\n" + QuestJournalCopy.RowState(q) + "  ·  " + (q.GiverLabel ?? string.Empty);
        }

        Row GetRow(int index)
        {
            while (_rows.Count <= index)
            {
                int rowIndex = _rows.Count;
                var root = MythicUi.Panel(_content, "QuestRow" + rowIndex, Vector2.zero, Vector2.one, ShowcasePalette.PanelRaised);
                var le = root.gameObject.AddComponent<UnityEngine.UI.LayoutElement>(); le.minHeight = 62; le.preferredHeight = 70;
                var button = root.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = root.GetComponent<UnityEngine.UI.Image>();
                root.GetComponent<UnityEngine.UI.Image>().color = Color.white; // the colour block below is the fill (a token times a token painted rows black)
                var colors = button.colors; colors.normalColor = ShowcasePalette.PanelRaised; colors.highlightedColor = MythicUi.HoverFill;
                colors.selectedColor = MythicUi.SelectedFill; button.colors = colors;
                var label = MythicUi.Text(root, "QuestRowLabel", "", Vector2.zero, Vector2.one, 14, ShowcasePalette.Text);
                label.margin = new Vector4(12, 5, 8, 5); label.alignment = TextAlignmentOptions.MidlineLeft;
                _rows.Add(new Row { Root = root, Button = button, Label = label });
            }
            return _rows[index];
        }

        void SetRows(int count)
        {
            for (int i = count; i < _rows.Count; i++) { _rows[i].Button.onClick.RemoveAllListeners(); _rows[i].Root.gameObject.SetActive(false); }
        }

        QuestRowView Find(QuestHandle handle)
        {
            if (_view == null || !_view.Available) return null;
            QuestRowView row = null;
            var rows = _view.Rows;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Handle.Equals(handle)) { row = rows[i]; break; }
            return row != null && QuestPresentation.ShowsOffer(row) ? row : null;
        }

        public void SelectQuest(QuestHandle handle)
        {
            if (!IsCurrentView(_session, _boundEpoch, _view) || Find(handle) == null) return;
            _selected = handle; RenderSelection();
        }

        bool IsCurrentView(Session session, uint epoch, QuestView view)
        {
            return IsVisible && session != null && ReferenceEquals(_session, session) && session.State == SessionState.InMatch &&
                session.TimelineEpoch == epoch && epoch == _boundEpoch && view != null &&
                ReferenceEquals(session.Collections.Quests, view) && ReferenceEquals(_view, view);
        }

        void SelectCurrent(Session session, uint epoch, QuestView view, QuestHandle handle)
        {
            if (!IsCurrentView(session, epoch, view) || Find(handle) == null) return;
            SelectQuest(handle);
        }

        void RenderSelection()
        {
            QuestRowView q = _selected.HasValue ? Find(_selected.Value) : null;
            bool has = q != null;
            if (_hasState != (has ? 1 : 0))
            {
                _hasState = has ? 1 : 0;
                for (int i = 0; i < _emptyHidden.Length; i++) _emptyHidden[i].SetActive(has);
            }
            SetText(_detailName, has ? q.Name : "Select a quest");
            SetText(_giver, has ? q.GiverLabel : "");
            SetText(_brief, has ? q.Brief : "");
            bool showBrief = has && QuestJournalCopy.ShowsBrief(q);
            if (_brief.transform.parent.gameObject.activeSelf != showBrief) _brief.transform.parent.gameObject.SetActive(showBrief);
            SetText(_objective, has ? q.Objective : "");
            SetText(_reward, has ? q.Reward : "");
            SetText(_participants, has ? ParticipantCopyCached(_view, q) : "");
            QuestHandle? effective = has ? q.Handle : (QuestHandle?)null;
            if (!QuestPresentation.SameHandle(effective, _renderedHandle))
            {
                _renderedHandle = effective;
                _detailScroll.StopMovement(); _detailContent.anchoredPosition = Vector2.zero;
            }
            if (!has)
            {
                _participantCacheView = null; _participantCacheRow = null; _participantCacheText = null;
            }
            bool acceptPending = has && _acceptPendingFor.HasValue && _acceptPendingFor.Value.Equals(q.Handle);
            if (has) RenderStatus(q, acceptPending);
            else { _statusRow = null; _status.text = ""; }
            MarkRows();
            RenderActions(q);
            _track.interactable = has && IsCurrentView(_session, _boundEpoch, _view);
            SetText(_trackLabel, _tracked.HasValue && has && _tracked.Value.Equals(q.Handle) ? "STOP TRACKING" : "TRACK OBJECTIVE");
            _accept.gameObject.SetActive(has && q.CanAccept);
            if (_acceptShowsPending != acceptPending)
            {
                _acceptShowsPending = acceptPending; _acceptLabel.text = acceptPending ? "ACCEPTING…" : "ACCEPT QUEST";
            }
            _accept.interactable = has && q.CanAccept && !acceptPending && _session != null && _session.State == SessionState.InMatch && _session.LastResult == null && ReferenceEquals(_session.Collections.Quests, _view);
            RenderHint(q, has && !q.CanAccept);
            if (!has)
            {
                if (_acceptBindingValid) _accept.onClick.RemoveAllListeners();
                _acceptBindingValid = false; _acceptBoundSession = null; _acceptBoundView = null;
            }
            else if (!_acceptBindingValid || !ReferenceEquals(_acceptBoundSession, _session) || !ReferenceEquals(_acceptBoundView, _view) ||
                _acceptBoundEpoch != _boundEpoch || !_acceptBoundHandle.Equals(q.Handle))
            {
                _accept.onClick.RemoveAllListeners();
                Session capturedSession = _session; uint capturedEpoch = _boundEpoch; QuestView capturedView = _view; QuestHandle capturedHandle = q.Handle;
                _accept.onClick.AddListener(() => TryAcceptFrom(capturedSession, capturedEpoch, capturedView, capturedHandle));
                _acceptBoundSession = capturedSession; _acceptBoundEpoch = capturedEpoch; _acceptBoundView = capturedView;
                _acceptBoundHandle = capturedHandle; _acceptBindingValid = true;
            }
        }

        void RenderStatus(QuestRowView q, bool acceptPending)
        {
            bool reward = false;
            if (_view != null)
                for (int i = 0; i < _view.Pending.Count; i++)
                    if (_view.Pending[i].Equals(q.Handle)) { reward = true; break; }
            int seconds = -1;
            if (q.ExpiresAt > 0)
            {
                double s = _session != null && _session.Latest != null ? Math.Ceiling((q.ExpiresAt - _session.ServerTimeMs) / 1000.0) : -1;
                if (_session != null && _session.Latest != null)
                    seconds = double.IsNaN(s) || double.IsInfinity(s) || s < 0 ? 0 : s > 1000000000.0 ? 1000000000 : (int)s;
            }
            if (!ReferenceEquals(_statusRow, q) || _statusPending != acceptPending || _statusReward != reward || _statusSeconds != seconds)
            {
                _statusRow = q; _statusPending = acceptPending; _statusReward = reward; _statusSeconds = seconds;
                _statusText = QuestJournalCopy.Status(q, acceptPending, reward, seconds);
                bool holds = q.OwnerAccepted || acceptPending || reward;
                _status.color = holds ? ShowcasePalette.Text : ShowcasePalette.MutedText;
            }
            SetText(_status, _statusText);
        }

        void RenderHint(QuestRowView q, bool buttonHidden)
        {
            bool show = false;
            if (q != null && buttonHidden)
            {
                bool far = true;
                var me = _session != null ? _session.Me : null;
                if (me != null)
                {
                    double dx = me.X - q.GiverX, dy = me.Y - q.GiverY, d = Math.Sqrt(dx * dx + dy * dy);
                    far = double.IsNaN(d) || double.IsInfinity(d) || d > QuestJournalCopy.GiverReach;
                }
                if (!ReferenceEquals(_hintRow, q) || _hintFar != far)
                {
                    _hintRow = q; _hintFar = far;
                    _hintText = QuestJournalCopy.Hint(q, far ? double.NaN : 0d);
                }
                show = _hintText.Length > 0;
                if (show) SetText(_acceptHint, _hintText);
            }
            else _hintRow = null;
            if (_acceptHint.gameObject.activeSelf != show) _acceptHint.gameObject.SetActive(show);
        }

        void MarkRows()
        {
            if (!_rowsNeedMark && _markedSelected.HasValue == _selected.HasValue && (!_selected.HasValue || _markedSelected.Value.Equals(_selected.Value))) return;
            _rowsNeedMark = false; _markedSelected = _selected;
            for (int i = 0; i < _visibleRowCount; i++)
                MythicUi.Select(_rows[i].Button, _selected.HasValue && _selected.Value.Equals(_rows[i].Handle));
        }

        static void SetText(TMP_Text label, string value)
        {
            value = value ?? string.Empty;
            string current = label.text;
            if (!ReferenceEquals(current, value) && current != value) label.text = value;
        }

        void RenderActions(QuestRowView row)
        {
            if (ReferenceEquals(_actionView, _view) && ReferenceEquals(_actionRow, row)) return;
            _actionView = _view; _actionRow = row;
            int used = 0;
            bool haveAbandon = false;
            QuestActionView abandon = default(QuestActionView);
            if (row != null)
            {
                var session = _session;
                uint epoch = _boundEpoch;
                QuestView view = _view;
                var actions = view.Actions;
                for (int a = 0; a < actions.Count; a++)
                {
                    QuestActionView action = actions[a];
                    if (!action.Quest.Equals(row.Handle)) continue;
                    if (action.Action == QuestPresentation.Abandon)
                    {
                        if (!haveAbandon) { haveAbandon = true; abandon = action; }
                        continue;
                    }
                    if (action.Action == QuestPresentation.RewardChoice || action.Action == QuestPresentation.PickupReward) continue;
                    var button = ActionButton(_actions, _stripContent, used++);
                    SetText(button.GetComponentInChildren<TMP_Text>(true), action.Label);
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() =>
                    {
                        if (_selected.HasValue && _selected.Value.Equals(action.Quest) && IsCurrentView(session, epoch, view))
                            QuestPresentation.TrySend(session, epoch, view, action.Quest, action.Action, action.Target);
                    });
                    button.gameObject.SetActive(true);
                }
            }
            for (int i = used; i < _actions.Count; i++)
            {
                _actions[i].onClick.RemoveAllListeners();
                _actions[i].gameObject.SetActive(false);
            }
            LayoutStrip(row, used);
            RenderAbandon(row, haveAbandon, abandon);
        }

        void LayoutStrip(QuestRowView row, int used)
        {
            int rows = Math.Min(used, QuestActionStrip.MaxRows);
            QuestHandle? quest = row != null ? row.Handle : (QuestHandle?)null;
            if (!QuestPresentation.SameHandle(quest, _stripQuest))
            {
                _stripQuest = quest;
                _stripScroll.StopMovement(); _stripContent.anchoredPosition = Vector2.zero;
            }
            if (rows == _stripRows) return;
            _stripRows = rows;
            _strip.sizeDelta = new Vector2(_strip.sizeDelta.x, QuestActionStrip.Height(rows));
            _strip.gameObject.SetActive(rows > 0);
            _textFrame.offsetMin = new Vector2(_textFrame.offsetMin.x, QuestActionStrip.ScrollBottom(rows));
        }

        void RenderAbandon(QuestRowView row, bool have, QuestActionView abandon)
        {
            if (!have)
            {
                _abandonArmed = false; _abandonQuest = null;
                if (_abandon.gameObject.activeSelf) { _abandon.onClick.RemoveAllListeners(); _abandon.gameObject.SetActive(false); }
                SetText(_abandonLabel, _abandonDelivered);
                return;
            }
            if (!QuestPresentation.SameHandle(row.Handle, _abandonQuest)) _abandonArmed = false;
            _abandonQuest = row.Handle; _abandonDelivered = abandon.Label ?? string.Empty;
            var session = _session; uint epoch = _boundEpoch; QuestView view = _view;
            _abandon.onClick.RemoveAllListeners();
            _abandon.onClick.AddListener(() => PressAbandon(session, epoch, view, abandon));
            WriteAbandonLabel();
            if (!_abandon.gameObject.activeSelf) _abandon.gameObject.SetActive(true);
        }

        void WriteAbandonLabel() => SetText(_abandonLabel, _abandonArmed ? ArmedText : _abandonDelivered);

        void PressAbandon(Session session, uint epoch, QuestView view, QuestActionView action)
        {
            if (!(_selected.HasValue && _selected.Value.Equals(action.Quest) && IsCurrentView(session, epoch, view))) return;
            if (!_abandonArmed) { _abandonArmed = true; WriteAbandonLabel(); return; }
            _abandonArmed = false; WriteAbandonLabel();
            QuestPresentation.TrySend(session, epoch, view, action.Quest, action.Action, action.Target);
        }

        static string ParticipantCopy(QuestView view, QuestRowView quest)
        {
            if (view == null || quest == null || quest.Participants == null || quest.Participants.Count == 0) return "";
            var lines = new List<string>(quest.Participants.Count);
            for (int i = 0; i < quest.Participants.Count; i++)
            {
                QuestParticipantView participant = quest.Participants[i];
                string label = "Participant";
                for (int r = 0; r < view.Roster.Count; r++)
                {
                    QuestRosterEntryView entry = view.Roster[r];
                    if (entry.Id.Equals(participant.Id))
                    {
                        label = entry.Label ?? string.Empty;
                        break;
                    }
                }

                bool isOwner = view.OwnerRoster.HasValue && view.OwnerRoster.Value.Equals(participant.Id);
                string accepted = participant.Accepted ? "Accepted" : "Not accepted";
                lines.Add(label + (isOwner ? " · You" : "") + " · " + accepted + " · Progress " + participant.Progress.ToString(CultureInfo.InvariantCulture));
            }
            return string.Join("\n", lines);
        }

        string ParticipantCopyCached(QuestView view, QuestRowView quest)
        {
            if (!ReferenceEquals(_participantCacheView, view) || !ReferenceEquals(_participantCacheRow, quest))
            {
                _participantCacheView = view; _participantCacheRow = quest;
                _participantCacheText = ParticipantCopy(view, quest);
            }
            return _participantCacheText ?? string.Empty;
        }


        public bool TryAccept(QuestHandle handle) => TryAcceptFrom(_session, _boundEpoch, _view, handle);

        bool TryAcceptFrom(Session session, uint epoch, QuestView view, QuestHandle handle)
        {
            if (!IsCurrentView(session, epoch, view) || session.LastResult != null) return false;
            QuestRowView current = Find(handle);
            if (current == null || !current.CanAccept) return false;
            if (_acceptPendingFor.HasValue && _acceptPendingFor.Value.Equals(handle)) return false;
            session.Input.QuestAccept(handle);
            _acceptPendingFor = handle;
            RenderSelection();
            return true;
        }


        void ToggleTrack()
        {
            if (!IsCurrentView(_session, _boundEpoch, _view)) return;
            QuestRowView q = _selected.HasValue ? Find(_selected.Value) : null;
            if (q == null) return;
            _tracked = _tracked.HasValue && _tracked.Value.Equals(q.Handle) ? (QuestHandle?)null : q.Handle;
            if (_onTracked != null) _onTracked(_tracked);
            RenderSelection();
        }

        public void SetVisible(bool visible)
        {
            if (_root == null) return;
            _root.gameObject.SetActive(visible);
            if (visible) { _root.SetAsLastSibling(); _scroll.StopMovement(); }
            else if (_abandonArmed) { _abandonArmed = false; WriteAbandonLabel(); }
        }

        void ClearTracked()
        {
            if (!_tracked.HasValue) return;
            _tracked = null; if (_onTracked != null) _onTracked(null);
        }

        void Close() { SetVisible(false); if (_onClose != null) _onClose(); }
    }
}
