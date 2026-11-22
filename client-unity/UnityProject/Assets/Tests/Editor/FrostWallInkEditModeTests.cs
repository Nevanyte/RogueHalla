using NUnit.Framework;
using System.Linq;
using System.IO;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    /// <summary>P-STORM-WISH-FROST-WALL-1: the planted wall uses authored ice relief and only lights delivered tiles.</summary>
    public sealed class FrostWallInkEditModeTests
    {
        GameObject _root;
        FrostWallPresenter _presenter;
        bool _oldLocalLights, _oldFxLights;
        int _oldMaxLocalLights;

        [SetUp]
        public void SetUp()
        {
            _oldLocalLights = AtmosphereQuality.LocalLights;
            _oldMaxLocalLights = AtmosphereQuality.MaxLocalLights;
            _oldFxLights = AtmosphereQuality.FxLights;
            AtmosphereQuality.LocalLights = true;
            AtmosphereQuality.MaxLocalLights = LocalLightField.Max;
            AtmosphereQuality.FxLights = true;
            LocalLightField.Clear();
            _root = new GameObject("frost ink test");
            _presenter = new FrostWallPresenter(_root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            if (_presenter != null) _presenter.Dispose();
            if (_root != null) Object.DestroyImmediate(_root);
            LocalLightField.Clear();
            AtmosphereQuality.LocalLights = _oldLocalLights;
            AtmosphereQuality.MaxLocalLights = _oldMaxLocalLights;
            AtmosphereQuality.FxLights = _oldFxLights;
        }

        static AreaView Wall()
        {
            return new AreaView
            {
                Kind = AreaKind.Wall,
                Ability = AbilityId.Freeze,
                StartedAt = 1000,
                EndsAt = 5000,
                Key = new AreaKey(1000, 12 * 40 + 10),
                Tiles = new[] { new MapTile(10, 12), new MapTile(11, 12), new MapTile(12, 12) }
            };
        }

        [Test]
        public void DeliveredTilesUseAuthoredShelfReliefAndKeepTheirBaseRegistration()
        {
            var texture = Resources.Load<Texture2D>("StormInk/Surfaces/FrostWallShelf");
            Assert.That(texture, Is.Not.Null, "the authored Frost shelf is wired through Resources");
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                Assert.That(ImageConversion.LoadImage(source, File.ReadAllBytes(Path.Combine(Application.dataPath, "Resources/StormInk/Surfaces/FrostWallShelf.png"))), Is.True);
                Assert.That(source.GetPixels32().Any(p => p.a > 0 && (p.r + p.g + p.b) > 90), Is.True, "the imported wall source contains painted ice pixels");
            }
            finally { Object.DestroyImmediate(source); }
            var wall = Wall();
            _presenter.PresentWalls(new[] { wall }, 1000);
            _presenter.Tick(1000, 1, false);
            Assert.AreEqual(3, _presenter.BlockCount);

            for (int i = 0; i < _presenter.BlockCount; i++)
            {
                var go = _presenter.BlockObject(i);
                var art = go.GetComponentInChildren<SpriteRenderer>(true);
                Assert.That(art, Is.Not.Null);
                Assert.That(art.enabled, Is.True);
                Assert.That(art.sprite, Is.Not.Null);
                Assert.That(art.sprite.texture, Is.SameAs(texture), "the authored texture supplies actual wall pixels");
                Assert.That(art.sprite.pivot.y, Is.EqualTo(.02f * texture.height).Within(1f), "the slice is registered at its painted feet");
                Assert.That(go.GetComponent<MeshRenderer>().enabled, Is.True);
                var face = _presenter.BlockLayers(i, FrostWallPresenter.BlockPart.Face);
                Assert.That(face[0].a, Is.EqualTo(255), "the source-driven fallback remains opaque");
                var tile = _presenter.BlockTile(i);
                var expected = ArenaView.WorldPoint(tile.X * 32 + 16, tile.Y * 32 + 16);
                Assert.That(Vector3.Distance(go.transform.position, expected), Is.LessThan(.001f), "the shard remains registered to its delivered base tile");
            }
            Assert.That(FrostWallPresenter.FractureArrivalMs, Is.InRange(150.0, 300.0));
        }

        [Test]
        public void StagedIceLightsAreSampledOnlyAtTheDeliveredWallTiles()
        {
            LocalLightField.Begin(LocalLightField.Max);
            _presenter.PresentWalls(new[] { Wall() }, 2000);
            _presenter.Tick(2000, 2, false);
            Assert.That(LocalLightField.Staged, Is.EqualTo(3), "three pooled analytic accents for one wall, no emitter per object");
            LocalLightField.AddFlashes(0);
            Assert.That(LocalLightField.Count, Is.EqualTo(3));
            for (int i = 0; i < LocalLightField.Count; i++)
            {
                var p = LocalLightField.PositionAt(i);
                Assert.That(p.x, Is.InRange(10f, 13f), "sample x is within the delivered wall span");
                Assert.That(p.y, Is.EqualTo(12.5f).Within(.001f), "sample y is the delivered wall row");
                Assert.That(p.w, Is.InRange(.5f, 1f), "a restrained local glow");
            }
        }

        [Test]
        public void GatheringLightStaysOnTheDeliveredTelegraphSegment()
        {
            LocalLightField.Begin(LocalLightField.Max);
            _presenter.Telegraph(100, 100, 200, 100, 40, 1000, 250);
            _presenter.Tick(1125, 1.125f, false);
            Assert.That(_presenter.TelegraphVisible, Is.True);
            Assert.That(LocalLightField.Staged, Is.EqualTo(3));
            LocalLightField.AddFlashes(0);
            Assert.That(LocalLightField.Count, Is.EqualTo(3));
            for (int i = 0; i < LocalLightField.Count; i++)
            {
                var p = LocalLightField.PositionAt(i);
                Assert.That(p.x, Is.EqualTo(6.25f).Within(.001f), "the light follows the telegraph's fixed x");
                Assert.That(p.y, Is.InRange(2.5f, 3.75f), "the light remains on the delivered wall segment");
            }
        }

        [Test]
        public void VerticalWallUsesRecedingAuthoredSlicesAndKeepsRowSpecificBases()
        {
            var wall = Wall();
            wall.Tiles = new[] { new MapTile(10, 12), new MapTile(10, 13), new MapTile(10, 14) };
            _presenter.PresentWalls(new[] { wall }, 1400);
            _presenter.Tick(1400, 1.4f, false);
            var front = _presenter.BlockObject(0);
            var frontArt = front.GetComponentInChildren<SpriteRenderer>(true);
            Assert.That(frontArt, Is.Not.Null);
            Assert.That(frontArt.sprite, Is.Not.Null);
            var receding = Resources.Load<Texture2D>("StormInk/Surfaces/FrostWallReceding");
            Assert.That(frontArt.sprite.texture, Is.SameAs(receding), "vertical runs use their own longitudinal relief rather than a rotated horizontal shelf");
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                Assert.That(ImageConversion.LoadImage(source, File.ReadAllBytes(Path.Combine(Application.dataPath, "Resources/StormInk/Surfaces/FrostWallReceding.png"))), Is.True);
                Assert.That(source.GetPixels32().Any(p => p.a > 0 && (p.r + p.g + p.b) > 90), Is.True, "the receding art contains painted ice pixels");
            }
            finally { Object.DestroyImmediate(source); }
            Assert.That(frontArt.sprite.rect.y, Is.EqualTo(receding.height * 2f / 3f).Within(1f), "screen-space Y is inverted from map Y when slicing longitudinal art");
            Assert.That(Mathf.Abs(Quaternion.Angle(frontArt.transform.localRotation, Quaternion.identity)), Is.LessThan(.1f));
            Assert.That(frontArt.transform.localScale.y, Is.EqualTo(ArenaView.GroundSquash).Within(.001f));
            Assert.That(_presenter.BlockLayers(0, FrostWallPresenter.BlockPart.Face)[0].a, Is.EqualTo(255));
            for (int i = 0; i < _presenter.BlockCount; i++)
            {
                var tile = _presenter.BlockTile(i);
                Assert.That(Vector3.Distance(_presenter.BlockObject(i).transform.position, ArenaView.WorldPoint(tile.X * 32 + 16, tile.Y * 32 + 16)), Is.LessThan(.001f));
            }
        }

        [Test]
        public void ActualGapsKeepExposedShardEnds()
        {
            var wall = Wall();
            wall.Tiles = new[] { new MapTile(10, 12), new MapTile(12, 12) };
            _presenter.PresentWalls(new[] { wall }, 1400);
            _presenter.Tick(1400, 1.4f, false);
            Assert.AreEqual(2, _presenter.BlockCount, "no visual tile is invented to bridge a delivered hole");
            for (int i = 0; i < 2; i++)
            {
                var sprite = _presenter.BlockObject(i).GetComponentInChildren<SpriteRenderer>(true);
                Assert.That(sprite.enabled, Is.True, "each signed island gets its own authored piece");
                Assert.That(sprite.sprite.rect.width / sprite.sprite.pixelsPerUnit * sprite.transform.localScale.x, Is.EqualTo(1f).Within(.01f),
                    "an isolated tile cannot stretch its art across the missing tile");
            }
        }

        [Test]
        public void AuthoredReliefReplacesRepeatedPerTilePolygonPanels()
        {
            _presenter.PresentWalls(new[] { Wall() }, 1400);
            _presenter.Tick(1400, 1.4f, false);
            for (int i = 0; i < _presenter.BlockCount; i++)
            {
                var go = _presenter.BlockObject(i);
                var sprite = go.GetComponentInChildren<SpriteRenderer>(true);
                Assert.That(sprite.enabled, Is.True);
                Assert.That(sprite.sprite.name, Does.StartWith("frost-wall-shelf-3-"), "contiguous horizontal slices share one connected authored shelf");
                var vertices = go.GetComponent<MeshFilter>().sharedMesh.vertices;
                Assert.That(HasGeometry(vertices, 0), Is.False, "repeated polygon face is removed when the painted relief is available");
                Assert.That(HasGeometry(vertices, 12), Is.False);
                Assert.That(HasGeometry(vertices, 13), Is.False);
            }
        }

        static bool HasGeometry(Vector3[] vertices, int quad)
        {
            int at = quad * 4;
            for (int i = 0; i < 4; i++) if (vertices[at + i].sqrMagnitude > .001f) return true;
            return false;
        }
    }
}
