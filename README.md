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

## Known limitations (phase 1a)

- No Cargo workspace integration yet (targets from `cargo metadata`, Build/Clean/Rebuild, Error
  List) - that is phase 1b. Opening a folder works and rust-analyzer starts against the nearest
  `Cargo.toml`, but there is no "Select Startup Item" content specific to Cargo targets yet.
- No Rust debugging integration yet - phase 1c.
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
