# Account presentation exports

Run `./tools/account-catalog/export.sh --check` to verify checked-in Resources/Account JSON against actual shared
COSMETICS/ACHIEVEMENTS and Rust cosmetics data. Use `--write` only after reconciling source changes. The wrapper uses the
repository's pinned Node22.23.4 image and npm lockfile dependencies in a disposable tmpfs; no legacy source is changed.
The exporter applies presentation-only aliases for the renamed Haunted faction to the `gilded-sentenced` cosmetic,
`gild_sentenced` achievement, and `win_sentenced` achievement description. Canonical ids, slots, faction targets, goals,
thresholds, rewards and Rust profile data keep their stable `sentenced` keys.
Output has no goal evaluator, roll, eligibility or purchase authority. Server profile/HTTP results alone grant items,
equip, progress and GV. See docs/evidence/account-full-profile/README.md for typed consumption and remaining browser gates.
