using UnityEngine;
using Roguehalla.Net;

namespace Roguehalla.Client
{
    /// <summary>The plate's eased health, ghost and barrier fills (P-UI-VITALS-EASE-1).</summary>
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
    }
}
