using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkHitMarkEditModeTests
    {
        [Test]
        public void DeliveredSignaturesHaveDifferentDarkAndPigmentedSilhouettes()
        {
            var type = typeof(ArenaView).Assembly.GetType("Roguehalla.Client.StormInkHitMarks");
            Assert.NotNull(type, "Regional damage has only numbers in the baseline.");
            var get = type.GetMethod("ForSignature", BindingFlags.Static | BindingFlags.Public);
            Assert.IsNotNull(get);
            var unique = new HashSet<Sprite>();
            foreach (byte code in new byte[] { 1, 2, 3, 4 })
            {
                var mark = (Sprite)get.Invoke(null, new object[]{code});
                Assert.IsNotNull(mark);
                Assert.IsTrue(unique.Add(mark));
                Assert.AreSame(mark, get.Invoke(null, new object[]{code}), "Reuse the authored mark.");
            }
            Assert.IsNull(get.Invoke(null, new object[]{(byte)0}));
            Assert.IsNull(get.Invoke(null, new object[]{(byte)255}));
        }

        [Test]
        public void BacklashSitsClearOfTheBootAndRingAndGetsAReadableScale()
        {
            var type = typeof(ArenaView).Assembly.GetType("Roguehalla.Client.StormInkHitMarks");
            Assert.NotNull(type);
            var anchor = type.GetMethod("Anchor", BindingFlags.Static | BindingFlags.Public);
            Assert.IsNotNull(anchor);
            var backlash = (Vector2)anchor.Invoke(null, new object[] { (byte)3, false });
            Assert.Greater(backlash.magnitude, 1.1f, "Backlash must clear the foot and its targeting ring.");
            Assert.Greater(backlash.y, .5f, "Keep the reverse burst out of the lower boot silhouette.");

            var scale = type.GetMethod("Scale", BindingFlags.Static | BindingFlags.Public);
            Assert.IsNotNull(scale, "Signature marks need a per-mark presentation scale.");
            var backlashScale = (float)scale.Invoke(null, new object[] { (byte)3, false });
            var ordinaryScale = (float)scale.Invoke(null, new object[] { (byte)1, false });
            Assert.Greater(backlashScale, ordinaryScale * 1.25f,
                "Enlarge only Backlash while preserving the other signature marks.");
            Assert.AreEqual(new Vector2(-.46f, .90f), anchor.Invoke(null, new object[] { (byte)1, false }));
            Assert.AreEqual(new Vector2(-.43f, .20f), anchor.Invoke(null, new object[] { (byte)2, false }));
            Assert.AreEqual(new Vector2(.43f, .84f), anchor.Invoke(null, new object[] { (byte)4, false }));
            Assert.AreEqual(ordinaryScale, (float)scale.Invoke(null, new object[] { (byte)2, false }));
            Assert.AreEqual(ordinaryScale, (float)scale.Invoke(null, new object[] { (byte)4, false }));
            Assert.AreEqual(ordinaryScale, (float)scale.Invoke(null, new object[] { (byte)3, true }));
        }
    }
}
