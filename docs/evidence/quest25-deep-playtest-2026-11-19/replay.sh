#!/usr/bin/env bash
# Verify all 25 room recordings using the checkout's Rust server, protocol and simulation.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(git -C "$here" rev-parse --show-toplevel)"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
mkdir "$scratch/src"
cp "$here/replay.rs" "$scratch/src/main.rs"
cp "$repo/Cargo.lock" "$scratch/Cargo.lock"
ln -s "$repo" "$scratch/game"
cat > "$scratch/Cargo.toml" <<'TOML'
[package]
name = "quest25-room-replay"
version = "0.1.0"
edition = "2021"

[dependencies]
rh-protocol = { path = "game/crates/rh-protocol" }
rh-server = { path = "game/crates/rh-server" }
rh-sim = { path = "game/crates/rh-sim" }
serde = { version = "1", features = ["derive"] }
serde_json = "1"
tokio = { version = "1", features = ["macros", "rt", "test-util"] }

[profile.dev.package.rh-sim]
opt-level = 2
TOML
gzip -dc "$here/room-inputs.json.gz" | CARGO_TARGET_DIR="${CARGO_TARGET_DIR:-$repo/target}" \
  cargo run --quiet --manifest-path "$scratch/Cargo.toml"
