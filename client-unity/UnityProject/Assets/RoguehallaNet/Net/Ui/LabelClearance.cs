using System;

namespace Roguehalla.Net.Ui
{
    /// <summary>A rectangle in world units.</summary>
    public readonly struct LabelBox
    {
        public readonly float X0, Y0, X1, Y1;
        public LabelBox(float x0, float y0, float x1, float y1) { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
        public static LabelBox Centred(float cx, float cy, float width, float height)
        {
            return new LabelBox(cx - width * .5f, cy - height * .5f, cx + width * .5f, cy + height * .5f);
        }
        /// <summary>True when this rectangle overlaps <paramref name="other"/> grown by <paramref name="pad"/> on every side, strictly. A NaN meets nothing.</summary>
        public bool Meets(in LabelBox other, float pad)
        {
            return other.X0 - pad < X1 && X0 < other.X1 + pad && other.Y0 - pad < Y1 && Y0 < other.Y1 + pad;
        }
    }

    /// <summary>One label's clearance state: it hides while it lies on the cluster and fades in and out.</summary>
    public struct LabelClearance
    {
        public const float HidePad = .10f, KeepPad = .40f, FadeSeconds = .15f;
        float _alpha;
        public float Alpha { get { return Has ? _alpha : 1f; } }
        public bool Hidden { get; private set; }
        public bool Has { get; private set; }
        public void Forget() { _alpha = 1f; Hidden = false; Has = false; }

        /// <summary>Steps the state once and returns the alpha.</summary>
        public float Next(in LabelBox label, bool haveCluster, in LabelBox cluster, float dt, bool reduced)
        {
            if (!(dt >= 0f)) dt = 0f;   // NaN or negative
            if (!Has)
            {
                Hidden = haveCluster && label.Meets(cluster, HidePad);
                _alpha = Hidden ? 0f : 1f;
                Has = true;
                return _alpha;
            }
            if (!Hidden) { if (haveCluster && label.Meets(cluster, HidePad)) Hidden = true; }
            else if (!(haveCluster && label.Meets(cluster, KeepPad))) Hidden = false;
            _alpha = UiEase.FadeStep(_alpha, !Hidden, dt, FadeSeconds, FadeSeconds, reduced);
            return _alpha;
        }
    }

    /// <summary>The clearance states kept by object (kind and place), searched linearly, no allocation.</summary>
    public sealed class LabelClearanceBook
    {
        public const int Capacity = 64;
        public const float SameSpot = .01f;

        struct Entry { public int Kind; public float X, Y; public LabelClearance State; public bool Asked, Stepped; public int Misses; }
        readonly Entry[] _e = new Entry[Capacity];
        int _n;

        public int Count { get { return _n; } }

        /// <summary>Starts a frame: an entry not asked last frame counts a miss and is dropped at its second.</summary>
        public void Begin()
        {
            int w = 0;
            for (int i = 0; i < _n; i++)
            {
                var e = _e[i];
                if (e.Asked) e.Misses = 0; else e.Misses++;
                e.Asked = false; e.Stepped = false;
                if (e.Misses >= 2) continue;
                _e[w++] = e;
            }
            for (int i = w; i < _n; i++) _e[i] = default;
            _n = w;
        }

        public void Clear() { for (int i = 0; i < _n; i++) _e[i] = default; _n = 0; }

        int Find(int kind, float x, float y)
        {
            for (int i = 0; i < _n; i++)
            {
                if (_e[i].Kind != kind) continue;
                float dx = _e[i].X - x, dy = _e[i].Y - y;
                if (dx <= SameSpot && dx >= -SameSpot && dy <= SameSpot && dy >= -SameSpot) return i;
            }
            return -1;
        }

        public float Next(int kind, float x, float y, in LabelBox label, bool haveCluster, in LabelBox cluster, float dt, bool reduced)
        {
            int i = Find(kind, x, y);
            if (i < 0)
            {
                if (_n >= Capacity) { var fresh = new LabelClearance(); return fresh.Next(in label, haveCluster, in cluster, dt, reduced); }
                i = _n++;
                _e[i] = new Entry { Kind = kind, X = x, Y = y };
            }
            _e[i].Asked = true;
            if (_e[i].Stepped) return _e[i].State.Alpha;
            _e[i].Stepped = true;
            return _e[i].State.Next(in label, haveCluster, in cluster, dt, reduced);
        }

        /// <summary>A skipped frame: marks the entry asked and returns its stored alpha (1 when there is none).</summary>
        public float Keep(int kind, float x, float y)
        {
            int i = Find(kind, x, y);
            if (i < 0) return 1f;
            _e[i].Asked = true;
            return _e[i].State.Alpha;
        }
    }

    /// <summary>Keeps the last cluster rectangle through one false frame.</summary>
    public struct ClusterHold
    {
        bool _has; LabelBox _rect; int _misses;

        public bool Read(bool answered, in LabelBox rect, out LabelBox use)
        {
            if (answered) { _has = true; _rect = rect; _misses = 0; use = rect; return true; }
            _misses++;
            if (_has && _misses == 1) { use = _rect; return true; }
            _has = false; use = default; return false;
        }

        public void Reset() { _has = false; _rect = default; _misses = 0; }
    }
}
