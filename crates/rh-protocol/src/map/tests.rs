//! Tests for the map blob: dressing-match checks.

use super::*;

fn props() -> Vec<MapProp> {
    vec![
        MapProp {
            kind: 1,
            x: 16,
            y: 16,
            variant: 0,
            flags: PROP_FLAG_TALL,
        },
        MapProp {
            kind: 2,
            x: 40,
            y: 70,
            variant: 3,
            flags: 0,
        },
    ]
}

/// A dressed and an undressed blob of one map both match that map; neither matches another map, and a blob whose
/// non-dressing sections differ (camps) never matches.
#[test]
fn a_blob_matches_its_map_with_or_without_dressing() {
    let tiles = vec![0u8; 12];
    let plain = MapPublic::default();
    let dressing = MapPublic {
        props: props(),
        ground_variants: Some((0..12).map(|i| (i % 4) as u8).collect()),
        ..MapPublic::default()
    };
    let undressed = EncodedMap::from_public(4, 3, &tiles, None, None, &plain).unwrap();
    let dressed = EncodedMap::from_public(4, 3, &tiles, None, None, &dressing).unwrap();
    let camps = MapPublic {
        camps: vec![MapCamp {
            x: 16,
            y: 16,
            r: 40,
            zone: 1,
        }],
        ..MapPublic::default()
    };
    let mut other = tiles.clone();
    other[5] = 1;
    for blob in [&undressed, &dressed] {
        assert!(blob.matches_public_any_dressing(4, 3, &tiles, None, None, &plain));
        // The caller's own dressing is ignored: only the blob's counts.
        assert!(blob.matches_public_any_dressing(4, 3, &tiles, None, None, &dressing));
        assert!(!blob.matches_public_any_dressing(4, 3, &other, None, None, &plain));
        assert!(!blob.matches_public_any_dressing(4, 3, &tiles, None, None, &camps));
    }
    // The exact match stays exact: a dressed blob is not the undressed map's blob.
    assert!(!dressed.matches_public(4, 3, &tiles, None, None, &plain));
}
