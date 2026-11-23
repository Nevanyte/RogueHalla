//! `balance_eval`: the command line of `rh_headless::balance_model`.
//! `--report PATH --inputs PATH`; JSON on stdout, table on stderr; exit 2 on a usage error, 1 on an unreadable or
//! unparseable file.
use rh_headless::balance_model::{evaluate_json, render_table, to_json};

fn main() {
    let mut report = None;
    let mut inputs = None;
    let mut args = std::env::args().skip(1);
    while let Some(a) = args.next() {
        match a.as_str() {
            "--report" => report = args.next(),
            "--inputs" => inputs = args.next(),
            _ => usage(),
        }
    }
    let (Some(report), Some(inputs)) = (report, inputs) else {
        usage()
    };
    let read = |p: &str| {
        std::fs::read_to_string(p).unwrap_or_else(|e| {
            eprintln!("balance_eval: cannot read {p}: {e}");
            std::process::exit(1);
        })
    };
    match evaluate_json(&read(&report), &read(&inputs)) {
        Ok(e) => {
            println!("{}", to_json(&e));
            eprint!("{}", render_table(&e));
        }
        Err(e) => {
            eprintln!("balance_eval: {e}");
            std::process::exit(1);
        }
    }
}

fn usage() -> ! {
    eprintln!("usage: balance_eval --report PATH --inputs PATH");
    std::process::exit(2);
}
