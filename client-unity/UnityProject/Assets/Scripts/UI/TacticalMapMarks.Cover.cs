using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>
    /// STUB (P-UI-MAP-PAINT-1): the cover flags of the camps and wards under a contact. The implementer replaces this with one
    /// cover pass per Present; for now the flags are computed live by the old per-call rule so the oracle compiles.
    /// </summary>
    public sealed partial class TacticalMapMarks
    {
        /// <summary>Distance tests made so far (cumulative; a test reads differences).</summary>
        public int CoverTests { get; private set; }

        /// <summary>True when a non-self actor covers the camp row's mark.</summary>
        public bool CampCovered(int campRow)
        {
            if (_map == null || campRow < 0 || campRow >= _camps.Count) return false;
            return ContactNear(At(new Vector2((float)_camps[campRow].WorldX, (float)_camps[campRow].WorldY)));
        }

        /// <summary>True when a non-self actor covers the ward row's mark.</summary>
        public bool WardCovered(int wardRow)
        {
            if (_map == null || wardRow < 0 || wardRow >= _wards.Count) return false;
            return ContactNear(At(new Vector2((float)_wards[wardRow].WorldX, (float)_wards[wardRow].WorldY)));
        }

        void ComputeCover() { }

        void EnsureCover() { }
    }
}
