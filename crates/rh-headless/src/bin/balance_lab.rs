//! `balance_lab`: the command line of `rh_headless::balance_lab`. JSON report on stdout, table on stderr; exit 2 on a usage
//! error. See `tools/balance-lab/README.md`.
use rh_headless::balance_lab;

fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let opts = match balance_lab::parse_args(&args) {
        Ok(o) => o,
        Err(e) => {
            eprintln!("balance_lab: {e}");
            std::process::exit(2);
        }
    };
    match balance_lab::run(&opts) {
        Ok(out) => {
            eprint!("{}", out.table);
            println!("{}", out.json);
        }
        Err(e) => {
            eprintln!("balance_lab: {e}");
            std::process::exit(1);
        }
    }
}
