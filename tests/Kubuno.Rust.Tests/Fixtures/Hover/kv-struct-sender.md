
```rust
kubuno_desktop_views::events::sender
```

```rust
pub struct Sender<'a, C>
where
    C: Component,
{
    inner: ElementRef<'a>,
    props: &'a <C as Component>::Resolved,
}
```

---

A typed, read-only sender: the [`ElementRef`](https://docs.rs/kubuno_desktop_views/0.1.0-alpha/kubuno_desktop_views/events/sender/struct.ElementRef.html) plus the element's
resolved properties, reachable directly through `Deref`.

```rust
use kubuno_desktop_views::events::{Component, ElementRef, Sender};

struct Button;
struct ButtonProps { text: String }
impl ButtonProps { fn text(&self) -> &str { &self.text } }
impl Component for Button { const ELEMENT: &'static str = "Button"; type Resolved = ButtonProps; }

let props = ButtonProps { text: "Save".into() };
let sender: Sender<'_, Button> = Sender::new(ElementRef::detached("SaveButton"), &props);
assert_eq!(sender.text(), "Save");               // ((Button)sender).Text
assert_eq!(sender.element().name, Some("SaveButton"));
```