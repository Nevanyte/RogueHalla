using System;
using Roguehalla.Net;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using Button = UnityEngine.UI.Button;

namespace Roguehalla.Client
{
    /// <summary>The planning rail renders the viewer's private deal alongside local pending input feedback.</summary>
    public sealed partial class TacticalMapController
    {
        const int SpawnChoices = 3, BoonChoices = SpawnChoices << 2;

        readonly Button[] _spawns = new Button[2], _boons = new Button[2], _spawnMarks = new Button[2];
        Button _ready;
        TMP_Text _status, _spawnHeading, _boonHeading, _readyDetail, _readyLabel;
        int _layoutChoices;

        void BuildPlanningRail(UiTheme theme)
        {
            _planningRail = HudUi.At(_panel, "PlanningDecisions", 0, 0, 340, 340);
            HudUi.Surface(_planningRail, theme.Surface, theme.CornerRadiusLarge, theme.Border, true, true);
            _status = HudUi.Label(_panel, "MapStatus", "", 16f, theme.TextPrimary, TextAlignmentOptions.TopLeft, true, UiFontRole.Body, true);
            _spawnHeading = HudUi.Label(_planningRail, "SpawnHeading", "Spawn point", 14f, theme.TextSecondary, TextAlignmentOptions.MidlineLeft, true);
            _boonHeading = HudUi.Label(_planningRail, "BoonHeading", "Optional boon", 14f, theme.TextSecondary, TextAlignmentOptions.MidlineLeft, true);
            _ready = LaunchUi.Button(_planningRail, "Ready", 0, 0, 308, 44, LaunchUi.Look.Primary, Ready);
            _readyLabel = HudUi.Label(_ready.transform, "Label", "READY", 18f, theme.OutlineDark, TextAlignmentOptions.Center, true);
            _readyDetail = HudUi.Label(_planningRail, "ReadyDetail", "", 15f, theme.TextSecondary, TextAlignmentOptions.TopLeft, false, UiFontRole.Body, true);
            for (byte i = 0; i < _spawns.Length; i++)
            {
                byte pick = i;
                _spawns[i] = BuildPlanChoice("Spawn" + i, (i + 1).ToString(), () => PickSpawn(pick));
                _boons[i] = BuildPlanChoice("Boon" + i, "", () => PickBoon(pick));
                _spawnMarks[i] = HudUi.Button(_large.transform, "SpawnMarker" + i, (i + 1).ToString(), HudAnchor.Center, 0, 0, 44, 34, () => PickSpawn(pick), 16f);
                UiFocusHalo.Attach(_spawnMarks[i], theme.CornerRadius);
                _spawnMarks[i].gameObject.SetActive(false);
            }
        }

        Button BuildPlanChoice(string name, string text, UnityEngine.Events.UnityAction pick)
        {
            var theme = UiTheme.Current;
            var button = HudUi.Button(_planningRail, name, "", HudAnchor.TopLeft, 0, 0, 150, 54, pick);
            HudUi.Label(button.transform, "Label", text, 16f, theme.TextPrimary, TextAlignmentOptions.Center, true, UiFontRole.Body, true);
            UiFocusHalo.Attach(button, theme.CornerRadius);
            return button;
        }

        static int PlanningChoices(PlanView plan)
        {
            if (plan == null) return 0;
            return (plan.Spawn0.HasValue ? 1 : 0) | (plan.Spawn1.HasValue ? 2 : 0) |
                (plan.Boons.Count > 0 ? 4 : 0) | (plan.Boons.Count > 1 ? 8 : 0);
        }

        void ApplyPlanningLayout(MapScreenLayout layout, int choices)
        {
            var rail = layout.Right;
            const float inset = 16f, gap = 8f;
            float width = rail.W - inset * 2f;
            int rows = ((choices & SpawnChoices) != 0 ? 1 : 0) + ((choices & BoonChoices) != 0 ? 1 : 0);
            // Use the existing stacked layout's vertical budget on narrow windows.
            float scale = layout.Stacked ? Mathf.Clamp(rail.H / (228f + 84f * rows), .5f, 1f) : 1f;
            float y = inset * scale;
            Put(_status.rectTransform, new MapRect(rail.X + inset, rail.Y + y, width, 56f * scale));
            _status.gameObject.SetActive(true);
            y += 62f * scale;
            y = PlaceChoices(_spawnHeading, _spawns, choices & SpawnChoices, inset, y, width, scale);
            y = PlaceChoices(_boonHeading, _boons, (choices & BoonChoices) >> 2, inset, y, width, scale);
            HudUi.Place((RectTransform)_ready.transform, HudAnchor.TopLeft, inset, y, width, 44f * scale);
            y += 52f * scale;
            Put(_readyDetail.rectTransform, new MapRect(inset, y, width, 50f * scale));
            y += 54f * scale;

            float half = (width - gap) / 2f;
            Put((RectTransform)_undo.transform, new MapRect(rail.X + inset, rail.Y + y, half, 28f * scale));
            Put((RectTransform)_clear.transform, new MapRect(rail.X + inset + half + gap, rail.Y + y, half, 28f * scale));
            Put(_planningRail, new MapRect(rail.X, rail.Y, rail.W, y + 44f * scale));
            Put(_routeText.rectTransform, layout.Legend);
        }

        static float PlaceChoices(TMP_Text heading, Button[] buttons, int choices, float x, float y, float width, float scale)
        {
            heading.gameObject.SetActive(choices != 0);
            if (choices != 0) Put(heading.rectTransform, new MapRect(x, y, width, 20f * scale));
            const float gap = 8f;
            float buttonWidth = choices == 3 ? (width - gap) / 2f : width;
            for (int i = 0; i < buttons.Length; i++)
            {
                bool offered = (choices & (1 << i)) != 0;
                buttons[i].gameObject.SetActive(offered);
                if (!offered) continue;
                float left = choices == 3 ? x + i * (buttonWidth + gap) : x;
                Put((RectTransform)buttons[i].transform, new MapRect(left, y + 22f * scale, buttonWidth, 52f * scale));
            }
            return choices == 0 ? y : y + 84f * scale;
        }

        void PresentPlan(PlanView plan)
        {
            UpdateSelections();
            var theme = UiTheme.Current;
            double remainingMs = Math.Max(0d, plan.EndsAt - _bound.ServerTimeMs);
            if (remainingMs > _planTotalMs) _planTotalMs = remainingMs;
            _title.text = "PLAN YOUR RUN";
            _timer.text = Math.Ceiling(remainingMs / 1000d) + "s";
            _planBarFill.rectTransform.anchorMax = new Vector2(_planTotalMs > 0d ? (float)Math.Min(1d, remainingMs / _planTotalMs) : 0f, 1f);

            PresentSpawn(plan, 0, plan.Option0, plan.Taken0, theme);
            PresentSpawn(plan, 1, plan.Option1, plan.Taken1, theme);
            for (byte i = 0; i < _boons.Length && i < plan.Boons.Count; i++)
                PresentChoice(_boons[i], plan.Boons[i], plan.Boon == i, false, _selections.PendingBoon == i, _selections.CanPickBoon(i), theme);

            bool connected = _selections.IsConnected;
            _ready.interactable = _selections.CanReady;
            _readyLabel.text = !connected ? "RECONNECTING" : plan.Ready ? "READY · CONFIRMED" : _selections.PendingReady ? "READY · PENDING" : "READY";
            _readyLabel.color = _ready.interactable ? theme.OutlineDark : connected && plan.Ready ? theme.Success : theme.TextPrimary;
            HudUi.SetFrame((RectTransform)_ready.transform, _ready.interactable ? theme.BorderStrong : connected && plan.Ready ? theme.Success : theme.Border);
            _readyDetail.text = !connected ? "Waiting for an updated plan." :
                plan.Ready ? "The arena starts when all connected\nplayers are ready or time runs out." :
                plan.Pick.HasValue ? "Ready locks your plan." : "Ready locks your plan. If you skip the spawn choice, the server places you.";
            _status.text = _selections.Feedback.Length > 0 ? _selections.Feedback : PlanGuidance(plan, connected);

            var picked = plan.Pick == 0 ? plan.Option0 : plan.Pick == 1 ? plan.Option1 : null;
            RoutePoint? start = picked.HasValue ? new RoutePoint(picked.Value.X, picked.Value.Y) : (RoutePoint?)null;
            if (!Nullable.Equals(_route.Start, start))
            {
                _route.SetStart(start);
                _nextPath = 0;
            }
        }

        string PlanGuidance(PlanView plan, bool connected)
        {
            if (!connected) return "Connection interrupted.";
            if (plan.Ready) return "Your plan is locked.";
            bool spawn0 = _selections.CanPickSpawn(0), spawn1 = _selections.CanPickSpawn(1);
            if ((spawn0 && plan.Option0.HasValue) || (spawn1 && plan.Option1.HasValue))
                return "Choose a spawn on the map or press its number key.";
            if (spawn0 || spawn1) return "Choose a spawn below or press its number key.";
            if (_selections.CanPickBoon(0) || _selections.CanPickBoon(1)) return "Pick a boon if you want one.";
            return plan.Pick.HasValue ? "Your spawn is selected." : "The server will assign your spawn.";
        }

        void PresentSpawn(PlanView plan, byte i, PlanOptionView? option, bool taken, UiTheme theme)
        {
            bool offered = (i == 0 ? plan.Spawn0 : plan.Spawn1).HasValue;
            var marker = _spawnMarks[i];
            marker.gameObject.SetActive(offered && option.HasValue);
            if (!offered) return;
            bool selected = plan.Pick == i;
            bool canPick = _selections.CanPickSpawn(i);
            bool pending = _selections.PendingSpawn == i;
            string key = (i + 1).ToString();
            string name = key + " · " + (option.HasValue ? MapZones.Name(option.Value.Zone) : "Spawn");
            PresentChoice(_spawns[i], name, selected, taken, pending, canPick, theme);
            if (!option.HasValue) return;
            var rect = (RectTransform)marker.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(option.Value.X / (_bound.Map.W * Tile), 1 - option.Value.Y / (_bound.Map.H * Tile));
            rect.anchoredPosition = Vector2.zero;
            marker.interactable = canPick;
            var label = marker.GetComponentInChildren<TMP_Text>();
            label.text = key + (selected ? " [S]" : taken ? " [X]" : pending ? " …" : "");
            label.color = selected ? theme.Success : taken ? theme.TextSecondary : theme.TextPrimary;
            HudUi.SetFrame(rect, selected ? theme.Success : canPick ? theme.BorderStrong : theme.Border);
        }

        static void PresentChoice(Button button, string name, bool selected, bool taken, bool pending, bool canPick, UiTheme theme)
        {
            button.interactable = canPick;
            var label = button.GetComponentInChildren<TMP_Text>();
            label.text = name + (selected ? "\nSelected" : taken ? "\nTaken" : pending ? "\nPending" : "");
            label.color = selected ? theme.Success : taken ? theme.TextSecondary : theme.TextPrimary;
            HudUi.SetFrame((RectTransform)button.transform, selected ? theme.Success : theme.Border);
        }
    }
}
