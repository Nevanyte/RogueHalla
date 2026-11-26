# P-UI-ALPHA-MAP-FIT-1: Fit large Tab-map text

- **Serves:** ORDER_OF_OPERATIONS Phase 3, item 16; open-alpha scope U2.
- **Implementer tier:** implementer-sonnet.
- **Reviewer:** reviewer.
- **Integration base:** open-alpha candidate at `968126954`.
- **D-rules touched:** D1 presentation regression using real Unity label bounds; D2 n/a; D5/D6/D9 n/a.

## 1. ID and outcome

At normal and 1.5× text sizes, the Tab map key's labels have separate space and zone names fit their boxes when the map is reopened after resizing. The base oracle in `CampKeyOneSizeEditModeTests.cs:312` documents the MAP KEY/You and Reward chest/Route stop collisions and excludes the key's rows from its 1.5× overlap assertion.

## 2. Dependencies

The candidate includes P-UI-TABMAP-TEXT-2-fix and P-UI-GUARDS-2.

## 3. Relevant legacy paths

- `packages/client/src/settings.ts:30` offers 100%, 125% and 150% type sizes.
- `packages/client/src/ui/planning.ts:503` applies the scale to the Phaser map. The Unity-specific fit boxes have no direct Phaser counterpart.

## 4. New paths

- This packet brief.

## 5. Exclusive write scope

- `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapController.cs`
- `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapMarks.ZoneLabels.cs`

## 6. Contracts and references

R-AUTH-2 and D9. Changes apply to type fit in the existing uGUI map. The larger text-fit inventory and unrelated HUD changes lie outside this packet.

## 7. Behaviour to preserve

The compact layout still hides the key heading and preserves IN VIEW. All nine fixed camp cells still share one size. Keep the default text layout, row order, icons, phrasing and the existing zone-label placement rules.

## 8. Acceptance checks

- All visible key labels use separate rendered space at 1.0× and 1.5× on 1280×720 and 1920×1080, including after resizing while the map is closed.
- Visible zone names fit their label boxes at both text sizes before and after the same resize and reopen.
- Web screenshots cover the stated sizes and confirm both the compact and large-text map.
- The earlier heading visibility, shared camp-size and zone placement tests pass.

## Oracle files (read-only for implementers)

- `client-unity/UnityProject/Assets/Tests/Editor/CampKeyOneSizeEditModeTests.cs`

## 9. Validation commands

- `scripts/gate.sh --quick --base codex/open-alpha-candidate-2026-11-24` on the packet branch, with the expected Unity assertions confirmed from the results.
- `scripts/gate.sh --quick --base codex/open-alpha-candidate-2026-11-24 --oracle packet/P-UI-ALPHA-MAP-FIT-1` on the implementation branch.
- `scripts/gate.sh --base codex/open-alpha-candidate-2026-11-24 --oracle packet/P-UI-ALPHA-MAP-FIT-1` for the full gate.
- `docs/unity/WEB_ROUND.md` for visual proof.

## 10. Risk and suggested model tier

Sonnet-tier Unity UI work. TextMeshPro can report undersized metrics before a label has been enabled, while the map creates and updates labels when its panel is closed.

## 11. Known traps

Measure rendered bounds only after opening the map and advancing canvas layout. Check the label's actual fit box for zone text, whose initial full-layer rectangle is replaced after creation. Keep the camp cells' shared sizing and existing layout breakpoints.
