# P-STORM-HUB-1: Storm Ink guild hall UI

- **Serves:** not in ORDER_OF_OPERATIONS; owner approval supplied by the 2026-11-19 task instruction
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `feature/unity-rework` (coordinator record); implementation branch `codex/storm-hub` as requested by the owner
- **D-rules touched:** D1 n/a · D2 n/a · D5/D6/D9 none; presentation only

## 1. ID and outcome

Use the original `StormInk/Hub/GuildHall` Sprite on the actual walkable `HubAtriumView` floor, soften broad lobby and shop surfaces so the painted floor can read through, add five faction portraits to the field guide, and show illustrated previews for the ten gilded faction/weapon entries in the live account cosmetics catalog. Preserve station geometry, labels, action rules, account gates, focus, disabled states and text. The UI owns no gameplay or account outcome.

## 2. Dependencies

The guide uses five square portraits in wire order. The shop atlas has five faction previews across the top row and sword, axe, bow, wand and shield across the bottom row. HubArtworkContract defines the resource keys and cell dimensions.

## 3. Relevant legacy paths

- `packages/client/src/hub/hubLayout.ts:1-47, 94-100`: walkable room, stations, and kiosk layout as presentation data.

## 4. New paths

- `client-unity/UnityProject/Assets/Scripts/UI/HubArtworkContract.cs`: engine-free resource keys, faction order and catalog-to-cell mapping.
- `client-unity/UnityProject/Assets/Scripts/UI/HubArtworkAssets.cs`: resource loading and runtime atlas slicing.
- `client-unity/UnityProject/Assets/Tests/Editor/StormHubArtworkEditModeTests.cs`: Unity UI rendering oracle for floor, guide portraits, shop previews and raycast safety.
- `client-unity/RoguehallaNet.Tests/StormHubArtworkContractTests.cs`: dotnet oracle against the checked-in account catalog.

## 5. Exclusive write scope

- `client-unity/UnityProject/Assets/Scripts/UI/HubArtworkContract.cs`
- `client-unity/UnityProject/Assets/Scripts/UI/HubArtworkAssets.cs`
- `client-unity/UnityProject/Assets/Scripts/UI/HubAtriumView.cs`
- `client-unity/UnityProject/Assets/Scripts/UI/LaunchScreenView.cs`
- `client-unity/UnityProject/Assets/Scripts/UI/HubGuideView.cs`
- `client-unity/UnityProject/Assets/Scripts/UI/HubCosmeticsView.cs`
- `client-unity/UnityProject/Assets/Scripts/UI/HubCosmeticsView.Cards.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/HubAtriumViewEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/HubGuideViewEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/StormHubArtworkEditModeTests.cs`
- `client-unity/RoguehallaNet.Tests/RoguehallaNet.Tests.csproj`
- `client-unity/RoguehallaNet.Tests/StormHubArtworkContractTests.cs`
- Unity `.meta` files paired with the new C# source and Editor test
- the three original painted hub resources and matching import metadata under `Assets/Resources/StormInk/Hub`
- this packet file

Account behaviour, network data and item IDs stay under their existing owners.

## 6. Contracts and references

- `docs/REQUIREMENTS.md` R-AUTH-2 and R-VIS-2: Unity remains presentation-only and preserves readability of gameplay geometry.
- `docs/CONTRACTS.md` Unity API rule and presentation/input boundary.
- `docs/adr/0008-unity-client.md`: Unity UI and presentation remain separate from authoritative rules.
- `client-unity/AGENTS.md`: Unity runtime UI lives in `UnityProject`; account actions remain server-owned.

## 7. Behaviour to preserve

- Keep the room size, station locations, solid obstacles, walkable floor bounds, walk speed, reach, pointer walking and direct station buttons unchanged.
- Keep all station labels and hints, keyboard/click behavior, faction selection forwarding, availability gates, focus paths and disabled semantics.
- Keep guide title/copy and shop catalog data/actions unchanged. Portraits and item images are decorative (`raycastTarget = false`); visible text retains the product identity.
- Use the live catalog's ten `gold-*` entries in the asserted order. Other cosmetics keep their current color/text preview.
- No change to champion artwork outside the guide faction portrait illustration, to mechanics, network, account mutation or protocol.

## 8. Acceptance checks

- The dotnet catalog oracle pins the ten actual `gold-*` product IDs to the row-major atlas cells and the five faction names to portrait-strip cells.
- Unity EditMode tests confirm `HubAtriumView` uses the loaded GuildHall Sprite, its floor still receives pointer input, guide portraits map in Fire/Sentenced/Plague/Frost/Holy order, and each gilded product card gets the matching preview without intercepting clicks.
- Existing `HubAtriumViewEditModeTests`, `HubGuideViewEditModeTests`, `HubCosmeticsViewEditModeTests`, `LaunchScreenEditModeTests`, focus/navigation, account gate and disabled-state expectations remain green.
- Review the source diff and compile the engine-free artwork contract with dotnet. Unity is not started in this packet run; the root walkthrough owns the live visual check.

## Oracle files (read-only for implementers)

- `client-unity/RoguehallaNet.Tests/StormHubArtworkContractTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/StormHubArtworkEditModeTests.cs`

## 9. Validation commands

- `dotnet test client-unity/RoguehallaNet.Tests/RoguehallaNet.Tests.csproj --filter FullyQualifiedName~StormHubArtworkContractTests`
- Unity Test Framework EditMode: `Roguehalla.Client.EditModeTests` (not run by the UI implementer; root walkthrough).
- `git diff --check`

## 10. Risk and suggested model tier

The main risk is accidentally changing focus, walk/click behavior or catalog semantics while adjusting surface alpha and adding decorative art. Keep the work in existing view construction paths and prove catalog order separately from Unity slicing. Suggested tier: implementer-sonnet.

## 11. Known traps

- The room floor's Image also participates in pointer targeting; the painting and overlays must leave it raycastable.
- A Sprite atlas loaded as a Texture2D must match the exact cell dimensions and top-row ordering in the scratch art contract; Unity texture pixel coordinates start at the bottom-left.
- Button ColorBlocks can replace the initial Image color; soften normal, hover, selected, pressed and disabled states while keeping button targets and disabled appearance intact.
- Unity resources and their `.meta` files belong to the root art lane and are excluded from this packet's write scope even if they appear in the shared worktree. Resource-backed EditMode assertions require Unity to import the root-owned assets.
