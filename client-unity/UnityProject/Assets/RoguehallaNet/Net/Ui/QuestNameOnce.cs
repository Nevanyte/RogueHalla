namespace Roguehalla.Net.Ui
{
    /// <summary>One name per quest over one frame's drawn objective points, sticky. Stub (P-UI-WORLD-LABELS-1): the implementer replaces every body.</summary>
    public sealed class QuestNameOnce
    {
        public int Count { get { return 0; } }
        public void Begin() { }
        public void Offer(uint quest, float x, float y) { }
        public bool Carries(uint quest, float x, float y) { return true; }
    }
}
