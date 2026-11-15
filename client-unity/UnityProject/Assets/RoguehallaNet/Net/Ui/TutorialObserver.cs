using Roguehalla.Net.Protocol;

namespace Roguehalla.Net
{
    /// <summary>STUB (P-TUTORIAL-2): the implementer builds a <see cref="TutorialObservation"/> from a session's accepted state (read-only).</summary>
    public sealed class TutorialObserver : System.IDisposable
    {
        public TutorialObserver(Session session) { }
        public void Dispose() { }
        public bool TryBuild(out TutorialObservation observation) { observation = default(TutorialObservation); return false; }
    }
}
