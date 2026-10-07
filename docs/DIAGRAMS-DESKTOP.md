# Kubuno Diagrams (desktop) — design note

Status: design of record for the native desktop version of the Office module's **Diagrams** sub-application
(`kubuno-diagrams.exe`), started 2026-10-07. User goal: the desktop app catches up with the **whole** web Diagrams
editor, not a subset. Method: the one of Kubuno Documents (`DOCUMENTS-EDITING.md`): a platform-neutral core crate that
ports the web's algorithms, a portable desktop app in `office/desktop/common`, and only the Win32 window and its
drawing in `office/desktop/windows`. §8 is the as-built log and §9 the feature coverage table, kept up to date as the
increments land.

Reference for behaviour and looks: **the web editor**, `office/web/src/DiagramEditorPage.tsx` (4 042 lines) and what it
imports. Every file:line below is in `office/web/src` unless stated.

## 1. What the web app does

### 1.1 Files

| File | Role |
|---|---|
| `DiagramsApp.tsx` (276) | landing page (ModuleHome + `DiagramsStartContent`) |
| `DiagramsStartContent.tsx` (128) | start page: recent diagrams (open, duplicate, trash), Drive browser of `Office/Diagrams`, « Nouveau » |
| `DiagramEditorPage.tsx` (4 042) | the editor: model types (`:49-123`), geometry and routing (`:163-426`), auto-layout (`:428-495`), canvas renderer (`:497-834`), stencil thumbnail (`:836-864`), minimap (`:866-938`), rulers (`:940-989`), the component (`:993-4042`) |
| `stencils.ts` (1 043) | `ShapeStyle` + defaults (`:9-40`), `renderShape` (`:44-268`) and its ~60 primitives (`:270-743`), `drawLabel` (`:747-777`), `drawArrow` (`:780-823`), the stencil catalogue (`:827-948`), the office catalogue shapes added as stencils (`:961-978`), categories and search (`:984-1042`) |
| `hardwareIcons.ts` | 30 « Ordinateur et Matériel » stencils, multicolour SVGs (SVG Repo, MIT) rasterised by the browser |
| `diagram-shape-kinds.ts`, `diagram-draw.ts`, `diagram-adjust.ts` | which stencils the shared shape engine paints (`sharedKindOf`), the draw-to-create gesture, the yellow adjustment knobs |
| `shapes/*` | the office-wide shape engine (shared with sheets, slides, whiteboard): 144 OOXML presets interpreted from LibreOffice's data (`preset-engine.ts`, `preset-data.ts`), 10 native fraction geometries (`native-geometry.ts`), adjustments (`adjust.ts`), the catalogue (`catalog.ts`), canvas painting (`canvas.ts`, `paths.ts`), draw gesture (`draw.ts`), the module registry (`registry.ts`, unused by diagrams) |
| `diagramIo.ts` (238) + `inflate.ts` | draw.io / mxGraph XML import (compressed or not) and export, CSV import |
| `diagramTemplates.ts` (105) | the template gallery (Vierge, Organigramme, …) |
| `diagramPdf.ts` (36) | a one-page PDF around a JPEG of the diagram |
| `api.ts:2641-2705` | `diagramsApi` |

### 1.2 Features (inventory)

Model and storage
- F1 Page data `{ shapes, connectors, layers? }` (`DiagramEditorPage.tsx:49-123`); shape: id, type (stencil id), x, y,
  w, h, label, style (partial `ShapeStyle`: fillColor, strokeColor, strokeWidth, strokeStyle, opacity, shadow,
  rounded), labelStyle (fontFamily, fontSize, bold, italic, color, align, verticalAlign), zIndex, flipH?, flipV?,
  rotation?, groupId? (null after ungroup), layerId?, adj?; connector: id, sourceId/targetId, sourcePoint/targetPoint,
  waypoints, label, labelOffset?, style (strokeColor, strokeWidth, strokeStyle, arrowStart, arrowEnd, orthogonal,
  routing?), layerId?.
- F2 Several pages per diagram: tabs, add, rename (double click), delete with confirmation (`:2900-2929`,
  `:3856-3925`); page metadata (name, position, bg_color) lives in the database, page content in the `.kbdia` file.
- F3 Autosave: a 1.5 s debounce after each change, flushed on hide/unload (`:1049-1089`); « Enregistrer » flushes.
- F4 Undo/redo: snapshots of the page, 100 deep, one entry per gesture (`:1091-1141`); Ctrl+Z, Ctrl+Y, Ctrl+Shift+Z.

Drawing (`:497-834`)
- F5 Grid: minor 1 cell (10 px) and major every 5 cells, drawn in screen space, hidden under 4 px a cell.
- F6 Shapes: ~150 stencils: house geometries and business templates (flowchart, ER, BPMN, UML, network, AWS,
  Kubernetes, UI mock-ups, containers and swimlanes), 30 hardware icons, and the 144 office catalogue shapes;
  opacity, shadow (rgba(0,0,0,.25), blur 5, offset 2), dash styles, rotation and flips about the centre; labels
  word-wrapped at `w - 16` with alignment and line height 1.3 × size.
- F7 Connectors: straight / orthogonal (elbows) / curved (Catmull-Rom, 16 samples a segment) routing; endpoints glued
  to the side of a shape that faces the next point, recomputed every frame; rounded corners (radius 8); line hops
  (radius 5) where a connector crosses an older one; arrow heads block, classic, open, oval, diamond at both ends;
  label on a white box at the middle of the central segment, plus a draggable offset.
- F8 Selection furniture: dashed box 14 px outside the shape, 8 resize handles, the 4 ports (hover and selection),
  yellow adjustment knobs (one shape selected, shared geometries only), waypoint handles and « + » mid-segment handles
  of a selected connector, the rubber-band connector, the lasso, green « magnet » segments, magenta alignment guides,
  the live ghost of a shape being drawn.

Editing (`:1633-2244`)
- F9 Click selects (group-aware: a grouped shape selects its group; Shift adds); drag moves the selection, a
  container also carries the shapes whose centre lies inside it; magnetism to connector neighbours (8 px) and smart
  alignment guides against every other shape's edges and centres; grid snapping when « Magnétisme » is on.
- F10 Resize by 8 handles (minimum 20 px); adjustment knobs (preset handles solved numerically).
- F11 Draw from a port to another shape (port or body) to create a connector; released elsewhere: nothing.
- F12 Connector editing: drag a waypoint (90° magnet within 15°), drag a « + » to add one, double-click a waypoint to
  remove it, drag a segment (creates two elbows on a straight one), drag the label; double-click edits the label.
- F13 Lasso on empty space (only fully enclosed shapes, > 5 px).
- F14 Armed shape tool (gallery, stencil panel, « Texte »): drag draws the real shape live (Shift square, Alt from the
  centre), a click drops the default size centred; Escape disarms; drag and drop from the panel.
- F15 Inline label editor (shapes and connectors), Enter commits, Escape cancels.
- F16 Context menus: shape (`:3733-3787`), connector (`:3704-3732`), canvas (`:3788-3808`).
- F17 Keys: Delete/Backspace, Escape, Ctrl+A, Ctrl+C/X/V/D, Ctrl+Z/Y (`:2439-2470`, `:2814-2831`).
- F18 Wheel zoom about the pointer (×1.1 a notch, 10 %–400 %), middle button or Alt+drag pans.

Commands (ribbon `:3016-3147`, shape tab `:3555-3583`)
- F19 Home: clipboard (Coller, Couper, Copier, Historique, Dupliquer), Diagramme (Nouveau, Dupliquer, Importer,
  Export PNG/JPEG/PDF/.drawio/JSON), Édition (Supprimer, Propriétés).
- F20 Insertion: Formes (shared shape gallery, arms the tool), Texte, Modèles (template gallery dialog).
- F21 Disposition: align ×6 (≥2), distribute ×2 (≥3), order ×4, group/ungroup, flip H/V, auto-layout (hierarchical
  ↓/→, circle, grid; the selection if ≥2 shapes, else all).
- F22 Affichage: zoom in/out (×1.2), fit, reset (100 %, pan 60/60); grid, snap, minimap, layers panel, rulers,
  properties panel; colour themes (Bleu, Vert, Gris, Mono, Sombre) on the selection or the whole page.
- F23 Contextual « Connecteur » tab: routing ×3, line ×3, end arrow toggle, reverse, duplicate, delete.
- F24 Contextual « Forme » tab (shared): fill / outline colour pickers, Aligner menu (6 + distribute 2), front/back,
  rotate ±90°, delete.
- F25 Title bar: title (rename = file rename), Save, Undo/Redo, star (favourite), delete (trash, confirmed), back.
- F26 Backstage (« Fichier »): start page (recent, browse, new), Informations (title, type, modified, shapes,
  connectors, pages), print, close.

Panels (dock: left Formes, right Format and Calques, `:3194-3553`)
- F27 Formes: search, collapsible categories (first one open), 5-column grid of 38×30 thumbnails, large hover preview
  (180×120), click arms, drag drops; footer hint while armed.
- F28 Format: shape tabs Style (12 presets, fill + hex + none, stroke colour + width, dash, rounded 0–40, shadow,
  opacity 0–100, « Modifier le style… » JSON dialog), Texte (label, font + size, colour, B, I, horizontal and vertical
  alignment), Disposition (x, y, w, h, rotation, flips, order, align, group/ungroup), delete; connector: colour,
  width, dash, routing, end arrow kind, label, delete; nothing selected: hint.
- F29 Calques: list (top layer first), active layer, visibility, lock, rename (double click), up/down, delete (not the
  last), new layer, « Déplacer la sélection ici »; hidden layers are not drawn, hidden or locked ones are not pickable.
- F30 Minimap (180×120, bottom right): shapes as rectangles, viewport box, click/drag to jump.
- F31 Rulers (18 px, top and left): « nice » world step ~70 px apart, 5 minor ticks.
- F32 Status bar: shapes count, connectors count, selection label, zoom control.
- F33 Macros menu (Script sub-module) in the page bar.
- F34 Mobile read mode, touch gestures (pinch, long press): web-mobile only, **not applicable** to the desktop.

### 1.3 Stored format and API

- A diagram is a row of `office.diagrams` (title, `file_id`, `draft_file_id`, settings, star, trash) plus rows of
  `office.diagram_pages` (name, position, bg_color, width, height, hidden). The content is ONE Drive file
  `Office/Diagrams/<title>.kbdia`: `gzip(JSON { "version": 1, "pages": { "<page uuid>": <page data> } })`
  (`server/src/services/content_files.rs:373-420`), MIME `application/vnd.kubuno.diagram+json`.
- Routes (`server/src/router/*.rs:225-259`): `GET/POST /office/diagrams`, `GET/PATCH /diagrams/:id`,
  `POST :id/trash|restore|duplicate`, `DELETE :id/delete`, `GET :id/export/json` (all pages with their data),
  `GET/POST :id/pages`, `PATCH :id/pages/reorder`, `GET/PATCH/DELETE :id/pages/:pid`, **`PUT :id/pages/:pid/data`
  `{data}`** (the save), `POST :id/editing/join` (returns the whole content), `POST :id/editing/ping`,
  `DELETE :id/editing/leave`, `POST /diagrams/open-by-file`, `GET /diagrams/delta`, `/shapes/custom`.
- Server facts that shape the client (`server/src/handlers/diagrams.rs`): every route is **owner-only**; the save
  writes the draft (created on demand) and mirrors it into the main file; `leave` promotes the draft and deletes it
  when the last editor leaves; there is **no etag at all** on diagrams, so the only protection against overwriting
  another client's work is a **content digest re-read right before each save** (`GET :id/export/json`); the request
  body limit is axum's default 2 MiB (413 is terminal for that page); the web never joins an editing session (it
  saves with `PUT …/data` only), the desktop does, like Documents.

## 2. Architecture

```
office/
  common/shapes/            kubuno-office-shapes-core     the office shape engine (port of web/src/shapes)
  common/diagrams-core/     kubuno-office-diagrams-core   the diagram engine (model, drawing, editing, io)
  desktop/common/kubuno-office-diagrams-common            the complete portable app
  desktop/windows/kubuno-office-diagrams                  the Win32 window, kubuno-diagrams.exe
```

### 2.1 `kubuno-office-shapes-core` (new, `office/common/shapes`)

The web's `shapes/` folder is shared by every office editor, so its port is a crate of its own that every office
client can use (sheets, slides and whiteboard desktop apps later), not a module of the diagrams core.

- `canvas` — **a Canvas 2D context in Rust** (`Ctx2D`): the state stack (transform, fill/stroke style, line width,
  dash, cap/join, global alpha, shadow, font, text align/baseline), the current path (`moveTo`, `lineTo`,
  `quadraticCurveTo`, `bezierCurveTo`, `arc`, `arcTo`, `ellipse`, `rect`, `roundRect`, `closePath`, SVG path data
  for `Path2D`), `fill`/`stroke`/`fillRect`/`strokeRect`/`fillText`/`measureText`, with the HTML spec's semantics
  (arcs flattened to cubic Béziers in core, points transformed when added). A backend implements the small
  `Surface` trait (fill a path, stroke a path, draw text, measure text) — Direct2D on Windows, a recorder in the
  tests. Porting the web's canvas code then stays line for line, which is what keeps it faithful.
- `color` — CSS colour parsing (`#rgb`, `#rrggbb`, `rgb()`, `rgba()`, names used by the code, `none`).
- `svgpath` — SVG path data (`M L H V C Q A Z`, relative forms) into canvas calls.
- `preset` — the 144 LibreOffice presets: the data extracted verbatim from `preset-data.ts` into `presets.json`
  (embedded), the equation evaluator, path building with the same 2-decimal formatting, text frame, handles and the
  numeric drag solver (`preset-engine.ts`).
- `native` (`native-geometry.ts`), `adjust` (`adjust.ts`), `catalog` (`catalog.ts`: kinds, French labels, default
  sizes), `paths` (`paths.ts`: aliases, `shapePaths`, `shadeColour`, `paintShape`), `view` (`canvas.ts`:
  `paintShapeView`), `draw` (`draw.ts`: `drawBoxFrom`, `finalizeDrawBox`, `paintShapeGhost`).
- No `windows` crate, no I/O: `cargo check` for `wasm32-unknown-unknown`, `x86_64-unknown-linux-gnu`,
  `aarch64-apple-darwin`.

### 2.2 `kubuno-office-diagrams-core` (new, `office/common/diagrams-core`)

| Module | Port of |
|---|---|
| `model` | the page data, **lossless**: unknown keys of a page, a shape, a connector or a style are kept; style objects stay partial (a missing key is not the same as its default); numbers are written as JavaScript writes them (`120`, never `120.0`); `groupId: null` is kept apart from an absent one |
| `file` | the `.kbdia` envelope (gzip, also plain JSON), page order |
| `stencils` | `stencils.ts`: the catalogue, `mergeStyle`, `renderShape` with every primitive, `drawLabel`, `drawArrow`, categories and search; the shapes of the office catalogue added as stencils |
| `hardware` | `hardwareIcons.ts`: the 30 SVGs embedded as data and drawn through the same `Ctx2D` (path, circle, rect, polygon, fills) |
| `geometry` | ports, anchors, endpoint resolution, routing (`orthogonalize`, Catmull-Rom), label centre, segment intersection, hit tests, `magnetizeRightAngle` |
| `layout` | `computeLayout` (hierarchical ↓/→, tree, circle, grid) |
| `render` | `renderCanvas`, `renderConnector` with corner rounding and hops, the minimap, the rulers, stencil thumbnails, export rendering |
| `editor` | `Editor`: pages, the current page's data, selection, history (snapshots, gesture coalescing), the pointer state machine (every drag of §1.2), commands (align, distribute, order, group, flip, rotate, themes, styles, layers, clipboard, connectors, labels, auto-layout, templates, import) — the façade a platform drives |
| `io` | `diagramIo.ts` + `inflate.ts` (draw.io XML both ways, CSV) |
| `templates`, `pdf` | `diagramTemplates.ts`, `diagramPdf.ts` |

Time and randomness come from the caller (ids are made by an injected generator: the web's `makeId` is 8 random
base-36 characters). Web quirks are ported as quirks and marked `QUIRK`; bugs are listed in §6.

### 2.3 Desktop app

- `desktop/common/kubuno-office-diagrams-common` — the complete portable app: launch options
  (`kubuno-diagrams [--dark] [--culture fr|en] [--zoom 50] [--sample|--live] [--doc <id> | file.kbdia]`), opening a
  local `.kbdia`/`.json` file, the server client for the diagram routes (over `kubuno-office-desktop-common`'s
  `Transport`, i.e. the shell's token broker, never a password), the session host (the Documents state machine
  `api::session` and worker `api::live`, reused as they are: they are generic over `Server`/`Journal`), the crash
  journal (`<data>/diagrams/journal/<id>.kbdia`), the extension points (`platform`) and a text UI for Linux/macOS.
- `desktop/windows/kubuno-office-diagrams` — `kubuno-diagrams.exe`: `views/diagram_window.kbview` (TitleBarStyle Tall,
  header menus, declarative `<Ribbon Tone="Diagrams">` matching the web's tabs, a `DockArea` with the Formes, Format
  and Calques panels around the canvas, the page bar, the status bar), the custom controls `DiagramCanvas` (the
  core's editor painted through a Direct2D `Surface`, input, minimap and rulers overlaid as on the web),
  `StencilPalette`, `LayerList`, `PageTabs`; user controls for the Format panel; dialogs (confirm, templates, style
  JSON, rename); the Windows `Surface` (`platform/surface.rs`: path geometries, strokes with dash/cap/join, DirectWrite
  text, a shadow pass), the clipboard, file dialogs, PNG/JPEG encoding through WIC.

### 2.4 Shared pieces with Documents

Reused without copying: `kubuno-office-desktop-common::api::{session, live, client::{Transport, SyncTransport,
Request, Response, Method}}` (a path dependency). The ribbon shell, the backstage and the confirm dialog are `.kbview`
files of each app (the framework's controls are the shared part); the confirm dialog follows Documents' vendored copy.
Moving `session`/`live`/`client` into a crate of their own (`kubuno-office-desktop-session`) is the clean next step
once both apps use them — it touches the Documents crate, so it is coordinated, not done here.

## 3. The save path

1. Open (`--doc <id>`): `GET /diagrams/:id` (title, pages), then the session `join` (`POST :id/editing/join` →
   the whole content) — owner-only, so on someone else's diagram the session is `DigestOnly` and says so.
2. The window holds every page's data; the session's content is the canonical JSON of `{ pageId: data }` for all
   pages (sorted keys: the digest compares meaning, not the server's formatting).
3. Autosave: the session's cadence (structural changes 700 ms, typing with the office autosave setting); before each
   save the session re-reads `GET :id/export/json` and compares digests; a mismatch raises the conflict dialog (keep
   mine / take theirs), never a silent overwrite.
4. The save `PUT`s `…/pages/:pid/data` for each page whose data differs from the last read; 413 on a page is
   terminal (status « trop volumineux »); page create/rename/delete are immediate REST calls, like the web.
5. Offline-first: the bytes go to the journal first; a journal left by a crash is offered at the next open.
6. Close: save what is unsaved, then `leave` exactly once.

## 4. Increments

- **Note** — this file (commit 1).
- **Core** — shapes-core, then diagrams-core (model + file + stencils + render + geometry + layout + editor + io +
  templates + pdf), golden fixtures written by the web editor, unit tests mirroring the web, cross-target checks.
- **A** — open a local file and `--doc <id>`, display read-only at parity (shapes, connectors, grid, layers, pages,
  minimap, rulers, zoom/pan), light and dark.
- **B** — editing: every gesture and command of §1.2, ribbon/context menus/panels wired, shortcuts.
- **C** — save (session, digest, join/leave, conflict, 413, journal), page management, star, rename, trash,
  exports (PNG/JPEG/PDF/.drawio/JSON), import, templates.
- **VS** — the views in the designer, `Kubuno.Office.slnx` regenerated.

## 5. Theme

The web editor's chrome: `THEME_DIAGRAMS` = indigo `#3b53b5` tab strip (`ribbon/officeThemes.ts`), the framework's
`Tone="Diagrams"`. The canvas: the page's `bg_color` (white by default) in both themes, as on the web; panels follow
the app theme (light/dark).

## 6. Risks and known divergences

- Fonts: the web asks for Inter/Outfit with Arial fallback; the desktop uses DirectWrite with the same family names
  and the system fallback (desktop font decision), so label wrapping can differ by a few pixels.
- Canvas shadows: the web blurs (5 px); the desktop draws the shadow with a Direct2D shadow effect where available,
  else an unblurred offset copy — listed in the parity report.
- Hardware icons are SVGs rasterised by the browser; the desktop draws the same paths (only `path`, `circle`, `rect`,
  `polygon` and fills occur in the data).
- The web keeps the clipboard inside the page (`clipboardRef`); the desktop also puts a private format on the system
  clipboard so a copy survives between two windows.
- Macros (F33) run JavaScript in the browser; the desktop has no script runtime — listed as not done.

## 7. Verification

`cargo test` + `clippy -D warnings` for the four crates; `cargo check` of both cores for wasm32, Linux (natively on
.220) and macOS; golden fixtures from the web editor round-tripped byte-for-meaning; side-by-side captures of the same
diagram, web vs desktop, light/dark, 100 % and 175 %; the server round trip on a dev core with its own database; real
mouse on the ribbon (under the shared VS/input lock), keyboard shortcuts, UIA names.

## 8. Log

**2026-10-07** — note written.

## 9. Feature coverage

✔ done and verified · ◐ partial · ✘ not done · — not applicable. Updated as the increments land.

| Feature | Web | Desktop |
|---|---|---|
| F1–F34 | see §1.2 | ✘ (not started) |
