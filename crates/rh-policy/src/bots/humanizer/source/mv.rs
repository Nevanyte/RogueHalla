//! The sealed corpus's tagged MethodValue codec: the `Mv` tree and its strict accessors.

use super::*;

// ------------------------------------------------------------------------------------------------ tagged MethodValue

/// The sealed corpus's ordered MethodValue codec (`tools/bot-private-brain-fixtures/codec.mts`, `rh-method-value/1`).
/// Only the tags the direct hand supplement declares are accepted; instance/callable/hole/map/set are rejected here (the
/// one declared side observation that carries Maps, `memory`, is shape-checked raw below and never decoded).
#[derive(Clone, Debug, PartialEq)]
pub(super) enum Mv {
    Undefined,
    Null,
    Bool(bool),
    Num(f64),
    Str(String),
    Arr(Vec<Mv>),
    U8(Vec<u8>),
    Obj(Vec<(String, Mv)>),
}

pub(super) fn decode_mv(value: &Value, path: &str) -> Result<Mv, String> {
    match value {
        Value::Null => Ok(Mv::Null),
        Value::Bool(flag) => Ok(Mv::Bool(*flag)),
        Value::Number(number) => number
            .as_f64()
            .filter(|number| number.is_finite())
            .map(Mv::Num)
            .ok_or_else(|| err(path, "expected a finite JSON number")),
        Value::String(text) => Ok(Mv::Str(text.clone())),
        Value::Array(_) => Err(err(path, "raw JSON arrays are not tagged method values")),
        Value::Object(fields) => {
            let tag = fields
                .get("$")
                .and_then(Value::as_str)
                .ok_or_else(|| err(path, "missing $ tag"))?;
            match tag {
                "undefined" => {
                    exact_tag_fields(fields, &["$"], path)?;
                    Ok(Mv::Undefined)
                }
                "number" => {
                    exact_tag_fields(fields, &["$", "value"], path)?;
                    let encoded = fields
                        .get("value")
                        .and_then(Value::as_str)
                        .ok_or_else(|| err(path, "number tag needs a string value"))?;
                    let number = match encoded {
                        "NaN" => f64::NAN,
                        "+Infinity" => f64::INFINITY,
                        "-Infinity" => f64::NEG_INFINITY,
                        "-0" => -0.0,
                        other => return Err(err(path, format!("unknown number tag {other}"))),
                    };
                    Ok(Mv::Num(number))
                }
                "u8" => {
                    exact_tag_fields(fields, &["$", "values"], path)?;
                    let values = fields
                        .get("values")
                        .and_then(Value::as_array)
                        .ok_or_else(|| err(path, "u8 tag needs a values array"))?;
                    let mut bytes = Vec::with_capacity(values.len());
                    for (index, value) in values.iter().enumerate() {
                        let byte = value
                            .as_u64()
                            .filter(|value| *value <= u64::from(u8::MAX))
                            .ok_or_else(|| {
                                err(&format!("{path}.values[{index}]"), "expected a u8")
                            })?;
                        bytes.push(byte as u8);
                    }
                    Ok(Mv::U8(bytes))
                }
                "array" => {
                    exact_tag_fields(fields, &["$", "values"], path)?;
                    let values = fields
                        .get("values")
                        .and_then(Value::as_array)
                        .ok_or_else(|| err(path, "array tag needs a values array"))?;
                    let mut items = Vec::with_capacity(values.len());
                    for (index, value) in values.iter().enumerate() {
                        items.push(decode_mv(value, &format!("{path}[{index}]"))?);
                    }
                    Ok(Mv::Arr(items))
                }
                "object" => {
                    exact_tag_fields(fields, &["$", "entries"], path)?;
                    let entries = fields
                        .get("entries")
                        .and_then(Value::as_array)
                        .ok_or_else(|| err(path, "object tag needs an entries array"))?;
                    let mut members: Vec<(String, Mv)> = Vec::with_capacity(entries.len());
                    for (index, pair) in entries.iter().enumerate() {
                        let pair = pair.as_array().ok_or_else(|| {
                            err(
                                &format!("{path}.entries[{index}]"),
                                "expected a key/value pair",
                            )
                        })?;
                        if pair.len() != 2 {
                            return Err(err(
                                &format!("{path}.entries[{index}]"),
                                "expected [key, value]",
                            ));
                        }
                        let key = pair[0]
                            .as_str()
                            .ok_or_else(|| {
                                err(&format!("{path}.entries[{index}]"), "expected a string key")
                            })?
                            .to_string();
                        if members.iter().any(|(member, _)| *member == key) {
                            return Err(err(
                                &format!("{path}.entries[{index}]"),
                                format!("duplicate object key {key}"),
                            ));
                        }
                        let value = decode_mv(&pair[1], &format!("{path}.{key}"))?;
                        members.push((key, value));
                    }
                    Ok(Mv::Obj(members))
                }
                other => Err(err(path, format!("unsupported method-value tag {other}"))),
            }
        }
    }
}

pub(super) fn exact_tag_fields(
    fields: &serde_json::Map<String, Value>,
    allowed: &[&str],
    path: &str,
) -> Result<(), String> {
    for key in fields.keys() {
        if !allowed.contains(&key.as_str()) {
            return Err(err(path, format!("unexpected tag field {key}")));
        }
    }
    for key in allowed {
        if !fields.contains_key(*key) {
            return Err(err(path, format!("missing tag field {key}")));
        }
    }
    Ok(())
}

impl Mv {
    pub(super) fn obj<'a>(&'a self, path: &str) -> Result<&'a [(String, Mv)], String> {
        match self {
            Mv::Obj(members) => Ok(members),
            _ => Err(err(path, "expected a tagged object")),
        }
    }

    pub(super) fn arr<'a>(&'a self, path: &str) -> Result<&'a [Mv], String> {
        match self {
            Mv::Arr(items) => Ok(items),
            _ => Err(err(path, "expected a tagged array")),
        }
    }

    pub(super) fn get<'a>(&'a self, key: &str, path: &str) -> Result<&'a Mv, String> {
        self.obj(path)?
            .iter()
            .find(|(member, _)| member == key)
            .map(|(_, value)| value)
            .ok_or_else(|| err(path, format!("missing member {key}")))
    }

    pub(super) fn optional<'a>(&'a self, key: &str, path: &str) -> Result<Option<&'a Mv>, String> {
        Ok(self
            .obj(path)?
            .iter()
            .find(|(member, _)| member == key)
            .map(|(_, value)| value))
    }

    /// The object is closed over exactly `keys` (any order; duplicates are rejected by `decode_mv`).
    pub(super) fn exact(&self, keys: &[&str], path: &str) -> Result<(), String> {
        let members = self.obj(path)?;
        for (member, _) in members {
            if !keys.contains(&member.as_str()) {
                return Err(err(path, format!("unexpected member {member}")));
            }
        }
        for key in keys {
            if !members.iter().any(|(member, _)| member == key) {
                return Err(err(path, format!("missing member {key}")));
            }
        }
        Ok(())
    }

    /// The object carries exactly `required` plus any of `optional`.
    pub(super) fn closed(
        &self,
        required: &[&str],
        optional: &[&str],
        path: &str,
    ) -> Result<(), String> {
        let members = self.obj(path)?;
        for (member, _) in members {
            if !required.contains(&member.as_str()) && !optional.contains(&member.as_str()) {
                return Err(err(path, format!("unexpected member {member}")));
            }
        }
        for key in required {
            if !members.iter().any(|(member, _)| member == key) {
                return Err(err(path, format!("missing member {key}")));
            }
        }
        Ok(())
    }

    pub(super) fn num(&self, path: &str) -> Result<f64, String> {
        match self {
            Mv::Num(value) if value.is_finite() => Ok(*value),
            Mv::Num(_) => Err(err(path, "nonfinite declared number")),
            _ => Err(err(path, "expected a number")),
        }
    }

    pub(super) fn uint(&self, path: &str) -> Result<u32, String> {
        match self {
            Mv::Num(value)
                if value.is_finite()
                    && value.fract() == 0.
                    && *value >= 0.
                    && *value <= f64::from(u32::MAX) =>
            {
                Ok(*value as u32)
            }
            _ => Err(err(path, "expected a u32")),
        }
    }

    pub(super) fn i8_number(&self, path: &str) -> Result<i8, String> {
        match self {
            Mv::Num(value)
                if value.is_finite()
                    && value.fract() == 0.
                    && *value >= f64::from(i8::MIN)
                    && *value <= f64::from(i8::MAX) =>
            {
                Ok(*value as i8)
            }
            _ => Err(err(path, "expected an i8")),
        }
    }

    pub(super) fn boolean(&self, path: &str) -> Result<bool, String> {
        match self {
            Mv::Bool(flag) => Ok(*flag),
            _ => Err(err(path, "expected a boolean")),
        }
    }

    pub(super) fn text(&self, path: &str) -> Result<&str, String> {
        match self {
            Mv::Str(text) => Ok(text),
            _ => Err(err(path, "expected a string")),
        }
    }

    pub(super) fn null(&self) -> bool {
        matches!(self, Mv::Null)
    }

    pub(super) fn string_list(&self, path: &str) -> Result<Vec<String>, String> {
        self.arr(path)?
            .iter()
            .enumerate()
            .map(|(index, item)| item.text(&format!("{path}[{index}]")).map(str::to_string))
            .collect()
    }
}
