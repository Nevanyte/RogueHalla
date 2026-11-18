//! Camp rows and the build summary panel (State keys 6 and 13): HUD constants, rewards, wards and mob bonuses.

use super::*;

/// The fixed-point unit of every HUD number on the wire: 1e-4 (a fraction 0.0025 is 25, 1.25 points is 12500).
pub const HUD_QUANTUM: f64 = 1e-4;
/// The most "Mob bonuses" rows a build summary carries (the 20 normal mob types of the farm table).
pub const MAX_SUMMARY_MOB_ROWS: usize = 20;
/// The nine GAINED stats in the legacy strip order (`buildModel.ts` `buildGains`); also a camp reward's stat. Codes:
/// 0 Offense, 1 Soul Offense, 2 Damage, 3 Attack speed, 4 Life steal, 5 Health, 6 Defense, 7 Soul Defense,
/// 8 Move speed.
pub const HUD_STAT_COUNT: u8 = 9;

/// A camp's per-kill reward: the HUD stat code (`< HUD_STAT_COUNT`) and the amount in [`HUD_QUANTUM`] units (`> 0`):
/// a fraction for Damage/Attack speed/Life steal/Health(% max hp)/Move speed, points for the flat four.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct WireCampReward {
    pub stat: u8,
    pub amount: u16,
}

/// A live camp ward as this viewer sees it: `yours` or another player's, and its expiry in server ms.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct WireWard {
    pub yours: bool,
    pub expires_at: f64,
}

/// One "Mob bonuses" row of the owner's build summary: mob type code, HUD stat code, stacks (`>= 1`) and the
/// cumulative total in [`HUD_QUANTUM`] units.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct WireMobBonusRow {
    pub mob_type: u8,
    pub stat: u8,
    pub stacks: u32,
    pub total: i32,
}

/// State key 13 (P-WIRE17-HUD-1): the owning seat's GAINED strip. `gains` in HUD stat code order, each in
/// [`HUD_QUANTUM`] units (fractions for 2,3,4,8; points for 0,1,5,6,7); `mob_rows` strictly ascending by `mob_type`,
/// at most [`MAX_SUMMARY_MOB_ROWS`].
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct WireBuildSummary {
    pub gains: [i32; 9],
    pub mob_rows: Vec<WireMobBonusRow>,
    /// P-WIRE17-HUD-2: the owner's champion panel values, strictly ascending by code, at most [`MAX_PANEL_VALUES`].
    /// Appended after the mob rows: count u8, then rows of code u8 (`< PANEL_CODE_COUNT`) and value i32 LE in
    /// [`HUD_QUANTUM`] units.
    pub panel: Vec<WirePanelValue>,
}

/// P-WIRE17-HUD-2: how many champion panel codes exist (`rh_sim::build_summary::PanelCode`; append only).
pub const PANEL_CODE_COUNT: u8 = 28;
/// P-WIRE17-HUD-2: the most panel values a build summary carries (one per code).
pub const MAX_PANEL_VALUES: usize = 28;

/// P-WIRE17-HUD-2: one champion panel number: its code and its value in [`HUD_QUANTUM`] units.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct WirePanelValue {
    pub code: u8,
    pub value: i32,
}
impl WireBuildSummary {
    pub(super) fn len(&self) -> usize {
        36 + 1
            + self
                .mob_rows
                .iter()
                .map(|r| 2 + varint_len(r.stacks) + 4)
                .sum::<usize>()
            + 1
            + self.panel.len() * 5
    }
    /// The first fault in the row lists (count caps are checked by the caller first): per mob row, mob code, strict mob
    /// order, stat code, stacks; then per panel row, code, strict code order.
    pub(super) fn fault(&self) -> Option<&'static str> {
        let mut previous: Option<u8> = None;
        for row in &self.mob_rows {
            if row.mob_type >= 30 {
                return Some("summary mob code");
            }
            if previous.is_some_and(|p| row.mob_type <= p) {
                return Some("summary mob order");
            }
            previous = Some(row.mob_type);
            if row.stat >= HUD_STAT_COUNT {
                return Some("summary stat code");
            }
            if row.stacks == 0 {
                return Some("summary stacks");
            }
        }
        let mut previous: Option<u8> = None;
        for row in &self.panel {
            if row.code >= PANEL_CODE_COUNT {
                return Some("summary panel code");
            }
            if previous.is_some_and(|p| row.code <= p) {
                return Some("summary panel order");
            }
            previous = Some(row.code);
        }
        None
    }
    pub(super) fn write(&self, w: &mut Writer) {
        for g in self.gains {
            w.u32(g as u32);
        }
        w.u8(self.mob_rows.len() as u8);
        for row in &self.mob_rows {
            w.u8(row.mob_type);
            w.u8(row.stat);
            w.varint(row.stacks);
            w.u32(row.total as u32);
        }
        w.u8(self.panel.len() as u8);
        for row in &self.panel {
            w.u8(row.code);
            w.u32(row.value as u32);
        }
    }
    pub(super) fn read(r: &mut Reader<'_>) -> Result<Self, DecodeError> {
        let mut gains = [0i32; 9];
        for g in &mut gains {
            *g = r.u32()? as i32;
        }
        let n = usize::from(r.u8()?);
        if n > MAX_SUMMARY_MOB_ROWS {
            return Err(DecodeError::TooMany(n));
        }
        let mut mob_rows = Vec::with_capacity(n);
        for _ in 0..n {
            mob_rows.push(WireMobBonusRow {
                mob_type: r.u8()?,
                stat: r.u8()?,
                stacks: r.varint()?,
                total: r.u32()? as i32,
            });
        }
        let n = usize::from(r.u8()?);
        if n > MAX_PANEL_VALUES {
            return Err(DecodeError::TooMany(n));
        }
        let mut panel = Vec::with_capacity(n);
        for _ in 0..n {
            panel.push(WirePanelValue {
                code: r.u8()?,
                value: r.u32()? as i32,
            });
        }
        let v = Self {
            gains,
            mob_rows,
            panel,
        };
        match v.fault() {
            None => Ok(v),
            Some(why) => Err(DecodeError::BadField(why)),
        }
    }
}

impl WireCamp {
    pub(super) fn valid(&self) -> bool {
        self.mob_type < 30
            && self.elite.is_none_or(|v| v < 30)
            && self.alive <= 512
            && self.state <= 2
            && self.cleared_at.is_finite()
            && self.cleared_at >= 0.
            && self.respawn_at.is_finite()
            && self.respawn_at >= self.cleared_at
            && self
                .reward
                .is_none_or(|r| r.stat < HUD_STAT_COUNT && r.amount > 0)
            && self
                .ward
                .is_none_or(|w| w.expires_at.is_finite() && w.expires_at >= 0.)
    }
    pub(super) fn len(&self) -> usize {
        19 + varint_len(self.alive)
            + 1
            + usize::from(self.reward.is_some()) * 2
            + 1
            + usize::from(self.ward.is_some()) * 8
    }
    pub(super) fn write(&self, w: &mut Writer) {
        w.u8(self.mob_type);
        w.u8(self.elite.map_or(0, |v| v + 1));
        w.varint(self.alive);
        w.u8(self.state);
        w.f64(self.cleared_at);
        w.f64(self.respawn_at);
        match self.reward {
            None => w.u8(0),
            Some(r) => {
                w.u8(1 + r.stat);
                w.u16(r.amount);
            }
        }
        match self.ward {
            None => w.u8(0),
            Some(ward) => {
                w.u8(if ward.yours { 1 } else { 2 });
                w.f64(ward.expires_at);
            }
        }
    }
    pub(super) fn read(r: &mut Reader<'_>) -> Result<Self, DecodeError> {
        let mob_type = r.u8()?;
        let elite = r.u8()?.checked_sub(1);
        let v = Self {
            mob_type,
            elite,
            alive: r.varint()?,
            state: r.u8()?,
            cleared_at: r.f64()?,
            respawn_at: r.f64()?,
            reward: match r.u8()? {
                0 => None,
                code if code <= HUD_STAT_COUNT => Some(WireCampReward {
                    stat: code - 1,
                    amount: r.u16()?,
                }),
                _ => return Err(DecodeError::BadField("camp reward")),
            },
            ward: match r.u8()? {
                0 => None,
                v @ (1 | 2) => Some(WireWard {
                    yours: v == 1,
                    expires_at: r.f64()?,
                }),
                _ => return Err(DecodeError::BadField("camp ward")),
            },
        };
        if v.valid() {
            Ok(v)
        } else {
            Err(DecodeError::BadField("camp"))
        }
    }
}
