//! Source-backed leaf role proof.
use super::*;
/// Source-backed leaf role proof only. These are not native mob actors and never enter Sim/headless.
pub(super) fn role_case(case: &V) -> R<()> {
    let input = &case["setup"]["input"];
    if input["trusted"] != json!({"roles":true}) {
        return Err("role helper setup".into());
    }
    let now = n(&input["clock"]["serverTime"])?;
    let mut ids = Ids::default();
    for p in input["players"].as_array().ok_or("players")? {
        ids.player(&p["id"])?;
    }
    let mut tables = [rt::StatusRuntime::default(), rt::StatusRuntime::default()];
    let steps = case["steps"].as_array().ok_or("steps")?;
    let ops = case["operations"].as_array().ok_or("operations")?;
    if steps.len() != 30 || ops.len() != steps.len() {
        return Err("role inventory".into());
    }
    for (index, step) in steps.iter().enumerate() {
        let op = &step["operation"];
        equal(op, &ops[index], "role operation")?;
        let slot = match s(&op["id"])? {
            "mob-role" => 0,
            "quest-role" => 1,
            _ => return Err("role recipient".into()),
        };
        let role = if slot == 0 {
            rt::Recipient::Mob
        } else {
            rt::Recipient::QuestGiver
        };
        let check = |tables: &[rt::StatusRuntime; 2], state: &V| -> R<()> {
            for (i, table) in tables.iter().enumerate() {
                let actor = &state["runtime"]["fighters"][3 + i];
                equal(&json!(table.effects), &actor["effects"], "role effects")?;
                let statuses = if table.initialized {
                    json!(table
                        .instances
                        .iter()
                        .map(|s| ids.status(s))
                        .collect::<Vec<_>>())
                } else {
                    undef()
                };
                equal(&statuses, &actor["statuses"], "role statuses")?;
            }
            Ok(())
        };
        check(&tables, &step["before"])?;
        let mut expected_hooks = Vec::new();
        let returned = match s(&op["kind"])? {
            "apply" => {
                keys(op, &["kind", "id", "apply"])?;
                let (apply, _) = ids.apply(&op["apply"])?;
                if rt::admits(&apply, role, true) {
                    let applied = rt::apply(&mut tables[slot], now, apply);
                    let st = ids.status(&tables[slot].instances[applied.index]);
                    expected_hooks.push(json!({"name":"statusApplied","role":"mob","status":st,"fresh":applied.fresh}));
                    st
                } else {
                    V::Null
                }
            }
            "read" => {
                keys(op, &["kind", "id"])?;
                read_table(
                    &ids,
                    &tables[slot],
                    now,
                    (&[], Some(PlayerId(2))),
                    false,
                    None,
                    rt::StatView::default(),
                )?
            }
            _ => return Err("role operation kind".into()),
        };
        equal(&returned, &step["returned"], "role return")?;
        let source_hooks=step["hooks"].as_array().ok_or("hooks")?.iter().map(|h|json!({"name":h["name"],"role":h["args"][0]["kind"],"status":h["args"][1],"fresh":h["args"][2]})).collect::<Vec<_>>();
        equal(
            &json!(expected_hooks),
            &json!(source_hooks),
            "role ordered hook facts",
        )?;
        for (i, table) in tables.iter().enumerate() {
            let actor = &step["after"]["runtime"]["fighters"][3 + i];
            equal(
                &json!(table.effects),
                &actor["effects"],
                "role after effects",
            )?;
            equal(
                &if table.initialized {
                    json!(table
                        .instances
                        .iter()
                        .map(|s| ids.status(s))
                        .collect::<Vec<_>>())
                } else {
                    undef()
                },
                &actor["statuses"],
                "role after statuses",
            )?;
        }
    }
    Ok(())
}
