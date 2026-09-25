# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Added

- **Integrated `Kubuno.VisualStudio.Views`, `Kubuno.TestAdapter` and `Kubuno.Mcp`/`Kubuno.Mcp.Bridge`
  into the VSIX**, following each library's own `INTEGRATION.md`/`docs/MCP.md`:
  - `.kbview` files now get an `ILanguageClient` (hosting `kubuno-views-ls.exe`), TextMate coloring
    (`Grammars\Kbview\`, merged into `languages.pkgdef` under a distinct `KubunoViews` key so it
    never collides with the Rust grammar), and a Tools > Options > Kubuno > Views page.
  - Cargo tests now show up in Test Explorer for an Open Folder workspace: `Kubuno.TestAdapter` is
    registered as both a `Microsoft.VisualStudio.MefComponent` and a `UnitTestExtension`
    (`source.extension.vsixmanifest`), and a new `Workspace/CargoWorkspaceSource.cs` implements
    `ICargoWorkspaceSource` against the real Open Folder workspace (`IWorkspace4.GetFilesAsync` for
    manifest enumeration - `IFileFinder`/`GetFileFinder()` does not exist in the
    `Microsoft.VisualStudio.Workspace` 17.12.19 this VSIX pins, contrary to the integration doc's
    tentative guess; `IWorkspace.OnActiveWorkspaceChanged` + `IFileWatcherService.OnFileSystemChanged`
    for change notification).
  - The MCP bridge (`Kubuno.Mcp.Bridge`'s net48 leg, hosting a named pipe for `kubuno-vs-mcp.exe`)
    now starts at package load, fully asynchronously (`JoinableTaskFactory.RunAsync(...).FileAndForget(...)`,
    never blocking the UI thread) and never crashes package load on failure (every step logged to
    the "Kubuno" Output pane). Verified live end to end: `kubuno-vs-mcp.exe` run standalone
    connects through its discovery file/named pipe to a running experimental instance and a real
    `vs_solution_or_folder` tool call returns the actual open folder and detected Cargo workspace.
  - `kubuno-views-ls.exe` (built from the separate `desktop` repo, crate `kubuno-views-ls`) and
    `kubuno-vs-mcp.exe` (this repo's own `Kubuno.Mcp`, net8.0, framework-dependent) both ship under
    the VSIX's `tools\` folder, via two new `Kubuno.VisualStudio.csproj` MSBuild properties -
    `KubunoViewsLsExePath` (default `C:\kubuno-build\desktop-target\release\kubuno-views-ls.exe`,
    matching this workspace's documented `CARGO_TARGET_DIR` convention) and `KubunoMcpOutputDir`
    (default: `Kubuno.Mcp`'s own build output for the same `$(Configuration)`, built automatically
    as part of the solution via a `ReferenceOutputAssembly=false` `ProjectReference`) - each with a
    `BeforeTargets="Build"` `Error` if the exe is missing. `.github/workflows/build.yml` now checks
    out `kubuno/desktop`, builds `kubuno-views-ls` before the solution build, and passes
    `KubunoViewsLsExePath` accordingly.
  - `Kubuno.VisualStudio.Views`, `Kubuno.TestAdapter`, `Kubuno.Mcp`, `Kubuno.Mcp.Bridge` and their
    three test projects are now part of `Kubuno.VisualStudio.sln`, so CI's `dotnet test --no-build`
    loop (which just discovers every `tests\*.csproj`) covers them.

### Changed

- `samples/hello-rust/src/main.rs` binds `greet(...)`'s result to a local `greeting: String` before
  printing it, instead of passing the call inline - gives the sample a concrete `String` local to
  breakpoint/inspect (used to verify natvis end to end for this task; also a generally more useful
  manual-testing fixture, matching `lib.rs`'s own stated purpose for this crate).

### Known limitations

- **`kubuno-views-ls.exe` was not observed starting for a `.kbview` file, live, in the experimental
  instance** (rust-analyzer reliably starts for `.rs` files opened the same way in the same
  session). `Kubuno.VisualStudio.Views.INTEGRATION.md`'s documented fallback (an explicit second
  `Microsoft.VisualStudio.MefComponent` VSIX asset for `Kubuno.VisualStudio.Views`, in case
  composition from `%CurrentProject%` alone is not enough) is now in `source.extension.vsixmanifest`,
  but did not change the outcome after a clean rebuild/redeploy/relaunch. The MEF composition error
  log (`ComponentModelCache\Microsoft.VisualStudio.Default.err`) shows no Kubuno-related errors
  either, and `Kubuno.VisualStudio.Views.dll` is confirmed present in the deployed extension
  folder's MEF catalog scan - so this looks like an `ILanguageClient` activation-time issue (or
  never being invoked at all for this content type) rather than a composition/export failure.
  `KubunoViewsLanguageClient.ActivateAsync` unconditionally attempts `Process.Start` once invoked
  (no early-return on a missing workspace root), so the next step is confirming, interactively,
  whether `ActivateAsync` runs at all (the "Kubuno" pane's `KubunoViewsLogHost` lines could not be
  read through `EnvDTE` `OutputWindow` automation from a script in this session - it reported 0
  panes even for the built-in "Build"/"General" panes, which is itself suspicious and may be a
  script-side automation issue rather than a real absence) - needs an interactive look at the
  "Kubuno" pane (View > Output) and/or a `/log` `ActivityLog.xml` capture. Not fixed blind in this
  session; flagged for the next one instead.

- `Kubuno.VisualStudio.Designer` (work package DSG-3, standalone library, not yet wired into the
  VSIX - see its own `INTEGRATION.md`): C# skeleton of the `.kbview` designer editor. An
  `IVsEditorFactory` (`KbviewEditorFactory`) produces a split Design | XML `WindowPane`
  (`DesignerWindowPane`/`DesignerSplitView`) where the XML half embeds a real `IVsCodeWindow` on the
  same `IVsTextLines` buffer (`CodeWindowHost`), so LSP/colorization for `.kbview` files keep working
  unchanged, and the Design half is a placeholder WPF panel (`PlaceholderDesignSurfaceHost`) behind a
  swappable `IDesignSurfaceHost`/`IDesignSurfaceHostFactory` seam for DSG-7's real embedded render
  surface. Includes a Design/XML/Split orientation tab strip and a "Use as default editor" Tools >
  Options toggle, off by default (the plain XML/text editor stays the default editor for `.kbview`
  until the designer is more than a placeholder).
- Initial VSIX skeleton for "Kubuno for Visual Studio": classic VSSDK `AsyncPackage`, .NET
  Framework 4.8, targeting Visual Studio 17.x/18.x (Community/Professional/Enterprise, amd64;
  arm64 declared but unverified).
- `ILanguageClient` for the `rust` content type: launches rust-analyzer over stdio for `.rs`
  files. rust-analyzer is located via, in order: a Tools > Options > Kubuno > Rust path override,
  `rustup which rust-analyzer`, `%USERPROFILE%\.cargo\bin\rust-analyzer.exe`, then PATH. An info
  bar with the exact fix (`rustup component add rust-analyzer`) is shown when it cannot be found.
- The Cargo workspace root passed to rust-analyzer is the folder containing the nearest
  `Cargo.toml` above the active document, or the Open Folder workspace root as a fallback
  (`Kubuno.VisualStudio.Core.CargoWorkspaceLocator`).
- A "Kubuno" pane in the Output window logs rust-analyzer discovery decisions, start/stop, and
  errors; raw LSP traffic can optionally be added via the new "LSP trace" option (Off by default).
- TextMate grammar for Rust (vendored from Visual Studio Code's built-in Rust extension, MIT
  licensed) shipped in the VSIX and registered via `languages.pkgdef`, so `.rs` files are
  colorized even before rust-analyzer starts.
- "Format Document" works through LSP formatting (rust-analyzer delegates to rustfmt); an
  optional "Format on save" setting (off by default) runs it automatically before a `.rs` file is
  saved.
- Tools > Options > Kubuno > Rust page: rust-analyzer path override, format on save, LSP trace
  level.
- `Kubuno.Cargo`: VS-independent netstandard2.0 library providing a `cargo metadata` model and  reader, an Error-List-ready parser for `cargo build/check/test --message-format=json` (ANSI-stripped  rendered text, span paths resolved against the workspace root, primary-span locations), a fluent  `CargoCommand` builder with correct Windows argument quoting, and a mockable process runner that  streams output. Tested with xUnit against real captured Cargo output.- `Kubuno.Launch`: VS-independent netstandard2.0 library resolving how to launch and debug Rust  targets (bin, example and test executables honouring `CARGO_TARGET_DIR` and `--target <triple>`,  PATH for `-C prefer-dynamic` builds, toolchain sysroot and natvis discovery) and writing  `launch.vs.json` entries for Visual Studio's native debugger. Tested with MSTest, including a real  `rustc --print sysroot` check.- Design notes: `docs/ARCHITECTURE.md` (extension model and phases) and `docs/XML_VIEWS.md`  (declarative XML views for `kubuno_ui`: format, code-behind, binding, metadata registry, hot reload).
- `Kubuno.VisualStudio.Core`: rust-analyzer/Cargo-workspace discovery logic with no VS SDK
  dependency, covered by unit tests (`tests/Kubuno.VisualStudio.Tests`, runnable with
  `dotnet test`).
- `samples/hello-rust`: a minimal Cargo project (bin + lib + example + test) used to manually
  verify the extension in an experimental Visual Studio instance.
- **Cargo workspace in Open Folder mode (phase 1b)**: every `Cargo.toml` in an opened folder now
  exposes Build/Rebuild/Clean, both from its Solution Explorer/Folder View right-click menu and
  from the Build menu when it is the active context (`Microsoft.VisualStudio.Workspace`'s
  `IFileContextProvider`/`IFileContextActionProvider`, `Workspace/CargoBuildFileContext*.cs`).
  `cargo build`/`clean` runs through `Kubuno.Cargo` with
  `--message-format=json-diagnostic-rendered-ansi`. Diagnostics become clickable Error List
  entries (file/line/column, severity, code, project) via `IBuildMessageService`; the full
  rustc-rendered text (source snippet, carets, notes) is additionally written straight to the
  **Build** Output pane via `IVsOutputWindowPane.OutputStringThreadSafe`, verified live end to
  end - see the Fixed entry below for why it isn't sent through `IBuildMessageService` too.
- **Launch and debug (phase 1c)**: every bin and example target reported by `cargo metadata`
  now appears in the "Select Startup Item" dropdown, via a `.vs\launch.vs.json` regenerated on
  workspace open and after every successful build (`Debugging/RustLaunchTargetsGenerator.cs`,
  using `Kubuno.Launch`'s `LaunchDescriptionBuilder`/`LaunchVsJsonWriter`). Its `"type": "default"`
  configurations are handled entirely by Visual Studio's own native (PDB) debug engine, so
  breakpoints in `.rs` files bind and F5/Ctrl+F5 work with no further code from this extension -
  verified live (stopped at a breakpoint in `main()`, stepped with F10). The active toolchain's
  `.natvis` files are copied to the per-user Natvis directory
  (`%USERPROFILE%\Documents\Visual Studio 2022\Visualizers`, `Debugging/NatvisInstaller.cs`) on
  each regeneration, since rustc does not embed them in the debuggee's own PDB and a VSIX-shipped
  copy would go stale against whichever toolchain version is actually installed.
- **"Kubuno: Debug Rust Test at Cursor"** (Tools menu, `Debugging/DebugRustTestAtCursorCommand.cs`):
  finds the `#[test]` function enclosing (or following) the caret in the active `.rs` document
  (`Kubuno.VisualStudio.Core.RustTestLocator`, a brace/string/comment-aware scanner), builds its
  test binary with `cargo test --no-run --message-format=json`, and launches it under the native
  debugger with `<name> --exact --nocapture --test-threads=1`
  (`Debugging/NativeDebugLauncher.cs`, `IVsDebugger4.LaunchDebugTargets4`).
- `Kubuno.Cargo`, `Kubuno.Launch` and their test projects are now part of
  `Kubuno.VisualStudio.sln` and referenced from the VSIX; `Kubuno.Launch.RustToolchain` gained
  `GetHostTriple`/`ParseHostTriple` (parses `rustc -vV`'s `host:` line), needed to resolve the PATH
  entries phase 1c's generated launch configurations require.

### Fixed

- The VSIX did not ship `System.Text.Json` (used by `Kubuno.Cargo`) or its netstandard2.0
  polyfill closure (`System.Buffers`/`System.Memory`/`System.Numerics.Vectors`/
  `System.Runtime.CompilerServices.Unsafe`/`System.Threading.Tasks.Extensions`): referencing
  `Kubuno.Cargo`/`Kubuno.Launch` alone was not enough for VSSDK's VSIX packaging to include their
  transitive dependencies, verified by inspecting the built `.vsix`'s contents. Explicit `Content`
  items (`Kubuno.VisualStudio.csproj`) now include them.
- A build diagnostic's Error List entry had an empty "Project" column (`BuildMessage.ProjectFile`
  was never set) and never showed its full rustc-rendered text in the Build Output pane (only the
  short one-line message did, despite `BuildMessage.LogMessage` being set on the same,
  Error/Warning-typed message - verified live). Reporting the rendered text as a *second*, plain
  `BuildMessage` for the same diagnostic (so the Build pane would get it independently of the
  Error List entry) was tried first and **reproducibly crashed devenv itself** (native access
  violation in `msenv.dll`, a few seconds into any build with a reported diagnostic, confirmed
  three times live and confirmed absent with a single `BuildMessage` per diagnostic) - root cause
  not isolated further. The fix that shipped instead: `ProjectFile` is now set on the one
  `BuildMessage` already sent per diagnostic, and the rendered text is written directly to the
  Build pane via `IVsOutputWindowPane.OutputStringThreadSafe` (bypassing `IBuildMessageService`
  for that part entirely) - verified live, stable across two consecutive builds with a
  diagnostic, each monitored for 30s.

### Known limitations (phase 1b/1c)

- No automatic "build before F5": Visual Studio's Open Folder `"type": "default"` launch
  configuration (what `launch.vs.json` generates) has no documented pre-launch build hook, unlike
  VS Code's `preLaunchTask`. Run Build (or Rebuild) explicitly first, the same workflow VS's own
  CMake/Makefile Open Folder support expects.
- "Debug Rust test at cursor" resolves the file's own workspace/package root and manifest by
  walking up for the nearest `Cargo.toml`; it does not yet support disambiguating two test
  functions of the same qualified name defined via `#[path]` tricks or multiple `#[test_case]`-style
  macros in one function.
- Natvis coverage is limited to the standard library's own `.natvis` files shipped by rustup
  (`liballoc`/`libcore`/`libstd`/`intrinsic`); it was verified live that this visualizes `std`
  types (not independently re-verified after reverting to install-to-per-user-directory, since no
  `String`/`Vec`/... local was in scope at the breakpoint tested - see the phase 1c report), not
  crate-specific types, which would need their own `.natvis` files (out of scope here).
