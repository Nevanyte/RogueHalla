# Choose the open-alpha scope

**First action for the next agent.** Read the [source and release handoff](OPEN_ALPHA_HANDOFF_2026-11-24.md), confirm the candidate and current test evidence, then give the owner a short ranked menu. Show **importance, remaining active hours, confidence, dependencies and acceptance** for each proposed item. Give a total with shared setup counted once. Ask **“Which of these would you like finished for this alpha, and which should I defer?”** Wait for the selection. Make new code, art and regression-test changes wherever they are the clearest way to finish the selected details.

The estimates below are preliminary person-hours for one engineer familiar with the project. They include relevant implementation and checking. Build queues, device availability, provider access, approvals and unattended tests add elapsed time. **Importance 5** blocks the promised release or data safety. **4** affects a common player action or obvious presentation defect. **3** improves a noticeable but narrower case. **2** serves a limited audience or later measurement. **1** is elective. Use importance and effort together. Within the same importance band, finish the lower-effort item first unless another item unlocks it. Re-estimate on the exact checkout. Use a time-boxed investigation for any open-ended defect.

## A practical first choice

| Choice to offer | Included work | Estimated active work |
|---|---|---|
| Short polish pass | U1 map-key proof and compact layout, U3 HUD/notification smoke, A1 all five grounds and crossings | **4–10 h**, plus individually selected fixes if a smoke check fails |
| Short pass plus accessibility | Add U2 large-text regression fixes | **7–18 h** total. Confirm which remaining 1.5× cases the owner wants fixed |
| Custom list | Choose any IDs below, or nominate another minor issue | Present a revised low/high total and the condition most likely to change it |

These choices estimate **polish**. Show release verification, hosting, data and production work on a separate line. For a fresh-account launch using a new supported origin, a first estimate for R1–R6 is **25–63 active hours** including the clean merge; carry-over adds roughly **4–12 h**. Numbers assume provider access, approved topology and no major defects. A scope decision such as intentionally fresh alpha accounts changes both the work and the acceptance test.

## Player-facing polish

| ID | Unfinished item and completion evidence | Importance | Remaining hours | Confidence or dependency |
|---|---|---:|---:|---|
| U1 | Finish Web round 3e, especially 1280×720, and verify the hidden key shows a single heading and equal-size camp cells. Correct a reproducible compact-layout defect within the estimate. | 4 | 1–3 | High for verification. Current screenshots stop short of the verdict. |
| U2 | Fix selected 1.5× text-fit cases, particularly “MAP KEY”/“You”, “Reward chest”/“Route stop”, and zone names after resizing. Check both normal and large text after reopening the map. | 4 | 3–8 | Medium. Use the saved text-fit spec; confirm the affected rows with the owner. |
| U3 | Play one fight with quest, stat and ability feedback at once. Check placement, clicks, the run-end panel and trader updates at normal and large type. Record any additional fixes individually. | 4 | 1–3 | Medium; shares browser setup with U1. |
| U4 | Complete the wave-four review record for the two guard packets and map-key Web evidence. Verify the account-backed menu and Training as part of the joined UI smoke. | 3 | 1–3 | High for records. Shares normal-menu proof with R4. |
| U5 | Check Settings and Help scroll cues on first open, tab switch and resize. Fix reproduced layout or lost-focus issues at 1280×400 and large type. | 3 | 2–4 | Medium; share large-type setup with U2. |
| U6 | Decide how quest interaction controls are taught, then verify F, Enter and Escape, keyboard focus, and Reduced Motion. | 4 for onboarding | 2–5 | Requires the owner's controls choice. |
| U7 | Verify a quest offer can be retried after a lost or timed-out acceptance, with the correct card and outcome shown. | 3 | 1–3 | Medium; local two-client test. |
| U8 | Play first-run and results flows in a fresh profile and correct chosen misleading or clipped copy. | 3 | 2–5 | Medium; shares R4. |
| A1 | Verify Plague, Haunted, Frost, Fire, Holy and representative borders in the packaged build. Check transitional vegetation, cracked paving, snow/sand edges and scree at gameplay scale. | 4 | 2–4 | Medium. Budget covers checking and recording failures. A new repaint gets its own estimate. |
| A2 | Retest church doors, pews and roof cutaway plus Holy pier footing and temple alignment. Correct a misplaced prop or sorting error if reproduced. | 3 | 2–5 | Medium; test the affected landmark orientations. |
| A3 | Check lava/water movement, ice contrast, bog footfalls and lamp/lava spill with bloom off and Low quality. Fix one bounded presentation issue if chosen. | 3 | 2–6 | Medium; a shader or particle-system redesign is a separate estimate. |
| A4 | Check the active Frost Wall and Chain Warden in combat on bright/dark ground; confirm the local owner's avatar and effects remain legible in a crowd. | 3 | 1–4 | Medium; shares the combat session with U3. |
| A5 | Complete the Inquisitor’s shared-sword hand registration and check eight directions if selected. | 2 | 4–12 | Low to medium. The eight-facing sheet is saved as a study. |
| A6 | Choose individual remaining character, elite, effects or environment upgrades. Review existing work before commissioning any new art; keep shared weapons and anatomical hands consistent. | 1–4 | Per check in the art register | [613 item estimates](OPEN_ALPHA_ART_ESTIMATES_2026-11-24.tsv) cover every tracker row without a full visual pass. Shared batches overlap. |

Further environment choices are the Fire volcano’s eight landmark views (**importance 3, 3–6 h to check**), a representative cross-zone dressing review (**2, 4–10 h**), and optional regional solid-wall reskins (**2, 2–8 h for each selected region**). Preserve every Rust obstacle footprint. The table’s church and lighting rows already cover those locations. A broad new cast or terrain repaint is a separate production choice.

The [art effort register](OPEN_ALPHA_ART_ESTIMATES_2026-11-24.tsv) gives the original tracker line, group, asset, weapon, facing, recorded status, importance, low/high hours, action and check for each row. Its proposed estimates are editorial release triage. The existing tracker remains authoritative for implementation and visual status. A “reviewed sample” or “Fire axe reviewed in Web” can justify a quick recheck of that row. Do not add 613 isolated estimates to produce a delivery promise. Group by asset and weapon, count shared authoring once and budget any discovered redraw after seeing the defect.

## Backend choices

The current game can use its existing live bot and balance. Choose experimental bot work for demonstrated alpha impact or if the owner wants the balance track in this release.

| ID | Unfinished item and completion evidence | Importance | Remaining hours | Dependency or scope |
|---|---|---:|---:|---|
| B1 | Finish the ranged bot line-of-sight correction, preserve its one-line working edit, run the twelve-seed comparison and review real wall-shooting behavior. | 3 if experimental bot used, otherwise 2 | 1–3 | Implementation and quick gate exist. |
| B2 | Recheck the two Integration 89 preview changes on the assembled build. Validate camp spell range and make the lack of ordinary Hook/Bolas use explicit. | 2 | 2–5 | Already merged into the candidate. Changing bot goal selection is B8. |
| B3 | Finish the paired card/stat grant lab and prove the no-grant path unchanged. | 2 | 5–10 | Revised acceptance tests and stub exist. |
| B4 | Fill the remaining per-seat, quest-death, cast-window and chest counters. | 2 | 6–14 | After the lab/report schema settles. |
| B5 | Add paired spell-reward experiments. | 2 | 6–14 | Requires B3 and usable spell selection. |
| B6 | Decide Q30's camp rate/faction basis and update quest valuation; isolate the cause of Fire’s faster clears. | 3 for a balance patch, otherwise 2 | 0.5–2 decision + 2–6 evaluator + 4–10 Fire measurement | Separate the three estimates when the owner chooses a subset. |
| B7 | Complete bot combat movement/disengagement and remaining quest spell rules. | 2 | 10–22 movement; 12–26 spell rules | Large gameplay additions. Estimate them independently. |
| B8 | Complete PvP/event/Coliseum goals; then validate dormant Hook/Bolas rules. | 2 | 16–32 | Experimental bot path. |
| B9 | Extend quest selection/replacement, kill objectives and Whispers search. | 2 | 2–5 replacement; 6–14 kills; 4–10 search | Three independent choices with shared quest setup. |
| B10 | Run the final measured baseline, then tune chosen elites, augments and quests with before/after evidence. | 2–3 | 5–12 baseline; 24–55 tuning/records | Needs representative bot coverage. Quest prices need Q30. |
| B11 | Offer RaidBot behind its off-by-default live flag and prove the existing fallback. | 1 unless selected for live | 4–8 | After experimental bot milestones. |
| B12 | Reproduce the exact-byte C# allocation-test instability; stabilize the measurement if selected and retain a check for genuine regressions. | 3 | 1–4 | Separate from a repeatable game regression. |
| B13 | Investigate server summary resends and collection allocations on a measured hotspot; audit the Unity allocation tests and isolate any test that writes a real art asset. | 2–3 | 1–3 per hotspot to measure; 2–6 per confirmed repair | Split findings into individual rows before proposing code changes. |

## Release, final merge and launch

| ID | Work and acceptance | Importance | Remaining hours | Dependency or scope |
|---|---|---:|---:|---|
| R1 | Assemble selected fixes on a clean integration branch. Refresh the target, resolve conflicts, review the final diff and merge once. Record the merge SHA and prove it contains approved source, UI and art. | 5 | 1–3 | After scope approval, required for handoff to release. |
| R2 | Run the full gate and CI on the final merge; build matched Rust/Unity artifacts, manifest, content identity and checksums. | 5 | 3–8 | Excludes extensive fixes, unattended compile/test time and platform setup beyond the existing toolchain. |
| R3 | Stand up the selected production-like staging topology, route assets/HTTP/WSS, verify auth and proxy limits, and establish capacity, cost and support ownership. | 5 | 8–20 | Low confidence until hosting choice/access are confirmed. |
| R4 | Real account-backed staging join, two independent clients, results, reconnect, persistence, classified disconnect logs, weak-device comparison, capacity and cache behavior. | 5 | 4–10 | Requires R2–3, accounts and hardware. Shares UI/ground checks where practical. |
| R5 | Rehearse data policy and restore. Fresh alpha accounts require backup/restore proof. Carry-over also requires dry import, collision reconciliation and the final writer freeze. | 5 | 4–8 fresh; 8–20 carry-over | Pick one approach; preserve original legacy profile data. |
| R6 | Rehearse rollback, approve the exact change, promote the verified SHA and prove a fresh match at play.roguehalla.com. | 5 | 5–14 | Includes operational preparation and verification, excludes waiting for owner/DNS. |
| R7 | Review the public dependency/auth findings on the exact candidate. Decide whether the parked advisory-CI and dev-auth changes are needed; fix actual release exposure. | 5 for an applicable public issue | 2–6 | Additional to R1–R6 if the audit finds a material issue. |
| R8 | Validate the parked staging proxy if selected, or the fleet-bridge hardening if using managed fleet hosting. | 3–5 by topology | 2–5 proxy; 4–10 bridge | Independent conditional estimates. Do not budget an unused topology. |

**Handling discoveries.** Give every newly identified unfinished task an ID, exact player/release impact, existing evidence, small/likely/pessimistic effort or a low–high range, dependencies, and a measurable done condition. If impact or effort is unknown, propose a 0.5–2 h investigation and return with a revised estimate. Ask about substitutions or material overruns before expanding the approved scope. End with a clean final merge, an acceptance record for each selected item, and a list of deliberate deferrals.
