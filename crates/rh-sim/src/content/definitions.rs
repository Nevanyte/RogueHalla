//! [`content_hash_of_definitions`]: the single fold that every `content_hash_of_*` entry point ends in.

use super::*;

pub(super) fn content_hash_of_definitions(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definitions: MapDefinitions<'_, '_>,
    signature_rules: &[(crate::factions::SignatureKind, bool)],
    stat_fold: &crate::stat_fold::StatFoldDefinition,
    mob_catalog: &crate::mob_catalog::Definition,
    runtime_words: RuntimeWords<'_>,
) -> u64 {
    let MapDefinitions {
        coliseum,
        zones,
        camps,
        attempt,
        spawns,
        bushes,
        landmarks,
        landmark_search,
        recipe_support,
        recipes,
        records,
        terrain_gate,
        style_features,
        style_sanctum,
        style_warren,
        style_scorch,
        style_rime,
        style_gallows,
        style_gallows_dials,
        style_registry,
        pipeline,
        certify,
        style_runtime,
        spots,
        tail,
        repair,
    } = definitions;
    let mut h = Fnv::new();

    h.str("registry");
    h.u64(registry.len() as u64);
    for e in registry {
        h.str(e.name);
        h.val(&e.val);
    }

    fold_mob_catalog(&mut h, mob_catalog);
    h.str("initial-world/1");
    crate::initial_world::visit_content(|word| h.u64(word));

    h.str("status-runtime/1");
    crate::terrain::visit_content(|word| h.u64(word));
    h.str("card-bleed-feedback/1");
    crate::systems::card_bleed_feedback::visit_content(|word| h.u64(word));
    h.str("wish-producers/1");
    crate::abilities::visit_content(|word| h.u64(word));
    crate::systems::arena_rules::visit_content(|word| h.u64(word));
    h.str("arena-collision-vision/1");
    crate::collision::visit_content(|word| h.u64(word));
    fold_status_content(&mut h, runtime_words.status);
    fold_status_stream(
        &mut h,
        "mvp-progression/1",
        runtime_words.progression,
        |word| visit_progression_content(word),
    );

    fold_status_stream(&mut h, "mirror-timings/1", runtime_words.timings, |word| {
        visit_mirror_timing_content(word)
    });

    h.str("stat-fold/1");
    crate::stat_fold::visit_content(stat_fold, |word| h.u64(word));
    // Fold the actual runtime key order/index/category mappings rather than shadow lists.
    let stat_keys = crate::stat_fold::StatKey::source_order();
    h.u64(stat_keys.len() as u64);
    for key in stat_keys {
        h.u64(key as u64);
        h.u64(key.index() as u64);
        h.u64(u64::from(crate::stat_fold::is_product_stat(key)));
        h.u64(u64::from(crate::stat_fold::is_flat_stat(key)));
    }
    let keys = crate::stat_fold::MultKey::source_order();
    h.str("mult-keys");
    h.u64(keys.len() as u64);
    for key in keys {
        h.u64(key as u64);
        h.u64(key.index() as u64);
    }
    let keys = crate::stat_fold::EffectFlag::source_order();
    h.str("effect-flags");
    h.u64(keys.len() as u64);
    for key in keys {
        h.u64(key as u64);
    }
    let keys = crate::stat_fold::ConvertFrom::source_order();
    h.str("convert-from");
    h.u64(keys.len() as u64);
    for key in keys {
        h.u64(key as u64);
    }
    let keys = crate::stat_fold::ConvertTo::source_order();
    h.str("convert-to");
    h.u64(keys.len() as u64);
    for key in keys {
        h.u64(key as u64);
        h.u64(key.stat_key() as u64);
    }
    let keys = crate::stat_fold::ChestBoostStat::source_order();
    h.str("chest-keys");
    h.u64(keys.len() as u64);
    for key in keys {
        h.u64(key as u64);
        h.u64(key.index() as u64);
    }
    let keys = crate::stat_fold::MirrorField::source_order();
    h.str("mirror-fields");
    h.u64(keys.len() as u64);
    for key in keys {
        h.u64(key as u64);
    }

    h.str("mapgen/coliseum/1");
    crate::mapgen::coliseum::visit_content(coliseum, |word| h.u64(word));

    h.str("mapgen/zones/partition/1");
    crate::mapgen::zones::visit_content(zones, |word| h.u64(word));

    h.str("mapgen/zones/camps/1");
    crate::mapgen::zones::camps::visit_content(camps, |word| h.u64(word));

    h.str("mapgen/attempt-prefix/1");
    crate::mapgen::attempt::visit_content(attempt, |word| h.u64(word));

    h.str("mapgen/spawns/1");
    crate::mapgen::spawns::visit_content(spawns, |word| h.u64(word));

    h.str("mapgen/elite-bushes/1");
    crate::mapgen::bushes::visit_content(bushes, |word| h.u64(word));

    h.str("mapgen/landmark-runtime/1");
    crate::mapgen::landmarks::runtime::visit_content(landmarks, |word| h.u64(word));

    h.str("mapgen/landmark-search/1");
    crate::mapgen::landmarks::search::visit_content(landmark_search, |word| h.u64(word));

    h.str("mapgen/landmark-recipe-support/1");
    crate::mapgen::landmarks::recipe_support::visit_content(recipe_support, |word| h.u64(word));
    let directions = crate::mapgen::landmarks::recipe_support::DIR16;
    h.u64(directions.len() as u64);
    for direction in directions {
        for coordinate in direction {
            h.u64(coordinate as u64);
        }
    }

    h.str("mapgen/landmark-recipes/1");
    crate::mapgen::landmarks::recipes::visit_content(&recipes, |word| h.u64(word));

    h.str("mapgen/records/1");
    crate::mapgen::records::visit_content(records, |word| h.u64(word));

    h.str("mapgen/terrain-gate/1");
    crate::mapgen::terrain_gate::visit_content(terrain_gate, |word| h.u64(word));

    h.str("mapgen/plain-pipeline/1");
    crate::mapgen::pipeline::visit_content(pipeline, |word| h.u64(word));

    h.str("mapgen/hard-certifier/1");
    crate::mapgen::certify::rules::visit_definition(certify, |word| h.u64(word));
    h.str("mapgen/style-features/1");
    crate::mapgen::style_features::visit_content(style_features, |word| h.u64(word));
    h.u64(crate::mapgen::style_features::DIR8.len() as u64);
    for [dx, dy] in crate::mapgen::style_features::DIR8 {
        h.u64(dx as u64);
        h.u64(dy as u64);
    }
    h.str("mapgen/style-sanctum/1");
    crate::mapgen::style_sanctum::visit_content(style_sanctum, |word| h.u64(word));
    h.str("mapgen/style-warren/1");
    crate::mapgen::style_warren::visit_content(&style_warren, |word| h.u64(word));
    h.str("mapgen/style-scorch/1");
    crate::mapgen::style_scorch::visit_content(&style_scorch, |word| h.u64(word));
    h.str("mapgen/style-rime/1");
    crate::mapgen::style_rime::visit_content(&style_rime, |word| h.u64(word));
    h.str("mapgen/style-gallows/1");
    crate::mapgen::style_gallows::visit_content(&style_gallows, &style_gallows_dials, |word| {
        h.u64(word)
    });

    h.str("mapgen/style-zones/1");
    crate::mapgen::style_zones::visit_entries(style_registry, |word| h.u64(word));

    h.str("mapgen/style-runtime/1");
    crate::mapgen::style_runtime::visit_content(style_runtime, |word| h.u64(word));
    h.u64(crate::mapgen::style_runtime::MAX_CELLS as u64);
    h.u64(crate::mapgen::style_runtime::DIAMOND2.len() as u64);
    for (dx, dy) in crate::mapgen::style_runtime::DIAMOND2 {
        h.u64(dx as u64);
        h.u64(dy as u64);
    }

    h.str("mapgen/spots/1");
    crate::mapgen::spots::visit_content(spots, |word| h.u64(word));

    h.str("mapgen/tail/1");
    crate::mapgen::tail::visit_content(tail, |word| h.u64(word));

    h.str("mapgen/repair/1");
    crate::mapgen::repair::visit_content(&repair, |word| h.u64(word));

    h.str("signatures/1");
    h.u64(signature_rules.len() as u64);
    for &(kind, is_dot) in signature_rules {
        h.str(kind.as_str());
        h.u64(u64::from(is_dot));
    }

    h.str("factions");
    for f in FactionId::ALL {
        h.str(f.as_str());
        let s = faction(f);
        for v in [s.hp, s.rate, s.hit, s.phys, s.true_share, s.armor, s.shield] {
            h.f64(v);
        }
    }

    h.str("weapons");
    for w in WeaponId::ALL {
        h.str(w.as_str());
        let s = weapon(w);
        h.f64(s.boost);
        h.f64(s.floor);
        h.f64(s.dps_mult);
        match s.shape {
            Shape::Arc { reach, half_arc } => {
                h.byte(1);
                h.f64(reach);
                h.f64(half_arc);
            }
            Shape::Projectile { speed, range } => {
                h.byte(2);
                h.f64(speed);
                h.f64(range);
            }
        }
    }

    h.str("stagger");
    for w in WeaponId::ALL {
        h.byte(stagger_weapons.contains(&w) as u8);
    }

    // Probes: the content formulas are code, so their results are folded for fixed inputs. A formula edit that moves any
    // outcome moves the identity even when no registered number changed.
    h.str("probes");
    for f in FactionId::ALL {
        let spec = paced(f);
        for w in WeaponId::ALL {
            for as_mult in [1.0, 1.35] {
                let interval = attack_interval(&spec, w, as_mult);
                h.f64(interval);
                h.f64(damage_per_hit(&spec, w, as_mult));
                let t = player_timings(w, interval);
                h.f64(t.windup);
                h.f64(t.root);
                h.byte(t.ranged as u8);
            }
        }
        let shares = damage_shares(&spec);
        h.f64(mitigate_shares(100.0, &shares, spec.armor, spec.shield));
    }
    for p in [0.0, 1.0, 6.0, 25.0] {
        h.f64(pvp_factor(p));
    }
    for k in 0..10 {
        h.u64(bounty_for(k) as u64);
    }
    // Domain revision: player Frost sources now affect every supported fighter recipient. NPC sources remain gated.
    h.str("frost/player-sources-all-recipients/1");
    h.str("frost/formulas/1");
    for multiplier in [1.0, 1.0 - FROST_SLOW] {
        for remaining in [-1.0, 0.0, 1.0, 33.999, 34.0, 50.0, 75.0] {
            h.f64(crate::systems::frost::stretch_until(
                1000.0 + remaining,
                1000.0,
                TICK_MS,
                multiplier,
            ));
        }
    }
    for exposure in [0.0, 999.0, 1000.0, 1001.0, 2500.0, 3999.0, 4000.0, 4001.0] {
        h.f64(crate::systems::frost::move_multiplier(exposure));
    }
    // The original probes above do not exercise victim resistance or its order before movement quantization.
    // Probe the helpers used by the runtime, including neutral, partial/full resistance and overlapping slows.
    h.str("frost/resistance/1");
    for resistance in [0.0, 0.4, 1.0] {
        for chill in [
            1.0,
            1.0 - FROST_SLOW,
            1.0 - FROST_SLOW - crate::modifiers::DEFINITION.frost_cap,
        ] {
            h.f64(crate::systems::frost::resisted_attack_chill(
                chill, resistance,
            ));
        }
        for exposure in [0.0, 1000.0, 1001.0, 2500.0, 3999.0, 4000.0] {
            let near = crate::systems::frost::move_multiplier(exposure);
            for ranged in [1.0, FROST_RANGED_CHILL_MULT] {
                h.f64(crate::systems::frost::resisted_move_mult(
                    near.min(ranged),
                    resistance,
                ));
            }
        }
    }
    h.str("sentenced/mark-rate/1");
    h.f64(crate::modifiers::maximum_mark_rate());
    for extra in [-1.0, 0.0, 0.5] {
        let mut sums = crate::stat_fold::zero_sums();
        sums.mark_rate = extra;
        let rate = crate::modifiers::mark_rate_of(&sums);
        h.f64(rate);
        for chill in [
            0.0,
            MARK_CHILL_FLOOR,
            1.0 - FROST_SLOW - crate::modifiers::DEFINITION.frost_cap,
            1.0 - FROST_SLOW,
            1.0,
        ] {
            h.f64(crate::modifiers::mark_progress_increment(rate, chill));
        }
    }
    // Actual Overkill arithmetic and strict threshold, shared with the live ledger producer.
    h.str("overkill/producer/1");
    for (total, absorbed, applied) in [(10.0, 2.0, 7.0), (10.0, 2.0, 8.0), (1e16, 1e16, 1.0)] {
        h.f64(crate::systems::ledger::overkill_excess(
            total, absorbed, applied,
        ));
    }
    for excess in [
        -OVERKILL_EPS,
        0.0,
        f64::from_bits(OVERKILL_EPS.to_bits() - 1),
        OVERKILL_EPS,
        f64::from_bits(OVERKILL_EPS.to_bits() + 1),
        2.0 * OVERKILL_EPS,
    ] {
        h.u64(u64::from(crate::systems::ledger::overkill_fires(excess)));
    }
    // Source birth and held ticks both refuse; Choose and observation use this exact runtime predicate.
    h.str("reward-chests/birth-availability/2");
    for (held, tick) in [(0, 0), (0, 1), (2, 1), (2, 2), (2, 3), (u32::MAX, u32::MAX)] {
        h.u64(u64::from(crate::reward_chests::birth_available(held, tick)));
    }
    h.str("sentenced/base-pulse/1");
    for weapon in WeaponId::ALL {
        for sentence in [0, 1, 5] {
            for target_max_hp in [1.0, 1000.0, 1234.5] {
                for near in [false, true] {
                    h.f64(sentenced_pulse(weapon, target_max_hp, sentence, near));
                }
            }
        }
    }
    h.0
}
