//! Two full quest slots, duplicate choices, dropped stocks and reconnect.
use super::*;
use rewards::{Award, Owned};

fn full_inventory() -> Raid {
    let layout = coliseum::fixed_layout();
    let generator = pipeline::Pipeline::new(&layout);
    let sim = (1..64)
        .map(|n| new_raid(&generator, Secrets::from_seed(n)))
        .find(|s| {
            s.w.quests
                .as_ref()
                .unwrap()
                .offers
                .iter()
                .any(|o| kind(o) == Some(StayAWhile))
        })
        .expect("a board containing Stay A While");
    let mut raid = Raid::new(sim, StayAWhile);
    raid.accept();
    let owner = raid.seat;
    let state = raid
        .sim
        .w
        .quests
        .as_mut()
        .unwrap()
        .rewards
        .as_mut()
        .unwrap();
    state.owners.push(Default::default());
    let equipment = state.owners.last_mut().unwrap();
    equipment.id = owner;
    let mut ward = Owned::new(Award::LanternWard, 0.);
    ward.charges = 1;
    ward.ready_at = 90_000.;
    equipment.slots = [Some(ward), Some(Owned::new(Award::Hook, 0.))];
    raid.action(6, 0);
    raid.legacy_steps(601);
    assert_eq!(raid.view().pending.len(), 1);
    raid
}

#[test]
fn pending_choice_restores_and_the_dropped_spell_keeps_its_cooldown_and_charge() {
    let mut raid = full_inventory();
    let bytes = serde_json::to_vec(&raid.sim).unwrap();
    raid.sim = Sim::restore(&mut serde_json::Deserializer::from_slice(&bytes)).unwrap();
    raid.observer = QuestObserver::new(raid.seat);
    let (handle, view) = raid.entry();
    assert_eq!(view.pending, [handle]);
    // A submitted duplicate must be harmless even before a new observer publication.
    let cmd = raid
        .observer
        .resolve_interact(&raid.sim, handle, 8, 0)
        .unwrap();
    raid.sim.submit(raid.seat, cmd).unwrap();
    raid.sim.submit(raid.seat, cmd).unwrap();
    raid.sim.step();
    let completed = raid.view();
    assert!(completed.pending.is_empty());
    assert!(!completed
        .rows
        .iter()
        .any(|r| r.def == StayAWhile.definition().id));
    assert_eq!(completed.spells[0].reward, Award::Stasis as u8);
    assert_eq!(completed.drops.len(), 1);
    let drop = completed.drops[0];
    assert_eq!(
        (drop.reward, drop.charges, drop.ready_at),
        (Award::LanternWard as u8, 1, 90_000.)
    );

    // Only the other observer gets to resolve the nearby drop into that player's empty slot.
    raid.arrange(raid.opponent, drop.at);
    let mut other = QuestObserver::new(raid.opponent);
    let prepared = other.prepare(&raid.sim).unwrap();
    let pickup = prepared
        .view
        .actions
        .iter()
        .find(|a| a.action == 9)
        .expect("a reachable droppable spell")
        .clone();
    assert!(prepared
        .view
        .rows
        .iter()
        .any(|r| r.def == StayAWhile.definition().id && !r.owner_accepted));
    prepared.commit();
    let cmd = other
        .resolve_interact(&raid.sim, pickup.quest, 9, pickup.target)
        .unwrap();
    raid.sim.submit(raid.opponent, cmd).unwrap();
    raid.sim.step();
    let view = other.prepare(&raid.sim).unwrap();
    assert!(view.view.drops.is_empty());
    assert_eq!(view.view.spells.len(), 1);
    let spell = view.view.spells[0];
    assert_eq!(
        (spell.reward, spell.charges, spell.ready_at),
        (drop.reward, drop.charges, drop.ready_at)
    );
}

#[test]
fn declining_leaves_one_new_spell_and_preserves_both_equipped_spells() {
    let mut raid = full_inventory();
    let before = raid.view().spells;
    raid.action(8, 2);
    let view = raid.view();
    assert_eq!(view.spells, before);
    assert_eq!(view.drops.len(), 1);
    assert_eq!(view.drops[0].reward, Award::Stasis as u8);
    assert!(view.pending.is_empty());
}
