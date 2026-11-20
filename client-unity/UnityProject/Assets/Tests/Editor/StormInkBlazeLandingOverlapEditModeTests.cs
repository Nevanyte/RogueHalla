using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>Guards the foreground landing layer drawn over the area presenter during a real Blaze.</summary>
    public sealed class StormInkBlazeLandingOverlapEditModeTests
    {
        [Test]
        public void TheTwoDeliveredEndpointsAndTheirConnectingStrokeUseFirePigment()
        {
            var root = new GameObject("Blaze overlay test");
            try
            {
                var p = new ConfirmedWishPresenter(root.transform, null);
                p.Accept(new WireFx { Kind = FxKind.WishLanded, Ability = (byte)AbilityId.Blaze,
                    EventTime = 500, HasAnchor = true, HasDestination = true,
                    AnchorX = 100, AnchorY = 100, DestinationX = 196, DestinationY = 120 });
                p.Tick(510);
                var art = root.transform.GetChild(0);
                string[] strokes = { WishFxArt.FireFleck, WishFxArt.FireWash, WishFxArt.FireTrail };
                for (int i = 0; i < strokes.Length; i++)
                {
                    var part = art.Find("Confirmed Wish shape " + i).GetComponent<SpriteRenderer>();
                    Assert.That(part.enabled, Is.True);
                    Assert.That(part.sprite, Is.SameAs(WishFxArt.Get(strokes[i])), "visible layer " + i);
                }
                p.Tick(1001);
                Assert.That(p.ActiveCount, Is.Zero);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
