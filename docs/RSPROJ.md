# `.rsproj`: a real MSBuild/CPS project type for Cargo packages

Status: work packages 1-4 implemented (SDK, MSBuild tasks, CPS project type, F5/Ctrl+F5 — see
README.md's "Building Rust with MSBuild (`.rsproj`)" section); work packages 5-7 not started.
Implementation notes that deviate from this design: the SDK now imports
`Microsoft.Common.props`/`.targets` like the JS SDK (CPS needs that targets graph), and the project
type is registered by a static `rsproj.pkgdef` mirroring the JS project system's own pkgdef (the
JS package itself carries no `[ProjectTypeRegistration]` attribute — verified by reflection).
Work package 4 (verified by reflection and live): `IDebugProfileLaunchTargetsProvider` belongs to
the managed project system, not to CPS — the seam is CPS's `DebugLaunchProviderBase` +
`[ExportDebugger(name)]`, picked by the `DebuggerFlavor` property (`debugger_general.xaml`), exactly
as the JS project system's `LaunchJsonDebugLaunchProvider`; it lives in
`Kubuno.VisualStudio.RustProjectSystem/RustDebugLaunchProvider.cs` (not `Debugging/`), with
`Rules/debug.xaml` (args/working dir/env) and `Rules/rust_debugger.xaml`; there is no separate
`Build.xaml` (the "Cargo" page already holds the build settings). Launch profiles
(`LaunchProfiles` capability) were not needed. A `.rsproj` is `x64`-only (host triple). Modeled on the JavaScript project type (`.esproj`,
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

## 4. Generation: "Generate Visual Studio projects"

A new command (Tools menu, or a right-click on a workspace-root `Cargo.toml`) runs `cargo metadata`
(reusing `CargoMetadataReader` as-is) and, for a workspace, emits one `.rsproj` per member with a
`[[bin]]` target (`<Project Sdk="Kubuno.Rust.Sdk/…"><PropertyGroup><CargoPackage>…</CargoPackage>
</PropertyGroup></Project>`), plus a `.sln` (and, once VS 2026's `.slnx` is verified stable enough
day-to-day, a `.slnx`) referencing them.

**Idempotency is the hard requirement**, matching `CLAUDE.md`'s "never regenerate what the
developer owns" rule already applied to XML views, and the same discipline
`EnsureStartupItemSelectedAsync` already follows for `ProjectSettings.json`: **only ever create a
`.rsproj` that does not already exist.** A package whose targets changed shape (a `[[bin]]` added
or removed) should be diffed and reported for the developer to accept, never silently rewritten.
`.sln`/`.slnx` membership can regenerate more freely — it holds no per-project customization — but
manual edits there (solution folders, extra projects) must still survive a re-run.

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
5. **(S/M) Generation command + idempotency.** Owns: `Commands/GenerateRustProjectsCommand.cs`,
   `ProjectGeneration/` (new, reusing `CargoMetadataReader`). Test: run twice against
   `samples/hello-rust` and a real multi-member workspace (`Z:\src\desktop\windows`), confirm the
   second run is a no-op; hand-edit a generated `.rsproj`, re-run, confirm the edit survives.
6. **(S) Open Folder / rust-analyzer coexistence pass.** Owns: `NonRustProjectExclusionScanner`
   (extend to recognize Kubuno-generated files), README/`ARCHITECTURE.md` updates. Test, live: open
   a folder with both a Cargo workspace and generated `.rsproj`/`.sln` in Open Folder mode, confirm
   targets and Select Startup Item still work; separately open the `.sln` and confirm the CPS path.

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
- **SDK distribution.** `Sdk="Kubuno.Rust.Sdk/1.0.0"` needs MSBuild to find that SDK. The JS model
  resolves it through ordinary NuGet (the VSIX declares it as a dependency so VS's installer places
  it in the shared NuGet packages folder — verified locally). A custom MSBuild `SdkResolver` (as
  `Microsoft.VC.VcpkgSdkResolver` does, under `MSBuild\Current\Bin\SdkResolvers\`) would avoid a
  NuGet/network dependency for a fully offline dev loop, but **whether a VSIX can register a
  resolver there is unverified** — exactly the kind of "looks fine, undocumented in practice" risk
  this project has already been burned by once. The lower-risk default is the JS model (NuGet
  package, restored from a local/private feed) unless that is verified impractical.

## Addendum — "Create a new project" templates (lot 7)

Requested by the product owner: Rust and Kubuno must appear in VS's *Create a new project*
dialog (and *Add New Item*), filterable by language/platform/project type, like C# does.

- Shipped in the VSIX as `.vstemplate` project templates (depends on lots 1–3: the templates
  create `.rsproj` projects). Custom tags: `LanguageTag` = `Rust`, `PlatformTag` = `Windows`
  (+ `Linux` where relevant), `ProjectTypeTag` = `Console` / `Library` / `Desktop` / `Kubuno`
  (custom tag values are supported by the template engine; verify the filter picks them up).
- Project templates: **Rust console application**, **Rust library**, **Kubuno desktop
  application** (`kubuno_ui` window + a starter `.kbview` view + handlers), **Kubuno module**
  (Axum backend skeleton following the module conventions: `module.toml`, dedicated DB schema,
  `build_kbpkg.sh`).
- Item templates (*Add New Item*): **Kubuno view (.kbview)** with its code-behind handlers `.rs`,
  **Rust module file**, **Rust integration test**.
- Each template runs `cargo generate-lockfile` on creation (optional) and opens the `.rsproj`
  in the current solution; names are sanitised to valid crate names.
- Test: live in the experimental instance — the templates appear under the *Rust* language
  filter, each creates a project that builds with F5.
