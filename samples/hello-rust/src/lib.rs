//! Tiny fixture used to manually verify the Kubuno Visual Studio extension: open this folder as a
//! workspace (`devenv /rootsuffix Exp samples\hello-rust`), open `src/main.rs`, and check that it
//! is colorized (TextMate grammar, works even before rust-analyzer is ready) and that
//! rust-analyzer starts against this crate's `Cargo.toml` as its workspace root (Output window,
//! "Kubuno" pane).

/// Returns a greeting for `name`.
///
/// # Examples
///
/// ```
/// assert_eq!(hello_rust::greet("Kubuno"), "Hello, Kubuno!");
/// ```
pub fn greet(name: &str) -> String {
    format!("Hello, {name}!")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn greet_includes_the_name() {
        assert_eq!(greet("Kubuno"), "Hello, Kubuno!");
    }
}
