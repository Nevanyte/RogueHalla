//! The room's task loop ([`run_room`]) and the panic guard around every call into the room.

use super::*;

/// Runs one call into the room for [`run_room`]. A panic inside it (a simulation assert, a bug) is caught, the room is
/// reported closed ([`Room::crashed`]) and `None` is returned: the task then ends. Only the room's own code is guarded,
/// so a task cancelled while its thread unwinds from someone else's panic reports nothing.
pub(super) fn guarded<T>(room: &mut Room, f: impl FnOnce(&mut Room) -> T) -> Option<T> {
    match std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| f(room))) {
        Ok(v) => Some(v),
        Err(_) => {
            room.crashed();
            None
        }
    }
}

/// The short kind of a seat fault, for the log (never the payload of an error).
pub(super) fn fault_kind(fault: &rh_policy::Fault) -> &'static str {
    use rh_policy::Fault;
    match fault {
        Fault::Input(_) => "input refused",
        Fault::Delivery(_) => "delivery error",
        Fault::FxProjection(_) => "fx projection error",
        Fault::ItemObservation(_) => "item observation error",
        Fault::WorkerStopped => "worker stopped",
        Fault::Panicked => "panicked",
        Fault::OverBudget { .. } => "over budget",
        Fault::Late { .. } => "late",
        Fault::IncompatibleContract { .. } => "incompatible contract",
        Fault::IncompatibleObservation { .. } => "incompatible observation",
    }
}

/// The room's task: commands in arrival order between steps; one wake per grid slot.
///
/// C-TICK note: the select is `biased` with the step deadline first. When a step is due and commands are queued at the same
/// poll, the step runs first and the commands are applied (and acknowledged) after it, so they take effect in the next
/// step's input window. Before, either order was possible and a paused-clock run varied with the runtime's random branch
/// choice (about 1.4% in instructions). Only already-allowed orderings remain: a command arriving a moment after the wake
/// would have met the same order, and replays record the order actually applied. The deadline is one pinned [`Sleep`]
/// that is reset only when the grid's next slot changes, not a new timer per loop turn.
///
/// Overload: under sustained overload (a wake that takes longer than its slot), the next deadline is already past when the
/// select runs again, and the biased select alone would step forever without polling the inbox (joins, leaves and inputs
/// would starve until every client is closed `TooSlow`). So when a wake ends with its next step already due, the commands
/// already queued (at most [`INBOUND_CAP`], one inbox) are applied before that step: at most one wake runs between two
/// looks at the inbox. That is the same order a command arriving during the wake meets in the normal case (it is applied
/// after the step that was running, before the next one), so no new ordering is introduced; in the normal case (the next
/// deadline is still ahead) nothing changes and a due step still runs before a queued command.
///
/// A panic in the room is caught at the call (`guarded`) and reported as `Closed`, marked `crashed`.
///
/// [`Sleep`]: tokio::time::Sleep
pub async fn run_room(mut room: Room, mut rx: mpsc::Receiver<RoomCmd>) {
    let room = &mut room;
    let mut armed = room.next_deadline();
    let sleep = tokio::time::sleep_until(armed);
    tokio::pin!(sleep);
    loop {
        tokio::select! {
            biased;
            _ = &mut sleep => {
                if guarded(room, |r| r.wake(Instant::now())) != Some(true) { return; }
                // Overloaded (the next step is already due): what is queued now is applied before it.
                if room.next_deadline() <= Instant::now() {
                    for _ in 0..INBOUND_CAP {
                        match rx.try_recv() {
                            Ok(cmd) => {
                                if guarded(room, |r| r.handle(cmd, Instant::now())).is_none() || room.is_closed() { return; }
                            }
                            Err(mpsc::error::TryRecvError::Empty) => break,
                            Err(mpsc::error::TryRecvError::Disconnected) => {
                                guarded(room, Room::close);
                                return;
                            }
                        }
                    }
                }
                // The sleep has fired: arm it for the next slot (a wake that ran no step leaves the slot where it was,
                // and re-arming it then behaves as a fresh `sleep_until`, as before).
                armed = room.next_deadline();
                sleep.as_mut().reset(armed);
            }
            cmd = rx.recv() => match cmd {
                Some(cmd) => {
                    if guarded(room, |r| r.handle(cmd, Instant::now())).is_none() || room.is_closed() { return; }
                    let next = room.next_deadline();
                    if next != armed {
                        armed = next;
                        sleep.as_mut().reset(armed);
                    }
                }
                None => { guarded(room, Room::close); return; }
            },
        }
    }
}
