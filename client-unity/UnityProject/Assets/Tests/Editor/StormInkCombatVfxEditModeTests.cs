using System.Linq;
using NUnit.Framework;
using Roguehalla.Net.Ui;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkCombatVfxEditModeTests
    {
        const int ArcSamples = 25;
        const int FanVertices = 1 + 3 * ArcSamples;
        GameObject _root;
        HitZonePresenter _presenter;

        [SetUp]
        public void SetUp()
        {
            WeaponShapes.Set(new[] { new WeaponShape { Weapon = 4, Kind = 1, Reach = 112, HalfArc = Mathf.PI / 2 } });
            _root = new GameObject("Crowded melee");
            _presenter = new HitZonePresenter(_root.transform, _ => true, "")
            {
                Position = (uint _, out float x, out float y) => { x = 160; y = 96; return true; }
            };
        }

        [TearDown]
        public void TearDown()
        {
            WeaponShapes.Clear();
            Object.DestroyImmediate(_root);
        }

        Mesh[] ActiveMeshes() => _root.GetComponentsInChildren<MeshFilter>().Select(f => f.sharedMesh).ToArray();
        static int FanPeak(Mesh mesh) => mesh.colors32.Take(FanVertices).Max(c => c.a);

        [Test]
        public void ThreeOverlappingWarningsKeepTheExactEdgeAndAtMostAQuarterCombinedFill()
        {
            const float aim = .38f;
            for (uint fighter = 1; fighter <= 3; fighter++)
                Assert.That(_presenter.Windup(fighter, 4, aim, 400, 0), Is.True);
            _presenter.Tick(.18f, false);
            Mesh[] meshes = ActiveMeshes();
            Assert.That(meshes.Length, Is.EqualTo(3));
            float remaining = 1f;
            foreach (var mesh in meshes)
            {
                remaining *= 1f - FanPeak(mesh) / 255f;
                Assert.That(mesh.colors32[FanVertices].a, Is.GreaterThanOrEqualTo(200), "Dark outline remains high contrast");
                Assert.That(mesh.colors32[FanVertices + 58].a, Is.GreaterThanOrEqualTo(145), "Light hairline remains high contrast");
                HitZoneModel.ArcPoint(160, 96, 112, Mathf.PI / 2f, aim, 0, out float x, out float y);
                Assert.That(Vector3.Distance(mesh.vertices[FanVertices], ArenaView.WorldPoint(x, y)), Is.LessThan(.0001f),
                    "Warning perimeter must match the unchanged delivered shape");
            }
            Assert.That(1f - remaining, Is.LessThanOrEqualTo(.25f), "Stacking three warnings must preserve the underlying fighter lane");
        }

        [Test]
        public void ConfirmedSwingHasAShortOuterStrokeAndNoInteriorFill()
        {
            _presenter.Windup(1, 4, 0, 400, 0);
            Assert.That(_presenter.Swing(1, 4, 160, 96, 0, .1f), Is.True);
            Assert.That(_presenter.WindupLive(1), Is.False);
            _presenter.Tick(.13f, false);
            Mesh mesh = ActiveMeshes().Single();
            Assert.That(mesh.colors32.Take(1 + 2 * ArcSamples).All(c => c.a == 0), Is.True);
            Assert.That(FanPeak(mesh), Is.InRange(25, 100), "A short outer brush is visible on a delivered swing");
            _presenter.Tick(.205f, false);
            Assert.That(FanPeak(mesh), Is.EqualTo(0), "Dense motion disappears while the zone's lifetime can still complete");
            Assert.That(_presenter.LiveCount, Is.EqualTo(1));
            _presenter.Tick(.5f, false);
            Assert.That(_presenter.LiveCount, Is.Zero);
        }

        [Test]
        public void ReducedMotionKeepsTheLogicalWarningAndCancelsTheAnimatedStroke()
        {
            _presenter.Windup(1, 4, 0, 400, 0);
            _presenter.Tick(.15f, true);
            Assert.That(ActiveMeshes().Single().colors32[FanVertices].a, Is.GreaterThanOrEqualTo(200));
            _presenter.CancelWindup(1);
            Assert.That(_presenter.LiveCount, Is.Zero);
            _presenter.Swing(1, 4, 160, 96, 0, .2f);
            _presenter.Tick(.23f, true);
            Assert.That(FanPeak(ActiveMeshes().Single()), Is.Zero);
        }
    }
}
