using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>
    /// The cover flags of the camps and wards under a contact (P-UI-MAP-PAINT-1): one pass per Present turns every non-self actor into a
    /// layer point once and tests each camp and ward row against them, so the six consumers read a flag instead of looping the actors.
    /// </summary>
    public sealed partial class TacticalMapMarks
    {
        bool[] _campCover = new bool[0], _wardCover = new bool[0];
        Vector2[] _coverPoints = new Vector2[0];
        int _campCoverCount, _wardCoverCount;
        float _coverRectW = -1f, _coverRectH = -1f;
        int _coverMapW = -1, _coverMapH = -1;

        /// <summary>Distance tests made so far (cumulative; a test reads differences).</summary>
        public int CoverTests { get; private set; }

        /// <summary>True when a non-self actor covers the camp row's mark.</summary>
        public bool CampCovered(int campRow) => (uint)campRow < (uint)_campCoverCount && _campCover[campRow];

        /// <summary>True when a non-self actor covers the ward row's mark.</summary>
        public bool WardCovered(int wardRow) => (uint)wardRow < (uint)_wardCoverCount && _wardCover[wardRow];

        /// <summary>The layer point of a world point: the expression <see cref="At"/> uses.</summary>
        static Vector2 AtIn(Rect layer, int mapW, int mapH, Vector2 world)
        {
            return new Vector2(layer.xMin + world.x / (mapW * 32f) * layer.width, layer.yMax - world.y / (mapH * 32f) * layer.height);
        }

        void ClearCover() { _campCoverCount = _wardCoverCount = 0; }

        void RecordCoverSizes(Rect r)
        {
            _coverRectW = r.width; _coverRectH = r.height;
            _coverMapW = _map != null ? _map.W : 0; _coverMapH = _map != null ? _map.H : 0;
        }

        void ComputeCover()
        {
            var r = rectTransform.rect;
            RecordCoverSizes(r);
            _campCoverCount = _wardCoverCount = 0;
            if (_map == null || (_camps.Count == 0 && _wards.Count == 0)) return;
            int mapW = _map.W, mapH = _map.H, n = 0;
            if (_coverPoints.Length < _actors.Count) _coverPoints = new Vector2[System.Math.Max(_actors.Count, _coverPoints.Length * 2)];
            for (int i = 0; i < _actors.Count; i++)
            {
                var actor = _actors[i];
                if (actor.Id == _selfId) continue;
                _coverPoints[n++] = AtIn(r, mapW, mapH, new Vector2(actor.X, actor.Y));
            }
            if (_campCover.Length < _camps.Count) _campCover = new bool[System.Math.Max(_camps.Count, _campCover.Length * 2)];
            if (_wardCover.Length < _wards.Count) _wardCover = new bool[System.Math.Max(_wards.Count, _wardCover.Length * 2)];
            for (int i = 0; i < _camps.Count; i++)
                _campCover[i] = Covered(AtIn(r, mapW, mapH, new Vector2((float)_camps[i].WorldX, (float)_camps[i].WorldY)), n);
            for (int i = 0; i < _wards.Count; i++)
                _wardCover[i] = Covered(AtIn(r, mapW, mapH, new Vector2((float)_wards[i].WorldX, (float)_wards[i].WorldY)), n);
            _campCoverCount = _camps.Count; _wardCoverCount = _wards.Count;
        }

        bool Covered(Vector2 p, int n)
        {
            for (int k = 0; k < n; k++)
            {
                CoverTests++;
                if ((_coverPoints[k] - p).sqrMagnitude < CampDropPx * CampDropPx) return true;
            }
            return false;
        }

        void EnsureCover()
        {
            var r = rectTransform.rect;
            int w = _map != null ? _map.W : 0, h = _map != null ? _map.H : 0;
            if (r.width != _coverRectW || r.height != _coverRectH || w != _coverMapW || h != _coverMapH) ComputeCover();
        }
    }
}
