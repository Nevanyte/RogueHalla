//! Visible props, fields and projectiles, and the small weapon and handle readers they share.

use super::*;

impl WireProjectile {
    pub(super) fn valid(&self) -> bool {
        self.kind <= 1 && self.owner != Some(0)
    }
    pub(super) fn len(&self) -> usize {
        9 + self.owner.map_or(0, varint_len)
    }
    pub(super) fn write(&self, w: &mut Writer) {
        w.u8(self.kind);
        w.u16(self.x);
        w.u16(self.y);
        w.u16(self.facing);
        w.opt(self.owner.as_ref(), |w, id| w.varint(*id));
        w.u8(self.height);
    }
    pub(super) fn read(r: &mut Reader<'_>) -> Result<Self, DecodeError> {
        let v = Self {
            kind: r.u8()?,
            x: r.u16()?,
            y: r.u16()?,
            facing: r.u16()?,
            owner: r.opt(Reader::varint)?,
            height: r.u8()?,
        };
        if v.valid() {
            Ok(v)
        } else {
            Err(DecodeError::BadField("projectile"))
        }
    }
}

pub(super) fn weapon(value: u8) -> Result<u8, DecodeError> {
    if value < 5 {
        Ok(value)
    } else {
        Err(DecodeError::BadField("carried weapon"))
    }
}
pub(super) fn read_weapon(r: &mut Reader<'_>) -> Result<Option<u8>, DecodeError> {
    match r.u8()? {
        0 => Ok(None),
        value => Ok(Some(weapon(value - 1)?)),
    }
}
pub(super) fn write_weapon(w: &mut Writer, value: Option<u8>) {
    w.u8(value.map_or(0, |v| v + 1));
}
pub(super) fn read_handle(r: &mut Reader<'_>) -> Result<PickupHandle, DecodeError> {
    PickupHandle::new(r.varint()?).ok_or(DecodeError::BadField("pickup handle"))
}
pub(super) fn bool_flag(r: &mut Reader<'_>) -> Result<bool, DecodeError> {
    match r.u8()? {
        0 => Ok(false),
        1 => Ok(true),
        _ => Err(DecodeError::BadField("flag")),
    }
}
pub(super) fn check_weapon(value: Option<u8>) -> Result<(), EncodeError> {
    if value.is_some_and(|v| v >= 5) {
        Err(EncodeError::BadValue("carried weapon"))
    } else {
        Ok(())
    }
}

impl WireProps {
    pub(super) fn check(&self) -> Result<(), EncodeError> {
        if self.rows.len() > MAX_VISIBLE_PROPS {
            return Err(EncodeError::TooMany {
                what: "props",
                n: self.rows.len(),
                max: MAX_VISIBLE_PROPS,
            });
        }
        let mut previous = 0;
        for row in &self.rows {
            if row.handle.get() <= previous {
                return Err(EncodeError::BadValue("prop handles"));
            }
            previous = row.handle.get();
            match &row.kind {
                WirePropKind::Ground(v) => check_weapon(Some(*v))?,
                WirePropKind::Chest { .. } => {}
                WirePropKind::Body { label } | WirePropKind::Corpse { label } => {
                    str_fits("prop label", label)?
                }
                WirePropKind::WindEmblem => {}
                WirePropKind::Cannon { .. } => {}
                WirePropKind::QuestItem { .. } => {}
            }
        }
        if let Some(focus) = self.focus {
            let row = self
                .rows
                .binary_search_by_key(&focus.handle, |row| row.handle)
                .ok()
                .map(|i| &self.rows[i])
                .ok_or(EncodeError::BadValue("prop focus"))?;
            check_weapon(focus.body_hint)?;
            if focus.body_hint.is_some() && !matches!(row.kind, WirePropKind::Body { .. }) {
                return Err(EncodeError::BadValue("body hint"));
            }
        }
        Ok(())
    }

    pub(super) fn len(&self) -> usize {
        varint_len(self.rows.len() as u32)
            + self
                .rows
                .iter()
                .map(|row| {
                    varint_len(row.handle.get())
                        + 5
                        + match &row.kind {
                            WirePropKind::Ground(_) | WirePropKind::Chest { .. } => 1,
                            WirePropKind::WindEmblem => 0,
                            WirePropKind::QuestItem { quest, .. } => varint_len(quest.get()) + 1,
                            WirePropKind::Body { label } | WirePropKind::Corpse { label } => {
                                str_len(label)
                            }
                            WirePropKind::Cannon { ready_at } => varint_len(*ready_at),
                        }
                })
                .sum::<usize>()
            + self.focus.map_or(1, |f| varint_len(f.handle.get()) + 1)
    }

    pub(super) fn write(&self, w: &mut Writer) {
        w.varint(self.rows.len() as u32);
        for row in &self.rows {
            w.varint(row.handle.get());
            w.u16(row.x);
            w.u16(row.y);
            match &row.kind {
                WirePropKind::Ground(v) => {
                    w.u8(1);
                    w.u8(*v);
                }
                WirePropKind::Chest { open } => {
                    w.u8(2);
                    w.u8(u8::from(*open));
                }
                WirePropKind::Body { label } => {
                    w.u8(3);
                    w.str(label);
                }
                WirePropKind::Corpse { label } => {
                    w.u8(4);
                    w.str(label);
                }
                WirePropKind::WindEmblem => {
                    w.u8(5);
                }
                WirePropKind::Cannon { ready_at } => {
                    w.u8(6);
                    w.varint(*ready_at);
                }
                WirePropKind::QuestItem { quest, fetch } => {
                    w.u8(7);
                    w.varint(quest.get());
                    w.u8(u8::from(*fetch));
                }
            }
        }
        w.varint(self.focus.map_or(0, |f| f.handle.get()));
        if let Some(focus) = self.focus {
            write_weapon(w, focus.body_hint);
        }
    }

    pub(super) fn read(r: &mut Reader<'_>) -> Result<Self, DecodeError> {
        let n = r.varint()? as usize;
        if n > MAX_VISIBLE_PROPS {
            return Err(DecodeError::TooMany(n));
        }
        let mut rows = Vec::with_capacity(n);
        let mut previous = 0;
        for _ in 0..n {
            let handle = read_handle(r)?;
            if handle.get() <= previous {
                return Err(DecodeError::BadField("prop handles"));
            }
            previous = handle.get();
            let (x, y) = (r.u16()?, r.u16()?);
            let kind = match r.u8()? {
                1 => WirePropKind::Ground(weapon(r.u8()?)?),
                2 => WirePropKind::Chest {
                    open: bool_flag(r)?,
                },
                3 => WirePropKind::Body { label: r.str()? },
                4 => WirePropKind::Corpse { label: r.str()? },
                5 => WirePropKind::WindEmblem,
                6 => WirePropKind::Cannon {
                    ready_at: r.varint()?,
                },
                7 => WirePropKind::QuestItem {
                    quest: QuestHandle::new(r.varint()?)
                        .ok_or(DecodeError::BadField("quest handle"))?,
                    fetch: bool_flag(r)?,
                },
                _ => return Err(DecodeError::BadField("prop kind")),
            };
            rows.push(WireProp { handle, x, y, kind });
        }
        let focus = match PickupHandle::new(r.varint()?) {
            None => None,
            Some(handle) => Some(WireFocus {
                handle,
                body_hint: read_weapon(r)?,
            }),
        };
        let result = Self { rows, focus };
        result
            .check()
            .map_err(|_| DecodeError::BadField("prop focus"))?;
        Ok(result)
    }
}

impl WireField {
    /// The row's checks in the decoders' order (docs/backlog/packets/P-FROST-WALL-2.md F4), as the refusal reason: an
    /// unknown kind or tiles on a row that is not a wall "field"; a wall's tile count "wall tiles", a tile off every map
    /// "wall tile", tiles not strictly ascending "wall order"; then the common checks "field".
    pub(super) fn check(&self) -> Result<(), &'static str> {
        if self.kind >= FIELD_KIND_COUNT || (self.kind != FIELD_KIND_WALL && !self.tiles.is_empty())
        {
            return Err("field");
        }
        if self.kind == FIELD_KIND_WALL {
            if self.tiles.is_empty() || self.tiles.len() > MAX_WALL_TILES {
                return Err("wall tiles");
            }
            if self
                .tiles
                .iter()
                .any(|&t| t as usize >= crate::map::MAX_MAP_TILES)
            {
                return Err("wall tile");
            }
            if !self.tiles.windows(2).all(|p| p[0] < p[1]) {
                return Err("wall order");
            }
        }
        if self.valid() {
            Ok(())
        } else {
            Err("field")
        }
    }
    pub(super) fn valid(&self) -> bool {
        self.kind < FIELD_KIND_COUNT
            && self.ability < 5
            && self.owner != Some(0)
            && self.subject != Some(0)
            && [self.x, self.y, self.x2, self.y2, self.r, self.vx, self.vy]
                .into_iter()
                .all(f32::is_finite)
            && self.r >= 0.
            && self.started_at.is_finite()
            && self.ends_at.is_finite()
            && self.ends_at >= self.started_at
    }
    pub(super) fn len(&self) -> usize {
        48 + self.owner.map_or(0, varint_len)
            + self.subject.map_or(0, varint_len)
            + if self.kind == FIELD_KIND_WALL {
                1 + 4 * self.tiles.len()
            } else {
                0
            }
    }
    pub(super) fn write(&self, w: &mut Writer) {
        w.u8(self.kind);
        w.u8(self.ability);
        w.opt(self.owner.as_ref(), |w, id| w.varint(*id));
        w.opt(self.subject.as_ref(), |w, id| w.varint(*id));
        for value in [self.x, self.y, self.x2, self.y2, self.r, self.vx, self.vy] {
            w.f32(value);
        }
        w.f64(self.started_at);
        w.f64(self.ends_at);
        // A wall row only: its tile list (checked before writing, so the count fits a u8).
        if self.kind == FIELD_KIND_WALL {
            w.u8(self.tiles.len() as u8);
            for &tile in &self.tiles {
                w.u32(tile);
            }
        }
    }
    pub(super) fn read(r: &mut Reader<'_>) -> Result<Self, DecodeError> {
        let mut result = Self {
            kind: r.u8()?,
            ability: r.u8()?,
            owner: r.opt(Reader::varint)?,
            subject: r.opt(Reader::varint)?,
            x: r.f32()?,
            y: r.f32()?,
            x2: r.f32()?,
            y2: r.f32()?,
            r: r.f32()?,
            vx: r.f32()?,
            vy: r.f32()?,
            started_at: r.f64()?,
            ends_at: r.f64()?,
            tiles: Vec::new(),
        };
        // The row is read whole first: an unknown kind is refused before any tile list would be read.
        if result.kind >= FIELD_KIND_COUNT {
            return Err(DecodeError::BadField("field"));
        }
        if result.kind == FIELD_KIND_WALL {
            // Count before tiles, and the whole list against what the frame still holds before reading any tile.
            let count = r.u8()? as usize;
            if count == 0 || count > MAX_WALL_TILES {
                return Err(DecodeError::BadField("wall tiles"));
            }
            if count * 4 > r.remaining() {
                return Err(DecodeError::Truncated);
            }
            result.tiles.reserve_exact(count);
            for _ in 0..count {
                result.tiles.push(r.u32()?);
            }
        }
        result
            .check()
            .map(|()| result)
            .map_err(DecodeError::BadField)
    }
}
