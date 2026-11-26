//! `balance_model` (P-BAL-EVAL-1): the value model's verdicts as a pure function of a `balance_lab` report and the design
//! intent in `tools/balance-lab/inputs.json`. Formulas: `docs/QUEST_VALUE_MODEL.md` and the workbook sheet
//! `Augment_Value`; see `docs/backlog/packets/P-BAL-EVAL-1.md`. Anything the report does not measure is `None` with a
//! reason in `not_measured`, never a zero. Shares nothing with the bot's price table. No I/O, no clock.
use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;

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
#[derive(Clone, Copy, Debug, PartialEq, Deserialize)]
pub struct Band {
    pub low: f64,
    pub high: f64,
}

/// The chance to beat the timer, by difficulty class.
#[derive(Clone, Copy, Debug, PartialEq, Deserialize)]
pub struct Targets {
    pub easy: f64,
    pub medium: f64,
    pub hard: f64,
}

/// Failure cost of a quest attempt.
#[derive(Clone, Copy, Debug, Deserialize)]
pub struct Failure {
    pub downtime_secs: f64,
    pub failed_attempt_share: f64,
}

/// One value per helper count.
#[derive(Clone, Copy, Debug, Deserialize)]
pub struct PerHelpers {
    pub solo: f64,
    pub duo: f64,
    pub trio: f64,
}

/// The elite's cost, labelled as assumed until the lab measures it.
#[derive(Clone, Copy, Debug, Deserialize)]
pub struct EliteAssumptions {
    pub pick_minute: f64,
    pub time_ce: PerHelpers,
    pub death_chance: PerHelpers,
}

/// A quest's reward descriptor, an enum by `kind`. Parsed by hand (`parse_reward`): serde cannot refuse an unknown field on
/// an internally tagged enum, and the errors must name the quest.
#[derive(Clone, Debug, PartialEq)]
pub enum Reward {
    /// Stacks now plus a farming dividend; `upfront_stacks` are paid at the trade and belong to the price.
    PermanentBuff {
        direct_stacks: f64,
        damage_fraction: f64,
        move_fraction: f64,
        upfront_stacks: f64,
    },
    /// A single-use spell (the default `spell_type`): its duel equivalent alone.
    SingleUse { duel_equivalent_ce: Option<f64> },
    /// A reusable spell: `Delta_CD` plus a farming dividend (QUEST_VALUE_MODEL 4.3).
    Reusable {
        duel_equivalent_ce: Option<f64>,
        farm_rate_gain: Option<f64>,
        cap_camps_per_minute: Option<f64>,
    },
    /// Any other kind: kept, not priced.
    Unpriced { kind: String },
}

fn reward_number(
    id: &str,
    rw: &serde_json::Map<String, serde_json::Value>,
    f: &str,
    required: bool,
) -> Result<Option<f64>, String> {
    match rw.get(f) {
        None | Some(serde_json::Value::Null) if !required => Ok(None),
        Some(x) if x.is_number() => Ok(x.as_f64()),
        _ => Err(format!("quest {id}: reward is missing field `{f}`")),
    }
}

fn parse_reward(id: &str, raw: &serde_json::Value) -> Result<Reward, String> {
    let Some(rw) = raw.as_object() else {
        return Err(format!("quest {id}: reward must be an object"));
    };
    let Some(kind) = rw.get("kind").and_then(|k| k.as_str()) else {
        return Err(format!("quest {id}: reward is missing field `kind`"));
    };
    let spell_type = if kind == "spell" {
        match rw.get("spell_type").map(|t| t.as_str()) {
            None => "single_use",
            Some(Some(t @ ("single_use" | "reusable"))) => t,
            Some(_) => {
                return Err(format!(
                    "quest {id}: reward `spell_type` must be single_use or reusable"
                ))
            }
        }
    } else {
        ""
    };
    let allowed: &[&str] = match (kind, spell_type) {
        ("permanent_buff", _) => &[
            "direct_stacks",
            "damage_fraction",
            "move_fraction",
            "upfront_stacks",
        ],
        ("spell", "reusable") => &[
            "duel_equivalent_ce",
            "farm_rate_gain",
            "cap_camps_per_minute",
        ],
        ("spell", _) => &["duel_equivalent_ce"],
        _ => &[],
    };
    for f in rw.keys() {
        let common = f == "kind" || f == "source" || (f == "spell_type" && kind == "spell");
        if !common && !allowed.contains(&f.as_str()) {
            return Err(format!(
                "quest {id}: reward field `{f}` is not read by kind {kind:?}{}",
                if spell_type.is_empty() {
                    String::new()
                } else {
                    format!(" ({spell_type})")
                }
            ));
        }
    }
    let num = |f: &str, req: bool| reward_number(id, rw, f, req);
    let positive = |f: &str, x: Option<f64>| match x {
        Some(v) if v <= 0. || !v.is_finite() => {
            Err(format!("quest {id}: reward `{f}` must be above 0"))
        }
        _ => Ok(x),
    };
    Ok(match (kind, spell_type) {
        ("permanent_buff", _) => {
            let upfront = num("upfront_stacks", false)?.unwrap_or(0.);
            if upfront < 0. {
                return Err(format!(
                    "quest {id}: reward `upfront_stacks` must not be negative"
                ));
            }
            Reward::PermanentBuff {
                direct_stacks: num("direct_stacks", true)?.unwrap_or(0.),
                damage_fraction: num("damage_fraction", true)?.unwrap_or(0.),
                move_fraction: num("move_fraction", true)?.unwrap_or(0.),
                upfront_stacks: upfront,
            }
        }
        ("spell", "reusable") => Reward::Reusable {
            duel_equivalent_ce: num("duel_equivalent_ce", false)?,
            farm_rate_gain: positive("farm_rate_gain", num("farm_rate_gain", false)?)?,
            cap_camps_per_minute: positive(
                "cap_camps_per_minute",
                num("cap_camps_per_minute", false)?,
            )?,
        },
        ("spell", _) => Reward::SingleUse {
            duel_equivalent_ce: num("duel_equivalent_ce", false)?,
        },
        _ => Reward::Unpriced {
            kind: kind.to_string(),
        },
    })
}

/// A quest's design class and reward.
#[derive(Clone, Debug, Default)]
pub struct QuestInput {
    pub difficulty: String,
    pub reward: Option<Reward>,
}

#[derive(Default, Deserialize)]
#[serde(default)]
struct RawQuest {
    difficulty: String,
    reward: Option<serde_json::Value>,
}

fn deserialize_quests<'de, D: serde::Deserializer<'de>>(
    d: D,
) -> Result<BTreeMap<String, QuestInput>, D::Error> {
    let raw: BTreeMap<String, RawQuest> = BTreeMap::deserialize(d)?;
    let mut out = BTreeMap::new();
    for (id, q) in raw {
        let reward = match q.reward.as_ref().filter(|r| !r.is_null()) {
            None => None,
            Some(r) => Some(parse_reward(&id, r).map_err(serde::de::Error::custom)?),
        };
        out.insert(
            id,
            QuestInput {
                difficulty: q.difficulty,
                reward,
            },
        );
    }
    Ok(out)
}

/// The farm rate every row is also computed at (the workbook's `c`), with where it comes from.
#[derive(Clone, Debug, Deserialize)]
pub struct ReferenceFarmRate {
    pub camps_per_minute: f64,
    #[serde(default)]
    pub source: String,
}

/// An augment's design numbers (workbook `Augment_Value`).
#[derive(Clone, Debug, Deserialize)]
#[serde(default)]
pub struct AugmentInput {
    pub fight_stacks_now: f64,
    pub one_shot_stacks: f64,
    pub farm_speed_pct: f64,
    pub income_mult: f64,
    pub hazard_mult: f64,
    pub cap_stacks: f64,
    pub growth: String,
    /// Readout units of the card's native per stack (needed when `growth` is "measured").
    pub readout_per_stack: Option<f64>,
}
impl Default for AugmentInput {
    fn default() -> Self {
        AugmentInput {
            fight_stacks_now: 0.,
            one_shot_stacks: 0.,
            farm_speed_pct: 0.,
            income_mult: 1.,
            hazard_mult: 1.,
            cap_stacks: 0.,
            growth: "none".to_string(),
            readout_per_stack: None,
        }
    }
}

/// Design intent (`tools/balance-lab/inputs.json`). Every design number is required: nothing here has a default.
#[derive(Clone, Debug, Deserialize)]
pub struct Inputs {
    #[serde(default)]
    pub schema: String,
    pub band: Band,
    pub premium: f64,
    pub stacks_per_camp: f64,
    pub coliseum_start_minute: f64,
    pub difficulty_targets: Targets,
    pub failure: Failure,
    pub elite_assumptions: EliteAssumptions,
    #[serde(default)]
    pub reference_farm_rate: Option<ReferenceFarmRate>,
    #[serde(default, deserialize_with = "deserialize_quests")]
    pub quests: BTreeMap<String, QuestInput>,
    #[serde(default)]
    pub augments: BTreeMap<String, AugmentInput>,
}

/// True when `s` is `prefix` followed by nothing or a non-digit.
fn schema_is(s: &str, prefix: &str) -> bool {
    s.strip_prefix(prefix)
        .is_some_and(|rest| !rest.starts_with(|c: char| c.is_ascii_digit()))
}

/// Parses the inputs file.
pub fn parse_inputs(json: &str) -> Result<Inputs, String> {
    let i: Inputs = serde_json::from_str(json).map_err(|e| format!("inputs: {e}"))?;
    if !schema_is(&i.schema, "balance-inputs/1") {
        return Err(format!("inputs: unsupported schema {:?}", i.schema));
    }
    if i.stacks_per_camp <= 0. {
        return Err("inputs: stacks_per_camp must be positive".to_string());
    }
    if let Some(r) = &i.reference_farm_rate {
        if r.camps_per_minute.is_nan()
            || r.camps_per_minute <= 0.
            || !r.camps_per_minute.is_finite()
        {
            return Err("inputs: reference_farm_rate.camps_per_minute must be above 0".to_string());
        }
    }
    for (card, a) in &i.augments {
        if a.growth != "none" && a.growth != "measured" {
            return Err(format!(
                "inputs: augment {card}: unknown growth {:?} (none or measured)",
                a.growth
            ));
        }
        if let Some(x) = a.readout_per_stack {
            if x.is_nan() || x <= 0. || !x.is_finite() {
                return Err(format!(
                    "inputs: augment {card}: readout_per_stack must be above 0"
                ));
            }
        }
        if a.growth == "measured" && a.readout_per_stack.is_none() {
            return Err(format!(
                "inputs: augment {card}: growth \"measured\" needs readout_per_stack"
            ));
        }
    }
    Ok(i)
}

// ---- the report, read tolerantly: every field optional, unknown fields ignored ----

#[derive(Default, Deserialize)]
#[serde(default)]
struct Sum {
    sum: Option<f64>,
    n: Option<f64>,
    samples: Option<Vec<f64>>,
}
impl Sum {
    fn mean(&self) -> Option<f64> {
        match (self.sum, self.n) {
            (Some(s), Some(n)) if n > 0. => Some(s / n),
            _ => None,
        }
    }
}

#[derive(Default, Deserialize)]
#[serde(default)]
struct ByQuest {
    quest: String,
    attempts: Option<f64>,
    completed: Option<f64>,
    complete_secs: Option<Sum>,
}

#[derive(Default, Deserialize)]
#[serde(default)]
struct QuestsBlock {
    by_quest: Vec<ByQuest>,
    accepted_by_minute: BTreeMap<String, f64>,
}

#[derive(Default, Deserialize)]
#[serde(default)]
struct DeathMinute {
    player_ticks: Option<f64>,
}

#[derive(Default, Deserialize)]
#[serde(default)]
struct Deaths {
    total: Option<f64>,
    by_minute: BTreeMap<String, DeathMinute>,
}

#[derive(Default, Deserialize)]
#[serde(default)]
struct Native {
    holders: Option<f64>,
    value_sum: Option<f64>,
}

#[derive(Default, Deserialize)]
#[serde(default)]
struct MinuteSample {
    samples: Option<f64>,
    farm_stacks: BTreeMap<String, f64>,
    native: BTreeMap<String, Native>,
}

#[derive(Default, Deserialize)]
#[serde(default)]
struct World {
    camps_credited: Option<f64>,
    kill_secs: Option<Sum>,
    heal_secs: Option<Sum>,
    walk_secs: Option<Sum>,
    player_deaths: Option<Deaths>,
    minutes: BTreeMap<String, MinuteSample>,
}

#[derive(Default, Deserialize)]
#[serde(default)]
struct Report {
    schema: String,
    world: World,
    quests: QuestsBlock,
}

// ---- output ----

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
    #[serde(skip_serializing_if = "Option::is_none")]
    pub value_at_reference: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub price_at_reference: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub ratio_at_reference: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub verdict_at_reference: Option<Verdict>,
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
    #[serde(skip_serializing_if = "Option::is_none")]
    pub value_at_reference: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub ratio_solo_at_reference: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub ratio_duo_at_reference: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub ratio_trio_at_reference: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub meets_rule_at_reference: Option<bool>,
    pub not_measured: Vec<String>,
}

/// The quantities measured from the report that the verdicts use.
#[derive(Clone, Debug, Default, Serialize)]
pub struct Measured {
    pub player_minutes: Option<f64>,
    pub c_eff: Option<f64>,
    pub kappa: Option<f64>,
    pub walk_share: Option<f64>,
    pub deaths_per_player_minute: Option<f64>,
    pub banked_ce: Option<f64>,
    pub pickup_minute: Option<f64>,
}

/// The evaluator's output.
#[derive(Clone, Debug, Default, Serialize)]
pub struct Evaluation {
    pub schema: String,
    pub needed: Needed,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub needed_at_reference: Option<Needed>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub reference_farm_rate: Option<f64>,
    pub quests: Vec<QuestVerdict>,
    pub augments: Vec<AugmentVerdict>,
    /// Numbers taken from `inputs.json` as design assumptions, not measured.
    pub assumed: Vec<String>,
    pub measured: Measured,
    /// Report-wide quantities that could not be measured, with the reason.
    pub not_measured: Vec<String>,
}

// ---- measurement ----

fn minute_of(key: &str) -> Option<u32> {
    key.parse().ok()
}

fn measure(r: &Report, inp: &Inputs, nm: &mut Vec<String>) -> Measured {
    let mut m = Measured::default();
    let w = &r.world;
    let ticks: Vec<f64> = w
        .player_deaths
        .iter()
        .flat_map(|d| d.by_minute.values())
        .filter_map(|b| b.player_ticks)
        .collect();
    if ticks.is_empty() {
        nm.push("player_minutes: no world.player_deaths.by_minute[].player_ticks".into());
    } else {
        let pm = ticks.iter().sum::<f64>() / 1200.;
        if pm > 0. {
            m.player_minutes = Some(pm);
        } else {
            nm.push("player_minutes: zero player ticks".into());
        }
    }
    match (m.player_minutes, w.camps_credited) {
        (Some(pm), Some(c)) => m.c_eff = Some(c / pm),
        _ => nm.push("c_eff: needs camps_credited and player minutes".into()),
    }
    match (
        m.player_minutes,
        w.player_deaths.as_ref().and_then(|d| d.total),
    ) {
        (Some(pm), Some(t)) => m.deaths_per_player_minute = Some(t / pm),
        _ => nm.push("mu: needs player_deaths.total and player minutes".into()),
    }
    let mean = |s: &Option<Sum>| s.as_ref().and_then(Sum::mean);
    match (mean(&w.kill_secs), mean(&w.heal_secs), mean(&w.walk_secs)) {
        (Some(k), Some(h), Some(wk)) if k + h + wk > 0. => {
            m.kappa = Some(k / (k + h + wk));
            m.walk_share = Some(wk / (k + h + wk));
        }
        _ => nm.push("kappa: needs kill, heal and walk seconds with samples".into()),
    }
    let pick = inp.elite_assumptions.pick_minute;
    let at = r
        .world
        .minutes
        .iter()
        .find(|(k, _)| minute_of(k).map(f64::from) == Some(pick));
    match at {
        Some((_, s)) if s.samples.unwrap_or(0.) > 0. => {
            let stacks: f64 = s.farm_stacks.values().sum();
            m.banked_ce = Some(stacks / s.samples.unwrap_or(1.) / inp.stacks_per_camp);
        }
        _ => nm.push(format!("banked_ce: no samples at minute {pick}")),
    }
    let (mut num, mut den) = (0., 0.);
    for (k, v) in &r.quests.accepted_by_minute {
        if let Some(mi) = minute_of(k) {
            num += f64::from(mi) * v;
            den += v;
        }
    }
    if den > 0. {
        m.pickup_minute = Some(num / den);
    } else {
        nm.push("pickup_minute: no quest acceptances".into());
    }
    m
}

/// Rate multiplier `1 / (1 - share * frac / (1 + frac))` (QUEST_VALUE_MODEL 4.1).
fn rate_gain(frac: f64, share: f64) -> f64 {
    1. / (1. - share * frac / (1. + frac))
}

/// A linear form `a + b * tau` (tau in minutes).
#[derive(Clone, Copy)]
struct Lin {
    a: f64,
    b: f64,
}

/// One quest's price and value at one farm rate `c`.
struct QuestRow {
    price_form: Option<Lin>,
    vform: Option<Lin>,
    value: Option<f64>,
    price: Option<f64>,
    ratio: Option<f64>,
    verdict: Option<Verdict>,
    why: Vec<String>,
}

/// Prices and values a quest with the farm rate `c` wherever the model uses it.
fn quest_row(
    c: Option<f64>,
    p: Option<f64>,
    tau: Option<f64>,
    inp: &Inputs,
    m: &Measured,
    rw: Option<&Reward>,
) -> QuestRow {
    let mut why: Vec<String> = Vec::new();
    // A stack paid at the trade is a certain cost: no premium, no retries, not scaled by the farm rate.
    let up = match rw {
        Some(Reward::PermanentBuff { upfront_stacks, .. }) => upfront_stacks / inp.stacks_per_camp,
        _ => 0.,
    };
    // Price = pi * E[C] + upfront = a + b * tau.
    let price_form = match (p, c) {
        (Some(p), Some(c)) => {
            let retries = (1. - p) / p;
            let l = c * inp.failure.downtime_secs / 60.;
            Some(Lin {
                a: inp.premium * retries * l + up,
                b: inp.premium * c * (1. + retries * inp.failure.failed_attempt_share),
            })
        }
        _ => None,
    };
    let price = price_form.zip(tau).map(|(f, t)| f.a + f.b * t);
    // Value = a + b * tau.
    let mut vform: Option<Lin> = None;
    let r = m.pickup_minute.map(|p| inp.coliseum_start_minute - p);
    match rw {
        None => why.push("value: no reward descriptor in inputs".into()),
        Some(Reward::PermanentBuff {
            direct_stacks,
            damage_fraction,
            move_fraction,
            ..
        }) => {
            let b = direct_stacks / inp.stacks_per_camp;
            let mut g = Some(1.);
            if *damage_fraction > 0. {
                g = g
                    .zip(m.kappa)
                    .map(|(g, k)| g * rate_gain(*damage_fraction, k));
            }
            if *move_fraction > 0. {
                g = g
                    .zip(m.walk_share)
                    .map(|(g, s)| g * rate_gain(*move_fraction, s));
            }
            match (g, c, r) {
                (Some(g), Some(c), Some(r)) => {
                    let k = (g - 1.) * c;
                    vform = Some(Lin {
                        a: b + k * r,
                        b: -k,
                    });
                }
                _ => {
                    why.push("value: needs kappa or walk share, c_eff and the pickup minute".into())
                }
            }
        }
        Some(Reward::SingleUse { duel_equivalent_ce }) => match duel_equivalent_ce {
            Some(d) => vform = Some(Lin { a: *d, b: 0. }),
            None => why.push("value: spell reward has no duel equivalent".into()),
        },
        Some(Reward::Reusable {
            duel_equivalent_ce,
            farm_rate_gain,
            cap_camps_per_minute,
        }) => {
            // V = Delta + (min(g * c, cap) - c) * (R - tau)  (QUEST_VALUE_MODEL 4.3)
            let missing: Vec<&str> = [
                ("duel_equivalent_ce", duel_equivalent_ce.is_none()),
                ("farm_rate_gain", farm_rate_gain.is_none()),
                ("cap_camps_per_minute", cap_camps_per_minute.is_none()),
            ]
            .iter()
            .filter(|(_, none)| *none)
            .map(|(f, _)| *f)
            .collect();
            if !missing.is_empty() {
                why.push(format!(
                    "value: reusable spell reward is missing {}",
                    missing.join(", ")
                ));
            } else if let (Some(d), Some(g), Some(cap), Some(c), Some(r)) = (
                duel_equivalent_ce,
                farm_rate_gain,
                cap_camps_per_minute,
                c,
                r,
            ) {
                let gain = (g * c).min(*cap) - c;
                vform = Some(Lin {
                    a: d + gain * r,
                    b: -gain,
                });
            } else {
                why.push("value: needs c_eff and the pickup minute".into());
            }
        }
        Some(Reward::Unpriced { kind }) => {
            why.push(format!("value: reward kind {kind:?} is not priced"))
        }
    }
    let value = vform.zip(tau).map(|(f, t)| f.a + f.b * t);
    let (mut ratio, mut verdict) = (None, None);
    if let (Some(val), Some(pr)) = (value, price) {
        let rt = val / pr;
        if pr > 0. && rt.is_finite() {
            ratio = Some(rt);
            verdict = Some(if rt < inp.band.low {
                Verdict::BelowBand
            } else if rt > inp.band.high {
                Verdict::AboveBand
            } else {
                Verdict::InBand
            });
        } else {
            why.push("ratio: the price is not above 0, so value over price is not measured".into());
        }
    }
    QuestRow {
        price_form,
        vform,
        value,
        price,
        ratio,
        verdict,
        why,
    }
}

fn quest_verdict(
    id: &str,
    q: &ByQuest,
    inp: &Inputs,
    m: &Measured,
    qi: Option<&QuestInput>,
) -> QuestVerdict {
    let mut v = QuestVerdict {
        quest: id.to_string(),
        ..QuestVerdict::default()
    };
    let mut why: Vec<String> = Vec::new();
    let (attempts, completed) = (q.attempts.unwrap_or(0.), q.completed.unwrap_or(0.));
    let p = if attempts > 0. && completed > 0. {
        Some(completed / attempts)
    } else {
        None
    };
    let cs = q.complete_secs.as_ref();
    let tau = if completed > 0. {
        cs.and_then(Sum::mean).map(|s| s / 60.)
    } else {
        None
    };
    if p.is_none() {
        why.push("p: no completions".into());
    }
    if tau.is_none() {
        why.push("tau: no completion seconds".into());
    }
    if m.c_eff.is_none() {
        why.push("c_eff not measured".into());
    }
    let rw = qi.and_then(|x| x.reward.as_ref());
    let row = quest_row(m.c_eff, p, tau, inp, m, rw);
    why.extend(row.why.iter().cloned());
    let (vform, price_form, price) = (row.vform, row.price_form, row.price);
    v.value = row.value;
    v.price = price;
    v.ratio = row.ratio;
    v.verdict = row.verdict;
    if let Some(rf) = &inp.reference_farm_rate {
        let at = quest_row(Some(rf.camps_per_minute), p, tau, inp, m, rw);
        v.value_at_reference = at.value;
        v.price_at_reference = at.price;
        v.ratio_at_reference = at.ratio;
        v.verdict_at_reference = at.verdict;
    }
    v.reward_value_at_parity = price;
    if let (Some(vf), Some(pf)) = (vform, price_form) {
        // value(t) = price(t)  =>  t = (vf.a - pf.a) / (pf.b - vf.b); the upfront cost is in pf.a
        let denom = pf.b - vf.b;
        let t = (vf.a - pf.a) / denom;
        if denom > 0. && t > 0. {
            v.duration_secs_at_parity = Some(t * 60.);
        } else {
            why.push("duration at parity: no positive duration balances the reward".into());
        }
    }
    match (qi, cs.and_then(|s| s.samples.as_ref())) {
        (None, _) => why.push("timer: quest has no difficulty class in inputs".into()),
        (Some(_), None) => why.push("timer: no completion-time samples in the report".into()),
        (Some(_), Some(s)) if s.is_empty() => {
            why.push("timer: empty completion-time samples".into())
        }
        (Some(qi), Some(s)) => {
            let target = match qi.difficulty.as_str() {
                "easy" => Some(inp.difficulty_targets.easy),
                "medium" => Some(inp.difficulty_targets.medium),
                "hard" => Some(inp.difficulty_targets.hard),
                _ => None,
            };
            match target {
                None => why.push(format!("timer: unknown difficulty {:?}", qi.difficulty)),
                Some(t) => {
                    let mut xs = s.clone();
                    xs.sort_by(f64::total_cmp);
                    // Nearest rank: the smallest sample with at least `t` of the samples at or below it.
                    let rank = ((t * xs.len() as f64 - 1e-9).ceil() as usize).clamp(1, xs.len());
                    v.timer_secs = Some(xs[rank - 1]);
                }
            }
        }
    }
    v.not_measured = why;
    v
}

/// `(1 - e^{-mu r}) / mu`, with its limit `r` at `mu = 0`.
fn a_bar(mu: f64, r: f64) -> f64 {
    if mu.abs() < 1e-12 {
        r
    } else {
        (1. - (-mu * r).exp()) / mu
    }
}

fn needed_for(inp: &Inputs, m: &Measured, c: Option<f64>, nm: &mut Vec<String>) -> Needed {
    let (Some(mu), Some(c), Some(banked)) = (m.deaths_per_player_minute, c, m.banked_ce) else {
        nm.push("needed: requires mu, c_eff and banked_ce".into());
        return Needed::default();
    };
    let ra = (inp.coliseum_start_minute - inp.elite_assumptions.pick_minute).max(0.);
    let risk = banked + c * a_bar(mu, ra);
    let e = &inp.elite_assumptions;
    let f = |t: f64, d: f64| Some(inp.premium * (t + d * risk));
    Needed {
        solo: f(e.time_ce.solo, e.death_chance.solo),
        duo: f(e.time_ce.duo, e.death_chance.duo),
        trio: f(e.time_ce.trio, e.death_chance.trio),
    }
}

/// Mean native value per holder, per minute; slope between the first and last minute with holders.
fn native_growth(r: &Report, card: &str) -> Option<f64> {
    let mut pts: Vec<(u32, f64)> = Vec::new();
    for (k, s) in &r.world.minutes {
        let (Some(mi), Some(n)) = (minute_of(k), s.native.get(card)) else {
            continue;
        };
        if let (Some(h), Some(v)) = (n.holders, n.value_sum) {
            if h > 0. {
                pts.push((mi, v / h));
            }
        }
    }
    pts.sort_by_key(|a| a.0);
    let (first, last) = (pts.first()?, pts.last()?);
    if last.0 == first.0 {
        return None;
    }
    Some((last.1 - first.1) / f64::from(last.0 - first.0))
}

/// One augment's numbers at the farm rate `c`.
struct AugmentRow {
    value: f64,
    solo: Option<f64>,
    duo: Option<f64>,
    trio: Option<f64>,
    meets_rule: Option<bool>,
}

/// `None` when the value is not finite. `growth` is stacks per minute.
fn augment_row(
    a: &AugmentInput,
    growth: f64,
    c: f64,
    mu: f64,
    inp: &Inputs,
    m: &Measured,
    needed: &Needed,
) -> Option<AugmentRow> {
    let ra = (inp.coliseum_start_minute - inp.elite_assumptions.pick_minute).max(0.);
    let g = growth;
    let mm = a.fight_stacks_now + if g > 0. { a.cap_stacks.min(g * ra) } else { 0. };
    let lh = a.hazard_mult;
    let n = (mm + a.one_shot_stacks) / inp.stacks_per_camp * (-mu * lh * ra).exp();
    let kg = m.kappa.map_or(1., |k| {
        1. / (1. - k * a.farm_speed_pct / (100. + a.farm_speed_pct))
    });
    let pp = c * (a.income_mult * kg * a_bar(mu * lh, ra) - a_bar(mu, ra));
    let val = n + pp;
    if !val.is_finite() {
        return None;
    }
    let ratio = |nd: Option<f64>| nd.filter(|x| *x > 0.).map(|x| val / x);
    let (solo, duo, trio) = (ratio(needed.solo), ratio(needed.duo), ratio(needed.trio));
    Some(AugmentRow {
        value: val,
        solo,
        duo,
        trio,
        meets_rule: duo
            .zip(solo)
            .map(|(d, s)| d >= inp.band.low && s <= inp.band.high),
    })
}

fn augment_verdict(
    card: &str,
    a: &AugmentInput,
    r: &Report,
    inp: &Inputs,
    m: &Measured,
    needed: &Needed,
    needed_ref: Option<&Needed>,
) -> AugmentVerdict {
    let mut v = AugmentVerdict {
        card: card.to_string(),
        ..AugmentVerdict::default()
    };
    let growth = if a.growth == "measured" {
        // The card's own readout per minute, in stacks per minute.
        let g = native_growth(r, card)
            .zip(a.readout_per_stack)
            .map(|(g, u)| g / u);
        if g.is_none() {
            v.not_measured
                .push("growth: no native samples with holders at two minutes".into());
        }
        g
    } else {
        Some(0.)
    };
    if a.farm_speed_pct != 0. && m.kappa.is_none() {
        v.not_measured.push("kappa not measured".into());
    }
    if m.c_eff.is_none() || m.deaths_per_player_minute.is_none() {
        v.not_measured.push("c_eff or mu not measured".into());
    }
    if v.not_measured.is_empty() {
        if let (Some(g), Some(c), Some(mu)) = (growth, m.c_eff, m.deaths_per_player_minute) {
            let Some(row) = augment_row(a, g, c, mu, inp, m, needed) else {
                v.not_measured
                    .push("value: not finite for these inputs".into());
                return v;
            };
            v.value = Some(row.value);
            v.ratio_solo = row.solo;
            v.ratio_duo = row.duo;
            v.ratio_trio = row.trio;
            v.meets_rule = row.meets_rule;
            if v.meets_rule.is_none() {
                v.not_measured
                    .push("needed: the elite cost is not measured".into());
            }
            if let (Some(rf), Some(nr)) = (&inp.reference_farm_rate, needed_ref) {
                if let Some(at) = augment_row(a, g, rf.camps_per_minute, mu, inp, m, nr) {
                    v.value_at_reference = Some(at.value);
                    v.ratio_solo_at_reference = at.solo;
                    v.ratio_duo_at_reference = at.duo;
                    v.ratio_trio_at_reference = at.trio;
                    v.meets_rule_at_reference = at.meets_rule;
                }
            }
        }
    }
    v
}

/// Evaluates a lab report (JSON text) against the inputs (JSON text).
pub fn evaluate_json(report: &str, inputs: &str) -> Result<Evaluation, String> {
    let inp = parse_inputs(inputs)?;
    let rep: Report = serde_json::from_str(report).map_err(|e| format!("report: {e}"))?;
    if !schema_is(&rep.schema, "balance-lab/1") {
        return Err(format!("report: unsupported schema {:?}", rep.schema));
    }
    let mut nm = Vec::new();
    let m = measure(&rep, &inp, &mut nm);
    let needed = needed_for(&inp, &m, m.c_eff, &mut nm);
    let reference = inp.reference_farm_rate.as_ref().map(|r| r.camps_per_minute);
    let needed_at_reference = reference.map(|c| needed_for(&inp, &m, Some(c), &mut Vec::new()));
    let mut by: BTreeMap<&str, &ByQuest> = BTreeMap::new();
    for q in &rep.quests.by_quest {
        if by.insert(q.quest.as_str(), q).is_some() {
            return Err(format!("report: duplicate quest id {:?}", q.quest));
        }
    }
    let quests = by
        .iter()
        .map(|(id, q)| quest_verdict(id, q, &inp, &m, inp.quests.get(*id)))
        .collect();
    let augments = inp
        .augments
        .iter()
        .map(|(c, a)| augment_verdict(c, a, &rep, &inp, &m, &needed, needed_at_reference.as_ref()))
        .collect();
    Ok(Evaluation {
        schema: EVAL_SCHEMA.to_string(),
        needed,
        quests,
        augments,
        assumed: vec![
            "elite_assumptions.time_ce (elite time in camp-equivalents, solo/duo/trio)".into(),
            "elite_assumptions.death_chance".into(),
            "elite_assumptions.pick_minute".into(),
            "quest reward descriptors and difficulty classes (design intent)".into(),
        ],
        measured: m,
        not_measured: nm,
        needed_at_reference,
        reference_farm_rate: reference,
    })
}

/// The evaluation as compact JSON, keys in declaration order.
pub fn to_json(e: &Evaluation) -> String {
    serde_json::to_string(e).expect("the evaluation serialises: no non-finite number reaches it")
}

fn fmt(x: Option<f64>) -> String {
    x.map_or("n/m".to_string(), |v| format!("{v:.3}"))
}

/// The evaluation as a table for stderr.
pub fn render_table(e: &Evaluation) -> String {
    let mut s = format!(
        "needed (CE)  solo {}  duo {}  trio {}\n",
        fmt(e.needed.solo),
        fmt(e.needed.duo),
        fmt(e.needed.trio)
    );
    let rf = e.reference_farm_rate;
    if let (Some(rate), Some(n)) = (rf, &e.needed_at_reference) {
        s += &format!(
            "needed at the reference rate (CE)  solo {}  duo {}  trio {}\n",
            fmt(n.solo),
            fmt(n.duo),
            fmt(n.trio)
        );
        s += &format!(
            "rates: measured {} camps/min, reference {rate:.3}; the plain columns use the measured rate, the ref_ columns the reference rate\n",
            fmt(e.measured.c_eff)
        );
    }
    s += "quest                      value   price   ratio  verdict      timer_s  parity_value  parity_secs";
    if rf.is_some() {
        s += "  ref_value ref_price ref_ratio ref_verdict";
    }
    s += "\n";
    for q in &e.quests {
        let verdict = q.verdict.map_or("n/m".to_string(), |v| format!("{v:?}"));
        s += &format!(
            "{:<26} {:>7} {:>7} {:>7}  {:<11} {:>8} {:>13} {:>12}",
            q.quest,
            fmt(q.value),
            fmt(q.price),
            fmt(q.ratio),
            verdict,
            fmt(q.timer_secs),
            fmt(q.reward_value_at_parity),
            fmt(q.duration_secs_at_parity)
        );
        if rf.is_some() {
            let vr = q
                .verdict_at_reference
                .map_or("n/m".to_string(), |v| format!("{v:?}"));
            s += &format!(
                "  {:>9} {:>9} {:>9} {}",
                fmt(q.value_at_reference),
                fmt(q.price_at_reference),
                fmt(q.ratio_at_reference),
                vr
            );
        }
        s += "\n";
        for r in &q.not_measured {
            s += &format!("    not measured: {r}\n");
        }
    }
    s += "augment                    value   solo    duo     trio    meets_rule";
    if rf.is_some() {
        s += "  ref_value ref_solo ref_duo ref_trio ref_meets_rule";
    }
    s += "\n";
    for a in &e.augments {
        let mr = |x: Option<bool>| x.map_or("n/m".to_string(), |b| b.to_string());
        s += &format!(
            "{:<26} {:>7} {:>7} {:>7} {:>7}  {}",
            a.card,
            fmt(a.value),
            fmt(a.ratio_solo),
            fmt(a.ratio_duo),
            fmt(a.ratio_trio),
            mr(a.meets_rule)
        );
        if rf.is_some() {
            s += &format!(
                "  {:>9} {:>8} {:>7} {:>8} {}",
                fmt(a.value_at_reference),
                fmt(a.ratio_solo_at_reference),
                fmt(a.ratio_duo_at_reference),
                fmt(a.ratio_trio_at_reference),
                mr(a.meets_rule_at_reference)
            );
        }
        s += "\n";
        for r in &a.not_measured {
            s += &format!("    not measured: {r}\n");
        }
    }
    for r in &e.not_measured {
        s += &format!("not measured: {r}\n");
    }
    for a in &e.assumed {
        s += &format!("assumed: {a}\n");
    }
    s
}
