# P-STORM-HAUNTED-PRESENTATION-1: Haunted faction display and champion copy

- **Serves:** ORDER_OF_OPERATIONS phase 3, item 16 (P-UI-7 combat presentation) and phase 4, item 21 (P-UI-8 visual upgrade); owner direction for Storm Ink presentation
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `codex/haunted-display`
- **D-rules touched:** D1 presentation-only · D2 n/a · D5/D6/D9 none

## 1. ID and outcome
Show the second faction as **Haunted** in player-facing map, guide, HUD, launch, and opponent-inspector copy. Give all five faction champions short role copy that matches the approved Storm Ink character direction: Fire is a woman burst duelist; Haunted is a man reaper bruiser; Plague is a hooded infection necromancer; Frost is a burly man guardian; Holy is an armored woman paladin. Where the second faction's gilded cosmetic and achievement names appear, show **Gilded Haunted**.

This is a presentation rename. Keep `FactionId.Sentenced`, its numeric/wire index, the `sentenced` parse key, the `gilded-sentenced` cosmetic id, `gold-sentenced` slot, `gild_sentenced` achievement id, all saved profile values, and all gameplay mechanics unchanged. Role copy is display text only.

## 2. Dependencies
None. Owner-approved art packet direction is the source for the names and champion roles.

## 3. Relevant legacy paths
- `crates/rh-sim/src/content.rs:30-61`: `FactionId::Sentenced`, its `as_str` value, and its append-only wire code. These remain stable.
- `client-unity/RoguehallaNet/Net/Ui/MapZones.cs`: region-to-name presentation used by tactical map labels.
- `client-unity/RoguehallaNet/Net/Ui/InspectorCard.cs`: faction name and role copy in opponent inspection.
- `client-unity/UnityProject/Assets/Scripts/UI/LaunchCopy.cs`, `HubGuideView.cs`, `MatchHud.Legacy.cs`, `HelpGuideView.cs`, and `ShowcasePalette.cs`: launch, guide, HUD, and showcase-facing text.
- `tools/account-catalog/export.mts`: generated account presentation files. It reads canonical display records but must not mutate legacy or saved identifiers.

## 4. New paths
- `client-unity/RoguehallaNet/Net/Ui/FactionPresentation.cs`: one engine-free source for the five player-facing faction names and champion role labels.
- `client-unity/RoguehallaNet.Tests/FactionPresentationTests.cs`: complete name/role/gilded display assertions.

## 5. Exclusive write scope
- This packet and its row in `docs/backlog/README.md`.
- `client-unity/RoguehallaNet/Net/Ui/FactionPresentation.cs`, `MapZones.cs`, and `InspectorCard.cs`, their synced Unity copies, and engine-free tests.
- The Unity presentation call sites listed in section 3 and focused EditMode tests.
- `tools/account-catalog/export.mts`, `tools/account-catalog/README.md`, and its generated Unity resource JSON and manifest, plus the account-catalog presentation tests.
- Do not edit `packages/`, Rust faction ids, simulation rules, migrations, profile schemas, account transaction code, or saved-data fixtures.

## 6. Contracts and references
- `FactionId.Sentenced` remains the protocol and gameplay identity; no wire or content hash changes.
- UI presentation aliases must not change a profile key, cosmetic id, slot key, target faction key, achievement id, reward id, or cosmetic code.
- `client-unity/AGENTS.md`: edit the engine-free C# library first and run `bash client-unity/sync-net.sh`; test `sync-net.sh --check` and `dotnet test client-unity/RoguehallaNet.Tests`.
- `tools/account-catalog/README.md`: generated resources are verified with `./tools/account-catalog/export.sh --check`; regenerate only with its documented `--write` command.

## 7. Behaviour to preserve
- Faction numeric order, parsing, wire serialization, tactical map region ids and glyphs.
- Existing Sentenced signature mechanics and action eligibility.
- Existing faction passives, Wishes, item keys, equipped cosmetics, profile ownership, and achievement thresholds/rewards.
- All other faction names, role copy, and account catalog output.

## 8. Acceptance checks
- Tests assert the five exact display names and champion roles in faction order, and verify map names and inspector copy use the shared presentation data.
- Unity EditMode checks cover Haunted on the guide, launch cards, HUD/inspector presentation, accessible faction mark naming, and the role text.
- Generated account resources show `Gilded Haunted` and the Haunted achievement description while retaining the canonical ids and reward target keys.
- Rust `FactionId.Sentenced` serialization/parsing and the legacy `sentenced` profile target are unchanged.
- `scripts/gate.sh --quick --oracle packet/P-STORM-HAUNTED-PRESENTATION-1`, `bash client-unity/sync-net.sh --check`, `dotnet test client-unity/RoguehallaNet.Tests`, `./tools/account-catalog/export.sh --check`, and applicable Unity EditMode tests pass.

## 9. Validation commands
- `dotnet test client-unity/RoguehallaNet.Tests`
- `bash client-unity/sync-net.sh --check`
- `./tools/account-catalog/export.sh --check`
- Unity EditMode batch test run when the Unity Editor is available.
- `scripts/gate.sh --quick --oracle packet/P-STORM-HAUNTED-PRESENTATION-1`

## 10. Risk and suggested model tier
Sonnet. The principal risk is an accidental identity/key rename while changing display text. Prove by comparing the generated catalog's ids, slots, codes, targets, and achievement rewards with the canonical inputs.
