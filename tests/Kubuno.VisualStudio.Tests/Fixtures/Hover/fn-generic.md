
```rust
hoverprobe
```

```rust
fn generic_fn<T, const N: usize>(items: [T; {const}]) -> Vec<T>
where
    T: Clone + fmt::Debug,
    T: Send,
```