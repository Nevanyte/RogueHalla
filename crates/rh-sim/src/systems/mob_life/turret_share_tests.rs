//! P-SYNC-ORIGIN-3-fix decision 3: the Rework elite helper share follows the quest turret rule. When the credited killer
//! of an elite would have its normal kill's stat skipped (`Award::skip_stat`: its only damage came from the quest's remote
//! turret), that killer gets no helper share; every other standing contributor still gets the camp's share.
use super::*;
use crate::mob_catalog::MobTypeId;
use crate::{
    Arena, CampBuffRule, EliteRule, FactionId, Placement, Secrets, Sim, SimConfig, TileMap,
};

fn seed1_arena() -> Arena {
    let import: crate::mapdata::MapExport =
        serde_json::from_str(include_str!("../../mapdata/fixtures/seed1.map.json")).unwrap();
    let actual = import.validate().unwrap();
    let spawns = actual
        .layers()
        .data()
        .spawns
        .iter()
        .map(|p| Vec2::new(p.x, p.y))
        .collect();
    let (w, h, tiles, layers) = actual.into_parts();
    Arena {
        map: TileMap::new(w, h, tiles)
            .unwrap()
            .with_layers(layers)
            .unwrap(),
        spawns,
    }
}

fn rework_world() -> Sim {
    let arena = seed1_arena();
    let places: Vec<Placement> = (0..3)
        .map(|i| Placement {
            profile: format!("p{i}"),
            faction: FactionId::Holy,
            bot: false,
            at: arena.spawns[i % arena.spawns.len()],
            hp: None,
            weapon0: None,
            weapon1: None,
        })
        .collect();
    Sim::reset_active(
        SimConfig {
            mob_mode: crate::mob_runtime::MobMode::Pve,
            initial_loot: true,
            consumables: true,
            camp_buffs: CampBuffRule::Legacy,
            elites: EliteRule::Rework,
            ..Default::default()
        },
        arena,
        Secrets::from_seed(41),
        &places,
    )
}

fn counts(s: &Sim, t: MobTypeId) -> Vec<u64> {
    s.players().iter().map(|p| p.farm.count(t)).collect()
}

#[test]
fn a_turret_only_elite_killer_gets_no_helper_share_and_the_other_contributors_do() {
    let share = u64::from(crate::constants::REWORK_ELITE_SHARE_STACKS);
    for skip_stat in [false, true] {
        let mut s = rework_world();
        let ps: Vec<PlayerId> = s.players().iter().map(|p| p.id).collect();
        let c = (0..s.camp_rosters().len())
            .find(|&c| s.camp_rosters()[c].elite.is_some())
            .expect("an elite camp");
        // Legacy camp buffs: the share pays the camp's normal mob type.
        let stat = s.camp_rosters()[c].mob_type;
        let m = s
            .mobs()
            .iter()
            .find(|m| m.camp as usize == c && m.elite)
            .expect("the camp's elite");
        let award = Award {
            mob: m.core.id,
            serial: 1,
            killer: Some(ps[0]),
            camp: c as u32,
            weapon: m.core.active_weapon(),
            at: m.core.pos(),
            elite: true,
            mob_type: m.mob_type.unwrap(),
            zone: m.core.faction_id().unwrap(),
            mult: 1.,
            skip_stat,
            smite_bonus: false,
            contributors: ps.clone(),
        };
        let before = counts(&s, stat);
        let mut events = vec![];
        pay(&s.k, &mut s.w, &mut events, award);
        s.w.pay.mob_settled.clear();
        let got: Vec<u64> = counts(&s, stat)
            .iter()
            .zip(&before)
            .map(|(a, b)| a - b)
            .collect();
        let killer = if skip_stat { 0 } else { share };
        assert_eq!(
            got,
            vec![killer, share, share],
            "skip_stat {skip_stat}: the killer's share follows the turret rule, the helpers keep theirs"
        );
    }
}
