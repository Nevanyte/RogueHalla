// The signature landmarks' generated-map pins, in one place (P-MAPGEN-SIG-BLUEPRINT-1). A packet meant to preserve
// maps proves them against these; a packet that changes an approved signature landmark re-pins only its option-on
// arrays using `mapgen_signature_blueprint.rs` `print_signature_pins` (`--ignored --nocapture`) and records why in its
// brief. The original pins were measured before the generic stamp on the stub commit of P-MAPGEN-SIG-BLUEPRINT-1 (the
// generator of feature/unity-rework @ 9890f291b, which holds the blueprint church and the Plague pools). The signature
// option-on pins were last changed by P-STORM-CHURCH-MAP-2: the approved rear church door changes church tiles and its
// generated map hashes. The option-off pins remain from the original P-MAPGEN-SIG-BLUEPRINT-1 baseline.
//
// Anchors are not in `map_hash`; the JSON digests below sort each anchor's `entrance` row-major first, so that
// P-MAPGEN-SIG-BLUEPRINT-2, which sorts the entrances in the generator (nothing in the generator, the server or the
// client reads their order), needs no re-pin. The order itself is pinned by `mapgen_signature_table.rs`
// `every_anchors_entrance_is_row_major` (P-MAPGEN-SIG-BLUEPRINT-2).
#![allow(dead_code)]
use rh_sim::mapdata::GameMapData;
use rh_sim::mapgen::landmarks::signature::SignatureContext;
use rh_sim::mapgen::landmarks::{recipes, stage};
use rh_sim::mapgen::{attempt, coliseum};

/// FNV-1a 64.
pub fn fnv(bytes: &[u8]) -> u64 {
    let mut h: u64 = 0xcbf2_9ce4_8422_2325;
    for &b in bytes {
        h ^= u64::from(b);
        h = h.wrapping_mul(0x0100_0000_01b3);
    }
    h
}

/// `m` with every anchor's entrance sorted row-major (y, then x).
pub fn with_sorted_entrances(m: &GameMapData) -> GameMapData {
    let mut m = m.clone();
    for a in &mut m.anchors {
        a.entrance.sort_by_key(|p| (p.y, p.x));
    }
    m
}

/// FNV-1a 64 of the map's JSON with every anchor's entrance sorted row-major.
pub fn map_digest(m: &GameMapData) -> u64 {
    fnv(&serde_json::to_vec(&with_sorted_entrances(m)).expect("map JSON"))
}

/// What the Landmarks phase leaves on (seed, attempt), option on, as one number: 1 when the prefix fails, 2 when the
/// legacy landmark stage refuses, 3 when the signature step refuses the attempt; otherwise FNV-1a 64 of the step's
/// anchors (JSON, entrances sorted row-major), the stage's tiles, terrain, feature owners (u16 LE), features (JSON),
/// protection, the masks `boxes` and `near1`, and the step's terrain layer.
pub fn landmarks_digest(seed: i64, n: u64) -> u64 {
    let fixed = coliseum::fixed_layout();
    let context = attempt::AttemptContext::new(&fixed);
    let Some(frame) =
        attempt::begin_attempt_signature(&context, seed, n, true).expect("prefix input")
    else {
        return 1;
    };
    let Some(mut st) = stage::StageContext::new(&fixed)
        .apply(&frame, &recipes::Recipes::default())
        .expect("stage input")
    else {
        return 2;
    };
    let Some(placed) = SignatureContext::new(&fixed)
        .apply(&frame, &mut st)
        .expect("signature input")
    else {
        return 3;
    };
    let mut bytes = Vec::new();
    for a in placed.anchors() {
        let mut a = a.clone();
        a.entrance.sort_by_key(|p| (p.y, p.x));
        bytes.extend(serde_json::to_vec(&a).expect("anchor JSON"));
    }
    bytes.extend(&st.state.tiles);
    bytes.extend(&st.state.terrain);
    for &o in &st.state.feature_owner {
        bytes.extend(o.to_le_bytes());
    }
    for f in &st.state.features {
        bytes.extend(serde_json::to_vec(f).expect("feature JSON"));
    }
    bytes.extend(&st.protect);
    bytes.extend(&st.masks.boxes);
    bytes.extend(&st.masks.near1);
    bytes.extend(&placed.terrain);
    fnv(&bytes)
}

/// Today's option-off generator, seeds 1-8: (seed, `map_hash`, FNV-1a 64 of the map's JSON) (G8). Moved here
/// unchanged by P-MAPGEN-SIG-BLUEPRINT-2 from the seven `mapgen_signature_*` oracles that each carried a copy (`_fire`,
/// `_frost`, `_frost_lake`, `_reserve`, `_scale`, `_church`, `_bog_tree`); option off has no anchors, so the plain JSON
/// digest (`fnv` of `serde_json::to_vec(&map)`) is meant.
pub fn pinned_off() -> [(i64, &'static str, u64); 8] {
    [
        (1, "d09b4fd9", 0xf638_ce4a_8563_d41d),
        (2, "190cd50f", 0xf13c_e037_fbd7_f651),
        (3, "b82117b8", 0xeb4f_0525_0310_7414),
        (4, "bec16d65", 0x785a_11c3_0a31_46b7),
        (5, "25032ba7", 0x0f6e_a513_6ba7_ca0d),
        (6, "2d58fdf5", 0x600e_c62d_a4c6_d207),
        (7, "6255dbbe", 0x7188_b411_554a_ddb7),
        (8, "dbb3602f", 0xd855_dc2a_be46_a4eb),
    ]
}

/// Seeds 1-32, option on (`pipeline::Options { signature: true, ..Default::default() }`, `certified_styled`): (seed,
/// the accepted attempt, `map_hash`, `map_digest`). Re-pinned by P-MAPGEN-SIG-SCALE-4 (2026-11-14: the 22 x 22 volcano
/// with three streams and the Fire fan; 89 attempts over the 32 seeds, worst 9 on seed 22), then by P-LAND-FIRE-1
/// (2026-11-14: Fire craters keep clear of the lava and shrink to fit; 15 of the 32 maps moved; 94 attempts, worst 9
/// on seeds 22 and 30), then by P-MAPGEN-SIG-HOLY-1 (2026-11-14: the Holy hall's reserved site and its two camps move
/// every option-on Prefix, so all 32 maps; 96 attempts, worst 11 on seed 11), then by P-LAND-FROST-1 (2026-11-14: the
/// Frost lake built first on the cave's softly reserved room; all 32 maps; 97 attempts, worst 10 on seed 16), then
/// P-STORM-CHURCH-MAP-2 (2026-11-19: the approved rear church opening changes signature-on map hashes; option-off pins
/// remain unchanged).
pub fn pinned_on_maps() -> [(i64, u64, &'static str, u64); 32] {
    [
        (1, 1, "12ecb887", 0x731bc496b476d845),
        (2, 2, "d05c8266", 0xbd1f04f4cb0752b5),
        (3, 1, "e261090e", 0xa1b26a646bdd46a3),
        (4, 1, "e8a2e4a4", 0x1d674bff040394a8),
        (5, 0, "625074c5", 0x9b8541f267355cca),
        (6, 0, "9f576b5d", 0x03850a6ba8653f1a),
        (7, 1, "f878537c", 0x1e700f116bb9e3ab),
        (8, 2, "5c026b14", 0xf46728d5dea6e111),
        (9, 0, "f1fddcd1", 0x061064351d69dac4),
        (10, 2, "69cd6008", 0x4f6c64b924500e25),
        (11, 0, "5405ddf6", 0x202190c58d0db72a),
        (12, 1, "793782ac", 0x4f31fdf3b9f46bdd),
        (13, 2, "723e0003", 0x2ae52b5e3a2ec249),
        (14, 1, "3781123d", 0xe198a6309c94957d),
        (15, 3, "223097a1", 0x63965d4c11891d09),
        (16, 10, "ebee9072", 0x7b98dcd81ebe5abe),
        (17, 1, "600edf06", 0x1c44c188c3127eef),
        (18, 3, "b3659ca2", 0x0da68650ff55d9e3),
        (19, 0, "c95ebacf", 0x240fc85458b4a61d),
        (20, 4, "8e62ed6e", 0x6e6ff842fb9aed7e),
        (21, 4, "14df4002", 0xbb88be34e1f0c9ea),
        (22, 0, "8f426622", 0xcc959dcf0f19ce30),
        (23, 0, "0a62722b", 0xd76a850ad89baa59),
        (24, 0, "4c60b84e", 0x221694494c2e4ff2),
        (25, 5, "57658851", 0x5cc1443457aab6c5),
        (26, 0, "f9d4d8a0", 0x173ef7c609bee07f),
        (27, 0, "34508bf4", 0x9493c262c06e9ac3),
        (28, 5, "0728b88a", 0x4d368ded60fa7630),
        (29, 6, "2d5ae090", 0x7f977b0359507ba3),
        (30, 4, "282d7871", 0xe7c24dd3c4ac6f0b),
        (31, 1, "b2dd20a1", 0x4e922da76b97ad0f),
        (32, 5, "facc255d", 0x3d35c59c734af4f1),
    ]
}
/// Seeds 1-16, attempts 0 and 1, option on: (seed, attempt, `landmarks_digest`). Seeds 2 and 11 fail their prefix on
/// attempt 1 (1); no attempt here is refused by the stage (2). Re-pinned by P-MAPGEN-SIG-SCALE-4 (2026-11-14): the
/// three-stream volcano's signature step refuses five of them (3: seed 7 attempt 0, 9/1, 12/0, 13/0, 16/0).
// Re-pinned by P-MAPGEN-SIG-HOLY-1 (2026-11-14): the Landmarks phase now places the hall in its reserved Holy site, and
// the site moves every option-on Prefix: all 32 rows moved.
// Re-pinned by P-LAND-FROST-1 (2026-11-14): the cave's soft room is reserved in the Prefix (and given up on a failed
// camp placement): 29 of the 32 rows moved. Re-pinned by P-STORM-CHURCH-MAP-2 (2026-11-19) after the approved rear
// church opening; option-off map pins remain unchanged.
pub fn pinned_landmarks() -> [(i64, u64, u64); 32] {
    [
        (1, 0, 0x3),
        (1, 1, 0xea75e718e61a5975),
        (2, 0, 0x1cd53150f4784cbd),
        (2, 1, 0x3),
        (3, 0, 0x1),
        (3, 1, 0x64633790204496cc),
        (4, 0, 0xc49bc30f4e3bb97b),
        (4, 1, 0x17407afd6775d9ed),
        (5, 0, 0x6637a2766ff02ee1),
        (5, 1, 0x3),
        (6, 0, 0x0d84013e542a1cf2),
        (6, 1, 0x1),
        (7, 0, 0x3),
        (7, 1, 0x252e235aadca852b),
        (8, 0, 0x07a2f911fa2c316f),
        (8, 1, 0x1),
        (9, 0, 0x33741a10b1ae888d),
        (9, 1, 0xb778a226222f7fe8),
        (10, 0, 0x1),
        (10, 1, 0x99bb0c5e5de3e9ec),
        (11, 0, 0x708e35c399c0f386),
        (11, 1, 0x09cbb5e9c1300133),
        (12, 0, 0x3ce8650df26c1b15),
        (12, 1, 0x1efcff2abd9dccce),
        (13, 0, 0x79416113b639ae70),
        (13, 1, 0x1),
        (14, 0, 0x1),
        (14, 1, 0x09534b3a921e0cc6),
        (15, 0, 0x1),
        (15, 1, 0x1),
        (16, 0, 0x3),
        (16, 1, 0x2a4ab39f911bf0af),
    ]
}
