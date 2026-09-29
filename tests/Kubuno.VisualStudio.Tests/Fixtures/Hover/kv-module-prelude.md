
```rust
kubuno_views
```

```rust
pub mod prelude
```

---

`use kubuno_views::prelude::*;`: what a view's code-behind uses (EVT-4).
What a view's code-behind uses, in one import: `use kubuno_views::prelude::*;`
(`vskubuno/docs/EVENTS.md` §5.4). The Kubuno templates start with it, and
`kubuno/createHandler` adds it when it writes a typed handler into a file without it.

It brings the view-model traits ([`ViewModel`](https://docs.rs/kubuno_views/0.1.0-alpha/kubuno_views/binding/trait.ViewModel.html), [`Value`](https://docs.rs/kubuno_views/0.1.0-alpha/kubuno_views/binding/enum.Value.html)), the typed-handler vocabulary
([`event_handlers`](https://docs.rs/kubuno_views_macros/0.1.0-alpha/kubuno_views_macros/macro.event_handlers.html), [`Sender`](https://docs.rs/kubuno_views/0.1.0-alpha/kubuno_views/events/sender/struct.Sender.html), [`ElementRef`](https://docs.rs/kubuno_views/0.1.0-alpha/kubuno_views/events/sender/struct.ElementRef.html), [`EventArgs`](https://docs.rs/kubuno_views/0.1.0-alpha/kubuno_views/events/trait.EventArgs.html), every
standard args type), the control types a sender is typed with ([`crate::controls`](https://docs.rs/kubuno_views/0.1.0-alpha/kubuno_views/controls/index.html)), and
the legacy [`HandlerTable`](https://docs.rs/kubuno_views/0.1.0-alpha/kubuno_views/binding/struct.HandlerTable.html) / [`handlers!`](https://docs.rs/kubuno_views/0.1.0-alpha/kubuno_views/binding/macro.handlers.html).