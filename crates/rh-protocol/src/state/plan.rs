//! The viewer's private plan and the public arena lifecycle view (State keys 8 and 9), and the inventory clock readers.

use super::*;

impl WirePlanOption {
    pub(super) fn valid(&self) -> bool {
        (1..=5).contains(&self.zone) && self.walk_seconds.is_none_or(|v| v.is_finite() && v >= 0.)
    }
    pub(super) fn len(&self) -> usize {
        varint_len(self.index) + 5 + 1 + self.walk_seconds.map_or(0, |_| 4)
    }
    pub(super) fn write(&self, w: &mut Writer) {
        w.varint(self.index);
        w.u16(self.x);
        w.u16(self.y);
        w.u8(self.zone);
        w.opt(self.walk_seconds.as_ref(), |w, v| w.f32(*v));
    }
    pub(super) fn read(r: &mut Reader<'_>) -> Result<Self, DecodeError> {
        let result = Self {
            index: r.varint()?,
            x: r.u16()?,
            y: r.u16()?,
            zone: r.u8()?,
            walk_seconds: r.opt(Reader::f32)?,
        };
        if result.valid() {
            Ok(result)
        } else {
            Err(DecodeError::BadField("plan option"))
        }
    }
}
impl WirePlan {
    pub(super) fn valid(&self) -> bool {
        self.ends_at.is_finite()
            && self.ends_at >= 0.
            && self.pick.is_none_or(|v| v <= 1)
            && self.boon.is_none_or(|v| v <= 1)
            && self.boons.len() <= 4
            && self.boons.iter().all(|b| str_fits("boon", b).is_ok())
            && self.options.iter().flatten().all(WirePlanOption::valid)
    }
    pub(super) fn len(&self) -> usize {
        8 + self
            .spawns
            .iter()
            .map(|s| 1 + s.map_or(0, varint_len))
            .sum::<usize>()
            + 2
            + 1
            + 1
            + self.boons.iter().map(|b| 1 + b.len()).sum::<usize>()
            + 1
            + 1
            + self
                .options
                .iter()
                .map(|o| 1 + o.as_ref().map_or(0, WirePlanOption::len))
                .sum::<usize>()
    }
    pub(super) fn write(&self, w: &mut Writer) {
        w.f64(self.ends_at);
        for spawn in self.spawns {
            w.opt(spawn.as_ref(), |w, v| w.varint(*v));
        }
        for taken in self.taken {
            w.u8(u8::from(taken));
        }
        w.u8(self.pick.map_or(0, |v| v + 1));
        w.u8(self.boons.len() as u8);
        for boon in &self.boons {
            w.str(boon);
        }
        w.u8(self.boon.map_or(0, |v| v + 1));
        w.u8(u8::from(self.ready));
        for option in self.options {
            w.opt(option.as_ref(), |w, v| v.write(w));
        }
    }
    pub(super) fn read(r: &mut Reader<'_>) -> Result<Self, DecodeError> {
        let ends_at = r.f64()?;
        let spawns = [r.opt(Reader::varint)?, r.opt(Reader::varint)?];
        let taken = [bool_flag(r)?, bool_flag(r)?];
        let code = |v: u8| match v {
            0 => Ok(None),
            1 => Ok(Some(0)),
            2 => Ok(Some(1)),
            _ => Err(DecodeError::BadField("plan pick")),
        };
        let pick = code(r.u8()?)?;
        let n = r.u8()? as usize;
        if n > 4 {
            return Err(DecodeError::TooMany(n));
        }
        let mut boons = Vec::with_capacity(n);
        for _ in 0..n {
            boons.push(r.str()?);
        }
        let boon = code(r.u8()?)?;
        let ready = bool_flag(r)?;
        let options = [r.opt(WirePlanOption::read)?, r.opt(WirePlanOption::read)?];
        let result = Self {
            ends_at,
            spawns,
            taken,
            pick,
            boons,
            boon,
            ready,
            options,
        };
        if result.valid() {
            Ok(result)
        } else {
            Err(DecodeError::BadField("plan"))
        }
    }
}
/// An owner inventory clock (P-WIRE17-HUD-3) is server milliseconds: finite and non-negative, refused otherwise.
pub(super) fn inventory_clock_ok(v: f64) -> bool {
    v.is_finite() && v >= 0.
}
pub(super) fn read_inventory_clock(r: &mut Reader<'_>) -> Result<Option<f64>, DecodeError> {
    match r.opt(Reader::f64)? {
        Some(v) if !inventory_clock_ok(v) => Err(DecodeError::BadField("inventory clock")),
        clock => Ok(clock),
    }
}
impl WireArena {
    pub(super) fn valid(&self) -> bool {
        [self.lobby_ends_at, self.plan_ends_at, self.raid_started_at]
            .into_iter()
            .chain(self.patience_ends_at)
            .chain([self.sudden_death_at])
            .all(|v| v.is_finite() && v >= 0.)
            && self.patience_ends_at.is_none_or(|v| v >= 0.)
            && self.started_with <= 512
            && self.min_players <= MAX_MIN_PLAYERS
    }
    pub(super) fn len(&self) -> usize {
        8 + 8
            + 8
            + 1
            + usize::from(self.patience_ends_at.is_some()) * 8
            + 1
            + 1
            + 8
            + varint_len(self.started_with)
            + 1
    }
    pub(super) fn write(&self, w: &mut Writer) {
        w.f64(self.lobby_ends_at);
        w.f64(self.plan_ends_at);
        w.f64(self.raid_started_at);
        w.opt(self.patience_ends_at.as_ref(), |w, v| w.f64(*v));
        w.u8(u8::from(self.in_coliseum));
        w.u8(u8::from(self.gates_locked));
        w.f64(self.sudden_death_at);
        w.varint(self.started_with);
        w.u8(self.min_players);
    }
    pub(super) fn read(r: &mut Reader<'_>) -> Result<Self, DecodeError> {
        let result = Self {
            lobby_ends_at: r.f64()?,
            plan_ends_at: r.f64()?,
            raid_started_at: r.f64()?,
            patience_ends_at: r.opt(Reader::f64)?,
            in_coliseum: bool_flag(r)?,
            gates_locked: bool_flag(r)?,
            sudden_death_at: r.f64()?,
            started_with: r.varint()?,
            min_players: r.u8()?,
        };
        if result.valid() {
            Ok(result)
        } else {
            Err(DecodeError::BadField("arena"))
        }
    }
}
