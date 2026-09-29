# Kubuno Views — event system (WinForms model) — design note

> Scope: product request "a very complete event system modelled on Windows Forms, including the
> abstraction levels that let developers create their own events". This note maps each layer of the
> WinForms event model onto idiomatic Rust in `kubuno-views` (runtime), `kubuno-views-ls` (language
> server) and `vskubuno` (Visual Studio designer). It builds on
> `docs/XML_VIEWS.md` §2/§4/§7 and `docs/DESIGNER.md` §11 (Properties window, ⚡ tab).
>
> **Status (2026-09-29):** EVT-1 is **done** (see §9, "EVT-1 as built"); EVT-2 to EVT-8 are not
> started.

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
| EVT-2 | Input router and ordered synthesis (mouse, hover, keys, focus, validation) | `events/router.rs`, `node.rs` `fire` (typed), every `families/*.rs` call site | EVT-1 | L | Scripted `Frame` sequences asserting exact WinForms orders (§3); legacy `ViewEvent` tests unchanged; live: `view_preview` with an event log overlay |
| EVT-3 | Registry metadata and catalogue | `registry/{mod,export,docs_fr}.rs`, families' `EventMeta` tables, aliases, `validate.rs` alias hint; C# `Registry/EventMeta.cs`, `KbviewElementObject` categories and default event | EVT-1 | M | Registry test: every `default_event` exists; alias parse tests; C# JSON round-trip; live: ⚡ tab grouped with descriptions in VS experimental instance |
| EVT-4 | Typed handlers and migration | `kubuno-views-macros` `#[handlers]`, `EventSink`, `Runtime::frame_typed`, legacy adapter; `handler_insert.rs` typed stub | EVT-1, EVT-2 | L | `trybuild` pass/fail tests (bad signature, async + Handled); existing `handlers!` tests untouched; live: `KubunoLot8App` old and new style both click through |
| EVT-5 | Designer/LS commands | LS `compatibleHandlers`, `renameHandler`, remove-empty-stub, missing-handler diagnostic, convert code action; C# `GetCompatibleMethods`, surface double-click → default event, rename menu | EVT-3, EVT-4 | M | LS unit tests on temp workspaces; live: dropdown lists handlers, rename updates XML + Rust in one undo per file |
| EVT-6 | View lifecycle, window events, threading | `runtime.rs` lifecycle hooks, host close/activation plumbing, `UiDispatcher`, `spawn_local` executor | EVT-2 | M | Unit tests with a fake host; live: FormClosing cancel keeps the window, background thread `begin_invoke` updates a label |
| EVT-7 | Custom controls and user controls | `kubuno-views-meta` (shared grammar), `#[derive(Component/UserControl)]`, extensible registry (`inventory`), `<UserControl x:Class>`, LS `syn` scan, project Toolbox tab, per-project design host | EVT-3, EVT-4 | L | Macro and LS produce identical `ComponentMeta` (shared golden tests); live: a `RatingBar` user control appears in the Toolbox, is dropped, its event bound and raised |
| EVT-8 | Paint, scroll, drag and drop | `PaintEventArgs` for `<Canvas>`/custom controls, `ScrollEventArgs`, OLE drop target in the host, `DragEventArgs` | EVT-2, EVT-6 | L | Unit tests on synthetic drag sequences (Enter → Over* → Drop/Leave); live: drop a file from Explorer onto a list |

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

## Scope note (product owner, 2026-09-29)

Changing `kubuno_ui` (and `kubuno-controls`) is explicitly in scope whenever the event system needs it — e.g. real input events (mouse/keyboard/focus/validation sequences), hit-testing, Paint and drag-and-drop hooks belong in the widgets/host that execute them, not in a layer bolted on top. Constraints when doing so: `kubuno_ui` is a dylib without a stable ABI — after any change, rebuild every exe (`tools/build-all.ps1`) and restage; keep the gallery and the desktop apps (shell, drive, documents, chat) building and behaving; add CHANGELOG entries in the desktop repo.
