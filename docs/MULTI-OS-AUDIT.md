# Multi-OS portability audit (Linux / Windows / macOS) - server side and packaging

Date: 2026-10-01. Scope: `core` (all crates, CLI, packaging scripts, CI), the 24 module repositories
(`app assistant books build calendar chat code contacts drive flow forms forum keestore mail maps media notes
office p2pnas paintsharp photos stt tasks wiki`), `_tools`. `desktop` was out of scope.

Trigger: `LocalStorage::resolve()` compared a path built on the raw root (`C:\...`) with the canonical root
(`\\?\C:\...`) and rejected every path on Windows (fixed in core `cd96bae`, **not yet tagged**). This audit looks
for the rest of that class of bug and for everything else that stops "export for macOS / Windows" from just
working.

The audit made no changes to product code. This file is its only output.

## 0. How this was verified

| What | How | Result |
|---|---|---|
| Shared crates on Windows | `cargo test -p kubuno-storage -p kubuno-seccomp -p kubuno-modauth -p kubuno-mcp -p kubuno-db` (MSVC, `SQLX_OFFLINE=true`) | all pass (19 storage, 77+ db; DB-backed tests are ignored) |
| Core on Windows | `cargo test -p kubuno-core --lib` (MSVC) | **765 passed, 0 failed**: core compiles and its unit tests pass on Windows |
| `LocalStorage` behaviour on Windows | scratch probe crate (outside the repos) against `kubuno-storage` at HEAD (with `cd96bae`) | see §2.1, several confirmed bugs |
| Everything else | systematic grep + reading every hit in context (3 parallel sweeps over the modules, one over core) | evidence is cited as `file:line` |

Not verified: anything on macOS (no Mac available to the agent), module builds on Windows/macOS (only core was built),
and the GitHub dist runs (`gh` not installed on this host). Lines marked *(read)* were found by reading code only.

## 1. Executive summary

1. **No platform-directory abstraction exists.** Core and almost every module hard-code the Linux FHS layout
   (`/etc/kubuno`, `/var/lib/kubuno`, `/var/log/kubuno`, `/var/backups/kubuno`, `/usr/lib/kubuno/modules`). Windows
   and macOS work only through overrides in the installer, and those overrides are incomplete. On Windows a
   path like `/var/lib/kubuno` is **drive-relative** and resolves to `C:\var\lib\kubuno`. Any authenticated user can
   create folders at `C:\` and has Modify rights on what they create, so those paths are both misplaced and insecure.
2. **Where the core keeps its secrets depends on whether a directory happens to exist.** `data.key`,
   `setup-token` and `initial-admin-password` go to `/var/lib/kubuno` *if that directory exists*, otherwise to the
   working directory. On Windows and macOS the first module install (default store `/var/lib/kubuno/modules-store`)
   *creates* that directory. From then on the core looks for `data.key` somewhere else. It either fails to start
   (macOS, permission denied) or re-seeds a key from `jwt_secret` (Windows). After a `security:rekey`, re-seeding
   means **every encrypted value becomes unreadable**.
3. **User file names go straight to disk** (drive's on-disk tree mirrors the virtual tree). On Windows and macOS this
   breaks in several ways:
   - names differing only by case collide, and the second upload overwrites the first (data loss);
   - NFC and NFD forms of the same name collide on macOS;
   - `:` creates NTFS alternate data streams;
   - `\` becomes a real path separator. Combined with drive's weak name validation, that allows cross-user path
     traversal on Windows;
   - `CON` and names ending in `.` or a space are created verbatim through `\\?\` and cannot be handled by Explorer
     or backup tools.
4. **Drive's scanner will trash files on Windows.** `storage_path` is stored as `uid\files\Docs/Sub\a.txt` but the
   scanner rebuilds `uid\files\Docs\Sub\a.txt`. For files two or more folders deep the two strings never match, so the
   scanner inserts duplicate rows and moves the originals to the trash. It runs after every write.
5. **The `\\?\` canonicalisation class is not fully fixed:** `LocalStorage::list()` (confirmed),
   code `file_tree` (blocker), and the drive and media watchers on macOS (`/private/var`).
6. **Platform-specific code in core is mostly well gated.** The gaps:
   - no graceful shutdown on Windows service stop or logoff;
   - Windows has no ACL equivalent of the `0600` permissions used for secrets;
   - the seccomp "no exec" guarantee exists only on Linux x86_64 (not Windows, macOS or Linux aarch64);
   - remote connectors spawn `smbclient`, `nfs-cp` and `curl` through hard-coded `/tmp`.
7. **Packaging:**
   - The `.kbpkg` *layout* is identical across OSes; the *content* is not (each package carries a native binary).
     `kubuno modules:install` never checks the package's target OS/arch.
   - The Linux packages are not portable across distributions: they need dynamic `libssl.so.3` (from reqwest's
     default native-tls) and the glibc of `ubuntu-latest`.
   - No repository has a `.gitattributes`. On a Windows CRLF checkout, the SQL migrations embedded by
     `sqlx::migrate!` get different checksums, so a database migrated by a Linux build is refused by a Windows build.
8. **CI:** tests and clippy run on Linux only. Windows and macOS are built only when a `v*` tag is pushed, and never
   tested, so platform breakage is discovered at release time. No `cargo test` runs in any module CI.

## 2. Findings

Severity: **blocker** = does not build, start, or a core feature is broken on that OS; **bug** = wrong behaviour
(including security); **risk** = latent or edge case; **cosmetic** = docs, messages, dev scripts.

### 2.1 Core and shared crates

| # | Repo | file:line | Cat. | OS | Severity | Finding | Suggested fix |
|---|---|---|---|---|---|---|---|
| C1 | core | `kubuno-core/src/crypto/datakey.rs:35-39`, `setup/token.rs:58-61`, `database/seed.rs:54-57` | 1 | Win, mac | **blocker** | The state dir is `/var/lib/kubuno` *if it exists*, else CWD. On Windows that is `C:\var\lib\kubuno` (drive-relative), and it starts to exist as soon as a module is installed with the default store (C3). From then on `data.key` is looked up elsewhere: on Windows it is re-seeded from `jwt_secret`, so data is lost after a rekey; on macOS the write fails with EACCES (`_kubuno` user) and the core will not start. The CLI (run from another CWD) also never finds the service's `data.key`. | One explicit `state_dir` from `kubuno-paths` (§3.1), never inferred from `is_dir()` |
| C2 | core | `config/settings.rs:178-187, 383-418`; `network/store.rs:30`; `backup/policy.rs:39`; `data_export/policy.rs:55`; `setup/handlers.rs:263`; `modules/db_registry.rs:127`; `handlers/admin/db_switch.rs:136,472`; `handlers/admin/module_databases.rs:276`; `rules/dispatch.rs:417` | 1 | Win, mac | **bug** | Every default is FHS: `/usr/lib/kubuno/modules`, `/var/lib/kubuno/{themes,db,tls,modules,modules-store}`, `/etc/kubuno/modules`, `/var/log/kubuno`, `/var/backups/kubuno`, `/var/lib/kubuno/exports`. On Windows these are drive-relative paths under `C:\`, which any local user can write; on macOS `/var/lib` does not exist and is root-owned. | Platform defaults from `kubuno-paths` |
| C3 | core | `build_windows.sh:150-160` (WinSW XML) and `:163-195` (config.toml) | 4 | Win | **blocker** | The installer overrides `frontend_dist`, `modules_dir`, `themes_dir`, `modules_config_dir`, `modules_data_dir`, `storage.local_path` and `log_dir`, but **not** `modules_install_dir` (store, which then becomes `C:\var\lib\kubuno\modules-store`), `database.path` (SQLite), the TLS dir, the backup/export destinations, or `KUBUNO_DATA_KEY_FILE` / `KV_SETUP_TOKEN_FILE`. It is also the trigger of C1. | Set them all, better: derive them in code (`%ProgramData%\Kubuno\...`) |
| C4 | core | `build_macos.sh:117-121, 154-160` | 4 | mac | **blocker** | The plist sets only `FRONTEND_DIST` and `MODULES_DATA_DIR`. `modules_install_dir` stays `/var/lib/kubuno/modules-store`, which `_kubuno` cannot create, so marketplace install fails, and `sudo kubuno modules:install` creates `/var/lib/kubuno` and triggers C1. `/etc/kubuno/modules` (module CWD and config) is root-owned, so modules cannot write their config. | Set all dirs; postinstall `chown` of config/store dirs; platform defaults in code |
| C5 | core | `bin/kubuno` + `config/settings.rs:415-420` | 1/4 | Win, mac | **bug** | The CLI loads `./config.toml` then `/etc/kubuno/config`. On Windows the real file is `C:\ProgramData\Kubuno\config.toml` and is never found, so `modules:install`, `db:*` and `users:*` silently run against defaults (another store, no DB credentials). | `kubuno-paths::config_file()` (ProgramData on Windows, `/etc/kubuno` or `/Library/Application Support/Kubuno` on macOS) |
| C6 | core | `setup/config_file.rs:136-153` | 1 | Win, mac | bug | The setup wizard writes `/etc/kubuno/config.toml` if `/etc/kubuno` exists, else `./config.toml`; examples are read from `/etc/kubuno/config.toml.example`. Same root cause as C5. | Same |
| C7 | core | `crypto/datakey.rs:73-77`, `setup/token.rs:42-46`, `database/seed.rs:178-182`, `setup/config_file.rs:192-196`, `network/store.rs:86-91`, `backup/dump.rs:103,289`, `backup/archive.rs:201`, `backup/portable.rs:146`, `data_export/archive.rs:238,265,360,424` | 3 | Win | **bug (security)** | Secrets (`data.key`, initial admin password, setup token, `config.toml` with the DB password and JWT secret, the TLS private key, backups, exports) get `0600`/`0640` on Unix and **nothing** on Windows. Under `C:\ProgramData` the inherited ACL gives `BUILTIN\Users` read access. | `kubuno-paths::write_private()` setting a DACL (SYSTEM + Administrators + service SID) via `windows-sys` `SetNamedSecurityInfoW`; harden the data root once at install |
| C8 | storage | `kubuno-storage/src/backend/local.rs:150-170` (`list`) | 1 | Win; Unix with a symlinked root | **bug** (confirmed) | `strip_prefix(&self.base)` against entries from the canonical path fails, so `StorageObject.path` is returned **absolute** (`\\?\C:\...\u\files\ab.txt`, confirmed by the probe) and with `\` separators. Same class as `cd96bae`. | Strip against the canonical base; return `/`-joined relative keys |
| C9 | storage | `local.rs:23-42` (`resolve`) | 2 | Win | **bug** (confirmed) | Because every path now goes through `\\?\`, Win32 name rules are bypassed. `CON`, `nul.txt` and `trail. ` were all created verbatim on disk (probe) and are unusable from Explorer, robocopy or most backup tools. `x:y.txt` is rejected with a misleading "outside the storage area" (`x:` parsed as a drive). `q?.txt` gives an I/O error 123. A key containing `\` creates a sub-directory. | Validate each key component in `resolve()` with a portable name policy (§3.3) and treat `\` as illegal in keys |
| C10 | storage | `local.rs:29-32` | - | all | cosmetic/perf | `canonicalize()` on every call (one extra syscall per I/O) and silent fallback to the raw base on error. | Canonicalise once in `new()` (with `dunce::canonicalize` for display/logs) |
| C11 | storage | `naming.rs:8,31` (`unique_file_name`/`unique_dir_name`) | 2 | Win, mac | **bug (data loss)** | Case-sensitive and normalisation-sensitive comparison ("Linux filesystem behaviour", per its own doc comment). Probe: `Report.txt` then `report.txt`, and `Report.txt` now contains the second upload. On macOS, NFC `café` and NFD `café` collide too (NTFS keeps both, confirmed). | Compare with a `name_key()` = NFC + Unicode case-fold (§3.4), on every OS, so behaviour does not depend on the host |
| C12 | storage | `path.rs:13-19` (`user_file_path`, `user_folder_dir`) | 1 | Win | **blocker (via drive D1)** | `folder_virt_path` (`Docs/Sub`) is joined as **one** component; on Windows the string is `uid\files\Docs/Sub\a.txt`. The filesystem accepts it, but string comparisons with a walked path fail. | Split on `/` and push components; produce storage keys as `/`-strings, convert to `PathBuf` only at the I/O edge |
| C13 | storage | `local.rs:190-201` (`mv_dir`), `put`/`put_stream` | 2 | Win | risk | `rename` of a directory fails on Windows if any file inside is open (a download stream, the thumbnailer), and `File::create` truncates in place (not atomic on any OS). Note: `std::fs::rename` **does** replace an existing *file* on Windows (`MOVEFILE_REPLACE_EXISTING`), but fails if the target is open without `FILE_SHARE_DELETE`. | Retry with backoff / map to 409; write to temp + rename for `put` |
| C14 | seccomp | `kubuno-seccomp/src/lib.rs:26-45` | 3 | Win, mac, Linux aarch64 | **risk (security)** | The no-`execve` filter is a no-op off Linux x86_64, and the doc comment says those platforms "are not deployment targets", which contradicts dist.yml. | Linux aarch64: add `AUDIT_ARCH_AARCH64` and syscall numbers (cheap). Windows: Job Object with `JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 1` (forbids child processes, a true equivalent). macOS: no supported API (`sandbox_init` is deprecated): document the gap. Fix the comment. |
| C15 | core | `modules/manager.rs:313-314` | 1 | Win | cosmetic | `format!("{}/{module_id}")` builds mixed-separator dirs. | `Path::join` |
| C16 | core | `modules/manager.rs:400-420` | 3 | Win (mac partly) | risk | Children are neither in a Job Object (Windows) nor have `PR_SET_PDEATHSIG` (Linux). If the core crashes, the modules stay alive and hold their ports, so the restarted core cannot start them ("port in use"). systemd's cgroup and launchd's process group hide this on Linux/macOS; WinSW restart does not. Stop is always `TerminateProcess`/SIGKILL, never graceful. | Windows Job Object `KILL_ON_JOB_CLOSE` (it can be combined with C14 for modules); graceful stop via an IPC `/internal/shutdown` before kill |
| C17 | core | `main.rs:571-597`, `setup/mod.rs:185-205` | 3 | Win | risk | Only Ctrl-C is handled on Windows. `CTRL_CLOSE`/`CTRL_SHUTDOWN`/`CTRL_LOGOFF` are not, so a machine shutdown kills the core without draining the job runner. WinSW sends Ctrl-C on stop, so a normal stop is fine. | Add `tokio::signal::windows::{ctrl_close, ctrl_shutdown}` arms; consider `windows-service` later to drop WinSW |
| C18 | core | `modules/manifest.rs:122` | 3 | Win | risk | `runtime = "python"` runs `python3`, which on Windows is usually absent or the Microsoft Store stub. | Try `python3`, `python`, `py -3` (or a configured interpreter) |
| C19 | core | `storage/remote/smb.rs:131-137, 233, 261`, `nfs.rs:58, 124, 145`, `ftp.rs:67-248` | 1/3 | Win, mac | **bug** | Remote mounts spawn `smbclient` / `nfs-cp` / `curl`, with temp files under a hard-coded `/tmp` (`C:\tmp` on Windows, usually missing). `smbclient` and `nfs-cp` do not exist on Windows or a stock Mac. The `get_file` temp copies are never deleted on any OS (leak). | `std::env::temp_dir()` + `tempfile` with delete-on-drop; pure-Rust clients (`suppaftp`, an SMB2 crate), or gate the connectors per OS in the UI |
| C20 | core | `bin/kubuno/db.rs:69, 132` | 3 | Win, mac | risk | `db:backup`/`db:restore` spawn `pg_dump`/`psql` (usually not on PATH on Windows), while the server-side backup is already in-process (`backup/dump.rs`). | Make the CLI use the in-process dump/restore |
| C21 | core | `bin/kubuno/modules.rs:76-81` | 3 | Win | bug | Module command dispatch looks for `kubuno-<id>` next to `kubuno.exe`; the file is `kubuno-<id>.exe`, so the sibling lookup fails and it falls back to PATH. It should also look in the store, not next to the CLI. | Reuse `ModuleManifest::entrypoint_path` on the store dir |
| C22 | core | `modules/marketplace/install.rs:539-607` (`install_local`) | 4 | all | **bug** | The `.kbpkg` target OS/arch is never checked; it lives only in the file name. A Linux package installed on Windows unpacks fine and then fails to spawn with "No such file". | Write `[package] target = "<rust triple>"` into the packaged `module.toml` (build_kbpkg.sh) and check it against `std::env::consts` |
| C23 | core | `install.rs:261-266, 576-587` | 2 | Win | **bug** | Updating a running module: `remove_dir_all(dest)` errors are ignored, then `rename` fails, and the `copy_dir_all` fallback fails on the locked `.exe` (sharing violation). Updates in place are impossible on Windows. | Stop the module's supervisor before relocating (the in-UI path can do it; the CLI must tell the user to stop the service) |
| C24 | core | `extract.rs:14-25` | 4 | Win | risk | `zip::ZipArchive::extract` on Windows: an archive with symlinks needs privilege; Unix modes are ignored (fine). | Reject symlink entries in `.kbpkg` explicitly (portable and safer) |
| C25 | core | `bin/kubuno/cli.rs:74,99,410-412`, `modules.rs:161`, `rekey.rs:275,306`, `reset.rs:183,247,283`, `db.rs:214` | 7 | Win, mac | cosmetic | Help and messages tell the user to run `systemctl restart kubuno` and quote `/var/lib/kubuno/...`. `reset.rs` mentions `kubuno-<id>` systemd units that no longer exist on any OS. | Per-OS message helper (`sc`/WinSW, `launchctl kickstart`) |
| C26 | core | `build_macos.sh:132-165` (plist) | 7 | mac | **bug** | No `SoftResourceLimits/NumberOfFiles`. The launchd default soft limit is **256** fds for the core *and* its ~24 child modules (DB pools, HTTP, streams), while Linux units set `LimitNOFILE=65536`. | Add `SoftResourceLimits`/`HardResourceLimits` `NumberOfFiles` = 65536 |
| C27 | core | `build_macos.sh` layout (`/usr/local/var/kubuno`, `/etc/kubuno`) | 4 | mac | risk | `/etc` -> `/private/etc` and `/var` -> `/private/var` are symlinks; any non-canonical prefix comparison breaks there (exactly the `cd96bae` class). `/usr/local/var` is a Homebrew-era convention. | macOS defaults: `/Library/Application Support/Kubuno` (data, config), `/Library/Logs/Kubuno`; canonicalise roots once |
| C28 | core | `kubuno-core/tests/tls_serve.rs:12` (`/tmp/kbtls`), `kubuno-db/tests/journal_delta.rs:68` (`tempdir_in("/tmp")`) | 6 | Win | cosmetic | Integration tests assume `/tmp` (they did not run here: they need a DB). | `tempfile::tempdir()` |
| C29 | core | `Cargo.lock`: `reqwest` default features, so `native-tls`; `tokio-tungstenite` native-tls | 5 | Linux | **bug** | Linux binaries link `libssl.so.3`/`libcrypto.so.3` dynamically, so the `.deb`/`.rpm` and every module `.kbpkg` built on `ubuntu-latest` will not start on RHEL 8, Debian 11 or any other OpenSSL 1.1 host. There are three TLS stacks in one binary (native-tls, rustls+ring, rustls+aws-lc) with different trust stores per OS (OpenSSL / SChannel / Security.framework vs webpki / platform-verifier). | `reqwest = { default-features = false, features = ["rustls-tls-native-roots", ...] }` or `rustls-platform-verifier` everywhere; one crypto provider |
| C30 | all repos | no `.gitattributes` anywhere; Windows runners have `core.autocrlf=true` | 4/6 | Win | **bug** | `sqlx::migrate!` (used by core via `kubuno-db` `migrations!`, and by every module) embeds a SHA-384 of the file **bytes** (sqlx 0.9 `migration.rs:86`; ignoring `\r` is opt-in only). A Windows CI build embeds CRLF checksums, and a DB migrated by a Linux build then fails with `VersionMismatch`. This blocks any Linux-to-Windows move and the multi-engine `db_switch` flows. `module.toml`, `config.toml.example` and the `SHA256SUMS` inside the kbpkg also differ per OS. | `.gitattributes`: `* text=auto eol=lf` + `*.sql *.sh *.toml text eol=lf`, in **every** repo; optionally a CI check "no `\r` in migrations/" |
| C31 | core | `.github/workflows/*.yml` | 6 | Win, mac | **bug (process)** | `checks.yml` (clippy, tests) is Linux only. Windows/macOS are compiled only on a `v*` tag (`dist.yml`), so every `#[cfg(windows)]`/`#[cfg(not(unix))]` branch is unchecked on PRs. macOS is arm64 only, and there is no Linux aarch64 build. | §5 |

### 2.2 drive (highest priority module: user files on disk)

| # | file:line | Cat. | OS | Severity | Finding | Suggested fix |
|---|---|---|---|---|---|---|
| D1 | `src/services/scanner.rs:110-112` vs `files.rs:829-830` + storage `path.rs:13-19` | 1 | Win | **blocker (data loss)** | Stored `storage_path` = `uid\files\Docs/Sub\a.txt`; the scanner builds `uid\files\Docs\Sub\a.txt`. For files two or more folders deep, the `storage_path NOT IN (...)` step (`scanner.rs:186-205`) trashes the real rows and inserts duplicates. The watcher (`watcher.rs:50-85`) triggers the scan after writes. | Canonical `/` keys (C12); scanner compares normalised keys; **never trash on mismatch without a guard** (e.g. refuse if more than N% would be trashed or any walk error happened) |
| D2 | `src/services/folders.rs:769-779` (used at `:100`, `:389`) | 2 | Win (traversal), all | **bug (security)** | `validate_folder_name` rejects only `/`, `.` and `..`. `..\..\<uuid>\files` is a real traversal on Windows. `resolve()` normalises lexically and still finds it inside the root, so it reaches **another user's tree**. `:`, `*?"<>|`, reserved names and trailing dot/space also pass. | Shared `kubuno-storage::names::validate_component` (§3.3) |
| D3 | `src/services/files.rs:448-451, 487-490` (rename), `:699-703` (`create_with_bytes`) | 2 | all; worse on Win | **bug (security)** | No name validation at all: a rename to `../../<uuid>/files/x` overwrites another user's file **on every OS**. Office, paintsharp and build titles reach this path through IPC. | Same validator on every entry point (rename, create, IPC, archive extract, URL import) |
| D4 | `src/handlers/archive.rs:231-243`, `services/archives.rs:106` | 2 | Win | **bug (security)** | ZIP entry names come from raw `e.name()` and are split on `/` only; entries with `\` become traversal on Windows. | `enclosed_name()` + treat `\` as separator + validator |
| D5 | storage `naming.rs` + `files.rs:242-269`, `folders.rs:398-408` (SQL `=`) | 2 | Win, mac | **bug (data loss)** | Case- and normalisation-sensitive uniqueness over a case-insensitive filesystem: two DB rows share one physical file; deleting one deletes the other. | `name_key` column (NFC + casefold) with a unique index per folder; at minimum on non-Linux hosts. Long term: store blobs by id (§3.5) |
| D6 | `src/services/scanner.rs:54` | 2 | Win | bug | `WalkDir` on the raw base with `.flatten()` drops errors. Entries created through `\\?\` (trailing dot, `CON`, more than 260 chars) cannot be read back, so they look missing and get trashed. | Walk from the canonical base; abort the trash phase on any walk error |
| D7 | `src/services/files.rs:813`, `uploads.rs:39`, `import_url.rs:115-123` | 2 | Win vs others | risk | `sanitize_filename` uses `windows: cfg!(windows)`, so the accepted names depend on the server OS (a Linux server accepts `CON.txt`, which later breaks the Windows desktop sync and a migration to Windows). | Always `windows: true` (portable policy) |
| D8 | `src/services/watcher.rs:97` | 2 | mac | bug | FSEvents reports canonical paths (`/private/var/...`); `strip_prefix` against the configured base fails, so on-disk drops are never synced. | Canonicalise the base once, match on canonical paths |
| D9 | `src/services/watcher.rs:33-39` | 2 | Linux | risk | One recursive inotify watch over the whole store can exhaust `max_user_watches`; the watcher then stops for good. | Fallback to periodic scan; document the sysctl |
| D10 | `config.toml.example:9-10`; `settings.rs:95` (`/etc/kubuno/modules/drive/config`) | 1/7 | Win, mac | **bug (security)** / blocker on mac | The example (copied as the live config by core `install.rs:288-296`) uses `/var/lib/...`: on Windows `C:\var\lib` is writable by any local user; on macOS `_kubuno` cannot create it. On Windows, `C:\etc\kubuno\modules\drive\config` can be **planted by any local user** and is loaded *after* the CWD config, so it overrides settings. | Defaults from `KUBUNO_DATA_DIR`/`KUBUNO_CONFIG_DIR`; drop absolute paths from examples; never read `/etc/...` off Unix |
| D11 | `src/services/thumbnails.rs:113` | 3/5 | Win, mac | risk | Video thumbnails spawn `ffmpeg`, which is rarely on PATH there, so the feature is silently off; no `CREATE_NO_WINDOW`. | Document or ship ffmpeg; configurable path; `creation_flags` on Windows |

### 2.3 Other modules

| # | Repo | file:line | Cat. | OS | Severity | Finding | Suggested fix |
|---|---|---|---|---|---|---|---|
| M1 | chat, keestore, maps | `Cargo.toml` `kubuno-storage tag = "storage-v0.1.1"` | 5 | Win | **blocker** | The pinned tag predates `cd96bae`: every storage path is rejected on Windows, which breaks chat media, keestore save/delete and maps GPX upload. Other storage users (media, contacts, photos, drive) need the same bump. | Tag `storage-v0.1.2`, `bump_shared_crates.sh` |
| M2 | chat, keestore, maps, contacts, photos, media, p2pnas | `config.toml.example` (chat:22-23, keestore:34-35, maps:15-16, contacts:23-24, photos:9-10, media:9-13, p2pnas:8) | 1/7 | mac, Win | **blocker (mac)** / bug-security (Win) | Absolute `/var/lib/kubuno/modules/<id>/...` paths become the live config. macOS: `create_dir_all` fails, so the module dies at start (`chat main.rs:190`, `keestore main.rs:185`, `maps main.rs:173`, `media main.rs:167`, `p2pnas main.rs:182-186`). Windows: user data lands in `C:\var\lib`, readable and writable by every local user. | As D10 |
| M3 | mail, media, code, books, p2pnas | mail `settings.rs:85`; media `settings.rs:120-123` (`/var/kubuno/media` cache); code `settings.rs:65,70,71`; books `settings.rs:68-69`; p2pnas `settings.rs:103` | 1 | mac, Win | **blocker (mac)** / bug (Win) | Same defaults in code, ignoring `KUBUNO_DATA_DIR` (which core passes: `manager.rs:369`). stt and paintsharp already do it right and serve as the model. | `KUBUNO_DATA_DIR`-based defaults |
| M4 | 20 modules | `settings.rs` `/etc/kubuno/modules/<id>/config` (mail:89, chat:103, contacts:85, calendar:59, tasks:59, notes:73, app:64, assistant:159, books:73, code:77, flow:99, forms:88, forum:59, wiki:73, drive:95, media:142, office:93, paintsharp:85, photos:80, p2pnas:112) | 1/7 | Win | **bug (security)** | Drive-relative `C:\etc\...` can be planted by any local user and takes precedence over the CWD config (e.g. `mail.encryption_key`, storage paths). | `#[cfg(unix)]` or read only `KUBUNO_CONFIG_DIR`: ideally a shared `kubuno-modconfig` loader (§3.1) |
| M5 | forms, chat, keestore, maps | forms `settings.rs:78-79`, chat:87-88, keestore:71-72, maps:144-145 (`./data/...`) | 1 | all | risk | Relative defaults resolve against the module CWD, which is the **config** dir (`/etc/kubuno/modules/<id>` on Linux), so user data is written under `/etc`. | `KUBUNO_DATA_DIR` |
| M6 | code | `handlers/files.rs:178-224`, `services/file_tree.rs:17-21` | 1 | Win; mac under `/var`, `/tmp` | **blocker** | `safe_join` returns a `\\?\` path, but `strip_prefix` uses the non-canonical project root, so the API returns absolute paths and the next request is rejected: the tree cannot go below level 1. Relative paths also use `\` and do not match git status (`/`). | Canonical root once; `/`-joined relative paths |
| M7 | code | `handlers/projects.rs:43,131-133,220-229`, `handlers/files.rs:119-127` | 2 | Win | **bug (security)** | Project and file names are checked only for `/` and `..`. `join("C:\\x")` / `join("\\\\srv\\share")` replaces the base, so a user can create, rename or move anywhere on the disk. | Single `Component::Normal` + portable validator |
| M8 | code | `Cargo.toml` (`git2` `vendored-libgit2`, no `https`) | 5 | all | **blocker** | libgit2 is built without HTTPS while only `https://` remotes are accepted, so every clone fails. Enabling `https` brings a different TLS backend per OS (OpenSSL / SecureTransport / WinHTTP). | Enable `https` and test on all three OSes, or use a reqwest-based smart-HTTP transport |
| M9 | code | `handlers/projects.rs:142-146` | 2 | Win, mac | risk | Checkout on Windows: protectNTFS refuses `aux.c`, `:` and trailing dots; the 260-char limit; symlinks become text files; partial folder left on failure. | `core.longpaths`, cleanup on failure, clear errors |
| M10 | maps | `build_kbpkg.sh:110-116`, `.gitignore:56`, `cosmos_service.rs:180-182` | 4/1 | all | **blocker (3D view)** | The `cosmos/textures` assets (85 MB) are never packaged, and the lookup only checks `/usr/lib/...`, CWD and `CARGO_MANIFEST_DIR`. | Look under `KUBUNO_MODULE_DIR/cosmos`; ship them or download on demand |
| M11 | maps | `cosmos_service.rs:144, 197` | 2/1 | Win | risk | `rename` over a cached texture fails while it is open (500 on concurrent first hits); cache default `/var/lib`. | Treat "exists" as success; `KUBUNO_DATA_DIR` |
| M12 | mail | `server/deliver.rs:288-292`, `services/sent_copy.rs:44-50` | 2 | Win | **bug** | The attachment-name sanitiser misses `: * ? " < > \|` and trailing dot/space. `:` creates an ADS; inbound mail is stored pointing at a missing file; sending a copy with such an attachment **fails**. `sent_copy` also builds paths with `format!("{}/{}_{}")`. | Shared portable sanitiser + `Path::join` |
| M13 | mail | `server/deliver.rs:290` (`take(180)` chars) | 2 | Linux, mac | bug | 180 chars can be more than 255 bytes (ext4/APFS limit: e.g. CJK), so long non-ASCII names are dropped. | Cap at about 200 UTF-8 bytes on a char boundary |
| M14 | mail | `server/config.rs:733-737`; `services/smtp_service.rs:140-146` | 7 | all (Win worst) | bug / risk | Hostname from `$HOSTNAME`: systemd does not export it and Windows uses `COMPUTERNAME`, so EHLO, Message-ID and banner always say `kubuno.local`. Lettre uses the NetBIOS name (Windows) or `*.local` (macOS) for HELO. | `hostname::get()` with the configured FQDN first; set `hello_name` |
| M15 | mail | `server/mod.rs:263` | 7 | Win | risk | Binding `::` is IPv6-only on Windows (dual-stack on Linux/macOS), so IPv4 MTAs are refused. Firewalls block unsigned listeners on Windows/macOS. | Bind v4 + v6 or clear `IPV6_V6ONLY` (socket2); document firewall rules |
| M16 | mail | `services/dns_opts.rs:48-49`; `server/outbound.rs:954` vs `services/imap_service.rs:101` | 7 | mac / all | risk | `/etc/resolv.conf` ignores scutil split-DNS on macOS; outbound TLS trusts webpki-roots while IMAP uses the OS store, so a corporate CA works on one path only. | Configurable resolver; one TLS trust policy |
| M17 | mail | `module.toml:186-187,249,355,403,465,517,528`, `server/mod.rs:247-248` | 7 | Win, mac | cosmetic | `CAP_NET_BIND_SERVICE`, systemd and `/etc/letsencrypt` wording. | Neutral wording |
| M18 | p2pnas | `crates/store/src/identity.rs:76-79,101-104,123-128`, `backup.rs:241` | 3 | Win | **bug (security)** | The node master key is written with no ACL on Windows and the permission check is skipped (with `C:\var\lib` it is readable by all users). | `write_private` from `kubuno-paths` |
| M19 | p2pnas | `crates/store/src/chunkstore.rs:19-22` | 2 | Win, mac | bug | Shard ids accept uppercase hex. On a case-insensitive filesystem a peer can overwrite an existing lowercase shard that the DB does not know about. | Accept `[0-9a-f]` only |
| M20 | p2pnas | `server/main.rs:308-310`, `settings.rs:104`, mDNS 5353 | 7 | Win, mac | bug (Win) | No firewall rule, so peers and mDNS are blocked by default on Windows. | Document; optionally let the core register module firewall rules on Windows |
| M21 | p2pnas | `crates/store/src/backup.rs:220-224` | 2 | Win | risk | `write_atomic` renames over `manifest.db` while SQLite handles are open. | Close pool, then rename; retry |
| M22 | media | `services/ffmpeg.rs:22,70,72,99`, `ffprobe.rs:111`, `covers.rs:59` | 3/5 | Win, mac | risk | ffmpeg/ffprobe are not shipped (silently off on Windows and macOS); `to_str().unwrap()` panics on non-UTF-8 paths (and breaks the zero-`unwrap` rule). | Configurable binary path + health check; `.arg(OsStr)`; `CREATE_NO_WINDOW` |
| M23 | media | `workers/watcher.rs:233`; `scan.rs:180`, `watcher.rs:210` | 2 | mac / Linux | bug / risk | Exact `PathBuf` match versus canonical FSEvents paths, so new files are never indexed on macOS; `to_string_lossy` loses non-UTF-8 names on Linux. | Canonicalise; skip and log non-UTF-8 names |
| M24 | media | `handlers/libraries.rs:147,180` | 1/7 | Win / mac | risk | `format!("{}/{}")` on a stored drive path; macOS TCC blocks daemons from `~/Movies` and removable volumes. | `Path::join`; document Full Disk Access |
| M25 | stt | `src/main.rs:556-563` | 2 | all (Win worse) | risk | `DELETE /admin/models/:engine/:model` puts the URL-decoded `model` (`%2F`, `%5C`) into a path, so an admin can delete arbitrary `*.bin` files or directories. | Accept catalog ids only |
| M26 | stt | `Cargo.toml` whisper-rs-sys (cmake) | 5 | Linux, Win | risk *(unverified)* | `GGML_NATIVE` may emit host-CPU instructions (crash on older CPUs); OpenMP may add `libgomp`. | `GGML_NATIVE=OFF`; `ldd`/`dumpbin` check in CI |
| M27 | notes, app, flow, wiki, build, paintsharp | `content_files.rs` (notes:51-54, app:108, flow:52, wiki:83, build:56, paintsharp:73) | 2 | all, Win differs | bug | `Path::new(title).file_stem()` on a user title: `v1.2 notes` becomes `v1`, `a/b` loses `a/`, and on Windows `\` and `C:` are parsed too, so the same title gives different file names per server OS. Titles with `:?*"<>\|` then fail in drive on Windows. | String-based `kb_file_name` in a shared helper + portable sanitiser |
| M28 | chat, forms, paintsharp | chat `handlers/media.rs:67-71`; forms `handlers/uploads.rs:72-76,198-201`; paintsharp `handlers/video.rs:251-256,315` | 2 | Win | risk | The stored extension comes from the user's file name (`x.a:b` creates an ADS; `"?` give a 500). | Whitelist `[a-z0-9]{1,10}` else `bin` |
| M29 | books | `handlers/libraries.rs:118`; `services/decode.rs:33,58` | 1 | Win | risk | `format!("{}/{}")` path stored in the DB; only `/` is trimmed, so a Windows absolute path replaces the base on join. | `Path::join`; normalise both separators |
| M30 | mail, contacts, code, books, forms, paintsharp, drive | absolute OS paths stored in DB (mail `deliver.rs:306`, contacts `avatar_service.rs:52`, code projects/extensions, books cache, forms `uploads.rs:88`, paintsharp `video.rs:256,316`, `pdf.rs:295`, drive `storage_path` with `\`) | 1 | cross-OS | risk | Backups and DBs cannot move between OSes or data dirs (the "export to macOS" scenario). | Store `/`-joined paths relative to the data dir |
| M31 | office | `converters/docx/write/field.rs:141` | 7 | all | risk | DATE fields use `Local::now()` (server time zone). | User time zone or UTC |
| M32 | office | `field.rs:441-447`, `xlsx/tests.rs:229` (`/home/martinien/...`), ignored tests in `/tmp` | 6 | Win | cosmetic | Unix paths and `file://` URLs in tests. | `temp_dir()`, `Url::from_file_path` |
| M33 | flow, office | `rquickjs-sys` (C) | 5 | Win | risk *(unverified)* | C code built with MSVC only at tag time. | Covered by the CI matrix |
| M34 | code | `services/extensions.rs:56-70,111-113` | 2 | Win | cosmetic | `validate_segment` allows `CON`/`aux` and trailing dots. | Shared validator |
| M35 | maintainers' hygiene (out of scope, security) | `calendar/tint.mjs` (~17-18), `_tools/publish_all.sh:34` | - | - | **security** | A committed script with local admin credentials in clear text, and a literal npm token hard-coded in the publish script (it overrides the `NPM_TOKEN:?` check). Values are not reproduced here. | Remove them; rotate both secrets; purge history if the repos are public |

Verified clean (evidence of absence):
- Most modules use no `cfg(unix)`, `std::os::unix`, `libc`, `nix`, Unix signals, `/proc`, Unix sockets, uid/gid,
  sd_notify or spawn. The exceptions are p2pnas (gated, with fallbacks) and media, drive and stt (ffmpeg spawn).
- calendar, tasks and flow use embedded `chrono-tz` (no system TZ, no `/usr/share/zoneinfo`).
- core `health/disk.rs` has a proper Windows branch (`GetDiskFreeSpaceExW`).
- Core backups are in-process, with no `pg_dump`.
- core `entrypoint_path` handles the `.exe` suffix.
- The VSIX extraction in code rejects zip-slip, drive prefixes and symlinks.
- The frontend builds (vite, `tsc -b`) are OS-neutral.

### 2.4 Packaging and tooling

| # | Repo | file:line | Cat. | OS | Severity | Finding | Suggested fix |
|---|---|---|---|---|---|---|---|
| P1 | all modules | `build_kbpkg.sh` header, `build.yml`, `dist.yml`, CLAUDE.md §5 | 4 | all | cosmetic (doc) | "Identical Linux/Windows/macOS" is true of the layout and the core's pure-Rust unpacking, not of the content: each package has a per-OS native binary, Linux ones depend on the distro (C29), and CRLF changes text files (C30). | Reword; embed the target triple (C22) |
| P2 | all modules | `build_kbpkg.sh:67` | 4 | Win (local) | bug | `uname -s` = `MINGW64_NT...` is not mapped, so the script stops locally without `OS=windows`. | Map `mingw*\|msys*\|cygwin*` to windows |
| P3 | all modules | `build_kbpkg.sh:121` | 4 | mac | risk | `sha256sum` is not guaranteed on stock macOS (`set -e` aborts). | Fall back to `shasum -a 256` |
| P4 | all modules | `build_kbpkg.sh:136-138` | 4 | Win | risk | The `Compress-Archive` fallback gets an untranslated MSYS path, and PS 5.1 writes `\` entry names; 7z (which masks it today) sets no exec bit (irrelevant on Windows). | `cygpath -w` or require 7z |
| P5 | all modules | `build_kbpkg.sh:153-157` | 4 | Win, mac | cosmetic | `--install` uses `sudo` + `systemctl`. | Per-OS branch |
| P6 | modules | `Makefile` `deb`/`install` | 4 | all | cosmetic | They call the removed `build_deb.sh`. | Point them at `build_kbpkg.sh`; delete `notes/build_bundle.sh` |
| P7 | stt, p2pnas | `build.yml` `RUSTFLAGS -L /usr/local/lib`; Strawberry Perl workaround | 4 | Linux / Win | cosmetic / risk | Leftovers; p2pnas vendored OpenSSL needs Perl on Windows. | Clean up; prefer rustls |
| P8 | _tools | `release.sh:45,56`, `bump_shared_crates.sh:23`, `release_sync.sh:41-47,56`, `extract_module.sh:65,85`, `bump_npm_floors.sh:11`, `add_topics.sh:6` | 4 | mac | **blocker (for releasing from the Mac)** | GNU `sed -i` / `0,/re/`, bash-4 `mapfile` / `declare -A` (macOS ships bash 3.2). | Port to Python, or require `gsed` + brew bash |
| P9 | _tools | `release.sh:71-88`, `bump_npm_floors.sh:27,35`, `check_versions.py` | 4 | Win | bug | Python text-mode writes CRLF; `open()` without an encoding uses cp1252 (corrupts non-ASCII). `check_versions.py:206` crashes on `dpkg-query` FileNotFoundError off Debian. | `encoding='utf-8', newline='\n'`; `shutil.which` guard |
| P10 | _tools | `publish_all.sh:29`, `normalize_branches.sh:6`, `deploy_local.sh:16-46` | 1/4 | Win, mac | bug / cosmetic | `/home/martinien/projects` hard-coded; `deploy_local.sh` is Linux only. | Derive from `$(dirname "$0")/..`; document |

## 3. Cross-cutting fixes (do once, in shared crates)

### 3.1 `kubuno-paths` (new crate in `core`, tag `paths-v0.1.0`)

One source of truth for platform locations, used by core, the CLI and every module.

| Function | Linux | Windows | macOS |
|---|---|---|---|
| `config_dir()` | `/etc/kubuno` | `%ProgramData%\Kubuno` | `/Library/Application Support/Kubuno` |
| `state_dir()` (data.key, setup-token, TLS, initial password) | `/var/lib/kubuno` | `%ProgramData%\Kubuno\state` | `/Library/Application Support/Kubuno/state` |
| `data_dir()` (files, modules data, db, themes, exports) | `/var/lib/kubuno` | `%ProgramData%\Kubuno\data` | `/Library/Application Support/Kubuno/data` |
| `modules_store()` | `/var/lib/kubuno/modules-store` | `%ProgramData%\Kubuno\modules-store` | `.../Kubuno/modules-store` |
| `log_dir()` | `/var/log/kubuno` | `%ProgramData%\Kubuno\logs` | `/Library/Logs/Kubuno` |
| `backup_dir()` | `/var/backups/kubuno` | `%ProgramData%\Kubuno\backups` | `.../Kubuno/backups` |

Rules:
- Every value can be overridden by `KV__...`.
- `%ProgramData%` comes from `SHGetKnownFolderPath(FOLDERID_ProgramData)` or the env var, **never** from
  `HOME`/`USERPROFILE`.
- Paths are **never inferred from `is_dir()`**: this fixes C1. A one-time migration reads the legacy location if the
  new one is empty, then logs it.
- The crate also provides:
  - `write_private(path, bytes)`: `0600` on Unix; on Windows a protected DACL (SYSTEM, Administrators, the service
    SID). Used by C7 and M18.
  - `harden_dir()`: called by the installers.
  - `module_config_sources()`: replaces the per-module `/etc/kubuno/modules/<id>/config` (M4) by
    `KUBUNO_CONFIG_DIR/config` only.
  - `module_data_dir()`: reads `KUBUNO_DATA_DIR` and replaces the absolute defaults of M2, M3 and M5.
- The settings defaults in core (`settings.rs`, `network/store.rs`, policies, setup wizard) and the installers
  (`build_windows.sh`, `build_macos.sh`) are then derived from it, so the installer no longer has to list each
  override (C3, C4).

### 3.2 Path safety module (`kubuno-storage::safe_path`, tag `storage-v0.2.0`)

- `Root::new(path)`: canonicalise **once** (`std::fs::canonicalize`) and keep a `dunce::simplified` copy for display
  and logs.
- `Root::resolve(key)`:
  - `key` is a `/`-separated relative key. Reject anything containing `\`, a drive prefix, a root, or `.`/`..`.
  - Validate each component with §3.3.
  - Push the components, then do an `starts_with` check on canonical paths.
- `Root::relative_key(abs)` is the inverse, returning a `/`-joined key. Use it for `list()` (C8), drive's scanner
  (D1/D6), code's `file_tree` (M6) and the watchers (D8, M23).
- Storage keys stored in DBs are always `/`-joined and relative (C12, D1, M30).
- Provide `fs_retry()` for rename/delete on Windows sharing violations (C13, M11, M21, C23).

### 3.3 Portable name policy (`kubuno-storage::names`)

`validate_component(name) -> Result<(), NameError>` and `sanitize_component(name) -> String`. They apply on
**every OS** (so a Linux server never accepts what a Windows server or a Windows desktop sync would choke on):

- Reject empty names, `.` and `..`.
- Reject `/ \ : * ? " < > |` and control chars U+0000-U+001F.
- Reject trailing `.` or space.
- Reject reserved device names, case-insensitive and with or without an extension: `CON PRN AUX NUL COM1-9 LPT1-9`
  plus `COM¹²³`/`LPT¹²³`.
- Cap at 255 UTF-8 bytes per component (this covers APFS and ext4, and NTFS's 255 UTF-16 units).

This fixes D2-D4, D7, C9, M7, M12, M27, M28 and M34. The known desktop sync bug (`:` in server names) disappears for
new names. Existing names need a one-off migration report (list them, offer a rename) rather than a silent change.

### 3.4 Case-folding and normalisation rules

- Normalise every user-supplied name to **NFC** on input (`unicode-normalization`, already a dependency of
  `kubuno-db`).
- `name_key(name) = casefold(NFC(name))`. Uniqueness inside a folder is checked on `name_key` (drive/folders unique
  index; `unique_file_name`/`unique_dir_name` take a comparator) on **every OS**, because a Linux server's data must
  stay valid when moved to Windows or macOS and when synced by the desktop clients. The display name keeps its
  case. Fixes C11, D5 and M19.

### 3.5 Longer term: content-addressed or id-addressed blobs for drive

The on-disk tree mirrors user names, which is the root of D1, D5, D6 and most Windows name issues. Storing blobs as
`{owner}/{file_id}` (as photos, chat and keestore already do) makes the filesystem name policy irrelevant. Treat this
as a separate design decision: the "browse the files on disk" feature and the scanner depend on the mirror.

### 3.6 Other shared helpers

- `kubuno-seccomp`: aarch64 support; a Windows Job Object `ACTIVE_PROCESS=1` backend; an honest doc comment (C14).
- Core module supervisor: Windows Job Object `KILL_ON_JOB_CLOSE`; graceful stop (C16).
- A TLS policy for every repo: reqwest `default-features = false` + rustls with platform roots (C29, M16).
- `.gitattributes` template in `_tools/packaging`, copied to every repo (C30).

## 4. Fix lots, in order

Each lot ends with build + tests on all three OSes (manual until §5 exists) and a CHANGELOG entry per repo touched.

| Lot | Repos | Content | Findings |
|---|---|---|---|
| **L0 - immediate** | core, chat, keestore, maps, contacts, media, photos, drive | Tag `storage-v0.1.2` (contains `cd96bae`) and bump every storage user. Add `.gitattributes` to **all** repos *before* the next Windows tag build. Rotate the two leaked secrets. | M1, C30, M35 |
| **L1 - core platform layer** | core | `kubuno-paths` crate; explicit `state_dir` (+ migration of an existing `data.key`/`setup-token`); settings defaults, CLI config lookup, setup wizard; `write_private` with Windows DACL; installers simplified (WinSW XML, plist with `NumberOfFiles`, macOS layout under `/Library`); `.kbpkg` target check; stop-before-update; Windows ctrl_close/shutdown; Job Object for children; CLI messages per OS; `/tmp` in remote connectors and tests; CLI `db:*` in-process. | C1-C7, C15-C28 |
| **L2 - storage safety** | core (`kubuno-storage` 0.2.0) | `safe_path` (canonical root once, `/` keys, `list()` relative), `names` (portable validator + sanitiser), `name_key`, `fs_retry`; `path.rs` component-wise join. | C8-C13 |
| **L3 - drive** | drive | Adopt L2 everywhere (folders, rename, `create_with_bytes`, IPC, archive extract, URL import); `name_key` unique index + migration report of existing conflicting or illegal names; scanner on canonical keys with a no-mass-trash guard and abort on walk errors; watcher on canonical paths; `KUBUNO_DATA_DIR` defaults, examples without absolute paths, no `/etc` lookup off Unix; `sanitize_filename` `windows: true`; ffmpeg optional and documented. **Migration:** rewrite `storage_path` values containing `\` to `/` (Windows installs only). | D1-D11 |
| **L4 - modules with user files** | code, mail, media, p2pnas, books, forms, chat, paintsharp, maps | code: canonical tree, name validation, git https; mail: attachment sanitiser, byte cap, hostname/HELO, bind; media: canonical watcher, `OsStr` args, ffmpeg config; p2pnas: key ACL, lowercase ids, rename; maps: cosmos assets + `KUBUNO_MODULE_DIR`; extension whitelists. | M2-M3, M6-M16, M18-M24, M28-M29 |
| **L5 - all remaining modules** | the other 20 module repos | Shared module bootstrap: `kubuno-paths::module_config_sources()` / `module_data_dir()` (replaces the `/etc/kubuno/modules/<id>/config` and `./data` defaults); string-based `kb_file_name`; reqwest to rustls; `build_kbpkg.sh` fixes (MINGW mapping, `shasum`, `cygpath`, `--install` per OS, target triple into `module.toml`, reworded header) from `_tools/packaging`. | M4-M5, M17, M25-M27, M30-M34, P1-P7 |
| **L6 - tooling** | _tools | Python-ise the sed/bash-4 parts, UTF-8 + LF writes, `dpkg-query` guard, no hard-coded home. | P8-P10 |

## 5. CI matrix proposal

GitHub bills macOS minutes at 10x Linux and Windows at 2x (private repos; the Kubuno repos are public, where
standard runners are free, but concurrency is limited per organisation). The proposal keeps macOS off the per-push
path where it adds little.

**core and shared crates** (`checks.yml`):

| Job | ubuntu-latest | windows-latest | macos-latest (arm64) |
|---|---|---|---|
| `cargo clippy --all-targets -D warnings` | every push/PR | every push/PR | PR to `main` + nightly |
| `cargo test --lib` (core + shared crates) | every push/PR | every push/PR | PR to `main` + nightly |
| integration tests with PostgreSQL service | every push/PR | nightly (PG via `ikalnytskyi/action-setup-postgres`) | weekly |
| frontend `tsc -b` + vitest | every push/PR | - | - |
| packaging dry-run (`build_windows.sh` / `build_macos.sh` without release) | - | nightly | nightly |
| `x86_64-apple-darwin` and `aarch64-unknown-linux-gnu` cross-check (`cargo check --target`) | nightly | - | - |

**Modules** (one reusable workflow in `kubuno/.github`, called by each repo):

- Every push/PR: ubuntu `clippy` + **`cargo test`** (none runs today) + frontend build.
- Every PR to `main`: `windows-latest` `cargo build --release` + `cargo test` (catches `\\?\`, CRLF and MSVC C-deps
  early).
- Nightly + before tag: `macos-latest` `cargo build` + `cargo test`.
- Tag: unchanged `build.yml`/`dist.yml`, plus a **smoke test** of the packaged module on each OS: unzip the `.kbpkg`
  and run the binary with `--version` (or a `--self-check` flag) to catch missing dynamic libs (`ldd` / `otool -L` /
  `dumpbin` listing). Also run `grep -rl $'\r' migrations/` and fail on a hit.
- Linux portability: build the Linux `.kbpkg` in a `manylinux_2_28`-style container, or switch fully to rustls and
  check `objdump -T` for the max `GLIBC_` symbol.
- Guard: a `swatinem/rust-cache` key per OS, and `fetch-depth: 0` kept only where the build id needs it.

## 6. Re-tags and bumps

| Shared artefact | Change | New tag | Consumers to bump |
|---|---|---|---|
| `kubuno-storage` | `cd96bae` (already on main) | `storage-v0.1.2` | chat, keestore, maps, contacts, media, photos, drive (+ books, code, forms, paintsharp if they depend on it: check with `check_versions.py`) |
| `kubuno-storage` | `safe_path`, `names`, `name_key`, component-wise `path.rs` (API change) | `storage-v0.2.0` | same list; drive first |
| `kubuno-paths` (new) | platform dirs, `write_private`, module config/data helpers | `paths-v0.1.0` | core, then every module (L5) |
| `kubuno-seccomp` | aarch64 + Windows Job Object + doc | `seccomp-v0.2.0` | every module calling `lock_down_process_execution` |
| `kubuno-db` | none required (CRLF is handled by `.gitattributes`); optionally a test asserting no `\r` in embedded migrations | - | - |
| `kubuno-drive` client | none, unless D3 changes the IPC create contract (name rejection now an error) | `drive-v0.1.x` if the error type changes | flow, notes, office, paintsharp |
| npm `@kubuno/*` | none | - | - |

Order of pushes: shared tags first (no more than 3 tags per `git push`), then `bump_shared_crates.sh`, then the
module releases. Every repo touched needs its `CHANGELOG.md` `[Unreleased]` entry.
