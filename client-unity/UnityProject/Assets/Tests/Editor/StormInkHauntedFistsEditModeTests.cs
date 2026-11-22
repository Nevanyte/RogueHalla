using System.Linq;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkHauntedFistsEditModeTests
    {
        GameObject _root;
        SpriteActorPresenter _presenter;
        WeaponId _weapon = WeaponId.Fists;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Haunted fist poses");
            _presenter = new SpriteActorPresenter(_root.transform, Resources.Load<ShowcaseCatalog>("Showcase/Catalog"));
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        SpriteRenderer Body() => _root.GetComponentsInChildren<SpriteRenderer>().Single(r => r.name == "Authored character");

        void Present(float aim)
        {
            _presenter.Begin();
            _presenter.Present(1, 100, 100, aim, 100, FactionId.Sentenced, ActorStatus.Alive, _weapon, ActorKind.Player, null, 0, 0, true);
            _presenter.End();
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        public void DeliveredSwingUsesIndependentKickForEachHeading(int row)
        {
            float aim = (2 - row) * Mathf.PI / 4f;
            Present(0);
            _presenter.ConfirmedCue(1, FxKind.Windup, WeaponId.Fists, aim, 0);
            _presenter.ConfirmedCue(1, FxKind.Swing, WeaponId.Fists, aim);
            Present(0);
            SpriteRenderer body = Body();
            Assert.That(body.sprite.name, Is.EqualTo($"SentencedFists/{row}/2"));
            Assert.That(body.transform.localScale.x, Is.Positive, "Each facing comes from its own painted row.");
        }

        [Test]
        public void LocalInputCanFeintButOnlyDeliveredSwingStartsTheKick()
        {
            Present(0);
            _presenter.LocalAttackCue(1);
            Present(0);
            Assert.That(Body().sprite.name, Is.EqualTo("SentencedFists/2/0"));
            _presenter.ConfirmedCue(1, FxKind.Windup, WeaponId.Fists, 0, 0);
            Present(0);
            Assert.That(Body().sprite.name, Is.EqualTo("SentencedFists/2/0"), "Zero-duration windup must leave time of contact to the swing.");
            _presenter.ConfirmedCue(1, FxKind.Swing, WeaponId.Sword, 0);
            Present(0);
            Assert.That(Body().sprite.name, Is.EqualTo("SentencedFists/2/0"), "A mismatched delivery must leave the local feint untouched.");
            _presenter.ConfirmedCue(1, FxKind.Cancel);
            Present(0);
            Assert.That(Body().sprite.name, Does.StartWith("SentencedChampion/"));
            _presenter.ConfirmedCue(1, FxKind.Swing, WeaponId.Fists, 0);
            Present(0);
            Assert.That(Body().sprite.name, Is.EqualTo("SentencedFists/2/2"));
            _weapon = WeaponId.Sword;
            Present(0);
            Assert.That(Body().sprite.name, Does.StartWith("SentencedChampion/"));
        }
    }
}
