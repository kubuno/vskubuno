//! Run with `cargo run --example greet -- <name>`.

fn main() {
    let name = std::env::args().nth(1).unwrap_or_else(|| "world".to_string());
    println!("{}", hello_rust::greet(&name));
}
