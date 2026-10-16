# Kubuno Data (desktop) - design note

Status: design of record for the native desktop version of the Office module's **Data** sub-application
(`kubuno-data.exe`), started 2026-10-11. User goal: the desktop app catches up with the **whole** web Data
editor, not a subset. Method: the one of Kubuno Documents (`DOCUMENTS-EDITING.md`) and Diagrams
(`DIAGRAMS-DESKTOP.md`): a platform-neutral core crate that ports the web's pure logic, a portable desktop app
in `office/desktop/common`, and only the Win32 window and its drawing in `office/desktop/windows`. §8 is the
as-built log and §9 the feature coverage table, kept up to date as the increments land.

Reference for behaviour and looks: **the web editor**, `office/web/src/DataReportEditor.tsx` (1 396 lines),
`DataApp.tsx` (698), `DataCharts.tsx` (150), `data/visuals.tsx` (953) and the pure helpers in `data/*.ts`.
Every file:line below is in `office/web/src` unless stated.

> ⚠️ **Data is not a file document like the other Office apps.** It is a server-coupled BI tool (Power
> BI-like). A report is a row of `office_data.reports` with its pages and widgets in `office_data.*`; the
> `.kbdrp` Drive file is a **best-effort mirror / pointer** (`server/src/handlers/data_reports.rs:98-110`),
> created with empty `pages`/`widgets` and **never kept in sync** with edits - the DB is the source of truth.
> Every edit is its own REST call (auto-save), so the Documents save method (content digest, `PUT …/data`,
> 2 MiB cap, join/leave edit sessions, offline journal) **does not apply**. This is the central divergence;
> it drives §2.2, §3 and the open question in §6.

## 1. What the web app does

### 1.1 Files

| File | Role |
|---|---|
| `DataApp.tsx` (698) | shell: list vs editor routing (`:33-51`), ModuleHome landing + start content (recents/browse/new, `:55-133`), editable title (`:154-178`), backstage « Fichier » (`:185-213`), macro read-only API (`:216-229`), Rapport/Données/Modèle view strip (`:239-314`), SQL query editor (`DataQueryEditor :363-580`), semantic model view (`DataModelView :584-675`) |
| `DataReportEditor.tsx` (1 396) | the report editor: edit history by **inverse ops** (`useEditHistory :56-59, :142-255`), keyboard shortcuts (`:357-374`), clipboard/clone/z-order/align/lock (`:383-407`), theme + palette repaint (`:411-425`), page CRUD mutations (`:293-348`), dockable panes (`:442-472`), canvas (`CanvasSurface :594-645`, `CanvasWidget :681-771`), slicer cross-filter (`:605-614, :650-662`), widget data hook (`useWidgetData :664-679`), panes Fields/Visualizations/Wells/Format/Filters (`:854-1229`), pages bar (`:831-848`), context menus (`:1237-1277`), the ribbon (`useDataRibbon :1283-1396`) |
| `DataCharts.tsx` (150) | legacy in-cell charts + `WidgetRenderer` loading/error states (`:132-150`) |
| `data/visuals.tsx` (953) | the catalogue `VISUALS` (38 types, `:43-90`), `renderVisual` (`:905-947`), `STATIC_VISUALS`/`SLICER_VISUALS` |
| `data/wells.ts` (91) | `wellsFor` (per-visual field buckets), `AGG_FUNCTIONS`, `FILTER_OPERATORS` |
| `data/format.ts` (107) | Power BI-like value/axis/label formatting (fr-FR locale) |
| `data/shape.ts` (143) | rows → series, `sumMetric`, `histogram`, `quartiles`, `linregress`, `movingAverage`, `treemapLayout` |
| `data/palettes.ts` (60) | 10 chart palettes, 7 report themes, diverging `colorScale` |
| `data-api.ts` (270) | the REST surface (datasources, datasets, measures, model, execute, reports, pages, widgets) |
| server `handlers/data_*.rs`, `models/data.rs` | the `office_data.*` tables and routes |

### 1.2 Features (inventory)

Shell - list/editor routing (`DataApp.tsx:33-51`); recents + Drive browse of `Office/Data` (`.kbdrp`/`.kbdst`) +
new (`:55-133`); trash/star/duplicate (`:68-75,162-173,274-290`); editable title (`:154-178`); backstage Fichier
with stats (`:185-213`); Rapport/Données/Modèle strip (`:239-314`); SQL query editor (datasets, validate,
preview, refresh - `:363-580`); semantic model view (tables, schema, measures, relations - `:584-675`); read-only
macro API (`:216-229`).

Report editor - 5-tab ribbon Accueil/Insertion/Modélisation/Affichage/Format(contextuel) (`:1283-1396`);
auto-save (one server mutation per edit, `:265-266`) + undo/redo by inverse ops with drag coalescing
(`:534-541,142-255`); canvas with absolute-positioned widgets, drag/resize/z-order/lock (`:681-771`), 8 px grid
snap (`:38-39`), clipboard copy/clone/paste (`:383-393`), align (distribute is a no-op, `:395-400`); dockable
panes persisted under `kubuno:office:dataDock` - Fields (draggable columns, `:854-899`), Visualizations gallery +
Wells editor with aggregations (`:901-996`), Format (general/data/colors/visual-elements/gauge/text/image/shape,
`:1026-1125`), Filters (page + visual, `:1159-1229`); slicer cross-filtering (`:605-614,650-662`); multi-page
(size/background/position, `:293-348,831-848`); Focus mode (`:809-827`); context menus widget/page (`:1237-1277`);
shortcuts Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y / Suppr (`:357-374`); field drag&drop `text/kbfield` (`:882-950`).

Visuals - **38** catalogue types in 8 categories (`visuals.tsx:43-90`), 37 `renderVisual` cases + `scorecard`
alias; static = text/image/shape; slicers = slicer/slicer_dropdown/slicer_range (+ filter_date/filter_dropdown).

Data flow - a non-static, non-slicer widget fetches its rows through `executeApi.query` (`POST /office/data/execute`,
dataset_id + dimensions + metrics + merged filters + sort + limit 200), cached by React Query; refresh invalidates
`['widget-data']`. Aggregations SUM/AVG/COUNT/COUNT_DISTINCT/MIN/MAX. SQL and measure evaluation run **on the
server** - the client never executes a query.

### 1.3 Stored format and API

- A report = a row of `office_data.reports` (`title`, `theme` JSONB, `dataset_ids`, `file_id`, star, trash,
  public, share_token) + rows of `office_data.report_pages` (title, position, width, height, background) + rows of
  `office_data.widgets` (widget_type, x, y, width, height, `config` JSONB, z_index). The **DB is the source of
  truth** (`handlers/data_reports.rs`, `models/data.rs`).
- The Drive file `Office/Data/<title>.kbdrp` holds `{ "version": 1, "report": …, "pages": [], "widgets": [] }`
  at creation only (a best-effort mirror so it shows in the file browser and can be reopened by file); it is **not
  rewritten** on edit. `open-by-file` resolves `file_id → report id` then loads pages+widgets **from the DB**
  (`:114-160`). `.kbdst` is the dataset file (definition + schema + result cache).
- Routes (`data-api.ts`, base `/office/data`): `datasources` CRUD + `/test`; `datasets` CRUD + `/refresh`,
  `/preview`, `/validate-sql`; `measures` CRUD + `/validate`; `model` + `/relations`; `execute` + `/execute/measure`;
  `reports` CRUD + `/trash`, `/restore`, `/duplicate`, `/open-by-file`; `pages` create/update/delete; `widgets`
  create/update/delete + `/widgets/batch`. Owner-only.
- Server facts that shape the client: an edit is a **single REST mutation**, applied immediately; there is **no
  etag, no content digest, no edit session** on a report - concurrency is last-write-wins per row. `widgets.batch`
  exists for bulk position moves. Measures and query results are server-computed (SQL against a datasource).

## 2. Architecture

```
office/
  common/data-core/          kubuno-office-data-core        the Data engine (model, shaping, format, palettes, wells, visuals, editor)   [as built]
  desktop/common/kubuno-office-data-common                  the complete portable app (planned)
  desktop/windows/kubuno-office-data                        the Win32 window, kubuno-data.exe (planned)
```

### 2.1 `kubuno-office-data-core` (`office/common/data-core`) - as built

A standalone, platform-neutral crate (path dependency, like `diagrams-core`), ported from the web's pure logic.
No UI, no Windows, no SQL (query execution stays on the server).

| Module | Web origin | Content |
|---|---|---|
| `model` | `data-api.ts`, server `models/data.rs` | `ReportDoc { version, report, pages, widgets }`, held **losslessly** (unknown server fields kept in a flattened `extra`; `config`/`theme` stay `serde_json::Value`, key order preserved); `.kbdrp`/REST (de)serialisation; widget config accessors |
| `format` | `data/format.ts` | `NumberFormat`, `format_value`/`format_axis`/`format_label`, fr-FR grouping with `U+202F`, `to_num`/`is_numeric_value` (a JS-`parseFloat` subset) |
| `shape` | `data/shape.ts` | `shape_series` (pivot by legend), `sum_metric`, `histogram`, `quartiles`, `linregress`, `moving_average`, squarified `treemap_layout` |
| `palettes` | `data/palettes.ts` | the 10 palettes, the 7 report themes, the diverging `color_scale` |
| `wells` | `data/wells.ts` | `wells_for`, `AGG_FUNCTIONS`, `FILTER_OPERATORS`, `operator_takes_value` |
| `visuals` | `data/visuals.tsx` | the 38-type catalogue, categories, `is_static`/`is_slicer`/`needs_query` |
| `editor` | `DataReportEditor.tsx` | `Editor`: current page, selection, `EditOp` with **inverse-op** undo/redo and drag coalescing (one undo per gesture); each op is the unit the platform maps to a REST mutation |

No clock, no randomness, no I/O: ids and time come from the caller, files are bytes. Measure/DAX evaluation and SQL
are intentionally absent (server-side on the web).

### 2.2 Desktop app (planned)

- `desktop/common/kubuno-office-data-common` - the portable app: launch options
  (`kubuno-data [--dark] [--culture fr|en] [--sample|--live] [--doc <report-id|file-id> | file.kbdrp]`), opening a
  local `.kbdrp`, and a **Data REST client** over `kubuno-office-desktop-common`'s `Transport` (the shell's token
  broker). Unlike Documents, there is **no session host / journal**: the client calls `reports.get`/`open-by-file`
  to load, then `execute.query` per queryable widget, and maps each `EditOp` to its mutation
  (`widgets.create/update/delete`, `pages.*`, `reports.update`), optionally batching position moves. Offline =
  read-only cache (no local write journal) until the open question in §6 is settled.
- `desktop/windows/kubuno-office-data` - `kubuno-data.exe`: `views/data_window.kbview` (TitleBarStyle Tall, header
  menus, declarative `<Ribbon Tone="Data">` matching the 5 web tabs, a `DockArea` with Fields / Visualizations +
  Wells / Format / Filters panes around the canvas, the pages bar, the status bar), the custom control
  `ReportCanvas` (widgets drawn through a Direct2D `Surface`: cards, the chart families, tables, slicers - ported
  from `visuals.tsx` via the core's shaping/format), `FieldsPane`, `VisualGallery`, `WellsEditor`, `FormatPane`,
  `FiltersPane`, `PagesBar`; dialogs reuse Documents' vendored confirm/prompt. Charts paint from
  `shape::*`/`format::*`, so the drawing stays faithful to the web.

### 2.3 Shared pieces with Documents

Reused without copying: `kubuno-office-desktop-common::api::client::{Transport, Request, Response, Method}` (a path
dependency) for the broker transport. The session/live/journal layer is **not** reused (Data has no file-save
session). The ribbon shell, backstage and confirm/prompt dialogs are `.kbview` files of each app (the framework's
controls are the shared part). Any change needed in `desktop/common` shared pieces is coordinated (other agents
own the Documents crates).

## 3. The save / sync path (how Data differs)

1. Open (`--doc <id>`): `reports.get(id)` (or `open-by-file(fileId)` for a `.kbdrp`) → `{ report, pages, widgets }`
   into a `ReportDoc`; `datasets.list` for the Fields pane; `execute.query` per queryable widget of the current page.
2. Each edit is one `EditOp` applied to the in-memory doc (instant, local) **and** mirrored to the server as its
   REST mutation - no digest, no `PUT …/data`, no edit session. Position drags coalesce in the undo history and may
   be flushed with `widgets.batch`.
3. Undo/redo replay the inverse op locally and send the corresponding mutation; concurrency is the server's
   last-write-wins per row (as on the web).
4. Title/theme/star/trash = `reports.update`/`trash`/`restore`; page add/rename/delete = `pages.*`;
   duplicate = `reports.duplicate`.
5. There is no 413 content cap (bodies are small per-row JSON) and no crash journal in increment C; offline is
   read-only until §6 is decided.

## 4. Increments

- **Note** - this file (commit 1).
- **Core** - `kubuno-office-data-core` (model + format + shape + palettes + wells + visuals + editor), unit tests
  mirroring the web, cross-target checks. *(as built, commit 2.)*
- **A** - open a local `.kbdrp` and `--doc <id>`, display read-only at parity (canvas, widgets painted from the
  core, pages, theme), light and dark; the Fields/Visualizations/Format panes shown.
- **B** - editing: every gesture and ribbon command of §1.2 wired to `EditOp`s, panes and context menus,
  shortcuts; wells drag&drop; slicer cross-filter.
- **C** - server mutations (§3), page management, star/rename/trash/duplicate, the SQL (Données) and semantic
  model (Modèle) views, refresh.
- **VS** - the views in the designer, `Kubuno.Office.slnx` regenerated.

## 5. Theme

The web editor's chrome uses `THEME_DATA` (`ribbon/officeThemes.ts`); the framework's `Tone="Data"` matches it.
The report canvas uses the report theme's `background`/`pageBackground`/`cardBackground`/`foreground`
(`palettes.ts:36-44`) in both app themes, as on the web (the report theme is the document's, not the OS'); panes
follow the app light/dark theme. Chart colours come from the report's `paletteId` (default `kubuno`).

## 6. Risks and known divergences

- **Server coupling (open question for the user).** Data cannot be meaningfully exercised without a dev core with a
  datasource and datasets (SQL runs server-side). Read-only parity and editing of layout/config are testable
  offline against a `.kbdrp` + a captured `execute` response, but true round-trip needs the office dev core on the
  local PostgreSQL with a datasource. **Decision needed:** confirm the server-coupled approach (no Documents-style
  file save) and whether standing up a datasource is in scope for this lot.
- Locale formatting: the web calls `toLocaleString('fr-FR')`, whose group separator is `U+202F` in modern ICU; the
  core reproduces that, but an older ICU/V8 on the web could differ. Rounding uses `halfExpand` (as `toFixed`),
  pinned by tests.
- Fonts: the web asks for Inter/Georgia; the desktop uses DirectWrite with the same family names and the system
  fallback (desktop font decision), so chart label wrapping can differ by a few pixels.
- Multi-select is only partially wired on the web (`DataReportEditor.tsx:133` operates on a single widget) and
  `align 'distribute'` is a no-op (`:397`) - the desktop matches the web, these are not implemented beyond it.
- Macros run JavaScript in the browser; the desktop has no script runtime - the read-only macro API is shown but
  inert (listed as not done).

## 7. Verification

`cargo test` + `clippy -D warnings` for the core (and later the app); `cargo check` of the core for wasm32, Linux
and macOS; a golden fixture written by the web editor round-tripped by meaning; side-by-side captures of the same
report, web vs desktop, light/dark at 100 % and 175 %; the server round trip on an office dev core with its own
database and a datasource; real mouse on the ribbon (under the shared VS/input lock), keyboard shortcuts, UIA names.

## 8. Log

**2026-10-11** - note written (commit 1).

**2026-10-11 (as built - core)**
- Core `office/common/data-core` (`kubuno-office-data-core`): `model` (lossless `ReportDoc`, flattened `extra`,
  config accessors), `format` (fr-FR grouping with `U+202F`, every `NumberFormat` token, `parseFloat` subset),
  `shape` (series pivot, histogram, quartiles, OLS, moving average, squarified treemap), `palettes` (10 palettes,
  7 themes, diverging scale), `wells` (per-visual buckets, agg/filter vocabularies), `visuals` (38-type catalogue,
  static/slicer classification), `editor` (`EditOp` inverse-op undo/redo with drag coalescing, page cascade,
  theme-as-one-undo). **29 unit tests pass** on Windows; `cargo clippy --all-targets -- -D warnings` clean;
  `cargo check` clean for `wasm32-unknown-unknown`, `aarch64-apple-darwin` and `x86_64-unknown-linux-gnu`.
  Build on `E:\kubuno-build\agent-office-data\target`.

**Where this lot stopped (2026-10-11) and next steps**
- Desktop app (`desktop/common/kubuno-office-data-common` + `desktop/windows/kubuno-office-data`) and the VS
  integration are **not built yet** - they are increments A–C and VS above. The core is ready for them.
- Before increment C, settle the §6 open question (server-coupled approach, datasource in scope) with the user.
- Golden fixture from the real web editor (a report with a few widgets) to pin `model` round-trip against actual
  server output; currently pinned against a hand-written sample mirroring `models/data.rs`.

## 9. Feature coverage

✔ done and verified · ◐ implemented and unit-tested, not exercised live · ✘ not done · - not applicable.
Web lines are in `DataReportEditor.tsx` / `DataApp.tsx` / `visuals.tsx` as noted.

| Feature | Web | Desktop |
|---|---|---|
| Report document, lossless (report/pages/widgets, unknown fields) | `models/data.rs`, `data-api.ts` | ◐ core round-trips by meaning (29 tests) |
| Value/axis/label formatting (Power BI tokens, fr-FR) | `format.ts` | ◐ core, unit-tested |
| Row → series shaping, pivot by legend | `shape.ts:14-43` | ◐ core, unit-tested |
| Histogram, quartiles, regression, moving average, treemap | `shape.ts:51-143` | ◐ core, unit-tested |
| 10 palettes, 7 themes, diverging scale | `palettes.ts` | ◐ core, unit-tested |
| Per-visual field wells, agg/filter vocabularies | `wells.ts` | ◐ core, unit-tested |
| 38-visual catalogue, static/slicer classification | `visuals.tsx:43-90` | ◐ core, unit-tested |
| Edit ops + inverse-op undo/redo with drag coalescing | `:142-255` | ◐ core, unit-tested |
| Page CRUD with cascade + restore | `:293-348` | ◐ core (`AddPage`/`DeletePage`/`SetPage`) |
| Theme apply as one undo (report + per-widget repaint) | `:411-425` | ◐ core (`ApplyTheme`) |
| Open local `.kbdrp` / `--doc <id>` read-only at parity | `DataApp.tsx`, `open-by-file` | ✘ increment A |
| Canvas: widgets painted, drag/resize/z-order/lock, grid snap | `:681-771` | ✘ increment B |
| Ribbon 5 tabs, panes, context menus, shortcuts | `:1283-1396` | ✘ increment B |
| Wells drag&drop, Format pane, Filters pane | `:854-1229` | ✘ increment B |
| Slicer cross-filtering, widget data via `execute.query` | `:605-679` | ✘ increment C (server) |
| Server mutations (widgets/pages/reports), refresh | `data-api.ts` | ✘ increment C (server) |
| SQL (Données) + semantic model (Modèle) views | `DataApp.tsx:363-675` | ✘ increment C |
| Trash, star, duplicate, title, backstage stats | `DataApp.tsx` | ✘ increment C |
| Focus mode | `:809-827` | ✘ increment B |
| Macros (read-only API) | `DataApp.tsx:216-229` | ✘ no script runtime (shown inert) |
| VS designer views, `Kubuno.Office.slnx` | - | ✘ VS increment |
