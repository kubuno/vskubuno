# Desktop offline-first sync: local SQLite + web API (design study)

> Product requirement (2026-10-01): "the desktop versions must have a local SQLite database and synchronise their data
> with the web version through the web version's API".
> Status: design study (read-only survey of `desktop`, `core`, the module back-ends and the Android apps
> (`mobile/android`); no database, no SSH, no server touched; every server fact below cites the file it comes from so
> it can be re-checked before a lot starts). **2026-10-01: the foundation lots SE-0 to SE-3 are built on the library
> side** (`desktop/common`: `kubuno-secrets`, `kubuno-api-client`, `kubuno-account`, `kubuno-sync-engine`), not yet
> wired into the shell or the apps: see §19.

Contents: 1 Summary - 2 What exists today - 3 Server API inventory - 4 Prior art - 5 Architecture -
6 Local database - 7 Pull, push, conflicts - 8 Scheduling - 9 Auth and accounts - 10 Security - 11 Multi-OS paths -
12 Views and data components - 13 Per-app plan - 14 Server lots - 15 Desktop lots and order - 16 Testing and
verification - 17 Risks - 18 Open questions - 19 Implementation status (foundation lots).

---

## 1. Summary

- **One protocol, already half-standard on the server.** calendar, contacts, tasks and notes already share a delta
  feed built on `kubuno_db::journal::changes_since` (`core/crates/kubuno-db/src/journal.rs`, tag `db-v0.9.0`):
  `GET /<entity>/delta?cursor=<i64>&limit=` -> `{changes:[{uuid, kind, change_seq, <entity>}], cursor, has_more}`,
  per-schema `change_counter` row taken inside the write transaction, explicit tombstone tables, client-minted ids on
  create. Drive (`/sync/delta`) and office (`/documents/delta`) have the same shape. This design names that shape the
  **Kubuno Delta Protocol (KDP) v1**, writes down the rules every feed must keep (§7.1), and closes the gaps per
  module (§14) instead of inventing a new protocol.
- **One shared desktop crate, `kubuno-sync-engine`**, on top of `kubuno-data`'s SQLite (sqlx) stack: per-account,
  per-app database; feeds (pull with server cursors, page + cursor in one transaction); an outbox of intents with one
  idempotency key per intent; conflict records; a scheduler (startup, interval, websocket hint, network change,
  wake, local write); change notifications to the views.
- **Views stay offline-capable by default**: a `SyncedTableAdapter` (a `TableAdapter` whose `Update` writes the local
  table *and* the outbox in one transaction) feeds the existing `BindingSource`; a `SyncStatus` component and a
  `SyncIndicator` control show state, last sync and conflicts.
- **The shell owns the account.** One process (the shell) owns each refresh token (lesson from Android: concurrent
  rotation revokes the whole token family); apps borrow 15-minute access tokens through a local broker. Tokens and the
  database keys move out of the plaintext `creds.json` into the OS credential store.
- **Recommended order**: engine + accounts/secrets first, then **drive metadata** as the pilot (pull-only first: the
  server feed exists and is the most exercised one), then chat once its server feed exists, then documents, then
  calendar/contacts/tasks/notes (server side ready, desktop apps to write). Chat is *not* a good pilot: its server has
  no forward cursor at all (§3.3).

---

## 2. What exists today (desktop)

| Area | Where | State |
|---|---|---|
| Accounts ("instances") | `desktop/common/kubuno-sync/src/config.rs` | `%APPDATA%\kubuno-desktop\instances\<id>\{config.json, creds.json, state.db}`; `<id>` = host slug + 8 hex. **`creds.json` holds refresh + access tokens in plaintext** (0600 on Unix only, nothing on Windows; "keyring is a follow-up"). |
| Auth | `kubuno-sync/src/api.rs` | `POST /api/v1/auth/login {login, password, client_type:"desktop", device_name, device_type}` (tokens in body, no TOTP step); `POST /auth/refresh {refresh_token}` with rotation, per-instance `refresh_lock`, `FRESH_TTL` 300 s (adopt a pair rotated < 5 min ago), re-read of `creds.json` before rotating, `REFRESH_COOLDOWN` 45 s, `AuthFailure::{Genuine, Transient}`, one refresh-and-retry on 401. Only GET/POST/PUT helpers: **no PATCH/DELETE, no per-request headers** (this blocks the documents app, see below). |
| File sync | `kubuno-sync/src/{engine,push,store,ws,daemon}.rs` | rusqlite 0.32 (bundled), synchronous, no WAL, no schema version (`CREATE TABLE IF NOT EXISTS`). Tables `meta(cursor)`, `folders(id, path)`, `files(id, folder_id, name, etag, local_path)`, `outbox(key uuid, op, file_id, folder_id, name, local_path, base_etag)`. Push then pull; pull = drive delta, 3 passes per page; push = scan + SHA-256 compare, `If-Match` PUT, `Idempotency-Key`, 412 -> "keep both" (`<stem> (conflit <host> <ts>).<ext>`). Guards: unreadable root, mass delete (>= 50 items and > 20 %), `subtree_really_gone`, reorg-trash guard (etag still present elsewhere), `other_files_at_path`, deterministic illegal-name mapping. Whole files in memory, no chunked upload. |
| Scheduling | `shell/src/sync.rs`, `kubuno-sync/src/daemon.rs`, `ws.rs` | one thread per instance; `notify` watcher (1.5 s debounce), websocket `ws(s)://host/ws?token=` matched **by substring** (`drive.changed`, `FileUploaded`...), 30 s poll **hard-coded** (`shell.json`'s `sync_interval_min` is saved but never used). |
| Cloud Files / Explorer | `shell/src/cloudfiles.rs`, `explorer.rs` | placeholders, hydration through `kubuno_sync::download_for` (whole file); network folders through an HKCU namespace entry. |
| Chat | `windows/src/chat/{api,model}.rs` | conversations, last 50 messages, send, read; websocket `/api/v1/chat/ws?token=` (new_message, presence, typing), backoff 2-30 s. **In memory only.** It sends `encrypted_data` = base64url JSON `{text}`; to be checked against the web client's E2E scheme before anything is cached (§10). |
| Documents | `windows/src/documents/api/{client,session}.rs`, `model/`, `edit/` | a complete client for `/api/v1/office/*` incl. `delta(cursor, limit, include_content)` and a tested session state machine (digest re-read before save, join/ping/leave, autosave interval from `/api/v1/config`, backoff 5-60 s, crash `Journal` trait). **Saving is impossible today**: `SyncTransport` returns `Unsupported` for PATCH/DELETE and any request with `If-Match`/`Idempotency-Key` (`MISSING_PATCH_HELPER`). The `Journal` has no disk implementation. No Yjs. |
| Drive app | `windows/src/drive/crates/*` | port of Files; "storage" = Windows Shell/FS. **No server access, no SQLite.** |
| Data components | `windows/src/crates/kubuno-data` (+ `-model`, `-macros`, `-tool`) | sqlx 0.8 on a private 2-worker Tokio runtime; `DbConnection`, `DbCommand`, `TableAdapter` (Fill/Update in one transaction, `ConflictOption`), `Table`/`RowState`, `BindingSource` (events `ListChanged`...), `.kbdata` + `data_source!` (typed rows, offline `.sqlx`), migrations only through the VS helper (no runtime API, but `raw.rs` exposes the pool so `sqlx::migrate!` works). **No DB-side change notification** (re-`fill` raises `Reset`). Not used by any app yet. See `DATA.md`. |
| Views | `kubuno-views/src/binding.rs`, `scope.rs::BindingProvider` | `{Binding Source=…, Path=…}`, `Rows` snapshots with a change stamp (lists rebuild only when the stamp changes). |

Consequences: there is one working sync engine (files) whose guards are hard-won (two data-loss incidents, see
`sync-folder-deletion-guard`, `sync-reorg-trash-dataloss`, `sync-illegal-filenames` in memory) and must not be
regressed; two SQLite stacks (rusqlite in `kubuno-sync`, sqlx in `kubuno-data`); secrets in plaintext; no local
data for chat and documents.

---

## 3. Server API inventory (what sync can use, what is missing)

All module routes go through the core proxy `/api/v1/<module>/…` (`core/crates/kubuno-core/src/modules/proxy.rs`),
which buffers the whole request body (`to_bytes(usize::MAX)`) and runs *before* the core's own idempotency/timeout
layers: a module must implement those itself.

### 3.1 Core

| Topic | Exists | Missing / defect |
|---|---|---|
| Auth | Native login returns `{access_token, refresh_token, refresh_expires_at, user}` in JSON (`handlers/auth/tokens.rs`); TOTP via `POST /auth/totp`; access JWT HS256, 900 s (`security.jwt_access_ttl_s`); refresh 30 d, **rotation on every native refresh**, family revoked on reuse, **24 h grace** if the successor was never used (`handlers/auth/refresh.rs`, migrations `000024`, `000033`); `X-Kubuno-Device-Key`; `security.max_sessions` = 10 (LRU revocation). | No session id in the JWT: a revoked session keeps working up to 15 min (acceptable). The desktop client does not implement the TOTP step. |
| Websocket | `GET /ws?token=<JWT>` (`handlers/ws.rs`, `websocket/hub.rs`): `{"type":"event","payload":AppEvent}`; `Custom` events with `recipient_user_ids` are targeted, **everything else is broadcast to every connected user**. | No sequence, no resume, no "resync" signal; token in the query string (ends up in proxy logs); token checked only at connect. |
| Idempotency | `middleware/idempotency.rs`, `core.idempotency_keys` (`000025`). | **Key scoped by a hash of the `Authorization` header**: a queued write replayed after a token refresh (new JWT) runs twice. Body not hashed; no in-flight lock (two concurrent equal keys both run); > 4 MB response = 500 after the write happened; never purged. Applies to core routes only. |
| Shell data | `/me` (`users.updated_at`), `/me/activity?limit<=50` (reads `core.event_log`, BIGSERIAL id), labels (`labels.updated_at`, `000034`; hard deletes), `/modules/:m/config`, clipboard, push devices. | **No delta anywhere** in core data; activity has no cursor nor read state; label links have no `updated_at`/tombstones. |
| Collab (Yjs) | `GET /collab/:room/sync?token=` (`collab/mod.rs`): snapshot + stored updates as binary frames, updates stored in `core.collab_updates`/`collab_snapshots` (`000020`, `000021`), consolidation. | Whole state on reconnect (no state vector exchange); **authorization fails open** if the module is unreachable; no HTTP endpoint for an offline client. |

### 3.2 Drive (`drive/src`, base `/api/v1/drive`)

Exists: `GET /sync/delta?cursor&limit<=2000&full` (`services/sync.rs`): `kind` file/folder/deleted, `change_seq`,
`etag` = content hash, tombstones (`000019`, never purged), counter row `drive.change_counter` (`000029`);
`PUT /sync/file/:id/content` + `If-Match` -> 412; `Idempotency-Key` scoped by **user id** (survives token refresh,
`drive/src/middleware/idempotency.rs`, `000020`), 24 h; client-chosen folder ids; chunked upload
(`/uploads`, `/chunks/:i`, `/complete`, `/abort`, `000003`); versions (manual), locks, `drive.changed` targeted event.

Missing (from the code): the feed is **owner-only** (shares never appear, losing access is silent); several writers
take `change_seq` in a separate transaction (`next_seq_on_pool`: `handlers/files.rs:170`, `transform.rs:156`,
`scanner.rs`, `thumbnails.rs`, `folder_reconcile.rs`) and can commit out of order -> a client can skip a change;
trashing a folder does not mark descendants; image `PUT /:id/content` does not refresh `content_hash` (stale etag);
`If-Match` check not atomic with the UPDATE; no `If-Match` on rename/move/trash; download without ETag/Range;
`chunks_received` is a counter (a resent chunk corrupts the count), `complete` not repeatable, no `drive.changed` on
folder ops and on chunked completion; `DELETE /folders/:id` detaches files to root while deleting the directory on
disk (likely data loss, `services/folders.rs:596-626`); no "cursor too old" signal; silent auto-rename `name (2)`
(Android pitfall).

### 3.3 Chat (`chat/src`, base `/api/v1/chat`)

Exists: conversations list (no paging, no `since`); messages `?limit&before=<id>` (backwards only, reading marks
delivered); send idempotency through `UNIQUE(conversation_id, nonce)` (`duplicate:true` on retry); delete leaves a
tombstone row; read receipts; reactions (PK makes add idempotent); websocket events (`new_message`,
`message_updated`, `reaction_update`, `presence_update`…), per-user channel of 128 (slow client = closed socket).

Missing: **no forward cursor** (`sequence_num` exists but is always 0), no `updated_at` on messages (edits, deletes,
pins, reactions, votes are only visible live), no conversation/membership delta or tombstones (hard
`DELETE FROM chat.conversations`), edit replaces the nonce (a late retry of the original send duplicates), media
upload without idempotency, `clear` emits nothing, presence goes offline when *one* of several sockets closes.

### 3.4 Office (`office/src`, base `/api/v1/office`)

Exists: content = gzipped `.kbdoc` (JSON) in drive through drive IPC; `GET /documents/:id` -> `{document{etag,
content_etag}, content_json}`; `PATCH` whole-content replacement with `If-Match` (412) and the best idempotency of the
platform (body hash, 409 on reuse with another body, 24 h + hourly purge in the current code); `GET /documents/delta`
(+ spreadsheets, presentations, diagrams, whiteboard) with tombstones (`000032`, `000033`); edit sessions
join/save/ping/leave.

Missing: delta built on PostgreSQL `SEQUENCE` + triggers (**not commit-ordered: skippable**); the `GET` title sync and
trash/restore bump `change_seq` without changing `etag`; `If-Match` not atomic (content written before the UPDATE);
owner-only feed; ~2 MB body limit (axum default; images are base64 inside); no events published; **REST saves and the
Yjs room are two sources of truth never reconciled** (an offline desktop PATCH is overwritten by the next web editor
that loads the room). Memory `office-server-doc-defects`: delta 500, missing etag, idempotency never purged and
`source_format` are **fixed in the repo but not deployed**; the fix includes migration `000064`, which is
irreversible and waits for the user's go.

### 3.5 Calendar, contacts, tasks, notes

| | Feeds | Conflict data | Gaps |
|---|---|---|---|
| calendar | `/calendars/delta`, `/events/delta` (attendees inline), `/time-blocks/delta` | `etag` + iCal `sequence` on events, `ctag` on calendars, never checked | owner-only (shared calendars missing); CalDAV REPORT ignores sync-collection, CalDAV PUT is a stub |
| contacts | `/contacts/delta`, `/labels/delta`, `/groups/delta`, `/reminders/delta` | `etag`, never checked; soft trash | shared book missing; no sync-collection |
| tasks | `/boards/delta` (stacks, labels, comments inline), `/tasks/delta` | `etag` + `sequence` | shared boards, attachments missing |
| notes | `/notes/delta`, `/notebooks/delta`, `/labels/delta` | **none** (no etag/version) | reminders/shares missing; update/delete publish no event |

Common: client-minted `id` accepted on create, but **replaying a create returns 500** (PK violation mapped to
`DATABASE_ERROR`), no `If-Match` anywhere (pure last-writer-wins), events mostly broadcast.

### 3.6 Mail, photos, maps

- mail: `GET /changes?since=<modseq>&limit&account_id` -> `{threads, deleted_ids, cursor, has_more}` (thread
  granularity); `Idempotency-Key` on `POST /send` (`000035`); misses hard deletes (retention, account removal),
  drafts/labels/folders/filters have no delta, `GET /messages/:id` marks read, star/important are toggles. A desktop
  client could also use the built-in IMAP (IDLE, CONDSTORE, QRESYNC), but the REST feed keeps one protocol.
- photos, maps: **no delta, no tombstones, no client ids** (hard deletes).

---

## 4. Prior art

### 4.1 In the project: the Android apps (`mobile/android`)

Reused as is (they were themselves ported from desktop `api.rs`):

- `AccountGraph` per account (`core-sync/.../account/AccountGraph.kt`): DB `kubuno-<accountId>.db`, client, engine,
  outbox, scheduler grouped and wiped together. **Account key = `serverUrl|userId`**: a local id re-minted after a
  data wipe caused the "session expired" bug (`android-shared-token-single-owner`).
- Pull: each page and its cursor in **one transaction** (`SyncEngine.kt`); stop if `has_more` and the cursor did not
  move; page cap; unknown `kind` skipped, never interpreted as a delete (`DocsDelta.kt`).
- Outbox (`DriveActions.kt`, `OutboxDrain.kt`): optimistic local write -> enqueue with a random UUID -> trigger; drain
  in order; DONE / DEFINITIVE / TRANSIENT classification; 404 on trash = done.
- Order of a sync job: **push, then pull, then reconcile local copies** (`SyncWorker.kt`); unique per-account job
  (`APPEND_OR_REPLACE`) so a change arriving during a run is not lost; a periodic safety net (WS and push are hints).
- Token: single-flight refresh, adopt a fresh pair, cooldown, persist before use, Genuine vs Transient; **one token
  owner** and a broker for sibling apps (`KubunoAuthenticator`, `KubunoTokenProvider`).
- Transfers: 8 MiB chunks (the 10 MiB server limit covers the whole multipart body), session id and next chunk
  persisted, **never resend a chunk index** (abort + restart), `complete` with its own key, download to `.part` +
  fsync + rename.

Mistakes not to copy: a DEFINITIVE rejection is dropped and left to "the next pull" to undo, but a rejected write
produces no change in the feed, so the optimistic state stays wrong (fix: explicit rollback from a snapshot);
giving up after 5 transient failures loses the intent silently; `MailRepository.apply` and `setCursor` not in one
transaction; Room `fallbackToDestructiveMigration` would wipe the outbox; idempotency key derived from a clock (replayed
old responses); toggles retried with the same key.

### 4.2 Known approaches

| Approach | Model | Fit for Kubuno |
|---|---|---|
| CouchDB/PouchDB replication | every document has a revision tree; `_changes` feed with `since`; conflicts kept as sibling revisions | the `_changes` feed is exactly KDP; revision trees would mean a new storage model in every module: rejected. Keep the idea of **surfacing conflicts as data** (conflict records) rather than dropping them. |
| CRDTs (Yjs/yrs, Automerge) | merge without conflicts; state vectors | right tool for **rich text** (core already stores Yjs updates); overkill for rows such as an event or a contact (field-level merge suffices). |
| Operation log with server sequence numbers (Linear, Figma-style "server-authoritative ops") | client sends intents, server orders them and emits a total order | our outbox of intents + `change_seq` feeds is this model, per module. |
| Electric SQL / PowerSync | server change stream into client SQLite; local writes in an upload queue; client keeps the server copy and re-applies pending local writes on top ("rebase") | closest to this design. We keep PowerSync's two key ideas: **writes go to a local upload queue processed by app-defined API calls** (our outbox ops call the existing REST endpoints), and **pending local writes are re-applied over freshly pulled server rows** (§7.3). We do not adopt their server components (Postgres logical replication across module schemas would break module isolation and the per-module API). |

---

## 5. Architecture

```text
                      +-----------------------------------------------------------+
  views (.kbview)     |  BindingSource <- SyncedTableAdapter / typed data_source!   |
                      |  SyncStatus component, SyncIndicator, ConflictsDialog      |
                      +---------------------------+-------------------------------+
                                                  | local reads/writes (sqlx, same pool)
                      +---------------------------v-------------------------------+
  kubuno-sync-engine  |  LocalDb (open, key, WAL, migrations)                       |
  (per app process)   |  Feeds (pull)    Outbox (push)    Conflicts    ChangeBus    |
                      |  Scheduler (triggers, backoff)    StatusModel               |
                      +-------+-----------------------------+-----------------------+
                              | HTTP (ApiClient: any method, headers)  | websocket hints
                      +-------v-----------------------------v-----------------------+
  kubuno-account      |  AccountStore (accounts, OS credential store)               |
  (shell = owner)     |  TokenBroker (named pipe / Unix socket) -> apps borrow JWTs |
                      +-------------------------------------------------------------+
                                     |
                                core /api/v1/<module>/... , /ws
```

### 5.1 Crates

| Crate | Repo / location | Content |
|---|---|---|
| `kubuno-account` | `desktop/common` (multi-OS, no UI) | `AccountKey` (`sha256(normalized server_url | user_id)`, hex 16), `AccountStore`, `SecretStore` trait (Windows Credential Manager/DPAPI, macOS Keychain, Linux Secret Service), `TokenOwner` (the existing `api.rs` refresh state machine, moved here unchanged), `TokenBroker` server + client, login incl. the **TOTP step**. Replaces `kubuno-sync::config` creds handling. |
| `kubuno-api` | `desktop/common` | `ApiClient`: any method, per-request headers (`If-Match`, `Idempotency-Key`, `X-Kubuno-Device-Key`), JSON/bytes/streams, one 401 -> refresh -> retry, `ApiError` from `{"error":CODE,"message"}`, `Retry-After`. Extracted from `kubuno-sync::api` (which then depends on it). Unblocks documents' `MISSING_PATCH_HELPER`. |
| `kubuno-sync-engine` | `desktop/common` | §6-§8. Depends on `kubuno-api`, `kubuno-account` (client side), `sqlx` (sqlite). **No UI dependency**, so it is usable by the shell, the apps, tests and later the macOS/Linux front-ends. |
| `kubuno-data` (extension) | `desktop/windows/src/crates` | `SyncedTableAdapter`, `SyncStatus`, `SyncIndicator`, `ConflictsDialog` behind a `sync` feature (§12). |
| `kubuno-sync` (files) | `desktop/common` (existing) | stays the file engine; moves gradually onto `kubuno-api`, `kubuno-account` and the engine's `LocalDb`/outbox primitives (lot D-3), **keeping every guard**. |

### 5.2 Processes

- **The shell owns accounts and refresh tokens** (`TokenOwner` runs only there). It also runs the file sync and the
  background sync of apps that want it while closed (drive metadata, chat unread counts and notifications).
- **Each app runs its own engine instance on its own database** while it is open (the UI binds to that pool,
  in-process change notifications are immediate). Access tokens come from the broker.
- **Single writer per database for sync**: a per-DB lock file (`<db>.sync.lock`, `fd-lock`/`LockFileEx`) elects the
  process that drains the outbox and pulls; another process opening the same DB (shell background sync vs the open
  app) only reads and writes local rows + outbox entries (SQLite WAL makes that safe across processes) and asks the
  owner to sync. When the app closes, the shell takes the lock.
- If the shell is not running when an app starts, the app starts it in background mode (`kubuno-desktop
  --background`), never a second token owner.

---

## 6. Local database

### 6.1 Files and stack

- One database per **account and app**: `<data>/accounts/<account_key>/<app>.db` (+ `-wal`, `-shm`), plus
  `<data>/accounts/<account_key>/blobs/<app>/` for content caches (attachments, document snapshots, thumbnails) and
  `account.json` (server URL, user id, display name, linked file-sync instance ids; no secret).
- **sqlx (SQLite) for everything new**, through `kubuno-data`'s runtime and `ConnectionHandle`, so the views and the
  engine share one pool per DB and in-process change notifications are trivial. `kubuno-sync` keeps rusqlite until
  D-3; both link the same `libsqlite3-sys` (cargo's `links = "sqlite3"` forces a single version; features are
  unified, which matters for SQLCipher, §10).
- Pragmas on every connection: `journal_mode=WAL`, `synchronous=NORMAL` (FULL for the outbox commit is unnecessary in
  WAL: a committed transaction survives a process kill; an OS crash may lose the last ones, which the idempotency key
  makes harmless), `foreign_keys=ON`, `busy_timeout=10000`, `key` first when encrypted.
- **Migrations at run time**: `sqlx::migrate!("migrations/<app>")` embedded in the app's binary, run by the engine at
  open, before any feed; forward-only (no `down` on user machines); a migration that adds a column to a synced
  entity also resets that feed's cursor to 0 (`_sync_feeds.needs_full = 1`) so existing rows get the new field. Never a
  destructive fallback: if a migration fails, the DB is opened read-only with a visible error and the outbox is kept.
  The VS *Migrations* node (DATA-7) works on these files as on any `kubuno-data` project.

### 6.2 Engine tables (prefix `_sync_`, created by the engine's own embedded migrations)

```sql
CREATE TABLE _sync_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
  -- schema_epoch, account_key (checked at open: a DB never syncs for another account), engine_version

CREATE TABLE _sync_feeds (
  feed          TEXT PRIMARY KEY,          -- 'drive.delta', 'notes.notes', 'chat.changes'
  cursor        TEXT NOT NULL DEFAULT '0', -- opaque: i64 for KDP, modseq string for mail
  needs_full    INTEGER NOT NULL DEFAULT 1,
  last_pull_at  TEXT,                      -- local clock, display only
  last_ok_at    TEXT,
  last_error    TEXT
);

CREATE TABLE _sync_outbox (
  seq             INTEGER PRIMARY KEY AUTOINCREMENT,  -- drain order
  op_id           TEXT NOT NULL UNIQUE,               -- uuid v4, the intent
  idem_key        TEXT NOT NULL,                      -- uuid v4; renewed only when the payload changes
  feed            TEXT NOT NULL,
  entity          TEXT NOT NULL,
  entity_id       TEXT NOT NULL,                      -- client-minted uuid when the server accepts it
  op              TEXT NOT NULL,                      -- 'create' | 'patch' | 'delete' | app-specific ('star', 'send')
  payload         TEXT NOT NULL,                      -- JSON of the intent (fields set, not the whole row)
  base_etag       TEXT,                               -- server etag/version the intent was made on
  base_row        TEXT,                               -- JSON snapshot for rollback / 3-way merge
  depends_on      INTEGER REFERENCES _sync_outbox(seq),
  state           TEXT NOT NULL DEFAULT 'pending',    -- pending | inflight | blocked | conflict | dead
  attempts        INTEGER NOT NULL DEFAULT 0,
  next_attempt_at TEXT,
  last_error      TEXT,                               -- code + message, never a payload or a token
  created_at      TEXT NOT NULL
);
CREATE INDEX _sync_outbox_entity ON _sync_outbox(entity, entity_id, state);

CREATE TABLE _sync_shadow (                           -- latest server version of rows that have pending ops
  entity TEXT NOT NULL, entity_id TEXT NOT NULL, server_row TEXT NOT NULL, etag TEXT, change_seq INTEGER,
  PRIMARY KEY (entity, entity_id));

CREATE TABLE _sync_id_map (                           -- only for servers that mint ids (drive upload, chat message)
  entity TEXT NOT NULL, local_id TEXT NOT NULL, server_id TEXT NOT NULL, PRIMARY KEY (entity, local_id));

CREATE TABLE _sync_conflicts (
  id INTEGER PRIMARY KEY, entity TEXT, entity_id TEXT, op_id TEXT, kind TEXT,  -- 'field' | 'deleted_remotely' | 'rejected' | 'content'
  local TEXT, server TEXT, base TEXT, fields TEXT, created_at TEXT, resolved_at TEXT, resolution TEXT);

CREATE TABLE _sync_log (id INTEGER PRIMARY KEY, at TEXT, level TEXT, feed TEXT, event TEXT, detail TEXT);
  -- ring buffer (last 1000), feeds the "Activité de synchronisation" panel; redacted
```

Entity tables are the app's own (declared in its `.kbdata` and migrations) plus three engine columns:
`_etag TEXT`, `_seq INTEGER` (server `change_seq` of the row), `_pending INTEGER NOT NULL DEFAULT 0` (count of
pending ops; drives the "not yet sent" badge in lists). Child collections that the server sends inline with their
parent (attendees, labels, stacks) are stored as child tables replaced as a whole when the parent arrives.

---

## 7. Pull, push, conflicts

### 7.1 Kubuno Delta Protocol v1 (the server contract)

What every feed must guarantee (most already do; §14 lists who does not):

1. `GET …/delta?cursor=<opaque>&limit=` -> `{changes, cursor, has_more}`; `cursor=0` = full snapshot; the returned
   cursor is the `change_seq` of the last change in the page.
2. **Commit-ordered sequence**: `change_seq` taken from the module's `change_counter` row *inside the write
   transaction* (`kubuno_db::journal::next_seq`), never from a `SEQUENCE` and never in a separate transaction.
3. Each change carries `uuid`, `kind` (`modified` | `deleted`, plus `revoked` once sharing is in the feed), the full
   row (or `full=true`), and the row's `etag`/version when the entity has one.
4. **Tombstones** for every hard delete, including purges, retention and cascades; a soft trash is a `modified` with
   `trashed=true`.
5. **Visibility, not ownership**: items shared with the user appear; losing access produces `revoked` (lot CORE-S4).
6. Cursor older than the tombstone retention -> `410 CURSOR_EXPIRED`; the client resets the feed (full pull, then
   deletes local rows not seen, except those with pending ops).
7. Writes: client-minted ids on create, **replay of the same create returns the existing row (200)**, not 500;
   `Idempotency-Key` honoured 24 h, **scoped by user id** (not by Authorization header), body-hashed (409 on reuse with
   another body), single-flight; `If-Match: <etag>` on PATCH/DELETE checked **atomically in the UPDATE**, 412 with the
   current row in the body.
8. After each committed change, a **targeted** websocket hint `{type:"Custom", event_type:"<module>.changed",
   payload:{recipient_user_ids, feed, seq}}`; hints are best-effort, the client never relies on them alone.

### 7.2 Pull

```text
for each feed due:
  loop:
    page = GET delta(cursor, limit)          # with retry/backoff on transient errors
    if page.has_more && page.cursor == cursor: stop + log (server bug guard)
    BEGIN
      for change in page.changes (in order of the feed's dependency passes, e.g. folders then files then tombstones):
        if row has pending ops: write server version to _sync_shadow (do not touch the visible row)
        else apply: upsert / delete (tombstone) / mark revoked; set _etag, _seq
        unknown kind -> skip + log, never delete
      UPDATE _sync_feeds SET cursor = page.cursor
    COMMIT
    ChangeBus.publish(tables touched)        # after commit only
    if !has_more: break
```

A per-item failure that does not concern the database (a file download in drive, a thumbnail) never blocks the
cursor: it is recorded in a deferred table with backoff (fixes the "cursor stuck on one bad file" defect of
`kubuno-sync/engine.rs`).

### 7.3 Push (outbox)

Local write path (one SQLite transaction, from the UI thread through `kubuno-data`'s runtime):
`UPDATE entity … ; _pending += 1 ; INSERT _sync_outbox(op_id, idem_key, payload, base_etag, base_row)`.
Consecutive patches of the same entity *not yet in flight* are **coalesced** (fields merged, new `idem_key` because
the body changed, original `base_etag` kept); a create followed by a delete before sending cancels both.

Drain (single writer, in `seq` order, per entity strictly ordered, entities independent):

| Server answer | Action |
|---|---|
| 2xx | apply the returned row (authoritative: auto-renamed names, server ids -> `_sync_id_map` and rewrite of dependent ops), `_pending -= 1`, delete the op; if `_pending` reaches 0 and a shadow exists, drop the shadow. |
| 412 / 409 CONFLICT | fetch current row (or use the 412 body) -> conflict policy (§7.4). |
| 404 on delete/trash | done (Android rule). 404 on patch -> conflict `deleted_remotely`. |
| 401 | one refresh via broker then retry; `Genuine` -> pause all feeds of the account, state `SessionExpired`, keep the outbox. |
| 400/403/413/422 (definitive) | op -> `dead`, **explicit rollback**: restore `base_row` (or the shadow) in the visible table, conflict record `rejected` shown to the user with the server message. Never "wait for the next pull". |
| 429 / 5xx / IO | transient: `attempts += 1`, `next_attempt_at = now + min(2^n * 2 s, 15 min) ± 20 %` (honour `Retry-After`); the op stays forever (no give-up), the UI shows "N modifications en attente" after 3 attempts. |
| "maybe applied" (timeout after sending) | retry with the **same** `idem_key`: the server either replays the stored answer or runs it once. This is why the key must survive token refresh (CORE-S1). |

After a successful drain the feed is pulled (push, then pull, as in `kubuno-sync` and Android). Pending ops are then
**re-applied over the shadow** (rebase) for rows still pending, so the user always sees "server + my pending
changes".

### 7.4 Conflict policy per entity type

| Entity type | Policy |
|---|---|
| Records with fields (event, contact, task, board, label, notebook, place) | **field-level three-way merge** with `base_row`: fields changed only locally win, only remotely win, both changed to different values -> server value kept, conflict record `field` listing the fields; the dialog offers "Garder la mienne" (re-queues a patch with the new etag) / "Garder celle du serveur". Requires `If-Match` server side (PIM-S1); until then, last-writer-wins and no conflict is detectable (documented limitation). |
| Notes body, document content without CRDT | **keep both**: the local version is saved as a copy ("<titre> (conflit <machine> <date>)"), the server version stays; same rule as files. |
| Rich documents (office, later notes) with Yjs | **CRDT merge**, no conflict (DOC-2). |
| Files (drive content) | **keep both** (existing `kubuno-sync` rule, unchanged). |
| File/folder metadata (rename, move, star) | intents replayed; name collisions -> server's auto-rename accepted and shown; move into a folder deleted remotely -> conflict `deleted_remotely`, item stays in place. |
| Toggles (star, important, pin) | desired state stored; call toggle; read the state from the response; toggle again **with a new key** if wrong (Android lesson). |
| Chat messages | append-only; send is idempotent by nonce/client id; edit/delete by the author = last writer wins; delete always wins over edit. |
| Deletes vs pending local edits | remote tombstone on a row with pending patches -> conflict `deleted_remotely` ("Restaurer" re-creates it with the same client id where the server allows it, or "Abandonner"). Local delete vs remote edit -> the delete is sent with `If-Match` and becomes a conflict on 412 (never silently deletes a newer server version). |

**Clocks are never used for ordering.** Order comes from `change_seq` (server) and `seq` (local outbox). Local
timestamps are display-only; server timestamps shown as received. A skewed client clock can only make
"dernière synchro il y a …" look odd.

---

## 8. Scheduling

| Trigger | Windows | macOS | Linux | Action |
|---|---|---|---|---|
| Startup / account added | | | | full cycle (push, pull all feeds) |
| Local write | | | | push after 1.5 s debounce |
| Interval | `shell.json` `sync_interval_min` (**actually wired**, fixes the hard-coded 30 s) | same | same | pull all feeds; default 5 min, 1 min while an app is in foreground |
| Websocket hint | targeted `<module>.changed` (parse JSON, no substring match) | | | pull that feed after 1 s debounce |
| Network back | `INetworkListManager` events / `NotifyNetworkConnectivityHintChange` | `NWPathMonitor` | NetworkManager D-Bus `StateChanged` (fallback: interval) | reconnect WS, full cycle |
| Wake from sleep | `WM_POWERBROADCAST` `PBT_APMRESUMEAUTOMATIC` | `NSWorkspaceDidWakeNotification` | logind `PrepareForSleep(false)` | full cycle after 5 s |
| Manual "Synchroniser" | | | | full cycle, ignores backoff |
| Metered connection | `NLM_CONNECTION_COST` | `NWPath.isExpensive` | NM `Metered` | metadata only, defer blobs (setting) |

The scheduler coalesces requests per feed (a request during a run marks "run again"), so nothing is lost and nothing
runs twice concurrently. The forced offline mode of `settings.json` (`offline`) suspends network work but keeps
local writes and the outbox.

---

## 9. Auth and accounts

- **Account identity** = `AccountKey(server_url normalized, user_id from GET /me)`. A file-sync "instance" is a sync
  root *of* an account (`account.json` lists them); existing `instances/<id>` are adopted at first start (login state
  read from `creds.json`, tokens moved to the OS store, file deleted).
- **Token owner** (shell): the existing state machine of `api.rs` (single-flight, adopt fresh pair, persist before
  use, 45 s cooldown, Genuine/Transient), unchanged, behind `TokenOwner`. Tokens are persisted in the OS credential
  store (`Kubuno/<account_key>/refresh`), written **before** the new token is used.
- **Broker**: Windows named pipe `\\.\pipe\kubuno-auth-<user SID>` with a DACL for the current user only and
  `PIPE_REJECT_REMOTE_CLIENTS`, client identity checked (`GetNamedPipeClientProcessId` + image path under the install
  dir); Unix domain socket `0600` in `$XDG_RUNTIME_DIR/kubuno/` (Linux) or `~/Library/Application Support/Kubuno/run/`
  (macOS). Requests: `access_token(account_key)`, `access_after_401(account_key, failed_jti)`, `accounts()`,
  `subscribe()` (session expired, account removed, switched). Apps never see a refresh token.
- **Revoked session** (`Genuine`): all feeds of the account pause, banner "Session expirée - se reconnecter", local
  data stays readable, the outbox is kept. Re-login with the **same** `AccountKey` resumes draining; a different user
  on the same server is a different account (its own DBs), so an outbox can never be sent as someone else.
- **Account switch**: views rebind to the other account's DBs; background sync keeps running for all accounts (Android
  `SyncCoordinator` lesson).
- **Websocket auth**: today the JWT is in the query string (logged by reverse proxies). Lot CORE-S3 adds a one-time
  ticket (`POST /api/v1/ws/ticket` -> 30 s single-use token) used as `?ticket=`; the client reconnects with a fresh
  ticket at each JWT refresh.
- **TOTP**: `kubuno-account::login` implements `requires_totp` -> `POST /auth/totp` (today unsupported on desktop).

---

## 10. Security

- **Secrets** (refresh token, DB keys) only in the OS store:
  - Windows: Credential Manager generic credentials (DPAPI-protected per user, blob <= 2560 bytes, enough), the same
    API `kubuno-data/secrets.rs` already uses;
  - macOS: Keychain generic password, `kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly` (not synced to iCloud);
  - Linux: Secret Service (D-Bus; GNOME Keyring/KWallet). No Secret Service (headless, minimal WM): refuse to store
    tokens persistently (session-only login) unless the user explicitly accepts a `0600` file with a warning.
  - The `keyring` crate covers the three back-ends; we wrap it in `SecretStore` so tests use an in-memory store.
- **Database encryption at rest**: SQLCipher (AES-256, page-level) with a random 256-bit key per account generated at
  first open and kept in the OS store (`Kubuno/<account_key>/dbkey`). Build: `libsqlite3-sys` feature
  `bundled-sqlcipher-vendored-openssl` (Windows needs Perl + NASM in the build image; macOS can use CommonCrypto with
  `bundled-sqlcipher`). Because cargo unifies `libsqlite3-sys`, enabling it in the app enables it for both sqlx and
  rusqlite. **Spike first (SE-0)**: build time, binary size (+~3 MB), CI images, and sqlx's `pragma("key", …)` ordering
  (key must be the first statement; statement logging disabled for that pragma). Fallback if the spike fails:
  OS-level protection only (profile ACL + BitLocker/FileVault/LUKS), tokens still in the OS store, chat bodies and
  document contents encrypted per row with an AES-GCM key from the OS store.
- **Wipe on sign-out**: if the outbox is not empty, a themed dialog "N modifications n'ont pas été envoyées"
  (Envoyer d'abord / Exporter / Supprimer quand même, default Envoyer d'abord); then delete the DB, `-wal`, `-shm`,
  `blobs/`, the account's secrets, Cloud Files registration and sync root config. Remote sign-out (device "not me",
  session revoked by an admin) only pauses; wiping on a remote signal is an admin policy (open question).
- **Logs**: never tokens, keys, payloads or message bodies; file and document names are user data and are logged at
  `debug` level only; `_sync_log` stores codes and ids. `ApiClient`'s `Debug` redacts `Authorization`, `Idempotency-Key`
  is fine to log.
- **Chat end-to-end**: the server is built for client-side encryption (`encrypted_data` + `nonce`, X3DH key routes
  under `/keys/*`; not verified in the web client during this study) while the desktop currently sends base64url
  JSON `{text}`. Before caching chat locally the desktop must use the same
  scheme as the web (or the server/web must confirm what `encrypted_data` contains); cached bodies are then either
  kept as ciphertext + decrypted on display, or decrypted and protected by SQLCipher. To be settled in C-1.

---

## 11. Multi-OS paths

`MULTI-OS-AUDIT.md` §3.1 plans `kubuno-paths` (core, tag `paths-v0.1.0`) for *server* locations. Add a `client`
module (feature `client`, no server deps) so the desktop and the server never disagree on rules (override variables,
never infer from `is_dir()`, `write_private`):

| Function | Windows | macOS | Linux |
|---|---|---|---|
| `user_config_dir()` (settings, `account.json`) | `%APPDATA%\Kubuno` (`FOLDERID_RoamingAppData`) | `~/Library/Application Support/Kubuno` | `$XDG_CONFIG_HOME/kubuno` |
| `user_data_dir()` (databases, outbox) | `%LOCALAPPDATA%\Kubuno` (**local**, a roaming profile must never copy an open SQLite DB) | `~/Library/Application Support/Kubuno` | `$XDG_DATA_HOME/kubuno` |
| `user_cache_dir()` (blobs that can be re-downloaded) | `%LOCALAPPDATA%\Kubuno\Cache` | `~/Library/Caches/Kubuno` | `$XDG_CACHE_HOME/kubuno` |
| `user_runtime_dir()` (broker socket) | named pipe, no dir | `~/Library/Application Support/Kubuno/run` | `$XDG_RUNTIME_DIR/kubuno` |

Known folders come from `SHGetKnownFolderPath` on Windows (not environment variables), `KUBUNO_USER_DATA_DIR` etc.
override for tests. One-time migration from `%APPDATA%\kubuno-desktop\` (and `%LOCALAPPDATA%\KubunoDrive\` for the
drive app's settings), logged. The drive app's illegal-name mapping (`sanitize`/`join_rel`) moves to the portable name
policy `kubuno-storage::names` (audit §3.3) so client and server share one rule set.

---

## 12. Views and data components

### 12.1 `SyncedTableAdapter`

A `TableAdapter` subclass (same `Connection`, `SelectCommand`, `UpdateTable`, `PrimaryKey`), with:

| Property | Meaning |
|---|---|
| `Feed` | the feed whose rows this table holds (`notes.notes`) |
| `Entity` | outbox entity name (default `UpdateTable`) |
| `KeyMint` | `ClientUuid` (default) / `ServerAssigned` (uses `_sync_id_map`) |
| `ConflictPolicy` | `FieldMerge` / `KeepBoth` / `ServerWins` / `Custom` (handler) |

- `Fill` reads the local table only (instant, offline).
- `Update(changes)` = in **one** local transaction: the generated DML of `TableAdapter` + one outbox op per row
  (`create` with the full row, `patch` with changed columns and `base_etag`/`base_row` from the row's original values,
  `delete`), then `ChangeBus.publish` and a push request to the scheduler. No network inside `Update`: saving never
  fails because the server is unreachable.
- The mapping row -> HTTP request is per entity, in Rust: `impl OutboxOp for NoteOps` (endpoint, body, how to read the
  answer). A `.kbdata` section declares the default (REST collection conventions) so simple entities need no code:

```toml
[sync]
feed = "notes.notes"
delta = "/api/v1/notes/notes/delta"
create = "POST /api/v1/notes/notes"
patch = "PATCH /api/v1/notes/notes/{id}"
delete = "DELETE /api/v1/notes/notes/{id}"
conflict = "keep_both"
```

- **Refresh on remote change**: the engine's `ChangeBus` (in-process `tokio::sync::broadcast` of table names) makes
  bound adapters re-run their select on the data runtime and merge by key (keep `Position` and the current edit, raise
  `ItemChanged`/`ItemAdded`/`ItemDeleted` instead of `Reset`, so lists do not jump). Cross-process (shell background
  sync writing while the app is open): `PRAGMA data_version` polled every second on an idle connection, plus the
  broker's notification.
- A row with `_pending > 0` exposes `IsPending` to bindings (italic or clock glyph); a row with an open conflict
  exposes `HasConflict`.

### 12.2 `SyncStatus` component and `SyncIndicator` control

`SyncStatus` (non-visual, tray): `State` (`Synced | Syncing | Pending(n) | Offline | SessionExpired | Error`),
`LastSync` (text "il y a 2 min"), `PendingCount`, `ConflictCount`, `Feeds` (rows for a details panel), commands
`SyncNow`, `ShowConflicts`; events `StateChanged`, `ConflictDetected`. `SyncIndicator` (visual) binds to it: icon +
text in a status bar, popup with per-feed details, "Synchroniser maintenant", link to the conflicts dialog.

`ConflictsDialog` (themed, from `kubuno-ui`): list of `_sync_conflicts`, side-by-side fields (base / mine / server),
actions per conflict; texts from `.kbres` (FR/EN).

### 12.3 Designer

- Design time never opens a real account: `SyncedTableAdapter` in the designer reads **sample rows** from the `.kbdata`
  (`[[tables.sample]]` TOML rows) or from a `samples/<table>.json`, shown in grids like the existing "Aperçu des
  données". The `SyncIndicator` shows a chosen `DesignState`.
- The Data Sources wizard (DATA-6) gets an option "Synchronisé avec Kubuno" that writes the `[sync]` section and the
  engine columns into the migration template.

---

## 13. Per-app plan

### 13.1 Shell

- `kubuno-account` adoption: accounts list from `AccountStore`, OS-store tokens, broker, TOTP, account switch.
- `shell.db`: `accounts_cache` (profile from `/me`), `activity` (from `/me/activity`; needs CORE-S2 for a cursor),
  `labels` + `label_links` (needs CORE-S2 delta), `module_config` (`/modules/:m/config`, refreshed on
  `SettingChanged`). Activity is pull-only; labels get an outbox (create/rename/delete with client ids).
- Sync status of all apps in the tray (aggregated `SyncStatus` per account).

### 13.2 Drive

- **D-1 metadata read model** (`drive.db`): `folders`, `files` (id, folder_id, name, size, mime, etag, starred,
  trashed, has_thumbnail, updated_at), from `/sync/delta?full=true`. Serves the Drive app (browse, search, recent,
  starred offline) and the shell. Separate from the file-sync `state.db` (a sync root is a *local mirror*; the
  metadata model is the *whole drive*), but the same pull code.
- **D-2 metadata outbox**: create folder (client id), rename, move, star (toggle rule), trash/restore, with
  `Idempotency-Key` (drive's is user-scoped, already safe). Name collisions: accept the server's auto-renamed name.
- **D-3 align the file engine**: `kubuno-sync` moves onto `kubuno-api`/`kubuno-account`, WAL + versioned migrations
  for `state.db`, deferred-item table (cursor never stuck), chunked/resumable upload (8 MiB, never resend an index,
  `complete` with its own key) and streamed download once DRIVE-S2 lands, real interval setting, JSON websocket
  hints. All guards and their tests stay as they are; nothing in D-3 changes the decisions of `push.rs`/`store.rs`.
- Thumbnails and offline-pinned contents in `blobs/drive/` keyed by etag.

### 13.3 Chat (after CHAT-S1)

- `chat.db`: `conversations`, `members`, `messages` (id, client_id, conversation_id, sender, type, body or ciphertext,
  reply_to, edited_at, deleted, pinned, created_at, `_seq`), `reactions`, `read_state`, `attachments` (metadata;
  bytes in `blobs/chat/`).
- Pull: `GET /api/v1/chat/sync/changes?cursor=` (to be built), plus backwards history paging on demand (older
  messages are fetched into the same table, not through the feed).
- Send offline: `messages` row with `client_id` and state "en attente", outbox `send` keyed by the client id; edits
  and deletes of pending messages coalesce into the send. Presence and typing are live only (never stored).
- Websocket stays the live channel; on reconnect the feed fills the gap (no more full refetch after each send).

### 13.4 Documents

- **DOC-1 (REST, no CRDT)**: `documents.db` = documents metadata from `/documents/delta` + content snapshots in
  `blobs/documents/<id>-<content_etag>.kbdoc.gz`; the session state machine gets a real `Journal` on disk and a real
  transport (`kubuno-api` with PATCH and headers). Offline edits are saved locally; at reconnection the save goes
  through the existing digest-compare rule; a mismatch produces a **"keep both"** copy (`<titre> (conflit …)`), never
  an overwrite. Must also check the Yjs room: if the document has a non-empty collab room, a REST save is refused
  locally and the user gets the copy (until DOC-2), because of the two-sources-of-truth defect.
- **DOC-2 (Yjs)**: the desktop model is ProseMirror JSON; `yrs` with the y-prosemirror mapping (the same XmlFragment
  layout as the web editor) gives a local Y.Doc per document stored as updates in `documents.db`; sync by state
  vector over the HTTP endpoints of OFFICE-S2 (offline catch-up) and the collab websocket (live). Large lot; requires
  the server to make the room the single source of truth.
- Prerequisite: OFFICE-S1 deployed (delta was returning 500 on dev).

### 13.5 Next candidates

calendar, contacts, tasks, notes: the server feeds and client ids exist, so a desktop app is mostly
`.kbdata` + `[sync]` + views. Before shipping them: PIM-S1 (create replay, `If-Match`, notes version) and CORE-S4
(shared items). Mail later (thread model, drafts gap, decision REST vs IMAP); photos and maps need server feeds first.

---

## 14. Server lots (gaps)

Rules for every lot: schema per module only, multi-table writes in one transaction, `change_seq` in the write
transaction, every DB error `tracing::error!` before returning, inputs validated before DB calls, no `unwrap`,
`cargo clippy -D warnings`, CHANGELOG `[Unreleased]` in each touched repo. Shared code goes through **tagged core
crates** (`kubuno-db` is already one, `db-v0.9.0`), never module-to-module imports.

| Lot | Repo(s) | Content | Size |
|---|---|---|---|
| **CORE-S1** idempotency | core (`kubuno-db` new module `idempotency`, tag `db-v0.10.0`), then drive/chat/office/PIM bumps | A reusable axum layer + table template: key scoped by **user id** (from `X-Kubuno-User-Id`/auth, not the header hash), body hash (409 on mismatch), in-flight row (second request waits or gets 409 `IN_PROGRESS`), > 4 MB answers stored as "done, re-read" instead of 500, hourly purge. Core's own middleware switched to it. | M |
| **CORE-S2** shell data feeds | core | `GET /me/activity?after=<event_log id>&limit` + read state; labels and label links on the journal (change_counter, tombstones, `/labels/delta`); clipboard optional. | M |
| **CORE-S3** websocket | core | One-time ws tickets; targeted `<module>.changed {feed, seq}` convention documented; per-connection sequence + `resync` message when a channel overflows; default to targeted delivery (no broadcast of user data events). | M |
| **CORE-S4** visibility-aware journal | core (`kubuno-db::journal`), then each module | `changes_since` takes a visibility predicate (owner OR shared to user) and emits `revoked` when access is lost (share removed -> per-user tombstone); `410 CURSOR_EXPIRED` with tombstone retention (e.g. 90 days) + purge job. | L |
| **DRIVE-S1** feed correctness | drive | `change_seq` in the write transaction everywhere (drop `next_seq_on_pool`); folder trash marks the subtree (or a `trashed_via` flag the client can use); image `PUT /:id/content` updates `content_hash` and takes `If-Match`; atomic `If-Match` (`UPDATE … WHERE content_hash = $base`); `If-Match` on rename/move/trash; `drive.changed` for folder ops and chunk completion; **fix `DELETE /folders/:id` deleting files on disk**; shared items via CORE-S4. | M |
| **DRIVE-S2** transfers | drive (+ core proxy streaming) | Received-chunk bitmap, idempotent chunk PUT, per-chunk hash, repeatable `complete`, streamed assembly; download with `ETag`, `If-None-Match`, `Range` (206), streamed; core proxy streams request bodies instead of `to_bytes(usize::MAX)`. | L |
| **CHAT-S1** chat feed | chat | `change_seq` (counter row) on messages, conversations, members, reactions, receipts; `GET /sync/changes?cursor` per user over member conversations, with tombstones (membership removed, conversation deleted, reaction removed); `client_id` on send (separate from the edit nonce), idempotent media upload; `clear` emits + tombstones; presence counts sockets. | L |
| **OFFICE-S1** deploy and harden | office | Deploy the pending fixes (`c3c6429`, migration `000064` after the user's go); counter row instead of `SEQUENCE` for all office feeds; `GET` must not bump `change_seq`; trash/restore rotate `etag`; atomic `If-Match`, `ETag` header; same idempotency for spreadsheets/presentations/diagrams/whiteboards; `office.changed` events; explicit body limit; shared documents via CORE-S4. | M |
| **OFFICE-S2** Yjs over HTTP | core (collab) + office | `GET /collab/:room/state?sv=<base64 state vector>` -> diff update; `POST /collab/:room/update` (idempotent by update hash); authorization **fail-closed**; one source of truth: when a room exists it is authoritative and a snapshot is written back to the `.kbdoc` by the server; REST `PATCH content_json` on a document with a live room is refused (409 `COLLAB_ACTIVE`). | L |
| **PIM-S1** | calendar, contacts, tasks, notes | Create replay with the same id and owner -> 200 + existing row (or `Idempotency-Key` via CORE-S1); `If-Match` on PATCH/DELETE (412 + current row); a `version`/`etag` on notes; reminders/shares in feeds; targeted `<module>.changed` events; CalDAV sync-collection and a real calendar PUT (separate, for third-party clients). | M per module |
| **MAIL-S1**, **PHOTOS-S1**, **MAPS-S1** | mail, photos, maps | mail: tombstones for retention/account removal, drafts/labels/folders feeds, a read-free message fetch; photos/maps: `change_seq` + tombstones + `/delta` + client ids (journal template). | M each |

---

## 15. Desktop lots and order

| Lot | Content | Size | Depends on |
|---|---|---|---|
| **SE-0** spike | SQLCipher with sqlx + rusqlite unification on Windows (and a macOS/Linux CI check), WAL multi-process with the lock-file writer, `keyring` back-ends, `kubuno-data` sharing a pool with the engine. Throw-away crate under `C:\kubuno-build`. | S | - |
| **SE-1** paths + accounts | `kubuno-paths::client`, `kubuno-account` (AccountKey, AccountStore, SecretStore, TokenOwner moved from `api.rs`, TOTP), migration from `%APPDATA%\kubuno-desktop` and `creds.json` to the OS store | M | SE-0 |
| **SE-2** API + broker | `kubuno-api` (any method, headers, errors, Retry-After), broker (named pipe / Unix socket), apps switched to borrowed tokens; documents' transport unblocked | M | SE-1 |
| **SE-3** engine | `kubuno-sync-engine`: LocalDb (key, pragmas, embedded migrations, account check), feeds (KDP + mail-style adapter), outbox (coalescing, classification, rollback, rebase), conflicts, ChangeBus, scheduler (all triggers of §8), status model, `_sync_log`; fake server + failpoint harness (§16) | L | SE-2 |
| **SE-4** data components | `SyncedTableAdapter`, `.kbdata [sync]`, `SyncStatus`, `SyncIndicator`, `ConflictsDialog`, designer sample rows, wizard option | M/L | SE-3, DATA-2/4/6 |
| **D-1** drive metadata pull | `drive.db`, Drive app browsing offline, shell uses it | M | SE-3 (SE-4 for views) |
| **D-2** drive metadata outbox | folder create, rename, move, star, trash/restore | M | D-1, DRIVE-S1 for atomic `If-Match` |
| **SH-1** shell data | `shell.db`: profile, activity, labels, module config; tray status | M | SE-4, CORE-S2 |
| **C-1** chat | §13.3 | L | SE-4, CHAT-S1, E2E decision |
| **DOC-1** documents REST offline | §13.4 | M | SE-2/3, OFFICE-S1 deployed |
| **D-3** file engine alignment | §13.2 | L | SE-3, DRIVE-S2 for transfers |
| **DOC-2** documents Yjs | §13.4 | L | DOC-1, OFFICE-S2 |
| **PIM-1..4** calendar, contacts, tasks, notes apps | views + `.kbdata [sync]` | M each | SE-4, PIM-S1, CORE-S4 |

**Recommended order**: SE-0 -> SE-1 -> SE-2 (also unblocks documents saving today) -> SE-3 (with CORE-S1 in
parallel on the server) -> **D-1 pilot** -> SE-4 -> D-2 -> SH-1 -> DOC-1 -> C-1 -> D-3 -> PIM apps -> DOC-2.

Why drive metadata as the pilot rather than shell + chat:
- the server feed exists, is commit-ordered (counter row, apart from DRIVE-S1's out-of-transaction writers) and is
  already consumed by two clients (desktop files, Android), so pilot problems are engine problems, not server ones;
- it starts **pull-only** (no conflict can lose data) and the outbox comes in D-2 against an endpoint family with a
  user-scoped idempotency that already survives token refresh;
- it brings visible value at once (the Drive app has no server access today);
- chat needs CHAT-S1 (no forward cursor exists), a large server lot, plus the E2E decision; shell activity and labels
  need CORE-S2. Both would make the pilot wait on server work.

In parallel, the engine's **contract tests run against the notes feed** (the cleanest KDP implementation) even
though no notes app exists yet: it validates the generic feed code on a second module from day one.

---

## 16. Testing and verification

### 16.1 Layers

1. **Unit** (engine): coalescing, classification table of §7.3, three-way merge, rebase over shadow, id mapping and
   dependent op rewrite, cursor guard, scheduler coalescing, redaction.
2. **Fake server** (`kubuno-sync-engine/tests/fake_server`): an in-process axum server implementing KDP for a toy
   entity with knobs: latency, 5xx/429 rates, "commit then drop the response", 412 on demand, out-of-order commit
   (to prove the client-side guard and to document why the server must be commit-ordered), auto-rename, tombstone
   expiry (410), duplicate websocket hints, token expiry and family revocation.
3. **Property test** (`proptest`): random interleavings of local edits on 2-3 simulated clients, server edits,
   disconnections and restarts; invariant: after everyone goes online and drains, all clients equal the server, no
   accepted intent is lost, every conflict is either merged or recorded.
4. **Crash tests**: the engine in a child process with `fail` crate failpoints (before/after local commit, after
   request sent, after response received, mid-page apply); the parent `kill -9`s (`TerminateProcess` on Windows) at
   each point, restarts, and checks: outbox op present or applied exactly once (server side counts by idempotency
   key), cursor never ahead of applied rows, DB `PRAGMA integrity_check` ok.
5. **Contract tests** against the real API on the dev core (`#[ignore]` + env `KUBUNO_DEV_URL`, test account
   credentials from the user secrets chain, never in the repo): login/refresh/rotation/grace, each module's delta shape
   (deserialize strictly, fail on unknown required fields), create replay, `If-Match` 412, idempotent replay
   (`idempotency-replayed: true`), websocket hint delivery. Server-side assertions (tombstone rows, change_seq order)
   only when the user has set `KUBUNO_DEV_DATABASE_URL` for `kubuno_dev` on 192.168.1.220 through the VS SSH tunnel;
   the tests read, never write, through that connection.
6. **Live** (per lot, on the dev core, Windows desktop): the scenarios below with screenshots.

### 16.2 Scenarios (each lot lists the ones it must pass)

| Scenario | Expected |
|---|---|
| Offline edits (forced offline + real cable/Wi-Fi off), then online | changes sent once, indicator Pending(n) -> Synced, web shows them |
| Same field edited on web and desktop while offline | field conflict recorded, dialog, both resolutions work |
| Different fields of one record | merged, no conflict |
| Item deleted on web, edited on desktop | `deleted_remotely` conflict; restore re-creates with the same id |
| Definitive rejection (422) | visible rollback + message, no zombie row |
| Response lost after commit (fake proxy drops it) | retry with same key, no duplicate |
| Token refreshed between send and retry | no duplicate (CORE-S1) |
| `kill -9` at each failpoint | no loss, no duplicate, DB consistent |
| Refresh token revoked (sign-out from web "devices") | SessionExpired, data readable, outbox kept; re-login resumes |
| Two accounts on the same server, two servers | DBs separated, no cross-sending, background sync for both |
| Account switch while a drain runs | drain finishes for the right account, UI switches |
| Clock skewed +/- 2 h on the client | ordering unaffected; only "il y a…" texts odd |
| Sleep 30 min then wake, network change (Wi-Fi -> 4G metered) | resync after wake; blobs deferred on metered |
| 50 000 rows full pull | pages of 500-2000, UI responsive, memory bounded |
| Shared item revoked (after CORE-S4) | row removed locally, pending ops on it -> conflict |
| Schema migration of the local DB with a non-empty outbox | outbox preserved, feed re-pulled |
| File-sync guards (D-3) | the existing guard tests of `kubuno-sync` still pass unchanged |

### 16.3 Definition of done per lot

Builds (`cargo build --release`), `cargo clippy -D warnings`, unit + fake-server + crash tests, contract tests on
dev where relevant, live check with screenshots, `cargo clean` after heavy builds, CHANGELOG entries in each touched
repo, nothing pushed by the agent.

---

## 17. Risks

- **SQLCipher build on Windows** (OpenSSL vendoring needs Perl/NASM) and link unification between sqlx and
  rusqlite: SE-0 decides before anything depends on it; fallback in §10.
- **Two token owners during migration** (old `kubuno-sync` code paths still refreshing from `creds.json` while the
  broker exists): SE-1 removes `creds.json` refresh paths in the same lot; a release note says "sign in again" is
  possible once.
- **Server feeds not commit-ordered** (office `SEQUENCE`, drive `next_seq_on_pool`): the client cannot fully guard
  against skipped changes; DRIVE-S1/OFFICE-S1 are prerequisites for write lots, and a weekly full resync of
  metadata feeds (`needs_full`) limits the damage meanwhile.
- **Owner-only feeds**: shared items invisible offline until CORE-S4; the UI must say "éléments partagés : en ligne
  uniquement" rather than show an incomplete list as complete.
- **Documents' two sources of truth**: DOC-1 must refuse REST saves when a collab room is active; real fix is
  OFFICE-S2.
- **Chat E2E** ambiguity (§10): blocks C-1 until settled.
- **Local DB growth** (chat history, mail): per-feed retention settings and `VACUUM` on idle; blobs as a bounded LRU.
- **Module isolation**: the engine is generic and knows modules only through feeds declared by apps; nothing in the
  shell hard-codes a module that may not be installed (feeds are enabled from `GET /api/v1/modules`).

---

## 18. Open questions (with recommended answers)

1. **Pilot**: drive metadata or shell + chat? -> *Drive metadata (pull-only first)*, §15.
2. **Database encryption**: SQLCipher for every desktop DB? -> *Yes, key per account in the OS store, after the
   SE-0 spike*; fallback = OS protection + per-row encryption of message bodies and document contents.
3. **Token owner**: the shell (broker) or each app? -> *The shell, apps borrow access tokens; an app starts the shell
   in background if needed.*
4. **Documents offline**: REST + keep-both now, Yjs later, or wait for Yjs? -> *REST + keep-both now (DOC-1),
   Yjs (DOC-2) after OFFICE-S2*; requires deploying the pending office fixes (migration `000064`, irreversible: the
   user's call).
5. **Sign-out with unsent changes**: block, export, or discard? -> *Dialog defaulting to "Envoyer d'abord", with
   "Exporter" and "Supprimer quand même"*; remote revocation pauses, never wipes, unless an admin policy says so.
6. **SQLite stack**: sqlx everywhere? -> *Yes for all new code (shared pool with `kubuno-data`, embedded
   migrations); `kubuno-sync` moves in D-3.*
7. **Chat E2E on desktop**: adopt the web's scheme before caching? -> *Yes, C-1 starts by aligning with the web
   client; cached bodies stay protected by SQLCipher.*
8. **Shared items in feeds** (CORE-S4) before or after the first PIM apps? -> *Before: an offline calendar without
   shared calendars would be misleading.*

---

## 19. Implementation status (foundation lots, 2026-10-01)

The user approved every recommendation of §18. The foundation lots are built as **libraries** in `desktop/common`
(the cross-OS workspace, next to `kubuno-sync`), listed in `windows/Kubuno.Core.Desktop.slnx` under `/Libraries/`.
Nothing is wired into the shell or the apps yet: the shell is being migrated by another lot, and its integration
(19.4) is scheduled after it. The drive metadata pilot (D-1), `SyncedTableAdapter` and the components (SE-4), chat,
documents and every server lot are the next lots.

### 19.1 SE-0: SQLCipher spike (decision: SQLCipher; the fallback is not needed)

Spike crate under `C:\kubuno-build\agent-syncf\spike` (throw-away): sqlx 0.8.6 + rusqlite 0.32 on one
`libsqlite3-sys` 0.30.1 with `bundled-sqlcipher-vendored-openssl`, Windows MSVC.

- **The Git Bash Perl cannot build OpenSSL** (`Can't locate Locale/Maketext/Simple.pm`, MSYS paths). A native
  Strawberry Perl (`PERL=<path>\perl.exe`) builds it with the `nmake` of VS 2026; without NASM, `openssl-src` adds
  `no-asm` by itself (no AES-NI). Alternatives evaluated: `sqlcipher-src` (SQLCipher + libtomcrypt) and
  `sqlite3mc-src` (SQLite3 Multiple Ciphers, no external crypto) are source-only crates: using them means a `-sys`
  crate of our own with `links = "sqlite3"`, i.e. forking `libsqlite3-sys` (sqlx and rusqlite require it), so they
  were rejected; a prebuilt library through `SQLITE3_LIB_DIR` is fragile across CI images. **Chosen**:
  `bundled-sqlcipher-vendored-openssl` on Windows and Linux, `bundled-sqlcipher` (CommonCrypto, no OpenSSL) on macOS,
  through target-specific dependency tables of `kubuno-sync-engine` (feature `sqlcipher`, on by default). Works
  offline once vendored (`openssl-src` carries the sources). Build requirements: `desktop/BUILD.md`, section
  "SQLCipher : prérequis de build".
- **Unification works**: with the feature on, rusqlite opens the sqlx-encrypted file with the same key, a wrong key
  fails, and both still read clear databases when no key is given (`kubuno-sync`'s `state.db` is unaffected).
- **sqlx key ordering**: `SqliteConnectOptions` puts `key` first among its pragmas; the raw-key form
  `PRAGMA key = "x'<64 hex>'"` (no KDF: the key is already 256 random bits) works; `disable_statement_logging()` keeps
  the pragma out of the logs. Opening with a key on a build **without** SQLCipher is refused (`PRAGMA cipher_version`
  returns nothing) instead of silently writing a clear file.
- **Measured overhead** (100 000 rows, averages of 3 runs, Windows VM, OpenSSL with NASM):

  | Workload | Clear | SQLCipher, default cache | SQLCipher, 64 MB cache |
  |---|---|---|---|
  | insert, 200 transactions of 500 rows | 3.2 s | +2.5 % | +3.6 % |
  | 10 000 point reads by key | 1.5 s | +33 % | +2 % |
  | 20 full scans (`LIKE`) | 0.6 s | +300 % | +73 % |

  Without NASM (`no-asm`) a mixed benchmark cost +192 % instead of +50 %. The engine sets `cache_size = -32768`
  (32 MB) on every connection. Binary size: +4.3 MB (static libcrypto). First OpenSSL build: about 12 minutes on the
  dev VM (sequential nmake), cached afterwards.
- **Not verified here**: SQLCipher for macOS needs the Apple SDK (`os/log.h`, CommonCrypto) and cannot be
  cross-checked from Windows (zig stops on `os/log.h`); Linux with vendored OpenSSL was not cross-built (needs `make`
  and a Linux toolchain). Both belong to the CI matrix (and the user's Mac).

### 19.2 Crates

| Crate | Content | Tests |
|---|---|---|
| `kubuno-secrets` | `SecretStore` trait; a Windows Credential Manager back-end **of our own** (generic credentials `Kubuno/<scope>/<item>`, `CRED_PERSIST_LOCAL_MACHINE`: `keyring` writes `CRED_PERSIST_ENTERPRISE`, which roams, and a refresh token presented from two machines revokes its family); macOS Keychain and Linux Secret Service through `keyring` 3 (`apple-native`; `async-secret-service` + `crypto-rust` = zbus, pure Rust); `MemorySecretStore`; opt-in `FileSecretStore` (`0600`) for a Linux session without Secret Service, never chosen silently; `OsSecretStore::probe`; `Secret` zeroized with a redacted `Debug`; `get_or_create` with read-back | unit; real OS store round trip (`--ignored`, run on Windows) |
| `kubuno-api-client` (the `kubuno-api` of §5.1) | async `ApiClient` (reqwest + rustls): any method, `If-Match`, `Idempotency-Key`, `X-Kubuno-Device-Key`, JSON or bytes; `TokenSource` trait (one refresh-and-retry on 401); retries with exponential backoff and jitter only for replayable requests (safe method or idempotency key), `Retry-After`; `ApiError` from `{"error","message"}` with the §7.3 classification (`ErrorClass`); KDP v1 types (`DeltaPage`, `Change`, `ChangeKind` whose `Other` is never read as a delete, opaque `Cursor` from a number or a string) and the "cursor must move" guard; redacted `Debug` | unit; contract tests against an axum fake; live tests gated on `KUBUNO_TEST_SERVER_URL` (plus an optional `KUBUNO_TEST_ACCESS_TOKEN` from the environment, never from a configuration file) |
| `kubuno-account` | `AccountKey` = `hex(sha256(normalized server URL + "|" + user id))[..16]`; `AccountStore` (`<data>/accounts/<key>/account.json`, no secret); `TokenOwner` (the §9 state machine: single flight, adopt-fresh, persist-before-use with read-back, rotation grace, genuine vs transient, 45 s cooldown, expiry from `exp - iat` on the monotonic clock, revoked session, signing in again restores the same key, account switch, events, per-account database key); `login` with the **TOTP step**; the broker (protocol v1 = JSON lines; server for the shell, client and `BrokerTokenSource` for the apps); `migrate::adopt_legacy_instances` (plaintext `creds.json` to the OS store) | unit; owner against a fake auth server that models rotation, reuse detection and grace; broker in-process over the real pipe/socket; **two-process** test (the test binary re-run as an "app"); refused client policy |
| `kubuno-sync-engine` | `paths` (§11); `LocalDb` (SQLCipher key, pragmas, engine schema versioned in `_sync_meta.engine_schema`, app migrations through a `sqlx::migrate::Migrator`, feed reset on schema change, read-only after a failed migration, account check, in-flight recovery, `SyncLock` single writer); outbox (`Intent`, coalescing of never-sent ops only, `create` + `delete` cancel, shadow rows, rebase, `_pending`); push with the §7.3 table and **explicit rollback**; pull (page + cursor in one `BEGIN IMMEDIATE` transaction, `change_seq` ordering, tombstones, full resync on `410 CURSOR_EXPIRED` deleting unseen rows without pending ops); policies (field merge, keep both, server wins, last writer wins, custom); conflict records and `resolve_conflict` (keep mine / keep server, restore after a remote delete); `Scheduler` (startup, local-write debounce, interval, hint, network change, resume, manual; coalescing); `SyncStatus` (watch) and `SyncEvent` (broadcast); `JsonTableAdapter` (convention table + REST collection) | unit; §16.2 scenarios against a fake KDP server (offline edits, merge, field conflict both ways, deleted remotely + restore, 422 rollback, lost response, rebase, delete vs edit, keep both, cursor expiry, clock skew ±2 h, multi-account isolation, session expiry, migrations); **crash tests** (child process killed with `TerminateProcess`/`SIGKILL` mid-page, after the server applied a write, before the local commit); scheduler; SQLCipher |

Choices that differ from the text above, and why:

- **One SQLite stack, sqlx**, but the engine does **not** depend on `kubuno-data` (which depends on `kubuno-views`, a
  UI crate): it uses the same mechanism (`sqlx::migrate!` files, the *Migrations* node of DATA-7) and exposes its
  `SqlitePool`, which SE-4 hands to `kubuno-data`. The engine's own tables use their own version counter, so their
  numbering never collides with the app's `_sqlx_migrations`.
- `paths` lives in the engine (the `kubuno-paths::client` of §11 does not exist yet; another lot creates
  `kubuno-paths` on the server side): same variable names (`KUBUNO_USER_*_DIR`), Windows known folders through
  `SHGetKnownFolderPath`, never `is_dir()` inference. It becomes a re-export when `kubuno-paths` gains `client`.
- The broker also hands out the **database key** (`database_key {account}`): apps open their own SQLCipher databases
  without touching the OS store; refresh tokens never cross the broker.
- Coalescing only merges into ops with `attempts = 0`: an op that was sent may have been applied, so it is never
  rewritten (a different body under a new key could run twice).
- A local delete answered 412 stays in `conflict` with the **server row visible** (`delete_vs_edit`); the rebase
  skips such deletes.
- The fallback of §10 (per-row AES-GCM) was not needed and is not built.

### 19.3 Multi-OS verification

- Windows: build, tests, `clippy -D warnings` (with and without `sqlcipher`).
- Linux (`x86_64-unknown-linux-gnu`) and macOS (`aarch64-apple-darwin`): `cargo check` and `clippy -D warnings` of
  the four crates **cross-compiled from Windows with `zig cc`** (C dependencies such as `ring` included), engine
  without `sqlcipher`; tests not run there. SQLCipher itself: see 19.1.

### 19.4 Shell integration (next, after the shell's migration lot)

1. At start: `migrate::adopt_legacy_instances(paths::legacy_config_dir(), …)` (user id from the JWT `sub`, else
   `GET /me`), then `TokenOwner::load`; on Linux, `OsSecretStore::probe()` first and the session-only / file fallback
   dialog when there is no Secret Service.
2. **Remove every `creds.json` refresh path of `kubuno-sync` in the same lot** (`Api::refresh`, `Creds::save`): the
   file-sync daemon takes its tokens from `OwnerTokenSource` (risk "two token owners", §17).
3. `BrokerServer::serve` with `BrokerEndpoint::for_current_user(paths::user_runtime_dir())` and
   `ClientPolicy::ImagesUnder(install dir)`; apps use `BrokerClient::token_source(account)` and start
   `kubuno-desktop --background` when the broker is unreachable (a transient error, never "session expired").
4. Sign-in page: `login::login` -> `TotpRequired` -> `login_totp` -> `TokenOwner::sign_in`. Sign-out:
   `unsent_count() > 0` opens « Envoyer d'abord / Exporter / Supprimer quand même », then `sign_out`,
   `LocalDb::wipe` and `AccountStore::remove_dir`.
5. Scheduler hooks: `shell.json` `sync_interval_min` -> `Scheduler::set_interval`; `WM_POWERBROADCAST` -> `Resume`;
   network list manager -> `NetworkChanged`; websocket `<module>.changed` -> `PushHint`; `settings.json` `offline`
   -> `set_forced_offline`.

### 19.5 Open risks found while building

- **`410 CURSOR_EXPIRED` and paging a full snapshot**: after a reset the client pages from `cursor=0`, each page's
  last `change_seq` being the next cursor; those cursors are older than the tombstone horizon by construction.
  CORE-S4 must not answer 410 to them (for example a `snapshot=true` parameter, or a horizon check only outside a
  snapshot). The fake server answers 410 once to model the client side.
- Windows CI needs a native Perl (preinstalled on GitHub runners) and preferably NASM (not preinstalled).
- On Linux the `os` back-end of `kubuno-secrets` uses zbus with Tokio: callers on a Tokio runtime call the store from
  `spawn_blocking` (the token owner does).
- Pipe squatting by a process of the same user is only prevented while the shell runs
  (`FILE_FLAG_FIRST_PIPE_INSTANCE`); the client does not yet verify the server's image path
  (`GetNamedPipeServerProcessId`).
- Server prerequisites unchanged: CORE-S1 (user-scoped idempotency), DRIVE-S1 (commit-ordered drive feed) before
  D-2, PIM-S1 before PIM writes.
