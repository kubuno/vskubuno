# Kubuno Spreadsheets (desktop) — design note

Status: design of record for the native desktop version of the office module's spreadsheet editor
(`kubuno-spreadsheets.exe`), written before the app code (mission of 2026-10-07: « the desktop app must fully
catch up with the web version — every feature of the web Spreadsheets editor, not a subset »). It follows the
method of `DOCUMENTS-EDITING.md`: the web editor is the behavioural and visual reference, its engine is ported
into a platform-neutral shared core (`kubuno-office-sheets-core`), the desktop app is the portable app of
`desktop/common` plus the Win32 window of `desktop/windows`. Section 7 (coverage) and section 9 (log) are kept up
to date as increments land.

Paths are relative to the office repository (`office/`) unless stated. `SA` is `web/src/SpreadsheetApp.tsx`
(8 954 lines), `FE` is `web/src/formula-engine.ts` (1 611 lines).

## 1. What the web editor does

### 1.1 Shape of the code

| Web file | Role |
|---|---|
| `FE` | tokenizer, recursive-descent parser, evaluator (references, ranges, cross-sheet, `A1#` spills, LET/LAMBDA, defined names, cycle detection), the engine's 73 own functions, number/date display (`formatCode`, `formatValue`), `translateRefs`, conditional formatting (`computeCondFormats`), dynamic-array spills (`computeSpills`) |
| `formula-fns/*.ts` (12 files, ~7 000 lines) | ~424 more functions: math, stat, stat2, text, date, logical, lookup, financial, financial2, engineering, engineering2, misc |
| `formula-catalog.ts`, `fn-wizard/*`, `FunctionBrowserDialog.tsx` | function catalogue (categories, French syntax strings), function wizard |
| `formula-refs.ts`, `formula-edit.ts`, `FormulaInput.tsx`, `sheet-formula-bar/*` | reference colouring, F4, auto-pair, autocomplete ranking, the formula bar and Name Box |
| `fill-series.ts`, `data-validation.ts`, `pivot-engine.ts`, `cfIcons.ts` | fill handle series, data validation, pivot tables, conditional-format icons |
| `SA:607-8440` `SpreadsheetEditor` | state, canvas grid renderer (`SA:4817-5303`), selection overlay (`SA:5309-5466`), hit testing, mouse/keyboard, cell editor, clipboard, fill, undo/redo, ribbon, dialogs, collaboration (Yjs), encryption, macros API |
| `SA:8441-8954` `SpreadsheetApp` | list pages, backstage, the shell around the editor |
| `sheet-chart/*`, `sheet-shape/*`, `sheet-object/*` | charts (SVG), shapes (shared 144-shape engine), pictures, equations (KaTeX), object menus/dialogs |
| `ConditionalFormatDialog`, `DataValidationDialog`, `NameManagerDialog`, `SortDialog`, `PivotDialog`, `SheetProtectDialog`, `WorkbookEncryptDialog`, `PrintDialog` (+ `sheetPrint.ts`) | the dialogs |
| `macros/*` | macro runtime (JavaScript `new Function`), IDE, forms |

### 1.2 Feature inventory

The inventory (116 user-visible features, each with its web file:line) is the coverage table of section 7; the
notes below are the facts that drive the port.

- **Evaluation is on demand** (`FE:387-410`): a reference to a formula cell re-parses and re-evaluates it (no
  memo, no dependency graph); a cell met again while it is computed reads `#REF!` (`FE:36`, circular = `#REF!`);
  nested evaluations do not see the spill index (`FE:401-402`). The visible grid evaluates each cell at paint
  (`SA:362`, `SA:4855`).
- **Values**: number, string, boolean, error; an empty cell is `''`; a non-empty numeric string in `v` is a number
  (`FE:408`, JavaScript `Number()` rules: `" 12 "`, `"0x1F"`, `"1e3"`); display = `String(round(x·1e10)/1e10)`
  (`FE:1597`). Comparison: numbers numerically, any boolean as 0/1, everything else as lower-cased text
  (`FE:497-509`: `=A1=0` on an empty cell is FALSE).
- **Grammar** (`FE:76-260`): `.` decimals only, `,` and `;` both separate arguments, `{1,2;3,4}` arrays, `'Sheet'!A1`,
  `A1#`, `LAMBDA(x,x)(5)`; no whole-column `A:A`, no intersection/union, no `#N/A` literals. QUIRKS kept:
  `""` inside a string ends it (`FE:85`); a name of letters + digits (`LOG10`, `ATAN2`, `DAYS360`, a defined name
  `FACT2`) tokenizes as a cell reference, so `LOG10(100)` is `#CALC!` (`FE:113`); `1.2.3` reads 1.2.
- **Stored cell** (`api.ts:197`): `{ v?, f?, s?, c?, link? }`; typed input (`SA:3328-3361`): `=…` → `f` (after
  `autoBalance` + `normalizeFormula`), a `Number()`-readable text → number, else string; empty → `v: null`. No
  decimal comma, no `%`, no date parsing on input.
- **Styles** (`api.ts:145-172`): bold/italic/underline/strike, `fontSize` (px), `fontFamily`, `color`, `bg`,
  `bgGradient`, `align`, `valign` (no UI), `wrap`, `numFmtCode` (imported only), `numFmt`
  (number/currency/percent/scientific) + `decimals` + `thousands` (Intl, EUR, app language), borders per edge
  (`bt br bb bl`, widths `btw…`). Effective style = column style < row style < cell style (`SA:928`).
- **Grid** (`SA:243-248`): 16 384 × 1 048 576, default column 100 px / row 24 px (per-sheet overrides imported),
  headers 46 × 24, zoom 25–300 % (status bar), frozen panes, merges, hidden rows/columns (size 0), outline groups,
  filter funnels, gridlines toggle. **The grid is always light**: every colour is a literal (`#e2e4e6` gridlines,
  `#f8f9fa` headers, `#d3e3fd` selected header, `#1a73e8` selection, `#202124` text) and the office module has no
  dark theme; only the DOM chrome (ribbon, bars) follows the host theme.
- **Persistence** (`SA:3141-3326`, `api.ts:509-565`): a debounced `PATCH /spreadsheets/:id/sheets/:sid
  {data: {cells, colStyles, rowStyles}}` (autosave `office.spreadsheet_autosave_s`, default 30 s, 0 = off), flushed
  on hide/unload; dimensions, merges, freeze, gridlines, cf, validations, groups, pivots, protection and objects
  are PATCHed immediately. Cells are also mirrored to a Yjs room (`office-spreadsheet:<id>`) when the WebSocket
  is up; defined names live **only** in the Yjs map `names` (no REST route writes them).
- **Undo** (`SA:3203-3247`): snapshots of `{cells, colStyles, rowStyles, merges, colW, rowH, fr, fc}`, 200 entries,
  reset on sheet switch; cf, validations, objects not covered.
- **Clipboard** (`SA:4124-4192`): in-memory only (no OS clipboard), formulas translated by a regex
  (`translateFormula`, not string-aware: QUIRK).

## 2. Stored format

One gzipped JSON file per workbook in Drive (`.kbcal`, `application/vnd.kubuno.spreadsheet+json`, folder
`Office/Spreadsheets`): `{ "version": 1, "sheets": { "<sheet uuid>": SheetData }, "names": { "NAME": "=…" } }`
(`server/src/services/content_files.rs:246-294`). Sheet names and order are **not** in the file: they are rows of
`office.spreadsheet_sheets` (`name`, `position`). `SheetData` keys (snake case on disk): `cells`, `col_widths`
(by letter), `row_heights` (by row number), `frozen_rows`, `frozen_cols`, `merges` (`"A1:B2"`), `gridlines`,
`default_row_height`, `default_col_width`, `col_styles`, `row_styles`, `cf`, `validations`, `row_groups`,
`col_groups`, `images`, `equations`, `charts`, `shapes`, `pivots`, `protection`, `enc`, and import-only `hidden`,
`tab_color`, `auto_filter`, `print_area`, `print_titles`.

The server reads and writes the file with `serde_json` (no `preserve_order`): keys are sorted, numbers are
serde_json's. The core therefore keeps the content as `serde_json` values (`float_roundtrip`) and models only what
it edits (cells as `{v, f, s, c, link, rest}` with every field kept as read): a workbook written back unchanged is
byte-identical (golden test).

A **local file** is either that content file (`.kbcal` gzipped or plain JSON; sheets are then named
« Feuille 1… » in key order) or the desktop's bundle `{ "spreadsheet": {…}, "sheets": [meta…], "content": {…} }`
(what the crash journal holds: names and order included).

## 3. API (all under `/api/v1/office`)

| Route | Use |
|---|---|
| `GET /spreadsheets/:id` | `{ spreadsheet, sheets: [{id, name, position}] }` (owner or collaborator) |
| `GET /spreadsheets/:id/sheets/:sid` | `{ sheet, data, names }` — `data` is that sheet's `SheetData` |
| `PATCH /spreadsheets/:id/sheets/:sid` | `{ name?, data?: {cells, colStyles?, rowStyles?}, col_widths?, row_heights?, frozen_rows?, frozen_cols?, merges?, gridlines?, images?, equations?, charts?, shapes?, cf?, validations?, row_groups?, col_groups?, pivots?, protection?, enc?, clear_enc?, position? }` — read-modify-write of the whole content file; **no precondition** (no etag, no If-Match) |
| `POST /spreadsheets/:id/sheets` | `{ name?, id? }` (client-minted id honoured) |
| `DELETE /spreadsheets/:id/sheets/:sid` | refused for the last sheet |
| `POST /spreadsheets/:id/editing/join` · `ping` · `DELETE leave` · `POST save` | the draft-file editing session (**owner only**), like documents |
| `GET/POST /spreadsheets/:id/versions`, `GET /spreadsheets/:id/export/{xlsx,ods}`, `POST /spreadsheets/:id/save-source`, `POST /spreadsheets/open-by-file` | versions (no restore route), export, save back to the imported file, import |
| `GET /api/v1/config` (core) | `office.spreadsheet_autosave_s` |

Server facts that shape the save path (same family as documents, `office-documents-desktop` notes): a PATCH writes
the **draft** when one exists and mirrors it to the main file (`write_content_mirrored`); `leave` promotes the
draft and deletes it when the last editor leaves; nothing protects a write against a concurrent one — the
desktop's precondition is a **digest of the whole workbook re-read right before each save** (§5.3); the request
body is capped at 2 MiB by axum (413, terminal for that sheet).

## 4. The shared core: `common/sheets-core` (`kubuno-office-sheets-core`)

Platform-neutral (serde, serde_json, regex; no UI, no OS API), checked for `wasm32-unknown-unknown`,
`x86_64-unknown-linux-gnu` and `aarch64-apple-darwin`. It never reads a clock or a random source: `formula::Env`
gives `NOW()`/`TODAY()` the local time and `RAND()` its numbers.

| Module | Content | Web origin |
|---|---|---|
| `js` | JavaScript's number and string semantics: `Number()`, `parseFloat`, `String(x)`, exact `toFixed`/`toPrecision`/`toExponential`, `Math.round`, UTF-16 lengths and slices | (the TypeScript runtime) |
| `jsdate` | JavaScript `Date` without a zone (local wall clock as ms), `MakeDay`/`TimeClip`, years 0–99, Excel serials (`excelSerial`, `serialToDate`) | `FE:793-812` |
| `addr` | column letters, `A1` keys, rectangles | `FE:53-62`, `SA:311-329` |
| `model` | the workbook: sheets (meta + `SheetData` kept losslessly), cells, styles (effective style), merges, sizes, frozen panes, groups, names; load from the API, from a content file, from a bundle; write back | `api.ts:120-460` |
| `formula` | `parser` (tokenizer, parser, AST), `value` (scalars, coercions), `eval` (evaluator, memo, depth limit), `criteria`, `spill`, `functions/*` (one module per web file, registered in the web's order, the engine's block last) | `FE`, `formula-fns/*` |
| `format` | `formatCode`/`formatDateSerial`/`formatNumberCode`, `TEXT()`'s format, Intl's `NumberFormat` for the ribbon formats (French, English) | `FE:813-896`, `SA:350-360` |
| `cf` | conditional formatting → per-cell style, data bar, icon | `FE:966-1513`, `cfIcons.ts` |
| `refs` | `translateFormula` (paste/fill), `shiftRefs` (insert/delete), `translateRefs` (CF), reference colouring, F4 cycling, auto-pair, `autoBalance`, `normalizeFormula` | `SA:105`, `SA:6773`, `FE:934`, `formula-refs.ts`, `formula-edit.ts` |
| `fill` | the fill-handle series | `fill-series.ts` |
| `validation` | data validation | `data-validation.ts` |
| `pivot` | pivot tables | `pivot-engine.ts` |
| `sheet` | grid geometry (column lefts, row tops, hidden, zoom, frozen bands), hit testing, display text of a cell, selection aggregates | `SA:2584-2760`, `SA:293-375` |
| `edit` | the editing operations (cells, styles, merges, structure, sort, fill, clipboard, find/replace, …) and the undo history (the web's snapshots) | `SA` handlers |

Two deliberate additions over the web, each invisible in results:

- **a memo** per recalculation: a referenced formula cell is computed once while the data is unchanged; a result
  is remembered only when its computation met no circularity and no depth limit, the only ways the web's value
  depends on the path that reached it. (The web is exponential on diamond-shaped dependencies.)
- **a depth limit** (`MAX_DEPTH` = 1 000 nested evaluations): the web catches the JavaScript stack overflow of a too
  deep chain and shows `#VALUE!`; native code would abort. The desktop evaluates on a thread with a large stack.

**Golden vectors.** `tools/golden/harness.ts` runs the **web's own engine** (`web/src/formula-engine.ts`, through
`jiti` on the Linux host, `TZ=UTC`, clock fixed at 2026-10-07 14:30:15.250) over `tools/golden/cases/*.json`; the
expected values are committed in `tests/golden/*.json` and `tests/golden.rs` checks the Rust engine against every
one (numbers within 1e-11 relative, everything else identical).

## 5. The desktop app

### 5.1 Crates and layout (the platform rule: common = complete app, OS folders = overrides)

| Crate | Folder | Role |
|---|---|---|
| `kubuno-office-sheets-core` | `common/sheets-core` | the engine (§4) |
| `kubuno-office-spreadsheets-common` | `desktop/common/` | the complete portable app: launch options (`--doc <id>`, a file, `--dark`, `--culture`, `--zoom`, `--sample`), the open workbook's view state (active sheet, selection, scroll, zoom), the server path (routes, the save rules, journal, the live thread — the session state machine of `kubuno-office-desktop-common` reused, §5.3), the text interface, the platform extension points |
| `kubuno-office-spreadsheets` | `desktop/windows/` | `kubuno-spreadsheets.exe`: the window (`.kbview`, declarative ribbon, backstage, formula bar, sheet tabs, status bar), the `GridCanvas` custom control (Direct2D), dialogs, clipboard |

Linux/macOS entry points join when the framework has a portable renderer (as for Documents).

### 5.2 The window

`spreadsheet_window.kbview`, `TitleBarStyle="Tall"` with the shell's waffle/account header (`ShowWaffle`,
`ShowAccount`), the ribbon (`Tone="Spreadsheets"` if the framework has it, else the accent `#0f7b3f` of
`ribbon/officeThemes.ts:22`), tabs as the web's: Fichier (backstage: Accueil, Informations, Exporter, Imprimer,
Fermer), Accueil, Insertion, Mise en page, Formules, Données, Révision, Affichage, and the contextual object tabs.
Under the ribbon the formula bar (Name Box, ✕ ✓ fx, the formula field), the grid (`GridCanvas`, painting the
web's renderer: headers, gridlines, fills, borders, text with overflow and wrap, merges, CF bars and icons,
validation marks, comment marks, frozen dividers, selection with fill handle), the sheet tabs (36 DIP) and the
status bar (cell, size, aggregates, sheet n/N, zoom). The grid is light in both themes, as on the web; the chrome
follows the theme.

### 5.3 Open and save

- Open: a local file (§2) or `--doc <id>` through the shell's token broker (sandbox profile under
  `KUBUNO_SANDBOX_DIR`; never a password): `GET /spreadsheets/:id` then each sheet.
- Save: `kubuno-office-desktop-common::api::{session, live}` are generic over a `Server` + `Journal` host; the
  spreadsheet host implements them: `fetch` reads the workbook (meta + every sheet) into a canonical bundle (its
  digest is the precondition), `patch` sends, per sheet, only what changed since the server copy just fetched
  (`data` = cells + column/row styles as the web sends them, each other key as its own field; sheets added,
  renamed, reordered, deleted through their routes), `join`/`ping`/`leave` the spreadsheet editing session,
  `config` maps `office.spreadsheet_autosave_s`. The journal is `<data>/spreadsheets/journal/<id>.json` (the
  bundle). Conflict → Kubuno `ConfirmDialog` (keep mine / take theirs), 413 → terminal, offline → journal kept.

## 6. Increments

| | Content |
|---|---|
| 0 | this note; the core skeleton (js, jsdate, addr, value, parser, evaluator, engine block, spills, format) with golden vectors |
| 1 | the function files (parallel ports, each checked by golden vectors), CF, refs, fill, validation, pivot, sheet geometry, workbook model and round-trip |
| A | the app read-only: window, ribbon declared, grid painted at parity, sheet tabs, formula bar (read), status bar; local file + `--doc` |
| B | editing: selection and keys, cell editor and formula bar (autocomplete, point mode, F4), styles, number formats, borders, merge, structure, clipboard (internal + OS text), fill handle, sort, filter, freeze, gridlines, find/replace, CF/DV/sort/name dialogs, undo/redo |
| C | save: the session (digest, join/leave, conflict, 413, journal) |
| D | objects and the rest: charts, shapes, pictures, pivots, goal seek, protection, print, export… (§7) |

## 7. Coverage

✔ done and verified · ◐ partial · ✘ not done (reason) · ⧗ planned in a later increment. Filled as increments land.

(The table is written in the log of each increment; the first version lands with increment A.)

## 8. Risks and open questions

- **Defined names cannot be saved by REST**: the web writes them only to the Yjs room; `UpdateSheetDto` has no
  `names`. The desktop reads them (`GET sheet` returns them) and the Name Manager edits them in memory; persisting
  needs either a Yjs client in the desktop or a server route (question for the user).
- **Collaboration (Yjs)**: the web mirrors cells to a Yjs room; a desktop save through REST is seen by web peers
  only after their next load (the web does not subscribe to REST changes). A Yjs client in Rust (`yrs`) is the
  way to real-time parity; not in this lot.
- **Macros** run as JavaScript in the browser; the desktop has no JS engine (a QuickJS embedding would be a C
  dependency in the Windows crate). Equations are rendered by KaTeX (JavaScript): the desktop shows their LaTeX
  source in the box until a native math renderer exists.
- **Encrypted workbooks** (Agile AES-256 / SHA-512, Web Crypto): portable Rust implementations exist (`aes`, `sha2`),
  not in the core's dependency set yet.
- The web's own bugs are ported as quirks (results must agree across clients); they are listed in §1.2 and in the
  code (`QUIRK`), to be fixed on both sides together.

## 9. Log

**2026-10-07 — increment 0.** Core skeleton: `js`, `jsdate`, `addr`, `model::{cell, style}`, `formula::{parser,
value, eval, criteria, spill, functions::base}`, `format`; 43 unit tests; the golden harness runs the web engine on
the Linux host and the first vectors match.
