//! Match results: writing, spooling and recovering them, and waiting for the writes to land.

use super::*;

impl Director {
    /// Recorded replays of the last closed rooms (private: they hold match secrets).
    pub fn take_replays(&self) -> Vec<rh_sim::replay::Replay> {
        std::mem::take(&mut self.inner.lock_recover().replays)
    }

    /// Writes one result off the room task and says whether the lease is gone afterwards (released by the write's own
    /// transaction, or no longer ours). With a spool the record is made durable first (see `spool.rs`); without one, or
    /// when the spool is full or unwritable, this is the in-memory retry that predates the spool.
    pub(super) async fn write_result(
        self: &Arc<Self>,
        match_id: MatchId,
        profile: &str,
        fence: i64,
        result: &rh_sim::RunResult,
        roster: crate::room::Roster,
        durable_id: Option<i64>,
    ) -> bool {
        #[cfg(feature = "postgres")]
        if let Some((name, rec, spool)) = self
            .spool_result(match_id, profile, fence, result, roster, durable_id)
            .await
        {
            return match self.drive_spooled(&spool, &name, &rec, false).await {
                // The write released the lease itself.
                SpoolEnd::Committed { applied: true } => true,
                // A duplicate: the ordinary fenced give-back releases the lease (a no-op if it moved on).
                SpoolEnd::Committed { applied: false } => false,
                // Refused for good. The lease is deliberately NOT released: a release would make a
                // later operator replay of the dead letter impossible, and an unreleased lease only expires.
                SpoolEnd::DeadLettered => true,
            };
        }
        self.write_unspooled(match_id, profile, fence, result, roster, durable_id)
            .await
    }

    pub(super) async fn write_unspooled(
        self: &Arc<Self>,
        match_id: MatchId,
        profile: &str,
        fence: i64,
        result: &rh_sim::RunResult,
        roster: crate::room::Roster,
        durable_id: Option<i64>,
    ) -> bool {
        let d = self;
        let key_id = durable_id;
        let mut delay = Duration::from_millis(100);
        let mut lease_gone = false;
        for attempt in 0..RESULT_ATTEMPTS {
            match d
                .persistence
                .record_with_id(match_id, profile, fence, result, roster, key_id)
                .await
            {
                Ok(Recorded::Applied) => {
                    d.inner.lock_recover().results_ok += 1;
                    lease_gone = true;
                    break;
                }
                // A duplicate report: the store answers before it looks at the lease, so the lease the
                // caller holds (if any) is still live. It is not marked gone: the seat's ordinary
                // fenced give-back releases it (a no-op when the lease already moved on).
                Ok(Recorded::AlreadyApplied) => {
                    d.inner.lock_recover().results_ok += 1;
                    break;
                }
                Err(RecordError::Refused(e) | RecordError::Permanent(e)) => {
                    eprintln!("[results] {profile}: refused by the store, not retried: {e}");
                    d.inner.lock_recover().results_refused += 1;
                    lease_gone = true;
                    break;
                }
                Err(e) if attempt == RESULT_ATTEMPTS - 1 => {
                    eprintln!("[results] giving up on {profile}: {e}");
                    d.inner.lock_recover().results_failed += 1;
                }
                Err(_) => {
                    tokio::time::sleep(delay).await;
                    delay = (delay * 2).min(Duration::from_secs(5));
                }
            }
        }
        lease_gone
    }

    /// Result-handoff counters for `/control/health` (loopback only; `/info` and its pinned key set are unchanged).
    /// `spooled`: fsynced locally (survives a process restart on this host). `committed`: in PostgreSQL (the only
    /// host-loss-durable point; the same count as `resultsOk` in `/info`).
    pub fn result_metrics(&self) -> serde_json::Value {
        let inner = self.inner.lock_recover();
        serde_json::json!({
            "spooled": inner.results_spooled,
            "committed": inner.results_ok,
            "recovered": inner.results_recovered,
            "refused": inner.results_refused,
            "failed": inner.results_failed,
            "deadLettered": inner.results_dead_lettered,
            "stuckAlerts": inner.results_stuck_alerts,
            "deadOverflow": self.spool_dead_overflow(),
            "spoolFull": inner.spool_full,
            "spoolErrors": inner.spool_errors,
            "spoolPending": self.spool_pending(),
            "spoolDead": self.spool_dead(),
        })
    }

    #[cfg(feature = "postgres")]
    pub(super) fn spool_pending(&self) -> usize {
        self.spool.as_ref().map_or(0, |s| s.stats().pending)
    }
    #[cfg(feature = "postgres")]
    pub(super) fn spool_dead(&self) -> usize {
        self.spool.as_ref().map_or(0, |s| s.stats().dead)
    }
    #[cfg(feature = "postgres")]
    pub(super) fn spool_dead_overflow(&self) -> u64 {
        self.spool
            .as_ref()
            .map_or(0, |s| s.stats().dead_overflow_total)
    }
    #[cfg(not(feature = "postgres"))]
    pub(super) fn spool_dead_overflow(&self) -> u64 {
        0
    }
    #[cfg(not(feature = "postgres"))]
    pub(super) fn spool_pending(&self) -> usize {
        0
    }
    #[cfg(not(feature = "postgres"))]
    pub(super) fn spool_dead(&self) -> usize {
        0
    }

    /// Makes the result durable locally before the first database attempt. `None` means no spool record exists (no
    /// spool, no durable identity, full, or unwritable) and the caller retries in memory only: the result is then in
    /// the wider loss window, which is counted and logged, never hidden.
    #[cfg(feature = "postgres")]
    pub(super) async fn spool_result(
        &self,
        match_id: MatchId,
        profile: &str,
        fence: i64,
        result: &rh_sim::RunResult,
        roster: crate::room::Roster,
        durable_id: Option<i64>,
    ) -> Option<(
        String,
        crate::spool::SpoolRecord,
        Arc<crate::spool::ResultSpool>,
    )> {
        let spool = self.spool.clone()?;
        let id = durable_id?;
        let meta = self.persistence.spool_meta(roster)?;
        let rec = crate::spool::SpoolRecord::new(
            uuid::Uuid::from_bytes(match_id),
            id,
            profile,
            fence,
            meta,
            crate::persist::pg::to_persist(result),
        );
        let name = rec.file_name();
        let (s2, r2) = (spool.clone(), rec.clone());
        let appended = tokio::task::spawn_blocking(move || s2.append(&r2)).await;
        match appended {
            Ok(Ok(_)) => {
                self.inner.lock_recover().results_spooled += 1;
                eprintln!(
                    "[results] spooled match={} profile_id={id} fence={fence} (durable on this host only; not yet committed)",
                    rec.match_id
                );
                Some((name, rec, spool))
            }
            Ok(Err(crate::spool::SpoolError::Full)) => {
                self.inner.lock_recover().spool_full += 1;
                eprintln!(
                    "[results] spool FULL: match={} profile_id={id} is retried in memory only",
                    rec.match_id
                );
                None
            }
            other => {
                self.inner.lock_recover().spool_errors += 1;
                let why = match other {
                    Ok(Err(e)) => e.to_string(),
                    Err(e) => e.to_string(),
                    Ok(Ok(_)) => unreachable!(),
                };
                eprintln!("[results] spool append failed ({why}): match={} profile_id={id} is retried in memory only", rec.match_id);
                None
            }
        }
    }

    /// Replays one spooled record through the fenced store path until it commits or is refused for good. A TRANSIENT
    /// error is retried forever at `backoff_max` (a database outage of any length must not lose a result; the profile
    /// gate stays held, which costs nothing because admission needs the same database). `retry_deadline` is only an
    /// alert threshold: past it the record is logged and counted as stuck, repeatedly. The record is removed only after
    /// the commit (or a definitive `AlreadyApplied`); a definitive refusal ([`RecordError::Refused`] or
    /// [`RecordError::Permanent`]) is dead-lettered. Never acquires, renews or releases a lease.
    #[cfg(feature = "postgres")]
    pub(super) async fn drive_spooled(
        &self,
        spool: &Arc<crate::spool::ResultSpool>,
        name: &str,
        rec: &crate::spool::SpoolRecord,
        recovered: bool,
    ) -> SpoolEnd {
        let started = tokio::time::Instant::now();
        let alert_every = spool.cfg.retry_deadline.max(Duration::from_millis(1));
        let mut next_alert = started + alert_every;
        let mut delay = spool.cfg.backoff_initial;
        loop {
            let attempt = {
                let _permit = self.spool_permits.acquire().await.expect("semaphore");
                self.persistence.record_spooled(rec).await
            };
            let dead = |reason: String| {
                let (s, n) = (spool.clone(), name.to_owned());
                async move {
                    let r = reason.clone();
                    match tokio::task::spawn_blocking(move || s.dead_letter(&n, &r)).await {
                        Ok(Ok(out)) => out,
                        Ok(Err(e)) => {
                            eprintln!("[results] could not dead-letter record: {e}");
                            crate::spool::DeadOutcome::Overflow
                        }
                        Err(_) => crate::spool::DeadOutcome::Overflow,
                    }
                }
            };
            match attempt {
                Ok(done) => {
                    let (s, n) = (spool.clone(), name.to_owned());
                    if let Ok(Err(e)) = tokio::task::spawn_blocking(move || s.remove(&n)).await {
                        // The result IS committed; a leftover record is replayed as AlreadyApplied and cleared.
                        eprintln!("[results] committed but could not remove its spool record: {e}");
                    }
                    {
                        let mut inner = self.inner.lock_recover();
                        inner.results_ok += 1;
                        if recovered {
                            inner.results_recovered += 1;
                        }
                    }
                    eprintln!(
                        "[results] committed match={} profile_id={} ({}{})",
                        rec.match_id,
                        rec.profile_id,
                        if done == Recorded::Applied {
                            "applied"
                        } else {
                            "already applied"
                        },
                        if recovered {
                            ", recovered from spool"
                        } else {
                            ""
                        }
                    );
                    return SpoolEnd::Committed {
                        applied: done == Recorded::Applied,
                    };
                }
                Err(e @ (RecordError::Refused(_) | RecordError::Permanent(_))) => {
                    let permanent = matches!(e, RecordError::Permanent(_));
                    let why = match &e {
                        RecordError::Refused(w) => format!("refused by the store: {w}"),
                        RecordError::Permanent(w) => format!("permanent store error: {w}"),
                        RecordError::Transient(_) => unreachable!(),
                    };
                    let out = dead(why.clone()).await;
                    {
                        let mut inner = self.inner.lock_recover();
                        if permanent {
                            inner.results_failed += 1;
                        } else {
                            inner.results_refused += 1;
                        }
                        if out == crate::spool::DeadOutcome::Moved {
                            inner.results_dead_lettered += 1;
                        }
                    }
                    eprintln!(
                        "[results] DEAD-LETTERED match={} profile_id={}: {why}; never retried{}",
                        rec.match_id,
                        rec.profile_id,
                        if out == crate::spool::DeadOutcome::Overflow {
                            " (dead-letter directory full or unwritable: the record stays pending)"
                        } else {
                            ""
                        }
                    );
                    return SpoolEnd::DeadLettered;
                }
                Err(RecordError::Transient(e)) => {
                    let now = tokio::time::Instant::now();
                    if now >= next_alert {
                        next_alert = now + alert_every;
                        self.inner.lock_recover().results_stuck_alerts += 1;
                        eprintln!(
                            "[results] STUCK match={} profile_id={}: still not committed after {}s ({e}); keeps retrying every {}s at most, never dead-lettered for time",
                            rec.match_id,
                            rec.profile_id,
                            started.elapsed().as_secs(),
                            spool.cfg.backoff_max.as_secs().max(1)
                        );
                    }
                    tokio::time::sleep(delay).await;
                    delay = (delay * 2).min(spool.cfg.backoff_max);
                }
            }
        }
    }

    /// Startup recovery: replays every spooled record left by an earlier process through the SAME fenced path with its
    /// original fence, identity and metadata. Call once, before the listener accepts admissions. Each profile with a
    /// pending record is held `Leaving` (no lease state) until its records resolve, so an admission in this process cannot
    /// race the replay; recovery itself takes, renews and releases no lease. Records the store refuses (a newer fence,
    /// a released lease) are dead-lettered, never retried forever. Returns how many records were found. Blocking scan,
    /// then background tasks: a database outage does not delay startup or any tick.
    #[cfg(feature = "postgres")]
    pub fn recover_spooled(self: &Arc<Self>) -> usize {
        let Some(spool) = self.spool.clone() else {
            return 0;
        };
        let records = spool.scan();
        let dead_on_scan = spool.stats().dead_total;
        if dead_on_scan > 0 {
            self.inner.lock_recover().results_dead_lettered += dead_on_scan;
        }
        if !records.is_empty() {
            eprintln!(
                "[results] recovering {} spooled result(s) from {}",
                records.len(),
                spool.dir().display()
            );
        }
        let n = records.len();
        for (name, rec) in records {
            let key = ProfileKey::Durable(rec.profile_id);
            let match_id = rec.match_uuid().map(|u| *u.as_bytes()).unwrap_or([0; 16]);
            let gen = {
                let mut inner = self.inner.lock_recover();
                inner.gens += 1;
                let fresh = inner.gens;
                let seat = inner.profiles.entry(key.clone()).or_insert(ProfileSeat {
                    renewal: None,
                    gen: fresh,
                    match_id,
                    fence: rec.fence,
                    state: SeatState::Leaving,
                    result_due: false,
                    writes: 0,
                    lease: LeaseState::Gone,
                });
                seat.writes += 1;
                seat.gen
            };
            let spool = spool.clone();
            let d = self.clone();
            d.pending_writes
                .fetch_add(1, std::sync::atomic::Ordering::SeqCst);
            tokio::spawn(async move {
                d.drive_spooled(&spool, &name, &rec, true).await;
                let mut inner = d.inner.lock_recover();
                let mut mine = false;
                if let Some(s) = inner.profiles.get_mut(&key).filter(|s| s.gen == gen) {
                    s.writes -= 1;
                    mine = true;
                }
                if mine {
                    d.advance(&mut inner, &key);
                }
                drop(inner);
                d.pending_writes
                    .fetch_sub(1, std::sync::atomic::Ordering::SeqCst);
            });
        }
        n
    }

    /// Waits (at most `max`) until no result write is in flight. Called before the process exits.
    pub async fn wait_for_writes(&self, max: Duration) -> bool {
        let t0 = tokio::time::Instant::now();
        while self
            .pending_writes
            .load(std::sync::atomic::Ordering::SeqCst)
            > 0
        {
            if t0.elapsed() >= max {
                return false;
            }
            tokio::time::sleep(Duration::from_millis(50)).await;
        }
        true
    }
}
