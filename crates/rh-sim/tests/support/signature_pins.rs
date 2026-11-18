// The signature landmarks' digest-neutrality pins, in one place (P-MAPGEN-SIG-BLUEPRINT-1). A packet that is meant to
// change no generated map proves it against these; a packet that is meant to move a landmark re-pins them here, once,
// with the values `mapgen_signature_blueprint.rs` `print_signature_pins` prints (run it with `--ignored --nocapture`),
// and says so in its brief. Pinned on the stub commit of P-MAPGEN-SIG-BLUEPRINT-1 (the generator of
// feature/unity-rework @ 9890f291b, which holds the blueprint church and the Plague pools), before the generic stamp.
// The option-on pins (`pinned_on_maps`, `pinned_landmarks`) were last re-taken by P-MAPGEN-SIG-SCALE-4 on
// agent/opus/P-MAPGEN-SIG-SCALE-4 (the 22 x 22 volcano with three streams; its shas changed with later rebases); `pinned_off` is unchanged.
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
/// Frost lake built first on the cave's softly reserved room; all 32 maps; 97 attempts, worst 10 on seed 16).
pub fn pinned_on_maps() -> [(i64, u64, &'static str, u64); 32] {
    [
        (1, 1, "820db995", 0x8afc_56a7_1f15_6633),
        (2, 2, "28fd17d6", 0x4ce8_5a89_d31a_479f),
        (3, 1, "de070922", 0x21e0_17a3_7011_9c88),
        (4, 1, "d2b54130", 0x5a06_ad57_dc7a_f22b),
        (5, 0, "ba9ee3c8", 0xee46_d4ee_cfdd_b28e),
        (6, 0, "99f48770", 0x6150_e68c_37c9_0920),
        (7, 1, "af64e9d2", 0xd683_2df5_1993_1ec6),
        (8, 2, "0be488ff", 0xcb07_864d_2774_0095),
        (9, 0, "ba036b68", 0x6494_ea37_d1ff_a4b6),
        (10, 2, "329bbece", 0xcaa5_bd85_ee02_a42e),
        (11, 0, "c89e36bc", 0xb254_f886_0387_4f0a),
        (12, 1, "a67f2d0b", 0x8a63_5d78_a52d_837b),
        (13, 2, "78de99e0", 0x4e77_7b05_4178_beb7),
        (14, 1, "a4167744", 0xbf30_ebe3_1948_2ffd),
        (15, 3, "f64ced4b", 0x6d30_67ee_f494_f28b),
        (16, 10, "231f16c0", 0x4116_f2ab_e412_008c),
        (17, 1, "a941a9e7", 0xcbcd_6a92_777c_dda1),
        (18, 3, "559c6a84", 0xb653_049c_5af9_0694),
        (19, 0, "e3010cca", 0xd3c5_8ce2_a346_483e),
        (20, 4, "2ff009cc", 0x82ce_88c9_e18c_a713),
        (21, 4, "bc9d85b9", 0xf8d7_b02f_f8e5_8195),
        (22, 0, "c34bbb8b", 0xd87f_7ad1_70e2_bd5c),
        (23, 0, "0b6d0f9e", 0xc6d6_8cd5_407e_0dab),
        (24, 0, "08c339cd", 0x92c9_cea1_5b69_b413),
        (25, 5, "dadc85df", 0xc479_a41f_b156_7c1f),
        (26, 0, "cb5fc4b5", 0xadac_c7fc_7afc_75b5),
        (27, 0, "ce19cbd5", 0x47fa_595c_c1a6_718a),
        (28, 5, "3c32091d", 0x2912_1aa7_c466_5a69),
        (29, 6, "959486b1", 0x55cf_a642_1321_b560),
        (30, 4, "89dd400c", 0x0e41_ec18_d07f_b05a),
        (31, 1, "fcb85307", 0xd3cc_cc3e_3354_9519),
        (32, 5, "c6b085db", 0x7102_3f39_0d6b_6355),
    ]
}

/// Seeds 1-16, attempts 0 and 1, option on: (seed, attempt, `landmarks_digest`). Seeds 2 and 11 fail their prefix on
/// attempt 1 (1); no attempt here is refused by the stage (2). Re-pinned by P-MAPGEN-SIG-SCALE-4 (2026-11-14): the
/// three-stream volcano's signature step refuses five of them (3: seed 7 attempt 0, 9/1, 12/0, 13/0, 16/0).
// Re-pinned by P-MAPGEN-SIG-HOLY-1 (2026-11-14): the Landmarks phase now places the hall in its reserved Holy site, and
// the site moves every option-on Prefix: all 32 rows moved.
// Re-pinned by P-LAND-FROST-1 (2026-11-14): the cave's soft room is reserved in the Prefix (and given up on a failed
// camp placement): 29 of the 32 rows moved.
pub fn pinned_landmarks() -> [(i64, u64, u64); 32] {
    [
        (1, 0, 0x0000_0000_0000_0003),
        (1, 1, 0x8cc8_1f1a_24b2_f993),
        (2, 0, 0xbbf9_6371_73b3_f929),
        (2, 1, 0x0000_0000_0000_0003),
        (3, 0, 0x0000_0000_0000_0001),
        (3, 1, 0x158b_a2f2_60a4_58da),
        (4, 0, 0x54e7_a1ea_8ccb_004c),
        (4, 1, 0x49c8_49af_478d_57ec),
        (5, 0, 0xb6a3_b15c_f956_8a77),
        (5, 1, 0x0000_0000_0000_0003),
        (6, 0, 0x3705_b7ed_a478_59a4),
        (6, 1, 0x0000_0000_0000_0001),
        (7, 0, 0x0000_0000_0000_0003),
        (7, 1, 0x99da_9471_3773_d092),
        (8, 0, 0x3bd0_8034_ced2_cd80),
        (8, 1, 0x0000_0000_0000_0001),
        (9, 0, 0xf77b_6cac_0e2a_838f),
        (9, 1, 0xeb50_b237_5d8d_5521),
        (10, 0, 0x0000_0000_0000_0001),
        (10, 1, 0x8e4e_5bee_e610_8dec),
        (11, 0, 0xf009_0239_e4bf_9fe8),
        (11, 1, 0x07e6_8fa6_7954_ce1b),
        (12, 0, 0x19e3_8c75_dfe4_e0da),
        (12, 1, 0x4b3b_2a6d_088d_edda),
        (13, 0, 0x098b_c367_fa96_747f),
        (13, 1, 0x0000_0000_0000_0001),
        (14, 0, 0x0000_0000_0000_0001),
        (14, 1, 0x712f_23d3_9a27_11ad),
        (15, 0, 0x0000_0000_0000_0001),
        (15, 1, 0x0000_0000_0000_0001),
        (16, 0, 0x0000_0000_0000_0003),
        (16, 1, 0xbd8c_f8d6_fe49_5797),
    ]
}
