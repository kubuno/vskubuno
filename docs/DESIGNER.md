# Kubuno Views — visual designer (phase 4) — design note

> Scope: a WinForms/XAML-designer-class visual editor for `.kbview` files inside
> Visual Studio 2026, building on `docs/XML_VIEWS.md` (the format, the
> `kubuno-views` crate) and `docs/ARCHITECTURE.md` phase 4 ("preview ⇄ XML
> selection sync, property grid, toolbox"). Nothing here is built: `src/Kubuno.
> VisualStudio.Views` today only hosts `kubuno-views-ls` as a plain LSP text
> editor (`LanguageService/KubunoViewsLanguageClient.cs`); there is no editor
> factory, no toolbox, no embedded renderer. This note is the plan, grounded in
> that library, in `kubuno-views`/`kubuno-views-ls` as they exist today, and in
> what is known and unknown about VS's own WinForms/XAML designers.

## 1. Goal and UX

A custom editor for the `kbview` content type (`KbviewConstants.ContentType`,
`LanguageService/ContentDefinition.cs`), registered through `IVsEditorFactory`
alongside — not instead of — the existing plain-text LSP editor (VS allows more
than one registered editor per extension; "Open With" picks). The new editor
opens a **split Design | XML view**, modelled on the XAML designer's own split
(surface and text pane share one document, with a splitter), rather than
WinForms' separate-tab model (`Form1.cs [Design]` vs `Form1.cs`): a `.kbview`
file is XML text end to end, so keeping the source visible alongside the
surface — the way XAML does, not the way WinForms hides its generated-looking
markup — fits a format whose entire premise (`XML_VIEWS.md` §6) is that a
human and Claude both hand-edit that same text. *(Exact XAML-designer split
behavior — user-resizable per document vs. a fixed default ratio — is from
general knowledge of VS's XAML editor and should be verified against VS 2026
specifically, not assumed identical.)*

Both panes stay in sync in both directions:

- **Selection/caret sync.** Selecting an element on the design surface moves
  the XML pane's caret to that element's tag; placing the caret in the XML
  pane (or selecting a range) highlights the corresponding element on the
  surface — the same behavior VS's XAML designer and the WinForms designer's
  "View Code"/"View Designer" pairing both give, just continuously rather than
  only on view-switch, because here both views are visible at once.
- **Toolbox.** One entry per `ComponentMeta` from the registry (`kubuno-views/
  src/registry/mod.rs`), grouped by family — the module grouping already used
  in the crate (`registry::families::{choice, containers, data, display,
  text}`, plus the five components declared directly in `registry/components.
  rs` as a "Core" group). Drag from the toolbox onto the surface inserts a new
  element (§4 covers per-container drop behavior); drag onto the XML pane
  inserts the same element as text at the drop position — one insertion
  command, two possible target panes.
- **Design surface — the real rendering.** Not a schematic/mock: the surface
  is `kubuno-views`' own interpreter (`compile`/`runtime`/`node`) painting the
  same pixels the app would, exactly as `examples/view_preview.rs` already
  proves end to end for a standalone window. §3 covers how that gets embedded.
- **Selection adorners.** Resize handles at the 8 compass points on a
  selectable element's bounds, a dashed/marching selection outline, multi-
  select via Shift/Ctrl-click and rubber-band drag, WinForms-style **snaplines**
  (blue guide lines when an edge or center aligns with a sibling's, shown only
  while dragging) in absolute-positioned containers, and keyboard nudge
  (arrow = 1 DIP, Shift+arrow = a larger step) once a control has selection
  focus on the surface.
- **Properties window** with **Properties** and **Events** tabs (the WinForms
  `PropertyGrid`'s own split, toggled by its toolbar's lightning-bolt icon),
  driven entirely by registry metadata: each `PropertyMeta` becomes one row,
  typed by `PropKind` — `Bool` → a checkbox/dropdown, `Enum(&[...])` → a
  dropdown of exactly those variants (the same list the language server
  already offers as completion, `completion.rs`), `F32` → a numeric editor,
  `String` → a text box; any property's value can instead be typed as a
  `{Binding Path[, Mode=TwoWay]}` expression (a small popup editor, mirroring
  `kubuno_views::binding`'s grammar) which the grid renders distinctly (a
  small binding glyph, as XAML's own property grid marks a bound value). The
  **Events** tab lists `ComponentMeta.events`; each row shows the current
  `On*="handler_name"` attribute if present, with a dropdown of handler names
  already found via the language server (see §3), plus **double-click on an
  empty row to generate a new handler** — see below.
- **Double-click to create a handler.** Double-clicking a control on the
  surface — or the empty cell of its default event in the Events tab — is a
  **surgical** two-part edit, never a regeneration: (1) set the `On*="…"`
  attribute (a synthesized name, `<x:Name or ordinal>_<EventName>`) via
  `kubuno_views::edit::set_attribute`, and (2) insert a new handler `fn` stub
  into the code-behind `.rs` file, in the `handlers!` table shape `XML_VIEWS.
  md` §7 already shows (`"button1_click" => |state, _v| { }`). `kubuno-views-
  ls/src/definition.rs` already does the read half of this exact link
  ("`Click="handler"` → `fn handler` in a sibling `.rs` file (text search)");
  the designer needs the corresponding *write*, built the same way (see §6,
  DSG-10).
- **Document outline** reuses `kubuno-views-ls/src/symbols.rs`
  (`textDocument/documentSymbol`, already implemented — "the element tree,
  `x:Name` as the label") verbatim; no new Rust is needed for the tree itself,
  only a WPF tree view bound to the existing LSP response.

## 2. Source of truth: the VS text buffer, never disk

Every designer gesture — a drag, a resize, a toolbox drop, a property edit, a
double-click handler stub — ends as **one surgical edit applied to the live VS
`ITextBuffer`**, exactly the channel a normal keystroke uses, never a direct
file write. This is what makes VS's own Undo/Redo, dirty-state (`*` in the
tab), and interleaving with the developer's or Claude's own edits (made with
Claude's ordinary file-editing tools, per `docs/ARCHITECTURE.md`'s "the
developer edits what Claude generates … in Visual Studio, and Claude keeps
assisting after those edits") all correct for free: the designer is just
another writer of the same buffer, not a second document model that has to be
reconciled with it.

Concretely: `kubuno_views::edit`'s functions (`set_attribute`, `insert_child`,
`remove_child`, `move_child`, `remove_attribute` — `kubuno-views/src/edit.rs`)
already compute a precise `TextRange` on the *original* source and splice it,
returning the whole document's new text (see the module's own doc: "computes
one precise byte range … and splices it"). For the designer, that return
value needs to travel as **`{range, newText}`**, not just the new full-text
string, so the VS side can apply a single minimal `ITextEdit.Replace` instead
of diffing two whole-file strings to find what changed — a small, additive
change to `edit.rs`'s public surface (return the computed `TextRange`
alongside the spliced text; the splice itself is unchanged) that the LSP
bridge in §3 depends on. Applying that minimal range, rather than the whole
document, keeps unrelated marks (breakpoints if any, other extensions'
adornments, the developer's own scroll position) stable — the same reason a
normal LSP `textEdit` is scoped, not a whole-document replace.

A **drag** is the one gesture that must not become dozens of undo units or
buffer writes per second: intermediate mouse-move frames update the design
surface's **local, client-side** preview only (§3 — the preview process
already renders every frame regardless; a drag just feeds it a temporary
override of one element's bounds, never touching text). Only **mouse-up**
computes and applies the final surgical edit, wrapped in one `IOleUndoManager`
compound action (or `ITextUndoHistory` transaction) so `Ctrl+Z` undoes the
whole drag in one step — the same granularity WinForms' own designer gives a
drag today.

The compiled preview always re-renders from **buffer text**, pushed over IPC
(§3) on every text change — including an *unsaved* one, since the buffer, not
the file on disk, is authoritative. This mirrors `examples/view_preview.rs`'s
`FileWatcher`, with one change: `FileWatcher::poll` reads from disk on an
mtime change; the designer's equivalent reads from `ITextBuffer.CurrentSnapshot`
on `ITextBuffer.Changed` and pushes that text directly, disk never entering
the loop while the document is open. Pushes are debounced (~150–250 ms, the
same order of magnitude as `view_preview.rs`'s own 250 ms repaint-nudge timer)
so a fast typist does not trigger a full re-parse (`compile::compile`, priced
in `XML_VIEWS.md` §5 as "once per file change", not once per frame) on every
keystroke.

## 3. Architecture

**Two Rust processes, already precedented by two things that exist today.**

- **`kubuno-views-ls`** (`src/kubuno-views-ls`) is already spawned by
  `KubunoViewsLanguageClient.ActivateAsync` (`LanguageService/
  KubunoViewsLanguageClient.cs`) with redirected stdio, wrapped in a
  `Microsoft.VisualStudio.LanguageServer.Client.Connection` over
  `process.StandardOutput`/`StandardInput`. The client already implements
  `ILanguageClientCustomMessage2` and exposes `Rpc` (a `StreamJsonRpc.JsonRpc`)
  in `AttachForCustomMessageAsync` — the exact hook custom, non-`textDocument/*`
  methods need. On the Rust side, `server.rs`'s `handle_request` dispatches on
  `req.method` as a plain string match with a `MethodNotFound` fallback — 
  adding `kubuno/registry`, `kubuno/applyEdit`, `kubuno/elementAtOffset` is a
  few new match arms, not a protocol redesign. `kubuno-views-ls` already links
  `kubuno_views`' `syntax`/`ast`/`validate`/`registry` (its own module doc is
  explicit it deliberately does **not** link `node`/`compile`/`runtime`/
  `binding` — "another agent is actively building" those); adding `edit` to
  that same allowed set is the natural next addition, not a boundary change.
- **The design surface is a separate, new process** (proposed crate
  `kubuno-views-designer`, promoting `examples/view_preview.rs`'s logic out of
  `examples/` into a real, shipped binary), because rendering needs a live
  Win32 message pump and Direct2D swap chain (`kubuno_controls::host::
  run_with_options`), which is structurally incompatible with `lsp-server`'s
  blocking `for msg in &connection.receiver` loop (`server.rs::main_loop`):
  mixing a GUI pump into that loop would stall diagnostics/completion whenever
  the surface repaints, and vice versa. This process links `kubuno_views`'
  `compile`/`runtime`/`node`/`registry` — the *other* half of the crate
  `kubuno-views-ls` deliberately stays out of, keeping that existing boundary
  intact rather than merging the two roles into one process.

**Who computes an edit — decision.** Both processes stay Rust; C# never
computes a text splice or re-implements layout math, per `docs/ARCHITECTURE.
md`'s explicit rule ("everything that knows Rust/Kubuno is in Rust; C# only
integrates") and `XML_VIEWS.md`'s repeated warning against a second, drifting
source of truth. Within Rust, the split is: **the designer process turns a
gesture into a structured, semantic edit request** (`SetAttribute`,
`InsertChild`, `MoveChild`, a `Move/Resize` batched into `X/Y/Width/Height`+
`Anchor`) using the bounds/hit-testing only it has (the real layout engines,
via `kubuno_controls`); **`kubuno-views-ls` performs the actual textual
splice**, because it — not the designer process — holds the buffer-synced
`rowan` tree at the exact moment VS asks (`documents.rs`'s full-text sync on
every `didChange`). If the designer process instead returned raw new-document
text from its own, separately-pushed copy of the buffer, a keystroke landing
between that snapshot and a drag's mouse-up could be silently clobbered by an
edit computed against stale text. Routing every mutation through `kubuno-
views-ls` avoids that race. So: **designer process = gesture → intent;
language server = intent → text**, and the VSIX is the only thing talking to
both.

**IPC.** The designer process is spawned and owned by the VSIX exactly like
`kubuno-views-ls` already is (`Process.Start` with redirected stdio) and
speaks the same `StreamJsonRpc` idiom over its own stdin/stdout — not a named
pipe with a discovery file (the pattern `docs/MCP.md` uses for `kubuno-vs-mcp.
exe`, appropriate there because Claude Code, not the VSIX, owns that process's
lifecycle and must *find* an already-running `devenv.exe`). Here the VSIX
starts the child itself, so reusing the LSP client's own transport shape needs
no new discovery mechanism and is trivially testable with an in-memory duplex
stream the same way `tests/Kubuno.Mcp.Tests/McpProtocolTests.cs` already
proves out for a different pipe.

Proposed methods (all JSON-RPC over the process's own stdio channel):

`kubuno-views-ls` (added alongside its existing `textDocument/*`):
| Method | Params → Result |
|---|---|
| `kubuno/registry` | `()` → the JSON registry export (§5) |
| `kubuno/applyEdit` | `{uri, op}` (`op` = SetAttribute/RemoveAttribute/InsertChild/RemoveChild/MoveChild, addressed by a stable element id, §6 cross-cutting note) → `{range, newText}` |
| `kubuno/elementAtOffset` | `{uri, offset}` → `{elementId, range}` (XML→Design selection sync) |
| `kubuno/rangeOfElement` | `{uri, elementId}` → `{range}` (Design→XML selection sync) |
| `kubuno/insertHandler` | `{uri, elementId, event, handlerName}` → the `applyEdit`-shaped attribute change, plus the sibling `.rs` file path and insertion point (extends `definition.rs`'s existing lookup — §6, DSG-10) |

`kubuno-views-designer` (new channel, spawned separately, its HWND hosted per
below):
| Method | Params → Result / Notification |
|---|---|
| `kubuno/embed` | `{parentHwnd}` → (creates the child window; see below) |
| `kubuno/surfaceReady` | notification, `{hwnd}` — once the child HWND exists, for reparenting |
| `kubuno/setBuffer` | `{text, version}` — full-text push, replacing `FileWatcher`'s disk polling |
| `kubuno/elementAt` | `{x, y}` → `{elementId, bounds}` (hit test) |
| `kubuno/selection` | `{elementIds[]}` — repaint with adorners |
| `kubuno/beginDrag` / `kubuno/drag` / `kubuno/endDrag` | local, no-text-write feedback; `endDrag` returns the structured intent for the VSIX to forward to `kubuno/applyEdit` |
| `kubuno/paletteDrop` | `{component, x, y}` → `InsertChild` intent |

**Hosting the child window.** `docs/ARCHITECTURE.md` already names the target
("Embedded native preview (phase 4): HwndHost in a tool window"), and `Kubuno.
VisualStudio.Views.csproj` already references `PresentationCore`/
`PresentationFramework`/`WindowsBase` (WPF's `HwndHost` lives in
`PresentationFramework`) — the interop path was anticipated, though not yet
used. Today, `kubuno_controls::host::run_with_options`
(`kubuno-controls/src/host/mod.rs`) only ever creates a **top-level**
`CreateWindowExW` window and blocks its thread in its own message loop
("Blocks until the window is closed" — `run`'s own doc); `HostOptions` has no
parent-HWND field. Embedding needs additive work: a `WS_CHILD` creation mode
keyed off the HWND handed in through `kubuno/embed`, and DPI handling that
reacts to the parent's `WM_DPICHANGED` instead of querying its own (now
meaningless, as a child) monitor — `mod.rs`'s current per-monitor-v2 path
assumes a top-level window. This is the single biggest open risk in the plan
(§6): cross-process `SetParent` is a known, valid technique, but keyboard/
focus/Tab routing across the process boundary — reconciling VS's own
`IOleCommandTarget` chain with the child's own message loop — is untried here
and needs a spike first (fallback: an always-on-top overlay kept positioned
over the tool window's client rect — simpler, but z-order/clipping-fragile).

## 4. Layout model and per-container drop behavior

`XML_VIEWS.md` §1 and the validator's `COMMON_ATTRIBUTES`
(`kubuno-views/src/validate.rs`, duplicated for the language server in
`kubuno-views-ls/src/common_attrs.rs`) already give the WinForms-shaped
vocabulary: `Dock` (`Top|Bottom|Left|Right|Fill`), `Anchor` (a comma-combination
of edges), and `X`/`Y`/`Width`/`Height`, all *attached* properties read by the
parent, not modelled per-component — exactly how WinForms reads
`Control.Dock`/`Control.Anchor` off any control regardless of its type. The
designer's drag/drop behavior branches on the parent's layout engine:

- **Anchor (absolute canvas).** Free drag anywhere; resize handles change
  `Width`/`Height` and, per the `Anchor` edges set, may also change `X`/`Y`.
  Snaplines appear against sibling edges/centers while dragging (Shift
  suppresses snapping, as WinForms does). Every intermediate frame is local
  only (§2); mouse-up emits one batched `SetAttribute` for whatever of `X/Y/
  Width/Height/Anchor` changed.
- **Dock.** `XML_VIEWS.md` §1 is explicit that dock bands "stack in reverse
  z-order" and that document order **is** z-order — so a drag over a `<Panel>`
  with docked children shows an **insertion marker between existing bands**
  (a line, like WinForms' own dock/table insertion cue), not free XY. Drop
  computes the target index and emits `MoveChild`/`InsertChild` plus
  `SetAttribute Dock="…"`.
- **Flow (`<Stack>`, `ChildrenModel::List`).** No `X`/`Y` at all; drop shows an
  insertion caret perpendicular to `Direction` between blocks. Resize handles
  on a `<Stack>`'s direct children are suppressed (a block's extent comes from
  its own measured content plus `Gap`, not a settable size) — only the
  child's own intrinsic properties (e.g. a `TextField`'s `Width`) remain
  editable, never its position.
- **SingleWidget** (`Card`'s body — `ChildrenModel::SingleWidget`). Dropping a
  second control onto an already-filled body needs a product decision this
  note does not make unilaterally: replace the existing child, or auto-wrap
  both in an implicit `<Stack>`. Flagged for the phase-4 owner to confirm
  before DSG-9 (§6) implements either.
- **Split (`<Splitter>`/`<Pane>`).** Not yet a registered component (`XML_
  VIEWS.md` names it among `kubuno-ui`'s four engines, but only `Card`/`Stack`/
  `Button`/`Switch`/`TextField` are in `registry/components.rs` today) —
  dragging the splitter itself, once registered, changes a ratio/size
  attribute; a `<Pane>` behaves like `SingleWidget` for drops.

**A concrete registry gap this surfaces:** `ChildrenModel::List` alone cannot
tell the designer whether a container is Dock, Flow, or (once registered)
Split — today only `<Stack>` (Flow) is registered, so the ambiguity is latent,
but the designer needs a `LayoutKind` (or equivalent) alongside `ChildrenModel`
before `<Panel>`/`<Splitter>` are registered, or its drop visualization cannot
pick the right cue. This is a small, additive registry field (§6, DSG-1).

## 5. Registry export for the C# side

**Shape.** One JSON object per `ComponentMeta`: `{name, doc, properties:
[{name, kind, default, doc}], events: [{name, doc}], children: "None"|
"SingleWidget"|"List", family}`, where `kind` serializes `PropKind` as
`"Bool"`/`"F32"`/`"String"`/`{"Enum": ["A", "B", …]}` — a direct mirror of
`kubuno-views/src/registry/mod.rs`'s Rust types, nothing invented. Two
additive fields are needed beyond what `ComponentMeta` carries today: an
**icon** glyph name (none exists yet; `node.rs`'s `static_icon` table is the
right precedent for a short curated name list, reused for the toolbox) and
the **`LayoutKind`** from §4. `family` groups the toolbox exactly as `registry
::families::ALL_FAMILIES` already groups the table internally.

**Sync mechanism — decision.** Query it **live from `kubuno-views-ls`** via
`kubuno/registry`, not a JSON file generated at VSIX build time and shipped in
the box. Reasons: `registry::all()` is already a runtime `OnceLock`
(`registry/mod.rs`) computed once per process, so asking the *already-running*
`kubuno-views-ls.exe` costs one round trip at document-open time and, crucially,
always matches the exact binary the developer's own `KubunoViewsLanguageServerLocator`
resolved (option override → extension `tools\` → PATH → local dev build
folders — `Locating/KubunoViewsLanguageServerLocator.cs`), which per `CLAUDE.
md`'s "mode de dev choisi = publié" may be a locally-rebuilt, ahead-of-VSIX
`kubuno-views-ls`. A VSIX-baked JSON snapshot would silently drift from that
the moment either side changes — exactly the failure mode `XML_VIEWS.md`
keeps naming and mitigating elsewhere (the registry's own `smoke`-tested link
to `kubuno-ui`, `common_attrs.rs`'s own note about its hand-copied table).
Cached for the language client's session only; the registry cannot change
without restarting the process that computed it.

## 6. Phased plan — parallelizable work packages

Ten packages, each with an owner scope narrow enough for a separate agent/
session to hold without touching another package's files. **Cross-cutting
coordination point, called out once rather than in every row below:** DSG-2
and DSG-6 must agree on one **stable element-id scheme** (e.g. a path of
child-ordinal indices from the document root, independent of `x:Name`) before
either is implemented in isolation — this is the one place two packages share
a contract that is not just a JSON shape.

| # | Package | Owns | Depends on | Effort | Test strategy |
|---|---|---|---|---|---|
| DSG-1 | Registry JSON export + `kubuno/registry`, plus the additive `icon`/`LayoutKind` fields on `ComponentMeta` | `kubuno-views/src/registry/*` (additive fields), new `kubuno-views-ls/src/registry_export.rs` + `server.rs` wiring | none | S | Rust unit/golden-file tests on the JSON shape; no visual check |
| DSG-2 | `kubuno/applyEdit`, `kubuno/elementAtOffset`, `kubuno/rangeOfElement`; `edit.rs` returning `{range, text}` instead of whole-file text; the element-id scheme | `kubuno-views/src/edit.rs` (additive), new `kubuno-views-ls/src/edit_bridge.rs` + `server.rs` | none (defines the id scheme DSG-6 must match) | M | Rust unit tests, purely textual — CLI-testable, no visual check |
| DSG-3 | `IVsEditorFactory` for `.kbview`, split Design\|XML `WindowPane`, XML pane reusing the existing text-editor view | new `src/Kubuno.VisualStudio.Views.Designer/EditorFactory.cs`, `DesignerWindowPane.*` | none (design surface can be a placeholder pane until DSG-7) | M | Manual, experimental instance — **visual check**: opening a `.kbview` shows the split |
| DSG-4 | Toolbox + Properties/Events WPF tool windows, bound to DSG-1's JSON (a fixture JSON is enough to start) | new WPF views/viewmodels under the Designer project | DSG-1 (schema; can stub) | M | Viewmodel unit tests (PropKind → editor kind); **visual check** for grid rendering |
| DSG-5 | Buffer-diff/apply plumbing: `{range,newText}` → one `ITextEdit`, compound-action batching for drags | new `Infrastructure/BufferEditApplier.cs`, `DesignerUndoScope.cs` | DSG-2's response shape | S–M | Unit test against a fake/real `ITextBuffer`; manual Ctrl+Z check |
| DSG-6 | `kubuno-views-designer` crate: promote `view_preview.rs`'s runtime loop out of `examples/`, add `kubuno/setBuffer`, `kubuno/elementAt` hit-testing (needs an id on *every* node, not just `x:Name`'d ones), adorner paint pass | new crate `kubuno-views-designer` | DSG-2 (id scheme) | L | Hit-test math is unit-testable headless (layout is already exercised without a `Canvas` in `node.rs`'s own tests); adorner rendering needs a **visual check** (standalone run + screenshot) |
| DSG-7 | HwndHost embedding: `WS_CHILD`/parent-HWND mode + parent-driven DPI in `kubuno_controls::host`; C# `HwndHost` subclass doing the spawn/handshake/`SetParent`/focus forwarding | `kubuno-controls/src/host/mod.rs` (additive), new `DesignSurfaceHost.cs` | DSG-6 (can integrate against a placeholder colored window first) | L, **highest risk** | **Visual check mandatory** — this is exactly the class of change that cannot be verified by compiling alone |
| DSG-8 | Bidirectional selection/caret sync + Document Outline (reusing `symbols.rs` as-is) | `DesignerSelectionSync.cs` | DSG-3, DSG-6, DSG-2 | M | Manual, **visual check** (inherently a UI behavior) |
| DSG-9 | Drag/drop, snaplines, insertion markers per §4's container kinds; toolbox-drop into the foreign child HWND (likely a Win32 `IDropTarget` on the child window, not WPF's own `DragDrop`, since OLE drag-drop across an `HwndHost` boundary into another process's HWND is untested here) | split across `kubuno-views-designer` (snap/insertion-index math) and `DesignerDragDropSource.cs` | DSG-6, DSG-7 | L | **Visual check mandatory** |
| DSG-10 | Double-click → handler generation: `kubuno/insertHandler` extending `definition.rs`'s existing handler-location logic with an insert mode; Events-tab double-click wiring | `kubuno-views-ls/src/definition.rs` (extended), `HandlerInsertionCommand.cs` | DSG-2 | M | Rust unit tests for the locate/insert logic; manual check for the VS-side open/insert/caret-jump |

Suggested parallel start: **DSG-1, DSG-2, DSG-3 together** (no shared files,
DSG-2 only needs to publish its id scheme early for DSG-6 to target); **DSG-4,
DSG-5** once DSG-1/DSG-2's shapes exist; **DSG-6** as soon as DSG-2's id scheme
is fixed; **DSG-7** the moment DSG-6 has *any* window to embed (a placeholder
is enough to de-risk the HwndHost spike early, since it is the highest-risk
item); **DSG-8–DSG-10** last, since each depends on at least two earlier
packages landing.

### Risks

- **HwndHost cross-process reparenting in VS 2026** — the single largest
  unknown (§3): no code in this repo does this today; verify against the real
  2026 experimental instance before the plan commits to it, with the overlay
  fallback noted in §3 ready if it does not work.
- **Keyboard/focus routing** across the process boundary — `IOleCommandTarget`
  and VS's accelerator table do not automatically see a foreign-process child
  window; needs an explicit forwarding shim.
- **DPI** — `host`'s current per-monitor-v2 handling assumes a top-level
  window querying its own monitor; a reparented child needs the parent's
  `WM_DPICHANGED` instead (§3).
- **Re-render performance** — debounce buffer pushes (§2); drags stay local
  (no text write, no re-parse) until mouse-up.
- **Undo granularity** — a drag or multi-attribute edit must land as exactly
  one undo unit (§2, DSG-5), or the designer feels broken even when each edit
  is individually correct.
- **OLE drag-and-drop into a foreign HWND** (DSG-9) — likely needs a raw
  Win32 `IDropTarget` on the child window; WPF's own `DragDrop` does not
  natively span a process boundary, and this is unverified here.

## 7. DSG-7 spike findings (2026-09-25)

**Verdict: cross-process `WS_CHILD` embedding works; go with it, not the
overlay fallback.** Every mechanical risk listed in §3/§6 was measured with
real input (mouse/keyboard synthesized through the system input queue, not
posted messages) on Windows 11 26200, both processes per-monitor-v2, 168 DPI.
Two gaps remain, and they are protocol work, not feasibility risks: **VS
accelerators** and **Tab-out** (see below).

### What was built

- **Rust**: `kubuno_controls::host::HostOptions::parent: Option<isize>`
  (additive; every existing caller builds `HostOptions` through `new()`, which
  defaults it to `None`; the whole workspace still compiles). With a parent the
  host creates `WS_CHILD | WS_CLIPSIBLINGS | WS_CLIPCHILDREN | WS_VISIBLE` at
  the parent's client size, skips DWM attributes / size-to-DPI / caption
  handling, sets focus on left/right press, turns `WM_KILLFOCUS` into the
  "blur" (`Frame::dismiss`; a child never gets `WM_ACTIVATE`), handles
  `WM_DPICHANGED_AFTERPARENT`, and runs a **thread** timer (500 ms) that ends
  the loop when the parent is gone. Example:
  `kubuno-views/examples/view_embed.rs` (`--parent <hwnd> <file.kbview>`),
  which also paints a probe line and a "Menu" popup that deliberately
  overflows the child, and traces focus/key/size/DPI messages on stderr.
- **C#**: `vskubuno/spikes/HwndHostSpike/` (net48 WPF, PMv2 manifest, not in
  the .sln). `DesignSurfaceHost : HwndHost` creates an in-process `Static`
  container child, starts the Rust exe with `--parent <container>`, polls
  (non-blocking `DispatcherTimer`) for `GetWindow(container, GW_CHILD)`,
  forwards the container's `WM_SIZE` with `MoveWindow(child)`, overrides
  `TabIntoCore` to `SetFocus(child)`, and in `DestroyWindowCore` destroys the
  container, then waits up to 2 s for the process. `--selftest` runs every
  probe below and logs PASS/FAIL (all PASS on the final run).

### Measured answers

| Question | Result |
|---|---|
| Cross-process child creation | **Works.** Rust `CreateWindowExW(parent = WPF container)`; `GetParent` = container, `GetAncestor(GA_ROOT)` = the WPF window. Child visible ~250-300 ms after `Process.Start` (debug build), ~60 ms on restart. No `SetParent` needed: creating the child directly under the foreign parent is simpler and avoids flipping a top-level window's styles. |
| Rendering | The DirectComposition target on a **child** HWND of another process renders fine (`CreateSwapChainForComposition` + `CreateTargetForHwnd`). |
| Keyboard focus on click | **Works** once the host calls `SetFocus` on press (a child is not focused by a click on its own). Creating a cross-process child attaches both threads' input queues, so `SetFocus` across the boundary is legal. The WPF window stays the active window. |
| Keys reaching the child | **Works** (`WM_KEYDOWN`/`WM_CHAR` land in the Rust thread's queue). |
| Host accelerators while the child has focus | **Do not fire.** A WPF `KeyBinding` Ctrl+S on the window did not run; the child received Ctrl and S. In VS this means Ctrl+S, F5, Ctrl+Shift+B, Ctrl+Z... are dead while the design surface is focused: VS's pump (`IVsFilterKeys2` / `ComponentDispatcher`) never sees messages queued on another process's thread. **Needs a forwarding shim** (below). |
| Tab from WPF into the surface | **Works** with `TabIntoCore` -> `SetFocus(child)`. |
| Tab out of the surface | **Not possible today**: 6 Tabs inside the child kept the focus there (the view's own focus ring cycles). Needs a `kubuno/tabOut {backward}` notification from the surface -> `MoveFocus(new TraversalRequest(...))` on the WPF side. |
| Mouse capture | **Works**: press in the child, drag 200 px outside the whole window -> `GUITHREADINFO.hwndCapture` = the child. |
| Popups (`kubuno_ui` menus/dropdowns) | **Work as owned top-levels, positioned right.** `acquire_popup` passes the child as owner; Windows resolves a child owner to its top-level ancestor, so the popup's owner is **the WPF window in the other process** (z-order follows the host window, hidden/destroyed with it). It is placed via `ClientToScreen(child)` and overflows the child as intended; a click on the part lying outside the child reached the page with correct client-DIP coordinates, and the focus stayed in the child (`WS_EX_NOACTIVATE`). Clicking a WPF element moves the focus out -> `WM_KILLFOCUS` -> menu dismissed. |
| Resize | **Follows**: container `WM_SIZE` -> `MoveWindow(child)` -> Rust `WM_SIZE` -> swap-chain resize; sizes matched exactly at 3 window sizes. A cross-process `MoveWindow` is a synchronous cross-thread send, so the WPF thread waits for the Rust thread at each step (fine while the surface is responsive; a hung surface would stall WPF layout, so consider `SWP_ASYNCWINDOWPOS`). Visual check: after 4 rapid `SetWindowPos` in a row the final frame is correct (surface fills the container, no grey/black band); flicker *during* a live drag could not be judged from stills. |
| Visual check (orchestrator, 2026-09-25) | View rendered inside the WPF frame with the probe line (`dpi=168`, `focused=true` after a click). The "Menu" popup opens right under its button, overflows right and bottom out of the window and stays on top; **moving the host window with the popup open, the popup follows** (it stays attached under the button). Its panel is taller than its 8 rows: spike code (`view_embed` sizes the menu to the child's height on purpose, to force an overflow), not `kubuno_ui` popup sizing. Closing the window: both processes gone within 1.5 s. |
| Per-monitor DPI v2 | Both processes PMv2 (`GetAwarenessFromDpiAwarenessContext` = 2 for both windows); child DPI = parent DPI = 168. `WM_DPICHANGED_AFTERPARENT` was **not exercised**: all three monitors of the test machine run at the same scale. The host re-reads `GetDpiForWindow` every frame, so a missed notification still converges on the next paint. To verify on a mixed-DPI setup. A parent that is *not* PMv2 (VS 2026 is) would need separate testing. |
| Child crash | **Host survives** (hard kill of the Rust process: container alive, WPF fine); `StartSurface()` relaunches into the same container. The VSIX should restart it with backoff and show a placeholder while it is down. |
| Host closes normally | Container destroyed -> child gets `WM_DESTROY` -> Rust exits with code 0 within ~60-90 ms. |
| Host process crashes | First attempt **failed**: the system destroyed the child window along with the dead parent but sent this thread **no message**, and an HWND-bound watchdog timer died with the window, so the Rust process lived on. Fixed with a **thread** timer (`SetTimer(None, .., TIMERPROC)`) that ends the loop once the parent is gone: Rust now exits ~250 ms after `Stop-Process -Force` on the host. Belt and braces for VS: also put the child in a Job Object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, owned by devenv. |
| Runtime DLLs | The workspace links with `-C prefer-dynamic`: the exe needs `kubuno_ui.dll` **and** Rust's `std-*.dll` beside it (`tools/stage-runtime.ps1`). A missing DLL pops the loader's modal "DLL not found" dialog on the user's desktop. The spike checks the files before launching and calls `SetErrorMode(SEM_FAILCRITICALERRORS \| SEM_NOOPENFILEERRORBOX)` (inherited by the child). The VSIX must ship/stage the runtime with the designer exe and do the same. |

### Recommended approach for DSG-7 proper

1. Keep **create-as-child** (`--parent` / `kubuno/embed`) into an in-process
   container owned by an `HwndHost` that implements DSG-3's
   `IDesignSurfaceHost` (the spike's `DesignSurfaceHost` is the template),
   with size forwarding from the container's `WM_SIZE`.
2. **Keyboard shim** (the one real work item): the surface reports the keys it
   did not consume (Ctrl/Alt chords, F-keys, Escape with nothing open) as a
   `kubuno/unhandledKey {vk, mods}` JSON-RPC notification; the VSIX turns it
   into a `MSG` and calls `IVsFilterKeys2.TranslateAcceleratorEx` (or
   `IOleCommandTarget.Exec` for the few designer-relevant commands: Save,
   Undo/Redo, Delete, Copy/Paste). Posting the raw `WM_KEYDOWN` to the
   container (on the VS thread) is a cheaper alternative worth trying first.
   Same channel for `kubuno/tabOut {backward}` -> `MoveFocus`.
3. Process lifetime: thread-timer watchdog (done) + Job Object in the VSIX +
   restart-on-crash with a placeholder.
4. Error mode + runtime staging as above: never let a loader dialog reach the
   user.
5. Still to verify in the real VS 2026 experimental instance (not done in the
   spike: the DSG-3 designer is not wired into the VSIX yet, so no tool window
   could host it cheaply): the same `HwndHost` inside a `WindowPane`,
   docking/undocking (a floating tool window changes the top-level ancestor;
   the container moves with the WPF tree, but popup ownership is fixed when a
   popup window is created and must be re-checked after a re-dock), VS theme
   switches, and the accelerator shim. Mixed-DPI monitors also remain to be
   tested.

## 8. DSG-2 protocol

Work package DSG-2 (§6) is implemented: `kubuno-views-ls` now also links
[`kubuno_views::edit`], and answers `kubuno/applyEdit`,
`kubuno/elementAtOffset` and `kubuno/rangeOfElement` alongside its existing
`textDocument/*` methods, in `src/kubuno-views-ls/src/edit_bridge.rs` (+
wiring in `server.rs`). `kubuno-views/src/edit.rs` and `src/ast/mod.rs` grew
the additive surface this bridge is built on. This section documents the
actual wire shapes, which refine §3's sketch table in a few places — each
refinement is called out below with why.

### The stable element id

An element's id is the dot-separated path of **child-ordinal indices**
(element children only — text/comments never consume an index) from the
document root, exactly as §6's cross-cutting note asked for: **independent of
`x:Name`**. The document's root element is the empty string `""`; its first
child is `"0"`; that child's second child is `"0.1"`; and so on.
[`kubuno_views::ast::Element::stable_id`] computes it, and
[`kubuno_views::ast::Document::resolve_id`] is its exact inverse (`None` for a
malformed id, an out-of-range index at any level, or a document with no root
element — never a panic). Both live in `kubuno_views::ast` rather than in
`kubuno-views-ls` specifically *because* DSG-6 (`kubuno-views-designer`, a
separate process that also links `kubuno_views` but not `kubuno-views-ls`)
needs to compute the identical id from its own copy of the document text and
agree with the language server on "which element" — the one place, per §6,
the two packages share a contract that is not just a JSON shape.

An id is **stable across edits that do not reorder/insert/remove elements
above it in the tree**, and *not* stable across ones that do (inserting a
sibling before an element shifts every id after it at that level) — a
client (the designer process, or the VSIX) that holds an id across an edit it
just requested must treat it as invalidated and re-resolve, exactly as it
would already have to re-parse after applying a `TextEdit`.

Because the id *is* the ordinal path, a caller does not need to walk the tree
to find an element's own parent and index: splitting the id at its last `.`
gives `(parent_id, index)` directly (`"2.0.3"` → `("2.0", 3)`; `"3"` →
`("", 3)`; `""` has no parent — the document root cannot be
removed/moved/renamed as a designer gesture, a `.kbview` file always having
exactly one). `kubuno-views-ls`'s `edit_bridge::split_parent` is this one-line
string operation, not a tree walk.

### `kubuno_views::edit`'s new public surface

Every function in `edit.rs` now returns `Vec<Edit>` (`struct Edit { range:
TextRange, new_text: String }`) instead of a whole new document string — one
disjoint `{range, newText}` pair per byte range actually touched, still
expressed in the *original* document's own coordinates, still empty for a
no-op (an out-of-range index, a missing attribute, an id that will not
resolve). `apply_edits(text, &edits)` is the one place that still folds a set
of edits back into a whole-file string (applying them back-to-front by
`range.start()`, since edits from the same call never overlap), kept for the
module's own tests and for any caller that wants to re-parse in one step
rather than apply `TextEdit`s incrementally. This is what §2 asked for
("a small, additive change to `edit.rs`'s public surface: return the computed
`TextRange` alongside the spliced text") — refined from a single `{range,
newText}` to a small `Vec` of them, because a single splice cannot express a
move (old slot + new slot are two disjoint ranges) or a rename with a
separate end tag (two token ranges) as one contiguous replacement.

Two functions are new, both additive, neither touching `registry/*`, `node
.rs`, `binding.rs`, `validate.rs`, `props.rs` or `macros.rs`:

- `move_element(source_parent, from, dest_parent, to) -> Vec<Edit>` — the
  general case `move_child` (same-parent reorder only) does not cover: a drop
  into a *different* container (§4's Dock/Flow drop targets). Delegates to
  `move_child` verbatim when `source_parent`/`dest_parent` are the same
  element; otherwise removes the child from its old parent and inserts its
  own `<Tag …>…</Tag>` text into the new one, safe as two disjoint edits
  since the two parents' subtrees never overlap in that case. A no-op when
  the destination is the moved element itself or one of its own descendants
  (moving a container into its own contents has no sane result).
- `rename_element(element, new_name) -> Vec<Edit>` — rewrites the start-tag
  name token and, when present, the separate end-tag name token (a self-
  closing element only has the one). Every attribute, and the element's own
  `x:Name` *value* (never the tag name), is left untouched.

`ast::Element` gained `end_name_range()` (mirrors the existing `name_range()`,
for the end tag) and `stable_id()`; `ast::Document` gained `resolve_id()`.

### `kubuno/applyEdit`

```
{ "uri": "file:///settings_view.kbview",
  "op": { "kind": "setAttribute", "elementId": "0", "name": "Text", "value": "Annuler" } }
  -> { "edits": [ { "range": {...}, "newText": "Annuler" } ] }
```

`op.kind` (tagged on `"kind"`, not `"op"`, to avoid a field named the same as
its own containing object) is one of: `setAttribute {elementId, name, value}`,
`removeAttribute {elementId, name}`, `insertChild {parentId, index, xml}`
(`xml` a well-formed `.kbview` fragment, inserted byte-for-byte —
`insert_child`'s own doc; addressed by the *parent's* id, since the new
element has no id of its own yet), `removeElement {elementId}`,
`moveElement {elementId, newParentId, index}` (same parent as today for a
reorder, a different one for a cross-container drop), `renameElement
{elementId, newName}`. Every field name that is `snake_case` in Rust is
`camelCase` on the wire (`elementId`, `newParentId`, `newName`, …).

The result is always `{ "edits": [ {range, newText}, … ] }` — a **list**, not
the single `{range, newText}` §3's sketch table shows, because `moveElement`
(two disjoint ranges, unless it degenerates to a same-parent `move_child`,
which can still be two) and `renameElement` (two token ranges, unless
self-closing) cannot always be expressed as one contiguous replacement; the
VS side applies every entry in the list as its own minimal `ITextEdit
.Replace` (§2), inside the one compound/undo transaction DSG-5 owns for the
whole gesture. An id that does not resolve (stale, or simply invalid) yields
`{"edits": []}`, never an error — the same "degrade to no-op" rule
`kubuno_views::edit` itself uses for an out-of-range index, so a client
racing its own debounced re-parse against a fresh keystroke never gets a hard
failure for it. A `uri` with no open document behaves the same way.

### `kubuno/elementAtOffset`

```
{ "uri": "file:///settings_view.kbview", "position": { "line": 0, "character": 25 } }
  -> { "elementId": "1", "range": {...} }        // or `null`
```

Takes a `position` — an ordinary LSP `Position` (0-based line, UTF-16
character), the exact shape `textDocument/hover`/`/completion`/`/definition`
already use — rather than the flat byte `offset` §3's sketch names. This is a
deliberate refinement: every position this server already accepts goes
through [`crate::position::PositionIndex`]'s existing UTF-16 conversion, and
reusing that shape means neither side of the wire needs a second coordinate
system (VS's own `ITextSnapshot` positions convert exactly the same way for
every other request already wired up). Returns the **innermost** element
whose subtree contains that position (XML pane → design surface selection
sync, §1) together with its own full range; `null` when the document is not
open, has no root element, or the position lands on nothing (an empty file).

### `kubuno/rangeOfElement`

```
{ "uri": "file:///settings_view.kbview", "elementId": "1" } -> { "range": {...} }   // or `null`
```

The inverse selection-sync direction (design surface → XML pane, §1): resolves
`elementId` against the document's current parse and returns its full range,
or `null` for an id that does not resolve.

### Testing

`kubuno-views/src/edit.rs` and `src/ast/mod.rs` cover the new functions with
unit tests (composition of disjoint edits, cross-parent moves, the
subtree-cycle guard, id ⇄ stable-id round trips, out-of-range/malformed ids).
`kubuno-views-ls/src/edit_bridge.rs` unit-tests the bridge itself (id
resolution, no-op degradation, root-element guard). `kubuno-views-ls/tests
/roundtrip.rs` exercises all three methods over a real in-memory
`lsp-server` `Connection`, the same harness the crate's existing
`textDocument/*` round-trip tests use — `cargo test -p kubuno-views-ls`.

## 9. DSG-6 protocol

Work package DSG-6 (§6) is implemented, scoped exactly as the orchestrating
task narrowed it: **inside the existing `kubuno-views` crate** (a new
`kubuno_views::design` module, plus the additive `PaintCx::design`/
`Runtime::frame_with_design` wiring `crate::compile::build_node` needs to
record it) and **`examples/view_embed.rs`**, the same exe DSG-7's spike
already proved out as the embedded surface `vskubuno`'s `RustDesignSurfaceHost`
launches — not a new, separate `kubuno-views-designer` crate/binary as §3's
original sketch proposed. `view_embed`'s own `<file.kbview>` argument is now
**optional**: the DSG-6 protocol's `setText` (below) pushes the buffer's live
text directly, the same "buffer, not disk, is authoritative" rule §2 already
states, finally implemented instead of the temp-file bridge `RustDesignSurfaceHost
.SetDocumentText` used until now (`DESIGNER.md`'s own §7/§3 "Document text"
remarks called this out as a placeholder to swap once DSG-6 shipped a process
that speaks the protocol; `view_embed` already existed and is that process —
no new `kubuno-views-designer` binary was needed for it to happen).

### The layout map and hit-testing

`kubuno_views::design::LayoutMap` is a frame-local `Vec<LayoutEntry>` —
`{id, parent_id, bounds, layout}` — rebuilt every frame design mode records
into. **Every** compiled element is wrapped in a `design::DesignSlot`
(`crate::compile::build_node`, the single choke point every element — this
crate's own five node types, or any `registry::families::*` component —
already passes through), so recording needs no change to any individual
`ViewNode` impl: `DesignSlot::paint` records `{id: Element::stable_id(),
parent_id: design::parent_id_of(id), bounds, layout}` (`layout` = the
**parent container's** `LayoutKind`, e.g. a `<Panel>` child is recorded with
`LayoutKind::DockAnchor` even though the child element itself might have
`LayoutKind::None`) and then delegates to the wrapped node unchanged. This is
additive and zero-cost when design mode is off: `PaintCx::design` is
`Option<&mut LayoutMap>`, `None` for an ordinary (non-designer) frame — the
only added cost anywhere in the paint path is one `if let Some(..)` branch
per element, no allocation. `Runtime::frame` (every existing caller) is
unchanged; `Runtime::frame_with_design` is the new entry point that threads a
`LayoutMap` through.

`LayoutMap::hit_test(x, y)` returns the **deepest** element under a point:
since a container is always recorded before its own children (`DesignSlot::
paint` records itself, then recurses), scanning the entries in **reverse**
paint order finds the most-nested match first — the same trick doubles as a
z-order tiebreak for overlapping siblings (the later-painted one wins), with
no separate depth bookkeeping. A degenerate (zero-or-negative-area) entry is
skipped, so an element that measured to nothing never steals a click from
whatever is actually under the pointer ("appropriately skip non-visual
items", §6's own phrasing).

### Design mode: input, selection, adorners

`design::DesignController` owns `enabled`/`selected`/`hover` and is
deliberately **host-agnostic**: it never reads `kubuno_controls::host`'s
global input state itself (`click_select`/`update_hover` take a plain `x, y`;
`handle_keys` takes a plain `DesignKeyInput` struct) — `view_embed.rs` is the
only place that bridges the real host globals (`host::take_key`, one call per
tracked key) into these host-agnostic shapes, which is what makes every
selection/nudge/delete code path unit-testable with no live window (see
`kubuno-views/src/design.rs`'s own tests).

- **User input does not reach widgets.** While design mode is on,
  `view_embed`'s frame closure paints the compiled view through
  `Runtime::frame_with_design` with a **neutralized** copy of the real
  `Frame` (`neutralize_frame`: pointer parked off-canvas, no button/wheel/
  dismiss state) instead of the real one. Every leaf's own `interact`
  (`ButtonNode::interact` et al., unchanged by DSG-6) gates everything behind
  `hot = bounds.contains(mouse)`/`mouse_down`, so a pointer that can never be
  "hot" and a button that is never "down" can never start a press, complete a
  click, or take keyboard focus — a `TextField` only starts reading typed
  characters once it has focus, which only a real click ever grants. This
  needed no change to `kubuno_controls::host` itself (out of DSG-6's scope;
  concurrent work owns that crate's keyboard-forwarding code) — the REAL
  `Frame` still drives design mode's own click/hover/keyboard handling,
  computed separately in the same frame closure.
- **Click → select.** A completed press-edge (`f.mouse_down` just went true)
  while design mode is on calls `DesignController::click_select` against the
  frame's freshly recorded `LayoutMap`: the deepest element under the pointer
  is selected, or the selection is cleared when the click lands on nothing.
- **Adorners** (`design::paint_adorners`, using `design::resize_handles`/
  `design::dashed_outline` for the pure geometry): a dashed outline around the
  **selected element's parent** container; a light stroke on the **hovered**
  element (skipped when it is also the selection); a solid outline plus
  **8 compass-point resize handles** on the **selected** element **only**
  when its recorded `layout == LayoutKind::DockAnchor`(an Anchor/absolute
  child, §4) — anything else (a `<Stack>` flow child, or a `SingleWidget`
  body) gets the outline alone, no handles, matching §4's "resize handles on
  a `<Stack>`'s direct children are suppressed". DSG-6 only **draws** the
  handles; a mouse-driven drag/resize through them is DSG-9's own scope
  (`§6`'s table), not implemented here.
- **Keyboard** (`DesignController::handle_keys`, driven by `view_embed`'s
  `read_design_keys` — one `host::take_key` pair per tracked key, so a held
  key does not keep re-triggering every frame): **Esc** moves the selection
  to its parent (a local selection change, no edit request — `handle_keys`
  returns `Vec::new()`); **Delete** returns one `EditOp::RemoveElement`;
  an **arrow** (`Shift` = a 10 DIP step instead of 1) returns one
  `EditOp::SetAttribute` per axis that moved, **only** when the selected
  element's recorded `layout` is `LayoutKind::DockAnchor` — nudging is a
  no-op for anything else (a flow child has no `X`/`Y` to nudge, §4). A
  nudge's new value is the element's CURRENT literal `X`/`Y` (read by
  re-parsing the surface's own current text on demand — `EditOp` always
  carries the new ABSOLUTE value, never a delta, matching `setAttribute`'s
  own wire shape below) plus the step; a missing/non-numeric current value is
  treated as `0`. **Nothing here ever mutates `.kbview` text directly** — every
  state-changing gesture becomes an `EditOp` the host forwards to
  `kubuno-views-ls`'s `kubuno/applyEdit` (DSG-2, §8); `view_embed` only ever
  applies a REQUEST locally to `design`'s own selection state, never to the
  text.

### Wire protocol

Line-delimited JSON on the surface's own stdin (host → surface) and stdout
(surface → host) — `kubuno_views::protocol` implements both directions;
`vskubuno`'s `RustDesignSurfaceHost.Protocol.cs` (a `partial class` split out
of `RustDesignSurfaceHost.cs` so it would not collide with concurrent
keyboard-forwarding work on that file) speaks the C# side, byte-for-byte the
same shapes (its own `DesignSurfaceProtocol`, unit-tested independently of a
live process — see that project's test suite). `stderr` is unchanged: still
the plain `[embed] …` trace/log channel `RustDesignSurfaceHost` already
captured before DSG-6 (`ErrorDataReceived`); the protocol lives on stdin/
stdout exclusively, never mixed into the trace text.

Host → surface (`kubuno_views::protocol::HostMessage`):

| `type` | Fields | Meaning |
|---|---|---|
| `setText` | `text: string` | Re-parses and renders `text` as the current document — replaces the temp-file bridge. |
| `setDesignMode` | `on: bool` | Turns design mode on/off. |
| `select` | `id: string \| null` | Host-driven selection (XML pane → Design surface sync, §1); `null` clears it. |

Surface → host (`kubuno_views::protocol::SurfaceMessage`):

| `type` | Fields | Meaning |
|---|---|---|
| `selectionChanged` | `id: string \| null`, `bounds: {left,top,right,bottom} \| null` | The selection changed (a click, or Esc-to-parent). `bounds` is the newly selected element's painted rect, `null` when nothing is selected or the id has no current layout-map entry. |
| `editRequest` | `op: EditOp` | Delete or a nudging arrow — `op` is EXACTLY DSG-2's `kubuno/applyEdit` op shape (below), for the host to forward as-is. |

`EditOp` (`kubuno_views::design::EditOp`, tagged on `"kind"`, every field
`camelCase` on the wire — the identical convention DSG-2's own `kubuno/
applyEdit` op already uses, §8):

```json
{"kind":"setAttribute","elementId":"0.1","name":"X","value":"42"}
{"kind":"removeElement","elementId":"0.1"}
```

Example traffic for a click that selects a `<Panel>` child, then a Delete:

```text
surface → host: {"type":"selectionChanged","id":"0.1","bounds":{"left":10.0,"top":10.0,"right":90.0,"bottom":34.0}}
host   (later): {"type":"select","id":"0.1"}                        // e.g. echoed back after applying, or the XML pane's own caret move
surface → host: {"type":"editRequest","op":{"kind":"removeElement","elementId":"0.1"}}
```

A malformed or unrecognised line is **never an error** on either side —
`kubuno_views::protocol::parse_host_message` returns `None`,
`DesignSurfaceProtocol.TryParseSelectionChanged`/`TryParseEditRequest` return
`false` — the same "stale/bogus input, no-op" rule `kubuno_views::edit`
itself already uses (§8): a client racing its own debounced re-parse against
a fresh keystroke never gets a hard failure for it, and one bad line never
tears down a whole design surface process.

### C# side (`vskubuno`)

`RustDesignSurfaceHost.Protocol.cs` adds, to the existing `RustDesignSurfaceHost`
(now a `partial class`):

- `SendSetText`/`SendSetDesignMode`/`SendSelect` — write one JSON line to
  `Process.StandardInput` (`ProcessStartInfo.RedirectStandardInput` is now
  `true`, alongside the pre-existing `RedirectStandardError`; `SetDocumentText`
  — `IDesignSurfaceHost`'s own member — now calls `SendSetText` instead of
  writing the temp file the class doc used to describe) and cache the last
  value sent, so a crash-restart (`OnSurfaceExited`'s existing backoff) can
  **resend** design mode/selection/text to the fresh process instead of
  silently losing that state.
- `OnSurfaceProtocolLine` — the `Process.OutputDataReceived` handler
  (`RedirectStandardOutput` is now `true` too; wired in `BeginProtocolIo`,
  called right after `BeginErrorReadLine`): parses each line with
  `DesignSurfaceProtocol`'s pure `TryParseSelectionChanged`/
  `TryParseEditRequest` and raises `SelectionChanged`/the new `EditRequested`
  event, marshalled onto the UI thread via `Dispatcher.BeginInvoke` — the same
  pattern `OnSurfaceExited` already uses, and for the same reason (a
  subscriber may touch WPF/VS objects that require it). A line matching
  neither shape is logged and dropped, never thrown.
- `DesignSurfaceEditOp`/`DesignSurfaceEditOpKind`/
  `DesignSurfaceEditRequestedEventArgs` — the C# mirror of `kubuno_views::
  design::EditOp`, carried by the new `EditRequested` event
  (`EventHandler<DesignSurfaceEditRequestedEventArgs>`).

`Toolbox/`, `Properties/`, `Editing/` are untouched by this package — forwarding
an `EditRequested` op to `kubuno-views-ls`'s `kubuno/applyEdit` and applying
the result through `Editing/Infrastructure/BufferEditApplier.cs` (DSG-5) is
wiring left for whichever package actually connects the two ends end-to-end
(DSG-8/DSG-9's own scope per §6's table), not DSG-6's.

### Testing

`kubuno-views/src/design.rs` unit-tests the layout map (`hit_test` depth/
z-order/degenerate-entry skipping, `parent_id_of`'s three documented cases),
the adorner geometry (`resize_handles`'s eight compass points, `dashed_outline`'s
four-edge coverage) with no `Canvas` at all, and `DesignController`'s full
request-generation surface (click/hover, Esc-to-parent, Delete, arrow nudge
incl. the `LayoutKind::DockAnchor`-only gate and the Shift step) against
hand-built `LayoutMap`s and parsed `ast::Document`s — no live window, no
`kubuno_controls::host` global state. `kubuno-views/src/compile.rs` gained a
regression test compiling a `<Panel>` with Anchor/Dock children end to end
(the one `build_node` call site that does not go through a `Props` helper).
`kubuno-views/src/protocol.rs` unit-tests every wire shape byte-for-byte
(`serde` round trips plus exact-string assertions, so a silent field-name
drift — confirmed live during this package's own development: `rename_all`
on an enum does not, on its own, camelCase a struct variant's fields, only
its tag — fails a test immediately rather than shipping). `Kubuno.VisualStudio
.Designer.Tests/DesignSurface/RustDesignSurfaceHostProtocolTests.cs` asserts
the identical strings on the C# side (`DesignSurfaceProtocol`, no live
process/WPF `Dispatcher` needed), so the two sides cannot silently drift from
each other. `DesignSlot`'s own wrapping — the mechanism, not the geometry —
is exercised the way `crate::node`'s own tests already establish nothing in
this crate can avoid: a live paint pass needs a real `kubuno_controls::
ControlCanvas` (Direct2D/DirectWrite), which no unit test in this crate
constructs (§6's own "without a Canvas where possible" carve-out) — covered
instead by the visual check below.

Environment note: `dotnet test`/`vstest.console.exe` on this machine fails to
load `MSTest.TestAdapter.dll` for `Kubuno.VisualStudio.Designer.Tests`
(`NETStandardCompatError_MSTest_TestAdapter`) for EVERY test in that project,
not just the new ones — a pre-existing tooling/SDK-resolution issue (only the
.NET 10 SDK is installed, no `global.json`), reproduced against an existing,
previously-passing test (`ComponentRegistryTests`) too. The new tests are
verified by `MSBuild` compiling the test project cleanly; running them was
not possible in this environment.
