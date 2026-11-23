using System;
using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-UI-WORLD-NAMES-1: the rule that lifts world names (a name with its marks) clear of each other. Presentation only: the
    /// numbers are the packet's, not a legacy rule. A box is one part (-W/2, -H/2, W/2, H/2); a box at x = W + g has a gap g to a
    /// box at 0. Every expectation names a constant of the library (Gap, the pads, MaxLift, ForgetSeconds) or of LabelLift.
    /// </summary>
    public sealed class OverheadBoxLayoutTests
    {
        const float W = 1f, H = .34f, Dt = 1f / 60f, Eps = .01f, Tol = 1e-4f;
        const float Step = H + OverheadBoxLayout.Gap;
        const float Mid = (OverheadBoxLayout.PadEnter + OverheadBoxLayout.PadKeep) / 2f;
        static readonly int Hold = (int)(LabelLift.HoldSeconds / Dt);

        static int Box(OverheadBoxLayout l, long key, byte cls, float x, float y = 0f, bool avoidFixed = true)
        {
            int b = l.AddBox(key, cls, x, y, avoidFixed);
            l.AddPart(-W / 2, -H / 2, W / 2, H / 2);
            return b;
        }

        /// <summary>One frame: A (class elite, key 1) at 0 and B (class mob, key 2) at gap g; returns B's lift.</summary>
        static float Frame(OverheadBoxLayout l, float g, bool reduced = false, float dt = Dt, float y = 0f)
        {
            l.Begin();
            Box(l, 1, OverheadBoxLayout.ClassElite, 0f);
            int b = Box(l, 2, OverheadBoxLayout.ClassMob, W + g, y);
            l.Solve(dt, reduced);
            return l.Lift(b);
        }

        static void Near(float expected, float actual, string what)
        {
            Assert.That(actual, Is.EqualTo(expected).Within(Tol), what);
        }

        [Test]
        public void TwoTouchingBoxesTheLaterInTheOrderYields()
        {
            float g = OverheadBoxLayout.PadEnter / 4f;
            var l = new OverheadBoxLayout();
            l.Begin();
            int a = Box(l, 1, OverheadBoxLayout.ClassElite, 0f);
            int b = Box(l, 2, OverheadBoxLayout.ClassMob, W + g);
            Near(0f, l.Lift(b), "before a solve Lift reads 0");
            Near(0f, l.Target(b), "before a solve Target reads 0");
            l.Solve(Dt, false);
            Near(0f, l.Lift(a), "the earlier in the order stays");
            Near(Step, l.Lift(b), "the later yields by the height of a name and the gap");
            Near(Step, l.Target(b), "Target of the one that yields");
            Near(0f, l.Target(a), "Target of the one that stays");

            l = new OverheadBoxLayout();   // B added first: the order is by class, not by add order
            l.Begin();
            b = Box(l, 2, OverheadBoxLayout.ClassMob, W + g);
            a = Box(l, 1, OverheadBoxLayout.ClassElite, 0f);
            l.Solve(Dt, false);
            Near(0f, l.Lift(a), "A stays when added second");
            Near(Step, l.Lift(b), "B yields when added first");

            l = new OverheadBoxLayout();   // B on the other side
            l.Begin();
            a = Box(l, 1, OverheadBoxLayout.ClassElite, 0f);
            b = Box(l, 2, OverheadBoxLayout.ClassMob, -(W + g));
            l.Solve(Dt, false);
            Near(0f, l.Lift(a), "A stays with B on its left");
            Near(Step, l.Lift(b), "B yields on its left");

            l = new OverheadBoxLayout();   // classes swapped: A is the mob name now
            l.Begin();
            a = Box(l, 1, OverheadBoxLayout.ClassMob, 0f);
            b = Box(l, 2, OverheadBoxLayout.ClassElite, W + g);
            l.Solve(Dt, false);
            Near(Step, l.Lift(a), "the mob name yields to the elite");
            Near(0f, l.Lift(b), "the elite stays");

            l = new OverheadBoxLayout();   // equal classes: the lower key stays
            l.Begin();
            a = Box(l, 1, OverheadBoxLayout.ClassFighter, 0f);
            b = Box(l, 2, OverheadBoxLayout.ClassFighter, W + g);
            l.Solve(Dt, false);
            Near(0f, l.Lift(a), "the lower key stays");
            Near(Step, l.Lift(b), "the higher key yields");

            l = new OverheadBoxLayout();   // the same with the keys the other way round
            l.Begin();
            a = Box(l, 2, OverheadBoxLayout.ClassFighter, 0f);
            b = Box(l, 1, OverheadBoxLayout.ClassFighter, W + g);
            l.Solve(Dt, false);
            Near(Step, l.Lift(a), "the higher key yields wherever it stands");
            Near(0f, l.Lift(b), "the lower key stays");
        }

        [Test]
        public void PadsGapAndJitter()
        {
            Assert.That(OverheadBoxLayout.Gap, Is.GreaterThan(0f), "Gap");
            Assert.That(OverheadBoxLayout.PadEnter, Is.GreaterThan(0f), "PadEnter");
            Assert.That(OverheadBoxLayout.PadKeep, Is.GreaterThan(OverheadBoxLayout.PadEnter), "PadKeep above PadEnter");

            Near(Step, Frame(new OverheadBoxLayout(), OverheadBoxLayout.PadEnter - Eps), "just inside PadEnter");
            Near(0f, Frame(new OverheadBoxLayout(), OverheadBoxLayout.PadEnter + Eps), "just outside PadEnter");
            Near(Eps, Frame(new OverheadBoxLayout(), -W, y: Step - Eps), "directly above, inside the gap");
            Near(0f, Frame(new OverheadBoxLayout(), -W, y: Step + Eps), "directly above, outside the gap");

            var l = new OverheadBoxLayout();   // a box jittering across the entry pad inside the keep pad never lets go
            for (int i = 0; i < 120; i++)
            {
                float lift = Frame(l, i % 2 == 0 ? OverheadBoxLayout.PadEnter / 2f : Mid);
                Near(Step, lift, "solve " + i);
            }
        }

        [Test]
        public void TheKeepBandHoldsThenReleases()
        {
            var l = new OverheadBoxLayout();
            Near(0f, Frame(l, Mid), "fresh at the mid gap: no lift");
            Assert.That(l.MemoryCount, Is.EqualTo(2), "a good dt records every box, a lift of 0 too");

            for (int n = 1; n <= 5; n++)
                Near(Math.Min(Step, n * LabelLift.RiseUnitsPerSecond * Dt), Frame(l, OverheadBoxLayout.PadEnter / 2f), "rise " + n);
            for (int i = 0; i < 10; i++) Near(Step, Frame(l, Mid), "kept at the mid gap, solve " + i);

            float far = OverheadBoxLayout.PadKeep + .05f, prev = Step;
            int zeroAt = Hold + (int)Math.Ceiling(Step / (LabelLift.FallUnitsPerSecond * Dt)) + 3;
            for (int n = 1; n <= zeroAt; n++)
            {
                l.Begin();
                Box(l, 1, OverheadBoxLayout.ClassElite, 0f);
                int b = Box(l, 2, OverheadBoxLayout.ClassMob, W + far);
                l.Solve(Dt, false);
                float lift = l.Lift(b);
                if (n <= Hold - 3)
                {
                    Near(Step, lift, "held, solve " + n);
                    Near(Step, l.Target(b), "target held, solve " + n);
                }
                else Assert.That(Math.Abs(lift - prev), Is.LessThanOrEqualTo(LabelLift.FallUnitsPerSecond * Dt + Tol), "a fall, never a jump, solve " + n);
                prev = lift;
                if (n == zeroAt) Near(0f, lift, "released by solve " + n);
            }

            // A also holds a mark above its name: B lifts over all of it, and keeps that whole lift in the keep band.
            float tall = H / 2f + .63f + OverheadBoxLayout.Gap + H / 2f;
            l = new OverheadBoxLayout();
            float Tall(float g)
            {
                l.Begin();
                Box(l, 1, OverheadBoxLayout.ClassElite, 0f);
                l.AddPart(-W / 2, H / 2 + .03f, W / 2, H / 2 + .63f);
                int b = Box(l, 2, OverheadBoxLayout.ClassMob, W + g);
                l.Solve(Dt, false);
                return l.Lift(b);
            }
            Near(tall, Tall(OverheadBoxLayout.PadEnter / 2f), "lifted over the name and its mark");
            for (int i = 0; i < 10; i++) Near(tall, Tall(Mid), "kept over the whole cluster, solve " + i);
            l = new OverheadBoxLayout();
            Near(0f, Tall(Mid), "fresh at the mid gap: nothing");
        }

        [Test]
        public void AClusterMovesAsOne()
        {
            float g = OverheadBoxLayout.PadEnter / 4f;
            var l = new OverheadBoxLayout();
            // One Solve far away (B's lift 0), one in contact: Lift (still rising) and Target differ.
            for (int pass = 0; pass < 2; pass++)
            {
                l.Begin();
                Box(l, 1, OverheadBoxLayout.ClassElite, 0f);
                l.AddPart(-.25f, H / 2 - .05f, .25f, H / 2 + .45f);
                float bx = pass == 0 ? 5f : W + g;
                int bb = Box(l, 2, OverheadBoxLayout.ClassMob, bx);
                l.AddPart(-W / 2, H / 2 - .06f, W / 2, H / 2 + .03f);
                l.Solve(Dt, false);
                if (pass == 0) continue;
                Near(Step, l.Target(bb), "the cluster rises by the name's need");
                Near(LabelLift.RiseUnitsPerSecond * Dt, l.Lift(bb), "drawn lift is still rising");
                Assert.That(l.PartCount(bb), Is.EqualTo(2), "B has two parts");
                float[] baseY0 = { -H / 2, H / 2 - .06f }, baseY1 = { H / 2, H / 2 + .03f };
                for (int p = 0; p < 2; p++)
                {
                    l.PartRect(bb, p, true, out float x0, out float y0, out float x1, out float y1);
                    Near(bx - W / 2, x0, "x0 at target, part " + p); Near(bx + W / 2, x1, "x1 at target, part " + p);
                    Near(baseY0[p] + Step, y0, "y0 at target, part " + p); Near(baseY1[p] + Step, y1, "y1 at target, part " + p);
                    l.PartRect(bb, p, false, out x0, out y0, out x1, out y1);
                    Near(baseY0[p] + LabelLift.RiseUnitsPerSecond * Dt, y0, "y0 drawn, part " + p);
                    Near(baseY1[p] + LabelLift.RiseUnitsPerSecond * Dt, y1, "y1 drawn, part " + p);
                }
            }

            // B's name beside the star at half PadEnter: the whole cluster rises over the star.
            l = new OverheadBoxLayout();
            l.Begin();
            int a = Box(l, 1, OverheadBoxLayout.ClassElite, 0f);
            l.AddPart(-.25f, H / 2 - .05f, .25f, H / 2 + .45f);
            int b = Box(l, 2, OverheadBoxLayout.ClassMob, .25f + OverheadBoxLayout.PadEnter / 2f + W / 2f);
            l.AddPart(-W / 2, H / 2 - .06f, W / 2, H / 2 + .03f);
            l.Solve(Dt, false);
            float over = .45f + H / 2 + OverheadBoxLayout.Gap + H / 2;
            Near(over, l.Lift(b), "over the star");
            for (int p = 0; p < l.PartCount(b); p++)
                for (int q = 0; q < l.PartCount(a); q++)
                {
                    l.PartRect(b, p, true, out float bx0, out float by0, out float bx1, out float by1);
                    l.PartRect(a, q, true, out float ax0, out float ay0, out float ax1, out float ay1);
                    bool meet = bx0 < ax1 && ax0 < bx1 && by0 < ay1 && ay0 < by1;
                    Assert.That(meet, Is.False, "part " + p + " of B meets part " + q + " of A");
                }
        }

        [Test]
        public void FixedRectanglesLiftEveryBoxButOptOuts()
        {
            var l = new OverheadBoxLayout();
            l.Begin();
            l.AddFixed(-W / 2, -.05f, W / 2, .05f);
            int b = Box(l, 1, OverheadBoxLayout.ClassMob, 0f);
            l.Solve(Dt, false);
            Near(.05f + OverheadBoxLayout.Gap + H / 2, l.Lift(b), "a bar under a name lifts it");

            l = new OverheadBoxLayout();
            l.Begin();
            l.AddFixed(-W / 2, -.05f, W / 2, .05f);
            b = Box(l, 1, OverheadBoxLayout.ClassMob, 0f, avoidFixed: false);
            l.Solve(Dt, false);
            Near(0f, l.Lift(b), "avoidFixed false ignores it");

            l = new OverheadBoxLayout();
            l.Begin();
            l.AddFixed(-W / 2, -H / 2 - OverheadBoxLayout.Gap - .15f, W / 2, -H / 2 - OverheadBoxLayout.Gap - Eps);
            b = Box(l, 1, OverheadBoxLayout.ClassMob, 0f);
            l.Solve(Dt, false);
            Near(0f, l.Lift(b), "a rectangle clear of the gap lifts nothing");
        }

        [Test]
        public void AFrozenOrCutClockForgets()
        {
            float far = OverheadBoxLayout.PadKeep + .05f;
            foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, OverheadBoxLayout.ForgetSeconds + .5f })
            {
                var l = new OverheadBoxLayout();
                Near(Step, Frame(l, OverheadBoxLayout.PadEnter / 4f), "contact first, dt " + bad);
                Assert.That(l.MemoryCount, Is.EqualTo(2), "memory after a good solve, dt " + bad);
                Near(0f, Frame(l, far, dt: bad), "a bad dt forgets, dt " + bad);
                Assert.That(l.MemoryCount, Is.EqualTo(0), "a bad dt records nothing, dt " + bad);
            }

            var c = new OverheadBoxLayout();
            Near(Step, Frame(c, OverheadBoxLayout.PadEnter / 4f), "contact before Clear");
            c.Clear();
            Assert.That(c.MemoryCount, Is.EqualTo(0), "Clear forgets at once");
            Near(0f, Frame(c, far), "after Clear nothing is kept");
            Assert.That(c.MemoryCount, Is.EqualTo(2), "a good solve records again");

            var t = new OverheadBoxLayout();   // two bad solves: each is first sight
            Near(Step, Frame(t, OverheadBoxLayout.PadEnter / 4f, dt: 0f), "bad dt, contact");
            Near(0f, Frame(t, far, dt: 0f), "bad dt, no contact: no hold");

            // Ageing: a key unseen for less than ForgetSeconds keeps its memory.
            var s = new OverheadBoxLayout();
            Near(Step, Frame(s, OverheadBoxLayout.PadEnter / 4f), "raised");
            s.Begin();
            Box(s, 1, OverheadBoxLayout.ClassElite, 0f);
            s.Solve(OverheadBoxLayout.ForgetSeconds * .5f, false);
            Assert.That(s.TryMemory(2L, out float v, out float tg), Is.True, "kept after half ForgetSeconds");
            Near(Step, tg, "its target is still there");
            Near(Step, Frame(s, far), "back inside the hold: still raised");

            var u = new OverheadBoxLayout();
            Near(Step, Frame(u, OverheadBoxLayout.PadEnter / 4f), "raised");
            for (int i = 0; i < 2; i++)
            {
                u.Begin();
                Box(u, 1, OverheadBoxLayout.ClassElite, 0f);
                u.Solve(OverheadBoxLayout.ForgetSeconds * .6f, false);
            }
            Assert.That(u.TryMemory(2L, out v, out tg), Is.False, "forgotten after more than ForgetSeconds");
            Near(0f, Frame(u, far), "a forgotten key starts again");
        }

        [Test]
        public void ReducedMotionJumpsAndKeepsTheHold()
        {
            float far = OverheadBoxLayout.PadKeep + .05f;
            var l = new OverheadBoxLayout();
            Near(0f, Frame(l, far, true), "an entry with no lift");
            l.Begin();
            Box(l, 1, OverheadBoxLayout.ClassElite, 0f);
            int b = Box(l, 2, OverheadBoxLayout.ClassMob, W + OverheadBoxLayout.PadEnter / 4f);
            l.Solve(Dt, true);
            Near(Step, l.Lift(b), "reduced motion: at once");
            Near(Step, l.Target(b), "reduced motion: target");
            for (int n = 1; n <= Hold + 3; n++)
            {
                float lift = Frame(l, far, true);
                if (n <= Hold - 3) Near(Step, lift, "reduced motion keeps the hold, solve " + n);
                if (n == Hold + 3) Near(0f, lift, "reduced motion lets go after the hold");
            }
        }

        [Test]
        public void TheStackTheCapAndTargets()
        {
            int n = (int)Math.Ceiling(OverheadBoxLayout.MaxLift / Step) + 2;
            var l = new OverheadBoxLayout();
            l.Begin();
            var ids = new int[n];
            for (int i = 0; i < n; i++) ids[i] = Box(l, i + 1, OverheadBoxLayout.ClassFighter, 0f);
            l.Solve(Dt, false);
            for (int i = 0; i < n; i++) Near(Math.Min(i * Step, OverheadBoxLayout.MaxLift), l.Lift(ids[i]), "box " + i);
            Near(OverheadBoxLayout.MaxLift, l.Lift(ids[n - 1]), "the last is capped");

            // A later box avoids where an earlier one is going, not where it is drawn.
            l = new OverheadBoxLayout();
            l.Begin();
            Box(l, 1, OverheadBoxLayout.ClassFighter, 0f);
            Box(l, 2, OverheadBoxLayout.ClassFighter, W + OverheadBoxLayout.PadKeep + .05f);
            l.Solve(Dt, false);
            l.Begin();
            Box(l, 1, OverheadBoxLayout.ClassFighter, 0f);
            int b = Box(l, 2, OverheadBoxLayout.ClassFighter, 0f);
            int c = Box(l, 3, OverheadBoxLayout.ClassFighter, 0f);
            l.Solve(Dt, false);
            Near(LabelLift.RiseUnitsPerSecond * Dt, l.Lift(b), "B is still rising");
            Near(Step, l.Target(b), "B's target");
            Near(2 * Step, l.Lift(c), "C clears B's target");
        }

        [Test]
        public void ASteadyFrameAllocatesNothing()
        {
            const int Boxes = 40, Fixed = 30;
            var x = new float[Boxes]; var y = new float[Boxes];
            for (int i = 0; i < Boxes; i++) { x[i] = (i % 8) * .6f; y[i] = (i / 8) * .3f; }
            var fx = new float[Fixed]; var fy = new float[Fixed];
            for (int i = 0; i < Fixed; i++) { fx[i] = (i % 10) * .5f; fy[i] = (i / 10) * .4f; }
            var l = new OverheadBoxLayout();
            Action frame = () =>
            {
                l.Begin();
                for (int i = 0; i < Boxes; i++)
                {
                    l.AddBox(OverheadBoxLayout.SerialKey(i + 1), OverheadBoxLayout.ClassFighter, x[i], y[i], true);
                    l.AddPart(-.5f, -.17f, .5f, .17f);
                    l.AddPart(-.3f, .2f, .3f, .5f);
                    l.AddPart(.3f, .2f, .6f, .5f);
                }
                for (int i = 0; i < Fixed; i++) l.AddFixed(fx[i], fy[i], fx[i] + .4f, fy[i] + .08f);
                l.Solve(Dt, false);
            };
            frame();
            Assert.That(l.BoxCount, Is.EqualTo(Boxes), "BoxCount");
            Assert.That(l.MemoryCount, Is.EqualTo(Boxes), "MemoryCount");
            bool lifted = false;
            for (int i = 0; i < Boxes; i++) lifted |= l.Lift(i) > 0f;
            Assert.That(lifted, Is.True, "overlapping boxes lift");
            double bytes = AllocationProbe.PerCallExact(i => frame(), 0, 2000, 5);
            Assert.That(bytes, Is.EqualTo(0.0), "bytes per steady frame");
        }

        [Test]
        public void TheMemoryFollowsTheKeyNotTheGeometry()
        {
            float far = OverheadBoxLayout.PadKeep + .05f;
            var l = new OverheadBoxLayout();
            l.Begin();
            Box(l, 1, OverheadBoxLayout.ClassElite, 0f);
            int b = Box(l, 7, OverheadBoxLayout.ClassMob, W + OverheadBoxLayout.PadEnter / 4f);
            l.Solve(Dt, false);
            Near(Step, l.Lift(b), "in contact");

            l.Begin();   // a pack that lost a member: another shape, the same key
            Box(l, 1, OverheadBoxLayout.ClassElite, 0f);
            b = Box(l, 7, OverheadBoxLayout.ClassMob, W + far);
            l.AddPart(-W / 2, H / 2, W / 2, H / 2 + .2f);
            l.Solve(Dt, false);
            Near(Step, l.Lift(b), "the memory follows the key");

            l.Begin();   // the same box under another key
            Box(l, 1, OverheadBoxLayout.ClassElite, 0f);
            b = Box(l, 8, OverheadBoxLayout.ClassMob, W + far);
            l.AddPart(-W / 2, H / 2, W / 2, H / 2 + .2f);
            l.Solve(Dt, false);
            Near(0f, l.Lift(b), "a new key has no memory");
            Assert.That(l.TryMemory(8, out float value, out float target), Is.True, "the new key is remembered");
        }

        [Test]
        public void KeysAndClassesKeepTheOrderTheDesignNeeds()
        {
            Assert.That(OverheadBoxLayout.SerialKey(5), Is.EqualTo(5L), "SerialKey");
            Assert.That(OverheadBoxLayout.SerialKey(int.MaxValue), Is.EqualTo((long)int.MaxValue), "SerialKey max");
            Assert.That(OverheadBoxLayout.CampKey(3), Is.EqualTo(OverheadBoxLayout.CampKeyBase + 3), "CampKey");
            Assert.That(OverheadBoxLayout.FighterKey(0), Is.EqualTo(OverheadBoxLayout.FighterKeyBase), "FighterKey 0");
            Assert.That(OverheadBoxLayout.FighterKey(uint.MaxValue), Is.EqualTo(OverheadBoxLayout.FighterKeyBase + uint.MaxValue), "FighterKey max");
            Assert.That(OverheadBoxLayout.CampKey(1), Is.GreaterThan(OverheadBoxLayout.SerialKey(int.MaxValue)), "camp keys above serial keys");
            Assert.That(OverheadBoxLayout.FighterKey(0), Is.GreaterThan(OverheadBoxLayout.CampKey(int.MaxValue)), "fighter keys above camp keys");

            byte[] order = { OverheadBoxLayout.ClassLocal, OverheadBoxLayout.ClassPrompt, OverheadBoxLayout.ClassFighter, OverheadBoxLayout.ClassGiver,
                OverheadBoxLayout.ClassElite, OverheadBoxLayout.ClassMob, OverheadBoxLayout.ClassProp };
            for (int i = 1; i < order.Length; i++) Assert.That(order[i], Is.GreaterThan(order[i - 1]), "class " + i);

            var l = new OverheadBoxLayout();   // a serial-keyed A beats a camp-keyed B of one class, wherever it was added
            l.Begin();
            int b = Box(l, OverheadBoxLayout.CampKey(1), OverheadBoxLayout.ClassMob, W + OverheadBoxLayout.PadEnter / 4f);
            int a = Box(l, OverheadBoxLayout.SerialKey(5), OverheadBoxLayout.ClassMob, 0f);
            l.Solve(Dt, false);
            Near(0f, l.Lift(a), "the serial key stays");
            Near(Step, l.Lift(b), "the camp key yields");
        }
    }
}
