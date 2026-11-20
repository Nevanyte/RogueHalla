using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Editor;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkChurchPreviewTests
    {
        [UnityTest]
        public IEnumerator SelectedPewFollowsItsArchitecturalSlotThroughEveryLayout()
        {
            try
            {
                EditorApplication.ExecuteMenuItem("Tools/RogueHalla/Storm Ink/Church Art Studio");
                var stage = (StormInkChurchStage)StageUtility.GetCurrentStage();
                var room = stage.scene.GetRootGameObjects().Single(o => o.name == "Storm Ink church");
                var bailiff = room.GetComponentsInChildren<SpriteRenderer>()
                    .Single(r => r.sprite != null && r.sprite.name.StartsWith("Bailiff/"));
                Assert.That(bailiff.color.a, Is.GreaterThan(.99f), "the remote fighter is visible immediately in the static study");
                var original = room.GetComponentsInChildren<ChurchArtPlacement>(true)
                    .Single(part => part.SlotId == "pew.row0.left");
                Selection.activeGameObject = original.gameObject;
                for (int turn = 0; turn < 4; turn++)
                foreach (bool reflected in new[] { false, true })
                {
                    stage.ShowLayout(turn, reflected);
                    yield return null;
                    yield return null;
                    var current = Selection.activeGameObject;
                    Assert.That(current, Is.Not.Null);
                    Assert.That(current.scene, Is.EqualTo(stage.scene));
                    var part = current.GetComponent<ChurchArtPlacement>();
                    Assert.That(part.SlotId, Is.EqualTo("pew.row0.left"));
                    Assert.That(AssetDatabase.Contains(part.Definition) && AssetDatabase.Contains(part.Layout), Is.True);
                }
            }
            finally
            {
                StageUtility.GoToMainStage();
            }
        }

        [UnityTest]
        public IEnumerator SavingAChurchAssetPreservesTheGroundPaletteAndSelectedFighter()
        {
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/StormInkStudioSaveTest.asset");
            try
            {
                var copy = Object.Instantiate(Resources.Load<ChurchArtDefinition>("StormInk/Church/Definitions/OakPew"));
                AssetDatabase.CreateAsset(copy, path);
                EditorApplication.ExecuteMenuItem("Tools/RogueHalla/Storm Ink/Church Art Studio");
                var stage = (StormInkChurchStage)StageUtility.GetCurrentStage();
                var room = stage.scene.GetRootGameObjects().Single(o => o.GetComponent<ArenaView>() != null);
                var body = room.GetComponentsInChildren<StormInkArtistSource>(true)
                    .Single(s => s.Kind == StormInkArtistSourceKind.FireAxeBody);
                Selection.activeGameObject = body.gameObject;
                var palette = room.GetComponent<ArenaView>().PreviewGroundMaterial.GetVectorArray("_PalBase");
                Assert.That(palette, Is.Not.Empty);
                copy = AssetDatabase.LoadAssetAtPath<ChurchArtDefinition>(path);
                copy.Defaults.Width += .25f;
                EditorUtility.SetDirty(copy);
                AssetDatabase.SaveAssetIfDirty(copy);
                yield return null;
                yield return null;
                var restored = stage.scene.GetRootGameObjects().Single(o => o.GetComponent<ArenaView>() != null);
                Assert.That(restored.GetComponent<ArenaView>().PreviewGroundMaterial.GetVectorArray("_PalBase"), Is.EqualTo(palette));
                Assert.That(Selection.activeGameObject.GetComponent<StormInkArtistSource>().Kind,
                    Is.EqualTo(StormInkArtistSourceKind.FireAxeBody));
            }
            finally
            {
                StageUtility.GoToMainStage();
                AssetDatabase.DeleteAsset(path);
            }
        }

        [Test]
        public void StudioMenuOpensTheRenderedRoomAndLeavesTheAuthorSceneIntact()
        {
            var previous = SceneManager.GetActiveScene();
            var originalRoots = previous.GetRootGameObjects();
            try
            {
                Assert.That(EditorApplication.ExecuteMenuItem("Tools/RogueHalla/Storm Ink/Church Art Studio"), Is.True);
                var stage = StageUtility.GetCurrentStage() as StormInkChurchStage;
                Assert.That(stage, Is.Not.Null);
                Assert.That(EditorSceneManager.IsPreviewScene(stage.scene), Is.True);
                var room = stage.scene.GetRootGameObjects().Single(o => o.name == "Storm Ink church");
                var paintings = room.GetComponentsInChildren<SpriteRenderer>(true);
                Assert.That(paintings.Any(r => r.sprite != null && r.sprite.texture.name == "FireAxe"), "preview uses the equipped Fire body");
                Assert.That(paintings.Any(r => r.sprite != null && r.sprite.texture.name.Contains("Bailiff")), "preview uses the opposing Bailiff");
                Assert.That(room.GetComponentsInChildren<Collider>(true), Is.Empty, "walk-through presentation");
            }
            finally
            {
                StageUtility.GoToMainStage();
            }
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(previous));
            Assert.That(previous.GetRootGameObjects().Except(originalRoots).Select(o => o.name), Is.Empty);
            Assert.That(originalRoots.Except(previous.GetRootGameObjects()), Is.Empty);
        }

        [Test]
        public void DesignerCanOpenAnIsolatedStageForEveryChurchFacing()
        {
            var stage = typeof(LookCapture).Assembly.GetType("Roguehalla.Editor.StormInkChurchStage");
            Assert.That(stage, Is.Not.Null, "the designer needs an edit stage independent of the running match");
            Assert.That(typeof(PreviewSceneStage).IsAssignableFrom(stage));
            Assert.That(stage.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(m => m.GetCustomAttributes<MenuItem>()).Any(m => m.menuItem == "Tools/RogueHalla/Storm Ink/Church Art Studio"));
            var build = stage.GetMethod("PreviewMap", BindingFlags.Static | BindingFlags.Public);
            Assert.That(build, Is.Not.Null);
            foreach (bool reflected in new[] { false, true })
            for (int turn = 0; turn < 4; turn++)
            {
                var map = (MapBlob)build.Invoke(null, new object[] { turn, reflected });
                Assert.That(map.Anchors.Length, Is.EqualTo(1));
                Assert.That(map.Anchors[0].Front, Is.EqualTo(new[] { 4, 8, 12, 0 }[turn]));
                Assert.That(map.Anchors[0].Entrance.Length, Is.EqualTo(5));
                Assert.That(ChurchArtOrientation.Resolve(map.Anchors[0]).Mirrored, Is.EqualTo(reflected));
            }
        }

        [UnityTest]
        public IEnumerator StudioCanHoldEveryFireSwordDrawingWithBothHandsAndTheSharedSwordConnected()
        {
            try
            {
                Assert.That(EditorApplication.ExecuteMenuItem("Tools/RogueHalla/Storm Ink/Church Art Studio"), Is.True);
                yield return null;
                var stage = (StormInkChurchStage)StageUtility.GetCurrentStage();
                var room = stage.scene.GetRootGameObjects().Single(o => o.name == "Storm Ink church");
                stage.PreviewFireLoadout(WeaponId.Sword, 0, 0);
                var body = room.GetComponentsInChildren<StormInkArtistSource>(true)
                    .Single(source => source.Kind == StormInkArtistSourceKind.FireSwordBody);
                var sword = body.Renderer.transform.parent.Find("Equipped weapon cue")
                    .GetComponent<StormInkArtistSource>();
                Assert.That(sword.Kind, Is.EqualTo(StormInkArtistSourceKind.EquippedSword));

                for (int row = 0; row < 8; row++)
                for (int column = 0; column < 10; column++)
                {
                    stage.PreviewFireLoadout(WeaponId.Sword, row, column);
                    Assert.That(body.Sprite.name, Is.EqualTo($"FireSword/{row}/{column}"));
                    Assert.That(body.PoseRow, Is.EqualTo(row));
                    Assert.That(body.TryBodyPalm(false, out var right), Is.True, $"right palm missing at {row}/{column}");
                    Assert.That(body.TryBodyPalm(true, out var left), Is.True, $"left palm missing at {row}/{column}");
                    Assert.That(Vector2.Distance(right, left), Is.GreaterThan(.03f), $"hands overlap at {row}/{column}");
                    Assert.That(sword.Sprite, Is.SameAs(StormInkWeaponArt.Get(WeaponId.Sword, row)));
                    Assert.That(sword.PoseRow, Is.EqualTo(row));
                    Assert.That(Vector3.Distance(Socket(body.Renderer, right), Socket(sword.Renderer, sword.HeldGripUv)),
                        Is.LessThan(.001f), $"shared sword grip misses the right hand at {row}/{column}");
                }

                stage.PreviewFireLoadout(WeaponId.Sword, 4, 7);
                var sample = new GameObject("Gameplay sword release");
                try
                {
                    var presenter = new SpriteActorPresenter(sample.transform, Resources.Load<ShowcaseCatalog>("Showcase/Catalog"));
                    const float north = -Mathf.PI / 2f;
                    void Present()
                    {
                        presenter.Begin();
                        presenter.Present(1, 100, 100, north, 100, FactionId.Fire, ActorStatus.Alive,
                            WeaponId.Sword, ActorKind.Player, null, 0, 0, true);
                        presenter.End();
                    }
                    Present();
                    for (int swing = 0; swing < 2; swing++)
                    {
                        presenter.ConfirmedCue(1, FxKind.Swing, WeaponId.Sword, north);
                        Present();
                    }
                    var actual = sample.GetComponentsInChildren<StormInkArtistSource>()
                        .Single(source => source.Kind == StormInkArtistSourceKind.EquippedSword).Renderer;
                    var actualBody = actual.transform.parent.Find("Authored character").GetComponent<SpriteRenderer>();
                    Assert.That(actualBody.sprite, Is.SameAs(body.Sprite));
                    Assert.That(Quaternion.Angle(actual.transform.localRotation, sword.Renderer.transform.localRotation),
                        Is.LessThan(.02f), "preview and gameplay must use the same base pose and authored blade angle");
                    Assert.That(sword.Renderer.sortingOrder - body.Renderer.sortingOrder,
                        Is.EqualTo(actual.sortingOrder - actualBody.sortingOrder));
                }
                finally { UnityEngine.Object.DestroyImmediate(sample); }
            }
            finally
            {
                StageUtility.GoToMainStage();
            }
        }

        [UnityTest]
        public IEnumerator StudioSamplesReadyAndReleaseBowWithSharedGripAndGameplayParity()
        {
            var authoringField = typeof(StormInkFireBowAuthoring).GetField("_default",
                BindingFlags.Static | BindingFlags.NonPublic);
            var readyRowsField = typeof(StormInkFireBowAuthoring).GetField("readyRows",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var middleThresholdField = typeof(StormInkFireBowAuthoring).GetField("midDrawAt",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var sustainedThresholdField = typeof(StormInkFireBowAuthoring).GetField("sustainedDrawAt",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var productionArt = StormInkFireBowAuthoring.Load();
            var previewArt = UnityEngine.Object.Instantiate(productionArt);
            var readyRows = (bool[])readyRowsField.GetValue(previewArt);
            readyRows[0] = false;
            readyRowsField.SetValue(previewArt, readyRows);
            middleThresholdField.SetValue(previewArt, .4f);
            sustainedThresholdField.SetValue(previewArt, .45f);
            authoringField.SetValue(null, previewArt);
            try
            {
                Assert.That(EditorApplication.ExecuteMenuItem("Tools/RogueHalla/Storm Ink/Church Art Studio"), Is.True);
                yield return null;
                var stage = (StormInkChurchStage)StageUtility.GetCurrentStage();
                var room = stage.scene.GetRootGameObjects().Single(o => o.name == "Storm Ink church");
                var art = StormInkFireBowAuthoring.Load();
                stage.PreviewFireLoadout(WeaponId.Bow, 2, 1);
                var body = room.GetComponentsInChildren<StormInkArtistSource>(true)
                    .Single(source => source.Kind == StormInkArtistSourceKind.FireBowBody);
                Assert.That(body.Sprite.name, Is.EqualTo("FireBow/2/1"),
                    "the midpoint of custom mid/sustained thresholds must hold the authored middle drawing");
                stage.PreviewFireSampler(WeaponId.Bow, 2, StormInkFireSwordMotionPhase.Windup, .8f, true);
                var bow = body.Renderer.transform.parent.Find("Equipped weapon cue").GetComponent<StormInkArtistSource>();
                Assert.That(body.Sprite.name, Is.EqualTo("FireBow/2/7"));
                Assert.That(body.BodyPainting, Is.SameAs(art));
                Assert.That(body.TryBodyPalm(false, out var right), Is.True);
                Assert.That(body.TryBodyPalm(true, out var left), Is.True);
                Assert.That(Vector2.Distance(right, left), Is.GreaterThan(.03f));
                Assert.That(bow.Sprite, Is.SameAs(StormInkWeaponArt.Get(WeaponId.Bow, 2)));
                Assert.That(Vector3.Distance(Socket(body.Renderer, left), Socket(bow.Renderer, bow.HeldGripUv)),
                    Is.LessThan(.001f), "shared bow grip must stay attached to the measured left hand");

                stage.PreviewFireSampler(WeaponId.Bow, 2, StormInkFireSwordMotionPhase.Strike, .5f, true);
                Assert.That(body.Sprite.name, Is.EqualTo("FireBow/2/8"));
                Assert.That(body.TryBodyPalm(true, out var releaseLeft), Is.True);
                Assert.That(Vector3.Distance(Socket(body.Renderer, releaseLeft), Socket(bow.Renderer, bow.HeldGripUv)),
                    Is.LessThan(.001f));
                var expectedRelease = body.Renderer.sprite;
                stage.PreviewFireSampler(WeaponId.Bow, 0, StormInkFireSwordMotionPhase.Windup, .8f, true);
                Assert.That(body.Renderer.sprite, Is.Not.Null);
                Assert.That(body.Renderer.sprite.name, Does.Not.StartWith("FireBow/"),
                    "unpainted facing must use the standard Fire fallback");
                Assert.That(bow.Sprite, Is.SameAs(StormInkWeaponArt.Get(WeaponId.Bow, 0)));

                var sample = new GameObject("Gameplay bow release parity");
                try
                {
                    var presenter = new SpriteActorPresenter(sample.transform, Resources.Load<ShowcaseCatalog>("Showcase/Catalog"));
                    presenter.Begin();
                    presenter.Present(1, 100, 100, 0, 100, FactionId.Fire, ActorStatus.Alive,
                        WeaponId.Bow, ActorKind.Player, null, 0, 0, true, drawStartedAt: 800, sampleTickMs: 1000);
                    presenter.End();
                    typeof(SpriteActorPresenter).GetMethod("ObservedProjectile", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(presenter, new object[] { 1u, ProjectileKind.Arrow, 100f, 100f, 0f });
                    typeof(SpriteActorPresenter).GetMethod("ObservedProjectile", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(presenter, new object[] { 1u, ProjectileKind.Arrow, 100f, 100f, 0f });
                    presenter.Begin();
                    presenter.Present(1, 100, 100, 0, 100, FactionId.Fire, ActorStatus.Alive,
                        WeaponId.Bow, ActorKind.Player, null, 0, 0, true, drawStartedAt: 800, sampleTickMs: 1000);
                    presenter.End();
                    var actual = sample.GetComponentsInChildren<StormInkArtistSource>()
                        .Single(source => source.Kind == StormInkArtistSourceKind.FireBowBody);
                    Assert.That(actual.Sprite, Is.SameAs(expectedRelease), "studio sampler must match owner draw + observed Arrow gameplay selection");
                }
                finally { UnityEngine.Object.DestroyImmediate(sample); }
            }
            finally
            {
                StageUtility.GoToMainStage();
                authoringField.SetValue(null, productionArt);
                UnityEngine.Object.DestroyImmediate(previewArt);
            }
        }

        static Vector3 Socket(SpriteRenderer renderer, Vector2 uv)
        {
            var sprite = renderer.sprite;
            Vector2 local = (Vector2.Scale(uv, sprite.rect.size) - sprite.pivot) / sprite.pixelsPerUnit;
            return renderer.transform.TransformPoint(local);
        }
    }
}
