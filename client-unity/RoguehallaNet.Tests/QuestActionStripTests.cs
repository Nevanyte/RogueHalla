using NUnit.Framework;
using Roguehalla.Net.Ui;

namespace Roguehalla.Net.Tests
{
    /// <summary>P-UI-JOURNAL-DETAILS-1 oracle: the sizes of the journal's strip of the selected quest's own actions.</summary>
    public sealed class QuestActionStripTests
    {
        [Test]
        public void HeightsFollowTheCountAndStopAtTwoRows()
        {
            int[] counts = { 1, 2, 3, 60 };
            float[] height = { 44f, 93f, 93f, 93f };
            float[] content = { 44f, 93f, 142f, 2935f };
            float[] bottom = { 50f, 99f, 99f, 99f };
            for (int i = 0; i < counts.Length; i++)
            {
                Assert.That(QuestActionStrip.Height(counts[i]), Is.EqualTo(height[i]).Within(.001f), "Height " + counts[i]);
                Assert.That(QuestActionStrip.ContentHeight(counts[i]), Is.EqualTo(content[i]).Within(.001f), "ContentHeight " + counts[i]);
                Assert.That(QuestActionStrip.ScrollBottom(counts[i]), Is.EqualTo(bottom[i]).Within(.001f), "ScrollBottom " + counts[i]);
            }
            foreach (int none in new[] { 0, -1 })
            {
                Assert.That(QuestActionStrip.Height(none), Is.EqualTo(0f), "Height " + none);
                Assert.That(QuestActionStrip.ContentHeight(none), Is.EqualTo(0f), "ContentHeight " + none);
                Assert.That(QuestActionStrip.ScrollBottom(none), Is.EqualTo(0f), "ScrollBottom " + none);
            }
        }
    }
}
