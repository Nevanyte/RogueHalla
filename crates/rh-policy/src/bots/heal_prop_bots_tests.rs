//! P-COLISEUM-HEALS-2 oracle (sim item view -> policy projection -> wire -> bots): the four grand-coliseum heal pickups
//! (P-COLISEUM-HEALS-1) reach a seat as props of kind `Heal { ready_at }` (prop kind 8, protocol 20) with their public
//! clocks, never as the F focus; and the HumanBot perception ignores them (neither a cannon nor a chest nor any
//! container). Loose proof: no legacy counterpart; the clock is the server time the pickup is available again, the
//! cannon's `ready_ms` clamp (0 when available).
use crate::bots::perception::Perception;
use crate::delivery::collections::CollectionPublisher;
use rh_protocol::{ServerMsg, StateItem, WirePropKind, WireProps};
use rh_sim::{
    arenas::practice_arena, Arena, ContainerKind, FactionId, ItemObserver, Observation,
    PickupHandle, Placement, PropCue, PropKind, Secrets, Sim, SimConfig, Vec2, VisibleProp,
};

/// The four pickup positions (HEALS-1): centres of tiles (79,79), (80,79), (79,80), (80,80), NW, NE, SW, SE.
const SPOTS: [(f64, f64); 4] = [
    (2544., 2544.),
    (2576., 2544.),
    (2544., 2576.),
    (2576., 2576.),
];

/// A certified styled map around the grand coliseum, as the server's pool makes it (no decor).
fn grand_arena() -> Arena {
    use rh_sim::mapgen::{coliseum, map_hash, pipeline};
    let fixed = coliseum::grand_layout();
    let options = pipeline::Options {
        decor: false,
        ..pipeline::Options::default()
    };
    let map = (1..=4)
        .find_map(|seed| {
            pipeline::Pipeline::new(&fixed)
                .certified_styled(seed, options)
                .expect("the generator runs")
        })
        .expect("a grand map certifies on seeds 1..=4");
    let legacy_map_hash_u32 = u32::from_str_radix(&map_hash::map_hash(&map), 16).unwrap();
    rh_sim::mapdata::MapExport {
        format: "rh-map/2".into(),
        legacy_map_hash_u32,
        map,
    }
    .validate()
    .unwrap()
    .into_arena()
    .unwrap()
}

fn placement(at: Vec2, hp: Option<f64>) -> Placement {
    Placement {
        profile: "viewer".into(),
        faction: FactionId::Fire,
        bot: false,
        at,
        hp,
        weapon0: None,
        weapon1: None,
    }
}

fn props_of(frame: &[u8]) -> WireProps {
    let msg = ServerMsg::decode(frame).expect("a State frame every decoder accepts");
    let ServerMsg::State { items, .. } = msg else {
        panic!("not a State frame")
    };
    items
        .into_iter()
        .find_map(|i| match i {
            StateItem::Props(p) => Some(p),
            _ => None,
        })
        .expect("key 2 in a first, full State")
}

fn quantized(spot: (f64, f64)) -> (u16, u16) {
    ((spot.0 * 8.) as u16, (spot.1 * 8.) as u16)
}

#[test]
fn the_four_heals_reach_the_seat_as_heal_props_with_their_clocks_and_are_never_the_focus() {
    // The viewer stands damaged on the NW pickup: it takes it on its first active steps, so NW's clock runs while
    // the other three stay available.
    let mut sim = Sim::reset_active(
        SimConfig::default(),
        grand_arena(),
        Secrets::from_seed(5),
        &[placement(Vec2::new(SPOTS[0].0, SPOTS[0].1), Some(1.))],
    );
    let viewer = sim.players()[0].id;
    let mut events = Vec::new();
    for _ in 0..4 {
        events = sim.step();
    }
    let heals: Vec<rh_sim::Container> = sim
        .containers()
        .iter()
        .filter(|c| c.kind == ContainerKind::Heal)
        .cloned()
        .collect();
    assert_eq!(heals.len(), 4, "the scene: four heal pickups (HEALS-1)");
    let now = sim.server_time();
    assert!(
        heals[0].ready_at > now && heals[1..].iter().all(|c| c.ready_at == 0.),
        "the scene: the viewer took the NW pickup, the others are available ({:?} at {now})",
        heals.iter().map(|c| c.ready_at).collect::<Vec<_>>()
    );
    let want_nw = heals[0].ready_at as u32;

    // The sim's item view offers them.
    let mut obs = Observation::default();
    let mut observer = ItemObserver::new(viewer);
    let offer = observer
        .prepare_into(&sim, &events, &mut obs, None)
        .expect("the viewer's offer");
    let changed = offer.props_changed();
    offer.commit();
    let mut seen: Vec<(u16, u16, u32)> = obs
        .props
        .iter()
        .filter_map(|p| match p.cue.kind {
            PropKind::Heal { ready_at } => Some((p.cue.x, p.cue.y, ready_at)),
            _ => None,
        })
        .collect();
    seen.sort_unstable();
    let mut want: Vec<(u16, u16, u32)> = SPOTS
        .iter()
        .enumerate()
        .map(|(i, &s)| {
            let (x, y) = quantized(s);
            (x, y, if i == 0 { want_nw } else { 0 })
        })
        .collect();
    want.sort_unstable();
    assert_eq!(
        seen, want,
        "the item view offers the four pickups as PropKind::Heal at their spots, NW with its clock, the others 0"
    );
    assert!(
        obs.props
            .iter()
            .all(|p| !(matches!(p.cue.kind, PropKind::Heal { .. }) && p.cue.focused)),
        "a heal pickup is never the F focus (it holds no item)"
    );

    // The policy projection puts them on the wire as kind Heal, and they decode back unchanged.
    let pending = CollectionPublisher::default()
        .prepare(&obs, changed, false, false)
        .expect("the observation encodes");
    let frame = pending.frame.expect("a first, full State");
    let props = props_of(&frame);
    let mut wire: Vec<(u16, u16, u32)> = props
        .rows
        .iter()
        .filter_map(|r| match r.kind {
            WirePropKind::Heal { ready_at } => Some((r.x, r.y, ready_at)),
            _ => None,
        })
        .collect();
    wire.sort_unstable();
    assert_eq!(wire, want, "the wire carries the four Heal rows unchanged");
    let spots: Vec<(u16, u16)> = SPOTS.iter().map(|&s| quantized(s)).collect();
    assert!(
        props
            .rows
            .iter()
            .all(|r| !spots.contains(&(r.x, r.y)) || matches!(r.kind, WirePropKind::Heal { .. })),
        "nothing at a heal spot is sent as another kind (a cannon or a chest)"
    );
    if let Some(focus) = props.focus {
        let row = props
            .rows
            .iter()
            .find(|r| r.handle == focus.handle)
            .unwrap();
        assert!(
            !matches!(row.kind, WirePropKind::Heal { .. }),
            "the focus is never a heal row"
        );
    }
    // Byte level: each heal row is `handle | x | y | 8 | ready_at varint` (the cannon's layout, kind 8).
    for (i, r) in props.rows.iter().enumerate() {
        if let WirePropKind::Heal { ready_at } = r.kind {
            let single = ServerMsg::State {
                tick: 1,
                items: vec![StateItem::Props(WireProps {
                    rows: vec![props.rows[i].clone()],
                    focus: None,
                })],
            }
            .encode();
            let mut row = rh_protocol::Writer::default();
            row.varint(r.handle.get());
            row.u16(r.x);
            row.u16(r.y);
            row.u8(8);
            row.varint(ready_at);
            let row = row.finish();
            assert!(
                single.windows(row.len()).any(|w| w == row.as_slice()),
                "row {i}: a heal row is the cannon's layout with kind byte 8"
            );
        }
    }
}

#[test]
fn the_humanbot_perception_ignores_heal_rows() {
    let sim = Sim::reset_active(
        SimConfig::default(),
        practice_arena(),
        Secrets::from_seed(7),
        &[placement(Vec2::new(100., 272.), None)],
    );
    let viewer = sim.players()[0].id;
    let mut obs = rh_sim::observe(&sim, viewer, &[]);
    let near: [(f64, f64); 4] = [(132., 272.), (164., 272.), (132., 304.), (164., 304.)];
    obs.props = near
        .iter()
        .enumerate()
        .map(|(i, &(x, y))| VisibleProp {
            handle: PickupHandle::new(i as u32 + 1).unwrap(),
            cue: PropCue {
                x: (x * 8.) as u16,
                y: (y * 8.) as u16,
                kind: PropKind::Heal {
                    ready_at: if i == 0 { 30_000 } else { 0 },
                },
                focused: false,
                body_hint: None,
            },
        })
        .collect();
    let frame = crate::delivery::test_support::frame(&obs);
    assert_eq!(
        frame
            .collections
            .props
            .rows
            .iter()
            .filter(|r| matches!(r.kind, WirePropKind::Heal { .. }))
            .count(),
        4,
        "the scene: the seat's frame holds four Heal rows"
    );
    let out = Perception::default()
        .build(&frame, 1.)
        .expect("the perception builds");
    let at_heal = |x: f64, y: f64| {
        near.iter()
            .any(|&(hx, hy)| (hx - x).abs() < 0.5 && (hy - y).abs() < 0.5)
    };
    let containers: Vec<_> = out
        .percept
        .containers
        .iter()
        .filter(|c| at_heal(c.x, c.y))
        .map(|c| (c.kind.clone(), c.x, c.y))
        .collect();
    assert!(
        containers.is_empty(),
        "a bot sees no container at a heal pickup (not a cannon, not a chest): {containers:?}"
    );
    let loot: Vec<_> = out
        .loot
        .props
        .iter()
        .filter(|p| at_heal(p.at.x, p.at.y))
        .map(|p| (p.kind, p.at))
        .collect();
    assert!(
        loot.is_empty(),
        "the loot executor gets no heal row to walk to or press F on: {loot:?}"
    );
}
