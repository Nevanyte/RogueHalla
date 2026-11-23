using UnityEngine;
using Roguehalla.Net;

namespace Roguehalla.Client
{
    /// <summary>The plate's eased health, ghost and barrier fills (P-UI-VITALS-EASE-1).</summary>
    public sealed partial class MatchHud
    {
        RectTransform _healthGhostFill;
        OwnerVitalFills _ownerVitals;

        OwnerVitalFills OwnerVitals() { return _ownerVitals; }
        void DeliverOwnerVitals(SelfView me) { }
        internal void TickOwnerVitals(float dt) { }
        void ClearOwnerVitals() { }
    }
}
