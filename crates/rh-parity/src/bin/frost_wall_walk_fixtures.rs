//! cargo run -p rh-parity --bin frost-wall-walk-fixtures [--check]: writes (or checks) the P-FROST-WALL-PREDICT-1 client
//! golden protocol/fixtures/frost_wall_walk_v1.json from the current core.
fn main() {
    let check = std::env::args().any(|a| a == "--check");
    let stale = rh_parity::frost_wall_walk_goldens::write_or_check(check);
    if check {
        if stale.is_empty() {
            println!("frost-wall-walk-fixtures --check: frost_wall_walk_v1 is current");
        } else {
            eprintln!(
                "frost-wall-walk-fixtures --check: stale {stale:?}; run cargo run -p rh-parity --bin frost-wall-walk-fixtures"
            );
            std::process::exit(1);
        }
    } else {
        println!(
            "wrote {:?}",
            rh_parity::frost_wall_walk_goldens::all()
                .iter()
                .map(|(p, _)| *p)
                .collect::<Vec<_>>()
        );
    }
}
