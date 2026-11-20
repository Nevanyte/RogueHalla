using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Editor;
using Roguehalla.Net.Protocol;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Roguehalla.Client.Tests
{
    public sealed class StormInkChurchPreviewTests
    {
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
            }
        }
    }
}
