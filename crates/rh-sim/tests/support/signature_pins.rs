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
/// every option-on Prefix, so all 32 maps; 96 attempts, worst 11 on seed 11).
pub fn pinned_on_maps() -> [(i64, u64, &'static str, u64); 32] {
    [
        (1, 1, "08088379", 0xcebd_0dbc_b8d6_e035),
        (2, 0, "383b9f49", 0x4830_6438_3e48_02d6),
        (3, 1, "b9641ce7", 0x30be_adb8_a94a_752c),
        (4, 0, "1a0a7e7c", 0x31f5_afc2_23fd_b815),
        (5, 0, "d08f93c3", 0xadc0_d03b_e846_01c8),
        (6, 0, "24f99cce", 0x1f50_d4b9_85d9_bcf8),
        (7, 3, "3f62aa66", 0x160c_f1be_2adc_70c3),
        (8, 2, "05e755dc", 0x69af_b7e3_9abd_477e),
        (9, 3, "5e5ce75c", 0x5fcf_4f20_b20f_5793),
        (10, 0, "d59e527c", 0x7388_e39a_79da_1778),
        (11, 11, "eb708254", 0x888b_0e2a_cb5b_ed85),
        (12, 1, "dfe53bb1", 0x4c0d_8b97_31b3_b6f6),
        (13, 1, "40a5c833", 0xb5ed_e486_ae09_4240),
        (14, 0, "2d7fda05", 0x05a1_82ce_98fb_e21b),
        (15, 0, "31893913", 0x9a70_2209_2a34_16d8),
        (16, 8, "46d1fee3", 0xbb58_8d46_1a55_1e1d),
        (17, 2, "ddc73a04", 0x915e_9d14_6e5c_3c13),
        (18, 5, "f72a3552", 0x550a_a068_a536_393e),
        (19, 0, "b7987630", 0x6233_9ad1_80e4_9ed7),
        (20, 0, "52054ece", 0x8c46_4f8a_0378_ec5e),
        (21, 1, "f5cd4327", 0xfd82_54a1_7941_3173),
        (22, 5, "18f249e5", 0x60cc_a4e5_a0f1_d815),
        (23, 0, "91d00b5e", 0x64e3_8554_bc3d_9765),
        (24, 1, "1c630a1e", 0x6f29_fabb_dddd_f18a),
        (25, 1, "5eac8ffd", 0xcc17_8f1b_d4c2_9de6),
        (26, 5, "1fc5638f", 0x947e_f2ff_d093_1a2d),
        (27, 0, "b86facc2", 0x8dad_d3c4_3b35_9946),
        (28, 2, "d677f839", 0xd8a6_3015_cdc3_1232),
        (29, 3, "3008d740", 0x865f_68cf_e6d8_28a1),
        (30, 7, "4fb96e3a", 0x046e_bc76_a361_65ea),
        (31, 1, "e1f96993", 0xd7e3_a1f8_8508_6a77),
        (32, 0, "d97d7c92", 0x5dbf_ebab_e13e_cedb),
    ]
}

/// Seeds 1-16, attempts 0 and 1, option on: (seed, attempt, `landmarks_digest`). Seeds 2 and 11 fail their prefix on
/// attempt 1 (1); no attempt here is refused by the stage (2). Re-pinned by P-MAPGEN-SIG-SCALE-4 (2026-11-14): the
/// three-stream volcano's signature step refuses five of them (3: seed 7 attempt 0, 9/1, 12/0, 13/0, 16/0).
// Re-pinned by P-MAPGEN-SIG-HOLY-1 (2026-11-14): the Landmarks phase now places the hall in its reserved Holy site, and
// the site moves every option-on Prefix: all 32 rows moved.
pub fn pinned_landmarks() -> [(i64, u64, u64); 32] {
    [
        (1, 0, 0x3ca9_eadd_0916_68b9),
        (1, 1, 0x1432_44fa_487e_6a9e),
        (2, 0, 0xc068_8b9c_398f_de16),
        (2, 1, 0x0000_0000_0000_0001),
        (3, 0, 0x0000_0000_0000_0001),
        (3, 1, 0xf475_048e_dcc2_01c4),
        (4, 0, 0x036d_079b_7d98_653b),
        (4, 1, 0x6015_b015_7c0e_37a8),
        (5, 0, 0x35fa_bb5f_5135_3ca2),
        (5, 1, 0x0000_0000_0000_0001),
        (6, 0, 0x727a_a3b2_65c7_2c1c),
        (6, 1, 0x502b_941c_13df_3a47),
        (7, 0, 0x0000_0000_0000_0003),
        (7, 1, 0xc619_32ab_6274_36f8),
        (8, 0, 0xee52_51b0_913e_b3b9),
        (8, 1, 0x6241_8db5_29fe_ec79),
        (9, 0, 0xcae9_4613_e5ef_efc7),
        (9, 1, 0x0000_0000_0000_0003),
        (10, 0, 0x74e4_02f1_6901_766f),
        (10, 1, 0x7b32_91a6_592b_6974),
        (11, 0, 0x0000_0000_0000_0001),
        (11, 1, 0x0000_0000_0000_0001),
        (12, 0, 0x0000_0000_0000_0003),
        (12, 1, 0x920a_434a_20da_c7ea),
        (13, 0, 0x0000_0000_0000_0003),
        (13, 1, 0x8abc_a0d4_9224_8a6d),
        (14, 0, 0x4ce7_1c61_f74f_da2d),
        (14, 1, 0x48f6_4f68_5509_47ef),
        (15, 0, 0x5fec_176e_b8b0_f556),
        (15, 1, 0x1cc9_6cae_bf08_ba3d),
        (16, 0, 0x0000_0000_0000_0003),
        (16, 1, 0x4b9e_1dc5_a5b9_60e3),
    ]
}
