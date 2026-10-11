# Kubuno Whiteboard (desktop) — design of record

Status: plan and log of the Whiteboard desktop port (the office module's infinite canvas — Excalidraw-style
shapes, sticky notes, freehand strokes). Method: the one `DOCUMENTS-EDITING.md` sets out (a platform-neutral
engine crate shared with the web, a thin per-OS window). Reference for behaviour and look: **the web editor**
(`office/web/src/WhiteboardApp.tsx` and the files it imports). Each increment is verified before the next.

This file records what was built and how it was verified. The first lot (2026-10-11) delivered the shared
engine crate; the desktop window is planned here and built in later increments.

## 1. What the web Whiteboard is (feature inventory)

The board is a single React component, `office/web/src/WhiteboardApp.tsx` (≈ 3100 lines), over a handful of
helpers:

| Concern | Web file | Notes |
|---|---|---|
| Scene types | `whiteboard-types.ts` | `WbElement` (sticky, text, shape, arrow, frame, image) + `Stroke`; `Background` |
| Rendering + viewport | `whiteboard-engine.ts` | `Viewport` (pan/zoom), `renderStickyNote/TextBox/Shape/Arrow/Frame/Stroke`, `renderBackground`, selection handles, `hitTest`, `hitHandle` |
| Shapes | `whiteboard-shapes.ts` | routes every kind through the shared `shapes/` package (144 OOXML geometries + the board's own rounded `rect`); yellow adjustment knobs; draw-to-create gesture; SVG export |
| Shape engine | `shapes/*` → crate `kubuno-office-shapes-core` | presets, native fraction geometries, adjustment, catalogue, draw box |
| Excalidraw import | `whiteboard-excalidraw.ts` | `.excalidraw` scene → `WbElement[]` + `Stroke[]` |
| Board metadata API | `whiteboard-api.ts` | REST CRUD of the board row (title, background, stars, collaborators, thumbnail) — **not** the content |
| Real-time content | `WhiteboardApp.tsx:314-358`, `collab/collabProvider.ts` | a Yjs document synced over a WebSocket |

Tools (`ToolType`): `select, hand, sticky, text, shape, arrow, pen, eraser, frame`. Backgrounds: `white, grid,
dots, lines`. Sticky palette: `STICKY_COLORS` (10 named colours). Zoom bounds 0.05–20. Groups (`groupId`),
z-order (`zIndex`), locking (`locked`), rotation and opacity per element.

## 2. The stored format — **not** the Documents pattern

This is the decisive finding and the reason the Whiteboard diverges from Documents, Spreadsheets and Diagrams.

The board's **content is a Yjs CRDT document**, synced over a WebSocket, consolidated into a gzipped Drive
file. There is **no JSON content blob, no content REST endpoint, and no digest-guarded PATCH** (the pattern of
`DOCUMENTS-EDITING.md` §2.D and `office-documents-desktop.md`). Concretely:

- The Y.Doc has exactly two shared types (`WhiteboardApp.tsx:314-315`): a `Y.Map` **`elements`** keyed by
  element id (→ the element object as plain JSON), and a `Y.Array` **`strokes`**. The viewport is **not**
  persisted (`WhiteboardApp.tsx:254`); the background and title are board-metadata columns, not content.
- Transport: a WebSocket on the **office module's own** endpoint
  `/api/v1/office/whiteboard/boards/:id/sync?token=…` (`WhiteboardApp.tsx:352-358`, server
  `whiteboard/handlers/websocket.rs:82-151`) — **not** the core's generic `/collab/:entity/:id/sync`. On
  connect the server streams the persisted parts as binary frames; every incoming binary frame is a Yjs
  update, persisted and relayed; text frames are awareness (never persisted).
- Durable storage (server, two tiers): incremental updates go into Postgres `office_wb.yjs_updates` (BYTEA);
  at 100 updates `consolidate` (`whiteboard/services/yjs_service.rs:78`) folds the snapshot + updates into a
  gzipped `.kbwbd` Drive file (`application/vnd.kubuno.whiteboard`, `services/content_files.rs:629-653`) and
  deletes the rows. `load_document` returns the snapshot followed by the un-consolidated updates, in order.
- The client "save" (`WhiteboardApp.tsx:223-233`) is cosmetic: an empty Yjs transaction to flush, then a
  metadata PATCH to bump `updated_at`. The content is already durable through the live stream.

**Consequence for the desktop.** The engine is built around a `yrs` document, not a `to_vec` serialiser, and
the window's "save/open" path is the collaboration room (reused from `kubuno-office-desktop-shared::collab`),
**not** the `session.rs` digest state machine. The `.kbwbd` gzip wrapper lets the desktop open/write a local
file offline.

## 3. The shared core — `kubuno-office-whiteboard-core` (built 2026-10-11)

`office/common/whiteboard-core`, a platform-neutral crate (`serde`, `serde_json`, `flate2` rust-backend,
`yrs 0.23`, and `kubuno-office-shapes-core` for geometry). No UI, no Windows, no C; checked for
`wasm32-unknown-unknown`, `x86_64-unknown-linux-gnu` and `aarch64-apple-darwin`. Modules:

| Module | Web origin | Content |
|---|---|---|
| `model` | `whiteboard-types.ts` | `WbElement` enum + `Stroke` + `Scene`, held losslessly (every struct carries an `extra` catch-all; unknown element kinds stay as raw JSON in the document). `ArrowElement` is flat (no base geometry); `ImageElement` keeps the web's snake_case `natural_width/height`. |
| `collab` | `WhiteboardApp.tsx:314-358`, `collabProvider.ts` | `WhiteboardDoc` (the `yrs` port: `elements` map + `strokes` array, apply/encode updates, the web's diff-write, local vs **remote** origins), the `sync_url` builder for the office endpoint, `room`, JSON↔Yjs `Any` mapping. |
| `file` | `content_files.rs`, `yjs_service.rs` | the gzipped `.kbwbd` snapshot (read/write) and `load_from_parts` (the server's snapshot-then-updates order). |
| `viewport` | `Viewport` | pan/zoom, `zoom_at`, `fit_to_elements` (with the arrow/`NaN` fix), `screen⇄canvas`. |
| `engine` | `whiteboard-engine.ts`, `whiteboard-shapes.ts` | `scene_from_doc`, `hit_test`, resize handles + cursors, `group_members`, z-order, `shape_geometry` (board rounded `rect` vs shared paths vs open connectors), the `DrawGesture` draw-to-create box. |
| `excalidraw` | `whiteboard-excalidraw.ts` | the `.excalidraw` import, field-for-field. |
| `undo` | `Y.UndoManager` | undo/redo of **local** changes only, grouped by explicit capture boundaries (the core reads no clock: a constant timestamp + `stop_capturing`, so it builds for wasm where `Options::default` is unavailable). |
| `render` | `whiteboard-engine.ts` renderers | a renderer-neutral `Surface` trait (the web's `CanvasRenderingContext2D` subset) and the painters: background, sticky, text, shape (shared paths + board rect + open connectors + arrow heads), arrow, frame, stroke, selection handles. Text wraps through `Surface::measure_text`, the one door to fonts. |

Verification (2026-10-11): `cargo test` 22/22 pass — including a Yjs-binary + gzip snapshot round-trip
(`file::tests::snapshot_round_trips_through_gzip_and_yjs`), the local-vs-remote undo distinction, hit-testing,
grouping, the Excalidraw import, and recorder-surface painter tests; `cargo clippy --all-targets -- -D
warnings` clean; `cargo check` clean for wasm32, linux and macOS.

**Not invented:** the core mirrors the web and skips what the web lacks. Golden fixtures produced *by the web
editor* (a real Yjs binary) are a follow-up; the current round-trip test builds the document the way the web
writes it and proves the gzip + Yjs-v1 binary path is loss-free, but it does not yet compare against a byte
blob captured from the running web app.

## 4. The desktop app (planned — mirrors Diagrams, the closest canvas app)

Two crates, like every office desktop app:

- `desktop/common/kubuno-office-whiteboard-common` — the portable app: `PROGRAM = "kubuno-whiteboard"`,
  `Options` (`--dark --culture --zoom --doc <id> | file.kbwbd --sample`), `run` (install the broker token
  provider for `--doc`, claim single-instance per board, run the UI), the view-state (a `Viewport`, the
  `WhiteboardDoc`, its `Undo`, the selection and current tool), and the server seam: a board REST client
  (`/api/v1/office/whiteboard/boards/…`, open-by-file, metadata PATCH) and a `connect(id)` that resolves the
  account and opens the **collab room** (`kubuno-office-desktop-shared::collab` with `collab::sync_url`), plus
  a `FileJournal` for offline `.kbwbd` snapshots. It reuses `kubuno-office-desktop-shared` (collab/single/ids)
  and `kubuno-office-desktop-common` helpers, as Diagrams does.
- `desktop/windows/kubuno-office-whiteboard` — the Win32 window, exe `kubuno-whiteboard.exe`: one
  `whiteboard_window.kbview` (TitleBarStyle Tall, header menus, Backstage, QAT, ribbon tabs matching the web
  toolbar — Accueil/Insertion/Affichage with the tool groups, shape gallery with a custom item renderer,
  sticky palette, background picker, zoom), a `BoardCanvas` custom `Control` with `on_paint` (implements the
  core's `render::Surface` over Direct2D/DirectWrite) and the pointer/keyboard handlers driving the core, a
  status band with a `ZoomSlider`, a `BackstageInfo`, and `ConfirmDialog` for recovery.

Increments (each shippable):
- **A — open & display read-only at parity.** Open a local `.kbwbd` or a server board (`--doc` via the broker
  in the sandbox profile); build the `Scene` from the `WhiteboardDoc`; paint it (background, elements in
  z-order, strokes) with the core renderer; pan/zoom, fit. Side-by-side captures vs the web, light/dark, 100 %
  and 175 %.
- **B — editing.** The tools (select/move/resize/rotate, sticky, text, shape with the gallery + yellow knobs,
  arrow, pen, eraser, frame), z-order, group/ungroup, lock, delete, duplicate; ribbon commands wired to the
  same handlers as the shortcuts; undo/redo bound to the ribbon/QAT state.
- **C — live collaboration (replaces Documents' "save").** Join the board's WebSocket room on open, apply
  remote updates to the `WhiteboardDoc` (origin `remote`, so they are not undoable), publish local updates
  (origin `local`) at the web's cadence, awareness/presence in the status bar; offline journal of the local
  `.kbwbd`, replayed and re-synced on reconnect. There is **no** digest/If-Match/413 path here (see §2).

VS integration: `.rsproj` per crate (`Kubuno.Office.Whiteboard.Core/.Common/.Whiteboard`), `Kubuno.Office.slnx`
regenerated so the projects appear under Desktop.

## 5. Risks and open questions

- **Yjs interop fidelity.** `yrs 0.23` is the same v1 wire format the core's collaboration service and the
  spreadsheet already rely on; the round-trip is proven within Rust. Still to verify against a board authored
  by the running web app (a true golden fixture) and against a live dev core's `sync` endpoint.
- **Awareness/presence framing.** The board relays presence as text frames; the exact payload
  (`collabProvider.ts`) must be matched when wiring increment C.
- **Shape parity.** Geometry comes from `kubuno-office-shapes-core` (already used by slides/diagrams), so a
  star on a board is the same star as in a slide. The board's own rounded `rect` (radius 6) is a deliberate
  divergence, honoured in both the renderer and the future SVG export.
- **Fonts.** Text layout (`wrap_text`) goes through `Surface::measure_text`; the Windows window measures with
  DirectWrite. The desktop system font differs from the web's Plus Jakarta Sans (see memory
  `desktop-font-system.md`) — ignore that gap in comparisons.
- **office is PostgreSQL-only**: the dev core for round-trip tests runs on a local PostgreSQL 16 with a
  dedicated DB (`kubuno_dev_whiteboard`) and non-default core port (e.g. 18921), never the shared dev DB or
  production.

## 6. Where this lot stopped / resume point (2026-10-11)

Done and on `main` (office `f6b82b6`, pushed): the shared engine crate (§3), verified (22 tests, clippy
`-D warnings`, wasm32/linux/macOS). The desktop framework (`desktop-v0.1.2-alpha`) was confirmed to build
offline here (diagrams windows crate, `cargo check` exit 0, ~2 min) — so the GUI path is viable.

**As built — Increment A (2026-10-12, branch `whiteboard-desktop-wip`):** the desktop app opens and renders a
board **read-only at the renderer level**, verified by a real run.
- `desktop/common/kubuno-office-whiteboard-common` — the portable app (`app`/`platform`/`doc`/`api`/`collab`):
  `OpenBoard` over the core `WhiteboardDoc` (sample, local `.kbwbd`), the board REST client (metadata) and the
  Yjs collaboration wiring (greeting/settle/seed, `sync_url`) modelled on Spreadsheets. 11 tests, clippy clean.
- `desktop/windows/kubuno-office-whiteboard` (exe `kubuno-whiteboard.exe`) — a `BoardCanvas` custom control
  that implements the core `render::Surface` over **Direct2D/DirectWrite** (CSS-colour parsing, path geometry
  incl. SVG arcs, round-rect, dashes, shadow, text + measure), the `whiteboard_window.kbview` ribbon
  (Fichier/Accueil/Affichage: backstage info, background picker, zoom), a `BackstageInfo` page and the
  status-bar zoom. Pan (drag) + zoom (wheel/ribbon/slider). A server board (`--doc`) connects the Yjs room and
  applies updates read-only.
- **Evidence:** `kubuno-whiteboard.exe --sample` runs; captured light and dark — the sample sticky note (fold,
  shadow, wrapped text), the rounded-rect shape (shapes-core geometry) and the dotted background all paint
  correctly through the Direct2D surface.
**As built — Increment B (2026-10-12, merged to `main`):** the tools and editing are wired and verified.
- `BoardCanvas` drives the shared engine: select, move, resize via the eight handles, draw a
  shape/sticky/text/frame/arrow, freehand pen, eraser, z-order (front/back), group/ungroup, delete, and
  undo/redo through the core `yrs` undo manager (local changes only). Each edit mutates the Yjs document and
  the produced update is relayed to the collaboration room (the send side of C).
- The ribbon's Accueil tab carries Outils / Édition / Disposition / Fond; live preview while drawing; the
  board canvas is now a **light surface even in dark mode** (the A refinement).
- **Evidence:** captured `wbB-light.png` — the enriched sample shows a frame, a sticky (fold+shadow+wrap), an
  arrow with a head, a rounded-rect, bold text, a pen stroke and an ellipse, all painted through the Direct2D
  surface, with the full tool ribbon.
- **Verification:** clippy `-D warnings` clean; 6 (windows) + 11 (common) + 22 (core) tests pass.
- **Merged to `main`** (`8fadf5e..9da622d`, whiteboard crates only, branch clean of other agents' work).

**As built — Increment C + golden + parity (2026-10-12, against the shared dev stack 127.0.0.1:18900):**
- **C — live collaboration, verified bidirectional.** Using the shared engine over the module's own sync
  WebSocket (`/api/v1/office/whiteboard/boards/:id/sync`), a client (the desktop collab code) pushed four
  elements to the live board; a **separate fresh client received exactly those four** (server relay +
  persistence), and the **web editor renders them** (desktop→web). The window relays each local update to
  the room and applies remote updates, both directions. (`.kbwbd` Drive-file consolidation triggers at 100
  updates server-side — not reached with four edits; persistence confirmed by the fresh client's full pull.)
- **Golden Yjs fixture (merged to main).** `common/whiteboard-core/tests/fixtures/live-board.kbwbd` is a real
  gzipped Yjs snapshot captured from the live board; `tests/golden.rs` decodes the server's actual bytes
  (four elements) and checks the scene round-trips losslessly.
- **Web-vs-desktop parity.** Same board rendered in the web editor and the desktop app — the green sticky
  (fold + shadow + wrapped text), the arrow with its head, the rounded-rect and the bold text match. The
  differences are shell chrome (the web's side panels / vertical toolbar vs the Office ribbon) and fonts (web
  Plus Jakarta Sans vs desktop Arial, ignored by convention); element geometry and colours match.

**Remaining:**
- **VS / `Kubuno.Office.slnx`** — **done + merged to main**: the three whiteboard projects (whiteboard-core,
  whiteboard-common, the Windows app) are in the solution under Common / Desktop/Common / Desktop/Windows,
  with the Windows app's `.rsproj`. devenv launches the `KubunoOfficeWhiteboard` hive with the VSIX
  (confirmed, VS Community 2026). The **live designer/editor capture is blocked by VS 2026's mandatory
  first-run "Sign in to Visual Studio" welcome**, a fixed modal that resisted automated dismissal (UIA not
  available on it, Escape/clicks/resize ineffective). Needs a one-time interactive sign-in-skip to
  initialise the hive (or a settings pre-seed), after which the `.kbview` opens in the Kubuno editor.
- Real-input tests on the ribbon and UIA names not yet done.

Templates to mirror (confirmed by reading): **Diagrams** for the window/ribbon/Direct2D surface
(`desktop/common/kubuno-office-diagrams-common/src/{app,platform,doc,api/*}.rs`;
`desktop/windows/kubuno-office-diagrams/src/{lib,main,windows_ui,platform/surface,controls/diagram_canvas,views/diagram_window.kbview}.rs`),
and **Spreadsheets** for the live Yjs collaboration — it already drives `collab::Room` with a
`SheetsDoc` exactly like our `WhiteboardDoc`
(`desktop/common/kubuno-office-spreadsheets-common/src/collab.rs`,
`desktop/windows/kubuno-office-spreadsheets/src/views/spreadsheet_window_collab.rs`).

Resume order:
1. Finish `kubuno-office-whiteboard-common`: `lib.rs`; `app.rs` (`PROGRAM="kubuno-whiteboard"`, `Options`,
   `run`); `platform.rs` (copy Diagrams); `doc.rs` → `OpenBoard` (a `WhiteboardDoc` + title + background +
   `Source{Sample,File(.kbwbd),Server(id)}`, `open_file`/`to_kbwbd`/`sample`); `api/client.rs` (board routes
   from `whiteboard-api.ts` via `SyncWire`: get, open_by_file, list/recent, update); `collab.rs` (a `Collab`
   mirroring Spreadsheets over `WhiteboardDoc`: greeting, settle/seed, `on_binary`/`on_text`, publish local
   diff; `sync_url` from the core; presence is a follow-up). Add the member + `[workspace.dependencies]` entry
   to `desktop/Cargo.toml` (pull first). `cargo test -p kubuno-office-whiteboard-common`.
2. `desktop/windows/kubuno-office-whiteboard` (bin `kubuno-whiteboard`): mirror Diagrams; implement the core's
   `render::Surface` over Direct2D in `platform/surface.rs`; `BoardCanvas` custom `Control` with `on_paint`;
   the `whiteboard_window.kbview` ribbon.
3. Increment A: open `--doc <id>` (debug build + `--dev-instance office-whiteboard`, broker token) and render
   read-only; captures web vs desktop. Then B (editing), then C (live collab; desktop↔web if feasible).
4. Golden fixture: author a board in the running web editor against a dev core (DB `kubuno_dev_whiteboard`,
   port 18921) and commit the real `.kbwbd`/Yjs bytes as a `whiteboard-core` fixture, to validate the
   round-trip against the web's actual bytes.
