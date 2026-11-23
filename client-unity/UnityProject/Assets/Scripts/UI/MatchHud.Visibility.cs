using System;
using System.Text;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Roguehalla.Client
{
    /// <summary>HUD part that decides which world HUD pieces are on screen and remembers the focus across modals.</summary>
    public sealed partial class MatchHud
    {
        bool _worldHudVisible = true;
        GameObject _worldHudFocus;
        bool WorldHudVisible => !BlocksKeyboardInput && !(_map?.IsOpen ?? false);

        /// <summary>One place that decides which HUD pieces are on screen: panels follow modals, state pieces follow the owner's state.</summary>
        void RefreshHudElementVisibility()
        {
            bool visible = _worldHudVisible;
            SetActiveIfChanged(_vitalsPanel, visible);
            SetActiveIfChanged(_championHud, visible && _championWanted);
            SetActiveIfChanged(_weaponBar, visible && _barWanted);
        }

        static void SetActiveIfChanged(RectTransform rect, bool active)
        {
            if (rect != null && rect.gameObject.activeSelf != active) rect.gameObject.SetActive(active);
        }

        void RefreshWorldHudVisibility()
        {
            bool visible = PanelHudVisible;
            if (_worldHudVisible != visible)
            {
                HudIconHint.CloseAll();
                _worldHudVisible = visible;
                RefreshHudElementVisibility();
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                var target = _worldHudFocus != null ? _worldHudFocus.GetComponent<UnityEngine.UI.Selectable>() : null;
                if (visible && target != null && target.IsActive() && target.IsInteractable() &&
                    EventSystem.current != null && (selected == null || !selected.activeInHierarchy))
                    EventSystem.current.SetSelectedGameObject(_worldHudFocus);
                _dirty = _rewardsDirty = true;
            }
            _map?.SetMinimapSuppressed(!visible);
            RefreshHeaderActionsVisibility();
            RefreshAroundTheMap();
            RememberWorldHudFocus();
        }

        bool _aroundMapOpen;

        /// <summary>Puts the phase line on the pill and hands the same line to the Tab map's header.</summary>
        void ApplyPhaseLine(string text, Roguehalla.Net.Ui.Indicators.ArenaLineTone tone)
        {
            ApplyPhaseStrip(text, tone);
            _map?.SetPhaseLine(text);
        }

        /// <summary>Empties the phase pill and the map header's line together.</summary>
        void ClearPhaseLine()
        {
            _phaseStrip.text = "";
            _map?.SetPhaseLine(null);
        }

        /// <summary>While the map is open the phase pill, the journal launcher, the edge threats and the world marks are veiled (the chrome stays).</summary>
        void RefreshAroundTheMap()
        {
            bool open = _map != null && _map.IsOpen;
            if (open == _aroundMapOpen) return;
            _aroundMapOpen = open;
            Veil(_phasePill, open);
            Veil(_journalButton != null ? (RectTransform)_journalButton.transform : null, open);
            Veil(_edgeThreats != null ? _edgeThreats.Root as RectTransform : null, open);
            var arena = _client != null ? _client.Arena : null;
            if (arena != null) arena.SetWorldMarksVisible(!open);
        }

        static void Veil(RectTransform rect, bool veiled)
        {
            if (rect == null) return;
            var group = rect.GetComponent<CanvasGroup>();
            if (group == null) group = rect.gameObject.AddComponent<CanvasGroup>();
            group.alpha = veiled ? 0f : 1f; group.blocksRaycasts = group.interactable = !veiled;
        }

        void RememberWorldHudFocus()
        {
            if (!_worldHudVisible || !WorldHudVisible || EventSystem.current == null) return;
            var selected = EventSystem.current.currentSelectedGameObject;
            _worldHudFocus = selected != null && selected.transform.IsChildOf(_hud) ? selected : null;
        }

        void RefreshHeaderActionsVisibility()
        {
            bool visible = (_drawer == null || !_drawer.IsVisible) && !PlayerFlowVisible;
            for (int i = 0; i < _headerActions.Length; i++)
                if (_headerActions[i] != null && _headerActions[i].gameObject.activeSelf != visible)
                    _headerActions[i].gameObject.SetActive(visible);
            RefreshJournalLauncher();
        }
    }
}
