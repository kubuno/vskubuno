# Kubuno XML views — design note (Phase 2)

> Scope: the declarative view format for `kubuno_ui`
> (`Z:\src\desktop\windows\src\crates\kubuno-ui`), its runtime, its
> metadata registry, and how it is edited surgically by the VS designer and by
> Claude. This is the design that Phase 2 of `docs/ARCHITECTURE.md` (component
> metadata registry, XML loader, hot reload) should implement. Nothing here has
> been built; this note is the plan, grounded in the widget library and the
> real shell screens as they exist today.

## 0. What the runtime actually looks like today

Everything in `kubuno_ui` is **immediate-mode**: a page is a plain function
that is re-run every frame. `kubuno_controls::host::run_with_options`
(`kubuno-controls/src/host/mod.rs`) calls a boxed `PaintFn` once per paint with
a `&dyn Canvas` and a `Frame` (mouse, wheel, modifiers, click count, DPI
scale…); there is no retained scene graph. A widget (`kubuno_ui::Widget`,
`kubuno-ui/src/widget.rs`) is a `measure`/`paint`/`hit_test` triple, built
fresh from application state and thrown away at the end of the frame — see
`shell/src/admin_users.rs::table()`, whose own comment says the `DataTable` is
"rebuilt from the state on every frame … so the pixels, the hit tests and the
scroll maths can never read a stale copy of the model." Three things *do*
persist across frames, owned by the caller: **layout memory**
(`containers::Panel::slots.design`, the anchoring reference),
**interaction controllers** (`navigation::TabsController`,
`containers::ScrollArea`, `ribbon::Ribbon`, `focus::FocusRing`), and the
**application state** itself. Any view format has to fit this shape: a view
is not a tree that is built once and mutated — it is a *recipe* re-evaluated
every frame against live state, plus a handful of long-lived controller
objects addressed by stable ids.

Controllers already return **events keyed by string id** rather than mutating
anything themselves: `Ribbon::frame` (`kubuno-ui/src/ribbon.rs:1366`) returns
a `RibbonRun { events: Vec<RibbonEvent>, .. }` where
`RibbonEvent::Clicked(String)` / `Changed { item, value }` carry the id the
`RibbonItem` was declared with (`ribbon.rs:1196`). `FocusRing::register`
(`kubuno-ui/src/focus.rs`) keys every focusable control by a `FocusId`, a
`const fn` FNV-1a hash of a `&str`. Both are exactly the shape an XML `x:Name`
/ event-handler system wants, and neither needs to be invented — they are
reused, not replaced.

## 1. The file format

An element is a component; the tag name is the Rust builder's name as it
already appears in `kubuno_ui` (`Panel`, `Card`, `Stack`, `Switch`,
`RadioButton`, `NumericField`, `TextField`, `DataTable`, `RibbonTab`…), and
attributes are the builder's fluent setters (`Dense="true"` →
`Card::dense()`, `Padding="XL"` → `Panel::with_padding`). This keeps one
source of truth for "what a component can do" — the builder API itself — and
avoids a parallel vocabulary that drifts from it.

**Layout.** `kubuno-ui/src/containers.rs` gives four engines, and its own
doc comment is explicit that this is deliberate: *"Not one line of Dock,
Anchor, Flow or Split arithmetic is written here: the four engines are
called, never copied."* XML views get exactly those four, no more:

- **Dock** — `<Panel>` children carry `Dock="Top|Bottom|Left|Right|Fill"`
  plus a `Height`/`Width` for the band, mapping straight onto
  `Panel::top/bottom/left/right/fill` (`containers.rs:590-618`). Bands stack
  in **reverse z-order** exactly as the engine resolves them (the last child
  eats the remaining space) — the XML loader must preserve document order
  as z-order, not reorder by convenience.
- **Anchor** — `<Button X="20" Y="10" Width="80" Height="24"
  Anchor="Top,Left,Right"/>` maps to `Panel::anchored(bounds, anchor)` /
  `Panel::fixed(bounds)` (`containers.rs:620-630`) — the WinForms
  design-time-coordinates model, for absolute placement and edge-stretching.
- **Flow** — `<Stack Direction="Horizontal|Vertical" Gap="MD">` over
  `kubuno_controls::layout_panels::flow_layout`, for the common "row of
  controls" and "column of fields" case that today is hand-rolled in every
  shell page (see `settings_view.rs::row()`, which recomputes `ROW_H`-tall
  bands by hand for six settings rows).
- **Split** — `<Splitter Orientation="Vertical"><Pane .../><Pane .../></Splitter>`
  over `kubuno_controls::layout_panels::SplitContainer`.

There is **no `<Grid>`**. `kubuno_controls` has no table/grid layout engine,
and rule 1 of `kubuno-ui/src/lib.rs` ("own a replica, never restate it") means
one is not manufactured just for XML. Every shell screen inspected
(`settings_view.rs`, `admin_users.rs`, `admin_groups.rs`) is built from
Dock/Anchor/Flow/Split already; if a real screen later needs a true grid, that
is a fifth `kubuno_controls::layout_panels` engine first, and an XML element
second — never the other way around.

**Containers as surfaces.** `<Card Title="…" Dense="true" Flush="true">`,
`<Panel Surface="Card|Layer|Raised|Well">`, `<ScrollArea>` map onto
`containers::Surface` and `containers::ScrollArea` directly.

**Resources / styles.** This is the one place XAML's model does not transfer.
Rule 3 of `lib.rs` is "paint through `Canvas` only… no colour literal in a
paint body" — every pixel comes from `Canvas::theme()` (a `Theme` resolved
per light/dark mode) or from a closed enum such as `Surface`, `ItemSize`,
`Density`, `Layout`. An XML `<Style>`/resource-dictionary system that lets a
view author set raw colours would build a second, driftable design system
next to `@kubuno/ui`'s tokens. So: **no free-form styling**. A view can only
choose among the enum values a component already exposes
(`Surface="Card"`, `Density="Comfortable"`, `Size="Large"`), each validated
against the metadata registry (§4) the same way the Rust compiler validates
`Surface::Card`. Theme tokens are not a resource dictionary a view edits —
they are `Canvas::theme()`, resolved at paint time, exactly as today.

**Names.** `x:Name="proxy"` is not a synthesized field name (there is no
retained widget to name) — it *is* a `FocusId::of("proxy")`, the same
identity `FocusRing::register` and the event tables above already use. This
makes `x:Name` free: the loader does not invent an id scheme, it feeds the
attribute string into the hashing the focus/event system already has.

## 2. Code-behind in Rust

Two options, matching the prompt's framing:

**(A) Runtime interpretation** — the XML is parsed into a small AST (§6), and
a loader walks it once per file-change (not once per frame — see §5's
"compile the plan, evaluate the bindings" split) building a tree of
constructor calls resolved through the metadata registry (§4): each XML
element name looks up a `(build_fn, property_setters)` entry and applies the
declared attributes as setter calls. Events are dispatched by string name
into a **handler table** the code-behind registers — a `HashMap<&'static str,
Box<dyn FnMut(&mut State, ViewEvent)>>` populated in the view struct's
constructor. This is a direct generalisation of what already exists:
`Ribbon::frame` already returns `Vec<RibbonEvent>` keyed by the id an XML
attribute would carry; a generic interpreter just needs to route those by
name instead of a hand-written `match`.

**(B) `build.rs` code generation**, never committed — parses the same XML at
build time and emits Rust into `OUT_DIR` (a typed accessor struct for
`x:Name`s, and/or a fully inlined tree-construction function), `include!`'d
by the code-behind. This gets compile-time checking (a typo in a component
or property name is a build error) and no runtime parser in the release
binary.

**Recommendation: (A) as the only *behavioural* path, with (B) as a strictly
additive, optional validation/sugar layer.** Hot reload (§5) requires
re-evaluating the view without recompiling, which by definition means an
interpreter has to exist and be exercised at runtime; if `build.rs` also
owned construction, "hot reload" and "what actually ships" would be two code
paths that can diverge — precisely the "designer works, build breaks" class
of bug `docs/ARCHITECTURE.md` is written to avoid. So the interpreter is
canonical, in dev *and* in release builds. `build.rs` is then free to do two
things without touching runtime behaviour: (1) run the same parser +
registry lookup at build time purely to fail the build on an unknown
component/property/enum value or a dangling `OnClick` reference — a
compile-time lint, not codegen; (2) optionally emit a small `OUT_DIR` file of
`pub const` `FocusId`s per `x:Name`, so code-behind writes
`FOCUS.proxy_field` instead of `FocusId::of("proxy_field")` — pure sugar, a
cache-hit on unchanged `.kbview` file hashes, never checked in, and the
interpreter does not depend on it existing.

## 3. Data binding

Because a page is rebuilt every frame from live state already (§0), one-way
binding is nearly free: `Text="{Binding Title}"` is evaluated by reading
`state.get("Title")` (a small `Bindable` trait the code-behind's state struct
implements, or — for the common case — a `macro_rules!` that derives
field-name lookups) at the moment the element is built, every frame. There is
no dirty-checking or observer graph to build, because immediate mode already
re-derives everything.

Two-way binding (`Value="{Binding SyncIntervalMin, Mode=TwoWay}"`) uses the
event side of the same mechanism: when the live widget reports a change (a
`NumericField` step, a `Switch` toggle), the interpreter calls
`state.set("SyncIntervalMin", value)` directly instead of requiring a named
handler — this is what removes the boilerplate `settings_view.rs::apply()`
currently hand-writes for the common "toggle a bool field" case. An explicit
`OnToggled="offline_toggled"` handler (§2) remains available, and is required
exactly where `settings_view.rs::apply()` shows it is today: the `Offline`
switch does **not** write into `Settings` — `apply()`'s own match arm is
`Hot::Offline | Hot::Proxy => {}`, because that flag lives in the live sync
engine, not the persisted model. Binding cannot guess that; a handler can.

Lists follow the shape `admin_users.rs::table()` already uses, not a generic
per-item widget template: `DataTable`/`ListView` do not composite an
arbitrary widget tree per row, they take `ListViewItem::new(name).with_sub(…)`
columns. So a bound list is

```xml
<DataTable Items="{Binding Users}">
  <Column Header="Utilisateur" Binding="{Binding Name}"/>
  <Column Header="Rôle" Binding="{Binding Role}" CellRenderer="paint_role_cell"/>
</DataTable>
```

which the loader turns into exactly the `.iter().map(|u| ListViewItem::new(...)
.with_sub(...)).collect()` shell pages already hand-write, plus — for a
column that needs custom painting the way `admin_users.rs` colours a status
pill — a `CellRenderer` attribute that names a handler installed on
`DataTable::cell_painter`, the same escape hatch the Rust code uses today.
Inventing a richer per-cell XAML `DataTemplate` would model something
`kubuno_ui::tables::DataTable` does not have.

**As built (lot F1, 2026-09-30):** a list of *views* is `<Repeater ItemsSource="{Binding Messages}"
ItemTemplate="MessageRow" ItemKey="Id"/>` (or the item's element written inside the `<Repeater>`): one user control
instance and one live tree per item in view (virtualised, identified by `ItemKey` across changes), bindings read
the row, then the user control, then the page. `Value::List` holds `Rows`, a shared snapshot with a stamp, and
every list element rebuilds its items only when the stamp (or, for a list converted anew each frame, the rows)
changed.

Conditionals reuse a field the layout engine already carries:
`kubuno_controls::layout::Item::visible` (`containers.rs:430`) is read by
`Panel::paint_children_unclipped` to skip a child outright. So
`Visible="{Binding ShowAdvanced}"` is not new machinery — it sets `item.visible`
before layout, exactly like `Child::item.visible` already gates painting and
hit-testing.

## 4. The component metadata registry

Given `kubuno-ui/Cargo.toml` deliberately keeps its dependency list
to `kubuno-controls`, `drive-app-controls`, `windows-numerics` and `windows`
(it is linked into every app), the registry must
**not** live inside `kubuno_ui` itself and must not pull `syn`/`quote` into
that crate. It belongs in a new sibling crate (e.g. `kubuno-views`) that
depends on `kubuno-ui`, never the reverse — the same layering
`docs/ARCHITECTURE.md` already uses for the VS extension ("everything that
knows Rust/Kubuno is in Rust; C# only integrates").

For "minimal boilerplate" without touching any existing `kubuno-ui` file (this
note owns only this document — nothing in `kubuno-ui` should need editing to
adopt XML views), a proc-macro derive on every builder is the wrong shape: it
would require annotating `containers.rs`, `buttons.rs`, `ribbon.rs`,
`tables.rs`… one at a time. `kubuno-ui` itself already leans on
`macro_rules!` to remove this kind of boilerplate (`containers.rs`'s
`panel_builders!`, forwarding `Panel`'s builder surface onto `Card` and
`GroupBox` without repeating twelve methods) — the registry should follow the
same idiom: one declarative table per family, in the new crate, entirely
additive:

```rust
component!(Card {
    ctor: Card::new,
    props: {
        title: String => Card::set_title (default: "") / "The header title. Empty hides the header band.",
        dense: bool   => |c: Card, v| if v { c.dense() } else { c } (default: false),
        flush: bool   => |c: Card, v| if v { c.flush() } else { c } (default: false),
        surface: enum [Card, Layer, Raised, Well] => |c: Card, v| c.with_surface(v) (default: Card),
    },
    children: single_widget, // body_rect
});
```

Per component: display name, constructor, and per property — name, Rust
type, a setter closure, a default, `enum` variants when the type is a closed
enum (feeds the XSD/schema *and* the property grid's dropdown *and* the
language server's completions from one list), and a doc string lifted from
the existing `///` doc comment where practical (kept as a literal here since
macros cannot read doc comments off another crate). Events and the children
model (`none` / `single_widget` / `list<Item>`) are declared the same way.
This one table is the single source `docs/ARCHITECTURE.md` Phase 2 asks
for: the loader reads it to build widgets, a generated XSD/JSON-schema reads
it for VS's XML IntelliSense, the language server reads it for hover and
completion, and the property grid (Phase 4) reads it for its rows.

## 5. Hot reload

Because state (the code-behind struct) and controllers (`FocusRing`,
`TabsController`, `ScrollArea`, `Ribbon`) are owned by the application, not by
the interpreter, reloading is naturally non-destructive: only the *recipe* — the
parsed tree — is replaced when the `.kbview` file changes on disk (watched
with the `notify` crate, the standard choice, or the VS extension's own file
watch when editing through the designer). Controllers and state persist
across the reload; only elements whose `x:Name` disappeared lose their
identity, which is already the behaviour `FocusRing::end_frame` implements
for a control that stops registering ("a focused control that did not
register this frame … loses the focus, like a removed DOM node").

To avoid re-parsing every frame (§0's 60–120 Hz repaint loop),
reload is two-phase: **parse+compile** happens once per file change,
resolving every element against the registry into a small closure plan;
**bind+paint** happens every frame, only re-evaluating `{Binding …}`
expressions and calling `measure`/`paint`. This mirrors the same split
`TabsController`/`Ribbon` already use between "structure" (tabs, groups —
rebuilt when the caller's data changes) and "per-frame interaction state"
(scroll, slide animation, hover).

A parse or a registry-lookup error must not blank the screen: the loader
keeps the **last successfully compiled plan** and keeps painting it, while
showing a small in-app banner with the file, line and column (Vite/rust-analyzer's
own "keep the last good tree" resilience). The same diagnostic — file +
`TextRange` from the lossless parser (§6) — is handed to the language
server as an LSP `Diagnostic`, so the in-app overlay and VS's Error List
report the exact same error from one computation, never two.

## 6. A lossless XML tree for surgical edits

The repo has direct, recorded experience here:
`desktop/windows/src/shell/Cargo.toml` pins `tauri-winrt-notification` to
`>= 0.7.3` specifically because up to 0.7.2 it built toast XML with
`quick-xml` 0.37, "which carries RUSTSEC-2026-0194 and RUSTSEC-2026-0195
(quadratic parsing and unbounded namespace allocation)" — so whatever is
chosen here needs its advisory history checked at adoption time, not
assumed clean because the crate is popular.

More fundamentally, `quick-xml` and `roxmltree` are the wrong shape for this
job on their own merits, independent of that CVE history: `roxmltree` is a
**read-only**, borrowed DOM (excellent for the runtime loader's parse step,
useless for an editor that must write changes back); `quick-xml` is a
streaming event reader/writer with no tree and no diagnostics API — building
"preserve comments and formatting, edit one attribute, re-serialise
byte-identically elsewhere" on top of it means hand-building the exact same
thing `rowan` already is. `xot` is a genuine edit-capable XML tree
(arena-based, mutation API) but is a small, single-maintainer crate with far
less field use than the alternative below, for a component that both the VS
designer and Claude will depend on for years.

**Recommendation: a small `rowan`-based lossless CST**, following the
architecture `taplo` (the TOML toolkit behind Even Better TOML / JetBrains'
TOML support) already proved for a sibling problem — a config-like format
that needs an editor, a formatter and a language server from *one* grammar.
`rowan` is what rust-analyzer itself is built on: a green/red tree that
keeps every byte (whitespace, comments, attribute order) as a "trivia" token
attached to real nodes, offers `TextRange`/`TextSize` for diagnostics for
free, and supports incremental re-parsing — which is exactly what hot reload
and an LSP both need. A hand-written XML tokenizer feeding this tree is a
few hundred lines (the format here is deliberately smaller than general
XML — no namespaces beyond the one `x:` prefix, no processing instructions,
no DTDs), and a thin typed `ast` layer over the green nodes (again, the
`rust-analyzer`/`taplo` pattern) is what the interpreter (§2), the `build.rs`
validator, and the language server all consume — one parser, three
consumers, no drift. `roxmltree` stays useful only as a quick way to validate
the approach in a throwaway prototype before investing in the `rowan`
grammar; it should not become a second, permanent parser in the tree.

## 7. Worked example: `settings_view.rs`

`shell/src/settings_view.rs` is a good test because it is completely
hand-rolled today: `row()` computes `ROW_H`-tall bands by arithmetic, `hit_test`
and `draw_rows` are two functions kept in sync by hand over a `thread_local!
SCROLL`, and `apply()` is a `match` from a `Hot` enum onto `&mut Settings`.

```xml
<!-- settings_view.kbview -->
<View xmlns="kubuno/ui/2026" x:Class="shell::settings_view::SettingsView">
  <ScrollArea x:Name="scroll">
    <Card Padding="XL">
      <Stack Direction="Vertical" Gap="none">

        <Panel Height="56">
          <Label Dock="Left" Text="Thème"/>
          <RadioButtons Dock="Right" x:Name="theme"
                         SelectedValue="{Binding Theme, Mode=TwoWay}">
            <RadioOption Value="System" Label="Système"/>
            <RadioOption Value="Light"  Label="Clair"/>
            <RadioOption Value="Dark"   Label="Sombre"/>
          </RadioButtons>
        </Panel>

        <Panel Height="56">
          <Label Dock="Left" Text="Intervalle de synchronisation"/>
          <NumericField Dock="Right" x:Name="interval" Width="96"
                         Min="1" Max="120"
                         Value="{Binding SyncIntervalMin, Mode=TwoWay}"/>
          <Label Dock="Right" Text="min" Width="32"/>
        </Panel>

        <Panel Height="56">
          <Label Dock="Left" Text="Notifications"/>
          <Switch Dock="Right" x:Name="notifications"
                   On="{Binding Notifications, Mode=TwoWay}"/>
        </Panel>

        <Panel Height="56">
          <Label Dock="Left" Text="Démarrer avec Windows"/>
          <Switch Dock="Right" x:Name="autostart"
                   On="{Binding Autostart, Mode=TwoWay}"/>
        </Panel>

        <!-- Two-way binding would be wrong here: Offline is not persisted
             on Settings (see apply()'s Hot::Offline arm today). -->
        <Panel Height="56">
          <Label Dock="Left" Text="Travailler hors ligne"/>
          <Switch Dock="Right" x:Name="offline" On="{Binding Offline}"
                   OnToggled="offline_toggled"/>
        </Panel>

        <Panel Height="56">
          <Label Dock="Left" Text="Proxy sortant"/>
          <TextField Dock="Right" x:Name="proxy" Width="320"
                      Placeholder="http://hôte:port (aucun)"
                      Text="{Binding Proxy, Mode=TwoWay}"/>
        </Panel>

      </Stack>
    </Card>
  </ScrollArea>
</View>
```

Code-behind (`settings_view.rs`, trimmed):

```rust
#[derive(Bindable)] // or a hand-written `impl Bindable for SettingsState`
struct SettingsState {
    theme: ThemeSetting,
    sync_interval_min: u32,
    notifications: bool,
    autostart: bool,
    offline: bool,   // mirrors the sync engine, NOT written back by binding
    proxy: String,
}

pub struct SettingsView {
    view: kubuno_views::CompiledView,   // §5's "plan", rebuilt on file change
    state: SettingsState,
    focus: FocusRing,
}

impl SettingsView {
    fn handlers(&self) -> kubuno_views::Handlers<SettingsState> {
        kubuno_views::handlers! {
            "offline_toggled" => |s: &mut SettingsState, on: bool| {
                s.offline = on; // actual engine call lives one level up
            },
        }
    }

    pub fn frame(&mut self, c: &dyn Canvas, bounds: Rect, f: &host::Frame) {
        self.view.frame(c, bounds, f, &mut self.state, &mut self.focus, &self.handlers());
    }
}
```

Everything `settings_view.rs` currently hand-writes — `row()`'s arithmetic,
the `thread_local! SCROLL`, the parallel `hit_test`/`draw_rows` pair, the
`Hot` enum and most of `apply()` — is replaced by the interpreter and the
binding layer; only `offline_toggled`'s real side effect (and the styling
choices, if any, beyond what `Card`/`Surface` already give it) remain
hand-written Rust, which is exactly where hand-written Rust belongs.

## 8. Phased plan

| Step | What | Effort | Risk |
|---|---|---|---|
| 2a | `kubuno-views` crate skeleton + `macro_rules!` metadata table for `containers`, `buttons`, `text`, `range` (enough for §7) | S | Registry silently drifting from `kubuno-ui` as builders change — no compiler-enforced link between the two; mitigate with a checklist rule (new public builder setter → registry entry) and a small `cargo test` that at least calls every registered `ctor`/setter once. |
| 2b | Hand-written lossless XML tokenizer + `rowan` green/red tree + typed `ast` layer, scoped to the subset in §1/§6 (no namespaces beyond `x:`, no DTD/PI/CDATA) | L | The single biggest net-new investment in this plan; no ready-made crate fits exactly (§6). Contain scope by deferring anything beyond what real shell screens need — do not build general XML. |
| 2c | Interpreter: `ast` → registry lookups → widget tree, split into parse+compile (per file change) / bind+paint (per frame, §5) | M | Per-frame binding evaluation cost at 60–120 Hz; mitigate with the two-phase split already designed in, and benchmark against a hand-written page (`settings_view.rs` itself) before adopting further. |
| 2d | Two-way binding + named handler table (`Bindable`, `handlers!`) | S–M | Mostly plumbing; the risky part (event identity) is already solved by `FocusId`/`RibbonEvent`'s string-keyed precedent. |
| 2e | Hot reload: file watch (`notify`), last-good-plan fallback, in-app error banner with line/col | S | Depends entirely on 2b's `TextRange`s existing; nothing new architecturally once 2b/2c ship. |
| 2f | Optional `build.rs` validation pass + `OUT_DIR`-only typed `x:Name` constants, content-hash cached, never committed | M | Must not slow every `cargo build`; must not become a second source of truth if it silently falls out of sync with the runtime interpreter — keep it read-only (validates, does not construct). |
| 3 | Language server over the same `rowan` AST + registry (diagnostics, hover, completion from `enum` metadata), embedded live preview | — (out of this note's scope) | Already phase 3 of `docs/ARCHITECTURE.md`; this note's payoff is that 2b–2d already produce everything phase 3 needs, so no rework. |

Suggested order: 2a and 2b in parallel (they do not depend on each other),
then 2c, then 2d and 2e together (both are thin layers on 2c), 2f last since
it is pure sugar. `settings_view.rs` (§7) is the natural first migration once
2c ships, precisely because it exercises Dock layout, one-way and two-way
binding, an explicit handler, and `FocusRing` continuity in one small,
already-tested file.
