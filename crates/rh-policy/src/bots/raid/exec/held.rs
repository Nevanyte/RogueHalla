//! The hp a RaidBot executor holds when it prices a fight (P-RAID-E-leave): hp share, the potions held and the heals
//! still running, one count for Sustain, Farm and Elite so a drink does not change the price of the fight it was drunk
//! for. The frame carries how many heals run and when the last ends, not their shares: each executor keeps those in its
//! own `HotWatch` (fed in its `observe`), so no percept field is needed. Nothing here draws or reads a clock.
use crate::bots::raid::values::SustainPrices;

/// Hp share held: the share, plus the potions held, plus what the heals already running will still give back (a drunk
/// potion leaves the stock at once while the sim returns its share over `heal_seconds`). `hots` are the shares of the
/// heals running, newest last; `healing_until` (server ms) is when the newest ends, so the share still to come is read
/// from its time left and reaches 0 as the heal ends (the hp itself has risen by then: the sum is continuous). With
/// sustain off: the hp share alone (as `potion_hp` gives 0).
#[allow(clippy::too_many_arguments)]
pub fn hp_held(
    s: &SustainPrices,
    hp_share: f64,
    large: u8,
    small: u8,
    healing: u8,
    healing_until: Option<f64>,
    now: f64,
    hots: &[f64],
) -> f64 {
    if !s.on {
        return hp_share;
    }
    let mut held = hp_share + s.potion_hp(large, small);
    if healing > 0 {
        if let Some(until) = healing_until {
            let to_come = ((until - now) / (s.heal_seconds * 1_000.)).clamp(0., 1.);
            let n = usize::from(healing).min(hots.len());
            held += hots[hots.len() - n..].iter().sum::<f64>() * to_come;
        }
    }
    held
}

/// The shares of the heals running, kept from the stock of potions between frames (a stock that fell by one is a drink
/// of that size). At most one entry per heal the frame says runs; cleared when none runs. After a `reset` the heals
/// already running read as nothing to come.
#[derive(Default)]
pub struct HotWatch {
    /// Potions held the frame before (large, small).
    prev: (u8, u8),
    hots: Vec<f64>,
}

impl HotWatch {
    /// Read one frame's potion stock and the count of heals running.
    pub fn watch(&mut self, s: &SustainPrices, large: u8, small: u8, healing: u8) {
        if large < self.prev.0 {
            self.hots.push(s.large_share);
        } else if small < self.prev.1 {
            self.hots.push(s.small_share);
        }
        self.prev = (large, small);
        let keep = usize::from(healing);
        if self.hots.len() > keep {
            self.hots.drain(..self.hots.len() - keep);
        }
    }

    /// The shares of the heals running, newest last.
    pub fn shares(&self) -> &[f64] {
        &self.hots
    }
}
