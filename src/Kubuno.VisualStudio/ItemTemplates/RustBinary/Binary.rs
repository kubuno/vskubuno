//! `$fileinputname$` binary (`cargo run --bin $fileinputname$`).
//!
//! Cargo discovers every `.rs` file directly under `src/bin/` automatically as its own binary
//! target - this file is not listed anywhere else (docs/RSPROJ.md's own "Cargo.toml stays the
//! single source of truth" rule).

fn main() {
    println!("$fileinputname$");
}
