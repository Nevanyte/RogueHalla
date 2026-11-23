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
    /// <summary>HUD part for the press pulses: a short scale pop on the element a command was sent from.</summary>
    public sealed partial class MatchHud
    {
        // Press pulses (P-UI-INPUT-CUES-1): a ~150 ms scale pop on an existing element the moment a cast, swap, pickup or
        // potion command leaves. It shows that the key was heard and nothing about the outcome.
        const float PulseSeconds = .15f, PulsePop = .1f;
        readonly System.Collections.Generic.List<(RectTransform Rect, float Start)> _pulses = new System.Collections.Generic.List<(RectTransform, float)>();

        void InputCue(WireCommand command)
        {
            switch (command.Kind)
            {
                case CommandKind.Cast:
                    int slot = command.Slot ?? 0;
                    Pulse(slot == 0 ? _wishTile : slot >= 1 && slot <= 2 ? _questSlotTiles[slot - 1] : null);
                    if (_client != null && _session != null && _client.Arena != null) _client.Arena.LocalCastCue(_session.PlayerId);
                    break;
                case CommandKind.UseConsumable: Pulse(_potionButton != null ? (RectTransform)_potionButton.transform : null); break;
                case CommandKind.Pickup: Pulse(_pickup != null ? (RectTransform)_pickup.transform : null); break;
                case CommandKind.Swap:
                    if (command.Slot.HasValue && command.Slot.Value < _slots.Length && _slots[command.Slot.Value] != null)
                        Pulse((RectTransform)_slots[command.Slot.Value].transform);
                    else for (int i = 0; i < _slots.Length; i++) if (_slots[i] != null) Pulse((RectTransform)_slots[i].transform);
                    break;
            }
        }

        void Pulse(RectTransform rect)
        {
            if (rect == null) return;
            float now = Time.unscaledTime;
            for (int i = 0; i < _pulses.Count; i++) if (_pulses[i].Rect == rect) { _pulses[i] = (rect, now); return; }
            _pulses.Add((rect, now));
        }

        void TickPulses()
        {
            if (_pulses.Count == 0) return;
            float now = Time.unscaledTime;
            bool reduced = ShowcaseSettings.Current != null && ShowcaseSettings.Current.ReducedMotion;
            for (int i = _pulses.Count - 1; i >= 0; i--)
            {
                var (rect, start) = _pulses[i];
                float t = (now - start) / PulseSeconds;
                if (rect == null) { _pulses.RemoveAt(i); continue; }
                bool wish = ReferenceEquals(rect, _wishTile) && _wishGlow != null && _wishGlow.enabled;
                if (t >= 1f)
                {
                    if (!wish) rect.localScale = Vector3.one;
                    _pulses.RemoveAt(i); continue;
                }
                float pop = reduced ? 1f : 1f + PulsePop * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                rect.localScale = wish ? rect.localScale * pop : Vector3.one * pop;
            }
        }

        void ClearPulses()
        {
            foreach (var p in _pulses) if (p.Rect != null && !(ReferenceEquals(p.Rect, _wishTile) && _wishGlow != null && _wishGlow.enabled)) p.Rect.localScale = Vector3.one;
            _pulses.Clear();
        }
    }
}
