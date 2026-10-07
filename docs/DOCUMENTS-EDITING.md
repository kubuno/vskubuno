# Kubuno Documents (desktop) — the editing path

Status: plan and log of the editing lot (user request of 2026-10-02: « fais ce qui n'est pas fait, et les pages ne sont
pas encore éditables »), started after app lot 3 (`DESKTOP-MIGRATION.md`). This file is the design of record for the
port; each increment below records what was built and how it was verified.

Reference for behaviour: **the web editor**. `office/frontend/src/canvas-engine.ts` lays out, paginates, places the
caret and the selection and paints one canvas per page; TipTap/ProseMirror is only a hidden 1-px view that captures
keys and holds the model. So the desktop ports **that engine** and **ProseMirror's editing semantics as the web
configures them** (TipTap StarterKit + the office extensions, Yjs undo), not a generic rich-text control.
`documents/text-surface/*.ts` (nav keys, pointer selection, caret, auto-scroll, context menu) is the reference for
input; `DocumentEditorPage.tsx` for the commands.

The planning folder `C:\kubuno-build\documents-spec\` named by the request does not exist on this machine (checked
C:\ and E:\, 2026-10-02); its decisions survive in the code comments that cite them (`31-CORRECTIONS.md` C1, C7, C9 in
`documents/src/api/session.rs` and `doc/images.rs`), which this plan follows.

## 1. Shared code: the platform-neutral crate `kubuno-office-docs-core`

User direction (2026-10-02): share the algorithms with the web rather than write them once per platform. Everything
that is not drawing, input or windowing lives in **`kubuno-office-docs-core`** — since 2026-10-06 in the office repository, `office/common/core` (name per the
`kubuno-<domain>-core` convention of the cross-module study), a member of the `desktop/common` workspace like
`kubuno-desktop-sync`, depended on by path from `documents`. It has no Windows, Direct2D, DirectWrite or `kubuno_desktop_ui`
dependency (only `serde`, `serde_json`) and is checked with `cargo check --all-targets` for `wasm32-unknown-unknown`,
`x86_64-unknown-linux-gnu` and `aarch64-apple-darwin`, like `kubuno-desktop-views-syntax`.

| Module | Content | Origin |
|---|---|---|
| `model` | the stored document held losslessly (`Node`: raw JSON per key; bare document vs multi-page envelope; write-back rules) | moved from `documents/src/model/{mod,node,fixtures}.rs` |
| `pm` | ProseMirror position arithmetic over `Node` (`node_size`, `resolve` → path + parent offset, `text_between`), the office schema's leaf/textblock/container classification | new |
| `marks` | `TextMark` (bold, italic, underline, strike, code, size, family, colour, highlight, script, caps, letter spacing, link, revision) read from a node's `marks`, and the mark JSON writers the commands use | new (port of `extractMarks`) |
| `measure` | the `Measure` trait (advance width of a string in a `TextMark`; ascent/descent) — the **only** door to fonts: DirectWrite on the desktop, `measureText` on the web | from `doc/para.rs` |
| `layout` | the port of `canvas-engine.ts`: `parse` (`parseDoc` → render paragraphs with ProseMirror positions, lists, headings, tables, images, breaks), `paragraph` (`layoutParagraph`, the line breaker with its quirks), `table` (`layoutTable` over `tables.rs`), `flow` (`layoutParagraphs`: the continuous layout, contextual spacing) with a per-block cache, `paginate` (`paginateMulti`: pages, columns, `breakBefore`, keep-with-next/keep-lines), `caret` (`posToCoords`, `coordsToPos`, `selectionRects`, `adjacentLineCenter`, word/paragraph/line boundaries, `prevWordPos`/`nextWordPos`) | `para.rs`, `tables.rs`, `images.rs` moved; the rest ported from the web |
| `edit` | steps and their exact inverses, transactions, history grouped like the web, commands (typing, Enter, Backspace/Delete across blocks, marks, block attributes, lists, indent, spacing, breaks, insertions), clipboard payloads (plain text, HTML in and out, private JSON), find/replace | `history.rs`/`clipboard.rs` payload layer reworked; commands new |
| `editor` | `Editor`: the document + selection + history + cached layout and pages — the façade a platform drives (desktop now, WASM later) | new |

What stays in `documents` (Windows): painting the pages (Direct2D/DirectWrite: `doc/paint.rs`, `doc/fonts.rs`,
`platform/painter.rs`), the DirectWrite `Measure`, input (keys, `WM_CHAR`, IME), the Win32 clipboard formats, the
caret timer, accessibility, the window, the ribbon wiring, the server session (`api/`).

### Positions

Positions are **ProseMirror positions** (integers counted over the whole document, `nodeSize` rules: text = UTF-16
length, leaf = 1, container = 2 + content), exactly what every `pmPos` of the web engine is. The earlier desktop
`Position { paragraph, offset }` could not address a paragraph inside a list item or a table cell; a ProseMirror
position can, and it is the unit the web, the clipboard slices and a future WASM bridge all share. Layout lines carry
`pm_start`/`pm_end` and spans `pm_pos` exactly as `LayoutLine`/`LayoutSpan` do.

Known web quirks are ported as quirks (each marked `QUIRK` in the code): hard breaks paint nothing; block quotes and
task lists are not drawn (their positions are still counted); list markers are `•` / `n.` at every depth; the line
breaker breaks only on JavaScript `\s` and tabs. One web **bug** is not ported: `parseDoc` counts a `horizontalRule`
as 2 positions where ProseMirror counts 1 (`canvas-engine.ts:996-1001`), which shifts every position after a rule by
one on the web; the desktop uses ProseMirror's count (an edit must land where the model says).

### Edits, exactly reversible

The one primitive step is `Replace { parent: Path, from, to, with: Vec<Node> }` (replace children `from..to` of the
node at `parent`, a path of child indices from the body). Its inverse is computed while applying it — the removed
nodes, byte-identical — so undo restores the stored bytes exactly; there are no hand-written inverses. Typing in a
paragraph replaces the paragraph node; a mark or attribute change replaces the nodes it touches. Consecutive replaces of
the same node inside one undo group are coalesced (first inverse, last forward), so a typed sentence costs two copies
of its paragraph, not two per keystroke.

Grouping follows the web: the body's undo is Yjs's `UndoManager` (y-prosemirror's `yUndoPlugin`, `captureTimeout`
500 ms, nothing calls `stopCapturing`), so **changes less than 500 ms apart merge into one undo step**, whatever they
are; undo restores the selection of the first, redo the selection of the last. The desktop history is told the time
by its caller (the core never reads a clock).

## 2. Increments

Each increment is verified before the next: `cargo test` (core + documents), `cargo clippy -- -D warnings`, the
core's cross-target `cargo check`, real runs with captures in light and dark, then `dist\kubuno-documents.exe`
refreshed (a self-contained exe: kubuno-desktop-ui is linked statically).

### A — editable pages

1. Core crate: model moved, positions, marks, `Measure`, the parse/paragraph/flow/paginate port with ProseMirror
   positions, the caret functions. Documents paints the **paginated layout** (`PageLayout`s) instead of its old
   paragraph-only flow — lists, tables, headings, colours, underline, highlight, sub/superscript become visible.
2. Caret and selection: blink (web: solid 0.5 s after a move, then 1 s period), click, drag with auto-scroll (48 px
   edge, ≤ 30 px a step), Shift+click, double-click word, triple-click paragraph (cell-aware), Ctrl+A; ←/→ (by
   character, by word with Ctrl), ↑/↓ keeping the goal column with the wrap-boundary affinity of `nav-keys.ts`,
   Home/End (visual line; Ctrl: document), PageUp/PageDown (one view height, goal column kept); selection painted
   across pages; the view scrolls to keep the caret visible.
3. Typing: `WM_CHAR` (dead keys and AltGr arrive composed: AltGr = Ctrl+Alt is never taken for a shortcut), surrogate
   pairs, IME (composition window placed at the caret with `ImmSetCompositionWindow`/`ImmSetCandidateWindow`, result
   through `WM_CHAR`; inline composition is a later refinement); stored marks (a toggled mark applies to the next typed
   character, like ProseMirror).
4. Enter (`splitBlock`; in a list `splitListItem`, an empty item lifts out), Backspace/Delete within and across blocks
   (`joinBackward`/`joinForward`; Backspace at the start of an indented paragraph removes one 48 px indent step first,
   like `nav-keys.ts`), Tab/Shift+Tab (cells, list nesting, first-line indent or a tab character), Ctrl+Enter (page
   break). Selections that span blocks are deleted ProseMirror-style (the end block's tail joins the start block).
5. Undo/redo (Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z), grouped as above, ribbon/QAT state bound.
6. Clipboard: out = private JSON (lossless) + `HTML Format` + `CF_UNICODETEXT`; in = private, `HTML Format` (a reader
   for the structure and character formatting the web's own HTML carries), plain text; images (`CF_DIB`/PNG) resized on
   insert (`images::plan_insert`, 1600 px long edge, JPEG/PNG budget) and refused with a message past the PATCH budget.
7. Live reflow: the layout cache keys each top-level block by a revision counter bumped by the steps that touch it, so
   a keystroke re-lays out one block and re-paginates (pagination is linear and cheap); measurement is cached per
   (string, font). Painting only the visible pages; no flicker (single Direct2D frame).
8. Accessibility: the canvas exposes its text and caret — at least the caret position (system caret moved with
   `SetCaretPos`, which screen readers and magnifiers follow); a UIA `TextPattern` provider if the host's
   accessibility layer allows it (see §4).
9. Context menu (right click): Couper, Copier, Coller, Coller sans mise en forme · Lien… · Saut (page) · Texte (gras,
   italique, souligné, barré) · Aligner · Paragraphe… · Tout sélectionner — the web's `buildCtxItems` minus what the
   desktop does not have, using the menus agent's `ContextMenu`/`MenuItem` components; a right click outside the
   selection moves the caret, inside keeps it (web).

### B — ribbon commands

Every `<Command>` the model supports gets an `OnExecute` and its `Checked`/`Enabled` bound to the selection
(`Editor::state_at_selection`: marks in common, block type, alignment, list kind, spacing). The table in §3 is
maintained as commands land.

### C — what lot 3 left

* Ruler edits written to the model through `edit` (undoable): indents → `indentLeft`/`indentFirstLine`/
  `indentRight` (rounded px, `null` when 0, like `setParaIndentAttrs`), tab stops → `tabStops: [{pos, type}]` (the
  web's shape, type kept), margins → `sections[0].margins` of the envelope (a bare document becomes an envelope only
  when its page setup changes, since a bare document has nowhere to store margins). During a drag the change is live
  but not recorded; the release records one step (web `commit=false` then `commit=true`).
* Pages side by side like the web: the page container is a wrapping row (`flex-wrap`, column gap 24, row gap
  `PAGE_GAP`, centred, rows top-aligned), so as many pages as fit the viewport width share a row — at 50 % two or more
  A4 pages on a common screen. The vertical ruler and the active page follow the caret's page.
* Redo the two wrong captures of the ruler comparison (« DESKTOP 100 % » showed a VS window; « dark » was light).

### D — open and save

* Open: a local file (`content_json` or `.kbdoc`), and from the server through the shell's token broker / the
  sandbox profile (never real credentials), `GET /api/v1/office/documents/:id`.
* Save (`api/session.rs`, already written as a pure state machine): join the editing session on open (owner-only;
  otherwise digest-only protection, said so), ping every 60 s, **re-read the content and compare digests right before
  each PATCH** (If-Match is sent but not trusted), PATCH the full document (`model::Document::to_vec`: envelope and
  sibling keys round-tripped, never a bare ProseMirror doc in place of an envelope), treat 413 as terminal, leave once
  on close after any in-flight save. Works with the server as deployed (no etag rotation on draft promotion, delta
  500) and with office `c3c6429` (not deployed).
* Ctrl+S, the dirty indicator in the title, a conflict dialog (Kubuno `ConfirmDialog`: keep mine / take theirs).
* Offline-first: edits persist locally first — a journal of the document bytes per open document under the app's
  local data folder, replayed at start — before any server write (`DESKTOP-OFFLINE-SYNC.md`). Syncing documents
  through `kubuno-desktop-sync-engine` (outbox, feeds) is a later increment: the document is one opaque blob of up to 2 MiB,
  not rows, and the conflict rule above (digest, owner-only sessions) is specific to it.
* Tests against the dev core and dev database only (`KUBUNO_DEV_DATABASE_URL`, `docs/WEB.md` §6–7); if the office
  module cannot run locally, save is tested against a local mock and open read-only against the live server, and the
  report says so.

## 3. Parity with the web editor

The target (user, 2026-10-07): **every feature of the web Documents editor**. This table lists them all, from the
inventory of the web sources (`DEP` = `office/web/src/DocumentEditorPage.tsx`, `CE` = `canvas-engine.ts`,
`docs/` = `office/web/src/documents/`), with the desktop's state.

✔ done (a unit test of `kubuno-office-docs-core` mirroring the web, plus a real run where marked *(run)*) ·
◐ partial (what is missing is said) · ✘ not done yet (why) · ⛔ blocked (a decision or a missing platform piece) ·
— nothing to port (a stub, a no-op or a bug on the web; the desktop does not invent what the web lacks).

Ribbon commands run through the same `OnExecute` whether clicked or typed (shortcut). Real mouse clicks on the ribbon
were verified on 2026-10-07 (tab, combo box, split-button menu, button: see §5); clicks *posted* to the window
(`PostMessage`) reach the page but not the ribbon, a property of the host's hover tracking (§5).

### File and Backstage

| Feature | Web | Desktop |
|---|---|---|
| Open a server document (`--doc <id>`), journal of unsaved work, recovery offer, conflict dialog, 413 terminal | `DEP:14559`, `useOfficeStore` | ✔ *(run, dev server)* — §5 |
| Save (Ctrl+S, QAT) and autosave (30 s debounce, 700 ms for page setup) | `DEP:14326`, `:7080`, `:14336` | ✔ *(run)* |
| Rename, info (created, modified, pages, words) | `DocumentsBackstage.tsx:62-78` | ◐ the Backstage shows title, pages, zoom; rename and dates not wired |
| Info tabs « Résumé », « Personnalisation » | `ModuleBackstage.tsx:149` | — (« bientôt disponible » on the web) |
| Save to source (DOCX/ODT) | `DEP:14748` | ✘ (server route exists; no desktop UI yet) |
| Export PDF (raster), DOCX/ODT (server), TXT | `DEP:14689`, `:14758`, `:14738` | ✘ (desktop printing/export lot: `PRINTING.md`) |
| Print | `DEP:14700` | ✘ (same) |
| Star, trash, share, presence avatars | `DEP:15706`, `:15653`, `:15625`, `:15676` | ✘ star/trash/share not on the desktop window; presence ◐ (status bar lists the other editors of the editing session) |
| New / duplicate | handlers `DEP:15227` | — (not reachable from the web editor's UI) |

### Accueil (Home)

| Feature | Web | Desktop |
|---|---|---|
| Paste, cut, copy, paste without formatting | `DEP:11958-11960` | ✔ (the desktop pastes rich content too: private JSON, HTML, images) |
| Clipboard history | `ribbon/clipboardGroup.tsx:127` | — (a no-op in the document body on the web) |
| Format painter (once, sticky, Esc) | `DEP:11964`, `docs/format-painter.ts` | ✔ |
| Clear formatting | `DEP:11969` | ✔ |
| Font family, size, bold, italic, underline, strike, sub/superscript, grow/shrink, case, small caps, colour, highlight | `DEP:11988-12009`, `:15285` | ✔ (bold *(run)*) |
| Character spacing dialog, « Autres couleurs » | `DEP:15291`, `RibbonColorBtn` | ✘ (no colour picker / spacing dialog on the desktop yet) |
| Text effects (WordArt presets, outline, shadow, glow…) | `docs/text-effects/ribbon.tsx:353` | ✘ (the `textEffect` mark is kept on save; not painted) |
| Bullets, numbering, alignment, indent, line spacing, space before/after, first-line, hanging | `DEP:12020-12040` | ✔ (bullets *(run)*) |
| Task list, quote (blockquote) | `DEP:12022`, `:12144` | — (written but never painted on the web: `CE:1016`; the desktop keeps them, paints them the same way, i.e. not at all) |
| Sort A→Z / Z→A | `DEP:872` | ✔ |
| Number headings (1., 1.1) | `DEP:15305`, `CE:928` | ✔ |
| Paragraph shading and border | `DEP:10769`, paint `DEP:5314-5346` | ◐ shading (default, light grey, none) and the default border are set and painted; the colour palette and the border's colour/width/style are not offered |
| Paragraph flow: keep with next, keep lines, page break before | `DEP:10879` | ✔ |
| Widow control, contextual spacing, suppress line numbers, don't hyphenate (flow popover) | `DEP:10879` | ◐ contextual spacing and suppressed line numbers are read and laid out; the toggles for widow control, contextual spacing, suppress line numbers and don't-hyphenate are not on the desktop ribbon (widow control and hyphenation have no effect on the web either) |
| Styles gallery (Normal, Titre 1–4, Titre, Sous-titre, Citation, Sans interligne) | `DEP:10924`, `:11046` | ✔ |
| Edit style dialog, per-document `styles` | `DEP:12779`, `:15212` | ✘ (`styles` kept on save; no dialog) |
| Find, replace, replace all, case | `DEP:10410` | ✔ (whole word and regex: ✘) |
| Link (Ctrl+K), remove link | `DEP:12050`, `:15553` | ✔ |
| E-mail link | `DEP:15355` | ✔ |
| Remove all links | `DEP:15361` | ✔ |
| Bookmark | `DEP:15325` | ✔ |
| Go to (headings, bookmarks) | `DEP:4130` | ✘ (dialog) |
| Statistics / details dialogs | `DEP:4062`, `:4022` | ◐ word count in the status bar; no dialog |
| Text tools: empty lines, spaces, tabs, quotes, number, reverse, dedupe, title, clear all | `DEP:12061-12071` | ✔ |

### Insertion (Insert)

| Feature | Web | Desktop |
|---|---|---|
| Cover page (two variants) | `DEP:15376` | ✔ |
| Page break (Ctrl+Enter) | `DEP:5569` | ✔ |
| Section break (next page) | `DEP:5558` | ✔ |
| Table (grid), text ↔ table | `DEP:12093`, `:969`, `:987` | ✔ (« Insérer un tableau… » prompt: ✘) |
| Image (file, resized) | `DEP:15502` | ✔ |
| Shapes (144, draw to create), text box | `docs/shapes/`, `DEP:5711` | ✘ (the next lot: `kubuno-office-shapes-core` exists; drawing and editing on the page are not done) |
| Header / footer: display | `DEP:5296` | ✔ (rich, faded, fields, first page, per-section) |
| Header / footer: inline editing (double-click, context tab) | `DEP:5633`, `:12292` | ✘ (next lot; the bands are read-only on the desktop) |
| Page numbers: position | `DEP:14648` | ✔ |
| Page numbers: format | `DEP:10848` | ✔ (the first number, `pageNumStart`, is read but not settable) |
| Table of contents (insert, update) | `DEP:15083`, `:14846` | ◐ inserted with the defaults (the web opens its options dialog first) |
| Update fields | `DEP:15070` | ◐ updates the table of contents; imported `field` nodes are not recomputed (`docs/fields.ts:408` not ported) |
| Special characters panel, symbols | `DEP:10535` | ◐ the 24 symbols of the menu; no panel |
| Date, time, date+time (static text) | `DEP:15312` | ✔ |
| Caption (« Figure N : ») | `DEP:15317` | ✔ |
| Horizontal rule, code block | `DEP:15316` | ✔ |
| Footnote, endnote (insert, edit, delete; endnotes after the last content) | `DEP:15294`, `docs/endnotes.ts`, `CE:1743` | ✔ |
| Drop cap | `DEP:12148`, `CE:1979` | ✘ (layout not ported) |
| Signature line, « Page X sur Y » | `DEP:15364`, `:15371` | ✔ |

### Mise en page (Layout)

| Feature | Web | Desktop |
|---|---|---|
| Margins (presets, rulers) on the caret's section | `DEP:15348`, `:5542` | ✔ *(run)* — the base section's margins are not undoable on the desktop (Yjs map on the web) |
| Orientation of the caret's section | `DEP:5523` | ✔ *(run, real clicks)* |
| Paper size | `DEP:14682` | ✔ |
| Columns (base section) | `DEP:5577` | ✔ *(run, real clicks)* — no column guides on the ruler yet |
| Line numbers | `DEP:10728`, `:5436` | ✔ (the interval is kept, not settable) |
| Paragraph metrics box (indents, spacing in pt) | `DEP:10824` | ✘ (the rulers and the paragraph buttons cover it) |
| Page colour (section-aware), gradient | `DEP:10603` | ◐ cream, light grey or none; no colour picker; gradients ✘ |
| Watermark | `DEP:10631`, paint `:423` | ✔ (the six presets, or none; no colour/opacity editor) — painted on the desktop; **not visible on the web's screen** (`solidPageBg` paints an opaque page before the `destination-over` watermark, `DEP:7537`) |
| Page border | `DEP:10681`, paint `:448` | ◐ the four styles or none with the default colour, width and distance; no editor |
| Page Setup dialog (gutter, header/footer distance, vertical alignment, section start, odd/even, first page, default) | `DEP:3618` | ✘ (most of its settings have no effect on the web either: UI-only) |

### Références

| Feature | Web | Desktop |
|---|---|---|
| Table of contents gallery: automatic, manual, remove | `docs/references/ribbon.tsx:62`, `DEP:14897`, `:14949` | ✔ |
| Update the table (with its own settings) | `DEP:14846` | ✔ |
| TOC options / table style dialogs | `TocOptionsDialog.tsx` | ✘ |
| Ajouter le texte (outline level 0–3) | `outline.ts:90` | ✔ |
| Insert footnote / endnote, next / previous, show notes | `docs/references/ribbon.tsx:99-114` | ✔ |
| Citations, sources, citation style, bibliography | `SourcesDialog.tsx`, `DEP:15032-15038` | ✘ (`sources` and `citationStyle` are kept on save) |
| Caption, table of figures | `DEP:15317`, `:14963` | ◐ caption ✔; table of figures ✘ |
| Cross-reference | `CrossRefDialog.tsx` | ✘ |
| Mark index entry, index | `MarkEntryDialog.tsx`, `DEP:14972` | ✘ (`indexEntry` marks kept) |
| Mark citation, table of authorities | `DEP:14982` | ✘ (`citationEntry` marks kept) |

### Affichage (View)

| Feature | Web | Desktop |
|---|---|---|
| Edit / read mode | `DEP:14554` | ✘ |
| Ruler, formatting marks, text boundaries | `DEP:12197-12200` | ✔ |
| Navigation pane | `DEP:3083` | ✘ |
| Collapsible headings | `DEP:15545`, `CE:346` | ✘ |
| Zoom: 100 %, one page, page width, slider, Ctrl+wheel | `DEP:12208-12219` | ✔ |
| Zoom dialog, multiple pages | `DEP:3286`, `:13902` | ✘ (pages side by side happen by themselves below 50 %) |
| Macros (show, record, pause) | `DEP:12223` | ✘ (the web's `Kubuno.Doc` scripting API has no desktop runtime) |

### Révision (Review)

| Feature | Web | Desktop |
|---|---|---|
| Spelling (Hunspell), dictionary, language | `DEP:12316-12319`, `spellcheck.ts` | ✘ (needs a speller on the desktop) |
| Track changes: recording (typing, paste, Backspace/Delete, typing over a selection; own pending insertion really removed; one revision per run of keystrokes; a paragraph join stays a real deletion; Delete leaves the caret before the struck text, as on the web) | `docs/track-changes.ts:255-330` | ✔ `edit/track.rs` (`tracked_insert`, `tracked_delete`): same marks and attributes as the web; web-written fixture `fixtures/web/track.json` |
| Revision ink (author colour), underline / strike, change bar in the margin | `CE:374-417`, `CE:2585-2680` | ✔ `paint.rs` (`track::author_color` = `authorColor`) |
| Accept / reject one or all; « Afficher le document final » (deleted text leaves the layout) | `track-changes.ts:560-620`, `CE:331-417` | ✔ `track::decide`, `Editor::set_final_view` |
| Review pane (cards: author, relative age, kind, text; accept / reject; accept all / reject all with confirmation) | `docs/ReviewPane.tsx` | ◐ a dialog walking the revisions one at a time (Précédente / Suivante) instead of a side pane: the framework has no list pane yet |
| Comments: new, gutter, reply, resolve | `DEP:15185`, `:12631` | ⛔ comments live in the web's Yjs map, not in the stored document (the envelope's `comments` is a mirror the web never reads back): a desktop comment would not reach the web — see the collaboration question in §5 |

### Contextual tabs

| Feature | Web | Desktop |
|---|---|---|
| Table: insert/delete rows and columns, merge/split, style, shading, alignment, borders, sort, distribute, autofit, split table, formula, properties dialog, hover bands | `DEP:12336-12379`, `:4249`, `:6088` | ✘ (no table context tab yet; tables are laid out, painted and edited as text) |
| Image: align, wrap, reset, layout dialog, handles | `DEP:12255-12268`, `:3748` | ✘ (images are inserted, resized at insertion and painted; no image tab) |
| Shape: fill, outline, wrap, order, rotate, adjust handles | `docs/shapes/ribbon.tsx:91` | ✘ (with the shapes lot) |
| Header & footer: fields, go to, first page, link to previous, close | `DEP:12292-12307` | ✘ (with inline header/footer editing) |

### Outside the ribbon

| Feature | Web | Desktop |
|---|---|---|
| Keyboard (typing, AltGr, dead keys, Enter, Backspace/Delete, Tab, arrows, Home/End, Page Up/Down, Ctrl shortcuts) | `DEP:6886-6994` | ✔ (typing *(run)*) |
| IME composition | TipTap hidden view | ◐ the composition window is placed at the caret; not verified with a real Japanese/Chinese IME (none installed on the test PC) |
| Mouse: click, double/triple click, drag, drag-move of text | `DEP:8643`, `:8296` | ◐ clicks and drag-select ✔; drag-and-drop of the selection ✘ |
| Context menu | `DEP:9287` | ◐ the text items; spelling, comment, object and table items ✘ |
| Floating mini toolbar | `DEP:1727` | ✘ |
| Status bar: page X of Y, words, language, zoom | `DEP:13182` | ◐ page, zoom, save state; words via the ribbon; language ✘ |
| Rulers: margins, indents, tab stops | `DEP:2378-3062` | ✔ *(run)* |
| Autocorrect (`(c)→©`, `->→→`, `1/2→½`…), markdown input rules | `DEP:1284-1291` | ✘ |
| Collaboration: live co-editing, remote carets | `useCollab`, `RemoteCursors` | ⛔ the desktop saves the whole document through REST; the web edits a Yjs room (see §5) |
| Accessibility (UI Automation) | — | ◐ the system caret follows ours (screen readers, magnifier); no `TextPattern`: the framework publishes AccessKit nodes with a role, a name and a value, without text runs or a text selection, and the page canvas publishes none — a framework lot (AccessKit text runs) comes first |

## 4. Later: the web on the same crate (WASM)

The web keeps `canvas-engine.ts` for now. Switching it to `kubuno-office-docs-core` compiled to WASM:

* **API surface** (`wasm-bindgen`, behind a `wasm` feature): `Engine::new(doc_json)`, `set_doc(json)` /
  `apply_steps(steps_json)` (ProseMirror steps from `tr.steps` mapped to `Replace` on paths, or simply `set_doc` per
  transaction with the per-block cache keeping it incremental), `layout(widths, geoms) -> handle`,
  `pages() -> PageLayout[]` (serialised with the same field names as the TypeScript interfaces: `spans`, `x`,
  `width`, `pmPos`, `y`, `height`, `ascent`, `baseline`, `pmStart`, `pmEnd`, `table`…), `posToCoords`,
  `coordsToPos`, `selectionRects`, `wordBoundariesAt`, `paragraphBoundariesAt`, `lineStartAt`, `lineEndAt`,
  `prevWordPos`, `nextWordPos`, `adjacentLineCenter`. `paintLayout` stays in TypeScript (Canvas 2D), reading the same
  structures — only the layout moves.
* **Measurer bridge**: the core calls `Measure` for every token. Across the JS boundary that must be batched: the
  core collects the distinct `(font, text)` pairs it has not cached, asks JS once per layout pass
  (`measureBatch(fonts[], texts[]) -> Float32Array`), then lays out from its cache; font metrics
  (`fontBoundingBoxAscent/Descent`) are asked once per font string. The font string is built by the core exactly as
  `fontStr()` does, so the browser's cache keys match.
* **Size and speed** (estimates to confirm when built): ~300–500 KB of WASM with `opt-level = "s"` and LTO
  (serde_json dominates), ~120–200 KB gzipped. Layout of a 50-page document is dominated by measurement; with the
  batch bridge and the per-block cache, a keystroke re-lays out one block — well under a frame.
* **Fidelity checks**: run the existing parity harness (`layout/parity.rs`, recorded from the real
  `canvas-engine.ts` in Chrome) against the WASM build with the browser's `measureText` — line tops, line contents
  and span `x` must match within 0.5 px; then the 337-document corpus: page count and the `pmStart/pmEnd` of every
  line identical. Known divergences (the horizontal-rule position bug above) are fixed on the web side first, or the
  harness records them explicitly.

## 5. Log

**2026-10-02 — A, B, C, D (first pass).** `kubuno-office-docs-core`: 247 tests, clippy `-D warnings` clean, `cargo check`
for `wasm32-unknown-unknown`, `x86_64-unknown-linux-gnu` and `aarch64-apple-darwin`. `kubuno-documents`: 128 tests
(the save session on its worker thread against an in-memory server and journal: Ctrl+S saves, a server-side change
raises the conflict instead of overwriting, « take theirs » reloads, offline opens the journal and keeps it on close,
413 stops saving, the session leaves exactly once). Real runs (`--zoom`, posted input, never the foreground; captures
in `C:\kbuild-docs-edit\out`): typing a paragraph (`typing-light.png`, `typing-dark.png`), a selection across a page
break (`d1-select-pages.png`), Ctrl+B on it (`d2-bold.png`, `bold-light.png`, `bold-dark.png`), a bullet list
(`list-light.png`, `list-dark.png`), undo (`d3-undo.png`), a ruler indent drag written to the document and undone
(`r-indent-drag.png`, `r-indent-undo.png`); the ruler comparison board redone (`rulers-web-vs-desktop-v2.png`: web
and desktop at 100 % and 50 % side by side, desktop dark at 100 % and 50 %).

Not verified live: **the server path** (`--doc <id>`). No development core with the office module was available on
this machine (the dev database was being moved by another agent), so open/save ran only against the in-memory server
of the tests — never against the live server, and never with real credentials. IME composition (posted messages
cannot drive an IME), the ribbon by mouse (see §3), and a UIA `TextPattern` (only the system caret is exposed) remain
to be checked by hand.

**2026-10-07 — server path, page layout, references, review (office `5b12e58`…`e7e0a61`).** Verified live against
an own development core (office + drive modules, own database, own ports) and the web editor in headless Chrome
(captures in `C:\kubuno-build\release\documents\captures`):
- open fidelity web vs desktop; an edit saved with Ctrl+S visible on the web (for a document whose Yjs room is empty —
  see the question below); a concurrent web save → 412 → the conflict dialog (`conflict-dialog-1.png`); the crash
  journal offered on reopen (`recover-dialog.png`); a 2 MiB document → 413 → a terminal status, no retry loop
  (`limit-413-status.png`); offline edits kept in the journal, shown on an offline open (`offline-3-open-offline.png`),
  saved once the server is back.
- ribbon by real mouse clicks (under the shared input lock): Mise en page orientation / columns, Ctrl+S, bold,
  Révision › Suivi des modifications, Afficher le document final, Volet Vérifications (Suivante, Accepter, Tout refuser
  with its confirmation). Posted clicks do not reach the ribbon: the host's `WM_LBUTTONUP` → `track_leave` → an
  immediate `WM_MOUSELEAVE` (the real cursor is elsewhere) moves the pointer away before the release is painted.
- track changes against a document tracked **by the web** (`fixtures/web/track.json`): same ink, strike, underline
  and change bars (`web-track.png` vs `desk-track-1.png`); typing / Delete tracked on the desktop and saved as the
  same marks; final view; review dialog; accept and reject (`desk-track-*.png`).
- a modal dialog opened by a ribbon command during the frame lays out against a half-built surface (first control
  and action bar missing): every command-opened dialog is now deferred with `begin_invoke` (framework bug, not fixed
  in core/desktop).

Performance, release build (static CRT), 40-page document (420 paragraphs), window 1500×1000 at 175 %: open to idle
1.2 s; typing (sent `WM_CHAR` + synchronous paint) median 38.8 ms, p95 44.8 ms; wheel scrolling (sent notch +
synchronous paint) median 37.7 ms, p95 59.8 ms. Engine bench (`examples/typing_bench.rs`, 50 pages): edit 2.0 ms +
relayout 4.8 ms per keystroke (was 5.4 + 12.9).

Tests: `kubuno-office-docs-core` 282 + 5 web-fixture tests (also on Linux, .220), clippy `-D warnings` clean;
`kubuno-documents` 33; `kubuno-office-desktop-common` checks on Linux.

Not done, with the reason: IME (no Japanese/Chinese IME on this machine; installing one changes the user's system),
UIA `TextPattern` (the framework's AccessKit nodes carry no text runs or selection), comments (they live in the web's
Yjs map, see the question), header/footer inline editing, drop cap, shapes and text boxes, the table contextual tab,
spelling — see §3 for every row. Open questions: (1) the web seeds a document from `content_json` only when its Yjs
room is empty, so a desktop (or Android) REST save of a document already opened on the web is overwritten by the
room — reseed when `content_etag` differs, make the desktop a Yjs peer, or have the core reset the room on a REST
PATCH? (2) a fresh dev core fails its migrations (`uuid_generate_v4()`): the extensions are created in schema `core`.
(3) the web's watermark is invisible on screen (opaque page ground painted before the `destination-over` watermark,
`DEP:7537`).
