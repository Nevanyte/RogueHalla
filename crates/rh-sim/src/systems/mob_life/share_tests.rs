//! P-ELITE-SHARE-1 oracle: under `EliteRule::Rework` an elite kill pays every standing player in the camp's contributor
//! snapshot (the credited killer included) `REWORK_ELITE_SHARE_STACKS` stat stacks of the stat the camp's normal kills pay,
//! through the ordinary farm path. Under `Legacy` an elite kill pays no stacks (`mobLife.ts` pays stacks to normal mobs only).
//! The oracle drives `pay` with an `Award`, as `pay_tests.rs` does.
use super::*;
use crate::{EliteRule, FactionId, Placement, Secrets, Sim, SimConfig};

fn share() -> u64 {
    u64::from(crate::constants::REWORK_ELITE_SHARE_STACKS)
}

fn sim(elites: EliteRule) -> Sim {
    let data =
        serde_json::from_str(include_str!("../../../tests/fixtures/mob-pve-map.json")).unwrap();
    let (w, h, tiles, layers) = crate::mapdata::MapExport {
        format: "rh-map/2".into(),
        legacy_map_hash_u32: 0,
        map: data,
    }
    .validate()
    .unwrap()
    .into_parts();
    let arena = crate::Arena {
        map: crate::TileMap::new(w, h, tiles)
            .unwrap()
            .with_layers(layers)
            .unwrap(),
        spawns: vec![Vec2::new(400., 400.)],
    };
    let place = |x: f64| Placement {
        profile: "human".into(),
        faction: FactionId::Fire,
        bot: false,
        at: Vec2::new(x, 400.),
        hp: None,
        weapon0: None,
        weapon1: None,
    };
    Sim::reset_active(
        SimConfig {
            mob_mode: crate::mob_runtime::MobMode::Pve,
            elites,
            ..Default::default()
        },
        arena,
        Secrets::from_seed(1),
        &[place(400.), place(420.), place(440.)],
    )
}
fn award(elite: bool, contributors: &[u16]) -> Award {
    Award {
        mob: PlayerId(9),
        serial: 1,
        killer: Some(PlayerId(1)),
        camp: 0,
        weapon: WeaponId::Fists,
        at: Vec2::new(450., 400.),
        elite,
        mob_type: crate::mob_catalog::MobTypeId::EmberImp,
        zone: FactionId::Fire,
        mult: 1.,
        contributors: contributors
            .iter()
            .map(|i| PlayerId(u32::from(*i)))
            .collect(),
    }
}
/// The stat the camp's normal kills pay (the camp's buff row, else the mob's own type), as `pay` reads it.
fn paid(s: &Sim, a: &Award) -> crate::mob_catalog::MobTypeId {
    s.w.mob_runtime
        .as_ref()
        .and_then(|rt| rt.buffs.0.get(a.camp as usize).copied().flatten())
        .unwrap_or(a.mob_type)
}
fn counts(s: &Sim, ty: crate::mob_catalog::MobTypeId) -> Vec<u64> {
    s.w.players.iter().map(|p| p.farm.count(ty)).collect()
}
fn kill(s: &mut Sim, a: Award) {
    let mut events = vec![];
    pay(&s.k, &mut s.w, &mut events, a);
    // The end of a tick forgets the per-tick settled set; a checkpoint refuses an unsettled queue.
    s.w.pay.mob_settled.clear();
}

#[test]
fn the_share_is_one_camps_worth() {
    assert_eq!(share(), 12);
}

#[test]
fn rework_elite_kill_pays_every_standing_contributor_the_share_killer_included() {
    let mut s = sim(EliteRule::Rework);
    let a = award(true, &[1, 2, 3]);
    let ty = paid(&s, &a);
    assert_eq!(s.w.players.len(), 3);
    kill(&mut s, a);
    // Each exactly once: the killer is not paid a second time for being the killer.
    assert_eq!(counts(&s, ty), [share(), share(), share()]);
}

#[test]
fn snapshot_order_does_not_matter() {
    let mut s = sim(EliteRule::Rework);
    let a = award(true, &[3, 2, 1]);
    let ty = paid(&s, &a);
    kill(&mut s, a);
    assert_eq!(counts(&s, ty), [share(), share(), share()]);
}

#[test]
fn a_contributor_who_is_not_standing_is_not_paid() {
    for kind in ["dead", "resolved", "downed"] {
        let mut s = sim(EliteRule::Rework);
        let a = award(true, &[1, 2, 3]);
        let ty = paid(&s, &a);
        match kind {
            "dead" => s.w.players[1].status = Status::Dead,
            "resolved" => s.w.players[1].resolved = true,
            _ => s.w.players[1].down = Some(crate::results::Outcome::Died),
        }
        kill(&mut s, a);
        assert_eq!(counts(&s, ty), [share(), 0, share()], "{kind}");
    }
}

#[test]
fn only_the_snapshot_is_paid_not_every_player_in_the_match() {
    let mut s = sim(EliteRule::Rework);
    let a = award(true, &[1, 3]);
    let ty = paid(&s, &a);
    kill(&mut s, a);
    assert_eq!(counts(&s, ty), [share(), 0, share()]);
}

#[test]
fn the_share_is_elite_only_a_normal_kill_still_pays_the_killer_alone() {
    let mut s = sim(EliteRule::Rework);
    let a = award(false, &[1, 2, 3]);
    let ty = paid(&s, &a);
    kill(&mut s, a);
    assert_eq!(
        counts(&s, ty),
        [crate::modifiers::farm::DEFINITION.kill_stacks, 0, 0]
    );
}

#[test]
fn legacy_elite_kill_pays_no_stacks_to_anyone() {
    let mut s = sim(EliteRule::Legacy);
    let a = award(true, &[1, 2, 3]);
    let ty = paid(&s, &a);
    let mut events = vec![];
    pay(&s.k, &mut s.w, &mut events, a);
    assert_eq!(counts(&s, ty), [0, 0, 0]);
    // What it pays today: GV to the killer, and no stack notice.
    assert!(events.iter().any(|e| matches!(e, Event::Gv { .. })));
    assert!(!events.iter().any(|e| matches!(e, Event::Toast { .. })));
}

#[test]
fn the_share_survives_a_checkpoint_and_hashes_equal() {
    let mut s = sim(EliteRule::Rework);
    let a = award(true, &[1, 2, 3]);
    let ty = paid(&s, &a);
    kill(&mut s, a);
    let back = Sim::restore(serde_json::to_value(&s).unwrap()).unwrap();
    assert_eq!(counts(&back, ty), [share(), share(), share()]);
    assert_eq!(back.state_hash(), s.state_hash());
}
