# P-ALPHA-RAID-SCENE-LAYOUT-1: preserve measured RaidBot decision scenes

- **Serves:** ORDER_OF_OPERATIONS M-full-loop bot qualification after the spread quest giver layout.
- **Implementer tier:** implementer-opus
- **Reviewer:** reviewer-opus
- **Integration base:** `a53ff11b8`
- **D-rules touched:** D1 loose proof; D2 existing gate; D5 bot qualification; D6/D9 none.

## 1. ID and outcome
Pin the fixed-map, fixed-seed RaidBot decision scenes to the legacy Top 25 quest board placement they were measured against. The normal live world and ordinary-room qualification continue to use the spread placement.

## 2. Dependencies
P-RAID-Q3b and P-ALPHA-QUEST-BOARD-CONFIG-1.

## 3. Relevant legacy paths
The scene is a native policy oracle with hard-coded seed and geometry premises. It does not modify `packages/`.

## 4. New paths
This packet brief.

## 5. Exclusive write scope
`crates/rh-policy/tests/raid_decide_b.rs`, `crates/rh-policy/tests/raid_decide_fix.rs`, and this packet.

## 6. Contracts and references
C-POLICY and C-OBS: the bot still acts on its own observation through validated commands. This correction only chooses the test fixture's board geometry.

## 7. Behaviour to preserve
Every guard threshold, losing-fight assertion, quest payment assertion, seed list and production policy remains unchanged. Live content defaults to spread placement.

## 8. Acceptance checks
The Trade Up losing-fight guard reaches at least five of six measured scenes and at least four leave; all tests in `raid_decide_b` pass. The out-of-reach token scene in `raid_decide_fix` keeps its pending choice or resolves it to a paid Letters attempt; it still requires late movement. Adjacent RaidBot decision tests remain green.

## 9. Validation commands
`cargo test --locked --release -p rh-policy --test raid_decide_b --test raid_decide --test raid_decide_fix`; `cargo clippy --locked -p rh-policy --all-targets -- -D warnings`; coordinator's full gate.

## 10. Risk and suggested model tier
The historical scene does not measure spread giver geometry. The ordinary-room RaidBot qualification covers that production layout. Implementer-opus, reviewer-opus.

## 11. Known traps
With spread layout, only two of the six listed Trade Up seeds reach the measured losing-fight handover. Those two leave by 88 units, so the red test is a failed scene premise, not evidence of failed retreat. In the out-of-reach token scene, the current bot can return and pay Letters: the old `still_pending` assertion rejects that successful route. The replacement checks the final authoritative quest attempt for payment; it never treats a vanished, unpaid choice as success.
