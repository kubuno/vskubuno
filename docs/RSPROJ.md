# `.rsproj`: a real MSBuild/CPS project type for Cargo packages

Status: work packages 1-6 implemented (SDK, MSBuild tasks, CPS project type, F5/Ctrl+F5, "Generate
Visual Studio Projects", Open Folder/rust-analyzer coexistence — see README.md's "Building Rust with
MSBuild (`.rsproj`)" section); lot 7 ("Create a new project" templates) partially implemented — see
its own Addendum below for exactly what shipped vs. what is still missing (the `IWizard`-based crate-
name sanitisation and `cargo generate-lockfile` step, and the "Kubuno module" Axum backend template).
The templates are now **live-verified in the real dialogs**: "Create a new project" offers "Rust" in
its language filter and lists the three project templates when searching "rust"; a Rust console
project created through that dialog builds and runs under F5; "Add New Item" on a `.rsproj` shows a
"Rust" category with the three item templates (root cause and fix in the addendum's "Root cause"
section); lot 8 (Solution Explorer nesting, symbol nodes, icons, Dependencies node) implemented and
live-verified - see its own addendum below.

Work package 5 ("Generate Visual Studio Projects", live-verified against `Z:\src\desktop\windows`
through a scratch mirror — see its own section below): the generator/planner
(`Kubuno.VisualStudio.Core/ProjectGeneration/`) is pure and unit-tested (87 tests total in
`tests/Kubuno.VisualStudio.Tests`); the command
(`Kubuno.VisualStudio/Commands/GenerateRustProjectsCommand.cs`) is reachable from the Tools menu
and from Solution Explorer/Open Folder's item context menu on a workspace-root `Cargo.toml`
(gated by `WorkspaceManifestScanner`). SDK distribution: the packed `Kubuno.Rust.Sdk` `.nupkg`
ships inside the VSIX (`tools\SdkFeed\`, verified present in the deployed extension folder) and
`RustSdkFeedInstaller` registers that folder as a NuGet source in the developer's own NuGet.Config
on package load (surgical merge, `NuGetLocalFeedRegistration`, unit-tested) — see §4's own note for
what was and wasn't exercised live.
Implementation notes that deviate from this design: the SDK now imports
`Microsoft.Common.props`/`.targets` like the JS SDK (CPS needs that targets graph), and the project
type is registered by a static `rsproj.pkgdef` mirroring the JS project system's own pkgdef (the
JS package itself carries no `[ProjectTypeRegistration]` attribute — verified by reflection).
Work package 4 (verified by reflection and live): `IDebugProfileLaunchTargetsProvider` belongs to
the managed project system, not to CPS — the seam is CPS's `DebugLaunchProviderBase` +
`[ExportDebugger(name)]`, picked by the `DebuggerFlavor` property (`debugger_general.xaml`), exactly
as the JS project system's `LaunchJsonDebugLaunchProvider`; it lives in
`Kubuno.VisualStudio.RustProjectSystem/RustDebugLaunchProvider.cs` (not `Debugging/`). The
"Débogage" property page's grid is the selected debugger flavor's own rule (`Rules/rust_debugger.xaml`,
`DisplayName="Local Rust Debugger"`, carrying the args/working dir/env properties directly — the
same shape as the installed VSIX project system's `VsixDebugger.xaml`, checked on disk); there is
no separate "Debug" page and no separate `Build.xaml` (the "Cargo" page already holds the build
settings). Launch profiles (`LaunchProfiles` capability) were not needed. A `.rsproj` is
`x64`-only (host triple). Modeled on the JavaScript project type (`.esproj`,
`Microsoft.VisualStudio.JavaScript.Sdk` + `Microsoft.VisualStudio.JavaScript.ProjectSystem`), whose
installed files in VS 2026 Community were read directly for this note. Facts checked against those
files are marked "(verified)"; everything else is this note's own proposal, and anything that would
need checking against real assemblies before coding is marked "(unverified)".

## 1. Goals and non-goals

**Goals**

- A `.rsproj` can sit in a mixed `.sln` next to `.csproj`/`.vcxproj` (the C# host side of
  `kubuno_ui`-consuming apps, or a future mixed Rust/.NET solution), with real Debug/Release
  configurations MSBuild and VS both understand natively.
- A **real startup project** — "Set as Startup Project" in Solution Explorer, not the Open Folder
  "Select Startup Item" dropdown the README already documents as flaky for a workspace-only
  manifest (`RustLaunchTargetsGenerator.EnsureStartupItemSelectedAsync`'s own remarks: neither
  `IProjectConfigurationService.SetCurrentProject` nor a direct `ProjectSettings.json` write make
  the toolbar reflect a pre-selection live; the developer has to open the dropdown once by hand). A
  project genuinely in the solution does not have this problem.
- **Build before F5** for free. The README's biggest documented gap: `launch.vs.json`'s
  `"type": "default"` configuration has no `preLaunchTask` equivalent. A real project participates
  in the Solution Build Manager, so VS's own build-before-run behavior applies with no extension
  code.
- Standard **property pages** (profile, features, binary target, args, env) instead of hand-edited
  JSON, and a normal Properties Editor tab.
- `msbuild Kubuno.sln` works in CI the same way it already does for C# projects in a solution — one
  build entry point across a polyrepo checkout that mixes Rust and .NET.
- **`Cargo.toml` stays the single source of truth.** A `.rsproj` never lists `.rs` files, never
  repeats `[dependencies]`, never repeats target definitions — it names one Cargo package and
  otherwise defers to `cargo metadata`.

**Non-goals**

- Not a replacement for rust-analyzer, Cargo's dependency resolution, or `cargo build` — `.rsproj`
  is a thin MSBuild/CPS shell around the same `Kubuno.Cargo`/`Kubuno.Launch` logic Open Folder mode
  already uses and has already proven live.
- Not a replacement for Open Folder mode; both stay supported (§5).
- Not, in phase 1, an XML-view build step or designer change; `.kbview` handling is
  extension-scoped (`KbviewEditorFactory`) and works unmodified once `.kbview` files appear as
  project items (§3).

## 2. `Kubuno.Rust.Sdk`: the MSBuild SDK

The JS project type ships in **two physically separate pieces**, and `.rsproj` should copy the
split exactly:

1. **The CPS host, inside the VSIX** — `Microsoft.VisualStudio.JavaScript.ProjectSystem.dll`
   (verified, under `Common7\IDE\Extensions\Microsoft\JavaScript`), registering the project
   factory, the `.esproj` extension, capabilities, and VS-side services. Compiled code, not on
   NuGet.
2. **The MSBuild SDK, as a NuGet package** — `Microsoft.VisualStudio.JavaScript.SDK` (verified,
   restored at `...\Shared\NuGetPackages\microsoft.visualstudio.javascript.sdk\1.0.6165494`,
   declared as a VSIX dependency in `catalog.json`). It contains only `Sdk/Sdk.props`,
   `Sdk/Sdk.targets`, `Sdk/Rules/*.xaml`, and a small task assembly built for both net472 and net6.0
   (selected by `$(MSBuildRuntimeType)`, since `dotnet build` and VS's own MSBuild must both load
   it). Its `.nuspec` is `packageType: MSBuildSdk` with **no** dependency on any CPS assembly — the
   SDK is pure MSBuild, the VSIX is pure CPS.

`Kubuno.Rust.Sdk` mirrors this: `Sdk.props`/`Sdk.targets`/`Rules/*.xaml` plus one task assembly
(`Kubuno.Cargo.MSBuild.Tasks`) reusing **existing** code (`CargoCommand`, `CargoMessageParser`,
`ProcessRunner`) instead of re-implementing cargo invocation:

- **`Sdk.props`**: `<DefaultProjectTypeGuid>` (§3), and defaults — `CargoProfile=dev`,
  `CargoTargetDir` (falling back to the `CARGO_TARGET_DIR` convention `CLAUDE.md` already
  documents, never overriding an explicit value), `CargoManifestPath` defaulting to the sibling
  `Cargo.toml`. The JS SDK's fake `<TargetFramework>net6.0</TargetFramework>` (purely so `dotnet
  restore`/`run` recognize the project) is worth copying if CI needs `dotnet msbuild` without a VS
  install — **(unverified: not tried; the README already notes `Kubuno.VisualStudio` itself needs
  `Microsoft.VsSDK.targets` from a full VS, so solution-wide CI may need VS's MSBuild regardless)**.
- **`Sdk.targets`**: declares `ProjectCapability` items (§3), imports `Rules/*.xaml` as
  `PropertyPageSchema` (`Context="Project"` or `"File;BrowseObject"`, matching the JS SDK), and —
  the load-bearing piece — **overrides `CoreCompile`** (an empty, documented extension seam inside
  `Microsoft.Common.targets`) to shell out to cargo instead of `csc`:
  ```xml
  <Target Name="CoreCompile" DependsOnTargets="CargoRestore">
    <CargoBuild ManifestPath="$(CargoManifestPath)" Package="$(CargoPackage)"
                Profile="$(CargoProfile)" TargetDir="$(CargoTargetDir)" Features="@(CargoFeature)" />
  </Target>
  ```
  `CargoBuild` is a `Microsoft.Build.Utilities.Task` that builds a `CargoCommand` with
  `--message-format=json`, runs it via `ProcessRunner`, and feeds each line through the
  already-tested `CargoMessageParser` — calling `Log.LogError`/`LogWarning` with
  `File`/`LineNumber`/`ColumnNumber`/`Code` instead of Open Folder's `IBuildMessageService`. This is
  the one genuinely new piece of Rust logic `.rsproj` needs; everything else (metadata, launch
  description, PATH/dylib environment, natvis) is reused as-is.
- **Incremental behaviour**: mirror the JS SDK's `Inputs`/`Outputs`-gated `RunNpmInstall` pattern —
  `CargoBuild`'s own `Inputs`/`Outputs` (`Cargo.toml`/`Cargo.lock`/`src/**`) only decide whether to
  shell out at all; Cargo itself still does the fine-grained incremental work.
- **`TargetPath`**: unlike the JS SDK, which repurposes it to a directory (no single artifact,
  `CopyBuildOutputToOutputDirectory=false`), a `.rsproj` has one real artifact per `[[bin]]`: the
  `.exe`, resolved via the same `ExecutableResolver`/`CargoLayout` logic `launch.vs.json` generation
  already uses.
- **`CargoRestore`**: `cargo fetch`, hooked like the JS SDK's `NPMRestore`
  (`AfterTargets="Restore;_GenerateProjectRestoreGraph"`) — NuGet-free restore for the Rust
  projects in a solution-wide `Restore`.
- **Stubs**: `ResolveAssemblyReferences`/`GetReferenceAssemblyPaths`/`GetFrameworkPaths` as no-ops,
  `NoStdLib=true` — no managed references to resolve.
- **Test target**: `cargo test --no-run --message-format=json` through the same task family,
  feeding **`Kubuno.TestAdapter`** (already in the repo) rather than reinventing discovery — the
  existing `ICargoWorkspaceSource`/`CargoWorkspaceSource` seam already abstracts "where are the
  `Cargo.toml`s" away from VS's own workspace API; a `.rsproj`-backed implementation of that
  interface is a small, isolated addition.

## 3. CPS integration

- **Project type GUID**: mint a new one (`.esproj`'s is `{54a90642-561a-4bb1-a94e-469adee60c69}`,
  verified in the pkgdef and in `Sdk.props`), registered via `[ProjectTypeRegistration(...)]` on
  the existing `KubunoPackage`, the same attribute pattern already used elsewhere in the VSIX.
- **Capabilities** (verified from the JS pkgdef and `Sdk.targets`'s `ProjectCapability` items):
  carry over `OpenProjectFile`, `HandlesOwnReload`, `ProjectConfigurationsDeclaredAsItems`,
  `ProjectPropertiesEditor`, `ProjectPropertyInterception`, `AvoidAddingProjectGuid`,
  `ConfigurableFileNesting`, `DynamicFileNesting`, `DynamicFileNestingEnabled`,
  `DynamicDependentFile`, `NoGeneralDependentFileIcon` — all directly applicable; add a
  Rust-specific `RustProjectSystem` capability (mirroring `JSProjectSystem`) this VSIX's own MEF
  exports can key off. `DependenciesTree`/`LaunchProfiles` (present in the JS pkgdef but not in its
  `Sdk.targets`, so VSIX-side) are stretch goals: a Dependencies node from `cargo metadata`, and
  multiple launch profiles (dev/release/custom args).
- **Rules XAML** (verified structure from `general.xaml`): one `<Rule>` per page with
  `Rule.Categories`, a `Rule.DataSource Persistence="ProjectFile"`, and typed properties each with
  their own `DataSource`. Proposed pages: `General.xaml` (package name, read-only from
  `Cargo.toml`; bin/example picker), `Debug.xaml` (profile, features, args, env — the same shape
  `LaunchDescriptionBuilder` already accepts), `Build.xaml` (target dir override, extra args).
  `File;BrowseObject`-context pages give every non-Rust file a normal Properties tab.
- **File globbing**: like the JS SDK's `**/*` glob into `None` items (excluding `obj/**`,
  `**/*.*proj`, `**/.*/**`, `**/node_modules/**`, purely so Solution Explorer has something to
  show), `.rsproj` globs `**/*` excluding `target/**` and dotfolders as `None`-equivalent items —
  **never** `Compile` items; Cargo compiles, MSBuild only displays. `.kbview` files fall out of this
  glob for free and open with the existing `KbviewEditorFactory` unmodified, since editor-factory
  dispatch is keyed on extension, not project type.
- **Debug launch provider** (unverified — check against the `Microsoft.VisualStudio.SDK`
  17.14.40265 assemblies `Kubuno.VisualStudio.csproj` already references, before implementing): a
  real CPS project has a documented, widely-implemented seam here —
  `IDebugProfileLaunchTargetsProvider`/`IDebugLaunchProvider` under
  `Microsoft.VisualStudio.ProjectSystem.Debug` — unlike Open Folder's
  `ILaunchDebugTargetProvider`/`IVsDebugLaunchTargetProvider`, which `RustLaunchTargetsGenerator`'s
  own remarks already found undocumented beyond member names and version-fragmented enough to
  justify today's static `launch.vs.json` workaround. If the CPS seam is as documented as it
  appears, `.rsproj` can build a `DebugLaunchSettings` straight from
  `LaunchDescriptionBuilder`'s output (the same `RustDebugEnvironment.Build` PATH-prepending logic
  that already fixes the `-C prefer-dynamic`/`kubuno_ui.dll` lesson) with no JSON file, no
  toolbar-refresh flakiness, and build-before-run for free. `NatvisInstaller` is unaffected either
  way.
- **NuGet-free restore**: `cargo fetch` via `CargoRestore` (§2) — VS's own Restore gesture already
  invokes the `Restore` target across every project regardless of type, so no VS-side plumbing
  beyond the target hook.
- **Design-time build**: should be even cheaper than JS's — Rust IntelliSense comes from
  rust-analyzer over LSP, entirely outside MSBuild, so the design-time build only evaluates the
  file glob and property values. It must **never** shell out to `cargo metadata`/`build` (§6 risk).

## 4. Generation: "Generate Visual Studio projects" (implemented)

**Command** (`Commands/GenerateRustProjectsCommand.cs`): Tools menu (auto-detects the workspace
root from the active document, else the Open Folder root) and Solution Explorer/Open Folder's
item-node context menu (`IDM_VS_CTXT_ITEMNODE`, shown only when the selection is a single
`Cargo.toml` whose own text has a `[workspace]` table — `WorkspaceManifestScanner`, no full TOML
parser needed). Runs `cargo metadata` (`CargoMetadataReader`, reused as-is) and, for a workspace,
emits one `.rsproj` per member with a `[[bin]]` target
(`<Project Sdk="Kubuno.Rust.Sdk/…"><PropertyGroup><CargoPackage>…</CargoPackage></PropertyGroup>
</Project>`; `<CargoBin>` only when the package has more than one `[[bin]]`, picked by the same
default-run/package-name/first-bin fallback `StartupItemSelector` already uses), plus a **`.sln`**
(classic format — `.slnx` deferred, see below) at the workspace root listing all of them. A member
with no `[[bin]]` (library-only) is skipped by default; `RsprojGenerationOptions.IncludeLibraryOnlyMembers`
opts in.

**Pure/testable split** (`Kubuno.VisualStudio.Core/ProjectGeneration/`, unit-tested in
`tests/Kubuno.VisualStudio.Tests/ProjectGeneration/` — 42 tests): `RsprojGenerationPlanner.Plan`
takes an already-read `CargoMetadata` + a `projectFileExists` predicate and returns one
`RsprojProjectPlanItem` per eligible member with `Action` = `Create`/`SkipExisting` — no file I/O.
`RsprojTemplate` builds the `.rsproj` text; `RsprojSolutionGenerator` builds/merges the `.sln`. The
VS command (impure) reads `cargo metadata`, calls the planner, writes files for `Create` items, and
logs to the "Kubuno" Output pane.

**Idempotency**, matching `CLAUDE.md`'s "never regenerate what the developer owns" rule already
applied to XML views: `RsprojPlanAction.SkipExisting` is decided purely from whether a file already
exists at the planned path — a `.rsproj` is **only ever created, never overwritten or diffed**
against a changed target shape (unlike the original proposal below, this was simplified: the
developer edits or deletes+regenerates by hand, matching `Kubuno.Rust.Sdk`'s own "outside VS is
still the truth" philosophy). `.sln` membership regenerates more freely — `RsprojSolutionGenerator`
edits an existing one **surgically** (only the missing `Project`/`EndProject` blocks and their 4
`ProjectConfigurationPlatforms` lines are inserted, right down to preserving the original file's
line-ending style byte-for-byte) so solution folders, other projects and manual edits survive a
re-run; project GUIDs are deterministic (MD5 of the project path — `DeterministicGuid`) so a re-run
proposes the same GUID for the same project. A workspace root with more than one existing `.sln` is
left untouched (logged) rather than guessed at.

**SDK distribution** (docs/RSPROJ.md §6's own risk): the packed `Kubuno.Rust.Sdk` `.nupkg` ships
inside the VSIX (`Kubuno.VisualStudio.csproj`'s `tools\SdkFeed\Kubuno.Rust.Sdk.1.0.0.nupkg` Content
item — verified present in the deployed extension folder after a build); `RustSdkFeedInstaller`
(`Infrastructure/`), called from `KubunoPackage.InitializeAsync`, registers that folder as a NuGet
package source in the developer's own `NuGet.Config` on package load
(`NuGetLocalFeedRegistration` — a surgical XML merge, existing sources untouched, unit-tested).
Verified live: a completely fresh, otherwise-empty NuGet package cache with a config containing
*only* a source pointing at the local feed folder restores `Kubuno.Rust.Sdk` and builds a `.rsproj`
successfully (§ below) — `RustSdkFeedInstaller`'s own write into the developer's *real* NuGet.Config
was not separately exercised live in this session, since it mutates shared, machine-wide state; the
merge logic it calls is unit-tested and the feed-resolution mechanism it depends on is the same one
just verified.

**Live test** (`Z:\src\desktop\windows`, a 13-member workspace): generated into a scratch mirror
under `C:\kubuno-build\rsproj-test\desktop-mirror\` (never written into the desktop tree itself) —
5 members have a `[[bin]]` target (`drive-app`, `kubuno-chat`, `kubuno-desktop`,
`kubuno-documents`, `kubuno-views-ls`), each got a `.rsproj` with `<CargoManifestPath>` pointing
back at the real manifest on `Z:` (the mirror's project directory differs from the manifest's own),
plus a `windows.sln` listing all 5. Opened in the experimental instance, built through the real
Solution Build Manager: 4/5 succeeded (`kubuno-desktop -> …\kubuno-desktop.exe` among them);
`kubuno-chat` failed on a pre-existing, unrelated bug in the real repo's own
`src\chat\.cargo\config.toml` (an unescaped backslash in a TOML string - Cargo itself rejects it),
left untouched. Set `kubuno-desktop` as the startup project and pressed F5: it built (already
up to date) and launched under the native debugger, its window opening with the correct
title/icon and no `STATUS_DLL_NOT_FOUND` - proof the generated project's PATH/dylib handling
(`RustDebugLaunchProvider`, work package 4) works unchanged through a mirrored, cross-drive
`CargoManifestPath`.
(This section used to claim `EnvDTE`/DTE automation returned empty `Name`/`UniqueName`/`FullName` for
a `.rsproj` node, as an open work-package-3 gap. Re-verified live during work package 6 (lot 6) against
the `desktop-mirror` solution this same section describes: `dte.Solution.Projects` correctly reports
`Name`/`UniqueName`/`FullName` for every `.rsproj` - e.g. `Name=[kubuno-desktop] UniqueName=[src\shell\
kubuno-desktop.rsproj] FullName=[C:\...\kubuno-desktop.rsproj]` - and `StartupProjects` accepts the
`UniqueName` form directly (only the bare `Name`, which VS's own C# projects also reject when
ambiguous, fails). No code change was needed; the original claim was inaccurate, not a real gap.)

**Not done in this pass**: `.slnx` (kept to classic `.sln` — simpler, universally supported, no
VS-2026-specific schema risk to verify); diffing/reporting a changed target shape for an existing
`.rsproj` (see idempotency note above — deliberately simplified instead).

## 5. Coexistence with Open Folder and rust-analyzer

Both modes stay: Open Folder is the zero-setup entry point (`devenv folder`, no generation step);
`.rsproj`/`.sln` is for a mixed-language solution, CI, or real startup-project semantics.

- `NonRustProjectExclusionScanner`/`EnsureWorkspaceSettingsExcludeNonRustProjectsAsync` (README
  "Known limitations") currently treats any `.sln`/project file under an opened folder as a foreign
  tree to hide from Open Folder's discovery. Once this extension generates `.rsproj`/`.sln` itself,
  that heuristic must special-case its own generated files (e.g. a marker or a known `Sdk=` value)
  so Open Folder still works once project files sit next to the Cargo workspace.
- **rust-analyzer's workspace-root discovery is untouched.** `CargoWorkspaceLocator` walks up to
  the nearest `Cargo.toml` regardless of whether a `.rsproj` sits next to it; a `.rs` file's
  language experience should not depend on which mode brought it into view.

## 6. Phased work packages

1. **(S) `Kubuno.Rust.Sdk` skeleton** — `Sdk.props`/`Sdk.targets`/minimal `General.xaml`,
   `CoreCompile` shelling to plain `cargo build` (no JSON parsing yet), restore/clean stubs,
   reference-resolution no-ops. Owns: new `sdk/Kubuno.Rust.Sdk/`. Test: hand-write a `.rsproj`
   against `samples/hello-rust`, `msbuild x.rsproj -t:Build` from the CLI, confirm the exe lands and
   no-op rebuilds are skipped.
2. **(M) `Kubuno.Cargo.MSBuild.Tasks`** — `CargoBuild`/`CargoTest`/`CargoFetch` tasks reusing
   `CargoCommand`/`CargoMessageParser`/`ProcessRunner`, emitting `Log.LogError`/`LogWarning` with
   file/line/column. Owns: new `src/Kubuno.Cargo.MSBuild.Tasks/`. Test: unit tests against known
   `--message-format=json` fixtures; one live check — open the generated `.rsproj` in the
   experimental instance, introduce a compile error, confirm a clickable Error List entry.
3. **(M) CPS project factory + capabilities + file glob**, in the VSIX. Owns: new
   `src/Kubuno.VisualStudio.RustProjectSystem/`, referencing the already-present
   `Microsoft.VisualStudio.SDK` package (CPS assemblies come transitively, no new heavyweight
   dependency). Test, live: open a hand-written `.rsproj` in `/rootsuffix Exp`, confirm it loads,
   Solution Explorer shows the tree with `target/` excluded and `.kbview` nested, Properties opens.
4. **(L) Debug launch provider + Debug/Build rule pages.** Owns: `Debugging/RustCpsLaunchProvider.cs`
   (new), `Rules/Debug.xaml`, `Rules/Build.xaml`. Highest-uncertainty package — verify the CPS debug
   seam against real assemblies the same way `IFileContextProvider`/`IBuildMessageService` were
   checked before, given `RustLaunchTargetsGenerator`'s own history with undocumented debug APIs.
   Test, live: F5/Ctrl+F5 on a generated project, breakpoint in `main()`, confirm build-before-F5,
   confirm the `-C prefer-dynamic` PATH fix still holds (no `STATUS_DLL_NOT_FOUND`).
5. **(S/M) Generation command + idempotency — done.** Owns: `Commands/GenerateRustProjectsCommand.cs`,
   `Kubuno.VisualStudio.Core/ProjectGeneration/` (reusing `CargoMetadataReader`),
   `Infrastructure/RustSdkFeedInstaller.cs`. See §4 above for the full writeup: unit-tested
   (42 tests), live-verified against `Z:\src\desktop\windows` through a scratch mirror (generate,
   build 4/5 through the real Solution Build Manager, F5 `kubuno-desktop` successfully) and against
   a from-scratch NuGet package cache (SDK distribution).
6. **(S) Open Folder / rust-analyzer coexistence pass — done.** Live-verified against
   `C:\kubuno-build\rsproj-test\samples\hello-rust` (the same fixture as work packages 1-2, with a
   hand-written `hello-rust.rsproj` sitting right next to `Cargo.toml`):
   - **`NonRustProjectExclusionScanner` already never hides `.rsproj`/`.sln`, by construction** —
     no code change was needed, only a regression test locking it in
     (`NonRustProjectExclusionScannerTests.NeverExcludesOurOwnGeneratedRsprojAndSolution...`).
     `.rsproj` was never in `ProjectFileExtensions` (only `.csproj`/`.vbproj`/`.fsproj`/`.vcxproj`/
     `.sln`/`.slnx` are foreign-project candidates); a `.sln` `GenerateRustProjectsCommand` produces
     always sits at the workspace root itself (0 relative segments, below the "2+ segments" threshold
     the scanner requires before it will exclude a directory at all). Verified live: opening
     `hello-rust` as a folder with `hello-rust.rsproj` present regenerated `.vs\launch.vs.json` with
     both Cargo targets and picked `hello-rust` (not the `.rsproj`, not "Active document") as the
     default Select Startup Item in `.vs\ProjectSettings.json` — no `VSWorkspaceSettings.json`
     exclusion was even written, because nothing needed excluding.
   - **rust-analyzer's workspace-root discovery needs no change either**: `RustLanguageClient`
     already falls back from `WorkspaceService.CurrentWorkspace` (Open Folder only) to the active
     document's own path (`CargoWorkspaceLocator.FindWorkspaceRoot`) when no Open Folder workspace is
     current — exactly the solution-mode case. Verified live opening `hello-rust.sln`: the "Kubuno"
     Output pane logged `Cargo workspace root: C:\...\hello-rust` (the correct package directory,
     found from `src\main.rs`'s own path) and rust-analyzer initialized; **Go To Definition** from
     `main.rs`'s call into `hello_rust::greet` landed in `lib.rs`, proving hover/diagnostics-class LSP
     features work end-to-end in solution mode, not just Open Folder.
   - **`.kbview` default editor + "Open With → Kubuno View Designer" — real bug found and fixed.**
     `KubunoPackage.cs`'s `[ProvideEditorLogicalView]` attributes for `KbviewEditorFactory` registered
     the wrong GUIDs (`{...a703...}`/`{...a704...}` labelled "Designer"/"TextView" in a comment, but
     actually `VSConstants.LOGVIEWID_TextView`/`LOGVIEWID_UserChooseView` — verified by reflecting the
     installed `Microsoft.VisualStudio.Shell.15.0.dll`'s real `VSConstants` field values); the real
     `LOGVIEWID_Designer` (`{...a702...}`) was never registered at all. Fixed to the correct GUID.
     Verified live, before/after, opening a never-before-seen `.kbview` inside `hello-rust.sln`
     (`ItemOperations.OpenFile` with the Designer logical view): before the fix it silently fell back
     to the plain text editor; after the fix it opens the real split Design/XML/Split view, rendering
     the `.kbview`'s controls live. Default double-click still opens the plain text editor either way
     (unaffected, and correct per `KbviewEditorFactory`'s own doc comment: the core text editor's
     `[ProvideEditorExtension]` priority 0x64 beats the Designer's 0x60 on purpose).

**Risks**

- **CPS complexity / undocumented corners.** The same risk that pushed Open Folder's launch
  integration to a static `launch.vs.json` could resurface for work package 4's debug launch
  provider — mitigate by verifying the target interface against installed assemblies first, and by
  keeping a `launch.vs.json`-style fallback in reserve if the CPS seam proves as fragile as Open
  Folder's.
- **Design-time build performance on a large workspace over SMB.** This repo already hit one
  .NET-Framework-on-`Z:` surprise (`COMPLUS_LoadFromRemoteSources` for MSTest); a multi-member
  workspace opened as many `.rsproj`s from a mapped drive means CPS runs one design-time build per
  project, repeatedly, over the network. Mitigate by keeping design-time builds to pure
  glob/property evaluation (never `cargo metadata`), and by caching workspace-wide data once per
  solution, mirroring `CargoWorkspaceSource`'s existing file-watcher cache.
- **SDK distribution — resolved (work package 5).** `Sdk="Kubuno.Rust.Sdk/1.0.0"` needs MSBuild to
  find that SDK. The JS model resolves it through ordinary NuGet, with VS *Setup* (not a VSIX)
  placing the package in the shared NuGet packages folder — not available to a third-party VSIX,
  which cannot participate in VS Setup's own component/workload manifest system. Adapted instead:
  the packed `.nupkg` ships as VSIX **content** (`tools\SdkFeed\`, verified present in the deployed
  extension folder) and the VSIX itself registers that folder as a NuGet source in the developer's
  NuGet.Config on package load (`RustSdkFeedInstaller`/`NuGetLocalFeedRegistration` — §4's own
  writeup). A custom MSBuild `SdkResolver` (as `Microsoft.VC.VcpkgSdkResolver` does, under
  `MSBuild\Current\Bin\SdkResolvers\`) would avoid the NuGet dependency entirely for a fully offline
  dev loop, but **whether a VSIX can register a resolver there remains unverified** — not needed
  now that the NuGet-source route is proven to work, but worth revisiting if the extra restore step
  ever proves too slow/fragile in practice.

## Addendum — "Create a new project" templates (lot 7)

Requested by the product owner: Rust and Kubuno must appear in VS's *Create a new project*
dialog (and *Add New Item*), filterable by language/platform/project type, like C# does.

**Shipped**: `.vstemplate` project templates in the VSIX (`src/Kubuno.VisualStudio/ProjectTemplates/`,
`ItemTemplates/`), packaged via two `Microsoft.VisualStudio.ProjectTemplate`/`ItemTemplate` assets in
`source.extension.vsixmanifest` (this VSSDK version needs explicit, wildcarded `Content`
`IncludeInVSIX` items too - a bare `Path=` asset with nothing else referencing those files packaged
nothing, verified live by unzipping the built `.vsix`). Custom tags on every template:
`LanguageTag=Rust`, `PlatformTag=Windows`, `ProjectTypeTag=Console` / `Library` / `Kubuno`.

- Project templates: **Rust console application**, **Rust library**, **Kubuno desktop application**
  (a `kubuno_controls::host::run_with_chrome` window loading a starter view through
  `kubuno_views::runtime::Runtime`/`FileWatcher` - the same API `kubuno-views/examples/
  view_preview.rs` uses - with a starter `main_view.kbview` + same-stem `main_view.rs` code-behind,
  deliberately named that way for lot 8's planned nesting). Its `Cargo.toml` depends on
  `kubuno-ui`/`kubuno-controls`/`kubuno-views` via **path dependencies on this machine's own
  `desktop/windows` checkout** (`Z:/src/desktop/windows/src/crates/...`, decision documented in the
  Cargo.toml itself) rather than git-tagged dependencies: this template's own bar is "must build on
  this machine", and `kubuno-ui` et al. are not (yet) published with git tags the way `kubuno/core`'s
  shared crates are (CLAUDE.md §3) - switch to `git = "https://github.com/kubuno/desktop", tag =
  "..."` once they are.
- **Not shipped this pass: a "Kubuno module" (Axum backend) project template.** Out of this
  session's actual scope (three project templates + three item templates, not four); left for a
  follow-up, tracked here so the addendum stays accurate about what exists on disk.
- Item templates (*Add New Item*): **Kubuno view** (`$fileinputname$.kbview` + same-stem
  `$fileinputname$.rs` code-behind), **Rust module file**, **Rust integration test** (under `tests/`,
  reusing `$safeprojectname$` for the crate-under-test's name).
- **Crate-name sanitisation and `cargo generate-lockfile` were tried and reverted, not shipped.**
  The plan was an `IWizard` (`RustCrateNameWizard`) computing a Cargo-safe `$saferustcratename$` and
  running `cargo generate-lockfile` after creation. Live-verified blocker:
  `Microsoft.VisualStudio.TemplateWizard.Wizard.CreateManagedInstance` loads the wizard assembly
  with a plain `Assembly.Load` that cannot see a VSIX's own private extension folder by default - the
  fix tried (self-registering `Kubuno.VisualStudio.dll`'s own codeBase via
  `[assembly: ProvideCodeBase(AssemblyName = "Kubuno.VisualStudio", ...)]`, the same mechanism
  `KubunoPackage.cs` already uses for its *other* dependencies) reproducibly broke `KubunoPackage`'s
  own load instead (`SetSite failed for package [KubunoPackage]`, VS loading a second, differently-
  probed copy of its own hosting assembly) - confirmed twice by toggling the attribute on/off against
  a clean profile. Reverted; every template now uses VS's own `$safeprojectname$` directly for the
  crate/package name (filesystem-safe, but not guaranteed to be a valid Cargo package name for every
  possible project name - e.g. one starting with a digit - documented in each template's own
  comments). A real fix needs a *separate*, small wizard assembly (its own `.dll`, not reusing
  `Kubuno.VisualStudio.dll`) so its codeBase registration cannot collide with the package's own load;
  not attempted here given the time already spent finding this root cause.
- Also fixed as part of getting a template to load at all: **`Kubuno.Mcp.Bridge` had no
  `ProvideCodeBase` entry**, a latent, pre-existing gap unrelated to lot 7 (`KubunoPackage.
  StartMcpBridgeAsync` references it directly) - hit only once something else forced eager
  resolution of it; same fix pattern as the four assemblies already registered.
- **Test, live**: `dte.Solution.AddFromTemplate` against all three project templates' installed
  `.vstemplate` files created each one correctly (`$safeprojectname$` substituted into
  `Cargo.toml`/`.rsproj`/`src/*.rs`/`src/*.kbview`, right folder layout); all three then **built
  successfully through the real Solution Build Manager** in a hand-written `.sln` referencing them
  (`RustConsoleApp -> ...\RustConsoleApp.exe`, `KubunoDesktopApp -> ...\KubunoDesktopApp.exe`, 3/3
  succeeded, 0 failed). **Not conclusively re-verified live this session: F5 on the console/desktop
  app** - `dte.Debugger.Go`/a synthetic F5 keystroke against the experimental instance repeatedly
  hung past 60-120s in this sandboxed VM regardless of which template/project was started
  (environment-level flakiness, not reproduced as a build/content problem). F5's own mechanism
  (`RustDebugLaunchProvider`, the `-C prefer-dynamic` PATH fix) is unmodified, shared code already
  live-verified for other `.rsproj` projects in work package 4/5's own tests above.

**Root cause of "the dialog never showed the templates" (fixed, live-verified).**
`dte.Solution.AddFromTemplate` (above) proves a `.vstemplate` *file* is instantiable - it says
nothing about whether "Create a new project" *discovers* it. The dialog silently drops any
`.vstemplate` whose `<ProjectType>` is not the `"Language(VsTemplate)"` value of a registered project
factory. `rsproj.pkgdef` registers `"Language(VsTemplate)"="Rust"` on `Projects\{6C7C4CB5-...}`, but
every template declared `<ProjectType>Kubuno.Rust</ProjectType>` - a language no project factory
claims, so all six templates were discarded. The same rule applies to *Add New Item*: its tree shows
the item templates whose `<ProjectType>` matches the target project's `Language(VsTemplate)`.
References: this VS install's own MSIX packaging project (`DesktopBridge\Microsoft.VisualStudio.
DesktopBridge.ProjectSystem.pkgdef`, a CPS project type with `"Language(VsTemplate)"="MSIX"` whose
templates arrive through a plain `.vstman`/`ProjectTemplate` asset exactly like ours), and the VSSDK
"basic project system" walkthrough (`ProvideProjectFactory(languageVsTemplate: "SimpleProject")` +
`<ProjectType>SimpleProject</ProjectType>`).

- **Fix**: `<ProjectType>Rust</ProjectType>` in all six `.vstemplate` files, plus an explicit
  `<TemplateID>Kubuno.Rust.<TemplateFolder></TemplateID>` (the key the dialog's recent-templates
  list uses). Nothing else is needed: the `Microsoft.VisualStudio.ProjectTemplate`/`ItemTemplate`
  VSIX assets and their build-generated `.vstman` manifests are the discovery mechanism.
- **Removed**: the `NewProjectTemplates\TemplateDirs` / `AddItemTemplates\TemplateDirs` keys added
  earlier (copied from `msbuildproj.pkgdef`). Live-tested with the `<ProjectType>` fix in place: they
  made the dialog read the template *folders* the legacy way, so each entry showed its folder name
  ("RustConsoleApplication") with no description and no tags. Without them, the entries show the real
  `<Name>`, `<Description>` and `Rust`/`Windows`/`Console` tags.
- **`InstalledTemplates.json` is not the dialog's index.** It is a list of template IDs, and it holds
  no JavaScript or Python entry either, although those languages appear in the filter. Do not use it
  to check whether a template is visible.
- **Live verification (experimental instance, UI Automation on the real dialogs)**: the language
  filter lists "Rust" (between Python and TypeScript); searching "rust" lists **Rust Console
  Application**, **Rust Library** and **Kubuno Desktop Application** with their descriptions and
  tags. A Rust console project created *through the dialog* (not DTE) produced `HelloRsproj.slnx`,
  `.rsproj`, `Cargo.toml` and `src/main.rs`, built through the Solution Build Manager (0 failed), and
  F5 hit a breakpoint on `main.rs:2` in `HelloRsproj::main` under the native debugger. *Add New Item*
  on that project shows a **Rust** category with **Kubuno View**, **Rust Module** and **Rust
  Integration Test**.
- **F5 fix found on the way**: the evaluated `$(TargetPath)` assumes `<manifest dir>\target` when
  neither `<CargoTargetDir>` nor `CARGO_TARGET_DIR` is set, but cargo also honors `build.target-dir`
  from a `.cargo/config.toml` (this machine has a per-user one). F5 then reported "The Rust
  executable ... does not exist" right after a successful build. `RustDebugLaunchProvider` now falls
  back to `cargo metadata`'s `target_directory` when `$(TargetPath)` is missing and no explicit
  target dir is set.
- **Environment notes**: after a hive reset, the experimental instance waits on the first-run screens
  ("Connectez-vous à Visual Studio" -> "Ignorez et ajoutez des comptes ultérieurement", then
  "Personnalisez votre expérience" -> "Démarrez Visual Studio"). That is why it looked hung earlier.
  Start `devenv` with the user's full PATH (machine + user) so the build finds `cargo`.
- **Template content is ASCII-only**: VS reads a template content file without a BOM in the ANSI
  code page, so a section sign or an em dash turned into mojibake in the generated `.rsproj`,
  `Cargo.toml` and `main.rs`. Those characters were replaced.

- **Crate-name casing, revisited**: given the `IWizard` route is confirmed broken for a VSIX-hosted
  assembly (above) and a second, separate wizard assembly was judged out of scope for the time
  remaining, every template's crate/package name stays `$safeprojectname$` as VS substitutes it
  (whatever case the developer typed) rather than a lowercased/hyphenated transform - documented
  directly in each generated `Cargo.toml`/`.rsproj`'s own comment and in README.md's lot 7 section.

## Addendum — Solution Explorer nesting like WinForms/WPF (lot 8)

Requested by the product owner (reference: WinForms `Form1.cs` › `Form1.Designer.cs`, `Form1.resx`,
and expandable type/member nodes under each file).

- **File nesting** — a view and its code-behind appear as one unit:
  `settings.kbview` (parent, opens the designer/XML) › `settings.rs` (handlers / view-model, same
  folder + same stem). The analogue of WPF's `MainWindow.xaml` › `MainWindow.xaml.cs`. Rust module
  names can't contain dots, so the code-behind keeps the plain `settings.rs` name; nesting is by
  stem. Implemented in the SDK with `DependentUpon` item metadata (CPS honours it), e.g. an
  `Update` item rule that sets `DependentUpon="%(Filename).kbview"` on `.rs` files that have a
  sibling `.kbview`; no hand-written item lists (Cargo.toml stays the truth, the glob stays).
- **Symbol nodes under files** (like `Form1` › `components`, `Dispose(bool)`,
  `InitializeComponent()`): an `IAttachedCollectionSourceProvider` for Solution Explorer that
  expands:
  - a `.rs` file into its items (structs, enums, impls, fns, consts) from rust-analyzer's
    `textDocument/documentSymbol`, with icons by kind and visibility (pub / private lock overlay);
  - a `.kbview` file into its element tree (`Card` › `Stack` › `Switch x:Name="notifications"`…)
    from kubuno-views-ls `documentSymbol`, double-click → select the element in the designer/XML.
  Symbols refresh on save/edit (debounced), lazily computed only when a node is expanded.
- **Dependencies node** (like "Dépendances"): the crate's Cargo dependencies from
  `cargo metadata` (read-only, grouped normal/dev/build).
- Research before coding: how Roslyn implements its Solution Explorer symbol tree
  (`Microsoft.VisualStudio.LanguageServices.Implementation.SolutionExplorer`,
  `IAttachedCollectionSourceProvider`, `IAttachedRelationProvider`), and whether an LSP-backed
  provider exists already (e.g. in the TypeScript/JS project system); CPS `DependentUpon` and
  `IProjectTreePropertiesProvider` for icons.
- Test live: the tree matches the WinForms screenshot's shape for a Kubuno desktop app created
  from the lot-7 template.
- **Icons equivalent to the WinForms/C# screenshot** (same visual language as VS):
  - symbols use the VS image catalog (`KnownMonikers`) exactly like Roslyn does, by kind:
    struct → `StructurePublic`/`Structure…`, enum → `Enumeration…`, trait → `Interface…`,
    fn/method → `Method…`, field → `Field…`, const/static → `Constant…`, module → `Module…`,
    type alias → `Typedef…`, macro → `Macro…`; visibility mapped to the same variants/overlays
    Roslyn uses: `pub` = Public (no overlay), `pub(crate)` = Internal/Friend (heart overlay),
    `pub(super)` = Protected (star overlay), private = Private (lock overlay).
    `.kbview` element nodes use a control-like moniker per family (button, checkbox, textbox,
    panel/container, list…).
  - files: a custom **`.kbview` icon** in the style of the WinForms form icon (`Form1.cs`) and a
    custom **Rust source icon** in the style of the C# file icon (`C#` glyph → Rust gear/“R”),
    shipped as an `.imagemanifest` (vector, light/dark/high-contrast variants) and bound via CPS
    (`IProjectTreePropertiesProvider`) + the file-extension image association; `Cargo.toml` gets a
    Cargo icon, and the project node keeps the orange “R”.

### Status (lot 8): implemented

**Research first.** Roslyn's Solution Explorer symbol tree (`RootSymbolTreeItemSourceProvider`,
`SymbolTreeItem`) is an exported `IAttachedCollectionSourceProvider` (Shell.15.0's
`AttachedCollectionSourceProvider<T>`) answering the `KnownRelationships.Contains` relationship for
`IVsHierarchyItem`s; its items implement `ITreeDisplayItem`/`ITreeDisplayItemWithImages`
(`KnownMonikers` by kind + accessibility), `IInvocationPattern` (double-click) and act as their own
`IAttachedCollectionSource`. Verified live: Solution Explorer only finds those patterns through
`IInteractionPatternProvider.GetPattern<T>()` (Roslyn's `BaseItem` implements it) - without it
double-click/Enter on a node did nothing. No LSP-backed provider exists in the installed VS (the
JS/TS project system shows no symbol nodes), so there was nothing to reuse.

**What shipped** (`src/Kubuno.VisualStudio/SolutionExplorer/`, pure parts in
`Kubuno.VisualStudio.Core/SolutionExplorer/`, unit-tested):

- **Nesting**: `Sdk.targets` adds `DependentUpon="%(Filename).kbview"` to the glob's `.rs` `None`
  items that have a same-stem `.kbview` sibling (an evaluation-time `Update` with a metadata
  condition - checked with MSBuild first); CPS honours it (`none.xaml` already declares
  `DependentUpon`). Opt-out: `EnableKbviewCodeBehindNesting=false`.
- **Symbol nodes, and how the language servers are reached** (the decision the lot asked for):
  - `.rs`: **`rust-analyzer symbols`**, a one-shot process fed the file on stdin. rust-analyzer's
    `documentSymbol` handler returns `Analysis::file_structure`, and `symbols` prints exactly that
    list, so the tree equals the editor's outline without a second rust-analyzer *server* (which
    would load the whole Cargo workspace again) and without depending on the editor's
    `ILanguageClient` having started (it only exists once a `.rs` is open). One rust-analyzer server
    per workspace remains the rule. The output is Rust `Debug` text, not an API: the parser is
    tolerant (unknown kinds -> generic node, bad lines skipped with parent indices kept) and pinned
    by a test on real rust-analyzer 1.98.1 output. Visibility is not in the symbol data: it is read
    back from the source between the item's node start and its name (`pub`, `pub(crate)`/`pub(in
    ..)` = Internal, `pub(super)` = Protected, else Private; trait members, trait-impl members and
    enum variants inherit Public; `#[macro_export]` makes a macro public). `let` locals are dropped
    (Roslyn stops at members too).
  - `.kbview`: **kubuno-views-ls `documentSymbol`, from a private short-lived instance**
    (initialize, didOpen, documentSymbol, shutdown). The server only answers for documents it got a
    `didOpen` for, and a `didOpen`/`didClose` pair through the editor's own connection would replace
    or drop the editor's copy of an open file, so its instance is not used.
  - Lazy (nothing runs until a node's `Items` is asked for), refreshed through a debounced (500 ms)
    `FileSystemWatcher` on save, merged in place so expanded nodes stay expanded. Unsaved edits show
    after saving (not on every keystroke).
  - Double-click: Rust items open the file at the item's name; a view element opens the designer
    (Designer logical view) and sets the XML pane's caret on the element, which the designer's own
    selection sync turns into a selection on the surface (`DesignerWindowPane.XmlTextView`, new).
  - Order: the file symbols provider is ordered *after* the hierarchy's own children
    (`HierarchyItemsProviderNames.Contains`), so `main_view.rs` comes before the `Card` element tree
    under `main_view.kbview`, like `Form1.Designer.cs` before the `Form1` class; the Dependencies
    provider is ordered *before*, so that node comes first like in a C# project.
- **Icons**: symbols use `KnownMonikers` names from `SymbolMonikerNames` (family per kind + the
  Public/Internal/Protected/Private variant; trait impl = `ImplementInterface`, inherent impl =
  `Type`); a unit test checks every name against the installed `Microsoft.VisualStudio.ImageCatalog.dll`.
  View elements get a control glyph per family (`Button`, `TextBox`, `CheckBoxChecked`,
  `StackPanel`, `ListView`...). Files: three new vector images in `RustProject.imagemanifest`
  (XAML, one source per `Background` - Light/Dark/HighContrast, `AllowColorInversion=false`): `.rs`
  (page + Rust badge, C#-file style), `.kbview` (form window, WinForms style), `Cargo.toml` (crate).
  Bound for `.rsproj` items by `RustProjectTreePropertiesProvider`, and for any `.rs`/`.kbview`
  (Open Folder included) by `ShellFileAssociations\.ext\DefaultIconMoniker` in `languages.pkgdef`
  (`Cargo.toml` is not associated there: the key is per extension and would hit every `.toml`).
- **Dependencies node**: `cargo metadata --no-deps` (the package's declared `dependencies`, now
  parsed into `CargoPackage.Dependencies`), package picked by `$(CargoPackage)` then
  `$(CargoManifestPath)` (read through `IVsBuildPropertyStorage`, so a mirrored project pointing at a
  manifest elsewhere works), grouped Crates / Dev-dependencies / Build-dependencies, refreshed when
  `Cargo.toml` changes.

**Live test** (experimental instance): a project created from **Kubuno Desktop Application** in the
real *Create a new project* dialog (UI Automation) under `C:\kubuno-build\rsproj-test\lot8\`, then
`samples\hello-rust.sln`. Solution Explorer tree read back through UI Automation:

```
KubunoLot8App
  Dependencies > Crates > kubuno-controls (path), kubuno-ui (path), kubuno-views (path)
  src
    main.rs > main_view, VIEW_PATH: &str, main() -> std::process::ExitCode
    main_view.kbview
      main_view.rs > MainViewModel > status: String; impl Default for MainViewModel > default() -> Self;
                     impl ViewModel for MainViewModel > get(..), set(..); handler_table() -> HandlerTable
      Card > Stack > status (TextField), hello (Button)
  Cargo.toml
```

Enter on `hello (Button)` opened `main_view.kbview` with the caret on line 11, column 6 (the
`Button` tag); appending `pub(super) fn lot8_added(flag: bool) {}` to `main_view.rs` on disk added
the node within the debounce, removing it removed the node. `hello-rust`: symbols under
`lib.rs`/`main.rs`/`examples`/`tests`, an empty Dependencies node (the crate has none).
Icons and dark theme: checked visually by the orchestrator (screenshot), not by UI Automation.
