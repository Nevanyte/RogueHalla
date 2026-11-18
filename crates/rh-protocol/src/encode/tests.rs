//! Tests for the stack-built record the Snapshot head and actors are written into.

use super::*;

/// The stack record's varint, record append and the byte-wise `Writer` agree on every length-class boundary.
#[test]
fn fixed_varint_matches_writer_varint() {
    let mut vals = vec![0u32, 1, u32::MAX, u32::MAX - 1];
    for sh in [7u32, 14, 21, 28] {
        let b = 1u32 << sh;
        vals.extend([b - 1, b, b + 1]);
    }
    for v in vals {
        let mut f = Fixed::<8>::new();
        f.varint(v);
        let mut w = Writer::with_capacity(8);
        w.varint(v);
        let mut out = Writer::with_capacity(0);
        out.u8(0xAA);
        f.put(&mut out);
        let got = out.finish();
        assert_eq!(&got[1..], &w.finish()[..], "{v}");
        assert_eq!(got[0], 0xAA);
    }
}
