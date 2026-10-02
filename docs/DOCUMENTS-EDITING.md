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

## 1. Shared code: the platform-neutral crate `kubuno-docs-core`

User direction (2026-10-02): share the algorithms with the web rather than write them once per platform. Everything
that is not drawing, input or windowing lives in **`desktop/common/kubuno-docs-core`** (name per the
`kubuno-<domain>-core` convention of the cross-module study), a member of the `desktop/common` workspace like
`kubuno-sync`, depended on by path from `documents`. It has no Windows, Direct2D, DirectWrite or `kubuno_ui`
dependency (only `serde`, `serde_json`) and is checked with `cargo check --all-targets` for `wasm32-unknown-unknown`,
`x86_64-unknown-linux-gnu` and `aarch64-apple-darwin`, like `kubuno-views-syntax`.

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
core's cross-target `cargo check`, real runs with captures in light and dark, then `dist\kubuno-documents.exe` and its
`kubuno_ui-<hash>.dll` refreshed.

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
  through `kubuno-sync-engine` (outbox, feeds) is a later increment: the document is one opaque blob of up to 2 MiB,
  not rows, and the conflict rule above (digest, owner-only sessions) is specific to it.
* Tests against the dev core and dev database only (`KUBUNO_DEV_DATABASE_URL`, `docs/WEB.md` §6–7); if the office
  module cannot run locally, save is tested against a local mock and open read-only against the live server, and the
  report says so.

## 3. Command coverage

✔ wired and verified · ◐ partial · ✘ not done (the model or the desktop lacks it). "Verified" = a unit test in
`kubuno-docs-core` mirroring the web's command, plus a real run for the ones marked *(run)*. Clicks posted by the test driver
(`PostMessage`, so as not to take the user's pointer) did not reach the ribbon's commands — most likely its hover
tracking, which sees the real pointer elsewhere; not investigated further — so ribbon commands were run through their
shortcuts, which execute the same `OnExecute` (`host::take_key` → `Command`).

| Group | Commands | State |
|---|---|---|
| Clipboard | Couper, Copier, Coller, Coller sans mise en forme (Ctrl+Shift+V), images collées redimensionnées | ✔ |
| Clipboard | Historique du presse-papiers | ✘ (no desktop clipboard history yet) |
| History | Annuler / Rétablir (Ctrl+Z, Ctrl+Y, Ctrl+Shift+Z), groupement 500 ms comme Yjs | ✔ *(run)* |
| Font | Gras *(run)*, italique, souligné, barré (Ctrl+Shift+S), indice, exposant, police, taille, A+ / A−, casse (maj., min., titre, phrase, inverser), petites majuscules, couleur (auto, rouge, bleu), surlignage (jaune, vert, cyan, aucun), effacer la mise en forme, reproduire la mise en forme | ✔ |
| Font | Espacement des caractères, « Autres couleurs » | ✘ (no picker dialog yet) |
| Paragraph | Puces (Ctrl+Shift+8) *(run)*, numéros (Ctrl+Shift+7), cases à cocher, alignements (Ctrl+L/E/R/J), retrait −/+, interligne 1–3, espace avant/après, première ligne, suspendu, enchaînements (paragraphes solidaires, lignes solidaires, saut de page avant), afficher ¶ | ✔ |
| Paragraph | Tri, numérotation des titres, trame, bordures, veuves et orphelines | ✘ |
| Styles | Galerie (Normal, Titre 1–3, …) via `apply_named_style` | ✔ |
| Edit | Rechercher (Ctrl+F), Remplacer / Tout remplacer (Ctrl+H), casse, Tout sélectionner, nombre de mots | ✔ |
| Edit | Atteindre, outils de texte (lignes vides, espaces, tabulations, guillemets…) | ✘ |
| Insert | Saut de page (Ctrl+Enter), tableau (grille), image (fichier, redimensionnée), trait horizontal, bloc de code, lien (Ctrl+K, boîte Kubuno) / supprimer le lien, symboles (24) | ✔ |
| Insert | Saut de section, formes, zone de texte, en-tête/pied, table des matières, champs, légende, citation, notes, lettrine, signature, page de garde, texte↔tableau, signet, lien courriel | ✘ (structures the layout draws but the editor cannot create yet) |
| Layout | Marges (presets) and the rulers: margins, indents, tab stops written to the model, undoable *(run)* | ✔ |
| Layout | Orientation, taille, colonnes, couleur de page, filigrane, bordure de page | ✘ (bound in the ribbon, not written) |
| References | Notes, citations, bibliographie, tables, index | ✘ |
| View | Règle, ¶, zoom 100 % / une page / largeur, curseur de zoom, pages côte à côte | ✔ |
| View | Lecture, navigation, macros | ✘ |
| Review | Orthographe, suivi des modifications, commentaires | ✘ |
| File | Enregistrer (Ctrl+S): local file in place, or the server session (§D) | ✔ (server path: mock-tested, see §5) |

## 4. Later: the web on the same crate (WASM)

The web keeps `canvas-engine.ts` for now. Switching it to `kubuno-docs-core` compiled to WASM:

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

**2026-10-02 — A, B, C, D (first pass).** `kubuno-docs-core`: 247 tests, clippy `-D warnings` clean, `cargo check`
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
