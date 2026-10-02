# Storage components — where a Kubuno app keeps its data, on every platform (design, and lot 1 as built)

> Product-owner requirement (2026-10-02): « n'oublie pas de créer des composants (par média ou OS) pour la gestion du
> stockage ou du partage des données ou variables, par exemple : la base des registres pour Windows, les cookies pour
> le web, etc. »
>
> Status: **design** for every lot; **lot ST-1 built** (desktop: `Settings`, `SecretStore`, `RegistryKey`, the
> `.kbsettings` file, its typed class and its Visual Studio editor). Code: `Z:\src\desktop\common\kubuno-app-storage`
> (portable engine), `Z:\src\desktop\windows\src\crates\kubuno-app-storage-components` (view components), small
> additive changes in `kubuno`, `kubuno-views-meta`, `kubuno-views-macros`, `kubuno-views-ls`, `kubuno-resources-model`,
> `kubuno-resources-macros`, `kubuno-print` (its bundled design surface), `kubuno-account` (its `paths` became a
> re-export); in this repository the Toolbox tab, the icons, the settings editor, the item template, the sample
> `samples/storage-desktop` and this note. Part 2 (§11) is the as-built record.

Related notes: `VIEWS-SPEC.md` (elements, `{Binding}`, the registry), `DATA.md` (databases, `BindingSource`),
`DESKTOP-OFFLINE-SYNC.md` (accounts, token broker, SQLCipher, the sandboxed profile), `RESOURCES.md` (`.kbres`, the
model this note copies for `.kbsettings`), `SHARED-CORES.md`, `WEB-VIEWS.md`, `MULTI-OS-AUDIT.md` (`kubuno-paths`).

---

## 0. Summary

- **One canonical API per need, several media.** A view (or plain code) uses a *portable* component — `Settings`,
  `SecretStore`, `KeyValueStore`, `FileStore`, `SharedChannel`… — whose back-end is chosen per platform (files on the
  desktop, the Keychain on macOS, `localStorage`/IndexedDB on the web, DataStore on Android…) and can be overridden
  (`Backend="Registry"`). The same `.kbview` and the same code-behind run everywhere.
- **Platform components when the medium itself is wanted.** `RegistryKey` (Windows), `CookieStore` (web),
  `UserDefaults` (macOS/iOS), `GSettingsKey` (Linux)… exist only where the medium exists; the language server warns
  when a view of a project that also targets another platform uses one, the runtime answers `Unsupported` there.
- **Everything is scoped to the app** (`AppId`, the module isolation rule of `VIEWS-SPEC.md`): a folder, a Registry
  key, a credential scope, a key prefix — never another app's. Secrets never reach a plain file, a cookie, a binding
  or a log. A **sandboxed profile** (`KUBUNO_SANDBOX_DIR`) moves every medium (files, Registry, secret names) away from
  the user's real profile: tests and samples run for real without touching it.
- **Designer parity with Windows Forms**: a « Stockage » / « Storage » Toolbox tab, the component tray, enum
  drop-downs in Properties, a settings grid editor (`Settings.settings`), a typed class (`Properties.Settings`) the
  code reaches with F7, `{Binding Theme, Source=settings, Mode=TwoWay}` in views.

---

## 1. Requirements and principles

| # | Requirement | Consequence |
|---|---|---|
| R1 | Multi-OS: Windows, Linux, macOS, web; later Android, iOS | the engine is pure Rust with `cfg`-gated OS code (`desktop/common`), checked for the three desktop targets; web and mobile get the same API shape, not the same code (§4.5) |
| R2 | One canonical API across platforms | portable components with identical names, properties, events and binding paths everywhere; platform components only add |
| R3 | Module isolation | every location is derived from a validated `AppId`; no component takes a raw path or key outside its app (except `RegistryKey`/`CookieStore`, which name a system location on purpose and are read-only by default) |
| R4 | Never log secrets | errors name keys, files and Registry paths, never values; a `Secret` is zeroed on drop and redacted in `Debug`; bindings never expose a secret's value |
| R5 | Reuse, do not duplicate | `kubuno-paths` rules (override variables, never infer from `is_dir()`), `kubuno-secrets` (OS credential stores), `kubuno-account` (the sandbox, the token broker), `kubuno-sync-engine` (SQLCipher), the `.kbres` tooling model |
| R6 | Testable without touching the user's profile | in-memory back-ends everywhere, the sandboxed profile, throw-away Registry keys deleted after the test; no test writes the real `Run` key or the Credential Manager |

---

## 2. What exists today (inventory, 2026-10-02)

| Where | What | Notes |
|---|---|---|
| `core/crates/kubuno-paths` | server locations (`/etc/kubuno`, `%ProgramData%\Kubuno`…), `write_private` with a Windows DACL | server side only; tag `paths-v0.1.0` |
| `desktop/common/kubuno-account::paths` (now `kubuno-app-storage::paths`, re-exported) | per-user directories (`%APPDATA%\Kubuno`, `%LOCALAPPDATA%\Kubuno`, XDG, `~/Library`), `KUBUNO_SANDBOX_DIR`, `system_integration_allowed()` | same rules as `kubuno-paths`; moved in ST-1 so an app can keep settings without linking the network stack |
| `desktop/common/kubuno-secrets` | `SecretStore` trait; Windows Credential Manager (`CRED_PERSIST_LOCAL_MACHINE`, never roams), macOS Keychain, Linux Secret Service (zbus), file fallback (opt-in, `0600`), `PrefixedSecretStore` for sandboxes | target `Kubuno/<scope>/<item>` |
| `desktop/common/kubuno-account` | accounts, token owner, **token broker** over a named pipe / Unix socket (squatting checks) | the first inter-app channel of the desktop |
| `desktop/common/kubuno-sync-engine` | SQLCipher database per account and app, outbox | key in the OS store (`dbkey`) |
| `windows/src/crates/kubuno-data` | `DbConnection`… and a `SecretResolver` (environment `ConnectionStrings__X`, Credential Manager `Kubuno:<UserSecretsId>:…`, `%APPDATA%\Kubuno\UserSecrets\<id>\secrets.json`) | a **third** secret naming scheme (open question Q4) |
| `windows/src/shell/services/settings.rs` | the shell's preferences in `shell.json` (serde struct, defaults, no versioning, write errors ignored) and the `Run` key through `winreg` (skipped in a sandbox) | to migrate onto `Settings` (lot ST-6) |
| `windows/src/drive/…/services/settings` | drive's settings, one JSON store at `%LOCALAPPDATA%\KubunoDrive\settings.json` (port of Files' `UserSettingsService`) | idem |
| `core/frontend` (web) | `localStorage` in ~29 files (theme, favourites, panel layouts, admin pins, recents, i18n `instance-lang`), `sessionStorage` (setup draft id), cookies: `access_token` (**JS-readable**, `SameSite=Strict`, 15 min, `Secure` on https — for downloads, see `kubuno-core/src/auth/middleware.rs`), language cookie (`SameSite=Lax`, 1 year), OAuth state cookie (path `/auth/oauth/callback`); the `refresh_token` cookie is **HttpOnly**, set by the server and never touched by JS; `BroadcastChannel` `kubuno-auth` and `kubuno-session-activity` | no common wrapper: each feature names its keys; modules share the host origin, so web isolation is a naming convention (§6.1) |
| Android (`mobile`) | multi-account `AccountManager`, Room/SQLite, WorkManager | out of this repository; mapping in §3 |

Findings: three secret naming schemes (`Kubuno/<account>/<item>`, `Kubuno:<id>:<key>`, now `Kubuno/app.<app>/<name>`);
settings code repeated per app with no versioning; the web stores everything ad hoc; nothing let a `.kbview` reach any
of it.

---

## 3. Inventory and mapping per platform

Legend: **bold** = the back-end `Backend="Auto"` picks; ✓ = available as an explicit back-end; — = none.

### 3.1 App settings (typed, defaults, scopes, notifications, upgrade)

| Need | Windows | macOS | Linux | Web | Android | iOS |
|---|---|---|---|---|---|---|
| User, roaming | **`%APPDATA%\Kubuno\<app>\<set>.settings.json`**; ✓ `HKCU\Software\Kubuno\Apps\<app>\<set>` | **`~/Library/Application Support/Kubuno/<app>/<set>.settings.json`**; ✓ `NSUserDefaults` suite `com.kubuno.<app>` (ST-5) | **`$XDG_CONFIG_HOME/kubuno/<app>/<set>.settings.json`** | **server-side user preferences** (roam across devices; ST-3), cached in `localStorage` `kubuno:<module>:settings:<set>` | DataStore (Preferences) `kubuno_<app>_<set>` (Auto Backup = roaming) | `NSUbiquitousKeyValueStore` (iCloud KVS) or server |
| User, this machine only | **`%LOCALAPPDATA%\Kubuno\<app>\<set>.local.settings.json`**; ✓ `HKCU\Software\Classes\Local Settings\Software\Kubuno\Apps\…` (never roams) | same directory, `.local.settings.json` | **`$XDG_DATA_HOME/kubuno/<app>/…`** | **`localStorage`** (per browser) | DataStore excluded from backup | `UserDefaults` |
| Application (machine-wide, admin-deployed, read-only) | **`%ProgramData%\Kubuno\Desktop\<app>\<set>.settings.json`**; ✓ `HKLM\Software\Kubuno\Apps\…` (64-bit view) | **`/Library/Application Support/Kubuno/Desktop/<app>/…`**; managed preferences (MDM) later | **`/etc/xdg/kubuno/<app>/…`** (`$XDG_CONFIG_DIRS`) | instance settings of the core (admin) | managed configurations (`RestrictionsManager`) | managed app config (MDM) |
| Policies (locked values) | `HKLM\Software\Policies\Kubuno\<app>` (ST-6) | configuration profiles | `/etc/xdg` read-only | core admin | `RestrictionsManager` | MDM |
| Change notification | in-process subscribers; other instances: file stamp / `RegQueryInfoKey` last write time polled every 2 s (ST-1); `ReadDirectoryChangesW` / `RegNotifyChangeKeyValue` (ST-4) | FSEvents / `NSUserDefaultsDidChangeNotification` | inotify | `storage` event + `BroadcastChannel` | DataStore `Flow` | KVO / `didChangeExternallyNotification` |
| Upgrade between versions | stored `$version`; `PreviousNames` renames, then the app's `Upgrade` step; never downgraded (all platforms, same engine rules) | | | | | |

GSettings (dconf) is **not** a back-end of `Settings`: its schemas must be compiled and installed system-wide (root),
which a per-app, per-user store cannot require; `GSettingsKey` reads desktop-wide values instead (§4.2).

### 3.2 Secrets

| | Windows | macOS | Linux | Web | Android | iOS |
|---|---|---|---|---|---|---|
| Store | **Credential Manager** (generic, `CRED_PERSIST_LOCAL_MACHINE`, DPAPI per user, ≤ 2 560 bytes) | **Keychain** (device-local) | **Secret Service** (GNOME Keyring, KWallet); absent on headless sessions → session-only or opt-in `0600` file | **none in the browser**: a secret stays in memory for the page's life, or on the server (a vault module) | Android Keystore + encrypted DataStore | **Keychain** (access groups to share between the user's Kubuno apps) |
| Name | `Kubuno/app.<app>/<name>` (sandbox: `Kubuno/sandbox-<tag>.app.<app>/<name>`) | idem (service) | idem (attributes) | — | alias `kubuno.<app>.<name>` | service `com.kubuno.<app>`, account `<name>` |

Never: a secret in `localStorage`, `sessionStorage`, IndexedDB, a non-HttpOnly cookie, a settings file, the Registry,
a URL, a log, a crash report.

### 3.3 Small key/value (unschematized, app-private)

| Windows / macOS / Linux | Web | Android | iOS |
|---|---|---|---|
| `KeyValueStore` (ST-2, as built): a JSON file `<user_data_dir>/<app>/kv/<store>.json` (`<data>/accounts/<key>/<app>/kv/` when account-scoped), written atomically under a lock with read-merge-write, values ≤ 1 MiB, TTL optional; no SQLite, so no C dependency and the cross-compiles stay plain | IndexedDB database `kubuno:<module>`, object store `kv` (`localStorage` only for tiny synchronous UI state) | DataStore / Room | SQLite / `UserDefaults` |

### 3.4 Files and blobs

| Need | Windows | macOS | Linux | Web | Android | iOS |
|---|---|---|---|---|---|---|
| Isolated app files (`IsolatedStorage`) | `%LOCALAPPDATA%\Kubuno\<app>\files\` | `~/Library/Application Support/Kubuno/<app>/files/` | `$XDG_DATA_HOME/kubuno/<app>/files/` | **OPFS** (`navigator.storage.getDirectory()`), folder `<module>/` | `Context.filesDir/<app>` | Application Support |
| Temp (deleted at exit / reboot) | `%TEMP%\Kubuno-<app>-<pid>\` (`GetTempPath2W`) | `NSTemporaryDirectory()` | `$XDG_RUNTIME_DIR` or `/tmp`, `0700` | OPFS `tmp/` or memory | `cacheDir/tmp` | `tmp/` |
| Cache with eviction | `%LOCALAPPDATA%\Kubuno\Cache\<app>\` (size cap, LRU) | `~/Library/Caches/Kubuno/<app>/` | `$XDG_CACHE_HOME/kubuno/<app>/` | **Cache Storage** `kubuno:<module>` (responses) / OPFS; `navigator.storage.persist()` asked for data, never for caches | `cacheDir` (the OS may purge) | `Caches/` (the OS may purge) |
| Quota / estimate | volume free space | idem | idem | `navigator.storage.estimate()` | `StorageManager` | `volumeAvailableCapacityForImportantUsage` |

`FileStore` (ST-2) takes names, never paths: `[A-Za-z0-9._ -]`, no `..`, sanitized with the portable name policy of
`SHARED-CORES.md` (drive rules: reserved Windows names, trailing dots/spaces, case collisions).

### 3.5 Structured local database

The existing data components (`DbConnection Provider="Sqlite"`, `TableAdapter`, `BindingSource`, `DATA.md`) with a
**`LocalDatabase`** component (ST-2, a `DbConnection` subclass): `Data Source=app:<name>` resolves to
`<user_data_dir>/<app>/databases/<name>.db`, `account:<name>` to the signed-in account's folder. Encryption
(`Encrypted="true"`, SQLCipher with the key in the OS store, the `kubuno-sync-engine` scheme) is left for later. Web: IndexedDB (or SQLite WASM on
OPFS, `sqlite-wasm`, when SQL is required). Android: Room. iOS: SQLite / GRDB.

### 3.6 Session vs persistent

| | Desktop | Web | Mobile |
|---|---|---|---|
| Session (until the app/tab closes) | `Backend="Memory"` | `sessionStorage` (per tab), memory | memory / `SavedStateHandle` |
| Persistent | every other back-end | `localStorage`, IndexedDB, OPFS (best effort unless `persist()` granted) | DataStore, files |

A `Persistence="Session|Persistent"` property on `KeyValueStore`/`FileStore` picks between them (ST-2).

### 3.7 Data shared between the user's Kubuno apps (and between instances of one app)

| | Windows | macOS | Linux | Web | Android | iOS |
|---|---|---|---|---|---|---|
| Message channel | **named pipe** `\\.\pipe\kubuno-<sid>-<channel>` (owner = current user, ACL; the broker's squatting checks) | **Unix socket** in `user_runtime_dir()` (`0700`; XPC later for sandboxed apps) | **Unix socket** in `$XDG_RUNTIME_DIR/kubuno` (`SO_PEERCRED`); D-Bus session bus as an alternative for desktop integration | **`BroadcastChannel('kubuno:<channel>')`** (same origin, every tab), `SharedWorker` for a single owner, `postMessage` to iframes/popups, `storage` events | bound `Service` / `ContentProvider` with a signature permission | App Groups + Darwin notifications |
| Shared state (small) | a `Settings` set with `AppId="kubuno"` (shared namespace, opt-in) | idem + `NSUserDefaults` suite `group.com.kubuno` | idem | IndexedDB `kubuno:shared` | `ContentProvider` | App Group `UserDefaults(suiteName:)` |
| Shared memory | file mapping (`CreateFileMapping`, named `Local\Kubuno-…`) — only for large, read-mostly data | `shm_open` | `memfd`/`shm_open` | `SharedArrayBuffer` (needs cross-origin isolation: not on the host) | `SharedMemory` | — |
| Window-to-window in one app | `WM_COPYDATA` (legacy, discouraged: no auth) | — | — | `postMessage` | — | — |

`SharedChannel` (ST-4) is the portable component: `<SharedChannel x:Name="sync" Channel="drive.selection"
OnMessageReceived="…"/>`, `Send(json)`, JSON payloads ≤ 64 KiB, the channel name scoped `kubuno:<app>:<channel>` unless
`Scope="User"` (any Kubuno app of the user — the cross-app case, explicit and reviewed). The token broker stays the only
channel that carries tokens.

### 3.8 Environment variables

Read-only portable component `Environment` (ST-5): `{Binding Variables.HOME, Source=env}`, expanded values, `KUBUNO_*`
overrides listed; never written by a view. Web: build-time `import.meta.env` and the host's config. Mobile: none
(build config). Code: `std::env` stays the API; the component exists for designers and bindings.

### 3.9 Clipboard

A `Clipboard` component (ST-5) is relevant as a data-exchange medium: formats (text, HTML, image, files, a private
`application/x-kubuno-<app>+json`), change notification (`AddClipboardFormatListener` / `NSPasteboard.changeCount` /
`navigator.clipboard` + `clipboardchange` where supported), and the rule that a secret is never copied without
`ExcludeClipboardContentFromMonitorProcessing` (Windows) / `org.nspasteboard.ConcealedType` (macOS). Documents already
has its own clipboard code (`DOCUMENTS-EDITING.md`); the component wraps the same primitives for views.

### 3.10 OS integration stores (as platform components)

| Medium | Platform | Component | Notes |
|---|---|---|---|
| Registry `HKCU`/`HKLM`/`HKCR`/`HKU`/`HKCC`, 32/64-bit views (`KEY_WOW64_32KEY`/`64KEY`), typed values (`REG_SZ`, `EXPAND_SZ`, `MULTI_SZ`, `DWORD`, `QWORD`, `BINARY`) | Windows | **`RegistryKey`** (ST-1) | read-only unless `Writable`; `HKLM` writes need elevation; in a sandbox every hive maps below `HKCU\Software\Kubuno\Sandbox\<tag>` |
| defaults / plist (`CFPreferences`, `NSUserDefaults` suites) | macOS, iOS | `UserDefaults` (ST-5) | through `CFPreferences*`, never by writing the plist behind `cfprefsd` (it caches and overwrites) |
| XDG config files, GSettings/dconf | Linux | `GSettingsKey` (ST-5, read of desktop-wide keys such as `org.gnome.desktop.interface color-scheme`) | per-app data stays in `Settings` (files) |
| Cookies | web | **`CookieStore`** (ST-3) | non-HttpOnly cookies of the module's path only; `SameSite=Lax` default, `Strict` allowed, `None` only with `Secure`; `Secure` on https; `Partitioned` (CHIPS) when embedded cross-site; `__Host-` prefix when path `/` and no domain; ≤ 4 KiB; never a secret; the `refresh_token` cookie is HttpOnly and **invisible** to it by design |
| `localStorage` / `sessionStorage` | web | `WebStorage` (`Kind="Local|Session"`, ST-3) | key prefix `kubuno:<module>:`; ≤ ~5 MiB per origin shared by every module → per-module budget (§6.3) |
| IndexedDB, Cache Storage, OPFS | web | back-ends of `KeyValueStore`/`FileStore`; `IndexedDbStore` only if a module needs raw object stores | |
| SharedPreferences / DataStore | Android | back-end of `Settings` / `KeyValueStore` | `SharedPreferences` only for migration |
| UserDefaults / Keychain | iOS | back-ends of `Settings` / `SecretStore`; `KeychainItem` for access groups | |

---

## 4. Component model

### 4.1 Portable components

| Element | What | Binding paths | Desktop back-ends | Web (ST-3) | Mobile (ST-7) | Lot |
|---|---|---|---|---|---|---|
| **`Settings`** | typed settings of a set (`Schema="settings"` = the project's `settings.kbsettings`) | `<Name>` (two-way for user settings) | `Auto`=File, `File`, `Registry` (Windows), `Memory` | `Auto` = server preferences + `localStorage` cache, `Local` (`localStorage`), `Memory` | DataStore / UserDefaults | **ST-1** (desktop) |
| **`SecretStore`** | the app's secrets | `Available`, `<Name>.Exists` (read-only); values only from code | `Os`, `Memory` | `Memory` only (or a server vault) | Keystore / Keychain | **ST-1** (desktop) |
| `KeyValueStore` | small untyped values, optional TTL, `Persistence` | `<key>` | JSON file (`kv/<store>.json`), `Memory` | IndexedDB, `localStorage`, `sessionStorage` | DataStore | ST-2 |
| `FileStore` | isolated files, temp, cache with eviction (`Kind="Data|Cache|Temp"`, `MaxSize`) | `Count`, `Size`, `Files` (list) | the directories of §3.4 | OPFS, Cache Storage | files dirs | ST-2 |
| `LocalDatabase` | a `DbConnection` source `app:<name>` (SQLCipher optional) | (data components) | SQLite/SQLCipher | IndexedDB / SQLite-WASM | Room | ST-2 |
| `SharedChannel` | messages between the app's instances / the user's Kubuno apps | `LastMessage`, `Connected` | pipe / Unix socket | `BroadcastChannel` | bound service / App Groups | ST-4 |
| `Environment` | environment variables (read-only) | `Variables.<NAME>` | `std::env` | build-time | — | ST-5 |
| `Clipboard` | data exchange | `HasText`, `Text` (one-way) | Win32 / NSPasteboard / X11-Wayland | async Clipboard API | ClipboardManager | ST-5 |

Common to every portable component: `AppId` (empty = the app's own), `Backend` (`Auto` + the explicit choices of its
row), `design_mode` / design surface → memory, never the developer's profile; errors through `last_error()` and an
`Error` event (ST-2), never a panic.

### 4.2 Platform components

| Element | Platforms | Toolbox | Lot |
|---|---|---|---|
| **`RegistryKey`** (`Hive`, `Path`, `View`, `Writable`; paths = value names; `ValueChanged`) | windows | Storage tab, with a Windows badge (ST-2 icon variant) | **ST-1** |
| `CookieStore` (`Name`, `Path` (module-scoped), `SameSite`, `Secure`, `Partitioned`, `MaxAge`) | web | Storage tab of web projects | ST-3 |
| `WebStorage` (`Kind="Local|Session"`) | web | idem | ST-3 |
| `UserDefaults` (`Suite`) | macos, ios | Storage tab of macOS/iOS projects | ST-5 |
| `GSettingsKey` (`Schema`, `Key`, read-only) | linux | | ST-5 |
| `KeychainItem` (`AccessGroup`) | ios, macos | | ST-7 |

Availability is declared once, in `kubuno_views_meta::kbview::PLATFORM_ELEMENTS` (`("RegistryKey", &["windows"])`):

- the **language server** reads the project's targets — `[package.metadata.kubuno] targets = ["windows", "linux",
  "macos"]` in `Cargo.toml` (desktop default: `windows`; web projects: `web`) — and warns on an element whose platforms
  do not cover them (code `platform-only`); the web profile (WEB-VIEWS §5) will make it an error;
- the **runtime** registers the class everywhere (a view still loads on another OS) but its calls return
  `StorageError::Unsupported` and its bindings read nothing; code that needs it uses `#[cfg(windows)]` on its own calls
  (the engine's `registry` module only exists under `cfg(windows)`);
- the **Toolbox** shows the platform components of the targets the project builds for (ST-2: today the desktop Toolbox
  shows `RegistryKey`, desktop projects being Windows-only).

### 4.3 Back-end choice

`Backend="Auto"` is the platform default of §3 (files on the three desktops: inspectable, sandbox-friendly, roaming
through `%APPDATA%`, identical code paths — the Registry is an explicit choice for apps that integrate with Windows
tooling, Group Policy preferences or `reg.exe` scripts). An explicit back-end that does not exist on the platform is an
error at open (`Unsupported`), reported by `last_error()` and a log line, and the component falls back to memory so
the view keeps working. The engine never silently switches media for persisted data.

### 4.4 How code reaches them (Rust, desktop)

```rust
kubuno::settings!("settings.kbsettings");             // the typed class (Properties.Settings), from the file

let theme: String = Settings::theme();                // user value, else the administrator's, else the default
Settings::set_theme("Dark");                          // saved at once (a failure is logged, never the value)
let channel = Settings::update_channel();             // Application scope: no setter
let _sub = Settings::on_changed(|c| println!("{} changed", c.name));
Settings::store().reload();                           // the shared kubuno::storage::engine::Settings

// x:Name'd components are typed fields of the form (#[kubuno::view]):
self.settings.set("ShowHidden", true)?;              // kubuno::storage::Settings (handle)
self.secrets.set_str("ApiKey", &key)?;               // kubuno::storage::SecretStore
let hidden = self.explorer.get_string("Hidden")?;    // kubuno::storage::RegistryKey (Windows)
```

The typed class, the view's `<Settings>` components and background code share **one** `Settings` instance per app,
set and back-end in the process (`Settings::shared`): a change from any of them is seen by all at once.

### 4.5 TypeScript (web, ST-3)

Same element names and properties (`VIEWS-SPEC.md` registry, `web` block), `@kubuno/views` runtime: `settings.get('Theme')`,
`settings.set('Theme', 'Dark')`, a typed `Settings` generated from the same `.kbsettings` by the views compiler
(`kubuno-views-web` reads it with `kubuno-resources-model::settings`); the web counterparts live in `core/frontend`
(owned by the web views agent, not edited by lot ST-1).

---

## 5. Tooling (Visual Studio)

### 5.1 Toolbox and component tray

A « Stockage » (FR) / « Storage » tab, present in every project that links the `kubuno` crate, without « Choisir des
éléments… » (`NativeToolboxInstaller.LibraryCrates` gains `kubuno_app_storage_components`), Lucide icons
`settings-2` (Settings), `key-round` (SecretStore), `folder-key` (RegistryKey) from `tools/generate-control-icons.ps1`.
Dropped on a view, the components go to the component tray (non-visual), like the printing and data components.

### 5.2 Properties window

Enum drop-downs from the components' `PropertyValue` enums (`Backend`, `Hive`, `View`), the « Stockage » category
localized (`PropertyCategoryMap.DisplayName("Storage")`), descriptions from the doc comments; double-click → the default
event (`SettingChanged`, `ValueChanged`). Planned (ST-2): `Schema` as a drop-down of the project's `.kbsettings` sets
(`reference:` editor), a **Registry key picker** for `Path` (a themed tree of the real Registry, read-only, `HKCU`/`HKLM`,
32/64-bit view switch, value list with types), `AppId` validated as typed.

### 5.3 `.kbsettings`, the settings editor and the typed class

`settings.kbsettings` is the `Settings.settings` of a Kubuno app (XML, like `.kbres`, read by Rust and C# alike, one
canonical form both writers produce byte for byte):

```xml
<?xml version="1.0" encoding="utf-8"?>
<Settings Version="2" App="kubuno-notes">
  <Setting Name="Theme" Type="String" Default="System" Values="System|Light|Dark" Description="The colour theme."/>
  <Setting Name="SyncIntervalMinutes" Type="Int" Default="5" PreviousNames="SyncInterval"/>
  <Setting Name="WindowBounds" Type="String" Roaming="false"/>
  <Setting Name="UpdateChannel" Type="String" Scope="Application" Default="stable"/>
  <Setting Name="RecentFiles" Type="StringList" Roaming="false">
    <Item>welcome.kbdoc</Item>
  </Setting>
</Settings>
```

- Types `String`, `Bool`, `Int` (i64), `Float` (f64), `StringList` (and the .NET aliases of a migrated
  `Settings.settings`); `Scope` `User` (default) / `Application`; `Roaming="false"` for machine-local user settings;
  `Values` = an enumeration of a `String`; `PreviousNames` = renames; `Version` drives the upgrade; `App` overrides the
  package name.
- The **settings editor** (`Kubuno.Views\Settings\Editor`, default editor of `.kbsettings` in a `.rsproj` and
  everywhere else): a themed grid (Name, Type, Scope, Roaming, Value, Accepted Values, Previous Names, Description),
  Add / Remove / Version / App id / View Code, the problems of the file in the status line and the rows' tooltips (the
  Rust rules: names, duplicates ignoring case, defaults per type, accepted values, secret-like names); the document stays
  the XML buffer (Save, source control and View Code work on it), changes are minimal edits.
- **Add > New Item > Kubuno Settings File**.
- The **typed class**: `kubuno::settings!("settings.kbsettings")` reads the file at compile time (an invalid file is a
  compile error at the path literal), embeds it for change tracking, and generates one accessor per setting (`theme()`,
  `set_theme(…)`; no setter for an `Application` setting), `SET`, `VERSION`, `NAMES`, `app()`, `schema()`, `store()`,
  `save()`, `reload()`, `reset_all()`, `refresh_if_changed()`, `on_changed(f)`; it registers its schema before `main`
  (`.CRT$XCU`) so the views' `<Settings Schema="…">` bind with the declared types and defaults. **F7** from the view goes
  to the code-behind, where the typed class and the typed component fields complete through rust-analyzer.

### 5.4 Bindings

`{Binding Theme, Source=settings, Mode=TwoWay}` (the `Source=` grammar of `VIEWS-SPEC.md` §6.1: the component is a
binding provider). A list setting binds as rows with a `Value` field (`ItemsSource`). Considered and deferred: a
`{Setting Theme}` markup extension (sugar for the line above; it would need a change of the shared binding grammar on
both targets — open question Q2).

### 5.5 Language server

- completion, hover and Properties of the components come from the scan of `kubuno-app-storage-components` (reached
  through the `kubuno` facade's workspace dependencies, like printing);
- the members of a `<Settings>` come from its `.kbsettings` (`binding_sources`: completion of `{Binding …, Source=s}`,
  hover with type/scope/default/description, F12 to the setting's line, **unknown setting = error**, two-way binding of
  an `Application` setting = warning);
- `<SecretStore>` exposes only `Available` (and `<Name>.Exists`, open); `<RegistryKey>` value names are open;
- diagnostics `platform-only` (§4.2) and `open-settings` (information: a `<Settings>` naming a set with no
  `.kbsettings` is open — any name, no default, no typed class).

---

## 6. Security, limits and quotas

### 6.1 Scoping (module isolation)

- `AppId`: 1–40 characters `[a-z0-9._-]`, starting with a letter or digit, no trailing dot, no Windows device name;
  lower case so ids never collide on NTFS/APFS or in the Registry. It is a folder (`<root>/<app>/`), a Registry key
  (`Software\Kubuno\Apps\<app>`), a credential scope (`app.<app>`), a web key prefix (`kubuno:<module>:`).
- Set names `[a-z0-9._-]` (file stems); setting names `[A-Za-z_][A-Za-z0-9_]*`; secret names `[A-Za-z0-9._-]{1,64}`.
- A shared namespace (`AppId="kubuno"`) is explicit and reviewed (Q5).
- **Web caveat**: every module runs in the host's origin, so `localStorage`, IndexedDB, Cache Storage, OPFS and
  non-HttpOnly cookies are readable by any module's code: isolation there is a **naming convention enforced by the
  components and the views compiler**, not a security boundary. Anything that must be protected from other modules
  stays on the server (per-module API with the module's auth scope).

### 6.2 Encryption at rest

Secrets: the OS stores (DPAPI, Keychain, Secret Service). Databases with personal data: SQLCipher, key in the OS store
(`kubuno-sync-engine`). Settings and key/value files: **not encrypted** (they hold preferences, never secrets — the
format and the tooling warn on secret-like names); on Windows they inherit the profile's ACL, on Unix the user's
`0700` XDG directories. Web: nothing encrypted client-side (no key would be safer than the data).

### 6.3 Limits

| What | Limit | Why |
|---|---|---|
| a setting's value | 64 KiB (a list: 1 024 items) | settings are not a database |
| a settings set (file) | 1 MiB | idem; refused, never truncated |
| a Registry value | 1 MiB read/write (Microsoft advises < 2 KB) | the Registry is loaded in memory |
| a secret | 2 560 bytes on Windows (Credential Manager) | the store's limit (error names the secret) |
| web `localStorage` | ~5 MiB per **origin**, shared by all modules → budget 256 KiB per module, the rest in IndexedDB | quota exhaustion breaks every module |
| web cookie | 4 KiB, ≤ 10 per module | sent with every request |
| `SharedChannel` message | 64 KiB JSON | IPC, not file transfer |
| cache (`FileStore Kind="Cache"`) | `MaxSize` (default 256 MiB), LRU eviction | |

### 6.4 Web rules

No secret in `localStorage`/`sessionStorage`/IndexedDB/cookies; tokens stay in memory and in the HttpOnly refresh
cookie the server sets; `CookieStore` cannot read or write an HttpOnly cookie (the browser hides them) and refuses names
of the host's own cookies (`refresh_token`, `access_token`, the language cookie); `SameSite=None` only with `Secure`;
`Partitioned` when the module is embedded cross-site; `navigator.storage.persist()` asked once for module data, never
for caches. Observation (not changed here): the host writes the access token into a JS-readable `access_token` cookie
for downloads (`client.ts` `writeTokenCookie`, `SameSite=Strict`, 15 min) — the token is in JS memory anyway, but a
download URL with a short-lived signed ticket would remove it (Q7).

### 6.5 Logging

Values are never logged: errors carry the app, the set, the setting or secret name, the file or Registry path and
the OS error; a corrupted settings file is reported with its position only (the parser's message could quote content).

---

## 7. Testing

- **In-memory back-ends** everywhere (`MemoryBackend`, `MemorySecretStore`, `Backend="Memory"` in views), and a memory
  back-end shared with the test to play "another process" (external changes, upgrades, administrators' values).
- **Sandboxed profile** (`KUBUNO_SANDBOX_DIR`): files under the sandbox, Registry under
  `HKCU\Software\Kubuno\Sandbox\<tag>` (deleted at the end of the test), secret names prefixed — one test per binary
  (the variable is process-wide).
- **Registry tests** run below a throw-away `HKCU\Software\Kubuno\Tests\<name>-<pid>-<nanos>` (`RegistryRoot::under`),
  deleted by a drop guard even when an assertion fails; parents left empty are removed with `RegDeleteKeyExW` (which
  refuses a key that still has sub-keys, so a concurrent test's key is never deleted). Checked after the runs:
  `HKCU\Software\Kubuno` keeps nothing.
- **Credential Manager**: the components' tests use `Backend="Memory"`; the engine's sandbox test uses a recording store;
  the real store is only exercised by `kubuno-secrets`' existing `--ignored` test.
- **Cross-language format**: the same `.kbsettings` sample is parsed and re-written by Rust and C#, both must give the
  same bytes; the Rust format and engine agree on types and spellings (a test).
- **Multi-OS CI**: `cargo test -p kubuno-app-storage` on Windows, Linux and macOS runners (the Registry tests are
  `cfg(windows)`; the sandbox test covers files and secrets everywhere); `cargo check`/`clippy` for
  `x86_64-unknown-linux-gnu`, `aarch64-apple-darwin`, `x86_64-apple-darwin` from Windows (done in ST-1).

---

## 8. Prior art — what we take, what we avoid

| Prior art | Take | Avoid |
|---|---|---|
| WinForms `ApplicationSettingsBase` + Settings designer | a project file with name/type/scope/default, a generated typed class, User vs Application scope (read-only), `SettingChanged`/`SettingsSaving`, `Reload`/`Reset`/`Save` | `user.config` under a path hashed from the exe's evidence and **one folder per version**, which made `Upgrade()` a manual copy and lost settings on every update; XML-serialized arbitrary types; no change notification across instances |
| `Microsoft.Win32.Registry` / `RegistryKey` | hives, `RegistryView`, typed values, `OpenSubKey(writable)`, `DeleteSubKeyTree` | settings in the Registry by default (opaque, not portable, roams the whole `HKCU\Software`); writing `HKLM` from apps |
| `IsolatedStorage` | per-app, per-user isolation, quotas | evidence-based scopes nobody understood, hidden locations |
| WPF | `{Binding}` to settings (`Source={x:Static p:Settings.Default}`) | a static `Default` instance hard to test |
| .NET MAUI `Preferences` / `SecureStorage` | one API, platform back-ends (NSUserDefaults, SharedPreferences, Keychain/Keystore) | `SecureStorage` silently falling back on some platforms; untyped preferences |
| Electron `electron-store`, Tauri `plugin-store` | JSON file in the app-data folder, schema with defaults, migrations by version, change events | encryption with a key stored next to the file (obfuscation presented as security) |
| Web Storage, IndexedDB, Cache Storage, OPFS, Cookie Store API | the right medium per need (§3.4), `storage` events and `BroadcastChannel` for cross-tab notification | `localStorage` for everything (synchronous, 5 MiB per origin, shared by all modules), secrets in JS-readable storage |
| Apple `NSUserDefaults` suites / App Groups, Android DataStore | suites for sharing between a vendor's apps, typed async stores with migrations | writing plists behind `cfprefsd`; `SharedPreferences` on the main thread |

---

## 9. Lots

| Lot | Content | Size | Order / depends on |
|---|---|---|---|
| **ST-1** (done) | `kubuno-app-storage` (paths moved from `kubuno-account`, `machine_config_dir`, `AppId`, typed settings engine with scopes, defaults, notifications, external-change refresh, versioned upgrade, shared instances; File / Registry / Memory back-ends; `AppSecrets`; Windows Registry API with views and sandbox redirection); `<Settings>`, `<SecretStore>`, `<RegistryKey>` components; `.kbsettings` format + `settings!` typed class; `kubuno::storage` handles; LS members/diagnostics; Toolbox tab, icons, Properties category; settings editor + item template; sample; tests | L | — |
| **ST-2** (done, §12) | `KeyValueStore`, `FileStore` (data/cache/temp, eviction), `LocalDatabase` for the data components; **Registry key picker** in Properties; `AccountScoped` settings (Q6); one secret naming scheme (Q4); the shell's `shell.json` and drive's `settings.json` migrated onto `Settings` (one-time import, logged); undo/redo in the settings editor; the declared defaults on the design surface. Moved to a later lot: the `Schema` drop-down, platform badges and target filtering in the Toolbox, the `Error` event | L | ST-1 |
| ST-3 | **web**: `@kubuno/views` `Settings` (server user preferences + `localStorage` cache, `storage`/`BroadcastChannel` notifications), `CookieStore`, `WebStorage`, `KeyValueStore`/`FileStore` on IndexedDB/OPFS/Cache Storage, `SecretStore` memory-only; per-module budgets; registry `web` blocks; conformance with the desktop export; a server endpoint for user preferences per module (core) | L | ST-1; coordinate with the web views agent (core/frontend) |
| ST-4 | `SharedChannel` (pipes / Unix sockets / BroadcastChannel), file and Registry change watchers (`ReadDirectoryChangesW`, `RegNotifyChangeKeyValue`, inotify, FSEvents) replacing the 2 s poll | M | ST-1 |
| ST-5 | `UserDefaults` (CFPreferences) and `GSettingsKey`; `Environment`; `Clipboard` | M | ST-1, the macOS/Linux renderers for views |
| ST-6 | policies (`HKLM\Software\Policies\Kubuno\<app>`, `/etc/xdg` locked keys, configuration profiles), administrative templates (ADMX) generated from `.kbsettings` Application settings | M | ST-1 |
| ST-7 | mobile: DataStore / UserDefaults / Keystore / Keychain back-ends through UniFFI when the mobile renderer of views exists (`ARCHITECTURE.md` roadmap) | L | mobile renderer |

---

## 10. Decisions (2026-10-02)

The product owner approved every recommendation below (Q1–Q9) on 2026-10-02: they are **decisions**. Q7 is handled
by a separate security lot (core/frontend and the auth code are not changed by the storage lots); ST-3 (web) waits for
that lot and the web views work to settle.

| # | Question | Decision (2026-10-02) |
|---|---|---|
| Q1 | Default back-end of `Settings` on Windows: files or the Registry? | **Files** (§4.3): portable, inspectable, sandbox-friendly, one code path; the Registry stays one property away. |
| Q2 | Add a `{Setting Name}` markup extension? | **Not now**: `{Binding Name, Source=settings, Mode=TwoWay}` already works on both targets and the designer's binding picker writes it; revisit when the web compiler (ST-3) shares the binding grammar change. |
| Q3 | Where does `Settings` roam on the web: browser storage or server? | **Server** (per user and module, like the core's existing user preferences) with a `localStorage` cache; `Roaming="false"` stays in the browser. |
| Q4 | Unify the three secret naming schemes? | Yes, in ST-2: `kubuno-data`'s `SecretResolver` gains an `AppSecrets` source (`Kubuno/app.<UserSecretsId>/ConnectionStrings.X`) before its own Credential Manager lookup, which stays for compatibility one release. |
| Q5 | Allow data shared between Kubuno apps? | Only through an explicit, reviewed namespace (`AppId="kubuno"` settings, `SharedChannel Scope="User"`), never by reading another app's id; the token broker remains the only path for tokens. |
| Q6 | Settings per account (multi-account apps)? | Add `AccountScoped="true"` in ST-2: the set goes under `<data>/accounts/<key>/<app>/` (the sync engine's layout), wiped with the account. |
| Q7 | The JS-readable `access_token` cookie of the web host? | Out of this lot; recommend short-lived signed download tickets so no bearer token is ever in a JS-readable cookie (core security review). |
| Q8 | Linux without a Secret Service | Keep `kubuno-secrets`' rule: session-only by default, the `0600` file only after an explicit warning; `SecretStore.Available` lets a view show it. |
| Q9 | macOS plist back-end now? | No file back-end on `~/Library/Preferences` (cfprefsd caches and overwrites it); `UserDefaults` through `CFPreferences` in ST-5; files meanwhile. |

---

## 11. Part 2 — as built (lot ST-1, 2026-10-02)

### 11.1 Code

| Where | What |
|---|---|
| `desktop/common/kubuno-app-storage` (new, workspace `desktop/common`) | `paths` (moved from `kubuno-account`, + `machine_config_dir`), `app` (`AppId`, default app id), `settings::{value, schema, store}` (`SettingValue`/`SettingType` with coercions, `SettingDef`/`SettingsSchema` with validation and limits, `Settings` engine), `backend::{file, registry, memory}`, `secrets` (`AppSecrets`), `registry` (Windows: `RegistryRoot`, `RegistryKey`, `RegValue`, `Hive`, `RegistryView`). Deps: `kubuno-secrets`, `serde_json`, `thiserror`, `tracing`, `sha2`, `hex`, `windows-sys` 0.61 |
| `desktop/common/kubuno-account` | `paths.rs` is now `pub use kubuno_app_storage::paths::*` (same API, same locations) |
| `windows/src/crates/kubuno-app-storage-components` (new) | `Settings`, `SecretStore`, `RegistryKey` (`#[derive(Component)]`, Toolbox category `Storage`), their args, the binding conversions |
| `kubuno-resources-model::settings` | the `.kbsettings` format (lenient read with positioned diagnostics, canonical write) |
| `kubuno-resources-macros` | `settings!` (next to `resources!`) |
| `kubuno` (facade) | `kubuno::storage` (handles `Settings`/`SecretStore`/`RegistryKey`, the engine as `kubuno::storage::engine`), `kubuno::settings!`, the event args in the prelude |
| `kubuno-views-meta` | `STORAGE_ELEMENTS`, `STORAGE_TYPED`, `PLATFORM_ELEMENTS`; `LIBRARY_ELEMENTS` extended |
| `kubuno-views-macros` | `<Settings x:Name>` → `kubuno::storage::Settings` field (likewise the two others) |
| `kubuno-views-ls` | `storage.rs` (settings members, `platform-only`, `open-settings`), hooks in `binding_sources` and `server` |
| `kubuno-print/examples/view_embed.rs` | the bundled design surface links the storage components |
| vskubuno | `LibraryCrates`, the « Stockage » tab and category, icons, `Kubuno.Views.Logic\Settings\KbsettingsFile`, `Kubuno.Views\Settings\Editor\*`, `KbsettingsEditorProvider` (CPS), package registration (`113`), the `KubunoSettingsFile` item template, `samples/storage-desktop`, tests |

### 11.2 Verified

See the CHANGELOG entry and the final report of the lot; the checks are: unit and integration tests (engine 26 incl.
Registry and sandbox, components 8, facade 2, LS 4 + fixture generator, format 3, C# format 3 + Toolbox 2), clippy
`-D warnings` on every touched crate, `cargo check`/`clippy` of the engine for Linux and macOS (x86_64 and aarch64),
the sample run live in a sandbox (`--self-test`, then interactively through UI Automation: upgrade v1→v2 of a stored
file, an administrator's machine value, an external change picked up in two seconds with `SettingChanged`, a two-way
binding saved and merged with the other instance's change), and Visual Studio: a fresh hive `KubunoStorage`
with the Release VSIX first opened the view in the XML editor (the known stale-configuration issue of fresh hives);
`devenv /rootsuffix KubunoStorage /updateconfiguration` fixed it. Then: the sample's view in the designer with
`settings`, `secrets` and `explorer` in the component tray (their icons), the Toolbox tab « Stockage » (RegistryKey,
SecretStore, Settings), the Properties window of the selected `Settings` (category « Stockage »: AppId, Backend
`Auto`, Schema `settings`, the descriptions), `Backend` changed to `Registry` from its drop-down (document marked
modified; undone), F7 opening `main_view.rs`, and `settings.kbsettings` opened in the settings editor (7 rows, Version
2, the French UI; « Ajouter un paramètre » added `Setting1`, the status became « 8 paramètre(s) » and the XML document
became dirty; closed without saving). C#: `Kubuno.Views.Tests`, `Kubuno.Desktop.Tests`, `Kubuno.Architecture.Tests`
692 passed, 3 ignored (pre-existing, they need a live language server).

### 11.3 Limits of ST-1

- Other instances' changes are polled (2 s), not watched (ST-4).
- The settings editor has no undo of its own (the text buffer's undo works from the code view); a `.kbsettings`
  attribute it does not know is dropped when it rewrites the file.
- The design surface shows the storage components in the tray; it never reads the developer's settings, secrets or
  Registry (memory). Before the project's own design build, the surface does not know the project's `.kbsettings`
  schema (an open set): bound controls show their fallbacks, not the declared defaults; `d:` attributes cover it.
- The Properties window of a non-visual component also lists the view-level « Disposition » rows (Anchor, Dock,
  Location, Size), like the printing components (a designer-wide behaviour, not specific to this lot).
- `kubuno-sync-engine` (which re-exports the moved paths) could not be rebuilt on this machine (its vendored OpenSSL needs
  a native Perl); its use of `kubuno_account::paths` names is unchanged.
- The doctests of `kubuno-views-macros` fail to link on this machine (LNK1120, the `prefer-dynamic` dylib without
  `RUSTDOCFLAGS`), independently of this lot.

---

## 12. Part 3 — lot ST-2 (2026-10-02)

### 12.1 Built

- Engine (`desktop/common/kubuno-app-storage`): `account` (current account, `<data>/accounts/<key>/<app>/`,
  Registry `Software\Kubuno\Accounts\<key>\Apps\<app>`), `AccountScoped` schemas (no account: memory + warning),
  `kv` (`KeyValueStore`, JSON file, TTL, read-merge-write), `files` (`FileStore` Data/Cache/Temp, LRU eviction,
  name-only API, `local_database_path`), `settings::migrate::import_legacy_json` (one-time import, file renamed
  `*.migrated`), `settings::serde_bridge` (a serde struct as an open-schema set).
- Components: `KeyValueStore`, `FileStore` (`kubuno-app-storage-components`), `LocalDatabase` (`kubuno-data`,
  a `DbConnection` subclass, `app:`/`account:` specs); `AccountScoped` on `Settings`; the designer reads the
  declared defaults of the project's `.kbsettings` (also when the surface never syncs the providers).
- Secrets (Q4): `kubuno-data`'s default chain = environment → `Kubuno/app.<id>/<key>` (`AppSecretsSource`) →
  the legacy Credential Manager names (read, then migrated) → user secrets.
- Shell (`shell.json` → `shell.kbsettings`) and drive (`settings.json` → `kubuno-drive` settings) moved onto the
  engine with a one-time import, both tested in a sandbox.
- vskubuno: Registry key picker (`[editor("registry-key")]`, `RegistryKeyPickerDialog`, read-only), undo/redo of the
  settings editor (`SettingsHistory`, Edit.Undo/Redo; a cell being edited keeps its own), the « Par compte » check
  box (`AccountScoped`, same canonical text as Rust), icons, LS binding members, fixture, sample (`state`, `thumbs`).

### 12.2 Verified

Rust tests of every touched crate (engine 37, components 12, data, facade, LS 166, format 19, shell and drive
migrations), clippy `-D warnings`, engine clippy for Linux and macOS; the sample's `--self-test` in a sandbox (KV
set/get/remove, file write/read/refused name/clear); C# `Kubuno.Views.Tests` 457 passed, 2 ignored (pre-existing);
VS (hive `KubunoStorage`, Release VSIX): `state` and `thumbs` in the component tray with their icons.

### 12.3 ST-2 status (stopped on budget, 2026-10-02)

- The declared-defaults fix (no provider sync on the design surface) is unit-tested but not yet seen in Visual Studio:
  rebuild `view_embed` + the VSIX and look at the sample's designer (Theme `System`, interval `5`).
- Not checked live in VS: the Registry key picker from Properties, undo/redo in the settings editor, the « Par
  compte » check box, the « Stockage » tab listing the 5 components (covered by the C# tests and the fixture).
- Moved to a later lot: the `Schema` drop-down, platform badges and target filtering, the `Error` event,
  `LocalDatabase` encryption (SQLCipher).
- `kubuno-views-meta`'s `framework` test fails on `kubuno-header-data` (another lot's new crate, not listed).
- `kubuno-sync-engine` still needs a native Perl (vendored OpenSSL) to compile here; nothing was installed.
- Lesson: the sample and the `windows` workspace must not share a target dir (two `drive-app-controls` builds
  conflict); clean those crates after building the sample.
