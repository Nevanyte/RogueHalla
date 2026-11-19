//! WallBuster's reliable, reconnectable replacement terrain plane.
use rh_protocol::{DecodeError, ServerMsg, StateItem};

fn state(cells: Vec<u32>) -> ServerMsg {
    ServerMsg::State {
        tick: 209,
        items: vec![StateItem::DestroyedTerrain(cells)],
    }
}

#[test]
fn destroyed_terrain_round_trips_complete_rows_and_refuses_malformed_ones() {
    let good = state(vec![52, 1057, 90_003]);
    assert_eq!(ServerMsg::decode(&good.try_encode().unwrap()), Ok(good));
    for invalid in [vec![7, 7], vec![9, 7], vec![1 << 20]] {
        assert!(state(invalid).try_encode().is_err());
    }

    // State, tick, one key, key 14, two cells in a nonascending order.
    let invalid = [0x88, 5, 1, 14, 2, 9, 7];
    assert_eq!(
        ServerMsg::decode(&invalid),
        Err(DecodeError::BadField("destroyed terrain cells"))
    );
    assert_eq!(
        ServerMsg::decode(&invalid[..invalid.len() - 1]),
        Err(DecodeError::Truncated)
    );
}
