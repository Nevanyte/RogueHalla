# testing

The legacy game's balance workbench: Python simulations and tuning notes from the TypeScript game's design work. It is
not a test suite (the Rust tests are in `crates/`).

| Path | What it is |
|---|---|
| `classes.py`, `engine.py` | The faction sheet and the combat engine the balance work was done on. |
| `realtime.py`, `realtime_paced.py`, `realtime_report.py` | A 20 Hz duel model with weapons (needs numpy), a paced JSON variant for the TypeScript harness, and the report. |
| `matchup_band.py`, `flatten_sheet.py`, `weapon_reference.py` | Faction-pair band check, sheet fitting, and the weapon numbers the TypeScript port is tested against. |
| `kite_buff_sweep.py`, `kite_windup_sweep.py`, `test_kite.py`, `test_rpsls.py` | The kiting-guarantee sweeps and two design checks (run each directly with `python3`). |
| `farm_route_proof.py`, `farm_route_data.json`, `farm_route_report.md` | The farming-route proof, its data and its report. |
| `TUNING_*.md`, `CLASS_SHEET.md`, `COUNTER_REVIEW.md`, `FARM_ROUTE_PROOF.md`, `WISHES.md` | Tuning logs and design notes. They are records: many documents link to them. |
| [`telemetry/`](telemetry/README.md) | Offline telemetry analysis and its shared fixture. |

Start with `TUNING_BALANCE.md` for the latest balance pass. Do not move or rename files here: many files cite these paths.
