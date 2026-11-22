using System.Linq;
using NUnit.Framework;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class PlagueRootBankEditModeTests
    {
        [Test]
        public void AdjacentMarshRootFeetCarryDifferentRigidSilhouettes()
        {
            var props = new[]
            {
                new MapProp(27, 4106, 3734, 2, 0), new MapProp(27, 4113, 3691, 3, 0),
                new MapProp(27, 4115, 3758, 0, 0), new MapProp(27, 4140, 3696, 0, 0),
                new MapProp(27, 4176, 3755, 1, 0), new MapProp(22, 4208, 3755, 1, 0)
            };
            var map = new MapBlob(160, 160, new byte[160 * 160]).WithPublic(null, null, null, props, null);
            var root = new GameObject("Plague bank with delivered public feet");
            try
            {
                using (RegionProps.Build(root.transform, map))
                {
                    var sprites = root.GetComponentsInChildren<SpriteRenderer>();
                    Assert.That(sprites.Length, Is.EqualTo(props.Length));
                    Assert.That(sprites.Take(5).Select(s => s.sprite).Distinct().Count(), Is.GreaterThanOrEqualTo(3),
                        "Tinted and flipped copies still repeat the same arch");
                    Assert.That(sprites[5].sprite.name, Is.EqualTo("plague.rubble"), "Ordinary rubble retains its artwork");
                    for (int i = 0; i < props.Length; i++)
                    {
                        Assert.That(sprites[i].transform.localPosition, Is.EqualTo(ArenaView.WorldPoint(props[i].X, props[i].Y)));
                        Assert.That(RegionPropArt.Sways(sprites[i].sprite.name), Is.False);
                        Assert.That(sprites[i].GetComponent<Collider2D>(), Is.Null);
                    }
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
