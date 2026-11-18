//! Explicit source-helper comparison. Never reads expected output into native setup.
use rh_sim::{
    status_runtime::{self as rt, probe::*},
    Event, FactionId, Placement, PlayerId, Secrets, Sim, SimConfig, Vec2,
};
use serde_json::{json, Value as V};
use std::{collections::BTreeMap, fs};
type R<T> = Result<T, String>;
#[path = "status_probe/admission.rs"]
mod admission;
#[path = "status_probe/boundary_v3.rs"]
mod boundary_v3;
#[path = "status_probe/compare.rs"]
mod compare;
#[path = "status_probe/harness.rs"]
mod harness;
#[path = "status_probe/ids.rs"]
mod ids;
#[path = "status_probe/operation.rs"]
mod operation;
#[path = "status_probe/project.rs"]
mod project;
#[path = "status_probe/roles.rs"]
mod roles;
use self::{compare::*, harness::*, ids::*, operation::*, project::*, roles::*};
fn run(path: &str, only: Option<&str>) -> R<(usize, usize)> {
    let root =
        serde_json::from_slice::<admission::Unique>(&fs::read(path).map_err(|e| e.to_string())?)
            .map_err(|e| e.to_string())?
            .0;
    admission::fixture(&root)?;
    keys(
        &root,
        &[
            "format",
            "slice",
            "provenance",
            "shared",
            "rules",
            "cases",
            "policies",
        ],
    )?;
    if root["format"] != "rh-status-probes/1" {
        return Err("format".into());
    }
    let cases = root["cases"].as_array().ok_or("cases")?;
    let mut seen = BTreeMap::new();
    let mut count = 0;
    let mut leaf = 0;
    for case in cases {
        let name = s(&case["name"])?;
        if seen.insert(name, ()).is_some() {
            return Err("duplicate case".into());
        }
        if only.is_some_and(|only| name != only) {
            continue;
        }
        let result = (|| {
            keys(
                case,
                &[
                    "name",
                    "classification",
                    "boundary",
                    "operations",
                    "setup",
                    "steps",
                    "observerFullStateEquality",
                    "observerReturnEquality",
                    "sourceRandomContinuationEquality",
                ],
            )?;
            for flag in [
                "observerFullStateEquality",
                "observerReturnEquality",
                "sourceRandomContinuationEquality",
            ] {
                if case[flag] != true {
                    return Err(format!("source assurance {flag}"));
                }
            }
            if name == "mob-and-quest-roles" {
                return role_case(case);
            }
            let mut ids = Ids::default();
            let mut h = build(&case["setup"]["input"], &mut ids)?;
            check_state(&ids, &h.snapshot(), &case["setup"]["initial"], "initial")?;
            boundary_v3::compare(&ids, &h, &case["setup"]["initial"], "initial")?;
            let steps = case["steps"].as_array().ok_or("steps")?;
            if steps.len() != case["operations"].as_array().ok_or("operations")?.len() {
                return Err("step count".into());
            }
            for (i, step) in steps.iter().enumerate() {
                keys(step, &["operation", "before", "returned", "after", "hooks"])?;
                equal(
                    &step["operation"],
                    &case["operations"][i],
                    "operation provenance",
                )?;
                check_state(&ids, &h.snapshot(), &step["before"], &format!("{i}/before"))?;
                boundary_v3::compare(&ids, &h, &step["before"], &format!("{i}/before"))?;
                let before_native = h.snapshot();
                let hook_start = before_native.hooks.len();
                let result = execute(&mut h, &mut ids, &step["operation"])?;
                equal(&result, &step["returned"], &format!("{i}/return"))?;
                equal(
                    &step["before"]["runtime"]["random"],
                    &step["after"]["runtime"]["random"],
                    "source zero-draw continuation",
                )?;
                if before_native.spawn_rng != h.snapshot().spawn_rng {
                    return Err("native unexpected RNG draw".into());
                }
                check_state(&ids, &h.snapshot(), &step["after"], &format!("{i}/after"))?;
                boundary_v3::compare(&ids, &h, &step["after"], &format!("{i}/after"))?;
                equal(
                    &hooks(&ids, &h.snapshot().hooks[hook_start..])?,
                    &expected_hooks(&step["hooks"])?,
                    &format!("{i}/hooks"),
                )?;
            }
            Ok(())
        })();
        result.map_err(|e| format!("{name}: {e}"))?;
        if name == "mob-and-quest-roles" {
            leaf += 1
        } else {
            count += 1
        }
    }
    if count + leaf == 0 {
        return Err("no matching cases".into());
    }
    Ok((count, leaf))
}
fn main() {
    let args: Vec<_> = std::env::args().collect();
    if !(2..=3).contains(&args.len()) || args.get(2).is_some_and(|s| s.starts_with('-')) {
        eprintln!("usage: status-probes fixture.json [case]");
        std::process::exit(2)
    }
    match run(&args[1], args.get(2).map(String::as_str)) {
        Ok((sim, leaf)) => {
            println!("{sim} Sim status source cases and {leaf} separate role leaf proofs passed")
        }
        Err(e) => {
            eprintln!("{e}");
            std::process::exit(1)
        }
    }
}
