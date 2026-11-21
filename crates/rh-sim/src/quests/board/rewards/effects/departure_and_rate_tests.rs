//! Leaving with rewards, Swap-Stick on a fill bot, and Hook/Bolas speeds at the live 40 Hz step.
use super::*;
use crate::{Arena, FactionId, Placement, RawCommand, Secrets, Sim, SimConfig, TileMap, WeaponId};

fn room_with(bots: [bool; 3], tick_ms: u32) -> Sim {
    let seats = [
        (320., 320., WeaponId::Sword, bots[0]),
        (455., 320., WeaponId::Axe, bots[1]),
        (1400., 600., WeaponId::Bow, bots[2]),
    ]
    .map(|(x, y, weapon, bot)| Placement {
        profile: format!("seat-{x}"),
        faction: FactionId::Holy,
        bot,
        at: Vec2::new(x, y),
        hp: None,
        weapon0: Some(weapon),
        weapon1: None,
    });
    let config = SimConfig {
        abilities: crate::abilities::Mode::Full,
        tick_ms,
        ..SimConfig::default()
    };
    let arena = Arena {
        map: TileMap::new(72, 30, vec![0; 72 * 30]).unwrap(),
        spawns: vec![seats[0].at, seats[1].at, seats[2].at],
    };
    let mut sim = Sim::reset_active(config, arena, Secrets::from_seed(11), &seats);
    let stream = crate::private_rng::Sfc32::from_words([1, 2, 3, 4]);
    sim.k.quest_deal = Some(Ok(crate::quests::board::Deal {
        placements: Vec::new(),
        stream: stream.clone(),
    }));
    sim.w.quests = Some(crate::quests::live::Quests {
        stream,
        offers: Vec::new(),
        carried: Vec::new(),
        recheck: false,
        board: Some(Default::default()),
        rewards: Some(Default::default()),
    });
    sim
}

fn cast(sim: &mut Sim, id: PlayerId, award: Award, at: Vec2, target: Option<PlayerId>) {
    assert!(inventory::grant(&mut sim.w, id, award));
    let cmd = crate::validate(RawCommand::Cast {
        slot: crate::abilities::Slot::Q,
        x: at.x,
        y: at.y,
        id: target,
        t: None,
        seq: None,
    })
    .unwrap();
    sim.submit_unbudgeted(id, cmd).unwrap();
    sim.step();
}

#[test]
fn a_rewarded_player_leaving_keeps_the_checkpoint_valid() {
    let mut sim = room_with([false; 3], 50);
    let a = sim.players()[0].id;
    assert!(inventory::grant(&mut sim.w, a, Award::Hook));
    sim.step();
    assert_eq!(inventory::validate(&sim.w), Ok(()));
    sim.remove_player(a);
    sim.step();
    assert!(inventory::owner(&sim.w, a).is_none());
    assert_eq!(inventory::validate(&sim.w), Ok(()));
}

#[test]
fn swap_stick_on_a_fill_bot_keeps_the_checkpoint_valid() {
    let mut sim = room_with([false, true, false], 50);
    let (a, b) = (sim.players()[0].id, sim.players()[1].id);
    let at = sim.player(b).unwrap().pos();
    cast(&mut sim, a, Award::SwapStick, at, Some(b));
    assert_eq!(state(&sim.w).unwrap().swaps.len(), 1, "the swap landed");
    assert_eq!(checkpoint::validate(&sim.k, &sim.w), Ok(()));
}

/// Milliseconds from casting Bolas until a target 540 units away is rooted.
fn bolas_ms(tick_ms: u32) -> f64 {
    let mut sim = room_with([false; 3], tick_ms);
    let (a, b) = (sim.players()[0].id, sim.players()[1].id);
    let far = Vec2::new(320. + 540., 320.);
    let ix = sim.w.fighter_idx(b).unwrap();
    sim.w.core_mut(ix).body.x = far.x;
    let t0 = sim.w.server_time;
    cast(&mut sim, a, Award::Bolas, far, None);
    for _ in 0..200 {
        let p = sim.player(b).unwrap();
        if rt::has(&p.statuses.instances, StatusKind::Root, sim.w.server_time) {
            return sim.w.server_time - t0;
        }
        sim.step();
    }
    panic!("bolas never rooted the target");
}

/// Milliseconds from casting Hook until the target is pulled within 100 units of the caster.
fn hook_ms(tick_ms: u32) -> f64 {
    let mut sim = room_with([false; 3], tick_ms);
    let (a, b) = (sim.players()[0].id, sim.players()[1].id);
    let far = Vec2::new(320. + 640., 320.);
    let ix = sim.w.fighter_idx(b).unwrap();
    sim.w.core_mut(ix).body.x = far.x;
    let t0 = sim.w.server_time;
    cast(&mut sim, a, Award::Hook, far, None);
    for _ in 0..200 {
        sim.step();
        if sim.player(b).unwrap().pos().x < 420. {
            return sim.w.server_time - t0;
        }
    }
    panic!("hook never pulled the target");
}

#[test]
fn hook_and_bolas_keep_their_speed_at_40_hz() {
    // One 20 Hz step of slack: arrival lands on each rate's step grid.
    let (slow, fast) = (bolas_ms(50), bolas_ms(25));
    assert!(
        (slow - fast).abs() <= 50.,
        "bolas 20 Hz {slow} ms vs 40 Hz {fast} ms"
    );
    let (slow, fast) = (hook_ms(50), hook_ms(25));
    assert!(
        (slow - fast).abs() <= 50.,
        "hook 20 Hz {slow} ms vs 40 Hz {fast} ms"
    );
}
