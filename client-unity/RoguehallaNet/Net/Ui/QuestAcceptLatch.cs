using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Ui
{
    /// <summary>The quest accepts sent and not yet answered, per handle, each held for HoldSeconds.</summary>
    public sealed class QuestAcceptLatch
    {
        public const float HoldSeconds = 2.5f;
        public const int Capacity = 4;
        public const int Gone = -1;

        readonly uint[] _handle = new uint[Capacity];
        readonly float[] _left = new float[Capacity];
        readonly float[] _pressed = new float[Capacity];
        readonly int[] _signature = new int[Capacity];

        public static int Signature(bool canAccept, bool ownerAccepted, QuestRowState state)
        {
            return (canAccept ? 1 : 0) | (ownerAccepted ? 2 : 0) | ((int)state << 2);
        }

        public static int SignatureOf(QuestRowView row)
        {
            return row == null ? Gone : Signature(row.CanAccept, row.OwnerAccepted, row.State);
        }

        public bool TryBegin(QuestHandle handle, int signature)
        {
            if (!handle.IsValid) return false;
            uint v = handle.Value;
            int slot = -1;
            for (int i = 0; i < Capacity; i++)
            {
                if (_handle[i] != v) continue;
                if (_left[i] > 0f) return false;
                slot = i;
                break;
            }
            if (slot < 0)
            {
                slot = 0;
                for (int i = 1; i < Capacity; i++) if (_pressed[i] < _pressed[slot]) slot = i;
            }
            _handle[slot] = v;
            _left[slot] = HoldSeconds;
            _pressed[slot] = HoldSeconds;
            _signature[slot] = signature;
            return true;
        }

        public bool IsHeld(QuestHandle handle)
        {
            uint v = handle.Value;
            for (int i = 0; i < Capacity; i++) if (_handle[i] == v && _left[i] > 0f) return true;
            return false;
        }

        public bool PressedRecently(QuestHandle handle)
        {
            uint v = handle.Value;
            for (int i = 0; i < Capacity; i++) if (_handle[i] == v && _pressed[i] > 0f) return true;
            return false;
        }

        public int Held
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Capacity; i++) if (_left[i] > 0f) n++;
                return n;
            }
        }

        public bool TryGetHeld(int slot, out QuestHandle handle)
        {
            if (slot >= 0 && slot < Capacity && _left[slot] > 0f) { handle = new QuestHandle(_handle[slot]); return true; }
            handle = default(QuestHandle);
            return false;
        }

        public void Answer(QuestHandle handle, int signature)
        {
            uint v = handle.Value;
            for (int i = 0; i < Capacity; i++)
            {
                if (_handle[i] != v || _left[i] <= 0f) continue;
                if (_signature[i] != signature) _left[i] = 0f;
                return;
            }
        }

        public void Tick(float dt)
        {
            if (float.IsNaN(dt) || !(dt > 0f)) return;
            for (int i = 0; i < Capacity; i++)
            {
                if (_pressed[i] <= 0f && _left[i] <= 0f) continue;
                if (_pressed[i] > 0f) _pressed[i] -= dt;
                if (_left[i] > 0f) _left[i] -= dt;
                if (_pressed[i] <= 0f) { _pressed[i] = 0f; _left[i] = 0f; }
                else if (_left[i] < 0f) _left[i] = 0f;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < Capacity; i++)
            {
                if (_left[i] > 0f || _pressed[i] > 0f) { _left[i] = 0f; _pressed[i] = 0f; }
            }
        }
    }
}
