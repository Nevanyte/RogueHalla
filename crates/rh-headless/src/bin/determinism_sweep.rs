//! The `determinism_sweep` binary: runs the scenario inventory (`tools/determinism/inventory-v1.json`) in `ci`, `soak`
//! or single `case` mode and reports any divergence between runs. All the work is in `determinism_sweep_support`.
mod determinism_sweep_support;

fn main() {
    if let Err(error) = determinism_sweep_support::main_entry() {
        eprintln!("determinism_sweep: {error}");
        std::process::exit(1);
    }
}
