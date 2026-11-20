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
    }
}
