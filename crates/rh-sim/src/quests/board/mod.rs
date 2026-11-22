//! Ranked quest offers for normal raids. Legacy K1 definitions retain their original indices.
pub(crate) mod attempts;
pub mod catalog;
pub mod map_admission;
pub mod placement;
pub(crate) mod rewards;
pub(crate) mod special;
pub(crate) mod tuning;

use super::placement::Placement;
use crate::private_rng::Sfc32;
use crate::{Secrets, TileMap};

/// Board definition numbers occupy a separate range in immutable giver provenance.
const FIRST_BOARD_DEF: usize = 256;

pub(crate) fn quest(index: usize) -> Option<catalog::QuestId> {
    catalog::QuestId::ALL
        .get(index.checked_sub(FIRST_BOARD_DEF)?)
        .copied()
}

/// Cold, derived match data shared by spawning and checkpoint validation. The private
/// stream is consumed once when dealing the board. Reconnecting never deals another.
#[derive(Clone, Debug)]
pub(crate) struct Deal {
    pub placements: Vec<Placement>,
    pub stream: Sfc32,
}

pub(crate) fn deal(map: &TileMap, secrets: Secrets) -> Result<Deal, String> {
    let mut stream = Sfc32::from_words(super::live::quest_salt(secrets.reward_salt));
    let sites = placement::plan(map, &mut || stream.next_f64())?;
    let placements = sites
        .into_iter()
        .map(|site| Placement {
            def: FIRST_BOARD_DEF + site.quest as usize,
            known: true,
            at: site.giver,
            tokens: site.objectives,
            parcel: None,
        })
        .collect();
    Ok(Deal { placements, stream })
}
