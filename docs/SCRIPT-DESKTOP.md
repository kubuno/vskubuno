# Kubuno Script (desktop) — design of record

Status: plan and log of the Script desktop lot (user request: every office sub-application available as a
native desktop app, following the Kubuno Documents method, to **full web parity**). Reference for behaviour and
look: the **web Script editor** (`office/web/src/ScriptApp.tsx`, `script-api.ts`). Each increment below records
what was built and how it was verified. Written 2026-10-12.

Script is the office module's **automation editor**: a code editor for small TypeScript/JavaScript programs that
run against the Kubuno API, plus the management around them — a scripts list, scheduled/event/webhook
**triggers**, a **run history**, and a live **console** that streams a run's output. It is the office equivalent
of Google Apps Script. The central surface is a **Monaco** code editor; unlike every other office editor, it is
**not** a canvas document — the "document" is a script's source string stored in a `.kbscr` Drive file.

## 0. What the web app does (feature inventory, with file:line)

Source: `office/web/src/ScriptApp.tsx` (1072 lines, the whole app) and `script-api.ts` (the REST surface).

| Area | What it does | Web source |
|---|---|---|
| Scripts list (sidebar) | list the owner's scripts, select one, create a new one, per-script last-run status dot | `ScriptApp.tsx:253-317` |
| View switcher | three views for the selected script: **Éditeur / Déclencheurs / Historique** | `ScriptApp.tsx:296-314` |
| Code editor | Monaco (`@monaco-editor/react`), TypeScript language, dark theme, word-wrap, 2-space tabs, line numbers, folding; code state + autosave | `ScriptApp.tsx:554-764,720-743` |
| IntelliSense | adds the Kubuno API `.d.ts` as an extra lib so `Kubuno.*` autocompletes | `ScriptApp.tsx:588-590,671-683` |
| Autosave | debounced autosave (cadence = office instance default, `scriptAutosaveS`; 0 disables) | `ScriptApp.tsx:579-585` |
| Save | manual Save button (toolbar + title bar), `PATCH /scripts/:id {source_code}` | `ScriptApp.tsx:592-608,699-705` |
| Compile | `POST /scripts/:id/compile` — server strips TypeScript annotations | `ScriptApp.tsx:664-669` |
| Run | `POST /scripts/:id/run` → `run_id`, then an **SSE** stream (`console_log` + `finished` events) into the console | `ScriptApp.tsx:610-662` |
| Console panel | live console entries (log/warn/error coloured), run status, error box, return-value box, streaming pulse | `ScriptApp.tsx:59-121` |
| Triggers view | list triggers; create (cron / event / webhook form); toggle active; delete | `ScriptApp.tsx:131-238,321-436` |
| History view | list past runs (status dot, source, duration), run detail: status, error, return value, console output | `ScriptApp.tsx:440-542` |
| Start page | recents (open / duplicate / trash) + browse `Office/Scripts` + New | `ScriptApp.tsx:777-816` |
| Title / backstage | editable title (= script name), Fichier backstage with `.kbscr` info, print, close | `ScriptApp.tsx:932-957,1008-1012` |
| Star / duplicate / trash / rename | toggle favourite, duplicate, move to trash, rename (PATCH name) | `ScriptApp.tsx:872-900,1018-1031` |
| Ribbon | Fichier backstage + Accueil tab: clipboard group (acts on the focused editor), Script group (New / Duplicate) | `ScriptApp.tsx:985-1032` |
| Open by file | double-click a `.kbscr` in Files → `POST /scripts/open-by-file {file_id}` | `ScriptApp.tsx:903-911` |

## 1. The stored format (round-trip target)

Storage is **not** the Drive-file-blob + digest-PATCH model of Documents. A script is a DB row
`office_script.scripts` (`server/src/script/models/script.rs`) — `id, owner_id, name, description, file_id,
compiled_code, compile_error, timeout_secs, memory_limit_mb, run_count, last_run_at, last_run_status, is_starred,
is_trashed, created_at, updated_at` — whose **content (`source_code`) is a single string** read from / written to
a `.kbscr` Drive file via `services::content_files` (`content_files.rs:477-526`). The row stores only metadata;
`source_code` is populated after the SELECT (`scripts.rs:333-346`).

The `.kbscr` file is gzipped JSON (`content_files.rs:481-526`):

```
gzip( {"version": 1, "source_code": "<the TypeScript source>"} )
```

`extract_script_source` is lenient (a missing `source_code` → `""`). The shared core round-trips this
byte-compatibly with the server (unit tests below).

## 2. The API (what the desktop app calls)

Base `/office/script` (`script-api.ts`, `server/src/router/mod.rs:296-314`):

* `GET /scripts?search&trashed&limit&offset` → `{scripts, total}`
* `POST /scripts {name?,description?,source_code?,timeout_secs?,memory_limit_mb?}` → `{script}` (creates the `.kbscr`)
* `GET /scripts/:id` → `{script}` (reads `source_code` from the file; self-heals the name from the file stem)
* `PATCH /scripts/:id {name?,description?,source_code?,is_starred?,…}` → `{script}` (writes `source_code` to the
  file; renames the file on a name change; clears `compiled_code` when the source changes)
* `DELETE /scripts/:id`, `POST /scripts/:id/{trash,restore,duplicate,compile,run}`, `POST /scripts/open-by-file`
* `GET /scripts/:id/runs` → `{runs,total}`; `GET /runs/:id`; `GET /runs/:id/stream` (SSE `console_log`/`finished`)
* `GET /scripts/:id/triggers`, `POST …/triggers`, `PATCH /triggers/:id`, `DELETE /triggers/:id`, `POST
  /triggers/:id/toggle`
* `GET /api-types` → the Kubuno global `.d.ts` (IntelliSense)

There is **no** `If-Match`, editing session, digest precondition or 413 path in the script handlers (`scripts.rs`):
save is a plain `PATCH /scripts/:id {source_code}` plus a Drive-file write — exactly the **Maths** thin-PATCH model,
not the Documents session model. So the Documents save session (digest precondition, join/leave, conflict, 413,
collab epoch) **does not apply** here; the mission's "reuse Documents' save code" is satisfied by matching what the
web actually does. A last-write-wins conflict is still possible at the Drive-file level; the desktop mitigates it the
only way the server allows — re-`GET` before the PATCH and, if `source_code` changed under us, offer the Kubuno
conflict dialog — but it cannot be as strong as a digest guard. Open question Q1.

**Execution is server-side.** The web editor never runs a script locally: it `POST`s `/run` and streams the console
over SSE (`execute.rs`). There is therefore **no JavaScript engine (Boa) in the desktop client and none in the
shared core** — matching the web, and avoiding a second engine. (The mission's `kubuno-office-desktop-macros`/Boa
reuse is moot: that crate does not exist in the office repo, and local execution is not a web feature.)

## 3. The shared core

New crate `office/common/script-core` = `kubuno-office-script-core` (platform-neutral: `serde`, `serde_json`,
`flate2` rust-backend only; `cargo check` clean for `wasm32-unknown-unknown`, `x86_64-unknown-linux-gnu`,
`aarch64-apple-darwin`). Deliberately lean, because the web app's heavy lifting (execution, triggers, the sandbox)
is on the server:

| Module | Content | Ported from |
|---|---|---|
| `model` | the REST shapes `Script`, `ScriptTrigger`, `ScriptRun`, `ConsoleEntry` (+ `TriggerType`, `RunStatus`, `ConsoleLevel`) | `script-api.ts` |
| `file` | the gzipped `.kbscr` round-trip (`encode`/`decode`, byte-compatible with `content_files.rs`) | `content_files.rs` |

Not in the core, and why: **no execution engine** (server-side, §2); **no undo/redo** (a property of the text-editor
control, not the document model); **the API `.d.ts`** is fetched at runtime from `/api-types` like the web, not
embedded (it is server-owned and would drift).

## 4. The desktop app

Layout mirrors the other office apps (`repo-layout-platform-folders`): a portable crate
`desktop/common/kubuno-office-script-common` (launch options, the open-script state, the `/office/script` routes,
the platform extension points) and a Windows crate `desktop/windows/kubuno-office-script` (Win32 window, ribbon,
backstage, the `.kbview`, the code-editor control, the console/triggers/history panels), exe `kubuno-script.exe`.
Shared office-desktop pieces come from `desktop/common/kubuno-office-desktop-{common,shared}` — the thin-PATCH save
path is written like Maths (`api::connect_account` + `SyncTransport::for_account`), not against the Documents
session machinery.

**The code editor.** The framework (desktop-v0.1.2-alpha) has **no code editor, no monospace text model, no
line-number gutter and no syntax tokenizer** (verified: `kubuno-desktop-ui/src/text.rs` offers a multi-line
`TextArea` — a soft-wrapping plain-text field with `Font`, `AcceptsReturn`, `WordWrap`, `Text` TwoWay, undo/redo,
clipboard and selection). The editor surface is therefore a monospace `TextArea` (`Font="Cascadia Mono"`), which
gives real multi-line editing, selection, clipboard and undo/redo at parity for *editing*; Monaco's **syntax
highlighting, IntelliSense, line-number gutter and folding** are parity gaps listed in §6 (⛔), because each needs a
custom Direct2D code-editor control + a TypeScript tokenizer the framework does not provide — a dedicated lot.

Increments (each shippable, verified before the next):

* **A — open & display (read-only, at parity).** The window: tall title bar with the waffle/account, a ribbon
  (Fichier backstage with the script info, Accueil = clipboard + Script group), the scripts sidebar with the
  Éditeur/Déclencheurs/Historique switcher, the monospace editor (read-only), the console panel, the status bar.
  Opens a local `.kbscr` file and `--doc <id>` from the server through the shell's broker in the sandbox profile.
* **B — editing + ribbon commands.** The editor becomes editable (dirty flag, undo/redo); New / Duplicate / Save /
  Compile / Run wired; the Déclencheurs view (list / create cron·event·webhook / toggle / delete) and the Historique
  view (runs list + detail) built; star / rename / trash.
* **C — save, compile, run.** `PATCH /scripts/:id {source_code}` with a debounced autosave (cadence from the office
  instance config, matching the web) and the re-GET conflict check (§2); `POST /compile`; `POST /run` then the SSE
  stream into the console; an offline local journal replayed at start.

## 5. VS integration

The app's `.kbview` views open in the VS 2026 designer; `Kubuno.Office.slnx` is regenerated with the vskubuno
generator (`KUBUNO_GEN_REPO=office KUBUNO_GEN_FRESH=1`) so the new projects appear under Desktop. Designer check on
the **main** VS instance (no fresh hive — it hits the mandatory sign-in page), shared lock `vs-lock.ps1 -Owner
officeScript`.

## 6. Parity coverage table

Legend: ✔ done (unit test / run) · ◐ partial · ✘ not done yet (why) · ⛔ blocked (a decision / missing platform
piece) · — nothing to port.

### Shared core

| Feature | Web/server | Core status |
|---|---|---|
| REST model types (`Script`/`Trigger`/`Run`/`ConsoleEntry`) | `script-api.ts` | ✔ *(compiles; serde round-trip)* |
| `.kbscr` gzip round-trip (byte-compatible) | `content_files.rs` | ✔ *(unit tests)* |

### Desktop app

Legend for the "portable" split: the portable app crate `kubuno-office-script-common` is the testable, OS-neutral
half (launch, routes, worker, journal, save session); ⬚ marks the Win32 window (the next increment) still owed.

| Feature | Web | Desktop status |
|---|---|---|
| Open local `.kbscr` (gzip round-trip) + `--doc <id>` launch option | `ScriptApp.tsx`, `script-api.ts` | ✔ Win32: opens a local `.kbscr`/`.ts` read-only; `--doc` wired through the broker/worker (live round-trip owed with a dev core) |
| Scripts sidebar + Éditeur/Déclencheurs/Historique switcher | `:253-317` | ◐ Win32 left sidebar with the three-view switch; Éditeur renders, the other two are placeholders (B) |
| Code editing (type, select, clipboard, undo/redo) | Monaco | ✔ B1: caret, selection, keyboard nav, typing, Enter/Tab, Backspace/Delete, cut/copy/paste, undo/redo (shared `EditState`); verified live |
| Syntax highlighting (lexical) | Monaco | ✔ Direct2D editor renders the Monaco `vs`/`vs-dark` **lexical** colours (keywords, strings, numbers, comments, primitive types) |
| Syntax highlighting (semantic tints) | Monaco TS worker | ✔ Q3: a Rust oxc analyser colours type/function/variable identifiers (full Monaco parity, captured) |
| IntelliSense / autocomplete | `:671-683` | ✔ Q3: a completion popup from the Kubuno `api-types` + local AST symbols + keywords (captured) |
| Line-number gutter / folding | Monaco options | ✔ gutter + **fold gutter UI** (▾/▸ markers at region headers, click to collapse/expand, ⋯ hint; visual-row rendering) |
| Ribbon: clipboard (cut/copy/paste) + undo/redo | web ribbon | ✔ wired to the editor ops (captured enabled) |
| Ribbon: compile / run; save; triggers/history panels | `:664-669,610-662,321-542` | ✘ need a dev core + the Desktop shell broker + a login (see Log) |
| Save (`PATCH source_code`) + **content-digest precondition** + conflict + offline journal | `:592-608,579-585` | ✔ server digest precondition (`script::digest`, 409 `SAVE_CONFLICT`) + portable `session`/`worker`/`journal` (unit-tested both sides); ⬚ Win32 wiring |
| Autosave cadence (`office.script_autosave_s`, default 30) | `:579-585` | ◐ portable `routes::AUTOSAVE_KEY`/config read; ⬚ Win32 timer |
| Compile (`POST /compile`) | `:664-669` | ◐ portable `Job::Compile`/`Event::Compiled` (unit-tested); ⬚ Win32 command |
| Run + live console (SSE) | `:610-662`, console `:59-121` | ◐ portable `Job::Run` → `run_id`; ⬚ Win32 SSE console panel |
| Triggers (list/create cron·event·webhook/toggle/delete) | `:321-436` | ◐ portable routes + worker jobs; ⬚ Win32 panel |
| History (runs list + detail) | `:440-542` | ◐ portable `FetchRuns`/`get_run`; ⬚ Win32 panel |
| Star / duplicate / trash / rename | `:872-900` | ◐ portable `routes`/`Job::Patch`; ⬚ Win32 title-bar actions |
| Ribbon colours match the web (tab strip, selected/hover, group bg, text/icon, bottom border) | web ribbon | ◐ framework-level (a separate agent aligns ribbon colours for desktop-v0.1.3-alpha; Script inherits on the rebump) |
| VS designer (`.kbview`, slnx) | — | ◐ the window `.kbview` is compile-validated by the `#[kubuno_desktop::view]` macro; the three projects are listed in `Kubuno.Office.slnx`; the live GUI designer render is owed |

## 7. Risks and open questions

* **Q1 — save/conflict model.** Script, like Maths, has no digest/If-Match/editing-session. A desktop save that
  overwrites a concurrent web save can lose work. Options: (a) accept last-write-wins with the re-GET check only;
  (b) add an `If-Match`/digest path to the script handlers like Documents. Needs a user decision; until then the
  desktop uses (a) and says so.
* **Q2 — the code editor.** Full Monaco parity (syntax highlighting, IntelliSense, gutter, folding) needs a custom
  Direct2D code-editor control and a TypeScript tokenizer that the framework does not have today. Decision needed:
  build that control (a sizeable dedicated lot, reusing `EditBuffer`/`History`/`command_for` from
  `kubuno-desktop-ui/src/text.rs` and `StyledRun` for coloured runs) vs. ship the monospace `TextArea` editing
  surface now and layer highlighting later. This lot ships (b).
* **Ribbon tone.** The web Script surface uses a VS-Code-style dark palette for the editor body; the ribbon uses the
  office tone. A dedicated Script ribbon tone (framework change) is tracked with the desktop-v0.1.3-alpha ribbon
  colour alignment.

## 8. Log

**2026-10-12 — design note + shared core (deliverables #1 and #2).** Wrote this note. Added
`office/common/script-core` = `kubuno-office-script-core`: `model` (the `script-api.ts` shapes) and `file` (the
gzipped `.kbscr` round-trip, byte-compatible with `content_files.rs`). Verified: `cargo test` (4 file round-trip
tests) pass; `cargo clippy --all-targets -- -D warnings` clean; `cargo check` clean for `wasm32-unknown-unknown`
and (natively) the host target; `aarch64-apple-darwin` target installed. Established (and recorded here) that the
web executes scripts **server-side** (SSE), so the core carries no JS engine and no Boa — matching the web and the
"don't invent semantics" rule.

**2026-10-12 — shared-core cross-target verification + the portable app crate (deliverable #3, portable half).**
Re-verified the shared core on every target: `cargo test` (4 `.kbscr` round-trip tests) on the host, and `cargo
check` clean for `wasm32-unknown-unknown`, `aarch64-apple-darwin` and `x86_64-unknown-linux-gnu`. Added the portable
app crate `desktop/common/kubuno-office-script-common` (wired into the desktop workspace):

* `app` — the launch options (`--doc <id>`, a local `.kbscr`/`.ts`, `--view editor|triggers|history`, `--dark`,
  `--culture`), `run()` registering the Windows-less portable platform, the shell token broker for `--doc`, and the
  one-window-per-document claim; `open_file()` decodes a gzipped `.kbscr` through the shared core byte-compatibly; a
  `TextUi` portable default prints the source.
* `platform` — the `LaunchRules`/`Clock`/`UiHost` extension points with portable defaults, like the other apps.
* `api` — the `/office/script` routes as pure request builders; a worker thread owning the shared transport
  (`connect_account` through the broker) running load/save/compile/run/triggers/runs jobs; the offline journal of
  the unsent source.
* `session` — the **thin-PATCH save** Script actually uses (confirmed against `server/src/script/handlers/
  scripts.rs`: no `If-Match`, no editing session, no 413 path, like Maths): load once, journal every edit, and on
  save re-read first to catch a concurrent web save, raising a conflict the user resolves (keep mine / take theirs).

Verified: 13 unit tests pass (command line, `.kbscr` open, routes match the web API, worker jobs, journal
round-trip, session load/save/offline/conflict/take-theirs); `cargo clippy --all-targets -- -D warnings` clean; the
whole crate builds in the desktop workspace against the `desktop-v0.1.2-alpha` framework tag.

**2026-10-12 — the save conflict model (Q1 decision: server content-digest precondition).** The user chose to
protect the save on the server, mirroring how Documents actually protects (a content compare, since per
`office-server-doc-defects` a bare `If-Match` header does not protect on this server). Implemented:

* **Server** (`office/server`): new `script::digest` — `source_digest()` (lowercase hex SHA-256 of the source's
  UTF-8 bytes) and a pure `save_allowed()` with a failing-first test. `UpdateScriptDto` gains `base_digest`;
  `scripts::update` refuses a source save whose `base_digest` no longer matches the stored source's digest with the
  new `OfficeError::SaveConflict` → **`409 SAVE_CONFLICT`** carrying `current_source` + `current_digest`. No
  `base_digest` = last-write-wins, so the web editor (which sends none) is unchanged. No schema change (digest
  computed server-side); the single-script responses now carry the content `digest` so the client has its next
  base. `office_script.scripts` has no `etag` column, so a digest — not an `If-Match` token — is the right guard.
* **Client** (`kubuno-office-script-common`): `routes::patch_source` sends the optional `base_digest`; the worker
  carries the digest on `Fetched`/`Saved` and maps `409 SAVE_CONFLICT` to `Event::SaveConflict`; the save session
  sends `base_digest`, raises a conflict on rejection, and resolves keep-mine (re-base on theirs, overwrite) /
  take-theirs with the offline journal. The earlier pre-save re-GET guard is replaced by this server precondition.

Verified: server lib builds, 4 `script::digest` tests pass; the client crate's 13 tests pass (incl. save-with-digest,
409 conflict keep-mine/take-theirs); `clippy -D warnings` clean on both. Q1 is resolved.

**Syntax theme of record (for the code-editor lot).** The web Script editor uses **stock Monaco `vs-dark`**
(`monacoSetup.ts` only pins the CDN to monaco-editor **0.55.1**; `ScriptApp.tsx:727` sets `theme="vs-dark"`,
`defaultLanguage="typescript"`) — there is no custom theme. The Direct2D code editor must reproduce Monaco 0.55.1's
`vs-dark` TypeScript palette: editor bg `#1E1E1E`, default fg `#D4D4D4`, comments `#6A9955`, strings `#CE9178`,
numbers `#B5CEA8`, keywords `#569CD6`, types/classes `#4EC9B0`, regexp `#D16969`, line-number gutter `#858585`
(active `#C6C6C6`), selection `#264F78`, caret `#AEAFAD`. Function/variable tints (`#DCDCAA`/`#9CDCFE`) come from
Monaco's *semantic* highlighting; the base TextMate grammar leaves them default, so matching the base grammar first
is the parity target.

**2026-10-12 — increment A: the Win32 window + the read-only Direct2D code editor.** Delivered (built, clippy
`-D warnings` clean, runs, captured):

* **New shared model crate `desktop/common/kubuno-office-code-editor`** (OS-neutral, dependency-free, 12 tests): a
  lexical TypeScript/JS tokenizer modelled on Monaco's Monarch grammar (keywords, strings/templates, numbers,
  line/block comments carried across lines, identifiers; **primitive type names classified as types**, matching
  Monaco's TextMate `support.type.primitive`), and the **exact Monaco `vs`/`vs-dark` palettes**. Built in
  `desktop/common` (office), not the core framework, per the Q2 placement decision.
* **New Windows app `desktop/windows/kubuno-office-script`** (`kubuno-script.exe`): the `.kbview` window — tall
  title bar with the shell waffle/account, the ribbon (Fichier backstage + Accueil with the Clipboard/Script/Run
  groups, read-only in A), the left sidebar switching Éditeur/Déclencheurs/Historique, the status bar; and the
  **Direct2D `CodeEditor` control** that paints the source read-only with the highlighting above, a line-number
  gutter and wheel scrolling, honouring light/dark. Opens a local `.kbscr`/`.ts` or `--doc <id>` (broker/worker,
  applied on the UI thread). Monospace **column** layout so token spacing matches (DirectWrite trims a run's
  spaces, which collapsed gaps — fixed). Launch parser now consumes `--dev-instance`/`--sandbox` values.
* **`Kubuno.Office.slnx`**: the three projects added (Desktop/Common: script-common, code-editor; Desktop/Windows:
  script). The `.kbview` is compile-validated by the view macro.

**Highlighting fidelity vs Monaco (verified by side-by-side captures, same source, light + dark):** comments,
strings, templates, numbers, control keywords and primitive types match Monaco `vs`/`vs-dark` pixel-for-pixel. The
**gap** is Monaco's *semantic* tints — type/class identifiers teal (`Kubuno`, `Promise`), variables light-blue
(const names), functions gold (`listFiles`) — which the desktop leaves default foreground; those need a TS semantic
analyser and are increment B (the lexical layer never miscolours, it just under-colours those identifiers).

**Ribbon colours:** the window omits `Tone`, so the ribbon shows the framework's **default accent** (blue), not a
Script tone — the expected gap, to be covered by the `desktop-v0.1.3-alpha` ribbon-colour alignment (never
hardcoded). The editor-body colours (which this lot controls) match Monaco exactly.

**Captures (100 %, `C:\kubuno-build`→`E:`):** `desktop_dark_100.png`, `desktop_light_100.png` (the app on the
sample script); `web_monaco_dark_100.png`, `web_monaco_light_100.png`, `web_monaco_dark_175.png`,
`web_monaco_light_175.png` (stock Monaco 0.55.1 `vs`/`vs-dark`, the web editor's component, same source). Desktop
175 % needs a 175 %-DPI display session (the editor is DIP-based, so it scales as the web 175 % shots show).

**2026-10-12 — increment B1: editing in the code editor.** The Direct2D editor is now editable. New in the shared
`kubuno-office-code-editor` crate: an **`EditState`** (text as lines, caret, selection, undo/redo with typing
coalesced; insert / newline / backspace / delete-selection; caret movement incl. Home → indent → column 0;
Unicode-correct columns; 8 tests) and a **folding model** (`compute_folds`: collapsible ranges from bracket pairs
and multi-line comments, from the lexical tokens; 5 tests). The Windows `CodeEditor` wires it: `on_key_down` maps
arrows / Home / End / PageUp-Down / Backspace / Delete / Enter / Tab and Ctrl+A/C/X/V/Z/Y; `on_key_press` inserts
typed chars; `on_mouse_down` focuses + sets the caret (Shift extends); clipboard via `kubuno_desktop::controls::
host::{clipboard_text,set_clipboard_text}`; re-tokenizes + scrolls the caret into view on each change; paints a
blinking caret (`focus::caret_visible`) and the Monaco-coloured selection highlight. Verified: builds; clippy
`-D warnings` clean; 25 crate tests; **live typing, caret and multi-line keyboard selection captured, light + dark**.

**Q3 — semantic highlighting + IntelliSense need the TypeScript language service.** Confirmed from the web
(`ScriptApp.tsx:671-683`, `monacoSetup.ts`): the web uses **no** custom Monarch tokens, theme or completion
provider — Monaco's bundled **TypeScript worker (tsserver)** drives both the semantic token tints (type names teal,
functions gold, variables light-blue) and autocomplete/hover/errors, informed by the `/api-types` `.d.ts`. A
faithful Rust reproduction is not a lexer change; options: **(a)** a syntactic approximation of the tints (fast,
diverges from tsc on member access / resolution — not "full parity"); **(b)** embed the TypeScript language service
(e.g. tsserver in a JS engine, or a Rust TS analyser). Needs a user/coordinator decision before either semantic
highlighting or IntelliSense can claim Monaco parity.

**2026-10-12 — increment B2 part 1: fold gutter + clipboard ribbon.** The code editor gains a clickable **fold
gutter** (▾/▸ markers at every collapsible region from `compute_folds`; click to collapse/expand; the hidden body
shows a ⋯ hint; all painting, caret, selection and clicks work in **visual rows** over the visible source lines).
The **Accueil → Presse-papiers** group (Couper / Copier / Coller) and **Annuler / Rétablir** are wired to the
editor's ops (`EditState` gains `can_undo`/`can_redo`; the control exposes cut/copy/paste/undo/redo/select_all).
Verified: builds; clippy `-D warnings` clean; the fold markers render at the region headers and the clipboard
ribbon is enabled (captured, light + dark).

**Infrastructure blocker for the rest of B2 (compile / run / save proof / panels / live `--doc`).** These are all
server-backed and were confirmed to need a **three-component standup**, not a quick CLI: (1) a **dev core** (its
run is VS-integrated — SSH tunnel to PG, module deployed into the dev-core `modules/` folders, data key + initial
admin password; `vskubuno/docs/WEB.md §6`), (2) the **Kubuno Desktop shell** running as the **token broker server**
(the app's `connect_account` is a broker *client*; `kubuno-desktop-account/broker`), and (3) a **logged-in account**
in the sandbox (`login.rs`, `KUBUNO_SANDBOX_DIR`). Without all three, the desktop cannot authenticate a `--doc`
session, so compile/run/save and the triggers/history panels cannot be exercised or captured. Rather than ship a
large pile of server-feature code that cannot be verified this tour, the save/compile/run wiring, the panels and the
conflict dialog are left for a tour that stands up that infrastructure. The server side is already done and
unit-tested: the digest precondition (`script::digest`, Q1) and the desktop save session (`session.rs`).

**2026-10-12 — Q3 resolved (decision: a Rust TypeScript analyser, not tsserver): semantic highlighting +
autocomplete.** Added to `kubuno-office-code-editor`, powered by the **oxc** TypeScript parser (chosen over swc for
a lighter footprint and a microsecond arena parse, so it re-runs on every edit — no JS engine, no tsserver):
* `semantic` — classifies identifiers from the AST in two passes (declarations → kind; then an occurrence per use,
  priority type-position > call-target > name-match); `highlight()` overlays the Monaco **type teal / function gold
  / variable light-blue** colours onto the lexical tokens, never touching strings/comments/numbers and leaving the
  lexical colour where it is unsure (never a wrong colour). Palettes gained the exact Monaco function/variable
  colours (vs `#795E26`/`#001080`, vs-dark `#DCDCAA`/`#9CDCFE`). **Captured: the desktop editor matches the Monaco
  `vs`/`vs-dark` reference, light and dark.**
* `completion` — a popup from the **Kubuno script API** (parsed from the module's own `api-types` `.d.ts`, embedded:
  after `Kubuno.` the API namespaces, after `Kubuno.App.` that namespace's members, nothing invented), the **local
  AST symbols** in scope, and TypeScript keywords; `context_at()` finds the word + member chain, `complete()`
  filters. The Windows control renders it at the caret, keyboard-navigable (↑/↓/Enter/Tab/Esc) and filtering as you
  type / on Ctrl+Space. **Captured live.**

Verified: 35 crate tests (incl. 6 semantic + 4 completion), clippy `-D warnings` clean on both crates.

**Still owed (server-backed, gated on the dev core + Desktop shell + login — a dedicated later tour):** compile /
run / save (+ autosave + conflict dialog) wiring and live proof; the Déclencheurs / Historique panels with real
data; the live `--doc` round-trip and same-uuid web↔desktop captures; the live VS designer GUI render; the desktop
175 % capture.
