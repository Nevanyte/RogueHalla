using System;
using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-UI-WORLD-NAMES-3 oracle: the pure state machine that glides a pack name's anchor when its token (carrier, singled-out state, member
    /// count) changes. Presentation only: the numbers are the packet's, not a legacy rule (legacy never moves a name). Time is always a dt argument.
    /// </summary>
    public sealed class NameAnchorGlideTests
    {
        const float Dt = 1f / 60f, Tol = 1e-3f;
        static readonly int GlideFrames = (int)Math.Ceiling(NameAnchorGlide.GlideSeconds / Dt);

        /// <summary>One frame of one key: Begin, Step, End.</summary>
        static (float x, float y) Frame(NameAnchorGlide g, long key, long token, float tx, float ty, float dt = Dt, bool reduced = false)
        {
            g.Begin();
            g.Step(key, token, tx, ty, dt, reduced, out float x, out float y);
            g.End();
            return (x, y);
        }

        static void Near(float actual, float expected, float tol, string what)
        {
            Assert.That(actual, Is.EqualTo(expected).Within(tol), what);
        }

        static void Residual(NameAnchorGlide g, long key, float ex, float ey, string what)
        {
            Assert.That(g.TryResidual(key, out float rx, out float ry), Is.True, what + ": the key has an entry");
            Near(rx, ex, 1e-4f, what + " residual x"); Near(ry, ey, 1e-4f, what + " residual y");
        }

        [Test]
        public void AStepIsCoveredInTheSameTimeWhateverItsSize()
        {
            Assert.That(NameAnchorGlide.GlideSeconds, Is.InRange(.12f, .18f), "a glide is a few frames long, not a snap and not a drift");
            Assert.That(NameAnchorGlide.MaxDt, Is.EqualTo(OverheadBoxLayout.ForgetSeconds), "MaxDt mirrors the layout's largest good dt");
            var dirs = new[] { (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f), (.6f, .8f), (-.6f, .8f), (.6f, -.8f), (-.6f, -.8f) };
            foreach (float dt in new[] { Dt, 1f / 30f })
            {
                int bound = (int)Math.Ceiling(NameAnchorGlide.GlideSeconds / dt) + 1;
                foreach (float size in new[] { 1f, 3f, 20f })
                    foreach (var (dx, dy) in dirs)
                    {
                        string what = "step " + size + " toward (" + dx + ", " + dy + ") at dt " + dt;
                        float sx = dx * size, sy = dy * size;
                        var g = new NameAnchorGlide();
                        Frame(g, 5, 0, 0f, 0f, dt);
                        var change = Frame(g, 5, 1, sx, sy, dt);
                        float share = dt / NameAnchorGlide.GlideSeconds;
                        Near(change.x, sx * share, Tol * size, what + ": the change frame moves dt / GlideSeconds of the step, x");
                        Near(change.y, sy * share, Tol * size, what + ": the change frame moves dt / GlideSeconds of the step, y");
                        var last = change;
                        for (int n = 1; n <= bound; n++)
                        {
                            Assert.That(g.TryResidual(5, out float rx, out float ry), Is.True, what + ": the key has an entry");
                            Assert.That(Math.Sqrt(rx * rx + ry * ry), Is.LessThanOrEqualTo(size + 1e-4), what + ": the residual never grows past the step");
                            Assert.That(sx >= 0 ? last.x >= -1e-4f && last.x <= sx + 1e-4f : last.x <= 1e-4f && last.x >= sx - 1e-4f, Is.True, what + ": drawn x stays between start and target, frame " + n);
                            Assert.That(sy >= 0 ? last.y >= -1e-4f && last.y <= sy + 1e-4f : last.y <= 1e-4f && last.y >= sy - 1e-4f, Is.True, what + ": drawn y stays between start and target, frame " + n);
                            last = Frame(g, 5, 1, sx, sy, dt);
                        }
                        Near(last.x, sx, 1e-4f, what + ": at the target after " + bound + " frames, x");
                        Near(last.y, sy, 1e-4f, what + ": at the target after " + bound + " frames, y");
                        Residual(g, 5, 0f, 0f, what + ": settled");
                    }
            }
        }

        [Test]
        public void EverySnapTakesTheTargetAtOnce()
        {
            // Each case: a settled key (two frames at 0, token 0), then the token changes and the target is 1 away.
            Action<string, Func<NameAnchorGlide, (float x, float y)>> check = (what, snap) =>
            {
                var g = new NameAnchorGlide();
                Frame(g, 1, 0, 0f, 0f); Frame(g, 1, 0, 0f, 0f);
                var drawn = snap(g);
                Near(drawn.x, 1f, 1e-5f, what + ": drawn x is the target"); Near(drawn.y, 0f, 1e-5f, what + ": drawn y");
                Residual(g, 1, 0f, 0f, what);
            };
            foreach (float dt in new[] { 0f, -1f, float.NaN, 1.0001f })
            {
                float d = dt;
                check("dt " + d, g => Frame(g, 1, 1, 1f, 0f, d));
            }
            check("reduced motion", g => Frame(g, 1, 1, 1f, 0f, Dt, true));
            check("a key never seen", g => Frame(g, 2, 1, 1f, 0f));
            check("a key skipped by one Begin/End", g =>
            {
                Frame(g, 9, 0, 0f, 0f);   // key 1 is not stepped in this frame, so End drops it
                return Frame(g, 1, 1, 1f, 0f);
            });

            var tiny = new NameAnchorGlide();
            Frame(tiny, 1, 0, 0f, 0f); Frame(tiny, 1, 0, 0f, 0f);
            var small = Frame(tiny, 1, 1, 1f, 0f, 1e-4f);
            Assert.That(small.x, Is.LessThan(.5f), "dt 1e-4 is a real dt: the name glides, it does not snap");
            Assert.That(tiny.TryResidual(1, out float rx, out _), Is.True);
            Assert.That(rx, Is.LessThan(-.5f), "and most of the step is still to come");
        }

        [Test]
        public void ATokenThatStaysNeverLags()
        {
            var g = new NameAnchorGlide();
            float x = 0f;
            for (int n = 0; n < 100; n++)
            {
                x = n * .1f;
                var drawn = Frame(g, 3, 7, x, 2f);
                Near(drawn.x, x, 1e-5f, "a walking pack with one token is never delayed, frame " + n);
                Near(drawn.y, 2f, 1e-5f, "y");
                Residual(g, 3, 0f, 0f, "frame " + n);
            }

            var w = new NameAnchorGlide();
            Frame(w, 1, 0, 0f, 0f);
            Near(Frame(w, 1, 1, 1f, 0f).x, .1111f, Tol, "the change frame");
            Near(Frame(w, 1, 1, 1.2f, 0f).x, .4222f, Tol, "the target moved on: the residual (-.8889) shrinks to -.7778 along its line");
        }

        [Test]
        public void AlternatingTokensDoNotPileUpAResidual()
        {
            var g = new NameAnchorGlide();
            Frame(g, 1, 0, 0f, 0f);   // frame 1: a first sight at 0
            float prev = 0f; bool differed = false;
            float bound = Dt / NameAnchorGlide.GlideSeconds + 1e-3f;
            for (int n = 2; n <= 120; n++)
            {
                float v = n % 2 == 0 ? 1f : 0f;
                var drawn = Frame(g, 1, (long)v, v, 0f);
                if (n == 2)
                {
                    Assert.That(g.TryResidual(1, out float rx, out float ry), Is.True, "positive control: the key has an entry on frame 2");
                    Assert.That(Math.Sqrt(rx * rx + ry * ry), Is.GreaterThan(.5), "positive control: a residual of about .889");
                    Assert.That(drawn.x, Is.GreaterThan(0f).And.LessThan(1f), "positive control: the drawn x is between the two anchors (.111)");
                }
                if (Math.Abs(drawn.x - v) > 1e-4f) differed = true;
                Assert.That(g.TryResidual(1, out float qx, out float qy), Is.True, "frame " + n + ": the key has an entry");
                Assert.That(Math.Sqrt(qx * qx + qy * qy), Is.LessThanOrEqualTo(1 + 1e-4), "frame " + n + ": the residual never piles up past one step");
                Assert.That(drawn.x, Is.InRange(-1e-4f, 1f + 1e-4f), "frame " + n + ": drawn stays between the anchors");
                Assert.That(Math.Abs(drawn.x - prev), Is.LessThanOrEqualTo(bound), "frame " + n + ": a frame moves the drawn x by at most a ninth of the step");
                if (n >= 40) Assert.That(drawn.x, Is.InRange(.45f, .55f), "frame " + n + ": the name rests near the middle (8/17 to 9/17)");
                prev = drawn.x;
            }
            Assert.That(differed, Is.True, "the drawn x differs from the target on at least one frame");

            // Frame 120 was B (token 1, target 1): keep it; the residual runs out inside the glide time.
            (float x, float y) last = (0f, 0f);
            for (int n = 0; n < GlideFrames + 1; n++) last = Frame(g, 1, 1, 1f, 0f);
            Near(last.x, 1f, 1e-4f, "steady on B: at the target within the glide time");
            Residual(g, 1, 0f, 0f, "steady on B");
        }

        [Test]
        public void ATokenChangeOfNoDistanceStaysPut()
        {
            var g = new NameAnchorGlide();
            Frame(g, 4, 0, 2f, 3f); Frame(g, 4, 0, 2f, 3f);
            var same = Frame(g, 4, 1, 2f, 3f);   // the token changes, the anchor does not
            Near(same.x, 2f, 1e-5f, "x stays"); Near(same.y, 3f, 1e-5f, "y stays");
            Residual(g, 4, 0f, 0f, "no distance, no residual");
            for (int n = 1; n <= 10; n++)
            {
                var drawn = Frame(g, 4, 1, 2f + n * .1f, 3f - n * .05f);
                Assert.That(float.IsNaN(drawn.x) || float.IsNaN(drawn.y), Is.False, "no NaN, frame " + n);
                Near(drawn.x, 2f + n * .1f, 1e-5f, "x follows, frame " + n); Near(drawn.y, 3f - n * .05f, 1e-5f, "y follows, frame " + n);
            }
        }

        [Test]
        public void KeysAreIndependentAndForgotten()
        {
            var g = new NameAnchorGlide();
            Action<long, long, float, float> step = (key, token, tx, ty) => { g.Step(key, token, tx, ty, Dt, false, out _, out _); };
            g.Begin(); step(1, 0, 0f, 0f); step(2, 0, 10f, 0f); g.End();
            Assert.That(g.Count, Is.EqualTo(2), "two keys, two entries");
            g.Begin();
            g.Step(1, 1, 1f, 0f, Dt, false, out float x1, out _);
            g.Step(2, 0, 10f, 0f, Dt, false, out float x2, out _);
            g.End();
            Near(x1, .1111f, Tol, "the key whose token changed glides"); Near(x2, 10f, 1e-5f, "the other key is not touched");
            Residual(g, 2, 0f, 0f, "the key that kept its token");

            g.Begin(); step(1, 1, 1f, 0f); g.End();
            Assert.That(g.Count, Is.EqualTo(1), "End drops the key that was not stepped");
            Assert.That(g.TryResidual(2, out _, out _), Is.False, "and it has no residual any more");

            g.Clear();
            Assert.That(g.Count, Is.EqualTo(0), "Clear drops all");

            // Forty keys: the arrays grow and every key keeps its own state.
            g.Begin(); for (int k = 0; k < 40; k++) step(k, 0, k, 0f); g.End();
            g.Begin();
            for (int k = 0; k < 40; k++)
            {
                g.Step(k, k % 2 == 0 ? 1 : 0, k % 2 == 0 ? k + 1f : k, 0f, Dt, false, out float x, out _);
                Near(x, k % 2 == 0 ? k + .1111f : k, Tol, "key " + k);
            }
            g.End();
            Assert.That(g.Count, Is.EqualTo(40));
            for (int k = 0; k < 40; k++) Residual(g, k, k % 2 == 0 ? -.8889f : 0f, 0f, "key " + k);
        }

        [Test]
        public void CarryGivesTheNewKeyTheOldKeysAnchor()
        {
            var g = new NameAnchorGlide();
            Frame(g, 7, 1, 0f, 0f);
            g.Begin();
            Assert.That(g.Carry(7, 9), Is.True, "positive control: the new key has none, the old one has");
            Assert.That(g.Count, Is.EqualTo(2));
            g.Step(9, 2, 1f, 0f, Dt, false, out float x, out _);
            Near(x, .1111f, Tol, "the new key's first frame is not a first sight: it glides from the old anchor");
            Assert.That(g.Carry(7, 9), Is.False, "the new key has an entry now");
            Assert.That(g.Carry(99, 9), Is.False, "an unknown old key");
            Assert.That(g.Carry(7, 7), Is.False, "the same key");
            Assert.That(g.TryResidual(7, out _, out _), Is.True, "the old entry stays");
            g.End();
            Assert.That(g.Count, Is.EqualTo(1), "End drops the old key that was not stepped");
        }

        [Test]
        public void ASteadyFrameAllocatesNothing()
        {
            var tokens = new long[8]; var offset = new float[8];
            int n = 0;
            var g = new NameAnchorGlide();
            Action frame = () =>
            {
                n++;
                if (n % 7 == 0) { int k = (n / 7) % 8; tokens[k]++; offset[k] += 1f; }
                g.Begin();
                for (int k = 0; k < 8; k++) g.Step(k, tokens[k], k + offset[k], 0f, Dt, false, out _, out _);
                g.End();
            };
            for (int i = 0; i < 7; i++) frame();
            Assert.That(g.Count, Is.EqualTo(8), "set-up: eight keys");
            Assert.That(g.TryResidual(1, out float rx, out float ry), Is.True, "set-up: the key that changed on frame 7 has an entry");
            Assert.That(rx != 0f || ry != 0f, Is.True, "set-up: and it is mid-glide, so the measured frames include a live residual");
            double bytes = AllocationProbe.PerCallExact(i => frame(), 0, 2000, 5);
            Assert.That(bytes, Is.EqualTo(0.0), "a steady frame of eight names allocates nothing");
        }
    }
}
