//! Command-line flag helpers shared by every settings parser.

/// The value of `name` when given: `Ok(None)` when absent, an error when the flag has no value after it.
pub(crate) fn flag_value(args: &[String], name: &str) -> Result<Option<String>, String> {
    match args.iter().position(|a| a == name) {
        None => Ok(None),
        Some(i) => args
            .get(i + 1)
            .filter(|v| !v.starts_with("--"))
            .cloned()
            .map(Some)
            .ok_or_else(|| format!("{name} requires a value")),
    }
}

pub(crate) fn arg(args: &[String], k: &str) -> Option<String> {
    args.iter()
        .position(|a| a == k)
        .and_then(|i| args.get(i + 1).cloned())
}

/// Deliberate server-binary opt-in for ordinary human Wish input. Library callers that set `Mode::Full` directly are
/// already explicit; the server's shipped default stays Off.
pub(crate) fn wish_full_enabled(args: &[String]) -> Result<bool, String> {
    let count = args.iter().filter(|a| a.as_str() == "--wish-full").count();
    if args
        .iter()
        .any(|a| a.starts_with("--wish-full=") || a.starts_with("--wish-full-"))
    {
        return Err("--wish-full is a flag without a value".into());
    }
    if count > 1 {
        return Err("--wish-full must occur once".into());
    }
    if let Some(index) = args.iter().position(|a| a == "--wish-full") {
        if args.get(index + 1).is_some_and(|a| !a.starts_with("--")) {
            return Err("--wish-full takes no value".into());
        }
    }
    Ok(count == 1)
}
