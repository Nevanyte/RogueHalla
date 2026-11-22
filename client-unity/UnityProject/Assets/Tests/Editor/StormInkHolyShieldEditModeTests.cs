using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkHolyShieldEditModeTests
    {
        GameObject _root;
        SpriteActorPresenter _presenter;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Holy shield poses");
            _presenter = new SpriteActorPresenter(_root.transform, Resources.Load<ShowcaseCatalog>("Showcase/Catalog"));
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        SpriteRenderer ActorSprite(string name) => _root.GetComponentsInChildren<SpriteRenderer>().Single(r => r.name == name);

        void Present(float facing, WeaponId weapon = WeaponId.Shield, FactionId faction = FactionId.Holy)
        {
            _presenter.Begin();
            _presenter.Present(1, 100, 100, facing, 100, faction, ActorStatus.Alive, weapon, ActorKind.Player, null, 0, 0, true);
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
        public void DeliveredShieldSwingUsesTheMeasuredPaladinPressAndCommonLeftHandGrip(int row)
        {
            float aim = (2 - row) * Mathf.PI / 4f;
            Present(0f);
            _presenter.ConfirmedCue(1, FxKind.Swing, WeaponId.Shield, aim);
            Present(0f);
            AssertPoseAndGrip(row, 1);
        }

        [Test]
        public void ShieldBraceCoverAndReleaseUseExistingCombatClocks()
        {
            Present(0f);
            _presenter.ConfirmedCue(1, FxKind.Windup, WeaponId.Shield, 0f, 200f);
            Present(0f);
            AssertPoseAndGrip(2, 0);

            _presenter.ConfirmedCue(1, FxKind.Swing, WeaponId.Shield, 0f);
            Present(0f);
            AssertPoseAndGrip(2, 1);

            var actors = (IDictionary)typeof(SpriteActorPresenter).GetField("_active", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(_presenter);
            var art = actors[1u];
            art.GetType().GetField("AttackAt").SetValue(art, Time.unscaledTime - .2f);
            Present(0f);
            AssertPoseAndGrip(2, 2);

            _presenter.ConfirmedCue(1, FxKind.Block, WeaponId.Shield, 0f);
            Present(0f);
            AssertPoseAndGrip(2, 3);

            _presenter.ConfirmedCue(1, FxKind.Cancel);
            Present(0f, WeaponId.Sword);
            Assert.That(ActorSprite("Authored character").sprite.name, Does.StartWith("HolyChampion/"));
            Present(0f, WeaponId.Shield, FactionId.Frost);
            Assert.That(ActorSprite("Authored character").sprite.name, Does.StartWith("FrostChampion/"));
        }

        void AssertPoseAndGrip(int row, int column)
        {
            var body = ActorSprite("Authored character");
            Assert.That(body.sprite.name, Is.EqualTo($"HolyShield/{row}/{column}"));
            Assert.That(body.transform.localScale.x, Is.Positive);
            Assert.That(StormInkActors.TryHand(FactionId.Holy, body.sprite, true, out var palm), Is.True);
            var held = ActorSprite("Equipped weapon cue");
            Assert.That(held.sprite, Is.SameAs(StormInkWeaponArt.Get(WeaponId.Shield, row)));
            Vector2 socket = StormInkWeaponArt.Grip(WeaponId.Shield, row);
            Assert.That(Vector2.Distance(WorldSocket(body, palm), WorldSocket(held, socket)), Is.LessThan(.001f));
        }

        static Vector3 WorldSocket(SpriteRenderer renderer, Vector2 uv)
        {
            var sprite = renderer.sprite;
            return renderer.transform.TransformPoint((Vector2.Scale(uv, sprite.rect.size) - sprite.pivot) / sprite.pixelsPerUnit);
        }
    }
}
