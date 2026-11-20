using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Client.Tests
{
    public sealed class ChurchGarnetLightEditModeTests
    {
        [Test]
        public void RaisedPaneUsesOneSteadyGlassLightAndDetachesWithoutAffectingCandles()
        {
            var api = typeof(Landmark).Assembly.GetType("Roguehalla.Client.ChurchGarnetLight");
            Assert.That(api, Is.Not.Null, "the church needs a pane light registration path");
            var sync = api.GetMethod("Sync", BindingFlags.Static | BindingFlags.Public);
            var detach = api.GetMethod("Detach", BindingFlags.Static | BindingFlags.Public);
            var fixture = typeof(ChurchLandmarkEditModeTests).GetMethod("Church", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { 0 });
            var map = (MapBlob)fixture.GetType().GetField("Map").GetValue(fixture);
            var anchor = (MapAnchor)fixture.GetType().GetField("Anchor").GetValue(fixture);
            var root = new GameObject("Garnet pane light test");
            root.transform.position = new Vector3(3f, -2f, 0f);
            var view = LandmarkView.Build(root.transform, map, new[] { anchor });
            var lights = new AccentLights();
            lights.Initialize(root.transform);
            lights.RegisterLandmarkCandles(view);
            try
            {
                var landmark = view.Get(0);
                var pane = landmark.Root.GetComponentsInChildren<ChurchArtPlacement>(true)
                    .First(part => part.SlotId.StartsWith("window.")).GetComponent<SpriteRenderer>();
                pane.enabled = true;
                var contact = new Vector2(anchor.Centre.X, anchor.Centre.Y);
                var source = landmark.Root.transform.TransformPoint(new Vector3(contact.x + .28f, -contact.y * ArenaView.GroundSquash + 1.6f, 0f));
                var garnet = new Color(.72f, .22f, .33f);
                int unchanged = lights.Planner.Count;
                void Sync(ChurchWindowPaneMode mode) =>
                    sync.Invoke(null, new object[] { landmark, pane, mode, contact, source, garnet, 3.7f, .65f });

                Sync(ChurchWindowPaneMode.Full);
                Sync(ChurchWindowPaneMode.Full);
                Assert.That(lights.Planner.Count, Is.EqualTo(unchanged + 1));
                var full = lights.Planner.Clusters.Last();
                Assert.That(full.X, Is.EqualTo(contact.x + .28f).Within(.001f));
                Assert.That(full.Y, Is.EqualTo(contact.y));
                Assert.That(full.Lift, Is.EqualTo(1.6f).Within(.001f));
                Assert.That(full.LightColor, Is.EqualTo(garnet));
                Assert.That(full.LightRadius, Is.EqualTo(3.7f));
                AccentLights.Shape(full, out _, out int cookie, out _);
                Assert.That(cookie, Is.EqualTo(LocalLightField.CookieGlass));

                AtmosphereQuality.ApplyPreset(ShowcaseQuality.Medium, false);
                AtmosphereQuality.Bloom = false;
                Assert.That(AtmosphereQuality.Bloom, Is.False);
                Vector4 UploadedGarnet(float dt, float time)
                {
                    LocalLightField.Begin(AtmosphereQuality.EffectiveLocalLights);
                    lights.Tick(full.X, full.Y, 1f, 1f, dt, time);
                    for (int i = 0; i < LocalLightField.Count; i++)
                        if (Mathf.Abs(LocalLightField.PositionAt(i).x - full.X) < .001f
                            && Mathf.Abs(LocalLightField.PositionAt(i).y - full.Y) < .001f)
                            return LocalLightField.ColorAt(i);
                    Assert.Fail("Medium must upload the garnet light without bloom");
                    return Vector4.zero;
                }
                var first = UploadedGarnet(1f, .91f);
                Assert.That(first.x, Is.GreaterThan(first.y));
                Sync(ChurchWindowPaneMode.Full);
                Assert.That(UploadedGarnet(0f, 2.83f), Is.EqualTo(first), "unchanged socket must neither restart the fade nor flicker");

                source.y += .4f;
                Sync(ChurchWindowPaneMode.Full);
                Assert.That(lights.Planner.Count, Is.EqualTo(unchanged + 1));
                Assert.That(lights.Planner.Clusters.Last().Lift, Is.EqualTo(2f).Within(.001f));

                Sync(ChurchWindowPaneMode.Jamb);
                Assert.That(lights.Planner.Count, Is.EqualTo(unchanged));
                Sync(ChurchWindowPaneMode.Full);
                Sync(ChurchWindowPaneMode.Concealed);
                Assert.That(lights.Planner.Count, Is.EqualTo(unchanged));
                Sync(ChurchWindowPaneMode.Full);
                pane.enabled = false;
                Sync(ChurchWindowPaneMode.Full);
                Assert.That(lights.Planner.Count, Is.EqualTo(unchanged));
                pane.enabled = true;
                Sync(ChurchWindowPaneMode.Full);
                detach.Invoke(null, new object[] { landmark, pane });
                detach.Invoke(null, new object[] { landmark, pane });
                Assert.That(lights.Planner.Count, Is.EqualTo(unchanged));
            }
            finally
            {
                LocalLightField.Begin(0);
                AtmosphereQuality.RefreshFromSettings();
                lights.RegisterLandmarkCandles(null);
                lights.Dispose();
                view.Dispose();
                Object.DestroyImmediate(root);
            }
        }
    }
}
