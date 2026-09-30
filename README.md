# Kubuno for Visual Studio

Rust language support for Visual Studio, so Kubuno modules (backend Rust/Axum, desktop apps in
Rust + `kubuno_ui`) can be developed inside Visual Studio. See `docs/ARCHITECTURE.md` for the
full roadmap; this repository currently implements phase 1a: rust-analyzer over LSP, TextMate
syntax coloring, and rustfmt (via Format Document / format-on-save).

**New to this extension?** `docs/GETTING-STARTED.md` is a step-by-step guide: prerequisites,
installing the VSIX, creating a project, the `.rsproj` build/F5/Test Explorer workflow, the
`.kbview` view designer, Open Folder mode, the MCP bridge for Claude, and troubleshooting. This
README is the full reference; the getting-started guide is the shorter path to a working setup.

## Repository layout

The code is split into layers whose dependencies only go down - Core, then Rust, then the Kubuno targets (Desktop,
Web, Mobile), which never reference each other. `docs/ARCHITECTURE.md`, "Layers (as built)", has the details and the
rules (enforced by `tests/Kubuno.Architecture.Tests`).

```
src/
  Kubuno.VisualStudio/        The one VSIX: KubunoPackage (every registration, lists the layers), KubunoCommands.vsct,
                              the VSIX manifest, the unified settings manifest; ships every layer below
  Core/                       Shared Visual Studio infrastructure
    Kubuno.Core/              Layer contracts, themed dialogs, settings plumbing, Output pane, dialog gallery, MCP start
    Kubuno.Core.Logic/        Pure helpers (LSP, QuickInfo model, UI language) - no VS SDK
    Kubuno.Core.Mcp(.Bridge)/ The MCP server for Claude and its in-proc bridge (docs/MCP.md)
  Rust/                       Product-agnostic Rust support
    Kubuno.Rust/              rust-analyzer, IntelliSense, Cargo workspaces, debugging, .rsproj commands, templates
    Kubuno.Rust.Logic/        Pure logic of the above - no VS SDK
    Kubuno.Rust.Cargo/        cargo metadata, build messages, TOML (netstandard2.0)
    Kubuno.Rust.Launch/       Launch/debug environment of Rust targets (netstandard2.0)
    Kubuno.Rust.ProjectSystem/ The .rsproj CPS project type
    Kubuno.Rust.TestAdapter/  Test Explorer adapter for cargo test
    Kubuno.Rust.Debugger/     Concord component: Rust panics in the exception helper
    Kubuno.Rust.TemplateWizard/ Crate-name wizard of the templates
    Kubuno.Cargo.MSBuild.Tasks/ MSBuild tasks of the Kubuno.Rust.Sdk NuGet package
  Desktop/                    Kubuno desktop applications (kubuno_ui, .kbview)
    Kubuno.Desktop/           .kbview language client, view designer, data tooling, printing, desktop templates
    Kubuno.Desktop.Logic/     Pure logic of the above - no VS SDK
    Kubuno.Desktop.ProjectSystem/ .kbview default editor and icon in a .rsproj, Kubuno control icons
    Kubuno.Desktop.TemplateWizard/ Control item wizard
  Web/Kubuno.Web/             Kubuno web modules (skeleton, see its README.md)
  Mobile/Kubuno.Mobile/       Kubuno mobile apps (skeleton, see its README.md)
sdk/
  Kubuno.Rust.Sdk/            The MSBuild SDK of .rsproj projects
tests/                        One test project per layer (Kubuno.Core.Tests, Kubuno.Rust.Tests, Kubuno.Desktop.Tests...)
                              plus Kubuno.Architecture.Tests (the layering rules)
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
    `Kubuno.Rust.Logic.CargoWorkspaceLocator`.
  - All of this (which path was chosen and why, start/stop, errors, and - opt-in - raw LSP
    traffic) is logged to a **"Kubuno" pane in the Output window**.
- **Syntax coloring before rust-analyzer is ready**: a TextMate grammar for Rust is shipped in the
  VSIX (`src/Rust/Kubuno.Rust/Grammars/rust.tmLanguage.json`, vendored from Visual Studio Code's
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
  `IFileContextActionProvider` - `src/Rust/Kubuno.Rust/Workspace/`), not a declarative
  `tasks.vs.json`: the goal is clickable, structured diagnostics (file/line/column, severity,
  code, project), which needs `cargo build/clean --message-format=json-diagnostic-rendered-ansi`
  parsed by `Kubuno.Rust.Cargo` - a static task file's generic output-window error matching can't do
  that. `cargo` runs through `Kubuno.Rust.Cargo`'s `CargoCommand`/`ProcessRunner`/`CargoMessageParser`.
  Diagnostics become clickable **Error List** entries through Open Folder's own
  `IBuildMessageService` (any `BuildMessage.TaskType` other than `None`); the full rustc-rendered
  text (source snippet, carets, notes) is written straight to the **Build** Output pane via
  `IVsOutputWindowPane.OutputStringThreadSafe` rather than through `IBuildMessageService` too -
  verified live that sending it as a second `BuildMessage` instead reproducibly crashed devenv
  (see `Workspace/CargoBuildMessageReporter.cs` and the CHANGELOG for the full story).
- **Launch and debug**: every `bin` and `example` target reported by `cargo metadata` appears in
  the **Select Startup Item** dropdown. This is driven by a `.vs\launch.vs.json` the extension
  generates - on Open Folder workspace open, and after every successful Build/Rebuild
  (`src/Rust/Kubuno.Rust/Debugging/RustLaunchTargetsGenerator.cs`, built on `Kubuno.Rust.Launch`'s
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
  (`src/Rust/Kubuno.Rust/Debugging/NativeDebugLauncher.cs`). The enclosing test's fully
  qualified name (module path + function name) is found by
  `Kubuno.Rust.Logic.RustTestLocator`, a small brace/string/comment-aware text scanner -
  no semantic model or rust-analyzer round-trip needed.
- **"Kubuno: Generate Visual Studio Projects"** (Tools menu, or right-click a workspace-root
  `Cargo.toml` in Solution Explorer/Open Folder): see "Building Rust with MSBuild (`.rsproj`)"
  below.

### Options (Tools > Options > Kubuno)

In Visual Studio 2026 the options are native pages of the unified settings ("All settings" > Kubuno > Rust, Views,
Designer, Debugging): searchable, edited in the document-style page or in the settings JSON file, with English and
French labels. Visual Studio 2022 keeps the classic Tools > Options pages. Values set in an earlier version are
copied to the new settings once. Rust settings:

| Option | Default | Effect |
|---|---|---|
| Path override | (empty) | Force a specific `rust-analyzer.exe` path, skipping auto-discovery. |
| Format on save | Off | Format `.rs` documents with rustfmt right before they are saved. |
| Show CodeLens | On | "3 references" above Rust items. |
| Inline hints | While pressing Alt+F1 | When rust-analyzer's grey hints show, like C#'s: hold Alt+F1 (default), always, follow Visual Studio's setting, or never. The kinds (parameter names, types, method chains, closure return types, elided lifetimes...) are chosen under "Inline hints"; see `docs/INTELLISENSE.md`. Changes apply live. |
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
  observed to race between the SDK-style `Kubuno.Rust.Logic` project's generated
  `.editorconfig` being deleted and regenerated. `Clean` then `Restore` then `Build` as separate
  invocations avoids it.
- `dotnet build`/`dotnet test` work for `Kubuno.Rust.Logic` and
  `tests\Kubuno.Rust.Tests` on their own (see Tests below), but **not** for
  `Kubuno.VisualStudio` itself or the `.sln` as a whole - the VSIX project depends on
  `Microsoft.VsSDK.targets`, which ships inside a full Visual Studio install, not the .NET SDK.
- A `Debug` build also deploys straight into the experimental instance (`DeployExtension=True`),
  via the same MSBuild run - see "Run and debug" below.

## Tests

Each layer keeps its pure logic in an assembly with no VS SDK dependency (`Kubuno.Core.Logic`, `Kubuno.Rust.Logic`,
`Kubuno.Rust.Cargo`, `Kubuno.Rust.Launch`, `Kubuno.Desktop.Logic`), specifically so it is testable without a Visual
Studio host, one test project per layer:

```powershell
dotnet test tests\Kubuno.Core.Tests\Kubuno.Core.Tests.csproj
dotnet test tests\Kubuno.Rust.Tests\Kubuno.Rust.Tests.csproj
dotnet test tests\Kubuno.Desktop.Tests\Kubuno.Desktop.Tests.csproj
# ... and Kubuno.Rust.Cargo.Tests, Kubuno.Rust.Launch.Tests, Kubuno.Rust.TestAdapter.Tests,
#     Kubuno.Cargo.MSBuild.Tasks.Tests, Kubuno.Core.Mcp.Tests
```

`tests\Kubuno.Architecture.Tests` checks the layering rules on the project files and on the built assemblies of the
solution (build the solution first; it reads each project's `bin\<Configuration>\`).

If this repository's path is on a **mapped network drive** (e.g. `Z:`), .NET Framework's
loader-from-remote-source restriction can make the MSTest adapter fail to load
(`FileLoadException`, HRESULT `0x80131515`). Work around it for the duration of the command:

```powershell
$env:COMPLUS_LoadFromRemoteSources = "1"
dotnet test tests\Kubuno.Rust.Tests\Kubuno.Rust.Tests.csproj
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

## Building Rust with MSBuild (`.rsproj`)

`docs/RSPROJ.md` designs a real MSBuild/CPS project type for Cargo packages, so a `.rsproj` can sit
in a mixed `.sln` next to `.csproj`/`.vcxproj` with real Debug/Release configurations and
build-before-F5, instead of Open Folder's workspace-only mode. This repository currently ships
work packages 1-2 of that design (a pure MSBuild SDK, no CPS/VSIX code yet - see "Known
limitations" below):

- **`sdk/Kubuno.Rust.Sdk/`** (work package 1) - `Sdk/Sdk.props`/`Sdk/Sdk.targets`: `<Project
  Sdk="Kubuno.Rust.Sdk/1.1.0">` with an optional `<CargoPackage>`/`<CargoBin>` maps
  Build/Rebuild/Clean/Run/Test to cargo, `Debug`/`Release` (or any other `$(Configuration)`) to a
  cargo profile, `<CargoTargetDir>` to `CARGO_TARGET_DIR` (falling back to the environment variable
  of the same name, never overriding an explicit value), and exposes `$(TargetPath)` as the built
  `.exe`'s path (the real `compiler-artifact` path once a build has actually run; a path-convention
  fallback - the same one `Kubuno.Rust.Launch.ExecutableResolver` already uses - beforehand or when an
  incremental build is skipped). `Kubuno.Rust.Sdk.csproj` packages it as a NuGet `MSBuildSdk`
  package, mirroring `Microsoft.VisualStudio.JavaScript.SDK`'s own split (a pure-MSBuild SDK, no
  CPS dependency).
- **`src/Rust/Kubuno.Cargo.MSBuild.Tasks/`** (work package 2) - `CargoBuild`/`CargoTest`/`CargoFetch`
  MSBuild tasks, reusing `Kubuno.Rust.Cargo`'s existing `CargoCommand`/`CargoMessageParser`/
  `ProcessRunner` (the same code the Open Folder integration's `cargo build
  --message-format=json-diagnostic-rendered-ansi` support already uses) to run cargo and turn each
  parsed diagnostic into a `Log.LogError`/`LogWarning` call with file/line/column/code - a clickable
  Visual Studio Error List entry - plus the full rustc-rendered text as a plain message. Multi-
  targeted `net472`/`net10.0`, selected in `Sdk.targets` by `$(MSBuildRuntimeType)`, so the same
  package loads under both the classic MSBuild.exe Visual Studio ships and `dotnet build`'s own
  MSBuild.

### Building and packaging the SDK

```powershell
$env:PATH = "$env:USERPROFILE\.cargo\bin;C:\Program Files\dotnet;$env:PATH"

# 1. Build the task assembly (both TFMs) and stage it where Kubuno.Rust.Sdk.csproj expects it.
dotnet build src\Rust\Kubuno.Cargo.MSBuild.Tasks\Kubuno.Cargo.MSBuild.Tasks.csproj -c Release
New-Item -ItemType Directory -Force sdk\Kubuno.Rust.Sdk\tasks\net472, sdk\Kubuno.Rust.Sdk\tasks\net10.0
Copy-Item src\Rust\Kubuno.Cargo.MSBuild.Tasks\bin\Release\net472\*.dll  sdk\Kubuno.Rust.Sdk\tasks\net472\
Copy-Item src\Rust\Kubuno.Cargo.MSBuild.Tasks\bin\Release\net10.0\*.dll,*.deps.json sdk\Kubuno.Rust.Sdk\tasks\net10.0\

# 2. Pack the SDK into a local feed (a real NuGet package, not just a path import - see below for why).
dotnet pack sdk\Kubuno.Rust.Sdk\Kubuno.Rust.Sdk.csproj -c Release -o C:\kubuno-build\nuget-local-feed
```

`sdk\Kubuno.Rust.Sdk\tasks\` is a build output (gitignored), not source - rebuild it after any
change to `Kubuno.Cargo.MSBuild.Tasks`. **This workspace's own mapped drive (`Z:`) cannot be the
SDK's resolution source**: loading `Kubuno.Cargo.MSBuild.Tasks.dll` directly from `Z:` fails
`dotnet build` outright (`MSB4061: Type must be a type provided by the runtime.` - the same class
of loader-from-remote-source restriction already documented for MSTest under "Tests" below,
confirmed live with a minimal repro assembly), which is why packaging through NuGet (restored into
the local machine's package cache, always on `C:`) is the real fix, not just a fidelity choice
matching `Microsoft.VisualStudio.JavaScript.SDK`.

### Testing `samples/hello-rust.sln`

`samples/hello-rust/hello-rust.rsproj` (`<Project Sdk="Kubuno.Rust.Sdk/1.1.0">`,
`<CargoPackage>hello-rust</CargoPackage>`) and `samples/hello-rust.sln` exercise the SDK against
this repository's existing manual-testing fixture. `samples/NuGet.config` points the `Sdk="…"`
resolution at the local feed built above (once published, a real `nuget.org`/private-feed source
replaces it - see `docs/RSPROJ.md`'s own "SDK distribution" risk note):

```powershell
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild samples\hello-rust\hello-rust.rsproj -t:Restore -p:Configuration=Debug
& $msbuild samples\hello-rust\hello-rust.rsproj -t:Build -p:Configuration=Debug   # -> $(TargetPath)
& $msbuild samples\hello-rust\hello-rust.rsproj -t:Build -p:Configuration=Release
& $msbuild samples\hello-rust\hello-rust.rsproj -t:Clean -p:Configuration=Debug

# dotnet build works too (same SDK, same NuGet.config):
dotnet build samples\hello-rust\hello-rust.rsproj -c Debug
```

Building the whole `.sln` (`& $msbuild samples\hello-rust.sln -t:Build ...` / `dotnet build
samples\hello-rust.sln`) builds the `.rsproj` correctly, but its own solution-wide restore pass
prints a benign `NU1503`/"project to restore not found" warning for it first - VS's/NuGet's
solution restore graph generator does not know what to do with a project that has no
`PackageReference`/`packages.config` of its own (only an `Sdk="…"` import, which is resolved by a
separate MSBuild mechanism *before* restore ever runs); restoring the `.rsproj` directly, as shown
above, is the reliable path and produces zero warnings.

A second no-op `-t:Build` correctly logs `La cible est ignorée "CoreCompile"` (skipped, inputs/
outputs up to date) - `CoreCompile`'s own `Inputs`/`Outputs` only decide whether to shell out to
cargo at all; cargo still does its own fine-grained incremental work either way. To see a real
compiler error surface as a clickable, correctly-positioned Error List entry, copy
`samples\hello-rust` somewhere under `C:\` (never edit an exe/task assembly in place while it is
loaded from `Z:` - see above), introduce a compile error (e.g. reference an undefined identifier in
`src\main.rs`), and build:

```
src\main.rs(4,20): error E0425: cannot find value `undefined_identifier` in this scope
```

### Opening a `.rsproj` in Visual Studio (work package 3)

The VSIX registers `.rsproj` as a Common Project System (CPS) project type, the same way the
JavaScript project system registers `.esproj`:

- **`src/Rust/Kubuno.Rust.ProjectSystem/rsproj.pkgdef`** registers project type
  `{6C7C4CB5-6E36-4C6F-9C6F-9C6E9B4D4C13}` (Kubuno.Rust.Sdk's `DefaultProjectTypeGuid`, what a
  `.sln` records) with CPS as its project factory, key for key like the JS project system's own
  pkgdef.
- **`src/Rust/Kubuno.Rust.ProjectSystem/`** is the CPS host part (MEF exports, shipped as a
  `MefComponent` asset of the VSIX): the project node icon
  (`RustProjectTreePropertiesProvider` + `RustProject.imagemanifest`) and the F5 launch provider
  (below), scoped to the `RustProjectSystem` capability. It compiles against the running Visual Studio's own
  `Microsoft.VisualStudio.ProjectSystem.dll` (never shipped).
- **Everything else comes from `Kubuno.Rust.Sdk`**, which now sits on `Microsoft.Common.props`/
  `.targets` exactly like the JS SDK (CPS drives a project through that standard targets graph):
  `ProjectConfiguration` items (Debug/Release in the configuration dropdown), `ProjectCapability`
  items, `Sdk/Rules/*.xaml` (item types, the Project Properties pages - see docs/RSPROJ.md lot 11 -, file/folder properties), and a
  `**/*` glob into `None` items for Solution Explorer, excluding `target/`, `bin/`, `obj/`,
  dot-folders and project/solution files. Cargo still compiles: `CoreCompile` runs `CargoBuild`,
  `cargo clean --profile <profile>` is hooked before Microsoft.Common's `Clean`, `cargo fetch` after
  `Restore`; Build/Rebuild/Clean keep their standard definitions, so the project context menu,
  Ctrl+Shift+B and solution builds all run them, with rustc errors in the Error List
  (file/line/column/code). `.kbview` files in the project open with their usual editor (editor
  dispatch is by extension).

Verified live in the experimental instance (`samples\hello-rust.sln` and a scratch copy under
`C:\`): the project loads with its Rust node, Solution Explorer lists `src/`, `examples/`,
`tests/`, `Cargo.toml`, `Cargo.lock` (a `target/` folder inside the project stays hidden), Debug and
Release build (and Rebuild) through the solution build, and a compile error shows as
`main.rs(2,38) error E0425` in the Error List. Launch the instance with `CARGO_TARGET_DIR` set (or
set `<CargoTargetDir>`) if the build outputs must land somewhere specific.

A `.rsproj` has a single platform, **x64** (the `x86_64-pc-windows-msvc` host triple; cargo is
not given a `--target`, so artifacts stay in `<target dir>\<profile>`): a solution listing one uses
`Debug|x64`/`Release|x64`.

### F5 / Ctrl+F5 on a `.rsproj` (work package 4)

Set the project as the startup project and press F5: Visual Studio builds it through the solution
build (cargo), then `RustDebugLaunchProvider` (`src/Rust/Kubuno.Rust.ProjectSystem`) starts
`$(TargetPath)` under the native debugger. It plugs into CPS's own debug seam, the one the
JavaScript project system uses (checked by reflection against the installed CPS assemblies: a
`DebugLaunchProviderBase` exported with `[ExportDebugger("RustDebugger")]`, selected by the
`DebuggerFlavor` property that `Kubuno.Rust.Sdk`'s `Sdk.props` sets, read through its
`debugger_general.xaml` rule). Ctrl+F5 runs the same launch without the debugger.

- **Environment**: PATH is prepended with the profile directory, its `deps` folder and the Rust
  standard library directory (`Kubuno.Rust.Launch.RustDebugEnvironment`, the same logic Open Folder's
  `launch.vs.json` uses), which is what `-C prefer-dynamic` builds need (`kubuno_ui.dll`,
  `std-*.dll`); `RUST_BACKTRACE=1` is set; the Just My Code/step-filter files and the Rust panic exception
  setting are applied as for Open Folder (`docs/DEBUGGING.md`).
- **Debug settings**: the Project Properties editor's Debug page (`Sdk/Rules/rust_debug.xaml`) and its
  "Open debug launch profile UI" dialog edit the selected debugger rule's properties
  (`Sdk/Rules/rust_debugger.xaml`, `DisplayName="Local Rust Debugger"`, stored in `<project>.rsproj.user`): command
  arguments (verbatim), working directory (default: the folder of `Cargo.toml`; relative paths are
  relative to the project folder) and environment variables, one `NAME=value` per line, applied
  over Visual Studio's own environment (setting `PATH` there replaces the computed one).

Verified live in the experimental instance: F5 on `samples\hello-rust.sln` builds, then stops at a
breakpoint in `main` with `greeting : alloc::string::String = "Hello, Kubuno!"` in Locals; Ctrl+F5
runs; a scratch `.rsproj` for the desktop shell (`CargoManifestPath` pointing at
`Z:\src\desktop\windows\src\shell\Cargo.toml`) starts `kubuno-desktop` with F5 and Ctrl+F5 - the
same executable exits with `STATUS_DLL_NOT_FOUND` (0xC0000135) when started without those PATH
entries.

Start Visual Studio with the environment you build with (`cargo` on PATH, `CARGO_TARGET_DIR` if
you use one): the build and `$(TargetPath)` both follow it.

Note: `$(TargetPath)`'s path-convention fallback (used for the incremental `CoreCompile` check
before cargo has reported its artifact) assumes `<manifest dir>\target` when neither
`<CargoTargetDir>` nor `CARGO_TARGET_DIR` is set; a global `build.target-dir` in
`~/.cargo/config.toml` is not read, so the build then simply always calls cargo (which is itself
incremental). F5/Ctrl+F5 does handle that case: when the conventional executable path does not
exist, the launch asks `cargo metadata` for the real target directory.

### "Generate Visual Studio Projects" (work package 5)

From the Tools menu, or a right-click on a workspace-root `Cargo.toml` (Solution Explorer/Open
Folder), runs `cargo metadata` and creates one `.rsproj` next to each workspace member that has a
`[[bin]]` target, plus a `.sln` at the workspace root listing all of them. **Idempotent**: an
existing `.rsproj` is never overwritten (only ever created); an existing `.sln` is edited
surgically - only the missing project entries are inserted, everything else (solution folders,
other projects, formatting) is preserved. A member with no `[[bin]]` at all (library-only) is
skipped by default.

The SDK a generated `.rsproj` needs (`Sdk="Kubuno.Rust.Sdk/1.1.0"`) ships **inside the VSIX**
(`tools\SdkFeed\Kubuno.Rust.Sdk.1.1.0.nupkg`) - on first package load, the extension registers that
folder as a NuGet source in your own `NuGet.Config` (adding to it, never replacing your other
sources), so the generated project restores/builds with no manual `dotnet pack`/`NuGet.config` setup.

Verified live against `Z:\src\desktop\windows` (13 members, 5 with a `[[bin]]`), generated into a
scratch mirror (never written into that workspace itself - `<CargoManifestPath>` in each generated
`.rsproj` points back at the real manifest): opened in Visual Studio, built through the real
Solution Build Manager (4/5 succeeded; the 5th failed on a pre-existing, unrelated TOML syntax
error in that repo's own `.cargo/config.toml`), `kubuno-desktop` set as the startup project and F5
launched it under the native debugger successfully. Also verified: a completely fresh NuGet package
cache with *only* the bundled feed configured restores `Kubuno.Rust.Sdk` and builds a `.rsproj`.

### "Create a new project" templates (lot 7)

File > New > Project, filtered by Language = **Rust**, offers four project templates - **Rust
Console Application**, **Rust Library**, **Kubuno Desktop Application** (a Windows Forms-like
project in three files - `main.rs`, `main_view.kbview` and its `#[kubuno::view]` form class `main_view.rs`,
`docs/PROGRAMMING-MODEL.md` - with one dependency, `kubuno`, path-dependent on your own
`desktop/windows` checkout found at creation through `KUBUNO_DESKTOP_SRC`, default
`Z:\src\desktop\windows`, and built into a cargo target directory of its own - see
`docs/GETTING-STARTED.md`),
**Kubuno Module** (an Axum/Tokio backend module skeleton - `/health` + `/internal/*`, a
`sqlx::migrate!`-driven Postgres schema, `module.toml`, `build_kbpkg.sh` - following the module
conventions the platform's own CLAUDE.md documents) - plus three Add New Item templates: **Kubuno
View** (`.kbview` + a same-stem `.rs` code-behind), **Rust Module**, **Rust Integration Test**.
Project/crate names go through a small wizard (`Kubuno.Rust.TemplateWizard`) that computes a
Cargo-valid `$cratename$` from whatever you typed (lower-cased, invalid characters folded to `-`,
forced to start with a letter - e.g. `My App 2` -> `my-app-2`) and, for the Kubuno Module template, a
matching `$moduleid$` (`$cratename$` with `-` -> `_`) for `module.toml`'s `id`/the Postgres schema
name; see `docs/RSPROJ.md`'s lot 7 addendum for the root cause the first attempt at this hit and how
the separate-assembly fix avoids it.

**Before every release**, check that every project template builds (and runs) out of the box:

```powershell
powershell -NoProfile -File tools\test-templates.ps1 -Run
```

It instantiates each template into `C:\kubuno-build\template-tests` the way Visual Studio does, then
runs `cargo build` (no warning allowed), the program itself (`-Run`), and `MSBuild -restore` on the
generated `.rsproj` with a shared `CARGO_TARGET_DIR` (the setup of the E0463 bug documented in
`docs/RSPROJ.md`). Exit code 1 on any failure; logs are kept only then.

### Solution Explorer: views, code-behind, symbols and dependencies (lot 8)

A `.rsproj` reads like a WinForms/WPF project in Solution Explorer:

- `main_view.rs` is nested under `main_view.kbview` (same folder, same stem - the SDK adds
  `DependentUpon`; set `<EnableKbviewCodeBehindNesting>false</EnableKbviewCodeBehindNesting>` to
  turn it off).
- Expanding a `.rs` file shows its items (structs, enums, traits, impl blocks, functions, fields,
  constants, modules, macros) with Visual Studio's symbol icons; the accessibility overlay follows
  the Rust visibility (`pub`, `pub(crate)` = internal, `pub(super)` = protected, private).
  Expanding a `.kbview` shows its element tree (`x:Name` when set, else the tag). Double-click
  jumps to the item; a view element opens the Kubuno View Designer with it selected.
- A **Dependencies** node like a .NET project's (see "Dependencies, Reference Manager and crate
  manager" below).

### Dependencies, Reference Manager and crate manager (lot 10)

- **Dependencies** shows the resolved graph (`cargo metadata` for the host platform, offline first) in
  the .NET project system's categories and icons: *Procedural macros* (~ Analyzers), *Toolchain*
  (~ Frameworks: `rustc -vV`, with the sysroot crates), *Crates* (~ Packages; each expands into its own
  dependencies), *Projects* (path dependencies) and *Git*. Dev/build dependencies carry a badge;
  unresolved or yanked ones the warning icon, outdated ones an update marker (crates.io sparse index,
  cached); errors appear as child nodes. Refreshed on `Cargo.toml`/`Cargo.lock` changes.
- Context menus: Add Project Reference, Manage Crates, Update Crates (`cargo update`), Remove Unused
  Dependencies (cargo-machete, or cargo-udeps with an installed nightly), Scope to This, New Solution
  Explorer View; on a dependency: Open Documentation, Open Source Code, Update, Remove, Copy Full Path,
  Open Folder in File Explorer, Properties (F4: versions, source, path, features, type, target, license,
  repository, description).
- **Reference Manager**: Projects > Solution, Browse > Recent, search, Browse... to any crate folder;
  OK runs `cargo add --path` / `cargo remove`.
- **Crate manager** (NuGet-like): Browse (crates.io search), Installed (offline), Updates; version,
  type, default features and feature checkboxes; Install/Update/Uninstall through `cargo add`/`cargo
  remove`, output in the "Kubuno" pane. `Cargo.toml` is only ever edited by cargo.

Rust symbols come from `rust-analyzer symbols` (the same syntactic outline rust-analyzer returns
for `textDocument/documentSymbol`, without starting a second language server); view elements come
from a short-lived `kubuno-views-ls` run. Both are computed only when a node is expanded and
refreshed when the file is saved (unsaved editor changes show up after saving).

## Third-party code

`src/Rust/Kubuno.Rust/Grammars/` ships a TextMate grammar for Rust vendored from Visual Studio
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
  limits" and `docs/DEBUGGING.md`): natvis views of `std` types work - rustc embeds the toolchain's own
  `.natvis` files in every PDB it links, `kubuno_views` embeds the Kubuno types' natvis the same way, and the
  VSIX registers a copy (`Kubuno.natvis`) - but Rust expressions (method calls, trait dispatch) in the
  Watch window do not, and `self.field` must be written `self->field`.
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
