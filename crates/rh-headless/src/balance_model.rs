//! `balance_model` (P-BAL-EVAL-1): the value model's verdicts as a pure function of a `balance_lab` report and the design
//! intent in `tools/balance-lab/inputs.json`. STUB: the types and signatures the oracle calls; the bodies return empty results.
//! See `docs/backlog/packets/P-BAL-EVAL-1.md` for what each field means and what "not measured" is.
use serde::{Deserialize, Serialize};

/// Schema tag of the evaluator's own output.
pub const EVAL_SCHEMA: &str = "balance-eval/1";

/// A ratio against the band.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
pub enum Verdict {
    BelowBand,
    InBand,
    AboveBand,
}

/// The balanced band of value / price.
#[derive(Clone, Copy, Debug, Default, PartialEq, Deserialize)]
pub struct Band {
    pub low: f64,
    pub high: f64,
}

/// The chance to beat the timer, by difficulty class.
#[derive(Clone, Copy, Debug, Default, PartialEq, Deserialize)]
pub struct Targets {
    pub easy: f64,
    pub medium: f64,
    pub hard: f64,
}

/// Design intent (`tools/balance-lab/inputs.json`). The stub holds only what the oracle reads directly.
#[derive(Clone, Debug, Default, Deserialize)]
#[serde(default)]
pub struct Inputs {
    pub band: Band,
    pub difficulty_targets: Targets,
}

/// Parses the inputs file.
pub fn parse_inputs(_json: &str) -> Result<Inputs, String> {
    Err("not implemented (P-BAL-EVAL-1)".to_string())
}

/// One quest's verdict. An `Option` is `None` exactly when the quantity is not measured; `not_measured` says why.
#[derive(Clone, Debug, Default, Serialize)]
pub struct QuestVerdict {
    pub quest: String,
    pub value: Option<f64>,
    pub price: Option<f64>,
    pub ratio: Option<f64>,
    pub verdict: Option<Verdict>,
    pub timer_secs: Option<f64>,
    pub reward_value_at_parity: Option<f64>,
    pub duration_secs_at_parity: Option<f64>,
    pub not_measured: Vec<String>,
}

/// What an augment must be worth to justify an elite, solo, with one helper and with two (camp-equivalents).
#[derive(Clone, Debug, Default, Serialize)]
pub struct Needed {
    pub solo: Option<f64>,
    pub duo: Option<f64>,
    pub trio: Option<f64>,
}

/// One augment's value over what an elite costs.
#[derive(Clone, Debug, Default, Serialize)]
pub struct AugmentVerdict {
    pub card: String,
    pub value: Option<f64>,
    pub ratio_solo: Option<f64>,
    pub ratio_duo: Option<f64>,
    pub ratio_trio: Option<f64>,
    pub meets_rule: Option<bool>,
    pub not_measured: Vec<String>,
}

/// The evaluator's output.
#[derive(Clone, Debug, Default, Serialize)]
pub struct Evaluation {
    pub schema: String,
    pub needed: Needed,
    pub quests: Vec<QuestVerdict>,
    pub augments: Vec<AugmentVerdict>,
}

/// Evaluates a lab report (JSON text) against the inputs (JSON text).
pub fn evaluate_json(_report: &str, _inputs: &str) -> Result<Evaluation, String> {
    Ok(Evaluation {
        schema: EVAL_SCHEMA.to_string(),
        ..Evaluation::default()
    })
}

/// The evaluation as compact JSON, keys in declaration order.
pub fn to_json(e: &Evaluation) -> String {
    serde_json::to_string(e).unwrap_or_default()
}

/// The evaluation as a table for stderr.
pub fn render_table(_e: &Evaluation) -> String {
    String::new()
}
