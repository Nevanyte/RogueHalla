using System;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net
{
    /// <summary>The attack-pause part of <see cref="LocalMotion"/>: the windows the server's attack pause places on the model clock, and the walk time they leave.</summary>
    public sealed partial class LocalMotion
    {
        // ----------------------------------------------------------------------------------------------------------------
        // Attack pause (the one attack fact the walk mirrors; see the type)

        /// <summary>
        /// The owner's <c>Windup</c> fx: the server started an attack at server time <paramref name="tickMs"/> (the tick the fx was
        /// sent in) that lands <paramref name="waitMs"/> later (whole ms, the wire's rounding). A swing stops the walk for its whole
        /// wind-up; a bow or wand shot stops it from <see cref="RangedReleasePauseMs"/> before it lands (rh-sim
        /// <c>move_multiplier</c>, evaluated once per 50 ms step: the step ending at T is stopped when T is at least
        /// <c>landsAt - RangedReleasePauseMs</c> and the shot has not landed by T). Nothing here reads a key press or a weapon
        /// timing: with no fx there is no window. The sim announces no wind-up for a bow draw, so a bow release is not covered.
        /// A landing off the step grid by under 0.5 ms (the wire rounds the wait) can place a window one step early or late.
        /// </summary>
        public void NoteOwnWindup(double tickMs, double waitMs, byte weapon)
        {
            if (!PredictAttackPause || !_hasStamp || double.IsNaN(tickMs) || double.IsNaN(waitMs) || waitMs < 0) return;
            bool release = weapon == (byte)WeaponId.Bow || weapon == (byte)WeaponId.Wand;
            PlacePause(tickMs, tickMs + waitMs, release, weapon == (byte)WeaponId.Bow);
        }

        /// <summary>
        /// The owner block of the sample at server time <paramref name="tickMs"/>: a bow draw has no fx, but the block carries
        /// <paramref name="drawStartedAt"/> (0 when no draw is pending) and <paramref name="readyAt"/> (the next attack), both in the
        /// sample's own clock (the caller adds the clock shift). The draw's interval is their difference and the shot lands
        /// <c>min(BowWindupMaxS, BowWindupIntervalShare * interval)</c> after it starts (rh-sim <c>player_timings</c>), then the
        /// same release pause as a wand applies. A windup mirror above 1 (<paramref name="windupMilli"/>) scales that wait by a rule
        /// the client does not hold, so the draw is left unpredicted. A draw clock that returns to 0 before the shot lands is a
        /// cancelled draw (swap, stun, death), whatever the cause: the pause ends the step before.
        /// </summary>
        public void NoteOwnerDraw(double tickMs, double drawStartedAt, double readyAt, double windupMilli)
        {
            if (!PredictAttackPause || !_hasStamp) return;
            uint draw = (uint)drawStartedAt;
            if (draw != 0)
            {
                if (draw == _lastDraw) return;
                _lastDraw = draw;
                double interval = (readyAt - drawStartedAt) / 1000.0;
                if (!(interval > 0) || windupMilli > 1000.0) return;
                double wait = Math.Min(BowWindupMaxS, BowWindupIntervalShare * interval) * 1000.0;
                PlacePause(tickMs, drawStartedAt + wait, true, true);
            }
            else if (_lastDraw != 0)
            {
                _lastDraw = 0;
                EndPauseAt(tickMs - StepMs, true);
            }
        }

        void PlacePause(double tickMs, double landsAt, bool release, bool draw)
        {
            double landStep = CeilStep(landsAt);            // the first step whose tick has the shot landed (weapons run before movement)
            double firstStep = release ? Math.Max(tickMs, CeilStep(landsAt - RangedReleasePauseMs)) : tickMs;
            double from = firstStep - StepMs, until = landStep - StepMs;
            if (!(until > from)) return;
            if (_pauseCount == PauseSlots) DropPauseSlot(0);
            _pauseFrom[_pauseCount] = from;
            _pauseUntil[_pauseCount] = until;
            _pauseDraw[_pauseCount] = draw;
            _pauseCount++;
        }

        /// <summary>
        /// The owner's <c>Stagger</c> fx at server time <paramref name="tickMs"/>: the cast or swing is cancelled by the ledger, after
        /// that tick's movement multiplier was read, so the step ending at the stagger's own tick is still stopped.
        /// </summary>
        public void NoteOwnStagger(double tickMs) { if (PredictAttackPause) EndPauseAt(tickMs, false); }

        /// <summary>
        /// The owner's <c>Cancel</c> fx at server time <paramref name="tickMs"/>: the order that cancelled the attack ran in the
        /// input phase, before that tick's multiplier was read, so the step ending at the cancel's tick walks.
        /// </summary>
        public void NoteOwnCancel(double tickMs) { if (PredictAttackPause) EndPauseAt(tickMs - StepMs, true); }

        void EndPauseAt(double untilMs, bool includeDraws)
        {
            for (int i = _pauseCount - 1; i >= 0; i--)
            {
                if (_pauseDraw[i] && !includeDraws) continue;
                if (_pauseUntil[i] > untilMs) _pauseUntil[i] = untilMs;
                if (!(_pauseUntil[i] > _pauseFrom[i])) DropPauseSlot(i);
            }
        }

        void DropPauseSlot(int i)
        {
            for (int j = i + 1; j < _pauseCount; j++) { _pauseFrom[j - 1] = _pauseFrom[j]; _pauseUntil[j - 1] = _pauseUntil[j]; _pauseDraw[j - 1] = _pauseDraw[j]; }
            _pauseCount--;
        }

        /// <summary>
        /// Whether the mirror stops the server step that ends at server time <paramref name="tickMs"/> (diagnostic and test seam: the
        /// step walks only when no held window covers it).
        /// </summary>
        public bool AttackPauseStops(double tickMs)
        {
            for (int i = 0; i < _pauseCount; i++)
                if (_pauseFrom[i] + StepMs <= tickMs + TieEpsMs && tickMs <= _pauseUntil[i] + TieEpsMs) return true;
            return false;
        }

        /// <summary>Drops every held attack pause (death, a cut source clock, a new Welcome).</summary>
        public void ClearAttackPause() { _pauseCount = 0; _lastDraw = 0; }

        double CeilStep(double ms) { return Math.Ceiling((ms - TieEpsMs) / StepMs) * StepMs; }

        /// <summary>The model-clock time of server time <paramref name="serverMs"/> (the sample stamp, minus the measured delay).</summary>
        double ModelClock(double serverMs) { return serverMs + _stampMs - _stampServerMs - _align; }

        /// <summary>Seconds of the model-clock span [t0, t1] (whose length is <paramref name="spanSeconds"/>) not covered by an attack pause.</summary>
        double MovingSeconds(double t0, double t1, double spanSeconds)
        {
            if (_pauseCount == 0 || !_hasStamp || !PredictAttackPause) return spanSeconds;
            double paused = 0;
            for (int i = 0; i < _pauseCount; i++)
            {
                double a = Math.Max(t0, ModelClock(_pauseFrom[i]));
                double b = Math.Min(t1, ModelClock(_pauseUntil[i]));
                if (b > a) paused += b - a;
            }
            double moving = spanSeconds - paused / 1000.0;
            return moving > 0 ? moving : 0;
        }

        /// <summary>
        /// The render time to stamp on an attack or release: the identity. With draw-ahead (P-NET-DRAWAHEAD-1b) remote fighters are
        /// drawn at <see cref="RenderClock.DrawMs"/>, but <see cref="RenderClock.RenderMs"/> is already the draw time less the lead
        /// (legacy remoteDraw.ts <c>attackTime</c>, owner decision 5), the time the clock without draw-ahead keeps, so the server's
        /// rewind is unchanged. <c>MatchClient.cs</c> sends <c>Session.Clock.RenderMs</c>, which is what this returns.
        /// </summary>
        public static double AttackTime(double renderMs) { return renderMs; }
    }
}
