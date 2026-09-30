
```rust
fn
```

---

A function or function pointer.

Functions are the primary way code is executed within Rust. Function blocks, usually just
called functions, can be defined in a variety of different places and be assigned many
different attributes and modifiers.

Standalone functions that just sit within a module not attached to anything else are common,
but most functions will end up being inside [`impl`](https://doc.rust-lang.org/stable/std/keyword.impl.html) blocks, either on another type itself, or
as a trait impl for that type.

```rust
fn standalone_function() {
    // code
}

pub fn public_thing(argument: bool) -> String {
    // code
}

struct Thing {
    foo: i32,
}

impl Thing {
    pub fn new() -> Self {
        Self {
            foo: 42,
        }
    }
}
```

In addition to presenting fixed types in the form of `fn name(arg: type, ..) -> return_type`,
functions can also declare a list of type parameters along with trait bounds that they fall
into.

```rust
fn generic_function<T: Clone>(x: T) -> (T, T, T) {
    (x.clone(), x.clone(), x.clone())
}

fn generic_where<T>(x: T) -> T
    where T: std::ops::Add<Output = T> + Copy
{
    x + x + x
}
```

Declaring trait bounds in the angle brackets is functionally identical to using a `where`
clause. It's up to the programmer to decide which works better in each situation, but `where`
tends to be better when things get longer than one line.

Along with being made public via `pub`, `fn` can also have an [`extern`](https://doc.rust-lang.org/stable/std/keyword.extern.html) added for use in
FFI.

For more information on the various types of functions and how they're used, consult the [Rust
book](https://doc.rust-lang.org/stable/book/ch03-03-how-functions-work.html) or the [Reference](https://doc.rust-lang.org/stable/reference/items/functions.html).