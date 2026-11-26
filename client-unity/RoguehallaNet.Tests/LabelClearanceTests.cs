using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-UI-WORLD-LABELS-1, the arithmetic half: a world label fades while its rectangle lies on the player's own cluster. Two pads (hide at
    /// <see cref="LabelClearance.HidePad"/>, return past <see cref="LabelClearance.KeepPad"/>), a fade of <see cref="LabelClearance.FadeSeconds"/> each
    /// way, state kept by object in <see cref="LabelClearanceBook"/>, and <see cref="ClusterHold"/> that keeps the last rectangle through one false
    /// frame. The cluster is (0,0)-(1,1) and a label is 1 by .3 beside it; "gap g" is the distance of the label's left edge from the cluster's right
    /// edge (negative: overlap), `dt` is .05 and one step moves the alpha .3333. The packet (docs/backlog/packets/P-UI-WORLD-LABELS-1.md,
    /// "Rules") has the worked numbers.
    /// </summary>
    public sealed class LabelClearanceTests
    {
        const float Dt = .05f, Tol = .001f;
        static readonly LabelBox Cluster = new LabelBox(0f, 0f, 1f, 1f);

        /// <summary>The label 1 by .3 whose left edge is <paramref name="gap"/> right of the cluster's right edge, level with its middle.</summary>
        static LabelBox Right(float gap) { return new LabelBox(1f + gap, .35f, 2f + gap, .65f); }

        static float Step(ref LabelClearance s, float gap, float dt = Dt, bool reduced = false, bool have = true)
        {
            return s.Next(Right(gap), have, Cluster, dt, reduced);
        }

        static void SameBox(LabelBox expected, LabelBox actual, string what)
        {
            Assert.That(actual.X0, Is.EqualTo(expected.X0).Within(1e-5f), what + " X0");
            Assert.That(actual.Y0, Is.EqualTo(expected.Y0).Within(1e-5f), what + " Y0");
            Assert.That(actual.X1, Is.EqualTo(expected.X1).Within(1e-5f), what + " X1");
            Assert.That(actual.Y1, Is.EqualTo(expected.Y1).Within(1e-5f), what + " Y1");
        }

        // ---- LabelBox ----

        [Test]
        public void MeetsIsStrict()
        {
            const float hide = LabelClearance.HidePad, keep = LabelClearance.KeepPad;
            Assert.That(Right(.05f).Meets(Cluster, hide), Is.True, "gap .05 meets at HidePad");
            Assert.That(Right(.25f).Meets(Cluster, hide), Is.False, "gap .25 does not meet at HidePad");
            Assert.That(Right(.25f).Meets(Cluster, keep), Is.True, "gap .25 meets at KeepPad");
            Assert.That(Right(.45f).Meets(Cluster, hide), Is.False, "gap .45 does not meet at HidePad");
            Assert.That(Right(.45f).Meets(Cluster, keep), Is.False, "gap .45 does not meet at KeepPad");
            Assert.That(Right(-.05f).Meets(Cluster, 0f), Is.True, "an overlap meets with no pad");

            // exactly the pad: the same float expression Meets evaluates, so the edge is exact
            var edge = new LabelBox(Cluster.X1 + LabelClearance.HidePad, .35f, Cluster.X1 + LabelClearance.HidePad + 1f, .65f);
            Assert.That(edge.Meets(Cluster, LabelClearance.HidePad), Is.False, "a gap of exactly HidePad does not meet (strict)");
            var edgeLeft = new LabelBox(Cluster.X0 - LabelClearance.HidePad - 1f, .35f, Cluster.X0 - LabelClearance.HidePad, .65f);
            Assert.That(edgeLeft.Meets(Cluster, LabelClearance.HidePad), Is.False, "exactly HidePad on the left does not meet");
            var edgeAbove = new LabelBox(.2f, Cluster.Y1 + LabelClearance.HidePad, 1.2f, Cluster.Y1 + LabelClearance.HidePad + .3f);
            Assert.That(edgeAbove.Meets(Cluster, LabelClearance.HidePad), Is.False, "exactly HidePad above does not meet");
            var edgeBelow = new LabelBox(.2f, Cluster.Y0 - LabelClearance.HidePad - .3f, 1.2f, Cluster.Y0 - LabelClearance.HidePad);
            Assert.That(edgeBelow.Meets(Cluster, LabelClearance.HidePad), Is.False, "exactly HidePad below does not meet");
        }

        [Test]
        public void MeetsOnEveryEdgeAndNaNMeetsNothing()
        {
            const float hide = LabelClearance.HidePad;
            Assert.That(new LabelBox(-1.95f, .35f, -.95f, .65f).Meets(Cluster, hide), Is.False, "left, 0.95 away");
            Assert.That(new LabelBox(-1.05f, .35f, -.05f, .65f).Meets(Cluster, hide), Is.True, "left, gap .05");
            Assert.That(new LabelBox(.2f, 1.05f, 1.2f, 1.35f).Meets(Cluster, hide), Is.True, "above, gap .05");
            Assert.That(new LabelBox(.2f, 1.25f, 1.2f, 1.55f).Meets(Cluster, hide), Is.False, "above, gap .25");
            Assert.That(new LabelBox(.2f, 1.25f, 1.2f, 1.55f).Meets(Cluster, LabelClearance.KeepPad), Is.True, "above, gap .25 at KeepPad");
            Assert.That(new LabelBox(.2f, -.35f, 1.2f, -.05f).Meets(Cluster, hide), Is.True, "below, gap .05");
            Assert.That(new LabelBox(.2f, -.55f, 1.2f, -.25f).Meets(Cluster, hide), Is.False, "below, gap .25");
            Assert.That(new LabelBox(.2f, -.55f, 1.2f, -.25f).Meets(Cluster, LabelClearance.KeepPad), Is.True, "below, gap .25 at KeepPad");

            var overlap = Right(-.05f);
            Assert.That(overlap.Meets(Cluster, hide), Is.True, "set-up: an overlapping box meets");
            Assert.That(new LabelBox(float.NaN, .35f, 2f, .65f).Meets(Cluster, hide), Is.False, "a NaN edge meets nothing");
            Assert.That(new LabelBox(.5f, float.NaN, 2f, .65f).Meets(Cluster, hide), Is.False, "a NaN y meets nothing");
            Assert.That(overlap.Meets(new LabelBox(0f, 0f, float.NaN, 1f), hide), Is.False, "a NaN cluster meets nothing");
        }

        [Test]
        public void CentredBuildsTheBox()
        {
            SameBox(new LabelBox(3f, 1f, 7f, 3f), LabelBox.Centred(5f, 2f, 4f, 2f), "Centred(5, 2, 4, 2)");
        }

        // ---- LabelClearance ----

        [Test]
        public void FadesAtPointOneFiveSeconds()
        {
            var s = new LabelClearance();
            Assert.That(Step(ref s, .45f), Is.EqualTo(1f).Within(Tol), "born clear: 1");
            Assert.That(Step(ref s, -.05f), Is.EqualTo(.667f).Within(Tol), "step 1 over the cluster");
            Assert.That(Step(ref s, -.05f), Is.EqualTo(.333f).Within(Tol), "step 2");
            Assert.That(Step(ref s, -.05f), Is.EqualTo(0f).Within(Tol), "step 3");
            Assert.That(Step(ref s, -.05f), Is.EqualTo(0f).Within(Tol), "step 4 stays 0");
            Assert.That(s.Hidden, Is.True, "hidden");
            Assert.That(Step(ref s, .25f), Is.EqualTo(0f).Within(Tol), "held at gap .25 (1)");
            Assert.That(Step(ref s, .25f), Is.EqualTo(0f).Within(Tol), "held at gap .25 (2)");
            Assert.That(Step(ref s, .25f), Is.EqualTo(0f).Within(Tol), "held at gap .25 (3)");
            Assert.That(Step(ref s, .45f), Is.EqualTo(.333f).Within(Tol), "back, step 1");
            Assert.That(Step(ref s, .45f), Is.EqualTo(.667f).Within(Tol), "back, step 2");
            Assert.That(Step(ref s, .45f), Is.EqualTo(1f).Within(Tol), "back, step 3");
            Assert.That(Step(ref s, .45f), Is.EqualTo(1f).Within(Tol), "back, step 4 stays 1");
            Assert.That(s.Hidden, Is.False, "visible again");

            var big = new LabelClearance();
            Step(ref big, .45f);
            Assert.That(Step(ref big, -.05f, 10f), Is.EqualTo(0f).Within(Tol), "a 10 s step lands on 0");
            Assert.That(Step(ref big, .45f, 10f), Is.EqualTo(1f).Within(Tol), "a 10 s step lands on 1");
        }

        [Test]
        public void TwoPadsGiveHysteresis()
        {
            var seen = new LabelClearance();
            Step(ref seen, .25f);
            for (int i = 0; i < 6; i++) Assert.That(Step(ref seen, .25f), Is.EqualTo(1f).Within(Tol), "born at gap .25 stays 1 (step " + i + ")");

            var s = new LabelClearance();
            Step(ref s, .45f);
            Assert.That(Step(ref s, .12f), Is.EqualTo(1f).Within(Tol), "a visible label outside HidePad does not hide at gap .12");
            Assert.That(s.Hidden, Is.False, "not hidden at gap .12");
            Assert.That(Step(ref s, .05f), Is.EqualTo(.667f).Within(Tol), "gap .05 hides");
            Assert.That(Step(ref s, .25f), Is.EqualTo(.333f).Within(Tol), "gap .25 keeps it hidden (the ramp goes on down)");
            Assert.That(Step(ref s, .25f), Is.EqualTo(0f).Within(Tol), "still hidden at gap .25");
            Assert.That(Step(ref s, .25f), Is.EqualTo(0f).Within(Tol), "and stays");
            Assert.That(s.Hidden, Is.True, "hidden inside the KeepPad band");
            Assert.That(Step(ref s, .45f), Is.EqualTo(.333f).Within(Tol), "gap .45 returns");
            Assert.That(s.Hidden, Is.False, "not hidden past KeepPad");
        }

        [Test]
        public void AFreshStateHasNoRamp()
        {
            var empty = new LabelClearance();
            Assert.That(empty.Alpha, Is.EqualTo(1f), "default(LabelClearance).Alpha is 1");
            Assert.That(empty.Has, Is.False, "default has no state");
            Assert.That(empty.Hidden, Is.False, "default is not hidden");

            var under = new LabelClearance();
            Assert.That(Step(ref under, -.05f), Is.EqualTo(0f).Within(Tol), "born under the cluster: 0 at once, no ramp");
            Assert.That(under.Has, Is.True, "Next sets Has");
            Assert.That(under.Hidden, Is.True, "born hidden");
            Assert.That(under.Alpha, Is.EqualTo(0f).Within(Tol), "Alpha reads the stored value");
            under.Forget();
            Assert.That(under.Has, Is.False, "Forget: Has false");
            Assert.That(under.Alpha, Is.EqualTo(1f), "Forget: Alpha 1");
            Assert.That(under.Hidden, Is.False, "Forget: not hidden");

            var noCluster = new LabelClearance();
            Assert.That(Step(ref noCluster, -.05f, Dt, false, have: false), Is.EqualTo(1f).Within(Tol), "born with no cluster: 1");
            Assert.That(Step(ref noCluster, -.05f, Dt, false, have: false), Is.EqualTo(1f).Within(Tol), "no cluster hides nothing");
        }

        [Test]
        public void NoClusterReturnsAHiddenLabel()
        {
            var s = new LabelClearance();
            Step(ref s, -.05f);
            Assert.That(s.Hidden, Is.True, "set-up: born hidden");
            Assert.That(Step(ref s, -.05f, Dt, false, have: false), Is.EqualTo(.333f).Within(Tol), "no cluster: the label returns (ramp up)");
            Assert.That(s.Hidden, Is.False, "no longer hidden");
        }

        [Test]
        public void ReducedAndBadDt()
        {
            var r = new LabelClearance();
            Step(ref r, .45f);
            Assert.That(Step(ref r, -.05f, Dt, true), Is.EqualTo(0f), "reduced: 0 at once");
            Assert.That(Step(ref r, .45f, Dt, true), Is.EqualTo(1f), "reduced: 1 at once");

            var s = new LabelClearance();
            Step(ref s, .45f);
            Assert.That(Step(ref s, -.05f), Is.EqualTo(.667f).Within(Tol), "set-up: one step down");
            Assert.That(Step(ref s, -.05f, float.NaN), Is.EqualTo(.667f).Within(Tol), "a NaN dt changes nothing");
            Assert.That(Step(ref s, -.05f, -1f), Is.EqualTo(.667f).Within(Tol), "a negative dt changes nothing");
            Assert.That(Step(ref s, -.05f, 0f), Is.EqualTo(.667f).Within(Tol), "a zero dt changes nothing");
            Assert.That(float.IsNaN(s.Alpha), Is.False, "the alpha is a number");
        }

        [Test]
        public void ASteadyStepAllocatesNothing()
        {
            var s = new LabelClearance();
            var label = Right(-.05f);
            var cluster = Cluster;
            Assert.That(s.Next(label, true, cluster, Dt, false), Is.EqualTo(0f).Within(Tol), "set-up: a step born under the cluster returns 0");
            float sink = 0f;
            double bytes = AllocationProbe.PerCall(i => { sink += s.Next(label, true, cluster, Dt, false); }, 2000);
            Assert.That(sink, Is.GreaterThanOrEqualTo(0f));
            Assert.That(bytes, Is.EqualTo(0.0), "bytes per step");
        }

        // ---- LabelClearanceBook ----

        static float Ask(LabelClearanceBook b, int kind, float x, float y, float gap, bool reduced = false)
        {
            return b.Next(kind, x, y, Right(gap), true, Cluster, Dt, reduced);
        }

        [Test]
        public void AnotherObjectStartsFresh()
        {
            var b = new LabelClearanceBook();
            b.Begin();
            Assert.That(Ask(b, 1, 100f, 100f, -.05f), Is.EqualTo(0f).Within(Tol), "an object born under the cluster is hidden at 0");
            Assert.That(Ask(b, 1, 400f, 100f, .45f), Is.EqualTo(1f).Within(Tol), "an object elsewhere, clear, is 1 at once");
            b.Begin();
            Assert.That(Ask(b, 1, 100.005f, 100f, .45f), Is.EqualTo(.333f).Within(Tol), "a place within .01 is the same object: it ramps up");
            Assert.That(Ask(b, 1, 400f, 100f, .45f), Is.EqualTo(1f).Within(Tol), "the other object is unaffected");
            Assert.That(Ask(b, 2, 100f, 100f, .45f), Is.EqualTo(1f).Within(Tol), "another kind at the same place is a fresh object");
            Assert.That(b.Count, Is.EqualTo(3), "three objects held");
            b.Begin();
            Assert.That(Ask(b, 1, 100.05f, 100f, .45f), Is.EqualTo(1f).Within(Tol), "a place .05 away is another object, fresh");
        }

        [Test]
        public void AnEarlierObjectLeavingResetsNothing()
        {
            var b = new LabelClearanceBook();
            b.Begin();
            Assert.That(Ask(b, 1, 100f, 100f, .45f), Is.EqualTo(1f).Within(Tol), "the first object, clear");
            Assert.That(Ask(b, 1, 400f, 100f, -.05f), Is.EqualTo(0f).Within(Tol), "the second object, born hidden");
            Assert.That(Ask(b, 1, 700f, 100f, .45f), Is.EqualTo(1f).Within(Tol), "the third object, clear");
            b.Begin();
            Assert.That(Ask(b, 1, 400f, 100f, .25f), Is.EqualTo(0f).Within(Tol), "the second, held in the band, with the first not asked for");
            Assert.That(Ask(b, 1, 700f, 100f, .45f), Is.EqualTo(1f).Within(Tol), "the third");
            b.Begin();
            Assert.That(Ask(b, 1, 400f, 100f, .25f), Is.EqualTo(0f).Within(Tol), "the second, still 0 a frame later");
            Assert.That(b.Count, Is.EqualTo(3), "the first is in its frame of grace and still counts");
            b.Begin();
            Assert.That(Ask(b, 1, 400f, 100f, .25f), Is.EqualTo(0f).Within(Tol), "the second, still 0 two frames later");
            Assert.That(b.Count, Is.EqualTo(2), "the first is forgotten after two frames not asked for; the third (asked a frame ago) is held");
        }

        [Test]
        public void AnObjectIsForgottenAfterTwoFramesNotAsked()
        {
            var b = new LabelClearanceBook();
            b.Begin();
            Assert.That(Ask(b, 1, 100f, 100f, -.05f), Is.EqualTo(0f).Within(Tol), "frame 1: born hidden");
            b.Begin();
            Assert.That(Ask(b, 1, 100f, 100f, .25f), Is.EqualTo(0f).Within(Tol), "frame 2: held at gap .25");
            b.Begin();   // frame 3: not asked
            Assert.That(b.Count, Is.EqualTo(1), "frame 3: held");
            b.Begin();   // frame 4: not asked for ONE frame, then asked
            Assert.That(b.Count, Is.EqualTo(1), "frame 4: one frame not asked for keeps the entry (its frame of grace)");
            Assert.That(Ask(b, 1, 100f, 100f, .25f), Is.EqualTo(0f).Within(Tol), "frame 4: still hidden after one frame away");
            Assert.That(b.Count, Is.EqualTo(1), "frame 4: Count kept");
            b.Begin();   // frame 5: not asked
            b.Begin();   // frame 6: not asked for the first frame
            Assert.That(b.Count, Is.EqualTo(1), "frame 6: in its frame of grace, still counted");
            b.Begin();   // frame 7: two frames in a row not asked for
            Assert.That(b.Count, Is.EqualTo(0), "frame 7: dropped after two frames");
            Assert.That(Ask(b, 1, 100f, 100f, .25f), Is.EqualTo(1f).Within(Tol), "asked at gap .25 after two frames away: fresh, 1");
            Assert.That(b.Count, Is.EqualTo(1), "and held again");
        }

        [Test]
        public void KeepReturnsTheStoredAlphaAndStepsNothing()
        {
            var b = new LabelClearanceBook();
            b.Begin();
            Assert.That(Ask(b, 1, 100f, 100f, .45f), Is.EqualTo(1f).Within(Tol), "frame 1: born clear");
            b.Begin();
            Assert.That(Ask(b, 1, 100f, 100f, -.05f), Is.EqualTo(.667f).Within(Tol), "frame 2: one step down");
            Assert.That(b.Keep(1, 400f, 100f), Is.EqualTo(1f), "Keep of an object with no state is 1");
            Assert.That(b.Count, Is.EqualTo(1), "Keep stores nothing");
            for (int i = 0; i < 6; i++)
            {
                b.Begin();
                Assert.That(b.Keep(1, 100f, 100f), Is.EqualTo(.667f).Within(Tol), "a skipped frame returns the stored alpha (" + i + ")");
                Assert.That(b.Keep(1, 100.005f, 100f), Is.EqualTo(.667f).Within(Tol), "asked twice, no step");
            }
            Assert.That(b.Count, Is.EqualTo(1), "Keep counts as asked: the entry is held after six skipped frames");
            b.Begin();
            Assert.That(Ask(b, 1, 100f, 100f, -.05f), Is.EqualTo(.333f).Within(Tol), "the first step after the skipped frames goes on from the stored alpha");
        }

        [Test]
        public void TwoAtOneSpotStepOnce()
        {
            var b = new LabelClearanceBook();
            b.Begin();
            Assert.That(Ask(b, 1, 100f, 100f, .45f), Is.EqualTo(1f).Within(Tol), "frame 1: born clear");
            b.Begin();
            Assert.That(Ask(b, 1, 100f, 100f, -.05f), Is.EqualTo(.667f).Within(Tol), "frame 2: the first ask steps");
            Assert.That(Ask(b, 1, 100.005f, 100f, -.05f), Is.EqualTo(.667f).Within(Tol), "the second ask of the same object returns the stored alpha");
            Assert.That(b.Count, Is.EqualTo(1), "two objects at one spot share one state");
            b.Begin();
            Assert.That(Ask(b, 1, 100f, 100f, -.05f), Is.EqualTo(.333f).Within(Tol), "frame 3: one more step, not two");
        }

        [Test]
        public void AFullBookAnswersFresh()
        {
            var b = new LabelClearanceBook();
            int cap = LabelClearanceBook.Capacity;
            b.Begin();
            for (int i = 0; i < cap; i++) Assert.That(Ask(b, 1, i * 10f, 0f, .45f), Is.EqualTo(1f).Within(Tol), "object " + i + " fits");
            Assert.That(b.Count, Is.EqualTo(cap), "the book is full");
            Assert.That(Ask(b, 1, 5000f, 0f, -.05f), Is.EqualTo(0f).Within(Tol), "object Capacity + 1 answers as a fresh state: born hidden");
            Assert.That(b.Count, Is.EqualTo(cap), "and stores nothing");

            b.Begin();   // frame 2: the first 64 are in their frame of grace and still count
            Assert.That(b.Count, Is.EqualTo(cap), "entries in their frame of grace count");
            Assert.That(Ask(b, 1, 5000f, 0f, -.05f), Is.EqualTo(0f).Within(Tol), "frame 2: still fresh and hidden");
            b.Begin();   // frame 3: grace again, the book is still full
            Assert.That(b.Count, Is.EqualTo(cap), "frame 3: still full");
            Assert.That(Ask(b, 1, 5000f, 0f, .45f), Is.EqualTo(1f).Within(Tol), "nothing was stored: clear, it is born at 1, not ramping");
            b.Begin();   // frame 4: the old entries are dropped
            Assert.That(b.Count, Is.EqualTo(0), "frame 4: the unasked entries are gone");
            Assert.That(Ask(b, 1, 5000f, 0f, -.05f), Is.EqualTo(0f).Within(Tol), "now it fits: born hidden");
            Assert.That(b.Count, Is.EqualTo(1), "and is stored");
            b.Begin();
            Assert.That(Ask(b, 1, 5000f, 0f, .45f), Is.EqualTo(.333f).Within(Tol), "stored: it ramps");
        }

        [Test]
        public void AClearCallDropsEveryEntry()
        {
            var b = new LabelClearanceBook();
            b.Begin();
            Ask(b, 1, 100f, 100f, -.05f); Ask(b, 1, 400f, 100f, -.05f);
            Assert.That(b.Count, Is.EqualTo(2), "set-up: two objects held");
            b.Clear();
            Assert.That(b.Count, Is.EqualTo(0), "Clear forgets them all");
            b.Begin();
            Assert.That(Ask(b, 1, 100f, 100f, .45f), Is.EqualTo(1f).Within(Tol), "an object asked after Clear is fresh");
        }

        [Test]
        public void ASteadyBookAllocatesNothing()
        {
            var b = new LabelClearanceBook();
            var label = Right(-.05f);
            var cluster = Cluster;
            b.Begin();
            Assert.That(b.Next(1, 100f, 100f, label, true, cluster, Dt, false), Is.EqualTo(0f).Within(Tol), "set-up: born hidden");
            b.Begin();
            Assert.That(b.Next(1, 100f, 100f, label, true, cluster, Dt, false), Is.EqualTo(0f).Within(Tol), "the next frame finds the stored entry, still 0");
            Assert.That(b.Count, Is.EqualTo(1), "one entry");
            float sink = 0f;
            double bytes = AllocationProbe.PerCall(i =>
            {
                b.Begin();
                sink += b.Next(1, 100f, 100f, label, true, cluster, Dt, false);
                sink += b.Keep(1, 100f, 100f);
            }, 2000);
            Assert.That(sink, Is.GreaterThanOrEqualTo(0f));
            Assert.That(bytes, Is.EqualTo(0.0), "bytes per frame");
        }

        // ---- ClusterHold ----

        static readonly LabelBox Rect1 = new LabelBox(1f, 2f, 3f, 4f);

        [Test]
        public void AFalseFrameKeepsTheLastRect()
        {
            var h = new ClusterHold();
            Assert.That(h.Read(true, Rect1, out var use), Is.True, "a true frame uses its rectangle");
            SameBox(Rect1, use, "the true frame's rectangle");
            Assert.That(h.Read(false, default, out use), Is.True, "the first false frame still answers");
            SameBox(Rect1, use, "the first false frame's rectangle is the last one");
        }

        [Test]
        public void TheSecondFalseFrameReleases()
        {
            var h = new ClusterHold();
            h.Read(true, Rect1, out _);
            Assert.That(h.Read(false, default, out _), Is.True, "first false: held");
            Assert.That(h.Read(false, default, out _), Is.False, "second false in a row: released");
            Assert.That(h.Read(false, default, out _), Is.False, "third false: still no cluster (the memory is gone)");
            var other = new LabelBox(5f, 5f, 6f, 6f);
            Assert.That(h.Read(true, other, out var use), Is.True, "a later true frame answers again");
            SameBox(other, use, "with its own rectangle");
        }

        [Test]
        public void ATrueFrameResetsTheCount()
        {
            var h = new ClusterHold();
            h.Read(true, Rect1, out _);
            Assert.That(h.Read(false, default, out _), Is.True, "false 1");
            Assert.That(h.Read(true, Rect1, out _), Is.True, "true");
            Assert.That(h.Read(false, default, out var use), Is.True, "false after a true frame is the first false again");
            SameBox(Rect1, use, "the remembered rectangle");
            Assert.That(h.Read(false, default, out _), Is.False, "the second false in a row releases");
        }

        [Test]
        public void ResetForgetsTheMemory()
        {
            var h = new ClusterHold();
            Assert.That(h.Read(true, Rect1, out _), Is.True, "set-up: a true frame is remembered");
            h.Reset();
            Assert.That(h.Read(false, default, out _), Is.False, "after Reset a false frame has nothing to keep");
        }

        [Test]
        public void NeverAnsweredIsNoCluster()
        {
            var h = new ClusterHold();
            Assert.That(h.Read(false, default, out _), Is.False, "never answered: no cluster");
            Assert.That(h.Read(false, default, out _), Is.False, "still none");
        }
    }
}
