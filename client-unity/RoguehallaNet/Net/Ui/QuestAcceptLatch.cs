using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Ui
{
    /// <summary>The quest accepts sent and not yet answered, per handle, each held for HoldSeconds.</summary>
    public sealed class QuestAcceptLatch
    {
        public const float HoldSeconds = 0f;
        public const int Capacity = 1;
        public const int Gone = -1;

        public static int Signature(bool canAccept, bool ownerAccepted, QuestRowState state) { return 0; }
        public static int SignatureOf(QuestRowView row) { return 0; }
        public bool TryBegin(QuestHandle handle, int signature) { return true; }
        public bool IsHeld(QuestHandle handle) { return false; }
        public bool PressedRecently(QuestHandle handle) { return false; }
        public int Held { get { return 0; } }
        public bool TryGetHeld(int slot, out QuestHandle handle) { handle = default(QuestHandle); return false; }
        public void Answer(QuestHandle handle, int signature) { }
        public void Tick(float dt) { }
        public void Clear() { }
    }
}
