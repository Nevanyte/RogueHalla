using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Editor;
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
    }
}
