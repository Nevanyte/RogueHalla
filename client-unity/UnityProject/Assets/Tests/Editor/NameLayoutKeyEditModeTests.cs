using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Client;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>
    /// P-UI-WORLD-NAMES-1: each presenter art carries a layout serial for the name layout. It is given once when an art is first
    /// used (or taken from the pool again), it survives a renewed observer handle (the art follows the creature) and a reused
    /// pooled art gets a fresh, larger one. The art class is private: the serial is read by reflection.
    /// </summary>
    public sealed class NameLayoutKeyEditModeTests
    {
        GameObject _root; SpriteActorPresenter _presenter; float _now; bool _savedContinuity;

        [SetUp]
        public void SetUp()
        {
            _savedContinuity = SpriteActorPresenter.ContinuityEnabled;
            _root = new GameObject("name layout key test");
            _presenter = new SpriteActorPresenter(_root.transform, null);
            _now = 50f; SpriteActorPresenter.FadeClock = () => _now;
            SpriteActorPresenter.ContinuityEnabled = true;
            _presenter.ExitFadesAllowed = true; _presenter.Delivered = new HashSet<uint>();
            SpriteActorPresenter.MobArt.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            SpriteActorPresenter.FadeClock = null;
            SpriteActorPresenter.ContinuityEnabled = _savedContinuity;
            Object.DestroyImmediate(_root);
        }

        // One presentation pass: each (id, x) as a RegionalMob "grunt" at y 640.
        void Frame(params (uint id, float x)[] mobs)
        {
            _presenter.Begin();
            foreach (var m in mobs)
                _presenter.Present(m.id, m.x, 640, 0, 80, null, ActorStatus.Alive, null, ActorKind.RegionalMob, null, 0, 0, false, 0, 0, "grunt");
            _presenter.End(); _presenter.StepFades();
        }

        void Deliver(params uint[] ids) { var set = new HashSet<uint>(ids); _presenter.Delivered = set; _presenter.Retain(set); }

        object ArtOf(uint id)
        {
            var active = (IDictionary)typeof(SpriteActorPresenter).GetField("_active", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_presenter);
            Assert.That(active.Contains(id), Is.True, "art of id " + id);
            return active[id];
        }

        static int KeyOf(object art)
        {
            var field = art.GetType().GetField("LayoutKey", BindingFlags.Public | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, "Art.LayoutKey is a public field");
            return (int)field.GetValue(art);
        }

        [Test]
        public void TheLayoutSerialIsGivenOncePerArtAndKeptByARenewedHandle()
        {
            Frame((1, 640), (3, 840));
            int k1 = KeyOf(ArtOf(1)), k3 = KeyOf(ArtOf(3));
            Assert.That(k1, Is.GreaterThan(0), "id 1 has a key");
            Assert.That(k3, Is.GreaterThan(0), "id 3 has a key");
            Assert.That(k3, Is.Not.EqualTo(k1), "two arts, two keys");

            _now += 1f;
            Frame((1, 640), (3, 840));
            Assert.That(KeyOf(ArtOf(1)), Is.EqualTo(k1), "a second frame keeps id 1's key");
            Assert.That(KeyOf(ArtOf(3)), Is.EqualTo(k3), "a second frame keeps id 3's key");

            Deliver(2, 3);   // the server renewed creature 1's handle as 2
            Frame((2, 648), (3, 840));
            Assert.That(SpriteActorPresenter.MobArt.Continued, Is.EqualTo(1), "the premise: the art followed the handle");
            Assert.That(KeyOf(ArtOf(2)), Is.EqualTo(k1), "a renewed handle keeps the art's key");
            Assert.That(KeyOf(ArtOf(3)), Is.EqualTo(k3), "the other art is untouched");

            object artOf2 = ArtOf(2);
            Deliver(3);   // creature 2 really goes: its art fades out and is pooled
            _now += .13f; _presenter.StepFades();
            _now += .2f; _presenter.StepFades();
            Assert.That(SpriteActorPresenter.MobArt.Destroyed, Is.EqualTo(1), "the premise: the art went back to the pool");

            Frame((3, 840), (7, 1040));
            Assert.That(ReferenceEquals(ArtOf(7), artOf2), Is.True, "the pooled art is reused");
            int k7 = KeyOf(ArtOf(7));
            Assert.That(k7, Is.GreaterThan(k1), "a reused art gets a fresh key, above id 1's");
            Assert.That(k7, Is.GreaterThan(k3), "a reused art gets a fresh key, above id 3's");
            Assert.That(KeyOf(ArtOf(3)), Is.EqualTo(k3), "id 3 keeps its key");
        }
    }
}
