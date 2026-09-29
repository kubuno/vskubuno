# `.rsproj`: a real MSBuild/CPS project type for Cargo packages

Status: work packages 1-6 implemented (SDK, MSBuild tasks, CPS project type, F5/Ctrl+F5, "Generate
Visual Studio Projects", Open Folder/rust-analyzer coexistence — see README.md's "Building Rust with
MSBuild (`.rsproj`)" section); lot 7 ("Create a new project" templates) implemented, all four
project templates shipped (Rust Console Application, Rust Library, Kubuno Desktop Application,
Kubuno Module) — see its own Addendum below for the full history, including the crate-name
sanitisation wizard's own root cause (now fixed with a separate assembly, see the addendum) and
`cargo generate-lockfile` (still not shipped, out of scope). The templates are **live-verified in
the real dialogs**: "Create a new project" offers "Rust" in its language filter and lists all four
project templates when searching "rust"; a Rust Console Application created through that dialog with
a project name containing spaces gets a Cargo-valid, sanitised crate name and builds; "Add New Item"
on a `.rsproj` shows a "Rust" category with the three item templates (root cause and fix in the
addendum's "Root cause" section); F5 itself is not conclusively re-verified live in this VM (see the
addendum - pre-existing, environment-level flakiness, not a build/content problem). Lot 8 (Solution
Explorer nesting, symbol nodes, icons, Dependencies node) implemented and live-verified - see its own
addendum below. Lot 11 (Project Properties as a searchable document tab like .NET's, with Application / Build /
Package / Code Analysis / Debug / Resources / Settings pages, Cargo.toml edited surgically, Win32 resources) - see
"Addendum - Project properties like .NET (lot 11)"; it replaces the former "Cargo" page and the legacy modal
property-page dialog.

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
debugger flavor's own rule (`Rules/rust_debugger.xaml`, `DisplayName="Local Rust Debugger"`, carrying the
args/working dir/env properties directly — the same shape as the installed VSIX project system's
`VsixDebugger.xaml`, checked on disk) still names the Start button; since lot 11 the same user-file properties are
edited on the Project Properties editor's "Debug" page (`rust_debug.xaml`) and its launch-profile dialog, and the
former "Cargo" page is gone (its properties moved to the Application and Build pages). Launch profiles
(`LaunchProfiles` capability) were not needed. A `.rsproj` is
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
  toolbar-refresh flakiness, and build-before-run for free. (`NatvisInstaller` has since been replaced by
  `RustDebuggerFiles`: see `docs/DEBUGGING.md`.)
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
  deliberately named that way for lot 8's planned nesting). Since 2026-09-29 the starter view is an
  absolute surface like a new WinForms form (docs/DESIGNER.md §14): a `<Panel DesignWidth="800"
  DesignHeight="450">` root with anchored children, and `main.rs` opens the window with that design
  size as its page area (`Runtime::design_size`, `HostOptions::client_size`), painting the view over
  the whole client area so the anchors follow every resize. Its `Cargo.toml` depends on
  `kubuno-ui`/`kubuno-controls`/`kubuno-views` via **path dependencies on this machine's own
  `desktop/windows` checkout** (`Z:/src/desktop/windows/src/crates/...`, decision documented in the
  Cargo.toml itself) rather than git-tagged dependencies: this template's own bar is "must build on
  this machine", and `kubuno-ui` et al. are not (yet) published with git tags the way `kubuno/core`'s
  shared crates are (CLAUDE.md §3) - switch to `git = "https://github.com/kubuno/desktop", tag =
  "..."` once they are.
- **Kubuno Module** (a follow-up pass shipped this): an Axum/Tokio backend module skeleton
  following the Kubuno module conventions (vskubuno's own CLAUDE.md, referencing the platform's
  CLAUDE.md sections 4/7 - separate process, SQLx/PostgreSQL with a schema dedicated to this
  module, `module.toml`, port allocation, `/internal/*` guarded by `X-Internal-Secret`, zero
  `unwrap()`, tracing, security response headers), mirroring `notes`/`tasks`'s real layout trimmed
  to a minimal, buildable-on-this-machine skeleton: `Cargo.toml` (a git-tagged `kubuno-seccomp`
  dependency, exactly like a real module - `cargo build`/`cargo clippy -- -D warnings` verified
  clean live), `module.toml`, `src/main.rs` (`/health` + `/internal/ping`), `migrations/postgres/`
  (one `sqlx::migrate!`-compatible up/down pair creating the module's own schema),
  `config.toml.example`, `build_kbpkg.sh` (copied byte-identical from a real module - see its own
  `ReplaceParameters="false"` remark below), `CHANGELOG.md`, `README.md`. `ProjectTypeTag=Kubuno`,
  same as the desktop application template.
- Item templates (*Add New Item*): **Kubuno view** (`$fileinputname$.kbview` + same-stem
  `$fileinputname$.rs` code-behind), **Rust module file**, **Rust integration test** (under `tests/`,
  reusing `$safeprojectname$` for the crate-under-test's name).
- **Crate-name sanitisation: shipped in a follow-up pass, as a separate wizard assembly.**
  `src/Kubuno.VisualStudio.TemplateWizard/` (net48, referencing only
  `Microsoft.VisualStudio.TemplateWizardInterface` + `EnvDTE`/`EnvDTE80`/`Microsoft.VisualStudio.
  Interop` - all three pinned to this VS install's own copies via `HintPath`, not NuGet packages,
  to keep every interop assembly this project touches on the SAME v18 identity: mixing the
  NuGet-packaged EnvDTE 17.x with `TemplateWizardInterface`'s own v18 `Microsoft.VisualStudio.
  Interop` dependency is a hard `CS1705` compile error, not just an `MSB3277` warning). Its
  `CrateNameWizard : IWizard` adds two tokens to every template's replacements dictionary before
  content expansion: `$cratename$` (Cargo-valid: lower-cased, non-`[a-z0-9_-]` characters folded to
  `-`, runs collapsed, forced to start with a letter - `"My App 2"` -> `my-app-2`, live-verified
  through the real dialog) and `$moduleid$` (`$cratename$` with `-` -> `_`, used by the Kubuno
  Module template's `module.toml`/schema name). All four project templates now use `$cratename$`
  for `Cargo.toml`'s `[package] name` and the `.rsproj`'s `CargoPackage`, in place of the previous
  plain `$safeprojectname$`.
  - **The root cause the previous attempt hit (self-registering `Kubuno.VisualStudio.dll`'s own
    codeBase, breaking `KubunoPackage`'s load) does not reproduce for a separate assembly** - its
    own `[assembly: ProvideCodeBase(AssemblyName = "Kubuno.VisualStudio.TemplateWizard", ...)]` in
    `KubunoPackage.cs`, next to the others, is not self-referential.
  - **A second, separate problem surfaced once that was fixed, live-verified**:
    `Microsoft.VisualStudio.TemplateWizard.Wizard.CreateManagedInstance`'s own `Assembly.Load`
    still could not find the wizard assembly even with the `ProvideCodeBase` registration correctly
    merged into the registry (confirmed present; other assemblies' identical registrations do
    resolve for MEF/package loads) - a `FileNotFoundException`, VS's own "ce modèle a tenté de
    charger un assembly de composant" dialog. Whatever resolves `RuntimeConfiguration\
    dependentAssembly\codeBase` for MEF/package loads is evidently not consulted on this
    particular code path. Fixed by forcing the assembly into the AppDomain eagerly instead of
    relying on that path at all: `KubunoPackage.InitializeAsync` now calls a new
    `PreloadTemplateWizardAssembly()` (`typeof(CrateNameWizard).Assembly.GetName()`, wrapped in
    try/catch, never allowed to fail package load) before any wizard can possibly run - once an
    assembly of a given identity is already loaded, the CLR's own identity cache satisfies any
    later `Assembly.Load` for that identity regardless of which subsystem asks.
  - `cargo generate-lockfile` (the other half of the original, reverted attempt) is still not
    shipped - out of this pass's scope, no `Cargo.lock` is generated by any template.
  (History: the first attempt reused `Kubuno.VisualStudio.dll` itself as the wizard assembly and
  was reverted for the `KubunoPackage`-breaking reason above; this pass's separate-assembly fix is
  what that revert's own note called for.)
- Also fixed as part of getting a template to load at all: **`Kubuno.Mcp.Bridge` had no
  `ProvideCodeBase` entry**, a latent, pre-existing gap unrelated to lot 7 (`KubunoPackage.
  StartMcpBridgeAsync` references it directly) - hit only once something else forced eager
  resolution of it; same fix pattern as the four assemblies already registered.
- **Test, live**: `dte.Solution.AddFromTemplate` against all four project templates' installed
  `.vstemplate` files created each one correctly (`$cratename$`/`$moduleid$`/`$safeprojectname$`
  substituted into `Cargo.toml`/`.rsproj`/`module.toml`/`src/*.rs`/`src/*.kbview`/
  `migrations/postgres/*.sql`, right folder layout, including a project named `My Module 2` ->
  `Cargo.toml`'s `name = "my-module-2"`, `module.toml`'s `id = "my_module_2"`); all four then
  **built successfully via `cargo build`** (Kubuno Module: 0 warnings, `cargo clippy -- -D
  warnings` clean too) and **through the real Solution Build Manager** (`dte.Solution.SolutionBuild.
  Build`, `LastBuildInfo = 0`, i.e. 0 failed).
  - **Re-verified through the REAL "Create a new project" dialog this pass** (UI Automation on the
    actual dialog, not `AddFromTemplate`): searching "rust" lists all **four** templates including
    **Kubuno Module**; created a **Rust Console Application** named `My App 2` at a real location -
    `Cargo.toml` came back with `name = "my-app-2"` (the crate-name sanitiser, live, through the
    real wizard pipeline) - and it built with 0 failures through `dte.Solution.SolutionBuild.Build`.
  - **F5 still not conclusively verified live** (same, pre-existing limitation this addendum
    already documented, re-confirmed this pass): the Debug toolbar's target button went into its
    launching/disabled state and never completed within the time this session waited, both via a
    synthetic `F5` keystroke and via invoking the "Local Rust Debugger" split button directly (DTE's
    `ExecuteCommand("Debug.Start")` itself returned `RPC_E_CALL_REJECTED` while a launch was already
    in flight). `devenv`'s own DTE stayed responsive throughout (not a full hang), only the launch
    itself never finished. F5's own mechanism (`RustDebugLaunchProvider`, the `-C prefer-dynamic`
    PATH fix) is unmodified by this pass; `cargo build` producing a working `my-app-2.exe` at the
    expected `$(CARGO_TARGET_DIR)` was confirmed directly.
  - **Two unrelated environment gotchas hit and worked around while re-verifying live, worth
    recording**: (1) the VSSDK `Deploy` target's copy of five specific assemblies (`Kubuno.Cargo`,
    `Kubuno.Launch`, `Kubuno.Mcp.Bridge`, `Kubuno.TestAdapter`, `Kubuno.VisualStudio.Core`) into the
    experimental extension folder is reproducibly racy in this VM - they land as 0-byte files with a
    later timestamp than the rest, causing `KubunoPackage` to fail to load
    (`FileNotFoundException` loading whichever one a given run needs first); the fix is to re-copy
    those five from the project's own `bin\Debug\` after a deploy and before launching, or rebuild
    again. (2) a `.sln`'s own `.vs\<name>\` cache remembers a "this project type is unsupported"
    migration decision **per solution file**, independent of whether the underlying cause (here, a
    not-yet-merged `rsproj.pkgdef` on a freshly reset experimental hive) is later fixed - reopening
    the same `.sln` keeps replaying the same stale verdict until that `.vs\` folder is deleted.

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

- **Crate-name casing, revisited again**: shipped in a follow-up pass. Every project template's
  crate/package name is now `$cratename$` (Cargo-valid, computed by `Kubuno.VisualStudio.
  TemplateWizard`'s `CrateNameWizard` - see its own bullet above), not VS's raw `$safeprojectname$`.

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
  by `ShellFileAssociations\.ext\DefaultIconMoniker` in `languages.pkgdef` - a plain per-extension
  registry association, independent of the project/hierarchy type, so it should cover Open Folder
  too, but (see the "Open Folder, live-verified" note below, which found the SYMBOL EXPANSION half
  of lot 8 broken there) this specific claim has not itself been visually re-confirmed live in Open
  Folder this session. (`Cargo.toml` is not associated there: the key is per extension and would
  hit every `.toml`.)
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

**Open Folder, live-verified (correction to `SymbolTreeProvider`'s own doc comment, which claims
"in a .rsproj as well as in Open Folder, since both hand out `IVsHierarchyItem`s"): symbol
expansion does NOT currently work.** Opened `Z:\src\desktop\windows` (a plain Cargo
workspace, no `.rsproj`) directly with `devenv "Z:\src\desktop\windows"` and read Solution
Explorer - "Affichage des dossiers" (Folder View) - back through UI Automation: the tree itself is
correct (`src\crates\kubuno-views\examples\views\settings.kbview`/`showcase.kbview`,
`src\crates\kubuno-views-ls\src\symbols.rs` and its siblings all listed with the right names), but
every `.rs`/`.kbview` file node reports `ExpandCollapseState.LeafNode` - not "Collapsed" (would-
expand-if-asked) - both right after the node appears and after an 8+ second wait (ruling out the
debounced/lazy load itself just being slow: `FileSymbolsSource.HasItems` is `true`, i.e. the node
should show as expandable, from construction until the first `ReloadAsync` completes and finds
nothing - `LeafNode` from the very first read means `SymbolTreeProvider.CreateForHierarchyItem`
is never being reached for an Open Folder file item at all, not that the query ran and returned
zero symbols). The **Dependencies** node correctly stays absent in Open Folder (gated on
`RustProjectCapability`, which an Open Folder workspace never has - working as designed, not a
bug). File **icons** were not independently re-confirmed visually this session (`devenv` kept
exiting under concurrent multi-agent load on the same experimental hive before a screenshot could
be taken) - the wiring itself (`ShellFileAssociations\.rs\.kbview\DefaultIconMoniker` in
`languages.pkgdef`, matched against `RustProject.imagemanifest`'s `RustFile`/`KbviewFile` ids) is a
plain per-extension registry association independent of `IAttachedCollectionSourceProvider`, so
there is no structural reason to expect it shares the symbol-expansion gap above, but this is not
yet a live-confirmed fact for Open Folder specifically. Root cause of the symbol-expansion gap not
yet isolated (needs a live `devenv` with a debugger attached to `Kubuno.VisualStudio.dll` to see
what type/relationship Open Folder's Folder View actually calls `CreateCollectionSource` with, if
it calls it at all, for a plain file node - not attempted blind here per this repo's own "always
test for real" rule) - tracked as a known limitation rather than shipped as an unverified fix.

**Open Folder follow-up (2026-09-28, designer WinForms pass).** File icons: the per-extension
association itself is confirmed working - `IVsImageService2.GetImageMonikerForFile` in the
experimental instance returns `7d2b8c4e-…:2` for `a.rs` and `…:3` for `b.kbview` (C# files return
VS's own). The images live in XAML resources of `Kubuno.VisualStudio.RustProjectSystem.dll`, which
the image service resolves by assembly NAME. Preloading that assembly from `KubunoPackage` (so Open
Folder, where CPS never loads it, would find it) was tried and REVERTED: verified visually that it
blanked every Kubuno icon, inside `.rsproj` projects too - the Toolbox showed the image service's grey
"missing image" placeholder - most likely a second copy of the assembly loaded from another context,
which a by-name pack URI cannot pick. Open Folder file icons therefore remain an open issue.
Symbol expansion in Open Folder: unchanged - `SymbolTreeProvider.CreateForHierarchyItem` is never
reached for an Open Folder file node; isolating why needs a debugger attached to the Folder View's
attached-collection lookup (not attempted blind).

## Addendum - Extended "Ajouter" project-node submenu (lot 9)

Requested by the product owner (reference: the WinForms project system's own "Ajouter" submenu -
"Référence de projet...", "Formulaire (Windows Forms)...", "Contrôle utilisateur...",
"Composant...", "Classe...", right after "Nouvel élément.../Élément existant.../Nouveau dossier").

**Placement, live-verified.** The real "Ajouter" flyout is not a distinct shell-defined `Menu` id
visible in `vsshlids.h` - it is assembled by the shell from a handful of well-known **groups**
inside `IDM_VS_CTXT_PROJNODE` that the WinForms/C# project system itself injects its own items into:
`IDG_VS_CTXT_PROJECT_ADD_REFERENCES` ("Référence de projet..."), `IDG_VS_CTXT_PROJECT_ADD_FORMS`
(the five item-template entries: "Vue Kubuno...", "Module Rust...", "Test d'intégration...",
"Exemple...", "Binaire..."), `IDG_VS_CTXT_PROJECT_ADD_MISC` ("Dépendance Cargo (crate)..."). A first
attempt parented three brand-new custom groups directly to `IDM_VS_CTXT_PROJNODE` instead - it built
and ran, but live UI Automation on the real context menu showed all seven commands rendered as
**flat top-level entries** next to "Ajouter", not nested inside its cascade. Fixed by re-parenting
every `<Button>` directly onto those three existing shell groups (no custom `<Group>` needed for
placement at all) and moving the `.rsproj`-only scoping from per-group `VisibilityConstraints` to
per-*button* ones (the groups are shared with every other project type's own items).

**Scoping**: a `[ProvideUIContextRule(..., expression: "RustProjectSystem", termValues: new[] {
"ActiveProjectCapability:RustProjectSystem" })]` on `KubunoPackage`, referenced from
`KubunoCommands.vsct`'s `VisibilityConstraints` (`guidRustProjectUIContext`) - the CPS-native way to
scope a menu item to one project capability, instead of `DynamicVisibility`+`BeforeQueryStatus`
(still used for the existing item-context-menu command, which targets a single-item *selection*
rather than a project capability).

**Item templates: not `IVsAddProjectItemDlg2` after all.** The original design reused the classic
"Add New Item" browsable dialog (`IVsAddProjectItemDlg2.AddProjectItemDlg`, from
`Common7\IDE\PublicAssemblies\Microsoft.VisualStudio.Interop.dll` - reflected live, this interface
is not in any `Microsoft.VisualStudio.Shell.Interop*` assembly in this VS install, only in that
consolidated one), preselecting the category (`"Rust"`, matching every `.vstemplate`'s own
`<ProjectType>`) and template name. It compiles and the call succeeds (a real "Ajouter un nouvel
élément" window opens, titled correctly), but **live-verified it never finishes loading its template
list** - stuck on "Chargement des modèles..." indefinitely, reproduced across a cold template-cache
rebuild (delete `Extensions\ExtensionMetadata{2.0,Cache}.mpack`, restart Exp once, restart again).
Testing the *stock* "Ajouter > Nouvel élément..." command side by side in the same session revealed
why: **Visual Studio 2026 itself no longer opens that classic dialog for this gesture either** - its
own "Nouvel élément..." now opens a small single-line "Entrez un nom de fichier ou de dossier"
prompt, not a browsable catalog. `IVsAddProjectItemDlg2` is evidently legacy infrastructure VS's own
UI has moved off of; reviving it from a third-party extension hit exactly that abandonment.
**Replaced with a themed equivalent** (`Commands\NewItemNameDialog.cs`, `DialogWindow` + `VsBrushes`,
matching the current stock command's own single-field shape) that then calls
`EnvDTE.ProjectItems.AddFromTemplate` directly against this extension's own installed, loose
`ItemTemplates\<folder>\<folder>.vstemplate` file (`Path.GetDirectoryName(Assembly.
GetExecutingAssembly().Location)`-relative, the same file the VSIX's packaged template catalog asset
is built from - no separate copy to keep in sync). **Live-verified end to end**: "Vue Kubuno..." on
a Kubuno Desktop Application project prompted for a name ("NewView"), created `NewView.kbview` +
`NewView.rs` at the project root (right-clicking the *project node* itself, not a subfolder - matches
real VS behavior for a project-node-level Add command), opened the new `.kbview` in the real
Design/XML/Split designer rendering its default content correctly, and the solution then built with
0 failures through the real Solution Build Manager (`dte.Solution.SolutionBuild.Build`).

**Référence de projet..., live-verified.** With only one `.rsproj` in the test solution, the dialog
correctly reported "Aucun autre projet .rsproj n'a été trouvé dans la solution." rather than opening
an empty list. The add/remove-via-`cargo`-on-OK path itself is exercised by the same code the Cargo
dependency dialog already proved live (below) - not separately re-verified with a second project in
this pass, for lack of time; `CargoCommandTests` covers the exact argument shapes for both `cargo
add --path`/`cargo remove` calls it builds.

**Dépendance Cargo (crate)..., live-verified with a real crate.** Typing `anyhow` and clicking OK
ran a real `cargo add anyhow`, appending `anyhow = "1.0.104"` to the target `Cargo.toml` - comments
and existing formatting untouched (cargo's own edit, never a hand rewrite). The solution then built
successfully with the new dependency present.

**Dependencies node context menu ("Dépendance Cargo (crate)...", "Supprimer" on a crate)**:
implemented via `Microsoft.Internal.VisualStudio.PlatformUI.IContextMenuPattern` on
`DependenciesTreeItem`/`DependencyTreeItem` (`SolutionExplorer\TreeItems.cs`), routed through a new
floating `KubunoDependenciesNodeContextMenu` (`IVsUIShell.ShowContextMenu` against a private
`IOleCommandTarget`, `SolutionExplorer\DependenciesNodeContextMenu.cs`) - neither synthetic node is a
real `IVsHierarchy` item with a shell-registered context menu of its own. **Not live-verified this
pass** (time-boxed out after the project-node submenu and both dialogs were already proven end to
end) - the wiring reuses the exact same `AddCargoDependencyCommand.ShowDialogAndAddAsync`/
`RemoveAsync` methods the project-node menu already exercised live, so the only genuinely untested
surface is the `IContextMenuPattern`/`ShowContextMenu` plumbing itself; flagged here rather than
silently left unmentioned, per this doc's own "say explicitly what wasn't possible" rule.

**`CargoCommand` gained `Add`/`Remove` kinds** (`Kubuno.Cargo\Commands\CargoCommand.cs`/
`CargoCommandKind.cs`) - the positional crate spec and `--path`/`--dev`/`--build` flags go through
the existing `WithExtraArgs`, no new builder surface needed. Unit-tested
(`CargoCommandTests.Add_with_path_and_features_emits_expected_arguments`/
`Remove_emits_manifest_package_and_crate_name`).

**Two more item templates**: **Rust Example** (`examples/$fileinputname$.rs`) and **Rust Binary**
(`src/bin/$fileinputname$.rs`), alongside the three lot-7 ones - same shape as Rust Integration
Test's own `tests/` folder wrapper. An XML-comment gotcha hit while writing them: a `.vstemplate`'s
leading XML comment cannot contain two consecutive hyphens (`--example`/`--bin` broke the packaged
template manifest generator with an `XmlException` at build time, not template-load time) - spelled
out in prose instead ("the example flag") in the comment; the `<Description>` element text (not a
comment) still shows the real flag.

## Addendum - Toolbox/Solution Explorer icons bug report (real install, not lot 9's own scope)

A product-owner bug report, unrelated to lot 9's own "Ajouter" work, came in mid-pass: in the user's
real (non-Exp), regularly-installed Visual Studio, the fallback "Kubuno Toolbox" tool window stayed
on its "Open a .kbview file to see its components here." placeholder despite a `.kbview` designer
being active, and Solution Explorer showed no icons at all (project node, `.rs`/`.kbview`/`Cargo.toml`).

**Toolbox: a real race found and fixed, but not the whole story.** `ToolboxToolWindow.
OnToolWindowCreated` fetched `kubuno/registry` exactly once; if the tool window is recreated from a
persisted layout before any `.kbview` document (and therefore the language client) exists for the
session, that single attempt permanently sees `ComponentRegistry.Empty` with no retry. Fixed with a
bounded retry loop (20 attempts, 1.5 s apart, mirroring `NativeToolboxInstaller`'s own precedent -
`ToolWindows\ToolboxToolWindow.cs`). **Live-verified this did NOT fully fix the reported symptom**:
reinstalled the rebuilt VSIX into the real hive (`VSIXInstaller.exe /q`), started Visual Studio twice
(template/image cache warm-up), opened the user's own `KubunoDesktopApp1` project - `main_view.kbview`
auto-opened in the designer (rendering correctly), yet "Kubuno Toolbox" still showed only its
placeholder text after 45+ seconds, well past the new 30 s retry budget. `Get-Process` confirmed
`kubuno-views-ls.exe` was genuinely running, launched from the correct newly-installed path
(`Extensions\<random>\tools\...`), ruling out "the language server binary isn't found/doesn't start"
as the cause. That narrows the real root cause to either the `initialize` handshake never completing
(`KubunoViewsLanguageClient.IsInitialized` staying false) or the `kubuno/registry` RPC call itself
failing silently once connected - **not isolated further this pass**: reading the "Kubuno" Output
pane's own log (which would show exactly which of those it is, per `KubunoViewsLogHost`'s existing
logging) needs either a live debugger attached or reliable UI automation against the Output tool
window, and repeated attempts at the latter (`View.Output`/`View.SolutionExplorer` via both DTE
`ExecuteCommand` and UI Automation `InvokePattern` on the real menu item) had no visible effect in
this environment across many retries, for reasons not resolved either - possibly an artifact of this
particular automation approach against this specific devenv session rather than a real "commands do
nothing" bug (the exact same technique worked reliably earlier in this same investigation against a
different, `/rootsuffix Exp` devenv instance). Tracked as a genuinely open issue, not shipped as
fixed - the retry loop is kept because it is a real, independent improvement either way.

**Icons: inconclusive, not re-confirmed either way.** Could not get Solution Explorer to render in
this pass's screenshots at all (see the automation difficulty above), so file/project icons were not
independently re-checked live against the real install. One positive, concrete data point: the
window's own title-bar/taskbar icon (the Kubuno "K" logo) rendered correctly and consistently across
every screenshot of the real install taken this pass, which at minimum confirms the VSIX's own
top-level icon resource loads fine there - it does not confirm or rule out the separate
`RustProject.imagemanifest`/`RustProjectImages` file-icon path `docs/RSPROJ.md`'s own earlier
"Open Folder follow-up" addendum already flagged as fragile (by-name pack URI resolution breaking if
a second copy of `Kubuno.VisualStudio.RustProjectSystem.dll` loads in another context). A dedicated
investigation pass (`docs/RSPROJ.md` search "VSIXInstaller.exe /q :rootSuffix:Exp" for the fullest
prior attempt) also could not reproduce a clean signal, for a different reason: it shared the same
`/rootsuffix Exp` hive this session's own dev `Debug`-configuration builds were repeatedly deploying
into and killing `devenv.exe` processes against, and that hive's dev-deploy folder was independently
found genuinely broken (a `FileNotFoundException` on `Kubuno.VisualStudio.dll` itself, root-caused to
a deploy race from repeated `Stop-Process`-based `devenv.exe` kills colliding with the VSSDK Deploy
target's delete-then-copy step, fixed by a clean rebuild with no `devenv.exe` holding locks) at some
point during that investigation - so its own "even a fresh VSIXInstaller install fails to load" finding
is now suspected to be contaminated by that same shared-hive corruption rather than a genuine platform
bug, and should be re-tried in an Exp hive not shared with any other concurrent dev-deploy activity
before being trusted as a real finding. **Recommendation for whoever picks this up next**: reproduce
with a debugger attached to `devenv.exe` (breakpoint in `KubunoViewsLanguageClient.
OnServerInitializedAsync` and `JsonRpcRegistryClient.FetchAsync`) rather than more black-box live
testing, which has now been tried at length across two separate passes without fully isolating either
symptom's true root cause.

## Addendum - Solution Explorer icons in a regular install: root cause and fix

**Root cause (evidence, not guesswork).** The two installs were compared file by file: the release
VSIX's installed folder and the experimental dev-deploy folder hold the same 148 files, the same
`.pkgdef`/`.imagemanifest`/`catalog.json` layout, and the same MEF parts in each hive's
`ComponentModelCache` (so neither packaging nor a Release/Debug difference was involved). What
differs is the hive's generated **`devenv.exe.config`**: VSIXInstaller (like `devenv
/updateconfiguration`) regenerates it from the pkgdefs, so in the regular hive it contained one
`<dependentAssembly><assemblyIdentity publicKeyToken=""/><codeBase href="...\Extensions\<id>\X.dll"/>`
per `[assembly: ProvideCodeBase]` of `KubunoPackage.cs` (six unsigned assemblies); MSBuild's dev
deployment never regenerates it, so the experimental hive had none. The .NET Framework refuses a
code base outside the application base (`Common7\IDE`) for an assembly without a strong name: a
standalone repro (host exe + config with such an entry) shows that any by-name `Assembly.Load` then
fails with `FileLoadException 0x80131041` ("private assembly located outside the appbase
directory") **even though the same assembly is already loaded** (via `LoadFrom`) and **without
raising `AssemblyResolve`** - whereas without the entry the load misses, `AssemblyResolve` fires and
the loaded copy is returned. The regular hive's `ComponentModelCache\...Default.err` carried the
same error for `Kubuno.Cargo`. Visual Studio's image service resolves the icons' pack URIs
(`/Kubuno.VisualStudio.RustProjectSystem;component/...` in `RustProject.imagemanifest` and
`KubunoControls.imagemanifest`) by assembly name, so every image-manifest moniker - project node,
`.rs`/`.kbview`/`Cargo.toml` file icons, `.kbview` element nodes - came out blank, while the
Toolbox icons kept working because `NativeToolboxInstaller` renders the same XAML through WPF's
`Application.LoadComponent`, which first looks among already-loaded assemblies. It also explains
earlier "a VSIXInstaller install into Exp fails too" observations, and why the dev loop never saw it.
Hypotheses (a) caches and (c) Release/Debug were ruled out: the `ImageLibrary.cache` files of both
hives differ only by the extension path length; clearing caches alone would not remove the config
entries.

**Fix.** The six `ProvideCodeBase` attributes are replaced by one `[ProvideBindingPath]` on
`KubunoPackage` (`[$RootKey$\BindingPaths\{package guid}] "$PackageFolder$"=""`). A binding path
writes nothing into `devenv.exe.config`; a by-name load misses the application base, and Visual
Studio's own resolver loads the assembly from the extension folder - the same file, hence the same
loaded copy, that MEF and the package loader use. (Strong-name signing every assembly would have been
the other valid fix; the binding path needs no key management and no `InternalsVisibleTo` changes.)
Verified on the regular hive after `VSIXInstaller /u` + `/q` of the rebuilt Release VSIX and two
starts: `devenv.exe.config` has no Kubuno entry, the MEF `.err` has no Kubuno error, the design
surface, language server and native Toolbox (49 components with icons) work.

**Fallback tool windows removed.** With the native Toolbox confirmed populated in the regular
instance, the "empty toolbox" reported there was the fallback "Kubuno Toolbox" window restored from
a saved layout (its one-shot/bounded registry fetch ran before any `.kbview` existed). It and the
"Kubuno Properties" fallback are removed (tool windows, commands, `ProvideToolWindow`
registrations, GUID constants, their WPF views and the `ToolboxViewModel` tests); with their GUIDs
unregistered, a persisted layout entry can no longer recreate them.

## Addendum - Template build fix: E0463 in a new Kubuno Desktop Application

**Report** (product owner, regular Visual Studio): a project freshly created from *Kubuno Desktop
Application* (`KubunoDesktopApp1`) failed on its first build/F5 with `error[E0463]: can't find crate
for 'kubuno_ui'` at `kubuno-views\src\runtime.rs:25`.

**Root cause (reproduced with `cargo build -v`).** The user environment sets
`CARGO_TARGET_DIR=C:\kubuno-build\desktop-target` - the target directory the `desktop` workspace
itself builds into - and the SDK passes it on (`Sdk.props`), so the new project built into the
desktop workspace's own target directory. `kubuno-ui` is `crate-type = ["dylib"]`, and cargo gives a
dylib no hash suffix: every build of it, whatever its flags, features or lock file, is written to the
same `debug\deps\kubuno_ui.dll` (twelve different `kubuno-ui-<hash>` fingerprints shared that one file
on this machine). The project's `kubuno-ui` fingerprint was still "fresh" while the DLL on disk had
since been rewritten by a desktop-workspace build (different metadata), so rustc, given
`--extern kubuno_ui=...\deps\kubuno_ui.dll` when compiling `kubuno-views`, rejected it: E0463. The
same project built from scratch into a directory of its own succeeds - neither the missing
`.cargo/config.toml` (`-C prefer-dynamic`) nor the crate type is the cause: rustc already links std
dynamically when a dylib dependency needs it (the exe imports `kubuno_ui.dll` and
`std-<hash>.dll`, verified with `dumpbin /dependents`). The clobbering also works the other way (a
template project build can break the next desktop-workspace build).

**Fix (template, no SDK change).**
- The template's `.rsproj` sets `<CargoTargetDir>`: `$(CARGO_TARGET_DIR)\rsproj\$(CargoPackage)` when
  `CARGO_TARGET_DIR` is set (keeps the build on the local disk that variable was chosen for), else
  `$(MSBuildProjectDirectory)\target` (also ignores a shared `build.target-dir` from a user-wide
  `.cargo/config.toml`). `$(TargetPath)`, `CargoFetch`/`CargoBuild` and F5 all follow that property;
  F5's PATH (profile directory, `deps`, the toolchain's std directory) already covers
  `kubuno_ui.dll` built there.
- The desktop checkout is no longer hardcoded: `CrateNameWizard` adds `$kubunodesktopsrc$`, resolved
  at creation from `KUBUNO_DESKTOP_SRC` (the `windows` workspace or the repository root), default
  `Z:\src\desktop\windows`, written with forward slashes into `Cargo.toml`'s three path dependencies
  and the `.rsproj`'s `<KubunoDesktopSrc>`. A git dependency on `github.com/kubuno/desktop` is not
  possible yet (its `windows/` tree is not published). A `KubunoCheckDesktopSources` target (before
  `CargoRestore`/`CoreCompile`) fails with `KUBUNO0001` and instructions when that folder is missing.
- Considered and rejected: `crate-type = ["rlib", "dylib"]` on `kubuno-ui` (it is dylib-only on
  purpose, see its `Cargo.toml`: the component gallery must exercise the DLL the apps load) and a
  `.cargo/config.toml` in the template (not needed - see above - and it cannot override the
  `CARGO_TARGET_DIR` environment variable, which is what caused the collision).
- The other three templates have no dylib dependency (hashed rlibs only), so a shared target
  directory is harmless for them; they are unchanged.

**Template build check: `tools/test-templates.ps1`.** Run before every release. It instantiates each
project template into `C:\kubuno-build\template-tests\<timestamp>` exactly as Visual Studio does (same
files, same token values as `CrateNameWizard`, fails on any unreplaced `$token$`), then per template:
`cargo build` in an own target directory (fails on any warning), with `-Run` runs the result (console
exit code; desktop app window up for 6 s with F5's PATH, and its page area - the client area below the
34-DIP Kubuno caption, read per-monitor-DPI-aware - equal to the view's `DesignWidth` x `DesignHeight`
within 1.5 DIP), and `MSBuild -restore` on the `.rsproj` with
`CARGO_TARGET_DIR` pointing at a shared directory, failing if `kubuno_ui.dll` lands in it. Verified to
fail on the previous template (`kubuno_ui.dll` in the shared directory) and on a missing desktop
checkout (`KUBUNO0001`), and to pass on all four templates. Target directories are deleted as it goes
(a desktop build is over a gigabyte) unless `-Keep`.

## Addendum - Dependencies node, Reference Manager and crate manager like .NET (lot 10)

Requested by the product owner (screenshots of a WinForms project's "Dépendances" node): the `.rsproj`
Dependencies experience as complete as .NET's.

**Research.**
- The .NET node is not CPS itself but the managed project system (`dotnet/project-system`): a
  `DependenciesTree` capability, dependency "subtree providers" per kind, `IProjectTreePropertiesProvider`
  for customization. None of it is reusable by a non-managed project type, so the node stays an
  `IAttachedCollectionSourceProvider` node (lot 8) and reproduces the design. Its category icons were read
  from the installed `Microsoft.VisualStudio.ProjectSystem.Managed*.dll` (KnownMonikers they reference):
  `ReferenceGroup`(`Warning`/`Error`) for the root, `CodeInformation` (Analyzers), `Framework`
  (Frameworks), `PackageReference`/`NuGetNoColor`(`Warning`) (Packages), `Application`(`Warning`)
  (Projects); French names from its satellite resources ("Dépendances", "Analyseurs", "Frameworks",
  "Projets", "Chemin d'accès"...). A unit test checks every moniker name against the image catalog.
- Solution Explorer patterns (`Microsoft.Internal.VisualStudio.PlatformUI`, reflected from
  Shell.Framework): `IBrowsablePattern.GetBrowseObject` feeds F4/Alt+Enter, `IContextMenuPattern` the
  menus, `IPivotItemProviderPattern.CreatePivotRootItem` "New Solution Explorer View", `IRefreshPattern`
  the Refresh button. **"Scope to This" also needs the provider to report the "Contains" relationship
  for the node** (`SolutionNavigatorCommandTarget.GetPivotInfo` filters
  `IAttachedCollectionService.GetRelationships(item)` on the current relationship, read from its IL):
  `GetRelationships` returned nothing before, so the command stayed hidden.
- Standard commands placed in our menus: `guidVSStd11` (`{D63DB1F0-...}`, not declared by the SDK's .vsct
  headers) 38 = Scope to This, 34 = New Solution Explorer View; `guidVSStd97:cmdidPropSheetOrProperties`.
- Cargo: `cargo metadata` `resolve.nodes[].deps[].dep_kinds[{kind,target}]`, `features` (activated),
  `--filter-platform <host>` (only what the host build uses: without it the graph included wasm and
  other-platform crates); `cargo add/remove` (`--dev/--build/--target/--rename/--optional/
  --[no-]default-features`), `cargo update -p name@version`. crates.io: `GET /api/v1/crates?q=`
  (`sort=downloads` when empty), `/api/v1/crates/{name}` (licenses per version), sparse index
  `index.crates.io/{1|2|3/a|ab/cd}/name` (versions, yanked, `features` + `features2`, implicit optional-
  dependency features) with a tool User-Agent, as crates.io's policy asks.

**Design decisions.**
- Categories by source like .NET (Packages vs Projects): *Macros procédurales* (direct proc-macros plus
  those in the build closure, like analyzers brought by packages), *Chaîne d'outils* (`rustc -vV` in the
  package folder so `rust-toolchain.toml` applies; sysroot crates open their `rust-src` sources),
  *Crates*, *Projets*, *Git*. Dev/build are **badges** (`[dev]`, `[build]`), not sub-groups: a crate
  declared both normal and dev is one node, and the table is in F4's *Type*.
- States: resolved; *pending* (first load, declarations only); unresolved (warning icon, tooltip says
  why); inactive (dimmed with `IsCut`) for an optional dependency no feature enables and for a
  target-specific one filtered out for this platform; yanked = warning icon; outdated = update overlay.
- Pipeline (never on the UI thread): `--no-deps` first (instant tree on first load), then the resolve
  graph offline, online only if needed, and `rustc -vV`/`--print sysroot` in parallel (cached 2 min per
  folder), then crates.io markers (session cache 30 min; unreachable = markers skipped, logged once).
  Reloads on `Cargo.toml` and the workspace `Cargo.lock` (changes cargo metadata itself makes are
  ignored for 3 s); a failed reload keeps the last good tree and adds the error node; merged in place.
- Every change goes through cargo; `CrateInstallPlanner` (Core, unit-tested) turns a crate-manager
  request into commands: `cargo add` updates a version or adds features, but cannot drop a feature, so
  that case is `cargo remove` + `cargo add` restoring rename/optional/default-features/table/target.
- Unused dependencies: cargo-machete (`cargo machete --skip-target-dir <dir>`, report parsed), else
  cargo-udeps **only if `cargo-udeps.exe` and a nightly toolchain are already installed**, run with
  `RUSTUP_AUTO_INSTALL=0` - verified live that a bare `cargo +nightly` makes rustup download a whole
  nightly toolchain (~900 MB) on demand; that toolchain was removed again.
- "Dépendance Cargo (crate)..." now opens the crate manager; the former small dialogs
  (`CargoDependencyDialog`, `ProjectReferenceDialog`) are gone. UI in code (no XAML build in this
  project), themed with `ThemedDialogStyleLoader` and `EnvironmentColors`; strings FR/EN from VS's UI
  culture (`DependenciesText`).
- Found on the way: `ProcessRunner` returned on the `Exited` event, before the asynchronous readers had
  delivered the output - large `cargo metadata` output was sometimes lost ("Could not parse ... does not
  contain any JSON tokens"). It now also calls the parameterless `WaitForExit()`.

**Live test** (experimental instance, UI Automation, solution `C:\kubuno-build\rsproj-test\deps`: a
Kubuno Desktop Application with registry, renamed, optional, `cfg(windows)`, dev, build, path and git
dependencies, plus two libraries): the tree (categories, resolved versions, badges, transitive
expansion, 6 proc-macros from the closure, toolchain with sysroot crates, `itoa` dimmed), both menus in
French, F4 (all rows and values), Scope to This and New Solution Explorer View, Update Crates / Update /
Remove / Copy Full Path / Open Source Code (serde's `lib.rs`, `std`'s `lib.rs`) / Open Folder / Open
Documentation (docs.rs), Remove Unused Dependencies with cargo-machete (dialog, only the checked one
removed) and without it (message), the error node on a broken `Cargo.toml` (groups kept expanded), the
Reference Manager (swap two projects, Browse... to a crate outside the solution), and the crate manager
(Browse search + install as dev with a feature, feature change on an installed crate = remove + add,
Updates tab with an outdated crate, Update, Uninstall). Not simulated: crates.io unreachable (the code
paths show the message and keep Installed working, unit-tested parsing only).

## Addendum - Project properties like .NET (lot 11)

Requested by the product owner (screenshot of a WinForms project's properties): "Properties" on a `.rsproj` opens a
**document tab** exactly like the .NET SDK's Project Properties editor - search box, navigation tree of pages and
categories, each property with a bold title, a description, a help link and its editor - as rich as .NET's, adapted
to Rust/Cargo/Kubuno.

**Research (decompiled from the installed Visual Studio 2026, not guessed).**
- The editor is CPS's own (`Microsoft.VisualStudio.ProjectSystem.VS.Implementation.dll`,
  `PropertyPages.Designer.ProjectPropertiesEditorFactory`, `{990036EB-F67A-4B8A-93D4-4663DB2A1033}`), not the managed
  project system's. "Properties" opens the editor the hierarchy reports for `VSHPROPID_ProjectDesignerEditor`; CPS's
  `ProjectNode` reports the App Designer only when **at least one `IVsProjectDesignerPageProvider` applies to the
  project** (`VsProjectDesignerPageService.IsProjectDesignerSupported`), and the App Designer's factory
  (`Microsoft.VisualStudio.AppDesigner.dll`, `ApplicationDesignerEditorFactory`) forwards to the new editor when the
  project has the **`ProjectPropertiesEditor` capability** (declared by Kubuno.Rust.Sdk since lot 3). So the whole
  opt-in is `RustProjectDesignerPageProvider`, an empty page provider exported for `RustProjectSystem`.
- The editor lists every **`PageTemplate="generic"`** rule of the project context through the Project Query API
  (`ProjectPropertyDataAccess`); `PropertyPagesHidden="true"` keeps a rule out (needed for `ConfigurationGeneral`,
  whose per-configuration `TargetPath` otherwise breaks the page). Its configuration matrix comes from the
  project's **`IProjectConfigurationDimensionsProvider`** exports: CPS's own applies only to
  `ProjectConfigurationsInferredFromUsage` projects and .NET's only to .NET ones, so without
  `RustConfigurationDimensionsProvider` every per-configuration property failed with "Expected 1 values ... but
  got 2".
- Rule metadata the editor reads: `VisibilityCondition` / `DependsOn` / `IsReadOnlyCondition` (the
  `(has-evaluated-value "Page" "Property" value)`, `(and ...)`, `(not ...)` language), `SearchTerms`, `HelpUrl`;
  editors `String`, `MultiLineString`, `Bool`, `Enum`, `Int`, `FilePath` (+ `FileTypeFilter`), `DirectoryPath`,
  `MultiStringSelector` (on a `DynamicEnumProperty` with `MultipleValuesAllowed`; value encoded as
  `name=False,name2=False` with `/` escapes), `NameValueList` (same encoding), `Description`, `LinkAction`
  (`Action` = `URL` / `Command` / `Focus`; a `Command` link calls the `ILinkActionHandler` exported with that
  `CommandName`), and inline validation `EvaluatedValueValidationRegex` + `EvaluatedValueFailedValidationMessage`.
  In XAML, an `EnumValue` name starting with `{` must be escaped as `{}{...}` (it is parsed as a markup extension
  otherwise, and the whole rule silently disappears from the editor).
- Values stored outside MSBuild: a rule's `DataSource Persistence="X"` is resolved to the
  `IProjectPropertiesProvider` exported with `Name` metadata `X` (`PropertyPagesDataModelProvider`). .NET's
  `IInterceptingPropertyValueProvider` is the managed project system's extension of that same seam; the `.rsproj`
  uses its own provider, `KubunoRust`, instead of depending on managed internals.
- `SetPropertyValueAsync` runs **inside a CPS project write lock**, sometimes once per configuration in parallel
  forks of it: reading an MSBuild property from there fails ("dangerous read lock request from a fork of a write
  lock", seen live), and touching text buffers under the lock risks deadlocks. Writes are therefore queued
  (`PropertyWriteQueue`, execution context not flowing) and applied right after, on the UI thread, with the manifest
  path and bin name the editor has just read. The editor refreshes when `ConfiguredProject.ProjectVersion` changes
  (`ProjectQueryUtilities.GetQueryDataVersion`), which only an evaluation moves: after a file write the provider
  marks the MSBuild project dirty under a write lock (`ProjectReevaluation`), which re-evaluates it without touching
  the project file.

**Pages** (`sdk/Kubuno.Rust.Sdk/Sdk/Rules/rust_*.xaml`; French copies in `Rules/fr/`, picked from Visual Studio's
`LangName` like .NET's own pages, structure checked by `LocalizedRulesTests`):

| Page | Categories | Stored in |
|---|---|---|
| Application | General (crate name, edition, output type, library crate types, default binary, binary to build and debug, **Windows subsystem**, target platform + *Install other targets...*, target OS, minimum Windows version, MSRV, Kubuno desktop sources, manifest, package); Win32 resources (embed, icon, manifest default/custom/none, DPI awareness, UAC level, long paths, Common Controls 6, file version, product, description, company, copyright, trademarks); Dependencies (summary, *Manage crates...*, *Reference Manager...*) | Cargo.toml `[package]` / `[lib]`, `src/main.rs` `#![windows_subsystem]`, `.rsproj` |
| Build (per configuration) | General (Cargo profile, extra cargo arguments); Optimization and code generation (opt-level, debug, incremental, lto, codegen-units, panic, overflow-checks, debug-assertions, strip); Features (default features, features checklist, all features); Errors and warnings (warnings as errors, cfg flags, rustc flags); Output (target directory, output file) | Cargo.toml `[profile.dev]` / `[profile.release]` (of the workspace root for a member), `.rsproj` per configuration |
| Package | General (version, authors, description, readme, homepage, repository, documentation); License (preset picker, SPDX expression, license file); Discovery (keywords, crates.io categories); Publishing (publish, registries, include, exclude) | Cargo.toml `[package]` |
| Code Analysis | Clippy (run on build); Lint levels (the 8 Clippy groups, `unsafe_code`, `missing_docs`); Formatting (format on save, rustfmt edition, max_width, hard_tabs, tab_spaces, newline_style, reorder_imports, use_field_init_shorthand, config file) | `.rsproj`, Cargo.toml `[lints]` (groups written with `priority = -1`), `rustfmt.toml` (created on first change; an existing `.rustfmt.toml` is used) |
| Debug | description, *Open debug launch profile UI*, arguments, working directory, environment (name/value list), backtraces on panic | `.rsproj.user` (the `RustDebugger*` properties F5 reads) |
| Resources | description, *Create or open application resources* (`.kbres`), link to the Win32 resources | - |
| Settings | description and a link to the design note below | - |

**Cargo.toml is never regenerated.** `Kubuno.Cargo.Toml.TomlDocument` is a lossless TOML reader/editor (253 unit
tests: round trips of real manifests including CRLF, a BOM and comments everywhere, dotted keys, inline tables,
arrays of tables, `field.workspace = true`; every edit is ONE minimal contiguous replacement). The pure property
model (`Kubuno.VisualStudio.Core.ProjectProperties.RustManifestProperties`, unit-tested with an in-memory file system)
turns a value into those edits; the VS layer applies each through the document's text buffer (the open editor's,
else an invisible editor's), so it is **one undo unit of Cargo.toml** (open it, Ctrl+Z), and saves the document if
it had no unsaved changes of the developer. A value equal to Cargo's default is not written when the key is
absent; Reset removes the key and prunes a table left empty. A field inherited from the workspace shows
`{ workspace = true }` with the inherited value as its evaluated preview; typing `{ workspace = true }` restores it.

**Validation.** Invalid input (crate name, SemVer, MSRV, URLs, SPDX expression, keywords, crates.io categories,
numbers out of range...) is never written: the provider keeps the typed value as an overlay whose evaluated form
ends with an invisible U+200B, and each such property's `EvaluatedValueValidationRegex` (`^[^\u200B]*$`) makes the
editor show its message under the field until a valid value is typed.

**Build.** Kubuno.Rust.Sdk turns the Build page into cargo arguments: `--features`, `--no-default-features`,
`--all-features`, `--target` (output under `<target dir>\<triple>\<profile>`), and one
`--config build.rustflags=[...]` for the rustc flags, `-D warnings` and the cfg flags (a command-line config array
is merged with the config files' `build.rustflags`; a `RUSTFLAGS` variable or a `target.<triple>.rustflags` still
wins, as Cargo documents). *Run Clippy on build* runs `cargo clippy` after the compile (lints in the Error List).
**Win32 resources** need no build script or crate dependency: the `KubunoWin32Resources` task writes a `.res`
(icon group, `VS_VERSION_INFO` - the root key really is `VS_VERSION_INFO`: `VS_VERSIONINFO` made Windows reject the
whole block -, the generated or custom manifest; empty version fields default from Cargo.toml) named after a hash
of its content, and `CoreCompile` builds with `cargo rustc --bin <bin> ... -- -C link-arg=<res>`, so only the
executable's link changes (a new hash means new arguments, so cargo relinks). Limitation: with Win32 resources,
Build builds that one binary target only (`CargoBin`, else the package's binary).

**Other seams.** `KubunoFormatOnSave` overrides Tools > Options' format-on-save per project; the Debug page's
"Backtraces on panic" (on by default, like before lot 11) sets `RUST_BACKTRACE=1` at F5 unless the environment list sets it, and off omits the variable; renaming the crate also
updates `<CargoPackage>` when it named the old package. Link commands: `KubunoInstallRustTargets` (a themed
`rustup target add/remove` checklist), `KubunoOpenCrateManager` / `KubunoOpenReferenceManager` (the lot 10 UIs,
reached through `RustProjectPropertiesHost` since Kubuno.VisualStudio references this assembly, not the reverse),
`KubunoOpenRustLaunchProfile`, `KubunoCreateOrOpenKbres`. Diagnostics: `KUBUNO_PROPERTIES_LOG=1` logs every read
and write to `%TEMP%\kubuno-properties.log`.

### Settings page design note

.NET's Settings page edits typed application settings (`Settings.settings` plus generated accessors). The Kubuno
equivalent will be a `.kbsettings` file per project: a list of settings (name, type among the view property types,
scope user or application, default value), edited in a designer grid like `.kbview` properties, and compiled at
build time (never checked in, per this repository's rule) into a typed `settings` module -
`settings::get().theme()`, `settings::get_mut().set_theme(...)`, `save()` - persisted per user under
`%APPDATA%\<company>\<app>\settings.toml` with the file's defaults, and reachable from views by binding
(`{Setting Theme}`). Until then the page only describes it.

**Live test** (experimental instance, UI Automation; fixture `C:\kubuno-build\rsproj-test\props` = `hello-rust` in a
local git repository for diffs, with the SDK imported from a staged copy so the shared NuGet cache stayed
untouched): Properties opens the tab (Application, Build, Package, Code Analysis, Debug, Resources, Settings; the
search filters across them); edited the edition, MSRV, description, Windows subsystem, the Release opt-level, LTO
(both profiles), a Clippy group, the rustfmt max width, features (Debug), warnings as errors, "Embed Win32
resources", the icon, the file version and the company - `git diff` showed only the edited lines of Cargo.toml,
src/main.rs and rustfmt.toml, and the `.rsproj` properties; an invalid version showed "Invalid version: expected a
SemVer version..." under the field and left Cargo.toml untouched; Ctrl+Z in Cargo.toml undid a property change in
one step; the solution then built in Visual Studio, and the executable had PE subsystem 2 (GUI) after "Windows
application", the icon, and version information 2.3.4 / "Kubuno SAS" in its file properties. Not automatable here
(left to the visual check): ticking a feature checkbox with the mouse (UI Automation's Toggle does not commit it,
the Add box does) and rows below the first category of a long page (not exposed to UI Automation until scrolled into
view by a real wheel or keyboard).
