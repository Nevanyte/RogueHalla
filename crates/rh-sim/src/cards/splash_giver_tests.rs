//! P-SYNC-ORIGIN-3-fix decision 1: Overkill's splash spares quest givers (legacy `splashOverkill`,
//! `packages/server/src/sim/cardHooks.ts:252`, skips `isQuestGiver(c)`). A splash that lands on a neutral giver gives it a
//! contact and a target, which its restore check refuses (`rework_at_25ms` seed 7 under the Top 25 quests).
use crate::cards::CardId;
use crate::state::Packet;
use crate::systems::ledger::{Entry, OverkillHit};
use crate::{Placement, PlayerId, Secrets, Sim, SimConfig, Vec2};

/// A K1 quest world (the quest lifecycle's own test world): three players and live neutral givers.
fn quest_world() -> Sim {
    use crate::mapgen::{coliseum, map_hash, pipeline};
    let fixed = coliseum::fixed_layout();
    let map = pipeline::Pipeline::new(&fixed)
        .generate_styled(
            1,
            Some(0),
            pipeline::Options {
                decor: false,
                ..Default::default()
            },
        )
        .expect("seed 1 attempt 0 generates");
    let hash = u32::from_str_radix(&map_hash::map_hash(&map), 16).unwrap();
    let arena = crate::mapdata::MapExport {
        format: "rh-map/2".into(),
        legacy_map_hash_u32: hash,
        map,
    }
    .validate()
    .expect("admitted")
    .into_arena()
    .expect("arena");
    let placements: Vec<Placement> = arena
        .spawns
        .iter()
        .take(3)
        .enumerate()
        .map(|(i, p)| Placement {
            profile: ["holy", "fire", "plague"][i].into(),
            faction: [
                crate::FactionId::Holy,
                crate::FactionId::Fire,
                crate::FactionId::Plague,
            ][i],
            bot: false,
            at: *p,
            hp: None,
            weapon0: None,
            weapon1: None,
        })
        .collect();
    Sim::reset_active(
        SimConfig {
            quests: true,
            pvp_rules: false,
            ..Default::default()
        },
        arena,
        Secrets {
            order_salt: 7,
            spawn_seed: 0,
            reward_salt: [1, 2, 3, 4],
        },
        &placements,
    )
}

fn place(s: &mut Sim, id: PlayerId, at: Vec2) {
    let now = s.w.server_time;
    let p = s.w.players.get_mut(id).unwrap();
    p.body.x = at.x;
    p.body.y = at.y;
    p.body.history.reset(crate::state::Sample {
        t: now,
        x: at.x,
        y: at.y,
    });
}

#[test]
fn an_overkill_kill_beside_a_quest_giver_leaves_the_giver_untouched() {
    let mut s = quest_world();
    let giver =
        s.w.quests
            .as_ref()
            .unwrap()
            .offers
            .iter()
            .find_map(|o| o.giver)
            .expect("a live giver");
    let at = s.w.mobs.get(giver).unwrap().core.pos();
    // The Overkill holder (fire) kills the plague player standing 6 u from the giver; the third player is far away, so
    // the giver is the nearest actor to the kill.
    let (holder, victim) = (PlayerId(2), PlayerId(3));
    assert!(crate::cards::grant(
        &s.k,
        &mut s.w,
        holder,
        CardId::Overkill
    ));
    place(&mut s, victim, Vec2::new(at.x + 6., at.y));
    place(&mut s, holder, Vec2::new(at.x + 40., at.y));
    let entry = Entry {
        terrain: false,
        dot: false,
        ability: None,
        arena: false,
        source: None,
        critical: false,
        critical_mult: None,
        sig: None,
        execute: false,
        victim,
        by: Some(holder),
        packet: Packet {
            raw: 100.,
            phys: 1.,
            magic: 0.,
            true_: 0.,
        },
        weapon: Some(crate::content::WeaponId::Sword),
        stagger: false,
        projectile: None,
        scale: 1.,
        derived: false,
        passive: false,
    };
    let mut ev = Vec::new();
    let mut hx = crate::hooks::Hx {
        stats: Default::default(),
        fx: None,
        k: &s.k,
        w: &mut s.w,
        ev: &mut ev,
    };
    crate::cards::on_overkilled::on_overkilled(
        &mut hx,
        &crate::hooks::HookArgs::Overkilled {
            victim,
            hits: vec![OverkillHit {
                entry,
                amount: 100.,
                raw: 100.,
            }],
            overkill: 100.,
            total: 100.,
        },
    );
    assert_eq!(hx.w.card_splashes.len(), 1, "premise: Overkill splashes");
    let radius = hx.w.card_splashes[0].radius;
    assert!(
        radius > 6.,
        "premise: the giver is inside the splash radius"
    );
    super::resolve_splashes(hx.w);
    assert!(
        s.w.deferred.hits.iter().all(|h| h.victim != giver),
        "the splash never picks the quest giver"
    );
    let hp = s.w.mobs.get(giver).unwrap().core.vitals.hp;
    s.step();
    let g = &s.w.mobs.get(giver).unwrap().core;
    assert_eq!(g.vitals.hp, hp, "no damage");
    assert_eq!(g.signature.target, None, "no target");
    assert_eq!(g.signature.contact_at, None, "no contact");
    assert!(
        Sim::restore(serde_json::to_value(&s).unwrap()).is_ok(),
        "the giver's checkpoint still restores"
    );
}
