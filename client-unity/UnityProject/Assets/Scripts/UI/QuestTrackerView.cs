using System.Collections.Generic;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>The quest tracker: the accepted quests that are still running, docked under the plate chain. It draws
    /// delivered facts (name, objective, the clock from the delivered deadline and server time, progress) and takes no input.</summary>
    public sealed class QuestTrackerView
    {
        sealed class RowUi
        {
            public RectTransform Box;
            public TMP_Text Name, Clock, Objective, Progress;
        }

        readonly RectTransform _root;
        readonly HudFade _fade;
        readonly TMP_Text _more;
        readonly RowUi[] _rowUi = new RowUi[HudRegions.TrackerMaxRows];
        readonly QuestTrackerRow[] _drawn = new QuestTrackerRow[HudRegions.TrackerMaxRows];
        readonly List<QuestTrackerRow> _rows = new List<QuestTrackerRow>(HudRegions.TrackerMaxRows + 1);
        Session _session;
        int _count;                 // rows wanted and showing
        int _drawnCount;            // rows the card last drew
        int _running = -1, _shownHeader = -1;
        float _placedTop = float.NaN;
        int _placedRows = -1;

        public QuestTrackerView(Transform parent)
        {
            var theme = UiTheme.Current;
            _root = HudUi.Box(parent, "QuestTracker", HudAnchor.TopLeft, HudRegions.Margin, 0f, HudRegions.TrackerWidth, 0f);
            HudUi.Surface(_root, UiTheme.With(theme.Surface, .82f), theme.CornerRadius, UiTheme.With(theme.Border, .6f), false);
            var header = HudUi.At(_root, "Header", 0f, 0f, HudRegions.TrackerWidth, HudRegions.TrackerHeader);
            HudUi.Label(HudUi.At(header, "Title", 8f, 2f, 120f, 14f), "Label", "QUESTS", theme.SizeTiny, theme.Accent, TextAlignmentOptions.MidlineLeft, true);
            _more = HudUi.Label(HudUi.At(header, "More", 132f, 2f, 120f, 14f), "Label", "", theme.SizeTiny, theme.TextSecondary, TextAlignmentOptions.MidlineRight);
            for (int i = 0; i < _rowUi.Length; i++)
            {
                var row = new RowUi { Box = HudUi.At(_root, "Row" + i, 0f, HudRegions.TrackerHeader + i * HudRegions.TrackerRow, HudRegions.TrackerWidth, HudRegions.TrackerRow) };
                row.Name = HudUi.Label(HudUi.At(row.Box, "Name", 8f, 3f, 190f, 16f), "Label", "", theme.SizeSmall, theme.TextPrimary, TextAlignmentOptions.MidlineLeft, true);
                row.Clock = HudUi.Label(HudUi.At(row.Box, "Clock", 202f, 3f, 50f, 16f), "Label", "", theme.SizeSmall, theme.TextSecondary, TextAlignmentOptions.MidlineRight);
                row.Objective = HudUi.Label(HudUi.At(row.Box, "Objective", 8f, 20f, 166f, 15f), "Label", "", theme.SizeTiny, theme.TextSecondary, TextAlignmentOptions.MidlineLeft);
                row.Progress = HudUi.Label(HudUi.At(row.Box, "Progress", 178f, 20f, 74f, 15f), "Label", "", theme.SizeTiny, theme.TextSecondary, TextAlignmentOptions.MidlineRight);
                row.Name.GetComponent<MythicTextScale>().MinSize = 8f;
                row.Objective.GetComponent<MythicTextScale>().MinSize = 8f;
                row.Box.gameObject.SetActive(false);
                _rowUi[i] = row;
            }
            _root.gameObject.SetActive(false);
            _fade = new HudFade(_root, false);
            _fade.Snap(false);
        }

        public RectTransform Root { get { return _root; } }
        /// <summary>Rows wanted and showing; 0 while hidden or leaving.</summary>
        public int Rows { get { return _count; } }

        public void Refresh(Session session, bool hudVisible, float dockTop, float dockFloor, float dt)
        {
            if (!ReferenceEquals(session, _session))
            {
                _session = session;
                Forget();
            }
            bool gate = session != null && hudVisible && session.Phase == MatchPhase.Active;
            if (!gate)
            {
                _count = 0;
                _fade.Snap(false);
                return;
            }
            int running = QuestTracker.Build(session.Collections.Quests, session.ServerTimeMs, _rows);
            int shown = System.Math.Min(_rows.Count, QuestTracker.RowsThatFit(dockTop, dockFloor));
            _count = shown;
            if (shown > 0)
            {
                if (running != _running || shown != _shownHeader)
                {
                    _running = running; _shownHeader = shown;
                    SetText(_more, QuestTracker.Header(running, shown));
                }
                for (int i = 0; i < _rowUi.Length; i++)
                {
                    bool on = i < shown;
                    if (_rowUi[i].Box.gameObject.activeSelf != on) _rowUi[i].Box.gameObject.SetActive(on);
                    if (!on) continue;
                    if (i >= _drawnCount || !_drawn[i].SameFace(_rows[i])) Draw(_rowUi[i], _rows[i]);
                    _drawn[i] = _rows[i];
                }
                _drawnCount = shown;
                if (shown != _placedRows || !Mathf.Approximately(dockTop, _placedTop))
                {
                    _placedRows = shown; _placedTop = dockTop;
                    HudUi.Place(_root, HudAnchor.TopLeft, HudRegions.Margin, dockTop, HudRegions.TrackerWidth, HudRegions.TrackerHeader + shown * HudRegions.TrackerRow);
                }
                _fade.Set(true);
            }
            else _fade.Set(false);
            _fade.Tick(dt);
        }

        // A new session (or none): drop every drawn fact and the card with it.
        void Forget()
        {
            _count = 0; _drawnCount = 0; _running = -1; _shownHeader = -1; _placedRows = -1; _placedTop = float.NaN;
            for (int i = 0; i < _drawn.Length; i++) _drawn[i] = default(QuestTrackerRow);
            _rows.Clear();
            SetText(_more, "");
            foreach (var row in _rowUi)
            {
                SetText(row.Name, ""); SetText(row.Clock, ""); SetText(row.Objective, ""); SetText(row.Progress, "");
                row.Box.gameObject.SetActive(false);
            }
            _fade.Snap(false);
        }

        static void SetText(TMP_Text label, string text)
        {
            if (label.text == text) return;
            label.text = text;
            var fit = label.GetComponent<MythicTextScale>();
            if (fit != null) fit.Refit();
        }

        static void Draw(RowUi row, QuestTrackerRow r)
        {
            var theme = UiTheme.Current;
            var q = r.Quest;
            SetText(row.Name, QuestTracker.Short(q.Name, QuestTracker.NameChars));
            row.Name.color = q.OwnerReady ? theme.Success : theme.TextPrimary;
            SetText(row.Clock, QuestTracker.Clock(r.SecondsLeft));
            row.Clock.color = r.SecondsLeft < 0 ? theme.TextSecondary
                : r.SecondsLeft <= QuestTracker.CriticalSeconds ? theme.Danger
                : r.SecondsLeft <= QuestTracker.UrgentSeconds ? theme.Warning : theme.TextSecondary;
            SetText(row.Objective, QuestTracker.Short(q.Objective, QuestTracker.ObjectiveChars));
            SetText(row.Progress, QuestTracker.Progress(q));
            row.Progress.color = q.OwnerReady ? theme.Success : theme.TextSecondary;
            row.Progress.fontStyle = q.OwnerReady ? FontStyles.Bold : FontStyles.Normal;
        }
    }
}
