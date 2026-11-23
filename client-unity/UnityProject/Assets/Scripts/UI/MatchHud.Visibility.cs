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
            RememberWorldHudFocus();
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
