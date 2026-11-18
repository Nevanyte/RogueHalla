//! Value comparison and source-JSON field readers shared by every probe step.
use super::*;
pub(super) fn keys(v: &V, allowed: &[&str]) -> R<()> {
    let o = v.as_object().ok_or("expected object")?;
    for k in o.keys() {
        if !allowed.contains(&k.as_str()) {
            return Err(format!("unknown input key {k}"));
        }
    }
    Ok(())
}
pub(super) fn s(v: &V) -> R<&str> {
    v.as_str().ok_or("expected string".into())
}
pub(super) fn n(v: &V) -> R<f64> {
    v.as_f64()
        .filter(|v| v.is_finite())
        .ok_or("expected finite number".into())
}
pub(super) fn parse<T: serde::de::DeserializeOwned>(v: &V) -> R<T> {
    serde_json::from_value(v.clone()).map_err(|e| e.to_string())
}
pub(super) fn undef() -> V {
    json!({"$undefined":true})
}
pub(super) fn equal(a: &V, b: &V, path: &str) -> R<()> {
    if let (Some(a), Some(b)) = (a.as_f64(), b.as_f64()) {
        if a == b {
            return Ok(());
        }
    }
    match (a, b) {
        (V::Array(a), V::Array(b)) if a.len() == b.len() => {
            for (i, (a, b)) in a.iter().zip(b).enumerate() {
                equal(a, b, &format!("{path}/{i}"))?
            }
            Ok(())
        }
        (V::Object(a), V::Object(b)) if a.keys().eq(b.keys()) => {
            for (k, a) in a {
                equal(a, &b[k], &format!("{path}/{k}"))?
            }
            Ok(())
        }
        _ if a == b => Ok(()),
        _ => Err(format!("{path}: native {a} != source {b}")),
    }
}
