using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>P-UI-FIRST-RUN-HINT-1: when the first-run hint is owed, counted and ended. Pure rules, no Unity.</summary>
    public sealed class FirstRunHintRulesTests
    {
        [Test]
        public void OwedFollowsTheRunCount()
        {
            Assert.IsTrue(FirstRunHintRules.Owed(null), "no profile");
            Assert.IsTrue(FirstRunHintRules.Owed(0L), "0 runs");
            Assert.IsTrue(FirstRunHintRules.Owed(1L), "1 run");
            Assert.IsFalse(FirstRunHintRules.Owed(2L), "2 runs");
            Assert.IsFalse(FirstRunHintRules.Owed(3L), "3 runs");
            Assert.IsFalse(FirstRunHintRules.Owed(long.MaxValue), "many runs");
        }

        [Test]
        public void JudgeEndsInPracticeAndWhenTheMatchIsOver()
        {
            var phases = new MatchPhase?[] { null, MatchPhase.Lobby, MatchPhase.Active, MatchPhase.Planning, MatchPhase.Ended };
            foreach (var phase in phases)
                foreach (bool inMatch in new[] { false, true })
                    foreach (bool visible in new[] { false, true })
                    {
                        string at = " (phase " + (phase.HasValue ? phase.Value.ToString() : "null") + ", inMatch " + inMatch + ", visible " + visible + ")";
                        Assert.AreEqual(FirstRunHintVerdict.End, FirstRunHintRules.Judge(true, false, inMatch, phase, visible), "practice" + at);
                        Assert.AreEqual(FirstRunHintVerdict.End, FirstRunHintRules.Judge(false, true, inMatch, phase, visible), "match over" + at);
                        Assert.AreEqual(FirstRunHintVerdict.End, FirstRunHintRules.Judge(true, true, inMatch, phase, visible), "both" + at);
                    }
            foreach (bool inMatch in new[] { false, true })
                foreach (bool visible in new[] { false, true })
                    Assert.AreEqual(FirstRunHintVerdict.End, FirstRunHintRules.Judge(false, false, inMatch, MatchPhase.Ended, visible),
                        "an Ended phase ends it (inMatch " + inMatch + ", visible " + visible + ")");
        }

        [Test]
        public void JudgeStandsUntilPlayIsLive()
        {
            Assert.AreEqual(FirstRunHintVerdict.Stand, FirstRunHintRules.Judge(false, false, true, null, true), "no snapshot yet is Stand, never End");
            Assert.AreEqual(FirstRunHintVerdict.Stand, FirstRunHintRules.Judge(false, false, true, MatchPhase.Lobby, true), "lobby");
            Assert.AreEqual(FirstRunHintVerdict.Stand, FirstRunHintRules.Judge(false, false, true, MatchPhase.Planning, true), "planning");
            Assert.AreEqual(FirstRunHintVerdict.Stand, FirstRunHintRules.Judge(false, false, false, MatchPhase.Active, true), "reconnecting (not in match)");
            Assert.AreEqual(FirstRunHintVerdict.Stand, FirstRunHintRules.Judge(false, false, true, MatchPhase.Active, false), "lane hidden");
            Assert.AreEqual(FirstRunHintVerdict.Count, FirstRunHintRules.Judge(false, false, true, MatchPhase.Active, true), "live play with the lane visible counts");
        }

        [Test]
        public void AdvanceCountsOnlyGoodSteps()
        {
            Assert.AreEqual(.25f, FirstRunHintRules.Advance(0f, .25f), 0f, "a good step");
            Assert.AreEqual(1f, FirstRunHintRules.Advance(1f, float.NaN), 0f, "NaN");
            Assert.AreEqual(1f, FirstRunHintRules.Advance(1f, -1f), 0f, "negative");
            Assert.AreEqual(1f, FirstRunHintRules.Advance(1f, 0f), 0f, "zero");
            Assert.AreEqual(1f, FirstRunHintRules.Advance(1f, float.PositiveInfinity), 0f, "infinity");
            Assert.AreEqual(FirstRunHintRules.PlaySeconds, 20f, 0f, "the one value is 20 seconds");
            Assert.AreEqual(20f, FirstRunHintRules.Advance(19.9f, 5f), 0f, "capped at PlaySeconds");
            float played = 0f;
            for (int i = 0; i < 80; i++) played = FirstRunHintRules.Advance(played, .25f);
            Assert.AreEqual(20f, played, 0f, "80 steps of .25 s");
            Assert.IsTrue(FirstRunHintRules.Spent(played), "20 s is spent");
            Assert.IsFalse(FirstRunHintRules.Spent(19.75f), "19.75 s is not");
            Assert.IsTrue(FirstRunHintRules.Spent(20f), "exactly 20 s is");
        }
    }
}
