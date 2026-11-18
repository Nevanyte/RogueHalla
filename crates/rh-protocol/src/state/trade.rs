//! The trade view (State key 10): offers, stats, merchants and their strict validation.

use super::*;

/// Closed native trader catalog code: Barter 0, Stats 1.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum WireTradeKind {
    Barter,
    Stats,
}
impl WireTradeKind {
    pub const fn code(self) -> u8 {
        match self {
            Self::Barter => 0,
            Self::Stats => 1,
        }
    }
    pub const fn from_code(code: u8) -> Option<Self> {
        match code {
            0 => Some(Self::Barter),
            1 => Some(Self::Stats),
            _ => None,
        }
    }
}

/// Closed native stat code in source order.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum WireTradeStat {
    MaxHp,
    Ad,
    Ap,
    AttackSpeed,
    MoveSpeed,
    Armor,
    Shield,
}
impl WireTradeStat {
    pub const fn code(self) -> u8 {
        match self {
            Self::MaxHp => 0,
            Self::Ad => 1,
            Self::Ap => 2,
            Self::AttackSpeed => 3,
            Self::MoveSpeed => 4,
            Self::Armor => 5,
            Self::Shield => 6,
        }
    }
    pub const fn from_code(code: u8) -> Option<Self> {
        match code {
            0 => Some(Self::MaxHp),
            1 => Some(Self::Ad),
            2 => Some(Self::Ap),
            3 => Some(Self::AttackSpeed),
            4 => Some(Self::MoveSpeed),
            5 => Some(Self::Armor),
            6 => Some(Self::Shield),
            _ => None,
        }
    }
}

#[derive(Clone, Debug, PartialEq)]
pub struct WireTradeOffer {
    pub cost: WireTradeStat,
    pub cost_value: f64,
    pub gain: WireTradeStat,
    pub gain_value: f64,
    pub label: String,
}
impl WireTradeOffer {
    pub(super) fn valid(&self) -> bool {
        self.cost != self.gain
            && self.cost_value.is_finite()
            && self.cost_value > 0.0
            && self.gain_value.is_finite()
            && self.gain_value > 0.0
    }
    pub(super) fn len(&self) -> usize {
        18 + str_len(&self.label)
    }
    pub(super) fn write(&self, w: &mut Writer) {
        w.u8(self.cost.code());
        w.f64(self.cost_value);
        w.u8(self.gain.code());
        w.f64(self.gain_value);
        w.str(&self.label);
    }
    pub(super) fn read(r: &mut Reader<'_>) -> Result<Self, DecodeError> {
        let cost = WireTradeStat::from_code(r.u8()?).ok_or(DecodeError::BadField("trade stat"))?;
        let cost_value = r.f64()?;
        let gain = WireTradeStat::from_code(r.u8()?).ok_or(DecodeError::BadField("trade stat"))?;
        let gain_value = r.f64()?;
        let label = r.str()?;
        let offer = Self {
            cost,
            cost_value,
            gain,
            gain_value,
            label,
        };
        if offer.valid() {
            Ok(offer)
        } else {
            Err(DecodeError::BadField("trade offer"))
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq)]
pub struct WireTradeDelta {
    pub stat: WireTradeStat,
    pub value: f64,
}

#[derive(Clone, Debug, PartialEq)]
pub struct WireMerchant {
    pub kind: WireTradeKind,
    pub x: f64,
    pub y: f64,
    pub name: String,
}
impl WireMerchant {
    pub(super) fn valid(&self) -> bool {
        let limit = f64::from(crate::quant::POS_MAX);
        self.x.is_finite()
            && self.y.is_finite()
            && (0.0..=limit).contains(&self.x)
            && (0.0..=limit).contains(&self.y)
    }
    pub(super) fn len(&self) -> usize {
        17 + str_len(&self.name)
    }
    pub(super) fn write(&self, w: &mut Writer) {
        w.u8(self.kind.code());
        w.f64(self.x);
        w.f64(self.y);
        w.str(&self.name);
    }
    pub(super) fn read(r: &mut Reader<'_>) -> Result<Self, DecodeError> {
        let kind = WireTradeKind::from_code(r.u8()?).ok_or(DecodeError::BadField("trade kind"))?;
        let merchant = Self {
            kind,
            x: r.f64()?,
            y: r.f64()?,
            name: r.str()?,
        };
        if merchant.valid() {
            Ok(merchant)
        } else {
            Err(DecodeError::BadField("trade merchant"))
        }
    }
}

/// The owner's exact saved trade facts plus point-visible, noncombat merchant locations.
#[derive(Clone, Debug, PartialEq)]
pub struct WireTradeView {
    pub trade_used: bool,
    pub offers: Vec<WireTradeOffer>,
    pub stats: Vec<WireTradeDelta>,
    pub barter_ready_at: Option<f64>,
    pub merchants: Vec<WireMerchant>,
    pub barter_labels: [String; 3],
}
impl WireTradeView {
    pub fn check(&self) -> Result<(), EncodeError> {
        if self.offers.len() > MAX_TRADE_OFFERS {
            return Err(EncodeError::TooMany {
                what: "trade offers",
                n: self.offers.len(),
                max: MAX_TRADE_OFFERS,
            });
        }
        if self.stats.len() > MAX_TRADE_STATS {
            return Err(EncodeError::TooMany {
                what: "trade stats",
                n: self.stats.len(),
                max: MAX_TRADE_STATS,
            });
        }
        if self.merchants.len() > MAX_TRADE_MERCHANTS {
            return Err(EncodeError::TooMany {
                what: "trade merchants",
                n: self.merchants.len(),
                max: MAX_TRADE_MERCHANTS,
            });
        }
        if !self.offers.iter().all(WireTradeOffer::valid) {
            return Err(EncodeError::BadValue("trade offer"));
        }
        if self.stats.iter().any(|row| !row.value.is_finite()) {
            return Err(EncodeError::BadValue("trade delta"));
        }
        if self
            .barter_ready_at
            .is_some_and(|time| !time.is_finite() || time < 0.0)
        {
            return Err(EncodeError::BadValue("trade deadline"));
        }
        if !self.merchants.iter().all(WireMerchant::valid) {
            return Err(EncodeError::BadValue("trade merchant"));
        }
        for offer in &self.offers {
            str_fits("trade offer label", &offer.label)?;
        }
        for merchant in &self.merchants {
            str_fits("trade merchant name", &merchant.name)?;
        }
        for label in &self.barter_labels {
            str_fits("trade barter label", label)?;
        }
        if 1 + self.encoded_len() > MAX_TRADE_VIEW_BYTES {
            return Err(EncodeError::TooLarge {
                len: 1 + self.encoded_len(),
                max: MAX_TRADE_VIEW_BYTES,
            });
        }
        Ok(())
    }
    pub(super) fn encoded_len(&self) -> usize {
        1 + varint_len(self.offers.len() as u32)
            + self.offers.iter().map(WireTradeOffer::len).sum::<usize>()
            + varint_len(self.stats.len() as u32)
            + self.stats.len() * 9
            + 1
            + usize::from(self.barter_ready_at.is_some()) * 8
            + varint_len(self.merchants.len() as u32)
            + self.merchants.iter().map(WireMerchant::len).sum::<usize>()
            + self.barter_labels.iter().map(|s| str_len(s)).sum::<usize>()
    }
    pub(super) fn write(&self, w: &mut Writer) {
        w.u8(u8::from(self.trade_used));
        w.varint(self.offers.len() as u32);
        for offer in &self.offers {
            offer.write(w);
        }
        w.varint(self.stats.len() as u32);
        for stat in &self.stats {
            w.u8(stat.stat.code());
            w.f64(stat.value);
        }
        w.opt(self.barter_ready_at.as_ref(), |w, time| w.f64(*time));
        w.varint(self.merchants.len() as u32);
        for merchant in &self.merchants {
            merchant.write(w);
        }
        for label in &self.barter_labels {
            w.str(label);
        }
    }
    pub(super) fn read(r: &mut Reader<'_>) -> Result<Self, DecodeError> {
        let trade_used = trade_bool(r)?;
        let offers_len = r.varint()? as usize;
        if offers_len > MAX_TRADE_OFFERS {
            return Err(DecodeError::TooMany(offers_len));
        }
        let mut offers = Vec::with_capacity(offers_len);
        for _ in 0..offers_len {
            offers.push(WireTradeOffer::read(r)?);
        }
        let stats_len = r.varint()? as usize;
        if stats_len > MAX_TRADE_STATS {
            return Err(DecodeError::TooMany(stats_len));
        }
        let mut stats = Vec::with_capacity(stats_len);
        for _ in 0..stats_len {
            let stat =
                WireTradeStat::from_code(r.u8()?).ok_or(DecodeError::BadField("trade stat"))?;
            let value = r.f64()?;
            if !value.is_finite() {
                return Err(DecodeError::BadField("trade delta"));
            }
            stats.push(WireTradeDelta { stat, value });
        }
        let barter_ready_at = r.opt(Reader::f64)?;
        if barter_ready_at.is_some_and(|time| !time.is_finite() || time < 0.0) {
            return Err(DecodeError::BadField("trade deadline"));
        }
        let merchants_len = r.varint()? as usize;
        if merchants_len > MAX_TRADE_MERCHANTS {
            return Err(DecodeError::TooMany(merchants_len));
        }
        let mut merchants = Vec::with_capacity(merchants_len);
        for _ in 0..merchants_len {
            merchants.push(WireMerchant::read(r)?);
        }
        let barter_labels = [r.str()?, r.str()?, r.str()?];
        let view = Self {
            trade_used,
            offers,
            stats,
            barter_ready_at,
            merchants,
            barter_labels,
        };
        if 1 + view.encoded_len() > MAX_TRADE_VIEW_BYTES {
            return Err(DecodeError::TooLarge(1 + view.encoded_len()));
        }
        Ok(view)
    }
}

pub(super) fn trade_bool(r: &mut Reader<'_>) -> Result<bool, DecodeError> {
    match r.u8()? {
        0 => Ok(false),
        1 => Ok(true),
        _ => Err(DecodeError::BadField("trade flag")),
    }
}
