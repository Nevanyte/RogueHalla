use super::definition::stray_camp;
use super::*;

fn camp(zone: u8, x: usize, y: usize) -> CampSite {
    CampSite {
        zone,
        site: y * W + x,
        elite: true,
        spots: Vec::new(),
    }
}

fn site(zone: Zone, x: i32, y: i32, camp: Option<usize>) -> ReservedSite {
    ReservedSite {
        zone,
        rect: TileRect { x, y, w: 4, h: 3 },
        camp,
        extra: Vec::new(),
        elite: camp.is_some(),
        stream: Mulberry32::new(1),
    }
}

/// `relabel_sites`'s precondition predicate: a second camp in the rect is found; one just outside it is not. (Which
/// sites take which label is P-MAPGEN-SIG-SCALE-2's oracle, `tests/mapgen_signature_church.rs`
/// `relabel_sites_labels_the_frost_and_the_sentenced_sites`, moved there from this test.)
#[test]
fn stray_camp_finds_a_second_camp_in_the_site() {
    let camps = [camp(1, 5, 5), camp(2, 30, 31), camp(3, 61, 61)];
    let frost = site(Zone::Frost, 60, 60, Some(camps[2].site));
    let crowded = [camp(4, 61, 61), camp(4, 63, 62), camp(4, 64, 62)];
    assert_eq!(stray_camp(&frost, &crowded, &[0]), Some(1));
    assert_eq!(
        stray_camp(&frost, &[crowded[0].clone(), crowded[2].clone()], &[0]),
        None
    );
}

/// `relabel_sites`'s precondition (its `debug_assert!`) on real reserving prefixes, seeds 1-8, attempts 0-1: no
/// camp but the inside camp has its site tile in a relabelled site, and the inside camp is one of the prefix's.
#[test]
fn no_other_camp_stands_in_a_relabelled_site_on_real_prefixes() {
    let fixed = crate::mapgen::coliseum::fixed_layout();
    let context = crate::mapgen::attempt::AttemptContext::new(&fixed);
    let mut checked = 0;
    for seed in 1..=8 {
        for n in 0..2 {
            let Some(frame) =
                crate::mapgen::attempt::begin_attempt_signature(&context, seed, n, true)
                    .expect("prefix input")
            else {
                continue;
            };
            let (reserved, camps) = (&frame.state.reserved, &frame.state.layout.camps);
            for r in reserved
                .iter()
                .filter(|r| relabelled_zones().contains(&r.zone))
            {
                let t = r.camp.expect("a relabelled site carries its camp");
                let inside = camps
                    .iter()
                    .position(|c| c.site == t)
                    .unwrap_or_else(|| panic!("seed {seed} attempt {n}: no camp on {t}"));
                assert_eq!(
                    stray_camp(r, camps, &[inside]),
                    None,
                    "seed {seed} attempt {n}: a camp in the {:?} site",
                    r.zone
                );
                checked += 1;
            }
        }
    }
    assert!(checked >= 8, "only {checked} relabelled sites checked");
}

/// One finished option-on map (seed 1, its first accepted attempt), as the entrance tests below start from.
fn signature_map() -> GameMapData {
    let fixed = crate::mapgen::coliseum::fixed_layout();
    let generator = crate::mapgen::pipeline::Pipeline::new(&fixed);
    let options = crate::mapgen::pipeline::Options {
        decor: false,
        signature: true,
        ..crate::mapgen::pipeline::Options::default()
    };
    (0..16)
        .find_map(|n| {
            generator
                .styled_attempt(1, n, options)
                .expect("generator error")
        })
        .expect("seed 1: no option-on map in 16 attempts")
}

/// The map's anchor of `zone`, the feature id (one-based, as `feature_grid`) of its feature of `kind`, and the tile
/// index of its first entrance tile.
fn door_of(m: &GameMapData, zone: Zone, kind: FeatureKind) -> (&ZoneAnchor, u8, usize) {
    let a = m
        .anchors
        .iter()
        .find(|a| a.zone == zone)
        .unwrap_or_else(|| panic!("no {zone:?} anchor"));
    let k = m
        .features
        .iter()
        .position(|f| f.kind == kind && f.zone == zone)
        .unwrap_or_else(|| panic!("no {kind:?} feature"));
    let p = a.entrance.first().expect("an entrance");
    (
        a,
        u8::try_from(k + 1).expect("feature id"),
        at(p.x, p.y).unwrap(),
    )
}

/// P-MAPGEN-SIG-SCALE-4 review note 1: the volcano's six outlets as built are LOW lava owned by the volcano, and the
/// map, outlets included, is intact.
#[test]
fn the_volcanos_outlets_as_built_are_intact() {
    let fixed = crate::mapgen::coliseum::fixed_layout();
    let ctx = SignatureContext::new(&fixed);
    let m = &signature_map();
    let (a, volcano, _) = door_of(m, Zone::Fire, FeatureKind::Volcano);
    assert_eq!(a.entrance.len(), 6, "the volcano's outlets");
    for p in &a.entrance {
        let i = at(p.x, p.y).unwrap();
        assert_eq!(
            (m.tiles[i], m.terrain[i], m.feature_grid[i]),
            (TILE_LOW, ctx.terrain_codes().lava, volcano),
            "outlet {p:?}"
        );
    }
    assert!(ctx.doors_intact(m));
    assert!(ctx.intact(m));
}

/// An outlet turned to wall, still owned by the volcano (a wall the volcano may own elsewhere: `owned_ok` accepts
/// it), is not what the drawing wrote there: the map is not intact.
#[test]
fn an_outlet_turned_to_a_volcano_wall_is_not_intact() {
    let fixed = crate::mapgen::coliseum::fixed_layout();
    let ctx = SignatureContext::new(&fixed);
    let mut m = signature_map();
    let (_, volcano, i) = door_of(&m, Zone::Fire, FeatureKind::Volcano);
    m.tiles[i] = TILE_WALL;
    m.terrain[i] = 0;
    assert_eq!(m.feature_grid[i], volcano);
    assert!(!ctx.doors_intact(&m));
    assert!(!ctx.intact(&m));
}

/// A tile of the church's main gate turned to a church wall (owned by the church, which may own any wall) is not
/// what the drawing wrote there (nothing): the map is not intact.
#[test]
fn a_church_doorway_turned_to_an_owned_church_wall_is_not_intact() {
    let fixed = crate::mapgen::coliseum::fixed_layout();
    let ctx = SignatureContext::new(&fixed);
    let mut m = signature_map();
    let (_, church, i) = door_of(&m, Zone::Sentenced, FeatureKind::Church);
    assert_eq!((m.tiles[i], m.feature_grid[i]), (TILE_FLOOR, 0));
    m.tiles[i] = TILE_WALL;
    m.feature_grid[i] = church;
    assert!(!ctx.doors_intact(&m));
    assert!(!ctx.intact(&m));
}

/// A tile of the cave's mouth owned by the cave, as the cave's ice floor (a tile the cave may own elsewhere), is not
/// what the drawing wrote there (nothing): the map is not intact.
#[test]
fn a_cave_mouth_tile_owned_by_the_cave_is_not_intact() {
    let fixed = crate::mapgen::coliseum::fixed_layout();
    let ctx = SignatureContext::new(&fixed);
    let mut m = signature_map();
    let (_, cave, i) = door_of(&m, Zone::Frost, FeatureKind::Cave);
    assert_eq!((m.tiles[i], m.feature_grid[i]), (TILE_FLOOR, 0));
    m.feature_grid[i] = cave;
    m.terrain[i] = ctx.terrain_codes().ice;
    assert!(!ctx.doors_intact(&m));
    assert!(!ctx.intact(&m));
}

/// P-MAPGEN-SIG-HOLY-1: the Holy site's relabel gives each tile the nearer of its two camps, `camp` on a tie (the
/// tiles on the perpendicular bisector), and a camp of the site no longer counts as a stray.
#[test]
fn the_holy_relabel_takes_the_nearer_camp_and_the_back_one_on_a_tie() {
    let camps = [camp(1, 5, 5), camp(5, 20, 10), camp(5, 20, 14)];
    let mut holy = site(Zone::Holy, 18, 8, Some(camps[1].site));
    holy.rect = TileRect {
        x: 18,
        y: 8,
        w: 5,
        h: 9,
    };
    holy.extra = vec![camps[2].site];
    holy.elite = false;
    assert_eq!(stray_camp(&holy, &camps, &[1, 2]), None);
    assert_eq!(stray_camp(&holy, &camps, &[1]), Some(2));
    let mut grid = vec![9u8; W * H];
    relabel_sites(std::slice::from_ref(&holy), &camps, &mut grid);
    for y in 8..17 {
        for x in 18..23 {
            // Row 12 is as far from both camps: the back camp (label 2) keeps it.
            let want = if y <= 12 { 2 } else { 3 };
            assert_eq!(grid[y * W + x], want, "({x}, {y})");
        }
    }
    assert_eq!(grid[7 * W + 20], 9, "outside the site");
}

/// The finished map's Holy anchor comes from the hall's frame alone (`finish` reads no styler feature): one Holy
/// anchor, its entrance the seven nave tiles, and the map is intact.
#[test]
fn the_holy_anchor_is_the_frames_and_the_hall_is_intact() {
    let fixed = crate::mapgen::coliseum::fixed_layout();
    let ctx = SignatureContext::new(&fixed);
    let m = signature_map();
    let holy: Vec<&ZoneAnchor> = m.anchors.iter().filter(|a| a.zone == Zone::Holy).collect();
    assert_eq!(holy.len(), 1);
    assert_eq!(holy[0].kind, FeatureKind::Hall);
    assert_eq!(holy[0].entrance.len(), 7);
    assert!(ctx.doors_intact(&m));
    assert!(ctx.intact(&m));
}

/// A hall wall turned to floor (still owned by the hall, no light) is not what the drawing wrote: not intact.
#[test]
fn a_hall_wall_turned_to_plain_floor_is_not_intact() {
    let fixed = crate::mapgen::coliseum::fixed_layout();
    let ctx = SignatureContext::new(&fixed);
    let mut m = signature_map();
    let k = m
        .features
        .iter()
        .position(|f| f.kind == FeatureKind::Hall)
        .expect("the hall");
    let i = (0..m.tiles.len())
        .find(|&i| usize::from(m.feature_grid[i]) == k + 1 && m.tiles[i] == TILE_WALL)
        .expect("a hall wall");
    m.tiles[i] = TILE_FLOOR;
    assert!(!ctx.intact(&m));
}

/// The Hall's spawn mask (owner decision 2026-11-14, 2): the box is the keep mask; `near1` holds the ring round the
/// box, the written walls and light, and every tile within `shrine_clear` of a light tile, and nothing else of the
/// box (the open floor stays open to the spawn pool). The Hall's params are the legacy row's.
#[test]
fn the_hall_keeps_spawns_off_its_walls_and_clear_of_its_light_only() {
    let fixed = crate::mapgen::coliseum::fixed_layout();
    let ctx = SignatureContext::new(&fixed);
    let context = crate::mapgen::attempt::AttemptContext::new(&fixed);
    let frame = (0..8)
        .find_map(|n| {
            crate::mapgen::attempt::begin_attempt_signature(&context, 1, n, true)
                .expect("prefix input")
        })
        .expect("a prefix");
    let mut stage = crate::mapgen::landmarks::stage::StageContext::new(&fixed)
        .apply(
            &frame,
            &crate::mapgen::landmarks::recipes::Recipes::default(),
        )
        .expect("stage input")
        .expect("a stage");
    stage.masks.near1.fill(0);
    stage.masks.boxes.fill(0);
    // A 9 x 9 box at (40, 40): a 3 x 3 pier in each corner and a 1 x 1 light in the middle.
    let light = spec::light_code();
    let mut cells = Vec::new();
    for y in 40..49 {
        for x in 40..49 {
            let corner = !(43..=45).contains(&x) && !(43..=45).contains(&y);
            if corner {
                cells.push((y * W + x, TILE_WALL, 0));
            } else if (x, y) == (44, 44) {
                cells.push((y * W + x, TILE_FLOOR, light));
            }
        }
    }
    cells.sort_unstable_by_key(|c| c.0);
    let piece = Piece {
        kind: FeatureKind::Hall,
        job: FeatureJob::Cover,
        anchor: (44, 44),
        cells: cells.clone(),
        clear: vec![44 * W + 44],
    };
    let mut terrain = vec![0u8; W * H];
    let bbox = ctx.write(&mut stage, &mut terrain, Zone::Holy, &piece);
    assert_eq!(
        bbox,
        TileRect {
            x: 40,
            y: 40,
            w: 9,
            h: 9
        }
    );
    let (b, c) = (ctx.rules.bands[0] as usize, ctx.rules.shrine_clear as usize);
    for y in 30..60usize {
        for x in 30..60usize {
            let i = y * W + x;
            let inside = (40..49).contains(&x) && (40..49).contains(&y);
            let ring = !inside && (40 - b..49 + b).contains(&x) && (40 - b..49 + b).contains(&y);
            let written = cells.iter().any(|c| c.0 == i);
            let near_light = x.abs_diff(44) <= c && y.abs_diff(44) <= c;
            assert_eq!(stage.masks.boxes[i], u8::from(inside), "boxes ({x}, {y})");
            assert_eq!(
                stage.masks.near1[i],
                u8::from(ring || written || near_light),
                "near1 ({x}, {y})"
            );
        }
    }
    let f = stage.state.features.last().expect("the hall feature");
    assert_eq!(f.p[1..], [R.hall_pitch, 4, 4, 0]);
    assert!((0..=1).contains(&f.p[0]));
}

/// P-MAPGEN-SIG-HOLY-1: the entrance check is a real check (`entrance_door`): a front run whose glyphs write
/// different things is refused; one that writes one thing is its door; no entrance, no door.
#[test]
fn an_entrance_whose_glyphs_write_different_things_is_refused() {
    use blueprint::{EntranceRule, Glyph, Legend};
    let g = |ch, write| Glyph {
        ch,
        write,
        feature: None,
        legality: Legality::Floor,
        mark: ch == b'E',
    };
    let legend = Legend(vec![
        g(b'.', Write::Nothing),
        g(b'E', Write::Nothing),
        g(b'o', Write::Floor(3)),
        Glyph {
            feature: Some(FeatureKind::Hall),
            ..g(b'#', Write::Wall)
        },
    ]);
    let mixed = Blueprint::parse("#E#\n.o.\n", &legend).expect("parses");
    assert_eq!(entrance_door(&mixed, EntranceRule::FrontRun(b'E')), Err(()));
    let plain = Blueprint::parse("#E#\n...\n", &legend).expect("parses");
    assert_eq!(
        entrance_door(&plain, EntranceRule::FrontRun(b'E')),
        Ok(Some(Door {
            write: None,
            owner: None
        }))
    );
    assert_eq!(entrance_door(&plain, EntranceRule::None), Ok(None));
    // The shipped table: every entry keeps its drawing, the hall's door is the nave's plain floor.
    let fixed = crate::mapgen::coliseum::fixed_layout();
    let ctx = SignatureContext::new(&fixed);
    assert!(ctx.landmarks.iter().all(|(_, bp)| bp.is_some()));
    assert_eq!(
        ctx.doors
            .iter()
            .find(|(z, _)| *z == Zone::Holy)
            .map(|d| d.1),
        Some(Door {
            write: None,
            owner: None
        })
    );
}
