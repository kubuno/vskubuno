# Kubuno for Visual Studio

Rust language support for Visual Studio, so Kubuno modules (backend Rust/Axum, desktop apps in
Rust + `kubuno_ui`) can be developed inside Visual Studio. See `docs/ARCHITECTURE.md` for the
full roadmap; this repository currently implements phase 1a: rust-analyzer over LSP, TextMate
syntax coloring, and rustfmt (via Format Document / format-on-save).

## Repository layout

```
src/
  Kubuno.VisualStudio/        The VSIX project (classic VSSDK AsyncPackage, .NET Framework 4.8)
  Kubuno.VisualStudio.Core/   Pure logic (rust-analyzer discovery, Cargo workspace root detection)
                               with no VS SDK dependency, so it can be unit-tested with plain dotnet test
tests/
  Kubuno.VisualStudio.Tests/  Unit tests for Kubuno.VisualStudio.Core
samples/
  hello-rust/                 Tiny Cargo project (bin + lib + example + test) for manual testing
                               in an experimental Visual Studio instance
Kubuno.VisualStudio.sln
```

## What it does

- **rust-analyzer over LSP** (`Microsoft.VisualStudio.LanguageServer.Client`): an `ILanguageClient`
  for the `rust` content type (`.rs` files) launches rust-analyzer over stdio.
  - **Discovery order**: Tools > Options > Kubuno > Rust "Path override", then
    `rustup which rust-analyzer`, then `%USERPROFILE%\.cargo\bin\rust-analyzer.exe`, then PATH.
  - When rust-analyzer cannot be found anywhere, an info bar is shown with the exact fix
    (`rustup component add rust-analyzer`) - this never fails silently.
  - The workspace root passed to rust-analyzer is the folder containing the nearest `Cargo.toml`
    above the active document (or the Open Folder workspace root as a fallback), computed by
    `Kubuno.VisualStudio.Core.CargoWorkspaceLocator`.
  - All of this (which path was chosen and why, start/stop, errors, and - opt-in - raw LSP
    traffic) is logged to a **"Kubuno" pane in the Output window**.
- **Syntax coloring before rust-analyzer is ready**: a TextMate grammar for Rust is shipped in the
  VSIX (`src/Kubuno.VisualStudio/Grammars/rust.tmLanguage.json`, vendored from Visual Studio Code's
  built-in Rust extension - see `Grammars/THIRD-PARTY-NOTICES.md` for provenance and licenses) and
  registered via `languages.pkgdef`. This colorizes `.rs` files independently of the language
  client, which is why the file already looks right the instant it opens.
- **rustfmt**: "Format Document" (`Edit.FormatDocument`) works through the LSP formatting request,
  which rust-analyzer delegates to rustfmt. An optional **"Format on save"** setting (off by
  default, Tools > Options > Kubuno > Rust) runs Format Document just before a `.rs` file is saved.
- **Cargo workspace in Open Folder mode**: every `Cargo.toml` in an opened folder (or a subfolder)
  exposes **Build**/**Rebuild**/**Clean**, both from its right-click menu (Solution Explorer/Folder
  View) and from the **Build** menu when it is the active context. This uses VS's Open Folder
  workspace extensibility (`Microsoft.VisualStudio.Workspace`'s `IFileContextProvider` /
  `IFileContextActionProvider` - `src/Kubuno.VisualStudio/Workspace/`), not a declarative
  `tasks.vs.json`: the goal is clickable, structured diagnostics (file/line/column, severity,
  code, project), which needs `cargo build/clean --message-format=json-diagnostic-rendered-ansi`
  parsed by `Kubuno.Cargo` - a static task file's generic output-window error matching can't do
  that. `cargo` runs through `Kubuno.Cargo`'s `CargoCommand`/`ProcessRunner`/`CargoMessageParser`.
  Diagnostics become clickable **Error List** entries through Open Folder's own
  `IBuildMessageService` (any `BuildMessage.TaskType` other than `None`); the full rustc-rendered
  text (source snippet, carets, notes) is written straight to the **Build** Output pane via
  `IVsOutputWindowPane.OutputStringThreadSafe` rather than through `IBuildMessageService` too -
  verified live that sending it as a second `BuildMessage` instead reproducibly crashed devenv
  (see `Workspace/CargoBuildMessageReporter.cs` and the CHANGELOG for the full story).
- **Launch and debug**: every `bin` and `example` target reported by `cargo metadata` appears in
  the **Select Startup Item** dropdown. This is driven by a `.vs\launch.vs.json` the extension
  generates - on Open Folder workspace open, and after every successful Build/Rebuild
  (`src/Kubuno.VisualStudio/Debugging/RustLaunchTargetsGenerator.cs`, built on `Kubuno.Launch`'s
  `LaunchDescriptionBuilder`/`LaunchVsJsonWriter`) - rather than a live
  `ILaunchDebugTargetProvider`/`IVsDebugLaunchTargetProvider` MEF component: those interfaces
  exist but are undocumented beyond their member names and version-fragmented (5 and 3 versions
  respectively), so implementing against them with no way to compile-check intermediate
  assumptions (unlike `IFileContextProvider`, whose shape could be verified against the real
  assemblies before writing the real implementation - see the design-choices note in
  `RustLaunchTargetsGenerator.cs`) was judged too high-risk. A generated `launch.vs.json`'s
  `"type": "default"` configurations are handled entirely by Visual Studio's own native (MSVC/PDB)
  debug engine once written, so F5/Ctrl+F5 and breakpoints in `.rs` files work with **no further
  launch code from this extension** - the one thing this route does not give is an automatic
  "build before F5" hook (see Known limitations below).
- **"Kubuno: Debug Rust Test at Cursor"** (Tools menu): with the caret in or just before a
  `#[test]` function in the active `.rs` file, builds that test's binary
  (`cargo test --no-run --message-format=json`) and launches it under the native debugger with
  `<name> --exact --nocapture --test-threads=1`, via `IVsDebugger4.LaunchDebugTargets4`
  (`src/Kubuno.VisualStudio/Debugging/NativeDebugLauncher.cs`). The enclosing test's fully
  qualified name (module path + function name) is found by
  `Kubuno.VisualStudio.Core.RustTestLocator`, a small brace/string/comment-aware text scanner -
  no semantic model or rust-analyzer round-trip needed.

### Options (Tools > Options > Kubuno > Rust)

| Option | Default | Effect |
|---|---|---|
| Path override | (empty) | Force a specific `rust-analyzer.exe` path, skipping auto-discovery. |
| Format on save | Off | Format `.rs` documents with rustfmt right before they are saved. |
| LSP trace | Off | `Off` logs only the startup line and errors. `Messages` adds one line per LSP request/response/notification. `Verbose` adds full StreamJsonRpc detail (raw JSON payloads). Startup and errors are always logged regardless of this setting. |

## Prerequisites

- Visual Studio 2026 (18.x) or Visual Studio 2022 (17.x), any SKU (Community/Professional/Enterprise).
  The "Visual Studio extension development" workload is **not required** to build - the SDK comes
  from NuGet.
- .NET Framework 4.8 targeting pack (developer pack) - normally already present with Visual Studio.
- A Rust toolchain with rust-analyzer for actually using the extension (not for building it):
  `rustup component add rust-analyzer rust-src`.

## Build

From this directory, using Visual Studio's own MSBuild (adjust the path/edition to your install):

```powershell
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild Kubuno.VisualStudio.sln -t:Restore -p:Configuration=Debug
& $msbuild Kubuno.VisualStudio.sln -t:Build   -p:Configuration=Debug
# Release:
& $msbuild Kubuno.VisualStudio.sln -t:Restore -p:Configuration=Release
& $msbuild Kubuno.VisualStudio.sln -t:Build   -p:Configuration=Release
```

This produces `src\Kubuno.VisualStudio\bin\<Configuration>\Kubuno.VisualStudio.vsix`.

Notes:
- Use separate `Restore`/`Build` invocations rather than a single `Rebuild` across the whole
  solution: `Rebuild` runs `Clean` then `Build` for every project in one pass, which has been
  observed to race between the SDK-style `Kubuno.VisualStudio.Core` project's generated
  `.editorconfig` being deleted and regenerated. `Clean` then `Restore` then `Build` as separate
  invocations avoids it.
- `dotnet build`/`dotnet test` work for `Kubuno.VisualStudio.Core` and
  `tests\Kubuno.VisualStudio.Tests` on their own (see Tests below), but **not** for
  `Kubuno.VisualStudio` itself or the `.sln` as a whole - the VSIX project depends on
  `Microsoft.VsSDK.targets`, which ships inside a full Visual Studio install, not the .NET SDK.
- A `Debug` build also deploys straight into the experimental instance (`DeployExtension=True`),
  via the same MSBuild run - see "Run and debug" below.

## Tests

`Kubuno.VisualStudio.Core` holds the pure logic (`RustAnalyzerLocator`, `CargoWorkspaceLocator`)
with no VS SDK dependency, specifically so it is testable without a Visual Studio host:

```powershell
dotnet test tests\Kubuno.VisualStudio.Tests\Kubuno.VisualStudio.Tests.csproj
```

If this repository's path is on a **mapped network drive** (e.g. `Z:`), .NET Framework's
loader-from-remote-source restriction can make the MSTest adapter fail to load
(`FileLoadException`, HRESULT `0x80131515`). Work around it for the duration of the command:

```powershell
$env:COMPLUS_LoadFromRemoteSources = "1"
dotnet test tests\Kubuno.VisualStudio.Tests\Kubuno.VisualStudio.Tests.csproj
```

(A local, non-mapped path does not need this.)

## Install / debug in an experimental instance

The cleanest loop is the one MSBuild already wires up for `Debug`: build, and it deploys itself.

```powershell
& $msbuild Kubuno.VisualStudio.sln -t:Build -p:Configuration=Debug
& "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\devenv.exe" /rootsuffix Exp "path\to\a\cargo\folder"
```

Equivalently, install the packaged VSIX by hand into the experimental hive:

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\VSIXInstaller.exe" /rootsuffix:Exp src\Kubuno.VisualStudio\bin\Debug\Kubuno.VisualStudio.vsix
```

Then open `samples\hello-rust` as a folder (`devenv /rootsuffix Exp samples\hello-rust`), open
`src\main.rs`, and check the "Kubuno" pane in View > Output for the rust-analyzer startup line.

Debugging the extension's own C# code: open `Kubuno.VisualStudio.sln` in a normal (non-experimental)
Visual Studio instance and press F5 on the `Kubuno.VisualStudio` project - its debug settings
already launch `devenv.exe /rootsuffix Exp`.

## Third-party code

`src/Kubuno.VisualStudio/Grammars/` ships a TextMate grammar for Rust vendored from Visual Studio
Code's built-in Rust extension (itself sourced from `dustypomerleau/rust-syntax`), both MIT
licensed. See `Grammars/THIRD-PARTY-NOTICES.md` for the full attribution and the (single, cosmetic)
change made to the vendored copy.

## Known limitations

- **No automatic "build before F5"**: a `launch.vs.json` `"type": "default"` configuration (what
  the Select Startup Item dropdown is built from) has no documented pre-launch build hook, unlike
  VS Code's `preLaunchTask`. Build (or Rebuild) explicitly first - the same workflow VS's own
  CMake/Makefile Open Folder support expects for a custom build system.
- **Select Startup Item only lists `bin`/`example` targets**, not test binaries: a test binary's
  name is hash-suffixed and only known after `cargo test --no-run` runs, so it can't be listed
  ahead of time the way `launch.vs.json` needs. Use "Kubuno: Debug Rust Test at Cursor" instead.
- `.vs\launch.vs.json` is regenerated on workspace open and after a successful Build/Rebuild, not
  on every `Cargo.toml`/source-file edit; if a target's name or kind changes without a build in
  between (e.g. editing `Cargo.toml` by hand), rerun Build (or Rebuild) to refresh it.
- **Select Startup Item only lists targets whose executable already exists on disk** at the time
  VS reads `launch.vs.json` - confirmed live: right after opening a folder where only one of many
  `[[bin]]`/`[[example]]` targets had ever been built, the dropdown offered only that one. This is
  VS's own behavior (a sensible one - it won't offer to run something that isn't there), not
  something this extension controls; build the targets you want to see listed first.
- **Automatic pre-selection of a default startup item does not reliably take live effect when the
  opened folder's own `Cargo.toml` is a virtual `[workspace]`-only manifest** (a folder with
  `[workspace] members = [...]` and no `[package]` of its own - very common for a multi-crate
  desktop app). `RustLaunchTargetsGenerator` computes a sensible default in this case too (see
  `StartupItemSelector.SelectDefaultBinTargetForWorkspace`'s own remarks: workspace
  `default-members`, then a `shell/`-directory convention, then "the first bin") and writes it to
  `.vs\ProjectSettings.json`, but confirmed live that VS's own toolbar still shows "Sélectionner un
  élément de démarrage..." on open and rewrites that file back to its own "Aucune configuration"
  placeholder - even editing the file by hand while the folder is already open, or changing window
  focus, does not make `Debug.Start` available. The developer has to open the "Select Startup
  Item" dropdown and pick the item once; after that, F5/Ctrl+F5 work normally and the file this
  extension writes is exactly what shows up. Root cause not identified (no public documentation of
  the toolbar's own refresh triggers for this Open-Folder scenario was found); a real fix would
  most likely mean getting `IProjectConfigurationService.SetCurrentProject` (mechanism 1 in
  `EnsureStartupItemSelectedAsync`'s own remarks) working live, which this project's time budget
  has twice deferred as too high-risk to implement blind.
- **A non-Rust project/solution file anywhere under the opened folder** (`.csproj`, `.vbproj`,
  `.fsproj`, `.vcxproj`, `.sln`, `.slnx` - e.g. a vendored/reference tree) makes VS's own native
  project-file discovery take over the "Select Startup Item" dropdown entirely, hiding every Cargo
  target `launch.vs.json` offers. `KubunoPackage.EnsureWorkspaceSettingsExcludeNonRustProjectsAsync`
  writes a `VSWorkspaceSettings.json` with `ExcludedItems` for exactly those directories - but only
  when that file does not already exist, so a developer's own choices there are never overridden;
  delete an unwanted auto-generated entry by hand if the heuristic (see
  `NonRustProjectExclusionScanner`'s own remarks) got a directory wrong.
- The debugger's expression evaluator speaks C++, not Rust (see `docs/ARCHITECTURE.md`'s "Known
  limits"): natvis views of `std` types work - the toolchain's own `.natvis` files (discovered via
  `Kubuno.Launch.RustToolchain.FindNatvisFiles`) are installed to the per-user Natvis directory
  (`%USERPROFILE%\Documents\Visual Studio 2022\Visualizers`, `Debugging/NatvisInstaller.cs`; VS's
  own auto-discovery mechanism for it - see the doc comment there for why PDB embedding and a
  VSIX asset were both ruled out) - but Rust expressions (method calls, trait dispatch) in the
  Watch window do not.
- Visual Studio's built-in LSP client does not let an `ILanguageClient` override the `initialize`
  request's `rootUri`; the workspace root is instead passed as the spawned rust-analyzer process's
  working directory (which rust-analyzer falls back to when `rootUri` is absent), combined with
  whatever VS's own Open Folder workspace detection already supplies. See
  `RustLanguageClient.GetWorkspaceRootAsync` for the exact logic.
- Inlay hints and code lenses through VS's LSP client are unverified (a known general limitation
  of VS's LSP client versus VS Code's, per `docs/ARCHITECTURE.md`).
- `arm64` is declared as a supported architecture in the VSIX manifest (this is a pure managed
  extension with no native P/Invoke beyond spawning a process) but has not actually been run on an
  arm64 Visual Studio - only `amd64` was verified.
