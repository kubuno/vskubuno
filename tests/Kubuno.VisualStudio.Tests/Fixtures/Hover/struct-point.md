
```rust
hoverprobe::shapes
```

```rust
pub struct Point {
    pub x: f64,
    pub(crate) y: f64,
}
```

---

A point in 2-D space.

Use [`Point::new`](https://docs.rs/hoverprobe/0.1.0/hoverprobe/shapes/struct.Point.html#method.new) to build one, or `Point { x, y }`:

```rust
let p = Point::new(1.0, 2.0);
assert_eq!(p.x, 1.0);
```

* **x** grows to the right
* *y* grows downwards