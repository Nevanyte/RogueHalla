//! Typed reads of the full hard tier's parameters.

use super::*;

// ------------------------------------------------------------------------------------------------------- typed reads

pub(super) fn read_ex_gaussian(value: &Mv, path: &str) -> Result<ExGaussian, String> {
    value.exact(&["mu", "sigma", "tau", "floor", "cap"], path)?;
    Ok(ExGaussian {
        mu: value.get("mu", path)?.num(&format!("{path}.mu"))?,
        sigma: value.get("sigma", path)?.num(&format!("{path}.sigma"))?,
        tau: value.get("tau", path)?.num(&format!("{path}.tau"))?,
        floor: value.get("floor", path)?.num(&format!("{path}.floor"))?,
        cap: value.get("cap", path)?.num(&format!("{path}.cap"))?,
    })
}

pub(super) fn read_cadence(value: &Mv, path: &str) -> Result<Cadence, String> {
    value.exact(&["mean", "jitter"], path)?;
    Ok(Cadence {
        mean: value.get("mean", path)?.num(&format!("{path}.mean"))?,
        jitter: value.get("jitter", path)?.num(&format!("{path}.jitter"))?,
    })
}

pub(super) fn read_tier(value: &Mv, path: &str) -> Result<TierParams, String> {
    value.exact(
        &[
            "id",
            "reaction",
            "step",
            "daze",
            "refractoryMs",
            "thinkMs",
            "strategyMs",
            "apm",
            "burst",
            "minGapMs",
            "moveScatter",
            "targetScatter",
            "minimapSigma",
            "edgeClickP",
            "timingSigmaMs",
            "hesitation",
            "softmaxT",
            "lapseP",
            "dodgeP",
            "jukeP",
            "orbwalk",
            "knowledge",
            "mobShareNoise",
            "route",
            "attention",
            "memorySpanMs",
            "feintRead",
            "appraisal",
            "wary",
            "caution",
        ],
        path,
    )?;
    let get = |key: &str| value.get(key, path);
    let num = |key: &str| -> Result<f64, String> { get(key)?.num(&format!("{path}.{key}")) };
    let nested = |key: &str| -> Result<&Mv, String> { get(key) };
    let id_text = get("id")?.text(&format!("{path}.id"))?;
    let id = TierId::parse(id_text)
        .ok_or_else(|| err(&format!("{path}.id"), format!("unknown tier id {id_text}")))?;
    let move_scatter = {
        let nested_path = format!("{path}.moveScatter");
        let nested = nested("moveScatter")?;
        nested.exact(&["base", "perUnit"], &nested_path)?;
        MoveScatter {
            base: nested
                .get("base", &nested_path)?
                .num(&format!("{nested_path}.base"))?,
            per_unit: nested
                .get("perUnit", &nested_path)?
                .num(&format!("{nested_path}.perUnit"))?,
        }
    };
    let target_scatter = {
        let nested_path = format!("{path}.targetScatter");
        let nested = nested("targetScatter")?;
        nested.exact(&["sigma", "lagS", "slipP", "slipMult"], &nested_path)?;
        TargetScatter {
            sigma: nested
                .get("sigma", &nested_path)?
                .num(&format!("{nested_path}.sigma"))?,
            lag_s: nested
                .get("lagS", &nested_path)?
                .num(&format!("{nested_path}.lagS"))?,
            slip_p: nested
                .get("slipP", &nested_path)?
                .num(&format!("{nested_path}.slipP"))?,
            slip_mult: nested
                .get("slipMult", &nested_path)?
                .num(&format!("{nested_path}.slipMult"))?,
        }
    };
    let hesitation = {
        let nested_path = format!("{path}.hesitation");
        let nested = nested("hesitation")?;
        nested.exact(&["p", "minMs", "maxMs"], &nested_path)?;
        Hesitation {
            p: nested
                .get("p", &nested_path)?
                .num(&format!("{nested_path}.p"))?,
            min_ms: nested
                .get("minMs", &nested_path)?
                .num(&format!("{nested_path}.minMs"))?,
            max_ms: nested
                .get("maxMs", &nested_path)?
                .num(&format!("{nested_path}.maxMs"))?,
        }
    };
    let orbwalk = {
        let nested_path = format!("{path}.orbwalk");
        let nested = nested("orbwalk")?;
        nested.exact(&["p", "stepU", "angleJitter"], &nested_path)?;
        Orbwalk {
            p: nested
                .get("p", &nested_path)?
                .num(&format!("{nested_path}.p"))?,
            step_u: nested
                .get("stepU", &nested_path)?
                .num(&format!("{nested_path}.stepU"))?,
            angle_jitter: nested
                .get("angleJitter", &nested_path)?
                .num(&format!("{nested_path}.angleJitter"))?,
        }
    };
    let route = {
        let nested_path = format!("{path}.route");
        let nested = nested("route")?;
        nested.exact(&["horizon", "walkPaths", "reach"], &nested_path)?;
        Route {
            horizon: nested
                .get("horizon", &nested_path)?
                .uint(&format!("{nested_path}.horizon"))?,
            walk_paths: nested
                .get("walkPaths", &nested_path)?
                .boolean(&format!("{nested_path}.walkPaths"))?,
            reach: nested
                .get("reach", &nested_path)?
                .num(&format!("{nested_path}.reach"))?,
        }
    };
    let attention = {
        let nested_path = format!("{path}.attention");
        let nested = nested("attention")?;
        nested.exact(
            &["central", "peripheryMult", "engagedMult", "reconsiderMs"],
            &nested_path,
        )?;
        Attention {
            central: nested
                .get("central", &nested_path)?
                .num(&format!("{nested_path}.central"))?,
            periphery_mult: nested
                .get("peripheryMult", &nested_path)?
                .num(&format!("{nested_path}.peripheryMult"))?,
            engaged_mult: nested
                .get("engagedMult", &nested_path)?
                .num(&format!("{nested_path}.engagedMult"))?,
            reconsider_ms: nested
                .get("reconsiderMs", &nested_path)?
                .num(&format!("{nested_path}.reconsiderMs"))?,
        }
    };
    let appraisal = {
        let nested_path = format!("{path}.appraisal");
        let nested = nested("appraisal")?;
        nested.exact(&["bias", "lead", "busy", "hue", "noise"], &nested_path)?;
        Appraisal {
            bias: nested
                .get("bias", &nested_path)?
                .num(&format!("{nested_path}.bias"))?,
            lead: nested
                .get("lead", &nested_path)?
                .num(&format!("{nested_path}.lead"))?,
            busy: nested
                .get("busy", &nested_path)?
                .num(&format!("{nested_path}.busy"))?,
            hue: nested
                .get("hue", &nested_path)?
                .num(&format!("{nested_path}.hue"))?,
            noise: nested
                .get("noise", &nested_path)?
                .num(&format!("{nested_path}.noise"))?,
        }
    };
    let caution = {
        let nested_path = format!("{path}.caution");
        let nested = nested("caution")?;
        nested.exact(&["mobShare", "eliteShare", "startHp"], &nested_path)?;
        Caution {
            mob_share: nested
                .get("mobShare", &nested_path)?
                .num(&format!("{nested_path}.mobShare"))?,
            elite_share: nested
                .get("eliteShare", &nested_path)?
                .num(&format!("{nested_path}.eliteShare"))?,
            start_hp: nested
                .get("startHp", &nested_path)?
                .num(&format!("{nested_path}.startHp"))?,
        }
    };
    Ok(TierParams {
        id,
        reaction: read_ex_gaussian(nested("reaction")?, &format!("{path}.reaction"))?,
        step: read_ex_gaussian(nested("step")?, &format!("{path}.step"))?,
        daze: read_ex_gaussian(nested("daze")?, &format!("{path}.daze"))?,
        refractory_ms: num("refractoryMs")?,
        think_ms: read_cadence(nested("thinkMs")?, &format!("{path}.thinkMs"))?,
        strategy_ms: read_cadence(nested("strategyMs")?, &format!("{path}.strategyMs"))?,
        apm: num("apm")?,
        burst: num("burst")?,
        min_gap_ms: num("minGapMs")?,
        move_scatter,
        target_scatter,
        minimap_sigma: num("minimapSigma")?,
        edge_click_p: num("edgeClickP")?,
        timing_sigma_ms: num("timingSigmaMs")?,
        hesitation,
        softmax_t: num("softmaxT")?,
        lapse_p: num("lapseP")?,
        dodge_p: num("dodgeP")?,
        juke_p: num("jukeP")?,
        orbwalk,
        knowledge: num("knowledge")?,
        mob_share_noise: num("mobShareNoise")?,
        route,
        attention,
        memory_span_ms: num("memorySpanMs")?,
        feint_read: num("feintRead")?,
        appraisal,
        wary: num("wary")?,
        caution,
    })
}
