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

/// Failure cost of a quest attempt.
#[derive(Clone, Copy, Debug, Deserialize)]
#[serde(default)]
pub struct Failure {
    pub downtime_secs: f64,
    pub failed_attempt_share: f64,
}
impl Default for Failure {
    fn default() -> Self {
        Failure {
            downtime_secs: 15.,
            failed_attempt_share: 1.,
        }
    }
}

/// One value per helper count.
#[derive(Clone, Copy, Debug, Default, Deserialize)]
#[serde(default)]
pub struct PerHelpers {
    pub solo: f64,
    pub duo: f64,
    pub trio: f64,
}

/// The elite's cost, labelled as assumed until the lab measures it.
#[derive(Clone, Copy, Debug, Default, Deserialize)]
#[serde(default)]
pub struct EliteAssumptions {
    pub pick_minute: f64,
    pub time_ce: PerHelpers,
    pub death_chance: PerHelpers,
}

/// A quest's reward descriptor.
#[derive(Clone, Debug, Default, Deserialize)]
#[serde(default)]
pub struct Reward {
    pub kind: String,
    pub direct_stacks: f64,
    pub damage_fraction: f64,
    pub move_fraction: f64,
    pub duel_equivalent_ce: Option<f64>,
}

/// A quest's design class and reward.
#[derive(Clone, Debug, Default, Deserialize)]
#[serde(default)]
pub struct QuestInput {
    pub difficulty: String,
    pub reward: Option<Reward>,
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
        }
    }
}

/// Design intent (`tools/balance-lab/inputs.json`).
#[derive(Clone, Debug, Deserialize)]
#[serde(default)]
pub struct Inputs {
    pub schema: String,
    pub band: Band,
    pub premium: f64,
    pub stacks_per_camp: f64,
    pub coliseum_start_minute: f64,
    pub difficulty_targets: Targets,
    pub failure: Failure,
    pub elite_assumptions: EliteAssumptions,
    pub quests: BTreeMap<String, QuestInput>,
    pub augments: BTreeMap<String, AugmentInput>,
}
impl Default for Inputs {
    fn default() -> Self {
        Inputs {
            schema: String::new(),
            band: Band {
                low: 0.85,
                high: 1.25,
            },
            premium: 1.15,
            stacks_per_camp: 12.,
            coliseum_start_minute: 12.,
            difficulty_targets: Targets {
                easy: 0.95,
                medium: 0.85,
                hard: 0.70,
            },
            failure: Failure::default(),
            elite_assumptions: EliteAssumptions::default(),
            quests: BTreeMap::new(),
            augments: BTreeMap::new(),
        }
    }
}

/// Parses the inputs file.
pub fn parse_inputs(json: &str) -> Result<Inputs, String> {
    let i: Inputs = serde_json::from_str(json).map_err(|e| format!("inputs: {e}"))?;
    if !i.schema.starts_with("balance-inputs/1") {
        return Err(format!("inputs: unsupported schema {:?}", i.schema));
    }
    if i.stacks_per_camp <= 0. {
        return Err("inputs: stacks_per_camp must be positive".to_string());
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
    // Price = pi * E[C] = a + b * tau.
    let price_form = match (p, m.c_eff) {
        (Some(p), Some(c)) => {
            let retries = (1. - p) / p;
            let l = c * inp.failure.downtime_secs / 60.;
            Some(Lin {
                a: inp.premium * retries * l,
                b: inp.premium * c * (1. + retries * inp.failure.failed_attempt_share),
            })
        }
        _ => None,
    };
    let price = price_form.zip(tau).map(|(f, t)| f.a + f.b * t);
    // Value = a + b * tau.
    let mut vform: Option<Lin> = None;
    match qi.and_then(|x| x.reward.as_ref()) {
        None => why.push("value: no reward descriptor in inputs".into()),
        Some(rw) if rw.kind == "permanent_buff" => {
            let b = rw.direct_stacks / inp.stacks_per_camp;
            let mut g = Some(1.);
            if rw.damage_fraction > 0. {
                g = g.zip(m.kappa).map(|(g, k)| g * rate_gain(rw.damage_fraction, k));
            }
            if rw.move_fraction > 0. {
                g = g
                    .zip(m.walk_share)
                    .map(|(g, s)| g * rate_gain(rw.move_fraction, s));
            }
            let r = m.pickup_minute.map(|p| inp.coliseum_start_minute - p);
            match (g, m.c_eff, r) {
                (Some(g), Some(c), Some(r)) => {
                    let k = (g - 1.) * c;
                    vform = Some(Lin {
                        a: b + k * r,
                        b: -k,
                    });
                }
                _ => why.push(
                    "value: needs kappa or walk share, c_eff and the pickup minute".into(),
                ),
            }
        }
        Some(rw) if rw.kind == "spell" => match rw.duel_equivalent_ce {
            Some(d) => vform = Some(Lin { a: d, b: 0. }),
            None => why.push("value: spell reward has no duel equivalent".into()),
        },
        Some(rw) => why.push(format!("value: reward kind {:?} is not priced", rw.kind)),
    }
    v.value = vform.zip(tau).map(|(f, t)| f.a + f.b * t);
    v.price = price;
    if let (Some(val), Some(pr)) = (v.value, price) {
        let ratio = val / pr;
        v.ratio = Some(ratio);
        v.verdict = Some(if ratio < inp.band.low {
            Verdict::BelowBand
        } else if ratio > inp.band.high {
            Verdict::AboveBand
        } else {
            Verdict::InBand
        });
    }
    v.reward_value_at_parity = price;
    if let (Some(vf), Some(pf)) = (vform, price_form) {
        // value(t) = price(t)  =>  t = (vf.a - pf.a) / (pf.b - vf.b)
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

fn needed_for(inp: &Inputs, m: &Measured, nm: &mut Vec<String>) -> Needed {
    let (Some(mu), Some(c), Some(banked)) = (m.deaths_per_player_minute, m.c_eff, m.banked_ce)
    else {
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
    pts.sort_by(|a, b| a.0.cmp(&b.0));
    let (first, last) = (pts.first()?, pts.last()?);
    if last.0 == first.0 {
        return None;
    }
    Some((last.1 - first.1) / f64::from(last.0 - first.0))
}

fn augment_verdict(
    card: &str,
    a: &AugmentInput,
    r: &Report,
    inp: &Inputs,
    m: &Measured,
    needed: &Needed,
) -> AugmentVerdict {
    let mut v = AugmentVerdict {
        card: card.to_string(),
        ..AugmentVerdict::default()
    };
    let ra = (inp.coliseum_start_minute - inp.elite_assumptions.pick_minute).max(0.);
    let growth = if a.growth == "measured" {
        let g = native_growth(r, card);
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
            let mm = a.fight_stacks_now + if g > 0. { a.cap_stacks.min(g * ra) } else { 0. };
            let lh = a.hazard_mult;
            let n = (mm + a.one_shot_stacks) / inp.stacks_per_camp * (-mu * lh * ra).exp();
            let kg = m.kappa.map_or(1., |k| {
                1. / (1. - k * a.farm_speed_pct / (100. + a.farm_speed_pct))
            });
            let pp = c * (a.income_mult * kg * a_bar(mu * lh, ra) - a_bar(mu, ra));
            let val = n + pp;
            v.value = Some(val);
            let ratio = |nd: Option<f64>| nd.filter(|x| *x > 0.).map(|x| val / x);
            v.ratio_solo = ratio(needed.solo);
            v.ratio_duo = ratio(needed.duo);
            v.ratio_trio = ratio(needed.trio);
            v.meets_rule = v
                .ratio_duo
                .zip(v.ratio_solo)
                .map(|(d, s)| d >= inp.band.low && s <= inp.band.high);
            if v.meets_rule.is_none() {
                v.not_measured
                    .push("needed: the elite cost is not measured".into());
            }
        }
    }
    v
}

/// Evaluates a lab report (JSON text) against the inputs (JSON text).
pub fn evaluate_json(report: &str, inputs: &str) -> Result<Evaluation, String> {
    let inp = parse_inputs(inputs)?;
    let rep: Report = serde_json::from_str(report).map_err(|e| format!("report: {e}"))?;
    if !rep.schema.starts_with("balance-lab/1") {
        return Err(format!("report: unsupported schema {:?}", rep.schema));
    }
    let mut nm = Vec::new();
    let m = measure(&rep, &inp, &mut nm);
    let needed = needed_for(&inp, &m, &mut nm);
    let mut by: BTreeMap<&str, &ByQuest> = BTreeMap::new();
    for q in &rep.quests.by_quest {
        by.entry(q.quest.as_str()).or_insert(q);
    }
    let quests = by
        .iter()
        .map(|(id, q)| quest_verdict(id, q, &inp, &m, inp.quests.get(*id)))
        .collect();
    let augments = inp
        .augments
        .iter()
        .map(|(c, a)| augment_verdict(c, a, &rep, &inp, &m, &needed))
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
    })
}

/// The evaluation as compact JSON, keys in declaration order.
pub fn to_json(e: &Evaluation) -> String {
    serde_json::to_string(e).unwrap_or_default()
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
    s += "quest                      value   price   ratio  verdict      timer_s  parity_value  parity_secs\n";
    for q in &e.quests {
        let verdict = q.verdict.map_or("n/m".to_string(), |v| format!("{v:?}"));
        s += &format!(
            "{:<26} {:>7} {:>7} {:>7}  {:<11} {:>8} {:>13} {:>12}\n",
            q.quest,
            fmt(q.value),
            fmt(q.price),
            fmt(q.ratio),
            verdict,
            fmt(q.timer_secs),
            fmt(q.reward_value_at_parity),
            fmt(q.duration_secs_at_parity)
        );
        for r in &q.not_measured {
            s += &format!("    not measured: {r}\n");
        }
    }
    s += "augment                    value   solo    duo     trio    meets_rule\n";
    for a in &e.augments {
        let mr = a.meets_rule.map_or("n/m".to_string(), |b| b.to_string());
        s += &format!(
            "{:<26} {:>7} {:>7} {:>7} {:>7}  {}\n",
            a.card,
            fmt(a.value),
            fmt(a.ratio_solo),
            fmt(a.ratio_duo),
            fmt(a.ratio_trio),
            mr
        );
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
