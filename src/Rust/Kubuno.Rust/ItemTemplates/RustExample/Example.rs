//! `$fileinputname$` example (`cargo run --example $fileinputname$`).
//!
//! Cargo discovers every `.rs` file directly under `examples/` automatically - this file is not
//! listed anywhere else (docs/RSPROJ.md's own "Cargo.toml stays the single source of truth" rule).

fn main() {
    println!("$fileinputname$");
}
