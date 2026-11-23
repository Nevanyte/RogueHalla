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
        const float PulseSeconds = HudPulses.Seconds, PulsePop = HudPulses.Pop;
        readonly HudPulses _pulses = new HudPulses();

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

        void Pulse(RectTransform rect) { _pulses.Play(rect, HudSeconds); }

        void TickPulses() { _pulses.Tick(HudSeconds); }

        void ClearPulses() { _pulses.Clear(); }
    }
}
