# Solution ↔ disk alignment (target spec)

> Status: **proposal, pending owner approval.** Once approved, the rules below are implemented in the
> generator (`ProjectNaming`, `PlatformLayout`, `WebRepository`, `CoreRepository`) and this file becomes the
> reference for the layout. See also [WEB.md](WEB.md) and [RSPROJ.md](RSPROJ.md).

## 1. Why

The Visual Studio tree must mirror the on-disk directory tree as closely as the two ecosystems allow, so that
a folder or project seen in Solution Explorer is found at the obvious place on disk, and vice-versa. This is a
maintenance property: there is one layout to learn, not two.

Today three things break that correspondence:

1. **Linked-item wrappers.** The npm packages built from the host app's own sources (`sdk`, `ui`, `drive`)
   have their project file in `web/packages/<id>/` while their real sources live in `web/src/<id>/`, surfaced
   as *linked items*. In Solution Explorer `Kubuno.Web.Sdk` shows a `src` folder that does **not** exist at
   `web/packages/sdk/src` on disk — it is a link to `web/src/sdk`.
2. **Names unrelated to their location.** `common/core` is shown as `Kubuno.Drive.Core` /
   `Kubuno.Office.Docs.Core`; two core desktop crates carry `Drive`/`Web` prefixes although they sit in the
   core's `desktop/common`.
3. **A name collision:** `Kubuno.Web.Drive` (an `@kubuno/*` package **inside the core**) vs `Kubuno.Drive.Web`
   (the **drive module's** frontend).

The top-level solution folders (`Common / Server / Web / Desktop/{Common,Windows,Linux,macOS} / Mobile`)
already map 1:1 to the disk folders (`common/ server/ web/ desktop/{…} mobile/`); the divergence is below that
level.

## 2. The hard constraint (sets expectations)

Rust crates are **kebab-case** on disk (`kubuno-drive-desktop-common`) because Cargo requires it. Visual
Studio / MSBuild project display names are **PascalCase dotted** (`Kubuno.Drive.Desktop.Common`) by .NET
convention. They **cannot** be made byte-identical without breaking one ecosystem. "Aligned" therefore means:

- **(R1) Folder hierarchy** — every solution folder equals a disk folder, 1:1, at every level (modulo OS
  casing, e.g. `macOS`↔`macos`).
- **(R2) Naming** — project display names stay PascalCase dotted, derived from the disk folder by a single,
  documented, reversible rule (§4). Given a project name you can compute its folder and vice-versa.
- **(R3) Co-location** — a project's files are **physically** under the folder the project lives in. No
  linked-item wrappers. What Solution Explorer shows beneath a project exists on disk at that path.

Owner decisions (2026-10-13): approach = *target spec first, then the generator*; naming = *keep PascalCase +
documented mapping* (this document).

## 3. Naming rule (R2)

One pure function maps a disk folder name (kebab) to a project name segment (Pascal):

```
Pascal(kebab) = kebab.split('-').map(capitalizeFirst).join('')      # drive-desktop → DriveDesktop
```

Applied per project kind:

| Kind | Disk | Project display name | Rule |
|---|---|---|---|
| Core host frontend | `core/web/` | `Kubuno.Core.Frontend` | fixed |
| Core server | `core/server/` (crate `kubuno-core`) | `Kubuno.Core.Server` | fixed |
| Core shared crate | `core/common/kubuno-<x>/` | `Kubuno.Core.<Pascal(x)>` | `ForCoreCrate` |
| Core npm package | `core/web/src/<id>/` | `Kubuno.Web.<Pascal(id)>` | package rule (§5) |
| Core desktop crate | `core/desktop/<os>/kubuno-desktop-<x>/` | `Kubuno.Desktop.<Pascal(x)>` | `ForCrate`, core-desktop face |
| Module frontend | `<m>/web/` | `Kubuno.<Pascal(m)>.Web` | fixed per module |
| Module server | `<m>/server/` | `Kubuno.<Pascal(m)>.Server` | fixed per module |
| Module shared core | `<m>/common/core/` (crate `kubuno-<m>-core`) | `Kubuno.<Pascal(m)>.Core` | `common/core` rule |
| Module other common crate | `<m>/common/<name>/` | `Kubuno.<Pascal(m)>.<Pascal(name)>` | `ForCrate` |
| Module desktop crate | `<m>/desktop/<os>/kubuno-<m>-desktop-<x>/` | `Kubuno.<Pascal(m)>.Desktop.<Pascal(x)>` | `ForCrate` |

**Mapping anchor for crates:** the project name is derived from the **crate's folder name** (kebab), stripping
the leading `kubuno-` and, for a module, the `<m>-` segment, then `Pascal()` per `-`. This removes today's
ad-hoc names:

- `common/core` → **`Kubuno.<Module>.Core`** everywhere (retire `Kubuno.Office.Docs.Core`; the crate is
  `kubuno-office-core`, so the name is `Kubuno.Office.Core`). The desktop app crates keep their own names
  (`Kubuno.Office.Documents`, `.Spreadsheets`, …) because those are distinct crates under
  `desktop/windows/kubuno-office-<app>` — no longer conflated with the shared `common/core`.
- The two core-desktop crates misprefixed `Kubuno.Drive.Desktop.Shared` / `Kubuno.Web.Views.Compiler.Core`
  (they live in `core/desktop/common/`) → **`Kubuno.Desktop.*`** (core-desktop face), e.g.
  `Kubuno.Desktop.DriveShared`, `Kubuno.Desktop.WebViewsCompilerCore` — matching their location in the core's
  Desktop folder, not a module.

**Collision resolved:** core packages use order `Kubuno.Web.<Thing>`, modules use `Kubuno.<Module>.Web`. These
never collide once `Kubuno.Web.Drive` (core package `@kubuno/drive`) and `Kubuno.Drive.Web` (drive module
frontend) are read with the documented rule (core-package vs module-frontend). The rule is kept; the spec
**documents** it so the two are not mistaken for each other. (No rename — renaming `@kubuno/drive` would break
the published npm surface.)

## 4. Co-location rule (R3) — the key change

Every `.esproj`/`.rsproj` lives **in the folder whose files it owns**. Concretely:

### 4a. Rust crates — already co-located

Desktop and common crates already have their sources physically under their crate folder; the generator emits
the `.rsproj` there. Only **(R1)** folder nesting and **(R2)** naming apply. No disk move. The `Desktop`
subfolders already match `desktop/{common,windows,linux,macos}`.

### 4b. Self-contained web packages — already co-located

`host-runtime`, `vectors`, `views`, `views-compiler`, `views-migrate`: their sources are physically in
`web/packages/<x>/`, the `.esproj` is there. Fine as-is under **(R1)/(R2)**. (Open question O1 below: whether
`web/packages/` should appear as a `packages` solution subfolder under `Web`, to mirror disk exactly, instead
of being flattened.)

### 4c. Host-tree npm packages (`sdk`, `ui`, `drive`) — the linked-item problem

These are built from the host app's own sources (`web/src/<id>`) and published as `@kubuno/<id>`. The wrapper
`web/packages/<id>/` (project file + `package.json` + `index.js` + `dist/` + `types/` + CHANGELOG/README + tgz)
is what creates the fake `src` link.

**Target (recommended, option A): make `web/src/<id>/` the package root.** Move the publish metadata
(`package.json`, `index.js`, `README.md`, `CHANGELOG.md`) into `web/src/<id>/`; the `.esproj` lives there too;
build outputs (`dist/`, `types/`) are generated under `web/src/<id>/` (committed as today, or moved to `obj/`
and regenerated). Retire `web/packages/<id>/`. Result: `Kubuno.Web.Sdk` is at `web/src/sdk/`, and everything it
shows is physically there — no links. The host frontend keeps excluding `src/sdk|ui|drive` so the files show
only under their own project. `build.sh` paths and the import-map aliases (which already point at
`web/src/<id>/index.ts`) are updated; the published npm identity (`@kubuno/<id>`) is unchanged.

**Alternative (option B, lower churn): keep `web/packages/<id>/` for publish artifacts, drop the linked
`.esproj` and move it to `web/src/<id>/`.** The node becomes truthful (shows the real `src/<id>`), but two
folders remain on disk (`src/<id>` for source, `packages/<id>` for the npm wrapper). Less clean than A.

### 4d. Admin console (done in this cycle as the pilot of R3)

`core/web/src/core/admin/` → **`core/web/src/admin/`** (done, verified: typecheck + build green, still a lazy
chunk). The project `Kubuno.Web.Admin` is **co-located** at `web/src/admin/` (no wrapper, not an npm package).
This is the reference shape for R3.

## 5. Target trees

### Core (`core/`)

```
/Core
  /Common          common/kubuno-db …          Kubuno.Core.Db, .Storage, .Seccomp, .Modauth, .Paths, .Mcp, .Vectors
  /Server          server/                     Kubuno.Core.Server
  /Web             web/                         Kubuno.Core.Frontend            (host app, web/)
                   web/src/admin/               Kubuno.Web.Admin                (co-located, R3 pilot)
                   web/src/sdk/                 Kubuno.Web.Sdk                  (co-located per 4c-A)
                   web/src/ui/                  Kubuno.Web.UI
                   web/src/drive/               Kubuno.Web.Drive
                   web/packages/host-runtime/   Kubuno.Web.Host.Runtime         (self-contained)
                   web/packages/vectors/        Kubuno.Web.Vectors
                   web/packages/views/          Kubuno.Web.Views
                   web/packages/views-compiler/ Kubuno.Web.Views.Compiler
                   web/packages/views-migrate/  Kubuno.Web.Views.Migrate
  /Desktop
    /Common        desktop/common/kubuno-desktop-*   Kubuno.Desktop.*  (+ Kubuno.Desktop.DriveShared, .WebViewsCompilerCore)
    /Windows       desktop/windows/kubuno-desktop-*   Kubuno.Desktop.*
    /Linux         desktop/linux/…                    Kubuno.Desktop.Shell.Linux
    /macOS         desktop/macos/…                    Kubuno.Desktop.Shell.Macos
  /Mobile          mobile/README.md
```

### A module (`drive/`, representative)

```
/Modules/Drive
  /Common          common/core/      Kubuno.Drive.Core         (crate kubuno-drive-core)
  /Server          server/           Kubuno.Drive.Server
  /Web             web/              Kubuno.Drive.Web
  /Desktop
    /Common        desktop/common/kubuno-drive-desktop-*   Kubuno.Drive.Desktop.Common, .CoreStorage, .Localization
    /Windows       desktop/windows/kubuno-drive-desktop-*   Kubuno.Drive.Desktop, .Windows, .AppStorage
    /Linux         desktop/linux/…                           Kubuno.Drive.Desktop.Linux
    /macOS         desktop/macos/…                           Kubuno.Drive.Desktop.Macos
  /Mobile          mobile/README.md
```

Every other module follows the same shape, with `<Module> = Pascal(module.toml id)`. The `/Modules/<Module>/`
prefix is added by `_tools/gen_master_solution.ps1` when stitching the master solution.

## 6. Divergences resolved (summary)

| # | Today | Target |
|---|---|---|
| D1 | `Kubuno.Web.Sdk` shows a `src` not on disk (linked) | co-located at `web/src/sdk/`; shown = disk (§4c-A) |
| D2 | `common/core` → `Kubuno.Drive.Core` / `Kubuno.Office.Docs.Core` (inconsistent) | always `Kubuno.<Module>.Core` |
| D3 | core-desktop crates misprefixed `Kubuno.Drive.*` / `Kubuno.Web.*` | `Kubuno.Desktop.*` (their real location) |
| D4 | `Kubuno.Web.Drive` vs `Kubuno.Drive.Web` read as a collision | documented rule: core-package vs module-frontend |
| D5 | admin buried in `src/core/admin`, hard to find | `src/admin` + `Kubuno.Web.Admin` (done) |

## 7. Disk moves required

- **Admin:** `core/web/src/core/admin` → `core/web/src/admin` — **done**.
- **npm packages (4c-A):** per package `sdk`/`ui`/`drive`, move publish metadata into `web/src/<id>/`, retire
  `web/packages/<id>/`. Update `web/packages/build.sh` and any publish scripts; aliases already point at
  `web/src/<id>`.
- **Rust crates:** none (already co-located). Naming only.

## 8. Generator changes required (vskubuno)

1. **`ProjectNaming`** — single `Pascal(kebab)` mapper; `ForCoreCrate`/`ForCrate` derive from crate folder name
   with the `kubuno-[<m>-]` strip; `common/core` → `Kubuno.<Module>.Core`; core-desktop crates use the
   `Kubuno.Desktop.*` face regardless of a `drive`/`web` token in the crate name.
2. **`WebRepository`** — discover host-tree packages at `web/src/<id>/` (package root) instead of
   `web/packages/<id>/` with linked items; keep self-contained packages at `web/packages/<x>/`. Discover the
   admin console project at `web/src/admin/`.
3. **`PlatformLayout`** — unchanged folder rule (already 1:1); optionally (O1) emit a `packages` solution
   subfolder under `Web` to mirror `web/packages/` exactly.
4. Keep "a project file that exists is never rewritten"; the generator (re)writes only the `.slnx` folder tree
   and missing project files.

## 9. Owner decisions (resolved 2026-10-13)

- **O1 — `packages` subfolder: YES.** Self-contained packages show under a `Web/packages/` solution subfolder
  that mirrors `web/packages/` on disk (R1). The generator emits that subfolder; co-located packages
  (`sdk`/`ui`/`drive`/`admin`) sit directly under `Web/` because their sources are at `web/src/<id>`.
- **O2 — npm build outputs: `obj/`, not committed.** For 4c-A, `dist/`/`types/` move under `obj/` and are
  regenerated; added to `.gitignore`. Publishing regenerates them first (already a user action).
- **O3 — rollout: core first, then modules.** Finish the core (admin ✓, then packages 4c-A, then names via the
  generator), validate in Visual Studio, then roll the 18 modules out through the generator.

## 10. Verification

- Code correctness of any disk move: `kbview-tsc.js -b --force` (typecheck) + `vite build` (bundling / lazy
  chunks). Admin move: **green**.
- VS layer: `KUBUNO_GEN_REPO=<repo>` headless dry-run (prints the planned `.slnx`/`.slnLaunch`), then open the
  solution in Visual Studio for the final visual check. No CLI substitute for the IDE render.
