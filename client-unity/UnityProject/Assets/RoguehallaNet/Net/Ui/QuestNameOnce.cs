namespace Roguehalla.Net.Ui
{
    /// <summary>One name per quest over one frame's drawn objective points, sticky: last frame's carrier keeps it while drawn, else the first offer takes it.</summary>
    public sealed class QuestNameOnce
    {
        const float Same = .01f;
        uint[] _oq = new uint[32]; float[] _ox = new float[32], _oy = new float[32]; int _on;
        uint[] _mq = new uint[32]; float[] _mx = new float[32], _my = new float[32]; int _mn;
        uint[] _nq = new uint[32]; float[] _nx = new float[32], _ny = new float[32];
        int _count;

        /// <summary>Quests offered this frame.</summary>
        public int Count { get { return _count; } }

        /// <summary>Starts a frame: the carriers of the last one become the memory.</summary>
        public void Begin()
        {
            if (_nq.Length < _oq.Length) { _nq = new uint[_oq.Length]; _nx = new float[_oq.Length]; _ny = new float[_oq.Length]; }
            int n = 0;
            for (int i = 0; i < _on; i++)
            {
                bool seen = false;
                for (int j = 0; j < n; j++) if (_nq[j] == _oq[i]) { seen = true; break; }
                if (seen) continue;
                Carrier(_oq[i], out float cx, out float cy);
                _nq[n] = _oq[i]; _nx[n] = cx; _ny[n] = cy; n++;
            }
            var q = _mq; _mq = _nq; _nq = q;
            var x = _mx; _mx = _nx; _nx = x;
            var y = _my; _my = _ny; _ny = y;
            _mn = n; _on = 0; _count = 0;
        }

        /// <summary>A drawn point, in delivered order.</summary>
        public void Offer(uint quest, float x, float y)
        {
            bool known = false;
            for (int i = 0; i < _on; i++) if (_oq[i] == quest) { known = true; break; }
            if (!known) _count++;
            if (_on == _oq.Length)
            {
                int size = _on * 2;
                System.Array.Resize(ref _oq, size); System.Array.Resize(ref _ox, size); System.Array.Resize(ref _oy, size);
            }
            _oq[_on] = quest; _ox[_on] = x; _oy[_on] = y; _on++;
        }

        /// <summary>True for the point that keeps or takes the quest's name.</summary>
        public bool Carries(uint quest, float x, float y)
        {
            if (!Carrier(quest, out float cx, out float cy)) return true;
            return Near(cx, cy, x, y);
        }

        static bool Near(float ax, float ay, float bx, float by)
        {
            float dx = ax - bx, dy = ay - by;
            return dx <= Same && dx >= -Same && dy <= Same && dy >= -Same;
        }

        bool Carrier(uint quest, out float cx, out float cy)
        {
            for (int m = 0; m < _mn; m++)
            {
                if (_mq[m] != quest) continue;
                for (int i = 0; i < _on; i++)
                    if (_oq[i] == quest && Near(_mx[m], _my[m], _ox[i], _oy[i])) { cx = _mx[m]; cy = _my[m]; return true; }
                break;
            }
            for (int i = 0; i < _on; i++)
                if (_oq[i] == quest) { cx = _ox[i]; cy = _oy[i]; return true; }
            cx = cy = 0f; return false;
        }
    }
}
