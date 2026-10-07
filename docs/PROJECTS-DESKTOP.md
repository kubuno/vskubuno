# Kubuno Projects (desktop) — design note

Status: design of record for the native desktop version of the office module's project editor
(`kubuno-projects.exe`), written before the app code (mission of 2026-10-07: « the desktop app must fully catch up
with the web version — every feature of the web Projects editor, not a subset »). It follows the method of
`DOCUMENTS-EDITING.md` and `SPREADSHEETS-DESKTOP.md`: the web editor is the behavioural and visual reference, its
logic goes into a platform-neutral shared core (`kubuno-office-projects-core`), the desktop app is the portable app
of `desktop/common` plus the Win32 window of `desktop/windows`. Section 7 (coverage) and section 9 (log) are kept up
to date as increments land.

Paths are relative to the office repository (`office/`). `PE` is `web/src/ProjectEditorPage.tsx` (2 545 lines),
`P/` is `web/src/project/`, `SRV` is `server/src/handlers/projects.rs`.

## 1. What the web editor does

### 1.1 Shape of the code

| Web file | Role |
|---|---|
| `PE:233-2540` | the editor: state, queries, the undoable commands (`PE:650-1000`), the Gantt gestures (`PE:1019-1490`), the ribbon (`PE:1781-1930`), the inspector and resources dock panels (`PE:1932-2052`), the plan views switch (`PE:2131-2413`), the status bar (`PE:2416-2434`), the context menus (`PE:2441-2503`) |
| `P/GanttRenderer.ts` (610) | the Gantt canvas: header (month band, day numbers / ISO weeks), weekends, today band, dependency elbows, bars (task pill with progress, padlocks, check, avatars, link handle; summary bracket over the visible children; milestone diamond), baseline ghost, project window veil and boundary markers with their dated flags and hover tooltip |
| `P/TaskRow.tsx`, `P/ganttTableConstants.ts` | the task table: 11 columns (n°, mode, name, duration, %, priority, start, end, variance, predecessors, resources; 6 shown by default), widths/visibility/compact per project in the user's prefs, inline editors, completion ring, drag handle |
| `P/rollup.ts`, `P/schedule.ts`, `P/workingCalendar.ts` | completion roll-up, scheduled dates, the calendar mirror used to predict drops |
| `P/TimelineBand.tsx` | the overview band (phases and milestones on up to 6 lanes) |
| `P/TaskDetailPanel.tsx`, `P/TimeLogSection.tsx` | the inspector: every task field, constraint/deadline rules, CPM figures, resources, time log |
| `P/BoardView.tsx`, `P/CalendarView.tsx`, `P/resources/*`, `P/NetworkView.tsx`, `P/RoadmapView.tsx`, `P/ResourceLoadView.tsx` | the other plan views: board, month calendar, resources (registry, heatmap, availability, cost), network (PERT, free and time-scaled layouts), roadmap (versions) |
| `P/CharterView.tsx` … `P/ManagementPlansView.tsx` (19 files, ~17 000 lines) | the project-management registers (PMBOK): charter, WBS dictionary, deliverables, requirements, traceability, risks, issues, earned value, expenses, stakeholders, RACI, quality, communications, decisions, change control, closure/lessons, procurement, management plans |
| `P/ProjectPropertiesPanel.tsx`, `P/ProjectSettingsPanel.tsx`, `P/ProjectCalendarSection.tsx`, `P/DocumentProductionPanel.tsx` | the backstage pages: properties, project modules (tailoring, methodology, working calendar), document production |
| `ProjectsApp.tsx`, `NewProjectDialog.tsx`, `ProjectsStartContent.tsx`, `CloudProjectPage.tsx`, `P/PortfolioBoard.tsx` | the list, the new-project dialog (management / cloud), the start page (recents, Drive browser, portfolio tree and health), the cloud project page |

### 1.2 Facts that drive the port

- **A project is rows, not a document.** `GET /projects/:id` returns `{ project, tasks, dependencies, resources,
  assignments }` (`SRV:216-278`); every edit is one REST call on one row, sent immediately; after each one the web
  re-reads the whole project (`refresh()`, `PE:583`). There is no editing session, no etag, no digest, no 2 MiB
  document: the Documents save session (`api::session`) does not apply. The `.kbprj` file in Drive
  (`build_project_json`, `SRV:1604-1642`, `kubuno-project/v1`) is a reduced export rewritten by the server.
- **The schedule is the server's.** `POST /projects/:id/cpm` (`SRV:1223-1577`) computes ES/EF/LS/LF, floats,
  criticality, deadline misses and the summary roll-up in working days of the project calendar
  (`services/working_calendar.rs`). The web calls it only after Gantt drags, link edits, project date moves,
  deletion undos and the « Replanifier » button — **not** after a duration typed in the table, so bars stay where
  they were until the next schedule (ported as is).
- **Undo is a stack of inverse REST calls** (`history/useEditHistory.ts`): 100 entries, ids resolved through a remap
  table (an undone deletion re-creates rows under new ids), a failed inverse clears both stacks, continuous edits
  of `name/description/progress/duration_days` on one task within 900 ms are one entry (`PE:93-103`, `PE:665-704`).
- **Quirks kept** (each marked `QUIRK` in the core): `position: 0` on creation means « after the last sibling »
  (`create_task`), so « Insérer au-dessus » of the first row appends; the Gantt renderer rolls summaries and
  progress up over the **visible** rows only; the web's calendar mirror falls back to the first calendar of the
  list when none is selected where the server falls back to the instance default; start/end columns are the CPM
  offsets, not `start_date`.
- **Collaboration** is a Yjs room `office-project:<id>` holding only a revision counter (`meta.rev`) bumped after
  each local edit, and the presence awareness (avatars, cursors) (`PE:556-583`): peers re-read the project when it
  changes. No row data travels through Yjs.
- **Theme**: the chart reads the CSS tokens (`--color-surface-0/1/2`, `--color-border`, `--color-primary`,
  `--color-danger`, `--color-success`, `--color-text-*`), so it follows light/dark (unlike the spreadsheet grid).
  Light `core/web/src/theme.css:12-49`, dark `core/web/themes/kubuno-dark/theme.json:12-27`. Ribbon tone
  `#1e7a6f` (`web/src/ribbon/officeThemes.ts:25`).

## 2. Stored format

The API JSON is the format: `Project`, `Task`, `TaskDependency`, `ProjectResource`, `TaskAssignment`
(`server/src/models/project.rs`), `Baseline` (`project_baselines.rs`), `Calendars` (`project_calendars.rs`),
`Settings` (`project_settings.rs`). The core keeps every record losslessly (unknown fields carried in `rest`): a
record read and written back is the same JSON value (test `model::round_trip`). PATCH semantics are the server's:
`COALESCE` fields (a `null` keeps the value: name, status, dates, duration, progress, position…) and tri-state
fields (`null` clears: `parent_id`, `constraint_date`, `deadline_date`, `version_id`, `budget_cost`; resources:
`hourly_rate`, `overtime_rate`, `cost_per_use`, `unit_label`).

A **local file** is either the API's `GET /projects/:id` body (what the desktop journals) or a `.kbprj` export
(gzipped or plain), opened without a schedule.

## 3. API (all under `/api/v1/office`, `server/src/router/mod.rs:107-222`)

| Route | Use on the desktop |
|---|---|
| `GET /projects/:id` · `PATCH /projects/:id` | open, title/star/dates/status/colour/description |
| `POST /projects/:id/tasks` · `PATCH`/`DELETE /projects/:id/tasks/:tid` | task edits (server renumbers the outline after each) |
| `POST /projects/:id/dependencies` (upsert on from/to) · `DELETE …/:did` | links |
| `POST`/`DELETE /projects/:id/tasks/:tid/assign[/:rid]` | assignments (upsert on task/resource) |
| `GET/POST /projects/:id/resources`, `PATCH/DELETE …/:rid`, `…/time-off` | resources |
| `POST /projects/:id/cpm` | the scheduler |
| `GET/POST /projects/:id/baselines`, `PATCH/DELETE …/:bid` | baselines |
| `GET /projects/:id/calendars`, `GET /projects/:id/settings`, `GET /projects/:id/versions` | side lists |
| `GET /projects` (`recent`, `starred`, `shared`, `trashed`), `POST /projects`, `…/duplicate`, `…/trash` | list, new, duplicate, trash |
| the 18 register routes (`/charter`, `/wbs`, `/deliverables`, `/requirements`, `/traceability`, `/risks`, `/issues`, `/costs`, `/stakeholders`, `/raci`, `/quality`, `/communications`, `/decisions`, `/changes`, `/closure`, `/lessons`, `/procurement`, `/plans`) | the registers (§7, later increments) |

## 4. The shared core: `common/projects-core` (`kubuno-office-projects-core`)

Platform-neutral (serde, serde_json; no UI, no OS API), checked for `wasm32-unknown-unknown`,
`x86_64-unknown-linux-gnu` and `aarch64-apple-darwin`. It never reads a clock: the caller gives today's date and the
time.

| Module | Content | Origin |
|---|---|---|
| `date` | civil dates, ISO weeks, the `Intl` formats the editor uses (fr/en) | `@kubuno/sdk` datetime |
| `model` | the API records held losslessly; `.kbprj` import | `models/project.rs`, `api.ts:911-1037` |
| `calendar` | working calendar (advance/retreat/next working/working days between), the scheduler's and the web's choice of calendar | `services/working_calendar.rs`, `P/workingCalendar.ts` |
| `cpm` | the scheduler, statement for statement | `SRV:1223-1577` |
| `wbs` | outline numbering (pre-order, position then creation time) | `project_wbs.rs:87-139` |
| `rollup` | completion roll-up (duration-weighted, JavaScript rounding) | `P/rollup.ts` |
| `view` | table rows (filter, sort, group, fold), row numbers, predecessors text ↔ links, scheduled dates, chart extent, status bar, CSV | `PE:449-554`, `PE:915-968`, `PE:1664-1673`, `P/schedule.ts` |
| `gantt` | chart geometry (bars, brackets, connectors, elbows), hit testing, drag preview and commit (snap to worked days, SNET pins, phase moves of the free leaves), boundary markers, link target | `P/GanttRenderer.ts`, `PE:1019-1490` |
| `views` | board columns, month grid, resource load, timeline band lanes | `P/BoardView.tsx`, `P/CalendarView.tsx`, `P/ResourceLoadView.tsx`, `P/TimelineBand.tsx` |
| `change` | one REST call (`Change` → method, path, body) and its mirror on the local rows (COALESCE/tri-state, cascades, upserts, renumbering, scheduler) | `SRV` handlers |
| `editor` | the outbox (changes shown at once, sent in order, temporary ids remapped on the server's answer), the journal, the history (inverse calls, remap, coalescing), conflict detection, every command of `PE` | `PE:650-1700`, `history/useEditHistory.ts` |

The server could later use `cpm`, `calendar` and `wbs` directly (one scheduler for every client and the server);
not in this lot (the server's code is another agent's).

## 5. The desktop app

### 5.1 Crates (platform rule: common = complete app, OS folders = overrides)

| Crate | Folder | Role |
|---|---|---|
| `kubuno-office-projects-core` | `common/projects-core` | the engine (§4) |
| `kubuno-office-projects-common` | `desktop/common/` | the portable app: launch options (`--doc <id>`, a file, `--dark`, `--culture`, `--zoom`, `--sample`, `--view`), the server path (routes, the outbox worker over the shared transport, the journal), sample project, text UI, platform extension points |
| `kubuno-office-projects` | `desktop/windows/` | `kubuno-projects.exe`: the window (`.kbview`, declarative ribbon `Tone`/accent `#1e7a6f`, backstage, title bar `Tall` with waffle/account), the custom controls (`GanttView`: table + splitter + chart + timeline band; `BoardView`, `CalendarView`, `LoadView`), the inspector panel, status bar, dialogs |

### 5.2 Save path (differs from Documents on purpose)

No document bytes: the editor's outbox **is** the save. A worker thread sends the queued calls one at a time
through the shared transport (`kubuno-office-desktop-common::api::client::SyncTransport`, the shell's token broker),
reports each answer to the UI thread, and re-reads the project when the queue is empty (the web's `refresh()`).

- **Offline / crash**: the journal `<data>/projects/journal/<id>.json` holds the last server copy and the unsent
  changes (temporary ids included), written after every command; at start the project opens from it and the
  queue resumes when the server answers.
- **Conflicts** (desktop addition, the web is last-write-wins): each field edit remembers the value it replaced;
  if the re-read project shows another value there before the edit was sent, the queue stops and the user keeps
  theirs or takes the server's (Kubuno `ConfirmDialog`).
- **Refusals** (4xx): the call and the rest of its command are dropped, the history cleared (the web's rule for a
  failed inverse), the message shown in the status bar.
- **413**: requests are small rows; not reachable in practice (handled as a refusal).
- **Collaboration**: the Yjs `meta.rev` bump and the presence need a Rust Yjs client (`yrs`) — the one the
  Spreadsheets agent is building; until it lands the desktop re-reads the project every 30 s while idle so web
  edits show up, and web peers see desktop edits on their next read (§8).

## 6. Increments

| | Content |
|---|---|
| 0 | this note; the core (model, calendar, cpm, wbs, rollup, view, gantt, views, change, editor) with unit tests mirroring the web and the server, golden schedules produced by the dev server |
| A | read-only at parity: window, ribbon declared (every web control, disabled until wired), `GanttView` (table + chart + timeline band) light/dark, status bar, backstage info; local file + `--doc` |
| B | editing: selection, table inline edits, inspector, ribbon commands (tasks, hierarchy, move, links, progress, status, priority, baselines, zoom, fold, filters), Gantt drags (move, resize, link, phase, markers), context menus, undo/redo, board/calendar/load views |
| C | save: outbox worker, journal, conflict dialog, re-read |
| D | network and roadmap views, resources views, registers (§7) |

## 7. Coverage

✔ done and verified (capture, input, or server round trip) · ◐ implemented, partly verified · ✘ not done (reason) ·
⧗ planned. State: see §9.

**Core (common/projects-core)**

| Feature | Web / server | Desktop |
|---|---|---|
| Lossless records, `.kbprj` import | `models/project.rs`, `SRV:1604` | ⧗ |
| Working calendar (server + web mirror) | `working_calendar.rs`, `P/workingCalendar.ts` | ⧗ |
| CPM (FS/SS/FF/SF, lags, 8 constraints, ALAP, deadlines, free float, roll-up, loop error) | `SRV:1223-1577` | ⧗ |
| Outline numbering | `project_wbs.rs:87` | ⧗ |
| Roll-up, scheduled dates, chart extent, status counts, CSV | `P/rollup.ts`, `P/schedule.ts`, `PE:529-554`, `PE:1664` | ⧗ |
| Filter / sort / group / fold | `PE:449-515` | ⧗ |
| Undo/redo (inverse calls, remap, 900 ms coalescing, 100 entries) | `useEditHistory.ts`, `PE:650-890` | ⧗ |

**Window — Gantt view**

| Feature | Web | Desktop |
|---|---|---|
| Title bar Tall, waffle/account, title, star, share, undo/redo, save | `PE:2055-2120` | ⧗ |
| Ribbon: Fichier, Accueil, Tâche, Liaisons, Périmètre, Pilotage, Format, Affichage | `PE:1781-1930` | ⧗ |
| Plan view tabs (Gantt, Tableau, Calendrier, Ressources, Réseau, Roadmap) | `PE:2135-2150` | ⧗ |
| Table: 11 columns, default 6, header menu, resize, compact, inline edits, ring, avatars, priority dot | `P/TaskRow.tsx`, `P/ganttTableConstants.ts` | ⧗ |
| Chart: header, weekends, today, elbows, bars, brackets, milestones, progress, padlocks, check, avatars, link handle, baseline ghost, window veil, markers, hover tooltip | `P/GanttRenderer.ts` | ⧗ |
| Chart gestures: move, resize both ends, phase move, link, markers, threshold, auto-scroll, Escape | `PE:1019-1490` | ⧗ |
| Timeline band | `P/TimelineBand.tsx` | ⧗ |
| Filter bar, baseline bar | `PE:2151-2191` | ⧗ |
| Inspector (all fields, constraints, deadline, CPM figures, resources, predecessors with type/lag) | `P/TaskDetailPanel.tsx`, `PE:1933-1973` | ⧗ |
| Resources panel (members, external, rate) | `PE:1986-2045` | ⧗ |
| Context menus (row, header) | `PE:2441-2503` | ⧗ |
| Status bar | `PE:2416-2434` | ⧗ |
| Export CSV / PNG | `PE:1664-1681` | ⧗ |
| Backstage: info, properties, modules, document production, print, close | `PE:1696-1746` | ⧗ |

**Other views and registers**: board, calendar, resources (registry, heatmap, availability, cost), network,
roadmap, time log, and the 18 registers of `P/` (§1.1) — ⧗ (increment D); the as-built table in §9 says which
landed.

## 8. Risks and open questions

- **Collaboration (Yjs)**: real-time parity needs the shared `yrs` client (Spreadsheets agent). Until then the
  desktop polls; web peers do not see desktop edits live (they re-read on their own edits or reload).
- **Registers**: ~17 000 lines of web forms; each is a REST list + forms over its own routes. They are a lot of
  UI but little logic (the figures are computed by the server: earned value, closure checks, portfolio flags,
  quality conformity). Planned as generic data views after the Gantt.
- **Timezone quirk**: the web reads `"YYYY-MM-DD"` as UTC midnight; west of UTC it shows the previous day. The
  desktop uses civil dates (correct everywhere).
- **Server CPM vs core CPM**: identical by construction (ported statement for statement, golden schedules from
  the server); the server remains the authority (the desktop shows its local schedule until the server's arrives).

## 9. Log

(as built — filled as increments land)
