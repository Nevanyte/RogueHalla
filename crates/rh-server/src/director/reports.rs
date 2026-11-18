//! What rooms report back: the report loop, unwinding and freeing profile entries, and waiting for a leaving profile.

use super::*;

impl Director {
    /// Unwinds an admission that took no seat: its room reservation goes, and its own entry (`gen`) is removed at once when
    /// no lease was taken, or stays `Leaving` until the lease give-back lands (so a retry, or another connection for the
    /// profile, waits for the give-back instead of racing it).
    pub(super) fn unwind(&self, key: &ProfileKey, gen: u64, match_id: MatchId, fence: Option<i64>) {
        let mut inner = self.inner.lock_recover();
        // Hosted mode: an admission that took no seat gives the admit back (pending again, same deadline).
        inner.control.restore_gen(gen);
        let own = inner
            .profiles
            .get(key)
            .is_some_and(|s| s.gen == gen && s.match_id == match_id);
        if own {
            if let Some(r) = inner.rooms.get_mut(&match_id) {
                r.reserved = r.reserved.saturating_sub(1);
            }
        }
        match fence {
            None => {
                if own {
                    inner.profiles.remove(key);
                    drop(inner);
                    self.freed.notify_waiters();
                }
            }
            Some(fence) => {
                if let Some(s) = inner.profiles.get_mut(key).filter(|s| s.gen == gen) {
                    s.state = SeatState::Leaving;
                    s.stop_renewing();
                    s.lease = LeaseState::Releasing;
                }
                drop(inner);
                self.give_back(key, match_id, fence, own.then_some(gen));
            }
        }
    }

    /// Moves a `Leaving` entry on (the lock is held): nothing while a result is due or a write is in flight (the write
    /// releases the lease itself); then the lease is given back unless a write already released it, and the entry is
    /// freed when that lands.
    pub(super) fn advance(&self, inner: &mut Inner, key: &ProfileKey) {
        let Some(s) = inner.profiles.get_mut(key) else {
            return;
        };
        if s.state != SeatState::Leaving || s.result_due || s.writes > 0 {
            return;
        }
        match s.lease {
            LeaseState::Held => {
                s.stop_renewing();
                s.lease = LeaseState::Releasing;
                let (fence, gen) = (s.fence, s.gen);
                self.give_back(key, s.match_id, fence, Some(gen));
            }
            LeaseState::Gone => {
                inner.profiles.remove(key);
                self.freed.notify_waiters();
            }
            LeaseState::Releasing => {}
        }
    }

    /// Gives a lease back off the asking task (no-op in memory), then tells `owner`'s entry (if it is still that entry).
    /// Best effort: an unreleased lease only expires later. Releasing by fence is harmless when the lease moved on.
    pub(super) fn give_back(
        &self,
        key: &ProfileKey,
        match_id: MatchId,
        fence: i64,
        owner: Option<u64>,
    ) {
        let Some(d) = self.me.upgrade() else {
            return;
        };
        let key = key.clone();
        tokio::spawn(async move {
            let _ = d.persistence.release_live(&key, match_id, fence).await;
            if let Some(gen) = owner {
                d.free(&key, gen);
            }
        });
    }

    pub(super) async fn report_loop(this: Arc<Self>, mut rx: mpsc::UnboundedReceiver<RoomReport>) {
        while let Some(r) = rx.recv().await {
            match r {
                RoomReport::Left {
                    admission,
                    match_id,
                    profile,
                    fence,
                    result_pending,
                } => {
                    let key =
                        ProfileKey::from_report(&profile, admission.and_then(|a| a.profile_id));
                    let mut inner = this.inner.lock_recover();
                    let seat = inner
                        .profiles
                        .get(&key)
                        .filter(|s| {
                            s.match_id == match_id
                                && s.fence == fence
                                && admission.is_none_or(|a| a.gen == s.gen)
                        })
                        .map(|s| s.state);
                    match seat {
                        // This seat is already being let go (cannot happen twice; ignored rather than released twice).
                        Some(SeatState::Leaving) => {}
                        Some(_) => {
                            if let Some(room) = inner.rooms.get_mut(&match_id) {
                                room.seated = room.seated.saturating_sub(1);
                            }
                            if let Some(s) = inner.profiles.get_mut(&key) {
                                // The profile stays reserved (`Leaving`) until its result is written or, with no result
                                // to come or in flight, until its lease is given back: a quick rejoin must not race the
                                // durable lease.
                                s.state = SeatState::Leaving;
                                s.result_due = result_pending;
                            }
                            this.advance(&mut inner, &key);
                        }
                        // No entry for this seat (never recorded, or another seat's): the fenced give-back is still owed,
                        // and it cannot touch a newer lease.
                        None => {
                            drop(inner);
                            this.give_back(&key, match_id, fence, None);
                        }
                    }
                }
                RoomReport::Result {
                    admission,
                    match_id,
                    profile,
                    bot,
                    bot_spec,
                    bot_choices,
                    fence,
                    result,
                    roster,
                } => {
                    if bot {
                        // Bots never touch profile persistence (legacy rule), but a resolved fill bot's run is the
                        // learning store's record (source BotManager.onResult), with the seat's own choices.
                        if let Some(spec) = &bot_spec {
                            this.record_bot_result(spec, &result, &bot_choices);
                        }
                        continue;
                    }
                    let key =
                        ProfileKey::from_report(&profile, admission.and_then(|a| a.profile_id));
                    // The seat it belongs to counts the write as in flight: its lease is not given back meanwhile.
                    let (fence, owner) = {
                        let mut inner = this.inner.lock_recover();
                        match inner.profiles.get_mut(&key).filter(|s| {
                            s.match_id == match_id
                                && fence.is_none_or(|f| f == s.fence)
                                && admission.is_none_or(|a| a.gen == s.gen)
                        }) {
                            Some(s) => {
                                s.writes += 1;
                                s.result_due = false;
                                (s.fence, Some(s.gen))
                            }
                            None => (fence.unwrap_or(0), None),
                        }
                    };
                    let d = this.clone();
                    // Durable I/O never runs on a room task: retried here with capped backoff for about two minutes
                    // (a database blip must not lose a result), idempotent by (match, profile); the process waits for
                    // in-flight writes before exiting (main.rs, `wait_for_writes`). A refusal for good is not retried.
                    d.pending_writes
                        .fetch_add(1, std::sync::atomic::Ordering::SeqCst);
                    tokio::spawn(async move {
                        // Whether the lease is gone after the write: released by its transaction, or no longer ours.
                        let lease_gone = d
                            .write_result(match_id, &profile, fence, &result, roster, key.id())
                            .await;
                        let mut inner = d.inner.lock_recover();
                        match owner {
                            Some(gen) => {
                                let mut mine = false;
                                if let Some(s) =
                                    inner.profiles.get_mut(&key).filter(|s| s.gen == gen)
                                {
                                    s.writes -= 1;
                                    if lease_gone && s.lease == LeaseState::Held {
                                        s.stop_renewing();
                                        s.lease = LeaseState::Gone;
                                    }
                                    mine = true;
                                }
                                // A profile that left is free once this lands (and gives its lease back first if the
                                // write gave up).
                                if mine {
                                    d.advance(&mut inner, &key);
                                }
                            }
                            None if !lease_gone => {
                                drop(inner);
                                d.give_back(&key, match_id, fence, None);
                            }
                            None => {}
                        }
                        d.pending_writes
                            .fetch_sub(1, std::sync::atomic::Ordering::SeqCst);
                    });
                }
                RoomReport::Closed {
                    match_id,
                    stats,
                    replay,
                    ..
                } => {
                    let mut inner = this.inner.lock_recover();
                    inner.rooms.remove(&match_id);
                    inner.sessions.retain(|_, m| *m != match_id);
                    // A room that closed normally reported every result and every seat's `Left` before this. A crashed
                    // one did not: its seats are let go here, and a result it still owed will never come. Reservations
                    // are their admissions' to unwind (an admission whose reply arrives after this finds the room gone).
                    let mut moved: Vec<ProfileKey> = Vec::new();
                    for (k, s) in inner.profiles.iter_mut() {
                        if s.match_id != match_id {
                            continue;
                        }
                        match s.state {
                            SeatState::Reserved => {}
                            SeatState::Seated => {
                                s.state = SeatState::Leaving;
                                moved.push(k.clone());
                            }
                            SeatState::Leaving if s.result_due => {
                                s.result_due = false;
                                moved.push(k.clone());
                            }
                            SeatState::Leaving => {}
                        }
                    }
                    let freed_seats = moved.len();
                    for k in moved {
                        this.advance(&mut inner, &k);
                    }
                    let stats_crashed = stats.crashed;
                    if stats.crashed {
                        let record = CrashRecord {
                            room_id: crate::control::room_id_hex(match_id),
                            at_unix_ms: std::time::SystemTime::now()
                                .duration_since(std::time::UNIX_EPOCH)
                                .map_or(0, |d| d.as_millis() as u64),
                            ticks: stats.ticks,
                            seats_freed: freed_seats,
                        };
                        inner.rooms_crashed += 1;
                        // One structured line at error level per crash; `rooms_crashed` in /control/health is the alert hook.
                        eprintln!(
                            "[server] ERROR event=room_crashed room={} ticks={} seats_freed={} crashed_total={}",
                            record.room_id, record.ticks, record.seats_freed, inner.rooms_crashed
                        );
                        inner.last_crash = Some(record);
                    }
                    inner.closed.push(stats);
                    if inner.closed.len() > 64 {
                        inner.closed.remove(0);
                    }
                    if let Some(r) = replay {
                        inner.replays.push(*r);
                        if inner.replays.len() > 4 {
                            inner.replays.remove(0);
                        }
                    }
                    let draining = this.lifecycle.draining();
                    inner.control.on_closed(
                        match_id,
                        stats_crashed,
                        draining,
                        tokio::time::Instant::now(),
                    );
                    let empty = inner.rooms.is_empty();
                    drop(inner);
                    if empty {
                        this.lifecycle.rooms_empty();
                    }
                }
                RoomReport::Seated {
                    match_id,
                    phase,
                    humans,
                } => {
                    let mut inner = this.inner.lock_recover();
                    if let Some(r) = inner.rooms.get_mut(&match_id) {
                        // The admission that took this seat stops being a reservation; the others still are.
                        r.reserved = r.reserved.saturating_sub(1);
                        r.seated = humans;
                        r.phase = phase;
                        r.public.on_phase(phase);
                    }
                    inner.control.on_phase(match_id, phase);
                }
                RoomReport::Seat {
                    match_id,
                    profile,
                    event,
                } => {
                    let took = {
                        let mut inner = this.inner.lock_recover();
                        if let Some(r) = inner.rooms.get_mut(&match_id) {
                            r.public.on_seat(&profile, event);
                        }
                        inner.control.on_seat(match_id, &profile, event)
                    };
                    if let Some(took) = took {
                        crate::metrics::process().note_admit_to_seat(took);
                    }
                }
                RoomReport::BotSeat {
                    match_id,
                    name,
                    seated,
                } => {
                    let mut inner = this.inner.lock_recover();
                    if let Some(r) = inner.rooms.get_mut(&match_id) {
                        let bots = &mut r.public.bots;
                        if seated {
                            bots.push(name);
                        } else if let Some(i) = bots.iter().position(|b| *b == name) {
                            bots.remove(i);
                        }
                    }
                }
                RoomReport::Phase { match_id, phase } => {
                    let mut inner = this.inner.lock_recover();
                    if let Some(r) = inner.rooms.get_mut(&match_id) {
                        r.phase = phase;
                        r.public.on_phase(phase);
                    }
                    inner.control.on_phase(match_id, phase);
                }
            }
        }
    }

    /// Never offer this room to an admission again (its lobby ended or its task is gone; its `Closed` report removes it).
    pub(super) fn mark_unjoinable(&self, match_id: MatchId) {
        if let Some(r) = self.inner.lock_recover().rooms.get_mut(&match_id) {
            r.phase = Phase::Active;
        }
    }

    /// A give-back for `gen`'s entry landed: its lease is gone, and the entry is freed (and the admissions waiting for the
    /// profile woken) once nothing else is pending for it: never while a result write is still in flight. An entry a newer
    /// admission owns is not touched.
    pub(super) fn free(&self, key: &ProfileKey, gen: u64) {
        let mut inner = self.inner.lock_recover();
        let mine = match inner.profiles.get_mut(key) {
            Some(s) if s.gen == gen => {
                s.stop_renewing();
                s.lease = LeaseState::Gone;
                true
            }
            _ => false,
        };
        if mine {
            self.advance(&mut inner, key);
        }
        drop(inner);
        self.freed.notify_waiters();
    }

    /// Waits, until `deadline` at the latest, while `key` is a `Leaving` profile. Returns at once for a free profile and
    /// for one with a live seat or an admission in flight (the caller refuses those: concurrent ownership is never
    /// weakened).
    pub(super) async fn wait_while_leaving(
        &self,
        key: &ProfileKey,
        deadline: tokio::time::Instant,
    ) {
        loop {
            // Register for the wake-up before looking, so a free between the look and the wait is not missed.
            let notified = self.freed.notified();
            tokio::pin!(notified);
            notified.as_mut().enable();
            let leaving = self
                .inner
                .lock_recover()
                .profiles
                .get(key)
                .is_some_and(|s| s.state == SeatState::Leaving);
            if !leaving || tokio::time::timeout_at(deadline, notified).await.is_err() {
                return;
            }
        }
    }
}
