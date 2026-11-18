//! Id and alias tables that translate source-JSON names into native ids and back.
use super::*;
#[derive(Clone, Default)]
pub(super) struct Ids {
    pub(super) actors: Vec<String>,
    pub(super) fields: Vec<String>,
    pub(super) aliases: BTreeMap<String, u32>,
    pub(super) modifiers: BTreeMap<u32, boundary_v3::Modifiers>,
}
impl Ids {
    pub(super) fn id(&mut self, v: &V) -> R<Option<PlayerId>> {
        let v = s(v)?;
        if v.is_empty() {
            return Ok(None);
        };
        if let Some(i) = self.actors.iter().position(|a| a == v) {
            Ok(Some(PlayerId(i as u32 + 1)))
        } else {
            self.actors.push(v.into());
            Ok(Some(PlayerId(self.actors.len() as u32)))
        }
    }
    pub(super) fn player(&mut self, v: &V) -> R<PlayerId> {
        self.id(v)?.ok_or("empty actor".into())
    }
    pub(super) fn name(&self, id: Option<PlayerId>) -> &str {
        id.map_or("", |id| self.actors[(id.0 - 1) as usize].as_str())
    }
    pub(super) fn field(&mut self, v: &V) -> R<u32> {
        let v = s(v)?;
        if let Some(i) = self.fields.iter().position(|a| a == v) {
            Ok(i as u32 + 1)
        } else {
            self.fields.push(v.into());
            Ok(self.fields.len() as u32)
        }
    }
    pub(super) fn source(&mut self, v: &V) -> R<rt::StatusSource> {
        keys(v, &["id", "name", "ownerId"])?;
        Ok(rt::StatusSource {
            id: self.id(&v["id"])?,
            name: s(&v["name"])?.into(),
            owner_id: v.get("ownerId").map(|v| self.id(v)).transpose()?.flatten(),
        })
    }
    pub(super) fn source_out(&self, v: &rt::StatusSource) -> V {
        let mut out = json!({"id":self.name(v.id),"name":v.name});
        if let Some(id) = v.owner_id {
            out["ownerId"] = json!(self.name(Some(id)));
        }
        out
    }
    pub(super) fn apply(&mut self, v: &V) -> R<(rt::StatusApply, bool)> {
        keys(
            v,
            &[
                "source", "kind", "ms", "ability", "tag", "value", "add", "data", "quiet",
            ],
        )?;
        let data = v.get("data").map(|v| self.payload(v)).transpose()?;
        Ok((
            rt::StatusApply {
                source: self.source(&v["source"])?,
                kind: parse(&v["kind"])?,
                ms: n(&v["ms"])?,
                ability: v.get("ability").map(parse).transpose()?,
                tag: v
                    .get("tag")
                    .map(|v| s(v).map(rt::StatusTag::from))
                    .transpose()?,
                value: v.get("value").map(n).transpose()?,
                add: v.get("add").map(parse).transpose()?,
                data,
            },
            v.get("quiet")
                .map(|v| v.as_bool().ok_or("quiet boolean"))
                .transpose()?
                .unwrap_or(false),
        ))
    }
    pub(super) fn payload(&mut self, v: &V) -> R<rt::Payload> {
        keys(
            v,
            &[
                "revealUntil",
                "fieldId",
                "dealtMult",
                "takenFromSourceMult",
                "moveMult",
                "floorHp",
                "healShare",
                "shares",
            ],
        )?;
        let mut p = rt::Payload::default();
        for (k, v) in v.as_object().unwrap() {
            let bit = match k.as_str() {
                "revealUntil" => 1,
                "fieldId" => 2,
                "dealtMult" => 4,
                "takenFromSourceMult" => 8,
                "moveMult" => 16,
                "floorHp" => 32,
                "healShare" => 64,
                "shares" => 128,
                _ => unreachable!(),
            };
            if *v == undef() {
                p.undefined_fields |= bit;
                continue;
            }
            match k.as_str() {
                "revealUntil" => p.reveal_until = Some(n(v)?),
                "fieldId" => p.field_id = Some(self.field(v)?),
                "dealtMult" => p.dealt_mult = Some(n(v)?),
                "takenFromSourceMult" => p.taken_from_source_mult = Some(n(v)?),
                "moveMult" => p.move_mult = Some(n(v)?),
                "floorHp" => p.floor_hp = Some(n(v)?),
                "healShare" => p.heal_share = Some(n(v)?),
                "shares" => {
                    keys(v, &["phys", "magic", "true"])?;
                    p.shares = Some(rh_sim::content::DamageShares {
                        phys: n(&v["phys"])?,
                        magic: n(&v["magic"])?,
                        true_: n(&v["true"])?,
                    })
                }
                _ => unreachable!(),
            }
        }
        Ok(p)
    }
    pub(super) fn field_input(&mut self, v: &V) -> R<rt::Field> {
        keys(
            v,
            &[
                "kind",
                "ability",
                "owner",
                "subject",
                "x",
                "y",
                "x2",
                "y2",
                "r",
                "vx",
                "vy",
                "startedAt",
                "endsAt",
                "exitStunMs",
            ],
        )?;
        Ok(rt::Field {
            id: 0,
            kind: parse(&v["kind"])?,
            ability: parse(&v["ability"])?,
            owner: self.source(&v["owner"])?,
            subject: if v["subject"].is_null() {
                None
            } else {
                self.id(&v["subject"])?
            },
            x: n(&v["x"])?,
            y: n(&v["y"])?,
            x2: n(&v["x2"])?,
            y2: n(&v["y2"])?,
            r: n(&v["r"])?,
            vx: n(&v["vx"])?,
            vy: n(&v["vy"])?,
            started_at: n(&v["startedAt"])?,
            ends_at: n(&v["endsAt"])?,
            exit_stun_ms: v.get("exitStunMs").map(n).transpose()?,
        })
    }
    pub(super) fn field_out(&self, f: &rt::Field) -> V {
        let mut out = json!({"id":self.fields[(f.id-1)as usize],"kind":f.kind,"ability":f.ability,"owner":self.source_out(&f.owner),"subject":f.subject.map(|id|self.name(Some(id))),"x":f.x,"y":f.y,"x2":f.x2,"y2":f.y2,"r":f.r,"vx":f.vx,"vy":f.vy,"startedAt":f.started_at,"endsAt":f.ends_at});
        if let Some(ms) = f.exit_stun_ms {
            out["exitStunMs"] = json!(ms)
        }
        out
    }
    pub(super) fn fields_out(&self, a: Option<&rt::AbilityRuntime>) -> V {
        json!(a
            .map(|a| a
                .fields
                .iter()
                .map(|f| json!([self.fields[(f.id - 1) as usize], self.field_out(f)]))
                .collect::<Vec<_>>())
            .unwrap_or_default())
    }
    pub(super) fn ability_out(&self, a: Option<&rt::AbilityRuntime>) -> V {
        let Some(a) = a else { return V::Null };
        json!({"fields":self.fields_out(Some(a)),"mirrors":a.fields.iter().map(|f|json!([self.fields[(f.id-1)as usize],{"kind":f.kind as u8,"ability":f.ability as u8,"x":f.x,"y":f.y,"x2":f.x2,"y2":f.y2,"r":f.r,"vx":f.vx,"vy":f.vy,"owner":self.name(f.owner.id),"subject":self.name(f.subject),"startedAt":f.started_at,"endsAt":f.ends_at}])).collect::<Vec<_>>(),"dotSince":a.dot_since,"teleportedAt":a.teleported_at.map_or_else(undef,|v|json!(v)),"dot":a.dot.iter().map(|d|json!([format!("{}|{}|{}",self.name(Some(d.victim)),self.name(d.by),d.ability.as_str()),{"victim":self.name(Some(d.victim)),"by":self.name(d.by),"ability":d.ability,"amount":d.amount}])).collect::<Vec<_>>()})
    }
    pub(super) fn hit(&mut self, v: &V) -> R<ProbeHit> {
        keys(
            v,
            &[
                "by", "victim", "packet", "weapon", "stagger", "dot", "ability", "arena", "sig",
            ],
        )?;
        let p = &v["packet"];
        keys(p, &["raw", "phys", "magic", "true", "crit", "mult"])?;
        Ok(ProbeHit {
            sig: v
                .get("sig")
                .map(|v| match s(v)? {
                    "burn" => Ok(rh_sim::factions::SignatureKind::Burn),
                    "disease" => Ok(rh_sim::factions::SignatureKind::Disease),
                    _ => Err("unsupported signature helper".to_owned()),
                })
                .transpose()?,
            source: self.source(&v["by"])?,
            victim: self.player(&v["victim"])?,
            packet: rh_sim::state::Packet {
                raw: n(&p["raw"])?,
                phys: n(&p["phys"])?,
                magic: n(&p["magic"])?,
                true_: n(&p["true"])?,
            },
            weapon: if v["weapon"].is_null() {
                None
            } else {
                Some(rh_sim::WeaponId::parse(s(&v["weapon"])?).ok_or("weapon")?)
            },
            stagger: v["stagger"].as_bool().ok_or("stagger")?,
            dot: v
                .get("dot")
                .map(|v| v.as_bool().ok_or("dot"))
                .transpose()?
                .unwrap_or(false),
            ability: v.get("ability").map(parse).transpose()?,
            arena: v
                .get("arena")
                .map(|v| v.as_bool().ok_or("arena"))
                .transpose()?
                .unwrap_or(false),
            critical: p
                .get("crit")
                .map(|v| v.as_bool().ok_or("crit"))
                .transpose()?
                .unwrap_or(false),
            critical_mult: p.get("mult").map(n).transpose()?,
        })
    }
    pub(super) fn hit_out(&self, h: &ProbeHit) -> V {
        let mut out = json!({"by":self.source_out(&h.source),"victim":self.name(Some(h.victim)),"packet":{"raw":h.packet.raw,"phys":h.packet.phys,"magic":h.packet.magic,"true":h.packet.true_},"weapon":h.weapon.map(|w|w.as_str()),"stagger":h.stagger,"scale":1});
        if let Some(sig) = h.sig {
            out["sig"] = json!(sig.as_str())
        }
        if h.dot {
            out["dot"] = json!(true)
        }
        if h.arena {
            out["arena"] = json!(true)
        }
        if let Some(a) = h.ability {
            out["ability"] = json!(a)
        }
        if h.critical {
            out["packet"]["crit"] = json!(true)
        }
        if let Some(v) = h.critical_mult {
            out["packet"]["mult"] = json!(v)
        }
        out
    }
    pub(super) fn status(&self, st: &rt::StatusInstance) -> V {
        let mut out = json!({"kind":st.kind,"source":self.source_out(&st.source),"startedAt":st.started_at,"until":st.until,"value":st.value});
        if let Some(a) = st.ability {
            out["ability"] = json!(a)
        }
        if let Some(t) = &st.tag {
            out["tag"] = json!(t.as_str())
        }
        if let Some(v) = st.reveal_from {
            out["revealFrom"] = json!(v)
        }
        if let Some(v) = st.taken {
            out["taken"] = json!(v)
        }
        let p = &st.data;
        for (k, bit, v) in [
            ("revealUntil", 1, p.reveal_until.map(|v| json!(v))),
            (
                "fieldId",
                2,
                p.field_id.map(|i| json!(self.fields[(i - 1) as usize])),
            ),
            ("dealtMult", 4, p.dealt_mult.map(|v| json!(v))),
            (
                "takenFromSourceMult",
                8,
                p.taken_from_source_mult.map(|v| json!(v)),
            ),
            ("moveMult", 16, p.move_mult.map(|v| json!(v))),
            ("floorHp", 32, p.floor_hp.map(|v| json!(v))),
            ("healShare", 64, p.heal_share.map(|v| json!(v))),
            (
                "shares",
                128,
                p.shares
                    .map(|v| json!({"phys":v.phys,"magic":v.magic,"true":v.true_})),
            ),
        ] {
            if let Some(v) = v {
                out[k] = v
            } else if p.undefined_fields & bit != 0 {
                out[k] = undef()
            }
        }
        out
    }
}
pub(super) fn negative_infinity() -> V {
    json!({"$number":"-Infinity"})
}
