
```rust
alloc::string
```

```rust
pub struct String {
    vec: Vec<u8>,
}
```

---

A UTF-8–encoded, growable string.

`String` is the most common string type. It has ownership over the contents
of the string, stored in a heap-allocated buffer (see [Representation](https://doc.rust-lang.org/stable/alloc/string/struct.String.html#representation)).
It is closely related to its borrowed counterpart, the primitive [`str`].

# Examples

You can create a `String` from [a literal string](https://doc.rust-lang.org/stable/alloc/str/index.html) with [`String::from`]:

```rust
let hello = String::from("Hello, world!");
```

You can append a [`char`](https://doc.rust-lang.org/nightly/core/primitive.char.html) to a `String` with the [`push`] method, and
append a [`&str`] with the [`push_str`] method:

```rust
let mut hello = String::from("Hello, ");

hello.push('w');
hello.push_str("orld!");
```

If you have a vector of UTF-8 bytes, you can create a `String` from it with
the [`from_utf8`] method:

```rust
// some bytes, in a vector
let sparkle_heart = vec![240, 159, 146, 150];

// We know these bytes are valid, so we'll use `unwrap()`.
let sparkle_heart = String::from_utf8(sparkle_heart).unwrap();

assert_eq!("💖", sparkle_heart);
```

# UTF-8

`String`s are always valid UTF-8. If you need a non-UTF-8 string, consider
[`OsString`](https://doc.rust-lang.org/stable/std/ffi/struct.OsString.html). It is similar, but without the UTF-8 constraint. Because UTF-8
is a variable width encoding, `String`s are typically smaller than an array of
the same `char`s:

```rust
// `s` is ASCII which represents each `char` as one byte
let s = "hello";
assert_eq!(s.len(), 5);

// A `char` array with the same contents would be longer because
// every `char` is four bytes
let s = ['h', 'e', 'l', 'l', 'o'];
let size: usize = s.into_iter().map(|c| size_of_val(&c)).sum();
assert_eq!(size, 20);

// However, for non-ASCII strings, the difference will be smaller
// and sometimes they are the same
let s = "💖💖💖💖💖";
assert_eq!(s.len(), 20);

let s = ['💖', '💖', '💖', '💖', '💖'];
let size: usize = s.into_iter().map(|c| size_of_val(&c)).sum();
assert_eq!(size, 20);
```

This raises interesting questions as to how `s[i]` should work.
What should `i` be here? Several options include byte indices and
`char` indices but, because of UTF-8 encoding, only byte indices
would provide constant time indexing. Getting the `i`th `char`, for
example, is available using [`chars`]:

```rust
let s = "hello";
let third_character = s.chars().nth(2);
assert_eq!(third_character, Some('l'));

let s = "💖💖💖💖💖";
let third_character = s.chars().nth(2);
assert_eq!(third_character, Some('💖'));
```

Next, what should `s[i]` return? Because indexing returns a reference
to underlying data it could be `&u8`, `&[u8]`, or something similar.
Since we're only providing one index, `&u8` makes the most sense but that
might not be what the user expects and can be explicitly achieved with
[`as_bytes()`]:

```rust
// The first byte is 104 - the byte value of `'h'`
let s = "hello";
assert_eq!(s.as_bytes()[0], 104);
// or
assert_eq!(s.as_bytes()[0], b'h');

// The first byte is 240 which isn't obviously useful
let s = "💖💖💖💖💖";
assert_eq!(s.as_bytes()[0], 240);
```

Due to these ambiguities/restrictions, indexing with a `usize` is simply
forbidden:

```rust
let s = "hello";

// The following will not compile!
println!("The first letter of s is {}", s[0]);
```

It is more clear, however, how `&s[i..j]` should work (that is,
indexing with a range). It should accept byte indices (to be constant-time)
and return a `&str` which is UTF-8 encoded. This is also called "string slicing".
Note this will panic if the byte indices provided are not character
boundaries - see [`is_char_boundary`] for more details. See the implementations
for [`SliceIndex<str>`] for more details on string slicing. For a non-panicking
version of string slicing, see [`get`].

The [`bytes`] and [`chars`] methods return iterators over the bytes and
codepoints of the string, respectively. To iterate over codepoints along
with byte indices, use [`char_indices`].

# Deref

`String` implements <code>
[Deref]\<Target = [str]\></code>, and so inherits all of [`str`]'s
methods. In addition, this means that you can pass a `String` to a
function which takes a [`&str`] by using an ampersand (`&`):

```rust
fn takes_str(s: &str) { }

let s = String::from("Hello");

takes_str(&s);
```

This will create a [`&str`] from the `String` and pass it in. This
conversion is very inexpensive, and so generally, functions will accept
[`&str`]s as arguments unless they need a `String` for some specific
reason.

In certain cases Rust doesn't have enough information to make this
conversion, known as [`Deref`] coercion. In the following example a string
slice [`&'a str`](https://doc.rust-lang.org/stable/alloc/str/index.html) implements the trait `TraitExample`, and the function
`example_func` takes anything that implements the trait. In this case Rust
would need to make two implicit conversions, which Rust doesn't have the
means to do. For that reason, the following example will not compile.

```rust
trait TraitExample {}

impl<'a> TraitExample for &'a str {}

fn example_func<A: TraitExample>(example_arg: A) {}

let example_string = String::from("example_string");
example_func(&example_string);
```

There are two options that would work instead. The first would be to
change the line `example_func(&example_string);` to
`example_func(example_string.as_str());`, using the method [`as_str()`]
to explicitly extract the string slice containing the string. The second
way changes `example_func(&example_string);` to
`example_func(&*example_string);`. In this case we are dereferencing a
`String` to a [`str`], then referencing the [`str`] back to
[`&str`]. The second way is more idiomatic, however both work to do the
conversion explicitly rather than relying on the implicit conversion.

# Representation

A `String` is made up of three components: a pointer to some bytes, a
length, and a capacity. The pointer points to the internal buffer which `String`
uses to store its data. The length is the number of bytes currently stored
in the buffer, and the capacity is the size of the buffer in bytes. As such,
the length will always be less than or equal to the capacity.

This buffer is always stored on the heap.

You can look at these with the [`as_ptr`], [`len`], and [`capacity`]
methods:

```rust
let story = String::from("Once upon a time...");

// Deconstruct the String into parts.
let (ptr, len, capacity) = story.into_raw_parts();

// story has nineteen bytes
assert_eq!(19, len);

// We can re-build a String out of ptr, len, and capacity. This is all
// unsafe because we are responsible for making sure the components are
// valid:
let s = unsafe { String::from_raw_parts(ptr, len, capacity) } ;

assert_eq!(String::from("Once upon a time..."), s);
```

If a `String` has enough capacity, adding elements to it will not
re-allocate. For example, consider this program:

```rust
let mut s = String::new();

println!("{}", s.capacity());

for _ in 0..5 {
    s.push_str("hello");
    println!("{}", s.capacity());
}
```

This will output the following:

```text
0
8
16
16
32
32
```

At first, we have no memory allocated at all, but as we append to the
string, it increases its capacity appropriately. If we instead use the
[`with_capacity`] method to allocate the correct capacity initially:

```rust
let mut s = String::with_capacity(25);

println!("{}", s.capacity());

for _ in 0..5 {
    s.push_str("hello");
    println!("{}", s.capacity());
}
```

We end up with a different output:

```text
25
25
25
25
25
25
```

Here, there's no need to allocate more memory inside the loop.

[`str`]: https://doc.rust-lang.org/stable/alloc/str/index.html
[`String::from`]: https://doc.rust-lang.org/stable/core/convert/trait.From.html#tymethod.from
[`push`]: https://doc.rust-lang.org/stable/alloc/string/struct.String.html#method.push
[`&str`]: https://doc.rust-lang.org/stable/alloc/str/index.html
[`push_str`]: https://doc.rust-lang.org/stable/alloc/string/struct.String.html#method.push_str
[`from_utf8`]: https://doc.rust-lang.org/stable/alloc/string/struct.String.html#method.from_utf8
[`chars`]: str::chars
[`as_bytes()`]: str::as_bytes
[`is_char_boundary`]: str::is_char_boundary
[`SliceIndex<str>`]: https://doc.rust-lang.org/stable/core/slice/index/trait.SliceIndex.html
[`get`]: str::get
[`bytes`]: str::bytes
[`char_indices`]: str::char_indices
[Deref]: https://doc.rust-lang.org/stable/core/ops/deref/trait.Deref.html
[str]: https://doc.rust-lang.org/stable/alloc/str/index.html
[`Deref`]: https://doc.rust-lang.org/stable/core/ops/deref/trait.Deref.html
[`as_str()`]: https://doc.rust-lang.org/stable/alloc/string/struct.String.html#method.as_str
[`as_ptr`]: str::as_ptr
[`len`]: https://doc.rust-lang.org/stable/alloc/string/struct.String.html#method.len
[`capacity`]: https://doc.rust-lang.org/stable/alloc/string/struct.String.html#method.capacity
[`with_capacity`]: https://doc.rust-lang.org/stable/alloc/string/struct.String.html#method.with_capacity