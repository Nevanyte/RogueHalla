//! P-BAL-LAB-ELITE-1: the elite fights of a whole match as a pure function of per-step facts. The observer builds one
//! [`EliteStep`] per step and calls [`tally_elites`] at the end; tests call it with hand-written steps. Rules:
//! docs/backlog/packets/P-BAL-LAB-ELITE-1.md (decisions 3 to 8).
use std::collections::{BTreeMap, BTreeSet};

/// A seat idle (no hit dealt or taken against the elite or its camp) for more than this is no longer in the fight.
pub const LEAVE_MS: f64 = 10_000.;

/// One seat dealt a hit to, or took a hit from, an elite or a mob of its camp, in one step.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Contact {
    pub seat: usize,
    /// The elite mob the fight is about (the camp's elite, whichever mob of the camp was hit).
    pub elite: u32,
    /// The elite's camp; negative when it has none.
    pub camp: i32,
    pub kind: String,
    /// The weapon of the hit (read only when it opens the attempt).
    pub weapon: String,
    /// The seat dealt the hit (false: the seat took it).
    pub dealt: bool,
    /// The other party is the elite itself (false: one of its camp's adds).
    pub on_elite: bool,
}

/// A seat died; the attacker is as the lab resolves it (`Death.by`, else the last hit inside the kill-credit window).
#[derive(Clone, Debug, Default, PartialEq)]
pub struct SeatDeath {
    pub seat: usize,
    pub attacker_mob: Option<u32>,
    pub attacker_camp: Option<i32>,
}

/// An elite died; `killer` is the seat with the killing blow (`Death.by`).
#[derive(Clone, Debug, Default, PartialEq)]
pub struct EliteDeath {
    pub elite: u32,
    pub killer: Option<usize>,
}

/// One step of the match.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct EliteStep {
    pub now_ms: f64,
    pub contacts: Vec<Contact>,
    pub seat_deaths: Vec<SeatDeath>,
    pub elite_deaths: Vec<EliteDeath>,
    /// A seat's first pick from the chest of an elite's death: (elite mob id, seat).
    pub chest_picks: Vec<(u32, usize)>,
    /// The chest of this elite's death is gone (expired, or in nobody's observation any more).
    pub chests_gone: Vec<u32>,
}

/// What the attempts of one (kind, weapon) came to.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct EliteTally {
    pub attempts: u64,
    pub killed: u64,
    pub taken_by_other: u64,
    pub died: u64,
    pub died_elsewhere: u64,
    pub left: u64,
    pub open: u64,
    /// Seconds from first contact to the kill, one per `killed`, in kill order.
    pub kill_secs: Vec<f64>,
    pub chest_killer: u64,
    pub chest_other: u64,
    pub chest_none: u64,
    pub chest_open: u64,
    pub chest_bystander_picks: u64,
}

struct Engage {
    start: f64,
    last: f64,
}

struct Open {
    key: (String, String),
}

/// A kill's chest while its outcome is undecided.
struct ChestRec {
    key: (String, String),
    killer: usize,
    killer_picked: bool,
    bystanders: BTreeSet<usize>,
}

#[derive(Default)]
struct State {
    /// (seat, elite) -> the engaged streak (hits dealt or taken against the elite or its camp).
    engaged: BTreeMap<(usize, u32), Engage>,
    /// (seat, elite) -> the attempt, while undecided.
    attempts: BTreeMap<(usize, u32), Open>,
    /// elite -> its camp (negative: none), as last seen in a contact.
    camps: BTreeMap<u32, i32>,
    chests: BTreeMap<u32, ChestRec>,
    out: BTreeMap<(String, String), EliteTally>,
}

impl State {
    fn row(&mut self, key: &(String, String)) -> &mut EliteTally {
        self.out.entry(key.clone()).or_default()
    }

    /// The order inside a step (decision 4): expire, contacts, elite deaths, seat deaths, then the chest facts.
    fn step(&mut self, s: &EliteStep) {
        self.expire(s);
        for c in &s.contacts {
            self.contact(s.now_ms, c);
        }
        for d in &s.elite_deaths {
            self.elite_death(s.now_ms, d);
        }
        for d in &s.seat_deaths {
            self.seat_death(d);
        }
        for &(elite, seat) in &s.chest_picks {
            if let Some(c) = self.chests.get_mut(&elite) {
                if seat == c.killer {
                    c.killer_picked = true;
                } else {
                    c.bystanders.insert(seat);
                }
            }
        }
        for elite in &s.chests_gone {
            if let Some(c) = self.chests.remove(elite) {
                let r = self.row(&c.key);
                r.chest_bystander_picks += c.bystanders.len() as u64;
                if c.killer_picked {
                    r.chest_killer += 1;
                } else if c.bystanders.is_empty() {
                    r.chest_none += 1;
                } else {
                    r.chest_other += 1;
                }
            }
        }
    }

    /// A pair with a contact in this very step is not judged idle: the steps of a real match are 50 ms apart, so a
    /// longer silence only arises in hand-written steps, where the contact itself shows the seat is in the fight.
    fn expire(&mut self, s: &EliteStep) {
        let now = s.now_ms;
        let idle: Vec<(usize, u32)> = self
            .engaged
            .iter()
            .filter(|(k, e)| {
                now - e.last > LEAVE_MS && !s.contacts.iter().any(|c| (c.seat, c.elite) == **k)
            })
            .map(|(k, _)| *k)
            .collect();
        for k in idle {
            self.engaged.remove(&k);
            if let Some(a) = self.attempts.remove(&k) {
                self.row(&a.key).left += 1;
            }
        }
    }

    fn contact(&mut self, now: f64, c: &Contact) {
        let k = (c.seat, c.elite);
        self.camps.insert(c.elite, c.camp);
        self.engaged
            .entry(k)
            .and_modify(|e| e.last = now)
            .or_insert(Engage {
                start: now,
                last: now,
            });
        if c.dealt && c.on_elite && !self.attempts.contains_key(&k) {
            let key = (c.kind.clone(), c.weapon.clone());
            self.row(&key).attempts += 1;
            self.attempts.insert(k, Open { key });
        }
    }

    fn elite_death(&mut self, now: f64, d: &EliteDeath) {
        let seats: Vec<usize> = self
            .attempts
            .keys()
            .filter(|(_, e)| *e == d.elite)
            .map(|(s, _)| *s)
            .collect();
        for seat in seats {
            let Some(a) = self.attempts.remove(&(seat, d.elite)) else {
                continue;
            };
            if d.killer == Some(seat) {
                let start = self.engaged.get(&(seat, d.elite)).map_or(now, |e| e.start);
                let r = self.row(&a.key);
                r.killed += 1;
                r.kill_secs.push((now - start) / 1000.);
                self.chests.insert(
                    d.elite,
                    ChestRec {
                        key: a.key,
                        killer: seat,
                        killer_picked: false,
                        bystanders: BTreeSet::new(),
                    },
                );
            } else {
                self.row(&a.key).taken_by_other += 1;
            }
        }
        self.engaged.retain(|(_, e), _| *e != d.elite);
        self.camps.remove(&d.elite);
    }

    fn seat_death(&mut self, d: &SeatDeath) {
        let fights: Vec<u32> = self
            .attempts
            .keys()
            .filter(|(s, _)| *s == d.seat)
            .map(|(_, e)| *e)
            .collect();
        for elite in fights {
            let Some(a) = self.attempts.remove(&(d.seat, elite)) else {
                continue;
            };
            let camp = self.camps.get(&elite).copied().unwrap_or(-1);
            let fight =
                d.attacker_mob == Some(elite) || (camp >= 0 && d.attacker_camp == Some(camp));
            let r = self.row(&a.key);
            if fight {
                r.died += 1;
            } else {
                r.died_elsewhere += 1;
            }
        }
        self.engaged.retain(|(s, _), _| *s != d.seat);
    }
}

/// The tally of a match, keyed by (elite kind, weapon of the opening hit).
pub fn tally_elites(steps: &[EliteStep]) -> BTreeMap<(String, String), EliteTally> {
    let mut st = State::default();
    for s in steps {
        st.step(s);
    }
    for a in std::mem::take(&mut st.attempts).into_values() {
        st.row(&a.key).open += 1;
    }
    for c in std::mem::take(&mut st.chests).into_values() {
        st.row(&c.key).chest_open += 1;
    }
    st.out
}
