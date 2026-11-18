//! Projection of native state, events, reads and hooks into the source JSON shape.
use super::*;
pub(super) fn weapon(ids: &Ids, p: &rh_sim::state::Player) -> V {
    let w = &p.weapon;
    let t = &w.targeting;
    let action=w.action.map(|a|json!({"kind":format!("{:?}",a.kind).to_lowercase(),"weapon":a.weapon.as_str(),"a":a.a,"startedAt":a.started_at,"landsAt":a.lands_at,"target":a.target.map_or_else(undef,|id|json!(ids.name(Some(id)))),"packet":{"raw":a.packet.raw,"phys":a.packet.phys,"magic":a.packet.magic,"true":a.packet.true_}}));
    json!({"readyAt":w.ready_at,"state":{"action":action,"aim":w.aim,"aimAt":w.aim_at,"attackMoveGoal":t.attack_move,"bufferedUntil":w.buffered_until,"held":w.held,"lagMs":w.lag_ms,"lock":t.target.map(|id|ids.name(Some(id))),"lockGoal":t.goal,"lockLosLostAt":t.lost_at.unwrap_or(0.),"lockPlanAt":t.plan_at.map_or_else(negative_infinity,|v|json!(v)),"lockSearchAt":t.search_at.map_or_else(negative_infinity,|v|json!(v)),"lockSeen":t.seen,"pressedAt":w.pressed_at,"rootUntil":w.root_until,"staggerImmuneUntil":w.stagger_immune_until}})
}
pub(super) fn actors(ids: &Ids, snap: &Snapshot) -> V {
    json!(snap.players.iter().map(|p|json!({"id":ids.name(Some(p.id)),"role":"player","name":p.seat.profile,"sig":p.signature.bits,"standing":p.standing(),"hp":p.vitals.hp,"maxHp":p.vitals.max_hp,"barrier":p.vitals.barrier,"immuneUntil":p.vitals.immune_until,"effects":p.statuses.effects,"asMult":rh_sim::stat_fold::decode_mult(p.modifiers.attack_rate_wire()),"body":body(p),"armorBonus":p.modifiers.mirrors().armor_bonus,"weapon":weapon(ids,p),"cast":p.statuses.cast.map(|c|json!({"ability":c.ability,"startedAt":c.started_at,"landsAt":c.lands_at})),"cards":({let c=p.card_dot_crit.unwrap_or_default();json!({"critAccDot":c.accumulator,"dotRollTick":c.roll_tick.map_or(-1,|v|v as i64),"dotCrit":c.critical})}),"statuses":if p.statuses.initialized{json!(p.statuses.instances.iter().map(|s|ids.status(s)).collect::<Vec<_>>())}else{undef()}})).collect::<Vec<_>>())
}
pub(super) fn body(p: &rh_sim::state::Player) -> V {
    let slip = p.motion.slip.as_ref().map(|s| {
        let mut v = json!({"vx":s.vx,"vy":s.vy,"ax":s.ax,"ay":s.ay,"path":s.path});
        for (k, n) in [
            ("replanAt", s.replan_at),
            ("stuckX", s.stuck_x),
            ("stuckY", s.stuck_y),
            ("stuckN", s.stuck_n),
            ("stuckBest", s.stuck_best),
            ("stuckAt", s.stuck_at),
            ("gripUntil", s.grip_until),
        ] {
            if let Some(n) = n {
                v[k] = json!(n)
            }
        }
        v
    });
    json!({"x":p.body.x,"y":p.body.y,"facing":p.body.facing,"displaceSeq":p.motion.displace_seq,"path":p.motion.path.as_slice(),"history":p.body.history.as_slice(),"slip":slip})
}
pub(super) fn expected_actors(v: &V) -> R<V> {
    Ok(json!(v.as_array().ok_or("fighters array")?.iter().map(|p|json!({"id":p["id"],"role":p["role"],"name":p["name"],"sig":p["sig"],"standing":p["standing"],"hp":p["hp"],"maxHp":p["maxHp"],"barrier":p["barrier"],"immuneUntil":p["immuneUntil"],"effects":p["effects"],"asMult":p["mirrors"]["asMult"],"body":p["body"],"armorBonus":p["mirrors"]["armorBonus"],"weapon":({let mut w=p["weapon"].clone();if let Some(o)=w["state"].as_object_mut(){o.remove("lastShotAt");}w}),"cards":if p["cards"]==undef(){json!({"critAccDot":0,"dotRollTick":-1,"dotCrit":false})}else{p["cards"].clone()},"cast":if p["cast"].is_null(){V::Null}else{json!({"ability":p["cast"]["ability"],"startedAt":p["cast"]["startedAt"],"landsAt":p["cast"]["landsAt"]})},"statuses":p["statuses"]})).collect::<Vec<_>>()))
}
pub(super) fn events(ids: &Ids, snap: &Snapshot) -> R<V> {
    Ok(json!(snap.events.iter().map(|e|match e{Event::StatusApplied{id,kind,ms,by}=>Ok(json!({"type":"fx","fx":{"kind":"status","id":ids.name(Some(*id)),"status":kind,"ms":ms,"by":by.map(|id|ids.name(Some(id)))}})),Event::CastEnded{id,ability,reason,..}=>Ok(json!({"type":"fx","fx":{"kind":"castEnd","by":ids.name(Some(*id)),"ability":ability,"reason":reason}})),Event::Hit{victim,by,amount,weapon,..}=>Ok(json!({"type":"fx","fx":{"kind":"hit","id":ids.name(Some(*victim)),"by":ids.name(*by),"amount":amount,"weapon":weapon.map(|w|w.as_str())}})),
 Event::Heal{id,amount}=>Ok(json!({"type":"fx","fx":{"kind":"heal","id":ids.name(Some(*id)),"amount":amount}})),
 Event::AbilityHit{victim,by,ability,amount}=>Ok(json!({"type":"fx","fx":{"kind":"hit","id":ids.name(Some(*victim)),"by":ids.name(*by),"amount":amount,"weapon":null,"ability":ability}})),
 Event::Resolved{id,result,..}=>Ok(json!({"type":"resolved","id":ids.name(Some(*id)),"result":{"profile":result.profile,"outcome":format!("{:?}",result.outcome).to_lowercase(),"durationSec":result.duration_sec,"kills":result.kills,"killedBy":result.killed_by,"place":result.place,"players":result.players,"gv":result.gv,"winBonusWithheld":result.win_bonus_withheld}})),
 Event::Windup{by,weapon,a,ms}=>Ok(json!({"type":"fx","fx":{"kind":"windup","by":ids.name(Some(*by)),"weapon":weapon.as_str(),"a":a,"ms":ms}})),
 Event::Swing{by,weapon,x,y,a}=>Ok(json!({"type":"fx","fx":{"kind":"swing","by":ids.name(Some(*by)),"weapon":weapon.as_str(),"x":x,"y":y,"a":a}})),
 Event::Death{id,x,y,by}=>Ok(json!({"type":"fx","fx":{"kind":"death","id":ids.name(Some(*id)),"by":by.map(|id|ids.name(Some(id))),"x":x,"y":y}})),
 Event::Gv{id,amount,reason}=>Ok(json!({"type":"gv","id":ids.name(Some(*id)),"amount":amount,"reason":format!("{reason:?}").to_lowercase()})),
 e=>Err(format!("unmapped event {e:?}"))}).collect::<R<Vec<_>>>()?))
}
pub(super) fn check_state(ids: &Ids, snap: &Snapshot, source: &V, label: &str) -> R<()> {
    let e = &source["runtime"];
    equal(
        &actors(ids, snap),
        &expected_actors(&e["fighters"])?,
        &format!("{label}/fighters"),
    )?;
    let suppressed: Vec<_> = snap
        .suppressed
        .iter()
        .map(|(id, value)| json!([ids.name(Some(*id)), value]))
        .collect();
    equal(
        &json!(suppressed),
        &if e["marks"] == undef() {
            json!([])
        } else {
            e["marks"]["suppressed"].clone()
        },
        &format!("{label}/mark suppression"),
    )?;
    equal(&json!(snap.time), &e["clock"]["serverTime"], label)?;
    equal(&json!(snap.tick), &e["clock"]["tick"], label)?;
    equal(&json!(snap.collecting), &e["collecting"], label)?;
    equal(
        &json!(snap.payouts),
        &json!(e["payouts"].as_array().ok_or("payouts")?.len()),
        label,
    )?;
    equal(
        &events(ids, snap)?,
        &e["events"],
        &format!("{label}/events"),
    )?;
    equal(
        &json!(snap.entity_counter),
        &e["entityCounter"],
        &format!("{label}/entityCounter"),
    )?;
    let callbacks:Vec<_>=snap.callbacks.iter().map(|c|json!({"hook":"hurt","victim":ids.name(Some(c.victim)),"operation":c.operation,"collecting":c.collecting,"statuses":c.statuses.iter().map(|s|ids.status(s)).collect::<Vec<_>>(),"returned":match &c.returned{CallbackReturn::Undefined=>undef(),CallbackReturn::Status(st)=>st.as_ref().map_or(V::Null,|s|ids.status(s)),CallbackReturn::Statuses(st)=>json!(st.iter().map(|s|ids.status(s)).collect::<Vec<_>>()),CallbackReturn::Hit(h)=>ids.hit_out(h)}})).collect();
    let expected_callbacks = e["callbackOperations"]
        .as_array()
        .ok_or("callback operations")?
        .iter()
        .map(|c| {
            let mut c = c.clone();
            c["operation"] = serde_json::to_value(reaction(&c["operation"], &mut ids.clone())?)
                .map_err(|e| e.to_string())?;
            Ok(c)
        })
        .collect::<R<Vec<_>>>()?;
    equal(
        &json!(callbacks),
        &json!(expected_callbacks),
        &format!("{label}/callbacks"),
    )?;
    let landed:Vec<_>=snap.landed.iter().map(|h|{let mut v=json!({"by":ids.name(h.by),"victim":ids.name(Some(h.victim)),"amount":h.amount,"weapon":h.weapon.map(|w|w.as_str()),"sig":h.sig.map_or_else(undef,|s|json!(format!("{s:?}").to_lowercase()))});if h.dot{v["dot"]=json!(true)}
if h.critical{v["crit"]=json!(true)}
if let Some(a)=h.ability{v["ability"]=json!(a)}
if h.cut!=1.{v["cut"]=json!(h.cut)}v}).collect();
    equal(&json!(landed), &e["landed"], &format!("{label}/landed"))?;
    let ability = if e["abilities"].is_null() {
        V::Null
    } else {
        json!({"fields":e["abilities"]["fields"],"mirrors":e["abilities"]["mirrors"],"dot":e["abilities"]["dot"],"dotSince":e["abilities"]["dotSince"],"teleportedAt":e["abilities"]["teleportedAt"]})
    };
    equal(
        &ids.ability_out(snap.abilities.as_ref()),
        &ability,
        &format!("{label}/abilities"),
    )?;
    equal(
        &json!(snap
            .ledger
            .iter()
            .map(|h| ids.hit_out(h))
            .collect::<Vec<_>>()),
        &json!(e["ledger"]
            .as_array()
            .ok_or("ledger")?
            .iter()
            .map(normalized_hit)
            .collect::<Vec<_>>()),
        &format!("{label}/ledger"),
    )?;
    equal(
        &json!(snap
            .deferred
            .iter()
            .map(|h| ids.hit_out(h))
            .collect::<Vec<_>>()),
        &json!(e["deferred"]
            .as_array()
            .ok_or("deferred")?
            .iter()
            .map(normalized_hit)
            .collect::<Vec<_>>()),
        &format!("{label}/deferred"),
    )?;
    Ok(())
}
pub(super) fn reads(
    ids: &Ids,
    snap: &Snapshot,
    id: PlayerId,
    at: Option<f64>,
    view: rt::StatView,
) -> R<V> {
    let p = snap
        .players
        .iter()
        .find(|p| p.id == id)
        .ok_or("read actor")?;
    let bob = snap.players.get(1).ok_or("bob")?;
    read_table(
        ids,
        &p.statuses,
        snap.time,
        (&bob.statuses.instances, Some(bob.id)),
        p.signature.bits & 1 != 0,
        at,
        view,
    )
}
pub(super) fn read_table(
    ids: &Ids,
    rt: &rt::StatusRuntime,
    now: f64,
    attacker: (&[rt::StatusInstance], Option<PlayerId>),
    frost_chilled: bool,
    at: Option<f64>,
    view: rt::StatView,
) -> R<V> {
    let (attacker, by) = attacker;
    let list = &rt.instances;
    let target = rt::untargetable(list, now);
    Ok(
        json!({"statuses":list.iter().map(|s|ids.status(s)).collect::<Vec<_>>(),"values":rt::STATUS_KINDS.map(|kind|json!({"kind":kind,"value":rt::value(list,kind,now)})),"stunned":rt::has(list,rt::StatusKind::Stun,now),"rooted":rt::has(list,rt::StatusKind::Root,now),"silenced":rt::has(list,rt::StatusKind::Silence,now),"slowed":rt::slowed(list,now,frost_chilled),"hidden":rt::hidden(list,now),"untargetable":target,"untargetableId":target,"untargetableAt":rt::untargetable(list,at.unwrap_or(now)),"casting":rt::is_casting(rt.cast),"cannotMove":rt::cannot_move(rt,now),"actionBlocked":rt::action_blocked(rt,now),"castBlocked":rt::cast_blocked(rt,now,rt::CancelCauses{stun:true,silence:true,root:true,stagger:false}),"effects":rt::effects_of(list,rt.cast,now),"healFactor":rt::heal_factor(list,now),"attackSlowFactor":rt::attack_slow_factor(list,now,view.slow_resist),"moveMultiplier":rt::move_multiplier(rt,now,view.slow_resist,view.buff_move),"dealtFactor":rt::dealt_factor(list,now),"damageFactor":rt::damage_factor(attacker,list,by,now)}),
    )
}
pub(super) fn hook_actor(ids: &Ids, p: &rh_sim::state::Player) -> V {
    json!({"id":ids.name(Some(p.id)),"hp":p.vitals.hp,"effects":p.statuses.effects,"asMult":p.modifiers.attack_rate_wire(),"immuneUntil":p.vitals.immune_until,"statuses":if p.statuses.initialized{json!(p.statuses.instances.iter().map(|s|ids.status(s)).collect::<Vec<_>>())}else{undef()}})
}
pub(super) fn hooks(ids: &Ids, list: &[HookRecord]) -> R<V> {
    Ok(json!(list.iter().map(|r|match r{
 HookRecord::NpcAttackStarted{..}|HookRecord::NpcHurt{..}|HookRecord::NpcHealed{..}|HookRecord::NpcStatusApplied{..}=>Err("NPC producer hook outside player-only status harness".into()),HookRecord::StatusApplied{actor,status,fresh}=>Ok(json!({"name":"statusApplied","actor":hook_actor(ids,actor),"status":ids.status(status),"fresh":fresh})),HookRecord::AttackStarted{actor}=>Ok(json!({"name":"attackStarted","actor":hook_actor(ids,actor)})),HookRecord::CastEnded{actor,ability,reason}=>Ok(json!({"name":"castEnded","actor":hook_actor(ids,actor),"ability":ability,"reason":reason})),HookRecord::Hurt{actor,amount,before}=>Ok(json!({"name":"hurt","actor":hook_actor(ids,actor),"amount":amount,"before":before})),
 HookRecord::CastAccepted{..}|HookRecord::CastLanded{..}=>Err("full producer hook outside isolated status harness".into()),
 HookRecord::Staggered{..}=>Err("full producer hook outside isolated status harness".into()),
 // The regional mob lifecycle hooks never fire in the isolated status harness (it has no mobs or camps).
 HookRecord::MobKilled{..}|HookRecord::CampCleared{..}=>Err("full producer hook outside isolated status harness".into()),
 HookRecord::HitLanded{hit,amount}=>Ok(json!({"name":"hitLanded","hit":ids.hit_out(hit),"amount":amount})),
 HookRecord::ArenaHurt{victim,by,source,amount}=>Ok(json!({"name":"arenaHurt","victim":ids.name(Some(*victim)),"by":source.as_ref().map(|s|ids.source_out(s)).unwrap_or(json!({"id":ids.name(*by),"name":""})),"amount":amount})),
 HookRecord::Healed{actor,amount,source}=>Ok(json!({"name":"healed","actor":hook_actor(ids,actor),"amount":amount,"source":match source{HealSource::Lifesteal=>"lifesteal",HealSource::DeathFloor=>"lastStand",_=>"other"}})),
 HookRecord::FighterOut{id,removed}=>Ok(json!({"name":"fighterOut","id":ids.name(Some(*id)),"cause":if *removed{"removed"}else{"down"}})),HookRecord::Teleported=>Ok(json!({"name":"teleported"})),HookRecord::RunEnded=>Ok(json!({"name":"runEnded"})),
}).collect::<R<Vec<_>>>()?))
}
pub(super) fn expected_hooks(v: &V) -> R<V> {
    Ok(json!(v.as_array().ok_or("hooks array")?.iter().map(|h|{let a=&h["args"][0];let actor=json!({"id":a["id"],"hp":a["state"]["hp"],"effects":a["state"]["effects"],"asMult":a["state"]["asMult"],"immuneUntil":a["state"]["immuneUntil"],"statuses":a["statuses"]});match h["name"].as_str(){Some("statusApplied")=>Ok(json!({"name":"statusApplied","actor":actor,"status":h["args"][1],"fresh":h["args"][2]})),Some("attackStarted")=>Ok(json!({"name":"attackStarted","actor":actor})),Some("castEnded")=>Ok(json!({"name":"castEnded","actor":actor,"ability":h["args"][1],"reason":h["args"][2]})),Some("hurt")=>Ok(json!({"name":"hurt","actor":actor,"amount":h["args"][1],"before":h["args"][2]})),
 Some("hitLanded")=>Ok(json!({"name":"hitLanded","hit":normalized_hit(&h["args"][0]),"amount":h["args"][1]})),
 Some("arenaHurt")=>Ok(json!({"name":"arenaHurt","victim":h["args"][0],"by":h["args"][1],"amount":h["args"][2]})),
 Some("healed")=>Ok(json!({"name":"healed","actor":actor,"amount":h["args"][1],"source":h["args"][2]})),
 Some("fighterOut")=>Ok(json!({"name":"fighterOut","id":h["args"][0],"cause":h["args"][1]})),Some("teleported")=>Ok(json!({"name":"teleported"})),Some("runEnded")=>Ok(json!({"name":"runEnded"})),
 _=>Err("unmapped source hook".into())}}).collect::<R<Vec<_>>>()?))
}
