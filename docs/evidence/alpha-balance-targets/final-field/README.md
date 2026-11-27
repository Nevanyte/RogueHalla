# Room timings on the final local build

All twelve generated maps (seeds 1–12) ran to match end with the final quest prices, post-opening elite defenses, ordinary three-stack farming and the stock RaidBot chooser. Each 40 Hz room had one account-eligible seat driven by RaidBot and seven fill bots. The two observers agreed on termination and terminal tick for every map. The content identity is `13a89cbaff80132f` under protocol 24.

## Fight times

Each cell gives median seconds and the number of completed encounters. A camp ends when the entire roster clears. Ten seconds without contact ends a pull. PvP timing measures the last continuous engagement that finished in a player final blow. Separate [full-health solo duel checks](../../raid-routing-ttk/README.md#camp-clears-and-player-kills-over-raid-time) supply a comparable early and late player TTK. Field episodes include teammates, terrain damage and opponents who start wounded. [Timing definitions](../field/README.md#timing-definitions)

| Raid age | Ordinary camp | Elite camp | Player final blow | Healthy solo PvP before lock |
|---|---:|---:|---:|---:|
| 0-5 | 13.36 (420) | 22.28 (55) | 10.20 (1) | — (0) |
| 5-10 | 18.20 (165) | 26.18 (82) | 12.08 (12) | — (0) |
| 10-15 | 20.02 (62) | 27.77 (61) | 8.35 (7) | — (0) |
| 15-20 | 13.07 (10) | 15.22 (17) | 5.54 (4) | — (0) |
| 20+ | — (0) | 26.62 (3) | — (0) | — (0) |
| all | 14.85 (657) | 23.59 (218) | 9.75 (24) | — (0) |

![Field camp clear times and full-health farm-kit duel times through minute twenty](raid-times.png)

The right panel uses the earlier independent farm-kit study. Both seats carried the inventory actually earned by that age. Only eight eligible map pairs reached the minute-twenty comparison. The final rooms supplied three elite clears and zero ordinary clears after minute twenty.

The previous stock medians were 14.50 (683) seconds for ordinary clears, 13.82 (311) for elite clears and 8.60 (29) for player final blows.

## Room end and bot survival

The median duration was 1268.50 (12) seconds. The shortest room ended after 800 seconds. The twelve rooms recorded 84 deaths, including 4 before five minutes, 17 before ten and 53 in sudden death. At the production room entry point, one human connection and seven RaidBots started three more matches. At 150 seconds all three humans were standing and the bot counts were 7/7, 6/7 and 7/7. On the first map a bot killed an elite at 145.6 seconds and picked a chest upgrade at 147.5 seconds. [Production room tests](../../../../crates/rh-server/src/room/tests/bot_fill.rs)

## Data and reproduction

[Age and participation groups](timings.csv) · [Individual raids](matches.csv) · [Encounter outcomes](outcomes.csv) · [Replay data for both cohorts](raw.json.gz)

Run `python3 tools/balance-lab/field_pacing_compare.py /tmp/raid-final --seeds 1..13 --read-archive docs/evidence/alpha-balance-targets/final-field/raw.json.gz` to reproduce the three CSVs. The [earlier checkpoint](../field/README.md#reproduce) gives the commands for collecting equivalent runs from source.

Run `python3 tools/balance-lab/field_pacing_plot.py /tmp/raid-final/timings.csv docs/evidence/raid-routing-ttk/route_summary.csv /tmp/raid-times.png` to regenerate the figure.

| Executable | SHA-256 |
|---|---|
| rh-server | `8dceea1275d8aa1e7f48ca94ea88b03d8f7f261e3827f21a4dabe89f646fdea7` |
| elite_field | `32224f60dbac248cd1d7588454650ea7e00a062d914d281eba4457945d22ab90` |
| strategy_ttk | `68d5cf0b6eba285092e751b40a62e2dc7fff3b705b5847b6f96a9aacd3676196` |
