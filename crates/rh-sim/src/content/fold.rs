//! Folding of the status, progression and mirror-timing content into the rules identity, and the visitors that
//! enumerate their words.

use super::*;

pub(super) fn fold_mob_catalog(h: &mut Fnv, definition: &crate::mob_catalog::Definition) {
    use crate::mob_catalog::{visit_definition, Metadata};
    h.str("mob-catalog/1");
    h.u64(definition.roles.len() as u64);
    h.u64(definition.types.len() as u64);
    h.u64(definition.regions.len() as u64);
    visit_definition(definition, |entry| match entry {
        Metadata::Role(row) => {
            h.str("role");
            h.str(row.id.as_str());
            h.str(row.label);
            h.f64(row.hp_mult);
            h.f64(row.dmg_mult);
            h.f64(row.move_mult);
            h.u64(row.tags.len() as u64);
            for tag in row.tags {
                h.str(tag.as_str());
            }
            h.str(row.blurb);
        }
        Metadata::Type(row) => {
            h.str("type");
            h.str(row.id.as_str());
            h.str(row.zone.as_str());
            h.u64(u64::from(row.elite));
            h.str(row.name);
            h.str(row.role.as_str());
            h.str(row.weapon.as_str());
        }
        Metadata::Region(row) => {
            h.str("region");
            h.str(row.zone.as_str());
            h.u64(row.names.len() as u64);
            for name in row.names {
                h.str(name);
            }
            h.str(row.weapon.as_str());
        }
        Metadata::Rules(row) => {
            h.str("rules");
            h.u64(row.bag_copies as u64);
            h.f64(row.melee_damage);
            h.f64(row.ranged_damage);
            h.f64(row.elite_damage);
        }
    });
}

/// Perturbs the words emitted by the actual status visitors through the real aggregate path.
pub fn content_hash_of_status_words(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    rule_words: &[u64],
    extra_words: &[u64],
) -> u64 {
    let signatures = crate::factions::SignatureKind::ALL.map(|kind| (kind, kind.is_dot()));
    content_hash_of_definitions(
        registry,
        stagger_weapons,
        MapDefinitions::default(),
        &signatures,
        &crate::stat_fold::default_definition(),
        &crate::mob_catalog::DEFINITION,
        RuntimeWords {
            status: Some((rule_words, extra_words)),
            ..Default::default()
        },
    )
}

// Only output words are injected; no status rules or gameplay operations are mutated.
// The default streams the real visitors twice (length, then words), without collecting.
pub(super) fn fold_status_stream(
    h: &mut Fnv,
    label: &str,
    supplied: Option<&[u64]>,
    visit: fn(&mut dyn FnMut(u64)),
) {
    h.str(label);
    if let Some(words) = supplied {
        h.u64(words.len() as u64);
        for &word in words {
            h.u64(word);
        }
    } else {
        let mut count = 0u64;
        visit(&mut |_| count += 1);
        h.u64(count);
        visit(&mut |word| h.u64(word));
    }
}
pub(super) fn fold_status_content(h: &mut Fnv, supplied: Option<(&[u64], &[u64])>) {
    fold_status_stream(h, "status-rules/1", supplied.map(|words| words.0), |word| {
        crate::status_runtime::visit_content(word)
    });
    fold_status_stream(h, "status-extra/1", supplied.map(|words| words.1), |word| {
        crate::status_runtime::visit_extra_content(word)
    });
}

#[derive(Default)]
pub(super) struct RuntimeWords<'a> {
    pub(super) status: Option<(&'a [u64], &'a [u64])>,
    pub(super) progression: Option<&'a [u64]>,
    pub(super) timings: Option<&'a [u64]>,
}

/// Ordered words from the actual selected-card, modifier, farm, reward and private-stream definitions.
/// Component names/lengths and actual public key mappings prevent membership/order ambiguity.
pub fn visit_progression_content(mut word: impl FnMut(u64)) {
    type Visit = fn(&mut dyn FnMut(u64));
    let visitors: [(&str, Visit); 10] = [
        ("cards/1", |f| {
            crate::cards::visit_content(&crate::cards::DEFINITION, f)
        }),
        ("modifiers/1", |f| {
            crate::modifiers::visit_content(&crate::modifiers::DEFINITION, f)
        }),
        ("farm/1", |f| {
            crate::modifiers::farm::visit_content(&crate::modifiers::farm::DEFINITION, f)
        }),
        ("reward-chests/1", |f| {
            crate::reward_chests::visit_content(&crate::reward_chests::DEFINITION, f)
        }),
        ("private-sfc32/1", |f| crate::private_rng::visit_content(f)),
        ("small-potions/1", |f| crate::potions::visit_content(f)),
        ("kill-augments/1", |f| {
            crate::kill_augments::visit_content(&crate::kill_augments::DEFINITION, f)
        }),
        ("quests/1", |f| crate::quests::catalog::visit_content(f)),
        ("traders/1", |f| crate::traders::visit_content(f)),
        ("hitbox-archetypes/1", |f| {
            crate::archetype::visit_content(f)
        }),
    ];
    for (tag, visit) in visitors {
        word(tag.len() as u64);
        for b in tag.bytes() {
            word(u64::from(b));
        }
        let mut n = 0;
        visit(&mut |_| n += 1);
        word(n);
        visit(&mut word);
    }
    word(FactionId::ALL.len() as u64);
    for faction in FactionId::ALL {
        word(u64::from(faction.code()));
        word(u64::from(crate::systems::mobs::basic_faction(faction)));
    }
    word(crate::cards::CardId::ALL.len() as u64);
    for id in crate::cards::CardId::ALL {
        word(id.index() as u64);
        word(id.as_str().len() as u64);
        for b in id.as_str().bytes() {
            word(u64::from(b));
        }
    }
    word(crate::reward_chests::BoostId::ALL.len() as u64);
    for id in crate::reward_chests::BoostId::ALL {
        word(id.index() as u64);
        word(id.as_str().len() as u64);
        for b in id.as_str().bytes() {
            word(u64::from(b));
        }
    }
}

/// Ordered probes of the real mirrored timing calculation, including both fighter profiles.
/// The schema/key tables identify every input/output word; all rows use actual paced faction
/// content and mirror_timings. Probe inputs describe identity coverage, not gameplay tuning.
/// The visitor uses only fixed stack arrays and streams directly to its caller.
pub fn visit_mirror_timing_content(mut word: impl FnMut(u64)) {
    fn text(word: &mut impl FnMut(u64), value: &str) {
        word(value.len() as u64);
        for byte in value.bytes() {
            word(u64::from(byte));
        }
    }
    let profiles = [("player", false), ("npc", true)];
    let inputs: [(f64, f64); 4] = [(1., 1.), (1.5, 0.5), (0.5, 1.5), (1., 1.5)];
    text(&mut word, "mirror-timing-probes/1");
    text(&mut word, "factions");
    word(FactionId::ALL.len() as u64);
    for faction in FactionId::ALL {
        word(u64::from(faction.code()));
        text(&mut word, faction.as_str());
    }
    text(&mut word, "weapons");
    word(WeaponId::ALL.len() as u64);
    for weapon in WeaponId::ALL {
        word(u64::from(weapon.code()));
        text(&mut word, weapon.as_str());
    }
    text(&mut word, "profiles");
    word(profiles.len() as u64);
    for (name, npc) in profiles {
        word(u64::from(npc));
        text(&mut word, name);
    }
    text(&mut word, "inputs");
    word(inputs.len() as u64);
    for (as_mult, windup_mult) in inputs {
        word(as_mult.to_bits());
        word(windup_mult.to_bits());
    }
    let fields = [
        "profile",
        "faction",
        "weapon",
        "asMult",
        "windupMult",
        "interval",
        "windup",
        "root",
        "ranged",
    ];
    text(&mut word, "row-keys");
    word(fields.len() as u64);
    for field in fields {
        text(&mut word, field);
    }
    text(&mut word, "rows");
    word((FactionId::ALL.len() * WeaponId::ALL.len() * profiles.len() * inputs.len()) as u64);
    for faction in FactionId::ALL {
        let spec = paced(faction);
        for weapon in WeaponId::ALL {
            for (_, npc) in profiles {
                for (as_mult, windup_mult) in inputs {
                    let timing = mirror_timings(&spec, weapon, as_mult, windup_mult, npc);
                    for value in [
                        u64::from(npc),
                        u64::from(faction.code()),
                        u64::from(weapon.code()),
                        as_mult.to_bits(),
                        windup_mult.to_bits(),
                        timing.interval.to_bits(),
                        timing.windup.to_bits(),
                        timing.root.to_bits(),
                        u64::from(timing.ranged),
                    ] {
                        word(value);
                    }
                }
            }
        }
    }
}

/// Sensitivity seam for the actual aggregate identity; supplied timing words replace only
/// this visitor's output stream, never the gameplay function, registry or other components.
pub fn content_hash_of_mirror_timing_words(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    words: &[u64],
) -> u64 {
    let signatures = crate::factions::SignatureKind::ALL.map(|kind| (kind, kind.is_dot()));
    content_hash_of_definitions(
        registry,
        stagger_weapons,
        MapDefinitions::default(),
        &signatures,
        &crate::stat_fold::default_definition(),
        &crate::mob_catalog::DEFINITION,
        RuntimeWords {
            timings: Some(words),
            ..Default::default()
        },
    )
}

/// Sensitivity seam for the real aggregate fold; only output words are supplied, no gameplay state changes.
pub fn content_hash_of_progression_words(
    registry: &[Entry],
    stagger_weapons: &[WeaponId],
    words: &[u64],
) -> u64 {
    let signatures = crate::factions::SignatureKind::ALL.map(|kind| (kind, kind.is_dot()));
    content_hash_of_definitions(
        registry,
        stagger_weapons,
        MapDefinitions::default(),
        &signatures,
        &crate::stat_fold::default_definition(),
        &crate::mob_catalog::DEFINITION,
        RuntimeWords {
            progression: Some(words),
            ..Default::default()
        },
    )
}
