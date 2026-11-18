//! Executing one probe operation against the harness.
use super::*;
pub(super) fn execute(h: &mut Harness, ids: &mut Ids, op: &V) -> R<V> {
    let kind = s(&op["kind"])?;
    let id = op.get("id").map(|v| ids.player(v)).transpose()?;
    let out = match kind {
        "read" => {
            keys(op, &["kind", "id", "at"])?;
            return reads(
                ids,
                &h.snapshot(),
                id.ok_or("id")?,
                op.get("at").map(n).transpose()?,
                h.stat_view(id.ok_or("id")?),
            );
        }
        "find" => {
            keys(op, &["kind", "id", "statusKind", "sourceId"])?;
            let snap = h.snapshot();
            let p = snap
                .players
                .iter()
                .find(|p| Some(p.id) == id)
                .ok_or("find player")?;
            return Ok(rt::find(
                &p.statuses.instances,
                parse(&op["statusKind"])?,
                op.get("sourceId").map(|v| ids.id(v)).transpose()?,
            )
            .map_or_else(undef, |s| ids.status(s)));
        }
        "apply" => {
            keys(op, &["kind", "id", "apply", "linkField"])?;
            let (mut apply, quiet) = ids.apply(&op["apply"])?;
            if let Some(alias) = op.get("linkField") {
                apply.data.get_or_insert_with(Default::default).field_id =
                    Some(*ids.aliases.get(s(alias)?).ok_or("field alias")?);
            }
            h.execute(Operation::Apply {
                id: id.ok_or("id")?,
                apply,
                quiet,
            })?
        }
        "clock" => {
            keys(op, &["kind", "at"])?;
            h.execute(Operation::Clock {
                at: n(&op["at"])?,
                tick: h.snapshot().tick,
            })?
        }
        "remove" => {
            keys(op, &["kind", "id", "selector"])?;
            keys(&op["selector"], &["kind", "statusKind"])?;
            let selector = match s(&op["selector"]["kind"])? {
                "all" => rt::Selector::All,
                "status" => rt::Selector::Kind(parse(&op["selector"]["statusKind"])?),
                _ => return Err("selector".into()),
            };
            h.execute(Operation::Remove {
                id: id.ok_or("id")?,
                selector,
            })?
        }
        "cleanse" => {
            keys(op, &["kind", "id", "classes"])?;
            let classes = parse::<Vec<String>>(&op["classes"])?;
            if classes
                .iter()
                .any(|c| c != "soft" && c != "hard" && c != "never")
            {
                return Err("cleanse class".into());
            }
            h.execute(Operation::Remove {
                id: id.ok_or("id")?,
                selector: rt::Selector::Cleanse {
                    soft: classes.iter().any(|c| c == "soft"),
                    hard: classes.iter().any(|c| c == "hard"),
                },
            })?
        }
        "reveal" => {
            keys(op, &["kind", "id", "until"])?;
            h.execute(Operation::Reveal {
                id: id.ok_or("id")?,
                until: n(&op["until"])?,
            })?
        }
        "collect" => {
            keys(op, &["kind", "value"])?;
            h.execute(Operation::Collect(
                op["value"].as_bool().ok_or("collect bool")?,
            ))?
        }
        "actorPatch" => {
            keys(op, &["kind", "id", "patch"])?;
            h.execute(Operation::Actor {
                id: id.ok_or("id")?,
                patch: actor_patch_current(&op["patch"], false, h, id.ok_or("id")?)?,
            })?
        }
        "displace" => {
            keys(op, &["kind", "id", "to"])?;
            keys(&op["to"], &["x", "y"])?;
            h.execute(Operation::Displace {
                id: id.ok_or("id")?,
                to: parse(&op["to"])?,
            })?
        }
        "input" => {
            keys(op, &["kind", "id", "type", "message"])?;
            keys(&op["message"], &["a", "t"])?;
            let a = n(&op["message"]["a"])?;
            let t = op["message"].get("t").map(n).transpose()?;
            let raw = match s(&op["type"])? {
                "attack" => rh_sim::RawCommand::Attack { a, t },
                "release" => rh_sim::RawCommand::Release { a, t },
                _ => return Err("input kind".into()),
            };
            h.execute(Operation::Input {
                id: id.ok_or("id")?,
                raw,
            })?;
            return Ok(json!(true));
        }
        "step" => {
            keys(op, &["kind", "ms"])?;
            if n(&op["ms"])? != rh_sim::constants::TICK_MS {
                return Err("step delta".into());
            }
            h.execute(Operation::Step)?
        }
        "fieldsRead" => {
            keys(op, &["kind"])?;
            return Ok(ids.fields_out(h.snapshot().abilities.as_ref()));
        }
        "fieldAdd" => {
            keys(op, &["kind", "alias", "field"])?;
            let f = ids.field_input(&op["field"])?;
            let r = h.execute(Operation::AddField(f))?;
            if let Returned::Field(Some(f)) = &r {
                let mapped = ids.field(&json!(format!("fld{}", f.id)))?;
                if mapped != f.id {
                    return Err("field identity collision".into());
                }
                if ids.aliases.insert(s(&op["alias"])?.into(), f.id).is_some() {
                    return Err("duplicate alias".into());
                }
            }
            r
        }
        "fieldRemove" => {
            keys(op, &["kind", "alias"])?;
            let id = *ids.aliases.get(s(&op["alias"])?).ok_or("field alias")?;
            h.execute(Operation::RemoveField(id))?
        }
        "fieldRemoveWhere" => {
            keys(op, &["kind", "selector"])?;
            keys(&op["selector"], &["kind", "size"])?;
            if op["selector"]["kind"] != "all" {
                return Err("field selector".into());
            }
            let len = h
                .snapshot()
                .abilities
                .as_ref()
                .map_or(0, |a| a.fields.len());
            let expected = op["selector"]["size"].as_u64().ok_or("predicate size")?;
            let predicate = (len as u64) == expected;
            let out = if predicate {
                match h.execute(Operation::RemoveFields(rt::FieldSelector::All))? {
                    Returned::Fields(v) => v,
                    _ => unreachable!(),
                }
            } else {
                Vec::new()
            };
            return Ok(
                json!({"removed":out.iter().map(|f|ids.field_out(f)).collect::<Vec<_>>(),"visited":(0..len).map(|i|json!({"id":ids.fields[i],"size":len})).collect::<Vec<_>>()}),
            );
        }
        "onTrail" => {
            keys(op, &["kind", "alias", "point"])?;
            keys(&op["point"], &["x", "y"])?;
            let id = *ids.aliases.get(s(&op["alias"])?).ok_or("field alias")?;
            let snap = h.snapshot();
            let f = snap
                .abilities
                .as_ref()
                .and_then(|a| a.fields.iter().find(|f| f.id == id))
                .ok_or("trail")?;
            return Ok(json!(rt::on_trail(
                f,
                Vec2::new(n(&op["point"]["x"])?, n(&op["point"]["y"])?)
            )));
        }
        "updateAbilities" => {
            keys(op, &["kind"])?;
            h.execute(Operation::UpdateAbilities)?
        }
        "ledgerHit" => {
            keys(op, &["kind", "hit"])?;
            let hit = ids.hit(&op["hit"])?;
            h.execute(Operation::Hit(hit.clone()))?;
            let mut out = op["hit"].clone();
            out["scale"] = json!(1);
            return Ok(out);
        }
        "applyLedger" => {
            keys(op, &["kind"])?;
            h.execute(Operation::ApplyLedger)?
        }
        "clearLedger" => {
            keys(op, &["kind"])?;
            h.execute(Operation::ClearLedger)?
        }
        "statusDamage" => {
            keys(op, &["kind", "dtMs"])?;
            h.execute(Operation::StatusDamage {
                dt_ms: n(&op["dtMs"])?,
            })?
        }
        "abilityDotFx" => {
            keys(op, &["kind"])?;
            h.execute(Operation::AbilityDotFx)?
        }
        "clearAll" => {
            keys(op, &["kind", "cause"])?;
            h.execute(match s(&op["cause"])? {
                "teleport" => Operation::ClearAll { teleport: true },
                "runEnd" => Operation::ClearAll { teleport: false },
                _ => return Err("clear cause".into()),
            })?
        }
        "runEnded" => {
            keys(op, &["kind"])?;
            h.execute(Operation::RunEnded)?
        }
        "heal" => {
            keys(op, &["kind", "id", "amount", "options"])?;
            let mut source = HealSource::Ordinary;
            if let Some(o) = op.get("options") {
                keys(o, &["lifesteal", "ignoreLockout", "source"])?;
                if o["lifesteal"] == true {
                    source = HealSource::Lifesteal
                } else if o["source"] == "lastStand" {
                    source = HealSource::DeathFloor
                } else if o["ignoreLockout"] == true {
                    source = HealSource::IgnoreLockout
                }
            }
            h.execute(Operation::Heal {
                id: id.ok_or("id")?,
                amount: n(&op["amount"])?,
                source,
            })?
        }
        "removePlayer" => {
            keys(op, &["kind", "id"])?;
            h.execute(Operation::RemovePlayer(id.ok_or("id")?))?
        }
        "extraRows" => {
            keys(op, &["kind", "id", "rows"])?;
            let id = id.ok_or("id")?;
            let m = ids.modifiers.entry(id.0).or_default();
            m.rows = boundary_v3::rows(&op["rows"])?;
            h.set_stats(id, m.view());
            return Ok(undef());
        }
        "farmSetup" => {
            keys(op, &["kind", "id", "counts"])?;
            let id = id.ok_or("id")?;
            let m = ids.modifiers.entry(id.0).or_default();
            m.counts = boundary_v3::counts(&op["counts"])?;
            h.set_stats(id, m.view());
            return Ok(undef());
        }
        "mapReplace" => {
            keys(op, &["kind", "rows"])?;
            h.execute(Operation::ReplaceMap(boundary_v3::map(&op["rows"])?))?
        }
        "runtimePatch" => {
            keys(op, &["kind", "id", "patch"])?;
            let id = id.ok_or("id")?;
            let v = &op["patch"];
            keys(v, &["path", "slip", "weapon"])?;
            let mut patch = ActorPatch::default();
            if let Some(path) = v.get("path") {
                patch.path = Some(points(path)?);
            }
            if let Some(s) = v.get("slip") {
                keys(
                    s,
                    &[
                        "vx",
                        "vy",
                        "ax",
                        "ay",
                        "path",
                        "replanAt",
                        "stuckX",
                        "stuckY",
                        "stuckN",
                        "stuckBest",
                        "stuckAt",
                        "gripUntil",
                    ],
                )?;
                patch.slip = Some(Some(rh_sim::fighter::SlipState {
                    vx: n(&s["vx"])?,
                    vy: n(&s["vy"])?,
                    ax: n(&s["ax"])?,
                    ay: n(&s["ay"])?,
                    // Frozen status witnesses only carry null here; a non-null array
                    // needs the terrain adapter's explicit source-reference identity.
                    path: if s["path"].is_null() {
                        None
                    } else {
                        return Err("non-null slip path identity requires terrain adapter".into());
                    },
                    replan_at: s.get("replanAt").map(n).transpose()?,
                    stuck_x: s.get("stuckX").map(n).transpose()?,
                    stuck_y: s.get("stuckY").map(n).transpose()?,
                    stuck_n: s.get("stuckN").map(n).transpose()?,
                    stuck_best: s.get("stuckBest").map(n).transpose()?,
                    stuck_at: s.get("stuckAt").map(n).transpose()?,
                    grip_until: s.get("gripUntil").map(n).transpose()?,
                }));
            }
            if let Some(w) = v.get("weapon") {
                keys(
                    w,
                    &[
                        "lock",
                        "lockSeen",
                        "lockLosLostAt",
                        "lockPlanAt",
                        "lockGoal",
                        "lockSearchAt",
                    ],
                )?;
                let mut t = h
                    .snapshot()
                    .players
                    .iter()
                    .find(|p| p.id == id)
                    .ok_or("actor")?
                    .weapon
                    .targeting;
                if let Some(v) = w.get("lock") {
                    t.target = ids.id(v)?;
                }
                if let Some(v) = w.get("lockSeen") {
                    t.seen = v.as_bool().ok_or("seen")?;
                }
                if let Some(v) = w.get("lockLosLostAt") {
                    t.lost_at = Some(n(v)?);
                }
                if let Some(v) = w.get("lockPlanAt") {
                    t.plan_at = Some(n(v)?);
                }
                if let Some(v) = w.get("lockSearchAt") {
                    t.search_at = Some(n(v)?);
                }
                if let Some(v) = w.get("lockGoal") {
                    t.goal = if v.is_null() { None } else { Some(point(v)?) };
                }
                patch.targeting = Some(t);
            }
            h.execute(Operation::Actor { id, patch })?
        }
        "queueNested" => {
            keys(op, &["kind", "id"])?;
            h.execute(Operation::Nested {
                id: id.ok_or("id")?,
                source: nested_source(ids)?,
            })?
        }
        "onHook" => {
            keys(op, &["kind", "hook", "victim", "operations"])?;
            if op["hook"] != "hurt" {
                return Err("hook recipe".into());
            }
            let ops = op["operations"].as_array().ok_or("reaction operations")?;
            if ops.len() > 8 {
                return Err("reaction size".into());
            }
            let operations = ops
                .iter()
                .map(|op| reaction(op, ids))
                .collect::<R<Vec<_>>>()?;
            let victim = Some(ids.player(&op["victim"])?);
            h.set_recipe(HurtRecipe { victim, operations });
            return Ok(undef());
        }
        "drain" => {
            keys(op, &["kind"])?;
            h.execute(Operation::Drain)?
        }
        _ => return Err(format!("unsupported operation {kind}")),
    };
    Ok(match out {
        Returned::Undefined => undef(),
        Returned::Status(s) => s.map_or(V::Null, |s| ids.status(&s)),
        Returned::Statuses(v) => json!(v.iter().map(|s| ids.status(s)).collect::<Vec<_>>()),
        Returned::Amount(v) => json!(v),
        Returned::Field(f) => f.map_or(V::Null, |f| ids.field_out(&f)),
        Returned::Fields(v) => json!(v.iter().map(|f| ids.field_out(f)).collect::<Vec<_>>()),
        Returned::Events(ev) => {
            let mut snap = h.snapshot();
            snap.events = ev;
            events(ids, &snap)?
        }
    })
}
