//! The stderr table of a `balance_lab` report: the same numbers as the JSON, for reading. Pure text, no clock.
use super::Report;
use std::fmt::Write;

fn mean(sum: f64, n: u64) -> String {
    if n == 0 {
        "-".to_string()
    } else {
        format!("{:.1}", sum / n as f64)
    }
}

pub fn render(r: &Report) -> String {
    let mut t = String::new();
    let ticks: u64 = r.seeds.iter().map(|s| u64::from(s.ticks)).sum();
    let bots = r.seating.iter().filter(|s| s.bot).count();
    let _ = writeln!(
        t,
        "balance_lab {} content {}: {} seeds, {} ticks, {}/{} seats bot:true",
        r.schema,
        r.content_hash,
        r.seeds.len(),
        ticks,
        bots,
        r.seating.len()
    );
    let w = &r.world;
    let d = &w.player_deaths;
    let _ = writeln!(
        t,
        "world: camps cleared {} (credited {}); kill s {} (n {}), heal s {} (n {}), walk s {} (n {})",
        w.camps_cleared,
        w.camps_credited,
        mean(w.kill_secs.sum, w.kill_secs.n),
        w.kill_secs.n,
        mean(w.heal_secs.sum, w.heal_secs.n),
        w.heal_secs.n,
        mean(w.walk_secs.sum, w.walk_secs.n),
        w.walk_secs.n
    );
    let _ = writeln!(
        t,
        "deaths: {} (player {}, elite {}, mob {}, hazard {}); hits on players {} of which by a mob id {}",
        d.total,
        d.by_cause.player,
        d.by_cause.elite,
        d.by_cause.mob,
        d.by_cause.hazard,
        d.hits_on_players,
        d.hits_on_players_by_mob
    );
    for (m, v) in &d.by_minute {
        let _ = writeln!(
            t,
            "  minute {m:>2}: {:>6.1} player-min, {} deaths (player {}, elite {}, mob {}, hazard {})",
            v.player_ticks as f64 / 1200.,
            v.total,
            v.by_cause.player,
            v.by_cause.elite,
            v.by_cause.mob,
            v.by_cause.hazard
        );
    }
    for (m, v) in &w.minutes {
        let stacks: u64 = v.farm_stacks.values().sum();
        let _ = writeln!(
            t,
            "  sample minute {m:>2}: {} players, {} farm stacks, {} native cards",
            v.samples,
            stacks,
            v.native.len()
        );
    }
    let _ = writeln!(
        t,
        "elites (kind/weapon: attempts killed other died elsewhere left open, kill s min-max, chest killer/other/none/open):"
    );
    for ((kind, weapon), c) in &r.elites.by_kind_weapon {
        let range = c
            .kill_secs
            .samples
            .iter()
            .fold(None, |r: Option<(f64, f64)>, &x| {
                Some(r.map_or((x, x), |(lo, hi)| (lo.min(x), hi.max(x))))
            })
            .map_or("-".to_string(), |(lo, hi)| format!("{lo:.1}-{hi:.1}"));
        let _ = writeln!(
            t,
            "  {kind}/{weapon}: {} {} {} {} {} {} {}, {range}, {}/{}/{}/{}",
            c.attempts,
            c.killed,
            c.taken_by_other,
            c.died,
            c.died_elsewhere,
            c.left,
            c.open,
            c.chest_killer,
            c.chest_other,
            c.chest_none,
            c.chest_open
        );
    }
    let _ = writeln!(
        t,
        "quests (dealt accepted attempts done timer death abandoned open unpaid, mean s to complete):"
    );
    for (name, c) in &r.quests.by_quest {
        let _ = writeln!(
            t,
            "  {name}: {} {} {} {} {} {} {} {} {}, {}",
            c.dealt,
            c.accepted,
            c.attempts,
            c.completed,
            c.ended_timer,
            c.ended_death,
            c.abandoned,
            c.open,
            c.turned_in_unpaid,
            mean(c.complete_secs.sum, c.complete_secs.n)
        );
    }
    let _ = writeln!(
        t,
        "rewards: chests offered {}, picks seen {:?}, ChestTaken events {}",
        r.rewards.chests_offered, r.rewards.picks_by_kind, r.rewards.chest_taken_events
    );
    let _ = writeln!(
        t,
        "augments (offered picked held_by_winner), winners {}:",
        r.augments.winners
    );
    for (name, c) in &r.augments.by_card {
        let _ = writeln!(
            t,
            "  {name}: {} {} {}",
            c.offered, c.picked, c.held_by_winner
        );
    }
    t
}
