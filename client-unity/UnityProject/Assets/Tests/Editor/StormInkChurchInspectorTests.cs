using System;
using NUnit.Framework;
using Roguehalla.Editor;
using UnityEditor;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkChurchInspectorTests
    {
        [Test]
        public void AuthoredTurnPolicyAndPermittedBaysApplyInChurchCoordinates()
        {
            var art = ScriptableObject.CreateInstance<ChurchArtDefinition>();
            try
            {
                art.AllowedTurns = ChurchArtTurns.AnyAngle;
                Assert.That(art.LocalTurn(37f), Is.EqualTo(37f));
                art.AllowedTurns = ChurchArtTurns.QuarterTurns;
                Assert.That(art.LocalTurn(-76f), Is.EqualTo(-90f));
                art.AllowedTurns = ChurchArtTurns.ReverseOnly;
                Assert.That(art.LocalTurn(140f), Is.EqualTo(180f));
                art.AllowedTurns = ChurchArtTurns.FixedToSlot;
                Assert.That(art.LocalTurn(150f), Is.Zero);
                art.Mount = ChurchArtMount.Wall;
                art.PermittedBays = ChurchWindowBays.RearInnerLeft;
                Assert.That(art.AllowsWindow("window.rear.bay1"), Is.True);
                Assert.That(art.AllowsWindow("window.rear.bay2"), Is.False);
                Assert.That(art.AllowsWindow("window.front.bay1"), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(art); }
        }

        [Test]
        public void FieldOverridesSurviveSavingAndTextureReimportAndResetIndependently()
        {
            const string slot = "pew.row0.left";
            string folder = "StormInkAuthoringTest-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder);
            string dir = "Assets/" + folder + "/";
            string oakPath = dir + "Oak.asset", layoutPath = dir + "Layout.asset", imagePath = dir + "OakPew.png";
            try
            {
                var original = Resources.Load<ChurchArtDefinition>("StormInk/Church/Definitions/OakPew");
                Assert.That(original, Is.Not.Null);
                Assert.That(AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(original.Defaults.Front), imagePath), Is.True);
                var oak = UnityEngine.Object.Instantiate(original);
                oak.Defaults.Front = AssetDatabase.LoadAssetAtPath<Sprite>(imagePath);
                Assert.That(oak.Defaults.Front, Is.Not.Null);
                var layout = ScriptableObject.CreateInstance<ChurchInteriorDefinition>();
                AssetDatabase.CreateAsset(oak, oakPath);
                AssetDatabase.CreateAsset(layout, layoutPath);
                EditFloat(oak, "Defaults.Width", 3);
                ChurchArtPlacementEditor.SetOverride(layout, oak, slot, ChurchArtOverrideFields.Width, true);
                Assert.That(layout.Resolve(slot, oak, null).Values.Width, Is.EqualTo(3f), "opting in preserves the current drawing");
                EditFloat(layout, "SlotOverrides.Array.data[0].Values.Width", 1.5f);
                EditFloat(oak, "Defaults.Width", 2.75f);
                EditFloat(oak, "Defaults.Depth", .8f);
                var material = new Material(Shader.Find("Sprites/Default"));
                string materialPath = dir + "Matte.mat";
                AssetDatabase.CreateAsset(material, materialPath);
                using (var source = new SerializedObject(oak))
                {
                    source.FindProperty("Defaults.Material").objectReferenceValue = material;
                    source.ApplyModifiedProperties();
                }
                var value = layout.Resolve(slot, oak, null).Values;
                Assert.That(value.Width, Is.EqualTo(1.5f));
                Assert.That(value.Depth, Is.EqualTo(.8f));
                Assert.That(layout.Resolve("pew.row0.right", oak, null).Values.Width, Is.EqualTo(2.75f));

                Undo.IncrementCurrentGroup();
                ChurchArtPlacementEditor.SetOverride(layout, oak, slot, ChurchArtOverrideFields.Width, false);
                Undo.FlushUndoRecordObjects();
                Assert.That(layout.SlotOverrides, Is.Empty);
                Undo.PerformUndo();
                Assert.That(layout.Resolve(slot, oak, null).Values.Width, Is.EqualTo(1.5f));
                Undo.PerformRedo();
                Assert.That(layout.Resolve(slot, oak, null).Values.Width, Is.EqualTo(2.75f));
                Undo.PerformUndo();
                AssetDatabase.SaveAssetIfDirty(oak);
                AssetDatabase.SaveAssetIfDirty(layout);
                Resources.UnloadAsset(oak);
                Resources.UnloadAsset(layout);
                AssetDatabase.ImportAsset(imagePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                oak = AssetDatabase.LoadAssetAtPath<ChurchArtDefinition>(oakPath);
                layout = AssetDatabase.LoadAssetAtPath<ChurchInteriorDefinition>(layoutPath);
                foreach (bool mirrored in new[] { false, true })
                for (int front = 0; front < 4; front++)
                {
                    var anchor = StormInkChurchStage.PreviewMap(front, mirrored).Anchors[0];
                    var resolved = layout.Resolve(slot, oak, anchor);
                    Assert.That(resolved.Mirrored, Is.EqualTo(mirrored));
                    Assert.That(resolved.Values.Width, Is.EqualTo(1.5f));
                    Assert.That(resolved.Values.Depth, Is.EqualTo(.8f));
                    Assert.That(AssetDatabase.GetAssetPath(resolved.Values.Front), Is.EqualTo(imagePath));
                    Assert.That(AssetDatabase.GetAssetPath(resolved.Values.Material), Is.EqualTo(materialPath));
                }
            }
            finally
            {
                AssetDatabase.DeleteAsset("Assets/" + folder);
            }
        }

        static void EditFloat(UnityEngine.Object owner, string path, float value)
        {
            using (var source = new SerializedObject(owner))
            {
                var property = source.FindProperty(path);
                Assert.That(property, Is.Not.Null);
                property.floatValue = value;
                source.ApplyModifiedProperties();
            }
        }
    }
}
