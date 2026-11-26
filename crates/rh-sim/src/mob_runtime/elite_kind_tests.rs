//! P-BAL-ELITE-SEAM-1 oracle (in-crate half; docs/backlog/packets/P-BAL-ELITE-SEAM-1.md). The source half is
//! `tests/elite_seam.rs`.
//!
//! The real per-kind tables are empty, so a test that the readers go THROUGH the table needs a table of its own:
//! `with_rework_table` makes a doctored Rework table (the real globals plus one test row) stand in for `REWORK_ELITES` on
//! this thread, and the world built inside it plays it everywhere (spawn, the restore check, the faction sheet, point
//! damage). The row's numbers are test values, not balance numbers: what is pinned is that each reader applies
//! `global * row` to the row's kind and nothing to any other kind. The guard at the end pins that the real (empty)
//! tables give today's numbers bit for bit under both rules.
use super::{spawn_hp, with_rework_table, EliteKindRow, EliteTuning, MobMode};
use crate::mob_catalog::{mob_type_by_id, MobTypeId};
use crate::{
    validate, Arena, Command, EliteRule, Event, FactionId, Placement, PlayerId, RawCommand,
    Secrets, Sim, SimConfig, Vec2, WeaponId,
};

// ---------------------------------------------------------------------------------------------------------------------
// Worlds (the seed1 elite worlds of `tests/elite_tune.rs`).
// ---------------------------------------------------------------------------------------------------------------------

fn cmd(raw: RawCommand) -> Command {
    validate(raw).expect("a valid test command")
}

fn seed1_arena() -> Arena {
    let import: crate::mapdata::MapExport =
        serde_json::from_str(include_str!("../mapdata/fixtures/seed1.map.json")).unwrap();
    import.validate().unwrap().into_arena().unwrap()
}

/// A seed1 Rework world with one player at `at`.
fn world(at: Vec2) -> Sim {
    world_under(EliteRule::Rework, at)
}

fn world_under(elites: EliteRule, at: Vec2) -> Sim {
    Sim::reset_active(
        SimConfig {
            quests: false,
            initial_loot: true,
            consumables: true,
            mob_mode: MobMode::Pve,
            elites,
            ..Default::default()
        },
        seed1_arena(),
        Secrets::from_seed(41),
        &[Placement {
            profile: "elite-seam".into(),
            faction: FactionId::Holy,
            bot: false,
            at,
            hp: None,
            weapon0: Some(WeaponId::Sword),
            weapon1: None,
        }],
    )
}

fn spawn_point() -> Vec2 {
    seed1_arena().spawns[0]
}

/// The row's test factors: hp x1.25, damage x0.8 on top of the global pair.
fn row_factors() -> (f64, f64) {
    (1.25, 0.8)
}

/// The real Rework table plus one row for `ty` (leaked: the stand-in is `&'static`, as the real tables are).
fn doctored(ty: MobTypeId) -> &'static EliteTuning {
    let (hp, dmg) = row_factors();
    let rows: &'static [EliteKindRow] = Box::leak(Box::new([EliteKindRow { ty, hp, dmg }]));
    Box::leak(Box::new(EliteTuning {
        per_kind: rows,
        ..*EliteTuning::of(EliteRule::Rework)
    }))
}

fn aim(sim: &Sim, pid: PlayerId) -> Option<f64> {
    let me = sim.player(pid)?.pos();
    sim.mobs()
        .iter()
        .filter(|m| m.core.vitals.hp > 0.)
        .map(|m| {
            let at = m.core.pos();
            let (dx, dy) = (at.x - me.x, at.y - me.y);
            (dx * dx + dy * dy, crate::math::atan2(dy, dx))
        })
        .min_by(|a, b| a.0.total_cmp(&b.0))
        .map(|(_, a)| a)
}

/// The player fights the nearest mob (mobs are neutral until hurt), as `tests/elite_tune.rs` scripts it.
fn drive_tick(sim: &mut Sim, t: u32) -> Vec<Event> {
    let pid = PlayerId(1);
    if let Some(a) = aim(sim, pid) {
        let _ = sim.submit(pid, cmd(RawCommand::Aim { a }));
        if t % 7 == 0 {
            let _ = sim.submit(pid, cmd(RawCommand::Attack { a, t: None }));
        }
        if t % 7 == 3 {
            let _ = sim.submit(pid, cmd(RawCommand::Release { a, t: None }));
        }
    }
    sim.step()
}

/// The first hit an elite of type `ty` deals to the player within `ticks`: (tick, amount).
fn first_hit_by(sim: &mut Sim, ty: MobTypeId, ticks: u32) -> Option<(u32, f64)> {
    let ids: Vec<PlayerId> = sim
        .mobs()
        .iter()
        .filter(|m| m.elite && m.mob_type == Some(ty))
        .map(|m| m.core.id)
        .collect();
    for t in 0..ticks {
        for e in drive_tick(sim, t) {
            if let Event::Hit {
                victim,
                by: Some(by),
                amount,
                ..
            } = e
            {
                if victim == PlayerId(1) && ids.contains(&by) && amount > 0. {
                    return Some((t, amount));
                }
            }
        }
    }
    None
}

/// An elite kind of the seed1 map that lands a hit on a player fighting beside it (real Rework table), with the spot
/// the player stands on. Every seed1 world builds the same camps (the elite table draws nothing).
fn measured_kind() -> (MobTypeId, Vec2) {
    let probe = world(spawn_point());
    let mut seen: Vec<MobTypeId> = vec![];
    for m in probe.mobs().iter().filter(|m| m.elite) {
        let ty = m.mob_type.unwrap();
        if seen.contains(&ty) {
            continue;
        }
        seen.push(ty);
        let at = m.core.pos();
        let near = Vec2::new(at.x + 48., at.y);
        if first_hit_by(&mut world(near), ty, 200).is_some() {
            return (ty, near);
        }
    }
    panic!("no elite of the seed1 map hit a player fighting beside it: {seen:?}");
}

fn near(got: f64, want: f64) -> bool {
    (got - want).abs() <= 1e-12 * want.abs().max(1.)
}

// ---------------------------------------------------------------------------------------------------------------------
// Behaviour through a doctored table (red on the stub: the accessors ignore the row).
// ---------------------------------------------------------------------------------------------------------------------

#[test]
fn a_row_scales_the_spawn_hp_of_its_kind_only() {
    let (ty, _) = measured_kind();
    let (row_hp, _) = row_factors();
    let plain = world(spawn_point());
    let doc = with_rework_table(doctored(ty), || world(spawn_point()));
    assert_eq!(
        plain.mobs().len(),
        doc.mobs().len(),
        "same camps, same mobs"
    );
    let (mut rows, mut others) = (0, 0);
    for (p, d) in plain.mobs().iter().zip(doc.mobs()) {
        assert_eq!(
            (p.core.id, p.mob_type, p.elite),
            (d.core.id, d.mob_type, d.elite)
        );
        let (pv, dv) = (&p.core.vitals, &d.core.vitals);
        if p.elite && p.mob_type == Some(ty) {
            rows += 1;
            assert!(
                near(dv.max_hp, pv.max_hp * row_hp),
                "{ty:?}: doctored max hp {} is not the real {} x row {row_hp}",
                dv.max_hp,
                pv.max_hp
            );
            assert_eq!(dv.hp, dv.max_hp, "{ty:?}: spawns at full hp");
        } else {
            others += usize::from(p.elite);
            assert_eq!(
                (dv.hp.to_bits(), dv.max_hp.to_bits()),
                (pv.hp.to_bits(), pv.max_hp.to_bits()),
                "{:?} (elite {}): a kind without a row keeps its hp bit for bit",
                p.mob_type,
                p.elite
            );
        }
    }
    assert!(rows >= 1, "the row's kind ({ty:?}) is on the map");
    assert!(others >= 1, "another elite kind is on the map");
}

#[test]
fn a_row_scales_the_faction_sheet_and_point_damage_of_its_kind_only() {
    let (ty, _) = measured_kind();
    let (row_hp, row_dmg) = row_factors();
    let plain = world(spawn_point());
    let table = doctored(ty);
    let doc = with_rework_table(table, || world(spawn_point()));
    let (mut rows, mut others) = (0, 0);
    for m in plain.mobs().iter().filter(|m| m.elite) {
        let pix = plain.w.fighter_idx(m.core.id).unwrap();
        let p = plain.w.faction_spec(pix).unwrap();
        let p_sig = plain.w.signature_damage(pix, 10., false);
        let (d, d_sig) = with_rework_table(table, || {
            let dix = doc.w.fighter_idx(m.core.id).unwrap();
            (
                doc.w.faction_spec(dix).unwrap(),
                doc.w.signature_damage(dix, 10., false),
            )
        });
        let kind = m.mob_type.unwrap();
        if kind == ty {
            rows += 1;
            assert!(
                near(d.hp, p.hp * row_hp),
                "{kind:?}: sheet hp {} vs {} x {row_hp}",
                d.hp,
                p.hp
            );
            assert!(
                near(d.hit, p.hit * row_dmg),
                "{kind:?}: sheet hit {} vs {} x {row_dmg}",
                d.hit,
                p.hit
            );
            assert!(
                near(d_sig, p_sig * row_dmg),
                "{kind:?}: signature damage {d_sig} vs {p_sig} x {row_dmg}"
            );
        } else {
            others += 1;
            assert_eq!(
                (d.hp.to_bits(), d.hit.to_bits(), d_sig.to_bits()),
                (p.hp.to_bits(), p.hit.to_bits(), p_sig.to_bits()),
                "{kind:?}: a kind without a row keeps its sheet bit for bit"
            );
        }
    }
    assert!(
        rows >= 1 && others >= 1,
        "both kinds measured: {rows} {others}"
    );
}

#[test]
fn a_row_scales_the_hits_its_kind_lands_on_a_player() {
    let (ty, at) = measured_kind();
    let (_, row_dmg) = row_factors();
    let (pt, plain) = first_hit_by(&mut world(at), ty, 200).expect("measured_kind hit");
    let (dt, doc) = with_rework_table(doctored(ty), || first_hit_by(&mut world(at), ty, 200))
        .unwrap_or_else(|| panic!("{ty:?}: the doctored elite never hit where the real one did"));
    assert_eq!(pt, dt, "{ty:?}: the first elite hit lands on the same tick");
    let ratio = doc / plain;
    assert!(
        (ratio - row_dmg).abs() < 0.02,
        "{ty:?}: doctored hit {doc} / real hit {plain} = {ratio}, not the row's {row_dmg}"
    );
}

#[test]
fn a_mid_fight_checkpoint_of_a_doctored_world_restores_under_its_table_only() {
    let (ty, at) = measured_kind();
    let (row_hp, _) = row_factors();
    let real_hp = {
        let w = world(at);
        let m = w
            .mobs()
            .iter()
            .find(|m| m.elite && m.mob_type == Some(ty))
            .unwrap();
        m.core.vitals.max_hp
    };
    let table = doctored(ty);
    let (cp, hash) = with_rework_table(table, || {
        let mut sim = world(at);
        first_hit_by(&mut sim, ty, 200).expect("the doctored elite hits");
        (serde_json::to_value(&sim).unwrap(), sim.state_hash())
    });
    assert_eq!(
        cp["world"]["mob_runtime"]["elites"],
        serde_json::json!("rework")
    );
    with_rework_table(table, || {
        let mut restored =
            Sim::restore(cp.clone()).expect("the doctored checkpoint restores under its own table");
        assert_eq!(restored.state_hash(), hash, "hash after restore");
        let m = restored
            .mobs()
            .iter()
            .find(|m| m.elite && m.mob_type == Some(ty))
            .unwrap();
        assert!(
            near(m.core.vitals.max_hp, real_hp * row_hp),
            "{ty:?}: the restored elite's max hp {} is not the real {real_hp} x row {row_hp}",
            m.core.vitals.max_hp
        );
        let mut sim = Sim::restore(cp.clone()).unwrap();
        for t in 0..40 {
            let a = drive_tick(&mut sim, 1000 + t);
            let b = drive_tick(&mut restored, 1000 + t);
            assert_eq!(a, b, "tick {t} after the restore: events");
        }
    });
    // Under the real table the same checkpoint's elite hp is not a legal spawn hp: the restore check reads the table.
    assert!(
        Sim::restore(cp).is_err(),
        "a checkpoint whose {ty:?} elite has the doctored hp restores under the real table"
    );
}

// ---------------------------------------------------------------------------------------------------------------------
// Guard (green on the stub and after): the real tables are empty and play today's numbers bit for bit.
// ---------------------------------------------------------------------------------------------------------------------

fn elite_kinds() -> Vec<MobTypeId> {
    MobTypeId::ALL
        .into_iter()
        .filter(|t| mob_type_by_id(t.as_str()).unwrap().elite)
        .collect()
}

/// Today's spawn hp of an elite, written out (`spawn_hp` before this packet).
fn todays_hp(t: &EliteTuning, ty: MobTypeId) -> f64 {
    let row = mob_type_by_id(ty.as_str()).unwrap();
    (crate::content::faction(row.zone).hp * t.k)
        * (crate::constants::MOB_HP_MULT * t.hp_mult)
        * row.role_def().hp_mult
}

/// Today's faction-sheet hit of an elite, written out (`World::point_damage` before this packet; an elite's reach is 1).
fn todays_hit(t: &EliteTuning, ty: MobTypeId) -> f64 {
    let row = mob_type_by_id(ty.as_str()).unwrap();
    let raw = crate::content::faction(row.zone).hit;
    let d = crate::constants::MOB_DMG_MULT * t.dmg_mult;
    let zone = match row.zone {
        FactionId::Plague => crate::constants::MOB_PLAGUE_DAMAGE,
        FactionId::Holy => crate::constants::MOB_HOLY_DAMAGE,
        _ => 1.,
    };
    (((raw * t.k) * d) * 1.) * (row.role_def().dmg_mult * zone * 1.)
}

#[test]
fn with_the_real_tables_every_elite_kind_plays_todays_numbers_bit_for_bit() {
    let kinds = elite_kinds();
    assert_eq!(kinds.len(), 10, "ten elite kinds: {kinds:?}");
    for rule in [EliteRule::Legacy, EliteRule::Rework] {
        let t = EliteTuning::of(rule);
        assert!(
            t.per_kind.is_empty(),
            "{rule:?}: the real per-kind table is empty in this packet"
        );
        for row in t.per_kind {
            assert!(
                kinds.contains(&row.ty),
                "{rule:?}: a row for a non-elite kind {:?}",
                row.ty
            );
            assert_eq!(
                t.per_kind.iter().filter(|r| r.ty == row.ty).count(),
                1,
                "{rule:?}: one row per kind"
            );
        }
        for &ty in &kinds {
            assert_eq!(
                t.hp_mult_of(ty).to_bits(),
                t.hp_mult.to_bits(),
                "{rule:?} {ty:?} hp"
            );
            assert_eq!(
                t.dmg_mult_of(ty).to_bits(),
                t.dmg_mult.to_bits(),
                "{rule:?} {ty:?} dmg"
            );
            let zone = mob_type_by_id(ty.as_str()).unwrap().zone;
            assert_eq!(
                spawn_hp(t, zone, true, ty).to_bits(),
                todays_hp(t, ty).to_bits(),
                "{rule:?} {ty:?}: spawn hp"
            );
        }
        let sim = world_under(rule, spawn_point());
        let mut measured = 0;
        for m in sim.mobs().iter().filter(|m| m.elite) {
            let ty = m.mob_type.unwrap();
            let ix = sim.w.fighter_idx(m.core.id).unwrap();
            let f = sim.w.faction_spec(ix).unwrap();
            assert_eq!(
                m.core.vitals.max_hp.to_bits(),
                todays_hp(t, ty).to_bits(),
                "{rule:?} {ty:?}: max hp"
            );
            assert_eq!(
                f.hp.to_bits(),
                todays_hp(t, ty).to_bits(),
                "{rule:?} {ty:?}: sheet hp"
            );
            assert_eq!(
                f.hit.to_bits(),
                todays_hit(t, ty).to_bits(),
                "{rule:?} {ty:?}: sheet hit"
            );
            measured += 1;
        }
        assert!(measured >= 2, "{rule:?}: seed1 has elites");
    }
}
