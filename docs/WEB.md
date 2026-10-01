# Kubuno Core Web and its modules in Visual Studio

Phases 1 and 2 of the web workstream (product owner, 2026-10-01): **Kubuno Core Web** - the web server, the `core`
repository, as opposed to **Kubuno Core Desktop** (`desktop/windows`: the shell, `kubuno_ui`, the desktop apps) - and the **web modules** of the
polyrepo are edited, built, tested and run from Visual Studio 2026, the way the desktop applications are
(docs/RSPROJ.md, "Cargo workspaces"). Remote Linux builds, publishing and remote debugging are later phases.

This is the `Kubuno.Web` layer of docs/ARCHITECTURE.md ("Layers (as built)"): it references Core and Rust only, and
reuses the `.rsproj` project type, `Kubuno.Rust.Sdk`, rust-analyzer, the native debugger and the Rust Test Explorer
adapter untouched. The frontends are Visual Studio's own JavaScript project type (`.esproj`).

## 1. What is where

| Piece | Assembly / file | Role |
|---|---|---|
| Pure logic | `src/Web/Kubuno.Web.Logic` (netstandard2.0) | development database guard, dev core layout and environment, `module.toml`, deployment, `.kbpkg`, node_modules from another OS, solution generation, template tokens, version audit. Tested by `tests/Kubuno.Web.Tests` (plain `dotnet test`). |
| MSBuild SDK | `sdk/Kubuno.Web.Sdk` (NuGet `Kubuno.Web.Sdk` 1.0.0) | an **additional** SDK a project names next to its main one; tasks in `src/Web/Kubuno.Web.MSBuild.Tasks` (net472 + net10.0) wrap the logic, so the command line and Visual Studio share one implementation. |
| F5 | `src/Web/Kubuno.Web.ProjectSystem` | `KubunoWebDebugLaunchProvider`, the CPS debugger `KubunoWebDebugger`. |
| Commands, template | `src/Web/Kubuno.Web` | Tools menu commands, the "Kubuno Core Web Module" template (moved here from the Rust layer, same TemplateID). |
| Template wizard | `src/Web/Kubuno.Web.TemplateWizard` | tokens of the module template, frontend project added to the solution. |

## 2. Solutions

**Tools > Kubuno Core Web: Generate Solution** (on the repository of the active document or open solution, else a folder
picker) writes, for a Kubuno repository - recognized by its manifests, no `cargo metadata` needed:

- one `.rsproj` next to each Cargo package (`Kubuno.Rust.Sdk/1.1.0`). The package F5 starts - `kubuno-core` for the
  core, the module's package for a module - also names `<Sdk Name="Kubuno.Web.Sdk" Version="1.0.0" />` and its
  `<KubunoWebRole>` (`Core` or `Module`). The core's projects build the workspace once
  (`<CargoBuildScope>Workspace</CargoBuildScope>`); `kubuno-core.rsproj` picks `<CargoBin>kubuno-core</CargoBin>`
  (the crate also builds the `kubuno` CLI). Libraries go to a `/Libraries/` solution folder;
- `frontend\<name>.esproj` - `kubuno-frontend.esproj` for the core, `<id>-frontend.esproj` for a module -
  `Microsoft.VisualStudio.JavaScript.Sdk/1.0.6887863` plus `Kubuno.Web.Sdk` (`KubunoWebRole` `CoreFrontend` or
  `ModuleFrontend`), and its `frontend\.kubuno\launch.json` (Edge and Chrome configurations of Visual Studio's script
  debugger; `.vscode` is ignored by the repositories' `.gitignore`, so `LaunchJsonFolder` points at `.kubuno`);
- `Kubuno.Core.Web.slnx` / `Kubuno.<Module>.slnx` (an existing `Kubuno.*.slnx` is merged into: only missing projects are
  added). The `.esproj` is mapped to `AnyCPU` (`<Platform Project="AnyCPU" />`); a module's backend has a
  `<BuildDependency>` on its frontend, so F5 on the backend builds the bundle it deploys;
- `<solution>.slnLaunch`, Visual Studio's shared multi-project launch profiles: "Kubuno Core Web (serveur)" and
  "Kubuno Core Web (serveur + Vite)" for the core, "<Module> (Kubuno Core Web + navigateur)" for a module (only
  programs get a profile, never a library crate);
- the **startup project**, set deliberately once the solution is open (Visual Studio keeps it per user, not in the
  `.slnx`): `kubuno-core`, or the module's backend - never a library; programs also come first in the `.slnx`, so a
  fresh `.vs` picks the same one. A library `.rsproj` cannot be started at all (Start is disabled for it by the Rust
  layer's launch provider, instead of a modal "executable does not exist" error);
- Solution Explorer tells the projects apart: the core's server project shows a web server icon, a module's backend a
  web application icon (capabilities `KubunoWebCore`/`KubunoWebModule` from Kubuno.Web.Sdk, same project type);
- the solution's SDK feed: `NuGet.Config` and `.kubuno\sdk-feed` with `Kubuno.Rust.Sdk` and `Kubuno.Web.Sdk` (the
  Rust layer's `SdkFeedDistribution` now copies every `Kubuno.*Sdk.*.nupkg` the extension bundles).

As with "Generate Visual Studio Projects", a project file that exists is never rewritten. The generated files are
not committed by the tooling: they are small and stable enough to commit (the `.slnx`, the `.rsproj`/`.esproj`, the
launch profiles), but that is the repository owner's decision - the `.user` files and `obj\` must stay out (the
core's `.gitignore` has no `*.user` / `obj/` entry yet).

**Layout.** Every repository gets the same four solution folders: **Server** (the program F5 starts: `kubuno-core`, a
module's backend), **Libraries** (the other crates), **Frontend** (the app: the core's host, a module's bundle) and
**Packages** (npm packages). Visual Studio lists solution folders alphabetically; in the `.slnx` the program comes
first. In a multi-repository solution they sit under `/<repository>/` (`/core/Server/`, `/drive/Frontend/`...).

**Sub-modules.** The generator reads the shapes generically: the root package and every `[workspace] members` entry
(`dir/*` globs included), the crates of git submodules (`.gitmodules`), the app of `frontend/` (or of the root), and
every package of `frontend/packages/*` or `packages/*`. Each is a project of its own. Inventory of `Z:\src`
(2026-10-01, `WebSolutionGenerator.Describe`): the 24 modules have one backend crate and one frontend each, except
**p2pnas** (a Cargo workspace: `kubuno-p2pnas` in Server, `p2pnas-core`, `p2pnas-store`, `p2pnas-p2p` in Libraries)
and **stt** (backend only); no module has git submodules or several frontends; **office** has no separate sub-app
crates or frontends (its apps are one bundle). The core: `kubuno-core` (Server), 5 libraries, `kubuno-frontend`
(Frontend), `@kubuno/ui`, `@kubuno/sdk`, `@kubuno/drive` (Packages). The command prints this table per repository in the
"Kubuno" Output pane.

**Packages of Kubuno Core Web.** `@kubuno/ui`, `@kubuno/sdk` and `@kubuno/drive` are built from the host app's sources
(`core/frontend/packages/build.sh`): `packages/<id>` is the build/publish wrapper. Each package project
(`packages/<id>/kubuno-<id>.esproj`, role `Package`) shows its real sources as linked items (`frontend/src/ui`,
`src/sdk`, `src/drive`), which the host app project no longer shows; its build runs
`Kubuno.Web.Sdk/tools/kubuno-packages.mjs` with the overlay environment: one emit of the host's declarations
(`tsc -p tsconfig.emit.json --newLine lf`, reused by the next package of the build), the package's type tree as
build.sh assembles it and, for `@kubuno/ui`, the ESM library (with `vite.uilib.config.ts`: the package's own
`vite.config.ts` decides "external" from the shape of the module id and bundles nothing on Windows) - all into
`packages/<id>/obj/package`. The committed `packages/<id>/types` and `dist` are never rewritten from Windows:
regenerating and publishing the npm packages stays `build.sh` / `_tools/publish_all.sh`, run by the developer. Build
order: ui, sdk, drive, then the host app (`BuildDependency`).

**Module isolation** (docs/VIEWS-SPEC.md, "Module isolation"). Modules never import each other nor the core's
sources; they build alone against the published `@kubuno/*` packages and the shared crates' git tags. The generator
never writes a reference between two repositories (build dependencies only join projects of the same repository,
checked by `ModuleIsolation.EnsureIsolated`), and refuses a multi-repository solution in which a module reaches
outside itself: a Cargo `path` dependency, an npm `file:`/`link:` dependency, a `tsconfig`/Vite path leaving the
module. A single-repository solution lists such references as warnings in the Output pane. None exists in `Z:\src`
today.
**Tools > Kubuno Core Web: Generate Multi-Repository Solution...** lists the Kubuno repositories next to the current one
(the polyrepo folder, `Z:\src`), with the core and the current module checked, and writes `Kubuno.Web.slnx` in that
folder: one solution folder per repository (`/core/`, `/core/Libraries/`, `/drive/`...), the same project files, one
launch profile per repository. Visual Studio's Git tooling follows every repository a solution's projects live in
(multi-repository support), so branches, changes and commits of the core and the chosen modules sit side by side.

## 3. Building on Windows

**Rust.** The core and the modules build with the MSVC toolchain from Visual Studio (Build, Rebuild, Clean, Error
List) through `Kubuno.Rust.Sdk`. The core's Linux-only code is already gated (`kubuno-seccomp` is a no-op off Linux,
`statvfs` has a Windows twin, the `chown`s are `cfg(unix)`); nothing is excluded per configuration. A package with a
`.sqlx` query cache builds with `SQLX_OFFLINE=true`: `Kubuno.Web.Sdk`'s `KubunoSqlxOffline` target sets it for the
build process when the variable is not set (today only the core has a `.sqlx` folder, and no query macro uses it).
On the share, give cargo a local target directory (`setx CARGO_TARGET_DIR C:\kubuno-build\...`), as for the desktop
apps.

**Frontends.** `npm run build` runs on build (`--sourcemap` in Debug), through `obj\kubuno-npm.cmd`, which
`Kubuno.Web.Sdk`'s `KubunoPrepareNode` writes before the build:

- **Node.js**: the one on PATH, else Visual Studio's own (`MSBuild\Microsoft\VisualStudio\NodeJs`, Node 24 - this
  machine has no other);
- **node_modules installed by Linux.** The checkouts live on the Linux server and are opened from Windows through
  the share, so `node_modules` holds the Linux builds of the native packages (`@rolldown/binding-linux-x64-gnu`,
  `lightningcss-linux-x64-gnu`, `@tailwindcss/oxide-linux-x64-gnu`, `@napi-rs/canvas-linux-x64-gnu`) and `.bin`
  entries that are Linux symbolic links (a share shows them as plain copies of their target). An `npm install` from
  Windows would replace the Linux packages and break the server's builds, so the tree is **never touched**. Instead
  an overlay folder, `%LOCALAPPDATA%\Kubuno\node-overlay\<hash of the package list>`, receives the Windows
  counterparts at the same versions (one `npm install` of them as optional dependencies), found through `NODE_PATH`
  (their loaders use `require`, which consults `NODE_PATH` after the `node_modules` chain), and a `.cmd` shim per
  `.bin` command, generated from the packages' `bin` fields, goes on PATH;
- **npm install**: the JavaScript SDK's `RunNpmInstall` is replaced: an existing `node_modules` is never reinstalled
  by a build (run `npm ci` yourself after a lock change); a missing one gets `npm ci`. `npm audit` is off by default
  (`ShouldRunNpmAudit`).

The `.esproj` keeps `node_modules` out of its tree (the JavaScript SDK's default) and `Kubuno.Web.Sdk` adds `dist`
and `*.tsbuildinfo`; a module's `.rsproj` excludes `frontend/**`, which its own `.esproj` shows. TypeScript
IntelliSense is Visual Studio's own (it reads `tsconfig.json`, including the `@ui` paths mapping).

## 4. Tests

The Rust tests are the Rust layer's Test Explorer adapter (solution mode lists the `.rsproj` manifests; the core's
workspace-scope projects give one container, the workspace root). Measured on the core: **1013 tests listed, 1000
passed, 13 failed** - the 13 are Windows failures of the core itself (11 `kubuno-storage` path tests building
`/var/...`-style paths, `backup::policy` and `data_export::policy` asserting that `/var/...` is absolute). The
69-test count of the first session was a discovery run while the very first `cargo test --no-run` of the workspace was
still building (14 minutes cold); with the test programs built, discovery lists them all. The core's integration tests
skip themselves without `KUBUNO_TEST_DATABASE_URL`.

The core frontend's vitest specs are **not** declared to Test Explorer: tried (`JavaScriptTestFramework=Vitest`),
Visual Studio's JavaScript test adapter runs in the same .NET Framework test host as the Rust adapter and fails to
load ("Failed to parse msbuild output ... FlushAsync ... System.Text.Json 9.0.0.0": the two adapters' System.Text.Json
closures collide), and it needs a Node.js on the PATH of Visual Studio's test host (Visual Studio's bundled Node.js is
not used there). Isolating the Rust adapter's dependency closure is a separate task; until then the specs run with
`npm run test` in the frontend folder.
## 5. The development database

A core runs its SQL migrations when it starts. A core started from Visual Studio therefore must never reach the live
database:

- the connection string comes **only** from the `KUBUNO_DEV_DATABASE_URL` environment variable (or, discouraged
  because it writes a password next to the sources, the project's per-user Debug environment) - never from a file of
  the repository, never from the server's configuration;
- it must name a **separate development database** on the team's PostgreSQL server (192.168.1.220), reached through
  an SSH tunnel (below): `postgres://kubuno:<password>@localhost:55432/kubuno_dev`;
- the **guard** (`DevDatabaseGuard`, in the launch provider and in `Kubuno.Web.Sdk`'s `KubunoCheckDevDatabase`
  target - never in the core's own code) refuses to start when the variable is missing, when it is not a database
  URL, when it names no database, and when the database's name has no development token (`dev`, `devel`,
  `development`, `test`, `testing`, `local`, `sandbox`, `scratch`, `ci`, `tmp`, `temp`, as a whole `_`/`-`/`.`
  separated word, trailing digits ignored: `kubuno_dev`, `kubuno-test`, `dev_kubuno2` pass; `kubuno`,
  `kubunodev` do not). `KUBUNO_DEV_ALLOW_ANY_DATABASE=1` lets a throw-away database with another name through, on
  purpose. Messages show the URL with its password replaced by `***`.

### Setting it up (once per machine)

PostgreSQL on 192.168.1.220 listens on the host's loopback only: port 5432 is closed to the network. Visual Studio
reaches it through an SSH tunnel, `localhost:55432` → `localhost:5432` on the host.

1. **Create the database** on the server, as `postgres`: `CREATE DATABASE kubuno_dev OWNER kubuno;`
2. **A dedicated key, without a passphrase** (Kubuno never asks for a password or a passphrase):
   `ssh-keygen -t ed25519 -f "%USERPROFILE%\.ssh\id_ed25519_kubuno" -N ""`.
3. **Authorise it** on the host: append `%USERPROFILE%\.ssh\id_ed25519_kubuno.pub` to `~/.ssh/authorized_keys` of
   the account Kubuno uses (`martinien` by default). When it is not, F5 says so in an info bar, whose
   "Copy the public key" link puts the `.pub` line on the clipboard.
4. **Set the variable** to the tunnel's local end, then restart Visual Studio:
   `setx KUBUNO_DEV_DATABASE_URL "postgres://kubuno:<password>@localhost:55432/kubuno_dev"`.
   `msbuild crates\kubuno-core\kubuno-core.rsproj -t:KubunoCheckDevDatabase` says what the guard would say.
5. **First connection**: the host's key is not known to Kubuno yet. An info bar shows its fingerprint
   (`SHA256:...`, ED25519 preferred); compare it with the server's (`ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub`
   on the host) and click **Accept**: the key is added to `%USERPROFILE%\.ssh\known_hosts_kubuno`, then F5 again.

### Tools > Options > Kubuno > Remote Linux host

The host is set in Visual Studio's unified settings (`kubuno.remote.*`), never in a repository. It is also the host
of the Linux builds to come.

| Setting | Default |
|---|---|
| Connection > Host | `192.168.1.220` |
| Connection > User | `martinien` |
| Connection > SSH port | 22 |
| Connection > Private key | `%USERPROFILE%\.ssh\id_ed25519_kubuno` |
| Connection > Known hosts file | `%USERPROFILE%\.ssh\known_hosts_kubuno` (only the keys accepted in Visual Studio) |
| Development database > SSH tunnel | *When KUBUNO_DEV_DATABASE_URL points at the local port* (`auto`), *At every launch*, *Never* |
| Development database > Local port | 55432 |
| Development database > PostgreSQL port on the host | 5432 |

### The tunnel

F5 / Ctrl+F5 of the Kubuno Core Web profiles (the core and every module) opens the tunnel **after** the guard has
accepted the URL and **before** anything is deployed, when the URL's host is `localhost`/`127.0.0.1`/`::1` at the
local port (or the setting says *At every launch*). `Tools > Kubuno Core Web: Open Development Database Tunnel`
opens the same tunnel without starting a core (for the Data Explorer, psql or `cargo sqlx prepare` on
`localhost:55432`).

- **Command**: Windows' OpenSSH (`C:\Windows\System32\OpenSSH\ssh.exe`),
  `ssh -N -L 55432:localhost:5432 -F none -i <key> -p <port> -o BatchMode=yes -o IdentitiesOnly=yes
  -o StrictHostKeyChecking=yes -o UserKnownHostsFile=<known_hosts_kubuno> -o ConnectTimeout=10
  -o ExitOnForwardFailure=yes -o ServerAliveInterval=15 -o ServerAliveCountMax=3 <user>@<host>`
  (`SshCommandLine`, Kubuno.Core.Logic). `BatchMode` means no prompt ever; `-F none` and `IdentitiesOnly` keep the
  user's own ssh configuration and agent out of it; host key checking is **strict**, against the Kubuno file only -
  it is never turned off.
- **Lifetime**: the tunnel is up when its local port accepts connections. It is reused by the next launches while
  its process lives (a port that already answers without a Visual Studio tunnel - one opened by hand - is used as
  is); a change of settings replaces it. It ends with Visual Studio (`SshTunnels.StopAll` at the package's disposal,
  and a kill-on-close job object if Visual Studio ends otherwise). A tunnel that drops later is logged; the next
  launch opens it again.
- **Errors** never open a dialog: the launch is cancelled, the "Kubuno" Output pane gets the reason and ssh's own
  output, and a main-window info bar names the fix (`SshFailure`): key not authorised (with "Copy the public key"),
  host unknown (fingerprint + **Accept**), host key **changed** (refused; no accept action - remove the old line by
  hand after checking), host unreachable, unknown host name, local port taken, key file missing, key readable by
  other accounts, key with a passphrase. Each bar has "Options" and "Log" links.
- **Accept** reads the host's keys with `ssh-keyscan` (no authentication), shows the preferred one's fingerprint and,
  on the click only, appends that one key (`host` on port 22, `[host]:port` otherwise) to the Kubuno known_hosts.

## 6. The dev core

F5 starts a development core of this machine, configured by environment only (the core reads `config.toml`, then
`KV__SECTION__KEY` variables): no configuration file is written, and the Linux default paths are all replaced by
folders of `%LOCALAPPDATA%\Kubuno\dev-core` (`KUBUNO_DEV_CORE_ROOT` or the `KubunoDevCoreRoot` property move it):

| Core setting | Dev core value |
|---|---|
| `database.url` | the accepted `KUBUNO_DEV_DATABASE_URL` |
| `server.host` / `server.port` | `127.0.0.1` / 8080 (`KubunoDevCorePort`) |
| `server.frontend_dist` | the core repository's `frontend\dist` (else an installed core's `frontend`) |
| `server.modules_dir` / `modules_install_dir` | `modules\` (F5 deployments) / `modules-store\` (`.kbpkg` installs) |
| `server.modules_config_dir` / `modules_data_dir` | `modules-config\` / `modules-data\` |
| `server.themes_dir` | `themes\` (seeded once from the core repository's `themes`) |
| `server.internal_secret`, `auth.jwt_secret` | generated once, kept in `dev-secrets.json` (local development secrets) |
| `storage.local_path`, `logging.log_dir`, `database.path` | `files\`, `logs\`, `db\` |
| data key, first administrator password | `data.key`, `initial-admin-password` (`KUBUNO_DATA_KEY_FILE`, `KUBUNO_INITIAL_PASSWORD_FILE`) |

The core's working directory is that folder. On a fresh development database the core creates the first
administrator and writes its password to `initial-admin-password` (the "Kubuno" Output pane says so).

## 7. F5

**The core** (`kubuno-core.rsproj`, debugger "Kubuno Core Web (serveur)"): guard and development database tunnel (section 5), then `kubuno-core.exe` under the native
debugger with the dev core environment (and `RUST_BACKTRACE=1`, `RUST_LOG=info` unless set); the browser opens on
`http://localhost:8080/` once the core answers. The core's console (its tracing log, and what the modules it
supervises print) is redirected by the native debugger (`> dev-core\logs\core-console.log 2>&1`, never passed to the
core) and followed into the **"Kubuno Core Web (serveur)"** Output pane, ANSI colours removed and the database
password masked. **Combined launch**: the "Kubuno Core Web (serveur + Vite)" profile of the
`.slnLaunch` starts the core and the frontend project; the frontend's F5 runs Vite's dev server (`npm run dev`,
port 5173, which proxies `/api`, `/internal`, `/modules` and `/ws` to :8080) and opens Edge on
`http://localhost:5173` with Visual Studio's script debugger. With a frontend project among the startup projects,
the core does not open a second browser.

**A module** (`<module>.rsproj`, `KubunoWebRole=Module`):

1. guard, then the development database tunnel (section 5);
2. deploy - the Windows `deploy_local.sh`: the built executable (and its PDB), `module.toml` (the core reads the
   installed manifest), the top-level `migrations/*.sql` and `frontend/dist` are copied into
   `dev-core\modules-store\<id>` when the module was installed from a `.kbpkg`, else `dev-core\modules\<id>`; copies
   of the module still running there from a previous session are stopped first (stopping the debugger ends the core,
   not the module it started; the running executable would be locked and its port taken) - only processes whose
   image lies in that folder are touched;
3. start a core: `KubunoDevCoreExecutable` (Debug page), else the newest `kubuno-core.exe` built from the core
   repository next to the module (`..\core`, in `CARGO_TARGET_DIR` or `core\target`, debug or release), else an
   installed `C:\Program Files\Kubuno\kubuno-core.exe`; under the native debugger, with the dev core environment;
4. the core starts the module (its supervisor, `KUBUNO_*` environment, port of `module.toml`); the launch provider
   **attaches the native debugger to every module process** whose image is the deployed executable, as soon as it
   appears and again after each restart by the supervisor (through the automation model, `Process2.Attach2` with the
   native engine: `IVsDebugger4.LaunchDebugTargets4` with `DLO_AlreadyRunning` refuses a process started by a core
   that is itself being debugged, E_INVALIDARG), until the core exits. Stop Debugging ends the core and only detaches
   from the module, so the module copies left in the deployed folder are then stopped too;
5. the browser opens on the module's first sidebar path (`http://localhost:8080/calendar`). A module checked out
   outside `..\core` gets the host frontend of the core repository of the last core F5 (`dev-core\core-repository.txt`).

**Frontend debugging.** The "<Module> (Kubuno Core Web + navigateur)" profile also starts the module's frontend project: its
F5 runs `vite build --watch --sourcemap` straight into the deployed `frontend` folder of the module (edit, save,
refresh) and opens Edge on the module with the script debugger; `launch.json` maps
`http://localhost:8080/modules/<id>/frontend/*` back to `dist/*`, whose source maps lead to `src`.

## 8. The module template

"Kubuno Core Web Module" (Create a new project > Kubuno), same TemplateID as the former backend-only "Kubuno Module":

- `kubuno-<id>.rsproj` (`Kubuno.Rust.Sdk` + `Kubuno.Web.Sdk`, role Module), `Cargo.toml` (`kubuno-<id>`, its own
  workspace, `kubuno-seccomp` by tag `seccomp-v0.1.1`, sqlx 0.9 / axum 0.7 like the modules), `module.toml`, a
  `src/main.rs` reading what the core gives a module (`KUBUNO_INTERNAL_SECRET`, `KUBUNO_DB_*`; `DATABASE_URL`/`PORT`
  when run alone) and registering with the core like the real modules (`POST /internal/modules/register` from the
  built-in `module.toml`, then a heartbeat every 30 s - without it the core never proxies `/api/v1/<id>/...`), migrations in the module's own schema (sqlx's migration table too, through `search_path`),
  `/internal/*` with a constant-time secret check, security headers, no `unwrap()`;
- `frontend/`: `<id>-frontend.esproj`, `package.json` with `@kubuno/sdk|ui|drive` at the published versions (read
  from `core/frontend/packages/*/package.json` when a core checkout is next to the new module, else the versions the
  extension was built with: ui 0.1.12, sdk 0.1.10, drive 0.1.7), `vite.config.ts` (shared specifiers external),
  `tsconfig.json` (`@ui` paths), `src/entry.ts` (`register()`, `sdkVersion`), a page, `index.css` (Tailwind in the
  `kubuno-module` cascade layer), `.kubuno/launch.json`;
- `build_kbpkg.sh` (the polyrepo's generic one), `README.md` and `CHANGELOG.md` in English, `.gitignore`.

The wizard (`Kubuno.Web.TemplateWizard.ModuleWizard`) computes the id (lower-case letters and digits of the project
name: `Inventory` -> `inventory`, crate `kubuno-inventory`), adds the `.esproj` to the solution with the backend
depending on it, writes the `.slnLaunch` profile and the solution's SDK feed.

## 9. `.kbpkg`

**Tools > Kubuno Core Web: Package Module (.kbpkg)** runs `Kubuno.Web.Sdk`'s `KubunoPackModule` target in Release with Visual
Studio's MSBuild (`msbuild <module>.rsproj -t:KubunoPackModule -p:Configuration=Release` does the same): the backend,
the frontend, then `dist\<id>-<version>-windows-x86_64.kbpkg` - a ZIP whose root is the module folder (executable,
`module.toml`, `frontend/`, `migrations/*.sql`, `config.toml.example`, `LICENSE`, `CHANGELOG.md`) and `SHA256SUMS`
in `sha256sum`'s format, like `build_kbpkg.sh`. `msbuild <module>.rsproj -t:KubunoDeployModule` is the command-line
deployment into the dev core.

## 10. Version tools

Under Tools, never committing, tagging, pushing nor publishing (those stay the developer's):

- **Kubuno Core Web: Check Versions** runs `_tools/check_versions.py` when a real Python is installed; otherwise (this
  machine: only the Store stub) its built-in subset: Cargo.toml / module.toml / frontend/package.json alignment per
  repository, each module's `@kubuno/*` build floors against the published versions (npm registry, read-only, else
  the core's `frontend/packages` sources), and the shared crate tags each module uses against the core's latest local
  tags (`git -c safe.directory=* tag -l`: the checkouts belong to the server's user).
- **Kubuno Core Web: Prepare npm Floors...** - `bump_npm_floors.sh`'s edits: every module's `dependencies`/`devDependencies`
  `@kubuno/*` range below the published version becomes `^<published>` (peer ranges untouched). Shown in the Output
  pane and confirmed first; `npm install --package-lock-only` and the commits are left to the developer.
- **Kubuno Core Web: Prepare Shared Crate Tags...** - `bump_shared_crates.sh`'s edits: every `tag = "<crate>-vX.Y.Z"` moved to
  the latest tag of the core (and `drive-v*` of the drive repository). The tags must be pushed before cargo can fetch
  them; `Cargo.lock` is refreshed by the next build.

## 11. Previewing a frontend inside Visual Studio (design note)

Decision (product owner, 2026-10-01): when the web UI is shown inside Visual Studio - a tool window with the running
module, a preview next to a `.tsx` - the engine is **WebView2** (the `Microsoft.Web.WebView2` WPF control, with the
Evergreen runtime Windows 11 and Visual Studio already ship); WebKit is dropped. Nothing is built yet, and no visual
designer for React/TSX: the choice between such a designer and a live preview is still open. When it comes:

- a tool window of the `Kubuno.Web` layer hosting `WebView2` on the dev core's URL (`http://localhost:8080/<id>`), its
  user data folder under `%LOCALAPPDATA%\Kubuno\webview2` (never the IDE's), its own cookie jar for the dev core's
  session;
- themed like Visual Studio: the window chrome from `EnvironmentColors`, and the page asked for the matching Kubuno
  theme (`prefers-color-scheme` through `CoreWebView2.Profile.PreferredColorScheme`, updated on
  `VSColorTheme.ThemeChanged`);
- reload on the module frontend's watch build, script debugging through Visual Studio's existing JavaScript debugger
  (attach to the WebView2 process), never a second debugger of our own.

## 12. Naming: Kubuno Core Web and Kubuno Core Desktop

The product owner's rule (2026-10-01): the web server is **Kubuno Core Web**, the desktop workspace **Kubuno Core
Desktop**. In this tooling: the core's solution is `Kubuno.Core.Web.slnx`; "Generate Visual Studio Projects" (the Rust
layer) names a NEW solution `Kubuno.Core.Web.slnx` for the core repository, `Kubuno.Core.Desktop.slnx` for the desktop
workspace and `Kubuno.<Module>.slnx` for a module (`SolutionNaming`); the commands are "Kubuno Core Web: ..."; the
debugger is "Kubuno Core Web (serveur)"; the module template is "Kubuno Core Web Module" (tags `Kubuno`,
`Kubuno Core Web`), and the desktop templates are tagged `Kubuno Core Desktop` ("Kubuno Core Desktop Application",
item templates suffixed "(Kubuno Core Desktop)"; IDs unchanged). The extension's internal `Kubuno.Core` layer keeps its
name for now.

## 13. Verification (2026-10-01, hive `/rootsuffix KubunoWeb`)

- **Command line**: the core workspace builds with MSVC (`cargo build --workspace`, 14 min cold, one MSVC linker
  message reported as a warning by rustc's `linker_messages` lint); `cargo test --workspace`: about 1000 tests, 2
  failures on Windows only (`backup::policy` and `data_export::policy` tests assert that `/var/...` is an absolute
  path - a core issue, not the tooling's); `kubuno-core --version` runs. The core frontend builds from the share with
  its Linux `node_modules` (overlay of 4 Windows native packages; `node_modules` checked untouched before and after);
  `KubunoCheckDevDatabase`: missing variable and `kubuno` refused, `kubuno_dev` accepted (no connection made).
- **Visual Studio**: "Generate Solution" on `Z:\src\core` -> `Kubuno.Core.Web.slnx` (7/7 projects loaded, web server
  icon, startup `kubuno-core`); solution build 7/7 (cargo workspace + `tsc -b && vite build`); F5 -> the guard's
  message, nothing started; Test Explorer ran the Rust tests: 69 listed, 58 passed, 11 failed - **fewer than the
  command line's ~1000**: the Rust layer's adapter does not list the whole workspace-scope core yet (open issue);
  the vitest specs were not listed by the JavaScript test adapter (not investigated further).
- **New module** from "Kubuno Core Web Module" (`Inventory`): backend + frontend in the solution, frontend built first
  (BuildDependency), 2/2 built, its Rust test listed; F5 with a dev-named URL pointing at nothing
  (`127.0.0.1:1/kubuno_dev`) and a built core: the module was deployed into the dev core (executable, PDB,
  `module.toml`, migrations, `frontend/entry.js|css|map`) and `kubuno-core.exe` started under the debugger (it then
  stops on the unreachable database, so the module process and the attach were not exercised end to end). The
  template also passes `tools/test-templates.ps1` (cargo build, MSBuild), `cargo clippy --all-targets -D warnings`,
  `cargo test`, the frontend build, `KubunoDeployModule` and `KubunoPackModule` from the command line.
- **drive**: `Kubuno.Drive.slnx` generated and loaded (2/2), the frontend builds; the backend does not build anywhere
  fresh today: drive's committed `Cargo.lock` pins `kubuno-modauth` (tag `modauth-v0.1.0`) to commit `4c9618cb`, which
  github.com/kubuno/core no longer has (the tag now points at `fb4952b`) - a repository issue (`cargo update -p
  kubuno-modauth` in drive fixes it).
- **Multi-repository**: `Z:\src\Kubuno.Web.slnx` with core + drive, 9/9 projects, Visual Studio's Git status bar
  showing the 2 repositories.
- Not verified: a real development database (no `KUBUNO_DEV_DATABASE_URL` was available: never set nor guessed),
  hence the dev core running, the module attach, the browser opening and Edge script debugging; "Check Versions"
  and the "Prepare" commands inside Visual Studio (covered by unit tests).

### Follow-up (same day): packages, sub-modules, module isolation

- `Kubuno.Core.Web.slnx` regenerated: 10 projects (Server: `kubuno-core`; Libraries: 5 crates; Frontend:
  `kubuno-frontend`; Packages: `kubuno-ui`, `kubuno-sdk`, `kubuno-drive`), loaded 10/10 and built 10/10 in Visual Studio
  in the expected order (ui, sdk, drive, then the host app); `node_modules` untouched. Test Explorer: 1013 Rust tests
  listed, 1000 passed, 13 failed (core issues, section 4).
- **p2pnas** (Cargo workspace): `Kubuno.P2pnas.slnx` with Server `kubuno-p2pnas`, Libraries `p2pnas-core`,
  `p2pnas-store`, `p2pnas-p2p`, Frontend `p2pnas-frontend`, loaded 5/5; the frontend builds; the crates do not build
  anywhere fresh today for the same reason as drive: its `Cargo.lock` pins `kubuno-db` (tag `db-v0.7.0`) to commit
  `63b51dbc`, which github.com/kubuno/core no longer has. **drive**: regenerated with the same layout (2/2).
  **stt** and **office** solutions generated (`Kubuno.Stt.slnx`, `Kubuno.Office.slnx`), not opened.
- While wiring the packages, one run of the first package build script copied freshly emitted declarations into
  `core/frontend/packages/ui/types` (10 modified `.d.ts`, 3 new ones) and overwrote `packages/ui/dist/index.js`, which
  was restored from HEAD at once; the build now writes only into `obj/package`.
