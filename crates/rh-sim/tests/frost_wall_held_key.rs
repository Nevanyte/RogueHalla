//! P-FROST-WALL-PREDICT-1, the server guard (coordinator addition 2026-11-14): under the LIVE configuration
//! (`SimConfig::live_content()`: Frost Wall, archetype hitboxes) no walker passes through a standing Frost Wall, even fed
//! the order stream today's Web client sends for a held key: a `Move` every 2 ticks (100 ms) whose goal is 64 u beyond
//! the walker's current position along the key (`MatchClient.cs:469-487`, `KeyboardSteer.cs:19-53` on a client map
//! with no wall tiles), i.e. goals inside or just past the ice. The live client report: the server position went
//! straight through where a wall stood.
//!
//! Scene: a 40 x 40 room. A Frost caster at tile (10, 20) aims 10 u east, which clamps to `FROST_WALL_MIN_RANGE` (64 u):
//! the wall stands on column 12 (x 384..416), rows 17..=23 (y 544..768), the walk's line y = 656 through its middle.
//! The walk goes east (+x). Asserted every tick while the wall stands (the walker's own observation lists it):
//! 1. the walker's foot (its archetype's box, open interior as `geometry::box_clear`) overlaps no wall tile;
//! 2. the walker's foot is east of the ice (past x 416) only after a tick on which it stood beyond the ice's rows
//!    (y 544..768, foot included): going round an end is allowed (the server plans round the ice), going through is the
//!    bug. With 1 at every tick (steps are 8.5 u, the ice 32 u wide) this pins "round, not through".
//!
//! And each case's outcome, so the guard is not vacuous: 64 u-ahead goals get round the 7-tile wall's nearer end within
//! its 4 s life (observed: about 1 s, along y 537); goals on an ice tile centre snap to the tile in front (x 368) and the
//! walker stops there.
//!
//! Configuration: `live_content()`'s abilities, `frost_wish`, `hitboxes`, `hazards` and `walk_hints` on the default
//! config, with `wish_lock_ms: 0`. The live lifecycle and PvE faction content need the source arena (`reset_active`
//! refuses them on a test room); neither walks. The Wish lock is not what is tested.
use rh_sim::abilities::Slot;
use rh_sim::status_runtime::FieldKind;
use rh_sim::{
    observe, validate, Arena, FactionId, Placement, PlayerId, RawCommand, Secrets, Sim, SimConfig,
    TileMap, Vec2,
};

const W: u32 = 40;
const H: u32 = 40;
const TILE: f64 = 32.;
/// The ice: column 12, rows 17..=23.
const ICE_X: (f64, f64) = (384., 416.);
const ICE_Y: (f64, f64) = (544., 768.);
const LINE_Y: f64 = 656.;

fn tc(t: u32) -> f64 {
    f64::from(t) * TILE + TILE / 2.
}

fn room() -> TileMap {
    let mut tiles = vec![0u8; (W * H) as usize];
    for y in 0..H {
        for x in 0..W {
            if x == 0 || y == 0 || x == W - 1 || y == H - 1 {
                tiles[(y * W + x) as usize] = 1;
            }
        }
    }
    TileMap::new(W, H, tiles).unwrap()
}

#[derive(Clone, Copy, Debug, PartialEq)]
enum Mover {
    Caster,
    Bystander,
}

#[derive(Clone, Copy, Debug, PartialEq)]
enum Goals {
    /// 64 u east of the walker's current position (rounded to f32, as the wire).
    Ahead,
    /// The centre of the ice tile on the walker's row (or the nearest ice row).
    IceCentre,
}

struct Rig {
    sim: Sim,
    caster: PlayerId,
    bystander: PlayerId,
}

fn rig(bystander_at: Vec2) -> Rig {
    // The live world's source arena cannot start as a test room (`reset_active` refuses source planning, and the PvE
    // faction content needs the source map): the room takes every walk- and wall-relevant field from `live_content()`.
    let live = SimConfig::live_content();
    let config = SimConfig {
        abilities: live.abilities,
        frost_wish: live.frost_wish,
        hitboxes: live.hitboxes,
        hazards: live.hazards,
        walk_hints: live.walk_hints,
        wish_lock_ms: 0.,
        ..SimConfig::default()
    };
    let placements = [
        (FactionId::Frost, Vec2::new(tc(10), LINE_Y)),
        (FactionId::Fire, bystander_at),
    ]
    .into_iter()
    .enumerate()
    .map(|(i, (faction, at))| Placement {
        profile: format!("p{i}"),
        faction,
        bot: false,
        at,
        hp: Some(1e9),
        weapon0: None,
        weapon1: None,
    })
    .collect::<Vec<_>>();
    let sim = Sim::reset_active(
        config,
        Arena {
            map: room(),
            spawns: placements.iter().map(|p| p.at).collect(),
        },
        Secrets::from_seed(3),
        &placements,
    );
    let caster = sim.players()[0].id;
    let bystander = sim.players()[1].id;
    Rig {
        sim,
        caster,
        bystander,
    }
}

fn walls(sim: &Sim, viewer: PlayerId, events: &[rh_sim::Event]) -> Vec<Vec<u32>> {
    observe(sim, viewer, events)
        .fields
        .iter()
        .filter(|f| f.kind == FieldKind::Wall)
        .map(|f| f.tiles.clone())
        .collect()
}

fn overlaps_tile(p: Vec2, hx: f64, hy: f64, tile: u32) -> bool {
    let (tx, ty) = (f64::from(tile % W) * TILE, f64::from(tile / W) * TILE);
    p.x + hx > tx && p.x - hx < tx + TILE && p.y + hy > ty && p.y - hy < ty + TILE
}

/// Casts the wall, then streams the held-key orders for `mover` through the wall's whole life, asserting every tick.
fn run(bystander_at: Vec2, mover: Mover, goals: Goals) {
    let Rig {
        mut sim,
        caster,
        bystander,
    } = rig(bystander_at);
    let who = if mover == Mover::Caster {
        caster
    } else {
        bystander
    };
    let cast = validate(RawCommand::Cast {
        slot: Slot::Wish,
        x: tc(10) + 10.,
        y: LINE_Y,
        id: None,
        t: None,
        seq: None,
    })
    .unwrap();
    sim.submit(caster, cast).expect("the Wish is cast");
    let foot = sim.player(who).unwrap().foot();
    let (hx, hy) = (foot.half_x(), foot.half_y());
    let mut landed = None;
    let mut fell = false;
    let mut seq: u8 = 0;
    let mut log = Vec::new();
    let (mut went_round, mut east_while_standing, mut west_after) = (false, false, None);
    for k in 1..=200u32 {
        if landed.is_some() && !fell && k % 2 == 0 {
            let p = sim.player(who).unwrap().pos();
            let (gx, gy) = match goals {
                Goals::Ahead => (p.x + 64., p.y),
                Goals::IceCentre => {
                    let row = ((p.y / TILE).floor() as u32).clamp(17, 23);
                    (tc(12), tc(row))
                }
            };
            seq = seq.wrapping_add(1).max(1);
            let order = validate(RawCommand::Move {
                x: f64::from(gx as f32),
                y: f64::from(gy as f32),
                seq: Some(seq),
            })
            .unwrap();
            sim.submit(who, order).expect("one move order");
        }
        let events = sim.step();
        let standing = walls(&sim, who, &events);
        let me = sim.player(who).unwrap();
        let p = me.pos();
        let path: Vec<(f64, f64)> = me
            .motion
            .path
            .as_slice()
            .iter()
            .map(|q| (q.x, q.y))
            .collect();
        log.push(format!(
            "tick {k}: ({:.2}, {:.2}) walls {} path {path:?}",
            p.x,
            p.y,
            standing.len()
        ));
        if standing.is_empty() {
            if landed.is_some() {
                fell = true;
            }
            continue;
        }
        if landed.is_none() {
            landed = Some(k);
            let tiles = &standing[0];
            let want: Vec<u32> = (17..=23).map(|y| y * W + 12).collect();
            assert_eq!(tiles, &want, "the ice is column 12, rows 17..=23");
        }
        for tile in standing.iter().flatten() {
            assert!(
                !overlaps_tile(p, hx, hy, *tile),
                "{mover:?}/{goals:?}: tick {k}: the foot ({hx} x {hy}) at ({:.2}, {:.2}) overlaps ice tile ({}, {})\n{}",
                p.x,
                p.y,
                tile % W,
                tile / W,
                log.join("\n")
            );
        }
        // Round an end, not through: the walker is east of the ice only after a tick beyond the ice's rows.
        let beyond_rows = p.y + hy <= ICE_Y.0 || p.y - hy >= ICE_Y.1;
        if beyond_rows {
            went_round = true;
        }
        if p.x - hx >= ICE_X.1 {
            assert!(
                went_round,
                "{mover:?}/{goals:?}: tick {k}: east of the ice at ({:.2}, {:.2}) without having gone round an end\n{}",
                p.x,
                p.y,
                log.join("\n")
            );
            east_while_standing = true;
        }
        if p.x + hx <= ICE_X.0 {
            west_after = Some(p);
        }
    }
    assert!(landed.is_some(), "the wall landed\n{}", log.join("\n"));
    assert!(fell, "the wall fell within 200 ticks\n{}", log.join("\n"));
    match goals {
        // The server routes each 64 u-ahead goal round the ice's nearer end (7 tiles: well inside its 4 s life).
        Goals::Ahead => assert!(
            east_while_standing,
            "{mover:?}: the held key gets past the standing ice (round an end)\n{}",
            log.join("\n")
        ),
        // A goal on an ice tile snaps to the open tile in front of it: the walker stops against the west face.
        Goals::IceCentre => {
            let w = west_after.expect("west of the ice");
            assert!(
                !east_while_standing && (w.x - (ICE_X.0 - 16.)).abs() < 1e-6,
                "{mover:?}: goals on the ice stop the walker on the tile before it ({:.2}, {:.2})\n{}",
                w.x,
                w.y,
                log.join("\n")
            );
        }
    }
}

#[test]
fn the_caster_holding_the_key_into_its_own_wall_never_walks_through_it() {
    run(Vec2::new(tc(4), LINE_Y), Mover::Caster, Goals::Ahead);
}

#[test]
fn a_bystander_on_the_line_holding_the_key_never_walks_through_the_wall() {
    run(Vec2::new(tc(6), LINE_Y), Mover::Bystander, Goals::Ahead);
}

/// The bystander stands where the ice lands (its body overlaps column 12): the wall sets it down clear of the ice
/// (`barriers::set_down`), touching or near the face; from there it holds the key east.
#[test]
fn a_walker_set_down_against_the_ice_holding_the_key_never_walks_through_it() {
    run(Vec2::new(380., LINE_Y), Mover::Bystander, Goals::Ahead);
}

#[test]
fn goals_on_the_ice_tile_centres_never_take_a_walker_through_the_wall() {
    run(Vec2::new(tc(6), LINE_Y), Mover::Bystander, Goals::IceCentre);
    run(Vec2::new(tc(4), LINE_Y), Mover::Caster, Goals::IceCentre);
}
