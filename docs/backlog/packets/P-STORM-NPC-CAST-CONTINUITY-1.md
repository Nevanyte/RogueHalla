# P-STORM-NPC-CAST-CONTINUITY-1 — Preserve a visible combat pose through consecutive NPC observations

**Outcome.** In the Blood Saint’s 1440p encounter, 25 delivered wand warnings and their projectiles were visible. An observer-handle renewal erased the body’s painted attack on the next observation. Native fixed-handle captures missed it.

**Scope.** SpriteActorPresenter’s observation retention and existing creature-art continuity, the ArenaView membership call site, an independent EditMode regression and this record. Preserve only a previously disclosed visual motion when both observations contain exactly one compatible adjacent body. Keep a true visibility loss, ambiguous crossings, weapon changes, concealed actors, deaths and pooled reuse clear. No alias decoding, server changes or locally generated attacks.

**Oracle.** Exercise the ordinary ArenaView snapshot-membership entry point for a visible caster, a hidden tick, a crowd and a displacement. Review the named fight in the Web player, checking the body alongside same-tick public wand events. D1 presentation only. D2 new visual regression. C-OBS and D9 unchanged.
