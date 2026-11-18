//! The Welcome frame: its header and the encoder that writes the pre-encoded map after it.

use super::*;

/// The fields of a Welcome before its map, for [`encode_welcome`].
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct WelcomeHeader {
    pub protocol: u16,
    pub content_hash: u64,
    pub match_id: [u8; 16],
    pub player_id: u32,
    pub session: [u8; 16],
    pub tick_rate: u8,
    pub tick: u32,
    pub server_ms: f64,
}

impl WelcomeHeader {
    pub(super) fn write(&self, w: &mut Writer) {
        w.u8(T_WELCOME);
        w.u16(self.protocol);
        w.u64(self.content_hash);
        w.bytes(&self.match_id);
        w.u32(self.player_id);
        w.bytes(&self.session);
        w.u8(self.tick_rate);
        w.u32(self.tick);
        w.f64(self.server_ms);
    }
}

/// A Welcome frame with a pre-encoded map: the same bytes as
/// `ServerMsg::Welcome { .., archetypes, vision, weapons, hud, map }.encode()`, without RLE encoding (or cloning) the map
/// per joining player. One allocation of the exact size. `archetypes` must pass [`crate::archetype::table_fault`],
/// `vision` [`WireVision::fault`], `weapons` [`crate::weapon::table_fault`] and `hud`
/// [`crate::hud_content::content_fault`] (the server builds them from build constants, sim content and the validated map,
/// checked by its tests).
pub fn encode_welcome(
    h: &WelcomeHeader,
    archetypes: &[WireArchetype],
    vision: &WireVision,
    weapons: &[WireWeapon],
    hud: &WireHudContent,
    map: &EncodedMap,
    mode: GroundMode,
) -> Vec<u8> {
    debug_assert_eq!(crate::archetype::table_fault(archetypes), None);
    debug_assert_eq!(vision.fault(), None);
    debug_assert!(vision.fits(map.w(), map.h()));
    debug_assert_eq!(crate::weapon::table_fault(weapons), None);
    debug_assert_eq!(crate::hud_content::content_fault(hud), None);
    let len = WELCOME_HEADER_LEN
        + crate::archetype::table_len(archetypes)
        + vision.encoded_len()
        + crate::weapon::table_len(weapons)
        + crate::hud_content::content_len(hud)
        + map.encoded_len_for_mode(mode);
    debug_assert!(
        len <= MAX_SERVER_FRAME,
        "a Welcome of {len} bytes exceeds MAX_SERVER_FRAME"
    );
    let mut w = Writer::with_capacity(len);
    h.write(&mut w);
    crate::archetype::write_table(&mut w, archetypes);
    vision.write(&mut w);
    crate::weapon::write_table(&mut w, weapons);
    crate::hud_content::write_content(&mut w, hud);
    map.write_with_mode(&mut w, mode);
    w.finish()
}

/// [`encode_welcome`] appended to `out`; a frame over [`MAX_SERVER_FRAME`] is an error and leaves `out` unchanged.
#[allow(clippy::too_many_arguments)] // the Welcome's sections, in wire order
pub fn encode_welcome_into(
    h: &WelcomeHeader,
    archetypes: &[WireArchetype],
    vision: &WireVision,
    weapons: &[WireWeapon],
    hud: &WireHudContent,
    map: &EncodedMap,
    mode: GroundMode,
    out: &mut Vec<u8>,
) -> Result<usize, EncodeError> {
    if let Some(why) = crate::archetype::table_fault(archetypes) {
        return Err(EncodeError::BadValue(why));
    }
    if let Some(why) = vision.fault() {
        return Err(EncodeError::BadValue(why));
    }
    if !vision.fits(map.w(), map.h()) {
        return Err(EncodeError::BadValue("vision"));
    }
    if let Some(why) = crate::weapon::table_fault(weapons) {
        return Err(EncodeError::BadValue(why));
    }
    if let Some(why) = crate::hud_content::content_fault(hud) {
        return Err(EncodeError::BadValue(why));
    }
    let len = WELCOME_HEADER_LEN
        + crate::archetype::table_len(archetypes)
        + vision.encoded_len()
        + crate::weapon::table_len(weapons)
        + crate::hud_content::content_len(hud)
        + map.encoded_len_for_mode(mode);
    frame_size(len, MAX_SERVER_FRAME)?;
    let mut w = Writer::from_vec(std::mem::take(out));
    w.reserve(len);
    h.write(&mut w);
    crate::archetype::write_table(&mut w, archetypes);
    vision.write(&mut w);
    crate::weapon::write_table(&mut w, weapons);
    crate::hud_content::write_content(&mut w, hud);
    map.write_with_mode(&mut w, mode);
    *out = w.finish();
    Ok(len)
}

/// Welcome up to the map: `tag, protocol u16, content_hash u64, match_id 16, player_id u32, session 16, tick_rate u8,
/// tick u32, server_ms f64`.
pub const WELCOME_HEADER_LEN: usize = 60;
