# Wiring Kubuno.Rust.TestAdapter into the VSIX

This library (`src/Rust/Kubuno.Rust.TestAdapter/`) is a self-contained VSTest adapter and does not touch
`src/Kubuno.VisualStudio/`, the `.sln`, or any VS package/command registration - by design (see
the task split). This document is the exact recipe for the orchestrator to make Rust tests show
up in Test Explorer for an Open Folder Cargo workspace.

## 1. Mechanism chosen, and why

Visual Studio's Test Explorer needs to know two things: **which files might contain tests**
("containers"), and **how to discover/run tests inside a container** (an adapter). For a project
system (csproj, vcxproj) VS derives containers from the build output automatically. Cargo has no
project system VS understands, so containers must be supplied explicitly.

The mechanism is `ITestContainerDiscoverer`/`ITestContainer`
(`Microsoft.VisualStudio.TestWindow.Extensibility`, physically shipped as
`Microsoft.VisualStudio.TestWindow.Interfaces.dll` under
`<VS install>\Common7\IDE\CommonExtensions\Microsoft\TestWindow\`). This is an older API (the
"legacy Test Window" MEF contract), but **it is still the documented, currently-shipping
mechanism for exactly this scenario** in VS 18 (2026) - confirmed two ways while building this
library:

- `ITestContainerDiscoverer`/`ITestContainer`/`TestContainersUpdated` are present in that DLL
  installed with this machine's VS 18 Community.
- The real, currently-shipping **"CMake Linux and Mac Test Adapter"** extension
  (`<VS install>\Common7\IDE\CommonExtensions\Microsoft\CMake\Linux.TestAdapter\`) - Microsoft's
  own adapter for CTest, a non-MSBuild, non-project-system build system, i.e. the closest
  available precedent to Cargo - registers exactly this way. Its manifest
  (`extension.vsixmanifest`):
  ```xml
  <Assets>
    <Asset Type="Microsoft.VisualStudio.MefComponent" Path="Microsoft.VisualStudio.CMake.Linux.TestAdapter.dll" />
    <Asset Type="UnitTestExtension" Path="Microsoft.VisualStudio.CMake.Linux.TestAdapter.dll" />
  </Assets>
  ```
  i.e. **one DLL, declared as both a MEF component and a UnitTestExtension.** Kubuno.Rust.TestAdapter.dll
  should be registered the same way.

Two independent registrations combine:
1. **`UnitTestExtension`** tells VSTest's extension manager to load Kubuno.Rust.TestAdapter.dll and
   reflect over it for `ITestDiscoverer`/`ITestExecutor` implementations (found via their
   `[FileExtension]`/`[DefaultExecutorUri]`/`[ExtensionUri]` attributes - see
   `Discovery/KubunoTestDiscoverer.cs` and `Execution/KubunoTestExecutor.cs`). This is what makes
   a *run* work once VS already has `TestCase`s.
2. **`Microsoft.VisualStudio.MefComponent`** tells VS's own composition container to compose
   `Containers/KubunoTestContainerDiscoverer.cs` (`[Export(typeof(ITestContainerDiscoverer))]`).
   This is what makes Test Explorer *know Cargo.toml files exist* in the first place and ask this
   adapter to discover tests in them.

## 2. What already exists in this library

- `Discovery/KubunoTestDiscoverer.cs` - `ITestDiscoverer`, `[FileExtension(".toml")]`,
  `[DefaultExecutorUri("executor://kubuno.testadapter/v1")]`.
- `Execution/KubunoTestExecutor.cs` - `ITestExecutor`, `[ExtensionUri("executor://kubuno.testadapter/v1")]`.
- `Containers/KubunoTestContainerDiscoverer.cs` - `[Export(typeof(ITestContainerDiscoverer))]`,
  `[ImportingConstructor]` taking one dependency: `Containers/ICargoWorkspaceSource.cs`.
- `Containers/KubunoTestContainer.cs` - the `ITestContainer` implementation; `Source` is the
  **Cargo.toml manifest path**, not a built executable (see &sect;4 for why).

The one thing this library deliberately does **not** provide: a real implementation of
`ICargoWorkspaceSource` (it needs live Visual Studio workspace services this project has no
access to). That is the whole integration surface below.

## 3. Steps for the orchestrator

### 3.1. Reference the project

In `src/Kubuno.VisualStudio/Kubuno.VisualStudio.csproj`, add a `<ProjectReference>` to
`Kubuno.Rust.TestAdapter.csproj`, following the exact same pattern already used for Kubuno.Rust.Cargo/Kubuno.Rust.Launch
(`IncludeOutputGroupsInVSIX` = `BuiltProjectOutputGroup;BuiltProjectOutputGroupDependencies;GetCopyToOutputDirectoryItems;SatelliteDllsProjectOutputGroup;`,
`IncludeOutputGroupsInVSIXLocalOnly` = `DebugSymbolsProjectOutputGroup;`). Add the project to
`Kubuno.VisualStudio.sln` (new solution folder entry under `src`, mirroring Kubuno.Rust.Cargo/Kubuno.Rust.Launch).

Kubuno.Rust.TestAdapter.csproj references two VS-install-only assemblies
(`Microsoft.VisualStudio.TestWindow.Interfaces.dll` and the VS copy of
`Microsoft.VisualStudio.TestPlatform.ObjectModel.dll`, both `Private=False` - resolved by its own
`KubunoResolveVsTestWindowAssemblies` MSBuild target, no NuGet package needed). Nothing extra is
required in Kubuno.VisualStudio.csproj for these two: they are not shipped inside the VSIX
(`Private=False`, exactly like the existing `Microsoft.VisualStudio.Workspace.Extensions.VS`
reference there) - they must resolve against the *running* Visual Studio at both build and load
time, which is always true for a VS extension.

### 3.2. Declare the two manifest assets

In `src/Kubuno.VisualStudio/source.extension.vsixmanifest`, add two `<Asset>` entries next to the
existing ones (which use the VS-project-system-generated `d:Source="Project"` shorthand for
"this VSIX's own project's output" - Kubuno.Rust.TestAdapter is a *different* project, so its assets
need `d:ProjectName="Kubuno.Rust.TestAdapter"` pointing at that project specifically):

```xml
<Assets>
  <Asset Type="Microsoft.VisualStudio.VsPackage" d:Source="Project" d:ProjectName="%CurrentProject%" Path="|%CurrentProject%;PkgdefProjectOutputGroup|" />
  <Asset Type="Microsoft.VisualStudio.MefComponent" d:Source="Project" d:ProjectName="%CurrentProject%" Path="|%CurrentProject%|" />
  <!-- New: registers KubunoTestContainerDiscoverer's [Export(typeof(ITestContainerDiscoverer))]. -->
  <Asset Type="Microsoft.VisualStudio.MefComponent" d:Source="Project" d:ProjectName="Kubuno.Rust.TestAdapter" Path="|Kubuno.Rust.TestAdapter|" />
  <!-- New: registers KubunoTestDiscoverer/KubunoTestExecutor with VSTest's extension manager. -->
  <Asset Type="UnitTestExtension" d:Source="Project" d:ProjectName="Kubuno.Rust.TestAdapter" Path="|Kubuno.Rust.TestAdapter|" />
</Assets>
```
(`d:ProjectName` must match the project name exactly as added to the `.sln` in &sect;3.1.)

### 3.3. Implement `ICargoWorkspaceSource`

Add a new MEF-exported class in `src/Rust/Kubuno.Rust/Workspace/` (e.g.
`CargoWorkspaceSource.cs`) implementing `Kubuno.Rust.TestAdapter.Containers.ICargoWorkspaceSource`:

```csharp
[Export(typeof(ICargoWorkspaceSource))]
internal sealed class CargoWorkspaceSource : ICargoWorkspaceSource, IDisposable
{
    [ImportingConstructor]
    public CargoWorkspaceSource([Import(typeof(SVsServiceProvider))] IServiceProvider serviceProvider) { ... }

    public IReadOnlyList<string> GetManifestPaths() { /* every Cargo.toml under the open folder */ }
    public event EventHandler? Changed;
}
```

What it needs to do:
- **Enumerate every `Cargo.toml`** under the Open Folder workspace root - not just the root's own
  manifest: a Cargo workspace's member packages each get their own container, so
  `cargo test --manifest-path <member>`/discovery stays incremental per package (see
  `Discovery/CargoTestBuild.cs`'s doc comments). `CargoBuildFileContextProvider.cs` (existing,
  `Workspace/CargoBuildFileContext.cs`) already recognizes a single Cargo.toml
  (`CargoBuildFileContextProvider.IsCargoManifest`) reactively (given a path, is it a manifest?)
  but nothing in this codebase currently does the *bulk enumeration* this needs - this is new
  ground. Use the Open Folder file-finding API already available via the
  `Microsoft.VisualStudio.Workspace`/`Microsoft.VisualStudio.Workspace.VSIntegration` packages
  already referenced by Kubuno.VisualStudio.csproj (`IWorkspace.GetFileFinder()` /
  `IFileFinder.GlobFilesAsync("**/Cargo.toml", ...)` in current SDK versions - verify the exact
  member names against the installed package version, they have moved slightly across SDK
  releases).
- **Raise `Changed`** when that set might have changed: subscribe to the workspace's file-change
  notifications (`IWorkspace`'s file watcher API / `IVsFileChangeEx`) filtered to `Cargo.toml`
  additions/removals/edits, and to the workspace-(re)opened event. A `Cargo.toml` *edit* matters
  too (e.g. a new `[[test]]`/dependency), not just add/remove - `KubunoTestContainerDiscoverer`
  re-raises this verbatim as `TestContainersUpdated`, which is what makes Test Explorer re-run
  discovery.
- Constructing a `CargoWorkspaceSource` must be cheap and must not itself run `cargo` - it only
  *lists paths*; building/listing tests happens later, inside `KubunoTestDiscoverer`, per
  container, only when Test Explorer actually asks.

### 3.4. Nothing else to wire for execution/debugging

`KubunoTestExecutor` is entirely self-contained once discovered `TestCase`s reach it (VSTest
routes runs to it via `TestCase.ExecutorUri`, set during discovery - see
`Discovery/CargoTestCaseFactory.cs`). It reuses Kubuno.Rust.Cargo (process running, command-line
quoting) and Kubuno.Rust.Launch (sysroot/host-triple resolution, `RustDebugEnvironment`,
`LaunchDescriptionBuilder`) directly; no additional VSIX-side glue is needed for either running or
debugging a test.

## 4. Design notes worth knowing before touching this

- **`TestCase.Source` is the Cargo.toml manifest path, never the built test executable.**
  VSTest requires every `TestCase.Source` a discoverer reports to equal one of the `sources` it
  was given, and container discovery must supply containers *before* anything has been built
  (Test Explorer populates its tree at workspace-open time). A test executable's path only exists
  *after* a build and is hash-suffixed (`hello_rust-1c0c90adf61aa746.exe` - changes across cargo
  invocations), so it cannot be the stable container/`Source` key. The concrete exe path a test
  needs to actually run travels instead as a hidden `TestCase` property
  (`KubunoTestProperties.ExecutablePath`, set during discovery, read during execution) - see
  `KubunoTestProperties.cs`.
- **`profile.test` is not modeled by Kubuno.Rust.Cargo's public `CargoArtifact`.** `CargoTestBuild.cs`
  parses `cargo test --no-run --message-format=json` itself (reusing Kubuno.Rust.Cargo's
  `CargoMessageParser` only for diagnostics/build-finished) because that field is the only
  reliable way to tell a target's *test-harness* artifact apart from its *ordinary* build artifact
  - both are emitted as separate `"compiler-artifact"` events for the same `[[bin]]` target. This
  is a real gap in Kubuno.Rust.Cargo's public model, not a workaround that should be copied elsewhere;
  if Kubuno.Rust.Cargo's owner ever adds `profile.test` to `CargoArtifact`, `CargoTestBuild.cs` should
  switch to it and delete its own local raw-JSON parse.
- **Libtest output parsing does not trust the per-line "test &lt;name&gt; ... ok/FAILED" text** as
  its primary signal, because `--nocapture` (used for every run, per spec) interleaves a test's
  own stdout with that line unpredictably. See the extensive remarks on
  `Execution/LibtestOutputParser.cs` for the actual algorithm (trailing `failures:` block +
  `... ignored` lines for the verdict; the panic hook's own `thread '&lt;name&gt;' panicked at`
  header - which names the test via its thread name - for per-test message attribution even under
  interleaving) and `tests/Kubuno.Rust.TestAdapter.Tests/Fixtures/Execution/*.txt` for the real
  captures this was built and tested against.
- **Debugging never batches.** Each selected test gets its own
  `IFrameworkHandle.LaunchProcessWithDebuggerAttached` call, run one after another - see the
  remarks in `Execution/KubunoTestExecutor.cs`. A debugged run's output is not captured (VS owns
  the debuggee's console once attached), so only a Passed/Failed verdict from the exit code is
  reported for a debug run, not the panic message/assertion diff a normal run gets.
- **Known limitation - no `--target <triple>` (cross-compiled) support.** `CargoTestBuild.cs`
  derives a test binary's Cargo target directory by walking three directories up from its
  executable path (`.../<target-dir>/debug/deps/<exe>` -&gt; `<target-dir>`), which assumes a
  host-triple build. A cross-compiled test binary lives one level deeper
  (`.../<target-dir>/<triple>/debug/deps/<exe>`) and would resolve to the wrong target directory,
  which only affects the debug-launch PATH computation (`RustDebugEnvironment`), not
  discovery/execution themselves. Not expected to matter for Kubuno's own modules (host-triple
  builds throughout - see `CLAUDE.md`), flagged here in case a future workspace cross-compiles.
- **`KubunoTestContainer.TargetPlatform` is a hardcoded `Architecture.X64` best-effort default.**
  Test Explorer only uses it for container labeling/grouping, never to gate discovery/execution,
  so this is safe today; wire it to the workspace's real host/target triple (available once
  `ICargoWorkspaceSource` or a sibling service can report it) if that labeling ever needs to be
  accurate.
