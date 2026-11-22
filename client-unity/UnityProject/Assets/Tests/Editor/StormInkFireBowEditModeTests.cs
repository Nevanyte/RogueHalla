using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkFireBowEditModeTests
    {
        GameObject _root;
        SpriteActorPresenter _presenter;
        StormInkFireBowAuthoring _previousAuthoring;
        StormInkFireBowAuthoring _fixture;
        FieldInfo _defaultField;

        [SetUp]
        public void SetUp()
        {
            _defaultField = typeof(StormInkFireBowAuthoring).GetField("_default", BindingFlags.Static | BindingFlags.NonPublic);
            _previousAuthoring = StormInkFireBowAuthoring.Load();
            _defaultField.SetValue(null, null);
            _root = new GameObject("Fire bow body route");
            _presenter = new SpriteActorPresenter(_root.transform, Resources.Load<ShowcaseCatalog>("Showcase/Catalog"));
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_root);
            _defaultField.SetValue(null, _previousAuthoring);
            UnityEngine.Object.DestroyImmediate(_fixture);
        }

        [Test]
        public void EastBowDrawUsesAuthoredTimingAndAlternatesOnlyOnObservedArrowThenCancelsCleanly()
        {
            var source = Resources.Load<StormInkFireBowAuthoring>("StormInk/Actors/FireBowAuthoring");
            var rig = StormInkFireAxeAuthoring.Load();
            Assert.That(source, Is.Not.Null);
            Assert.That(rig, Is.Not.Null);
            var art = _fixture = UnityEngine.Object.Instantiate(source);
            // Exercise an unfinished bearing and known timing on a private copy of the artist's asset.
            var settings = new SerializedObject(art);
            settings.FindProperty("readyRows").GetArrayElementAtIndex(0).boolValue = false;
            settings.FindProperty("midDrawAt").floatValue = .22f;
            settings.FindProperty("sustainedDrawAt").floatValue = .68f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            _defaultField.SetValue(null, art);
            Assert.That(art.Frame(0, 0), Is.Null, "Unpainted bearings must keep the current Fire art.");
            AssertColumn(1, .1f, false, 0);
            AssertColumn(1, .3f, false, 1);
            AssertColumn(1, .8f, false, 2);
            AssertColumn(2, 0f, false, 3);
            AssertColumn(3, 1f, false, 4);
            AssertColumn(1, .1f, true, 5);
            AssertColumn(3, 1f, true, 9);
            for (int column = 0; column < 10; column++)
            {
                var frame = art.Frame(2, column);
                Assert.That(frame, Is.Not.Null, $"Missing east-facing Fire bow pose {column}.");
                Assert.That(art.TryHand(frame, false, out _), Is.True, $"Missing right string-pull hand in pose {column}.");
                Assert.That(art.TryHand(frame, true, out _), Is.True, $"Missing left bow-grip hand in pose {column}.");
            }

            Present(0, 1000);
            Assert.That(Body().sprite, Is.SameAs(rig.Frame(2, StormInkFireAxePose.Guard,
                rig.IdleStage(2, Time.unscaledTime))));
            Assert.That(Body().transform.localScale.y, Is.EqualTo(rig.WorldHeight));
            Assert.That(Body().GetComponent<StormInkArtistSource>().BodyPainting, Is.SameAs(rig));
            Assert.That(Body().GetComponent<StormInkArtistSource>().TryBodyPalm(false, out _), Is.True);
            SetStampTime(Time.unscaledTime - .04f);
            Present(0, 1000, 100.5f);
            var gait = CurrentGait();
            Assert.That(gait.Walking, Is.True);
            int strideStage = Gait.FrameIndex(gait.Phase, rig.StrideStagesFor(2));
            Assert.That(Body().sprite, Is.SameAs(rig.Frame(2, StormInkFireAxePose.Stride, strideStage)));

            Present(0, 1000, 100.5f, Mathf.PI / 2f);
            var unreadyHeight = Body().transform.localScale.y;
            Assert.That(Body().sprite.name, Does.Not.StartWith("FireBow/"));
            Assert.That(Body().sprite.name, Does.Not.StartWith("FireAxe/"));
            Present(0, 1000, 100.5f, Mathf.PI / 2f, WeaponId.Fists);
            Assert.That(Body().transform.localScale.y, Is.EqualTo(unreadyHeight));

            Present(995, 1000);
            AssertBody(0);
            Present(940, 1000);
            AssertBody(1);
            Present(800, 1000);
            AssertBody(2);
            Assert.That(Body().GetComponent<StormInkArtistSource>().BodyPainting, Is.SameAs(art));

            ObserveArrow();
            Present(800, 1000);
            AssertBody(3);
            Present(800, 1000);
            AssertBody(3);

            _presenter.ConfirmedCue(1, FxKind.Cancel);
            Present(0, 1000);
            Assert.That(Body().sprite.name, Does.Not.StartWith("FireBow/"));

            Present(790, 1000);
            ObserveArrow();
            Present(790, 1000);
            AssertBody(8);
        }

        [TestCase(0)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void BothFireReleasesKeepTheSharedBowInTheRegisteredHand(int row)
        {
            var art = StormInkFireBowAuthoring.Load();
            var sharedBow = StormInkWeaponArt.Get(WeaponId.Bow, row);
            float heading = (2 - row) * Mathf.PI / 4f;
            for (int release = 0; release < 2; release++)
            {
                uint started = (uint)(800 + release * 1000);
                double sample = started + 200;
                Present(started, sample, facing: heading);
                CheckPose(release * 5 + 2);
                ObserveArrow(heading);
                Present(started, sample, facing: heading);
                CheckPose(release * 5 + 3);
                _presenter.ConfirmedCue(1, FxKind.Cancel);
                Present(0, sample, facing: heading);
            }

            void CheckPose(int column)
            {
                var body = Body();
                var expected = art.Frame(row, column);
                Assert.That(expected, Is.Not.Null, $"Missing Fire bow drawing {row}/{column}");
                Assert.That(body.sprite, Is.SameAs(expected));
                var bow = body.transform.parent.Find("Equipped weapon cue").GetComponent<SpriteRenderer>();
                Assert.That(bow.sprite, Is.SameAs(sharedBow));
                Assert.That(art.TryHand(expected, true, out var left), Is.True);
                Assert.That(art.TryHand(expected, false, out var right), Is.True);
                Assert.That(left, Is.Not.EqualTo(right), "The grip and draw hand must have separate registrations.");
                Vector3 Socket(SpriteRenderer renderer, Vector2 uv) => renderer.transform.TransformPoint(
                    (Vector2.Scale(uv, renderer.sprite.rect.size) - renderer.sprite.pivot) / renderer.sprite.pixelsPerUnit);
                Assert.That(Vector3.Distance(Socket(body, left), Socket(bow, StormInkWeaponArt.Grip(WeaponId.Bow, row))),
                    Is.LessThan(.0001f), $"Bow grip drifted from the left palm on {row}/{column}");
            }
        }

        [Test]
        public void RegisteredFireBowStringCanPassBehindBodyWithoutChangingBowOrderOrGrip()
        {
            var production = Resources.Load<StormInkFireBowAuthoring>("StormInk/Actors/FireBowAuthoring");
            var art = UnityEngine.Object.Instantiate(production);
            try
            {
                _defaultField.SetValue(null, art);
                var productionData = new SerializedObject(production);
                var productionRows = productionData.FindProperty("stringBehindBodyRows");
                Assert.That(productionRows.arraySize, Is.EqualTo(8));
                Assert.That(productionRows.GetArrayElementAtIndex(6).boolValue, Is.True,
                    "West is the production bearing configured to tuck the pulled string behind Fire's shoulder.");
                Assert.That(productionRows.GetArrayElementAtIndex(2).boolValue, Is.False);

                var previewData = new SerializedObject(art);
                previewData.FindProperty("stringBehindBodyRows").GetArrayElementAtIndex(2).boolValue = true;
                previewData.ApplyModifiedPropertiesWithoutUndo();

                Present(800, 1000);
                var actor = _root.transform.Find("Observed actor");
                var body = actor.Find("Authored character").GetComponent<SpriteRenderer>();
                var bow = actor.Find("Equipped weapon cue").GetComponent<SpriteRenderer>();
                var upper = actor.Find("Bowstring upper").GetComponent<SpriteRenderer>();
                var lower = actor.Find("Bowstring lower").GetComponent<SpriteRenderer>();
                Assert.That(body.sprite.name, Is.EqualTo("FireBow/2/2"));
                Assert.That(upper.enabled && lower.enabled, Is.True);
                Assert.That(upper.sortingOrder, Is.LessThan(body.sortingOrder));
                Assert.That(lower.sortingOrder, Is.LessThan(body.sortingOrder));

                var canonicalBow = StormInkWeaponArt.Get(WeaponId.Bow, 2);
                Assert.That(bow.sprite, Is.SameAs(canonicalBow));
                int bowOrder = bow.sortingOrder;
                Vector3 bowScale = bow.transform.localScale;
                Assert.That(StormInkActors.TryHand(FactionId.Fire, body.sprite, true, out var leftHand), Is.True);
                Vector3 Socket(SpriteRenderer renderer, Vector2 uv) => renderer.transform.TransformPoint(
                    (Vector2.Scale(uv, renderer.sprite.rect.size) - renderer.sprite.pivot) / renderer.sprite.pixelsPerUnit);
                Vector3 grip = Socket(bow, StormInkWeaponArt.Grip(WeaponId.Bow, 2));
                Assert.That(Vector3.Distance(grip, Socket(body, leftHand)), Is.LessThan(.0001f));

                // The old string order remains the default when the artist turns this row off.
                previewData.FindProperty("stringBehindBodyRows").GetArrayElementAtIndex(2).boolValue = false;
                previewData.ApplyModifiedPropertiesWithoutUndo();
                Present(800, 1000);
                Assert.That(bow.sprite, Is.SameAs(canonicalBow));
                Assert.That(bow.sortingOrder, Is.EqualTo(bowOrder));
                Assert.That(bow.transform.localScale, Is.EqualTo(bowScale));
                Assert.That(upper.sortingOrder, Is.EqualTo(bowOrder + 1));
                Assert.That(lower.sortingOrder, Is.EqualTo(bowOrder + 1));
                Assert.That(Vector3.Distance(grip, Socket(body, leftHand)), Is.LessThan(.0001f));

                // A different faction keeps its legacy front string ordering even with this Fire authoring loaded.
                _presenter.Begin();
                _presenter.Present(2, 100, 100, 0, 100, FactionId.Sentenced, ActorStatus.Alive, WeaponId.Bow,
                    ActorKind.Player, null, 0, 0, true, drawStartedAt: 800, sampleTickMs: 1000);
                _presenter.End();
                var other = _root.transform.Find("Observed actor");
                var otherBow = other.Find("Equipped weapon cue").GetComponent<SpriteRenderer>();
                Assert.That(other.Find("Bowstring upper").GetComponent<SpriteRenderer>().sortingOrder,
                    Is.EqualTo(otherBow.sortingOrder + 1));
                Assert.That(other.Find("Bowstring lower").GetComponent<SpriteRenderer>().sortingOrder,
                    Is.EqualTo(otherBow.sortingOrder + 1));
            }
            finally { UnityEngine.Object.DestroyImmediate(art); }
        }

        void Present(uint drawStartedAt, double sampleTickMs, float x = 100f, float facing = 0f,
            WeaponId weapon = WeaponId.Bow)
        {
            _presenter.Begin();
            _presenter.Present(1, x, 100, facing, 100, FactionId.Fire, ActorStatus.Alive, weapon,
                ActorKind.Player, null, 0, 0, true, drawStartedAt: drawStartedAt, sampleTickMs: sampleTickMs);
            _presenter.End();
        }

        GaitState CurrentGait()
        {
            var active = (IDictionary)typeof(SpriteActorPresenter).GetField("_active", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_presenter);
            return (GaitState)active[1u].GetType().GetField("Gait").GetValue(active[1u]);
        }

        void SetStampTime(float time)
        {
            var active = (IDictionary)typeof(SpriteActorPresenter).GetField("_active", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_presenter);
            active[1u].GetType().GetField("StampTime").SetValue(active[1u], time);
        }

        void AssertBody(int column) => Assert.That(Body().sprite.name, Is.EqualTo($"FireBow/2/{column}"));

        void AssertColumn(int phase, float progress, bool alternate, int expected)
        {
            var enumType = typeof(StormInkFireBowAuthoring).Assembly.GetType("Roguehalla.Client.StormInkWeaponMotion+Phase");
            var method = typeof(StormInkFireBowAuthoring).GetMethod("PoseColumn", BindingFlags.NonPublic | BindingFlags.Instance);
            object result = method.Invoke(StormInkFireBowAuthoring.Load(), new[] { Enum.ToObject(enumType, phase), (object)progress, alternate });
            Assert.That((int)result, Is.EqualTo(expected));
        }

        void ObserveArrow(float heading = 0f) => typeof(SpriteActorPresenter).GetMethod("ObservedProjectile", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(_presenter, new object[] { 1u, ProjectileKind.Arrow, 100f, 100f, heading });

        SpriteRenderer Body() => _root.transform.Find("Observed actor/Authored character").GetComponent<SpriteRenderer>();
    }
}
