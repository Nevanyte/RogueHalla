//! The public entry points of the rules identity: [`content_hash`] and the `content_hash_of_*` perturbation
//! variants that `tests/content.rs` uses to move one definition at a time.

use super::*;

/// the rule flags and probes of the content formulas. Sent in the protocol's Welcome and recorded in replays.
pub fn content_hash() -> u64 {
    content_hash_of(REGISTRY)
}

/// [`content_hash`] with the registry rows given by the caller (the tables are always the build's own). Public so that
/// `tests/content.rs` can perturb any single row and require the hash to move.
pub fn content_hash_of(registry: &[Entry]) -> u64 {
    content_hash_of_rules(registry, STAGGER_WEAPONS)
}

/// [`content_hash_of`] with the stagger rule flag table given as well (for the perturbation tests).
pub fn content_hash_of_rules(registry: &[Entry], stagger_weapons: &[WeaponId]) -> u64 {
    content_hash_of_map_definition(
        registry,
        stagger_weapons,
        &crate::mapgen::coliseum::default_definition(),
    )
}

/// Stat-fold tuning shares its actual definition with the aggregate rules identity.
pub fn content_hash_of_stat_fold_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::stat_fold::StatFoldDefinition,
) -> u64 {
    let signatures = crate::factions::SignatureKind::ALL.map(|kind| (kind, kind.is_dot()));
    content_hash_of_definitions(
        registry,
        stagger_weapons,
        MapDefinitions::default(),
        &signatures,
        definition,
        &crate::mob_catalog::DEFINITION,
        RuntimeWords::default(),
    )
}

/// The roster helpers and aggregate identity consume the same catalog definition.
pub fn content_hash_of_mob_catalog_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mob_catalog::Definition,
) -> u64 {
    let signatures = crate::factions::SignatureKind::ALL.map(|kind| (kind, kind.is_dot()));
    content_hash_of_definitions(
        registry,
        stagger_weapons,
        MapDefinitions::default(),
        &signatures,
        &crate::stat_fold::default_definition(),
        definition,
        RuntimeWords::default(),
    )
}

/// The map foundation's fixed geometry is content too. The same definition drives generation;
/// this entry point lets the identity tests perturb it without constructing a map.
pub fn content_hash_of_map_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::coliseum::FixedDefinition<'_>,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            coliseum: definition,
            ..Default::default()
        },
    )
}

/// The exact zone-partition definition is shared by generation and this identity fold.
pub fn content_hash_of_zone_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::zones::ZoneDefinition,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            zones: definition,
            ..Default::default()
        },
    )
}

/// Camp placement rules share one definition with the initial territory generator.
pub fn content_hash_of_camp_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::zones::camps::CampDefinition,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            camps: definition,
            ..Default::default()
        },
    )
}

/// Attempt-prefix retry/arithmetic rules are shared by the producer and its identity.
pub fn content_hash_of_attempt_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::attempt::AttemptDefinition,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            attempt: definition,
            ..Default::default()
        },
    )
}

/// Spawn selection/protection and gate decisions use the same rules as this fold.
pub fn content_hash_of_spawn_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::spawns::SpawnDefinition,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            spawns: definition,
            ..Default::default()
        },
    )
}

/// Elite-bush rules share the actual placement definition with the content identity.
pub fn content_hash_of_bush_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::bushes::BushDefinition,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            bushes: definition,
            ..Default::default()
        },
    )
}

/// The whole-wall landmark guard shares its actual runtime definition with this fold.
pub fn content_hash_of_landmark_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::landmarks::runtime::LandmarkDefinition,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            landmarks: definition,
            ..Default::default()
        },
    )
}

/// Search defaults drive candidate selection and placement attempts, and share their runtime definition.
pub fn content_hash_of_landmark_search_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::landmarks::search::SearchDefinition,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            landmark_search: definition,
            ..Default::default()
        },
    )
}

/// Shared recipe helpers consume one definition; fixed direction encodings are folded alongside it.
pub fn content_hash_of_landmark_support_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::landmarks::recipe_support::RecipeSupportDefinition,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            recipe_support: definition,
            ..Default::default()
        },
    )
}

/// Actual callback tunables and live template rows/keys/jobs share the same definitions as execution.
pub fn content_hash_of_landmark_recipe_definitions(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::landmarks::recipes::Definitions<'_>,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            recipes: *definition,
            ..Default::default()
        },
    )
}

/// Canonical record ordering and region defaults use this same live definition.
pub fn content_hash_of_records_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::records::Definition<'_>,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            records: definition,
            ..Default::default()
        },
    )
}

/// The actual terrain acceptance tables and protection distances share the execution definitions.
pub fn content_hash_of_terrain_gate_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::terrain_gate::TerrainGateDefinition<'_>,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            terrain_gate: definition,
            ..Default::default()
        },
    )
}

/// The independent hard-certifier thresholds are part of generator content identity.
pub fn content_hash_of_certify_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::certify::rules::Definition,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            certify: definition,
            ..Default::default()
        },
    )
}

/// Stage-six guards, transactions and stream selection share the execution policy.
pub fn content_hash_of_style_runtime_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::style_runtime::Definition,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            style_runtime: definition,
            ..Default::default()
        },
    )
}

/// Shared style feature/search arithmetic uses these exact policies.
pub fn content_hash_of_style_features_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::style_features::Definition,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            style_features: definition,
            ..Default::default()
        },
    )
}
/// Holy shape, surface and decor consume the registered callback definition.
pub fn content_hash_of_style_sanctum_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::style_sanctum::Definition,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            style_sanctum: definition,
            ..Default::default()
        },
    )
}

/// Warren geometry, route repair and decor read the same borrowed definition as this identity fold.
pub fn content_hash_of_style_warren_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::style_warren::Definition<'_>,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            style_warren: *definition,
            ..Default::default()
        },
    )
}

/// Scorch nested crater/river/decor policies participate in the same aggregate identity.
pub fn content_hash_of_style_scorch_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::style_scorch::Definition,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            style_scorch: *definition,
            ..Default::default()
        },
    )
}

/// Frost callback geometry, ICE growth, path repair and decor share this exact identity fold.
pub fn content_hash_of_style_rime_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::style_rime::Definition,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            style_rime: *definition,
            ..Default::default()
        },
    )
}

/// Sentenced row definitions and the actual resolved factory dials are content, including route policy.
pub fn content_hash_of_style_gallows_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::style_gallows::Definition<'_>,
    dials: &crate::mapgen::style_gallows::Dials,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            style_gallows: *definition,
            style_gallows_dials: dials.clone(),
            ..Default::default()
        },
    )
}

/// Actual callback identity/phase metadata drives the registry and aggregate rules identity.
pub fn content_hash_of_style_registry_entries(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    entries: &[crate::mapgen::style_zones::Entry],
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            style_registry: entries,
            ..Default::default()
        },
    )
}

/// Bounded plain/styled orchestration uses these same retry and feature limits.
pub fn content_hash_of_pipeline_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::pipeline::Definition,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            pipeline: definition,
            ..Default::default()
        },
    )
}

/// Camp/crate/ground placement consumes the same borrowed definition visited here.
pub fn content_hash_of_spot_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::spots::Definition,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            spots: definition,
            ..Default::default()
        },
    )
}

/// Final shape admission and loot clearing use these same live policy values.
pub fn content_hash_of_tail_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::tail::Definition,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            tail: definition,
            ..Default::default()
        },
    )
}

/// Safety-net and territory-repair policy uses the same borrowed terrain classification.
pub fn content_hash_of_repair_definition(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definition: &crate::mapgen::repair::Definition<'_>,
) -> u64 {
    content_hash_of_map_definitions(
        registry,
        stagger_weapons,
        MapDefinitions {
            repair: *definition,
            ..Default::default()
        },
    )
}

/// Signature classification affects damage budgets and reporting, so it is content as well.
pub fn content_hash_of_signature_rules(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    signature_rules: &[(crate::factions::SignatureKind, bool)],
) -> u64 {
    content_hash_of_definitions(
        registry,
        stagger_weapons,
        MapDefinitions::default(),
        signature_rules,
        &crate::stat_fold::default_definition(),
        &crate::mob_catalog::DEFINITION,
        RuntimeWords::default(),
    )
}
fn content_hash_of_map_definitions(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    definitions: MapDefinitions<'_, '_>,
) -> u64 {
    let signature_rules = crate::factions::SignatureKind::ALL.map(|kind| (kind, kind.is_dot()));
    content_hash_of_definitions(
        registry,
        stagger_weapons,
        definitions,
        &signature_rules,
        &crate::stat_fold::default_definition(),
        &crate::mob_catalog::DEFINITION,
        RuntimeWords::default(),
    )
}
