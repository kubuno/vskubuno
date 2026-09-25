# vskubuno — Architecture

Kubuno for Visual Studio 2026: Rust, Cargo, Rust debugging and (later) Kubuno XML views,
inside Visual Studio, plus an MCP server so Claude sees what the developer sees.

## Goal

The developer edits what Claude generates (backend and frontend) in Visual Studio, and Claude
keeps assisting after those edits. Consequences for the design:

- **Git is the shared history.** Claude reads `git diff`/`git log` to understand the
  developer's changes.
- **Nothing the developer owns is ever regenerated.** Views (XML) are the single source of
  truth and are edited surgically; code-behind lives in Rust files nothing regenerates; any
  code derived from XML is produced at build time (`build.rs`) and never committed.
- **Canonical formatting** (rustfmt, an XML formatter) keeps diffs meaningful.
- **Tests, clippy and type-checks are the contract** between the two editors.

## Extension model

Classic VSSDK (in-process, .NET Framework 4.8, `AsyncPackage`), because phase 1 needs:

| Need | VS API |
|---|---|
| Rust language features | `ILanguageClient` (LSP client) hosting rust-analyzer |
| Coloring | TextMate grammar shipped in the VSIX |
| Cargo projects without a project file | Open Folder extensibility (`IWorkspaceProviderFactory`, file scanners, file context actions for build/debug) |
| Build output → Error List | `cargo … --message-format=json` parsed into Error List entries |
| Launch / debug a target | Open Folder launch/debug providers (native debugger, PDB + Rust natvis) |
| Embedded native preview (phase 4) | `HwndHost` in a tool window |

The out-of-process `VisualStudio.Extensibility` model has no workspace/debug providers and
cannot host a foreign native window, so it is not used.

## Phases

1. **Rust + Cargo + debug** (no dependency on XML views)
   - 1a. VSIX skeleton, rust-analyzer LSP client, TextMate grammar, rustfmt on save.
   - 1b. Cargo workspace: targets from `cargo metadata`, Build/Rebuild/Clean, Error List.
   - 1c. Launch/debug any bin/example/test target with the native debugger.
2. **XML view framework in `kubuno_ui`** (Rust): component metadata registry (single source
   for loader, schema, language server, property grid), XML loader, hot reload.
3. **View editing in VS**: XML language server (Rust), embedded live preview.
4. **Reduced designer**: preview ⇄ XML selection sync, property grid, toolbox.
5. **MCP server** in the extension: open file, selection, Error List, designer selection,
   preview capture, debugger state (stack, locals at a breakpoint).

## Known limits

- VS's LSP client is less complete than VS Code's (inlay hints, code lenses: to verify).
- The debugger's expression evaluator speaks C++, not Rust: fields and natvis views work,
  Rust expressions (method calls, traits) in the Watch window do not.
- rust-analyzer uses 1–2 GB on a large workspace.
