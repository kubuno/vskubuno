# Shared cores: one implementation of each algorithm for web, desktop and mobile

Date: 2026-10-02. Status: **approved on 2026-10-02** (decisions in §8.1); first lots in progress.

Trigger (user, 2026-10-02): « il faudra peut-être partager une partie du code avec la version web pour éviter de
refaire certains algorithmes identiques plusieurs fois (et ceci vaut pour tous les modules avec leurs versions
multi-plateforme) ».

Scope: every module repository (backend Rust + `frontend/` React/TS), the core host (`core/frontend`, core crates),
the desktop repository (`desktop/windows`, `desktop/common`), the mobile repository (`mobile/android`, `mobile/ios`
which is only a README today), `api-spec`.

## 0. Summary

- **The duplication is real and already costs correctness, not only effort.** Reading the code found the same
  algorithm written two to four times in three languages, and the copies disagree: weekly recurrences drift by one
  hour across DST and can land on the wrong weekday (backend expands in UTC, frontend builds `BYDAY` in local time);
  the drive server accepts names Windows cannot write while each client "fixes" them differently; four different
  conflict-copy names; four different retry/backoff schemes; three hand-written ProseMirror models (TS, Rust, Kotlin);
  three iCalendar escape/fold implementations; two subject normalisers for mail threading that disagree with each
  other and with the web; search folding that expands `œ` on the server but not in the eight client copies; and,
  worst, forms whose conditional logic exists only in TypeScript, so the server **skips required-field validation**
  on every form that has a rule (§3).
- **Recommendation: Rust "sans-IO" cores + conformance vectors.** An algorithm needed on two or more platforms is
  written once as a pure Rust crate `kubuno-<domain>-core` (no network, no database, no clock, no UI; data in, data
  out), used natively by the backend and the desktop, compiled to WASM for the browser (shipped as a built artefact,
  so module builds stay Rust-free and offline, exactly like `@kubuno/views-compiler`), and exposed to Kotlin/Swift
  through UniFFI when a mobile app needs it. Each core ships **conformance vectors** (JSON input → expected output)
  that every remaining implementation, in any language, must pass.
- **Start cheaper than that.** Conformance vectors alone (no shared binary) are the first step for every domain:
  they find the divergences, they are the acceptance test of a later Rust core, and they are the only thing needed
  where a platform keeps its own implementation (e.g. Android rendering with Compose).
- **Not a return of the WASM backends.** The 2026-07 "`<module>-core.wasm` for the whole fleet" programme (whole
  module backends with SQLite running in wasmtime on the desktop) was abandoned on 2026-08-16 and stays abandoned.
  A shared core is a small pure library, never a backend, never run in wasmtime, never owning storage or sync
  transport (§4.2).
- **First lots**: (0) the conformance-vector format and runners; (1) **`kubuno-docs-core`** (pilot, already being
  extracted from Documents desktop); (2) **`kubuno-calendar-core`** (recurrence with time zones, fixes the DST bug);
  (3) **drive name/path rules and conflict naming** (fixes the illegal-name class of sync failures). Sizes and order
  in §7.

## 1. How this was established

| What | How |
|---|---|
| Inventory | Code read on the SMB share, per domain: office/notes, drive/sync/auth, calendar/contacts/mail/tasks, the other modules and cross-cutting helpers. File paths and line counts below are from `wc -l` on 2026-10-02. Agreement claims marked "verified" were re-read at the cited lines; the rest come from targeted reads and greps, and negative results ("no implementation found") can miss code with unusual names. |
| Precedents | `docs/WEB-VIEWS.md` §12–14 (views compiler in WASM), `Z:\src\COORDINATION_WASM.md` (the 2026-07 WASM programme and its reversal, messages 47–49), `docs/DESKTOP-OFFLINE-SYNC.md`, desktop `documents/src/doc/parity.rs`, `api-spec`. |
| WASM sizes | Two throwaway crates built under `C:\kubuno-build\agent-shared-cores` (deleted afterwards), `wasm32-unknown-unknown`, the release profile of the views compiler (`opt-level="s"`, LTO, `panic=abort`, strip), no `wasm-opt`: §5.1. |
| Not done | No browser run, no Node benchmark (Node is not installed on this Windows machine), no Android NDK build, no iOS build. Call-cost and startup numbers in §5.1 are therefore estimates from the sizes and from the views compiler, and are labelled as such. |

## 2. Where code runs today

| Platform | Language / runtime | Can run Rust today? |
|---|---|---|
| Module backends, core | Rust (Axum, SQLx) | yes, natively |
| Desktop apps (shell, chat, documents, drive) | Rust, Win32/Direct2D; `desktop/common` multi-OS crates | yes, natively |
| Web (host + module frontends) | TypeScript, React, Vite; modules are bundled as `entry.js` + `entry.css` | yes through WASM (proved by `@kubuno/views-compiler`, 474 KB `.wasm`, committed, Rust-free downstream) |
| Android (`mobile/android`, 6 apps + 5 `core-*` libraries, ≈ 46 k lines of Kotlin) | Kotlin, Compose, Room, WorkManager | not yet: **no NDK, no native library** in the repository today |
| iOS | Swift (planned on the user's Mac) | not started |

Rust already covers three of the five targets natively. The two open questions are the browser (solved once, by
the views compiler) and mobile (unsolved: §5.1.3).

## 3. Inventory

Columns: **W** web, **B** backend, **D** desktop, **A** Android. Value = correctness risk × size × change rate,
graded ★ (low) to ★★★ (high). "—" = no implementation.

### 3.1 Office and notes

| Algorithm | W | B | D | A | Agree? | Value |
|---|---|---|---|---|---|---|
| ProseMirror document model and `.kbdoc` JSON | schema in `office/frontend/src/DocumentEditorPage.tsx`; read by `canvas-engine.ts` | `PmNode` in converters, `handlers/documents.rs` 1308 | `documents/src/model/node.rs` 374 + `mod.rs` 319 (byte-exact round trip, unknown subtrees kept raw) | `app-docs/.../pm/PmModel.kt` 478 + `PmParser.kt` 696 ("names copied from the web") | three hand-written models; two serialisers write the same format differently (browser omits absent keys, serde writes `null`: desktop `tests/fixtures/README.md`) | ★★★ |
| Line breaking, line box, pagination, tables, lists, images | `office/frontend/src/canvas-engine.ts` **4033** + `documents/layout/*` (≈ 2700) | — | `documents/src/doc/para.rs` 960, `tables.rs` 2176, `lists.rs` 751, `images.rs` 915, `convert.rs` 607 — a deliberate quirk-for-quirk port | no engine: Compose renderers `ui/render/Pm*Renderer.kt` (≈ 2200) with constants copied from the web (`DocsDesign.kt`) | desktop kept honest by `parity.rs` (Chrome recordings, 9/9 paragraphs, line ends within 0.28 px on 2026-09-19); not ported yet: floats, drop caps, inline atoms, letter spacing, indents, tab stops; Android uses the platform line breaker (different breaks by construction) | ★★★ |
| Editing, undo/redo, clipboard | ProseMirror + `history/useEditHistory.ts`, `documents/track-changes.ts` 652 | — | `edit/history.rs` 1339, `caret.rs` 1184, `clipboard.rs` 1182 | `DocEditorScreen.kt` 1234 + `DocActions.kt` 1068 | three independent models | ★★ |
| Heading sizes, spacing, line-height ratio 1.15 | `canvas-engine.ts:233-261, 844` | — | `convert.rs:26, 132-135` | `DocsDesign.kt:321, 366-392` | agree today (H6 = 11 pt, bold ≤ level 4) but copied by hand three times; the comment in `parity.rs` is already stale | ★★ (codegen/constants) |
| DOCX / ODT / DOC / XLSX / ODS / ODP import-export | — | `office/src/converters/*` (server only) | through the server | through the server | single implementation | ★ (already shared by the API) |
| Spreadsheet formulas, recalc, fill series, pivot, number formats | `formula-engine.ts` 1611, `formula-refs.ts` 250, `formula-fns/*` (12 files), `fill-series.ts` 383, `pivot-engine.ts`, `data/format.ts` | — (`services/data_engine.rs` is a separate BI evaluator) | — | — | one implementation today | ★★ now, ★★★ the day a second platform needs sheets |
| Autosave, etag, `If-Match`, 412 arbitration | `api.ts` has no `If-Match` (collab websocket) | `handlers/documents.rs:11, 328` | `api/client.rs` defines the header but cannot send it (`kubuno-sync` has no generic PATCH/headers) | `DocsDelta.kt` 223 etag cache, 412 arbitration | three behaviours; shared documents get no etag on Android | ★★ (protocol, see §3.6) |
| Notes body | plain text, no markdown library found | `content: String` | — | — | nothing to share yet | ★ |

### 3.2 Drive and file sync

| Algorithm | W | B | D | A | Agree? | Value |
|---|---|---|---|---|---|---|
| **File/folder name rules** | `maxLength=255` characters only (`RenameModal.tsx:108`, `NewFolderModal.tsx:49`) | folders: empty, > 255 **bytes**, `/ \ \0`, `.`, `..` (`drive/src/services/folders.rs:907-918`, verified); file rename: > **1000** bytes, same chars (`files.rs:390-395, 718`, verified); uploads: `sanitize_filename` (deletes illegal chars) | `kubuno-sync/engine.rs:268-302`: maps illegal chars to `-`, strips trailing dots/spaces, prefixes `_` to reserved names, **Windows only** (`#[cfg(windows)]`) | no check; `File(dir, name)` + `name.part` (`OfflineFiles.kt:96-97`) | **no**: the server accepts `:`, `<>"|?*`, `CON`, trailing dots via rename/create-folder; the desktop maps `a:b` and `a-b` to the same local path; limits differ (255 chars / 255 bytes / 1000 bytes) | ★★★ |
| Case and Unicode normalisation | — | case-sensitive uniqueness (`files.rs:140`, `folders.rs:476`, `kubuno-storage/src/naming.rs` "Linux behaviour"), no NFC | none | none | `A.txt` and `a.txt` coexist on the server and collide on NTFS/APFS; NFD names from macOS never compared | ★★★ |
| Auto-rename and conflict-copy naming | — | `name (2)` (`kubuno-storage/src/naming.rs`) | legacy `{stem} (conflit {HOSTNAME} {epoch}){ext}` (`push.rs:395`, `HOSTNAME` unset on Windows → "desktop"); engine `<title> (conflit {machine} {YYYY-MM-DD HH-MM})` (`kubuno-sync-engine` `adapter.rs:355`) | none (never edits content in place) | four rules | ★★ |
| Content hash | — | SHA-256 (`files.rs:1064`, etag = `content_hash`) | SHA-256 (`push.rs:17, 379`) | SHA-256 (`Entities.kt:117`) | yes | ★ (trivial, constant) |
| Chunked upload | no chunking | 10 MiB default, client may override (`config/settings.rs:90`, `uploads.rs:29`) | legacy: one multipart POST, no chunks; engine: none | 8 MiB, session + next chunk persisted (`Transfers.kt:17`) | three behaviours | ★★ |
| Mass-delete and reorg guards | — | protected folders | `MASS_DELETE_RATIO` 0.2 floor 50, same-etag-survives = reorg, `subtree_really_gone` (`push.rs:44-47, 135-140`) | none found | guards that prevented real data loss exist on one platform only | ★★★ |
| Delta page size, cursor type | — | max 2000 | 500, numeric (legacy); opaque (engine `delta.rs:11`) | 2000, Long | minor | ★ |

### 3.3 Calendar, tasks, contacts, mail

| Algorithm | W | B | D | A | Agree? | Value |
|---|---|---|---|---|---|---|
| **Recurrence expansion** | builds/parses/describes rules: `calendar/frontend/src/rrule.ts` 171 | `calendar/src/services/recurrence_service.rs` 144 (`rrule` 0.12), six callers (events, availability, appointments, rooms ×2, room stats) | — (was in the abandoned `calendar-core.wasm`) | — | **no** (verified): the backend writes `DTSTART:<UTC>Z` and expands in `Tz::UTC` (`recurrence_service.rs:39-60`), ignoring `event.timezone`, so a weekly 09:00 Paris event is at 10:00 after the DST change; the frontend derives `BYDAY`/`BYMONTHDAY` from the **local** date (`rrule.ts:102-111`), so an event at 00:30 Paris on Monday (Sunday 22:30 UTC) is expanded on the wrong day; `UNTIL` is end of day **UTC** (`rrule.ts:115-117`); hard cap `.all(500)` | ★★★ |
| EXDATE / RECURRENCE-ID / VTIMEZONE in iCalendar | — | not exported/imported (`icalendar_service.rs`); TZID lost on import, floating time = UTC, ambiguous autumn hour → UTC | — | — | incomplete | ★★ |
| iCalendar escape/fold | — | three implementations: `icalendar` crate, hand-rolled in calendar `icalendar_service.rs:291-322`, hand-rolled in mail `services/imip.rs:42, 62` | — | — | three | ★★ (backend-only, but same algorithm thrice) |
| Task recurrence | presets `FREQ=…` in `InlineComposer.tsx:79-84` | stored as a string, never validated or expanded; `rrule` and `chrono-tz` are unused dependencies of tasks; `parse_ical_dt` ignores TZID (`tasks/src/services/icalendar_service.rs:33-46`) | — | — | the presets do nothing | ★★ (a calendar-core consumer) |
| vCard | — | hand-written `contacts/src/services/vcard_service.rs` 228, 3.0 only, no output folding, `trim_start` on unfold eats content spaces, parameter values upper-cased, no `item1.` groups | — | — | backend only | ★ now, ★★ when a native contacts app exists |
| MIME parsing | — | `mail-parser` 0.9 at six call sites | — | — (server JSON) | single library | ★ |
| **Subject normalisation (threading)** | `MessageCard.tsx:426` `/^\s*(re|fwd|tr)\s*:\s*/i` | duplicated `normalize_subject` in `server/deliver.rs:707` and `services/sync_service.rs:982`: lower-case, exact `"re: "`, `"fwd: "`, `"fw: "` once (verified) | — | uses server `thread_id` | three behaviours: `RE:x`, `Re[2]:`, `AW:`, `TR :`, `Re: Re:` handled differently | ★★ |
| Reply subject / quoting | `InlineCompose.tsx:127-172` (`startsWith('Re:')`, case-sensitive) | — | — | none (no quoting) | `RE: x` → `Re: RE: x` | ★★ |
| Address parsing | `AddressSuggest.tsx:49` (`lastIndexOf('<')`) | `handlers/addresses/mod.rs:116` | — | `ComposeViewModel.kt:100` (splits on `,`, `;`, space) | three; Android breaks `"Doe, John" <j@x.com>` | ★★ |
| HTML sanitising | DOMPurify, `EmailHtmlView.tsx` 586 (forbid list + hook, "kept in step" by comment) | `ammonia` with a custom allowlist, `services/html_sanitize.rs` 237; but `send.rs:167` and `store.rs:285` still call bare `ammonia::clean` | — | WebView with JS off, no sanitiser, remote images can be loaded | allowlists differ; two server paths bypass the hardened one | ★★★ (security), but see §5.5: keep the browser's DOMPurify, share the **policy** |

### 3.4 Other modules

| Module | Algorithm | Where | Agree? | Value |
|---|---|---|---|---|
| **forms** | conditional logic (12 operators, hidden questions, jumps) | web only: `forms/frontend/src/logic.ts` 135; backend `models/logic.rs` 34 stores strings | **no, and it is a security gap** (verified): because the server cannot evaluate the logic, `forms/src/handlers/public.rs:153-175` **skips the required-question check entirely** on any form that has a rule ("we trust the client"); a direct API submission bypasses required fields | ★★★ (small, server needs it) |
| forms | quiz scoring / input formats | scoring server-only (`services/scoring.rs` 141); email/url/number checked only by HTML inputs | no server format validation | ★★ |
| maps | haversine, bearing, destination point | 7 haversine copies: Rust `gpx_service.rs:360`, TS `greatCircle.ts:16`, `mapMarkers.ts:47`, `mapSketch.ts:27`, `MapsLocalCard.tsx:42`, Kotlin `MapsViewModel.kt:799`; bearing in TS and Kotlin | numerically equal (R = 6 371 000 m) | ★ (tiny, stable: vectors suffice) |
| maps | distance/duration formatting, turn instructions | `routing.ts:132` (1 decimal < 10 km), `mapMarkers.ts:5` (always 1), `mapSketch.ts:83` (2 decimals), Kotlin `DirectionsPanel.kt:280`, `NavOverlay.kt:140` (hard-coded "km", no miles), three Kotlin `formatDuration`; instructions built separately, Kotlin hard-coded French (`MapsViewModel.kt` ≈ 795) | **no** | ★★ (formatting rules + i18n, §3.5) |
| maps | tile math | web only (`mapOffline.ts:17-21`) | — | ★ |
| photos | EXIF, thumbnails, dedup | server `photo_service.rs` 560 (`kamadak-exif`, `image`): **Orientation is never read**, so thumbnails of rotated phone photos come out unrotated; thumbnail size 256 (`instance.rs:50`) vs 400 (`settings.rs:75`); SHA-256 dedup on server and Android | one implementation, with a bug | ★ for sharing (server-side), fix in place |
| chat | formatting, mentions | no markdown, no mention parser on any side | nothing to share yet; when added, one core from day one | ★ (future ★★) |
| keestore | KDBX crypto, Argon2 | browser only (`kdbxweb` + `hash-wasm`, already WASM); server is a blob store; `argon2`, `aes-gcm`, `totp-rs` declared in `keestore/Cargo.toml` but unused | no drift (parameters come from the KDBX header) | ★ now; ★★★ the day a native keestore client exists (a Rust KDBX core would then be the one to trust) |
| flow | `{{ }}` expression language | Rust only, `flow/src/runtime/expr.rs` 827; the frontend reads catalogues from the API | single implementation | ★ (a future editor-side checker would reuse it through WASM) |
| notes, wiki | markdown rendering | server `pulldown-cmark` 0.11 with `SMART_PUNCTUATION` (`notes/src/services/markdown_service.rs`, `wiki/src/.../wiki_markup.rs` 572); client `react-markdown` + `remark-gfm` without smart punctuation | **no** (quotes and dashes render differently) | ★★ |
| code | — | Monaco on the web, git services on the server | nothing duplicated | — |

### 3.5 Cross-cutting

| Topic | Where | Agree? | Verdict |
|---|---|---|---|
| **Search folding** (accents, ligatures) | server `kubuno-db/src/search.rs` 654 (NFD, marks dropped, **œ/æ/ß expanded**, stemmer); Postgres `unaccent` still called directly by mail (`filters.rs:87-98`, `search_query.rs:501+`); ≥ 8 JS copies (`ui/uiText.ts:97`, `ui/mention/foldHighlight.ts`, `RoleCreateDialog.tsx:13`, `MapsSearchDropdown.tsx:27`, `chat/ExpressionPanel.tsx:80`, `office/fill-series.ts`, `app/store.ts`…) with two regexes (`[̀-ͯ]` vs `\p{Diacritic}`); none in Kotlin | **no**: "coeur" finds "cœur" on the server, not in a client filter | core-repo `kubuno-text` fold (small): server native, web through a host package (or a TS port held by vectors: the function is ≈ 30 lines), Kotlin through vectors |
| Byte sizes | 20+ copies: base 1024 French units in core `utils/format.ts`, drive `api/format.ts` (localised), chat ×2, mail ×3, admin ×3, p2pnas; English units in keestore, forum, core widgets; Android `core-ui/Format.kt` (correct), mail ×2, **`app-photos/PhotoViewer.kt:268` base 1000** | **no** (units, decimals, base) | not a Rust core: one TS helper in `@kubuno/sdk` and one in `core-ui`, held by a shared vector file; delete the copies |
| Dates, relative time | `core/frontend/src/core/intl/datetime.ts` (intents over `Intl`, the right design); forum uses the browser locale instead of the app locale, p2pnas hard-codes French, mail has a third variant | mostly | keep platform-native (`Intl`, `java.time`/ICU, Foundation); share the **intent vocabulary** (`date`, `weekdayDate`, …) as generated data |
| Plurals, i18n | web i18next with CLDR `_one`/`_other` keys in core; maps and photos have no plural keys ("2 photo"); Android: 5 strings in resources, no `<plurals>`, mostly hard-coded French; desktop: no i18n | **no** | platform-native plural rules (CLDR is in every platform); share the **catalogue** (one source per module, generated to i18next JSON, Android resources, desktop resources) — a codegen lot, not a core |
| Permissions / ACL | server `kubuno-core/src/authz/` 2149; clients only test `role === 'admin'` | n/a (no client evaluator) | keep server-only; clients ask the server ("can I") instead of re-evaluating |
| Shared UI copied between module frontends | `collab/presence.tsx` byte-identical in app and flow, near-identical in core and office; `CollaboratorsDialog.tsx` in forms and office; `ShareDialog.tsx` core vs app | — | not an algorithm question: these belong in `@kubuno/ui`/`@kubuno/sdk` (module isolation forbids the copies only when they import each other, but they will drift) |

### 3.6 Client protocol: API client, tokens, offline sync

| Algorithm | D (`desktop/common`) | A (`mobile/android`) | Agree? | Value |
|---|---|---|---|---|
| Token refresh state machine | `kubuno-account` `owner.rs:53-55`: 300 s fresh window, 45 s cooldown, 60 s margin, single-flight, persist the rotated pair before use, only 401/403 = session ended | `core-api/TokenManager.kt` 208, `:45-49`: same numbers and rules | **yes**, by careful copying (Android was ported from desktop `api.rs`) | ★★ (small, but a mistake revokes the token family) |
| Idempotency keys, 412 = conflict, 404 on trash = done | `kubuno-sync/api.rs:861-980`, `kubuno-sync-engine` | `Entities.kt:30`, `OutboxDrain.kt:81` | yes | ★★ |
| Pull loop (page + cursor in one transaction, stop if the cursor does not move, unknown kind skipped) | `kubuno-sync-engine` (`engine.rs` 1298) | `core-sync/SyncEngine.kt` 88 | yes in intent; page sizes differ | ★★ |
| Outbox classification, coalescing, rollback, backoff | `outbox.rs` 519: `min(2^n × 2 s, 15 min)` ± 20 %, `Retry-After`, no give-up, explicit rollback | `OutboxDrain.kt` 145: WorkManager `retry()`, 3–5 attempts then the intent is lost (a mistake already listed in DESKTOP-OFFLINE-SYNC §4.1) | **no** | ★★★ |
| HTTP retry | `kubuno-api-client` `client.rs:37`: 4 attempts, 500 ms base, 30 s cap, ± 20 % | none explicit; websocket reconnect flat 5 s | no | ★ |
| Field-level three-way merge (DESKTOP-OFFLINE-SYNC §7.4) | `conflict.rs` 127 | — | one side | ★★★ when PIM apps go offline on mobile |
| Generated API types | hand-written | hand-written (the generated `api-spec/clients/kotlin` is **not used** by the Android apps) | — | codegen candidate (§5.3) |

### 3.7 Ranking

| Rank | Candidate | Why |
|---|---|---|
| 1 | Document model + layout (`kubuno-docs-core`) | largest (≈ 6–7 k lines per copy), changes weekly, a divergence is visible to every user (pagination), a second copy already exists and a third is coming (web via WASM, Android) |
| 2 | Calendar recurrence + time zones (`kubuno-calendar-core`) | small (≈ 300 lines + crate), **current user-visible bug**, needed offline on every native client, the backend has six callers |
| 3 | Drive name/path/case rules + conflict naming | small, causes sync failures and data-loss classes, needed by server, desktop, Android, web validation |
| 3b | Forms conditional logic | tiny (135 lines), but its absence on the server is a **validation bypass**; the server must run the same evaluator as the client |
| 4 | Sync client protocol (outbox, backoff, merge) | medium-large, already diverging between desktop and Android, iOS will need a third copy |
| 5 | Mail text rules (subject, reply prefix, addresses, quote detection) | small, user-visible threading errors |
| 6 | Spreadsheet engine | large but single-platform today; becomes rank 1–2 the day desktop or mobile sheets start |
| 7 | Sanitiser policy, vCard, iCalendar escaping | security/standards; partly backend-only dedup |
| 8 | Search folding, markdown options | small, user-visible mismatches; core-repo text helpers / shared options |
| — | Sizes, dates, plurals, haversine | **not cores**: platform-native helpers, one per platform, held by shared vectors; catalogues by codegen |

## 4. Precedents and lessons

### 4.1 `@kubuno/views-compiler` (WEB-VIEWS §12–14): the model to copy

- The grammar (`kubuno-views-syntax`, `-model`) was split out of a desktop crate into **platform-neutral crates**
  that depend on no UI, no `windows`, no C library, and are checked on `wasm32-unknown-unknown`, Linux and both
  macOS targets in addition to Windows.
- The WASM shim is tiny (116 lines), **C ABI, JSON in / JSON out, no wasm-bindgen**, in its own `[workspace]`, and
  depends on the compiler through a **git tag** (`views-web-v0.1.0`).
- The built `.wasm` (473 893 bytes) and a `BUILD-INFO.json` (tag, rustc, size, SHA-256) are **committed** and
  shipped in an npm package: module builds, including offline `.kbpkg` builds, never need Rust.
- A local-checkout override (`build-wasm.mjs --desktop <dir>`) avoids needing a pushed tag during development.
- A **conformance suite** (the same view rendered compiled and interpreted, the same DOM at each step, on Linux and
  Windows) is what made the two paths trustworthy.

### 4.2 The 2026-07 WASM programme and its reversal (COORDINATION_WASM.md, messages 39–49)

Nine module backends (documents, drive, notes, tasks, keestore, assistant, contacts, wiki, calendar) were ported to
`wasm32-wasip1` with SQLite inside, run by wasmtime in the desktop daemon. `calendar-core.wasm` (2.9 MB, `rrule`
+ `chrono` + SQLite) reproduced the server's RRULE expansion exactly. On **2026-08-16 the user reversed it**: desktop
apps are clients of the API; wasmtime and the per-module local backends were removed.

What it teaches:

- What was rejected was *running module backends locally* (storage, routing, sync drivers, component installs), not
  compiling Rust to WASM. The views compiler, built after the reversal, is the accepted form.
- Porting whole backends meant forking them (SQLx/Postgres → rusqlite, Axum → a custom ABI): two copies again.
  A shared core must be **the code the backend itself runs**, not a port of it.
- It showed `rrule` + `chrono` compile to WASM unchanged and agree with the server, which de-risks lot 2.

### 4.3 Documents desktop parity harness (`documents/src/doc/parity.rs`)

The desktop layout is calibrated against the **real** `canvas-engine.ts` run in Chrome (recordings of line tops and
contents per paragraph). This is exactly a conformance vector set, with two weaknesses to fix when it becomes the
docs-core suite: the recordings are not in the repository (machine-specific, the suite *skips* without them), and
only one direction is checked. With a shared core the web would run the same Rust layout, and the recordings become
the one-time acceptance test of the switch.

### 4.4 `api-spec`: codegen that nobody consumes

`api-spec` generates Kotlin and Swift clients from the OpenAPI document, but the Android apps use hand-written
clients (`core-api`, no import of `com.kubuno.api`). Lesson: generated code is adopted only if it is cheaper than the
hand-written alternative on day one and is published where the consumer builds (a Gradle artefact), not as a
folder to copy.

### 4.5 Others

- `_tools/gen_holidays.py` turns a date oracle into **rules** expanded by one Rust implementation in the core
  (`kubuno-core/src/holidays/rules.rs`), the web only describes them: the "one expander, data elsewhere" shape.
- Shared crates by git tag (`kubuno-seccomp`, `kubuno-storage`, `kubuno-db`, `kubuno-drive` client with
  `default-features = false`) are the established distribution channel; `_tools/check_versions.py` audits them by a
  hard-coded list, which new cores must join.

## 5. Options

### 5.1 Rust core: native on backend and desktop, WASM on the web, UniFFI on mobile

#### 5.1.1 Web (WASM)

Measured sizes (`wasm32-unknown-unknown`, release profile of the views compiler, no `wasm-opt`):

| Crate | Content | Raw | gzip -9 |
|---|---|---|---|
| `tiny` (probe) | name validation + NFC (`unicode-normalization`) + `serde_json`, C ABI | 183 KB | 86 KB |
| `rr` (probe) | `rrule` 0.12 expansion + `serde_json` (pulls `chrono-tz` 0.8 **and `regex`**) | 2 087 KB | 478 KB |
| `kubuno-views-web` (shipping) | `.kbview` parser, validator, plan and `.d.ts` generator | 474 KB | — |
| `calendar-core.wasm` (2026-07, removed) | `rrule` + `chrono` + SQLite, WASI | 2.9 MB | — |

Reading:

- **The floor is ≈ 80–100 KB gzip** once `serde_json` and a Unicode table are in. Fine for a module that loads it
  lazily (the office bundle is already several MB), too heavy for a two-line rule needed on every page.
- **Dependencies dominate.** `rrule` alone costs ≈ 400 KB gzip because it embeds the whole tz database and a regex
  engine. A calendar core should parse RRULE itself or use `rrule` with a trimmed tz set, and take time-zone
  offsets from the platform where it has them (`Intl` on the web): measure each dependency before adopting it.
- **Startup**: instantiation of a few hundred KB is a few ms with streaming compilation; it must be **lazy** (load
  on first use) and cached by URL hash. Not measured here (no Node).
- **Interop cost**: a JSON-in/JSON-out C ABI copies strings twice (UTF-16 → UTF-8 → UTF-16) and parses JSON on both
  sides. Negligible for coarse calls (expand a month of events, validate a name, lay out a paragraph); wrong for
  chatty calls (measure one glyph). Rule: **one call per user-level operation**, batch inside. For the documents
  layout, text measurement stays in the platform (canvas `measureText`, DirectWrite) and is passed in as a metrics
  table or a callback batch; the core decides breaks and positions.
- **Threading**: WASM runs on the calling thread. Heavy work (layout of a 300-page document, recalc of a large
  sheet) goes into a Web Worker that owns the instance; shared-memory threads would need COOP/COEP headers on the
  host, which the core does not send today: avoid.
- **Debugging**: panics become `unreachable` traps (`panic = "abort"`); the shim must catch errors and return them as
  JSON, and the core is debugged with its native tests, not in the browser. DWARF debugging in Chrome exists but is
  not needed if the vectors run natively. No source maps from `.wasm` to Rust in the module bundle (acceptable).
- **Packaging for a module frontend** (new, the views compiler is a host package): the built `.wasm` is committed in
  the module repo (`frontend/wasm/<core>.wasm` + `BUILD-INFO.json`) and either (a) emitted by Vite as an asset next
  to `entry.js`, or (b) inlined as base64 in a lazily loaded chunk (+33 %). Both already work today: module
  frontends already emit a `chunks/` folder, the core's module asset route serves `*.wasm` as `application/wasm`
  (`core/crates/kubuno-core/src/handlers/modules/assets.rs:161`), and keestore already ships WASM inlined in a lazy
  chunk (`hash-wasm` for Argon2, `keestore/frontend/dist/chunks/KeeStorePage-*.js`, 244 KB). Use (b) for cores under
  ≈ 100 KB gzip, (a) above (streaming compilation, cached separately from `entry.js`).

#### 5.1.2 Desktop and backend

Native Rust, path or git-tag dependency, no FFI. This is where Rust cores cost nothing.

#### 5.1.3 Mobile (UniFFI)

- UniFFI (Mozilla, MPL-2.0) generates Kotlin (over JNA) and Swift bindings from the Rust crate; `cargo-ndk` builds
  `.so` files per ABI (`arm64-v8a`, `x86_64` for emulators; `armeabi-v7a` optional), `cargo` + `xcodebuild` build an
  XCFramework for iOS (on the Mac).
- **Cost to accept**: the Android repository has no native code today; adding the first `.so` adds the NDK to the
  build, JNA (≈ 1 MB per ABI before shrinking) and the core itself (≈ 0.3–1.5 MB per ABI for the sizes above) to
  each app that uses it. JNA calls cost microseconds: same rule as WASM, coarse calls.
- **Build without Rust downstream**: like the `.wasm`, the mobile repository consumes a **prebuilt** artefact
  (an AAR with the `.so` files and the generated Kotlin, built by CI from the core's tag), never a cargo build in
  Gradle. iOS consumes a prebuilt XCFramework the same way.
- **When**: only for logic that must run offline on the device and is costly to get wrong (recurrence for an
  offline calendar, name rules for sync, the sync protocol core). Rendering-adjacent code on Android (Compose text)
  stays Kotlin and is held to the vectors instead.

### 5.2 TypeScript as the single source, run elsewhere

Running the TS implementation in an embedded engine (QuickJS, Hermes, V8/J2V8, JavaScriptCore) on desktop, backend
and mobile: rejected.

- The desktop and backend are Rust: embedding a JS engine adds a runtime, a GC and an FFI boundary to every call,
  and the backend could not use it in hot paths (six recurrence callers, mail delivery).
- The best code today is not in TS for every domain (layout: TS reference; name rules, recurrence, sanitising,
  sync: Rust). A TS single source would force rewriting the Rust ones the other way.
- Debugging and profiling across an embedded engine are poor, and the reversal of 2026-08 shows the user's
  aversion to running foreign runtimes inside the desktop.

### 5.3 Codegen from specifications

Good for **data**, not for algorithms:

- constants and tables (heading sizes and spacing, reserved file names and forbidden characters, error codes,
  plural categories, the sanitiser allow-list): one TOML/JSON source in the owning repo → generated Rust, TS and
  Kotlin constants, `--check` in CI (like `npm run build:views-handles --check`);
- API types: `api-spec` already exists; to become useful it must publish a Gradle artefact and be adopted by
  `core-api` file by file (§4.4).

Writing algorithms in a spec language (a DSL compiled to three targets) is a research project: rejected.

### 5.4 Shared conformance vectors only

Each domain publishes `vectors/*.json` (input, expected output, a short reason), and each implementation runs them
in its own test runner (cargo test, vitest, JUnit, XCTest). No shared binary.

- **Cheapest** (a runner is ≈ 50–100 lines per language), works for every platform immediately, needs no build
  change, and found nothing it cannot express so far (recurrence instances, name verdicts, subject normalisation,
  layout line tops, sanitiser outputs).
- It does not remove the duplicated effort, only the divergence. It is the **first step** of every lot and the
  permanent acceptance test once a Rust core replaces an implementation.
- Distribution: vectors live **with the reference implementation** (the core crate) and are vendored by consumers
  at a pinned tag with a checksum file, so each repository's tests run offline.

### 5.5 Keep platform-native implementations on purpose

Some algorithms should stay native and share only a policy + vectors:

- **HTML sanitising in the browser**: DOMPurify uses the browser's own parser, which is what will render the HTML;
  a WASM `ammonia` would parse with a different parser (mutation-XSS risk). Share the allow-list (codegen, §5.3)
  and an XSS vector corpus; the server keeps `ammonia`.
- **Locale data the platform already has** (`Intl` on the web, ICU on Android/iOS): do not ship ICU in WASM.
- **Text shaping and measurement**: DirectWrite, canvas, Compose.

### 5.6 Kotlin Multiplatform

KMP could share code between Android, iOS and even JS, but not with the Rust backend and desktop, which hold most
of the reference implementations. Acceptable later for mobile-only glue (view models, WorkManager/BGTask
scheduling), not as the cross-platform core.

### 5.7 Comparison

| Criterion | Rust core + WASM/UniFFI | TS everywhere | Codegen | Vectors only |
|---|---|---|---|---|
| One implementation | yes | yes | data only | no |
| Divergence detected | by construction + vectors | yes | data only | yes |
| Backend usable in hot paths | yes | no | yes | n/a |
| Web cost | 80–500 KB gzip per core, lazy | none | none | none |
| Mobile cost | NDK, JNA, prebuilt AAR/XCFramework | JS engine | none | none |
| Toolchain downstream | none (prebuilt artefacts) | none | generator in CI only | none |
| Effort to start | M per core | L | S per table | S per domain |
| Fits module isolation | yes (§6.4) | yes | yes | yes |

## 6. Recommended architecture

### 6.1 What a core is

- A Rust library crate `kubuno-<domain>-core`, **sans-IO**: no network, no database, no file system, no async
  runtime, no clock (time is a parameter), no global state, no UI, no `windows`/`objc`/JNI code, no C library.
  Inputs and outputs are serde types; errors are values with stable codes.
- Deterministic and total (no panics on any input; fuzzed where it parses).
- `#![forbid(unsafe_code)]` in the core; the only `unsafe` is in the thin shims.
- Platform shims are separate tiny crates: `…-core-wasm` (C ABI, JSON, like the views compiler) and
  `…-core-ffi` (UniFFI), built only where needed. The backend and the desktop use the core directly.
- Ships `vectors/` (§5.4) and runs them in its own tests.
- Platform services enter through traits or input tables (text measurement, time-zone offsets when the platform
  owns the tz database, randomness).

The protocol cores follow the same rule: a `kubuno-sync-core` would hold the outbox state machine, classification,
coalescing, backoff schedule and three-way merge as pure functions over records; SQLite (desktop sqlx, Android Room,
iOS GRDB/Core Data) and HTTP stay in each platform.

### 6.2 Where each core lives

| Kind | Home | Examples | Tag |
|---|---|---|---|
| Owned by one module (its formats, its rules) | the **module repository**, as a workspace member `crates/kubuno-<domain>-core` next to the backend, which uses it by path | `kubuno-docs-core` (office), `kubuno-calendar-core` (calendar; tasks uses it as a client of the format), mail rules (mail), sheet engine (office) | `<domain>-core-vX.Y.Z` on that repo |
| Drive naming, path and conflict rules | the **drive** repository, in the existing `kubuno-drive` client crate (`default-features = false` already keeps it light) or a sibling `kubuno-drive-core` | name verdicts, case/NFC keys, conflict names | `drive-core-vX.Y.Z` |
| Used by several modules or by the core itself | the **core** repository (`core/crates/`) | text normalisation for search, plural/format helpers if any, the KDP client protocol (`kubuno-sync-core`, the server half already lives in `kubuno-db::journal`) | `<crate>-vX.Y.Z` like `db-v0.9.0` |
| Client-only infrastructure (secrets, accounts, sockets) | `desktop/common` (stays) | `kubuno-account`, `kubuno-secrets` | — |

**Pilot exception for `kubuno-docs-core`**: it is being extracted inside the desktop workspace, where the code and
the parity harness are. Keep it there until the web adopts it (like `kubuno-views-web`, which lives next to its
grammar in the desktop repo), with the neutrality rules of §6.1 enforced by CI (`cargo check --target
wasm32-unknown-unknown`). Move it to the office repository when the office backend or a second client needs it
(open question Q1).

### 6.3 Packaging per platform

| Consumer | How it gets the core | Rust needed downstream? |
|---|---|---|
| Module backend | path dependency (same repo) or git tag | yes (already) |
| Desktop app | git tag (`[patch]`/env override to a local checkout during development, as `build-wasm.mjs --desktop`) | yes (already) |
| Module frontend | committed `frontend/wasm/<core>.wasm` + `BUILD-INFO.json`, built by `npm run build:wasm` from the tag, inlined or emitted as an asset (§5.1.1); a small hand-written TS wrapper with types | **no** (`npm ci && npm run build` and `.kbpkg` stay offline) |
| Host / cross-module core on the web | an `@kubuno/<name>` npm package with the `.wasm` inside, resolved as a host singleton through the import map (like `@kubuno/views-compiler`) | no |
| Android | prebuilt AAR (`.so` per ABI + UniFFI Kotlin) produced by the core repo's CI on the tag, attached to the GitHub Release; the mobile repo pins it by version + SHA-256 (committed in `gradle/libs.versions.toml` or vendored under `third_party/` with `BUILD-INFO.json`) | no |
| iOS | prebuilt XCFramework (Swift package with a binary target), same pinning | no |

### 6.4 Module isolation

- A module's core is consumed **only by that module's own clients**: its backend, its web frontend, the desktop app
  and the mobile app that front this module. `kubuno-calendar-core` is never imported by mail's frontend; mail
  shows invitations through the API or an extension point, as today.
- Tasks reading RRULE is the one borderline case: tasks may depend on `kubuno-calendar-core` only if the recurrence
  part moves to a **core-repo** crate (`kubuno-recurrence`), because module → module crate dependencies are
  forbidden. Recommended: put the RRULE/time-zone engine in the core repo from the start, and keep calendar-only
  rules (availability, rooms) in calendar (Q3).
- A module frontend bundles its own `.wasm`; it never loads another module's. Cross-module web cores exist only as
  host packages from the core repo.
- The desktop and Android apps already follow "one app per module" (WEB-VIEWS isolation rule 5); a core joins the
  app of its module only.

### 6.5 Versioning

- SemVer per core, independent of the module's `vX.Y.Z` (like `drive-v0.1.10` vs drive `v0.1.x`).
- The WASM shim exports `abi()`; the TS wrapper refuses a mismatch (as `VIEWS_ABI`). The FFI shim does the same.
- `BUILD-INFO.json` (tag, rustc, bytes, SHA-256) next to every committed binary; CI checks the binary matches the
  tag it claims (rebuild + compare hash on Linux).
- `_tools/check_versions.py` learns the cores: tag referenced everywhere identical, source unchanged since the tag,
  committed binaries match `BUILD-INFO.json`.
- Changelog: each core has its section in its repository's `CHANGELOG.md` (English, `[Unreleased]`), plus an entry
  in every consumer bumped.

### 6.6 Licences

Cores are `AGPL-3.0-or-later` like the rest. Dependencies to prefer are MIT/Apache-2.0 (`rrule`, `chrono`,
`chrono-tz`, `unicode-normalization`, `ammonia`); UniFFI is MPL-2.0 (file-level copyleft, compatible). Shipping
AGPL code inside an iOS App Store binary is a known tension (store terms vs AGPL obligations); it is a question
for the iOS app as a whole, not new with shared cores (Q6).

### 6.7 CI on Linux, Windows, macOS

For each core, in its home repository (`checks.yml`):

- `cargo test` + `clippy -D warnings` on `ubuntu-latest`, `windows-latest`, `macos-latest`;
- `cargo check` for `wasm32-unknown-unknown`, `aarch64-linux-android`, `aarch64-apple-ios` (cross targets, no SDK
  for `check`);
- the vectors run natively (they are part of `cargo test`);
- on a `<domain>-core-v*` tag: build the `.wasm` (and the AAR/XCFramework once mobile adopts it), attach them to the
  Release with their SHA-256.

Consumers run the same vectors with their own runner (vitest in the frontend, JUnit in Android) against the vendored
copy, so a platform that keeps its own implementation is still checked.

## 7. Recommendation and lots

Sizes: **S** ≤ 3 days, **M** 1–2 weeks, **L** 3–6 weeks of agent work, testing included.

| Lot | Content | Size | Depends on |
|---|---|---|---|
| **SC-0** Vectors | JSON format (`{suite, version, cases:[{id, input, expected, note}]}`), runners for Rust (`kubuno-vectors` dev-dependency), TS (vitest helper) and Kotlin (JUnit helper); vendoring script with checksums; `check_versions.py` entries | S | — |
| **SC-1** `kubuno-docs-core` (pilot 1) | Extract `model/`, `doc/convert`, `para`, `lists`, `tables`, `images` (layout decisions only), the constants, and the history/edit operations that do not touch the UI into a neutral crate; text measurement behind a trait; turn the Chrome parity recordings into committed vectors (generated, reviewed, with the font set named); CI wasm32 check. Then SC-1b: WASM shim + office frontend running the Rust layout behind a flag, compared with `canvas-engine.ts` on the vectors and the 18-document corpus, before any switch | M (1) + L (1b) | SC-0 |
| **SC-2** Recurrence and time zones (pilot 2) | `kubuno-recurrence` (core repo) or `kubuno-calendar-core`: expansion in the event's zone (fixes the DST/weekday bug), `UNTIL` in the event's zone, EXDATE/RECURRENCE-ID semantics, rule builder/describer used by the web (replaces `rrule.ts` logic, keeps its UI), iCalendar escape/fold (replaces the three copies); the backend's six callers switch; tasks validates its rules with it; vectors first (DST transitions, midnight-crossing, monthly last-weekday, leap years, count/until) | M | SC-0 |
| **SC-3** Drive names (pilot 3) | One verdict function (`valid`, `invalid(reason)`, `portable-warning`) with server, Windows, macOS, Linux profiles, a case/NFC comparison key and the conflict-copy name; the server applies it to rename and create-folder (choose: reject or sanitise, Q4), desktop and Android use it before writing to disk; the web validates in the dialog through WASM (or the TS port held by vectors, the logic is small) | S–M | SC-0 |
| **SC-3b** Forms logic | `kubuno-forms-core` (forms repo): the 12 operators, `computeHidden`, `resolveJump`, required-question check over the visible set; the server validates every submission with it (closes the bypass); the web keeps `logic.ts` held by the vectors or switches to the WASM build (≈ 100 KB gzip: keep TS) | S | SC-0 |
| **SC-4** Mail text rules | subject normalisation (one function, used by both server paths and the web), reply prefix, address list parsing, quote-attribution detection, sanitiser allow-list as generated data + XSS vector corpus; fix the two bare `ammonia::clean` calls | S–M | SC-0 |
| **SC-5** Sync protocol core | `kubuno-sync-core` sans-IO: outbox classification, coalescing, backoff schedule, rollback decisions, three-way field merge, conflict names; desktop `kubuno-sync-engine` uses it; Android adopts it through UniFFI **when** the first offline PIM app or iOS starts | L | SC-0, SC-3, Q5 |
| **SC-5b** Small helpers by vectors | one byte-size, duration and distance formatter per platform (`@kubuno/sdk`, `core-ui`), search fold in a core-repo `kubuno-text` crate + TS port, all held by shared vectors; delete the ≈ 30 copies; fix base 1000 in `PhotoViewer.kt` | S–M | SC-0 |
| **SC-6** Spreadsheet engine | port `formula-engine.ts` + functions to `kubuno-sheets-core` only when a second platform needs sheets; until then, export the existing web results as vectors so the port has an oracle | L (deferred) | SC-0 |
| **SC-7** Mobile FFI channel | first AAR with UniFFI (probably SC-3 or SC-2), CI job, Gradle consumption of a prebuilt artefact, size budget per app | M | Q2 |

Order: SC-0 → SC-1 (already in motion) ∥ SC-2 → SC-3 and SC-3b → SC-4, SC-5b → SC-7 with the first mobile need →
SC-5 → SC-6.

Bugs found by this inventory that should be fixed **now**, independently of any shared core (each one a vector in
its future suite): the forms required-field bypass; recurrence in UTC; drive rename/create-folder accepting
non-portable names and the 255/1000-byte limit mismatch; the two bare `ammonia::clean` calls in mail; photo
thumbnails ignoring EXIF orientation; Android's outbox giving up after 5 attempts (`OutboxDrain.kt:32, 51`); base
1000 in `PhotoViewer.kt`; the stale memory note `sync-illegal-filenames` (the fix is in `kubuno-sync/engine.rs`).

### 7.1 Rules for new code (proposed for CLAUDE.md §7)

1. An algorithm needed on **two or more platforms** (backend, web, desktop, Android, iOS) goes into a
   `kubuno-<domain>-core` crate with conformance vectors, in the repository that owns the domain (§6.2). A second
   hand-written copy needs a written reason (platform-native parser, text shaping, locale data) and must pass the
   same vectors.
2. Cores are **sans-IO** and checked on `wasm32-unknown-unknown` in CI.
3. Module builds never need Rust: web consumers use a committed `.wasm` with `BUILD-INFO.json`; mobile consumers a
   prebuilt artefact pinned by hash.
4. A core is used only by its own module's clients; a cross-module core lives in the core repo.
5. Every bug fixed in a shared algorithm adds a vector first.
6. Constants shared across languages are generated from one source with a `--check` in CI, never copied by hand.

### 7.2 Migration path per module

| Module | Now | Path |
|---|---|---|
| office (documents) | TS engine (web), Rust port (desktop), Compose (Android) | SC-1: neutral crate → vectors → web on WASM behind a flag → switch; Android keeps Compose rendering, adopts the model/vectors |
| office (sheets, slides) | web only | vectors from the web now; Rust core when a second platform starts (SC-6) |
| calendar, tasks | backend expansion, web builder | SC-2; native apps use it from day one |
| drive | rules in four places | SC-3, then SC-5 for the sync protocol |
| mail | rules in three places | SC-4 |
| contacts | backend vCard only | vectors now (folding, groups, `item1.`); core only with a native contacts app |
| forms | logic web-only, server trusts the client | SC-3b |
| notes, wiki | two markdown renderers with different options | align options now (vectors); a WASM `pulldown-cmark` only if a native notes app appears |
| maps | geometry ×7, formatters ×6 | vectors for geometry; one formatter per platform (SC-5b) |
| photos | server-only EXIF/thumbnails | fix orientation in place; core only if Android generates thumbnails |
| keestore | browser-only KDBX | nothing now; Rust KDBX core first if a native client is planned |
| chat, flow, code | nothing duplicated | new chat formatting/mentions go into a core from day one (rule 1) |
| core (auth, i18n, search) | §3.5, §3.6 | token state machine and KDP client into `kubuno-sync-core` (SC-5); search fold in `kubuno-text`; catalogues by codegen |

## 8. Open questions (with recommended answers)

| # | Question | Recommended answer |
|---|---|---|
| Q1 | Where does `kubuno-docs-core` live? | In the desktop workspace during extraction (the code, the parity harness and the agent are there; tags are a user action and would slow iteration), **moved to the office repository** when the office frontend adopts it (SC-1b), so the format's owner owns its core. |
| Q2 | Accept native code (NDK, JNA, UniFFI) in the Android apps? | Yes, but only through prebuilt AARs, only for offline-critical logic, with a per-app size budget (≈ +2 MB per ABI); Compose rendering stays Kotlin. |
| Q3 | Recurrence: a calendar-owned core or a core-repo crate? | Core repo (`kubuno-recurrence`), because tasks needs it too and modules may not depend on each other. |
| Q4 | Drive names the server accepts but some OS cannot store: reject, sanitise, or accept and map locally? | Accept portable names only for new names (reject `\ / : * ? " < > |`, control chars, trailing dot/space, reserved device names, > 255 UTF-8 bytes) with a clear message; existing illegal names are mapped locally by the shared rule with a reversible, collision-free scheme; case-only and NFC/NFD clashes produce a server-side `(2)` on create. |
| Q5 | Unify desktop and Android sync behaviour (backoff, no give-up, explicit rollback) before or with a shared core? | Before: fix Android's give-up and rollback against shared vectors now (S); move to the shared core when iOS starts. |
| Q6 | AGPL in App Store binaries? | Out of scope of this study; decide for the iOS app as a whole before its first store submission. |
| Q7 | Web: run the Rust layout for documents, or keep `canvas-engine.ts` as the reference and the Rust port as a follower? | Run the Rust core on the web (SC-1b) if the corpus and the vectors agree and the WASM stays under ≈ 400 KB gzip; otherwise keep two implementations held together by committed vectors. |
| Q8 | Commit WASM binaries in module repositories? | Yes, as the views compiler does (the only way to keep `.kbpkg` builds offline and Rust-free), with `BUILD-INFO.json` and a CI hash check. |
| Q9 | Forms: fix the required-field bypass now (server ignores hidden questions only when it can evaluate the logic), before the shared core exists? | Yes, through SC-3b directly (it is small); until then, at least validate required questions that no rule targets. |
| Q10 | i18n catalogues: one source per module generated for web, Android and desktop? | Yes, but as a separate lot after SC-0 (codegen, not a core); plural rules stay native. |

## 8.1 Decisions (2026-10-02)

On 2026-10-02 the user approved every recommendation of this study. Q1–Q10 are answered as recommended in §8:

| # | Decision |
|---|---|
| Q1 | `kubuno-docs-core` is extracted in the desktop workspace and moves to the office repository when the office frontend adopts it (SC-1b). |
| Q2 | Native code is accepted in the Android apps, only as prebuilt AARs, only for offline-critical logic, with a per-app size budget (≈ +2 MB per ABI); Compose rendering stays Kotlin. |
| Q3 | Recurrence lives in the core repository (`kubuno-recurrence`), usable by calendar and tasks. |
| Q4 | Drive accepts only portable names for new names (rejects `\ / : * ? " < > \|`, control characters, trailing dot/space, reserved device names, > 255 UTF-8 bytes) with a clear message; existing illegal names are mapped locally by the shared reversible, collision-free rule; case-only and NFC/NFD clashes get a server-side `(2)` on create. |
| Q5 | Android's outbox give-up and rollback are fixed now, before a shared sync core; the shared core comes with iOS. |
| Q6 | AGPL in App Store binaries is decided for the iOS app as a whole before its first store submission. |
| Q7 | The web runs the Rust documents layout (SC-1b) if the corpus and vectors agree and the WASM stays under ≈ 400 KB gzip; otherwise two implementations held by committed vectors. |
| Q8 | WASM binaries are committed in module repositories with `BUILD-INFO.json` and a CI hash check. |
| Q9 | The forms required-field bypass is fixed now through SC-3b. |
| Q10 | i18n catalogues: one source per module generated for web, Android and desktop, as a separate codegen lot after SC-0; plural rules stay native. |

The rules of §7.1 are adopted. Execution order: SC-3b (security) together with SC-0, the Android outbox fix (Q5),
then SC-2 and SC-3.

## 9. Limits of this study

- No runtime measurement of WASM instantiation or call cost (no Node or browser run), no Android/iOS build.
- Negative findings ("no implementation") come from greps over a slow network share and may miss code with
  unusual names.
- The Android Compose line breaking was not compared with the web's (assumed different: the platform breaker vs
  whitespace-only breaking).
