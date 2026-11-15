//! P-LAND-FROST-1: the hand-off's per-seed measurements (brief section 13), printed, never asserted. Ignored; run with
//! `cargo test -p rh-sim --features trusted-status-probes --release --lib style_rime::report -- --ignored --nocapture`.
//! Option on, no decor, 16 attempts, seeds 1-128; the definitions are `tests/mapgen_land_frost.rs`'s (`targets`). The
//! counted floors, the ice share and the spires and headlands are printed by the oracle itself.
use super::targets::{dry_way_holds, lake_sizes, mouth_lake_holds, second_lake_holds};
use crate::constants::TILE;
use crate::mapdata::Zone;
use crate::mapgen::landmarks::signature::{frame::LandmarkFrame, SignatureContext};
use crate::mapgen::landmarks::{recipes, stage};
use crate::mapgen::{attempt, coliseum, pipeline};
use std::collections::BTreeMap;

/// The Frost frame and whether the cave's room was recorded, for an accepted attempt.
fn frame(fixed: &coliseum::FixedLayout, seed: i64, n: u64) -> (LandmarkFrame, usize) {
    let context = attempt::AttemptContext::new(fixed);
    let f = attempt::begin_attempt_signature(&context, seed, n, true)
        .expect("prefix input")
        .expect("accepted attempt");
    let mut st = stage::StageContext::new(fixed)
        .apply(&f, &recipes::Recipes::default())
        .expect("stage input")
        .expect("accepted attempt");
    let frame = SignatureContext::new(fixed)
        .apply(&f, &mut st)
        .expect("signature input")
        .expect("accepted attempt")
        .frames
        .into_iter()
        .find(|f| f.zone() == Zone::Frost)
        .expect("a Frost frame");
    (frame, f.state.fans.len())
}

fn label(r: &pipeline::Refusal) -> String {
    let s = format!("{r:?}");
    match s.split_once("rule: \"") {
        Some((_, rest)) => rest.split('"').next().unwrap_or_default().to_string(),
        None => s
            .split([' ', '(', '{'])
            .next()
            .unwrap_or_default()
            .to_string(),
    }
}

#[test]
#[ignore = "report: run by hand for the hand-off"]
fn print_frost_measurements() {
    let fixed = coliseum::fixed_layout();
    let g = pipeline::Pipeline::new(&fixed);
    let on = pipeline::Options {
        decor: false,
        signature: true,
        ..pipeline::Options::default()
    };
    let mut refusals: BTreeMap<String, usize> = BTreeMap::new();
    let mut attempts = [0u64; 3];
    let mut worst = (0, 0);
    let (mut nn_sum, mut nn_n, mut nn_min) = (0.0, 0usize, f64::MAX);
    println!("seed attempt lake others mouth way second fans refusals");
    for seed in 1..=128i64 {
        let mut refused = Vec::new();
        for n in 0..16 {
            let (r, _) = g.styled_attempt_with_trace(seed, n, on).expect("generator");
            let Some(m) = r.map else {
                let l = label(&r.refusal.expect("a refusal says why"));
                *refusals.entry(l.clone()).or_default() += 1;
                refused.push(l);
                continue;
            };
            for (k, bound) in [32, 64, 128].into_iter().enumerate() {
                if seed <= bound {
                    attempts[k] += n + 1;
                }
            }
            worst = worst.max((n, seed));
            let (f, fans) = frame(&fixed, seed, n);
            let (lake, others) = lake_sizes(&m, &f).expect("a line");
            let camps: Vec<(f64, f64)> = m
                .camps
                .iter()
                .filter(|c| c.zone == Zone::Frost)
                .map(|c| (c.x / TILE, c.y / TILE))
                .collect();
            for (i, a) in camps.iter().enumerate() {
                let near = camps
                    .iter()
                    .enumerate()
                    .filter(|&(j, _)| j != i)
                    .map(|(_, b)| {
                        crate::math::sqrt((a.0 - b.0) * (a.0 - b.0) + (a.1 - b.1) * (a.1 - b.1))
                    })
                    .fold(f64::MAX, f64::min);
                if near < f64::MAX {
                    nn_sum += near;
                    nn_n += 1;
                    nn_min = nn_min.min(near);
                }
            }
            println!(
                "{seed} {n} {lake} {others:?} {} {} {} {fans} {refused:?}",
                u8::from(mouth_lake_holds(&m, &f)),
                u8::from(dry_way_holds(&m, &f)),
                u8::from(second_lake_holds(&m, &f)),
            );
            break;
        }
    }
    println!("attempts 1-32 / 1-64 / 1-128: {attempts:?}; worst (attempt, seed) {worst:?}");
    println!("refusals by label: {refusals:?}");
    println!(
        "Frost camp nearest neighbour: mean {:.2}, min {:.1} (tiles)",
        nn_sum / nn_n.max(1) as f64,
        nn_min
    );
}
