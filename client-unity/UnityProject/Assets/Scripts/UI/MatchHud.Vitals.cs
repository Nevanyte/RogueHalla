using System.Text;
using UnityEngine;
using Roguehalla.Net;

namespace Roguehalla.Client
{
    /// <summary>The plate's eased health, ghost and barrier fills (P-UI-VITALS-EASE-1), and the owner texts written only when shown and changed (P-UI-HUD-GARBAGE-1).</summary>
    public sealed partial class MatchHud
    {
        RectTransform _healthGhostFill;
        OwnerVitalFills _ownerVitals;

        OwnerVitalFills OwnerVitals()
        {
            if (_ownerVitals == null && _healthBarFill != null && _barrierBarFill != null)
                _ownerVitals = new OwnerVitalFills(_healthBarFill, _barrierBarFill, _healthGhostFill);
            return _ownerVitals;
        }
        void DeliverOwnerVitals(SelfView me) { var v = OwnerVitals(); if (v != null) v.Deliver(me, UiMotion.Reduced); }
        internal void TickOwnerVitals(float dt) { var v = OwnerVitals(); if (v != null) v.Tick(dt, UiMotion.Reduced); }
        void ClearOwnerVitals() { var v = OwnerVitals(); if (v != null) v.Clear(); }

        // The vitals and kills lines are built from the remembered _last* fields, only while their own label is shown. A hidden label
        // that missed a change is stale and is brought current when it is shown (SetPlateMode writes it before SetActive(true)).
        FactionId _lastFaction;
        bool _vitalsStale, _statsStale;

        void NoteOwnerTextsChanged(bool vitalsShown, bool statsShown)
        {
            _vitalsStale = true; _statsStale = true;
            WriteOwnerTexts(vitalsShown, statsShown);
        }

        void WriteOwnerTexts(bool vitalsShown, bool statsShown)
        {
            if (_vitalsStale && vitalsShown && _vitals != null)
            {
                _vitalsStale = false;
                Set(_vitals, ShowcasePalette.GetFactionName((int)_lastFaction) + " · HP " + _lastHp.ToString("0.#") + " / " + _lastMaxHp.ToString("0.#") + " · Barrier " + _lastBarrier.ToString("0.#"));
            }
            if (_statsStale && statsShown && _combatStats != null)
            {
                _statsStale = false;
                Set(_combatStats, "Kills " + _lastKills + " · GV " + _lastGv + " · " + PlayerWords.Word(_lastStatus));
            }
        }

        // The hp label's key: clamped hp and max, barrier, bounty and the accent's RGB. Reset with _lastHp.
        float _hpKeyHp, _hpKeyMax, _hpKeyBarrier;
        ushort _hpKeyBounty;
        int _hpKeyAccent;
        bool _hpKeyValid;
        readonly StringBuilder _hpText = new StringBuilder(48);

        void ResetOwnerTexts()
        {
            _vitalsStale = _statsStale = false; _lastFaction = default(FactionId); _hpKeyValid = false;
        }

        void WriteHpLabel(float hp, float max, SelfView me, UiTheme theme)
        {
            Color32 accent = theme.Accent;
            int rgb = accent.r << 16 | accent.g << 8 | accent.b;
            if (_hpKeyValid && hp == _hpKeyHp && max == _hpKeyMax && me.Barrier == _hpKeyBarrier && me.Bounty == _hpKeyBounty && rgb == _hpKeyAccent) return;
            _hpKeyValid = true; _hpKeyHp = hp; _hpKeyMax = max; _hpKeyBarrier = me.Barrier; _hpKeyBounty = me.Bounty; _hpKeyAccent = rgb;
            _hpText.Length = 0;
            _hpText.Append(hp.ToString("0")).Append('/').Append(max.ToString("0"));
            if (me.Barrier > 0) _hpText.Append(" +").Append(me.Barrier.ToString("0"));
            if (me.Bounty > 0) _hpText.Append(" <color=").Append(Hex(accent)).Append("> · Bounty ").Append(me.Bounty).Append("</color>");
            Set(_hpValue, _hpText.ToString());
        }
    }
}
