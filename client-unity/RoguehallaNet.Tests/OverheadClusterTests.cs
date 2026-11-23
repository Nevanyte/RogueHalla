using System;
using System.Collections.Generic;
using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>
    /// P-UI-WORLD-NAMES-1: an overhead stack (OverheadStack.Build) becomes the parts of one box: the name, the marks above and
    /// beside it, the elite star; the bars are not parts. Offsets are world units from the actor's origin, y relative to the
    /// name's centre. k is world units per legacy pixel at the test's worldPerLocal.
    /// </summary>
    public sealed class OverheadClusterTests
    {
        const float WorldPerLocal = 1.2f, K = WorldPerLocal * .026f, NameH = .34f, Tol = 1e-3f;

        static List<OverheadElement> Stack(OverheadFacts f)
        {
            var into = new List<OverheadElement>();
            OverheadStack.Build(in f, into);
            return into;
        }

        static OverheadBoxLayout One(out int box)
        {
            var l = new OverheadBoxLayout();
            l.Begin();
            box = l.AddBox(1, OverheadBoxLayout.ClassFighter, 0f, 0f, true);
            return l;
        }

        static void Part(OverheadBoxLayout l, int box, int part, float x0, float y0, float x1, float y1, string what)
        {
            l.PartRect(box, part, false, out float ax0, out float ay0, out float ax1, out float ay1);
            Assert.That(new[] { ax0, ay0, ax1, ay1 }, Is.EqualTo(new[] { x0, y0, x1, y1 }).Within(Tol), what);
        }

        static OverheadFacts Fighter(int bounty, bool offhand, byte stat1, byte stat2, int tier, int wish) => new OverheadFacts
        {
            Kind = OverheadActorKind.Player, Name = "Ann", NameWidthPx = 40f, HpPct = 100f,
            Bounty = bounty, HasSecondary = offhand, Secondary = 2, Stat1 = stat1, Stat2 = stat2, MarkTier = tier, WishState = wish,
        };

        [Test]
        public void AnEliteGivesItsNameAndItsStar()
        {
            var elite = new OverheadFacts { Kind = OverheadActorKind.Elite, Name = "Gaoler", HpPct = 100f };
            var stack = Stack(elite);
            var l = One(out int b);
            Assert.That(OverheadCluster.AddParts(l, stack, true, WorldPerLocal, 1.0f, NameH), Is.EqualTo(2), "name and star");
            Assert.That(l.PartCount(b), Is.EqualTo(2), "parts in the box");
            Part(l, b, 0, -.5f, -.17f, .5f, .17f, "the name");
            float half = OverheadCluster.EliteStarSize * OverheadCluster.EliteStarPlate * WorldPerLocal / 2f;
            float y = K * 5.5f + WorldPerLocal * (OverheadCluster.EliteStarRise + OverheadCluster.EliteStarSize / 2f);
            Part(l, b, 1, -half, y - half, half, y + half, "the star plate");

            l = One(out b);
            Assert.That(OverheadCluster.AddParts(l, stack, false, WorldPerLocal, 1.0f, NameH), Is.EqualTo(1), "name only without the star");
            Part(l, b, 0, -.5f, -.17f, .5f, .17f, "the name alone");

            var nameless = Stack(new OverheadFacts { Kind = OverheadActorKind.Elite, Name = null, HpPct = 100f });
            l = One(out b);
            Assert.That(OverheadCluster.AddParts(l, nameless, true, WorldPerLocal, 1.0f, NameH), Is.EqualTo(0), "no name, no parts, no star");
            Assert.That(l.PartCount(b), Is.EqualTo(0), "no parts in the box");
        }

        [Test]
        public void AFighterGivesEightPartsInStackOrder()
        {
            var stack = Stack(Fighter(40, true, 1, 2, 2, 1));
            var l = One(out int b);
            Assert.That(OverheadCluster.AddParts(l, stack, false, WorldPerLocal, 1.248f, NameH), Is.EqualTo(8), "name, wish, pips, crown, number, off-hand, two stats");
            Assert.That(l.PartCount(b), Is.EqualTo(8), "parts in the box");
            Part(l, b, 0, -.624f, -.17f, .624f, .17f, "name");
            Part(l, b, 1, -1.1856f, -.2028f, -.7176f, .2652f, "wish");
            Part(l, b, 2, -.4992f, .1092f, .4992f, .2028f, "pips");
            l.PartRect(b, 3, false, out float x0, out float y0, out float x1, out float y1);
            Assert.That(x1 - x0, Is.EqualTo(.7001f).Within(Tol), "crown width");
            Assert.That(y1 - y0, Is.EqualTo(.7001f).Within(Tol), "crown is a square");
            Assert.That((x0 + x1) / 2f, Is.EqualTo(0f).Within(Tol), "crown centre x");
            Assert.That((y0 + y1) / 2f, Is.EqualTo(.5262f).Within(Tol), "crown centre y");
            Part(l, b, 4, -.312f, .7873f, .312f, 1.1305f, "bounty number");
            Part(l, b, 5, -.8724f, .8821f, -.2508f, 1.5037f, "off-hand");
            Part(l, b, 6, -.3108f, .8821f, .3108f, 1.5037f, "first stat");
            Part(l, b, 7, .2508f, .8821f, .8724f, 1.5037f, "second stat");

            stack = Stack(Fighter(0, true, 1, 2, 2, 1));
            l = One(out b);
            Assert.That(OverheadCluster.AddParts(l, stack, false, WorldPerLocal, 1.248f, NameH), Is.EqualTo(6), "no bounty: no crown, no number");
            Part(l, b, 4, -.3108f, .36f, .3108f, .9816f, "first stat without a bounty");
        }

        [Test]
        public void TheLargestLiftCoversTheTallestCluster()
        {
            var stack = Stack(Fighter(150, true, 1, 2, 4, 1));
            var l = One(out int b);
            Assert.That(OverheadCluster.AddParts(l, stack, false, WorldPerLocal, 1.248f, NameH), Is.EqualTo(8), "the fullest fighter");
            float low = float.MaxValue, high = float.MinValue;
            for (int p = 0; p < l.PartCount(b); p++)
            {
                l.PartRect(b, p, false, out _, out float y0, out _, out float y1);
                low = Math.Min(low, y0); high = Math.Max(high, y1);
            }
            Assert.That(high - low + OverheadBoxLayout.Gap, Is.LessThanOrEqualTo(OverheadBoxLayout.MaxLift), "extent plus Gap within MaxLift");
            Assert.That(high - low, Is.GreaterThan(1f), "the extent was measured from real parts");
        }
    }
}
