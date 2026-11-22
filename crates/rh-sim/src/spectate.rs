//! Spectating after death: whose sight a player whose run is over sees the match through.
//!
//! Not legacy (the TypeScript game removed a dead player's view): a presentation choice of the live server. It reads the
//! world and never changes it, so it touches no state, hash, checkpoint or replay. The choice itself lives with the seat
//! (`ItemObserver::set_spectate`, `FxProjector::set_eye`); this module only decides whether it applies.

use crate::sim::{PlayerId, Sim, Status};

impl Sim {
    /// The viewer's run ended in death (`Dead` and resolved; a Rebirth Egg still pending is not resolved): it may watch.
    pub fn may_spectate(&self, viewer: PlayerId) -> bool {
        self.player(viewer)
            .is_some_and(|p| p.status == Status::Dead && p.resolved)
    }

    /// The player whose sight `viewer` sees the world through, or `None` for the viewer's own view.
    ///
    /// Only a viewer whose run ended in death (`Dead` and resolved: a Rebirth Egg still pending is not resolved) may
    /// spectate, and only a standing player (alive, not down, not resolved, never the viewer). When `requested` is not
    /// standing, the next standing player after it in id order is watched, wrapping around, so a client that asks for "the
    /// next one" by id always lands on someone while anyone is standing. A revived viewer is `Alive` again and so gets
    /// its own view back at once.
    pub fn spectate_eye(&self, viewer: PlayerId, requested: Option<PlayerId>) -> Option<PlayerId> {
        let requested = requested?;
        if !self.may_spectate(viewer) {
            return None;
        }
        let standing = |id: PlayerId| id != viewer && self.player(id).is_some_and(|p| p.standing());
        if standing(requested) {
            return Some(requested);
        }
        let ids = self
            .players()
            .iter()
            .map(|p| p.id)
            .filter(|&id| standing(id));
        ids.clone()
            .filter(|id| id.0 > requested.0)
            .min_by_key(|id| id.0)
            .or_else(|| ids.min_by_key(|id| id.0))
    }
}

#[cfg(test)]
mod tests;
