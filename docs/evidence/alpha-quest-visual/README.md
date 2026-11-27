# Quest completion and Unity visual verification

The local Web preview serves protocol 24 and content `cb811d19ebed6dfe`. [Playable local preview](http://localhost:8891/)

The Rust suite exercised all 25 quest definitions for two independent players at each of 20 Hz and 40 Hz. It asserts the intended gameplay effect of each of the 19 castable awards and seven permanent rewards. Examples include Anchor planting and recall, Turret damage, Bolas root, Cleanse removing stun and root, weapon restoration after Swap-Stick, and a Rebirth Egg returning a defeated player to play. [All 25 expected outcomes and fixture details](outcomes/README.md)

Unity Web consumed 155 owner-view checkpoints from those runs. The browser submitted 25 matching quest acceptances and 20 matching cast inputs. All 25 quests reached a reward receipt. The 20 inputs cover 19 castable awards plus the Seed award's second activation. The browser matched each sampled owner position, HP, status, effect flags and quest progress against the captured frame. The screenshots show the running Unity Web client. [Machine-readable browser receipt](browser-receipt.json) · [Outcome contact sheet 1](screens/quest-outcomes-1.jpg) · [Outcome contact sheet 2](screens/quest-outcomes-2.jpg) · [Outcome contact sheet 3](screens/quest-outcomes-3.jpg)

The controlled server runs arrange the initial positions and combat situations. Objective progress, claims and cast outcomes use authoritative simulation commands. During browser playback the recorded server outcomes feed the real Unity Web rendering pipeline. When two samples share a source tick, the browser harness advances the transport tick so Unity shows both. The source and playback ticks are recorded for every screen. Absolute timestamps in the reward, effect and event data come from the captured authority. Two ordinary browser runs also verified room admission, planning and the raid.

For Lost Brother, the player walked to the Lost Brother giver, accepted, followed the relic direction, collected the relic and returned. The server granted BlindRage and StillMind. The Shift cast spent BlindRage, increased attack speed by 20%, and left StillMind equipped. This run used the actual joined server and real Unity mouse and keyboard inputs throughout. [Lost Brother screenshots](screens/live-lost-brother.jpg) · [Lost Brother receipt](live-browser-receipt.json)

For Letters, the ordinary room awarded the Forget-Me-Not Seed after delivery. The first cast placed it at the player’s location and retained its charge. The player moved 167 units away. The second cast returned the player to within 0.06 units of the planted point, removed the field and spent the charge. The normal planning and raid screens showed five and ten distinct offers respectively. [Letters screenshots](screens/live-letters.jpg) · [Letters receipt](live-letters-receipt.json)

## What changed in the game

- The tactical map identifies the current step as Accept, Next, or Turn In and uses the server-delivered objectives or giver. The minimap keeps visible objectives for accepted quests. Tracking emphasizes the selected quest without suppressing the other accepted objectives. [Next objective](screens/map-next-objective.jpg) · [Turn-in](screens/map-turn-in.jpg)
- The offer panel and active tracker are wider. The offer scrolls long copy while its accept and close buttons stay visible. The journal gives the objective and reward more room. The field ledger, ability tiles, quest actions and controls guide use larger text where the earlier screen review showed tight copy. [Offer before](screens/offer-before.jpg) · [Offer after](screens/offer-after.jpg) · [Journal at 720p](screens/journal-720.jpg)
- Warden's Cage now renders as the iron fence on its delivered barrier tiles. Its gameplay already blocked bodies and melee. Unity now uses distinct art for the positive-radius cage and the zero-radius Frost Wall. [Warden’s fence in play](screens/warden-fence.jpg)

I reviewed the main menu, settings, planning, raid HUD, quest offer, journal, tactical map, controls and mechanics pages, and field ledger at 1440×900 or 1280×720. I also set the in-game slider to 1.50× and reviewed the journal, map and guide at 1280×720. The scrollable panes keep more text within reach at the higher scale. The two draught spell names wrap between words at 1.50×. [Planning](screens/planning-720.jpg) · [Field ledger](screens/field-ledger-720.jpg) · [Text size at 1.50×](screens/settings-large.jpg) · [Enlarged journal](screens/journal-large-720.jpg) · [Reward after scrolling](screens/journal-large-reward-720.jpg) · [Enlarged map](screens/map-large-720.jpg) · [Enlarged guide](screens/controls-large-720.jpg) · [Spell tiles at 1.50×](screens/spell-tiles-large.jpg)

## Reproduction

Generate the ordered native capture using the [authority commands](outcomes/README.md#reproduce-the-wire-playback). Point the visual tools at a local Unity Web build served by the normal game router. They use Playwright Chromium, with optional `RH_PLAYWRIGHT` and `RH_CHROMIUM` environment variables for explicit local paths.

```sh
node tools/quest-visual/browser-replay.cjs http://localhost:8891/ \
  .agent-runs/quest-visual/v2/wire .agent-runs/quest-visual/replay
node tools/quest-visual/browser-sweep.cjs http://localhost:8891/ \
  .agent-runs/quest-visual/ordinary
```

Set `RH_LIVE_QUEST=1` for the ordinary browser script to also complete one of Lost Brother, Letters or Stay A While when it appears on the generated board. The screenshot grids show an outcome state for every quest. The full screenshot sequence for the final Web build, including offered and accepted map states, is retained under `.agent-runs/quest-visual/verified-replay`. The original full live Lost Brother run is retained under `.agent-runs/quest-visual/live-text-v3`. The Letters playthrough is under `.agent-runs/quest-visual/delivery-normal`.
