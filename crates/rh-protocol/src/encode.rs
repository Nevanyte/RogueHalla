//! Encoders. Every message has an exact [`ServerMsg::encoded_len`] / [`ClientMsg::encoded_len`], so a frame is written
//! into a buffer sized once:
//!
//! * `encode()` allocates exactly one `Vec` of exactly the frame's size (infallible: for frames that cannot be refused,
//!   such as Pong and Reject; a message decoders would reject trips a `debug_assert`).
//! * `try_encode()` is the same single exact allocation, but returns an [`EncodeError`] instead of writing a frame
//!   decoders would reject (the server's Events and Fx path). `encode_into(&mut Vec<u8>)` appends to a caller's buffer
//!   (reused across frames: no allocation once it is warm) with the same refusals.
//! * The server does not encode `ServerMsg::Snapshot` at all: it builds a [`SnapshotData`] ([`SnapshotData::build_with`], which
//!   refuses what these encoders refuse) and its connection's [`ConnEncoder`] writes the Full or Delta frame.
//! * [`encode_welcome`] takes a map that was RLE-encoded once ([`crate::map::MapBlob::pre_encode`]).
//!
//! One layout: `ServerMsg::Snapshot` is the reference writer of the v2 Snapshot, and tests hold the Full frame of a
//! [`SnapshotData`] byte-identical to it and to the golden vectors. A v2 Snapshot's size depends on its content (field masks, varints), so
//! `encoded_len()` walks the actors once (no allocation) instead of multiplying.

use crate::codec::{blob_len, str_fits, str_len, varint_len, EncodeError, Writer};
use crate::map::EncodedMap;
use crate::*;

/// Protocol v3: per-connection snapshot deltas. A child module so it reaches this module's private writers.
#[path = "delta.rs"]
pub mod delta;

mod client;
mod record;
mod server;
mod snapshot;
mod welcome;

use self::record::point_valid;
pub use self::welcome::{encode_welcome, encode_welcome_into, WelcomeHeader, WELCOME_HEADER_LEN};
pub(crate) use self::{record::*, snapshot::*};

const fn opt_len(present: bool, len: usize) -> usize {
    if present {
        1 + len
    } else {
        1
    }
}

/// What both message enums share: an exact size, the checks a decoder would apply, and the writer.
trait Frame {
    const MAX_FRAME: usize;
    fn encoded_len(&self) -> usize;
    /// `len` is `encoded_len()` (computed once by the caller).
    fn check(&self, len: usize) -> Result<(), EncodeError>;
    fn write(&self, w: &mut Writer);
    /// The frame in one allocation of exactly its length. The default sizes it first (`encoded_len`), then writes.
    fn exact_vec(&self) -> Vec<u8> {
        sized_then_written(self)
    }
}

fn frame_size(len: usize, max: usize) -> Result<(), EncodeError> {
    if len > max {
        return Err(EncodeError::TooLarge { len, max });
    }
    Ok(())
}

fn list(what: &'static str, n: usize, max: usize) -> Result<(), EncodeError> {
    if n > max {
        return Err(EncodeError::TooMany { what, n, max });
    }
    Ok(())
}

fn encode_exact<F: Frame>(m: &F) -> Vec<u8> {
    m.exact_vec()
}

fn sized_then_written<F: Frame + ?Sized>(m: &F) -> Vec<u8> {
    let len = m.encoded_len();
    #[cfg(debug_assertions)]
    if let Err(e) = m.check(len) {
        panic!("encoding a frame every decoder rejects: {e} (use try_encode or encode_into)");
    }
    let mut w = Writer::with_capacity(len);
    m.write(&mut w);
    debug_assert_eq!(w.len(), len, "encoded_len disagrees with the writer");
    w.finish()
}

/// The refusing twin of [`sized_then_written`]: the same single allocation of exactly the frame's size, or the reason a
/// decoder would reject the frame (nothing is allocated then).
fn checked_exact<F: Frame>(m: &F) -> Result<Vec<u8>, EncodeError> {
    let len = m.encoded_len();
    m.check(len)?;
    let mut w = Writer::with_capacity(len);
    m.write(&mut w);
    debug_assert_eq!(w.len(), len, "encoded_len disagrees with the writer");
    Ok(w.finish())
}

fn encode_into<F: Frame>(m: &F, out: &mut Vec<u8>) -> Result<usize, EncodeError> {
    let len = m.encoded_len();
    m.check(len)?;
    let start = out.len();
    let mut w = Writer::from_vec(std::mem::take(out));
    w.reserve(len);
    m.write(&mut w);
    *out = w.finish();
    debug_assert_eq!(
        out.len() - start,
        len,
        "encoded_len disagrees with the writer"
    );
    Ok(len)
}

#[cfg(test)]
mod tests;
