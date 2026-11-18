# Hand-off: P-MAPGEN-SIG-HOLY-1-fix

```
PACKET:          P-MAPGEN-SIG-HOLY-1-fix — serves the review note of P-MAPGEN-SIG-HOLY-1 (note 1) and ADR 0028 (the per-landmark table is the landmark contract)
BRANCH:          agent/sonnet/P-MAPGEN-SIG-HOLY-1-fix @ the commit that adds the section "Gate and review" (last code commit f30ed0690, the two review notes; packet/P-MAPGEN-SIG-HOLY-1-fix @ 95c7fbf01; base origin/feature/unity-rework @ 86dd4adca)
GATE:            PASS (quick, --oracle packet/P-MAPGEN-SIG-HOLY-1-fix --base origin/feature/unity-rework) on f30ed0690, run by the lane's session from an empty build folder of this worktree's own: path checks, rustfmt, the card provenance chain, the staging preparation pins, clippy rh-sim, the content registry, the whole rh-sim suite as CI (92 result lines, none failed), the parity report. [not run: the full gate (the coordinator's). Run besides it on the same build: cargo test -p rh-mapdress, green.] One docs-only commit (this file) follows the gated commit.
CHANGED:         crates/rh-sim/src/mapgen/landmarks/signature/{mod.rs,spec.rs,volcano.rs}, crates/rh-sim/src/mapgen/attempt/mod.rs, this file; docs/adr/0028-signature-landmarks.md (decision 14, added by the lane's session)
D-RULES:         D1 none (no generated map moves: every pin and golden green, unedited) · D2 n/a · D5/D6/D9 none (no draw, wire, save, hash or content-hash change; no new const/static)
DEVIATIONS:      none
SHARED RECORDS:  none. (ADR 0028 decision 14 and the wording of decision 10 were added by the lane's session on this branch, outside the packet's literal write scope: a docs change for the coordinator to accept or strike.)
LEGACY DOUBTS:   none (no legacy counterpart, DEV-020)
NOT ESTABLISHED: the full gate; the Hall params home (`Spec::params`) is read by `write` but no test doctors it (only the default is observable); a Pin entry's own `site_side` differing from `Definition::site_side` is not exercised by any test; the own-fan test does not cover an `extra` camp, or a camp-carrying site with its own fan against another site's fan (not needed today)
RISKS:           none known; a changed candidate count would have moved `pinned_on_maps` / `pinned_landmarks`, which are green.
```

## Special cases gone and left

Gone (`rg "Zone::Holy|FeatureKind::Hall|hall_zone|hall_marks" signature/mod.rs` finds no branch in `reserve`, `write`, `fan`):

- `reserve`: the side is `entry.site_side` (was `hall_zone ? hall_site_side : site_side`); which glyph pins the camps and the elite flag come from `entry.site_camps` (was `matches!(z, Sentenced | Frost)`, `hall_zone`, `hall_marks`, `elite` false for hall). Pin entry with camps: the one camp is `inside_camp(x, y, entry.site_side)` (now takes the side). Centre entry with camps: `centred_camps` (was `hall_camps`) reads the landed tiles of the glyph. `elite = camp.is_some() && site_camps.elite`. `hall_marks` is deleted (the stray doc inside `inside_camp`'s comment went with it).
- `write`: the spawn rule is `entry.spawn` (was `p.kind == FeatureKind::Hall`); by-tile kinds (the volcano's streams and pools) keep their by-tile mask first, whatever the entry says. The light is `Piece::clear`, the landed tiles of the `clear_of` glyph carried from `stamp` (was "any written floor with terrain"). The hall's params are chosen by `entry.params == FeatureParams::Hall` on the entry's own feature kind (was `p.kind == FeatureKind::Hall`); `Spec::params` is the table home, `FeatureSpec` is unchanged.
- `fan` and `fan_holds_a_site_camp`: now `SignatureContext` methods reading the entry's `fan` (was Fire only from `d.fan_depth`/`d.fan_side`); a site's own camps are not tested against its own fan (each fan is tested against the camps of every other site). The free functions are removed; `attempt/mod.rs` calls `context.sites.fan(r)` and `context.sites.fan_holds_a_site_camp(&reserved)`.

Left:

- `signature/mod.rs:256` `relabel_sites`: `|| r.zone == Zone::Holy` (not required; not pinned; reading "a site with extra camps" would change a Holy site with one camp). `relabelled_zones()` stays `[Sentenced, Frost]` (length pinned); its doc now says so.
- `signature/mod.rs:229` `reserved_zones()` names the five zones (the order is behaviour).
- `signature/mod.rs` `hall_params` stays a function named for the hall; the table selects it with `FeatureParams::Hall`.
- `t5_exempt`'s `landmark_kind` list (doc now says only T5 reads it and that `Hall` is not in it).
- `spec.rs` names Holy and the Hall kind in its Holy entry (the table is where they belong).
- The in-module tests name Holy and Hall.

## How the light's tiles reach `write`

`stamp` computes `clear`: when `spec.spawn` is `OpenFloor { clear_of, .. }`, `bp.tiles_of(clear_of)` landed with `landing.tile` and mapped through `at` (empty for `Box`). Every `Piece` of the entry carries it (`Piece::clear`); the volcano's streams and pools carry none. `write` closes the written cells and every tile within `by` (Chebyshev) of a `clear` tile. For the default Holy entry `L` is the only floor with terrain the hall writes, so the set equals the old one. The in-module test `the_hall_keeps_spawns_off_its_walls_and_clear_of_its_light_only` passes `clear: vec![44 * W + 44]` in its `Piece` literal (setup only; assertions unchanged).

## Test results (cargo test -p rh-sim --features trusted-status-probes)

- `mapgen_signature_site_rules`: 11 passed, 0 failed.
- Guards: `mapgen_signature_blueprint` 10 passed (1 ignored); `mapgen_guarantees` 7 (1 ignored); `mapgen_signature_hall` 18 (1 ignored); `mapgen_signature_reserve` 11 (1 ignored); `mapgen_signature_volcano` 20 (1 ignored); `mapgen_signature_church` 13 (1 ignored); `cargo test -p rh-mapdress` all green (3, 10, 10, 11, 19, 7, 16, 1, 0).
- Others: `mapgen_signature_table` 8, `mapgen_landmark_frame` 8, `mapgen_signature_core` 8, `mapgen_signature_aprons` 2, `mapgen_signature_fire` 11, `mapgen_signature_frost` 7 (1 ignored), `mapgen_land_fire` 16, `mapgen_land_plague` 27 (3 ignored), `mapgen_land_sentenced` 11 (2 ignored): all passed.
- Lib: `landmarks::signature`, `zones::camps`, `certify`: 28 passed.
- `cargo fmt --check` clean; `cargo clippy -p rh-sim --lib --tests --features trusted-status-probes -- -D warnings` clean.

No pin, golden or existing test file changed: `git diff --stat packet/P-MAPGEN-SIG-HOLY-1-fix..HEAD -- 'crates/*/tests'` is empty. The only test-adjacent edit is the setup of the in-module test named above (in `signature/mod.rs`).

## Gate and review (the lane's session, 2026-11-14)

- **Gate:** as the block says. An earlier quick gate passed on the implementer's tip (1311cb07e, before the review
  notes); a second run was stopped and discarded by the lane's session because its build folder was, for an hour,
  shared with another worktree on a different branch (binaries of one worktree can be taken as fresh by the other);
  the result above is from a clean build in this worktree's own folder.
- **Guards (no re-pin, no test file edited):** `signature_pins.rs` `pinned_on_maps`, `pinned_landmarks` (it digests the
  spawn masks) and `pinned_off`, `mapgen_guarantees.rs` `pinned_labels`, the rh-mapdress goldens: all green on
  f30ed0690. `git diff --stat packet/P-MAPGEN-SIG-HOLY-1-fix..HEAD -- 'crates/*/tests'` is empty.
- **Moved expectations:** none. The oracle is one new file (`mapgen_signature_site_rules.rs`, 11 tests through a
  doctored table); no existing test was amended.
- **Review:** reviewer-opus on dd223694d, reading only (no test run: the coordinator's gate had the machine):
  **LAND WITH NOTES**, two notes, both fixable without moving a byte. It compared `reserve`, `stamp`, `write`, the fans
  and the attempt's use of them against the base and found no input on which the shipped table behaves differently;
  no draw added, dropped or reordered; option off untouched.
  - **Note 1, fixed in f30ed0690:** `relabel_sites` still filtered by zone (`relabelled_zones()` or Holy). It now
    relabels every reserved site that carries a camp (`r.camp.is_some()`): identical today (Sentenced, Frost and Holy
    always carry one; Fire and Plague never do), and a later entry given site camps is relabelled without a line here.
    `relabelled_zones()` stays for the oracle that pins it; its doc says so.
  - **Note 2, fixed in f30ed0690:** `site_camps` means two things by placement (a pinned entry's camp is the pinned
    tile and its glyph was never read; a centred entry's camps are its glyph's landed tiles) and nothing tied the pin
    glyph to the camp glyph. `from_table` now checks (`debug_assert!`) that a pinned entry's site camps name its pin
    glyph, and `SiteCamps::glyph`'s doc says the field is unused for a pinned entry and must equal the pin's.
- **Minor, not notes** (the reviewer's list; no packet): the module doc claimed the table carries every
  per-landmark rule (corrected in f30ed0690: Plague's line rule and deepest-square fallback in `reserve` and the
  volcano's streams in `place` are still decided by zone); ADR decision 14's wording on the glyph and the elite flag
  and decision 10's `signature::fan` (corrected); `reserve`'s reason for "looks unreachable" (reworded: not seen,
  not proven); `attempt/mod.rs:34`'s doc line is over 120 columns and it, `attempt/mod.rs:248` and
  `landmarks/stage/mod.rs:121` still read as Fire-only (left: comments); `from_table` and `with_signature_table`
  could be `#[doc(hidden)]` and `from_table`'s "the table keeps `reserved_zones` order" is unchecked (lookups are by
  zone); `Piece::clear` is cloned into every piece of a multi-feature open-floor entry (harmless; not the hall).
- **For the next packet (the Frost cave's reserved room):** it is one table line, `fan: Some(Fan { depth, side })` on
  the cave's entry; the cave's own camp is not tested against its own fan here, and `place_camps_reserving` checks a
  site's own camps against the halo rule, not against the reserved rects.
