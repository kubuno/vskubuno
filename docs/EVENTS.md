# Kubuno Views — event system (WinForms model) — design note

> Scope: product request "a very complete event system modelled on Windows Forms, including the
> abstraction levels that let developers create their own events". This note maps each layer of the
> WinForms event model onto idiomatic Rust in `kubuno-views` (runtime), `kubuno-views-ls` (language
> server) and `vskubuno` (Visual Studio designer). It builds on
> `docs/XML_VIEWS.md` §2/§4/§7 and `docs/DESIGNER.md` §11 (Properties window, ⚡ tab).
>
> **Status (2026-09-29):** EVT-1 to EVT-5 are **done** (see §9, "EVT-1 as built", §10,
> "EVT-2 and EVT-3 as built", §11, "EVT-4 as built" — the attribute is `#[kubuno_views::event_handlers]`,
> see there —, and §12, "EVT-5 as built"); EVT-6: see §13; EVT-7 is split in two: **EVT-7a** (the control
> hierarchy and the overridable `on_…` methods) is done, see §14; EVT-7b (custom controls and user controls
> in XML, tooling) is done, see §15; EVT-7c (WinForms-rich property sets) is §16; **EVT-8** (paint and the
> `Graphics` API, owner-draw, drag and drop, the paint debug overlay) is done, see §17.

## 0. Where we are today

- **Raising.** A node detects an interaction while it paints (the runtime is immediate-mode: the
  compiled `ViewNode` tree persists across frames, the `kubuno_ui` widgets are rebuilt every frame)
  and calls `PaintCx::fire` / `InteractCx::fire` (`kubuno-views/src/node.rs`). `fire` does two things:
  it dispatches `HandlerTable::dispatch(name, vm, value)` when the element declared `On*="name"`, and
  pushes a `ViewEvent { focus_id, handler, kind }` into the `Vec` returned by `Runtime::frame`.
- **Payload.** `ViewEventKind` has three variants (`Clicked`, `Toggled(bool)`, `Changed(String)`); the
  handler gets a single untyped `binding::Value` (`Bool`, `Str`, `F32`…). A selection change encodes
  its index as `Value::F32`; a toolbar item click passes its index the same way.
- **Handlers.** `handlers! { "name" => |vm, value| { … } }` builds a `HashMap<String, Box<dyn
  FnMut(&mut dyn ViewModel, Value)>>`. No sender, no mutable args (no `Handled`/`Cancel`), no
  multicast, no unsubscribe, unknown names are silently ignored.
- **Metadata.** `registry::EventMeta { name, doc }` (+ `doc_fr` in the JSON export). About 40 event
  entries across the families, mostly `OnClick`, `OnChanged`, `OnSelectionChanged`,
  `OnCheckedChanged`, `OnValueChanged`, `OnToggled`, `OnActivate`. There are no pointer, keyboard,
  focus, layout or lifecycle events. The registry is a closed, compiled-in list (`registry::all()`).
- **Tooling.** `kubuno/createHandler` (`kubuno-views-ls/src/handler_insert.rs`) writes the `On*`
  attribute and a `fn name(vm: &mut dyn ViewModel, value: Value)` stub, plus a forwarding entry when
  the file already has a `handlers!` table. The ⚡ tab (`KbviewEventBindingService`) works;
  `GetCompatibleMethods` returns an empty list. The "default event" is simply `Events[0]`
  (`KbviewElementObject`).

The input side is already rich enough: `kubuno_controls::host::Frame` carries all three mouse buttons
(latched so a sub-frame tap is never lost), `click_count`, `wheel`, `mods`, `window_focused`,
`dismiss`, and a per-frame queue of keys and text. The missing piece is a typed event layer on top of
it.

## 1. Event arguments

### WinForms

`EventArgs` is the root. Its subclasses add data (`MouseEventArgs`: Button, Clicks, X, Y, Delta),
writable output (`HandledEventArgs.Handled`, `KeyEventArgs.SuppressKeyPress`), or cancellation
(`CancelEventArgs.Cancel`, from which `FormClosingEventArgs` inherits, adding `CloseReason`).
Every delegate has the shape `(object sender, TEventArgs e)`.

### Rust mapping

Plain structs, one per args kind, plus a small trait and two capability traits:

```rust
pub trait EventArgs: Any {
    fn as_any(&self) -> &dyn Any;
    fn as_any_mut(&mut self) -> &mut dyn Any;
    /// The legacy `Value` the `handlers!` table received for this event (§5.4).
    fn legacy_value(&self) -> Value { Value::Bool(true) }
    /// Ancestor chain for tooling ("MouseEventArgs" -> "EventArgs"), from the derive.
    fn type_chain(&self) -> &'static [&'static str];
}
pub trait Handled { fn handled(&self) -> bool; fn set_handled(&mut self, v: bool); }
pub trait Cancelable { fn cancel(&self) -> bool; fn set_cancel(&mut self, v: bool); }

#[derive(EventArgs, Debug, Clone)]            // generates EventArgs + chain
pub struct MouseEventArgs { pub button: MouseButton, pub clicks: u8,
                            pub x: f32, pub y: f32, pub delta: f32, pub mods: Modifiers }
#[derive(EventArgs)] #[args(handled)]
pub struct KeyEventArgs { pub key: Key, pub mods: Modifiers,
                          pub handled: bool, pub suppress_key_press: bool }
#[derive(EventArgs)] #[args(cancel)]
pub struct FormClosingEventArgs { pub reason: CloseReason, pub cancel: bool }
```

Rust has no struct inheritance, so the hierarchy becomes **composition plus traits**: "is a
`CancelEventArgs`" means "implements `Cancelable`"; the ancestor chain used by tooling for
signature compatibility (§5.3) is declared by the derive (`#[args(extends = "MouseEventArgs")]`).
Coordinates are DIP, relative to the sender's bounds (WinForms: client coordinates of the control).

The standard catalogue (`kubuno-views/src/events/args.rs`):

| Args | Fields | Used by |
|---|---|---|
| `EventArgs` (unit struct) | — | Click, Enter/Leave, GotFocus/LostFocus, Load, Shown, Activated… |
| `MouseEventArgs` | button, clicks, x, y, delta, mods | MouseDown/Up/Move/Click/DoubleClick/Wheel |
| `KeyEventArgs` | key, mods, handled, suppress_key_press | KeyDown, KeyUp |
| `KeyPressEventArgs` | key_char, handled | KeyPress |
| `PaintEventArgs<'a>` | canvas `&'a dyn Canvas`, clip `Rect` | Paint (custom controls, `<Canvas>`) |
| `CancelEventArgs` | cancel | Validating, TabSelecting… |
| `FormClosingEventArgs` | reason, cancel | FormClosing |
| `FormClosedEventArgs` | reason | FormClosed |
| `DragEventArgs` | data `DataObject`, allowed, effect (mut), x, y, mods | DragEnter/Over/Drop |
| `ScrollEventArgs` | kind `ScrollEventType`, old, new, orientation | Scroll |
| `LayoutEventArgs` | affected element, affected property | Layout |
| `ValueChangedEventArgs<T>` | old, new, source `ChangeSource::{User, Binding, Code}` | TextChanged, CheckedChanged, ValueChanged, SelectedIndexChanged |
| `PropertyChangedEventArgs` | property name | view-model `PropertyChanged` |
| `HotReloadedEventArgs` | diagnostics count | Kubuno-specific |

`ValueChangedEventArgs<T>` deliberately differs from WinForms, where `TextChanged` carries a bare
`EventArgs`. In an immediate-mode runtime a bound value can change "behind the control's back", and
WinForms' classic re-entrancy trap (handler sets `Text`, which raises `TextChanged`) is avoided by
telling handlers *where* the change came from. By default the designer-visible `*Changed` events fire
for **all** sources, like WinForms; the node detects binding-originated changes by comparing the
resolved value with the previous frame's value, and `source` lets a handler ignore them.

### Sender

`object sender` becomes a borrowed, typed handle:

```rust
pub struct ElementRef<'a> { pub name: Option<&'a str>,  // x:Name
                            pub element: &'static str,     // "Button"
                            pub id: &'a ElementId,         // stable path (DESIGNER.md §8)
                            pub bounds: Rect, pub focus_id: Option<FocusId> }
pub struct Sender<'a, C: Component> { inner: ElementRef<'a>, props: &'a C::Resolved }
```

`Sender<Button>` exposes the **resolved** property values of this frame (`sender.text()`), which
covers the common "`((Button)sender).Text`" use. It is read-only: the node is mutably borrowed while
it raises the event, and in Kubuno state lives in the view model anyway. Imperative actions on
controls (`focus()`, `select_all()`, a property override) go through `cx.controls()`, a
**deferred command queue** applied right after the dispatch that produced it. That keeps the borrow
checker happy and avoids mutating the tree mid-traversal.

## 2. Delegates and multicast

### WinForms

`EventHandler<T>` is a multicast delegate. `+=`/`-=` combine immutable invocation lists. Raising
snapshots the list: a handler added during the raise is not called; a handler removed during the
raise **is still called** in that raise. Handlers run in subscription order, synchronously, on the
raising thread.

### Rust mapping: `Event<A>` and `Subscription`

```rust
pub struct Event<A: EventArgs> { slots: Rc<RefCell<Slots<A>>> }   // !Send: UI thread only
impl<A: EventArgs> Event<A> {
    pub fn subscribe(&self, f: impl FnMut(&ElementRef, &mut A) + 'static) -> Subscription;
    pub fn raise(&self, sender: &ElementRef, args: &mut A);
    pub fn has_subscribers(&self) -> bool;
}
#[must_use = "dropping a Subscription unsubscribes"]
pub struct Subscription { /* Weak<slots> + slot id */ }
impl Subscription { pub fn detach(self) { /* keep alive for the event's lifetime */ } }
```

- **`-=` is `Drop`.** Dropping the token unsubscribes (RAII, like `Rx`'s `IDisposable`).
  `detach()` covers the frequent "subscribe for the object's lifetime" case.
- **Order.** Subscription order, guaranteed, tested.
- **Adding during a raise.** Snapshot semantics, like .NET: the new handler runs from the next raise.
- **Removing during a raise.** *Deliberate deviation*: a removed handler is **not** called any more
  in the current raise (a tombstone check before each call). In Rust, dropping a `Subscription` is
  usually tied to dropping the state the closure talks to; calling it afterwards would surprise.
- **Re-entrancy.** Raising the same event from one of its handlers is allowed. Each slot is a
  `RefCell<Box<dyn FnMut>>`; a slot already running is **skipped** (`try_borrow_mut` fails) with a
  `tracing::warn!`, instead of panicking. Recursion depth is capped (32) to catch
  event ping-pong between two controls.
- **Handled.** When `A: Handled`, `raise` stops at the first handler that sets `handled` (the
  `HandledEventArgs` convention). `Cancelable` does not short-circuit: every handler sees and may
  reset `cancel`, as in WinForms.

`Event<A>` is the building block for Rust code (custom controls, view models, services). Events
named in XML (`OnClick="save"`) do not need one: they dispatch by name into the typed sink of §5.

**Hot reload.** A reload rebuilds every node, so an `Event<A>` stored on a node would lose its
subscribers. Rust-side subscriptions to controls of a view therefore go through the runtime, keyed by
`x:Name`, and survive reloads: `runtime.element::<Button>("ok").click().subscribe(…)`. An
`x:Name` that disappears keeps its subscriptions dormant and logs one warning.

## 3. Standard event catalogue, routing and ordering

### Catalogue

"`Click`" below is the display name (⚡ tab, docs). The XML attribute keeps the existing `On` prefix
(`OnClick`) so events never collide with properties and **no existing `.kbview` breaks**.

| Group | Events (all controls unless stated) |
|---|---|
| Action | Click, DoubleClick, MouseClick, MouseDoubleClick |
| Mouse | MouseDown, MouseUp, MouseMove, MouseEnter, MouseLeave, MouseHover, MouseWheel |
| Key | KeyDown, KeyPress, KeyUp, PreviewKeyDown |
| Focus | Enter, Leave, GotFocus, LostFocus, Validating (cancelable), Validated |
| Property changed | TextChanged, VisibleChanged, EnabledChanged, SizeChanged, LocationChanged |
| Layout | Resize, Move, Layout |
| Appearance | Paint (custom controls and `<Canvas>` only) |
| Drag drop | DragEnter, DragOver, DragDrop, DragLeave, ItemDrag (lists/trees) |
| Per control | CheckedChanged (CheckBox, RadioButton, Switch), SelectedIndexChanged / SelectionChanged (lists, ComboBox, TabControl), ValueChanged (Slider, NumericUpDown, DatePicker), Scroll (ScrollViewer, Slider), ItemActivate / RowActivated, ColumnClick / SortChanged, SplitterMoved (SplitContainer), Selecting/Selected (TabControl, cancelable) |
| View (root) | Load, Shown, Activated, Deactivate, FormClosing (cancelable), FormClosed, Resize, HotReloaded |

Existing names stay valid as **aliases**: `OnToggled` becomes an alias of `OnCheckedChanged` on
`Switch`, `OnChanged` of `OnTextChanged`, and so on. `EventMeta.aliases` makes the validator accept
them, and a new hint diagnostic ("`OnToggled` is an older name for `OnCheckedChanged`") offers a
quick fix. Aliases are never removed within 1.x.

**Default events** (double-click on the design surface): Button/Link/ToolbarItem → Click; CheckBox,
RadioButton, Switch → CheckedChanged; TextField/TextArea → TextChanged; ComboBox, ListView, TabControl →
SelectedIndexChanged; Slider/NumericUpDown/DatePicker → ValueChanged; DataTable → SelectionChanged;
Timer → Tick; view root → Load. Every other component defaults to Click.

### Routing: direct, with two targeted exceptions

WinForms raises events **directly** on the control concerned, with no routing. WPF adds tunnelling
(`Preview*`) and bubbling. **Recommendation: direct by default, like WinForms**, for three reasons:
(1) the ⚡ tab and the handler model are per-element, and a designer user expects "my handler runs when
*this* button is clicked"; (2) bubbling needs `e.Source` vs `sender` and `Handled` discipline everywhere,
which is WPF's steepest learning curve; (3) in an immediate-mode tree, direct dispatch is free,
while general routing needs an explicit ancestor path for every event.

The two WinForms behaviours that *are* routing are reproduced exactly:

- **`KeyPreview`** (a bool property on the view root): when set, the root receives
  KeyDown/KeyPress/KeyUp **before** the focused control, and `handled`/`suppress_key_press` stops
  delivery. This covers form-wide shortcuts, the main use of tunnelling.
- **Mouse wheel bubbling**: an unhandled `MouseWheel` goes to the nearest scrollable ancestor (Win32's
  `WM_MOUSEWHEEL` does the same through `DefWindowProc`).

`EventMeta.routing: Routing::{Direct, Bubble}` records this, so a later extension (for example
bubbling `Click` from items to a container) remains a metadata change.

### Ordering guarantees (tested sequences)

The router (`events/router.rs`) is a per-node state machine fed by `Frame`. It emits exactly the
WinForms sequences:

- **Click:** MouseDown → Click → MouseClick → MouseUp. Click fires only on a release *inside*
  after a press inside (as today). Keyboard activation (Space/Enter) raises Click alone.
- **Double-click:** MouseDown, Click, MouseClick, MouseUp, MouseDown, DoubleClick, MouseDoubleClick,
  MouseUp (`Frame::click_count == 2`). Controls whose metadata says `standard_double_click = false`
  (Button, as in WinForms) raise a second Click instead.
- **Hover:** MouseEnter → MouseMove* → MouseHover (once, after 400 ms stationary, via `clock.rs`) →
  MouseLeave.
- **Keys:** KeyDown → KeyPress (for character keys, unless `suppress_key_press`) → KeyUp. Repeats raise
  KeyDown+KeyPress again.
- **Focus by keyboard (Tab, `focus()`):** Enter → GotFocus on the new element; on the old one
  Leave → Validating → Validated → LostFocus.
- **Focus by mouse:** Enter → GotFocus; old: LostFocus → Leave → Validating → Validated.
- **Validating** with `cancel = true` keeps the focus on the element (WinForms `AutoValidate =
  EnablePreventFocusChange`); `CausesValidation="False"` on the target skips validation.
  `Enter`/`Leave` are raised on containers too (focus-within); GotFocus/LostFocus on leaves only.
- **View lifecycle:** Load (first successful compile, before the first paint) → Activated → Shown
  (after the first frame is presented). Closing: FormClosing (cancel keeps the window) → FormClosed →
  Deactivate. Activated/Deactivate follow `Frame::window_focused` edges.
- Within one frame, events are delivered in **paint order** (document order), which is also the
  order a user can reason about in the XML.

## 4. Custom events: three abstraction levels

### 4.1 Level 1 — events declared by application code

WinForms: `public event EventHandler<T> ValueCommitted;` plus `[Category]`, `[Description]`,
`[DefaultEvent]`. Rust:

```rust
#[derive(Component)]                                // or UserControl, §4.2
#[component(default_event = "ValueCommitted")]
pub struct RatingBar {
    #[property(category = "Behavior", default = 5, description = "Number of stars.")]
    pub max: u32,
    #[event(category = "Action", description = "Occurs when the user picks a rating.")]
    pub value_committed: Event<ValueCommittedArgs>,
}
#[derive(EventArgs, Clone)]
pub struct ValueCommittedArgs { pub value: u32 }
```

The attribute grammar (`category`, `description`, `description_fr`, `default_event`, `browsable`,
`routing`) is the same for every level, and the Properties window groups the ⚡ tab by `category`
and shows `description` in its help pane, exactly as for built-in controls. Field names become
PascalCase event names (`OnValueCommitted` in XML, "ValueCommitted" in the ⚡ tab).

A view model can also declare events (`#[event] pub saved: Event<EventArgs>` on a struct deriving
`ViewModel`). They do not appear in the designer, because they belong to no element, but other Rust
code subscribes to them normally.

### 4.2 Level 2 — user controls (composite `.kbview`)

WinForms `UserControl`: a designed composite that appears in the project's Toolbox after a build.
Kubuno: a `.kbview` whose root is `<UserControl x:Class="RatingBar">`, with a code-behind struct
deriving `UserControl`:

```rust
#[derive(UserControl)]
#[user_control(view = "rating_bar.kbview", default_event = "ValueCommitted", toolbox_category = "Inputs")]
pub struct RatingBar {
    #[property(category = "Behavior", default = 5)] pub max: u32,
    #[property(bindable)] pub value: u32,
    #[event(category = "Action")] pub value_committed: Event<ValueCommittedArgs>,
}
#[kubuno_views::handlers]
impl RatingBar {
    fn star_click(&mut self, sender: &Sender<Button>, _e: &mut MouseEventArgs) {
        self.value = star_index(sender);
        self.value_committed.raise_self(ValueCommittedArgs { value: self.value }); // re-raise
    }
}
```

- The user control **is its own view model**: its inner `{Binding}`s resolve against the struct, so
  it is isolated from the host view (WinForms encapsulation). The host binds its public properties:
  `<RatingBar Max="10" Value="{Binding Score, Mode=TwoWay}" OnValueCommitted="score_committed"/>`.
- **Re-raising** an inner event is either explicit code (above), or declarative when it is a
  pure forward: `#[event(forward = "OkButton.Click")] pub confirmed: Event<MouseEventArgs>`. The
  macro wires the inner subscription, and the sender becomes the user control, as in WinForms.
- `raise_self` fills the sender with the user control's own `ElementRef`, which the runtime provides
  through a thread-local "current element" set while the user control's subtree dispatches.

### 4.3 Level 3 — custom controls in Rust

WinForms: a `Control` subclass with `OnPaint`, attributes, and a designer. Kubuno: a type implementing
the component trait, fully in Rust:

```rust
pub trait Component: 'static {
    type Resolved;                                   // resolved props for Sender<Self>
    fn meta() -> &'static ComponentMeta;             // generated by #[derive(Component)]
    fn build(props: &Props) -> Result<Box<dyn ViewNode>, BuildError>;
}
```

`#[derive(Component)]` generates `meta()` (properties, events, default event, children model,
toolbox bitmap, description) and a registration: `inventory::submit! { ComponentRegistration(…) }`.

**The registry becomes extensible.** `registry::all()` changes from a closed list to "built-in
families + every `ComponentRegistration` linked into the binary", collected once in `OnceLock`. The
validator, compiler and exporter are unchanged: they already go through `lookup()`. The name space
is the element name; a clash with a built-in is a compile-time `const` assertion where
possible, and otherwise a startup error naming both crates.

**How the tools learn about user components.** The language server and the VS designer are separate
processes that do not link the user crate, so:

1. **Metadata without building (instant):** the attribute grammar lives in a small shared crate,
   `kubuno-views-meta`, used by the proc macros **and** by the LS. The LS scans the workspace's `.rs`
   files with `syn` for `#[derive(Component | UserControl)]` and builds the same `ComponentMeta`
   the macro would. Completion, validation, hover, the Properties window, the ⚡ tab and the Toolbox
   all see a custom control as soon as it is typed. The LS merges these entries into its registry
   export (`kubuno/registry`, `DESIGNER.md` §5) with `origin: "project"`.
2. **Rendering needs a build (as in WinForms).** The design surface is rendered by a host process.
   For projects with custom controls, `vskubuno` builds a per-project **design host** (a generated
   `bin` target that links the user crate and `kubuno_views::design`), exactly like WinForms loads the
   project's compiled assembly. Until it has been built, custom elements are drawn as a labelled
   placeholder ("RatingBar: build the project to preview"). After a build, the design host's own
   `--export-registry` output is authoritative and replaces the `syn` result, so any drift between
   the two is short-lived and visible.
3. **Toolbox.** A "<Project> Components" tab is populated from `origin: "project"` entries
   (WinForms `AutoToolboxPopulate`), with `toolbox_category` and an optional icon.

## 5. Designer and Visual Studio integration

### 5.1 Metadata

`EventMeta` grows (Rust, JSON export, `Registry/EventMeta.cs`):
`{ name, display_name, doc, doc_fr, category, args_type, args_chain, cancelable, routing, aliases,
browsable }`, and `ComponentMeta` gets `default_event`. The C# side then:

- groups the ⚡ tab by `category` (`CategoryAttribute` on each `KbviewEventDescriptor`, localized
  for the standard names: Action, Behavior, Focus, Key, Mouse, Drag Drop, Layout, Property Changed);
- sets `DefaultEventAttribute` from `default_event` instead of `Events[0]`;
- shows `doc`/`doc_fr` in the description pane (as today);
- hides `browsable = false` events; the grid's own category/alphabetical toggle and search box
  provide filtering by events.

### 5.2 Double-click on the design surface

Double-clicking a control creates or opens its **default** handler (`kubuno/createHandler` with
`event = default_event`), then shows the code-behind at the handler. This is the same path the
⚡ tab already uses; only the event choice is new.

### 5.3 Compatible handlers dropdown

New LS request `kubuno/compatibleHandlers { uri, argsType }` → the names of the handler methods in the
code-behind whose args parameter type is in the event's `args_chain` (or that take no args). A
handler taking `&EventArgs` is therefore offered for every event, like a `(object, EventArgs)` method
in WinForms. `KbviewEventBindingService.GetCompatibleMethods` returns that list, which fills the ⚡
row's dropdown. The scan is syntactic (`syn` over the code-behind `impl` marked
`#[kubuno_views::handlers]`), consistent with `definition.rs`.

### 5.4 Handler shape and migration

Target shape, generated by `createHandler`:

```rust
#[kubuno_views::handlers]
impl SettingsViewModel {
    fn say_hello_click(&mut self, sender: &Sender<Button>, e: &mut MouseEventArgs) {
    }
}
```

The attribute macro generates `impl EventSink for SettingsViewModel`, a `match` from handler name to
method, with a checked downcast of `&mut dyn EventArgs` to the declared type (or the base
`EventArgs`) and a `HANDLERS: &[HandlerInfo]` table used for diagnostics. Shorter forms are accepted:
`fn f(&mut self)`, `fn f(&mut self, e: &mut MouseEventArgs)`. `Runtime::frame` gains a generic
companion `frame_typed<V: ViewModel + EventSink>`, so a handler has `&mut self` of the concrete type
instead of `&mut dyn ViewModel`.

**Migration without breaking anything:**

1. `HandlerTable`, `handlers!` and `Handler = FnMut(&mut dyn ViewModel, Value)` stay, unchanged.
   Dispatch tries the typed sink first, then the legacy table. The legacy `Value` comes from
   `EventArgs::legacy_value()`, so each event keeps producing exactly the value it produces today
   (`Bool(true)` for a click, the new state for a toggle, the text, the index as `F32`).
2. `ViewEvent`/`ViewEventKind` keep their three variants; a new `ViewEventKind::Other { name,
   args: Rc<dyn EventArgs> }` carries every new event for callers that `match` on returned events.
3. `createHandler` writes the typed form when the target file has a `#[kubuno_views::handlers]`
   impl, and the legacy form otherwise, so each project keeps a single style.
4. A code action "Convert handlers! table to typed handlers" rewrites a table whose closures can be
   moved mechanically (legacy parameters `vm, value` map to `self` and `e.legacy_value()`).
5. The legacy form is marked `#[deprecated]` only once the converter has been used on the samples,
   and it is never removed within 1.x.

### 5.5 Rename and remove

- **Rename** (⚡ row context menu "Rename handler…", or F2 on the attribute value in XML): new
  `kubuno/renameHandler { uri, old, new }` returns one `WorkspaceEdit` covering every `On*="old"` in
  the project's `.kbview` files, the `fn old` declaration, and the legacy table string when there is
  one. rust-analyzer's own rename does not update XML; the reverse case (a rename made in Rust) is
  caught by the new diagnostic "handler `old` not found", whose quick fix offers the closest name.
- **Remove:** clearing a ⚡ row removes the attribute (today). As in WinForms, if the handler's body
  is still empty and no other element references it, the stub is deleted too.
- **Validation:** now that the LS sees the typed `impl`, an `On*` naming a missing handler, or a
  handler whose args type is incompatible, becomes a warning (not an error: legacy tables built at
  runtime cannot be seen statically).

## 6. Threading and async

- **UI thread only.** `Event<A>`, `EventSink` and the view model are `!Send`; all handlers run
  synchronously on the UI thread during the frame. This is required for `Handled`/`Cancel`, whose
  results the raiser reads immediately.
- **`Invoke` / `BeginInvoke`.** `UiDispatcher` is a `Send + Clone` handle obtained from the runtime:
  `begin_invoke(move |vm: &mut V| …)` posts a closure (a queue plus a `PostMessage(WM_APP)` wake-up
  through the host), `invoke(…) -> R` posts and blocks for the result, and `is_ui_thread()` is
  `InvokeRequired`. `invoke` called from the UI thread runs inline (no self-deadlock); calling it
  while the UI thread waits on the caller is documented as a deadlock, as in WinForms.
- **Async handlers.** The UI thread gets a single-threaded executor (`spawn_local`), polled from the
  message loop, which plays the role of WinForms' `SynchronizationContext`. A handler may be
  `async fn f(ui: UiHandle<Self>, e: MouseEventArgs)`: the macro passes **owned** args (a clone) and
  a handle instead of `&mut self`, because a borrow cannot live across `.await`. After an await, state
  is changed through `ui.update(|vm| …)`. Consequently `Handled`/`Cancel` cannot be set by an async
  handler, which the macro rejects at compile time; this is the same "set it before the first await"
  rule as WinForms, but enforced.
- Blocking work in a handler is linted in debug builds: a dispatch longer than 50 ms logs a warning
  with the handler name.

## 7. Work packages

| # | Package | Owns | Depends on | Size | Tests / live verification |
|---|---|---|---|---|---|
| EVT-1 ✅ done (§9) | Args, traits, `Event<A>`/`Subscription`, `ElementRef`/`Sender` | new `kubuno-views/src/events/{mod,args,multicast,sender}.rs`, `#[derive(EventArgs)]` in new `kubuno-views-macros` | — | M | Unit tests: order, drop = unsubscribe, add/remove during raise, re-entrancy skip, depth cap, `Handled` short-circuit, `Cancelable` visibility |
| EVT-2 ✅ done (§10) | Input router and ordered synthesis (mouse, hover, keys, focus, validation) | `events/router.rs`, `node.rs` `fire` (typed), every `families/*.rs` call site | EVT-1 | L | Scripted `Frame` sequences asserting exact WinForms orders (§3); legacy `ViewEvent` tests unchanged; live: `view_preview` with an event log overlay |
| EVT-3 ✅ done (§10) | Registry metadata and catalogue | `registry/{mod,export,docs_fr}.rs`, families' `EventMeta` tables, aliases, `validate.rs` alias hint; C# `Registry/EventMeta.cs`, `KbviewElementObject` categories and default event | EVT-1 | M | Registry test: every `default_event` exists; alias parse tests; C# JSON round-trip; live: ⚡ tab grouped with descriptions in VS experimental instance |
| EVT-4 ✅ done (§11) | Typed handlers and migration | `kubuno-views-macros` `#[handlers]`, `EventSink`, `Runtime::frame_typed`, legacy adapter; `handler_insert.rs` typed stub | EVT-1, EVT-2 | L | `trybuild` pass/fail tests (bad signature, async + Handled); existing `handlers!` tests untouched; live: `KubunoLot8App` old and new style both click through |
| EVT-5 ✅ done (§12) | Designer/LS commands | LS `compatibleHandlers`, `renameHandler`, remove-empty-stub, missing-handler diagnostic, convert code action; C# `GetCompatibleMethods`, surface double-click → default event, rename menu | EVT-3, EVT-4 | M | LS unit tests on temp workspaces; live: dropdown lists handlers, rename updates XML + Rust in one undo per file |
| EVT-6 | View lifecycle, window events, threading | `runtime.rs` lifecycle hooks, host close/activation plumbing, `UiDispatcher`, `spawn_local` executor | EVT-2 | M | Unit tests with a fake host; live: FormClosing cancel keeps the window, background thread `begin_invoke` updates a label |
| EVT-7a ✅ done (§14) | Control hierarchy and overridable `on_…` methods | `kubuno-views/src/component/`, `controls.rs` (classes), `#[derive(Component)]` in `kubuno-views-macros`, router/nodes/`DesignSlot` delivering through the classes, registry base chain, `ControlHost` | EVT-2, EVT-4 | L | Upcast/downcast, override + event order, router through a control, `compile_fail` macro misuse; live: a `RoundButton` (extends `Button`) in a template app |
| EVT-7b ✅ done (§15) | Custom controls and user controls | `kubuno-views-meta` (shared grammar), `#[derive(Component/UserControl)]`, extensible registry (static constructors, not `inventory`: §15), `<UserControl x:Class>`, LS `syn` scan, project Toolbox tab, per-project design host | EVT-3, EVT-4 | L | Macro and LS produce identical `ComponentMeta` (shared golden tests); live: a `RatingBar` user control appears in the Toolbox, is dropped, its event bound and raised |
| EVT-8 ✅ done (§17) | Paint, scroll, drag and drop | `PaintEventArgs` for `<Canvas>`/custom controls, `ScrollEventArgs`, OLE drop target in the host, `DragEventArgs` | EVT-2, EVT-6 | L | Unit tests on synthetic drag sequences (Enter → Over* → Drop/Leave); live: drop a file from Explorer onto a list |

Suggested order: EVT-1 → (EVT-2 ∥ EVT-3) → EVT-4 → (EVT-5 ∥ EVT-6) → EVT-7 → EVT-8. EVT-1 to EVT-4
bring the visible benefit (typed handlers, full ⚡ tab); EVT-7 is the largest risk and the most
differentiating.

## 8. Risks

- **Concurrent edits.** EVT-2 touches `node.rs` and every family's `fire` call site, which are
  active areas (design mode, DSG work). Land EVT-1 first (purely additive), and make `fire`'s new
  signature a thin wrapper, so call sites migrate one family at a time.
- **Frame-granular input.** Ordering relies on the host's latched buttons and key queue. Two clicks
  within one frame, or a press and release on different elements within one frame, must be covered
  by router tests; the host may need to queue pointer transitions like it already queues keys.
- **Borrows during dispatch.** Handlers get `&mut` view model while a node is mid-paint; that is
  fine because both are disjoint borrows of `PaintCx`, but any "mutate the tree from a handler"
  feature must stay behind the deferred command queue.
- **Macro/LS drift.** Mitigated by the shared `kubuno-views-meta` crate and golden tests, and made
  temporary by the design host's authoritative export after a build.
- **Compile time and rust-analyzer.** Proc macros add a dependency on `syn`; keep them in one small
  crate, and generate plain `match` code that rust-analyzer can navigate.
- **Name compatibility.** Keeping the `On` prefix and aliases avoids breaking files; renaming the
  display names in the ⚡ tab ("Click" rather than "OnClick") must stay consistent in
  `createHandler`'s default handler names (`say_hello_click`) and in the C# `DefaultHandlerName`.
- **Async deadlocks** with `invoke`, and handlers that block the UI thread: documented, debug-time
  warnings, no silent failure.
- **Custom control rendering in the designer** requires building user code: build failures must
  leave placeholders, never an empty or crashed surface (same rule as hot reload's "keep the last good
  tree").

## 9. EVT-1 as built (2026-09-29)

Purely additive, in `Z:\src\desktop\windows` (uncommitted there at the time of writing): nothing raises
these events yet (EVT-2), `HandlerTable`/`handlers!`/`ViewEvent` are untouched, `kubuno_ui` and
`kubuno-controls` are unchanged.

**Files.** `kubuno-views/src/events/{mod,args,multicast,sender}.rs` (`pub mod events` in `lib.rs`, plus
`extern crate self as kubuno_views` so the derive's `::kubuno_views::…` paths resolve inside the crate);
new proc-macro crate `src/crates/kubuno-views-macros` (`syn` 2 / `quote`, already locked; workspace member
and `workspace.dependencies` entry); `kubuno-views` gained the `kubuno-views-macros` and `tracing`
dependencies.

**Traits (`events/mod.rs`).**

- `EventArgs: Any` — `as_any`, `as_any_mut`, `legacy_value()` (default `Value::Bool(true)`),
  `type_chain()`, plus four hooks with `None` defaults that the derive overrides: `as_handled`,
  `as_handled_mut`, `as_cancelable`, `as_cancelable_mut`. They are how `Event::raise` (and later the
  router) finds `Handled`/`Cancelable` on a generic `A` without specialization.
- `impl dyn EventArgs { is::<T>(), downcast_ref::<T>(), downcast_mut::<T>(), is_a(name) }` — `is_a`
  tests the chain, i.e. handler/event compatibility (§5.3).
- `ArgsChain { const NAME; const CHAIN: &[&str] }` — compile-time name and ancestor chain, root last.
- `Handled { handled, set_handled }`, `Cancelable { cancel, set_cancel }`.

**`#[derive(EventArgs)]`** (re-exported as `kubuno_views::events::EventArgs`, same name as the trait, like
`serde`): implements `ArgsChain` + `EventArgs`, and on request `Handled`/`Cancelable` with their hooks.
Options, in `#[args(…)]`: `handled` / `handled = "field"` (default field `handled`), `cancel` /
`cancel = "field"`, `extends = Path` (a type path, or a string holding one; the parent must implement
`ArgsChain`; default: the root), `legacy = path` (`fn(&Self) -> Value`). The chain is the struct's name
followed by the parent's `CHAIN`, concatenated in a `const` (so a three-level chain such as
`FormClosingEventArgs → CancelEventArgs → EventArgs` costs nothing at run time). Lifetime parameters are
rejected (`EventArgs: Any`), type parameters get a `'static` bound, unknown options and a missing
`handled`/`cancel` field are compile errors (covered by `compile_fail` doctests).

**Catalogue (`events/args.rs`)**, deviations from §1 in bold:

| Rust type | Notes |
|---|---|
| **`EmptyEventArgs`** | the root unit struct: the trait already owns the name `EventArgs` in Rust, so the struct is renamed; its tooling name (`NAME`, chain) is still `"EventArgs"` |
| `HandledEventArgs { handled }` | WinForms `HandledEventArgs` (added) |
| `MouseEventArgs { button: MouseButton, clicks, x, y, delta, mods }` | `MouseButton::{None, Left, Right, Middle}`; `mods` is `kubuno_controls::host::Modifiers` |
| `KeyEventArgs { key, mods, handled, suppress_key_press }` | `Key(pub u16)` wraps the `host::vk` code (`Key::letter('s')`); `suppress()` sets both flags, as WinForms' setter does |
| `KeyPressEventArgs { key_char, handled }` | legacy value `Str(char)` |
| **`PaintEventArgs { clip }`** | **placeholder**: `EventArgs: Any` needs `'static`, so the borrowed canvas (`PaintEventArgs<'a>`) needs its own raise path, added by EVT-8 |
| `CancelEventArgs { cancel }` | |
| `FormClosingEventArgs { reason, cancel }` | `extends = CancelEventArgs`; `CloseReason::{None, UserClosing, ApplicationExitCall, OwnerClosing, WindowsShutDown, TaskManagerClosing}` |
| `FormClosedEventArgs { reason }` | |
| `DragEventArgs { data, allowed, effect, x, y, mods }` | `DataObject { text, files, custom }`, `DragDropEffects` bit set (`NONE/COPY/MOVE/LINK/SCROLL/ALL`, `\|`, `contains`) |
| `ScrollEventArgs { kind, old, new, orientation }` | `ScrollEventType` (WinForms' nine values), `ScrollOrientation`; legacy `F32(new)` |
| `LayoutEventArgs { affected_element, affected_property }` | both `Option<String>` (element = stable id) |
| `ValueChangedEventArgs<T> { old, new, source }` | `ChangeSource::{User, Binding, Code}`; hand-written impl, legacy value = `new` via `IntoLegacyValue` (bool, String, f32, f64, i32, u32, usize, `Option<usize>` → `-1` for none, `Value`). Aliases `TextChangedEventArgs` (`String`), `CheckedChangedEventArgs` (`bool`), `SelectionChangedEventArgs` (`Option<usize>`), `NumericValueChangedEventArgs` (`f32`) — all share the chain `ValueChangedEventArgs → EventArgs` |
| `PropertyChangedEventArgs { property }` | legacy `Str(property)` |
| `HotReloadedEventArgs { diagnostics }` | |

Legacy values reproduce what `node.rs`/the families pass today (`Bool(true)` for a click, the state for a
toggle, the text, the index as `F32`).

**Multicast (`events/multicast.rs`).** `Event<A: EventArgs>` (`Rc<RefCell<…>>`, `!Send`; `Default`;
`Clone` = another handle to the same list): `new()`, `subscribe(FnMut(&ElementRef, &mut A) + 'static)
-> Subscription`, `raise(&ElementRef, &mut A)`, `has_subscribers()`, `subscriber_count()`, `clear()`.
`Subscription` (`#[must_use]`, holds a `Weak`): drop = unsubscribe, `detach()`, `unsubscribe()`,
`is_active()`; harmless if the event is gone first. Semantics exactly as §2, with two precisions:

- the depth cap (`MAX_RAISE_DEPTH = 32`) is a **thread-wide** count of nested raises over all events, so a
  cascade across many events is caught too; a raise beyond it calls nothing and logs `tracing::warn!`. A
  ping-pong between *two* events actually stops earlier, at the re-entrancy skip (the first handler is
  still running when its event is raised again). The counter is restored by a guard, even if a handler
  panics;
- `Handled` is checked **after** each handler: a handler that sets then resets it does not stop the raise.

**Sender (`events/sender.rs`).** `ElementId = str` (the child-ordinal path of `DESIGNER.md` §8).
`ElementRef<'a> { name, element, id, bounds, focus_id }` (`Copy`) with `detached(name)` (sender that is no
element: view models, services, tests), `to_local(x, y)`, `contains(x, y)`, `display_name()`. A minimal
`Component: 'static { const ELEMENT; type Resolved; }` (EVT-7 extends it with `meta`/`build`).
`Sender<'a, C> { inner, props }` (`Copy`): `new`, `element()`, `props()`, and `Deref<Target =
C::Resolved>` so `sender.text()` works. Deferred commands: `ControlQueue` (`focus`, `select_all`,
`set_property`, `clear_property`, `push`, `commands`, `len`, `is_empty`, `drain`), `ControlCommand::{Focus,
SelectAll, SetProperty, ClearProperty}`, `ControlTarget::{Name, Id}` (`From<&str>` = by name,
`From<&ElementRef>` = by name when it has one, else by path). Where the queue lives (`cx.controls()`) and
applying it after the dispatch is EVT-2.

**Not in EVT-1:** `UiDispatcher`/`spawn_local` (EVT-6), the router and typed `fire` (EVT-2), metadata
(EVT-3), `#[handlers]`/`EventSink` (EVT-4).

**Tests.** 34 unit tests in `kubuno-views` (`events::args` 9, `events::multicast` 21, `events::sender` 4:
order, drop = unsubscribe, detach, add/remove/self-remove/clear during a raise, re-entrancy skip, depth
cap and its exact limit, panic-safe depth counter, `Handled` short-circuit, `Cancelable` visibility,
three-level chains, generic and unit derives, legacy values, downcasts), 11 doctests in `kubuno-views`,
4 in `kubuno-views-macros` (1 example + 3 `compile_fail`). The existing 401 unit tests of `kubuno-views`
still pass; `cargo clippy --all-targets -D warnings` is clean for both crates.

## 10. EVT-2 and EVT-3 as built (2026-09-29)

In `Z:\src\desktop\windows` (uncommitted there) and in this repository. Existing `.kbview` files and
`handlers!` projects are unchanged: old attribute names are aliases, legacy handlers receive the same values.

### EVT-2 — input router and typed `fire`

- **`kubuno_ui` (scope note)**: `FocusRing` records every focus move with its cause
  (`take_changes() -> Vec<FocusChange { from, to, cause }>`, `FocusCause::{Pointer, Keyboard, Program,
  Removed}`, capped at 64 unread) and can put the focus back silently (`restore`) — the only kubuno_ui change;
  every exe was rebuilt (`tools/build-all.ps1`) and restaged. `kubuno-controls` was not changed: the
  latched buttons, `click_count`, wheel and the key/text queue of `Frame`/`host::events()` were enough.
- **`kubuno-views/src/events/router.rs`**: `SlotEvents` (per compiled element: stable id, element name,
  `x:Name`/focus id, the handler of each `On*` attribute resolved to its canonical event — aliases and, on the
  root, view events —, `native_click`, `keyboard_click` for Button/IconButton/LinkLabel,
  `standard_double_click = !native_click`), `InputRouter` (owned by `Runtime`, state keyed by element id so it
  survives hot reloads), `Dispatch { vm, handlers, events }`, `raise()`.
- **How a frame runs**: `DesignSlot` (the wrapper `compile::build_node` puts around every element) registers
  its `SlotEvents` and bounds with the router while painting, and sets the *current element* (the sender of
  the events its node raises). `Runtime::frame_with_design` calls `router.begin_frame` right after
  `FocusRing::begin_frame` — routed against the PREVIOUS frame's geometry, like the focus ring: Load →
  Activated (first frame), window focus edges, focus moves, MouseLeave/MouseEnter (the deepest element under
  the pointer; the parent gets MouseLeave when the pointer enters a child), MouseMove (to the capturing element
  while a button is held), MouseHover (400 ms at rest, `request_repaint_after` for the rest), MouseDown
  (captures), MouseWheel, then KeyDown/KeyPress/KeyUp to the focused element in queue order (Space/Enter →
  Click on a button). A release queues the tail. The tree paints (a `<Button>` raises its own Click there), then
  `router.end_frame` raises the queued [Click if the node has none] → MouseClick (or DoubleClick →
  MouseDoubleClick) → MouseUp, Resize → SizeChanged / Move → LocationChanged for moved elements, Shown after
  the first frame. The designer (a layout map is recorded) runs no router: design mode never raises
  application events.
- **Focus**: keyboard/program: Enter (containers, outermost first) → GotFocus, then old Leave (innermost
  first) → Validating → Validated → LostFocus; pointer: Enter → GotFocus, old LostFocus → Leave → Validating →
  Validated (a press focuses before MouseDown). A cancelled Validating restores the focus
  (`FocusRing::restore`) and balances the new side (its LostFocus and Leave; for the pointer the old element's
  Enter and GotFocus again). Removal: LostFocus/Leave, no validation.
- **Dispatch**: an event reaches only the handler its element names, through the new
  `HandlerTable::dispatch_args` — a typed handler (`insert_typed`, `TypedHandler = FnMut(&mut dyn ViewModel,
  &ElementRef, &mut dyn EventArgs)`, can set `handled`/`cancel`) wins, else the legacy handler gets
  `args.legacy_value()`. A handled KeyDown/KeyPress/KeyUp is consumed from the host queue; `suppress_key_press`
  drops the following KeyPress. Router events are returned as `ViewEventKind::Other { name, args: Rc<dyn
  EventArgs> }` (manual `Debug`/`PartialEq`) only when the element names a handler for them.
- **Typed `fire`**: `PaintCx::fire`/`InteractCx::fire` take `&mut dyn EventArgs` instead of a `Value`; every
  call site (node.rs and the five families) passes the typed args whose legacy value is exactly what it passed
  before (`EmptyEventArgs`, `CheckedChangedEventArgs`, `TextChangedEventArgs`, `NumericValueChangedEventArgs`,
  `SelectionChangedEventArgs`, and three new ones: `ItemEventArgs { index }` — toolbar/breadcrumb items,
  `ItemActivateEventArgs { item: Value }` — row/node activation, `ItemCheckEventArgs { index, checked }`). The
  `ViewEventKind` each site reports is unchanged. `PaintCx` gained `router`/`sender` (`pub(crate)`) and
  `with_surface(canvas, frame)` (the scroll area and tab page paths used to rebuild it field by field).
- **Deviations / not built**: within a phase, Leave is raised before Enter rather than strictly in paint
  order; `KeyPreview`, wheel bubbling and `CausesValidation` (§3) are not built; FormClosing/FormClosed stay
  EVT-6; Click is synthesized for the left button only (MouseClick for any); the old value of a few
  `*Changed` args is not tracked (lists, sort, calendar: `None`/empty).
- **Tests**: 18 router tests (`events::router::tests`: Load → Activated → Shown once, Deactivate/Activated
  edges; MouseDown → Click → MouseClick → MouseUp; a native Click between MouseDown and MouseClick; release
  outside = MouseLeave/MouseMove (captured)/MouseUp only; the double-click sequence; a button's second Click;
  hover Enter → Move* → Hover once (with the repaint delay) → Leave; deepest element + parent MouseLeave;
  wheel; keys in order; handled KeyDown consumed + suppressed KeyPress; Space/Enter Click on a button; focus by
  keyboard, by mouse, focus before MouseDown, cancelled Validating; Resize/Move; `SlotEvents` from XML
  incl. aliases and root-only view events; legacy value), plus a node test with a real `ButtonNode`
  (`ok_down → ok_click → ok_mouse_click → ok_up`, legacy `Bool(true)`), and `FocusRing`'s change log test in
  kubuno-ui. All previous `ViewEvent` tests unchanged.

### EVT-3 — registry metadata

- **`EventMeta`** = `{ name, doc, category: EventCategory, args_type, args_chain, cancelable, routing:
  Routing, aliases, browsable }` built with `const` methods (`EventMeta::new(..).category(..).args::<A>()
  .aliases(&[..]).routing(..).hidden()`; `args::<A>()` reads `ArgsChain` and sets `cancelable` when the chain
  contains `CancelEventArgs`). `display_name()`, `matches(attr)`.
- **Catalogue**: `COMMON_EVENTS` (24: Click, DoubleClick, MouseClick, MouseDoubleClick, the 7 mouse, 3 key, 6
  focus/validation, Resize, Move, SizeChanged, LocationChanged) on every control — not on the gated
  structural elements (`is_gated`: Item, Column, TabItem, Option, Step, AccordionSection, BreadcrumbItem,
  ToolbarItem) — a component's own entry of the same name wins (Button's OnClick doc). `VIEW_EVENTS` (OnLoad,
  OnShown, OnActivated, OnDeactivate) are accepted on the root element only (validator, LS, `SlotEvents`).
  `ComponentMeta::{event (own by name or alias, then common), all_events, has_common_events, default_event,
  alias_target}`.
- **Renames with aliases**: Switch `OnToggled` → `OnCheckedChanged`; TextField/TextArea/SearchField/
  MaskedField `OnChanged` → `OnTextChanged`; Dropdown/ComboBox `OnChanged` → `OnSelectedValueChanged`
  (deviation: the payload is the value, so WinForms' SelectedValueChanged rather than SelectedIndexChanged);
  DatePicker `OnChanged` → `OnValueChanged`; ListView/TreeView `OnActivate` → `OnItemActivate`. `Props::event`
  reads an event under its canonical name, then its aliases, whichever name the build closure asks for, so
  the families' build code is unchanged. `validate::hints` reports "`OnToggled` is an older name for
  `OnCheckedChanged`" (the LS publishes it as a HINT; the quick fix is left to EVT-5).
- **Default events** (`component!`'s new optional `default_event:`, fallback OnClick, else the first own
  event): Button/IconButton/LinkLabel/ColorField and containers → OnClick; Switch/CheckBox/RadioButton →
  OnCheckedChanged; text fields → OnTextChanged; Dropdown/ComboBox → OnSelectedValueChanged; Slider/
  NumericField/DatePicker → OnValueChanged; ListBox/CheckedListBox/ListView/TreeView/DataTable/Tabs →
  OnSelectionChanged; Splitter → OnDistanceChanged; Stepper → OnStepSelected; MonthCalendar → OnDateSelected;
  the view root → OnLoad (chosen by the designer).
- **Export** (`kubuno/registry`): each event `{ name, display_name, doc, doc_fr, category, args_type,
  args_chain, cancelable, routing, aliases, browsable, root_only, common }`; a control's list is its own events,
  then the common ones, then the view events flagged `root_only`; each component gets `default_event`. French
  docs for the common and view events (`docs_fr`: `*.OnMouseDown`, `View.OnLoad`, `event_french`). The C#
  fixture `registry.sample.json` is regenerated from the real export (ignored test `write_registry_fixture`).
- **Language server**: completion offers own + common (+ view on the root) events with `category (args)`
  detail; hover shows category/args and "older name for"; go-to-definition and `kubuno/createHandler` accept
  common and root view events, and createHandler finds a handler already written under an alias (no second
  attribute); a request is written under the attribute name it asks for.
- **C#**: `EventMeta` gained the fields (+ `EffectiveDisplayName`, `LocalizedCategory` through
  `DesignerText.EventCategory` — Action, Comportement, Focus, Touche, Souris, Glisser-déplacer, Disposition,
  Propriété modifiée —, `Matches`, `AttributeNames`); `ComponentMeta.DefaultEvent`, `DefaultEventFor(isRoot)`,
  `FindEvent`. The ⚡ tab rows carry the event's category, display name (`Click`, not `OnClick`) and
  description; non-browsable and (off the root) root-only events are hidden; `DefaultEventAttribute`/
  `GetDefaultEvent` come from the registry (root: OnLoad); a row reads and edits its handler under an alias
  when the file uses one. The context menu's *Create Handler ›* lists the default event then the component's
  own events (the common ones stay in the ⚡ tab).
- **Double-click on the surface (§5.2)**: `view_embed` sends `{"type":"doubleClick","elementId":…}` when a
  press has `click_count >= 2` (the element under the pointer, `""` for the frame's title bar), after
  `selectionChanged`, and cancels the move drag the second press started; the coordinator resolves the
  element's default event and calls the same `CreateOrShowHandler` as the ⚡ tab.
- **Tests**: Rust — registry (every default event exists, design-note defaults, aliases, common vs own,
  unique names/aliases), props alias reading, validator (common/alias/root-only, alias hint), export (metadata
  and key set), docs_fr (common/view French docs), protocol (`doubleClick`), LS (alias-wired handler found,
  common + root view event handlers). C# — fixture round-trip of the new fields and an old-shape export,
  default events, ⚡ categories/display names/descriptions (English and French), root events, alias rows,
  `doubleClick` parsing, the short *Create Handler* list (290 Designer tests pass).

### Live verification (regular Visual Studio, 2026-09-29)

A new *Kubuno Desktop Application* (`EvtApp`, created through DTE from the installed template) with the
template's button extended by `OnMouseDown`/`OnMouseClick`/`OnMouseUp` handlers appending to the status field.
The designer ran on the project's own `kubuno_ui.dll` (§15 of DESIGNER.md). The ⚡ tab of the selected button
listed the events grouped by category in French (Action, Disposition, Focus, Propriété modifiée, Souris,
Touche). A real double-click on a button without handler wrote `OnClick="on_second_click"`, the `fn
on_second_click` stub and its `handlers!` entry, and opened the code at the stub. **Found live**: that entry
(`"x" => |vm, value| x(vm, value),`) did not compile — `handlers!` only accepted a block body — a
pre-existing `createHandler`/macro mismatch; the macro now takes any expression. F5 (no console window, see
GETTING-STARTED "Console window"), then a real click on the button: VS's Output ▸ Debug pane showed the
application's `tracing` lines `event MouseDown`, `event Click`, `event MouseClick`, `event MouseUp`, in that
order, followed by the returned `ViewEvent`s (`Other { name: "MouseDown", args: "MouseEventArgs" }`, `Clicked`,
`MouseClick`, `MouseUp`).

## 11. EVT-4 as built (2026-09-29)

In `Z:\src\desktop\windows` (uncommitted there) and in this repository. `kubuno_ui` and `kubuno-controls` are
unchanged. Existing `handlers!` projects build and run unchanged (checked on a copy of a project created with
the previous template).

### Deviation: the attribute is `#[kubuno_views::event_handlers]`

§4.2/§5.4 wrote `#[kubuno_views::handlers]`. That name is taken: `handlers!` (the legacy table, `#[macro_export]`)
lives in the crate root's macro namespace, and an attribute macro of the same name cannot be re-exported next
to it (E0255; declarative attribute macros, which could serve both, are still unstable). Renaming the legacy
macro would break every existing project, so the attribute is **`#[kubuno_views::event_handlers]`** (or
`#[event_handlers]` after `use kubuno_views::prelude::*;`). Everything below uses that name.

### Runtime (`kubuno-views`)

- **`events/typed.rs`**: `EventSink { const HANDLERS: &[HandlerInfo]; fn handle_event(&mut self, handler, cx:
  &HandlerContext, args: &mut dyn EventArgs) -> bool; fn handler_info(name) }`, `HandlerInfo { name, method,
  sender (element, "*" for any, None), args (tooling name, None), args_mut }`, `HandlerContext` (the sender +
  its resolved properties, `typed_sender::<C>()`), `dispatch_typed(vm, handler, sender, args)`, the args
  adapter `with_args::<T>()` (exact type; the root `EmptyEventArgs` for any event; `CancelEventArgs` /
  `HandledEventArgs` bridged over any cancelable / handled args, the flag written back; otherwise a
  `tracing::warn!` and the handler is not called), and `TypedViewModel<V>` (forwards `get`/`set`, answers
  `dispatch_event`).
- **Dispatch order**: `ViewModel` gained a default method `dispatch_event(&mut self, handler, sender, args) ->
  bool { false }`; `HandlerTable::dispatch_args` calls it first, then the table's typed entries, then its legacy
  entries with `args.legacy_value()` (the legacy adapter of §5.4 point 1). A sink handler whose args or sender
  do not fit is skipped with a warning but still "claims" its name, so a same-named legacy entry never runs in
  its place.
- **`Runtime::frame_typed(canvas, frame, vm: &mut V, bounds)`** and **`frame_typed_with(…, handlers, bounds)`**
  (`V: ViewModel + EventSink`) paint through `TypedViewModel`. `frame` is unchanged.
- **Sender** (`events/sender.rs`): `ElementRef` gained `attributes: &[(String, String)]` (the element's
  non-event XML attributes, recorded by `SlotEvents::from_element`); `ElementProps::resolve(name, attributes,
  vm)` turns them into the properties of the frame (`{Binding P}` → `vm.get(P)`), read through `get`, `string`,
  `bool`, `f32`, `text()` (`Text`), `name()` (`x:Name`), `iter`. **`kubuno_views::controls`**: one zero-sized
  `Component` per non-structural control of the registry (`Button`, `Switch`, `TextField`… 41 types, checked
  against the registry by a test), `Resolved = ElementProps`; `AnyElement` (`ELEMENT = "*"`) accepts any
  element. Control-specific resolved structs are EVT-7. The structural elements (`<ToolbarItem>`…) have no type:
  their events are raised by their parent.
- **`kubuno_views::prelude`**: `ViewModel`, `Value`, `HandlerTable`, `handlers!`, `event_handlers`, `Sender`,
  `ElementRef`, `ElementProps`, `AnyElement`, `EventArgs` (trait + derive), `EventSink`, `ArgsChain`, `Handled`,
  `Cancelable`, every args type and every control type.
- **Click carries `MouseEventArgs`** (deviation from WinForms' declared `EventArgs`, matching its runtime
  object): a `<Button>`/`<IconButton>`/`<LinkLabel>` click (`node::click_args`: left button, click count, the
  pointer relative to the control, modifiers), the router's synthesized Click and DoubleClick (`mouse_args`),
  and a keyboard activation (`MouseButton::None`, `clicks == 0`). The registry declares `OnClick`/`OnDoubleClick`
  (common) and the three controls' `OnClick` with `MouseEventArgs`. Legacy value unchanged (`Bool(true)`).
- **Tooling metadata**: `ArgsChain` gained `RUST_TYPE` (the type a handler declares: `"EmptyEventArgs"` for the
  root, the alias for `ValueChangedEventArgs<T>` through `IntoLegacyValue::ARGS_TYPE`: `TextChangedEventArgs`,
  `CheckedChangedEventArgs`, `NumericValueChangedEventArgs`, `SelectionChangedEventArgs`) and `WRITABLE` (the
  derive's `handled`/`cancel`); `EventMeta` gained `args_rust`/`args_mut`, exported as `args_rust_type`/
  `args_mut` (C# `EventMeta.ArgsRustType`/`ArgsMut`, fixture regenerated). `EventArgs`, `ArgsChain` and
  `Component` carry `#[diagnostic::on_unimplemented]` messages ("`u32` is not an event args type", "… is not a
  control a `Sender` can be typed with").

### The macro (`kubuno-views-macros`, `syn` now with `full`)

`#[event_handlers]` on an inherent impl (generic impls supported) re-emits the impl without the `#[handler]`
helper attributes, adds `#[allow(unused_variables)]` to each handler (a designer stub ignores its sender and
args, as in WinForms), and generates `impl EventSink` (a `match` on the handler name, one arm per method; the
arm builds the typed sender with `cx.typed_sender::<C>()` and calls the method inside `with_args::<A>()`, with
`quote_spanned!` so trait errors point at the user's types). Every method with a `&mut self`/`&self` receiver is
a handler; associated functions without `self` are left alone; `#[handler(skip)]`, `#[handler(name = "…")]`.
Accepted after the receiver: nothing, `e: &A`/`&mut A`, `&dyn EventArgs`/`&mut dyn EventArgs`, `sender:
&Sender<C>`/`&ElementRef`, or sender then args. Compile errors (each with the expected shape): trait impl,
arguments on the attribute, async (EVT-6), generics, a return type, `self` by value, more than two parameters,
args or sender by value, `&mut Sender`, `Sender` without a type, the sender after the args, two args, a trait
object other than `dyn EventArgs`, duplicate handler names, unknown `handler` option. On an error the impl and an
empty `EventSink` are still emitted, so only the real mistake is reported.

### Language server (`kubuno-views-ls`)

- **`code_behind.rs`**: a literal/comment-aware scanner (brackets, the `#[event_handlers]` impl, the single
  `impl ViewModel for X`, the `handlers!` table and its `"name" => |a, b| body` entries, the prelude import
  point, the 5-argument `.frame(` calls).
- **`kubuno/createHandler`**: in a file with a typed impl, the stub is a method appended to it (after a blank
  line) — `fn on_ok_click(&mut self, sender: &Sender<Button>, e: &MouseEventArgs) { // TODO }`: sender typed with
  the element's control type (`&ElementRef` for a structural element), args from `EventMeta::args_rust`
  (`&dyn EventArgs` for the root, `&mut` when `args_mut`); `use kubuno_views::prelude::*;` is added when
  missing; no table entry. A legacy file keeps the legacy stub (§5.4 point 3). The C# `HandlerCreationService`
  now places the caret on the new `fn` even when an earlier edit (the import) shifts it.
- **Convert (§5.4 point 4)**: `textDocument/codeAction` (capability advertised) returns one `refactor.rewrite`
  action, *Convert the handlers! table to typed handlers*, on a `.kbview` whose code-behind can be converted;
  `kubuno/convertHandlers { uri }` returns the same `{ edit, converted, reason }`. Each entry becomes a method of
  a new `#[kubuno_views::event_handlers] impl <ViewModel>` placed after the `impl ViewModel` (or appended to an
  existing typed impl): `let vm = self;` and `let value = e.legacy_value();` (each only when the closure bound
  it; no `e` parameter when the value was ignored), then the closure body; a name that is not an identifier (or
  is `get`/`set`…) gets `#[handler(name = "…")]`. The table is emptied (`handlers! {}`) so its function keeps
  compiling, `.frame(` calls with five arguments in the sibling files become `.frame_typed_with(`, the prelude
  import is added. CRLF files stay CRLF.

### Templates

*Kubuno Desktop Application*: `main_view.rs` imports the prelude and declares `#[kubuno_views::event_handlers]
impl MainViewModel { fn on_hello_click(&mut self, sender: &Sender<Button>, e: &MouseEventArgs) }` (the view's
`OnClick="on_hello_click"`); `main.rs` paints with `runtime.frame_typed(canvas, frame, &mut view_model, body)`
(no `handler_table`). *Kubuno View* item: the same shape (`on_action_click` on `State`).

### Not built (EVT-5)

The ⚡ tab's compatible-handlers dropdown (`GetCompatibleMethods`), a designer menu command for the conversion
(the LS request exists), rename/remove-stub, the missing-handler diagnostic; `#[deprecated]` on `handlers!`
(§5.4 point 5).

### Tests

Rust: `events::typed` (9: the `HANDLERS` table, typed args and sender, bindings resolved in sender props,
`handled` written back, `cancel` bridged from a derived cancelable args, base/`dyn` args, mismatches skipped but
claimed, legacy table behind the sink with the legacy value, a plain view model unchanged), a node test where a
real `ButtonNode` click reaches typed methods in the WinForms order with a typed sender and `MouseEventArgs`
beside a legacy entry, `controls` (one type per control), `ElementProps` doctest; macro unit tests on the
expansion and on every error message (4), 8 `compile_fail` doctests and a passing example; all previous tests
unchanged (478 unit tests in `kubuno-views`). LS: `code_behind` (7), `handler_insert` typed round trips (4:
typed method in the impl, signatures per event — MouseDown, KeyDown `&mut`, TextChanged, Validating `&mut
CancelEventArgs`, Switch toggled, GotFocus `&dyn EventArgs`, root Load —, structural element, prelude added
and empty impls), `convert_handlers` (3: the previous template converted exactly, append to an existing typed
impl in a CRLF file, reasons), an LSP round trip (`createHandler` typed, `codeAction` absent/present,
`convertHandlers`). C#: caret position of a typed stub past the added import, `ArgsRustType`/`ArgsMut` from the
fixture (292 Designer tests). `tools/test-templates.ps1 -Run` passes with the typed template.

## 12. EVT-5 as built (2026-09-29)

In `Z:\src\desktop\windows` (`kubuno-views-ls` only, uncommitted there) and in this repository. `kubuno-views`,
`kubuno_ui` and the macros are unchanged.

### Language server (`kubuno-views-ls`)

- **The handler set of a view** (`handlers.rs`, `CodeBehind`): the union, over the `.rs` files of the view's
  folder, of the methods of the `#[event_handlers]` impls (`code_behind::impl_methods`: name, `#[handler(name =
  "…")]`, `#[handler(skip)]`, receiver, the parameters classified as `AnySender` (`&ElementRef`,
  `&Sender<AnyElement>`), `Sender(C)`, `AnyArgs` (`&dyn EventArgs`) or `Args { ty, generic }`, the body's braces,
  the item's start with its doc comments/attributes) and of the `handlers!` table entries (now with their offsets).
  It is *unknown* — and no handler is ever reported missing — when no typed impl or table is found, or when a file
  builds handlers in a way the scanner cannot read (`HandlerTable::new(`, `insert_typed(`, an unparsable table).
- **Compatibility** (`accepts`) follows the runtime (`with_args`, `typed_sender`), not §5.3's chain rule: a
  `Sender<C>` must name the element's own control; args must be absent, `&dyn EventArgs`, `EmptyEventArgs`, the
  event's `args_rust` (`ValueChangedEventArgs<String>` is read as `TextChangedEventArgs`, etc.), `CancelEventArgs`
  for a cancelable event, `HandledEventArgs` for a handled one. A legacy table entry accepts every event.
- **`openFiles`** (`sources.rs`): every handler request (`createHandler`, `convertHandlers`, and the three new
  ones) takes the client's open buffers (`uri → text`), installed for the request and read instead of the disk,
  so the edits' offsets match the editor (this also fixes two `createHandler`s in a row against an unsaved
  code-behind). Sibling views are read from the server's open documents first (an unsaved new view included).
- **`kubuno/compatibleHandlers { uri, elementId, event }`** → `{ handlers }`: typed methods first (declaration
  order), then table entries.
- **`kubuno/renameHandler { uri, old?, new, position?, rustRenamed? }`** → `{ edit, oldName, reason }`: every event
  attribute naming `old` in the folder's views (`edit::set_attribute`, under the name the file uses — an alias
  stays an alias), the method name (or its `#[handler(name)]` string), `.old(` calls, the table entry's string, the
  identifier it forwards to and the legacy `fn old`. Refused (with `reason`) when `new` is not a method identifier
  or is already taken by any `fn` of the folder. From a `.rs` file, `position` gives the method under the cursor;
  `rustRenamed` (rust-analyzer already renamed the Rust side) limits the edit to the views and the strings, and a
  method bound under `#[handler(name)]` is left alone (the views name the string).
- **`kubuno/removeHandler { uri, elementId, event }`** → `{ edit, handlerName, removedStub }`: removes the
  attribute as written (canonical or alias); when that was the handler's only reference in the folder's views and
  its body is still the stub (`// TODO: implement <any name>` — a renamed stub keeps its original comment —, the legacy `let _ = (vm, value);`, or empty), deletes the
  method with its doc/attributes and one adjacent blank line — or the legacy `fn` and its forwarding table entry
  (only when the entry is exactly `name(vm, value)`).
- **Diagnostics**: an event attribute naming a handler the set does not have is a WARNING `missing-handler`
  ("handler `x` not found in the code-behind"); one whose every declaration refuses the event is a WARNING
  `incompatible-handler` ("… cannot take the arguments of `OnMouseUp` (`MouseEventArgs` from a `Button`)"). Both
  carry `data { handler, elementId, event }`. The main loop now wakes every 1.5 s (`recv_timeout`) and republishes
  the diagnostics of an open view when the (modified time, length) of its folder's `.rs` files changed — a
  handler added or renamed in Rust and saved clears or raises the warning without touching the view.
- **Code actions** (`quick_fixes`, computed from the document, not from the client's `context.diagnostics`):
  *Create handler `x`* (`handler_insert::insert_handler_stub`, the code-behind edit only, marked preferred), *Use
  `closest`* (a compatible handler within `max(2, len/3)` edits), *Use `OnCheckedChanged`* on an older event name
  (§10's hint); then the EVT-4 conversion.
- **F2 in the XML**: `renameProvider` with `prepareRename` (the value of an event attribute) and
  `textDocument/rename` → the same rename; a refusal is a `RequestFailed` error carrying the reason.
- **`kubuno/createHandler`** with a `suggestedName` that is already a handler binds the event to it (the
  attribute only) instead of creating `name_2`: what picking an existing handler in the dropdown sends.

### Visual Studio

- **⚡ tab**: each event row's converter (`HandlerNamesConverter`) lists `kubuno/compatibleHandlers` (asked
  synchronously when the dropdown opens, 2 s timeout, cached per buffer version for 3 s; not exclusive, a new name
  can be typed); `KbviewEventBindingService.GetCompatibleMethods` returns the same list (the event descriptor now
  knows its element). Setting a row: empty → `kubuno/removeHandler`; unbound → `createHandler` (an existing
  handler is bound); same name → show it; a compatible handler → rebind (`setAttribute`); another name →
  `kubuno/renameHandler` (like WinForms, which renames the method when its name is edited in the grid). Reset =
  remove. No separate "Rename handler…" row menu: the grid's own cell is the rename gesture.
- **Applying** (`WorkspaceEditApplier`, `VsWorkspaceFileHost`): every file of the answer is planned first (no
  overlap, fits its text), then applied as ONE edit per file through the document's buffer when it is open in any
  editor (running document table: the designer's `.kbview`, a code window's `.rs`) — never reopening it —, else
  spliced into the file on disk. The open `.rs` of the view's folder are sent as `openFiles`.
- **Rust editor rename**: `RustAnalyzerMiddleLayer` intercepts `textDocument/rename`, lets rust-analyzer answer,
  then asks `kubuno/renameHandler { position, rustRenamed: true, openFiles }` (when the Kubuno Views server is
  running, i.e. a view was opened in this session) and merges its edits into rust-analyzer's `WorkspaceEdit`
  (`RenameEditMerger`: appended to the same file's `TextDocumentEdit`, else a new one with `version: null`; or into
  `changes`).
- **Context menu**: *Convert to Typed Handlers* / *Convertir en gestionnaires typés* (`cmdidDesignerConvertHandlers`
  0x0205, view menu: right-click on the canvas background) → `kubuno/convertHandlers`, result in the status bar.
- Double-click on a control still creates/opens its default event's handler (§10).

### Deviations and limits

- The scan is textual, like EVT-4's (no `syn` in the server); only the folder of the view is considered, and a
  rename does not follow other call paths than `.name(` calls.
- A Rust-editor rename updates the views only when the Kubuno Views server is running; otherwise the view gets the
  missing-handler warning (and its quick fixes) as soon as it is opened.
- Warnings follow the code-behind on save (disk), not on each keystroke in the Rust editor.

### Tests

LS: `code_behind` (+2: methods of a typed impl with every parameter shape and attribute, `fn`/identifier search),
`sources` (1), `handlers` (10: a renamed stub still removed, compatibility by sender/args/cancel, the open buffer used, rename across two views and
the method, rename after rust-analyzer, legacy rename, removal of a typed stub with its blank line and of a legacy
stub with its entry, a shared or edited handler kept, missing/incompatible warnings + quick fixes + the alias fix +
an unknown set, F2), and an LSP round trip (dropdown on an unsaved buffer, quick fix, rename both ways, F2
refusal, removal, the warning cleared by a saved code-behind); 117 unit + 17 round-trip tests, clippy clean. C#:
the ⚡ row semantics (rebind/rename/remove, alias), the dropdown and `GetCompatibleMethods`, the answers' parsing,
the applier (buffer = one edit, disk, nothing applied on a bad edit), the rename merge (both `WorkspaceEdit`
shapes), the context-menu command (298 Designer tests). The C# registry fixture was regenerated from the real export
after EVT-6 (FormClosing/FormClosed). `tools/test-templates.ps1 -Run` passes.

### Live verification (regular Visual Studio, 2026-09-29)

A new *Kubuno Desktop Application* (`Evt5App`, from the installed template through DTE), with two extra methods
(`any_click(&mut self)`, `switch_only(&mut self, sender: &Sender<Switch>, e: &CheckedChangedEventArgs)`) and a second
button `OnClick="missing_one"`:

- the Error List and the XML editor showed the warning *handler `missing_one` not found in the code-behind* (line 15,
  squiggle, light bulb); the light bulb's *Create handler `missing_one`* added the typed stub to the open `main_view.rs`
  buffer; once saved, the warning disappeared without touching the view;
- the ⚡ Click row of that button showed a dropdown listing `on_hello_click`, `any_click`, `missing_one` — not
  `switch_only`;
- typing `other_click` in that row renamed the handler in the view and in the Rust method (both buffers edited,
  unsaved); the project built;
- F2 (Refactor.Rename) on `other_click` in the Rust editor, renamed `other_pressed`: rust-analyzer renamed the method
  and the view's `OnClick` followed;
- clearing the row removed the attribute and the stub (with its blank line); the project built — **found live**: the
  renamed stub kept its `// TODO: implement missing_one` comment and was not recognized as a stub; the check now accepts
  any name there;
- a Rust hover (`ViewModel`) showed the C#-style Quick Info (signature, `kubuno_views::binding`, the documentation);
- a fresh *Kubuno Desktop Application* built, and after *Restart rust-analyzer* the Error List and the editor showed no
  diagnostic.

Not exercised live: *Convert to Typed Handlers* in the design surface's menu (registered in the view menu; the
conversion itself is EVT-4's, unit- and round-trip-tested) and F2 in the XML editor (round-trip-tested).

## 13. EVT-6 as built (2026-09-29)

In `Z:\src\desktop\windows` (uncommitted there): `kubuno-controls` (host), `kubuno-views` (runtime, events),
`kubuno-views-macros`. `kubuno_ui` is unchanged; every exe was rebuilt (`build-all`) and restaged. Applications that
use none of this behave as before (the host changes are opt-in per frame).

### Host (`kubuno_controls::host`)

- **Wake-up from any thread**: `UiWaker` (`Send + Sync + Clone`, `host::ui_waker()` on the UI thread, usable before
  the window exists) posts `WM_KUBUNO_WAKE` (`WM_APP + 0x4B51`) to its thread's host window, coalesced (one in the
  queue at a time; a process-wide registry maps UI thread → window, filled at `WM_NCCREATE`, emptied at
  `WM_NCDESTROY`). `is_ui_thread()` is `!InvokeRequired`.
- **Frames for work, not looks**: the host asks for a frame with an invalidation when the window can paint, and
  **renders directly when it is minimised or hidden** (no `WM_PAINT` would come) — for `WM_KUBUNO_WAKE`, for the new
  `request_wake_after(ms)` (a second one-shot `SetTimer`, id `0x4B56`, same per-frame "shortest wins" rule as
  `request_repaint_after`, which keeps its paint-only meaning) and for a close request. A render re-entered from a
  modal loop is turned into an invalidation (`Host::rendering`).
- **Deferred close**: `defer_close()` is a per-frame declaration (reset at each frame start, like the cursor). When
  the last frame declared it, `WM_CLOSE` (after `set_close_handler`, which still decides first; `quit` still
  bypasses everything) queues `InputEvent::CloseRequested(CloseReason)` for the next frame instead of destroying the
  window (`InputEvent` is `#[non_exhaustive]`, so this is additive). `CloseReason::{UserClosing,
  ApplicationExitCall, WindowsShutDown, TaskManagerClosing}`: a plain `WM_CLOSE` (caption, Alt+F4, task bar,
  `close_window`) is `UserClosing` — as WinForms gives `Form.Close()` —, `request_close(reason)` marks the `wParam`
  (`0x4B55_000r`), `WM_QUERYENDSESSION` renders a frame **synchronously** with `WindowsShutDown` and answers `FALSE`
  when the page called `cancel_close()`. **Safety net**: a `CloseRequested` nobody consumed by the end of its frame
  closes the window (`quit`), so a page that stops deferring (the template's "waiting for the view to compile"
  branch) can always be closed. `WM_EXITSIZEMOVE` invalidates once (the view's Move after a drag).
- Not changed: activation is still read from the focus edges (`Frame::window_focused`): for a top-level window
  `WM_ACTIVATE` and `WM_SETFOCUS`/`WM_KILLFOCUS` coincide, and an embedded window never gets `WM_ACTIVATE`.

### Runtime (`kubuno_views::runtime::Runtime`)

- **Frame order** (module doc): (1) a close request (host's, or `Runtime::close(reason)`) → FormClosing on the
  root (cancelable, `FormClosingEventArgs { reason, cancel }`; Rust subscribers: `runtime.form_closing()` after
  the XML handler); not cancelled → FormClosed → Deactivate (if the window was active) → `shutdown()` →
  `host::quit()`, and the frame only paints; cancelled → `host::cancel_close()`; (2) the router's first half (Load
  → Activated on the first frame, input); (3) the dispatcher's closures in posting order, the due timer ticks, the
  woken async tasks; (4) paint, the router's second half (Shown); (5) the first poll of tasks spawned by this
  frame's handlers (+ a 1 ms repaint so their changes show), `request_wake_after` for the next due timer/delay,
  `defer_close()`. Deviation from §3: Load/Activated/Shown were EVT-2's; the root also raises **Move /
  LocationChanged when the window moves on screen** (`Frame::client_origin`).
- **Graceful shutdown** (`Runtime::shutdown`, idempotent, also in `Drop`): async tasks dropped at their `.await`
  (their `JoinHandle`s report `Cancelled`, `UiHandle::update` returns `None`), dispatcher closed (queued closures
  dropped, blocked `invoke`s released with `DispatchError::Closed`, new posts refused), timers stopped. A closed view
  raises nothing any more.
- `VIEW_EVENTS` gained **OnFormClosing** (`FormClosingEventArgs`, cancelable) and **OnFormClosed**
  (`FormClosedEventArgs`), category Behavior, with French docs; the export, the LS and the ⚡ tab get them from the
  registry (the C# fixture `registry.sample.json` was not regenerated: it belongs to the C# side).
- A host abstraction (`runtime::HostPort`, crate-private) lets the unit tests drive the whole frame with a fake host
  and a fake clock (`run_frame` with no canvas: everything but the paint).
- `frame_typed`/`frame_typed_with` now require `V: 'static` (the closures and tasks reach `&mut V` through `Any`).

### `UiDispatcher<V>` (`events/dispatcher.rs`)

`runtime.dispatcher::<V>()` (or `UiHandle::dispatcher()`): `begin_invoke(FnOnce(&mut V) -> R + Send) ->
AsyncResult<R>` (queue + `UiWaker`), `invoke(f) -> Result<R, DispatchError>` (blocks the worker), `is_ui_thread`,
`invoke_required`, `is_closed`. `AsyncResult<R>` is the oneshot: `wait`, `wait_timeout`, `try_take`,
`is_completed`, and `Future`. `DispatchError::{Closed, OnUiThread, WrongViewModel, Timeout}`. **Deviations**: `invoke`
on the UI thread returns `OnUiThread` at once rather than running inline (the view model is already mutably borrowed
there — use it); closures need the typed frame (`frame_typed`): under the untyped `frame` they report
`WrongViewModel`. Lock poisoning is ignored (single-operation critical sections), no `unwrap`.

### Executor and async handlers (`events/executor.rs`)

- One executor per runtime (`UiContext`: tasks, delay timers, frame clock), polled at steps 3 and 5 (up to 8 passes
  for tasks woken meanwhile; a poll longer than 50 ms logs a warning with the task/handler name). Wakers are
  `Send + Sync`: they mark the task ready and wake the host, so a task always resumes on the UI thread.
- `spawn_local(future) -> JoinHandle<T>` on the current view (a thread-local "current runtime" set during frames;
  outside a frame the future is dropped with a warning), `Runtime::spawn_local` from outside. `JoinHandle<T>`:
  `Future<Output = Result<T, Cancelled>>`, `cancel`, `is_finished`, `is_cancelled`, `try_take`; dropping it detaches.
  `delay(Duration)` (on the frame clock; a helper-thread sleep outside a view), `yield_now()`.
- `UiHandle<V>`: `update(|vm| …) -> Option<R>` lends `&mut V` for the closure. The runtime lends the view model to
  the tasks it polls through a scoped thread-local pointer (`with_vm_scope`, the only `unsafe` of the feature): taken
  out while an update runs, so a nested `update` gets `None` rather than a second `&mut`; the type and the runtime are
  checked. `None` too after close. `dispatcher()`, `is_closed()`, `UiHandle::current()`.
- **Async handlers** (`#[event_handlers]`): an `async fn` is a handler; it takes no `self` (error: "cannot borrow the
  view model across `.await`: take `ui: UiHandle<Self>`"), an optional `ui: UiHandle<Self>` then an optional copy of
  the args **by value**. `&mut A` / `&A` / a sender / a trait object are compile errors; writable args by value
  (`FormClosingEventArgs`, `KeyEventArgs`) are refused by the new marker trait **`ReadOnlyArgs`** (implemented by the
  derive for args without `handled`/`cancel`, and by `EmptyEventArgs`/`ValueChangedEventArgs<T>`), whose
  `on_unimplemented` message says to set them in a synchronous handler — the §6 rule, enforced. The generated arm
  clones the args (`typed::copy_for_async`), takes `UiHandle::<Self>::current()` and spawns the handler's future named
  after it; its first poll is in the same frame (step 3 or 5). `HandlerInfo::asynchronous`. An `async fn` helper that
  is not a handler needs `#[handler(skip)]`.

### `Timer` (`events/timer.rs`)

`Timer::new(name)` (interval 100 ms, disabled, like WinForms), `with_interval`/`set_interval` (restarts counting),
`with_handler`/`set_handler` (the view-model handler a tick runs, as `OnTick="…"` would; sender element `"Timer"`,
`x:Name` = the timer's name), `start`/`stop`/`set_enabled`, `tick()` (`Event<EmptyEventArgs>` for Rust subscribers),
`runtime.add_timer(&t)` / `remove_timer`. Ticks run at step 3; late ticks coalesce into one (like `WM_TIMER`); the next
tick is due `interval` after the one that ran; the runtime wakes the host for it (minimised included). Not built: a
`<Timer>` XML element / component tray (EVT-7).

### Macro robustness (product-owner report during EVT-6)

- `HandlerInfo` is built by `const` builders (`HandlerInfo::new(name, method).with_sender(..).with_args(..,
  mut).asynchronous()`), never a struct literal: a field added to the runtime type no longer breaks code expanded by
  a stale macro build (the `E0063: missing structure fields: asynchronous` rust-analyzer showed mid-change).
- Handler parameters are exempted from the unused lint by touching each one once at the top of the body
  (`let _ = &e;`, spanned at the parameter) instead of `#[allow(unused_variables)]` on the method, so an unused
  `let` in a handler's body still warns. Done for every handler candidate even when its signature is wrong, so one
  mistake does not also flag its parameters.
- An impl that does not parse (a syntax error in a method body being typed) is **recovered**, not rejected: its
  header and every item of its body that parses on its own are expanded as usual (so the other handlers, the
  `EventSink` and `frame_typed(…, &mut vm, …)` elsewhere stay valid), and a method whose body is broken keeps its
  signature with an `unreachable!()` body. The macro adds no error of its own: rustc parses the item before
  expanding it and rust-analyzer parses the file, so each reports the syntax error once, at its token (adding syn's
  copy showed it twice; re-emitting the broken tokens made rust-analyzer add "Syntax Error in Expansion" on the
  attribute and a spurious E0282). Anything that is not an impl still gets "goes on the impl block", at its first
  token.
- Checked with rustc and `rust-analyzer diagnostics` (1.98.1) on the scratch project below: a correct project has
  no diagnostic (only the workspace's usual `inactive-code` hints); with `let x = ;` in one handler and an unused
  `let` in another, rustc reports exactly the syntax error and the unused local (no unused parameter), and
  rust-analyzer only its own parser's error at that token.
- rust-analyzer loads the compiled proc-macro dylib: after the macro crate changes, rebuild and **restart
  rust-analyzer** (VS: *Redémarrer rust-analyzer*) or its expansion stays the old one.

### Tests

`kubuno-views` (all 503 unit tests pass): `events::dispatcher` (6: posting from threads, order and typed results,
`invoke` blocking a worker while the UI thread pumps, `invoke` on the UI thread refused, close drops queued closures
and releases a blocked worker, wrong view model type, `wait_timeout` and awaiting the result), `events::timer` (3:
ticks on a fake clock, coalescing, interval change/stop/restart, Rust subscriber stopping its timer), `events::router`
(4: window Move, FormClosing → FormClosed → Deactivate, cancelled closing then a later one, no Deactivate when
inactive), `runtime` (12, fake host + fake clock through the real frame: lifecycle order Load → Activated → Shown then
FormClosing → FormClosed → Deactivate + quit + consumed request + no more deferral; cancel keeps the view then
`Runtime::close(ApplicationExitCall)` closes it; Rust `form_closing` subscriber cancels; 3 threads × 5 closures in
per-thread order + `invoke` result; close releases workers; drop = shutdown; untyped frame → `WrongViewModel`; timer
wake-up requests and coalescing; an async `OnLoad` handler awaiting a 1 s delay then completing; an async handler
getting its args copy from a timer; close cancels tasks/handles and disables `UiHandle`; nested `spawn_local`, nested
`update` refused). Macros: 4 new unit tests (async expansion, async errors ×8, parameters-only exemption, syntax-error
recovery and item splitting) + 2 doctests (an async handler compiles; `FormClosingEventArgs` by value in an async
handler does not — checked to fail on `ReadOnlyArgs`, with the message pointing at the user's type).
`kubuno-controls` host: 4 new tests (per-frame wake/close declarations, close request queued once and reported until
consumed, reason round trip through `wParam`, waker thread identity). `kubuno-views-ls` tests unchanged and passing.
`cargo clippy --all-targets -D warnings` clean on the three crates; no `unwrap` outside tests. The whole workspace
(`--bins --examples`) builds, the runtime was restaged, and gallery, drive, documents and chat start and close on
`WM_CLOSE` (the shell hides to its tray, as its close handler says).

### Live verification (2026-09-29)

A scratch *Kubuno Desktop Application* made from the template (`C:\kubuno-build\evt6`, built with cargo, run from its
target directory): the root names `OnLoad`/`OnShown`/`OnActivated`/`OnDeactivate`/`OnFormClosing`/`OnFormClosed`;
`main.rs` starts a thread that calls `dispatcher.begin_invoke` every second; a button runs `async fn
on_run_async_click(ui: UiHandle<Self>, e: MouseEventArgs)` (update, `delay(1.5 s).await`, update). From the app's log:
Load (status set) → Activated → Shown on the first frame; one dispatcher closure per second, on the UI thread
(posted from a non-UI thread); **4 ticks ran while the window was minimised** (no `WM_PAINT` then: the wake-up
renders directly); the async handler (Tab to the button, Enter) logged "started" then, 1.50 s later, "completed,
update applied = true"; a `WM_CLOSE` with unsaved changes raised FormClosing (UserClosing, dirty) and the window
stayed; "Discard and close" (`host::close_window`) raised FormClosing (not dirty) → FormClosed → Deactivate and the
process exited; a `WM_CLOSE` without changes closed at once in the same order. Synthetic mouse clicks posted to a
background window do not work for any host app (the host's `TME_LEAVE` fires at once because the real cursor is
elsewhere, so the release lands "outside"); the keyboard was used instead.

## Scope note (product owner, 2026-09-29)

Changing `kubuno_ui` (and `kubuno-controls`) is explicitly in scope whenever the event system needs it — e.g. real input events (mouse/keyboard/focus/validation sequences), hit-testing, Paint and drag-and-drop hooks belong in the widgets/host that execute them, not in a layer bolted on top. Constraints when doing so: `kubuno_ui` is a dylib without a stable ABI — after any change, rebuild every exe (`tools/build-all.ps1`) and restage; keep the gallery and the desktop apps (shell, drive, documents, chat) building and behaving; add CHANGELOG entries in the desktop repo.

## Requirement — WinForms-style overridable control methods ("OnPaint" family) (product owner, 2026-09-29)

Custom controls (EVT-7) and user code must be able to override the WinForms `protected virtual On…` family, not only subscribe to events. Rust shape: a `Control` trait with default methods (the "base" behaviour, callable explicitly like `base.OnPaint(e)`), implemented by `#[derive(Component)]` controls and optionally by subclass-like wrappers of existing controls:
- **Painting**: `on_paint(&mut self, e: &mut PaintEventArgs)` with a WinForms-`Graphics`-like API over kubuno's `Canvas` (lines, rects, rounded rects, ellipses, arcs, paths, gradients, images, text with layout/measure, clip, transforms, save/restore, antialias), `on_paint_background`, `invalidate()` / `invalidate_rect()` / `refresh()`, double-buffered by default (like `DoubleBuffered`), `ControlStyles`-equivalent flags (UserPaint, Opaque, ResizeRedraw, Selectable, SupportsTransparentBackColor…). `PaintEventArgs` becomes real in EVT-8 (borrowed canvas via a scoped callback instead of `Any`).
- **Owner-draw** for list-like controls: `DrawItem`/`MeasureItem` (ListBox, ComboBox, ListView, TreeView, DataTable cells, tabs, menus) with `DrawMode::OwnerDrawFixed/Variable`.
- **Input/lifecycle overrides**: `on_mouse_down/up/move/enter/leave/hover/wheel`, `on_click`, `on_key_down/up/press`, `process_cmd_key`/`process_dialog_key`, `is_input_key`, `on_got_focus/lost_focus/enter/leave`, `on_validating`, `on_resize/size_changed/layout/move`, `on_visible_changed/enabled_changed`, `on_handle_created`/`on_create_control`, `on_load`, `dispose`. Each default method raises the corresponding event (WinForms rule: `OnClick` raises `Click`), so overriding and subscribing compose.
- **Low level**: a `wnd_proc(&mut self, msg: &mut Message) -> bool` hook (message pre-filter on Windows; platform-neutral subset on other backends) and `create_params`-like window options for native-hosted controls.
- **Layout**: `get_preferred_size`, `on_layout`, `set_bounds_core` equivalents for custom containers.
- Designer: overridden paint is what the designer shows (it renders through the project's own kubuno_ui.dll / design host); `DesignMode` flag available to the control.
Implemented across EVT-7 (Component trait + overrides + registry) and EVT-8 (Paint/Graphics, owner-draw, drag-drop); changes to `kubuno_ui`/`kubuno-controls` expected (host invalidation, double buffering, message hook).

## Requirement — tooling that goes with custom controls & overrides (product owner, 2026-09-29)

Everything WinForms/VS provides around custom controls must have a Kubuno equivalent:
- **Override assistance**: completion inside `impl Control for X` listing overridable `on_…` methods with their exact signatures (like typing `override` in C#), a quick action "Substituer des membres…" (Override members dialog with checkboxes), and "Implémenter le trait" for required items; generated bodies call the base behaviour by default.
- **Item templates**: Kubuno Custom Control (owner-drawn `Control` with `on_paint` skeleton), Kubuno User Control (`.kbview` + code-behind usable as a control), Inherited control (wrap/extend an existing control), Component (non-visual, like WinForms components in the tray).
- **Toolbox integration**: project controls appear automatically in a "<Project> Components" tab after build (like WinForms), with their icon (`#[toolbox(icon = "...")]`), plus "Choisir des éléments…" to add controls from other crates.
- **Design-time metadata attributes** (WinForms `ComponentModel` equivalents): `#[category]`, `#[description]`, `#[default_value]`, `#[browsable]`, `#[default_event]`, `#[default_property]`, `#[designer_serialization_visibility]`, `#[localizable]`, `#[editor(...)]`, `#[type_converter(...)]`; `DesignMode` flag at runtime.
- **Property editors** in the Properties window: colour picker, font picker, image/resource picker, enum flags, collection editor (items, columns, tabs), string list, anchor/dock (done), plus custom editors a control can provide; **smart tags / designer verbs** (the ▸ action panel on a selected control, e.g. "Modifier les colonnes…", "Ancrer dans le conteneur parent").
- **Non-visual components tray** under the design surface (timers, background workers, data sources), like WinForms' component tray.
- **Resources**: `.kbres` (resx-like) for images/strings/icons with a resource editor, localizable views (per-culture resources), and typed access from Rust.
- **Debugging & diagnostics for painting**: a "paint debug" overlay (flash invalidated regions, show layout bounds/padding, FPS/frame time), a debugger visualizer for `Rect`/`Color`/images/`Value`, natvis for kubuno types, and Output-pane tracing of event dispatch (optional, like WPF trace sources).
- **Code navigation**: Class View / Object Browser-like listing of controls, their properties/events/overridable methods from the registry; Go to definition from `.kbview` element → Rust control type.
- **Snippets**: `onpaint`, `event`, `handler`, `prop` (bindable property with change notification).

## Requirement — cascading control hierarchy rooted in Component/Control equivalents (product owner, 2026-09-29)

Mirror the WinForms hierarchy (System.ComponentModel.Component → System.Windows.Forms.Control → …) in idiomatic Rust (no class inheritance: **trait hierarchy + embedded base state + generated delegation**):
- Levels: `Component` (site/container, `DesignMode`, `Disposed` event, non-visual components) → `Control: Component` (bounds, anchor/dock, visible/enabled, parent/children, focus, all `on_…` overrides + events, invalidate/paint) → `ScrollableControl` → `ContainerControl` → `UserControl` / `View` (the Form equivalent: Load/Shown/Closing). Intermediate bases: `ButtonBase` (Button, CheckBox, RadioButton, IconButton, Switch), `TextBoxBase` (TextField, TextArea, MaskedField, SearchField), `ListControl` (ListBox, ComboBox, CheckedListBox, Dropdown), `ScrollBarBase`, `LabelBase`, `ContainerBase` (Panel, GroupBox, Card, Stack…), etc. — every existing Kubuno control placed in the tree.
- Each level = a trait with default methods (the "virtual" behaviour) + a plain base-state struct embedded in the concrete control; `#[derive(Component)]` with `#[kubuno(extends = ButtonBase)]` generates the delegation boilerplate and a `base()`/`base_mut()` accessor so an override can call the parent's implementation (like `base.OnPaint(e)`); trait upcasting (`&dyn Button` → `&dyn ButtonBase` → `&dyn Control` → `&dyn Component`, stable since Rust 1.86) gives polymorphism, plus `downcast_ref::<T>()`.
- User code extends any level: `#[kubuno(extends = Button)] struct RoundButton` overriding `on_paint`, inheriting all Button props/events.
- Metadata inheritance: the registry records the base chain; properties/events/default event are inherited and overridable; the Properties window shows inherited members, the Object Browser/Class View shows the hierarchy, `.kbview` validation accepts inherited attributes, and the Toolbox/`is`-checks (e.g. "any ButtonBase") use it.
- `kubuno_ui` widgets are refactored onto this hierarchy (they execute it); the XML views layer and the designer consume the same chain. Planned as part of EVT-7 (before custom controls), with a compatibility layer so current views keep working.

## 14. EVT-7a as built (2026-09-29)

The first half of EVT-7: the cascading control hierarchy of the requirement above ("rooted in Component/Control
equivalents") and the WinForms-style overridable methods ("OnPaint family"), in `Z:\src\desktop\windows`
(uncommitted there): `kubuno-views` (new `component/` module, `controls.rs`, router, nodes, `DesignSlot`, registry)
and `kubuno-views-macros` (`#[derive(Component)]`). `kubuno_ui` and `kubuno-controls` are **unchanged** (see
"Deviations"). Existing `.kbview` files, `handlers!` and typed-handler projects behave identically (pixel comparison
and the full test suite below). XML usage of custom controls, `<UserControl>`, the tooling and owner-draw are EVT-7b
and EVT-8.

### The hierarchy (`kubuno_views::component`)

```text
Component                (ComponentCore)          site, design_mode, dispose / Disposed
└─ Control               (ControlCore)            WinForms property replica (kubuno_controls::ControlBase), styles,
   │                                                focus, invalidation, every on_… override + event accessors
   ├─ ButtonBase         (ButtonBaseCore)         Button, IconButton, CheckBox, RadioButton, Switch
   ├─ TextBoxBase        (TextBoxBaseCore)        TextField, TextArea, MaskedField, SearchField
   ├─ ListControl        (ListControlCore)        ListBox, CheckedListBox, ComboBox, Dropdown
   ├─ LabelBase          (LabelBaseCore)          Label, LinkLabel, Badge
   ├─ RangeBase          (RangeBaseCore)          Slider, ProgressBar, NumericField   (the note's ScrollBarBase)
   └─ ScrollableControl  (ScrollableControlCore)  ScrollArea
      ├─ ContainerBase   (ContainerBaseCore)      Panel, GroupBox, Card, Stack, Tabs, Splitter, Accordion
      └─ ContainerControl (ContainerControlCore)
         ├─ UserControl  (UserControlCore)        (EVT-7b)
         └─ View         (ViewCore)               the Form: on_load/shown/activated/deactivate/form_closing/form_closed
```

The other controls (`Icon`, `Separator`, `Spinner`, `Callout`, `EmptyState`, `Toolbar`, `Breadcrumb`, `Stepper`,
`ListView`, `TreeView`, `DataTable`, `MonthCalendar`, `DatePicker`, `ColorField`) derive `Control` directly; the eight
structural elements (`Item`, `Column`, `TabItem`, `Option`, `Step`, `AccordionSection`, `BreadcrumbItem`, `ToolbarItem`)
are non-visual `Component`s (`controls::items`, not in the prelude: one is named `Option`).

- **Each level = a trait with default methods + a core struct.** The cores nest (`ButtonBaseCore { control: ControlCore
  }`, `ControlCore { component, props: ControlBase, … }`) and `Deref` down (`core.text`, `core.bounds` are the
  replica's fields). Each **core is itself an object of its level** (it implements the level's traits with their
  defaults): it is the abstract base, so a class extending a level calls `self.base_mut().on_click(e)` like one
  extending a class.
- **`#[derive(Component)]` + `#[kubuno(extends = X, overrides(…), levels(…))]`** generates the hidden plumbing traits
  (`ComponentLink`: `as_any`, class name and chain, the base object, one upcast per level `as_button_base()`…;
  `Has<Level>Core` and `<Level>Link` per level: the shared core, the base object), `base()` / `base_mut()`,
  `Lineage::CHAIN` (built at compile time from the base's), `ClassInfo`, `ElementType` (so `Sender<RoundButton>`
  works), and an empty `impl` of each level trait of the chain except those in `overrides(…)`, which the class writes
  with its overrides. `extends` names a built-in class (the macro knows their levels) or a level (the base field is
  then the level's core); a class of your own as the base needs `levels(ButtonBase)`. The base field is `base` or the
  one marked `#[kubuno(base)]`; a check makes it the type `extends` names.
- **Delegation = virtual behaviour.** Every default method first delegates to the base object (`RoundButton` →
  its `Button` → the `ButtonBaseCore`); the root behaviour raises the event. So a method a class does not override
  runs its base's, an override that calls `self.base_mut().on_x(e)` raises the event (WinForms `base.OnX(e)`), and one
  that does not suppresses it. The hosts call every `on_…` on the outermost object, so overrides are virtual; a base
  reached by delegation calls its own methods on itself (the classic delegation limit): behaviour that must reach an
  override is a provided trait method called on the outer object (`perform_click`, `set_text`, `set_value`…).
- **Upcasts / downcasts**: trait upcasting (`&dyn ButtonBase` → `&dyn Control` → `&dyn Component`), `downcast_ref` /
  `downcast_mut` / `is::<T>` / `is_a("ButtonBase")` on every level's trait object, `find_base::<Button>()` (the object or
  its embedded base of that class, WinForms' `(Button)control`).

### The overridable methods (`Control`, and per level)

- **Argument shape.** `fn on_click(&mut self, e: &mut EventCx<'_, MouseEventArgs>)`: `EventCx` derefs to the args
  (`e.x`, `e.handled = true`) and carries the `RaiseSink` the caller lends for the call (the router's, a node's: the
  element's `.kbview` handler) — so an override stays one argument and `base_mut().on_click(e)` works. Raising order:
  the sink (the XML handler, WinForms' designer-generated subscription) then the control's Rust subscribers
  (`button.click().subscribe(…)`, held in the core's `EventMap`, created on first use); a sink handler that sets
  `handled` stops the Rust subscribers. `on_paint(&mut self, e: &mut PaintEventCx<'_>)`: `e.graphics` (the Kubuno
  `ControlCanvas`), `e.clip_rectangle`, `e.state` (hover/pressed/focus/disabled); it raises `Paint`
  (`PaintEventArgs { clip }`); the richer `Graphics` surface is EVT-8.
- **Control**: `on_paint`, `on_paint_background`, `on_click`, `on_double_click`, `on_mouse_click`,
  `on_mouse_double_click`, `on_mouse_down/up/move/enter/leave/hover/wheel`, `on_key_down/up/press`, `on_enter`,
  `on_leave`, `on_got_focus`, `on_lost_focus`, `on_validating`, `on_validated`, `on_resize` (raises Resize, then
  invalidates under `RESIZE_REDRAW`), `on_move`, `on_size_changed`, `on_location_changed`, `on_layout`,
  `on_visible_changed`, `on_enabled_changed`, `on_text_changed`, `on_handle_created`, `on_handle_destroyed`,
  `on_create_control`, `get_preferred_size`, `set_bounds_core` (honours `BoundsSpecified`), `is_input_key`,
  `is_input_char`, `process_cmd_key`, `process_dialog_key`, `wnd_proc`, `create_params`, and `on_event` (an event the
  level has no method for: `ItemActivate`, `StepSelected`…). Levels: `ButtonBase::on_checked_changed` (+
  `perform_click`), `ListControl::on_selection_changed` / `on_selected_value_changed`, `RangeBase::on_value_changed` /
  `on_scroll`, `ScrollableControl::on_scroll`, `TextBoxBase::on_read_only_changed`, `UserControl::on_load`,
  `View::on_load/on_shown/on_activated/on_deactivate/on_form_closing/on_form_closed`; `Component::dispose` /
  `dispose_core(disposing)`.
- **`Control::dispatch_event(name, e)`** delivers an event by attribute name to its method (the level methods through
  the class's upcasts), else `on_event`; args of another type than the method's (a `RadioButton`'s `CheckedChanged` is
  a `TextChangedEventArgs`) take `on_event` too. What the router, the nodes and `ControlHost` call.
- **Operations** (provided, not overridden): `name`/`set_name`, `text`/`set_text` (TextChanged, source Code), `bounds`/
  `set_bounds`, `visible`/`set_visible`, `enabled`/`set_enabled`, `focus()` (a request the host applies next frame),
  `can_focus`, `can_select`, `get_style`/`set_style`, `invalidate`/`invalidate_rect` (accumulated)/`update`/`refresh`,
  `create_control` (once: `on_create_control` then HandleCreated), `suspend_layout`/`resume_layout`/`perform_layout`,
  per level `set_selected_index`, `set_value` (clamped), `set_read_only`, `set_auto_scroll_position`… Event
  accessors: `click()`, `key_down()`, `validating()`, `paint()`, `checked_changed()`, `form_closing()`…
- **`ControlStyles`** (WinForms values): `Control` sets `USER_PAINT | STANDARD_CLICK | STANDARD_DOUBLE_CLICK |
  SELECTABLE | ALL_PAINTING_IN_WM_PAINT | USE_TEXT_FOR_ACCESSIBILITY | OPTIMIZED_DOUBLE_BUFFER` (Kubuno always paints
  double-buffered); `ButtonBase` clears `STANDARD_DOUBLE_CLICK` (a second quick click is a second Click) and adds
  `RESIZE_REDRAW`…; `LabelBase` and `ContainerBase` are not `SELECTABLE` (no tab stop), `LinkLabel` and `Tabs` are.
  Honoured by the hosts: `SELECTABLE`, `STANDARD_CLICK`, `STANDARD_DOUBLE_CLICK`, `RESIZE_REDRAW`, `OPAQUE`.
- **`DesignMode`**: `Component::design_mode()` from the `Site` (name, design mode, container); the view runtime sites
  every element (design mode when the frame records a layout map, i.e. in the designer).
- **`wnd_proc`** is a platform-neutral pre-filter: the router shows the target control Win32-shaped `Message`s
  (`WM_KEYDOWN/KEYUP/CHAR`, `WM_MOUSEMOVE`, `WM_xBUTTONDOWN/UP`, `WM_MOUSEWHEEL`; point in DIP relative to the control)
  before turning them into events; `true` consumes the message (a consumed press starts no capture and no Click).
- **Keys** (router, WinForms order): on a KeyDown, `wnd_proc`, then `process_cmd_key` on the focused control and each
  ancestor control; then, for a dialog key (Tab, arrows, Enter, Escape) the control does not take
  (`is_input_key`), `process_dialog_key` up the same chain. A `true` consumes the key (and its character): no KeyDown,
  no KeyPress. `is_input_char` false skips a character's KeyPress. Tab itself is consumed by the focus ring before the
  router; a hosted control that takes it (`is_input_key(Tab)`) is registered with `FocusOpts::wants_tab`.

### Where it runs

- **Views.** `compile::build_node` gives every element's `DesignSlot` an instance of its class
  (`controls::class_of(name).create`). Each paint the slot syncs the class's core (site with `x:Name` and design mode,
  name, bounds, focus id, focused), calls `create_control` once, lends the instance to the node's context
  (`PaintCx::control`) and registers it with the router (`register_with_control`). The router's raises
  (`raise_to`) and the nodes' own `fire` (which now names its event: `"OnClick"`, `"OnSelectionChanged"`,
  `"OnItemClicked"` for a toolbar/breadcrumb item…) go through `dispatch_event` with a sink that dispatches the
  element's handler and reports the `ViewEvent` exactly as before; the router reports a routed event after the
  override ran, only when a handler ran (unchanged). `<Button>`'s node syncs its resolved properties into its
  `controls::Button` (found with `find_base_mut`, so a class extending `Button` works in EVT-7b) and paints through
  the class's `on_paint` (which paints the same `kubuno_ui` button); the other nodes paint their widgets as before. A
  slot disposes its instance when dropped (hot reload, view closed).
- **Rust code.** `ControlHost` hosts controls built in Rust outside any view: `host.add(control) -> HostedControl<C>`
  (sited under its name, or a generated `label2`), then `host.frame(canvas, frame)` each frame — the same router
  (with `report_all`: every raised event is returned), its own focus ring (`SELECTABLE`, `tab_stop`,
  `is_input_key(Tab)`), `focus()` requests, hover/pressed state, `on_paint_background` (unless `OPAQUE`) and `on_paint`,
  a repaint when a control invalidated. `Button` and `Label` paint themselves standalone (`widget()` builds the
  `kubuno_ui` widget from the class's properties); the other classes paint nothing outside a view yet (EVT-7b).
- **Sender typing.** `events::Component` (the trait `Sender<C>` is typed with) is renamed **`ElementType`** (the name
  `Component` is the hierarchy's root); the `controls::*` zero-sized types became the real classes, so
  `Sender<Button>`, the templates and the LS stubs are unchanged; `#[event_handlers]` emits `ElementType`.

### Registry and export

- `ComponentMeta::base_chain()` (from the class table: `["Button", "ButtonBase", "Control", "Component"]`), `is_a`,
  `levels()`, `inherited_event(name)`; `registry::LEVELS` (`LevelMeta { name, doc, events, default_event }`):
  `Control` declares `COMMON_EVENTS` and the default `OnClick`, `TextBoxBase` `OnTextChanged`, `ListControl`
  `OnSelectionChanged`, `RangeBase` `OnValueChanged`, `View` the view events. `event`, `all_events`,
  `has_common_events` and `default_event` now flow from the chain (declared default, else the nearest level's that
  the element has, else its first own event); a test checks the old rules give the same answers for every element.
- Export (`kubuno/registry`): each component gains `base_chain`, each event `inherited_from` (`"Control"` for the
  common events, `"View"` for the root-only ones, null for its own). Everything else is identical to the previous
  export (checked by comparing the JSON with the new keys removed). The C# fixture was regenerated; the C# side is
  unchanged (unknown keys are ignored).

### Deviations

- **`kubuno_ui` is not changed.** Its widgets are immediate-mode painters without an event layer (built every frame,
  painted with a caller-supplied state); the hierarchy's classes own the state and events and execute the widgets
  (`controls::Button::widget().paint(…)`). Nothing required a `kubuno_ui` change (the focus ring's `wants_tab` and
  change log were enough), so no dylib/ABI change, and the apps (shell, drive, documents, chat) and the gallery do not
  depend on `kubuno-views`; the workspace was nevertheless rebuilt and restaged (`build-all`).
- `ScrollBarBase` is **`RangeBase`** (the base of every value-in-a-range control; Kubuno has no standalone scroll bar
  element); `LabelBase` also holds `Badge`; `ContainerBase` derives `ScrollableControl` (WinForms `Panel`).
- The event methods take `EventCx<A>` / `PaintEventCx` rather than the bare args (the sink must travel with the call).
- Button-family Click is synthesized by the host from `STANDARD_CLICK` rather than raised by `Button::on_mouse_up`
  (WinForms internals): with delegation, a base raising Click itself would bypass a derived `on_click`. Same events,
  same order.
- `ControlCore` keeps a single replica (`kubuno_controls::ControlBase`); the level cores hold only the few fields their
  level adds (the family replicas also embed a `ControlBase` and would duplicate it).
- A `ControlHost` has its own focus ring; next to a view in the same window, Tab stays within the view's ring (the
  view's ring takes Tab first). Hosting Rust controls inside a view (and `<RoundButton>` in XML) is EVT-7b.
- While a hosted control's event runs, the control is borrowed: its own subscribers must not `borrow_mut()` its
  `HostedControl` (use the args, or an override).

### Tests

`kubuno-views` (530 unit tests pass, all previous ones unchanged): `component::tests` (21: upcasts/downcasts/chains, one
shared core for every level, an override around its base raise, sink before Rust subscribers, a suppressing override,
`handled` in the sink, un-overridden methods reaching the base through a two-level user chain, level methods
dispatched virtually and a mismatched args type taking `on_event`, `on_event` for unknown events, property setters
raising their events, dispose once, styles/bounds/invalidation/creation/layout suspension, key and message packing,
`EventMap` typing, text classes' input keys; through the real router: MouseDown → override → XML Click → Rust Click →
override end → MouseClick → MouseUp with the reported events, a suppressed Click neither handled nor reported,
`process_cmd_key` consuming Ctrl+S and its character, `wnd_proc` eating a press; a real `ButtonNode` click delivered
through a control), `component::host` (sites, names, focus ids, styles → click behaviour, design mode), `controls` (one
class per element, chains per family, WinForms styles), `registry` (metadata flows from the chain), `export` (chain and
`inherited_from`). `kubuno-views-macros`: 6 expansion unit tests, 1 passing doctest (a class extending `Button`, a
non-visual component, a `Control`-level class, a user class as a base) and **9 `compile_fail` doctests** (no
`extends`, wrong base field type, an `impl Control` without `overrides(Control)`, `overrides(Control)` without the impl,
a level not in the chain, a user base without `levels`, a tuple struct, a generic struct). `cargo clippy --all-targets
-D warnings` clean on both crates, no `unwrap` outside tests. `kubuno-views-ls` 117 + 17 tests pass. C# Designer tests:
298 pass with the regenerated fixture.

### Live verification (2026-09-29)

- **Pixel comparison.** `view_preview` on `settings.kbview` and on the five tabs of `showcase.kbview` (one copy per
  selected tab), and `showcase`: captured with the binaries of before the change (saved aside) and after the final
  build and restage: **0 differing pixels** on every capture except the animated `<Spinner>` of the Display tab (84–86 px, which also differ between two captures of the same binary); a first run showed a list row highlighted, the real pointer resting over it — identical once the pointer was moved away. The gallery, drive, documents and chat start and close on `WM_CLOSE` with the rebuilt runtime.
- **Scratch app** `C:\kubuno-build\evt7a` (the *Kubuno Desktop Application* template's files, built with cargo): the
  template's view (its `<Button>` now painted through `controls::Button`) and, under it, a `ControlHost` with
  `#[kubuno(extends = Button, overrides(Control))] struct RoundButton` (a pill drawn in `on_paint` with the current
  Canvas; `on_click` logs, calls `self.base_mut().on_click(e)`, counts, invalidates), a plain `Button` and a `Label`.
  Real mouse clicks and Space/Enter: the log shows, for every click, `on_click: before base` → the Rust `Click`
  subscriber (sender `round`, element `RoundButton`) → `on_click: after base`; the pill shows the count; the plain
  button counts its own clicks; the XML "Say hello" button still runs its typed handler.

## Requirement — WinForms-rich property sets on every control (product owner, 2026-09-29)

Each control must expose (and the runtime must really honour) a property set as rich as its WinForms counterpart, organised in the same categories, inherited through the Component/Control hierarchy:
- **Accessibilité**: AccessibleName, AccessibleDescription, AccessibleRole (→ UIA / AccessKit).
- **Apparence**: BackColor/ForeColor (see colour policy below), Font (family/size/style), Cursor, TextAlign, ImageAlign/TextImageRelation, Image/BackgroundImage/BackgroundImageLayout, FlatStyle-like Variant, BorderStyle, RightToLeft, UseMnemonic (`&` access keys), UseVisualStyleBackColor-like flag.
- **Comportement**: Enabled, Visible, TabIndex, TabStop, ContextMenu (menu element reference), AllowDrop, UseWaitCursor, ToolTip text (extender-provider style, like WinForms' ToolTip component), control-specific (AutoCheck, ThreeState, ReadOnly, MaxLength, Multiline, WordWrap, AcceptsReturn/AcceptsTab, PasswordChar, CharacterCasing, SelectionMode, Sorted, DropDownStyle, Minimum/Maximum/SmallChange/LargeChange, Increment, DecimalPlaces, ThousandsSeparator, Format/CustomFormat, ShowCheckBox, MultiSelect, FullRowSelect, GridLines, HideSelection, LabelEdit, View…).
- **Données**: Tag, (DataBindings) — binding editor for any property (`{Binding …}`), DataSource/DisplayMember/ValueMember for list controls.
- **Design**: (Name), Locked, GenerateMember/Modifiers-equivalent (visibility of the generated accessor on the view model).
- **Focus**: CausesValidation.
- **Disposition**: Location (X, Y) and Size (Width, Height) as expandable composite properties, MinimumSize/MaximumSize, Margin/Padding (expandable All/Left/Top/Right/Bottom), AutoSize/AutoSizeMode, Anchor, Dock.
- **View (Form equivalent)**: Text/Title, Icon, StartPosition, FormBorderStyle, ControlBox/MinimizeBox/MaximizeBox, ShowInTaskbar, TopMost, Opacity, WindowState, AcceptButton/CancelButton, KeyPreview, AutoScroll, MinimumSize/MaximumSize.
Composite/expandable properties, reset to default, bold when non-default, rich editors (colour, font, image, cursor, collection), multi-selection editing — as in WinForms. Runtime support lands in kubuno-views/kubuno_ui (per control class of the hierarchy), not metadata only; each property gets a French/English description.
Colour policy (decided 2026-09-29): **theme tokens by default + free colours allowed**. The colour editor offers Kubuno theme tokens first (Primary, Surface, Danger… — follow light/dark/high-contrast automatically), then free `#RRGGBB(AA)` / system colours like WinForms; a free colour triggers a non-blocking designer warning when it breaks contrast (WCAG) in one of the themes.

## 15. EVT-7b as built (2026-09-29)

The second half of EVT-7: custom controls, user controls and non-visual components written in the application's own
crate, usable as XML elements of its views, and the tooling around them. Rust in `Z:\src\desktop\windows` (uncommitted
there): new crate `kubuno-views-meta`, `kubuno-views-macros`, `kubuno-views`, `kubuno-views-ls`. `kubuno_ui` and
`kubuno-controls` are **unchanged** (no dylib/ABI change: the apps and the gallery are untouched).

### Declaring a control (`kubuno-views-meta`, `kubuno-views-macros`)

- **One grammar, two readers.** `kubuno-views-meta` parses the derive input with `syn` (`parse_decl` →
  `ComponentDecl`: class, level chain, properties, events, design-time attributes, doc comments). The proc macro expands
  from it; the language server scans the project's `.rs` files with the same function (`scan_source`), **without
  building**. A golden test (`kubuno-views-ls/tests/golden.rs`) checks that what the macro registers and what the scan
  declares are identical for the same source.
- `#[derive(Component)]` (EVT-7a) now also accepts the design-time attributes `#[category("…")]`,
  `#[description("…")]` (else the `///` doc), `#[default_value(…)]`, `#[browsable(false)]`, `#[default_event("…")]`,
  `#[default_property("…")]`, `#[toolbox(icon = "…", category = "…")]`, `#[localizable]`,
  `#[designer_serialization_visibility(…)]`, `#[editor("…")]`, `#[type_converter("…")]`. `#[property]` fields are XML
  attributes (PascalCase of the field: `corner_radius` → `CornerRadius`), their Rust type decides the value kind
  (`PropertyValue`: `String`, `bool`, integers, `f32`/`f64`, `#[derive(PropertyValue)]` enums → enum values).
  `#[event] pub x: Event<A>` fields are events (`OnX` attributes, ⚡ tab) with a generated `raise_x(args)`.
- `#[derive(UserControl)]` + `#[user_control(view = "rating_bar.kbview", default_event = "…")]`: a class of the
  `UserControl` level (`base: UserControlCore`, default `extends = UserControl`) that is **its own view model**: its
  view's `{Binding}`s read/write its properties, its `#[kubuno_views::event_handlers]` impl runs its view's `On*`
  handlers, and a handler re-raises an inner event as the user control's own (`self.raise_action_clicked(…)`). The
  view's root is `<UserControl x:Class="RatingBar" DesignWidth DesignHeight>`.
- `Timer` is the built-in non-visual component (family `components`: `Interval`, `Enabled`, `OnTick`), and
  `#[kubuno(extends = Component)]` classes are the user's own non-visual components.

### Registry (`kubuno-views`)

- **Registration without `inventory`:** each derive emits a static constructor (`#[used]` in `.CRT$XCU`, Windows —
  the only desktop target) calling `registry::register_class`, so a linked class registers before `main`, even from an
  rlib nothing references (the binary names it with `extern crate app as _;`). Verified experimentally before building
  on it. The macro skips it inside `kubuno_views` itself.
- Three tiers, merged into one snapshot rebuilt when dirty: **built-ins** → **linked** classes (real factory, custom
  properties applied through `kubuno_set_property`/`kubuno_get_property`) → **declared** classes (from the language
  server's scan or the designer's `projectComponents` message: metadata only). The export gains `origin`, `kind`,
  `linked`, `crate_name`, `extends`, `toolbox_category`/`toolbox_icon`, `browsable`, `default_property`, `view_path`,
  `source_file`/`line`, and per property `category`, `browsable`, `bindable`, `localizable`, `serialization`, `editor`,
  `type_converter`.
- **Rendering:** a linked custom control is a `CustomControlNode` painted by its own `on_paint` (and receiving the
  router's events through its overrides); a user control is a `UserControlNode` that compiles and renders its view with
  the instance as view model (nesting guarded at depth 8, `on_load` once); a declared-but-not-linked class is a
  **placeholder** box showing its name (before the first build); non-visual components render nothing and a `Timer`
  ticks at run time. `DesignMode` is true at design time (no ticks, no handlers run).

### Language server (`kubuno-views-ls`)

`ProjectScanner` scans the package of each open view (and its path dependencies that are control libraries),
incrementally (per file stamp, keeping a file being typed that does not parse yet), and declares the classes found:
completion, validation (no more "unknown element"), hover ("Control of `crate`, extends `Button`") and go to
definition (the struct) for `<RoundButton>`, `<RatingBar>`, `<Heartbeat>`, and their attributes and events. New
requests: `kubuno/registryVersion` (what the designer polls) and `kubuno/crateComponents`.

### Designer and Visual Studio

- **Design build (docs/DESIGNER.md §15) now includes the project crate:** the crate root is compiled as an rlib against
  the project's own build artifacts, and the design surface is compiled with `--cfg kubuno_design_project --extern
  <crate>=…` and a generated `project.rs` (`extern crate <crate> as _;`), so the project's controls are linked into
  the surface and render with their real `on_paint`; `--export-registry` then writes `registry.json` next to it.
- **Toolbox:** after a build, a "*&lt;Project&gt;* Composants" tab lists the project's linked controls with an icon
  (their `#[toolbox(icon)]` when it names a known icon, else the custom control / user control / component icon).
  "Choisir des éléments…" (designer context menu) adds controls of the other crates linked into the project.
- **Properties window:** custom properties with their categories (Apparence, Comportement… mapped from the English
  names) and descriptions; `#[browsable(false)]` hides one; the default property/event follow the attributes.
- **Component tray** under the design surface: the view's non-visual elements (`<Timer>`, user components) — select
  (Properties window), double-click (default event handler), Delete.
- **Overrides without rust-analyzer:** "Substituer des membres…" (light bulb on an `impl Control for X` / on the
  struct) opens a checkbox list of the members of the class's chain (57 members: 13 hooks, 29 event methods, 15 level
  members, from a table generated from `component::overrides`), with their exact signatures; each inserted body calls
  the base (`self.base_mut().on_x(e);`). Snippets in Rust files: `onpaint`, `event`, `handler`, `prop`.
- **Item templates** (Add New Item, and "Ajouter ›" on a Rust project): *Contrôle personnalisé Kubuno*, *Contrôle
  utilisateur Kubuno* (`.kbview` + nested code-behind), *Contrôle hérité Kubuno* (base class picked in a small
  dialog), *Composant Kubuno*. The wizard names the struct in PascalCase and declares the module in the crate root (a
  file not named after its module gets a `#[path]`); the "Ajouter ›" commands name the files after the module
  (`RoundButton` → `src\round_button.rs`) and put them in `src`. (Found live: a template whose target file name differs
  from the entered name makes Visual Studio's project system fail to find the new item.)
- **Fixed — Properties window empty after reopening a solution.** Visual Studio stops `kubuno-views-ls` itself when a
  solution closes and starts a new one for the next; the client kept `IsInitialized = true` from the first server, so
  the designer of the reopened solution could send its first requests on the new connection *before* `initialize` —
  `kubuno-views-ls` rejects that and exits, and nothing (Properties window, selection sync, registry) worked until
  Visual Studio restarted. The client now resets the state on every new connection and when a connection drops
  (`ReadyRpc` never returns a disposed connection), and a designer that waits too long asks Visual Studio to start the
  server again.

### Deviations and limits

- Registration is Windows-only (`.CRT$XCU`); other targets would need their own section (`.init_array`).
- A project control appears in the Toolbox and renders for real only after a build (design build); before that it is a
  named placeholder — completion and validation work at once (scan).
- Snippets are offered by the extension's own completion source in `.rs` files, not through the VS snippet manager.

### Tests

`kubuno-views-meta` 4; `kubuno-views-macros` 18 unit + 27 doctests (incl. `compile_fail` misuse and a user control
with its `.kbview` fixture); `kubuno-views` 540 unit + `tests/custom_controls.rs` 4 (a linked custom control painted by
its `on_paint`, custom properties applied, a user control rendering its own view with bindings and re-raising its
event, a declared class drawn as a placeholder) + the existing integration tests; `kubuno-views-ls` 119 unit +
golden 1 + round trip 18 (incl. project controls declared by the scan; the round-trip tests now run one at a time, the
registry being process-wide). `cargo clippy --all-targets -D warnings` clean. C#: Designer 304, Core 209 (override
assistant, snippets, wizard naming/module declaration), Cargo design surface 13. `tools/test-templates.ps1 -Run`: the
desktop application template gets the four control items (custom, user, inherited, component) used in its view,
builds with 0 warnings, passes `cargo test` and runs.

### Live verification (regular Visual Studio, 2026-09-29)

New *Kubuno Desktop Application* `Evt7bApp` → Ajouter › Contrôle personnalisé Kubuno "RoundButton" (real command and
name dialog: `src\round_button.rs`, `mod round_button;` added to `main.rs`) → Ajouter › Contrôle utilisateur Kubuno
"RatingBar" (`rating_bar.kbview` + `rating_bar.rs`) → Générer: the design build links `evt7bapp`
(`registry.json`: `RoundButton` and `RatingBar` linked) and the Toolbox gets "Evt7bApp Composants" with both.
`<RoundButton>`, `<RatingBar>` and `<Timer>` in `main_view.kbview`: no diagnostic; the surface renders with the
project runtime. "Substituer des membres…" from the light bulb in `impl Control for RoundButton`: the dialog lists the
chain's members (Control → Component) with signatures and French descriptions; `on_click` and `on_mouse_enter`
inserted with their base calls; the project rebuilds and the design build follows. F5: the app runs, the `Timer` ticks
every 500 ms in the debug output. Solution closed and reopened in the same session: Visual Studio restarts the server,
the restored designer gets its selection sync, Toolbox project tab and registry again.

## 16. EVT-7c as built — WinForms-rich property sets (2026-09-29)

The requirement above ("WinForms-rich property sets on every control"), implemented in `desktop/windows`
(`kubuno_controls`, `kubuno_ui`, `kubuno-views`, `kubuno-views-ls`) and in the Designer.

### Metadata (`kubuno_views::registry`)

- Each level of the hierarchy carries its property table (`LevelMeta::properties`, `registry/common.rs`): `Control`
  (Accessibilité, Apparence, Comportement, Données, Design, Focus, Disposition), `ButtonBase`, `LabelBase`,
  `TextBoxBase`, `ListControl` (`Sorted`), `ScrollableControl` (`AutoScroll`), `ContainerBase` (`BorderStyle`), and
  `VIEW_PROPERTIES` for the root element (the window: *Style de fenêtre*, plus `Title`/`FormBorderStyle` in Apparence,
  `StartPosition`/`WindowState` in Disposition, `AcceptButton`/`CancelButton`/`KeyPreview` in Divers, as in Windows
  Forms). `ComponentMeta::property` resolves own, then inherited properties, then aliases; `all_properties` lists them
  with their level.
- `PropertyMeta` gains `aliases` (older names accepted and reported as hints: `Label.Align` → `TextAlign`,
  `Min`/`Max` → `Minimum`/`Maximum`, `Step` → `SmallChange`/`Increment`, `LargeStep` → `LargeChange`) and
  `design_time`. The export writes `inheritedFrom`, `rootOnly`, `designTime`, `aliases` and always a category (a
  default one by name and kind when a table gives none; the language server's source scan applies the same default).
- Every property has an English and a French description (`docs_fr::property_french`, level entries such as
  `Control.BackColor`); the tests check both and the "no developer jargon" rule.
- New components `ToolTip`, `ContextMenu` (children `MenuItem`) and `MenuItem`; event `OnDragDrop` on `Control`.
- The registry snapshot uses a generation counter (a registration racing a rebuild could be lost with the old flag).

### Colours, fonts, contrast (`kubuno_views::style`)

- `ColorValue`: a theme token (27 tokens = `Theme` fields, each with its light and dark values, EN/FR doc and its
  high-contrast system colour), `#RRGGBB(AA)`, a .NET web colour name or a `SystemColors` name. Tokens follow the
  theme and high contrast at paint time; free colours are fixed.
- `contrast_warnings(fore, back, size, bold)`: WCAG relative luminance, AA = 4.5:1 (3:1 for large text), computed for
  the light and the dark theme; `validate::contrast_warnings` reports an element whose own free `ForeColor`/`BackColor`
  fails (against the ambient colours of its ancestors) as a **warning** only.
- `FontSpec` (`Segoe UI, 12pt, style=Bold, Italic, Underline, Strikeout`), `parse_padding` (`l, t, r, b` or one
  number), `parse_size`, `cursor(name)`.
- Attribute values decode XML character references (`&amp;` → `&`, `&#10;` → line break), and the designer's edits
  escape `&`, `<`, the quote, line breaks and tabs, so any text round-trips (tests in `edit.rs`, and in C# for the
  Properties window's reader and the collection planners).

### Runtime

- `DesignSlot` applies `CommonProps` (`common.rs`) around every element: visibility (not painted, routed, focusable
  nor announced; still shown in the designer), `Margin` (flow parents), `Padding` (a leaf grows its measure, a
  container insets its content), `MinimumSize`/`MaximumSize`, `AutoSize`/`AutoSizeMode` (`Width`/`Height` ignored, or
  a floor with `GrowOnly`), `AutoScroll` (`AutoScrollNode`: the content laid out at its measured size in a Kubuno
  `ScrollArea`), `BackColor`/`ForeColor`/`Font`/`RightToLeft` through `kubuno_controls::styled::StyledCanvas` (the
  control paints with an overridden theme and text formats; a button's own face, a fill behind other controls;
  `UseVisualStyleBackColor="true"` keeps the theme face), `BackgroundImage`/`BackgroundImageLayout`, `BorderStyle`,
  `Enabled` (ambient: disables the whole subtree, unregistered from focus and hit tests), `TabIndex`/`TabStop`
  (nested Tab keys in `FocusRing`, missing = 0), and per-frame offers (`FrameServices`): cursor
  (`Cursor`/`UseWaitCursor`), tooltip, accessibility node, mnemonic, context menu, drop target.
- Level properties read by the nodes: `ButtonBaseProps` (`TextAlign`, `Image`/`ImageAlign`/`TextImageRelation` via
  `kubuno_ui::buttons::aligned_face`, `UseMnemonic`), `LabelBase` on `Label`/`LinkLabel` (`TextAlign`, `Image`,
  `UseMnemonic`), `TextBoxProps` (`ReadOnly` bindable, `MaxLength`, `AcceptsTab`, `PasswordChar`, `CharacterCasing`,
  `HideSelection`, `TextAlign`, `AcceptsReturn`, `WordWrap`), CheckBox `CheckState`/`AutoCheck`/`ThreeState`,
  RadioButton `AutoCheck`, the Slider/NumericField/ProgressBar ranges, NumericField `DecimalPlaces`/
  `ThousandsSeparator`, `Sorted` on ListBox/CheckedListBox/Dropdown/ComboBox (static and bound items).
- Window services (`window.rs`, `Runtime::window_input`/`window_output`): mnemonics (Alt+letter activates the element
  or, for a label, focuses the next control; the letter is underlined while Alt is held, always in the designer),
  `AcceptButton` (Enter, and Enter in a single-line field) / `CancelButton` (Escape), `KeyPreview` (the view's key
  events first), `CausesValidation="false"` (the focus moves in without validating the element left), tooltips
  (`<ToolTip>` delays), context menus (right click → `OnOpening` → Kubuno menu, `MenuItem.OnClick`, shortcut text),
  file drops (`AllowDrop` → `OnDragDrop` with `DragEventArgs`), the cursor, the accessibility tree, and `FormSpec` →
  `host::set_form` every frame.
- Host (`kubuno_controls::host`): `FormOptions` (title, icon file, `StartPosition`, `FormBorderStyle` incl. `None` and
  tool windows, caption buttons shown or greyed in the Kubuno caption, `ShowInTaskbar`, `TopMost`, `Opacity` through
  `WS_EX_LAYERED`, `WindowState`, `WM_GETMINMAXINFO` limits), applied at creation (`HostOptions::form`, so the window
  opens right) and live. Accessibility: `host::access` builds an AccessKit tree (roles from `AccessibleRole` or the
  control class, name/description/value/states/bounds/access key, focus) behind a lazily created
  `accesskit_windows::Adapter` answering `WM_GETOBJECT`; Click and Focus actions come back to the elements. Files:
  `DragAcceptFiles` + `WM_DROPFILES`.
- Logging: `diagnostics` starts at `INFO` in every build (`KUBUNO_LOG` or `diagnostics::set_max_level` change it); the
  template's per-event `tracing::debug!` is silent unless asked for, and its `main.rs` passes
  `runtime.form_options(..)` to the host.

### Language server and designer

- Completion and hover offer inherited properties (with their level) and the view's own on the root, the enum values
  of both, and alias notes; contrast warnings are published as warnings; `kubuno/bindingPaths { uri, openFiles } →
  { paths }` reads the `match` patterns of the code-behind's `fn get`. A notification the server cannot read is logged
  instead of ending the server.
- Designer surface: the window frame shows `Title` and the caption buttons as `FormBorderStyle`/`ControlBox`/
  `MinimizeBox`/`MaximizeBox` set them; `setText` carries `baseDir` (relative images); `Locked` elements are selected
  but never moved, resized or nudged.
- Properties window (C#): composite `Location`/`Size` (shown as *Dimensions* when the control has its own `Size`)/
  `Margin`/`Padding`/`MinimumSize`/`MaximumSize` rows, bold non-default values, Reset, multi-selection; editors:
  colour (Theme tab first with light|dark swatches, Custom with Windows' colour dialog, Web, System, and the contrast
  line), font (Windows' font dialog, owned by Visual Studio), image (project images, preview, copy next to the view),
  cursor, collection (`Item`/`Column`/`TabItem`...), string list, binding. The drop-downs use Visual Studio's
  combo-popup theme colours and scale with the DPI; the dialogs are `DialogWindow`s with `ThemedDialogColors`, the
  default themed-dialog styles and the `VsResourceKeys` control styles (`ThemedEditorDialog`, to be rebased on the
  shared `ThemedDialog` once it is committed), message boxes through `VsShellUtilities.ShowMessageBox`.

### Deviations and limits

- CheckBox/RadioButton `TextAlign`/`Image` are not drawn, nor the mnemonic underline on their label (Alt+letter works).
- `AllowDrop` covers files dropped from Explorer (`OnDragDrop`), not in-app drag and drop of data.
- `Modifiers`/`GenerateMember` are design-time metadata only (no generated accessor yet).
- A container's own `Padding` (`Stack`, `Panel`, `Card`, `GroupBox`) stays its single-number property; the inherited
  four-sided `Padding` applies to the other controls.
- The designer's selection sync keeps a language-server connection that Visual Studio replaced (seen once when the
  server was restarted during solution load): reopening the view reconnects it.

### Tests

`kubuno-views` 572 unit + integration and doc tests (common properties, styles and contrast, form/tooltip/menu
reading, runtime → host form and accessibility tree, validation of formats/view properties/contrast warnings,
`Locked`, `setText.baseDir`, attribute entities and escaping); `kubuno_controls` 391 (form styles, caption buttons,
access tree, styled formats); `kubuno_ui` 728 (Tab indexes, disabled parts, aligned faces, mnemonic underline);
`kubuno-views-ls` 124 + golden + round trip (inherited/view completion and hover, binding paths). `cargo clippy
--all-targets -D warnings` clean on the five crates. `tools/build-all.ps1` + restage: gallery, shell, drive,
documents and chat start. `tools/test-templates.ps1 -Run`: all four templates build and run. C#: Designer 341 (colour/
font/composite converters, category map, multi-selection, entity round trip, the theme-tokens fixture generated from
the runtime).

## 17. EVT-8 as built — painting, the `Graphics` API, owner-draw, drag and drop (2026-09-29)

In `Z:\src\desktop\windows` (uncommitted there): `drive-app-controls` (two default methods on `Canvas`, `Debug`/
`PartialEq` on `Rect`, `Clone` on `ShadowLayer`), `kubuno-controls` (host: `dnd`, `paint_debug`; painter),
`kubuno_ui` (new `graphics` module; owner-draw in the list widgets), `kubuno-views`; and in this repository (the
*Paint debug* command, the Custom Control item template, the regenerated `OverridableMembers.json`). `kubuno_ui` is a
dylib: the workspace was rebuilt and restaged; the gallery and the `view_preview` showcase are pixel-identical with the
defaults (see "Live verification").

### The `Graphics` API (`kubuno_ui::graphics`)

- **`Graphics<'a>`**, a borrowed view over a `&dyn Canvas` — `System.Drawing.Graphics`' surface in Rust: `draw_line(s)`,
  `draw/fill_rectangle(s)`, `draw/fill_rounded_rectangle` (.NET 9), `draw/fill_ellipse`, `draw_arc`, `draw/fill_pie`,
  `draw_bezier(s)`, `draw_curve` and `draw/fill_closed_curve` (cardinal splines), `draw/fill_polygon`, `draw/fill_path`,
  `clear`, `draw_string` (in a box: `StringFormat` alignment and line alignment, wrapping, `StringTrimming` incl. the
  ellipses and `EllipsisPath`, `DirectionRightToLeft`, `NoClip`; `Font` underline/strikeout), `draw_string_at`,
  `measure_string`, `draw_image` / `draw_image_with` (source rectangle, opacity) / `draw_image_unscaled` / `image_size`,
  `draw_icon` (the Kubuno vector icons); clip (`set_clip`, `set_clip_path`, `intersect_clip(_path)`, `reset_clip`,
  `clip_bounds`, `is_visible`), transforms (translate, scale, rotate, `rotate_transform_at`, `multiply_transform`,
  `transform`, `set_transform`, `reset_transform`; `Matrix` with `MatrixOrder`, prepend by default like GDI+),
  `save`/`restore` (WinForms' `GraphicsState`: restoring discards the later states; an unknown state does nothing) and
  `with_saved`, and the hints (`SmoothingMode` — antialiased by default, a deliberate deviation from WinForms —,
  `TextRenderingHint`, `InterpolationMode`, `CompositingMode`).
- Value types: `Color` (straight alpha, `rgb`/`argb`/`from_hex`, from/to the theme's `D2D1_COLOR_F`), `PointF`, `SizeF`,
  `RectExt` (helpers on the shared `Rect`), `Brush` (`Solid`, `LinearGradientBrush` — `from_rect` with
  `LinearGradientMode`, `with_angle`, stops, `WrapMode` —, `RadialGradientBrush`: GDI+'s elliptical `PathGradientBrush`),
  `Pen` (width, `DashStyle` or a custom pattern in pen widths, dash offset, `LineCap`s, `LineJoin`, miter limit,
  `PenAlignment::Inset`), `GraphicsPath` (figures of lines, cubic/quadratic Béziers and SVG-style arcs; `add_line(s)`,
  `add_rectangle`, `add_rounded_rectangle`, `add_ellipse`, `add_arc` — GDI+'s true-angle arcs, clockwise on screen —,
  `add_pie`, `add_polygon`, `add_bezier(s)`, `add_curve`, `add_closed_curve`, `add_path`, `start_figure`/`close_figure`,
  `transform`, `flatten`, `bounds`, `is_visible` by `FillMode`), `Font` (points like WinForms, or DIP, or a theme text
  role — the default, which follows the app font —; `FontStyle` bits), `StringFormat`, `Image` (a file decoded by WIC on
  first use per device, or a bitmap).
- **How it draws.** When the canvas lends its renderer (new `Canvas::graphics_renderer`, default `None`; the host's
  painter lends it), each call is drawn with Direct2D on the canvas' own device context, in the frame's `BeginDraw`: the
  op pushes its clip (axis-aligned when the transform allows, a geometric-mask layer otherwise), composes its transform
  with the context's current one (the scroll offsets), sets its hints, draws, and restores everything — so `Graphics`
  calls interleave safely with the canvas primitives — and reports what it drew for the scroll extents (new
  `Canvas::note_drawn`). Gradient stops are interpolated in straight alpha, dashes are a custom stroke style, text goes
  through a DirectWrite layout (formats built per family/size/weight and kept). Without a renderer (another app's
  canvas, a test canvas) the calls fall back to the primitives: rectangles and circles as rounded fills and strokes,
  other shapes flattened (triangle fans, dotted lines), gradients as their middle colour, axis-aligned clips only, no
  images.
- **Recording.** Every call is an `Op` carrying its state (transform, clip list, hints); `Graphics::recording()` and
  `Graphics::recorder()` keep them in a `DisplayList` (`describe`, `bounds`, `replay`). `Graphics` also implements
  `Canvas`: a canvas primitive called on it is recorded as a `CanvasCall` and forwarded, so a `kubuno_ui` widget
  painted through a `Graphics` is recorded whole; `raw_canvas()` / `renderer()` hand the underlying objects out and mark
  the recording incomplete (`has_unrecorded_drawing`).
- **Lending to event args.** Every method takes `&self`, and a `Graphics` is covariant in its lifetime: `GraphicsSlot`
  lends one to `'static` event args for the duration of a raise (a scope guard clears it, even on a panic); read
  afterwards, the slot answers a null `Graphics` that draws nothing — never a dangling one.
- Deviations: images and icons follow translation and scale, not rotation; `EllipsisPath` trims at a character with
  the last `\` segment kept; `PathGradientBrush` is the elliptical case only; no hatch or texture brushes, no regions
  beyond rectangles and paths, no `PageUnit`.

### Paint in the control hierarchy (`kubuno-views`)

- `PaintEventCx` now carries `graphics: &Graphics` (`e.graphics`, which also answers the canvas primitives, so EVT-7
  code such as `e.graphics.fill_rounded(..)` still compiles), `canvas()` (the raw `ControlCanvas`; asking for it turns
  the paint buffer off for that paint), `clip_rectangle` / `bounds()` (surface coordinates, as before),
  `client_rectangle()` (local), `state`. The `Paint` event's `PaintEventArgs { clip_rectangle, graphics() }` is real:
  its handlers (the element's `OnPaint="…"`, Rust subscribers of `control.paint()`) draw on the lent surface.
- **`on_paint_background`** (root behaviour): the control's `BackColor` — over the parent's background (the canvas'
  `current_bg`) when the colour has alpha and the class has `SUPPORTS_TRANSPARENT_BACK_COLOR`, made opaque otherwise —,
  then its `BackgroundImage` laid out by `BackgroundImageLayout`, clipped to the bounds. It raises no event.
- **`on_paint`**: unchanged (the base raises `Paint`).
- **`on_print`** (WinForms `OnPrint`, product-owner request): called for off-screen rendering (`draw_to_bitmap`) and
  printing; the default runs `paint_layers(e)` when `USER_PAINT` is set — `on_paint_background` unless `OPAQUE`, then
  `on_paint` (which raises `Paint`, as in the reference source; `OnPrint` raises nothing of its own). `paint_layers` is a
  provided method called on the outer object, so an `on_print` override that calls it reaches the class's own
  `on_paint`, which `self.base_mut().on_print(e)` would not (the delegation limit of §14); the override catalogue's stub
  for `on_print` therefore calls `self.paint_layers(e)`.
- **`invoke_paint(child, e)` / `invoke_paint_background(child, e)`** (composite controls drawing their children),
  **`draw_to_display_list()`** (records `on_print` through a recorder, shifted to the control's origin) and
  **`draw_to_bitmap(target, target_bounds)`** (replays it on any `Graphics` at `target_bounds`, at the control's size).
- **`component::paint::paint_control`** is the WM_PAINT of the hosts (`CustomControlNode`, `<PaintBox>`,
  `ControlHost`): nothing when `USER_PAINT` is cleared; the background unless `OPAQUE`; `on_paint`. **Double
  buffering**: every frame is composed off screen, and with `OPTIMIZED_DOUBLE_BUFFER` (the default;
  `double_buffered()` / `set_double_buffered`) the paint is recorded and the next frames **replay** it instead of
  calling the paint methods, while the control stays valid: no `invalidate`/`refresh`, the same bounds, interaction
  state, DPI scale and theme, no property change (the view runtime invalidates a class whose applied property values
  changed; `ControlHost` one lent mutably through `HostedControl::borrow_mut`). A paint that touched the raw canvas, or
  whose `Paint` event has a handler or a Rust subscriber, is not kept. `invalidate_rect` asks for a frame (WinForms
  posting `WM_PAINT`); an invalidation made while painting (an animation) stands. The `<Button>` node skips the class's
  `on_paint` when a class clears `USER_PAINT` (the built-in look paints, like a native control), and raises the
  element's `OnPaint`.
- **`ControlStyles` honoured** (documented on the type): `USER_PAINT`, `OPAQUE`, `RESIZE_REDRAW`,
  `SUPPORTS_TRANSPARENT_BACK_COLOR`, `OPTIMIZED_DOUBLE_BUFFER`; `DOUBLE_BUFFER` and `ALL_PAINTING_IN_WM_PAINT` hold by
  construction (no flicker is possible, no separate erase pass).
- **`<PaintBox>`** (display family, default event `OnPaint`): a surface its `Paint` handler draws on — the design
  note's `<Canvas>`, renamed because `Canvas` is the drawing trait of the prelude (a `Sender<Canvas>` stub would not
  compile).
- Registry: `OnPaint` joins the common events (category Appearance; raised by custom controls, `<PaintBox>` and
  buttons, not by the other built-in controls, which paint their widgets directly).

### Owner-draw

- `kubuno_ui::graphics::owner_draw`: `DrawMode` (the replica's), `DrawItemState` (WinForms' bits), `DrawItemEventArgs`
  (`graphics`, `index`, `sub_index`, `bounds`, `state`, `text`, `font`, `fore_color`, `back_color`, `draw_default`;
  `draw_background`, `draw_focus_rectangle`, `draw_text`), `MeasureItemEventArgs`, the `OwnerDrawHandler` trait
  (closures are handlers). The widgets are painted with `&self` inside a frame, so the handler is **lent for a paint**
  (`with_handler`, a scoped thread-local stack; the running handler is taken out of it while it runs, so a nested
  owner-drawn widget never gets a second `&mut`). Without a handler, or when it sets `draw_default`, the item paints
  normally (WinForms would leave it blank).
- The widgets: `ListBox` (`OwnerDrawFixed`; `OwnerDrawVariable` with `measure_items` → `item_heights`, used for layout,
  hit-testing, scrolling and the scroll indicator — single column, as in WinForms), `ComboBox` and `Dropdown` (their
  edit field with `COMBO_BOX_EDIT`, and their drop-down rows; variable heights are not applied to the drop-down),
  `ListView` (`owner_draw`: rows in Details, tiles in LargeIcon), `TreeView` (`OwnerDrawText`: the label;
  `OwnerDrawAll`: the row), `DataTable` (new `owner_draw_cells`: each cell, `sub_index` = column, clipped to its column),
  `Tabs` (`OwnerDrawFixed`: the caption; the strip keeps its hover wash and indicator), `Menu` (new `owner_draw`: the menu
  items; separators and section labels stay the menu's).
- **Popups**: a combo box's list and a context menu are painted in their own popup window after the page, when the view
  model is no longer at hand; their owner-drawn rows are **recorded** during the page's paint through the real handler
  (`RecordedItems::record`, on a recorder `Graphics`) and replayed in the popup.
- Views: `DrawMode` on `ListBox`/`ComboBox`/`Dropdown` (bindable), `Tabs` (`Normal`/`OwnerDrawFixed`) and `TreeView`
  (`OwnerDrawText`/`OwnerDrawAll`), `OwnerDraw` on `ListView`, `DataTable` and `ContextMenu`; events `OnDrawItem`
  (aliases `OnDrawNode` on `TreeView`, `OnCellPainting` on `DataTable`) and `OnMeasureItem`, with
  `events::DrawItemEventArgs` / `MeasureItemEventArgs` (the surface lent through a `GraphicsSlot`; `draw_default`, the
  colours, the font and the measured size are copied back). They go through the element's class (`on_event`) then its
  handler, and are not reported in the frame's events (they are raised for every item of every paint).

### Drag and drop

- **Host (`kubuno_controls::host::dnd`)**: `DataObject` (text, files, custom formats by name; `from_text`, `from_files`,
  `with_custom`, `has_format` / `formats` with WinForms' names) and `DragDropEffects` (with `pick(mods)`: Ctrl copies,
  Shift moves…) now live here and are re-exported by `kubuno_views::events`. **Target**: a page calls `accept_drops`
  (views: when an element has `AllowDrop`), and the window registers with OLE once (`RegisterDragDrop`; a refusal — a
  design surface owns its drop target — is remembered; revoked at `WM_DESTROY`). Each OLE call (`DragEnter`,
  `DragOver`, `DragLeave`, `Drop`) updates the `Tracker` (the frame's `DragFrame`: phase, data, allowed effects, client
  DIP point, modifiers, buttons, internal) and **renders a frame at once** from the drop target (never inside another
  paint), so the page's handlers answer (`set_effect`, masked by the allowed effects) before OLE is answered — the
  cursor shows what a drop would do while the pointer moves. A foreign data object is read (`CF_HDROP`,
  `CF_UNICODETEXT`, every registered `HGLOBAL` format up to 1 MB); the process's own drag hands over its `DataObject`
  whole. `accept_files` (EVT-7c) still works: with the OLE target registered, dropped files nobody answered for become
  `FilesDropped`. **Source**: `do_drag_drop(data, allowed, done)` starts after the frame (a posted `WM_KUBUNO_DRAG`:
  OLE's modal loop runs from the message loop, and the window keeps rendering as a target meanwhile), with an
  `IDataObject` offering text, `CF_HDROP` (files can be dropped onto the Explorer) and the custom formats as registered
  clipboard formats, and an `IDropSource` (Escape cancels, releasing the button drops); the button state is
  resynchronised afterwards (the release happened inside OLE's loop). Implemented with windows-rs `#[implement]`.
- **Views**: the runtime routes the drag to the deepest element that allows drops under the pointer (last frame's
  geometry): `DragEnter`, then `DragOver` while it moves over the same element (starting from the last answer),
  `DragLeave` when it moves off or is cancelled (the next target gets `DragEnter`), and `DragDrop` only when the target
  accepted (`e.effect` not `NONE`); the effect goes back to the source. `DragEventArgs` gained `key_state` (WinForms'
  bits) and `suggested_effect()`; its coordinates stay relative to the element (WinForms gives screen coordinates).
  `Control` gained `on_drag_enter/over/drop/leave` (overridable, in the catalogue) with their accessors, and
  `do_drag_drop(data, allowed) -> DragOperation` (also `kubuno_views::dnd::do_drag_drop`), which completes with the
  effect — poll it, or `.await` it in an async handler. `ItemDrag`, `GiveFeedback` and `QueryContinueDrag` are not
  raised (the source uses OLE's default cursors).

### Paint debug overlay

`kubuno_controls::host::paint_debug`: invalidated regions flash (a repainted buffered control, an explicit invalidation:
magenta, fading over 600 ms), the layout bounds of every view element (cyan) with its padding (green) and margin
(orange), and the frame's paint time and frames per second (top right). On with `KUBUNO_PAINT_DEBUG` (`1`/`all`, or
`invalidate,layout,fps`), live with the registered window message `Kubuno.PaintDebug` (`wParam` = the bits 1/2/4), or
`paint_debug::set_flags`. Visual Studio: **Debug › Kubuno › Paint debug** (*Débogage › Kubuno › Débogage du rendu*,
checkable, persisted) posts the message to every `KubunoControlsHost` window and sets `KUBUNO_PAINT_DEBUG=all` in
Visual Studio's own environment, which F5/Ctrl+F5 launches inherit (a value the user set is kept). It is a run-time
diagnostic only: the designer's surfaces are started without the variable, ignore the message, and never report
layout bounds (a designed control looks exactly as at run time; when the overlay reached them, it boxed every Label
and TextField of the designed view in cyan).

### Tooling

- The override catalogue gained `on_print` and the four drag methods (62 members); `overrides_fixture.rs` and
  `OverridableMembers.json` were regenerated. The *Contrôle personnalisé Kubuno* item template shows
  `on_paint_background` (calling the base), an `on_paint` with a gradient face, an inset outline (dotted with the focus)
  and centred text with an ellipsis, and `on_print` as a commented example.
- French documentation for every new event and property.

### Tests

`kubuno_ui` (760 unit tests): the graphics module (matrices, colours, gradients, dash patterns; paths: fill modes, arcs,
pies, curves, transforms, rounded corners; fonts and string formats, the approximate measure; every call as an op with
its state, save/restore nesting, clip replace/intersect/path, text placement, the canvas primitives recorded and
forwarded, the fallback mapping onto a recording canvas, display-list replay in its recorded state, the null
`Graphics`; the slot cleared after a raise and after a panic; owner-draw handlers with `draw_default`, nesting without
aliasing, measure, focus rectangles) and an owner-drawn `ListBox` (fixed: rows to the handler in order, the default row
painted; variable: measured heights driving `item_rect`, `item_at`, `visible_rows`, `max_top_index`).
`kubuno_controls` host (42): the drag `Tracker` (enter → over → drop, leave, the answer masked by the allowed
effects), effects to and from OLE and `pick`, `DataObject` formats, a data object round trip through real OLE (text,
files, a custom format), start requests, the paint debug flags and notes. `kubuno-views` (588 unit + integration +
doc tests): `paint_control` (order, `OPAQUE`, `USER_PAINT`, a replay identical to the paint, repaints on invalidation,
state and bounds, no buffer with a handler, a subscriber, `DoubleBuffered` off or a raw-canvas paint, `BackColor`
opaque and transparent), `on_print` and `draw_to_bitmap`, `invoke_paint`, paint and draw-item args lent and copied
back, `DragOperation` (awaited, replaced), and through the real runtime with a fake host and a recording canvas: a drag
routed A.DragEnter → A.DragOver → A.DragLeave → B.DragEnter → B.DragDrop with the effects returned, nothing over an
element without `AllowDrop`, a cancelled drag; an `OwnerDrawVariable` list's MeasureItem before its DrawItem, and a
`<PaintBox>` handler drawing on the lent surface. `kubuno-views-ls` 124 + golden + round trip pass unchanged. `cargo
clippy --all-targets -D warnings` clean on `kubuno_ui`, `kubuno-controls`, `kubuno-views`. C#: the Paint debug helpers
(15 tests in `Kubuno.Rust.Launch.Tests`).

### Live verification (2026-09-29)

- Desktop workspace rebuilt and restaged: gallery, settings and the `view_preview` showcase (tabs 0–4) pixel-identical
  to the binaries built before EVT-8 (0 px; the spinner's 81 px are the same animation noise as before-vs-before);
  drive, documents, chat and shell start and close normally. `tools/test-templates.ps1 -Run`: the four templates OK,
  including the new *Contrôle personnalisé Kubuno*.
- Scratch *Kubuno Desktop Application* `Evt8App` (a `Gauge` custom control: radial dial, gradient arc with round caps,
  ticks under rotating transforms, centred text; an `OwnerDrawVariable` `ListBox` with icons and two-line rows; a drag
  source panel, a text drop zone, a `<PaintBox>` area chart). Dragging the source panel onto the list:
  `DoDragDrop` → `DragEnter`/`DragOver`… → `DragDrop` with the text and the custom format
  (`["UnicodeText","Evt8App.Item"]`) → the awaited effect `COPY`. A drag from a second instance of the application
  (another process) reached the list through the OLE `IDataObject`. `KUBUNO_PAINT_DEBUG`/the registered message
  toggled the overlay on (frame-time box, flashes) and off (0 px difference with the original frame).
- Regular Visual Studio with the Release VSIX: the designer's first design build failed while an instance of the
  application held `kubuno_ui.dll` (the expected *Failed* bar + **Générer**); once it was closed, **Générer** rebuilt
  it and the preview swapped to the project's `kubuno-design-surface.exe`, painting the `Gauge` through its `on_paint`.
  F5 started the application under the debugger, and **Déboguer › Kubuno › Paint debug** switched its overlay on
  live.
- Not automated: a file drag from the Explorer itself (a synthetic drag source did not complete its OLE loop); the
  file path of `DragEventArgs` was covered by the OLE round-trip test and the cross-process drag.

## Requirement — the WinForms printing stack (product owner, 2026-09-29; built 2026-09-30: see `docs/PRINTING.md`)

`on_print` (EVT-8) is the control's side of printing. The rest of WinForms' printing stack is to come:

- a **`PrintDocument`** non-visual component (component tray, `<PrintDocument>` in XML): `BeginPrint`,
  `QueryPageSettings`, `PrintPage` (with `HasMorePages`, the page's `Graphics`, the margin and page bounds), `EndPrint`;
  `DocumentName`, `DefaultPageSettings`, `PrinterSettings`;
- **`PrintPreviewControl`** / **`PrintPreviewDialog`** (the pages rendered through the same `PrintPage` handler, zoom,
  columns and rows), **`PrintDialog`** (printer, copies, range) and **`PageSetupDialog`** (paper, orientation, margins);
- on Windows through the print spooler / XPS, with Direct2D printing (`ID2D1PrintControl` over an XPS print job), the
  page `Graphics` being the EVT-8 `Graphics` over a print surface, and controls rendered onto it through `on_print`.

## 18. User controls as built — Windows Forms parity (2026-10-01)

A pass over the whole user control workflow against Windows Forms' `UserControl`, in a fresh *Kubuno Core Desktop
Application* in Visual Studio (`GETTING-STARTED.md` §6, "User controls", has the walkthrough). It differs from §4.2
and §15 as follows.

**Declaring.**
- A user control's `Load` runs **in the designer too**, with `design_mode()` true, as in Windows Forms (sample data
  for the designer). The order is: the handler its own view's root names (`<UserControl x:Class="…"
  OnLoad="address_editor_load">`, a method of its `#[event_handlers]` impl), then `on_load` (the class's override,
  the `Load` subscribers, the `OnLoad` of the element using it, never run in the designer). A panic there is caught
  in the designer and shown in the control's box. Repeater items (`ItemTemplate` or an inline template) load the same
  way (`node::custom::load_user_control`).
- New property types: `Option<ColorValue>` / `ColorValue` (theme colour, `#RRGGBB`, web or system name) and a fixed
  `kubuno_ui::graphics::Color` (editor `color`); `Vec<String>` (editor `lines`, one item per line). They are read
  from the type by the derive (`PropertyValue::EDITOR`) and by the language server's scan (`value_editor`), so a
  linked and a scanned class agree (golden test). `#[property(on_change = "method")]` calls a method after the
  property is set: from the view using the control, a binding, a Repeater row, or the user control's own two-way
  bindings.
- `#[toolbox(bitmap = "address_editor.png")]` (or an `icon` ending with `.png`/`.bmp`/`.ico`/`.gif`/`.jpg`):
  Windows Forms' `[ToolboxBitmap]`. The derive resolves the image next to the declaring file, fails the build when
  it is missing, tracks it, and registers its absolute path as `toolbox_icon`. The Toolbox scales it to 16 × 16
  (`NativeToolboxInstaller.ImageFileIcon`).
- The properties an element sets on a project control are applied **when their value changes** (first paint, a
  binding that moved), not at every frame. Every frame overwrote what the user typed into a field bound two-way to
  the same property, and the `Load` sample data.

**Placing.** A user control dropped or double-clicked from the Toolbox gets its view's `DesignWidth` ×
`DesignHeight` (`design::user_control_design_size`, exported as `design_size`). It is one selectable unit, and its
inner controls are not selectable (unchanged). The designer of a `<UserControl>` root draws no window frame
(`ViewFrameStyle::user_control`).

**Input inside nested views (router).** The inner views of user controls and Repeater items used to paint without
the window's input router, so a custom control inside them got no mouse, wheel or key event. They now register with
the router inside a **dispatch scope** (`events::router::DispatchScope`):
- `ControlScope`, a user control's instance, whose view model answers first and then falls back to the enclosing
  scope;
- `ItemScope`, a Repeater item: its row, its user control, then the page.

The ids are made unique per instance. A user control's view is compiled in an id scope hashed from its element's
scoped id (`compile::scope_hash`), as Repeater items already were per item key. Router ids, focus ids,
`AcceptButton`/`CancelButton` and accessibility ids no longer collide (the accessibility tree had duplicate nodes,
which AccessKit refused). The host also drops duplicate node ids and catches an AccessKit panic in `WM_GETOBJECT`.

**Context menus of a user control.** The `<ContextMenu>`s of a user control's own view (and of an `ItemTemplate`
user control's view, per item) are offered to the window every frame (`FrameServices::local_menus`, keyed per
instance). An inner element's `ContextMenu="menu"` opens it, as does `show_context_menu("menu", …)` from the user
control's code. Its `OnOpening` and item handlers run in its scope.

**Visual inheritance** (Windows Forms' inherited forms and user controls; `kubuno_views_meta::inherit`):
- A view's root may name a base view, `x:Inherits="base_form.kbview"`, relative to the view's file. The merge puts the
  derived file's elements first, in order (so their ids, and the designer's edits, stay those of the derived
  document), then the base controls it does not override, marked `x:Inherited="true"` (descendants `"inner"`). An
  element of the derived view with the `x:Name` of a base control, at the same place, overrides it: its attributes
  override the base's, which requires the base control's `Modifiers` to be `Protected`, `Public`, `Internal` or
  `ProtectedInternal`. A private container written with no attribute, only to reach its protected children, stays
  locked. Chains merge recursively (depth 8, so a loop is an error).
- `#[kubuno::view]` embeds the merged view and tracks the view and its bases (no hot reload of an inherited view). A
  `#[base] base: BaseForm` field shares the derived form's `Form`, lends its controls (`__kubuno_members`), and runs
  the handlers that only the base view names. Without `#[base]`, the derived struct gets a field for each `Protected`
  or `Public` base control, and the base's private ones stay out of reach, as in Windows Forms.
- `#[derive(UserControl)] #[kubuno(extends = AddressEditor)]`, with the base user control as its `base` field, gets
  `levels(UserControl)` implicitly. Its view model, handlers and properties fall back to the base's. Its view may
  inherit the base's (`x:Inherits`), and the derive embeds the merged view.
- The runtime and the design surface merge again (`compile::compile_full` reads the bases next to the view): in the
  designer, the base's controls show with a padlock and are not selectable (`design::INHERITED_LOCKED_PREFIX`), while
  the override elements are selected and edited in the derived document.
- Item templates: **Kubuno Inherited Form** and **Kubuno Inherited User Control** (`InheritedViewWizard`, an
  Inheritance Picker; the override elements of every changeable base control are pre-written, nested in their named
  containers).

**Out of date.** After a design build, the project's folder and its control libraries (path dependencies) are
watched (`DesignSourceWatch`). Saving a file that declares a control (a `derive` of `UserControl`, `Component`,
`PropertyValue` or `EventArgs`) or a user control's view moves the runtime to `OutOfDate`: an info bar says the views
show the previous version, with **Générer**. The build's design build brings the designers back to `Project`,
without reopening them.

**Code first.** `Custom::<T>::new()`, `Custom::<T>::init(|t| …)` and `Custom::from_instance(t)` create a project
control in code: its declared properties are read from the instance, and those differing from `T::default()` are
written. They come with `name`/`location`/`size`/`bounds`/`anchor`/`dock`/`property`/`on::<A>(event)`, and the
control joins a form with `controls().add(&c)`. The struct update syntax (`AddressEditor { street, ..Default::default() }`)
does not work outside the control's module (its `base` field is private), hence `init`.

**Add New Item at the project root.** A control added next to `Cargo.toml` is declared from the crate root with a
`#[path]` (`ControlItemNames.ModulePathFrom`). The language server and `#[kubuno::view]` scan the **whole package**
for control declarations (not only `src/`; `target`, `obj`, `bin`, hidden folders and nested packages are skipped):
the Properties window of a control declared next to `Cargo.toml` was empty. `Kubuno.Rust.Sdk` counts the views
(`src/**/*.kbview`) and the files next to `Cargo.toml` (`*.rs`, `*.kbview`, images) as build inputs: saving a view
alone left the build up to date.

**Clicks on a user control.** A user control's own view root is the user control itself, as in Windows Forms: it is
merged into the element using the user control in the router (`InputRouter::absorb_view_root`), instead of covering
it. The pointer over the user control's own surface reaches its `on_mouse_…` overrides and raises the `Click` of the
element using it. The router raises that `Click` (`ProjectInfo::routed_click`), since a user control's node, like a
custom control's, never raised it. The handlers its root names (`<UserControl OnClick="…">`, Windows Forms'
`this.Click += …`) run on the user control after the page's. A click on a label or a button of its view is that
control's (Windows Forms does not raise the user control's `Click` then either). An `ItemTemplate` item's root is
given the item's instance, so its overrides get the pointer too.

**Handlers of a user control's own view.** A double-click in the ⚡ tab of a user control's designer writes a
method in its `#[event_handlers]` impl (also when written `#[kubuno::views::event_handlers]`), named
`<x:Name>_<event>` with its typed arguments, as for a form. A project args type is written `crate::<module>::<Args>`.
It used to write a legacy free function `fn on_x_click(vm, value)`.

**A user control extending another one** (`FancyAddress` over `AddressEditor`) has the base's properties in the
registry, so a view may set `Street` on it (`registry::project::merged`, when the base is a project class).

**Library crates.** A user control of a library crate the project depends on (`uclib = { path = "../UcLib" }`)
appears in the Toolbox's « <project> Composants » tab. The language server scans path dependencies that depend on
`kubuno-views` **or on the `kubuno` facade**, so its Properties window and ⚡ tab are filled (they were empty).

**Design-time data.**
- Attributes prefixed with `d:` apply in the designer only (`inherit::apply_design_attributes`, run by
  `compile_full` when `design::design_time()`): `d:Text`, `d:Visible` (a control bound `Visible="{Binding …}"`
  shown while designing)…
- `d:ItemsSource` gives a Repeater's sample items: a JSON array of objects, inline or in a file next to the view.
  A byte order mark is accepted.
- Without it, the sample values follow the bound field's name and type (`items::design_sample`): initials, counts,
  booleans, colours, times, dates, image paths left empty.
- The design surface opens in the culture the application starts in: the Windows UI culture, or its parent, when
  the project has that `.kbres` (`RustDesignSurfaceHost.DefaultDesignCulture`). « (Par défaut) » is still offered.

**The inherited view's designer** shows the derived view at the size the merge gives (`compile::designed_view_text`),
and the padlock sits in the locked control's top-right corner, clear of its text.

**Limits.**
- The Properties window of an override element shows the attributes the derived view writes, not the base's merged
  values.
- An inherited form is not hot-reloaded.
- Binding a user control's computed text needs an `on_change` method that stores it in a (`#[browsable(false)]`)
  property.
- Inside a `<Repeater>`, an item's user control is the item's own instance: with `ItemTemplate`, its row fields set
  its properties of the same name.
- `#[default_value]` is metadata (the Properties window's bold / Reset): the field's initial value is the struct's
  `Default`, as a WinForms `[DefaultValue]` does not set the field either. Derive `Default` by hand when it differs.
- A project class named like a built-in one (`Card`) is shadowed by the built-in class in views.

**Tests.**
- `kubuno-views-meta`: `inherit::tests` (4).
- `kubuno-views`: `tests/custom_controls.rs`, routed events of a custom control inside a user control inside a
  Repeater; the context menus of a user control and of an `ItemTemplate` user control; unique accessibility ids;
  the `Click` and override-raised events of a user control on a page and inside a Repeater, and its own `Click`
  subscription. `items::design_sample_tests` (sample values, a `d:ItemsSource` file with a byte order mark).
- `kubuno-views-ls`: `a_library_of_the_facade_holds_controls`,
  `a_user_control_of_a_facade_application_gets_typed_windows_forms_named_methods`.
- `kubuno-controls`: `duplicate_node_ids_are_left_out_of_the_update`.
- C#: `UserControlDesignerTests` (design size, colour and lines editors, Toolbox bitmap, out-of-date texts, design
  culture),
  `InheritedViewNamesTests`, `DesignSourceWatchTests`, `ControlItemNamesTests.A_control_added_at_the_project_root_is_declared_with_a_path`.
