
```rust
kubuno_desktop_views::binding
```

```rust
pub trait ViewModel
```

---

The code-behind's state, read and written by path (§3: "a small `Bindable`
trait the code-behind's state struct implements"). A path is whatever the
view model chooses — a field name, as in every example in `XML_VIEWS.md`
§7 (`Notifications`, `SyncIntervalMin`, `Proxy`…) — this trait does not
interpret it.