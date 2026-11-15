# Hand-off: P-MAPGEN-SIG-HOLY-1-fix

```
PACKET:          P-MAPGEN-SIG-HOLY-1-fix — serves the review note of P-MAPGEN-SIG-HOLY-1 (note 1) and ADR 0028 (the per-landmark table is the landmark contract)
BRANCH:          agent/sonnet/P-MAPGEN-SIG-HOLY-1-fix @ see `git log -1` (the hand-off commit follows fe50d738e)
GATE:            not run by the implementer (targeted tests and clippy below; scripts/gate.sh not run, as instructed)
CHANGED:         crates/rh-sim/src/mapgen/landmarks/signature/{mod.rs,spec.rs,volcano.rs}, crates/rh-sim/src/mapgen/attempt/mod.rs, this file
D-RULES:         D1 none (no generated map moves: every pin and golden green, unedited) · D2 n/a · D5/D6/D9 none (no draw, wire, save, hash or content-hash change; no new const/static)
DEVIATIONS:      none
SHARED RECORDS:  proposed edits: docs/adr/0028-signature-landmarks.md (outside this packet's write scope, so not edited): in decision 12/13 add "The per-landmark table `spec::Spec` carries the site's side (`site_side`), its camps and whether the first is the zone's elite (`site_camps`), the spawn rule (`spawn`: `Box` or `OpenFloor { clear_of, by }`), the fan (`fan`) and the own feature's params (`params`); `reserve`, `write`, `fan` and `fan_holds_a_site_camp` read the entry, no zone or kind branch decides them." There is no signature README in the tree; the module doc of `signature/mod.rs` says it now.
LEGACY DOUBTS:   none (no legacy counterpart, DEV-020)
NOT ESTABLISHED: the Hall params home (`Spec::params`) is read by `write` but no test doctors it (only the default is observable); a Pin entry's own `site_side` differing from `Definition::site_side` is not exercised by any test; `gate.sh --quick --oracle ... --base ...` not run.
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
