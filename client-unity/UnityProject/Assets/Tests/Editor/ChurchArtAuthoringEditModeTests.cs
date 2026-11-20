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
            Assert.That(Resources.Load<ChurchArtDefinition>("StormInk/Church/Definitions/OakPew").Defaults.Depth, Is.Positive);
            Assert.That(Resources.Load<ChurchArtDefinition>("StormInk/Church/Definitions/Candelabrum").EmitsCandleLight, Is.True);
            var pulpit = Resources.Load<ChurchArtDefinition>("StormInk/Church/Definitions/GothicPulpit").Defaults;
            Assert.That(pulpit.Front, Is.Not.SameAs(pulpit.Side), "approach views are separately authored sprites");
            Assert.That(pulpit.Side, Is.Not.SameAs(pulpit.Back));
            var lancet = Resources.Load<ChurchArtDefinition>("StormInk/Church/Definitions/GarnetLancet").Defaults;
            Assert.That(lancet.Front, Is.Not.SameAs(lancet.Side), "side jamb has no glass panes");
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
                    Assert.That(pulpit.GetComponent<SpriteRenderer>().flipX, Is.EqualTo(ChurchArtOrientation.Resolve(rotatedAnchor).Mirrored),
                        "pulpit stairs mirror with the resolved handed approach");
                    var pulpitValues = pulpit.Resolve(rotatedAnchor).Values;
                    var pulpitViewSprite = pulpit.ResolvedView == ChurchArtView.Side ? pulpitValues.Side :
                        pulpit.ResolvedView == ChurchArtView.Back ? pulpitValues.Back : pulpitValues.Front;
                    Assert.That(pulpit.GetComponent<SpriteRenderer>().sprite, Is.SameAs(pulpitViewSprite), "pulpit uses its distinct approach painting");
                    var candle = placements.First(p => p.SlotId == "candle.front.bay0");
                    var candleProbe = ScriptableObject.CreateInstance<ChurchInteriorDefinition>();
                    candleProbe.SlotOverrides.Add(new ChurchArtSlotOverride { SlotId = candle.SlotId,
                        OverrideFields = ChurchArtOverrideFields.Contact | ChurchArtOverrideFields.FlameSocket,
                        Values = new ChurchArtValues { Contact = new Vector2(.42f, .36f), FlameSocket = new Vector2(.72f, .76f) } });
                    Vector3 contactInRoot = view.Get(0).Root.transform.InverseTransformPoint(candle.ContactPoint);
                    Vector2 contactTile = ChurchArtOrientation.LocalToTile(new Vector2(contactInRoot.x, contactInRoot.y));
                    foreach (bool mirrored in new[] { false, true })
                    {
                        candle.GetComponent<SpriteRenderer>().flipX = false;
                        candle.Layout = candleProbe;
                        candle.Configure(view.Get(0), rotatedAnchor, contactTile, 0f, mirrored);
                        Assert.That(candle.Resolve(rotatedAnchor).Mirrored, Is.EqualTo(mirrored));
                        Assert.That(Vector2.Distance(candle.FlameWickPoint, candle.FlameRenderer.transform.position), Is.LessThan(.001f),
                            "off-centre wick and separate flame layer share their reflected anchor");
                        var wickUv = candle.WorldToUv(candle.FlameWickPoint);
                        Assert.That(wickUv.x, Is.EqualTo(.72f).Within(.002f), "off-centre wick UV, reflection " + mirrored + ", orientation " + k);
                        Assert.That(wickUv.y, Is.EqualTo(.76f).Within(.002f), "off-centre wick V, reflection " + mirrored + ", orientation " + k);
                        var flameSprite = candle.FlameRenderer.sprite;
                        float flamePivotY = flameSprite.pivot.y / flameSprite.rect.height;
                        Vector3 flameCentre = candle.FlameRenderer.transform.TransformPoint(new Vector3(0f,
                            (.5f - flamePivotY) * flameSprite.bounds.size.y, 0f));
                        Assert.That(Vector2.Distance(flameCentre, candle.FlameWorldPoint), Is.LessThan(.001f),
                            "emission centre matches the separate flame sprite in orientation " + k + " reflection " + mirrored);
                    }
                    candle.Layout = layouts;
                    candle.GetComponent<SpriteRenderer>().flipX = false;
                    candle.Configure(view.Get(0), rotatedAnchor, contactTile, 0f);
                    candle.Layout = layouts; candle.RefreshResolved();
                    Object.DestroyImmediate(candleProbe);
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
                    var diagonalLayout = ScriptableObject.CreateInstance<ChurchInteriorDefinition>();
                    diagonalLayout.SlotOverrides.Add(new ChurchArtSlotOverride { SlotId = pew.SlotId,
                        OverrideFields = ChurchArtOverrideFields.RotationDegrees | ChurchArtOverrideFields.Width | ChurchArtOverrideFields.Depth,
                        Values = new ChurchArtValues { RotationDegrees = 37f, Width = 4.55f, Depth = 1.155f } });
                    pew.Layout = diagonalLayout; pew.RefreshResolved();
                    var pewSprite = pew.GetComponent<SpriteRenderer>().sprite;
                    Vector3 widthAxis = pew.transform.TransformVector(Vector3.right * pewSprite.bounds.size.x);
                    Vector3 depthAxis = pew.transform.TransformVector(Vector3.up * pewSprite.bounds.size.y);
                    float radians = -pew.transform.localEulerAngles.z * Mathf.Deg2Rad;
                    var expectedWidth = new Vector3(4.55f * Mathf.Cos(radians), -4.55f * ArenaView.GroundSquash * Mathf.Sin(radians), 0f);
                    var expectedDepth = new Vector3(1.155f * Mathf.Sin(radians), 1.155f * ArenaView.GroundSquash * Mathf.Cos(radians), 0f);
                    Assert.That(Vector3.Distance(widthAxis, expectedWidth), Is.LessThan(.002f), "width endpoint uses diag(1,squash) * R");
                    Assert.That(Vector3.Distance(depthAxis, expectedDepth), Is.LessThan(.002f), "depth endpoint uses diag(1,squash) * R");
                    Assert.That(Mathf.Abs(Vector3.Dot(widthAxis.normalized, depthAxis.normalized)), Is.GreaterThan(.01f), "diagonal axes shear in projection");
                    var planeContact = pew.transform.parent.InverseTransformPoint(pew.ContactPoint);
                    float originalTileY = -planeContact.y;
                    var localRowOffset = ChurchArtOrientation.Resolve(rotatedAnchor).LocalToMapOffset(new Vector2(0f, 4f));
                    int rowDelta = Mathf.FloorToInt(originalTileY + localRowOffset.y) - Mathf.FloorToInt(originalTileY);
                    int rowSort = pew.GetComponent<SpriteRenderer>().sortingOrder;
                    diagonalLayout.SlotOverrides[0].OverrideFields |= ChurchArtOverrideFields.LocalOffset | ChurchArtOverrideFields.MountHeight;
                    diagonalLayout.SlotOverrides[0].Values = new ChurchArtValues { RotationDegrees = 37f, Width = 4.55f, Depth = 1.155f,
                        LocalOffset = new Vector2(0f, 4f), MountHeight = 0.75f };
                    pew.RefreshResolved();
                    Assert.That(pew.GetComponent<SpriteRenderer>().sortingOrder, Is.EqualTo(rowSort + rowDelta * 16),
                        "crossing ground rows updates art sorting by the established row step");
                    diagonalLayout.SlotOverrides[0].OverrideFields = ChurchArtOverrideFields.RotationDegrees | ChurchArtOverrideFields.MountHeight;
                    diagonalLayout.SlotOverrides[0].Values = new ChurchArtValues { RotationDegrees = 37f, MountHeight = 0.75f };
                    pew.RefreshResolved();
                    Assert.That(pew.GetComponent<SpriteRenderer>().sortingOrder, Is.EqualTo(rowSort), "height-only edit keeps its ground row and reset is idempotent");
                    Object.DestroyImmediate(diagonalLayout);
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
                    ChurchArtOverrideFields.SpillRadius | ChurchArtOverrideFields.SpillIntensity | ChurchArtOverrideFields.Material |
                    ChurchArtOverrideFields.FlameSocket | ChurchArtOverrideFields.Contact,
                Values = new ChurchArtValues { FlameColor = Color.magenta, FlameIntensity = 1.4f, SpillRadius = 3.4f,
                    SpillIntensity = .25f, Material = customMaterial, Contact = new Vector2(.42f, .36f), FlameSocket = new Vector2(.72f, .76f) }
            });
            candle.Layout = previewLayout;
            candle.RefreshResolved();
            Assert.That(candle.GetComponent<SpriteRenderer>().sharedMaterial, Is.SameAs(customMaterial));
            var live = lights.Planner.Clusters.Where(c => c.Candle && Mathf.Abs(c.LightIntensity - 1.4f) < .001f).ToArray();
            Assert.That(live.Length, Is.EqualTo(1));
            Assert.That(live[0].LightColor, Is.EqualTo(Color.magenta));
            Assert.That(live[0].LightRadius, Is.EqualTo(3.4f));
            Assert.That(live[0].SpillIntensity, Is.EqualTo(.25f));
            var wick = candle.FlameWickPoint;
            Assert.That(Vector2.Distance(wick, candle.FlameRenderer.transform.position), Is.LessThan(.001f), "flame layer base follows the mirrored wick socket");
            var wickUv = candle.WorldToUv(wick);
            Assert.That(wickUv.x, Is.EqualTo(.72f).Within(.002f));
            Assert.That(wickUv.y, Is.EqualTo(.76f).Within(.002f));
            var flameSprite = candle.FlameRenderer.sprite;
            float flamePivotY = flameSprite.pivot.y / flameSprite.rect.height;
            Vector3 flameCentre = candle.FlameRenderer.transform.TransformPoint(new Vector3(0f,
                (.5f - flamePivotY) * flameSprite.bounds.size.y, 0f));
            Assert.That(Vector2.Distance(flameCentre, candle.FlameWorldPoint), Is.LessThan(.001f), "spill centre matches the separate painted flame centre");
            var rootTransform = view.Get(0).Root.transform;
            var rootLocalLight = rootTransform.InverseTransformPoint(candle.FlameWorldPoint);
            float expectedGroundTileY = candle.GroundTile.y;
            Assert.That(live[0].X, Is.EqualTo(rootLocalLight.x).Within(.002f), "pooled spill X follows the actual reflected emission point");
            Assert.That(live[0].Y, Is.EqualTo(expectedGroundTileY).Within(.002f), "pooled spill stays on its ground row, separate from elevation");
            Vector3 reconstructedEmitter = ArenaView.WorldPoint(live[0].X * 32f, live[0].Y * 32f) + Vector3.up * live[0].Lift;
            Assert.That(Vector2.Distance(reconstructedEmitter, candle.FlameWorldPoint), Is.LessThan(.001f),
                "AccentLights reconstruction matches the actual flame center without double elevation");
            previewLayout.SlotOverrides[0].OverrideFields &= ~ChurchArtOverrideFields.Material;
            candle.RefreshResolved();
            Assert.That(candle.GetComponent<SpriteRenderer>().sharedMaterial, Is.SameAs(otherCandleMaterial), "resetting a material slot returns to shared flame material");
            Object.DestroyImmediate(customMaterial); Object.DestroyImmediate(previewLayout);
            view.Dispose(); Object.DestroyImmediate(root);
        }

        [Test]
        public void DirectionalProjectionProjectsPewAxesAndUprightArtUsesSupportedViews()
        {
            ChurchArtPlacement.ProjectFloorSize(4.55f, 1.155f, 0f, ArenaView.GroundSquash,
                out float width0, out float depth0, out float angle0);
            ChurchArtPlacement.ProjectFloorSize(4.55f, 1.155f, 45f, ArenaView.GroundSquash,
                out float width45, out float depth45, out float angle45);
            ChurchArtPlacement.ProjectFloorSize(4.55f, 1.155f, 90f, ArenaView.GroundSquash,
                out float width90, out float depth90, out float angle90);
            Assert.That(width0, Is.EqualTo(4.55f).Within(.001f));
            Assert.That(width45, Is.EqualTo(4.55f * Mathf.Sqrt(.5f + .5f * ArenaView.GroundSquash * ArenaView.GroundSquash)).Within(.001f));
            Assert.That(width90, Is.EqualTo(4.55f * ArenaView.GroundSquash).Within(.001f), "a quarter-turn projects width down-map");
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
            Assert.That(ChurchArtPlacement.ResolvePaneMode(ChurchWindowPaneMode.Auto, ChurchArtView.Front, true), Is.EqualTo(ChurchWindowPaneMode.Full));
            Assert.That(ChurchArtPlacement.ResolvePaneMode(ChurchWindowPaneMode.Auto, ChurchArtView.Side, true), Is.EqualTo(ChurchWindowPaneMode.Jamb));
            Assert.That(ChurchArtPlacement.ResolvePaneMode(ChurchWindowPaneMode.Auto, ChurchArtView.Back, true), Is.EqualTo(ChurchWindowPaneMode.Concealed));
            Assert.That(ChurchArtPlacement.ResolvePaneMode(ChurchWindowPaneMode.Auto, ChurchArtView.Side, false), Is.EqualTo(ChurchWindowPaneMode.Concealed));
            Assert.That(ChurchArtPlacement.ResolvePaneMode(ChurchWindowPaneMode.Full, ChurchArtView.Side, true), Is.EqualTo(ChurchWindowPaneMode.Full),
                "an explicit slot override can request the full pane edge-on");
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
                Sprite full = window.Resolve(anchor).Values.Front;
                Assert.That(window.ResolvedPaneMode, Is.EqualTo(ChurchArtPlacement.ResolvePaneMode(ChurchWindowPaneMode.Auto, window.ResolvedView, true)));
                layout.SlotOverrides.Add(new ChurchArtSlotOverride { SlotId = window.SlotId,
                    OverrideFields = ChurchArtOverrideFields.PaneMode,
                    Values = new ChurchArtValues { PaneMode = ChurchWindowPaneMode.Full } });
                window.Layout = layout; window.RefreshResolved();
                Vector3 fullScale = window.transform.localScale;
                layout.SlotOverrides[0].Values = new ChurchArtValues { PaneMode = ChurchWindowPaneMode.Jamb };
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

        [Test]
        public void WindowVisibilityFollowsAbsoluteCameraFaceWhenNamedFrontAndRearSwap()
        {
            bool northWallIsNamedFront = false, northWallIsNamedRear = false;
            foreach (int k in new[] { 0, 4 })
            {
                object site = typeof(ChurchLandmarkEditModeTests).GetMethod("Church", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { k });
                var type = site.GetType();
                var map = (MapBlob)type.GetField("Map").GetValue(site);
                var anchor = (MapAnchor)type.GetField("Anchor").GetValue(site);
                var root = new GameObject("church absolute window face test");
                var view = LandmarkView.Build(root.transform, map, new[] { anchor });
                try
                {
                    var placements = view.Get(0).Root.GetComponentsInChildren<ChurchArtPlacement>(true);
                    var namedFront = placements.First(p => p.SlotId == "window.front.bay0");
                    var namedRear = placements.First(p => p.SlotId == "window.rear.bay0");
                    float frontY = -view.Get(0).Root.transform.InverseTransformPoint(namedFront.ContactPoint).y / ArenaView.GroundSquash;
                    float rearY = -view.Get(0).Root.transform.InverseTransformPoint(namedRear.ContactPoint).y / ArenaView.GroundSquash;
                    Assert.That(Mathf.Abs(frontY - rearY), Is.GreaterThan(4f), "the two named ends occupy distinct signed map rows");
                    var north = frontY < rearY ? namedFront : namedRear;
                    var south = frontY < rearY ? namedRear : namedFront;
                    Assert.That(north.ResolvedView, Is.EqualTo(ChurchArtView.Front), "the physical min-Y wall faces the fixed camera");
                    Assert.That(north.ResolvedPaneMode, Is.EqualTo(ChurchWindowPaneMode.Full));
                    Assert.That(south.ResolvedView, Is.EqualTo(ChurchArtView.Back), "the physical max-Y wall resolves its back face");
                    Assert.That(south.ResolvedPaneMode, Is.EqualTo(ChurchWindowPaneMode.Concealed));
                    if (frontY < rearY) northWallIsNamedFront = true; else northWallIsNamedRear = true;
                }
                finally { view.Dispose(); Object.DestroyImmediate(root); }
            }
            Assert.That(northWallIsNamedFront && northWallIsNamedRear, Is.True, "north camera-facing wall remains the same when graveyard front/rear labels swap");
        }

        static MapAnchor Anchor(byte front)
        {
            var entrance = new[] { new MapTile(11, 20), new MapTile(12, 20), new MapTile(13, 20) };
            return new MapAnchor(2, MapBlob.AnchorKindChurch, new MapTile(12, 12), new MapRect(0, 0, 25, 25), front, entrance);
        }
    }
}
