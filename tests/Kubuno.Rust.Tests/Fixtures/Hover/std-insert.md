
```rust
std::collections::hash::map::HashMap
```

```rust
impl<K, V, S, A> HashMap<K, V, S, A>
pub fn insert(&mut self, k: K, v: V) -> Option<V>
where
    // Bounds from impl:
    K: Eq + Hash,
    S: BuildHasher,
    A: Allocator,
```

---

`K` = `String`, `V` = `Vec<u8>`, `S` = `RandomState`, `A` = `Global`

---

Inserts a key-value pair into the map.

If the map did not have this key present, [`None`](https://doc.rust-lang.org/stable/core/option/enum.Option.html#variant.None) is returned.

If the map did have this key present, the value is updated, and the old
value is returned. The key is not updated, though; this matters for
types that can be `==` without being identical. See the [module-level
documentation](https://doc.rust-lang.org/stable/std/collections/index.html#insert-and-complex-keys) for more.

# Examples

```rust
use std::collections::HashMap;

let mut map = HashMap::new();
assert_eq!(map.insert(37, "a"), None);
assert_eq!(map.is_empty(), false);

map.insert(37, "b");
assert_eq!(map.insert(37, "c"), Some("b"));
assert_eq!(map[&37], "c");
```