use super::*;
use crate::rng::Mulberry32;
use serde::Deserialize;
#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Corpus {
    format: String,
    node: String,
    platform: String,
    arch: String,
    scan: Vec<Scan>,
    cases: Vec<Case>,
}
#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Scan {
    seed: u32,
    success: bool,
    partition_draws: usize,
    draws: usize,
}
#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Case {
    seed: i64,
    attempt: u64,
    initial_u32: u32,
    pillar: usize,
    order: Vec<String>,
    prefix_words: Vec<u32>,
    partition_words: Vec<u32>,
    words: Vec<u32>,
    draws: usize,
    continuation: Vec<u32>,
    zone: Option<Vec<u8>>,
    cost: Option<Vec<i32>>,
    camps: Option<Vec<CampSite>>,
    territory_grid: Option<Vec<u8>>,
    reach_all: Option<Vec<i32>>,
    reach_walkable: Option<Vec<i32>>,
    reach_empty_mask: Option<Vec<i32>>,
    reach_empty_territory: Option<Vec<i32>>,
}
fn corpus() -> Corpus {
    serde_json::from_str(include_str!("../../fixtures/zones-camps.json")).unwrap()
}
fn prefix(rng: &mut Mulberry32, pillars: usize) -> (usize, Vec<String>, Vec<u32>) {
    let mut words = Vec::new();
    let w = rng.next_u32();
    words.push(w);
    let pillar = ((f64::from(w) / 4294967296.0) * pillars as f64).floor() as usize;
    let mut order: Vec<String> = crate::content::FactionId::ALL
        .iter()
        .map(|f| f.as_str().to_string())
        .collect();
    for i in (1..order.len()).rev() {
        let w = rng.next_u32();
        words.push(w);
        let j = ((f64::from(w) / 4294967296.0) * (i + 1) as f64).floor() as usize;
        order.swap(i, j);
    }
    (pillar, order, words)
}
#[test]
fn actual_stage_prefix_partition_and_camps_match_every_field_and_rng_word() {
    let c = corpus();
    assert_eq!(c.format, "rh-zone-camps/1");
    assert_eq!(
        (c.node.as_str(), c.platform.as_str(), c.arch.as_str()),
        ("v22.23.4", "linux", "x64")
    );
    let fixed = crate::mapgen::coliseum::fixed_layout();
    let zones = ZoneContext::new(&fixed);
    let context = CampContext::new(&zones);
    for f in c.cases {
        let initial = if f.attempt == 0 {
            f.seed as f64
        } else {
            f.seed as f64 + f.attempt as f64 * 2654435761.0
        };
        assert_eq!(
            (((initial % 4294967296.0) + 4294967296.0) % 4294967296.0) as u32,
            f.initial_u32
        );
        let mut rng = Mulberry32::new(f.initial_u32);
        let (pillar, order, words) = prefix(&mut rng, fixed.pillars.len());
        assert_eq!(pillar, f.pillar);
        assert_eq!(order, f.order);
        assert_eq!(words, f.prefix_words);
        let mut partition_words = Vec::new();
        let part = super::super::partition_zones(&zones, &mut || {
            let w = rng.next_u32();
            partition_words.push(w);
            f64::from(w) / 4294967296.0
        });
        assert_eq!(
            partition_words, f.partition_words,
            "partition seed {} attempt {}",
            f.seed, f.attempt
        );
        let mut words = Vec::new();
        let result = part.as_ref().and_then(|part| {
            place_camps(
                &context,
                &mut || {
                    let w = rng.next_u32();
                    words.push(w);
                    f64::from(w) / 4294967296.0
                },
                part,
            )
        });
        assert_eq!(words.len(), f.draws);
        assert_eq!(words, f.words, "camp draws seed {}", f.seed);
        assert_eq!(
            (0..8).map(|_| rng.next_u32()).collect::<Vec<_>>(),
            f.continuation
        );
        if let Some(part) = part {
            assert_eq!(Some(part.zone), f.zone);
            assert_eq!(Some(part.cost), f.cost);
        } else {
            assert!(f.zone.is_none() && f.cost.is_none());
        }
        if let Some(layout) = result {
            assert_eq!(
                Some(&layout.camps),
                f.camps.as_ref(),
                "camps seed {}",
                f.seed
            );
            assert_eq!(
                Some(&layout.territory_grid),
                f.territory_grid.as_ref(),
                "territories seed {}",
                f.seed
            );
            assert!(layout.camps.iter().all(|camp| camp.spots.is_empty()));
            assert_eq!(
                Some(territory_reach2(
                    &zones,
                    &layout.camps,
                    &layout.territory_grid,
                    None
                )),
                f.reach_all
            );
            let tiles: Vec<u8> = (0..N)
                .map(|i| {
                    if i % 13 == 0 {
                        1
                    } else if i % 17 == 0 {
                        2
                    } else {
                        0
                    }
                })
                .collect();
            assert_eq!(
                Some(territory_reach2(
                    &zones,
                    &layout.camps,
                    &layout.territory_grid,
                    Some(&tiles)
                )),
                f.reach_walkable
            );
            assert_eq!(
                Some(territory_reach2(
                    &zones,
                    &layout.camps,
                    &layout.territory_grid,
                    Some(&[])
                )),
                f.reach_empty_mask
            );
            assert_eq!(
                Some(territory_reach2(&zones, &layout.camps, &[], None)),
                f.reach_empty_territory
            );
        } else {
            assert!(
                f.camps.is_none() && f.territory_grid.is_none(),
                "failed seed {}",
                f.seed
            );
        }
    }
}
#[test]
fn bounded_real_scan_matches_camp_admission_and_stream_consumption() {
    let c = corpus();
    assert!((32..=128).contains(&c.scan.len()));
    let f = crate::mapgen::coliseum::fixed_layout();
    let zones = ZoneContext::new(&f);
    let context = CampContext::new(&zones);
    for (index, s) in c.scan.iter().enumerate() {
        assert_eq!(index as u32, s.seed);
        let mut rng = Mulberry32::new(s.seed);
        prefix(&mut rng, f.pillars.len());
        let mut draws = 0;
        let part = super::super::partition_zones(&zones, &mut || {
            draws += 1;
            rng.next_f64()
        });
        assert_eq!(draws, s.partition_draws);
        draws = 0;
        let camps = part.as_ref().and_then(|p| {
            place_camps(
                &context,
                &mut || {
                    draws += 1;
                    rng.next_f64()
                },
                p,
            )
        });
        assert_eq!(camps.is_some(), s.success, "seed {}", s.seed);
        assert_eq!(draws, s.draws, "seed {}", s.seed);
    }
}
#[test]
fn rule_visitor_covers_all_fields_and_runtime_rebuilds_derived_geometry() {
    fn words(d: &CampDefinition) -> Vec<u64> {
        let mut out = Vec::new();
        visit_content(d, |w| out.push(w));
        out
    }
    let d = default_definition();
    let original = words(&d);
    assert_eq!(original.len(), 13);
    macro_rules! changed {
        ($field:ident,$value:expr) => {{
            let mut next = d;
            next.$field = $value;
            assert_ne!(words(&next), original, stringify!($field));
        }};
    }
    changed!(camps_per_zone, d.camps_per_zone + 1);
    changed!(elites_per_zone, d.elites_per_zone - 1);
    changed!(elite_min_sep, d.elite_min_sep + 1.0);
    changed!(camp_gap, d.camp_gap + 1.0);
    changed!(territory_radius_max, d.territory_radius_max + 1.0);
    changed!(candidates, d.candidates + 1);
    changed!(lloyd_steps, d.lloyd_steps + 1);
    changed!(tries, d.tries + 1);
    changed!(edge_clear, d.edge_clear + 1);
    changed!(halo_window, d.halo_window + 1);
    changed!(halo_ring_margin, d.halo_ring_margin + 1);
    changed!(halo_dt, d.halo_dt + 1);
    changed!(final_from_step, d.final_from_step + 1);
    let f = crate::mapgen::coliseum::fixed_layout();
    let zones = ZoneContext::new(&f);
    let context = CampContext::new(&zones);
    let mut changed = d;
    changed.edge_clear += 1;
    let different = CampContext::from_definition(&zones, &changed);
    assert_ne!(context.halo_ok, different.halo_ok);
    let fixture = corpus()
        .cases
        .into_iter()
        .find(|f| f.seed == 1 && f.attempt == 0)
        .unwrap();
    let part = ZonePartition {
        zone: fixture.zone.unwrap(),
        cost: fixture.cost.unwrap(),
    };
    let mut changed = d;
    changed.candidates = 1;
    let custom = CampContext::from_definition(&zones, &changed);
    let mut rng = Mulberry32::new(fixture.initial_u32);
    for _ in 0..fixture.prefix_words.len() + fixture.partition_words.len() {
        rng.next_u32();
    }
    let mut draws = 0;
    place_camps(
        &custom,
        &mut || {
            draws += 1;
            rng.next_f64()
        },
        &part,
    );
    assert_ne!(draws, fixture.draws);
    let mut changed = d;
    changed.tries = 0;
    let no_tries = CampContext::from_definition(&zones, &changed);
    let mut draws = 0;
    assert!(place_camps(
        &no_tries,
        &mut || {
            draws += 1;
            0.5
        },
        &part
    )
    .is_none());
    assert_eq!(draws, 0);
}
#[test]
fn shared_radius_halo_geometry_matches_literal_source_bounds() {
    let fixed = crate::mapgen::coliseum::fixed_layout();
    for radius in [0.0, 2.5, 4.5] {
        let mut rules = super::super::default_definition();
        rules.camp_radius = radius;
        let zones = ZoneContext::from_definition(&fixed, &rules);
        let context = CampContext::new(&zones);
        let mut clear = Vec::new();
        for y in -4i32..=4 {
            for x in -4i32..=4 {
                if f64::from(x * x + y * y) <= radius * radius {
                    clear.push((x, y));
                }
            }
        }
        let mut halo = Vec::new();
        for y in -4i32..=4 {
            for x in -4i32..=4 {
                if clear
                    .iter()
                    .any(|&(a, b)| (a - x).abs() <= 1 && (b - y).abs() <= 1)
                {
                    halo.push(y * W as i32 + x);
                }
            }
        }
        assert_eq!(context.halo_offsets, halo, "radius {radius}");
        let mut camp_ok = vec![false; N];
        for y in 3..=H - 4 {
            for x in 3..=W - 4 {
                let i = y * W + x;
                camp_ok[i] = fixed.painted_mask[i] != 0 && fixed.dist_fp[i] > 4;
            }
        }
        let mut expected = vec![false; N];
        for y in 4..H - 4 {
            for x in 4..W - 4 {
                let i = y * W + x;
                expected[i] = halo
                    .iter()
                    .all(|&off| camp_ok.get((i as i32 + off) as usize) == Some(&true));
            }
        }
        assert_eq!(context.halo_ok, expected, "radius {radius}");
    }
}
#[test]
fn hull_preserves_y_x_orientation_collinear_removal_and_empty_singletons() {
    assert!(hull(&[]).is_empty());
    assert_eq!(hull(&[161]), [161]);
    assert_eq!(hull(&[161, 162, 163]), [161, 163]);
    assert_eq!(hull(&[161, 162, 321, 322]), [161, 321, 322, 162]);
}
/// One partition and today's camps on it (`place_camps`), the first seed from 1 on which both exist, and the stream
/// state right after the partition (so the reserving placement draws from the same point).
fn a_partition_and_its_camps(
    zones: &ZoneContext<'_>,
    context: &CampContext<'_, '_>,
) -> (ZonePartition, CampLayout, Mulberry32) {
    for seed in 1..64 {
        let mut rng = Mulberry32::new(seed);
        let Some(part) = super::super::partition_zones(zones, &mut || rng.next_f64()) else {
            continue;
        };
        let after = rng.clone();
        if let Some(layout) = place_camps(context, &mut || rng.next_f64(), &part) {
            return (part, layout, after);
        }
    }
    panic!("no partition with camps on seeds 1-63");
}
/// P-MAPGEN-SIG-HOLY-1: a site may pin several camps. Pinned camps keep their tiles, in order, as the zone's first labels
/// (1, 2); with the site's `elite` the first is the zone's first elite, without it the zone's elites follow today's rule
/// among all its camps (the farthest from the centre, then the farthest of the rest at least `elite_min_sep` from it);
/// two pinned camps closer than the camp gap refuse the round.
#[test]
fn a_site_pins_two_camps_and_its_elite_flag_chooses_the_elite_rule() {
    let fixed = crate::mapgen::coliseum::fixed_layout();
    let zones = ZoneContext::new(&fixed);
    let context = CampContext::new(&zones);
    let d = &context.rules;
    let (part, today, stream) = a_partition_and_its_camps(&zones, &context);
    // Two of zone 1's own camps today: legal tiles, spaced at least the gap.
    let pins: Vec<usize> = today
        .camps
        .iter()
        .filter(|c| c.zone == 1)
        .take(2)
        .map(|c| c.site)
        .collect();
    let site = |camps: Vec<usize>, elite: bool| Reserve {
        zone: 1,
        x0: pins[0] % W,
        y0: pins[0] / W,
        x1: pins[0] % W,
        y1: pins[0] / W,
        camps,
        elite,
    };
    let centre = CY as usize * W + CX as usize;
    let mut placed = 0;
    for elite in [false, true] {
        let mut rng = stream.clone();
        let Some(layout) = place_camps_reserving(
            &context,
            &mut || rng.next_f64(),
            &part,
            &[site(pins.clone(), elite)],
        ) else {
            continue;
        };
        placed += 1;
        let zone1: Vec<&CampSite> = layout.camps.iter().filter(|c| c.zone == 1).collect();
        assert_eq!(zone1.len(), d.camps_per_zone);
        assert_eq!(
            (zone1[0].site, zone1[1].site),
            (pins[0], pins[1]),
            "pinned, in order"
        );
        let elites: Vec<usize> = (0..zone1.len()).filter(|&k| zone1[k].elite).collect();
        if elite {
            assert!(zone1[0].elite, "the pinned elite camp is an elite");
        } else {
            let mut by_far: Vec<usize> = (0..zone1.len()).collect();
            by_far.sort_by(|&a, &b| {
                d2i(zone1[b].site, centre)
                    .cmp(&d2i(zone1[a].site, centre))
                    .then(a.cmp(&b))
            });
            let first = by_far[0];
            let second = by_far[1..]
                .iter()
                .copied()
                .find(|&c| {
                    f64::from(d2i(zone1[c].site, zone1[first].site))
                        >= d.elite_min_sep * d.elite_min_sep
                })
                .unwrap_or(by_far[1]);
            let mut want = vec![first, second];
            want.truncate(d.elites_per_zone);
            want.sort_unstable();
            assert_eq!(elites, want, "today's elite rule among all the zone's camps");
        }
    }
    assert!(placed > 0, "no reserving placement succeeded");
    // Two pinned camps, each a legal camp tile, closer than the camp gap.
    let near = (0..N)
        .find(|&t| {
            context.halo_ok[t]
                && t != pins[0]
                && f64::from(d2i(t, pins[0])) < d.camp_gap * d.camp_gap
        })
        .expect("a legal tile near the first pin");
    let mut rng = stream.clone();
    let close = vec![pins[0], near];
    assert!(place_camps_reserving(
        &context,
        &mut || rng.next_f64(),
        &part,
        &[site(close, false)]
    )
    .is_none());
}
