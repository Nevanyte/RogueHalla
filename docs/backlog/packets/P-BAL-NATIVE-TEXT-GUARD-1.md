# P-BAL-NATIVE-TEXT-GUARD-1: a guard that fails when a number in a native card's text disagrees with the Rust table

- **Serves:** not in the plan: owner approval 2026-11-22 (balance patch; `docs/OWNER_DECISIONS.md` row "Balance patch, test
  bot, per-elite numbers"; plan of record `docs/backlog/BALANCE_LAB_PATCH.md` section 3 "Augments ... Added guard" and the
  "beside" row of the packet table; backlog row `docs/backlog/README.md` section 15)
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `feature/unity-rework` @ `c0ef232a1`
- **D-rules touched:** D1 loose proof (owner direction 2026-11-10) · D2 n/a · D5/D6/D9 none (test only: no card number, no
  card text, no content hash, no wire, no hash or checkpoint change)

## 1. ID and outcome

The 14 native augments' numbers live in Rust (`crates/rh-sim/src/constants.rs:607-669`, the `REWORK_*` rows; caps wired in
`crates/rh-sim/src/cards/native/table.rs:123-170`) and the text a player reads lives in the Unity client
(`client-unity/UnityProject/Assets/Scripts/UI/CardDisplayCatalog.cs:94-109`, the hand-kept `Natives` array; the generated
`Entries` array above it is only the 80 legacy cards). Nothing checks that a number written in the text matches the table, so
P-BAL-NUM-AUG-1 (retunes) could leave a stale text with every gate green.

Outcome: a Rust integration test, run by `scripts/gate.sh --quick` with no Unity, that reads `CardDisplayCatalog.cs` as text
and fails when a number in a native card's description differs from the number the Rust constants give, or when a native card
is missing from the text. Cheap enough for every retune packet.

## 2. Dependencies

None. P-BAL-NUM-AUG-1 runs this guard after it lands.

## 3. Relevant legacy paths

No legacy counterpart: the 14 natives are Rework-only (`docs/agent/AUGMENT_REWORK.md`); nothing under `packages/` is read.
Facts established on `c0ef232a1`:

- The Natives text in C# is byte-identical to `CardDef.text` in `table.rs:35-115` today (read side by side). Nothing tests that
  either. `crates/rh-sim/src/cards/rework_content_tests.rs:7` part 3 pins the override text (the 18 Rework-changed legacy cards)
  to the catalog's exact string; it does not touch ids 80-93. `rework_overrides.rs` holds only legacy-card rows, none of the 14
  natives, so there is no second source for native numbers.
- The generator `tools/card-catalog-fixtures/emit_cards.py` writes only `catalog_data.rs` (`table.rs:3`), so `Natives` is not
  generated and is exactly what this guard protects.
- Existing C# tests of the catalog (`client-unity/RoguehallaNet.Tests/AugmentUi2Tests.cs:37-53,140-149`,
  `CardDisplayReworkTests.cs:45-85`) pin titles, count, a "minimap" substring and the file's source pin, never a number.
  `client-unity/RoguehallaNet.Tests/RoguehallaNet.Tests.csproj:25` links the catalog, so a C# route would also work (see choices).

### Text and table agree today (checked by hand, all 14)

| Card | Numbers in the text, in order | Rust source |
|---|---|---|
| MarksmansTally | 0.3, 80, 30 (percent) | `PER` .003, `RANGE_SHARE` .8, `CAP` .30 |
| Bastion | 35 (percent) | `REWORK_BASTION_SHARE` .35 |
| Calluses | 0.07, 30 | `PER`, `CAP` |
| ExecutionersLedger | 3, 30 | `PER`, `CAP` |
| EighthStrike | 8 ("doubles" is a word for `EIGHTH_STRIKE_MULT` 2) | `EIGHTH_STRIKE_EVERY` |
| Pathfinder | 1.2, 24 (percent) | `PER` .012, `CAP` .24 |
| HazardWalker | 15, 20 (s) | `CAP` 15, `IMMUNE_MS` 20000 / 1000 |
| WishEngine | 25, 50 (percent) | `CAP` .25; the 50 is `WISHFUL_FLOOR_SHARE` .5 (see traps) |
| Overflow | 8 (s) | `OVERFLOW_FADE_MS` 8000 / 1000 |
| CorpseHoarder | 3, 24 (percent) | `PER`, `CAP` |
| Stoked | 2, 30 | `PER`, `CAP` |
| Glacier | 0.15, 30 | `PER`, `CAP` |
| TrophyHunter | 2, 15 (percent) | `PER`, `CAP` |
| MarkedMan | 50 (percent) | `STACK_MULT` 1.5 (the extra half) |

No disagreement today, so the guard cannot be its own failing oracle: the oracle tests the checker on doctored input.

## Choices to read

- **Shape (a), a Rust integration test, not (b) an EditMode / `dotnet test`.** The plan's wording ("an EditMode test") predates
  looking. The constants are Rust; a C# test would need them exported to a fixture (a new generated file, a new tool and a
  second thing to forget to regenerate), and Unity EditMode does not run in any gate. `dotnet test` of `RoguehallaNet.Tests`
  does link the catalog, but is not part of `--quick` on a Rust-only retune. A Rust test reads the `.cs` as text with
  `include_str!` (precedent: `tests/card_catalog.rs:15`, `tests/wish_terminal_producers.rs:15` read repo files that way) and
  uses the constants directly, so it runs on every branch, and a retune that moves a constant fails it at once.
- **Numbers only, never phrasing.** The checker extracts the numeric tokens of each native description in reading order and
  compares them with the list the table gives for that card. Words ("doubles", "twice") and wording are not compared.
- **Doctored input.** `check(catalog_text, expected)` is a pure function so the oracle can feed it one wrong number.
- Rust test files that are not a test target live in a subdirectory (`tests/native_text_guard/`), so cargo does not build
  them as a second test binary.

## 4. New paths

- `crates/rh-sim/tests/native_text_guard/check.rs`: the checker `check`, `Expected`, `Mismatch` (stub: returns no mismatch).
- `crates/rh-sim/tests/native_text_guard.rs`: the oracle (declares `#[path = "native_text_guard/check.rs"] mod check;`).

## 5. Exclusive write scope

`crates/rh-sim/tests/native_text_guard/check.rs` only (the implementer replaces the stub body). The oracle file is read-only.
No change to `src/`, any card number or text, `CardDisplayCatalog.cs`, `Cargo.toml`, `Cargo.lock`, the content hash or the wire.

## 6. Contracts and references

C-PARITY not touched (no number moves). `CardDisplayCatalog.cs` is under the source pin
`tools/training-console/training-console-source-pins.json`; the guard only reads it, so the pin stays valid.

## 7. Behaviour to preserve

Every existing test stays green and unedited: none pins what this packet changes (it adds a test and edits no table, text or
constant). Tests that read the same data and must still pass: `rework_content_tests.rs` part 3; `AugmentUi2Tests.cs` lines
37-53 and 140-149; `native_cards.rs` (registry rows).

## 8. Acceptance checks

- `export PATH="$HOME/.cargo/bin:$PATH"; cargo test -p rh-sim --test native_text_guard` passes.
- `scripts/gate.sh --quick --oracle packet/P-BAL-NATIVE-TEXT-GUARD-1` passes.
- Mutation check by hand: change one number in a Natives description (not committed); the test names that card.

### The checker's contract (what the oracle pins)

```rust
pub struct Expected { pub id: &'static str, pub numbers: Vec<f64> }   // the C# Entry id, numbers in text order, in the text's units
pub struct Mismatch { pub card: String, pub detail: String }
pub fn check(catalog_text: &str, expected: &[Expected]) -> Vec<Mismatch>
```

For each `Expected`, find the `new Entry("<id>", ...)` in `catalog_text` and take its third string (the description). The numeric
tokens of that string, in order (digits with an optional `.`; a leading sign or `%` is not part of the number; "8th" is 8), must
equal `numbers` in count and, one by one, within 1e-9. A card with no entry, a different count or a different value gives one
`Mismatch` with `card == id`. A clean text and table give an empty vector. Percent figures are written in percent (0.3 for 0.003).

## 9. Validation commands

`cargo test -p rh-sim --test native_text_guard`, `cargo fmt --all -- --check`, `scripts/gate.sh --quick --oracle packet/P-BAL-NATIVE-TEXT-GUARD-1`.

## 10. Risk and suggested model tier

Low (a test-side parser). implementer-sonnet, reviewer `reviewer`.

## Known traps

- **WishEngine's "50% floor".** Rust has no Wish-Engine-owned floor row; the text's 50 is bound here to `WISHFUL_FLOOR_SHARE`
  (0.5, `constants.rs:110`), the Wishful Thinking floor, as the nearest registered number. If a later reading shows Wish Engine
  takes its floor elsewhere, the oracle's table entry for it is wrong, not the checker: tell the coordinator.
- "Every 8th hit doubles": the 2 is a word and is unguarded (`REWORK_EIGHTH_STRIKE_MULT`). Left out on purpose (phrasing).
- Token rule: "Every 8th" gives 8; "1/8" style fractions do not occur in the 14 texts.
- `MarkedMan`'s 50 is `(STACK_MULT - 1) * 100`; float noise like 0.30000000000000004 is why the compare has a tolerance.

## Oracle files (read-only for implementers)

- `crates/rh-sim/tests/native_text_guard.rs`

Six tests: the real catalog and the real table give no mismatch (passes against the stub; it is the guard itself, made
non-vacuous by the others); each of the 14 cards, doctored in its first digit, is reported alone and by name; a doctored
table value is reported; a later number of a longer text (Hazard Walker 20, Wish Engine 50, Marksman's Tally 80) is
reported; a different count of numbers is reported; a card missing from the text, and an empty text, are reported.

## Routing block

- serves: balance patch (owner approval 2026-11-22) · tier: implementer-sonnet · reviewer: reviewer
- D-rules: D1 loose · write scope: `crates/rh-sim/tests/native_text_guard/check.rs`
