//! Persistent terrain removals and their derived open/locked collision maps.
use super::{Variant, View};
use crate::{barriers::OverlayMap, state::World, statics::MatchStatic, TileMap};
use serde::{Deserialize, Deserializer, Serialize, Serializer};
use std::sync::Arc;

#[derive(Clone, Debug, Default)]
pub(crate) struct Terrain {
    pub(crate) cells: Vec<u32>,
    pub(crate) derived: Option<Arc<OverlayMap>>,
}

impl Serialize for Terrain {
    fn serialize<S: Serializer>(&self, serializer: S) -> Result<S::Ok, S::Error> {
        if serializer.is_human_readable() {
            self.cells.serialize(serializer)
        } else if self.cells.is_empty() {
            serializer.serialize_unit()
        } else {
            serializer.serialize_some(&self.cells)
        }
    }
}

impl<'de> Deserialize<'de> for Terrain {
    fn deserialize<D: Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        let cells = Vec::<u32>::deserialize(deserializer)?;
        Ok(Self {
            cells,
            derived: None,
        })
    }
}

impl PartialEq for Terrain {
    fn eq(&self, other: &Self) -> bool {
        self.cells == other.cells
    }
}

impl Terrain {
    pub(crate) fn rebuild(&mut self, k: &MatchStatic) {
        self.derived = (!self.cells.is_empty()).then(|| {
            Arc::new(OverlayMap {
                fields: Vec::new(),
                open: k.arena.map.without_obstructions(&self.cells),
                locked: k
                    .locked_map
                    .as_ref()
                    .map(|m| m.without_obstructions(&self.cells)),
            })
        });
    }
}

pub(crate) fn view<'a>(k: &'a MatchStatic, w: &'a World) -> View<'a> {
    if let Some(m) = w.destroyed_terrain.derived.as_deref() {
        return View {
            open: &m.open,
            locked: m.locked.as_ref().unwrap_or(&m.open),
            variant: Variant::of(w.rt.gates_locked),
        };
    }
    k.collision_view(w.rt.gates_locked)
}

pub(crate) fn map<'a>(k: &'a MatchStatic, w: &'a World) -> &'a TileMap {
    view(k, w).current()
}

pub(crate) fn open<'a>(k: &'a MatchStatic, w: &'a World) -> &'a TileMap {
    view(k, w).open
}
