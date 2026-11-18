# P-READ-A4: C# summaries in the Unity project

- **Serves:** owner direction 2026-11-16, the readability sweep ("Yes, both"); standard `docs/agent/READABILITY.md`.
- **Tier:** `implementer-sonnet`. Reviewer: reviewer (Sonnet); comments only.
- **Write scope:** the files named below, `///` comment lines only.
- **Base:** `coord/int-56` @ `a8d46a9f5`.

## What

Add a `/// <summary>` (one or two sentences, and for a MonoBehaviour what it draws or listens to) on the first type of each file named below. Comment lines only. Unity EditMode runs in the quick gate when C# changes; let it.

## How (all P-READ packets)

Follow `docs/agent/READABILITY.md` exactly: what to write, the move-only rules, frozen files, the comparator's
constants, and the proof. This packet changes no behaviour. Its oracle is `scripts/readability-allowlist.txt` with the
lines listed below removed (committed on `packet/P-READ-A4`); the gate's readability step is red until the work is done.
Never edit the allowlist (it is the oracle file) or add lines to it.

- Branch: `agent/sonnet/P-READ-A4` from `packet/P-READ-A4`; first command in your worktree:
  `git checkout -B agent/sonnet/P-READ-A4 packet/P-READ-A4`.
- Iterate with `scripts/gate.sh --quick --base coord/int-56`; finish with
  `scripts/gate.sh --quick --base coord/int-56 --oracle packet/P-READ-A4`. Prefix cargo and gate commands with
  `export PATH="$HOME/.cargo/bin:$PATH"`.
- A red parity, golden, hash, digest, pin or test-expectation step means a change altered behaviour: undo it. Never
  refresh a fixture, pin or expectation in a P-READ packet.
- Hand-off: the AGENTS.md report, plus the `readability: OK (...)` line, and for a split the output of
  `git diff --stat coord/int-56` and every changed line that is not a move (expected: `mod`, `use`, `pub use`, visibility,
  `#[cfg(test)] mod tests;`, comments).

## Allowlist lines this packet removes (its oracle)

```
header client-unity/UnityProject/Assets/Editor/AssetReadinessPreviewMenu.cs
header client-unity/UnityProject/Assets/Editor/FlareCatalogImporter.cs
header client-unity/UnityProject/Assets/Editor/FlareExpansionImporter.cs
header client-unity/UnityProject/Assets/Editor/FlareFullBaseImporter.cs
header client-unity/UnityProject/Assets/Editor/Look/BandOutlineCapture.cs
header client-unity/UnityProject/Assets/Editor/Look/LookCapture.cs
header client-unity/UnityProject/Assets/Editor/RhBuild.cs
header client-unity/UnityProject/Assets/Editor/ShowcaseEnvironmentStudyMenu.cs
header client-unity/UnityProject/Assets/Editor/ShowcaseLiveCatalogBaker.cs
header client-unity/UnityProject/Assets/Editor/ShowcaseSetup.cs
header client-unity/UnityProject/Assets/Scripts/BakedContent.cs
header client-unity/UnityProject/Assets/Scripts/Bootstrap.cs
header client-unity/UnityProject/Assets/Scripts/ContrastLab/ContrastLabPlan.cs
header client-unity/UnityProject/Assets/Scripts/ContrastLab/ContrastLabStage.cs
header client-unity/UnityProject/Assets/Scripts/Net/InputLatencyProbe.cs
header client-unity/UnityProject/Assets/Scripts/Net/LatencyTrace.cs
header client-unity/UnityProject/Assets/Scripts/Net/MatchClient.Accounts.cs
header client-unity/UnityProject/Assets/Scripts/Net/MatchClient.Resume.cs
header client-unity/UnityProject/Assets/Scripts/Net/MatchClient.Settlement.cs
header client-unity/UnityProject/Assets/Scripts/Net/MatchClient.cs
header client-unity/UnityProject/Assets/Scripts/Net/MatchLaunchConfig.cs
header client-unity/UnityProject/Assets/Scripts/Net/NativeTransport.cs
header client-unity/UnityProject/Assets/Scripts/Net/TransportFactory.cs
header client-unity/UnityProject/Assets/Scripts/Net/WebGLTransport.cs
header client-unity/UnityProject/Assets/Scripts/Presentation/ActorOutlineModel.cs
header client-unity/UnityProject/Assets/Scripts/Presentation/ActorShadowStrip.cs
header client-unity/UnityProject/Assets/Scripts/Presentation/FrostWallPresenter.cs
header client-unity/UnityProject/Assets/Scripts/Presentation/GroundMarkArt.cs
header client-unity/UnityProject/Assets/Scripts/Presentation/HitZonePresenter.cs
header client-unity/UnityProject/Assets/Scripts/Presentation/HitboxOverlay.cs
header client-unity/UnityProject/Assets/Scripts/Presentation/IndicatorArt.cs
header client-unity/UnityProject/Assets/Scripts/Presentation/LegacyFacts.cs
header client-unity/UnityProject/Assets/Scripts/Presentation/OverheadExtras.cs
header client-unity/UnityProject/Assets/Scripts/Presentation/OverheadIndicators.cs
header client-unity/UnityProject/Assets/Scripts/Presentation/QualityModel.cs
header client-unity/UnityProject/Assets/Scripts/Presentation/ShowcaseAnimationPreview.cs
header client-unity/UnityProject/Assets/Scripts/Presentation/ShowcaseBiomeArt.cs
header client-unity/UnityProject/Assets/Scripts/Presentation/ShowcaseCatalog.cs
header client-unity/UnityProject/Assets/Scripts/Presentation/WeaponRangePresenter.cs
header client-unity/UnityProject/Assets/Scripts/Presentation/WorldIndicators.cs
header client-unity/UnityProject/Assets/Scripts/Presentation/WorldMarks.cs
header client-unity/UnityProject/Assets/Scripts/UI/CardDisplayCatalog.cs
header client-unity/UnityProject/Assets/Scripts/UI/HelpGuideView.cs
header client-unity/UnityProject/Assets/Scripts/UI/HubCosmeticsView.cs
header client-unity/UnityProject/Assets/Scripts/UI/HubProgressView.cs
header client-unity/UnityProject/Assets/Scripts/UI/HudActionGlyphs.cs
header client-unity/UnityProject/Assets/Scripts/UI/HudUi.cs
header client-unity/UnityProject/Assets/Scripts/UI/HurtVignette.cs
header client-unity/UnityProject/Assets/Scripts/UI/LaunchIcons.cs
header client-unity/UnityProject/Assets/Scripts/UI/MapGlyphs.cs
header client-unity/UnityProject/Assets/Scripts/UI/MatchHud.Accounts.cs
header client-unity/UnityProject/Assets/Scripts/UI/MatchHud.EdgeThreats.cs
header client-unity/UnityProject/Assets/Scripts/UI/MatchHud.Features.cs
header client-unity/UnityProject/Assets/Scripts/UI/MatchHud.IconPresentation.cs
header client-unity/UnityProject/Assets/Scripts/UI/MatchHud.PlayerFlows.cs
header client-unity/UnityProject/Assets/Scripts/UI/MatchHud.Resume.cs
header client-unity/UnityProject/Assets/Scripts/UI/MatchHud.Settlement.cs
header client-unity/UnityProject/Assets/Scripts/UI/MatchHud.Training.cs
header client-unity/UnityProject/Assets/Scripts/UI/MythicUi.cs
header client-unity/UnityProject/Assets/Scripts/UI/TrainingConsoleSelectors.cs
header client-unity/UnityProject/Assets/Scripts/UI/UiFocusNavigation.cs
header client-unity/UnityProject/Assets/Scripts/UI/UiTheme.cs
header client-unity/UnityProject/Assets/Scripts/UiLab/UiLabStage.cs
header client-unity/UnityProject/Assets/Scripts/View/ArenaView.cs
header client-unity/UnityProject/Assets/Scripts/View/Atmosphere/AtmospherePreviewCapture.cs
header client-unity/UnityProject/Assets/Scripts/View/Atmosphere/ParticlePresets.cs
header client-unity/UnityProject/Assets/Scripts/View/MeshUtil.cs
header client-unity/UnityProject/Assets/Scripts/View/Regions/ColiseumProps.cs
header client-unity/UnityProject/Assets/Scripts/View/Regions/PlagueGround.cs
header client-unity/UnityProject/Assets/Scripts/View/Regions/RegionCanvas.cs
header client-unity/UnityProject/Assets/Scripts/View/Regions/RegionGround.cs
header client-unity/UnityProject/Assets/Scripts/View/Regions/RegionGroundTextures.cs
header client-unity/UnityProject/Assets/Scripts/View/Regions/RegionMap.cs
header client-unity/UnityProject/Assets/Scripts/View/Regions/RegionNoise.cs
header client-unity/UnityProject/Assets/Scripts/View/Regions/RegionPocket.cs
header client-unity/UnityProject/Assets/Scripts/View/Regions/RegionPreviewCapture.cs
header client-unity/UnityProject/Assets/Scripts/View/Regions/RegionPropArt.Temple.cs
header client-unity/UnityProject/Assets/Scripts/View/Regions/RegionPropArt.cs
header client-unity/UnityProject/Assets/Scripts/View/Regions/RegionProps.cs
header client-unity/UnityProject/Assets/Scripts/View/Regions/RegionThemes.cs
header client-unity/UnityProject/Assets/Scripts/View/Regions/RegionWallAtlas.cs
header client-unity/UnityProject/Assets/Scripts/View/Regions/RegionWalls.cs
header client-unity/UnityProject/Assets/Scripts/View/Regions/SurfaceMap.cs
header client-unity/UnityProject/Assets/Scripts/View/Regions/TempleLayout.cs
header client-unity/UnityProject/Assets/Scripts/View/Regions/TempleStyle.cs
header client-unity/UnityProject/Assets/Scripts/View/VisionFog.cs
```
