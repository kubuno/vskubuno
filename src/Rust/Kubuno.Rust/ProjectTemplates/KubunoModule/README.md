# $safeprojectname$

A [Kubuno](https://github.com/kubuno/kubuno) module: a standalone Rust/Axum process the Kubuno
core connects to at startup, proxies routes for, and supervises. Generated from Visual Studio's
"Kubuno Module" project template.

## Layout

- `src/main.rs` - the Axum/Tokio server (`/health`, `/internal/*`).
- `module.toml` - the manifest the core reads at registration (id, port, sidebar entry, events).
- `migrations/postgres/` - this module's own schema (`$moduleid$`), applied with `sqlx::migrate!`
  at startup.
- `config.toml.example` - copy to `config.toml` (or set the same names as environment variables)
  for local development.
- `build_kbpkg.sh` - packages this module into a `.kbpkg`, the only format the core installs.

## Building

```bash
cargo build --release
bash build_kbpkg.sh              # -> dist/$moduleid$-<version>-<os>-<arch>.kbpkg
bash build_kbpkg.sh --install    # + `kubuno modules:install` on this machine's core + restart
```

## License

AGPL-3.0
