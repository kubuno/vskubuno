# Kubuno Presentations (desktop) — design note

Status: design of record for the native desktop version of the office module's presentation editor
(`kubuno-presentations.exe`), written before any code (mission of 2026-10-07: « the desktop app must fully catch
up with the web version — every feature of the web Presentations editor, not a subset »). It follows the method of
`DOCUMENTS-EDITING.md`: the web editor is the behavioural and visual reference, its engine is ported into a
platform-neutral shared core, the desktop app is the portable app of `desktop/common` plus the Win32 window of
`desktop/windows`. Section 7 (coverage) and section 8 (log) are kept up to date as increments land.

Paths below are relative to the office repository (`office/`) unless stated. `PEP` is
`web/src/PresentationEditorPage.tsx` (6 133 lines).

## 1. What the web editor does

### 1.1 Shape of the code

| Web file | Role |
|---|---|
| `PEP:593-1319` `class SlideRenderer` | the ONE renderer (Canvas 2D): `render()` for the editor, thumbnails, PDF and PNG; `renderPresent()` for the slideshow (animation reveal); background, text (rich runs), shapes (shared engine + fallback switch), images (crop, filters, tint, border, radius), lines (7 kinds, arrow heads), charts, tables, shadows, opacity, rotation, flips; `hitTest` |
| `PEP:1322-1660` `SlideThumbnail`, `SlidePanel` | the slide list: live thumbnails, multi-selection, drag reorder, context menu |
| `PEP:1707-3735` `SlideCanvas` | the editing surface: tools, pointer state machine (move, marquee, resize, rotate, knobs, crop, draw), smart guides and grid snapping, inline rich-text editor (`contentEditable` overlay), table cell editor, context menus, zoom |
| `PEP:3737-4255` | toolbar widgets: line tool, text format bar, geometry popover, chart/table/SmartArt dropdowns and editors, insert extras (emoji, symbols, fields) |
| `PEP:4256-4515` `PresenterNotes`, `PresenterMode` | notes pane, the slideshow (keys, animations, transitions, timer, autoplay, loop, black screen, laser) |
| `PEP:4517-6133` `PresentationEditorPage` | state, persistence (REST + Yjs), undo/redo, ribbon tabs, backstage, status bar, export |
| `presentationRichText.ts` | text model (doc → paragraphs → runs), HTML in/out of the editor, `layoutRich` (word wrap across styled runs, lists, indent, justify) |
| `presentationChart.ts`, `presentationTable.ts`, `presentationSmartArt.ts` | chart drawing, table drawing and structure, SmartArt geometry |
| `shapes/*` | the office-wide shape engine (presets, native geometry, knobs, catalogue, gallery) |
| `presentation/*` | mobile reader, notes sheet, phone remote (Yjs awareness transport) |

### 1.2 Feature inventory (F-numbers are referenced by the coverage table)

Model and rendering

- F1 Slide 960×540 fixed (`PEP:111`); element geometry in fractions 0..1, absolute sizes in slide px × `sf` (`PEP:602`).
  The server's `aspect_ratio`/`slide_width` are shown in the backstage only. **QUIRK**: 4:3 presentations still
  render 16:9.
- F2 Elements sorted by `zIndex`; `hidden` skipped; rotation and flips about the box centre; `opacity`; drop
  `shadow` (`true` = blur 8, dx/dy 3, rgba(0,0,0,.35)) (`PEP:736-790`).
- F3 Background: colour, gradient (multi-stop `grad` linear with angle / radial, legacy 2-stop), image stretched
  (`PEP:793-822`); default `theme.bgColor`.
- F4 Text element (`PEP:824-955`): rich runs (bold, italic, underline, strike, sup/sub, colour, highlight, size),
  paragraph attrs (align, list bullet/number, indent, line height), element defaults (size 24, align, colour,
  family, b/i/u), padding as % of width, vertical align, background + radius, placeholder (grey `#9aa0a6`),
  `textTransform`, `letterSpacing`, `columns` (2, gap 16), `autofit: shrink` (×0.9 steps), `textShadow`,
  `textOutline`, `wordArt` (vertical gradient).
- F5 Shape element (`PEP:977-1040`): ~144 kinds through the shared engine, solid/gradient fill, stroke with
  dash/dot, knobs (`adj`, legacy `cornerRadius`), caption text (plain, centred, wrapped).
- F6 Image element (`PEP:1146-1189`): `kbfile:` / `data:` / http sources, crop, filters (grayscale, sepia,
  brightness, contrast, saturate, blur), tint (multiply), border, corner radius, loading placeholder.
- F7 Line element (`PEP:1191-1252`): straight, arrow, elbow, curved, arc, polyline, freehand; arrow heads at both
  ends with size; dash styles.
- F8 Chart element (`presentationChart.ts`): column, bar, line, area, pie, donut; title, legend, grid.
- F9 Table element (`presentationTable.ts`): header row, first column, banded rows, cell fills/colours/bold/align,
  border colour, font size, style presets.
- F10 SmartArt (`presentationSmartArt.ts`): process, list, cycle, hierarchy, pyramid, matrix → grouped shapes and
  lines.

Editing (canvas)

- F11 Select tool: click (group-aware), Shift+click toggle, Ctrl+drag clone, drag move (Shift axis lock),
  marquee (Shift additive) (`PEP:2002-2255`).
- F12 Smart guides (7 px, slide/guides/elements edges and centres, magenta `#e1149e`) and grid snap (1/24)
  (`PEP:505-580`, `PEP:2189`).
- F13 Resize 8 handles (min 0.02, Shift ratio, snapping) (`PEP:2269`); rotate handle (Shift 15°) (`PEP:2368`);
  adjustment knobs (`PEP:2334`); crop mode (`PEP:1809-1913`).
- F14 Tools: text box (`PEP:2056`), shape draw-to-create (`PEP:2076`), lines of 7 kinds incl. polyline/freehand
  (`PEP:2098`).
- F15 Double-click: rich text edit (text, shape caption), table cell edit, image crop, polyline end.
- F16 Keyboard (`PEP:2768`): Delete, Ctrl+D, Ctrl+A, Ctrl+G / Ctrl+Shift+G, Ctrl+C/X/V (element clipboard),
  Esc, Ctrl+[ / ] (Shift: back/front), arrows nudge 0.004 (Shift 0.02); Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z.
- F17 Element context menu (`PEP:3106`): clipboard, alt text, rotate/flip, select same type, centre, duplicate,
  group, order, align/distribute, animate, link, format painter, opacity, shadow, lock, hide; shape (change
  shape, quick styles, corner radius), image (filters, border, radius, tint), text (effects, case, spacing,
  columns), line (arrows, arrow size, width, dash).
- F18 Background context menu (`PEP:3248`): background, layouts (11), guides (add, show, clear).
- F19 Rich text editing: caret, selection, typing, Enter (new paragraph), marks on the selection or the whole
  element, paragraph attrs on every paragraph of the element (`PEP:2664-2766`), fit menu (none, resize shape,
  shrink).
- F20 Zoom: fit, ±1.2×, 50–200 % of fit, grid, snap to grid, checkerboard, dark ground, ruler (`PEP:3d`).

Slides

- F21 Slide panel: thumbnails, click / Ctrl / Shift selection, drag reorder, « + Nouvelle diapositive », context menu
  (cut, copy, paste, delete, new, duplicate, show/hide, move up/down, background) (`PEP:1435-1640`).
- F22 Layouts (`PEP:351-390`), placeholders, new slides seeded with title + subtitle (`PEP:202`).
- F23 Background editor (colour, gradient, image, apply to all) (`PEP:4866-4890`).
- F24 Transitions (12 types, duration) (`PEP:168`, `PEP:5025`); hidden slides; notes pane.

Ribbon (tabs Fichier, Accueil, Insertion, Disposition, Animations, Transitions, contextual Forme)

- F25 Accueil: clipboard (slides), Diapositive (new, duplicate, export PDF/PNG/JPG/all PNG), Diaporama.
- F26 Insertion: shape gallery (draw-to-create); the floating toolbar's image, chart, table, SmartArt, lines, text,
  emoji, symbols, fields, text presets.
- F27 Disposition: align, distribute, order, group, same size, swap, rotate/flip, centre, stretch, duplicate,
  delete.
- F28 Animations: entry (20 types), exit (stored only on the web), duration, delay.
- F29 Transitions tab; F30 Forme tab (fill, outline, align, order, rotate, delete, opacity, shadow).
- F31 Backstage: start page (recent, browse, new), Informations (title, type, format, slide count), Imprimer,
  Fermer.

Slideshow

- F32 Full-screen show of non-hidden slides from the current one: next/prev keys, click, Home/End, Esc, B (black),
  N (counter), P (autoplay 3.5 s), L (loop), timer, notes bar (`PEP:4285-4515`).
- F33 Entry animations one per step (zIndex order), delay/duration, 20 effects with the web's easings;
  transitions on the incoming slide (CSS keyframes `kbp_*`, `PEP:4442`).
- F34 Phone remote and laser through Yjs awareness (`presentation/remote.ts`).

Files and server

- F35 Persistence: per-slide `PUT` (elements, notes) after 1 s, `PATCH` meta (background after 350 ms,
  transition, hidden), structural calls immediate (§2).
- F36 Undo/redo: element snapshots per slide, 500 ms sliding coalescing, depth 80 (`PEP:4976-5010`).
- F37 Images uploaded as assets (`POST :id/assets`, ≤ 1600 px), referenced `kbfile:<id>`; legacy `data:` images
  migrated on open (`PEP:4719`).
- F38 Title rename, star, trash, share (collaborators), presence avatars, live Yjs merge per slide.
- F39 Status bar: « Diapositive n sur N », « N objet(s) », mode.

Web dead UI and bugs (not ported as bugs; listed so the desktop does not copy them): disabled menu entries
(paste without formatting, theme, comments), buttons without handlers, exit animations and links never played,
locked elements unclickable, lines without endpoint handles, rotated resize in the unrotated frame, shape
caption marks dropped, polyline points not nudged, one pending debounce slot losing an earlier slide's save,
placeholders visible in the slideshow, « Pousser » = « Glisser », « Couvrir » = « Glisser ».

## 2. Stored format and API

Content file `.kbsld` in Drive (`Office/Presentations/<title>.kbsld`, gzip JSON, mime
`application/vnd.kubuno.presentation+json`), written by the server only:

```json
{ "version": 1,
  "slides": { "<slide uuid>": { "elements": [ … ], "background": { "type": "color", "color": "#ffffff" },
                                "notes": "", "transition": { "type": "none", "duration": 0.3 } } } }
```

The order and the hidden flag are NOT in the file: they are rows of `office.slides` (`position`, `is_hidden`).
`office.presentations` holds the title, theme, aspect ratio, `file_id` and `draft_file_id`. Element JSON is the web's
`SlideElement` (`web/src/api.ts:645-797`); text content is a ProseMirror-like doc (`presentationRichText.ts`).

Routes (`server/src/router`, handlers `server/src/handlers/presentations.rs`):

| Route | Use |
|---|---|
| `GET /office/presentations/:id` | metadata + ordered slide summaries |
| `GET /presentations/:id/slides/:sid` | one slide: summary + `data` (content of the draft if any, else the main file) |
| `PUT /presentations/:id/slides/:sid` `{elements?, notes?}` | read-modify-write of the whole content file, mirrored to the main file |
| `PATCH /presentations/:id/slides/:sid` `{background?, transition?, is_hidden?}` | same, plus the hidden flag |
| `POST /presentations/:id/slides` `{id?, position?}`, `DELETE …/:sid`, `POST …/:sid/duplicate`, `PATCH …/slides/reorder` | structure |
| `POST /presentations/:id/editing/join` / `DELETE …/leave` / `POST …/ping` | session: join returns the whole content; leave copies the draft into the main file and deletes it when no editor remains |
| `POST /presentations/:id/assets` (multipart), `GET …/assets/:file_id` | images (`kbfile:<id>`) |
| `PATCH /presentations/:id` `{title?, is_starred?, theme?}`, trash, restore, duplicate, `GET /presentations` (list), `GET /presentations/delta` | file operations |

Server facts that shape the client:

- **No concurrency control at all**: no etag, no `If-Match`, no revision; every slide write re-reads and re-writes
  the whole file. Two writers of the same slide: last one wins; two writers of different slides can still lose one
  write if their read-modify-writes interleave (server issue, reported).
- Any slide write creates a draft file (`ensure_pres_content_file`) that only `leave` cleans up; reads prefer the
  draft. Access is owner OR collaborator for every route here (unlike Documents' owner-only sessions).
- Request bodies are capped at axum's default 2 MiB per request (no `DefaultBodyLimit`): a slide whose JSON exceeds
  it is refused with 413 forever — terminal for that slide. Images go to assets, so only legacy `data:` images can
  hit it.
- The web keeps Yjs (`office-presentation:<id>` room, one `Y.Map` of `{elements, notes}` per slide) for live merge.
  The desktop does not speak Yjs in this lot (§6): web clients see desktop writes on their next load, and a web
  client still holding an older copy of a slide can overwrite it with its next keystroke on that slide.

## 3. Architecture

```
office/
  common/shapes-core/      kubuno-office-shapes-core       the office shape engine (shared with diagrams, sheets…)
  common/slides-core/      kubuno-office-slides-core       the presentation engine (this note)
  desktop/common/kubuno-office-presentations-common         the complete portable app
  desktop/windows/kubuno-office-presentations               the Win32 window, kubuno-presentations.exe
```

### 3.1 `kubuno-office-slides-core` (platform-neutral)

| Module | Port of | Content |
|---|---|---|
| `model` | `api.ts` types, `content_files.rs` | the `.kbsld` content held **losslessly** (every element is its original JSON object, unknown keys kept, numbers written as the web writes them), typed accessors and setters, slide order and hidden flags from the server |
| `richtext` | `presentationRichText.ts` | doc ⇄ paragraphs of runs, `layoutRich` over a `Measure` trait (the platform measures: DirectWrite, canvas `measureText`), plus the desktop's own text editing (caret, selection, typing, marks) replacing the browser's `contentEditable` |
| `render` | `SlideRenderer`, `presentationChart.ts`, `presentationTable.ts` | the slide drawn through a Canvas-2D-like surface trait (paths, gradients, text runs, images, transforms, opacity, shadow, clip) that each platform implements |
| `edit` | `SlideCanvas` gestures and commands | selection, hit testing, move/resize/rotate/knob/crop/draw state machine, snapping, every ribbon and menu command as a pure function over the slide |
| `history` | `PEP:4976` | per-slide element snapshots, 500 ms sliding coalescing, depth 80 |
| `show` | `PresenterMode` | the slideshow sequence (visible slides, animation steps, reveal timing, transition progress, autoplay, loop) — pure, time given by the caller |
| `smartart`, `layouts` | `presentationSmartArt.ts`, `SLIDE_LAYOUTS` | generators |

No Win32, no clock, no randomness: ids come from an injected generator (the web's `uid()`: 8 base-36 characters),
the time from the caller. Checked with `cargo check --all-targets` for `wasm32-unknown-unknown`,
`x86_64-unknown-linux-gnu` (natively on the build host) and `aarch64-apple-darwin`.

Golden fixtures: geometry recorded from the web engine under Node (`tools/*.ts`, like `shapes-core/tools/golden.ts`):
shape paths, rich-text line breaking with a fixed measurer, chart and table drawing commands, SmartArt boxes; a
`.kbsld` written by the web editor of the dev core for the round trip.

### 3.2 Desktop app

`desktop/common/kubuno-office-presentations-common` — everything but the window: launch options (`--doc <id>`,
local `.kbsld`/JSON file, `--dark`, `--culture`, `--zoom`, `--sample`), the open deck's view state, the server client
(routes above) and the save session (§4), the crash journal, the platform extension points (`LaunchRules`,
`UiHost`) and a text interface for Linux/macOS until their windows exist. It reuses Documents' portable pieces
(`kubuno-office-desktop-common`: HTTP transport over the shell's token broker, error mapping, digests, ids) instead
of copying them; what both apps need beyond that is factored into `desktop/common` when Documents' owner agrees.

`desktop/windows/kubuno-office-presentations` — `kubuno-presentations.exe`: `presentation_window.kbview` (TitleBarStyle
Tall with the header menus, the declarative `<Ribbon>` with the web's tabs, the QAT, the Backstage, the status bar)
and the custom controls drawn with the framework's `on_paint`: `SlidePanel` (thumbnails), `SlideCanvas` (the
editing surface), `NotesPane`, `SlideShowWindow` (full screen). The core's surface trait is implemented with
Direct2D (geometries, gradient brushes, DirectWrite runs, WIC bitmaps, layers for opacity/clip, the shadow
effect). Light and dark follow the web's tokens: module accent `#b7472a` (`THEME_PRESENTATION`), workspace ground
`surface-2` `#f1f3f4`, selection `#1a73e8`, smart guides `#e1149e`, knobs `#ffd400`.

## 4. Save path

The web has no conflict protection beyond Yjs. The desktop adds the Documents rules, adapted to per-slide writes:

1. Open: `GET :id` (order, hidden, title) then `POST :id/editing/join` (the whole content; presence). Each slide's
   loaded bytes are digested.
2. Edits are local first: the deck is journaled on disk (crash journal under the app's data folder, replayed at
   start with a Kubuno `ConfirmDialog`) before any server write.
3. Save (1 s after the last change, Ctrl+S, close): per dirty slide, **re-read the slide (`GET …/:sid`) and compare
   its digest with the one the desktop last read or wrote**; equal → `PUT` (elements, notes) / `PATCH`
   (background, transition, hidden); different → conflict dialog (keep mine / take theirs) for that slide.
   Structural changes (new, delete, duplicate as create + put, reorder) are sent immediately in order.
4. 413 is terminal for that slide (message, the rest keeps saving); network failures retry with backoff, the
   journal keeps the work.
5. Close: flush, then `DELETE :id/editing/leave` exactly once; `ping` every 60 s while open.

## 5. Increments

- **A — read-only display at parity**: crates, model round trip, rendering of every element type, slide panel,
  notes, status bar, ribbon shell, light/dark, open a local file and `--doc <id>` through the shell's broker.
- **B — editing**: selection and the pointer state machine, text editing, every ribbon/menu command with checked
  states, undo/redo, slide operations, slideshow.
- **C — save**: §4, journal, conflict dialog, 413.
- **D — the rest of the web**: export PDF/PNG/JPG, image assets, charts/tables/SmartArt editors, backstage start page,
  share, presence (read-only), remote control (needs Yjs: §6).
- **VS**: the views open in the designer; `Kubuno.Office.slnx` regenerated (`KUBUNO_GEN_REPO=office
  KUBUNO_GEN_FRESH=1`).

## 6. Risks and open questions

- Yjs: live merge with web clients and the phone remote need a Rust Yjs client (`yrs`) speaking the core's collab
  protocol — a lot in itself; out of this lot unless time allows. Until then concurrent web editing of the same
  slide is last-writer-wins on the web side.
- Server: no concurrency token (a slide revision or an etag on `GET …/:sid` would make the digest re-read
  unnecessary); interleaved read-modify-writes; drafts that outlive web sessions. To report to the office owner.
- Fonts: the web uses `Outfit, Arial` by default; the desktop measures with DirectWrite, so line breaks can differ
  where the fonts' metrics do (Outfit is not installed on Windows by default → fallback).
- Canvas features without a direct Direct2D equivalent: CSS filters (`blur`, `sepia`…) map to D2D effects; `letterSpacing`
  needs per-glyph advances.
- The shared shape engine and its canvas are also being built by the Diagrams agent (`common/shapes-core`): one
  crate, coordinated, never two copies.

## 7. Coverage (feature → web → desktop)

✔ done and verified (test or capture) · ◐ implemented, not verified by real input yet · ✘ not done (reason).
Desktop paths: `office/common/slides-core` (core), `office/desktop/common/kubuno-office-presentations-common`
(common), `office/desktop/windows/kubuno-office-presentations` (app).

| F | Feature | Web | Desktop |
|---|---|---|---|
| F1 | 960×540 slide, fractions, `sf` | `PEP:111` | ✔ core `model`, `render` (tests); 4:3 quirk kept |
| F2 | z-order, hidden, rotation/flip, opacity, shadow | `PEP:736` | ✔ core `render` + app `d2d` (capture) |
| F3 | Background colour / gradient / image | `PEP:793` | ✔ render; ◐ side panel pickers, background dialog |
| F4 | Rich text rendering (runs, paras, effects, columns, autofit, WordArt) | `PEP:824` | ✔ core `richtext`/`render` (tests); `stroke_text` (text outline) ✘ in the D2D surface |
| F5 | Shapes (~144 kinds, fills, knobs, captions) | `PEP:977` | ✔ `shapes-core` golden test against the web engine |
| F6 | Images (sources, crop, filters, tint, border, radius) | `PEP:1146` | ✔ render; http(s) images ✘ (signed URLs not fetched) |
| F7 | Lines (7 kinds, arrows, dashes) | `PEP:1191` | ✔ core (tests) |
| F8 | Charts | `presentationChart.ts` | ✔ render; ◐ toolbar type / data dialog / legend |
| F9 | Tables | `presentationTable.ts` | ✔ render; ◐ table bar (rows, cols, flags, styles, border, size, reset) |
| F10 | SmartArt | `presentationSmartArt.ts` | ✔ core `smartart` (tests); ◐ insertion |
| F11–F13 | Select, marquee, move, smart guides, grid snap, resize, rotate, knobs, crop | `PEP:2002-2368` | ✔ core `edit` (tests mirroring the web; locked-selectable and line-move bugs fixed); ◐ canvas input |
| F14 | Tools (text, shape draw, 7 line kinds) | `PEP:2056-2098` | ✔ core; ◐ toolbar / gallery arming |
| F15 | Double click (text, cell, crop, polyline end) | — | ◐ canvas |
| F16 | Canvas keyboard | `PEP:2768` | ✔ core `key` (tests); ◐ canvas |
| F17 | Element menu (all sections incl. shape/image/text/line) | `PEP:3106` | ✔ actions (common `model/actions.rs`, tests); ◐ menu clicks |
| F18 | Ground menu (background, 11 layouts, guides) | `PEP:3248` | ◐ |
| F19 | Rich text editing, marks, paragraph attrs, fit menu | `PEP:2664` | ✔ core `textedit`, `format` (tests); ◐ caret/IME in the canvas (no IME composition window yet) |
| F20 | Zoom pill (fit, ±, %, grid, snap, checker, dark, ruler) | — | ✔ drawn (capture); ◐ clicks |
| F21 | Slide list (selection, drag reorder, footer, menu) | `PEP:1435` | ◐; resizable width ✘ (fixed 150) |
| F22 | Layouts, placeholders | `PEP:351` | ✔ core (tests) + capture |
| F23 | Background editor | `PEP:4866` | ◐ dialog + side panel |
| F24 | Transitions, hidden slides, notes pane | `PEP:168` | ◐ ribbon combos, notes bound two-way |
| F25 | Accueil tab | `PEP:5524` | ✔ capture vs web; ◐ commands |
| F26 | Insertion (shape gallery) + toolbar insertions | `PEP:5513`, `SlideToolbar` | ◐ |
| F27 | Disposition | `PEP:5551` | ◐ (all commands wired to tested core ops) |
| F28 | Animations tab | `PEP:5585` | ◐ |
| F29/F30 | Transitions tab, Forme tab | — | ◐ |
| F31 | Backstage | `useFileTab` | ◐ Informations, Ouvrir, Enregistrer une copie, Exporter PDF, Fermer; start page (recent/browse/new) ✘, Imprimer ✘ |
| F32 | Slideshow (keys, click, black, numbers, autoplay, loop, timer, notes, bar) | `PEP:4285` | ◐ `SlideShow` control over core `show` (tests) |
| F33 | Entry animations, transitions | `PEP:4442` | ✔ core (tests); ◐ drawing (layers for fade/wipe) |
| F34 | Phone remote, laser | Yjs awareness | ✘ (no Yjs on the desktop) |
| F35 | Per-slide persistence | §2 | ✔ live round trip test on a dev core (`tests/live_roundtrip.rs`): edits identical, conflict kept until « keep mine », 413 terminal per slide |
| F36 | Undo/redo (500 ms coalescing, depth 80) | `PEP:4976` | ✔ core `history` (tests) |
| F37 | Image assets upload (`kbfile:`) | `PEP:4719` | ✘ images are inserted as `data:` URLs (the server's 2 MiB slide limit applies) — upload route not wired yet |
| F38 | Rename, star, trash, share, presence, Yjs merge | — | ✘ rename/star/share/presence (title bar has no title field yet); Yjs replaced by the digest-guarded save (by design) |
| F39 | Status bar | `PEP:6061` | ✔ capture vs web |
| — | Exports PDF/PNG/JPG/all | `PEP:5034` | ✔ off-screen render + PDF writer (tests); ◐ dialogs |
| — | Web top bar extras (slide navigator, Slideshow button, share, « Affichage » tab of OfficeShell, Macros) | `OfficeShell` | ✘ not in the desktop chrome yet |

## 7.1 As built (2026-10-07)

- `common/slides-core` gains `format` (the toolbar's paragraph attributes, run marks and element fallback) and
  `overlay` (the editor's furniture); `shapes` re-exports the shared shape engine.
- `desktop/common/kubuno-office-presentations-common`: `model/actions.rs` (the element menu by key),
  `model/gradient_css.rs` (picker CSS ↔ stored `grad`), `pdf.rs` (`pagesToPdf`), `api/live.rs::asset_fetch`.
- `desktop/windows/kubuno-office-presentations` (`kubuno-presentations.exe`): `presentation_window.kbview` (ribbon
  `Tone="Presentation"`, 200+ commands, five context menus, toolbar row, side panel, notes, status bar),
  `slideshow_window`, dialogs (background, chart data, geometry, prompt, confirm), user control
  `PresentationInfo`, custom controls `SlideCanvas`, `SlidePanel`, `SlideShow`; `platform/export.rs` renders through
  a WIC-backed Direct2D context. View tests compile every view and check icons, labels, tab order and resources.
- Verification: 64 core tests, 23 common tests (+1 ignored live), 13 app tests, clippy `-D warnings`; dev core on
  127.0.0.1:18280 with database `kubuno_dev_presentations`; captures (desktop light/dark 175 %, web 100 %/175 %) in
  `C:\kubuno-build\release\presentations\captures`. Not done: real mouse/keyboard runs over the ribbon and canvas,
  UIA names check, VS designer check, desktop 100 % capture (the test machine runs at 175 %), web dark capture
  (the web theme is an account setting, not `prefers-color-scheme`).

## 8. Log

**2026-10-07** — design note; `kubuno-office-shapes-core` (shared with Diagrams) committed with its golden test
against the web engine (every catalogue kind and slide kind: sub-paths, text frames, knobs).

**2026-10-07** — `kubuno-office-slides-core` and `kubuno-office-presentations-common` (office 35047eb, c999f89,
f01eebf); the Windows app `kubuno-presentations.exe` (office 53713c1), the solution regenerated by the generator
(4e975fa), the live round trip test (9be547b). Coverage in §7; next: real-input runs, UIA, designer check, asset
upload, backstage start page, title field and the web top bar's extras.
