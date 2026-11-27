# One prospective comparison of the Bargain lot size

Every one of the 29 maps offering The Bargain in a fresh 80-map draw, seeds 701–780, was played to match end under both prices. Each paired room used the same map, focal seat and bot policies, with the planner reading the price from its authoritative public rules. Both arms used actual quest settlements, real travel time and the same historical farming rates. The full camp alternative values twelve nearby stacks. The remaining-roster alternative values the mobs actually alive there.

| Measure | Give 10, receive 14 | Give 8, receive 11 |
|---|---:|---:|
| Claims / offers | 22 / 29 | 22 / 29 |
| Build-valued return against full nearby camp | 1.555× (1.148–2.176) | 1.252× (0.949–1.689) |
| Build-valued return against remaining nearby roster | 1.753× (1.247–2.635) | 1.245× (0.944–1.716) |
| Deaths during a Bargain attempt | 0 | 0 |
| Focal deaths before five minutes | 2 | 2 |

The intervals resample maps. All 44 trades paid with stacks. One of the two new point estimates was 0.52 percentage points below the requested 25% lower end. The [prespecified test](../../../../backlog/packets/P-ALPHA-BALANCE-TARGETS-1.md) required both to be inside and therefore missed. We selected 8-for-11 for the local game because both returns moved closer to the owner’s stated aim and the same number of trades settled. Variation by map remains substantial.

The input alternatives under identical settled choices had predicted 1.48× and 1.30× for full nearby camps in the two earlier 10-for-14 cohorts. In the prospective test the changed price could alter which offers were accepted, where the player walked and which stat they exchanged.

Each arm recorded zero concurrent ordinary clears on these trades. Keeping the full precision of every route leg before pooling gives 1.2516× and 1.2445× for the candidate. This also places the remaining-roster result just below 1.25×.

[10-for-14 trade ledger](current.csv) · [8-for-11 trade ledger](candidate.csv) · [Control summary](current.json) · [Candidate summary](candidate.json) · [Control observations](current.jsonl.gz) · [Candidate observations](candidate.jsonl.gz)

Use the same reporter and historical rate archive as the [unselected 10-for-14 cohort](../field-lot14/README.md). Decompress each observation archive and set `--fee 10 --payout 14` or `--fee 8 --payout 11` accordingly.

## Return across both sets of boards

The 80-map final cohort (seeds 601–680) contained 35 offers. The prospective draw (701–780) contained 29 more. Pooling the 160 boards and the actual cost of unsuccessful routes gives these descriptive totals. The earlier 10-for-14 observations from the first set had helped choose the candidate, so the prospective set remains its separate independent test.

| Price | Paid / offered | Net build-valued gain in camp units | Full nearby cost | Return | Remaining-roster cost | Return |
|---|---:|---:|---:|---:|---:|---:|
| 10 for 14 | 51 / 64 | 40.167 | 24.716 | 1.625× | 23.850 | 1.684× |
| 8 for 11 | 50 / 64 | 31.983 | 21.904 | 1.460× | 21.803 | 1.467× |

Resampling 80 map seeds within each set, with replacement, preserves both sampling frames. The selected price has a 95% interval of 1.163–1.888× on the full nearby basis and 1.123–2.006× on the remaining basis (4,000 samples, seed 20261127). The underlying 35-offer cohort returned 1.676× and 1.702× at the selected price. Compute the point estimates by summing the reported fit gain across the two cohorts and dividing by the sum of paid route cost plus failed route spend. A board without Bargain contributes zero to both sums. [Actual final matched-map reward data](../final-field/README.md) · [Earlier matched-map reward data](../field-lot14/README.md)
