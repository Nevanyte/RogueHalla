use super::*;

fn sim() -> crate::Sim {
    let at = crate::Vec2::new(292., 284.);
    let mut sim = crate::Sim::reset_active(
        crate::SimConfig::default(),
        crate::arenas::practice_arena(),
        crate::Secrets::from_seed(919),
        &[crate::Placement {
            profile: "reward-owner".into(),
            faction: crate::FactionId::Holy,
            bot: false,
            at,
            hp: None,
            weapon0: None,
            weapon1: None,
        }],
    );
    sim.w.quests = Some(crate::quests::live::Quests {
        stream: crate::private_rng::Sfc32::from_words([1, 2, 3, 4]),
        offers: Vec::new(),
        carried: Vec::new(),
        recheck: false,
        board: None,
        rewards: Some(RewardState::default()),
    });
    sim
}

#[test]
fn catalog_rewards_have_the_expected_inventory_split() {
    let mut single = 0;
    let mut reusable = 0;
    let mut permanent = 0;
    for definition in super::super::catalog::DEFINITIONS {
        match definition.reward.kind() {
            super::super::catalog::RewardKind::SingleUse => single += 1,
            super::super::catalog::RewardKind::Reusable => reusable += 1,
            super::super::catalog::RewardKind::Permanent => permanent += 1,
        }
    }
    assert_eq!((single, reusable, permanent), (8, 10, 7));
}

#[test]
fn spell_charges_and_cooldowns_are_independent_per_owned_slot() {
    let slot = inventory::Owned::new(Award::Hook, 1.);
    assert_eq!(slot.charges, 1);
    assert_eq!(slot.ready_at, 1.);
    assert_eq!(slot.award, Award::Hook);
    let ward = inventory::Owned::new(Award::LanternWard, 1.);
    assert_eq!((ward.charges, ward.max_charges), (2, 2));
}

#[test]
fn inventory_round_trips_with_cooldown_and_permanent_reward() {
    let mut owner = inventory::Owner {
        id: crate::PlayerId(7),
        ..Default::default()
    };
    owner.slots[0] = Some(inventory::Owned::new(Award::Hook, 12_000.));
    owner.permanent.push(Award::WarmCoat);
    let encoded = serde_json::to_vec(&owner).unwrap();
    let restored: inventory::Owner = serde_json::from_slice(&encoded).unwrap();
    assert_eq!(restored, owner);
}

#[test]
fn lost_brother_is_two_individually_claimable_draughts() {
    let rewards = awards::bundle(Award::BlindRage);
    assert_eq!(rewards, vec![Award::BlindRage, Award::StillMind]);
}

#[test]
fn inventory_has_two_slots_and_permanent_awards_do_not_occupy_them() {
    let mut sim = sim();
    let id = sim.players()[0].id;
    assert!(inventory::grant(&mut sim.w, id, Award::Hook));
    assert!(inventory::grant(&mut sim.w, id, Award::LanternWard));
    assert!(!inventory::grant(&mut sim.w, id, Award::Bolas));
    assert!(inventory::grant(&mut sim.w, id, Award::WarmCoat));
    assert!(inventory::has(&sim.w, id, Award::WarmCoat));
    assert_eq!(
        inventory::owned_spells(&sim.w, id).map(|s| s.map(|v| v.award)),
        [Some(Award::Hook), Some(Award::LanternWard)]
    );
    assert!(inventory::validate(&sim.w).is_ok());
}

#[test]
fn breakers_helm_is_enabled_only_for_its_owner_and_weapon_types() {
    let mut sim = sim();
    let id = sim.players()[0].id;
    assert_eq!(
        super::armor_bypass_fraction(&sim.w, id, crate::WeaponId::Bow),
        0.
    );
    assert!(inventory::grant(&mut sim.w, id, Award::BreakersHelm));
    assert_eq!(
        super::armor_bypass_fraction(&sim.w, id, crate::WeaponId::Bow),
        0.20
    );
    assert_eq!(
        super::armor_bypass_fraction(&sim.w, id, crate::WeaponId::Sword),
        0.
    );
}
