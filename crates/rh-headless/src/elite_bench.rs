//! Elite mode of `camp_bench` (packet P-ELITE-BENCH-1): how often a minute-5 build dies soloing an elite camp under the
//! Rework rules, plus the sampler that produces those builds. STUB: the implementer replaces the bodies; the types and
//! signatures are what `tests/elite_bench.rs` compiles against.
//!
//! This file is compiled twice: by `src/bin/camp_bench.rs` under `--features elite-bench` (which turns on rh-sim's
//! `trusted-build-setup`), and by `tests/elite_bench.rs` through `#[path]`, where rh-sim's dev-dependency features
//! provide the same API. It must not be added to `lib.rs` (rh-server depends on rh-headless).
// Stub only: the bin does not call `sample_builds` until the implementer wires `--sample-builds`; remove this then.
#![allow(dead_code)]
use rh_sim::trusted_build::TrustedBuild;
use serde::{Deserialize, Serialize};
use std::ops::Range;

/// Minute 5 at 20 Hz.
pub const MINUTE5_TICK: u32 = 6000;
/// The checked-in sample, relative to the repository root.
pub const SAMPLE_PATH: &str = "tools/balance-camps/minute5_builds.json";

/// One bot's build at the sample tick.
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct BuildSample {
    pub seed: u64,
    pub bot: u32,
    /// `FactionId::as_str` of the bot's faction.
    pub faction: String,
    pub build: TrustedBuild,
}

/// The sample file (`SAMPLE_PATH`).
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct SampleFile {
    pub version: u32,
    pub tick: u32,
    /// Map path as given to `source_soak`, relative to the repository root.
    pub map: String,
    /// Half-open range of match seeds tried.
    pub seeds: [u64; 2],
    /// Seeds whose match ended before `tick` (they contribute no builds).
    pub ended_early: Vec<u64>,
    /// In (seed, bot) order.
    pub builds: Vec<BuildSample>,
}

/// Plays `source_soak`-style live matches (8 `HumanBot`s, `SimConfig::live_content()`) for each seed and reads every
/// bot's build at `tick`. Deterministic.
pub fn sample_builds(_map: &str, _seeds: Range<u64>, _tick: u32) -> Result<SampleFile, String> {
    Err("elite_bench::sample_builds is not implemented".into())
}

/// What a run prints: the JSON report (stdout) and the readable table (stderr).
pub struct Output {
    pub json: String,
    pub table: String,
}

/// The whole elite mode minus the process: `args` are the flags after the program name (`--quick`, `--seed N`,
/// `--seeds N`, `--max-ticks N`, `--builds PATH`, `--control-bare`). `Ok` whenever it reported, even if every fighter
/// died or every fight was cut off; `Err` only for bad arguments or an unreadable sample.
pub fn run(_args: &[String]) -> Result<Output, String> {
    Err("elite_bench::run is not implemented".into())
}
