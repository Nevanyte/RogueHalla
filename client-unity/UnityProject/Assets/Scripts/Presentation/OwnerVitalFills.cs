using UnityEngine;
using Roguehalla.Net;
using Roguehalla.Net.Ui;

namespace Roguehalla.Client
{
    /// <summary>Eases the plate's health, ghost and barrier fills toward the delivered owner vitals.</summary>
    public sealed class OwnerVitalFills
    {
        readonly RectTransform _health, _barrier, _ghost;
        BarEase _h, _b;
        bool _lingers = true;
        float _prevHp;
        uint _prevId;
        ActorStatus _prevStatus;
        float _wHealth = float.NaN, _wBarrierMin = float.NaN, _wBarrierMax = float.NaN, _wGhost = float.NaN;

        public OwnerVitalFills(RectTransform health, RectTransform barrier, RectTransform ghost)
        { _health = health; _barrier = barrier; _ghost = ghost; }

        public bool Ready { get { return _h.Has; } }
        public float HealthShown { get { return _h.Shown; } }
        public float HealthGhost { get { return _h.Ghost; } }
        public float BarrierShown { get { return _b.Shown; } }

        public void Deliver(SelfView me, bool reduced)
        {
            if (me == null) { Clear(); return; }
            OwnerVitalBars.Percent(me, out float hp, out float bp);
            float h = hp / 100f, b = bp / 100f;
            if (_h.Has && (me.Id != _prevId || (me.Status == ActorStatus.Alive && _prevStatus != ActorStatus.Alive)))
            { _h.Clear(); _b.Clear(); }
            if (!_h.Has) _lingers = true;
            else if (h != _h.Target) _lingers = !(h < _h.Target && me.Hp >= _prevHp);
            _prevHp = me.Hp; _prevId = me.Id; _prevStatus = me.Status;
            _h.Step(h, 0f, reduced, _lingers);
            _b.Step(b, 0f, reduced, false);
            Write();
        }

        public void Tick(float dt, bool reduced)
        {
            if (!_h.Has) return;
            _h.Step(_h.Target, dt, reduced, _lingers);
            _b.Step(_b.Target, dt, reduced, false);
            Write();
        }

        public void Clear()
        {
            _h.Clear(); _b.Clear(); _lingers = true; _prevHp = 0f; _prevId = 0; _prevStatus = default(ActorStatus);
            _wHealth = _wBarrierMin = _wBarrierMax = _wGhost = 0f;
            if (_health != null) _health.anchorMax = Vector2.zero;
            if (_barrier != null) { _barrier.anchorMin = Vector2.zero; _barrier.anchorMax = Vector2.zero; }
            if (_ghost != null) _ghost.anchorMax = Vector2.zero;
        }

        void Write()
        {
            float hs = _h.Shown;
            float bEnd = hs + _b.Shown; if (bEnd > 1f) bEnd = 1f;
            if (_health != null && hs != _wHealth) { _health.anchorMax = new Vector2(hs, 1f); _wHealth = hs; }
            if (_barrier != null && (hs != _wBarrierMin || bEnd != _wBarrierMax))
            {
                _barrier.anchorMin = new Vector2(hs, 0f); _barrier.anchorMax = new Vector2(bEnd, 1f);
                _wBarrierMin = hs; _wBarrierMax = bEnd;
            }
            if (_ghost != null && _h.Ghost != _wGhost) { _ghost.anchorMax = new Vector2(_h.Ghost, 1f); _wGhost = _h.Ghost; }
        }
    }
}
