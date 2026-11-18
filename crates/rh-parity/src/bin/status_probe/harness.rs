//! Building the native harness from a case's setup, and the actor, selector and reaction adapters.
use super::*;
pub(super) fn build(v: &V, ids: &mut Ids) -> R<Harness> {
    keys(
        v,
        &[
            "seed",
            "randomSeed",
            "rewardSalt",
            "questSalt",
            "newItemUid",
            "config",
            "modules",
            "mapRows",
            "players",
            "clock",
            "trusted",
        ],
    )?;
    let mut placements = Vec::new();
    for p in v["players"].as_array().ok_or("players")? {
        keys(p, &["id", "name", "faction", "patch", "runtime"])?;
        let id = ids.player(&p["id"])?;
        if id.0 != placements.len() as u32 + 1 {
            return Err("duplicate player".into());
        }
        if p["faction"] != "fire" {
            return Err("fixture faction".into());
        }
        let patch = &p["patch"];
        keys(patch, &["x", "y", "immuneUntil", "weapon0"])?;
        placements.push(Placement {
            profile: s(&p["name"])?.into(),
            faction: FactionId::Fire,
            bot: false,
            at: Vec2::new(n(&patch["x"])?, n(&patch["y"])?),
            hp: None,
            weapon0: if patch["weapon0"] == "" {
                None
            } else {
                Some(rh_sim::WeaponId::parse(s(&patch["weapon0"])?).ok_or("weapon0")?)
            },
            weapon1: None,
        });
    }
    let map = rh_sim::TileMap::from_rows(&parse::<Vec<String>>(&v["mapRows"])?)
        .map_err(|e| e.to_string())?;
    let sim = Sim::reset_active(
        SimConfig {
            quests: false,
            consumables: true,
            min_players: usize::try_from(v["config"]["minPlayers"].as_u64().ok_or("minPlayers")?)
                .map_err(|_| "minPlayers overflow")?,
            lobby_seconds: n(&v["config"]["lobbySeconds"])?,
            fill_seconds: n(&v["config"]["fillSeconds"])?,
            pvp_rules: v["config"]["pvpRules"].as_bool().ok_or("pvpRules")?,
            faction_mode: if v["modules"]["signatures"] == true {
                rh_sim::FactionMode::EngagementHolyPlagueFireFrostSentenced
            } else {
                rh_sim::FactionMode::Off
            },
            ..Default::default()
        },
        rh_sim::sim::Arena {
            map,
            spawns: placements.iter().map(|p| p.at).collect(),
        },
        Secrets::from_seed(v["seed"].as_u64().ok_or("seed")?),
        &placements,
    );
    let mut h = Harness::new(sim);
    h.execute(Operation::Clock {
        at: n(&v["clock"]["serverTime"])?,
        tick: u32::try_from(v["clock"]["tick"].as_u64().ok_or("tick")?)
            .map_err(|_| "tick overflow")?,
    })?;
    for p in v["players"].as_array().unwrap() {
        h.execute(Operation::Actor {
            id: ids.player(&p["id"])?,
            patch: ActorPatch {
                immune_until: Some(n(&p["patch"]["immuneUntil"])?),
                history: Some(parse(&p["runtime"]["history"])?),
                path: Some(parse(&p["runtime"]["path"])?),
                ..Default::default()
            },
        })?;
    }
    keys(
        &v["trusted"],
        &[
            "actorOverrides",
            "runtimeOverrides",
            "signatures",
            "pvpRules",
            "splitMap",
            "markSuppressed",
        ],
    )?;
    for (group, runtime) in [("actorOverrides", false), ("runtimeOverrides", true)] {
        if let Some(patches) = v["trusted"].get(group) {
            for (id, patch) in patches.as_object().ok_or("patches")? {
                let id = ids.player(&json!(id))?;
                h.execute(Operation::Actor {
                    id,
                    patch: {
                        let mut out = actor_patch_current(patch, runtime, &h, id)?;
                        if let Some(at) = out.position {
                            out.history = Some(vec![rh_sim::state::Sample {
                                t: n(&v["clock"]["serverTime"])?,
                                x: at.x,
                                y: at.y,
                            }]);
                        }
                        out
                    },
                })?;
            }
        }
    }
    if let Some(rows) = v["trusted"].get("markSuppressed") {
        for row in rows.as_array().ok_or("mark rows")? {
            let row = row
                .as_array()
                .filter(|r| r.len() == 2)
                .ok_or("mark tuple")?;
            let id = ids.player(&row[0])?;
            let mut view = h.stat_view(id);
            view.mark_suppression = Some(n(&row[1])?);
            h.set_stats(id, view);
        }
    }
    Ok(h)
}
pub(super) fn actor_patch(v: &V, runtime: bool) -> R<ActorPatch> {
    let mut p = ActorPatch::default();
    if runtime {
        keys(v, &["down", "cast"])?;
        if let Some(d) = v.get("down") {
            keys(d, &["outcome"])?;
            p.down = Some(Some(match s(&d["outcome"])? {
                "died" => rh_sim::results::Outcome::Died,
                _ => return Err("down outcome".into()),
            }))
        }
        if let Some(c) = v.get("cast") {
            keys(
                c,
                &[
                    "ability",
                    "rank",
                    "slot",
                    "startedAt",
                    "landsAt",
                    "target",
                    "point",
                    "landing",
                    "prevReadyAt",
                    "lagMs",
                ],
            )?;
            p.cast = Some(Some(rt::CastFact {
                producer: None,
                ability: parse(&c["ability"])?,
                started_at: n(&c["startedAt"])?,
                lands_at: n(&c["landsAt"])?,
                cancelled_by: rt::CancelCauses {
                    stun: true,
                    silence: true,
                    root: false,
                    stagger: false,
                },
            }));
        }
    } else {
        keys(
            v,
            &[
                "hp",
                "maxHp",
                "barrier",
                "immuneUntil",
                "status",
                "weapon0",
                "x",
                "y",
                "sig",
                "displaceSeq",
            ],
        )?;
        p.hp = v.get("hp").map(n).transpose()?;
        p.max_hp = v.get("maxHp").map(n).transpose()?;
        p.barrier = v.get("barrier").map(n).transpose()?;
        p.immune_until = v.get("immuneUntil").map(n).transpose()?;
        p.status = v
            .get("status")
            .map(|v| match s(v)? {
                "dead" => Ok(rh_sim::Status::Dead),
                "alive" => Ok(rh_sim::Status::Alive),
                _ => Err("actor status".to_owned()),
            })
            .transpose()?;
        p.displace_seq = v
            .get("displaceSeq")
            .map(|v| {
                v.as_u64()
                    .and_then(|n| u8::try_from(n).ok())
                    .ok_or("displaceSeq".to_owned())
            })
            .transpose()?;
        p.signature_bits = v
            .get("sig")
            .map(|v| {
                v.as_u64()
                    .and_then(|n| u8::try_from(n).ok())
                    .ok_or("sig".to_owned())
            })
            .transpose()?;
        if let Some(w) = v.get("weapon0") {
            p.weapon0 = Some(if s(w)?.is_empty() {
                None
            } else {
                Some(rh_sim::WeaponId::parse(s(w)?).ok_or("weapon")?)
            });
        }
        if v.get("x").is_some() && v.get("y").is_some() {
            p.position = Some(Vec2::new(n(&v["x"])?, n(&v["y"])?));
        } else if v.get("x").is_some() || v.get("y").is_some() {
            return Err("partial position patch requires current actor".into());
        }
    }
    Ok(p)
}
pub(super) fn normalized_hit(v: &V) -> V {
    let mut v = v.clone();
    if v["arena"] == false {
        v.as_object_mut().unwrap().remove("arena");
    }
    v
}
pub(super) fn actor_patch_current(
    v: &V,
    runtime: bool,
    h: &Harness,
    id: PlayerId,
) -> R<ActorPatch> {
    let mut v = v.clone();
    if !runtime && (v.get("x").is_some() || v.get("y").is_some()) {
        let snap = h.snapshot();
        let p = snap
            .players
            .iter()
            .find(|p| p.id == id)
            .ok_or("actor patch")?;
        if v.get("x").is_none() {
            v["x"] = json!(p.body.x)
        }
        if v.get("y").is_none() {
            v["y"] = json!(p.body.y)
        }
    }
    actor_patch(&v, runtime)
}
pub(super) fn point(v: &V) -> R<Vec2> {
    keys(v, &["x", "y"])?;
    Ok(Vec2::new(n(&v["x"])?, n(&v["y"])?))
}
pub(super) fn points(v: &V) -> R<Vec<Vec2>> {
    v.as_array().ok_or("points")?.iter().map(point).collect()
}
pub(super) fn nested_source(ids: &mut Ids) -> R<rt::StatusSource> {
    ids.source(&json!({"id":"bob","name":"BOB"}))
}
pub(super) fn selector(op: &V, ids: &mut Ids) -> R<rt::Selector> {
    match s(&op["kind"])? {
        "cleanse" => {
            keys(op, &["kind", "id", "classes"])?;
            let c = parse::<Vec<String>>(&op["classes"])?;
            if c.iter()
                .any(|c| !["soft", "hard", "never"].contains(&c.as_str()))
            {
                return Err("cleanse class".into());
            }
            Ok(rt::Selector::Cleanse {
                soft: c.iter().any(|c| c == "soft"),
                hard: c.iter().any(|c| c == "hard"),
            })
        }
        "remove" => {
            keys(op, &["kind", "id", "selector"])?;
            let s0 = &op["selector"];
            keys(s0, &["kind", "statusKind", "sourceId"])?;
            match s(&s0["kind"])? {
                "all" => Ok(rt::Selector::All),
                "status" => Ok(rt::Selector::Kind(parse(&s0["statusKind"])?)),
                "source" => Ok(rt::Selector::Source(ids.id(&s0["sourceId"])?)),
                _ => Err("selector".into()),
            }
        }
        _ => Err("selector operation".into()),
    }
}
pub(super) fn reaction(op: &V, ids: &mut Ids) -> R<Reaction> {
    let kind = s(&op["kind"])?;
    if kind == "deferHit" {
        keys(op, &["kind", "hit"])?;
        return Ok(Reaction::DeferHit(ids.hit(&op["hit"])?));
    }
    let id = ids.player(&op["id"])?;
    match kind {
        "apply" => {
            keys(op, &["kind", "id", "apply"])?;
            let (apply, quiet) = ids.apply(&op["apply"])?;
            Ok(Reaction::Apply { id, apply, quiet })
        }
        "remove" | "cleanse" => Ok(Reaction::Remove {
            id,
            selector: selector(op, ids)?,
        }),
        "displace" => {
            keys(op, &["kind", "id", "to"])?;
            Ok(Reaction::Displace {
                id,
                to: point(&op["to"])?,
            })
        }
        "queueNested" => {
            keys(op, &["kind", "id"])?;
            Ok(Reaction::Nested {
                id,
                source: nested_source(ids)?,
            })
        }
        _ => Err("unsupported reaction".into()),
    }
}
