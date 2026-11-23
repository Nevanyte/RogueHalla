using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-UI-WORLD-NAMES-3: <see cref="OverheadBoxLayout.Carry"/> copies one key's memory to another, so a pack whose key changes (a mob leaves its
    /// camp's reach, a camp-less pack loses its lowest serial) keeps the lift of its name. The box, hold and frame helpers are those of
    /// <see cref="OverheadBoxLayoutTests"/>; the box under test takes its key as a parameter.
    /// </summary>
    public sealed class OverheadBoxLayoutCarryTests
    {
        const float W = 1f, H = .34f, Dt = 1f / 60f, Tol = 1e-4f;
        const float Step = H + OverheadBoxLayout.Gap;
        static readonly int Hold = (int)(LabelLift.HoldSeconds / Dt);

        static int Box(OverheadBoxLayout l, long key, byte cls, float x, float y = 0f)
        {
            int b = l.AddBox(key, cls, x, y, true);
            l.AddPart(-W / 2, -H / 2, W / 2, H / 2);
            return b;
        }

        /// <summary>One frame: A (elite, key 1) at 0 and B (mob, key <paramref name="key"/>) at gap g; returns B's lift.</summary>
        static float Frame(OverheadBoxLayout l, long key, float g, bool reduced = false)
        {
            l.Begin();
            Box(l, 1, OverheadBoxLayout.ClassElite, 0f);
            int b = Box(l, key, OverheadBoxLayout.ClassMob, W + g);
            l.Solve(Dt, reduced);
            return l.Lift(b);
        }

        static void Near(float expected, float actual, string what)
        {
            Assert.That(actual, Is.EqualTo(expected).Within(Tol), what);
        }

        // A lifted box under key 7 (the contact of TheMemoryFollowsTheKeyNotTheGeometry).
        static OverheadBoxLayout LiftedUnder7(bool reduced = false)
        {
            var l = new OverheadBoxLayout();
            float lift = Frame(l, 7, OverheadBoxLayout.PadEnter / 4f, reduced);
            Near(Step, lift, "set-up: in contact the box is lifted");
            return l;
        }

        [Test]
        public void CarryCopiesTheEntryWhenTheNewKeyHasNone()
        {
            var l = LiftedUnder7();
            Assert.That(l.TryMemory(7, out float value, out float target), Is.True, "set-up: key 7 has a memory");
            int before = l.MemoryCount;
            Assert.That(l.Carry(7, 9), Is.True, "the old key has an entry, the new key none");
            Assert.That(l.TryMemory(9, out float v9, out float t9), Is.True, "the new key has a memory");
            Near(value, v9, "the value"); Near(target, t9, "the target");
            Assert.That(l.MemoryCount, Is.EqualTo(before + 1));
            Assert.That(l.TryMemory(7, out float v7, out float t7), Is.True, "the old entry stays");
            Near(value, v7, "old value"); Near(target, t7, "old target");
        }

        [Test]
        public void CarryNeverOverwritesAndIgnoresUnknownKeys()
        {
            var l = LiftedUnder7();
            l.TryMemory(7, out float value, out float target);
            Assert.That(l.Carry(7, 12), Is.True, "positive control");
            l.TryMemory(12, out float v12, out float t12);
            Near(value, v12, "the copy"); Near(target, t12, "the copy's target");
            Assert.That(l.Carry(7, 12), Is.False, "the same call again: the new key has an entry");

            // Key 9 present with no lift, key 7 lifted.
            l.Begin();
            Box(l, 1, OverheadBoxLayout.ClassElite, 0f);
            Box(l, 7, OverheadBoxLayout.ClassMob, W + OverheadBoxLayout.PadEnter / 4f);
            Box(l, 9, OverheadBoxLayout.ClassMob, 10f);
            l.Solve(Dt, false);
            Assert.That(l.TryMemory(9, out float v9, out float t9), Is.True, "set-up: key 9 has a memory");
            Near(0f, v9, "set-up: key 9 has no lift");
            int count = l.MemoryCount;
            Assert.That(l.Carry(7, 9), Is.False, "never overwrites");
            l.TryMemory(9, out float after, out float afterTarget);
            Near(0f, after, "key 9 unchanged"); Near(0f, afterTarget, "key 9 target unchanged");
            Assert.That(l.Carry(99, 9), Is.False, "an unknown old key");
            Assert.That(l.Carry(7, 7), Is.False, "the same key");
            Assert.That(new OverheadBoxLayout().Carry(7, 9), Is.False, "an empty memory");
            Assert.That(l.MemoryCount, Is.EqualTo(count), "the count did not change");
        }

        [Test]
        public void ACarriedEntryHoldsLikeAnyOther()
        {
            float far = OverheadBoxLayout.PadKeep + .05f;
            var l = LiftedUnder7(true);
            Assert.That(l.Carry(7, 9), Is.True, "set-up: the entry is carried to key 9");
            for (int n = 1; n <= Hold + 3; n++)
            {
                float lift = Frame(l, 9, far, true);
                if (n <= Hold - 3) Near(Step, lift, "a carried entry holds its lift, frame " + n);
                if (n == Hold + 3) Near(0f, lift, "and lets go after the hold");
            }

            var plain = LiftedUnder7(true);   // without Carry the new key is a first sight
            Near(0f, Frame(plain, 9, far, true), "without Carry the new key reads 0 on its first frame");
        }
    }
}
