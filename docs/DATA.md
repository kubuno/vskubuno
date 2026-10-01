# Data access components & tooling (requirements) — product owner, 2026-09-29

Goal: make database access as easy as WinForms/ADO.NET + VS data tooling, idiomatic Rust, secure by default.

## Runtime components (kubuno-views / a new `kubuno-data` crate)
- **Connection component** (non-visual, in the component tray): provider (PostgreSQL first — Kubuno's stack —, SQLite, MySQL/MariaDB, SQL Server), connection string **resolved from settings/user secrets or an OS credential store, never hard-coded**, pooling (sqlx pools), async open/close on the UI dispatcher, state events (StateChange), retry policy.
- **Query/command components** (TableAdapter-like): parameterized queries only (no string concatenation), `Fill`/`Update`/`Insert`/`Delete`, typed rows generated from the schema (compile-time checked via `sqlx::query_as!` with an offline `.sqlx` cache — `SQLX_OFFLINE`), transactions (`TransactionScope`-like), stored procedures/functions, cancellation, paging.
- **BindingSource**: current item, position, filter, sort, add/edit/cancel/end-edit, change tracking (row states Added/Modified/Deleted/Unchanged), `ListChanged`/`CurrentChanged`/`PositionChanged` events; binds controls (`{Binding Source=customersBinding, Path=Name}`), master/detail relations.
- **BindingNavigator** control (first/prev/next/last, position, count, add, delete, save).
- **DataTable/ListView/ComboBox** bound to a BindingSource with in-place editing, validation (`RowValidating`), error provider (ErrorProvider-like component showing field errors).
- Async everywhere: loading never blocks the UI thread (EVT-6 dispatcher/executor), progress + cancellation.

## VS tooling
- **Data Explorer** tool window (Server Explorer / SQL Server Object Explorer equivalent): add connection dialog (themed, test connection, credentials to user secrets / Windows Credential Manager), browse schemas/tables/views/columns/keys/indexes/functions, view data, run queries (query window with results grid), generate SELECT/INSERT scripts.
- **Data Sources** window + **"Ajouter une source de données…" wizard**: pick connection → tables/views/queries → generates typed Rust row structs + query components (surgical code generation into a `data/` module, regenerable parts clearly separated from user code).
- **Drag & drop from Data Sources onto a view** creates DataTable/detail fields + BindingSource + BindingNavigator wired up (WinForms behaviour).
- **Migrations**: `sqlx migrate` add/run/revert commands, migration file templates, status in Solution Explorer; `cargo sqlx prepare` command to refresh the offline cache (and a build warning when stale).
- IntelliSense in SQL strings inside `query!`/`query_as!` (tables/columns completion from the connected schema), SQL syntax colouring in those strings.
- Kubuno modules: dedicated schema per module (`<module>`) enforced by templates/wizards; all DB errors logged (tracing::error!) before returning; inputs validated before DB calls (Kubuno rules).

## Security rules
No credentials in source, logs or `.kbview`; secrets via settings with user-secret storage; parameterized queries only; least-privilege connection hints; TLS on for remote servers by default.

---

# Design (2026-09-30)

> Scope: the requirements above, mapped onto the existing Kubuno desktop stack: `kubuno-views` (XML views,
> the binding engine, the EVT-6 executor, the EVT-7 component hierarchy and registry), the designer (component
> tray, Properties window, ⚡ tab) and `vskubuno`. Status: **DATA-1 to DATA-8 are built** (see the
> "as built" sections at the end — §12 to §20).

## 1. Principles

- **ADO.NET's shape, Rust's guarantees.** The WinForms vocabulary is kept where developers look for it
  (`Connection`, `TableAdapter`, `BindingSource`, `ErrorProvider`, `Fill`, `Update`, `EndEdit`, row states,
  `ListChanged`…), but values are a closed enum, errors are `Result`s, and nothing ever builds SQL from values.
- **Two data paths, one runtime.** *Dynamic* components (a `TableAdapter` configured from the `.kbview` or from code,
  schema discovered at run time) cover the designer's drag-and-drop scenarios; *typed* access (`sqlx::query_as!`
  against an offline `.sqlx` cache, generated at compile time from a data-source description, DATA-4) covers business
  code. Both run on the same connection component, pools and secrets.
- **The UI thread never waits on the database.** Every I/O runs on a private Tokio runtime owned by `kubuno-data`;
  the UI side awaits a runtime-agnostic handle from an EVT-6 task (`spawn_local` / async handlers) and applies the
  result on the UI thread (`UiHandle::update`). Components that raise events (`BindingSource`, `DbConnection`) are
  UI-thread objects (`!Send`, like `Event<A>`); the data they exchange with the I/O side (`Table`, `DbValue`, change
  sets) is plain `Send` data.
- **The view is the source of truth** (vskubuno rule "never regenerate what the developer owns"): data components are
  declared in the `.kbview` like `<Timer>` (the designer shows them in the component tray), and no generated code is
  versioned: typed code is produced at compile time (proc macro), never written into the developer's files.
- **Kubuno rules apply to the library itself**: zero `unwrap` outside tests, every DB error logged with
  `tracing::error!` before it is returned, inputs validated before any DB call, a dedicated schema per Kubuno module,
  no secret in logs, errors, `Debug` output or `.kbview`.

## 2. Crate layout

New workspace member `desktop/windows/src/crates/kubuno-data` (library, rlib — never part of the `kubuno_ui` dylib, so
adding it rebuilds nothing of the shared runtime):

```text
kubuno-data/
  Cargo.toml            features: default = ["sqlite", "postgres", "credential-manager"]
                        sqlite, postgres (sqlx 0.8 drivers), mysql (DATA-3), mssql (DATA-3, tiberius)
                        credential-manager (Windows Credential Manager through windows-sys)
  src/lib.rs            re-exports, `user_secrets!()`
  src/error.rs          DataError (thiserror): Config, Secret, Validation, Conversion, Database, Concurrency,
                        Cancelled, Closed — Display never contains a connection string or a value of a secret
  src/value.rs          DbValue (Null, Bool, Int, Float, Text, Bytes) ↔ kubuno_views Value; DbType
  src/rt.rs             the private Tokio runtime (2 workers, "kubuno-data"), DataTask<T> (await from any
                        executor, cancel = abort), block_on for tests and tools
  src/secrets.rs        SecretSource trait; UserSecrets, CredentialManager, Environment; SecretResolver chain
  src/conn_string.rs    ConnectionStringBuilder (key=value and URL forms, redacted Display/Debug)
  src/provider.rs       Provider enum, placeholder style, identifier quoting, the per-driver pools
  src/sql.rs            named parameters (@name → $n / ?n) with a small lexer (strings, quoted identifiers,
                        comments), identifier validation
  src/connection.rs     DbConnection component + ConnectionHandle (Send + Sync, clone of the pool slot)
  src/command.rs        DbCommand component: parameterized text, execute / scalar / query
  src/table.rs          Table, DataColumn, DataRow, RowState, change sets, accept/reject
  src/adapter.rs        TableAdapter component: Fill, Update (generated parameterized DML in one transaction)
  src/binding_source.rs BindingSource component: view (filter + sort), position, edit buffer, events
  src/filter.rs         the Filter / Sort expression language
  src/error_provider.rs ErrorProvider component
  src/context.rs        DataContext: the view's data components by x:Name, binding path routing, event pump,
                        the async Fill / Save helpers over UiHandle
```

MySQL/MariaDB reuse sqlx (`mysql` feature). SQL Server is **reasonable through `tiberius`** (pure Rust TDS, rustls or
native TLS, Windows integrated auth through SSPI): it has no pool and its own row/param types, so it gets its own
driver arm (a small bb8-style pool of `tiberius::Client`s on the same Tokio runtime) behind the `mssql` feature; the
rest of the crate only sees `Pool` and `DbValue`, so no component changes.

## 3. Connection component

```rust
#[derive(Component, Default)] #[kubuno(extends = Component)] #[toolbox(icon = "database")]
pub struct DbConnection {
    base: ComponentCore,
    #[property] pub provider: Provider,               // Postgres (default) | Sqlite | MySql | SqlServer
    #[property] pub connection_string_name: String,   // key resolved through the secrets chain
    #[property] pub connection_string: String,        // only for secret-free strings (sqlite:app.db)
    #[property] pub schema: String,                   // PostgreSQL: SET search_path (Kubuno: one per module)
    #[property] pub max_pool_size: u32,               // 10
    #[property] pub connect_timeout: u32,             // seconds, 15
    #[property] pub retry_count: u32, pub retry_delay: u32,   // 3, 200 ms, exponential backoff
    #[event] pub state_change: Event<StateChangeEventArgs>,   // OnStateChange
}
```

- **States** (ADO.NET `ConnectionState`): `Closed → Connecting → Open`, `Broken` after a failure; `StateChange`
  (`original_state`, `current_state`) is raised on the UI thread.
- **Open is async and lazy**: `open(&mut self) -> DataTask<()>` builds the pool on the data runtime (retrying transient
  connect errors with backoff), and every command opens it on first use. `close()` drops the pool (connections are
  closed on the runtime). `handle()` returns a `ConnectionHandle` (`Arc`, `Send + Sync`) for commands and adapters.
- **Pools** are sqlx pools (`max_pool_size`, `connect_timeout` as acquire timeout). An in-memory SQLite database
  (`sqlite::memory:`) gets one connection that never expires (each connection would otherwise be a new database).
- **Secrets resolution** (`SecretResolver`, first hit wins): (1) the environment variable
  `ConnectionStrings__<Name>` (CI, services — the .NET convention), (2) the **Windows Credential Manager**, generic
  credential `Kubuno:<UserSecretsId>:ConnectionStrings:<Name>` (the secret is the credential's blob, UTF-8), (3) the
  **user secrets store** `%APPDATA%\Kubuno\UserSecrets\<UserSecretsId>\secrets.json`, a flat JSON object
  (`{"ConnectionStrings:Northwind": "postgres://…"}`), for development. The id comes from the application's
  manifest: `[package.metadata.kubuno] user-secrets-id = "<uuid>"`, read at compile time by
  `kubuno_data::user_secrets!()` (`include_str!` of the manifest, no build script). A connection string may also
  keep secrets out of itself with placeholders, `Password={secret:NorthwindPassword}`, resolved through the same
  chain at open time. A literal `ConnectionString` containing a password, or a `ConnectionString` with a
  `{secret:…}` that no source resolves, is refused with a `DataError::Secret` naming the key, never the value.
- **Connection-string builder**: `ConnectionStringBuilder` parses and writes both forms (`Host=…;Database=…;` and
  `postgres://user@host/db?sslmode=require`), exposes typed accessors (`host`, `port`, `database`, `username`,
  `ssl_mode`), redacts the password in `Display`/`Debug` (`Password=***`), and converts to the driver's options.
  **TLS by default for remote servers**: a PostgreSQL host that is not local (`localhost`, `127.0.0.1`, `::1`, a
  socket path) without an explicit `sslmode` gets `sslmode=require` (native TLS on Windows, SChannel).
- **Least-privilege hint**: after connecting to PostgreSQL the component checks `rolsuper` of `current_user` and logs a
  warning (once per pool) when the application connects as a superuser.
- **Kubuno schema rule**: `Schema="drive"` sets `search_path` on every pooled connection (`after_connect`,
  identifier validated then quoted), so unqualified names in adapters stay inside the module's schema.

## 4. Commands, tables and the TableAdapter

**Values.** `DbValue { Null, Bool, Int(i64), Float(f64), Text(String), Bytes(Vec<u8>) }` plus `DbType` (the
column's logical type and its native name, e.g. `Int` / `"INT4"`). Types without a lossless native mapping
(timestamps, dates, UUID, JSON, NUMERIC) travel as `Text` in ISO/canonical form; on PostgreSQL every parameter of
generated DML is written `CAST($n AS <native type>)`, so a text timestamp or a 64-bit integer lands in a
`timestamptz` / `int4` column exactly as the server parses it (native type names come from the driver's column
description and are validated before use).

**Parameters only.** A command's text is static; values are parameters. Commands accept named parameters
`@name` (provider-neutral, the ADO.NET convention) which a small lexer rewrites to `$n` (PostgreSQL) or `?n` (SQLite)
without touching string literals, quoted identifiers or comments; a repeated name reuses its number. A missing or
unused parameter is a `DataError::Validation` before any I/O. There is no API that accepts a value inside SQL text.
Identifiers the library itself writes (table and column names of generated DML) are validated
(`[A-Za-z_][A-Za-z0-9_$]*`, optionally `schema.table`) and double-quoted.

**`DbCommand` component** (`Connection`, `CommandText`, `CommandTimeout`): `param(name, value)`, then
`execute_non_query() -> DataTask<u64>`, `execute_scalar() -> DataTask<DbValue>`, `query() -> DataTask<Table>`; the
same with an explicit transaction (DATA-3: `TransactionScope`, stored procedures, cancellation tokens, paging).

**`Table`** (ADO.NET `DataTable`; named `Table` because `DataTable` is the grid control's element name): columns
(`name`, `DbType`, `nullable`, `max_length`, `read_only`, `auto_increment`, `primary_key`) and rows. A `DataRow` keeps
its current values, its original values (from the last Fill/accept), a `RowState`
(`Unchanged | Added | Modified | Deleted | Detached`), a row error and per-column errors (`SetColumnError`).
`accept_changes` / `reject_changes` / `changes()` / `has_changes()` behave as in ADO.NET; deleted rows stay in the
table (hidden from views) until accepted.

**`TableAdapter` component** (`Connection`, `SelectCommand`, `UpdateTable`, `PrimaryKey`, `AutoIncrementKey`):

- `fill(params) -> DataTask<Table>` runs the select with the connection's retry policy; the column schema comes from
  the driver's `describe`, so an empty result still has typed columns; key columns come from `PrimaryKey` (default:
  a column named `id`).
- `update(&changes) -> DataTask<UpdateResult>` generates, per changed row, a parameterized `DELETE … WHERE key = @k`,
  `UPDATE … SET c = @c … WHERE key = @k`, `INSERT … VALUES (…) RETURNING key` (PostgreSQL and SQLite ≥ 3.35), runs
  them **in one transaction** (order: deletes, updates, inserts — a unique value freed by a delete can be reused),
  and checks that each statement affected exactly one row (else `DataError::Concurrency`, ADO.NET's
  `DBConcurrencyException`, and a rollback). Nothing is applied to the table until the commit succeeded: then the UI
  side accepts the changes and writes back generated keys. No retry after a commit may have happened.
- Optimistic concurrency on original values / `xmin` / rowversion, custom `InsertCommand`/`UpdateCommand`/
  `DeleteCommand`, and a `TableAdapterManager` for hierarchical updates are DATA-3.

## 5. BindingSource

`BindingSource` (`DataSource` = the adapter's x:Name, `Filter`, `Sort`, `AllowNew`, `AllowEdit`, `AllowRemove`) owns a
`Table` and a **view**: the indices of the rows that are not deleted, pass the filter and are in sort order.

- **Currency**: `position` (−1 when empty), `current()`, `move_first/previous/next/last`, `set_position`; changing the
  position first ends the current edit (WinForms), and stays put when validation refuses it (a `DataError` event is
  raised and the errors stay visible).
- **Editing**: the first write to a field of the current row begins an edit (`begin_edit` snapshot). Writes come from
  bindings as `Value`s and are converted to the column's type; a text that does not convert (`"abc"` for an integer)
  is kept as the *proposed text* of that field, so the bound text box keeps showing what the user typed, and becomes
  a column error. `end_edit()` validates (pending conversions, `NOT NULL`, `max_length`, then the cancelable
  `RowValidating` event where application code adds its own column errors), commits the values into the row
  (`Unchanged → Modified`; `Added` stays `Added`), raises `ListChanged(ItemChanged)` and `CurrentItemChanged`.
  `cancel_edit()` restores the snapshot, or removes a row being added. `add_new()` raises `AddingNew`, appends a row in
  edit mode and moves to it; `remove_current()` marks it `Deleted` (an added row is simply dropped).
- **Filter and sort** are small expression languages evaluated in memory: `Name LIKE 'A%' AND Age >= 18`,
  `Email IS NOT NULL`, `Name ASC, Age DESC` (comparisons `= <> != < <= > >=`, `LIKE` with `%`/`_`, `IS [NOT] NULL`,
  `AND`/`OR`, parentheses). A malformed expression is a `DataError::Validation` when it is set, never at paint time.
- **Events** (UI thread, Rust subscribers first, then the `.kbview` handler of the element): `ListChanged`
  (`ListChangedType::{Reset, ItemAdded, ItemDeleted, ItemChanged, ItemMoved}`, new/old index), `CurrentChanged`,
  `PositionChanged`, `CurrentItemChanged`, `AddingNew`, `RowValidating` (cancelable, carries the row's errors),
  `DataError`.
- **Master/detail** (DATA-3): a `BindingSource` whose `DataSource` is another binding source and `DataMember` a
  relation (`orders.customer_id = customers.id`) filters its rows on the parent's current key and refills on
  `CurrentChanged`.

## 6. ErrorProvider

`ErrorProvider` (`DataSource` = a binding source) shows the column errors of the current row and the binding source's
last `DataError`. DATA-1 exposes them to bindings (see §7): `{Binding Source=errors, Path=Email}` is the message,
`Email.HasError` a bool (for `Invalid` on text fields), `HasErrors` and `Summary` for a callout. Application code can
also `set_error(field, message)` / `clear()`. DATA-2 adds the WinForms adornment: an error glyph drawn next to every
control bound to a field in error, with the message as its tooltip (an extender like `ToolTip`, painted by the
runtime), `BlinkStyle` and `IconAlignment`.

## 7. Bindings: `{Binding Source=…, Path=…}` and the view model

The binding engine is unchanged in shape: a property's `{Binding}` reads `ViewModel::get(path)` every frame and
two-way writes call `ViewModel::set(path, value)`. `parse_binding` learns the named forms `Path=` and `Source=`, and
`{Binding Source=customers, Path=Name}` is sugar for the path `customers.Name` (`{Binding Source=customers}` alone is
`customers`). The view model owns a **`DataContext`** (built from the view's own text, so the XML stays the source of
truth) and forwards paths to it:

```rust
impl ViewModel for MainViewModel {
    fn get(&self, path: &str) -> Option<Value> { self.data.get(path).or_else(|| match path { … }) }
    fn set(&mut self, path: &str, value: Value) { if !self.data.set(path, value.clone()) { … } }
}
```

| Path | Value | Notes |
|---|---|---|
| `customers` | `Value::List` of `Row`s (fields named after the columns) | `ItemsSource` of `DataTable`/`ListView`/`ListBox`/`ComboBox` |
| `customers.Position` | `F32`, two-way | `SelectedIndex` of a list control |
| `customers.Count`, `.PositionText`, `.HasChanges`, `.IsEditing`, `.CanMovePrevious`, `.CanMoveNext` | read-only | navigator labels and button states |
| `customers.Current.Name` (or `customers.Name`) | the current row's field, two-way | detail controls |
| `errors.Name`, `errors.Name.HasError`, `errors.HasErrors`, `errors.Summary` | read-only | ErrorProvider |
| `db.State` | `Str` | connection state |

Field values reach bindings as `Bool` for boolean columns and as display text for every other type (the controls that
show record fields are text controls; a number keeps its exact digits). Typed conversions per target property
(`F32` for a `NumericField`, date formats, `NullValue`, `FormatString`) are DATA-2.

**Events declared in XML** (`<BindingSource OnCurrentChanged="customers_current_changed"/>`) are queued by the
component and delivered by `DataContext::pump(vm)` through the view model's typed handlers
(`kubuno_views::events::dispatch_typed`), so the ⚡ tab and `#[event_handlers]` work for data components as for
controls. The async helpers pump after each operation; a view model calls `pump` at the end of `set`.

**Async operations** follow EVT-6's rule (no borrow across `.await`): `DataContext` starts an operation on the UI
thread (`begin_fill`, `begin_save`: validate, snapshot the change set, raise `Connecting`), awaits the `DataTask`, then
completes it on the UI thread (`end_fill`, `end_save`: replace/accept rows, raise `ListChanged(Reset)`, errors to the
ErrorProvider). The one-liners for handlers:

```rust
async fn on_load(ui: UiHandle<Self>) { let _ = kubuno_data::fill(&ui, "customers").await; }
async fn save_click(ui: UiHandle<Self>) { let _ = kubuno_data::save(&ui, "customers").await; }
```

(`V: HasDataContext`, a one-method trait returning the view model's `DataContext`). DATA-2 moves the ownership of the
components into the view runtime (the XML-created instances become the live ones, reachable as
`runtime.component::<BindingSource>("customers")`), after which the forwarding in `get`/`set` is no longer needed.
*As built (§13): the runtime owns them; `runtime.with_component::<BindingSource, _>("customers", |bs| …)`, the
helpers `kubuno_data::fill/save/save_all(&ui, …)` need no trait, and `DataContext` remains for code outside a view.*

## 8. BindingNavigator (DATA-2)

A visual control (`ToolStrip`-like row: ⏮ ◀ `Position` / `Count` ▶ ⏭ | ＋ 🗑 | 💾) with `BindingSource` and optional
`SaveCommand` (handler name) properties. It reads and drives the binding source through the same paths as §7
(`Position`, `Count`, `CanMove…`, `add_new`, `remove_current`), and raises `ItemClicked` for custom items. The
designer's "drag a table from Data Sources" drops one above the grid.

## 9. Designer and Visual Studio integration

- **Registry**: every component is a `#[derive(Component)] #[kubuno(extends = Component)]` class; its static
  constructor registers it (EVT-7b), so an application that links `kubuno-data` (`extern crate kubuno_data as _;` in
  `main.rs`, which the template adds) gets `<DbConnection>`, `<DbCommand>`, `<TableAdapter>`, `<BindingSource>` and
  `<ErrorProvider>` as known elements: validation, completion, hover, the Toolbox (*Données* tab, after a build), the
  **component tray** (non-visual: `registry::is_non_visual`), the Properties window (categories *Données*,
  *Comportement*, *Connexion*) and the ⚡ tab (`OnStateChange`, `OnCurrentChanged`, …). Before the project is built the
  language server's scan of path dependencies declares them (placeholders in the tray, metadata complete).
- **Property editors** (DATA-6): `DataSource`/`Connection` get a drop-down of the view's other data components of the
  right class (`#[editor("ComponentReference:TableAdapter")]`), `ConnectionStringName` a drop-down of the names known
  to the Data Explorer with "Nouvelle connexion…", `SelectCommand` a SQL editor with "Tester la requête", `Filter` and
  `Sort` column pickers. Smart tags: "Aperçu des données…", "Ajouter une requête…", "Modifier la requête…".
- **Data Explorer** (DATA-5): a tool window backed by a small Rust helper (`kubuno-data-tool`, JSON over stdio, the same
  crate), so schema introspection, connection tests and queries use exactly the runtime's drivers. Connections are
  stored by name; secrets go to user secrets or the Credential Manager (a themed dialog with "Tester la connexion"),
  never to a project file. Tree: server → schemas → tables/views (columns, keys, indexes) → functions; "Afficher les
  données", a query window with a results grid, "Générer SELECT/INSERT".
- **Data Sources window and wizard** (DATA-6): "Ajouter une source de données…" (connection → tables/views/queries →
  name) writes a declarative `src/data/<name>.kbdata` (connection name, schema, tables with their columns and keys,
  named queries) — the developer-owned source of truth —, and `kubuno_data::data_source!("customers.kbdata")` expands
  at compile time into typed row structs and `sqlx::query_as!` calls (offline `.sqlx` cache, `SQLX_OFFLINE=true` in
  builds); user code extends them in plain `impl` blocks next to it. Nothing generated is written into the project.
  Dragging a table onto a view inserts, surgically, a `<DataTable>` (or detail fields: label + bound control per
  column), a `<BindingSource>`, a `<TableAdapter>`, a `<BindingNavigator>` and, if missing, the `<DbConnection>`.
- **Migrations** (DATA-7): commands "Ajouter une migration" (template with up/down), "Appliquer", "Annuler la
  dernière", status under a *Migrations* node in Solution Explorer (applied / pending), "Mettre à jour le cache SQLx"
  (`cargo sqlx prepare`), and a build warning when `.sqlx` is older than a migration or a `.kbdata`. The Kubuno module
  templates create `migrations/` with `CREATE SCHEMA IF NOT EXISTS <module>`.
- **SQL IntelliSense** (DATA-8): the Rust completion source recognises string literals passed to `query!`,
  `query_as!`, `query_scalar!` and `DbCommand::new`/`CommandText`, colours them as SQL, and completes tables, columns
  and functions from the Data Explorer's cached schema snapshot of the connection the data source names
  (`.vs/kubuno/schema/<connection>.json`); unknown columns get a warning squiggle before `cargo sqlx prepare` would
  fail.

## 10. Lots

| Lot | Content | Size | Depends on | Tests / live verification |
|---|---|---|---|---|
| **DATA-1** ✅ | `kubuno-data` crate: private runtime + `DataTask`, secrets chain (env, Credential Manager, user secrets) and `ConnectionStringBuilder`, `DbConnection` (PostgreSQL + SQLite, pools, states, retry, schema, TLS default), `DbCommand`, `Table`/row states, `TableAdapter` Fill/Update (transactional generated DML), `BindingSource` (position, filter, sort, edit, events), `ErrorProvider`, `DataContext` (paths, event pump, async fill/save), registry metadata; `parse_binding` `Source=`/`Path=` | L | EVT-6, EVT-7b | Unit tests; SQLite in-memory integration tests; PostgreSQL tests gated on `KUBUNO_TEST_PG_URL`; live: scratch desktop app on a SQLite file (grid + detail, add/edit/delete, Save persists, ErrorProvider shows a validation error) |
| **DATA-2** ✅ | Runtime integration: view-owned data components (`runtime.component::<T>()`), `Source=` resolved by the runtime, XML `On*` of data components dispatched by the runtime, typed binding conversions (`F32`, dates, `NullValue`, `FormatString`), `BindingNavigator` control, ErrorProvider adornment (glyph + tooltip), in-place editing in `DataTable` cells (*built afterwards, §15*) | L | DATA-1 | Runtime tests with a fake host (bind, edit a cell, navigate, error glyph hit-test); live: the DATA-1 app without forwarding code |
| **DATA-3** ✅ | Providers and depth: MySQL/MariaDB, SQL Server (`tiberius` + pool), `TransactionScope`-like transactions across adapters, stored procedures/functions, paging (keyset and offset), cancellation tokens and progress, optimistic concurrency (original values, `xmin`, rowversion), custom DML commands, master/detail relations, hierarchical update manager | L | DATA-1 | Integration tests per provider gated by `KUBUNO_TEST_<PROVIDER>_URL`; SQLite always |
| **DATA-4** ✅ | Typed data sources: `.kbdata` format, `data_source!` proc macro (typed rows, `query_as!`), `.sqlx` offline cache flow, typed adapters feeding the same `BindingSource` | M | DATA-1 | `trybuild` pass/fail, offline build with a committed `.sqlx` fixture |
| **DATA-5** ✅ | Data Explorer tool window + `kubuno-data-tool` helper (connections, secrets dialog, schema tree, view data, query window, script generation) | L | DATA-1 | Helper unit tests on SQLite; C# tests of the protocol; live in VS on a SQLite file and, when available, a dev PostgreSQL |
| **DATA-6** ✅ | Data Sources window, "Ajouter une source de données…" wizard, drag and drop onto views (surgical `.kbview` edits), designer property editors and smart tags for data components | L | DATA-2, DATA-4, DATA-5 | LS edit tests (inserted XML round-trips), C# wizard tests; live: drag a table, F5, edit and save |
| **DATA-7** ✅ | Migrations and SQLx tooling: add/run/revert, Solution Explorer status, `cargo sqlx prepare` command, stale-cache warning, Kubuno module templates with their schema | M | DATA-4 | Template tests (`test-templates.ps1`), command tests on a temp SQLite database |
| **DATA-8** ✅ | SQL colouring and IntelliSense in `query!` strings and `CommandText`, schema snapshots, unknown-column warnings | M | DATA-5 | Completion tests on a snapshot fixture; live in VS |

Order: DATA-1 → DATA-2 ∥ DATA-3 ∥ DATA-5 → DATA-4 → DATA-6 → DATA-7 ∥ DATA-8.

## 11. Risks

- **Two async worlds.** sqlx needs Tokio; the UI has the EVT-6 local executor. The bridge is a Tokio `JoinHandle`
  awaited from the UI executor (its waker is executor-agnostic); nothing UI-side ever blocks on the data runtime.
  A handler that forgets `.await` simply detaches the operation; dropping a view cancels pending tasks through their
  `DataTask` handles.
- **Immediate-mode bindings write on every keystroke.** Conversions must never lose what the user typed (the proposed
  text rule), and validation runs at `end_edit`, not per keystroke, apart from conversion errors.
- **`Value` is small** (no integer, no null, no date): exact numbers travel as text until DATA-2's conversions.
- **Static constructors** register the classes only when the rlib's objects are linked: `extern crate kubuno_data as _;`
  (template) — the same rule as EVT-7b project controls.
- **Driver differences** (placeholders, `RETURNING`, type names) are confined to `provider.rs`; SQL Server lacks
  `RETURNING` (`OUTPUT INSERTED.key`), handled in its arm.

## 12. DATA-1 as built (2026-09-30)

In `Z:\src\desktop\windows` (uncommitted there): new workspace member `src/crates/kubuno-data` (layout of §2 plus
`events.rs` for the args and the outbox), and one change in `kubuno-views` (`binding::parse_binding` accepts `Path=` and
`Source=`; `Source=S, Path=P` is the path `S.P`). `kubuno_ui` is unchanged (no dylib rebuild needed).

- **Components** are `#[derive(Component)] #[kubuno(extends = Component)]` classes with `#[property]`/`#[event]` fields,
  so they register themselves (EVT-7b static constructors) as linked, non-visual components (`ClassKind::Component`,
  `is_non_visual`), Toolbox category *Data*, with their default event/property. Deviation: the generated
  `raise_<event>` methods queue for the view runtime's element, which does not own these instances in DATA-1; the
  components raise through their own outbox instead (Rust subscribers at once, `.kbview` handlers through
  `DataContext::take_events` / `pump`). XML handlers of cancelable events (`OnRowValidating`, `OnAddingNew`) are
  therefore delivered after the fact and cannot cancel; use a Rust subscriber (`row_validating().subscribe`) until
  DATA-2 (runtime-owned components) dispatches them synchronously.
- **`DataContext::from_view`** reads the data elements anywhere in the view (unnamed ones get `bindingSource1`-style
  names), applies their attributes through the derive's `kubuno_set_property`, records their `On*` handlers; bindings
  on data-component attributes are ignored with a warning (DATA-2).
- **SQLite specifics**: generated keys by `RETURNING`; the schema comes from `pragma_table_info` (`INTEGER PRIMARY
  KEY` = generated key); an in-memory database keeps a single connection forever. **PostgreSQL**: schema from
  `information_schema` (nullability, `character_maximum_length`, serial/identity, primary key), `CAST($n AS <udt_name>)`
  on every generated parameter, decoding of bool/int2-8/float4-8/bytea/uuid/timestamp(tz)/date/time/json(b)/text;
  NUMERIC and other types without a mapping give an error that says to cast them in the query.
- **Secrets**: `set_user_secrets_id(user_secrets_id!())` at the start of `main` registers the id for the default chain
  (environment → Credential Manager → user secrets). `UserSecrets::set/remove` and `CredentialManager::set/remove`
  exist for the tools (DATA-5).
- Not built (later lots): MySQL/SQL Server, stored procedures, paging, transactions across adapters, master/detail,
  `BindingNavigator`, the error glyph, typed conversions, all Visual Studio tooling.

### Tests

`kubuno-data`: 47 unit tests (errors, values and conversions, runtime and cancellation, secrets chain/placeholders/
manifest id, connection strings incl. redaction, SQL rewriting and identifiers, filter/sort, table states, update
plans incl. PostgreSQL casts and injection text kept as a parameter, binding source navigation/edit/validation/
add/delete/events/outbox, error provider, connection validation/state events/retry, `DataContext::from_view`) + 1
ignored test run by hand (`--ignored`: a real Credential Manager write/read/delete of a `Kubuno:kubuno-data-test-<pid>:…`
credential, removed afterwards — passed); `tests/sqlite.rs` 7 (fill → edit/add/delete through binding paths → save →
database checked, generated key written back; a UNIQUE violation rolls the whole transaction back and keeps the
changes, shown in `errors.Summary`; a row deleted by someone else → `Concurrency`; conversion and `RowValidating` errors
block the save and show in the ErrorProvider; injection text stored as data; a file database persists across
connections, typed columns without rows, XML handlers queued; configuration errors reported without I/O);
`tests/registry.rs` 2 (the five classes are linked non-visual components with their metadata; a view with data
components, `Source=`/`Path=` bindings and `Invalid` bound to the ErrorProvider validates and compiles);
`tests/postgres.rs` 1, **gated on `KUBUNO_TEST_PG_URL`** (a disposable database; the test creates and drops its own
schema) — no PostgreSQL was reachable without looking for credentials, so it ran as a skip. `kubuno-views` 586 + 36
integration/doc tests and `kubuno-views-ls` 143 still pass; `cargo clippy --all-targets -D warnings` clean on
`kubuno-views` and `kubuno-data`; no `unwrap` outside tests.

### Live verification (2026-09-30)

Scratch *Kubuno Desktop Application* `C:\kubuno-build\data1` (template files + `kubuno-data`, `user-secrets-id =
"data1-live-check"`, the connection string `ConnectionStrings:Customers` = a SQLite file in
`%APPDATA%\Kubuno\UserSecrets\data1-live-check\secrets.json`), built with cargo into its own target and run from C:.
The view declares `<DbConnection ConnectionStringName="Customers">`, a `TableAdapter`, a `BindingSource`, an
`ErrorProvider`, a `<DataTable>` bound to `{Binding Source=customers}` with `SelectedIndex` two-way on `Position`, three
`TextField`s bound two-way to the current row with `Invalid` bound to `errors.<field>.HasError` and a red label per field,
navigation/Add/Delete/Cancel/Save buttons (async `save_click`) and the error summary. With `DATA1_SCRIPT=1` a background
thread drove the running window through the UI dispatcher exactly as the controls do (binding writes, handler
dispatch). Log: `StateChange Closed → Open`, `fill Ok(3)`; editing row 2's name then moving committed it
(`HasChanges=true`); Add + three field writes; moving to row 3 and Delete; Save → `3 change(s) saved` and, read through
a separate connection, the file held Ada / Linus Torvalds / Barbara Liskov (the deleted row gone, the new key written
back); `age = "abc"` → the field kept "abc", `errors.age = "Enter a whole number."`, `Invalid = true`, Save refused
with a `DataError`; a bad e-mail → refused by `RowValidating`, message on `errors.email`; fixed → `1 change(s) saved`,
the database updated, `HasErrors = false`. `OnLoad` (async fill), `OnCurrentChanged`, `OnDataError`, `OnStateChange`
handlers of the view ran through `pump`.

## 13. DATA-2 as built (2026-09-30)

In `Z:\src\desktop\windows` (uncommitted there): `kubuno-views` (two new modules, `scope` and `format`, and changes to
`binding`, `compile`, `design`, `runtime`, `component`, `events::executor`/`typed`) and `kubuno-data`. `kubuno_ui` and
`kubuno-controls` are unchanged (no dylib rebuild).

**Components owned by the runtime (`kubuno_views::scope`).**
- `compile` gives every non-visual class of an application or a library (`ClassKind::Component`, linked) a place in
  the view's `ComponentScope` — its `x:Name`, else `<class>N` for the n-th element of its class (`bindingSource2`, the
  names `DataContext` gives) — and every *named* linked control too (a `BindingNavigator`). The instance is sited and
  its literal attributes are applied **when the view is built**, so the view's `OnLoad` finds it configured (bound
  attributes follow at every paint, so `Filter="{Binding …}"` now works on a data component). A hot reload reuses the
  instance of an element with the same name and class (its rows survive); `DesignSlot` disposes an instance only when
  it is its last owner. The scope holds the instances weakly (the slots own them).
- Reaching them: `Runtime::components()` / `Runtime::with_component::<T, _>(name, f)`, `kubuno_views::scope::current()`
  inside a frame (handlers, timer ticks, tasks, posted closures), `UiHandle::components()` in an async handler;
  `ComponentScope::with/with_ref/with_dispatch`. *Deviation:* no `runtime.component::<T>()` returning a guard (the
  scope holds `Weak`s): a closure-based accessor instead. After `with`, the dependent components are synced (a
  detail list follows its master at once).
- **Bindings**: `Component::as_binding_provider()` (default: the base object's) exposes a `BindingProvider`
  (`binding_get(path, want, format)`, `binding_set`, `binding_sync`, `field_error`). The runtime paints with a
  `ScopedViewModel` around the application's view model: a path whose first segment names a provider goes to it,
  every other path to the view model — no forwarding code in the view model any more. After every binding write and
  once per frame (before the paint), `binding_sync` lets providers follow each other (master/detail, bound
  `Filter`/`Sort`, page requests).
- **Events**: a data component raises to its Rust subscribers, then `kubuno_views::scope::raise_now(name, event,
  args)`: while the runtime or a binding is calling the component, a thread-local *sync sink* (scoped like
  `UiHandle::update`'s view-model pointer, `unsafe` confined to `scope.rs` with its SAFETY notes) runs the element's
  `.kbview` handler **synchronously** through the view model's typed handlers — `OnRowValidating` adds errors that
  refuse the row, `OnAddingNew` cancels. Called from code (the view model is borrowed there), the event is queued on
  the component (`ComponentCore::queue_event`) and the runtime delivers it after the frame's jobs/tasks and after the
  paint (a legacy `handlers!` table entry is reached this way too). `DesignSlot` now releases the instance before
  delivering its queued events (a handler may use its component).

**Typed conversions (`kubuno_views::format`, `binding`).** `BindingSpec` gains `format: BindingFormat`
(`FormatString=`/`StringFormat=`, `NullValue=`/`TargetNullValue=`, `Culture=`/`ConverterCulture=`; values may be quoted
`'#,##0.00'`). `FromValue::KIND` says what a property wants; `PropSource::resolve` calls `ViewModel::get_bound(spec,
want)`, and the two-way writes (TextField, NumericField, CheckBox, sliders, lists…) call `set_bound` — defaults convert
generically (`to_target`/`from_target`: a text that parses reaches a numeric property, a number shown in a text
property is formatted instead of falling back to empty; numeric formats `N/F/D/C/P/G` with precision and custom
`0/#/,/.` patterns; date formats `d/D/t/T/g/G/f/F/s` and custom `yyyy MM dd HH mm ss tt` with French/German/English
month names; `NullValue` both ways). Cultures: `fr`, `de`, `es/it/nl/pt`, `en-US`, `en-GB`, `fr-CH`, `fr-CA`, invariant
(ISO dates); default = `set_default_culture`, else the user's locale (`GetUserDefaultLocaleName`, declared by hand —
no new `windows` feature). Without a `FormatString` and a `Culture`, values are shown as held (DATA-1 behaviour). The
`BindingSource` answers typed reads itself (`to_bound`: integers formatted from `i64`, never through `f32`; dates
formatted from their ISO text; `NULL` → `NullValue`, or the numeric property's fallback) and parses typed writes
(`from_bound`: `1 234,50`, `10/12/1815` with `d` in French → `1815-12-10`, a 31st of February refused with "Enter a date
as dd/MM/yyyy." kept as proposed text).

**`<BindingNavigator>`** (`kubuno-data`, a linked `Control`, Toolbox *Data*): first / previous | position box (type a
number, Enter moves, Escape gives up) `/ count` | next / last | add / delete | save, Lucide icons (`ChevronsLeft`,
`ChevronLeft`, `ChevronRight`, `ChevronsRight`, `Plus`, `Trash2`, `Save`), hover/pressed fills from the theme, items
disabled where the action is impossible (and while a fill or save runs). `ShowAddItem`, `ShowDeleteItem`,
`ShowSaveItem`; `AutoSave` (default true: Save saves through the adapter on the view's executor); events `ItemClicked`
(`NavigatorItemClickedEventArgs.item`) and `SaveItemClick` (with `AutoSave="false"`, the view model saves, e.g.
`save_all` of a master and its details). *Deviation from §8:* `SaveItemClick` event + `AutoSave` instead of a
`SaveCommand` handler-name property. It paints unbuffered (it shows another component's state) and reads its binding
source through `scope::current()`; `perform(item)` is what a click runs.

**ErrorProvider adornment.** After the paint, the runtime asks the providers `field_error(path)` for every path each
painted element is bound to (collected once per element from its attributes) and draws, for a field in error, a 16 DIP
danger-coloured round badge with a white exclamation mark at the control's `IconAlignment` (six positions,
`IconPadding`), blinking per `BlinkStyle` (`BlinkIfDifferentError`: three blinks when it appears or its message changes;
`AlwaysBlink`; `NeverBlink`) at `BlinkRate`; hovering the badge shows the message as the tooltip (it wins over the
control's own). `ErrorProvider.field_error` answers for the fields of its `DataSource` (`customers.email`,
`customers.Current.email`), not for the source's own paths (`Position`…).

**Also**: `kubuno_data::fill/save/save_all(&ui, …)` (and `fill_scope`/`save_scope` without a view model) work on the
view's scope (`V: ViewModel + EventSink`, no `HasDataContext`); the operations themselves live in `kubuno_data::ops`
(`begin_*`/`end_*` over a `ComponentScope`). `DataContext` is now a standalone `ComponentScope` it owns (accessors
return `Ref`/`RefMut` guards; `set`/`get` go through the same `ScopedViewModel`; `take_events`/`pump` drain the
components' queues); its async helpers moved to `kubuno_data::context::{fill, save}`.

**Not built**: in-place editing in `DataTable` cells (it needs editing support in `kubuno_ui::tables::DataTable`, i.e.
a `kubuno_ui` change — left for a lot of its own); column `FormatString`s in a `DataTable` (cells show the held text);
the binding options in the language server's completion. (The first two were built afterwards: §15.)

### Tests (DATA-2)

`kubuno-views`: 599 unit tests (the new ones: `format` — numbers, custom patterns, parsing per culture, dates, binding reads and
writes, cultures; `binding` — formatting options, typed reads; `scope` — paths routed to a provider, an XML handler
cancelling synchronously, queued events delivered, a busy component; `runtime` with the fake host — a registered test
provider: scope names, synchronous `Changing` handler cancelling, events from code delivered by the next frame, the
error glyph recorded at `(114,17,130,33)` next to the bound `TextField` on a `RecordingCanvas` and gone when fixed,
the instance kept by a hot reload; glyph placement and blink phases), 24 doc tests, `custom_controls` 4, `roundtrip`
7, `showcase` 1; `kubuno-views-ls` 146. `kubuno-data` (DATA-2 part): `binding_source` typed reads/writes, detail list,
paging requests, queued events of a named component; `error_provider` paths; `navigator` item states, layout and hit
test; `tests/sqlite.rs` — an `OnRowValidating` XML handler (a view model's `dispatch_event`) refuses a move
synchronously through the scope, typed bindings with `N0`/`fr-FR`/`NullValue` save `1234` and NULL; `tests/registry.rs`
— `BindingNavigator` is a linked Data control, the new properties are declared, and a real `Runtime` owns the
components of a view (configured before the first paint, kept by a hot reload, a renamed element is a new instance).

## 14. DATA-3 as built (2026-09-30)

- **Providers.** `mysql` feature: sqlx MySQL/MariaDB (`?` placeholders — a repeated `@name` is bound again, backtick
  quoting, `LAST_INSERT_ID()` for generated keys, a follow-up `SELECT` for what `RETURNING` would give, schema from
  `information_schema.COLUMNS` (`COLUMN_KEY`, `EXTRA`), decoding of BOOLEAN/TINYINT(1), signed and unsigned integers,
  DECIMAL as its exact text, dates, JSON, blobs; `Schema` = `USE` on every pooled connection; TLS `Required` for remote
  servers unless `SslMode` says otherwise). Not in the defaults: sqlx's MySQL driver brings `rsa` (RUSTSEC-2023-0071).
  `mssql` feature: `tiberius` 0.12 (`tds73`, `native-tls`, `winauth`, `chrono`) in `mssql.rs` — its own pool (idle
  clients, a semaphore of `MaxPoolSize`, a client whose operation failed is dropped), ADO.NET connection strings
  (`Config::from_ado_string`, `Integrated Security=true`), encryption `Required` for a remote server when `Encrypt` is
  unset, `@P1` placeholders, `[bracket]` quoting, `OUTPUT INSERTED.[col]` for generated keys and row versions,
  `BEGIN TRANSACTION`/`COMMIT` on one client, schema from `INFORMATION_SCHEMA` + `COLUMNPROPERTY` (identity, computed,
  `rowversion` read-only), decoding by TDS column type (DECIMAL/NUMERIC as text, `datetimeoffset` as RFC 3339).
  `Schema` is ignored with a warning (no search path). Transient codes extended (MySQL 1213/2006/2013, SQL Server
  1205 and Azure throttling).
- **Transactions across adapters.** `DbTransaction::begin(&handle)` → `execute`, `query`, `update(&plan)`, `commit`,
  `rollback` (dropped: rolled back); it holds one pooled connection and a key map, so a master saved then its details in
  the same transaction pass the generated keys on. `Pool::run_in_transaction` and `TxConn` share one statement runner
  per driver.
- **Master/detail and the hierarchical update.** `BindingSource.DataMember = "child = parent"` (qualifiers dropped;
  a single name = the child column, the parent being the master's key) with `DataSource` = the master binding source
  and `TableAdapter` = the detail's adapter. The detail shows nothing until it follows a master row, then only the
  rows whose child column equals the master's current key; `add_new` fills that key; a detail edit that does not
  validate is cancelled when the master moves. A select parameterized by the relation (`WHERE customer_id =
  @customer_id`) makes the detail refill on each master change (the runtime starts the fill on its executor; a
  `DataContext` exposes it as the next `current_request()`). New rows with a generated key get **temporary negative
  keys** (process-wide counter, ADO.NET's seed/step -1); `save_all(&ui, &["customers", "orders"])` (or
  `save_all_blocking`) plans each list (a detail's plan knows its foreign-key column) and runs them in **one
  transaction**: deletes (details first), updates, inserts (masters first); each insert maps its temporary key to the
  generated one and later statements' foreign-key parameters take it; after the commit the tables rewrite their
  foreign keys and the detail's master key. A detail saved alone with a reference to an unsaved master fails as a whole
  (the foreign key refuses the temporary key).
- **Paging.** `TableAdapter.PageSize` (0: all), `PagingMode` `Offset` (`LIMIT/OFFSET`, `OFFSET … FETCH NEXT` on SQL
  Server with `ORDER BY (SELECT NULL)` when there is none) or `Keyset` (`SELECT * FROM (<select without its ORDER
  BY>) AS kubuno_page WHERE key > @kubuno_after ORDER BY key LIMIT n`, `TOP (n)` on SQL Server, `KeysetColumn` default
  the key; pages read in order, the binding source remembers each page's start key), plus `SELECT COUNT(*)` of the
  select for the total. `BindingSource` paths `PageIndex` (two-way: asks for a page — refused while there are unsaved
  changes), `PageCount`, `PageText`, `TotalCount`, `CanPreviousPage`, `CanNextPage`.
- **Cancellation and progress.** `DataTask::canceller()` → `Canceller` (`Send + Sync`, `cancel()` from any thread);
  fills stream their rows (`futures::TryStreamExt`) and count them in a `Progress` (`TableAdapter::fill_page(…,
  Some(progress))`; the binding source's `RowsRead`, `IsBusy`).
- **Optimistic concurrency.** `ConflictOption`: `OverwriteChanges` (default, DATA-1), `CompareAllSearchableValues`
  (the WHERE clause also matches every original value — `IS NULL` for a NULL — except bytes/JSON/unknown types),
  `CompareRowVersion` with `RowVersionColumn` (read-only in DML; PostgreSQL `xmin` read as `xmin::text AS xmin`,
  compared and returned as `"xmin"::text`; SQL Server `rowversion` through `OUTPUT INSERTED`; MySQL through a
  follow-up `SELECT`); the new version is read back in the same statement, so the next edit passes. SQLite's
  `RETURNING` does not see what AFTER triggers change, so a trigger-maintained version is not re-read there.
- **Custom DML.** `InsertCommand`/`UpdateCommand`/`DeleteCommand`: parameterized text; `@column` takes the row's value,
  `@Original_column` its original value; an unknown parameter is a validation error; an insert returning its key
  (`RETURNING`/`OUTPUT`) writes it back.
- **Stored procedures.** `DbCommand.CommandType = StoredProcedure` (`DbCommand::procedure(name)`): parameters in the
  order they were set — PostgreSQL `CALL name($1…)` (`SELECT * FROM name(…)` for `query`/`execute_scalar`), MySQL
  `CALL`, SQL Server `EXEC name @p = @P1…`; refused on SQLite; the name is validated.

### Tests (DATA-3)

`kubuno-data`: 63 unit tests (64 with `--features mysql,mssql`) + 1 ignored (Credential Manager round trip), among them: MySQL/SQL Server placeholders and
quoting, top-level `ORDER BY`, generated DML per provider (backticks + `LAST_INSERT_ID`, brackets + `OUTPUT INSERTED`),
`CompareAllSearchableValues` and `xmin` row versions, custom commands with `@Original_…`, paged/keyset selects,
dependency order of master/detail plans and foreign-key rewriting, stored procedure calls per provider, transient
codes. `tests/sqlite.rs` 16 (the 7 of DATA-1 adapted to the `Ref`/`RefMut` accessors, plus: master/detail follow and
hierarchical `save_all` of a new customer and its order — the generated key reaches the order in the database and the
local rows; a parameterized detail; a `DbTransaction` rolled back then committed across a command and an adapter; paging
by offset and keyset over 25 rows; `CompareAllSearchableValues` catching another writer; custom insert/delete commands
and a stored procedure refused by SQLite; 20 000 rows counted by `Progress` and a 5-million-row recursive query
cancelled from another thread, the connection still usable). `tests/postgres.rs` 2 (gated on `KUBUNO_TEST_PG_URL`: adds
`xmin` row versions, a customer and its order in one `DbTransaction`, paging), `tests/mysql.rs` 1 (feature `mysql`,
gated on `KUBUNO_TEST_MYSQL_URL`), `tests/mssql.rs` 1 (feature `mssql`, gated on `KUBUNO_TEST_MSSQL_URL`: identity,
`rowversion` read back and a stale version refused): **no PostgreSQL, MySQL or SQL Server server was reachable
without looking for credentials, so these ran as skips**; the `mysql`/`mssql` code is compiled, clippy-clean and unit
tested, not exercised against a server. `cargo clippy --all-targets -D warnings` clean on `kubuno-views` and
`kubuno-data`, with and without `--features mysql,mssql`; no `unwrap` outside tests; the `kubuno` facade crate
(`--features data`) still builds.

### Live verification (2026-09-30)

Scratch app `C:\kubuno-build\data2` (`user-secrets-id = "data2-live-check"`, `ConnectionStrings:Shop` = a SQLite file in
the user secrets), built into `E:\cargo-target\data2`, run from `C:\kubuno-build\data2\run` with its `kubuno_ui.dll`
(md5 identical to the build's) and `std-44a584f44bc3dd65.dll` (identical to the toolchain's), the exe importing exactly
those two. The view declares a connection, two adapters, `customers` and `orders` (`DataSource="customers"
DataMember="customer_id = id" TableAdapter="ordersAdapter"`), two ErrorProviders, two `BindingNavigator`s (the orders
one with `AutoSave="false" OnSaveItemClick="save_all_click"`), two DataTables, and typed detail fields — `NumericField
Value` bound to `age`, `TextField` bound to `birth_date` with `FormatString=d, Culture=fr-FR`, a `CheckBox` on `vip`,
the order's `amount` with `FormatString=N2, Culture=fr-FR` and `ordered` with `d` and `NullValue='-'`. The view model
has only a status line and handlers (no forwarding). With `DATA2_SCRIPT=1` a background thread drove the window
through the UI dispatcher — binding writes through `scope::current().view_model(…)` (what the controls write),
navigator items through `BindingNavigator::perform` (what a click runs). Log: `StateChange Closed → Connecting → Open`,
`fill customers=Ok(3) orders=Ok(4)`; age read as `F32(36.0)`, birth date shown `10/12/1815`, amount `1 250,50`;
MoveNext → Linus, the detail followed at once (`Kernel 0.99`); typed writes (29, `28/12/1969`, VIP) committed by
MoveFirst (the XML `OnRowValidating` ran); AddNew → id `-1`, the order's `customer_id` `-1`, amount typed `1 234,50`;
the orders navigator's Save → `save_all: Ok(3)` and, read through a separate connection, customer 4 Grace Hopper
(`1906-12-09`) and order 5 `A-0 compiler | 1234.5 | 1952-05-01` with `customer_id = 4`, Linus `29 | 1969-12-28 | true`;
the local rows then showed id 4 / customer_id 4. An e-mail without `@` → the XML handler refused the move
synchronously (`position 4 / 4`, `errors.email = "Enter a valid e-mail address."`); a birth date `31/02/1906` → kept
as typed, error "Enter a date as dd/MM/yyyy.". The window is left in that state for the visual check of the navigators
and the two error glyphs.

## 15. DataTable: formatted columns and in-place editing (DATA-2 left-overs, as built 2026-09-30)

In `Z:\src\desktop\windows` (uncommitted): `kubuno-ui` (`tables.rs`, new `tables/edit.rs`, `text.rs`), `kubuno-views`
(`registry/families/data.rs` — `DataTableNode` rewritten —, `events/args.rs`, `registry/project.rs`, `registry/docs_fr.rs`,
new `data_edit_tests.rs`), `kubuno-controls` (`host::input::set_frame_events`, doc-hidden, for tests) and `kubuno-data`
(`binding_source.rs`). `kubuno_ui.dll` changed: `tools/build-all.ps1` rebuilt the workspace and restaged the runtime.

- **Formats** (WinForms `DefaultCellStyle.Format`): `<Column Header Binding FormatString Culture NullValue Alignment
  ReadOnly/>` — `FormatString`/`Culture`/`NullValue` may also be written in the column's `{Binding …}` (the attribute
  wins); `<DataTable Culture=…>` is the default culture of its columns. Cells go through `kubuno_views::format` like
  bound fields (`1 250,50`, `01/09/1843`, `-` for NULL); `Alignment` Left (default, as WinForms)/Center/Right. Header
  sorting compares the raw values (numbers numerically, empties first), never the formatted text.
- **In-place editing** (`EditMode = EditOnKeystrokeOrF2`): `DataTable.ReadOnly` (default false, WinForms) and
  `Column.ReadOnly`; a grid needs an `x:Name` to take the keyboard focus, hence to be edited (a grid without one is
  unchanged). F2 (caret at the end), typing a character or double-click (all selected) begins the edit of the current
  cell; ←/→ move the caret while editing, else the current column; ↑/↓/Tab/Shift+Tab/Enter commit then move (Tab
  wraps rows and leaves the grid at its ends; Enter moves down, outside an edit it still raises `OnRowActivated`);
  Escape cancels the cell, a second Escape the row (`CancelEdit` of the binding source); a click elsewhere or losing
  the focus commits. Events (⚡ tab): `OnCellBeginEdit` (cancelable), `OnCellValidating` (cancelable, carries the typed
  text), `OnCellValueChanged`, `OnCellEndEdit` (`CellEventArgs`, `CellCancelEventArgs`, `CellValidatingEventArgs`;
  `row_index` is the row in the bound list whatever the sort).
- **Write-back**: bound to a `BindingSource` `P`, a commit writes `P.Position` = the row then `P.Current.<field>` through
  `set_bound` with the column's format (`2000,75` → 2000.75), so the row edit ends when the current row changes
  (WinForms `CurrencyManager`); a conversion error or a `RowValidating` refusal keeps the grid on the row, the typed
  text stays in the cell with a red error glyph (read from the new path `P.Current.<field>.Error`). Bound to a view
  model list: `P[<row>].<field>` (parsed per the format), for the view model to apply. `BindingSource` also accepts the
  write-only paths `CancelEdit` and `EndEdit`.
- Deviations: the grid's current row follows a user choice and moves `Position`, but a grid without a `SelectedIndex`
  binding does not follow `Position` changed by code (that would have changed existing apps' rendering); no new row
  line (`AllowUserToAddRows`), no Delete key, no tooltip on the cell's error glyph. Fixed on the way: a bound grid
  without `SelectedIndex` lost its selection at the next frame, and header sorting of a bound grid did not hold.
- **Parity**: the `RecordingCanvas` calls of the showcase `DataTable`, a bound grid with `SelectedIndex` plus a static
  grid over four frames (two hovered), and a hand-built `kubuno_ui::DataTable` like the gallery's (at rest and focused)
  were recorded before and after: identical (560 lines).
- **`BindingSource.AutoFill`** (new, for DATA-6's drag and drop): `AutoFill="true"` starts the fill of a list without
  master on the view's first live frame (never in the designer), through its `DataSource` adapter — the `Fill` call
  Windows Forms adds to `Form_Load`, without code.

Tests: `kubuno-ui` 767 (7 new: the edit state machine), `kubuno-views` 609 + 4 ignored (10 new in `data_edit_tests.rs`
on a real `Runtime` and `RecordingCanvas`: column formats, F2/typing/double-click, commit paths, Escape, Tab wrap,
events and cancelation, sorting on raw values), `kubuno-views-ls` 127 + 1 + 18, `kubuno-controls` 401, `kubuno-data`
`tests/sqlite.rs` +2 (a grid bound to a binding source over a SQLite file edited with the keyboard — `N2`/`d` in French,
`abc` refused with the glyph, a second Escape cancels the row, Save → the file holds `2000.75` and `1952-05-01`; and
`AutoFill` filling the grid with no view-model code). Live: scratch app `C:\kubuno-build\data-ui-live` driven by real
`PostMessage` input (mouse, `WM_KEYDOWN`, `WM_CHAR`): `2000,75` + Enter, `24/12/1991`, F2 + typing committed, `abc`
kept the row with "Enter a number.", Save → the database held the values.

## 16. DATA-4 as built (2026-09-30): typed data sources

In `Z:\src\desktop\windows` (uncommitted): two new workspace crates, `kubuno-data-model` and `kubuno-data-macros`, and
`kubuno-data` (`typed.rs`, `lib.rs`, `sql.rs`, `provider.rs`, the committed test cache `kubuno-data/.sqlx/`).

- **`kubuno-data-model`** (no driver, no UI — usable by a proc macro): the `.kbdata` format (`DataSource`,
  `TableSource`, `Column`, `Query`, `Param`, `ObjectKind`; TOML, `deny_unknown_fields`, `version = 1`, a header comment
  saying the file is the developer's and never holds a connection string), `ProviderName`, the native-type → Rust-type
  mapping `rust_type_for` (`RustType { ty, text_cast, sqlx_feature }`: SQLite affinities, PostgreSQL `int4`→`i32`,
  `timestamptz`→`chrono::DateTime<Utc>`, `uuid`, `json(b)`; types without a decoder the application surely has —
  NUMERIC, MONEY, intervals… — travel as text: `CAST(col AS TEXT)` when read, `CAST(CAST($n AS TEXT) AS <native>)` when
  written), `naming` (row struct / field names), `sql` (the `@name` lexer moved here from `kubuno-data`, which delegates to
  it unchanged), `typed::plan(&DataSource) -> TypedPlan` (every generated statement, validated, with errors naming the
  table or query) and `cache` (`check`/`stale_reason` by modification times, `query_file_name(sql)`,
  `missing_queries(plan, dirs)`).
- **`data_source!("shop.kbdata")`** (`kubuno_data::data_source!`, `kubuno::data::data_source!`: a `macro_rules!` that
  passes `$crate` to the proc macro). The path is resolved from the calling file, then `src/`, then the package root, then — for rust-analyzer, whose
  proc-macro server gives no calling file — the one file under `src` whose path ends with it (`#[kubuno::view]` does the same).
  **No direct `sqlx` dependency is needed**: the macro calls `sqlx_macros_core::query::expand_input` (the code of
  `query_as!` itself) and rewrites the `::sqlx::…` paths of the expansion to `$crate::sqlx::…`. Per table or view:
  `#[derive(Debug, Clone, PartialEq, Default)] pub struct Customer { pub id: i64, pub email: Option<String>, … }`
  (`PartialEq`/`Default` only when every field has them), `TABLE`/`COLUMNS`/`KEY`, `fetch_all`, `fetch_by_key`,
  `insert` (`RETURNING` the row; MySQL: `LAST_INSERT_ID`), `update`, `delete`/`delete_by_key` for tables with a key —
  each over any `sqlx::Executor` of the provider (pool, `&mut *tx`) — and the `_task` twins
  (`fetch_all_task(&ConnectionHandle) -> DataTask<Vec<Self>>`) that run on `kubuno-data`'s private runtime, so UI code
  awaits them from the EVT-6 executor. Selected columns carry sqlx's overrides (`"id" AS "id!: i64"`,
  `"email" AS "email?: String"`) so the struct is exactly what the `.kbdata` says; identifiers are quoted per provider
  and schema-qualified (PostgreSQL, MySQL). Named `[[queries]]`: a free function `name(executor, params…)` (+ `_task`)
  returning rows of `row`, of their own `<Name>Row` from `columns`, or the rows affected; their SQL is wrapped
  `SELECT <typed columns> FROM (<sql>) AS "kubuno_q"`, so it must be a `SELECT`/`WITH`. SQL Server (no sqlx driver):
  structs, `TypedRow` and runtime-checked `_task` reads through `DbCommand`, no DML.
- **Typed rows and the `BindingSource`**: `TypedRow` (`table_name`, `columns`, `to_values`, `from_values`; `FieldValue`
  for integers, floats, bool, `String`, `Vec<u8>`, `Option`, chrono types, `Uuid`, JSON), `Table::from_typed`,
  `add_typed`, `to_typed` (columns matched by name, so a table filled by a `TableAdapter` converts too), `typed_row`,
  `typed_changes`; `BindingSource::load_typed`, `to_typed`, `current_typed`, `typed_changes`, `add_typed`. Pools:
  `ConnectionHandle::sqlite_pool()/pg_pool()/mysql_pool()` (`DataTask<Pool>`) and `run_sqlite/run_pg/run_mysql(|pool|
  async move {…})`; the wrong provider is a `DataError::Config`.
- **The offline cache** (verified in sqlx 0.8.6's source): offline (no `DATABASE_URL`, or `SQLX_OFFLINE=true`), sqlx
  reads `query-<sha256 of the exact SQL>.json` from `SQLX_OFFLINE_DIR` *of a `.env` file*, then
  `CARGO_MANIFEST_DIR/.sqlx`, then the workspace root's `.sqlx` — the `SQLX_OFFLINE_DIR` *environment variable* is not
  read then. Online (`DATABASE_URL` set, `SQLX_OFFLINE` not true), each expanded query writes its file into the
  `SQLX_OFFLINE_DIR` environment variable's directory. Regeneration without sqlx-cli:
  `DATABASE_URL=sqlite:<db> SQLX_OFFLINE=false SQLX_OFFLINE_DIR=<crate>\.sqlx cargo check`. The macro reads
  `option_env!("SQLX_OFFLINE")`/`option_env!("SQLX_OFFLINE_DIR")`, which rustc records in the dep-info, so changing them
  re-expands it without `cargo clean` (`DATABASE_URL` deliberately not: dep-info stores values); it also
  `include_bytes!` the `.kbdata` and the cache files it uses.
- **Stale-cache build warning**: a real rustc warning (the `#[deprecated]` item-then-use trick, lint `deprecated`,
  level `warning` in `--message-format=json`, so it reaches the Error List):
  ``use of deprecated unit struct `_::kubuno_data_stale_sqlx_cache`: kubuno-data: `src/data/shop.kbdata`: the offline
  query cache (.sqlx) is older than the migration `20260930_add_vip.up.sql`: regenerate it: …``. It fires when a
  statement is missing from the cache (online builds; offline, sqlx already refuses) or a migration is newer than the
  source's cache files; never during a regeneration (`SQLX_OFFLINE_DIR` set) nor in an up-to-date build. It is checked
  by hash rather than by the `.kbdata`'s date (editing a comment does not make the cache stale). Adding a migration
  file does not by itself trigger a recompilation (untracked file) — Visual Studio's *Migrations* node shows it (§19).
- Errors are compile errors prefixed with the `.kbdata` path: a statement missing from the cache (one error listing
  them, with the regeneration command), a column the database does not know (`no such column: nme — the .kbdata does
  not match the database schema`), a malformed `.kbdata` (TOML error with its line), an unknown provider, an undeclared
  parameter, a provider feature not enabled (`mysql`).

Tests: `kubuno-data-model` 19, `kubuno-data-macros` 10 (tokens of a table, a view, a query, SQL Server, PostgreSQL
casts; path rewriting; stale reasons), `kubuno-data` 67 unit + `tests/typed.rs` 5 (a temp SQLite file: insert, fetch,
update, delete, transaction, view, three named queries, tasks awaited from a non-Tokio thread, a `BindingSource` round
trip saved typed then through a `TableAdapter`) + `tests/typed_ui.rs` (trybuild: the pass case compiles offline
against the committed cache; five offline failures and one online failure against a temp SQLite database, with their
`.stderr`). The fixture cache is generated by `cargo test -p kubuno-data --test typed_fixture -- --ignored` (creates
`shop.db` from `tests/typed/shop.sql` and prints the command) then the `cargo test --no-run` above with the three
variables. PostgreSQL/MySQL typed sources: token-level tests only (no server); `kubuno::data::data_source!` through
the facade not compiled (the same re-export mechanism is proven through `kubuno_data::`).

## 17. DATA-5 as built (2026-09-30): `kubuno-data-tool` and the Data Explorer

**`kubuno-data-tool`** (new workspace crate `src/crates/kubuno-data-tool`, lib + bin; features forward kubuno-data's,
the Visual Studio build uses `--features mysql,mssql`; built with `kubuno-views-ls` into the same release folder, whose
`kubuno_ui.dll`/`std-*.dll` it shares in the VSIX `tools\`). JSON lines over stdio (`--stdio`; `--version`), requests
run concurrently, one output writer, `cancel {id}`; errors `{kind, message}` with `kind` = the `DataError` variant,
`Protocol` or `Io`; every message goes through a redactor (the connection string and its password never leave the
process). Modules: `server` (dispatch), `targets` (the three target forms: `explorer`, `project` — the crate's
`user-secrets-id` and the secrets chain —, inline), `explorer` (the list in `%APPDATA%\Kubuno\DataExplorer\
connections.json` — name, provider, store, a redacted display — and the connection string ONLY in the Credential
Manager (`Kubuno:DataExplorer:ConnectionStrings:<name>`) or the user secrets of id `DataExplorer`; root overridable by
`KUBUNO_DATA_TOOL_HOME` for tests), `schema` (per provider: SQLite `sqlite_master` + pragmas, PostgreSQL
`information_schema` + `pg_catalog` (functions/procedures through `pg_proc`), MySQL `information_schema`, SQL Server
`INFORMATION_SCHEMA` + `sys.*`; catalog queries parameterized, interpolated identifiers validated then quoted),
`query` (`connection.test`, `data.top`, `query.execute` split on top-level `;` by `split` — strings, quoted
identifiers, comments, dollar quotes, MySQL escapes, SQL Server brackets and `GO`, trigger bodies —), `rows` (display
text of values), `scripts` (SELECT/INSERT/UPDATE/DELETE with `@column` parameters, CREATE), `kbdata` (`kbdata.build`
from the live schema with `rust_type_for`, `kbdata.read` + `rowNames`), `migrate` (sqlx `Migrator`: add with the
template and, for a Kubuno module, `CREATE SCHEMA IF NOT EXISTS <schema>` in the first migration; status from
`_sqlx_migrations`; run; revert; SQL Server refused), `sqlxcmd` (`sqlx.status`; `sqlx.prepare`: `cargo sqlx prepare`
when `cargo-sqlx` is installed, else `cargo clean -p <package>` + `cargo check --all-targets` with `DATABASE_URL`,
`SQLX_OFFLINE=false`, `SQLX_OFFLINE_DIR=<temp>` in the child's environment only; the fresh files replace `.sqlx` only
when the build succeeded). Deviations from the planned protocol: a missing SQLite file is never created by the tool
(the Add Connection dialog creates the empty file itself); `cancel` answers `{cancelled}`.
Tests: 48 unit + 1 ignored (a real Credential Manager round trip) and 7 end-to-end tests spawning the exe (a whole
SQLite session — explorer add/list with no secret in the list file, schema with a view/FK/index, data.top,
two-statement execute with a `;` inside a literal, every script kind, kbdata.build parsed back, migrate
add/status/run/revert, sqlx.status —; a slow recursive query neither blocking `ping` nor surviving `cancel`; a
`Password=S3cr3t!` never appearing in an error; `--version` and EOF), 3 of them gated on `KUBUNO_TEST_*_URL`
(skipped: no server). clippy `-D warnings` clean with and without `--features mysql,mssql`.

**Visual Studio** (`src/Rust/Kubuno.Rust.Logic/Data/*` — the client, models, connection-string builders, tree and
texts, testable without VS — and `src/Desktop/Kubuno.Desktop/DataExplorer/*`):
- `DataToolHost.Service` (`DataToolService`): starts the helper on first use (`tools\kubuno-data-tool.exe`, dev
  fallback `C:\kubuno-build\desktop-target\release\`), restarts it after a crash, stops it with the package; typed calls
  (`ListConnectionsAsync`, `AddConnectionAsync`, `TestConnectionAsync`, `LoadSchemaAsync`, `DataTopAsync`,
  `ExecuteQueryAsync`, `GenerateScriptAsync`, `CopySecretToProjectAsync`…) plus `SendAsync(method, params)`; a
  `CancellationToken` sends `cancel`; targets `DataConnectionTarget.Explorer/Project/Inline`; the requests logged to the
  Kubuno pane (option, off by default) have connection strings and passwords masked and SQL reduced to its length.
- **Data Explorer** tool window (View, next to Server Explorer, and View > Other Windows; docked with Server Explorer):
  toolbar Add connection / Refresh / New query / Delete; lazy asynchronous tree connection → schemas → Tables / Views /
  Functions → columns (`id (INTEGER, PK, auto-incrément, non NULL)`), keys (PK, FK → table), indexes (unique);
  KnownMonikers icons; context menus (New query, Refresh, Delete connection; on a table: Show data (also double-click),
  New query, Generate script ▸ SELECT/INSERT/UPDATE/DELETE/CREATE, Copy name).
- **Add Connection** (ThemedDialog, in the dialog gallery): name, provider, per-provider fields (SQLite file +
  Browse + create if missing; server, port, database, Windows/SQL authentication, user, password, SSL/encryption),
  *Advanced* with the connection string shown password-masked or typed raw, the store (Credential Manager default /
  user secrets) with the note that nothing is written into the project, *Test connection* (server version or error,
  cancellable). Connection strings are built in the forms `kubuno-data` parses (tested with a password full of special
  characters).
- **Query window** (document well, one per query): monospace editor, Execute (F5, Ctrl+Shift+E; the selection or all),
  Cancel (Alt+Break), results grids per result set (`NULL` greyed italic, column names with dots handled), Messages,
  status (rows, ms, truncation). Show data = `data.top`; Generate script = pre-filled, not executed.
- **Options** (unified settings, Kubuno > Data): rows of Show data (200), max rows per result (1000), query timeout
  (30 s), initial credential store (Credential Manager), log helper requests (off).
- Not built: "Edit connection…" (a stored string cannot be read back — delete and add again); the query editor has no
  SQL colouring (DATA-8 colours Rust strings).

Tests: `Kubuno.Rust.Tests` +34 (client framing/ids/concurrency/cancel/crash-restart/missing exe/log
redaction, connection-string builders per provider, the SQLite file creation, the tree from a `schema.load` fixture
FR/EN, query texts). Live (hive `KubunoData`, `C:\kubuno-build\data-explorer-live`, UIA/DTE): a SQLite connection
added with the Credential Manager store (`cmdkey` shows `Kubuno:DataExplorer:ConnectionStrings:Shop`,
`connections.json` holds no secret), tables/view/index created from a query window, the tree down to columns/keys/
indexes, Show data (3 rows, `NULL`), SELECT/INSERT scripts, F5 and Ctrl+Shift+E, an error in Messages, a user-secrets
connection removed with its secret. PostgreSQL/MySQL/SQL Server not exercised live (no server).

## 18. DATA-8 as built (2026-09-30): SQL in Rust strings

`src/Rust/Kubuno.Rust.Logic/Sql/*` (testable) and `src/Rust/Kubuno.Rust/LanguageService/Sql/*`:
- **Recognised literals**: the SQL argument of `query!`, `query_scalar!`, `query_unchecked!`,
  `query_scalar_unchecked!`, `query_as!`/`query_as_unchecked!` (second argument), with or without `sqlx::`
  (`query_file*` skipped); `sqlx::query(`, `query_as::<…>(`, `query_scalar(`, the `*_with(` forms, `raw_sql(` (without
  the `sqlx::` prefix only when the text starts with a SQL keyword); kubuno-data's `DbCommand::with_text("…")`,
  `TableAdapter::new(conn, "…")` and the fields `command_text`, `select_command`, `insert_command`, `update_command`,
  `delete_command`. `"…"` with escapes, `r"…"`, `r#"…"#`, multi-line; a Rust lexer that knows nested comments, chars,
  lifetimes, `b""`/`c""`, `r#ident`. Typing inside a literal that inserts no `"`, `\` or `#` re-lexes only that literal.
- **Colouring**: an `IClassifier` on the `rust` content type, classifications *Kubuno SQL - Keyword / Name / Function /
  Number / String / Comment / Parameter / Operator* in Fonts and Colors, theme-aware defaults switched with the theme
  unless the user changed them (like the Rust macro colour); their formats are ordered after `Priority.High` and after
  `string - escape character`, so they win over the string colour of TextMate and rust-analyzer.
- **Completion** (`IAsyncCompletionSource`; the Rust completion source stays out of SQL strings, and in a SQL session
  commits only on `(),;` — never on space or `.`): keywords, tables/views after `FROM`/`JOIN`/`INTO`/`UPDATE`, columns of
  the statement's tables (aliases resolved) with their type, `alias.` → that table's columns, `schema.` → its tables,
  functions; KnownMonikers icons; FR/EN descriptions.
- **Warnings**: a warning squiggle on an unknown table after `FROM`/`JOIN`/`UPDATE`/`INTO` and on an unknown
  `alias.column`, with a French/English tooltip naming the connection — only with a snapshot, never for CTEs, subquery
  aliases, table functions, quoted identifiers, unknown schemas, system tables, three-part names, tables created in the
  same string, `EXTRACT(… FROM …)`, `IS DISTINCT FROM`, ambiguous aliases, `excluded.`, `ON DUPLICATE KEY UPDATE`,
  `FOR UPDATE`.
- **Schema snapshots**: `SchemaSnapshotStore` (Core): `<root>\.vs\kubuno\schema\<connection>.json` in the
  `schema.load` format; `Write` (atomic, validates the JSON, raises `Changed`), `TryRead`, `List`, `Delete`, `Watch(root)`
  (shared `FileSystemWatcher`), `ResolveRoot(file, candidates)`. A Rust file's connections are those of its crate's
  `src/**/*.kbdata` (merged when several); without a snapshot only keywords and built-in functions are offered and
  nothing is squiggled. The Data Sources wizard writes the snapshot (DATA-6).
- Not built: an option to turn it off; crates using sqlx without a `.kbdata` get no schema.

Tests: +48 in `Kubuno.Rust.Tests` (literal detection incl. `query!(` in a comment, escapes and their offsets,
incremental edits, a 12 000-line file under 500 ms; the SQL tokenizer; completion contexts and lists from SQLite and
PostgreSQL fixtures; ~20 no-squiggle cases; the snapshot store and `.kbdata` connections).

## 19. DATA-6 as built (2026-09-30): Data Sources, the wizard, drag and drop

`src/Rust/Kubuno.Rust.Logic/DataSources/*` (testable: the `.kbdata` model, names, column kinds, the view reader, the
drop planner, the code writer, the per-project settings, the wizard model, the crate lookup, FR/EN texts),
`src/Desktop/Kubuno.Desktop/DataSources/*` (window, tree, wizard, generator, command) and, in the designer,
`DesignSurface/ExternalDesignerDrop.cs` + `DesignSurfaceEditingCoordinator.ExternalDrop.cs` (and a few lines in
`DesignSurfaceEditingCoordinator.cs` / `.Native.cs`).
- **Data Sources window** (View > Other Windows > Data Sources, Shift+Alt+D, tabbed with Server Explorer): the
  active crate's `src/**/*.kbdata` (`kbdata.read`) → tables/views → columns, with the icon of their drop control.
  Toolbar: Add New Data Source…, Configure…, Refresh, Edit the .kbdata file. Like Windows Forms, a table's ▾ chooses
  **DataTable (grid)** or **Details**, a column's its control (TextField, NumericField, CheckBox, DatePicker, Label,
  [None]); defaults: text fields, check boxes for booleans, labels for generated/read-only keys. The choices are kept
  per project in `.vs\kubuno\datasources.json`, never in the `.kbdata`. "Add to the open view" inserts without a drag.
- **"Add New Data Source…" wizard** (ThemedDialog, four pages, in the dialog gallery): a Data Explorer connection (or
  "New connection…" through DATA-5's dialog) → the connection string's name in the application and its store (user
  secrets by default, Credential Manager), with the note that nothing is written into the project → objects (schemas,
  tables, views with check boxes from `schema.load`; a Kubuno module's schema preselected) → the source's name and a
  summary. Finish writes `src/data/<name>.kbdata` (`kbdata.build`), `src/data/<name>.rs` (the developer's:
  `kubuno::data::data_source!("<name>.kbdata");` and an empty `impl <Row> {}` per table — never rewritten),
  `pub mod <name>;` in `src/data/mod.rs` and `mod data;` in `main.rs` (surgical, idempotent), the `kubuno` dependency's
  `features = ["data"]` and `[package.metadata.kubuno] user-secrets-id` in `Cargo.toml` (the repo's TOML editor,
  comments kept), `kubuno::data::set_user_secrets_id(kubuno::data::user_secrets_id!().as_deref());` at the top of `main`
  (the `kubuno` facade does not register the id yet), copies the connection string into the project's store
  (`secrets.copyToProject`), writes the SQL IntelliSense snapshot (`SchemaSnapshotStore.Write`), then runs
  `sqlx.prepare` in the background (Task Status Center, Kubuno pane, Error List on failure; in the `.rsproj`'s own
  `CargoTargetDir`). "Configure…" rewrites only the `.kbdata` (after confirmation, its header comments kept).
- **Drag and drop onto a view** (Windows Forms behaviour): the design surface only understands Toolbox items, so the
  drag carries a stand-in `Label` item — the surface gives its usual feedback and answers its `insertChild` at the drop
  point — and the extension replaces it with its own `insertFragment` edits, applied as one undo unit (Ctrl+Z removes
  the whole drop). A table in grid mode inserts, if missing, `<DbConnection x:Name="shop_connection" Provider
  ConnectionStringName [Schema]/>`, `<TableAdapter x:Name="customers_table_adapter" Connection SelectCommand="SELECT <columns>
  FROM <table>" UpdateTable PrimaryKey/>` (views: no `UpdateTable`), `<BindingSource x:Name="customers_binding_source"
  DataSource="customers_table_adapter" AutoFill="true"/>`, `<ErrorProvider/>`, then at the drop point a
  `<BindingNavigator>` and a named `<DataTable>` (so it is editable) with a `<Column>` per column: humanized header,
  `FormatString` by type (`d` dates, `g` date-times, `N2` NUMERIC/decimal/floats), `Alignment="Right"` for numbers,
  `ReadOnly="true"` for generated/read-only columns; width limited to the container. Details mode: the navigator and a
  label + bound control per column (`Text="{Binding Source=…, Path=…, FormatString=…, Mode=TwoWay}"`, `NumericField
  Value`, `CheckBox Checked`). A single column inserts its label + control. Components are shared when a table is
  dropped twice; names are unique (suffix 1, 2…). Deviation: `x:Name`s are snake_case (`customers_binding_source`) —
  `#[kubuno::view]` makes each a Rust field, and camelCase gave `non_snake_case` warnings.
- **`#[kubuno::view]` and data components**: the view macro knew only the controls of `kubuno_views` and the crate's own
  `#[derive(Component)]` types, so a view holding named data components did not compile. `kubuno-views-meta` now has
  `LIBRARY_ELEMENTS` (`BindingNavigator`, `BindingSource`, `DbCommand`, `DbConnection`, `ErrorProvider`,
  `TableAdapter`) that the macro accepts (their fields are `kubuno::Control`), kept equal to what `kubuno-data`
  registers by a test of `kubuno-data`. (The facade's own `tables` test compares `BUILTIN_ELEMENTS` with the registry and
  fails when run with `--features data`, the registry then holding the data classes — to be adapted by the owner of
  the facade.)

Tests: +38 in `Kubuno.Rust.Tests` (wizard steps and names, the generated files — idempotent surgical edits that
keep comments —, the drop fragments for a grid, details and a column, unique names, formats by type, Kubuno schema,
and a real round trip of the inserted XML through `kubuno-views-ls`: well-formed, no new diagnostic). Live (hive
`KubunoDataDS`, app `C:\kubuno-build\ds-live` from the current template, SQLite customers/orders/v_orders): the wizard
wrote the files (no secret in the project; the connection string in its user secrets), `sqlx.prepare` produced the
`.sqlx` files, `customers` dropped as a grid and `orders` in details with the mouse, Ctrl+Z / Ctrl+Y, "Add to the open
view" for a column, Configure… adding `v_orders`; the application built and ran (connection through the user secrets,
AutoFill), a cell edited in the grid and a details field saved through the navigators reached the database — first
with a patched copy of the view macro, then, after the `LIBRARY_ELEMENTS` fix, the same app builds with the real
crates. Not verified: Open Folder crates, a real Kubuno module (the schema rule is unit-tested only). Not built: the
property editors and smart tags of §9 (component-reference and `ConnectionStringName` drop-downs, SQL editor with
"Tester la requête", "Aperçu des données…").

**After the first visual check (2026-09-30).**
- A drop never lands on existing controls: the block it adds (navigator, grid or detail rows) moves down, below every
  positioned child of the drop container it would cover (`DataSourceDropPlanner.FreeTop`); a column dropped where its
  list already has detail rows joins them (same label/field columns, next row). A generated or read-only column is a
  read-only `TextField` (`Enabled="false"`, one-way binding) like Windows Forms' ReadOnly TextBox for the key, no longer a
  bare label.
- In the designer, a bound `DataTable` shows its column headers over three blank rows (the Windows Forms designer's
  DataGridView) instead of the empty-state illustration (`kubuno-views`, design frames only).
- The designer surface the VSIX bundles (`tools\surface\view_embed.exe`) is now built from `kubuno-data`'s
  `examples/view_embed.rs` — kubuno-views' surface unchanged, with the data components linked — so a view holding data
  components renders before the project's own design build (it showed "Waiting for a view that compiles…").
- The Data Explorer docks on the left, tabbed with the Toolbox (where Server Explorer lives), and Data Sources is
  tabbed with Solution Explorer, as in Windows Forms (a hive that already stored a floating position keeps it until
  Window > Reset Window Layout).

## 20. DATA-7 as built (2026-09-30): migrations and the SQLx cache

`src/Rust/Kubuno.Rust.Logic/Migrations/*` (texts, the `migrate.*`/`sqlx.*` calls, description rules, the crate's
data info: connections/provider/schema of its `.kbdata` files, `user-secrets-id`, secret names without their values,
the node model) and `src/Desktop/Kubuno.Desktop/Migrations/*` (commands, dialogs, the Solution Explorer node, the info
bar, `RsprojTargetDirectory`).
- **Commands**: a *Database* submenu on a `.rsproj` project node (`VisibilityItem` on the Rust project context):
  **Add Migration…** (ThemedDialog: description, reversible by default, preview of the files; for a Kubuno module's first
  migration a note that it starts with `CREATE SCHEMA IF NOT EXISTS <schema>` — never for SQLite/SQL Server; opens the up
  file; touches the crate's `.kbdata` files so the next build re-expands `data_source!`, rustc not tracking new files),
  **Apply Migrations**, **Revert Last Migration** (VS confirmation, "No" by default), **Update SQLx Cache**
  (cancellable wait dialog, cargo's tail in the Kubuno pane, the project's `CargoTargetDir` passed as `targetDir`),
  **Refresh Status**; DTE names `Data.AddMigration`, `Data.ApplyMigrations`, `Data.RevertMigration`,
  `Data.UpdateSqlxCache`, `Data.RefreshMigrationStatus`. The connection is the crate's `.kbdata` connection (several or
  none → a picker with the names found, including those of the user secrets, and "Set the connection string…" copying
  a Data Explorer connection with `secrets.copyToProject`). No secret in any message.
- **Solution Explorer**: a *Migrations* node under a project that has `migrations/` or `.kbdata` files (presence computed
  off the UI thread, the database queried only when expanded): "Migrations (N pending)", one child per migration
  (`20260930044746 create customers — Applied on 30/09/2026 06:48` ✓ / Pending (clock) / Checksum changed ⚠ / failed or
  missing file), "SQLx cache: up to date (5 queries)" or "stale (…reason)", "Status unavailable: <message>" on a
  connection error; double-click/Open opens the up file; context menus on the root, the migrations and the cache node;
  refreshed after each command and when `migrations/`, `.sqlx/` or a `.kbdata` changes (700 ms debounce).
- **Stale cache**: the rustc warning of `data_source!` (§16) reaches the Error List (verified after a DTE build); now
  it points at the application's `data_source!` call (the literal's span) instead of kubuno-data's wrapper, and the
  macro also tracks the existing migration files; after a build an info bar offers "The SQLx cache of <crate> is stale —
  Update". The SDK (`sdk/Kubuno.Rust.Sdk/Sdk/Sdk.targets`) adds `src\**\*.kbdata`, `.sqlx\*.json` and `migrations\*.sql`
  to the inputs that decide whether MSBuild runs cargo at all (the nupkg was repacked).
- Helper additions: `sqlx.prepare` takes an optional `targetDir` (`CARGO_TARGET_DIR` of the child cargo);
  `kubuno_data_model::cache` no longer names a migration as "the file needing a cache" when there is none yet.
- Not built: the menu on folders in Open Folder mode; Kubuno module templates with `migrations/` (the templates belong
  to the programming-model work; the helper's first migration of a module already creates its schema).

Tests: +26 in `Kubuno.Rust.Tests` (connection and schema discovery, secret names, the connection choice,
description rules, the node model from a real `migrate.status` answer — order, states, FR/EN texts, checksum change,
failed/missing migrations, connection error —, the request shapes incl. `targetDir`). Live (hive `KubunoDataMig`, crate
`C:\kubuno-build\mig-live`): two migrations added → "2 pending", applied (dates), an applied file edited → ⚠, the last
one reverted → "1 pending", Update SQLx Cache with `data_source!` → 5 files and "up to date", a new migration → "stale",
an unknown connection → "Status unavailable…", the Data Explorer copy through the picker, the build warning in the
Error List and the info bar.

## 21. Data bindings in the designer and the Data Sources window (as built 2026-10-01)

The binding UI of the designer (`docs/DESIGNER.md` §18) knows the data components of a view: the binding picker, the
« Liaison de données » dialog and the XML completion offer each named `BindingSource` with its columns (read from its
`TableAdapter`'s `SelectCommand`) and its navigation members (`Position` — two-way —, `Count`, `PositionText`,
`HasChanges`, `IsEditing`, `CanMovePrevious`, `CanMoveNext`), an `ErrorProvider`'s `HasErrors` / `Summary` and a
`DbConnection`'s `State`, written `{Binding Source=customers, Path=Name}`. An unknown component or column is a warning
(a source whose `SelectCommand` is `*` is not checked), a two-way binding of a read-only member too. A `DataTable`'s or a
`Repeater`'s template bound to a binding source (`ItemsSource="{Binding Source=customers}"`) completes and checks the
columns as the row's fields.

**Data Sources window.**
- A « Modèle de vue — `Type` » node, above the project's sources, lists the members of the active view's data context
  (the view-model members of `docs/DESIGNER.md` §18: `#[bind]` fields, a user control's properties, the paths of an
  `impl ViewModel`), with an icon of their shape and their Rust type in the tooltip; it follows the active designer.
- Like Windows Forms, a member **dropped onto a control binds it**: the control's default binding property —
  `Checked` of a check box, `On` of a switch, `Value` of a numeric field / slider / progress bar, `Date` of a date picker,
  `SelectedValue` of a combo box / drop-down / radio button, `SelectedIndex` of a list box, `Image`, `Color`, else `Text`
  (`ItemsSource` for a list member) —, two-way when the member is writable and the property writes back. Dropped onto empty
  space it adds a label and a bound control of its shape (`TextField` for writable text, `Label` for read-only text,
  `CheckBox`, `NumericField`, `ListBox` for a list), below what they would cover, with a unique snake_case `x:Name`.
- A **column dropped onto a control** of a view that already has the table's `BindingSource` binds that control to
  `{Binding Source=<binding source>, Path=<column>, Mode=TwoWay}` instead of adding a detail row. (Without the binding
  source yet, the drop still adds the components and the detail row: the first drop of a table creates them.)
- Every drop is one request, one undo unit (`ExternalDropResult.AttributeEdits` are applied with the insertions).

The hit test of a drop reads the positioned children of the drop container (`X`, `Y`, `Width`, `Height`); a docked or
flowed control is not a drop target.
