using System;

namespace Roguehalla.Net.Ui
{
    /// <summary>Per layout key: a pack name's anchor glides to its new place when the key's token (carrier, singled-out state, member count) changes; a walking pack with an unchanged token is never delayed.</summary>
    public sealed class NameAnchorGlide
    {
        /// <summary>Seconds a step of any size takes to close; the largest good dt.</summary>
        public const float GlideSeconds = .15f, MaxDt = 1f;

        long[] _key = new long[32], _token = new long[32];
        float[] _lastX = new float[32], _lastY = new float[32], _resX = new float[32], _resY = new float[32], _speed = new float[32];
        bool[] _seen = new bool[32];
        int _count;

        /// <summary>Marks every entry unseen.</summary>
        public void Begin() { for (int i = 0; i < _count; i++) _seen[i] = false; }

        /// <summary>Drops the entries no Step touched since Begin.</summary>
        public void End()
        {
            for (int i = _count - 1; i >= 0; i--)
            {
                if (_seen[i]) continue;
                int last = --_count;
                Move(last, i);
            }
        }

        /// <summary>Drops every entry.</summary>
        public void Clear() { _count = 0; }

        /// <summary>The drawn anchor of a key this frame: the target plus a residual that shrinks to nothing in GlideSeconds after a token change.</summary>
        public void Step(long key, long token, float targetX, float targetY, float dt, bool reduced, out float x, out float y)
        {
            int e = Find(key);
            bool good = dt > 0f && dt <= MaxDt;
            if (e < 0 || reduced || !good)
            {
                if (e < 0) e = Add(key);
                _token[e] = token; _resX[e] = _resY[e] = 0f; _speed[e] = 0f; _seen[e] = true;
                _lastX[e] = x = targetX; _lastY[e] = y = targetY;
                return;
            }
            if (_token[e] != token)
            {
                float rx = _lastX[e] - targetX, ry = _lastY[e] - targetY;
                float len = (float)Math.Sqrt(rx * rx + ry * ry);
                _resX[e] = rx; _resY[e] = ry;
                _speed[e] = len > 0f ? len / GlideSeconds : 0f;
                _token[e] = token;
            }
            float cx = _resX[e], cy = _resY[e];
            float l = (float)Math.Sqrt(cx * cx + cy * cy);
            if (l > 0f)
            {
                float d = _speed[e] * dt;
                if (d >= l) { cx = cy = 0f; }
                else { float k = (l - d) / l; cx *= k; cy *= k; }
                _resX[e] = cx; _resY[e] = cy;
            }
            _seen[e] = true;
            _lastX[e] = x = targetX + cx; _lastY[e] = y = targetY + cy;
        }

        /// <summary>Copies the entry of oldKey to newKey when oldKey has one and newKey has none; true when it copied.</summary>
        public bool Carry(long oldKey, long newKey)
        {
            int o = Find(oldKey);
            if (o < 0 || Find(newKey) >= 0) return false;
            int n = Add(newKey);
            _token[n] = _token[o]; _lastX[n] = _lastX[o]; _lastY[n] = _lastY[o];
            _resX[n] = _resX[o]; _resY[n] = _resY[o]; _speed[n] = _speed[o]; _seen[n] = false;
            return true;
        }

        /// <summary>Entries now.</summary>
        public int Count { get { return _count; } }

        /// <summary>The residual of a key (drawn minus target); false when the key has no entry.</summary>
        public bool TryResidual(long key, out float x, out float y)
        {
            int e = Find(key);
            if (e < 0) { x = y = 0f; return false; }
            x = _resX[e]; y = _resY[e]; return true;
        }

        int Find(long key)
        {
            for (int i = 0; i < _count; i++) if (_key[i] == key) return i;
            return -1;
        }

        int Add(long key)
        {
            if (_count == _key.Length)
            {
                int n = _count * 2;
                Array.Resize(ref _key, n); Array.Resize(ref _token, n); Array.Resize(ref _lastX, n); Array.Resize(ref _lastY, n);
                Array.Resize(ref _resX, n); Array.Resize(ref _resY, n); Array.Resize(ref _speed, n); Array.Resize(ref _seen, n);
            }
            _key[_count] = key;
            return _count++;
        }

        void Move(int from, int to)
        {
            if (from == to) return;
            _key[to] = _key[from]; _token[to] = _token[from]; _lastX[to] = _lastX[from]; _lastY[to] = _lastY[from];
            _resX[to] = _resX[from]; _resY[to] = _resY[from]; _speed[to] = _speed[from]; _seen[to] = _seen[from];
        }
    }
}
