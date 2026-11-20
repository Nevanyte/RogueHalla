using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Roguehalla.Client;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client.Tests
{
    public sealed class ChurchArtAuthoringEditModeTests
    {
        [Test]
        public void FamilyAssetsCarryWorldUnitDefaultsAndTheirPaintedSprites()
        {
            var names = new[] { "OakPew", "GothicPulpit", "GarnetLancet", "Candelabrum" };
            foreach (string name in names)
            {
                var definition = Resources.Load<ChurchArtDefinition>("StormInk/Church/Definitions/" + name);
                Assert.That(definition, Is.Not.Null, name + " definition resource");
                Assert.That(definition.Defaults.Front, Is.Not.Null, name + " front sprite");
                Assert.That(definition.Defaults.Width, Is.GreaterThan(.1f), name + " authored world width");
                Assert.That(definition.Defaults.Height, Is.GreaterThan(.1f), name + " authored world height");
                Assert.That(definition.Defaults.Tint.a, Is.EqualTo(1f));
            }
            Assert.That(Resources.Load<ChurchArtDefinition>("StormInk/Church/Definitions/OakPew").Defaults.Width, Is.EqualTo(4.55f).Within(.001f));
            Assert.That(Resources.Load<ChurchArtDefinition>("StormInk/Church/Definitions/Candelabrum").EmitsCandleLight, Is.True);
        }

        [Test]
        public void SlotMaskOverridesOneFieldAndUnselectedFieldsInherit()
        {
            var family = ScriptableObject.CreateInstance<ChurchArtDefinition>();
            family.Defaults.Width = 4.55f;
            family.Defaults.Height = 2.5f;
            family.Defaults.Tint = Color.white;
            var inheritedMaterial = new Material(Shader.Find("Sprites/Default"));
            family.Defaults.Material = inheritedMaterial;
            var layout = ScriptableObject.CreateInstance<ChurchInteriorDefinition>();
            layout.SlotOverrides = new List<ChurchArtSlotOverride>
            {
                new ChurchArtSlotOverride { SlotId = "pew.row0.left", OverrideFields = ChurchArtOverrideFields.Width,
                    Values = new ChurchArtValues { Width = 5.25f, Height = 9f, Tint = Color.red } }
            };
            var resolved = layout.Resolve("pew.row0.left", family, Anchor(0));
            Assert.That(resolved.Values.Width, Is.EqualTo(5.25f));
            Assert.That(resolved.Values.Height, Is.EqualTo(2.5f));
            Assert.That(resolved.Values.Tint, Is.EqualTo(Color.white));
            Assert.That(resolved.Values.Material, Is.SameAs(inheritedMaterial));
            family.Defaults.Material = ShowcaseSprites.Lit;
            resolved = layout.Resolve("pew.row0.left", family, Anchor(0));
            Assert.That(resolved.Values.Material, Is.SameAs(ShowcaseSprites.Lit), "unmasked material follows later family edits");
            layout.SlotOverrides[0].OverrideFields = ChurchArtOverrideFields.None;
            resolved = layout.Resolve("pew.row0.left", family, Anchor(0));
            Assert.That(resolved.Values.Width, Is.EqualTo(4.55f));
            Object.DestroyImmediate(family); Object.DestroyImmediate(layout); Object.DestroyImmediate(inheritedMaterial);
        }

        [Test]
        public void MirrorIsStablePublicAndCanBeOverriddenPerPreviewCall()
        {
            var anchor = Anchor(0);
            var layout = ScriptableObject.CreateInstance<ChurchInteriorDefinition>();
            bool first = ChurchArtOrientation.Resolve(anchor).Mirrored;
            Assert.That(ChurchArtOrientation.Resolve(anchor).Mirrored, Is.EqualTo(first));
            Assert.That(layout.IsMirrored(anchor), Is.EqualTo(first));
            Assert.That(ChurchArtOrientation.Resolve(anchor, true).Mirrored, Is.True);
            Assert.That(ChurchArtOrientation.Resolve(anchor, false).Mirrored, Is.False);
            bool sawMirrored = false, sawUnmirrored = false;
            for (ushort x = 0; x < 32; x++)
            {
                var shifted = new MapAnchor(anchor.Zone, anchor.Kind, new MapTile((ushort)(anchor.Centre.X + x), anchor.Centre.Y),
                    new MapRect(x, anchor.Footprint.Y, anchor.Footprint.W, anchor.Footprint.H), anchor.Front, anchor.Entrance);
                if (ChurchArtOrientation.Resolve(shifted).Mirrored) sawMirrored = true; else sawUnmirrored = true;
            }
            Assert.That(sawMirrored && sawUnmirrored, Is.True, "translation changes the mixed cosmetic parity across public anchors");
            Object.DestroyImmediate(layout);
        }

        [Test]
        public void RuntimePlacementsResolveAllEightChurchOrientationsAndKeepContactHandlesInvertible()
        {
            var layouts = Resources.Load<ChurchInteriorDefinition>("StormInk/Church/Definitions/SentencedChurch");
            Assert.That(layouts, Is.Not.Null);
            for (int k = 0; k < 8; k++)
            {
                object rotated = typeof(ChurchLandmarkEditModeTests).GetMethod("Church", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { k });
                var rotatedType = rotated.GetType();
                var rotatedMap = (MapBlob)rotatedType.GetField("Map").GetValue(rotated);
                var rotatedAnchor = (MapAnchor)rotatedType.GetField("Anchor").GetValue(rotated);
                var root = new GameObject("church authoring test");
                var view = LandmarkView.Build(root.transform, rotatedMap, new[] { rotatedAnchor });
                try
                {
                    var placements = view.Get(0).Root.GetComponentsInChildren<ChurchArtPlacement>(true);
                    Assert.That(placements.Count(p => p.SlotId.StartsWith("pew.row")), Is.EqualTo(8), "eight stable pew slots, orientation " + k);
                    Assert.That(placements.Count(p => p.SlotId.StartsWith("window.")), Is.EqualTo(8), "eight stable window slots, orientation " + k);
                    Assert.That(placements.Count(p => p.SlotId.StartsWith("candle.")), Is.EqualTo(8), "eight matching candle slots, orientation " + k);
                    Assert.That(placements.Any(p => p.SlotId == "pulpit.left"), "pulpit exists in orientation " + k);
                    var pulpit = placements.First(p => p.SlotId == "pulpit.left");
                    float nearestCandle = placements.Where(p => p.SlotId.StartsWith("candle."))
                        .Min(p => Vector2.Distance(pulpit.ContactPoint, p.ContactPoint));
                    Assert.That(nearestCandle, Is.GreaterThan(1.5f), "pulpit and closest candle feet have clear spacing in orientation " + k);
                    foreach (var placement in placements)
                    {
                        Assert.That(placement.Definition, Is.Not.Null, placement.SlotId);
                        Assert.That(placement.Layout, Is.SameAs(layouts), placement.SlotId);
                        placement.RefreshResolved();
                        var worldContact = placement.ContactPoint;
                        var uv = placement.WorldToUv(worldContact);
                        Assert.That(uv.x, Is.EqualTo(placement.Resolve(rotatedAnchor).Values.Contact.x).Within(.002f), placement.SlotId + " contact U");
                        Assert.That(uv.y, Is.EqualTo(placement.Resolve(rotatedAnchor).Values.Contact.y).Within(.002f), placement.SlotId + " contact V");
                    }
                    var pew = placements.First(p => p.SlotId == "pew.row0.left");
                    var beforePosition = pew.transform.localPosition;
                    int beforeSort = pew.GetComponent<SpriteRenderer>().sortingOrder;
                    pew.RefreshResolved();
                    Assert.That(Vector3.Distance(beforePosition, pew.transform.localPosition), Is.LessThan(.0001f), "refresh is position-idempotent");
                    Assert.That(pew.GetComponent<SpriteRenderer>().sortingOrder, Is.EqualTo(beforeSort), "refresh is sort-idempotent");
                    var offset = pew.WorldToLocalOffset(pew.ContactPoint);
                    Assert.That(offset.magnitude, Is.LessThan(.001f), "world contact handle inverses to zero slot offset");
                }
                finally { view.Dispose(); Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void RefreshUpdatesPooledCandleSpillWithoutRebuildingTheMap()
        {
            object site = typeof(ChurchLandmarkEditModeTests).GetMethod("Church", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { 0 });
            var siteType = site.GetType();
            var map = (MapBlob)siteType.GetField("Map").GetValue(site);
            var anchor = (MapAnchor)siteType.GetField("Anchor").GetValue(site);
            var root = new GameObject("church candle light test");
            var view = LandmarkView.Build(root.transform, map, new[] { anchor });
            var lights = new AccentLights(); lights.Initialize(root.transform); lights.RegisterLandmarkCandles(view);
            var candle = view.Get(0).Root.GetComponentsInChildren<ChurchArtPlacement>(true).First(p => p.SlotId == "candle.front.bay0");
            var otherCandleMaterial = view.Get(0).Root.GetComponentsInChildren<ChurchArtPlacement>(true).First(p => p.SlotId == "candle.front.bay1").GetComponent<SpriteRenderer>().sharedMaterial;
            var previewLayout = ScriptableObject.CreateInstance<ChurchInteriorDefinition>();
            var customMaterial = new Material(Shader.Find("Sprites/Default"));
            previewLayout.SlotOverrides.Add(new ChurchArtSlotOverride
            {
                SlotId = candle.SlotId,
                OverrideFields = ChurchArtOverrideFields.FlameColor | ChurchArtOverrideFields.FlameIntensity |
                    ChurchArtOverrideFields.SpillRadius | ChurchArtOverrideFields.SpillIntensity | ChurchArtOverrideFields.Material,
                Values = new ChurchArtValues { FlameColor = Color.magenta, FlameIntensity = 1.4f, SpillRadius = 3.4f, SpillIntensity = .25f, Material = customMaterial }
            });
            candle.Layout = previewLayout;
            candle.RefreshResolved();
            Assert.That(candle.GetComponent<SpriteRenderer>().sharedMaterial, Is.SameAs(customMaterial));
            var live = lights.Planner.Clusters.Where(c => c.Candle && Mathf.Abs(c.LightIntensity - 1.4f) < .001f).ToArray();
            Assert.That(live.Length, Is.EqualTo(1));
            Assert.That(live[0].LightColor, Is.EqualTo(Color.magenta));
            Assert.That(live[0].LightRadius, Is.EqualTo(3.4f));
            Assert.That(live[0].SpillIntensity, Is.EqualTo(.25f));
            previewLayout.SlotOverrides[0].OverrideFields &= ~ChurchArtOverrideFields.Material;
            candle.RefreshResolved();
            Assert.That(candle.GetComponent<SpriteRenderer>().sharedMaterial, Is.SameAs(otherCandleMaterial), "resetting a material slot returns to shared flame material");
            Object.DestroyImmediate(customMaterial); Object.DestroyImmediate(previewLayout);
            view.Dispose(); Object.DestroyImmediate(root);
        }

        [Test]
        public void DirectionalProjectionKeepsPewWidthAndUprightArtUsesSupportedViews()
        {
            ChurchArtPlacement.ProjectFloorSize(4.55f, 1.155f, 0f, ArenaView.GroundSquash,
                out float width0, out float depth0, out float angle0);
            ChurchArtPlacement.ProjectFloorSize(4.55f, 1.155f, 45f, ArenaView.GroundSquash,
                out float width45, out float depth45, out float angle45);
            ChurchArtPlacement.ProjectFloorSize(4.55f, 1.155f, 90f, ArenaView.GroundSquash,
                out float width90, out float depth90, out float angle90);
            Assert.That(width0, Is.EqualTo(4.55f).Within(.001f));
            Assert.That(width45, Is.EqualTo(4.55f).Within(.001f));
            Assert.That(width90, Is.EqualTo(4.55f).Within(.001f), "quarter-turn does not collapse the authored pew width");
            Assert.That(depth0, Is.EqualTo(1.155f * ArenaView.GroundSquash).Within(.001f), "depth pointing down-map foreshortens");
            Assert.That(depth90, Is.EqualTo(1.155f).Within(.001f), "cross-map depth stays full-size");
            Assert.That(depth45, Is.GreaterThan(depth0).And.LessThan(depth90), "projection changes continuously between facings");
            Assert.That(Mathf.Abs(angle45), Is.GreaterThan(1f));
            Assert.That(Mathf.Abs(angle0), Is.LessThan(.001f));
            Assert.That(Mathf.Abs(angle90 + 90f), Is.LessThan(.01f));

            Assert.That(ChurchArtPlacement.ResolveSupportedView(ChurchArtView.Front, 0f, ChurchArtMount.Wall), Is.EqualTo(ChurchArtView.Front));
            Assert.That(ChurchArtPlacement.ResolveSupportedView(ChurchArtView.Front, 90f, ChurchArtMount.Wall), Is.EqualTo(ChurchArtView.Side));
            Assert.That(ChurchArtPlacement.ResolveSupportedView(ChurchArtView.Front, 180f, ChurchArtMount.Wall), Is.EqualTo(ChurchArtView.Back));
            Assert.That(ChurchArtPlacement.ResolveSupportedView(ChurchArtView.Side, 90f, ChurchArtMount.Upright), Is.EqualTo(ChurchArtView.Side));
            Assert.That(ChurchArtPlacement.ResolveSupportedView(ChurchArtView.Front, 37f, ChurchArtMount.Wall), Is.EqualTo(ChurchArtView.Front),
                "supported upright paintings snap to the nearest authored cardinal view and remain screen-flat");
            Assert.That(ChurchArtPlacement.ResolveSupportedView(ChurchArtView.Front, 90f, ChurchArtMount.Floor), Is.EqualTo(ChurchArtView.Front),
                "floor geometry keeps continuous projection rather than switching sprites");
        }

        [Test]
        public void WindowPaneModesAndCandleFlameLayerResolveAsSeparatePaintedParts()
        {
            object site = typeof(ChurchLandmarkEditModeTests).GetMethod("Church", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { 0 });
            var siteType = site.GetType();
            var map = (MapBlob)siteType.GetField("Map").GetValue(site);
            var anchor = (MapAnchor)siteType.GetField("Anchor").GetValue(site);
            var root = new GameObject("church directional art test");
            var view = LandmarkView.Build(root.transform, map, new[] { anchor });
            var layout = ScriptableObject.CreateInstance<ChurchInteriorDefinition>();
            try
            {
                var window = view.Get(0).Root.GetComponentsInChildren<ChurchArtPlacement>(true).First(p => p.SlotId == "window.front.bay0");
                var windowRenderer = window.GetComponent<SpriteRenderer>();
                Sprite full = windowRenderer.sprite;
                Vector3 fullScale = window.transform.localScale;
                layout.SlotOverrides.Add(new ChurchArtSlotOverride { SlotId = window.SlotId,
                    OverrideFields = ChurchArtOverrideFields.PaneMode,
                    Values = new ChurchArtValues { PaneMode = ChurchWindowPaneMode.Jamb } });
                window.Layout = layout; window.RefreshResolved();
                Assert.That(windowRenderer.enabled, Is.True);
                Assert.That(windowRenderer.sprite, Is.SameAs(window.Resolve(anchor).Values.Side));
                Assert.That(Mathf.Abs(window.transform.localScale.x), Is.LessThan(Mathf.Abs(fullScale.x) * .35f));
                layout.SlotOverrides[0].Values = new ChurchArtValues { PaneMode = ChurchWindowPaneMode.Concealed };
                window.RefreshResolved();
                Assert.That(windowRenderer.enabled, Is.False);
                layout.SlotOverrides[0].Values = new ChurchArtValues { PaneMode = ChurchWindowPaneMode.Full };
                window.RefreshResolved();
                Assert.That(windowRenderer.enabled, Is.True);
                Assert.That(windowRenderer.sprite, Is.SameAs(full));

                var candle = view.Get(0).Root.GetComponentsInChildren<ChurchArtPlacement>(true).First(p => p.SlotId == "candle.front.bay0");
                var flame = candle.FlameRenderer;
                Assert.That(flame, Is.Not.Null, "the moving painted main flame is a separate child sprite");
                Assert.That(flame.sprite, Is.SameAs(candle.Resolve(anchor).Values.FlameLayer));
                Vector3 flamePoint = candle.FlameWorldPoint;
                Vector3 spillBefore = flame.transform.position;
                layout.SlotOverrides.Add(new ChurchArtSlotOverride { SlotId = candle.SlotId,
                    OverrideFields = ChurchArtOverrideFields.FlameSocket,
                    Values = new ChurchArtValues { FlameSocket = new Vector2(.72f, .76f) } });
                candle.Layout = layout; candle.RefreshResolved();
                Assert.That(Vector3.Distance(spillBefore, flame.transform.position), Is.GreaterThan(.05f), "visible flame follows the edited socket");
                Assert.That(Vector3.Distance(flamePoint, candle.FlameWorldPoint), Is.GreaterThan(.05f), "spill handle follows the same edited socket");
            }
            finally { view.Dispose(); Object.DestroyImmediate(layout); Object.DestroyImmediate(root); }
        }

        static MapAnchor Anchor(byte front)
        {
            var entrance = new[] { new MapTile(11, 20), new MapTile(12, 20), new MapTile(13, 20) };
            return new MapAnchor(2, MapBlob.AnchorKindChurch, new MapTile(12, 12), new MapRect(0, 0, 25, 25), front, entrance);
        }
    }
}
