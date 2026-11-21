//! Seating: a join (new seat, reconnect or takeover), a leave, and the expiry of reconnect leases.

use super::*;

impl Room {
    pub(super) fn join(
        &mut self,
        profile: String,
        faction: Option<FactionId>,
        outbox: Arc<Outbox>,
        conn: u64,
        fence: i64,
        lease: Option<(Admission, tokio::sync::watch::Receiver<Instant>)>,
    ) -> Result<JoinOk, RejectCode> {
        if self.draining {
            return Err(RejectCode::ServerDraining);
        }
        // A training room seats exactly one human (the practice console is a solo room; there is no second seat to fill).
        if self.training && !self.humans.is_empty() {
            return Err(RejectCode::MatchFull);
        }
        if self.sim.phase() != Phase::Lobby {
            return Err(RejectCode::MatchFull);
        }
        // A human takes a bot's seat when the lobby is full (legacy: the newest bot yields).
        if self.sim.players().len() >= rh_sim::constants::MAX_PLAYERS {
            self.yield_newest_bot();
        }
        let player = self
            .sim
            .add_player(&profile, faction, false)
            .ok_or(RejectCode::MatchFull)?;
        self.record(ReplayInput::Join {
            profile: profile.clone(),
            faction,
            bot: false,
            policy: None,
        });
        let session = random16();
        let admission = lease.as_ref().map(|(identity, _)| *identity);
        let mut collections = crate::wire_state::CollectionPublisher::default();
        Self::bind_rate(self.sim.tick_ms(), &outbox, &mut collections);
        self.fences.push(SeatRecord {
            player,
            fence: Some(fence),
            admission,
        });
        // A room that has had a human closes the moment it has none again (see `wake`).
        self.had_human = true;
        self.humans.push(HumanSeat {
            fx: rh_sim::FxProjector::attach(&self.sim, player),
            admission,
            deadline: lease.map(|(_, deadline)| deadline),
            player,
            conn,
            fence,
            profile,
            session,
            outbox: Some(outbox),
            dropped_at: None,
            ack_input: 0,
            input_received: None,
            rtt: RttEstimator::new(),
            carry: Vec::new(),
            items: rh_sim::ItemObserver::new(player),
            collections,
            spare: None,
            result: None,
        });
        if self.fill.is_none() {
            self.fill_bots();
        }
        let _ = self.reports.send(RoomReport::Seated {
            match_id: self.id,
            phase: self.sim.phase(),
            humans: self.humans_present(),
        });
        if let Some(h) = self.humans.last() {
            let _ = self.reports.send(RoomReport::Seat {
                match_id: self.id,
                profile: h.profile.clone(),
                event: SeatEvent::Seated,
            });
        }
        Ok(JoinOk {
            player,
            session,
            welcome: self.welcome(player, session),
            clock: self.clock.clone(),
        })
    }

    /// Expire before processing commands and before stepping queued inputs. A committed result
    /// already ended this human's gameplay; its reliable delivery survives release of the lease.
    pub(super) fn expire_leases(&mut self, now: Instant) {
        let mut i = 0;
        while i < self.humans.len() {
            let h = &self.humans[i];
            if h.deadline.as_ref().is_some_and(|d| *d.borrow() <= now)
                && h.result.is_none()
                && self.sim.player(h.player).is_some_and(|p| !p.resolved)
            {
                let id = h.player;
                if let Some(seat) = self.fences.iter_mut().find(|s| s.player == id) {
                    seat.fence = None; // This forced MIA is not authorized to bank a result.
                }
                self.leave(id, GoneReason::LeaseExpired);
            } else {
                i += 1;
            }
        }
    }

    pub(super) fn leave(&mut self, player: PlayerId, why: GoneReason) {
        if let Some(i) = self.humans.iter().position(|h| h.player == player) {
            let h = self.humans.remove(i);
            if let Some(o) = h.outbox {
                o.close(RejectCode::Kicked);
                self.retire_outbox(&o);
            }
            // Leaving a run one still stands in is MIA: the sim resolves it now and its result is reported with the next
            // step's events; the director holds the profile until it is written.
            let result_pending = self
                .fences
                .iter()
                .any(|seat| seat.player == player && seat.fence.is_some())
                && self.sim.phase() == Phase::Active
                && self.sim.player(player).is_some_and(|p| !p.resolved);
            self.record(ReplayInput::Leave { player });
            self.sim.remove_player(player);
            let _ = self.reports.send(RoomReport::Seat {
                match_id: self.id,
                profile: h.profile.clone(),
                event: SeatEvent::Gone(why),
            });
            let _ = self.reports.send(RoomReport::Left {
                admission: h.admission,
                match_id: self.id,
                profile: h.profile,
                fence: h.fence,
                result_pending,
            });
        }
    }
}
