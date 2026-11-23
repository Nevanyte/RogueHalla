//! The hp a RaidBot executor holds when it prices a fight (P-RAID-E-leave): hp share, the potions held and the heals
//! still running, one count for Sustain, Farm and Elite so a drink does not change the price of the fight it was drunk
//! for. Stub: the implementer replaces both bodies (design decision 4 of the packet).
use crate::bots::raid::values::SustainPrices;

/// Hp share held: the share, plus the potions held, plus what the heals already running will still give back (a drunk
/// potion leaves the stock at once while the sim returns its share over `heal_seconds`). `hots` are the shares of the
/// heals running, newest last.
#[allow(clippy::too_many_arguments)]
pub fn hp_held(
    s: &SustainPrices,
    hp_share: f64,
    large: u8,
    small: u8,
    _healing: u8,
    _healing_until: Option<f64>,
    _now: f64,
    _hots: &[f64],
) -> f64 {
    hp_share + s.potion_hp(large, small)
}

/// The shares of the heals running, kept from the stock of potions between frames (a stock that fell by one is a drink
/// of that size).
#[derive(Default)]
pub struct HotWatch {
    hots: Vec<f64>,
}

impl HotWatch {
    /// Read one frame's potion stock and the count of heals running.
    pub fn watch(&mut self, _s: &SustainPrices, _large: u8, _small: u8, _healing: u8) {}

    /// The shares of the heals running, newest last.
    pub fn shares(&self) -> &[f64] {
        &self.hots
    }
}
