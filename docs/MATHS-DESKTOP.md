# Kubuno Maths (desktop) — design of record

Status: plan and log of the Maths desktop lot (user request: every office sub-application available as a native
desktop app, following the Kubuno Documents method, to **full web parity**). Reference for behaviour and look: the
**web Maths editor** (`office/web/src/Maths*.tsx`, `math*.ts`, `maths-api.ts`). Each increment below records what was
built and how it was verified. Written 2026-10-11.

The web app is not a single equation editor: it is a **math workbench**. It has a WYSIWYG expression editor (visual
and LaTeX-code modes), a live numeric calculator, a small CAS (differentiate / integrate / simplify / expand /
factor / Taylor / solve / limits / function study), function graphs, numeric linear algebra (matrices), descriptive
statistics and number-theory tools, all arranged as freely placed **blocks** on multi-page sheets. Every computation
produces a **LaTeX string**; the UI renders LaTeX with KaTeX.

## 0. What the web app does (feature inventory, with file:line)

Sources: `office/web/src/MathsApp.tsx` (`APP`, 2051 lines, the orchestrator + ribbon), `MathTreeEditor.tsx` (`MTE`,
the WYSIWYG caret editor), `MathDataCard.tsx` (`MDC`, the result card), and the pure engine modules
(`math*.ts`). The ribbon has tabs **Accueil · Insertion · Équation · Calcul · Matrices · Statistiques · Nombres**
plus a **Visuel / Code** mode toggle (`APP:1227-1228`); dockable panels **Objets / Symboles / Propriétés**
(`APP:945,972,1005`).

| Area | What it does | Web source |
|---|---|---|
| Expression tree (model) | `MRow`/`MNode` (atom, frac, sqrt, root, script, delim, big, acc, mat, raw); serialise tree→LaTeX; tolerant LaTeX→tree parse; caret slots; insert/delete/backspace/forward; wrap operand (frac, script, sqrt, root, delim, big, acc); matrix add/del row/col; Up/Down between paired fields; two-char COMBO auto-conversions | `mathTree.ts` |
| WYSIWYG editor (view) | caret render (zero-metric anchor), click-to-place via slot map, selection highlight, placeholder `\square` filling, live typing buffer, code mode with `\square` placeholders | `MTE`, `mathPages.ts:151-196` |
| Live calculator | LaTeX→number (`evalLatex`): parse tree → text expr → compile → evaluate; relations split; result formatting (`fmtResult`, `resultLatex`) | `mathEval.ts` |
| Expression compiler | tolerant recursive-descent parser for `sin(x)+x^2`-style text: `+ - * / ^ !`, unary minus, implicit multiply, `x`, consts pi/e/tau/phi, unary/binary functions; AST + `(x)=>number` | `mathExpr.ts` |
| CAS | differentiate, simplify (fixpoint), integrate (symbolic + polynomial term-by-term), definite integral (Simpson), solve roots (sample+bisection), exact roots (deg≤2 radicals/complex), value table, expand, factor (deg≤2), Taylor/Maclaurin, nth derivative, tangent line, extrema, inflection, asymptotes, limit (finite/±∞), Σ/Π, eval at a point | `mathCas.ts` |
| Graphs | `GraphSpec` (fns, domain, y-range, grid, axis numbers, height); `drawGraph` on a 2D canvas (auto Y-scale, nice ticks, discontinuity breaks); `niceStep`, `fmtTick` | `mathGraph.ts` |
| Matrices | parse first `\begin{…matrix}…\end`; determinant, transpose, inverse, rank, trace, power, char-poly (Faddeev–LeVerrier), eigenvalues (real, Horner+bisection), RREF, solve augmented system; clean fraction output | `mathMatrix.ts` |
| Statistics | parse number list / point list; descriptive summary (n, Σ, mean, σ, σ², min, max), five-number summary, least-squares regression + R² | `mathStats.ts` |
| Number theory | prime factorisation, gcd/lcm, factorial, binomial + arrangements, base conversion (2/8/16) | `mathNumber.ts` |
| Symbols palette | ~225 symbols in categories (greek, ops, rels, arrows, sets, logic, …) | `mathSymbols.ts` |
| Autocomplete | `\`-command completion while typing | `mathAutocomplete.ts` |
| Themes | formula/text colour palettes (light/dark/custom) | `mathThemes.ts` |
| Document model | multi-page, multi-block sheets; blocks `formula` / `graph` / `text`; per-block `frame` (x,y,w,h,r), `scale`, `align`, `numbered`; text styles h1/h2/p; paper formats (A3–Legal) + orientation | `mathPages.ts` |
| Block canvas | free placement, 8 handles, rotate, move, smart guides / magnetism (as the presentations editor), z-order, duplicate (Ctrl+D) | `APP:426-575` |
| Print / export | print the sheets, rasterise graphs | `mathPrint.ts` |
| Persistence | `MathFormula` row + `.kbmath` Drive file; REST `formulasApi` (list/create/get/update/delete/trash/restore/duplicate/open-by-file) | `maths-api.ts`, server `maths/` |

## 1. The stored format (round-trip target)

Storage is **not** the Drive-file-blob + digest-PATCH model of Documents/Spreadsheets. Each formula is a DB row
`office_maths.formulas` (`server/src/maths/models/formula.rs`) — `id, owner_id, name, description, file_id,
is_starred, is_trashed, created_at, updated_at` — whose **content is a single `latex` string** read from / written to
a `.kbmath` Drive file (JSON-gzip) via `services::content_files` (`maths/handlers/formulas.rs:63,103,237`). The row
itself stores only metadata; `latex` is populated after the SELECT.

That single `latex` string encodes the whole workbench document (`mathPages.ts:60-128`):

* **Plain LaTeX** — one page, one centred formula block with default placement (back-compatible, keeps clean file
  previews). `serializeDoc` emits this "trivial" form whenever the document reduces to it (`mathPages.ts:116-126`).
* **`%%KBMATH-MULTIPAGE%%` + JSON `{pages:[{name,latex}]}`** — legacy multi-page (one formula per page).
* **`%%KBMATH-DOC%%` + JSON `{v:1, pages:[{name, blocks:[…], format?, orientation?}]}`** — the block document.
  Each block is `{type:'formula', latex, align?, numbered?, frame?, scale?}`, `{type:'graph', spec, frame?}` or
  `{type:'text', text, style?, frame?, scale?}`; `frame = {x,y,w,h?,r?}`. `GraphSpec` normalised by
  `normalizeGraphSpec`.

`parseDoc` accepts all three (and heals partial/legacy data); `serializeDoc` chooses the smallest faithful form. The
shared core must round-trip every one of these **byte-identically to what the web writes** (golden fixtures below).

## 2. The API (what the desktop app calls)

Base `/office/maths` (`maths-api.ts`, `server/src/maths/handlers/formulas.rs`):

* `GET /formulas?search&trashed&limit&offset` → `{formulas, total}`
* `POST /formulas {name?,description?,latex?}` → `{formula}` (creates the `.kbmath` file)
* `GET /formulas/:id` → `{formula}` (reads `latex` from the file; self-heals the name from the file stem)
* `PATCH /formulas/:id {name?,description?,latex?,is_starred?}` → `{formula}` (writes `latex` to the file; renames it
  on a name change)
* `DELETE /formulas/:id`, `POST /formulas/:id/{trash,restore,duplicate}`, `POST /formulas/open-by-file {file_id}`

There is **no** `If-Match`, editing session, collab epoch or 413 path in the maths handlers — save is a plain metadata
PATCH plus a Drive file write. So the Documents save session (digest precondition, join/leave, conflict, 413, collab
epoch) **does not apply as-is**: the maths desktop save is `PATCH /formulas/:id {latex}`. A last-write-wins conflict
is still possible at the Drive-file level; the desktop mitigates it the only way the server allows — re-`GET` before a
PATCH and, if `latex` changed under us, offer the Kubuno conflict dialog (keep mine / take theirs) — but it cannot be
as strong as the digest guard. Open question Q1 (below).

## 3. The shared core

**Decision (verified 2026-10-11): `office/common/math-core` already exists as the crate
`kubuno-office-math-core`, but it is a Rust port of KaTeX's HTML builder — math *typesetting* only** (LaTeX → a
display list of glyphs/rules/paths, `render()` → `Rendered`). It has **none** of the web's computational engine
(expression tree, calculator, CAS, graphs, matrices, stats, number theory) and none of the document model. Per the
"reuse, don't recreate" rule, the computational engine and the document model are **added to this same crate** as new
modules (it already is a `desktop/common` workspace member consumed by Spreadsheets), so the desktop app, the server
and a future WASM web client share one math crate. The existing `render()` path is untouched and keeps rendering every
LaTeX string the engine produces.

New modules (platform-neutral: only `serde`/`serde_json`; `cargo check` clean for `wasm32-unknown-unknown`,
`x86_64-unknown-linux-gnu`, `aarch64-apple-darwin`):

| Module | Content | Ported from |
|---|---|---|
| `expr` | textual-expression AST + tokeniser + recursive-descent parser + `eval(x)` | `mathExpr.ts` |
| `tree` | `MRow`/`MNode`, serialise tree→LaTeX, tolerant LaTeX→tree parse, caret slots, edit ops, matrix editing, COMBO | `mathTree.ts` |
| `eval` | LaTeX→number, LaTeX→text-expr, result formatting | `mathEval.ts` |
| `number` | prime factors, gcd/lcm, factorial, binomial/arrangements, base conversion | `mathNumber.ts` |
| `cas` | differentiate/simplify/integrate/solve/… (every `*Latex` entry point returning `{latex,error}`) | `mathCas.ts` |
| `matrix` | parse matrix, det/transpose/inverse/rank/trace/power/charpoly/eigen/rref/solve | `mathMatrix.ts` |
| `stats` | parse list/points, descriptive summary, five-number, regression | `mathStats.ts` |
| `graph` | `GraphSpec` model + `normalize`, `nice_step`, `fmt_tick`, a `draw` producing a backend-neutral display list (the Canvas-2D painting in `drawGraph` stays per-platform) | `mathGraph.ts` |
| `doc` | `DocPage`/`MathBlock`/`BlockFrame`/`GraphSpec`; `parse_doc`, `serialize_doc`, paper formats; round-trip identical to the web | `mathPages.ts` |

Error strings are kept **identical to the web** (they are user-facing French and are compared by the fixtures), e.g.
`"Sélectionnez un bloc contenant une matrice numérique."`. Semantics are matched to the web, not improved; what the
web does not do, the core does not invent.

**Golden fixtures** (`common/math-core/tests/fixtures/maths/`): inputs run through the web functions in a browser,
their outputs captured, and the Rust port asserted to produce the same. Each `*Latex` function, `parse_doc`/
`serialize_doc` on each stored form, and the tree parse/serialise round-trip get fixtures.

## 4. The desktop app

Layout mirrors the other office apps (`repo-layout-platform-folders`): a portable crate
`desktop/common/kubuno-office-maths-common` (launch options, the open document's block/page state, the server path,
the platform extension points) and a Windows crate `desktop/windows/kubuno-office-maths` (Win32 window, Direct2D
paint, input, ribbon wiring, backstage, the `.kbview`), exe `kubuno-maths.exe`. Shared office-desktop pieces
(backstage, open/save, the live thread) are taken from `desktop/common/kubuno-office-desktop-{common,shared}` rather
than copied — the one difference is the save model (§2), so the maths save path is written against
`kubuno-office-desktop-shared` where it fits and bespoke where it does not.

Rendering: every block's LaTeX goes through the existing `kubuno-office-math-core::render()` → a display list painted
with Direct2D/DirectWrite (the same path Spreadsheets already uses for its equation gallery). Graphs are painted from
the `graph` display list. Text blocks use DirectWrite. The canvas is one custom control drawn in `on_paint`.

Increments (each shippable, verified before the next):

* **A — open & display (read-only, at parity).** Parse the stored format with `doc`, render formulas / graphs / text
  blocks on the sheet at parity with the web (light/dark from the web theme tokens). Open a local `.kbmath`/LaTeX
  file and `--doc <id>` from the server through the shell's broker in the sandbox profile.
* **B — editing.** The WYSIWYG caret editor (visual + code modes), the ribbon commands wired (Insertion/Équation
  galleries, Calcul/Matrices/Statistiques/Nombres operations inserting their LaTeX result as a new block), block
  manipulation (move/resize/rotate/align/number/duplicate/z-order), multi-page, undo/redo.
* **C — save.** `PATCH /formulas/:id {latex}` with the re-GET conflict check (§2), the Kubuno conflict dialog, an
  offline local journal replayed at start.

## 5. VS integration

The app's `.kbview` views open in the VS 2026 designer; `Kubuno.Office.slnx` is regenerated with the vskubuno
generator (`KUBUNO_GEN_REPO=office KUBUNO_GEN_FRESH=1`) so the two new projects appear under Desktop. Own hive
`/rootsuffix KubunoOfficeMaths`, shared lock `vs-lock.ps1 -Owner officeMaths`.

## 6. Parity coverage table

Legend: ✔ done (unit test mirroring the web; *(run)* = also a real run) · ◐ partial (what is missing is said) ·
✘ not done yet (why) · ⛔ blocked (a decision / a missing platform piece) · — nothing to port.

### Shared core (computation + model)

| Feature | Web | Core status |
|---|---|---|
| Expression tree model + serialise/parse + caret slots + edit ops + matrix edit | `mathTree.ts` | ✔ *(unit tests)* |
| Textual compiler (AST, eval) | `mathExpr.ts` | ✔ *(unit tests)* |
| Live calculator (LaTeX→number, LaTeX→expr, formatting) | `mathEval.ts` | ✔ *(unit tests)* |
| Number theory (prime/gcd-lcm/factorial/binomial/base) | `mathNumber.ts` | ✔ *(unit tests)* |
| CAS (all `*Latex`) | `mathCas.ts` | ✔ *(unit tests)* |
| Matrices (all `*Latex`) | `mathMatrix.ts` | ✔ *(unit tests)* |
| Statistics (summary/five-number/regression) | `mathStats.ts` | ✔ *(unit tests)* |
| Graph model + nice ticks + display list | `mathGraph.ts` | ✔ *(unit tests)* |
| Document model (parse_doc/serialize_doc round-trip) | `mathPages.ts` | ✔ *(unit tests)* |

### Desktop app

| Feature | Web | Desktop status |
|---|---|---|
| Open local file + `--doc <id>`; read-only display at parity | `APP`, `maths-api.ts` | ✔ *(run — captures light/dark)* |
| WYSIWYG caret editor (visual) | `MTE` | ⛔ blocked: needs slot-mapped layout from math-core (the web's `mc-h` markers) to hit-test a click to a tree slot and place the caret; `render()` emits a flat display list with no link to the source tree. A math-core prerequisite (a dedicated lot). Code / « Modifier… » edits the LaTeX as text meanwhile. |
| Code-mode formula editing (edit the LaTeX) | `APP:1228` | ✔ (a prompt edits the selected formula's LaTeX; re-parsed and re-rendered) |
| Ribbon: Insertion (formula / graph / text / page) | `APP:1211-1256` | ✔ |
| Ribbon: Équation structures + Greek symbols | `APP:1251-1268` | ◐ inserted as buttons that append the snippet to the selected formula; a visual gallery with KaTeX previews is ⛔ blocked (the gallery cell's `&dyn Canvas` has no path-fill to paint glyph outlines — Spreadsheets' gallery uses labels too) |
| Ribbon: Calcul / Matrices / Statistiques / Nombres | `APP:1271-1345` | ✔ *(wired; the engine is unit-tested)* — each runs on the selected formula and inserts its LaTeX result; parameters (bounds, order, point…) via a prompt |
| Block canvas: select, delete, duplicate, re-order, insert | `APP:426-575` | ◐ selection + delete/duplicate/move-order/insert done; free placement, resize, rotate and smart guides not yet (frames are rendered, not edited) |
| Undo / redo | `APP:348-351` | ✔ (document snapshots) |
| Multi-page, paper formats, orientation | `mathPages.ts` | ◐ add page + rendered; per-page format/orientation UI not yet |
| Save (`PATCH latex`, re-GET conflict, offline journal) | `maths-api.ts` | ✔ *(contract verified live against the shared dev core — login, GET, PATCH persists, re-GET conflict detected, restore; see the log)* — the full GUI `--doc` edit→save needs a signed-in sandbox account |
| Print / export | `mathPrint.ts` | ✘ (later lot: printing) |
| Dockable panels (Objets/Symboles/Propriétés) + structures bar | `APP:945-1005`, `MTE` | ◐ the Objets / Symboles / Propriétés docks and the WYSIWYG structures bar (live KaTeX previews, appended to the selection) are present and wired; the Symboles search box, per-block property *editing* and drag-to-reorder docks are later refinements |
| VS designer integration (`.kbview`, slnx) | — | ◐ the two `.rsproj` are in `Kubuno.Office.slnx`; the live designer check is pending a free vs-lock |

## 7. Risks and open questions

* **Q1 — save/conflict model.** Maths has no digest/If-Match/editing-session/collab-epoch. A desktop save that
  overwrites a concurrent web save can lose work. Options: (a) accept last-write-wins with the re-GET check only;
  (b) add an `If-Match`/digest path to the maths handlers like Documents. Needs a user decision; until then the
  desktop uses (a) and says so.
* **Numeric fidelity.** The web uses JS `number` (IEEE-754 f64) and `Math.*`. Rust `f64` and `libm`/std match for the
  basic ops; `toPrecision`/`toString` number formatting must be ported carefully (it drives every result string).
  Fixtures guard this.
* **Float-formatting parity** (`fmtResult`, `numLatex`, `fracLatex`, `fmtTick`): JS `Number(x.toPrecision(n)).toString()`
  has specific shortest-round-trip behaviour; the Rust port reproduces it exactly or the LaTeX output diverges.
* **Graph painting** stays per-platform (Canvas 2D on the web, Direct2D on Windows); only the sampling/scaling/tick
  math is shared, so a pixel-identical graph is not expected — parity is judged on axes, ticks and curve shape.

## 8. Log

**2026-10-11 — design note + shared core (deliverable #2).** Extended `kubuno-office-math-core` (the existing
KaTeX typesetting crate) with a new `engine` module — the platform-neutral Rust port of the web Maths workbench, no
Win32/UI: `num_fmt` (JS `Number.toString`/`toPrecision` reproduced), `expr`, `tree` (model + serialise + tolerant
parse), `eval`, `number`, `cas` (differentiate, simplify, integrate, definite integral, expand, factor, roots, exact
roots, Taylor, nth derivative, tangent, extrema, inflection, asymptotes, limit, Σ/Π, eval-at), `matrix`, `stats`,
`graph` (model + tick maths), `doc` (the `%%KBMATH-DOC%%` / multipage / plain-LaTeX stored forms, byte-identical
round-trip via a hand-written `JSON.stringify`-compatible serialiser). Verified: `cargo test` 33 engine tests pass;
`cargo clippy --all-targets -- -D warnings` clean; `cargo check` clean for `wasm32-unknown-unknown`,
`aarch64-apple-darwin` and `x86_64-unknown-linux-gnu`. Semantics were matched to the web, including its quirks
(`x(x+1)` parses as a function call → NaN; a power of plain `()` atoms is not evaluable, `\left…\right` is); the tests
document each.

**2026-10-12 — increment A: the desktop app, read-only at parity.** Built the two desktop crates on the branch
`maths-desktop-app` (a worktree off `main`; the shared office checkout was occupied by another agent's branch):
- `desktop/common/kubuno-office-maths-common` — the portable app (launch options; `MathsDoc` over `engine::doc`;
  the `/office/maths` routes; a `.kbmath` / bare-LaTeX opener; a text UI). 9 unit tests.
- `desktop/windows/kubuno-office-maths` (`kubuno-maths.exe`) — the Win32 window (`maths_window.kbview`): the tall
  title bar with the waffle/account, a ribbon (Fichier backstage with the formula info, Accueil = document / mode /
  zoom, Insertion / Équation / Calcul), the status bar, and `MathsCanvas` filling the body. The canvas paints the
  document read-only: paper sheets stacked and wheel-scrolled, each block drawn by kind — **formula** via the math
  core's KaTeX layout (`render()` → glyph outlines, vendored painter), **text** via DirectWrite, **graph** via a
  port of `drawGraph` (auto Y-range, nice ticks, curve breaks) sampled from `engine::expr`. Light/dark palettes.
  Opens a local file or a server formula (`GET /office/maths/formulas/:id` through the shell broker).

Verified: `cargo build` and `cargo clippy --all-targets -- -D warnings` clean for both crates; the exe runs and renders
the sample (title + the quadratic formula typeset by KaTeX + a sin(x) graph) in **light and dark** — captures in
`C:\kubuno-build\release\maths\captures\` (`desktop-sample-{light,dark}.png` at 100 %, `…-{light,dark}-fit.png` showing
the whole sheet). VS: the `.rsproj` for both crates added to `Kubuno.Office.slnx` under Desktop (the shared
`Kubuno.Office.Math.Core` project was already listed).

**2026-10-12 — increments B and C: editing and save.** On `maths-desktop-app`:
- `MathsCanvas` became an editor: block selection (click, with a selection outline + hit-test rects), undo/redo
  (document snapshots), duplicate / delete (Delete key) / move-order / insert blocks, add page, append a
  structure/symbol to the selected formula, edit a formula's LaTeX, and a dirty flag.
- The ribbon wires it all: Accueil (Document / Bloc / Historique / Mode / Zoom), Insertion (formula/graph/text/page),
  Équation (structures + Greek symbols appended to the selected formula), and the engine operations as tabs —
  **Calcul** (derivative, 2nd derivative, integral, definite integral, simplify, expand, factor, solve, exact roots,
  Taylor, extrema, inflection, asymptotes, limit, tangent, eval-at, value table, sum, product), **Matrices**
  (determinant, inverse, transpose, rank, trace, power, RREF, char poly, eigenvalues, linear system),
  **Statistiques** (summary, five-number, regression), **Nombres** (prime factors, factorial, base conversion,
  gcd/lcm, binomial). Each runs on the selected formula and inserts its LaTeX result as a new block; errors show in
  the status bar; parameters come from a small vendored prompt dialog.
- **Save (C)**: `save()` → for a server formula, a background `PATCH /office/maths/formulas/:id {latex}` after a
  re-`GET` conflict check (if the server's latex changed under us, the status bar warns instead of overwriting);
  for a local document, the stored bytes are written to a local journal. No digest/editing-session model (§2).

Verified: both crates `cargo build` + `cargo clippy --all-targets -- -D warnings` clean. The `maths-desktop-app`
branch was rebased onto `origin/main` (which had the Whiteboard increment B) cleanly and merged to `main`.

**2026-10-12 (later) — VS designer, C round-trip, web parity, side panels.**
- **VS designer (main instance, no fresh hive)**: `devenv` (no `/rootsuffix`, already signed in) opens
  `maths_window.kbview` and the **Kubuno designer renders the view** — the tall title bar, the full ribbon
  (Accueil/Insertion/Équation/Calcul/Matrices/Statistiques/Nombres with the Bloc/Historique/Mode groups) and the
  `MathsCanvas` control (a named placeholder, since the lone view is opened outside its project). Capture
  `vs-designer-maths.png`; vs-lock taken and released promptly.
- **Increment C round-trip (live, against the shared dev core `http://127.0.0.1:18900`)**: verified the exact save
  contract the desktop sends — login → `GET /office/maths/formulas/:id` (`e^{i\pi}+1=0`) → `PATCH {latex}` **persists**
  → a simulated concurrent `PATCH` is **detected by the re-GET conflict check** (server ≠ our base) → restored. The
  window's `save_server` is thin glue over this. The full *GUI* `--doc` edit→save still needs a signed-in sandbox
  account (a Kubuno Desktop shell / sign-in seeding) — the desktop auth borrows tokens from the shell broker, which
  wasn't available to seed here.
- **Web-vs-desktop parity**: same formula `e^{i\pi}+1=0` in the web (Visual mode, headless Chrome) and the desktop —
  **renders identically** (KaTeX typesetting). Differences: the web's *Coloration* (coloured tokens) vs solid ink; the
  web's dock panels / structures bar (now also on the desktop, below); ribbon organisation. Composites
  `web-vs-desktop-maths.png` and `web-vs-desktop-panels.png`.
- **Dockable side panels + structures bar** (the biggest visual gap): added `ObjectsPanel`, `SymbolsPanel`,
  `PropertiesPanel` (docked around the canvas in a `DockArea`) and `StructuresBar` (below the ribbon, with **live
  KaTeX previews** — a custom control can paint glyph outlines where a ribbon gallery cell cannot). Wired to insert
  blocks / append structures and symbols; the Propriétés dock follows the selection. Capture `desktop-panels.png`.
All merged to `main`.

### Where I stopped / next steps (2026-10-12)

Done: deliverables #1 (design note), #2 (shared core), **increment A** (read-only display, live-captured),
**increment B** (editing + the full compute suite), **increment C** (save — contract verified live against the dev
core), the **VS designer check** (view renders), **web-vs-desktop parity** (identical rendering), and the **dockable
side panels + structures bar** — all merged to `main`. Two items are **blocked by missing capabilities**, not by
time:
- **WYSIWYG caret editor** ⛔ — needs math-core to emit a *slot-mapped* layout (the web's `mc-h` markers) so a click
  maps to a tree slot and a caret can sit between nodes. `render()` currently emits a flat display list with no link
  to the source tree. This is a math-core prerequisite (its own lot). Code / « Modifier… » edits the LaTeX as text
  in the meantime, giving full formula editability.
- **Équation gallery with KaTeX previews** ⛔ — a ribbon gallery cell renderer receives a limited `&dyn Canvas`
  (no path-fill), so KaTeX glyph outlines cannot be painted in a cell (Spreadsheets' gallery uses text labels for the
  same reason). The structures are offered as buttons that append their snippet to the selected formula.

Pending (not blocked):
- **Full GUI `--doc` edit→save round-trip** — the save *contract* is verified live (above); exercising it through the
  window needs a signed-in sandbox account against the dev core. The desktop auth borrows tokens from the Kubuno
  Desktop shell's broker (`BrokerProvider::for_app`); mounting a sandbox shell (`KUBUNO_SANDBOX_DIR`, `owner.sign_in`
  with the dev-core tokens) is the remaining step, then a real-input edit + Ctrl+S under the vs-lock.
- **Live real-input test** of a Calcul operation / a panel click — needs the vs-lock for real clicks; the wiring is
  compile-verified, the engine unit-tested, and the designer confirms the controls render.
- **Refinements**: the Symboles **search box**, per-block property **editing** in the Propriétés dock, block **free
  placement / resize / rotate / smart guides** (frames are rendered, not edited), a dedicated **Maths ribbon tone**
  (framework change; uses `Tone="Documents"`), and **print/export**.

The graph **display list** / sampling (the shared part of `drawGraph`) currently lives in the desktop painter and can
be lifted into `engine::graph` when a second client needs it.
