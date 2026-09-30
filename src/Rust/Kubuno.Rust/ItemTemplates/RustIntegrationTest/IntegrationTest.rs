//! Integration test for the `$safeprojectname$` crate - runs against its public API only (Cargo
//! compiles everything under `tests/` as its own external crate), the same way
//! `Kubuno.TestAdapter`/Test Explorer discover and run any other Cargo test (README.md).

#[test]
fn it_works() {
    // Replace with a real assertion against $safeprojectname$'s public API, e.g.:
    // assert_eq!($safeprojectname$::greet("Kubuno"), "Hello, Kubuno!");
    assert_eq!(2 + 2, 4);
}
