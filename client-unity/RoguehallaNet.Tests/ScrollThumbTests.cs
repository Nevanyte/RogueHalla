using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>P-UI-JOURNAL-DETAILS-1 oracle: the place and length of a scroll position handle on its track.</summary>
    public sealed class ScrollThumbTests
    {
        const float Tol = .0005f;

        static void Hidden(float viewport, float content, string what)
        {
            bool shown = ScrollThumb.Compute(viewport, content, 1f, out float top, out float bottom);
            Assert.That(shown, Is.False, what);
            Assert.That(top, Is.EqualTo(0f), what + " top");
            Assert.That(bottom, Is.EqualTo(0f), what + " bottom");
        }

        static void Shown(float viewport, float content, float p, float top, float bottom, string what)
        {
            bool shown = ScrollThumb.Compute(viewport, content, p, out float t, out float b);
            Assert.That(shown, Is.True, what);
            Assert.That(t, Is.EqualTo(top).Within(Tol), what + " top");
            Assert.That(b, Is.EqualTo(bottom).Within(Tol), what + " bottom");
        }

        [Test]
        public void TheThumbIsShownOnlyWhileTheContentIsTaller()
        {
            Shown(390f, 391f, 1f, 0f, .9974f, "390 / 391");
            Hidden(390f, 390.4f, "390 / 390.4 (inside the slack)");
            Hidden(390f, 389f, "390 / 389");
            Hidden(390f, 390f, "390 / 390");
            Hidden(0f, 100f, "no viewport");
            Hidden(float.NaN, 1000f, "NaN viewport");
            Hidden(390f, float.NaN, "NaN content");
        }

        [Test]
        public void TheLengthIsViewportOverContentAndThePlaceFollowsThePosition()
        {
            Shown(390f, 1170f, 1f, 0f, .3333f, "top");
            Shown(390f, 1170f, 0f, .6667f, 1f, "bottom");
            Shown(390f, 1170f, .5f, .3333f, .6667f, "middle");
            Shown(390f, 1170f, 2f, 0f, .3333f, "p above 1 counts as 1");
            Shown(390f, 1170f, -1f, .6667f, 1f, "p below 0 counts as 0");
            Shown(390f, 1170f, float.NaN, 0f, .3333f, "NaN position counts as the top");
            ScrollThumb.Compute(100f, 10000f, 1f, out float top, out float bottom);
            Assert.That(bottom - top, Is.EqualTo(.06f).Within(Tol), "a long content keeps the minimum length");
        }
    }
}
