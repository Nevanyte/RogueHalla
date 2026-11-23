using UnityEngine;
using Roguehalla.Net;

namespace Roguehalla.Client
{
    /// <summary>Eases the plate's health, ghost and barrier fills toward the delivered owner vitals.</summary>
    public sealed class OwnerVitalFills
    {
        public OwnerVitalFills(RectTransform health, RectTransform barrier, RectTransform ghost) { }
        public bool Ready { get { return false; } }
        public float HealthShown { get { return 0f; } }
        public float HealthGhost { get { return 0f; } }
        public float BarrierShown { get { return 0f; } }
        public void Deliver(SelfView me, bool reduced) { }
        public void Tick(float dt, bool reduced) { }
        public void Clear() { }
    }
}
