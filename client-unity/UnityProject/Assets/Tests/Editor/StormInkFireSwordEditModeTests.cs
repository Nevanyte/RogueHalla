using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkFireSwordEditModeTests
    {
        GameObject _root;
        SpriteActorPresenter _presenter;
        Texture2D _atlas;
        StormInkFireSwordAuthoring _authoring;
        FieldInfo _defaultField;

        [SetUp]
        public void SetUp()
        {
            _atlas = new Texture2D(1280, 1024, TextureFormat.RGBA32, false);
            _authoring = ScriptableObject.CreateInstance<StormInkFireSwordAuthoring>();
            SetField(_authoring, "atlas", _atlas);
            SetField(_authoring, "palms", MakePalms());
            SetField(_authoring, "poses", MakePoses());
            _defaultField = typeof(StormInkFireSwordAuthoring).GetField("_default", BindingFlags.Static | BindingFlags.NonPublic);
            _defaultField.SetValue(null, _authoring);
            _root = new GameObject("Fire sword poses");
            _presenter = new SpriteActorPresenter(_root.transform, Resources.Load<ShowcaseCatalog>("Showcase/Catalog"));
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_root);
            var frames = (Sprite[])typeof(StormInkFireSwordAuthoring)
                .GetField("_frames", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_authoring);
            if (frames != null) foreach (var frame in frames) UnityEngine.Object.DestroyImmediate(frame);
            _defaultField.SetValue(null, null);
            UnityEngine.Object.DestroyImmediate(_authoring);
            UnityEngine.Object.DestroyImmediate(_atlas);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        public void BothServerTimedSwordReleasesKeepTheSharedSwordAndAnatomicalSockets(int row)
        {
            float aim = (2 - row) * Mathf.PI / 4f;
            Present();

            _presenter.ConfirmedCue(1, FxKind.Windup, WeaponId.Sword, aim, 200f);
            Present();
            AssertBody(row, 0);
            SetWindupProgress(.95f, .2f);
            Present();
            AssertBody(row, 1);
            AssertSharedWeaponAndHands(row);

            _presenter.ConfirmedCue(1, FxKind.Swing, WeaponId.Sword, aim);
            Present();
            AssertBody(row, 2);
            AssertSharedWeaponAndHands(row);

            _presenter.ConfirmedCue(1, FxKind.Windup, WeaponId.Sword, aim, 200f);
            Present();
            AssertBody(row, 5);
            SetWindupProgress(.95f, .2f);
            Present();
            AssertBody(row, 6);
            AssertSharedWeaponAndHands(row);

            _presenter.ConfirmedCue(1, FxKind.Swing, WeaponId.Sword, aim);
            Present();
            AssertBody(row, 7);
            AssertSharedWeaponAndHands(row);
        }

        [Test]
        public void FivePhasesOfEachSwordReleaseMapToTheirAuthoredColumns()
        {
            foreach (int row in Enumerable.Range(0, 8))
            {
                Assert.That(_authoring.Frame(row, StormInkFireSwordMotionPhase.Windup, .25f, false).name,
                    Is.EqualTo($"FireSword/{row}/0"));
                Assert.That(_authoring.Frame(row, StormInkFireSwordMotionPhase.Windup, .95f, false).name,
                    Is.EqualTo($"FireSword/{row}/1"));
                Assert.That(_authoring.Frame(row, StormInkFireSwordMotionPhase.Strike, .5f, false).name,
                    Is.EqualTo($"FireSword/{row}/2"));
                Assert.That(_authoring.Frame(row, StormInkFireSwordMotionPhase.Follow, .25f, false).name,
                    Is.EqualTo($"FireSword/{row}/3"));
                Assert.That(_authoring.Frame(row, StormInkFireSwordMotionPhase.Follow, .75f, false).name,
                    Is.EqualTo($"FireSword/{row}/4"));
                Assert.That(_authoring.Frame(row, StormInkFireSwordMotionPhase.Windup, .25f, true).name,
                    Is.EqualTo($"FireSword/{row}/5"));
                Assert.That(_authoring.Frame(row, StormInkFireSwordMotionPhase.Windup, .95f, true).name,
                    Is.EqualTo($"FireSword/{row}/6"));
                Assert.That(_authoring.Frame(row, StormInkFireSwordMotionPhase.Strike, .5f, true).name,
                    Is.EqualTo($"FireSword/{row}/7"));
                Assert.That(_authoring.Frame(row, StormInkFireSwordMotionPhase.Follow, .25f, true).name,
                    Is.EqualTo($"FireSword/{row}/8"));
                Assert.That(_authoring.Frame(row, StormInkFireSwordMotionPhase.Follow, .75f, true).name,
                    Is.EqualTo($"FireSword/{row}/9"));
            }
        }

        [Test]
        public void AngleAndDrawDepthCanBeAdjustedPerFacingAndAttackPhase()
        {
            var poses = (Array)typeof(StormInkFireSwordAuthoring)
                .GetField("poses", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_authoring);
            object eastReleaseB = poses.GetValue(27);
            var poseType = eastReleaseB.GetType();
            poseType.GetField("WeaponAngleOffset").SetValue(eastReleaseB, 31f);
            poseType.GetField("WeaponDepthOffset").SetValue(eastReleaseB, -3);
            poses.SetValue(eastReleaseB, 27);
            SetField(_authoring, "poses", poses);

            Assert.That(_authoring.WeaponAngleOffset(2, 7), Is.EqualTo(31f));
            Assert.That(_authoring.WeaponDepthOffset(2, 7), Is.EqualTo(-3));
            Assert.That(_authoring.WeaponAngleOffset(2, 2), Is.Zero);
            Assert.That(_authoring.WeaponDepthOffset(2, 2), Is.Zero);
        }

        [Test]
        public void ReleaseTimingEditsAffectOnlyTheirOwnWindupAndRecoveryIntervals()
        {
            var timingType = typeof(StormInkFireSwordAuthoring).GetNestedType("ReleaseTiming", BindingFlags.NonPublic);
            object releaseA = Activator.CreateInstance(timingType);
            timingType.GetField("PassingAt").SetValue(releaseA, .3f);
            timingType.GetField("RecoveryAt").SetValue(releaseA, .7f);
            SetField(_authoring, "releaseATiming", releaseA);

            Assert.That(_authoring.PoseColumn(StormInkFireSwordMotionPhase.Windup, .4f, false), Is.EqualTo(1));
            Assert.That(_authoring.PoseColumn(StormInkFireSwordMotionPhase.Windup, .4f, true), Is.EqualTo(5));
            Assert.That(_authoring.PoseColumn(StormInkFireSwordMotionPhase.Follow, .6f, false), Is.EqualTo(3));
            Assert.That(_authoring.PoseColumn(StormInkFireSwordMotionPhase.Follow, .6f, true), Is.EqualTo(8));
            Assert.That(_authoring.PoseColumn(StormInkFireSwordMotionPhase.Windup, .95f, false), Is.EqualTo(1));
            Assert.That(_authoring.PoseColumn(StormInkFireSwordMotionPhase.Strike, 0f, false), Is.EqualTo(2));
        }

        [Test]
        public void BladeAngleFollowsAuthoredKeysAndReducedMotionKeepsTheExactReleaseAngle()
        {
            SetReleaseTiming(.5f, .5f);
            SetPoseAngle(2, 0, 0f);
            SetPoseAngle(2, 1, 20f);
            SetPoseAngle(2, 2, 40f);
            SetPoseAngle(2, 3, 60f);
            SetPoseAngle(2, 4, 0f);

            Assert.That(_authoring.BladeAngleOffset(2, StormInkFireSwordMotionPhase.Windup, .25f, false, false), Is.EqualTo(10f));
            Assert.That(_authoring.BladeAngleOffset(2, StormInkFireSwordMotionPhase.Strike, .5f, false, false), Is.EqualTo(40f));
            Assert.That(_authoring.BladeAngleOffset(2, StormInkFireSwordMotionPhase.Follow, .25f, false, false), Is.EqualTo(50f));
            Assert.That(_authoring.BladeAngleOffset(2, StormInkFireSwordMotionPhase.Follow, .75f, false, false), Is.EqualTo(30f));
            Assert.That(_authoring.BladeAngleOffset(2, StormInkFireSwordMotionPhase.Windup, .5f, false, true), Is.Zero);
            Assert.That(_authoring.BladeAngleOffset(2, StormInkFireSwordMotionPhase.Strike, 1f, false, true), Is.EqualTo(40f));
        }

        [Test]
        public void LocalSwordAnticipationCanReachPassingBeforeTheUnchangedSwingCue()
        {
            Present();
            _presenter.LocalAttackCue(1);
            SetWindupProgress(.3f, .16f);
            Present();
            AssertBody(2, 0);

            SetWindupProgress(.95f, .16f);
            Present();
            AssertBody(2, 1);

            _presenter.ConfirmedCue(1, FxKind.Swing, WeaponId.Sword, 0f);
            Present();
            AssertBody(2, 2);
            AssertSharedWeaponAndHands(2);
        }

        [Test]
        public void ProductionSwordAuthoringContainsTheCompletePaintedSheetAndHandMap()
        {
            var production = Resources.Load<StormInkFireSwordAuthoring>("StormInk/Actors/FireSwordAuthoring");
            Assert.That(production, Is.Not.Null);
            for (int row = 0; row < 8; row++)
            for (int column = 0; column < 10; column++)
            {
                var frame = production.Frame(row, column);
                Assert.That(frame, Is.Not.Null, $"Missing Fire sword drawing at facing {row}, pose {column}.");
                Assert.That(production.TryHand(frame, false, out _), Is.True,
                    $"Missing right hand registration at facing {row}, pose {column}.");
                Assert.That(production.TryHand(frame, true, out _), Is.True,
                    $"Missing left hand registration at facing {row}, pose {column}.");
            }
            Assert.That(production.WorldHeight, Is.EqualTo(StormInkFireAxeAuthoring.Load().WorldHeight));
            Assert.That(production.FootPivot, Is.EqualTo(StormInkFireAxeAuthoring.Load().FootPivot));
        }

        [Test]
        public void CancelAndWeaponSwitchReturnToSharedGuardBeforeAnotherSwordSwing()
        {
            Present();
            _presenter.ConfirmedCue(1, FxKind.Windup, WeaponId.Sword, 0f, 200f);
            Present();
            AssertBody(2, 0);

            _presenter.ConfirmedCue(1, FxKind.Cancel);
            Present();
            Assert.That(ActorSprite("Authored character").sprite.name, Does.StartWith("FireAxe/"));

            _presenter.ConfirmedCue(1, FxKind.Swing, WeaponId.Sword, 0f);
            Present();
            AssertBody(2, 2);

            Present(WeaponId.Axe);
            Assert.That(ActorSprite("Authored character").sprite.name, Does.StartWith("FireAxe/"));
            _presenter.ConfirmedCue(1, FxKind.Swing, WeaponId.Sword, 0f);
            Present(WeaponId.Axe);
            Assert.That(ActorSprite("Authored character").sprite.name, Does.StartWith("FireAxe/"));

            Present(WeaponId.Sword);
            Assert.That(ActorSprite("Authored character").sprite.name, Does.StartWith("FireAxe/"));
            _presenter.ConfirmedCue(1, FxKind.Swing, WeaponId.Sword, 0f);
            Present(WeaponId.Sword);
            AssertBody(2, 7);
            AssertSharedWeaponAndHands(2);
        }

        void Present(WeaponId weapon = WeaponId.Sword)
        {
            _presenter.Begin();
            _presenter.Present(1, 100, 100, 0f, 100, FactionId.Fire, ActorStatus.Alive,
                weapon, ActorKind.Player, null, 0, 0, true);
            _presenter.End();
        }

        void SetWindupProgress(float progress, float duration)
        {
            var active = (System.Collections.IDictionary)typeof(SpriteActorPresenter)
                .GetField("_active", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_presenter);
            var actor = active[1u];
            float now = Time.unscaledTime;
            actor.GetType().GetField("WindupAt").SetValue(actor, now - progress * duration);
            actor.GetType().GetField("WindupUntil").SetValue(actor, now - progress * duration + duration);
        }

        void AssertBody(int row, int column) =>
            Assert.That(ActorSprite("Authored character").sprite.name, Is.EqualTo($"FireSword/{row}/{column}"));

        void AssertSharedWeaponAndHands(int row)
        {
            var body = ActorSprite("Authored character");
            var weapon = ActorSprite("Equipped weapon cue");
            Assert.That(body.transform.localScale.x, Is.Positive);
            Assert.That(weapon.sprite, Is.SameAs(StormInkWeaponArt.Get(WeaponId.Sword, row)));
            Assert.That(StormInkActors.TryHand(FactionId.Fire, body.sprite, false, out var right), Is.True);
            Assert.That(StormInkActors.TryHand(FactionId.Fire, body.sprite, true, out var left), Is.True);
            Assert.That(Vector2.Distance(WorldSocket(body, right), WorldSocket(weapon, StormInkWeaponArt.Grip(WeaponId.Sword, row))),
                Is.LessThan(.001f));
            Assert.That(Vector2.Distance(WorldSocket(body, left), WorldSocket(body, right)), Is.GreaterThan(.10f));
        }

        SpriteRenderer ActorSprite(string name) => _root.GetComponentsInChildren<SpriteRenderer>().Single(r => r.name == name);

        static Vector3 WorldSocket(SpriteRenderer renderer, Vector2 uv)
        {
            var sprite = renderer.sprite;
            return renderer.transform.TransformPoint((Vector2.Scale(uv, sprite.rect.size) - sprite.pivot) / sprite.pixelsPerUnit);
        }

        static Array MakePalms()
        {
            var palmType = typeof(StormInkFireSwordAuthoring).GetNestedType("Palms", BindingFlags.NonPublic);
            var result = Array.CreateInstance(palmType, 80);
            var right = palmType.GetField("Right");
            var left = palmType.GetField("Left");
            for (int index = 0; index < result.Length; index++)
            {
                int column = index % 10;
                int row = index / 10;
                object entry = Activator.CreateInstance(palmType);
                right.SetValue(entry, new Vector2Int(20 + column, 48 + row));
                left.SetValue(entry, new Vector2Int(104 - column, 48 + row));
                result.SetValue(entry, index);
            }
            return result;
        }

        static Array MakePoses()
        {
            var poseType = typeof(StormInkFireSwordAuthoring).GetNestedType("PosePresentation", BindingFlags.NonPublic);
            var result = Array.CreateInstance(poseType, 80);
            var rowField = poseType.GetField("Row");
            var columnField = poseType.GetField("Column");
            for (int index = 0; index < result.Length; index++)
            {
                object entry = Activator.CreateInstance(poseType);
                rowField.SetValue(entry, index / 10);
                columnField.SetValue(entry, index % 10);
                result.SetValue(entry, index);
            }
            return result;
        }

        void SetPoseAngle(int row, int column, float value)
        {
            var poses = (Array)typeof(StormInkFireSwordAuthoring)
                .GetField("poses", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_authoring);
            int index = row * 10 + column;
            object entry = poses.GetValue(index);
            entry.GetType().GetField("WeaponAngleOffset").SetValue(entry, value);
            poses.SetValue(entry, index);
            SetField(_authoring, "poses", poses);
        }

        void SetReleaseTiming(float passingAt, float recoveryAt)
        {
            var timingType = typeof(StormInkFireSwordAuthoring).GetNestedType("ReleaseTiming", BindingFlags.NonPublic);
            object timing = Activator.CreateInstance(timingType);
            timingType.GetField("PassingAt").SetValue(timing, passingAt);
            timingType.GetField("RecoveryAt").SetValue(timing, recoveryAt);
            SetField(_authoring, "releaseATiming", timing);
        }

        static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
