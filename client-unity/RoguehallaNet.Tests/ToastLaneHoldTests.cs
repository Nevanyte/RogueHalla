using System;
using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-UI-ABILITY-FEEDBACK-1: the two additions to the toast lane's state machine: a push with its own hold and an option to never
    /// show late (<see cref="ToastLaneModel.Push"/> holdSeconds, dropIfLate) and <see cref="ToastLaneModel.End"/>. A line lives .12 s
    /// in, its hold, .25 s out. Time is passed in (steps of 10 ms); nothing here reads a clock. ToastLaneModelTests stays green: it
    /// guards that every other push gets today's behaviour (hold 2.2 s for a short text, queue, pause while invisible).
    /// </summary>
    public sealed class ToastLaneHoldTests
    {
        const float Dt = .01f;
        const string Cast = "Ready in 3";

        static void Run(ToastLaneModel m, float seconds, float dt = Dt, bool visible = true, bool reduced = false)
        {
            int steps = (int)Math.Round(seconds / dt);
            for (int i = 0; i < steps; i++) m.Tick(dt, visible, reduced);
        }

        static int IndexOf(ToastLaneModel m, string text)
        {
            for (int i = 0; i < m.Count; i++) if (m.Line(i).Text == text) return i;
            return -1;
        }

        static void ShowsFully(ToastLaneModel m, string text, string what)
        {
            int i = IndexOf(m, text);
            Assert.That(i, Is.GreaterThanOrEqualTo(0), what + ": '" + text + "' shows");
            Assert.That(m.Line(i).Alpha, Is.EqualTo(1f).Within(.001f), what + ": '" + text + "' is fully opaque");
        }

        static void Gone(ToastLaneModel m, string text, string what) =>
            Assert.That(IndexOf(m, text), Is.EqualTo(-1), what + ": '" + text + "' is gone");

        [Test]
        public void AHoldOfOneSecondEndsTheLineSooner()
        {
            var m = new ToastLaneModel(3);
            m.Push(Cast, NoticeTone.Warn, "cast", 1f);
            m.Push("Other", NoticeTone.Info);
            Run(m, .9f);
            ShowsFully(m, Cast, "at .9 s"); ShowsFully(m, "Other", "at .9 s");
            Run(m, .6f);
            Gone(m, Cast, "at 1.5 s");
            ShowsFully(m, "Other", "at 1.5 s: the 2.2 s hold");
        }

        [Test]
        public void ALineThatCannotShowAtOnceIsDropped()
        {
            var m = new ToastLaneModel(2);
            m.Push("A", NoticeTone.Info); m.Push("B", NoticeTone.Info);
            Run(m, .3f);
            m.Push("Ready in 9", NoticeTone.Warn, "cast", 1f, dropIfLate: true);
            Assert.That(m.Waiting, Is.EqualTo(0), "nothing waits");
            Assert.That(m.Count, Is.EqualTo(2), "the lane still holds A and B");
            Assert.That(IndexOf(m, "Ready in 9"), Is.EqualTo(-1), "no line reads it");
            Run(m, 4f);
            Assert.That(m.Count, Is.EqualTo(0), "set-up: A and B are gone");
            Assert.That(IndexOf(m, "Ready in 9"), Is.EqualTo(-1), "and the dropped line did not come back");
            Assert.That(m.Waiting, Is.EqualTo(0), "still nothing waits");
        }

        [Test]
        public void ALineWithRoomShowsAtOnceAndASecondPressReplacesIt()
        {
            var m = new ToastLaneModel(2);
            m.Push("A", NoticeTone.Info);
            Run(m, .3f);
            m.Push("Ready in 3", NoticeTone.Warn, "cast", 1f, dropIfLate: true);
            Assert.That(m.Count, Is.EqualTo(2), "it shows at once");
            Assert.That(m.Waiting, Is.EqualTo(0), "and nothing waits");
            Run(m, .5f);
            m.Push("Ready in 2", NoticeTone.Bad, "cast", 1f, dropIfLate: true);
            Assert.That(m.Count, Is.EqualTo(2), "the second press replaced it in place");
            Assert.That(m.Waiting, Is.EqualTo(0), "nothing waits");
            int i = IndexOf(m, "Ready in 2");
            Assert.That(i, Is.GreaterThanOrEqualTo(0), "the new text shows (no ' x2')");
            Assert.That(IndexOf(m, "Ready in 3"), Is.EqualTo(-1), "the old text is gone");
            Assert.That(m.Line(i).Tone, Is.EqualTo(NoticeTone.Bad), "with the new tone");
            Assert.That(m.Line(i).Count, Is.EqualTo(1), "and a count of 1");
            Run(m, .9f);
            ShowsFully(m, "Ready in 2", ".9 s after the second push");
            Run(m, .6f);
            Gone(m, "Ready in 2", "1.5 s after the second push: its hold restarted at 1 s");
        }

        [Test]
        public void ARestartedKeyedLineTakesTheHoldOfThePush()
        {
            var m = new ToastLaneModel(3);
            m.Push("a", NoticeTone.Info, "k");
            Run(m, .5f);
            m.Push("b", NoticeTone.Info, "k", 1f);
            Run(m, .9f);
            ShowsFully(m, "b", ".9 s after the second push");
            Run(m, .6f);
            Gone(m, "b", "1.5 s after it");
        }

        [Test]
        public void AHoldThatIsNotFiniteAndAboveZeroUsesTheDefault()
        {
            var control = new ToastLaneModel(3);
            control.Push(Cast, NoticeTone.Warn, "k", 1f);
            Run(control, 1.6f);
            Gone(control, Cast, "a hold of 1 s: gone at 1.6 s");

            foreach (float hold in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                var m = new ToastLaneModel(3);
                m.Push(Cast, NoticeTone.Warn, "k", hold);
                Run(m, 2f);
                ShowsFully(m, Cast, "hold " + hold + " at 2.0 s");
                Run(m, 1f);
                Gone(m, Cast, "hold " + hold + " at 3.0 s (the 2.2 s default)");
            }
        }

        [Test]
        public void AStickyLineCountsTowardTheRoom()
        {
            var m = new ToastLaneModel(2);
            m.SetSticky("hint", NoticeTone.Info);
            m.Push("A", NoticeTone.Info);
            Run(m, .3f);
            Assert.That(m.Count, Is.EqualTo(2), "set-up: the sticky line and A fill the lane");
            m.Push(Cast, NoticeTone.Warn, "cast", 1f, dropIfLate: true);
            Assert.That(m.Waiting, Is.EqualTo(0), "nothing waits");
            Assert.That(m.Count, Is.EqualTo(2), "the lane is unchanged");
            Assert.That(IndexOf(m, Cast), Is.EqualTo(-1), "no line reads it");
        }

        [Test]
        public void ANeverLatePushWithNoKeyIsAPlainPush()
        {
            var m = new ToastLaneModel(1);
            m.Push("A", NoticeTone.Info);
            m.Push("X", NoticeTone.Warn, null, 1f, dropIfLate: true);
            Assert.That(m.Waiting, Is.EqualTo(1), "with no key it queues like any push");
            Run(m, 2f);
            Assert.That(m.Waiting, Is.EqualTo(0), "at 2.0 s it has come in");
            ShowsFully(m, "X", "at 2.0 s");
            Run(m, 1.2f);
            Gone(m, "X", "at 3.2 s: its 1 s hold applies");

            var two = new ToastLaneModel(2);
            two.Push("A", NoticeTone.Info);
            two.Push("X", NoticeTone.Warn, null, 1f, dropIfLate: true);
            Assert.That(two.Count, Is.EqualTo(2), "with room it shows");
            two.Tick(.01f, false, false);
            Assert.That(two.Count, Is.EqualTo(2), "and an invisible tick leaves it: it is not never-late");
        }

        [Test]
        public void ALeavingKeyedLineIsRevivedByANeverLatePush()
        {
            var m = new ToastLaneModel(2);
            m.Push(Cast, NoticeTone.Warn, "cast", 1f, dropIfLate: true);
            Run(m, 1.25f);
            Assert.That(m.Count, Is.EqualTo(1), "set-up: still drawn while it fades out");
            Assert.That(m.Line(0).Alpha, Is.LessThan(1f), "set-up: it is leaving");
            m.Push("Ready in 1", NoticeTone.Warn, "cast", 1f, dropIfLate: true);
            Assert.That(m.Count, Is.EqualTo(1), "the same line is revived");
            Assert.That(m.Waiting, Is.EqualTo(0), "nothing waits");
            Assert.That(m.Line(0).Text, Is.EqualTo("Ready in 1"), "with the new text");
            Run(m, .9f);
            ShowsFully(m, "Ready in 1", ".9 s after the revival");
            Run(m, .8f);
            Gone(m, "Ready in 1", "1.7 s after it");
        }

        [Test]
        public void ANeverLateLineIsRemovedAtTheFirstTickThatIsNotVisible()
        {
            var m = new ToastLaneModel(2);
            m.Push("Other", NoticeTone.Info);
            m.Push(Cast, NoticeTone.Warn, "cast", 1f, dropIfLate: true);
            Run(m, .3f);
            Assert.That(m.Count, Is.EqualTo(2), "set-up: both show");
            m.Tick(.01f, false, false);
            Assert.That(m.Count, Is.EqualTo(1), "the invisible tick removes the cast line");
            Assert.That(IndexOf(m, "Other"), Is.GreaterThanOrEqualTo(0), "and keeps the plain line (paused)");
            m.Tick(.01f, true, false);
            Assert.That(m.Count, Is.EqualTo(1), "it does not come back when the lane shows again");
            Assert.That(IndexOf(m, Cast), Is.EqualTo(-1), "no line reads the cast text");

            var kept = new ToastLaneModel(2);
            kept.Push("Keyed", NoticeTone.Info, "k2", 1f);
            Run(kept, .3f);
            kept.Tick(.01f, false, false);
            Assert.That(kept.Count, Is.EqualTo(1), "a keyed line pushed without the option survives the invisible tick");
            Assert.That(IndexOf(kept, "Keyed"), Is.GreaterThanOrEqualTo(0), "and still reads its text");
        }

        [Test]
        public void EndFadesAShowingLineByKeyAndNothingElse()
        {
            var m = new ToastLaneModel(3);
            m.Push(Cast, NoticeTone.Warn, "cast", 1f, dropIfLate: true);
            m.Push("Other", NoticeTone.Info);
            Run(m, .5f);
            ShowsFully(m, Cast, "set-up");
            m.End("cast");
            Run(m, .1f);
            Assert.That(m.Count, Is.EqualTo(2), ".1 s later the line is still fading");
            int i = IndexOf(m, Cast);
            Assert.That(i, Is.GreaterThanOrEqualTo(0), "it is still drawn");
            Assert.That(m.Line(i).Alpha, Is.LessThan(1f), "and fading");
            Run(m, .3f);
            Assert.That(m.Count, Is.EqualTo(1), ".3 s after that it is gone");
            ShowsFully(m, "Other", "the other line shows");

            var untouched = new ToastLaneModel(3);
            untouched.Push(Cast, NoticeTone.Warn, "cast", 1f);
            untouched.SetSticky("hint", NoticeTone.Info);
            Run(untouched, .5f);
            untouched.End("none"); untouched.End(null);
            Run(untouched, .1f);
            Assert.That(untouched.Count, Is.EqualTo(2), "an unknown or null key changes nothing");
            ShowsFully(untouched, Cast, "the keyed line");
            ShowsFully(untouched, "hint", "the sticky line (its key is null)");

            var waiting = new ToastLaneModel(1);
            waiting.Push("A", NoticeTone.Info);
            waiting.Push("queued", NoticeTone.Info, "cast");
            Assert.That(waiting.Waiting, Is.EqualTo(1), "set-up: a keyed line waits");
            waiting.End("cast");
            Assert.That(waiting.Waiting, Is.EqualTo(1), "End leaves a waiting line alone");
        }
    }
}
