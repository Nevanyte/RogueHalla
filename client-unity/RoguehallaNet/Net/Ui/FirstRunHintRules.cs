namespace Roguehalla.Net.Ui
{
    /// <summary>The verdict of one frame for the first-run hint.</summary>
    public enum FirstRunHintVerdict { Stand, Count, End }

    /// <summary>When the first-run hint is owed, counted and ended (P-UI-FIRST-RUN-HINT-1).</summary>
    public static class FirstRunHintRules
    {
        public const float PlaySeconds = 20f;

        /// <summary>A new player (no profile, or fewer than two runs) is owed the hint.</summary>
        public static bool Owed(long? runs) { return runs == null || runs.Value < 2; }

        /// <summary>End wins over everything; Count only while the match is live and the lane is visible.</summary>
        public static FirstRunHintVerdict Judge(bool practice, bool matchOver, bool inMatch, MatchPhase? phase, bool laneVisible)
        {
            if (practice || matchOver || phase == MatchPhase.Ended) return FirstRunHintVerdict.End;
            if (inMatch && phase == MatchPhase.Active && laneVisible) return FirstRunHintVerdict.Count;
            return FirstRunHintVerdict.Stand;
        }

        /// <summary>Adds a finite, positive step, capped at <see cref="PlaySeconds"/>.</summary>
        public static float Advance(float played, float dt)
        {
            if (!(dt > 0f) || float.IsInfinity(dt)) return played;
            float next = played + dt;
            return next > PlaySeconds ? PlaySeconds : next;
        }

        public static bool Spent(float played) { return played >= PlaySeconds; }
    }
}
