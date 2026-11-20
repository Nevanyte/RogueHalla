using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
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
        }

        [Test]
        public void EastBowDrawUsesAuthoredTimingAndAlternatesOnlyOnObservedArrowThenCancelsCleanly()
        {
            var art = Resources.Load<StormInkFireBowAuthoring>("StormInk/Actors/FireBowAuthoring");
            var rig = StormInkFireAxeAuthoring.Load();
            Assert.That(art, Is.Not.Null);
            Assert.That(rig, Is.Not.Null);
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

        void ObserveArrow() => typeof(SpriteActorPresenter).GetMethod("ObservedProjectile", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(_presenter, new object[] { 1u, ProjectileKind.Arrow, 100f, 100f, 0f });

        SpriteRenderer Body() => _root.transform.Find("Observed actor/Authored character").GetComponent<SpriteRenderer>();
    }
}
