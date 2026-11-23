namespace Roguehalla.Net.Ui
{
    /// <summary>The verdict of one frame for the first-run hint.</summary>
    public enum FirstRunHintVerdict { Stand, Count, End }

    /// <summary>When the first-run hint is owed, counted and ended (P-UI-FIRST-RUN-HINT-1). Stub: the implementer replaces the bodies.</summary>
    public static class FirstRunHintRules
    {
        public const float PlaySeconds = 20f;

        public static bool Owed(long? runs) { return false; }

        public static FirstRunHintVerdict Judge(bool practice, bool matchOver, bool inMatch, MatchPhase? phase, bool laneVisible)
        { return FirstRunHintVerdict.Stand; }

        public static float Advance(float played, float dt) { return played; }

        public static bool Spent(float played) { return false; }
    }
}
