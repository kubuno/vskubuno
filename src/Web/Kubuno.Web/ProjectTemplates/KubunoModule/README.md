# Kubuno $moduletitle$

A module for [Kubuno](https://github.com/kubuno/core), the self-hosted, libre (AGPLv3) cloud platform: a Rust/Axum
process the core starts, supervises and proxies, plus a React/TypeScript frontend the core's host loads at run time.

Created from Visual Studio's "Kubuno Web Module" template (the vskubuno extension).

## Layout

| Path | What it is |
|---|---|
| `src/main.rs` | The module's server: `/health`, `/api/$moduleid$/...`, `/internal/*` (guarded by `X-Internal-Secret`). |
| `migrations/` | The module's PostgreSQL schema (`$moduleid$`), applied at startup with `sqlx::migrate!`. |
| `module.toml` | The manifest the core reads: id, port, routes, sidebar entry, events. |
| `frontend/` | The frontend bundle (`dist/entry.js`, `dist/entry.css`): Vite 8, React 19, TypeScript 6, Tailwind v4, `@kubuno/sdk`/`@kubuno/ui` from npm (external at run time, resolved by the host's import map). |
| `$cratename$.rsproj`, `frontend/$moduleid$-frontend.esproj` | The Visual Studio projects (Kubuno.Rust.Sdk, the JavaScript SDK, Kubuno.Web.Sdk). |
| `build_kbpkg.sh` | Packages the module as a `.kbpkg`, the only format the core installs. |

## Developing in Visual Studio

- **Build** (Ctrl+Shift+B) builds the backend with cargo and the frontend with npm/Vite.
- **F5** on the backend deploys the module into a development core on this machine and starts it under the
  debugger, attached to the module process; the browser opens on `/$moduleid$`. The core needs a DEVELOPMENT
  database: set `KUBUNO_DEV_DATABASE_URL` (for example `postgres://kubuno:<password>@<server>:5432/kubuno_dev`) -
  never the live database, the core migrates it at startup.
- **F5 on the frontend project** rebuilds the bundle on every change into the running core (refresh the page) and
  opens Edge with the script debugger.
- **Tools > Kubuno: Package Module (.kbpkg)** writes `dist/$moduleid$-<version>-windows-x86_64.kbpkg`.

## Building from the command line

```bash
cargo build --release
cd frontend && npm ci && npm run build
bash build_kbpkg.sh              # -> dist/$moduleid$-<version>-<os>-<arch>.kbpkg
bash build_kbpkg.sh --install    # + `kubuno modules:install` on this machine's core + restart
```

## License

AGPL-3.0
