# Augment and elite rework: agent guide

(P-AUG-RULES-1 writes the rest of this guide; this section is the content-hash script.)

## Moving the content hash: `scripts/regolden-content.sh`

A packet that changes content moves `content_hash`. Never hand-edit the copies. After the change, run
`scripts/regolden-content.sh` (it computes the hash with `cargo run -q -p rh-sim --example content_hash`). It rewrites
the four text copies (`crates/rh-sim/tests/content.rs` `GOLDEN_CONTENT_HASH`, the two `tools/unity-browser-reload-proof`
`.mjs` files, `BakedContent.cs`), then runs the documented commands for the two protected copies
(`cargo test -p rh-sim --lib record_ability_hash_tape -- --ignored` and `cargo run -p rh-parity --bin hitbox-fixtures`).

- `--check` lists stale copies as `STALE <path>` (exit 1) without running cargo's protected commands.
- `--dry-run` prints `WOULD EDIT` / `WOULD RUN` lines; `--text-only` skips the protected commands.
- `BakedContent.cs` is a build artefact by name: a diff that changes it needs `scripts/gate.sh --allow-artefacts`.
- Copies of the hash exist only in those six files; a new value pin elsewhere fails `regolden_content`.
