//! Reading the corpus envelope: the open map, the profiles, operations, seeds, traces and counts.

use super::*;

// ------------------------------------------------------------------------------------------------------------ reading

pub(super) const PROFILE_ORDER: [&str; 6] = [
    "absent",
    "empty",
    "attack-only",
    "target-only",
    "partial",
    "all",
];

pub(super) fn read_hand_corpus(source: &Value) -> Result<HandCorpus, String> {
    let format = source
        .get("format")
        .and_then(Value::as_str)
        .ok_or_else(|| err("source", "missing format"))?;
    if format != "rh-bot-private-brain-source/1" {
        return Err(err("source.format", format!("unexpected format {format}")));
    }
    let method_value = source
        .get("methodValue")
        .and_then(Value::as_str)
        .ok_or_else(|| err("source", "missing methodValue"))?;
    if method_value != "rh-method-value/1" {
        return Err(err(
            "source.methodValue",
            format!("unexpected method-value format {method_value}"),
        ));
    }
    let (map, map_hash_u32) = read_open_map(source)?;
    let profiles_json = source
        .get("handSupplement")
        .and_then(Value::as_array)
        .ok_or_else(|| err("source.handSupplement", "expected an array"))?;
    if profiles_json.len() != PROFILE_ORDER.len() {
        return Err(err(
            "source.handSupplement",
            format!(
                "expected {} direct hand profiles, found {}",
                PROFILE_ORDER.len(),
                profiles_json.len()
            ),
        ));
    }
    let mut profiles = Vec::with_capacity(PROFILE_ORDER.len());
    for (index, expected) in PROFILE_ORDER.iter().enumerate() {
        profiles.push(read_profile(&profiles_json[index], expected, index)?);
    }
    Ok(HandCorpus {
        map,
        map_hash_u32,
        profiles,
    })
}

pub(super) fn read_open_map(source: &Value) -> Result<(BotMap, u32), String> {
    let maps = source
        .get("maps")
        .and_then(Value::as_array)
        .ok_or_else(|| err("source.maps", "expected an array"))?;
    let entry = maps
        .iter()
        .find(|entry| entry.get("id").and_then(Value::as_str) == Some("open"))
        .ok_or_else(|| err("source.maps", "missing the open map"))?;
    let object = entry
        .as_object()
        .ok_or_else(|| err("source.maps.open", "expected an object"))?;
    for key in object.keys() {
        if !["id", "mapHashU32", "value"].contains(&key.as_str()) {
            return Err(err("source.maps.open", format!("unexpected member {key}")));
        }
    }
    let hash = object
        .get("mapHashU32")
        .and_then(Value::as_u64)
        .filter(|hash| *hash <= u64::from(u32::MAX))
        .ok_or_else(|| err("source.maps.open.mapHashU32", "expected a u32"))? as u32;
    let value = object
        .get("value")
        .ok_or_else(|| err("source.maps.open", "missing value"))?;
    let decoded = decode_mv(value, "source.maps.open.value")?;
    let map_path = "source.maps.open.value";
    decoded.exact(
        &[
            "seed",
            "w",
            "h",
            "tiles",
            "terrain",
            "props",
            "attempt",
            "territoryGrid",
            "regionGrid",
            "coliseum",
            "regions",
            "camps",
            "spawnPool",
            "buildings",
            "spawns",
            "crateSpots",
            "groundSpots",
            "features",
            "featureGrid",
        ],
        map_path,
    )?;
    let seed = decoded
        .get("seed", map_path)?
        .uint(&format!("{map_path}.seed"))?;
    if seed != 0 {
        return Err(err(
            &format!("{map_path}.seed"),
            format!("the declared open arena seed is {seed}, expected 0"),
        ));
    }
    let w = decoded.get("w", map_path)?.uint(&format!("{map_path}.w"))?;
    let h = decoded.get("h", map_path)?.uint(&format!("{map_path}.h"))?;
    if w == 0 || h == 0 {
        return Err(err(map_path, "empty map dimensions"));
    }
    let cells = (w as usize) * (h as usize);
    decoded
        .get("attempt", map_path)?
        .uint(&format!("{map_path}.attempt"))?;
    let tiles = read_byte_grid(
        decoded.get("tiles", map_path)?,
        &format!("{map_path}.tiles"),
        cells,
    )?;
    // The other map layers are declared GameMap facts; the hand's click model reads only the collision layer (tiles),
    // so the remaining declared grids/records are shape-checked here and not reconstructed.
    for key in ["terrain", "territoryGrid", "regionGrid", "featureGrid"] {
        read_byte_grid(
            decoded.get(key, map_path)?,
            &format!("{map_path}.{key}"),
            cells,
        )?;
    }
    for key in [
        "props",
        "regions",
        "camps",
        "spawnPool",
        "buildings",
        "spawns",
        "crateSpots",
        "groundSpots",
        "features",
    ] {
        decoded
            .get(key, map_path)?
            .arr(&format!("{map_path}.{key}"))?;
    }
    decoded
        .get("coliseum", map_path)?
        .obj(&format!("{map_path}.coliseum"))?;
    Ok((BotMap::new(i64::from(w), i64::from(h), tiles), hash))
}

pub(super) fn read_byte_grid(value: &Mv, path: &str, cells: usize) -> Result<Vec<u8>, String> {
    let bytes = match value {
        Mv::U8(bytes) => bytes.clone(),
        _ => return Err(err(path, "expected a u8 array")),
    };
    if bytes.len() != cells {
        return Err(err(
            path,
            format!("{} bytes for a {cells}-cell grid", bytes.len()),
        ));
    }
    Ok(bytes)
}

pub(super) fn read_profile(
    value: &Value,
    expected: &str,
    profile_index: usize,
) -> Result<HandProfile, String> {
    let path = format!("source.handSupplement[{profile_index}]");
    let object = value
        .as_object()
        .ok_or_else(|| err(&path, "expected an object"))?;
    for key in object.keys() {
        if !["profile", "features", "tier", "seeds", "operations"].contains(&key.as_str()) {
            return Err(err(&path, format!("unexpected member {key}")));
        }
    }
    let name = object
        .get("profile")
        .and_then(Value::as_str)
        .ok_or_else(|| err(&path, "missing profile name"))?;
    if name != expected {
        return Err(err(
            &format!("{path}.profile"),
            format!("expected profile {expected}, found {name}"),
        ));
    }
    let features = read_features(
        &decode_mv(
            object
                .get("features")
                .ok_or_else(|| err(&path, "missing features"))?,
            &format!("{path}.features"),
        )?,
        &format!("{path}.features"),
    )?;
    let tier = read_tier(
        &decode_mv(
            object
                .get("tier")
                .ok_or_else(|| err(&path, "missing tier"))?,
            &format!("{path}.tier"),
        )?,
        &format!("{path}.tier"),
    )?;
    // The direct hand supplement declares the four runtime seed numbers as plain JSON (an input descriptor, not a
    // tagged method value); they are still required to be exact non-negative 32-bit integers.
    let seeds = read_seeds(
        object
            .get("seeds")
            .ok_or_else(|| err(&path, "missing seeds"))?,
        &format!("{path}.seeds"),
    )?;
    let operations = object
        .get("operations")
        .and_then(Value::as_array)
        .ok_or_else(|| err(&format!("{path}.operations"), "expected an array"))?;
    if operations.len() != 2 {
        return Err(err(
            &format!("{path}.operations"),
            format!(
                "expected 2 real submit/release operations, found {}",
                operations.len()
            ),
        ));
    }
    let mut prof = HandProfile {
        name: name.to_string(),
        features,
        tier,
        seeds,
        operations: Vec::with_capacity(operations.len()),
    };
    let mut drawn = Counts::default();
    for (index, operation) in operations.iter().enumerate() {
        let op = read_operation(operation, &format!("{path}.operations[{index}]"))?;
        let label = format!("{}.operations[{index}]", prof.name);
        // The declared draw indices are the actual chronological per-lane sequence of one profile lifetime: submit
        // first, then the later release of the same hand. They must start at 0, be contiguous, and agree with the
        // recorded cumulative counts after every operation; the decoder never reorders traces.
        for draw in op.submit_trace.iter().chain(op.release_trace.iter()) {
            if draw.lane == Lane::Decide {
                return Err(err(
                    &label,
                    "the direct hand reads only the motor and lapse lanes but a decide draw is recorded",
                ));
            }
            if draw.index != drawn.get(draw.lane) {
                return Err(err(
                    &label,
                    format!(
                        "{} draw index {} is not the next declared index {}",
                        match draw.lane {
                            Lane::Decide => "decide",
                            Lane::Motor => "motor",
                            Lane::Lapse => "lapse",
                        },
                        draw.index,
                        drawn.get(draw.lane)
                    ),
                ));
            }
            drawn.add(draw.lane, 1);
        }
        if drawn != op.counts {
            return Err(err(
                &label,
                format!(
                    "recorded counts {:?} disagree with the trace's cumulative counts {:?}",
                    op.counts, drawn
                ),
            ));
        }
        if drawn.decide != 0 {
            return Err(err(&label, "the direct hand records no decide draws"));
        }
        prof.operations.push(op);
    }
    Ok(prof)
}

pub(super) fn read_operation(value: &Value, path: &str) -> Result<HandOperation, String> {
    let object = value
        .as_object()
        .ok_or_else(|| err(path, "expected an object"))?;
    for key in object.keys() {
        if ![
            "percept",
            "intents",
            "submitTrace",
            "releasePercept",
            "result",
            "releaseTrace",
            "misses",
            "stats",
            "counts",
            "memory",
        ]
        .contains(&key.as_str())
        {
            return Err(err(path, format!("unexpected member {key}")));
        }
    }
    for key in [
        "percept",
        "intents",
        "submitTrace",
        "releasePercept",
        "result",
        "releaseTrace",
        "misses",
        "stats",
        "counts",
        "memory",
    ] {
        if !object.contains_key(key) {
            return Err(err(path, format!("missing member {key}")));
        }
    }
    Ok(HandOperation {
        percept: read_percept(
            &decode_mv(&object["percept"], &format!("{path}.percept"))?,
            &format!("{path}.percept"),
        )?,
        intents: read_intents(
            &decode_mv(&object["intents"], &format!("{path}.intents"))?,
            &format!("{path}.intents"),
        )?,
        submit_trace: read_trace(
            &decode_mv(&object["submitTrace"], &format!("{path}.submitTrace"))?,
            &format!("{path}.submitTrace"),
        )?,
        release_percept: read_percept(
            &decode_mv(&object["releasePercept"], &format!("{path}.releasePercept"))?,
            &format!("{path}.releasePercept"),
        )?,
        result: read_timed_commands(
            &decode_mv(&object["result"], &format!("{path}.result"))?,
            &format!("{path}.result"),
        )?,
        release_trace: read_trace(
            &decode_mv(&object["releaseTrace"], &format!("{path}.releaseTrace"))?,
            &format!("{path}.releaseTrace"),
        )?,
        misses: read_misses(
            &decode_mv(&object["misses"], &format!("{path}.misses"))?,
            &format!("{path}.misses"),
        )?,
        stats: read_stats(
            &decode_mv(&object["stats"], &format!("{path}.stats"))?,
            &format!("{path}.stats"),
        )?,
        counts: read_counts(&object["counts"], &format!("{path}.counts"))?,
        memory_keys: read_memory_keys(&object["memory"], &format!("{path}.memory"))?,
    })
}

pub(super) fn read_features(value: &Mv, path: &str) -> Result<Option<Features>, String> {
    if matches!(value, Mv::Undefined) {
        return Ok(None);
    }
    value.exact(
        &[
            "messageTypes",
            "wishes",
            "statusEffects",
            "fields",
            "planning",
            "chests",
            "skills",
            "relics",
            "camps",
            "hazards",
        ],
        path,
    )?;
    Ok(Some(Features {
        message_types: value
            .get("messageTypes", path)?
            .string_list(&format!("{path}.messageTypes"))?,
        wishes: value
            .get("wishes", path)?
            .boolean(&format!("{path}.wishes"))?,
        status_effects: value
            .get("statusEffects", path)?
            .boolean(&format!("{path}.statusEffects"))?,
        fields: value
            .get("fields", path)?
            .boolean(&format!("{path}.fields"))?,
        planning: value
            .get("planning", path)?
            .boolean(&format!("{path}.planning"))?,
        chests: value
            .get("chests", path)?
            .boolean(&format!("{path}.chests"))?,
        skills: value
            .get("skills", path)?
            .boolean(&format!("{path}.skills"))?,
        relics: value
            .get("relics", path)?
            .boolean(&format!("{path}.relics"))?,
        camps: value
            .get("camps", path)?
            .boolean(&format!("{path}.camps"))?,
        hazards: value
            .get("hazards", path)?
            .boolean(&format!("{path}.hazards"))?,
    }))
}

pub(super) fn read_seeds(value: &Value, path: &str) -> Result<Seeds, String> {
    let fields = value
        .as_object()
        .ok_or_else(|| err(path, "expected an object"))?;
    for key in fields.keys() {
        if !["decide", "motor", "lapse", "knowledge"].contains(&key.as_str()) {
            return Err(err(path, format!("unexpected member {key}")));
        }
    }
    let seed = |key: &str| -> Result<u32, String> {
        fields
            .get(key)
            .and_then(Value::as_u64)
            .filter(|seed| *seed <= u64::from(u32::MAX))
            .map(|seed| seed as u32)
            .ok_or_else(|| err(&format!("{path}.{key}"), "expected a u32 seed"))
    };
    Ok(Seeds {
        decide: seed("decide")?,
        motor: seed("motor")?,
        lapse: seed("lapse")?,
        knowledge: seed("knowledge")?,
    })
}

pub(super) fn read_trace(value: &Mv, path: &str) -> Result<Vec<Draw>, String> {
    let mut draws = Vec::new();
    for (index, entry) in value.arr(path)?.iter().enumerate() {
        let entry_path = format!("{path}[{index}]");
        entry.exact(&["kind", "lane", "index", "value"], &entry_path)?;
        let kind = entry
            .get("kind", &entry_path)?
            .text(&format!("{entry_path}.kind"))?;
        if kind != "rng" {
            return Err(err(
                &format!("{entry_path}.kind"),
                format!("unexpected trace kind {kind}"),
            ));
        }
        let lane_name = entry
            .get("lane", &entry_path)?
            .text(&format!("{entry_path}.lane"))?;
        let lane = Lane::parse(lane_name).ok_or_else(|| {
            err(
                &format!("{entry_path}.lane"),
                format!("unknown lane {lane_name}"),
            )
        })?;
        let draw_index = entry
            .get("index", &entry_path)?
            .uint(&format!("{entry_path}.index"))?;
        let value = entry
            .get("value", &entry_path)?
            .num(&format!("{entry_path}.value"))?;
        if !(0.0..1.0).contains(&value) {
            return Err(err(
                &format!("{entry_path}.value"),
                format!("a raw uniform draw left [0,1): {value}"),
            ));
        }
        draws.push(Draw {
            lane,
            index: draw_index,
            value,
        });
    }
    Ok(draws)
}

pub(super) fn read_counts(value: &Value, path: &str) -> Result<Counts, String> {
    let fields = value
        .as_object()
        .ok_or_else(|| err(path, "expected an object"))?;
    for key in fields.keys() {
        if !["decide", "motor", "lapse"].contains(&key.as_str()) {
            return Err(err(path, format!("unexpected member {key}")));
        }
    }
    let count = |key: &str| -> Result<u32, String> {
        fields
            .get(key)
            .and_then(Value::as_u64)
            .filter(|count| *count <= u64::from(u32::MAX))
            .map(|count| count as u32)
            .ok_or_else(|| err(&format!("{path}.{key}"), "expected a u32 count"))
    };
    Ok(Counts {
        decide: count("decide")?,
        motor: count("motor")?,
        lapse: count("lapse")?,
    })
}

/// The declared `memory` is a side observation of the real Memory the supplement constructed; the direct hand never
/// reads it, so only its declared ordered eight-field shape is checked (its tagged Map/Set contents are not decoded and
/// are explicitly outside the narrow hand lease).
pub(super) fn read_memory_keys(value: &Value, path: &str) -> Result<Vec<String>, String> {
    let fields = value
        .as_object()
        .ok_or_else(|| err(path, "expected a tagged object"))?;
    let tag = fields
        .get("$")
        .and_then(Value::as_str)
        .ok_or_else(|| err(path, "missing $ tag"))?;
    if tag != "object" {
        return Err(err(path, format!("unexpected memory tag {tag}")));
    }
    exact_tag_fields(fields, &["$", "entries"], path)?;
    let entries = fields
        .get("entries")
        .and_then(Value::as_array)
        .ok_or_else(|| err(path, "expected an entries array"))?;
    let mut keys = Vec::with_capacity(entries.len());
    for (index, pair) in entries.iter().enumerate() {
        let pair_path = format!("{path}.entries[{index}]");
        let pair = pair
            .as_array()
            .ok_or_else(|| err(&pair_path, "expected [key, value]"))?;
        if pair.len() != 2 {
            return Err(err(&pair_path, "expected [key, value]"));
        }
        let key = pair[0]
            .as_str()
            .ok_or_else(|| err(&pair_path, "expected a string key"))?
            .to_string();
        if keys.contains(&key) {
            return Err(err(&pair_path, format!("duplicate memory field {key}")));
        }
        keys.push(key);
    }
    let expected = [
        "fighters",
        "mobs",
        "camps",
        "containers",
        "self",
        "stimuli",
        "bounties",
        "choices",
    ];
    if keys != expected {
        return Err(err(
            path,
            format!(
                "memory fields {keys:?} are not the declared eight-field snapshot {expected:?}"
            ),
        ));
    }
    Ok(keys)
}
