//! P-COLISEUM-GRAND-1 oracle (server): the map pool serves the grand coliseum by default (owner request 2026-11-19:
//! the new arena live) and `--legacy-coliseum` serves the legacy one. `StyledMaker::default()` stays the legacy maker
//! so every existing pool, anchor and dress test keeps its maps byte-identical; the server's choice is
//! `Settings::styled_maker`, the maker `main.rs` hands to `MapPool::start`.
use rh_server::map_pool::{MapMaker, StyledMaker};
use rh_server::runtime::Settings;
use rh_sim::mapgen::coliseum;

fn parse(s: &str) -> Result<Option<Settings>, String> {
    let args: Vec<String> = s.split_whitespace().map(str::to_owned).collect();
    Settings::from_args(&args)
}

fn maker(s: &str) -> Result<Option<StyledMaker>, String> {
    parse(s).map(|o| o.map(|settings| settings.styled_maker()))
}

#[test]
fn the_map_pool_makes_the_grand_coliseum_unless_legacy_is_asked_for() {
    assert_eq!(
        StyledMaker::default(),
        StyledMaker {
            signature: false,
            grand_coliseum: false
        },
        "the bare maker stays legacy (existing tests and tools)"
    );
    assert_eq!(
        maker("--runtime source-arena --map x --map-pool"),
        Ok(Some(StyledMaker {
            signature: false,
            grand_coliseum: true
        })),
        "the pool defaults to the grand coliseum"
    );
    assert_eq!(
        maker("--runtime source-arena --map x --map-pool --signature-landmarks"),
        Ok(Some(StyledMaker {
            signature: true,
            grand_coliseum: true
        }))
    );
    assert_eq!(
        maker("--runtime source-arena --map x --map-pool --legacy-coliseum"),
        Ok(Some(StyledMaker {
            signature: false,
            grand_coliseum: false
        })),
        "--legacy-coliseum turns the grand coliseum off"
    );
    assert_eq!(
        maker("--runtime source-arena --map x --map-pool --signature-landmarks --legacy-coliseum"),
        Ok(Some(StyledMaker {
            signature: true,
            grand_coliseum: false
        }))
    );
    for args in [
        "--runtime source-arena --map x --legacy-coliseum",
        "--runtime source-arena --map x --map-pool --legacy-coliseum --legacy-coliseum",
        "--runtime source-arena --map x --map-pool --legacy-coliseum=on",
        "--runtime training --legacy-coliseum",
    ] {
        assert!(maker(args).is_err(), "{args} is refused");
    }
    let plain = parse("--runtime source-arena --map x --map-pool")
        .unwrap()
        .unwrap();
    let legacy = parse("--runtime source-arena --map x --map-pool --legacy-coliseum")
        .unwrap()
        .unwrap();
    assert_eq!(
        plain.sim(),
        legacy.sim(),
        "the switch changes the maps, not the rules"
    );
}

/// The maker builds the coliseum it is told to: the served arena's coliseum is the grand layout's (36x36 rect, its
/// eight inner spawns) with `grand_coliseum`, and the legacy layout's without.
#[test]
fn the_maker_serves_the_chosen_coliseum() {
    let grand = coliseum::grand_layout();
    let legacy = coliseum::fixed_layout();
    for (grand_coliseum, want) in [(true, &grand), (false, &legacy)] {
        let m = StyledMaker {
            signature: false,
            grand_coliseum,
        };
        let arena = (1..=4u32)
            .find_map(|seed| m.make(seed).unwrap())
            .expect("a map certifies on seeds 1..=4");
        let c = arena.map.layers().expect("map layers").coliseum().clone();
        let r = c.rect;
        assert_eq!(
            (r.x, r.y, r.w as i32, r.h as i32),
            (
                want.co_rect.x,
                want.co_rect.y,
                want.co_rect.w,
                want.co_rect.h
            ),
            "grand_coliseum={grand_coliseum}: coliseum rect"
        );
        let tile = rh_sim::constants::TILE;
        let spawns: Vec<(i32, i32)> = c
            .inner_spawns
            .iter()
            .map(|p| ((p.x / tile).floor() as i32, (p.y / tile).floor() as i32))
            .collect();
        let want_spawns: Vec<(i32, i32)> = want.inner_tiles.iter().map(|t| (t.x, t.y)).collect();
        assert_eq!(
            spawns, want_spawns,
            "grand_coliseum={grand_coliseum}: inner spawns"
        );
    }
    assert_eq!(grand.co_rect.w, 36);
}
