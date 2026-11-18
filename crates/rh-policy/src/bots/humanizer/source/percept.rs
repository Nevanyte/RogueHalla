//! Typed reads of one percept: the self, HUD, fighter, mob and container views.

use super::*;

pub(super) fn read_vec2(value: &Mv, path: &str) -> Result<Vec2, String> {
    value.exact(&["x", "y"], path)?;
    Ok(Vec2::new(
        value.get("x", path)?.num(&format!("{path}.x"))?,
        value.get("y", path)?.num(&format!("{path}.y"))?,
    ))
}

pub(super) fn read_weapon(value: &Mv, path: &str) -> Result<Option<BotWeapon>, String> {
    if value.null() {
        return Ok(None);
    }
    let text = value.text(path)?;
    BotWeapon::parse(text)
        .map(Some)
        .ok_or_else(|| err(path, format!("unknown weapon {text}")))
}

pub(super) fn read_faction(value: &Mv, path: &str) -> Result<Option<FactionId>, String> {
    if value.null() {
        return Ok(None);
    }
    let text = value.text(path)?;
    FactionId::parse(text)
        .map(Some)
        .ok_or_else(|| err(path, format!("unknown faction {text}")))
}

pub(super) fn read_percept(value: &Mv, path: &str) -> Result<Percept, String> {
    value.exact(
        &[
            "now",
            "viewT",
            "self",
            "hud",
            "fighters",
            "mobs",
            "containers",
            "projectiles",
            "minimap",
            "events",
            "extras",
            "features",
        ],
        path,
    )?;
    let now = value.get("now", path)?.num(&format!("{path}.now"))?;
    // `viewT` is a declared source clock the narrowed hand Percept does not carry (it is not read by the hand); it is
    // still a required finite source fact here.
    let _view_t = value.get("viewT", path)?.num(&format!("{path}.viewT"))?;
    let self_view = read_self_view(value.get("self", path)?, &format!("{path}.self"))?;
    let hud = read_hud_view(value.get("hud", path)?, &format!("{path}.hud"))?;
    let mut fighters = Vec::new();
    for (index, fighter) in value
        .get("fighters", path)?
        .arr(&format!("{path}.fighters"))?
        .iter()
        .enumerate()
    {
        fighters.push(read_fighter(fighter, &format!("{path}.fighters[{index}]"))?);
    }
    let mut mobs = Vec::new();
    for (index, mob) in value
        .get("mobs", path)?
        .arr(&format!("{path}.mobs"))?
        .iter()
        .enumerate()
    {
        mobs.push(read_mob(mob, &format!("{path}.mobs[{index}]"))?);
    }
    let mut containers = Vec::new();
    for (index, container) in value
        .get("containers", path)?
        .arr(&format!("{path}.containers"))?
        .iter()
        .enumerate()
    {
        containers.push(read_container(
            container,
            &format!("{path}.containers[{index}]"),
        )?);
    }
    // Projectiles, the minimap and the event stream are declared source Percept facts the hand does not read; their
    // tagged shape is required but they are not asserted against a fabricated native projection.
    let _projectiles = value
        .get("projectiles", path)?
        .arr(&format!("{path}.projectiles"))?;
    {
        let minimap_path = format!("{path}.minimap");
        let minimap = value.get("minimap", path)?;
        minimap.exact(&["t", "mobs", "gatesLocked"], &minimap_path)?;
        let _t = minimap
            .get("t", &minimap_path)?
            .num(&format!("{minimap_path}.t"))?;
        let _dots = minimap
            .get("mobs", &minimap_path)?
            .arr(&format!("{minimap_path}.mobs"))?;
        let _locked = minimap
            .get("gatesLocked", &minimap_path)?
            .boolean(&format!("{minimap_path}.gatesLocked"))?;
    }
    let _events = value.get("events", path)?.arr(&format!("{path}.events"))?;
    let extras = read_extras(value.get("extras", path)?, &format!("{path}.extras"))?;
    // `Percept.features` is a declared source fact the narrowed hand Percept does not carry and the humanizer never
    // reads; the profile feature set (validated above) is the hand's gate input.
    let _percept_features =
        read_features(value.get("features", path)?, &format!("{path}.features"))?;
    Ok(Percept {
        now,
        self_view,
        hud,
        fighters,
        mobs,
        containers,
        extras,
    })
}

pub(super) const SELF_KEYS: [&str; 27] = [
    "id",
    "name",
    "x",
    "y",
    "facing",
    "status",
    "hp",
    "maxHp",
    "barrier",
    "faction",
    "weapons",
    "slot",
    "attackTarget",
    "drawStartedAt",
    "readyAt",
    "immuneUntil",
    "sig",
    "brand",
    "power",
    "asMult",
    "windupMult",
    "speedMult",
    "kills",
    "mobKills",
    "gv",
    "bounty",
    "moveSeq",
];

pub(super) fn read_self_view(value: &Mv, path: &str) -> Result<SelfView, String> {
    value.exact(&SELF_KEYS, path)?;
    let get = |key: &str| value.get(key, path);
    let num = |key: &str| -> Result<f64, String> { get(key)?.num(&format!("{path}.{key}")) };
    let _name = get("name")?.text(&format!("{path}.name"))?;
    let _facing = num("facing")?;
    let _barrier = num("barrier")?;
    let _draw_started_at = num("drawStartedAt")?;
    let _immune_until = num("immuneUntil")?;
    let _sig = num("sig")?;
    let _brand = num("brand")?;
    let _power = num("power")?;
    let _as_mult = num("asMult")?;
    let _windup_mult = num("windupMult")?;
    let _speed_mult = num("speedMult")?;
    let _kills = num("kills")?;
    let _mob_kills = num("mobKills")?;
    let _gv = num("gv")?;
    let _bounty = num("bounty")?;
    let _move_seq = num("moveSeq")?;
    let weapons = get("weapons")?.arr(&format!("{path}.weapons"))?;
    if weapons.len() != 2 {
        return Err(err(
            &format!("{path}.weapons"),
            format!("expected two slots, found {}", weapons.len()),
        ));
    }
    let slot = get("slot")?.uint(&format!("{path}.slot"))?;
    if slot > 1 {
        return Err(err(
            &format!("{path}.slot"),
            format!("slot {slot} is not 0 or 1"),
        ));
    }
    let attack_target = match get("attackTarget")? {
        Mv::Null => None,
        Mv::Str(target) => Some(target.clone()),
        _ => {
            return Err(err(
                &format!("{path}.attackTarget"),
                "expected null or a fighter id",
            ))
        }
    };
    let owner_lock = match &attack_target {
        None => OwnerLock::Clear,
        Some(target) => OwnerLock::Held {
            target: Some(target.clone()),
        },
    };
    Ok(SelfView {
        id: get("id")?.text(&format!("{path}.id"))?.to_string(),
        faction: read_faction(get("faction")?, &format!("{path}.faction"))?,
        x: num("x")?,
        y: num("y")?,
        facing: num("facing")?,
        status: get("status")?.text(&format!("{path}.status"))?.to_string(),
        hp: num("hp")?,
        max_hp: num("maxHp")?,
        // The direct hand supplement's source SelfView has no effect bits; the narrowed hand never reads them.
        effects: 0,
        weapons: [
            read_weapon(&weapons[0], &format!("{path}.weapons[0]"))?,
            read_weapon(&weapons[1], &format!("{path}.weapons[1]"))?,
        ],
        slot: slot as u8,
        attack_target,
        owner_lock: Some(owner_lock),
        ready_at: num("readyAt")?,
    })
}

pub(super) fn read_hud_view(value: &Mv, path: &str) -> Result<HudView, String> {
    value.exact(
        &[
            "phase",
            "training",
            "lobbyEndsAt",
            "raidStartedAt",
            "patienceEndsAt",
            "suddenDeathAt",
            "inColiseum",
            "gatesLocked",
            "startedWith",
            "alive",
            "playerCount",
            "minPlayers",
            "winner",
        ],
        path,
    )?;
    let get = |key: &str| value.get(key, path);
    for key in [
        "lobbyEndsAt",
        "raidStartedAt",
        "patienceEndsAt",
        "suddenDeathAt",
    ] {
        get(key)?.num(&format!("{path}.{key}"))?;
    }
    for key in ["training", "inColiseum", "gatesLocked"] {
        get(key)?.boolean(&format!("{path}.{key}"))?;
    }
    for key in ["startedWith", "alive", "playerCount", "minPlayers"] {
        get(key)?.uint(&format!("{path}.{key}"))?;
    }
    get("winner")?.text(&format!("{path}.winner"))?;
    Ok(HudView {
        phase: get("phase")?.text(&format!("{path}.phase"))?.to_string(),
        gates_locked: get("gatesLocked")?.boolean(&format!("{path}.gatesLocked"))?,
    })
}

pub(super) fn read_fighter(value: &Mv, path: &str) -> Result<FighterView, String> {
    value.exact(
        &[
            "id",
            "name",
            "bot",
            "x",
            "y",
            "facing",
            "status",
            "hpFrac",
            "barrierFrac",
            "faction",
            "inHand",
            "drawStartedAt",
            "bounty",
            "immune",
            "sig",
            "brand",
            "targetable",
            "vx",
            "vy",
            "dist",
            "los",
            "tint",
            "central",
        ],
        path,
    )?;
    let get = |key: &str| value.get(key, path);
    get("bot")?.boolean(&format!("{path}.bot"))?;
    get("immune")?.boolean(&format!("{path}.immune"))?;
    get("los")?.boolean(&format!("{path}.los"))?;
    get("central")?.boolean(&format!("{path}.central"))?;
    read_faction(get("faction")?, &format!("{path}.faction"))?;
    read_weapon(get("inHand")?, &format!("{path}.inHand"))?;
    for key in [
        "facing",
        "hpFrac",
        "barrierFrac",
        "drawStartedAt",
        "bounty",
        "sig",
        "brand",
        "dist",
        "tint",
    ] {
        get(key)?.num(&format!("{path}.{key}"))?;
    }
    Ok(FighterView {
        id: get("id")?.text(&format!("{path}.id"))?.to_string(),
        x: get("x")?.num(&format!("{path}.x"))?,
        y: get("y")?.num(&format!("{path}.y"))?,
        vx: get("vx")?.num(&format!("{path}.vx"))?,
        vy: get("vy")?.num(&format!("{path}.vy"))?,
        status: get("status")?.text(&format!("{path}.status"))?.to_string(),
        targetable: get("targetable")?.boolean(&format!("{path}.targetable"))?,
    })
}

pub(super) fn read_mob(value: &Mv, path: &str) -> Result<MobView, String> {
    value.exact(
        &[
            "id",
            "kind",
            "name",
            "type",
            "faction",
            "x",
            "y",
            "facing",
            "status",
            "hpFrac",
            "inHand",
            "drawStartedAt",
            "sig",
            "brand",
            "vx",
            "vy",
            "dist",
            "los",
            "central",
        ],
        path,
    )?;
    let get = |key: &str| value.get(key, path);
    get("name")?.text(&format!("{path}.name"))?;
    get("type")?.text(&format!("{path}.type"))?;
    read_faction(get("faction")?, &format!("{path}.faction"))?;
    read_weapon(get("inHand")?, &format!("{path}.inHand"))?;
    get("los")?.boolean(&format!("{path}.los"))?;
    get("central")?.boolean(&format!("{path}.central"))?;
    for key in ["facing", "hpFrac", "drawStartedAt", "sig", "brand", "dist"] {
        get(key)?.num(&format!("{path}.{key}"))?;
    }
    Ok(MobView {
        id: get("id")?.text(&format!("{path}.id"))?.to_string(),
        x: get("x")?.num(&format!("{path}.x"))?,
        y: get("y")?.num(&format!("{path}.y"))?,
        vx: get("vx")?.num(&format!("{path}.vx"))?,
        vy: get("vy")?.num(&format!("{path}.vy"))?,
        status: get("status")?.text(&format!("{path}.status"))?.to_string(),
        kind: get("kind")?.text(&format!("{path}.kind"))?.to_string(),
    })
}

pub(super) fn read_container(value: &Mv, path: &str) -> Result<ContainerView, String> {
    value.exact(
        &[
            "id",
            "kind",
            "x",
            "y",
            "label",
            "closed",
            "weapon",
            "interactable",
            "dist",
        ],
        path,
    )?;
    let get = |key: &str| value.get(key, path);
    get("label")?.text(&format!("{path}.label"))?;
    get("closed")?.boolean(&format!("{path}.closed"))?;
    read_weapon(get("weapon")?, &format!("{path}.weapon"))?;
    get("dist")?.num(&format!("{path}.dist"))?;
    Ok(ContainerView {
        id: get("id")?.text(&format!("{path}.id"))?.to_string(),
        kind: get("kind")?.text(&format!("{path}.kind"))?.to_string(),
        x: get("x")?.num(&format!("{path}.x"))?,
        y: get("y")?.num(&format!("{path}.y"))?,
        interactable: get("interactable")?.boolean(&format!("{path}.interactable"))?,
    })
}

pub(super) fn read_extras(value: &Mv, path: &str) -> Result<PerceptExtras, String> {
    // This direct hand supplement declares no extras; the two reads the hand can make (chests, wish) stay absent rather
    // than being reconstructed from a future/other profile.
    value.exact(&[], path)?;
    Ok(PerceptExtras::default())
}

pub(super) fn read_stimulus(value: &Mv, path: &str) -> Result<Stimulus, String> {
    value.closed(&["kind", "at", "central"], &["subject", "value"], path)?;
    let kind_text = value.get("kind", path)?.text(&format!("{path}.kind"))?;
    let kind = StimulusKind::parse(kind_text).ok_or_else(|| {
        err(
            &format!("{path}.kind"),
            format!("unknown stimulus kind {kind_text}"),
        )
    })?;
    let subject = match value.optional("subject", path)? {
        None | Some(Mv::Null) => None,
        Some(Mv::Str(subject)) => Some(subject.clone()),
        Some(_) => return Err(err(&format!("{path}.subject"), "expected null or a string")),
    };
    let value_of = match value.optional("value", path)? {
        None => None,
        Some(number) => Some(number.num(&format!("{path}.value"))?),
    };
    Ok(Stimulus {
        kind,
        at: value.get("at", path)?.num(&format!("{path}.at"))?,
        subject,
        central: value
            .get("central", path)?
            .boolean(&format!("{path}.central"))?,
        value: value_of,
    })
}

pub(super) fn read_timing(value: &Mv, path: &str) -> Result<IntentTiming, String> {
    let kind = value.get("kind", path)?.text(&format!("{path}.kind"))?;
    match kind {
        "step" => {
            value.exact(&["kind"], path)?;
            Ok(IntentTiming::Step {})
        }
        "timed" => {
            value.exact(&["kind", "due"], path)?;
            Ok(IntentTiming::Timed {
                due: value.get("due", path)?.num(&format!("{path}.due"))?,
            })
        }
        "react" => {
            value.exact(&["kind", "stimulus"], path)?;
            Ok(IntentTiming::React {
                stimulus: read_stimulus(value.get("stimulus", path)?, &format!("{path}.stimulus"))?,
            })
        }
        other => Err(err(
            &format!("{path}.kind"),
            format!("unknown timing {other}"),
        )),
    }
}

pub(super) fn read_plan_msg(value: &Mv, path: &str) -> Result<PlanMsg, String> {
    value.closed(&[], &["spawn", "boon", "ready"], path)?;
    let spawn = match value.optional("spawn", path)? {
        None => None,
        Some(spawn) => Some(spawn.uint(&format!("{path}.spawn"))? as u8),
    };
    let boon = match value.optional("boon", path)? {
        None => None,
        Some(boon) => Some(boon.uint(&format!("{path}.boon"))? as u8),
    };
    let ready = match value.optional("ready", path)? {
        None => None,
        Some(ready) => Some(ready.boolean(&format!("{path}.ready"))?),
    };
    Ok(PlanMsg { spawn, boon, ready })
}

pub(super) fn read_choose_msg(value: &Mv, path: &str) -> Result<ChooseMsg, String> {
    value.closed(&["chest", "pick"], &["replace"], path)?;
    let replace = match value.optional("replace", path)? {
        None => None,
        Some(replace) => Some(replace.uint(&format!("{path}.replace"))? as u8),
    };
    Ok(ChooseMsg {
        chest: value
            .get("chest", path)?
            .text(&format!("{path}.chest"))?
            .to_string(),
        pick: value
            .get("pick", path)?
            .i8_number(&format!("{path}.pick"))?,
        replace,
    })
}
