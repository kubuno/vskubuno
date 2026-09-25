# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Added

- **DSG-9: move/resize drag, Flow reorder and toolbox drop** (`docs/DESIGNER.md` §10): the design
  surface protocol (`kubuno_views::protocol`, implemented in the `desktop` repo) gained a batched
  `editRequests {ops, gesture}` message so a move/resize drag's mouse-up applies as one undo unit,
  and a `dragEnter`/`dragOver`/`drop`/`dragLeave` quartet for a VS Toolbox drag translated by the
  host. New file `Kubuno.VisualStudio.Designer/DesignSurface/RustDesignSurfaceHost.DragDrop.cs` (a
  third `partial class` piece alongside the DSG-6-era `.cs`/`.Protocol.cs`, kept separate so it
  would not collide with concurrent DSG-8 work on those two files): `NotifyDragEnter`/
  `NotifyDragOver`/`NotifyDrop`/`NotifyDragLeave` (named to avoid shadowing `UIElement`'s own
  same-named routed events) send the new host→surface messages; a second, independent
  `Process.OutputDataReceived` listener (wired lazily on first use, since an instance field
  initializer cannot call an instance method and this file must not touch
  `RustDesignSurfaceHost.cs`'s own constructor) raises the new `EditRequestsReceived`/
  `DragDropEditRequested`/`DropTargetChanged` events for the shapes `RustDesignSurfaceHost
  .Protocol.cs`'s own listener does not already recognise. New
  `Kubuno.VisualStudio.Designer.Tests/DesignSurface/RustDesignSurfaceHostDragDropTests.cs`
  (`DesignSurfaceDragDropProtocol`'s encode/parse, no live process). Forwarding these events into
  `kubuno-views-ls`'s `kubuno/applyEdit`/an actual OLE drop effect is left for a later package,
  same as DSG-6's own `EditRequested` was.

- **DSG-8: bidirectional selection sync + Document Outline** (`Kubuno.VisualStudio.Designer`,
  `vskubuno/docs/DESIGNER.md` §6/§8/§9): new `Selection/` and `Outline/` folders.
  - `Selection.SelectionSyncService` keeps the design surface, the XML text view and the (optional)
    Document Outline showing the same selected element, whichever originated the change: a surface
    click (`IDesignSurfaceHost.SelectionChanged`) resolves the element's full range via
    `kubuno/rangeOfElement` and selects it in the XML view (whole element, caret at its start); a
    caret move in the XML view (polled every ~150 ms - the legacy `IVsTextView` this library already
    standardises on has no caret-changed event) resolves the element via `kubuno/elementAtOffset`
    and pushes `select {id}` to the surface; an Outline click does the same through the new
    `SelectFromOutlineAsync`. Each origin never gets echoed back to itself (a per-origin skip plus a
    synchronous re-entrancy guard). The Properties panel follows every selection change, fed from a
    new `Selection.ElementAttributeReader` - a pure, hand-rolled reader over the CURRENT buffer text
    (no LS round trip; `kubuno-views-ls` has no `kubuno/elementAttributes` method today, and reading
    the buffer directly keeps this on the selection-change hot path) using the same stable element-id
    scheme as `kubuno_views::ast::Element::stable_id`/`Document::resolve_id` - deliberately NOT
    `System.Xml.Linq` (checked against `kubuno-views`' own corpus: `x:Name`/`x:Class` are flat
    identifiers, not real XML-namespaced names resolved against a declared `xmlns:x`, which
    `XDocument` would reject).
  - New `Selection.IViewsSelectionLanguageServerClient` (real impl
    `Selection.Infrastructure.JsonRpcViewsSelectionLanguageServerClient`) calls
    `kubuno/elementAtOffset`/`kubuno/rangeOfElement`/the standard `textDocument/documentSymbol` over
    the same `JsonRpc` object `Handlers.Infrastructure.JsonRpcKubunoViewsLanguageServerClient`
    already uses for `kubuno/createHandler`.
  - New `Outline.OutlineViewModel`/`OutlineNodeViewModel`/`OutlineView` (a plain `TreeView`, no
    `.xaml`) and `Outline.DocumentSymbolTreeBuilder`, which maps a `textDocument/documentSymbol`
    response onto stable element ids purely from each node's ordinal position in the tree - no extra
    round trip, since `kubuno-views-ls`'s `symbols.rs` already walks `Element::children()` in the
    same order `stable_id` itself indexes by.
  - Every VS-dependent piece (`Selection.Infrastructure.VsTextViewSelectionAdapter` wrapping
    `IVsTextView`, `Selection.Infrastructure.DesignSurfaceSelectionTarget` wrapping
    `RustDesignSurfaceHost.Select`, the JsonRpc client above) sits behind a small, unit-testable
    interface (`ITextViewSelectionAdapter`, `IDesignSurfaceSelectionTarget`,
    `IViewsSelectionLanguageServerClient`); `SelectionSyncService`/`ElementAttributeReader`/
    `DocumentSymbolTreeBuilder`/`OutlineViewModel`/`StableElementId`/`SelectionResponseParser` are
    all covered by `tests/Kubuno.VisualStudio.Designer.Tests/Selection/` and `.../Outline/` with no
    live VS/JsonRpc/design-surface process needed. Wiring into the VSIX (constructing the service
    once a `.kbview` designer pane opens, resolving a real `ITextView`/`ComponentRegistry`/`JsonRpc`
    for it) is left to the integration step - see `Kubuno.VisualStudio.Designer/INTEGRATION.md`'s new
    "Selection sync & Outline" section.
  - Flagged (not fixed - out of this change's scope, `Properties/` gets no logic changes):
    `Registry.EventMeta.Name`/`Properties.EventRowViewModel.AttributeName` disagree with the real
    DSG-1 registry export (`events[].name` is already the full attribute name, e.g. `"OnClick"`, not
    a bare `"Click"` `"On" + Name` would need) - verified directly against
    `tests/Kubuno.VisualStudio.Designer.Tests/Fixtures/registry.sample.json`.

- **Wired `Kubuno.VisualStudio.Designer` into the VSIX**, following its own `INTEGRATION.md`:
  - Added the project and its tests to `Kubuno.VisualStudio.sln`, with a `ProjectReference` from
    `Kubuno.VisualStudio.csproj`.
  - Registered `KbviewEditorFactory` as a classic, package-registered `IVsEditorFactory`
    (`[ProvideEditorFactory]`/two `[ProvideEditorLogicalView]`/`[ProvideEditorExtension]` at
    priority `0x60`, below `languages.pkgdef`'s `0x64` for the plain text editor, plus the
    `RegisterEditorFactory` call `KubunoPackage.InitializeAsync` needs) and its Tools > Options
    page (`KbviewDesignerOptionsPage`); `.kbview` still opens in the plain text editor by
    double-click, with "Kubuno View Designer" available from "Open With…". Added the
    `Resources\VSPackage.resx` this attribute's display-name resource id needs (this VSIX had none
    before).
  - Hosted the Toolbox/Properties panels as VS tool windows (`ToolboxToolWindow`/
    `PropertiesToolWindow`, `[ProvideToolWindow]`, shown from new "View > Other Windows > Kubuno
    Toolbox/Properties" commands in `KubunoCommands.vsct`); the Toolbox refreshes itself from the
    live `kubuno/registry` RPC (`JsonRpcRegistryClient`) once shown.
  - Plugged `RustDesignSurfaceHost` into the Design pane through the `IDesignSurfaceHostFactory`
    seam (`DesignSurfaceHostFactoryHost.Current`, resolved via a new `KubunoViewsSurfaceLocator`
    pointing at `tools\surface\kubuno-views-surface.exe`, shipped with its own `kubuno_ui.dll` in
    that subfolder since it is built from a separate `CARGO_TARGET_DIR` than `kubuno-views-ls.exe`
    and a Rust dylib has no stable ABI to share across builds).
  - Added `IDesignSurfaceHost.SetDesignMode`/`EditRequested` (implemented by `RustDesignSurfaceHost`
    already, and as no-ops by `PlaceholderDesignSurfaceHost`) and a new
    `DesignSurfaceEditingCoordinator` (`Kubuno.VisualStudio.Designer`) that turns design mode on,
    forwards a Delete/nudge `EditRequested` to `kubuno-views-ls`'s `kubuno/applyEdit` and applies
    the result through `BufferEditApplier` (one `ITextEdit`, one undo unit), and pushes the XML
    pane's own buffer text back to the surface (`SetDocumentText`) on every `ITextBuffer.Changed`,
    debounced 200 ms. Wired into `DesignerSplitView`'s constructor/`Dispose`.

### Fixed

- **`dotnet test` on `tests\Kubuno.VisualStudio.Designer.Tests`** now works unconditionally, without
  a shell environment variable to remember: a new `.runsettings` (referenced from the csproj via
  `RunSettingsFilePath`) sets `COMPlus_LoadFromRemoteSources=1` for the test host process, working
  around .NET Framework's loader-from-remote-source restriction on this repository's mapped network
  drive (`Z:`) that otherwise fails `MSTest.TestAdapter.dll`'s own load for every test in this net48
  project (see `README.md`'s "Tests" section for the same issue, previously worked around by hand).
- **`DesignSurfaceProtocol.EncodeSetText`/`EncodeSetDesignMode`/`EncodeSelect`** now match
  `kubuno-views/src/protocol.rs`'s wire shape byte-for-byte for `.kbview` content (all angle
  brackets): `System.Text.Json`'s default encoder HTML-escapes `<`/`>`/`&` for browser-embedding
  safety, which `serde_json` on the Rust side does not do, so a `setText` line for real `.kbview`
  text previously came out as `<...>` instead of `<...>`. Fixed by serializing with
  `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` (safe here: this JSON is only ever parsed back by
  `kubuno-views-surface.exe`'s own `serde_json` over a pipe, never rendered in a browser).

- **Design surface DSG-6 protocol** (`Kubuno.VisualStudio.Designer`): `RustDesignSurfaceHost` now
  speaks the DSG-6 line-delimited JSON protocol (`vskubuno/docs/DESIGNER.md` §9) with the design
  surface exe over its own stdin/stdout (`ProcessStartInfo.RedirectStandardInput`/
  `RedirectStandardOutput`, both now `true`) — implemented in a new sibling file,
  `RustDesignSurfaceHost.Protocol.cs` (the class is now `partial`, kept separate from its
  keyboard-forwarding code). `SetDocumentText` (`IDesignSurfaceHost`) now sends a `setText`
  message instead of writing a temp file, which this class no longer creates or cleans up; new
  `SetDesignMode`/`Select` methods send `setDesignMode`/`select`. A new `EditRequested` event
  (`EventHandler<DesignSurfaceEditRequestedEventArgs>`) is raised for a `SetAttribute`/
  `RemoveElement` edit request the surface reports (a nudge/resize or a Delete on the design
  surface), and `SelectionChanged` is now actually wired to the surface's own `selectionChanged`
  messages. A crash-restart resends the pane's last known text/design-mode/selection to the
  fresh process. The pure encode/parse half (`DesignSurfaceProtocol`) is unit-tested
  independently of any live process
  (`tests/Kubuno.VisualStudio.Designer.Tests/DesignSurface/RustDesignSurfaceHostProtocolTests.cs`),
  matching `kubuno-views/src/protocol.rs`'s wire shapes byte-for-byte. `Toolbox/`, `Properties/`
  and `Editing/` are untouched by this change.

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
- **Open Folder now pre-selects a sensible "Select Startup Item" automatically**, the same way
  CMake Tools/Makefile Open Folder support does, so F5 works right after opening a Cargo folder
  instead of showing "Sélectionner un élément de démarrage…" until the developer picks one by
  hand. New `Kubuno.VisualStudio.Core.StartupItemSelector` (pure, unit-tested -
  `tests/Kubuno.VisualStudio.Tests/StartupItemSelectorTests.cs`, 7 cases) mirrors `cargo run`'s own
  fallback chain: the package's `default-run` (`Kubuno.Cargo.Metadata.CargoPackage.DefaultRun`, new
  field), then the only `[[bin]]`, then a bin named after the package, then the first bin.
  `Debugging/RustLaunchTargetsGenerator.GenerateAsync` calls it once per launch-target
  regeneration and, only when nothing has been selected yet (never overrides an existing choice,
  including one made in an earlier session), applies it two ways: the documented
  `Microsoft.VisualStudio.Workspace.Debug.IProjectConfigurationService.SetCurrentProject` API
  (found by reflecting over the real `Microsoft.VisualStudio.Workspace.dll` - no public sample of
  it exists for a plain `launch.vs.json`-only setup, so this is best-effort and its exact
  `ProjectTargetFileContext.FilePath` semantics for this scenario are unconfirmed), and a
  read-modify-write of `.vs\ProjectSettings.json`'s `CurrentProjectSetting` key - the actual
  on-disk state backing the toolbar dropdown for this scenario, confirmed live. Verified live:
  opening `samples\hello-rust` for the first time writes `CurrentProjectSetting: "hello-rust"`
  with no prior manual selection, and `Debug.Start` goes from "command unavailable" (no startup
  item) to recognizing a target.
- **`Kubuno.VisualStudio.Designer.Registry` now targets the real `kubuno/registry` export
  (work package DSG-1 lands).** `kubuno-views-ls` (in the separate `desktop` repo) now actually
  implements `kubuno/registry`, serializing the real component registry via a new
  `kubuno_views::registry::export` module; this repo's checked-in test fixture
  (`tests\Kubuno.VisualStudio.Designer.Tests\Fixtures\registry.sample.json`) is regenerated
  straight from that export (49 real components, not the 10 illustrative ones DSG-4 was built
  against), so both sides now share one truth instead of a fixture that could quietly drift.
  Two places where the note the C# side was first built against (`docs\DESIGNER.md` §5) and the
  real Rust schema disagreed, resolved by following the real schema (documented in
  `export.rs`'s own module doc on the Rust side):
  - `Registry\LayoutKind.cs` is widened to the real `kubuno_views::registry::LayoutKind`'s actual
    four variants (`Flow`, `DockAnchor`, `Split`, `Tabs`) - the original guess (`Anchor`, `Dock`,
    `Flow`, `Split`) predated that Rust enum and split into two variants an engine that, in the
    real implementation, decides Dock-vs-Anchor per *child*, not per container; it also missed
    `Tabs`.
  - `ComponentMeta.AllowedChildren` is a new field carrying `ChildrenModel::List`'s gated child
    names (e.g. `["TabItem"]` for `Tabs`) - not in the original sketch, added as its own array
    alongside `Children` (which stays the bare `"List"` string `JsonStringEnumConverter` needs)
    per the task's "children model incl. allowed child names" requirement.
  - `Registry\ComponentRegistryTests.cs`/`Toolbox\ToolboxViewModelTests.cs` are updated for the
    real component set (counts, family order/sizes, real property/event names such as Button's
    `OnClick` rather than the fixture's old fictional `Click`, and search terms that are no
    longer ambiguous or extinct - e.g. `"grid"`/`"DataGrid"` -> `"tree"`/`TreeView`, the real
    registry has no `DataGrid`). `dotnet test` passes (`Kubuno.VisualStudio.Designer.Tests`, and
    the rest of the solution unaffected - that project is not yet part of `Kubuno.VisualStudio
    .sln`, same as before this change).
- **`Kubuno.VisualStudio.Designer` (work packages DSG-4/DSG-5, still standalone - not yet wired into
  the VSIX, see its own `INTEGRATION.md`):**
  - **Toolbox + Properties/Events (DSG-4).** A new `Registry\*` namespace mirrors the
    `kubuno/registry` JSON shape `docs/DESIGNER.md` §5 specifies (`ComponentMeta`/`PropertyMeta`/
    `EventMeta`/`PropKind`/`ChildrenModel`, plus the two additive fields that section calls for,
    `Icon` and the new `LayoutKind` enum) with a `System.Text.Json`-based loader
    (`ComponentRegistry.FromJson`) - DSG-1 hasn't landed yet, so this is exercised against a
    checked-in, schema-faithful fixture (`tests\Kubuno.VisualStudio.Designer.Tests\Fixtures\
    registry.sample.json`), not real `kubuno-views-ls` output. `Toolbox\ToolboxViewModel`/
    `ToolboxView` group the registry by family with a live search filter; `Properties\
    PropertiesPanelViewModel`/`PropertiesPanelView` render a Properties tab (one editor per
    `PropKind` - checkbox for `Bool`, dropdown for `Enum`, a digits-only text box for `F32`, plain
    text box for `String`; a `{Binding Path[, Mode=TwoWay]}` value instead shows a small binding
    glyph, parsed by the new `Properties\BindingExpressionParser`; unset/default-equal values are
    shown greyed with a reset-to-default button) and an Events tab (handler name dropdown, plus a
    "Create handler" button/double-click-on-empty-row hook - `EventRowViewModel
    .CreateHandlerRequested` - for DSG-10 to wire up later). All WPF views are code-only (no .xaml,
    matching the rest of this repo) and themed via `Microsoft.VisualStudio.PlatformUI.
    EnvironmentColors` dynamic resource keys so they track the VS light/dark/high-contrast theme.
  - **Buffer-apply plumbing (DSG-5).** `Editing\LspPositionMapper`/`TextEditPlanner` are pure,
    VS-free range→offset mapping, edit ordering and overlap/conflict detection over the LSP-style
    `{range, newText}` shape `kubuno/applyEdit` will return (`Editing\TextEditDto`/`LspRange`/
    `LspPosition`); `Editing\BufferEditCore` version-checks a request's base version against the
    live buffer (rejecting a stale request rather than clobbering a concurrent edit) and applies
    every edit in a batch as ONE call to the narrow `IEditableTextBuffer` seam, so it lands as ONE
    `ITextEdit`/undo unit. `Editing\Infrastructure\BufferEditApplier` is the thin real
    `Microsoft.VisualStudio.Text.ITextBuffer` adapter; `Editing\Infrastructure\DesignerUndoScope`
    (real `ITextUndoHistory`) plus the pure `Editing\CompoundEditCoordinator` coalesce a gesture
    that needs more than one `kubuno/applyEdit` round trip (e.g. a `MoveChild` decomposed into a
    remove then a re-computed insert) into one user-visible undo transaction, rolling back the
    whole batch if any request in it fails its version check or conflicts. All of the pure logic is
    unit-tested (`tests\Kubuno.VisualStudio.Designer.Tests\Editing\`, incl. a regression test for a
    non-adjacent-overlap conflict a naive adjacent-pairs scan would miss); the two VS-dependent
    adapters need a live `ITextBuffer`/`ITextUndoHistory` and are left for a manual Ctrl+Z check in
    the experimental instance, same as this library's other VS-SDK-bound classes.
  - Added an explicit `System.Text.Json` `PackageReference` (pinned to the same version
    `Microsoft.VisualStudio.SDK` already resolves to, not lower) to `tests\
    Kubuno.VisualStudio.Designer.Tests.csproj`: that transitive dependency's runtime assets were
    being excluded from the test output directory (`Microsoft.VisualStudio.SDK`'s own
    `ExcludeAssets="runtime"` cascades to its whole dependency graph), which is fine inside
    `devenv.exe` but left the standalone `dotnet test` host unable to load the assembly at all.
- **`RustDesignSurfaceHost` (work package DSG-7, production): the real embedded design surface,
  turning the DSG-7 spike (`docs/DESIGNER.md` §7) into code that plugs into DSG-3's
  `IDesignSurfaceHost` seam.** New `Kubuno.VisualStudio.Designer.DesignSurface.RustDesignSurfaceHost`
  (an `HwndHost`) and `RustDesignSurfaceHostFactory`:
  - Embeds the design surface exe exactly as the spike proved out (a plain Win32 "Static" container
    child of WPF's own hosting window, the surface launched with `--parent <container>` and creating
    its own `WS_CHILD` inside it - no `SetParent` needed).
  - **Lifecycle**: a persistent Job Object (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`) so the surface
    process cannot outlive its host even on a hard VS kill; `SetErrorMode(SEM_FAILCRITICALERRORS |
    SEM_NOOPENFILEERRORBOX)` so a missing runtime DLL fails with an exit code instead of the
    loader's modal dialog; restart-with-backoff on an unexpected exit (250 ms, doubling to a 30 s
    cap, reset after 10 s of stable uptime); a pre-launch check for `kubuno_ui.dll`/`std-*.dll`
    beside the exe, showing a clear message in the container itself (and retrying with the same
    backoff, so staging the runtime while the pane is open recovers automatically) instead of
    launching into a missing-DLL crash loop.
  - **Keyboard protocol**, both sides, verified live end to end (every `--selftest` check passes,
    including Ctrl+S) after several rounds of fixes to genuine bugs each interactive run caught:
    - The surface (`kubuno_controls::host`, additive in `kubuno-controls/src/host/mod.rs` - see the
      `desktop` repo's own changelog) forwards a key it did not consume as the SAME
      `WM_KEYDOWN`/`WM_SYSKEYDOWN` a real keystroke would have produced, preceded by a new
      `kubuno_controls::host::WM_KUBUNO_KEY_MODS` message carrying the modifiers held at that
      ORIGINAL, physical moment (`PostMessage` to the same destination from the same source thread is
      FIFO, so delivery order is guaranteed) - a Win32 keyboard message carries no modifiers at all,
      and by the time the forwarded key is finally processed (after a frame, a `PostMessage` round
      trip, this thread's own queue) the physical Ctrl/Shift/Alt keys may already be released again.
    - `RustDesignSurfaceHost.HandleUnhandledKey` remembers the captured modifiers and briefly forces
      this thread's key-state table to that snapshot (`GetKeyboardState`/`SetKeyboardState`,
      `AttachThreadInput` to the surface's own thread kept alongside as belt and braces) for the
      duration of one call, restoring the real table right after in a `finally` - never held for the
      surface's whole focused lifetime, so a hung surface cannot freeze this thread's own input.
    - **VS is the primary path, WPF only the fallback for when the host is not VS**: a new
      `VsFilterKeysBridge` (isolating every VS SDK type behind an `object`-only boundary - see its own
      doc for why: `Microsoft.VisualStudio.SDK`'s package reference is `ExcludeAssets="runtime"`
      throughout this repo, and simply DECLARING a VS interop type in `RustDesignSurfaceHost`'s own
      always-executed constructor/`WndProc` was enough to throw `FileNotFoundException` for
      `Microsoft.VisualStudio.Interop.dll` on first construction, standalone - a real, live-observed
      startup crash this isolation fixes) queries `SVsFilterKeys`/`IVsFilterKeys2` from an optional
      `IOleServiceProvider` and calls `TranslateAcceleratorEx` with the global keybinding scope.
    - **The WPF fallback** (this library's own tests, the updated spike - no VS to query) walks up
      from this element and directly executes the matching `InputBinding`'s command - deliberately
      NOT by synthesizing routed keyboard events. Two other approaches were tried first and both
      failed, live and reproducibly: `ComponentDispatcher.RaiseThreadMessage` never reached a
      `KeyBinding` at all (WPF only turns a message into a routed keyboard event through an
      `HwndSource`'s own `HwndKeyboardInputProvider` for THAT `HwndSource`'s window, and the
      embedding container is a plain child `HWND`, not an `HwndSource`); raising real, routed
      `PreviewKeyDown`/`KeyDown` through `InputManager.ProcessInput` needed an explicit
      `Keyboard.Focus(this)` first to route to the right element at all (confirmed with logging:
      focus and modifiers both correct, `KeyBinding` still silently did not fire) - and THAT call
      itself moves native Win32 focus from the surface's own child window onto the container, so the
      very next physical keystroke would go to WPF instead of the surface; restoring native focus
      afterwards (tried both synchronously and deferred to the next dispatcher pass) reliably stopped
      the same `KeyBinding` from firing again. Walking `InputBindings` directly and calling
      `ICommand.Execute` sidesteps all of it: it never touches focus, native or logical, so nothing
      needs restoring, and it is exactly what firing a VS-accelerator-shaped `KeyBinding` needs -
      confirmed live, twice, with every `--selftest` check green, including two new checks added for
      exactly this regression (native focus stays on the child after Ctrl+S, and the very next key
      still reaches it).
    - `tabOut`: a new `WM_APP`-based message (`kubuno_controls::host::WM_KUBUNO_TAB_OUT`/
      `notify_tab_out`) moves the WPF focus out with `MoveFocus`/`TraversalRequest`; `TabIntoCore`
      (WPF Tab INTO the surface) matches the spike's own override, exactly (missing from an early
      version of this class - a real regression an interactive `--selftest` run caught, since without
      it WPF has no way to hand the surface the focus on Tab).
  - `SetDocumentText` bridges DSG-3's "buffer is truth" rule onto the current exe (`view_embed.exe`,
    file-polling - DSG-6's own `kubuno-views-designer` process/`kubuno/setBuffer` IPC does not exist
    yet) via a private temp `.kbview` file, swappable for real IPC without touching anything else in
    this class.
  - `spikes/HwndHostSpike` now drives this production class instead of its own (removed) local
    `DesignSurfaceHost`, and its `--selftest` turned two former "INFO, not implemented" lines into
    real `PASS`/`FAIL` checks: Ctrl+S reaching the WPF `KeyBinding` while the surface has focus, and
    Tab exiting the surface once `view_embed.rs`'s own two-control demo focus ring (`save_btn`/
    `menu_btn` - added there for exactly this, since the compiled `.kbview` content's own focus ring
    is `kubuno-views/src/runtime.rs`'s, out of this task's scope) runs past its last control. Also
    fixed a race in the crash-restart check itself: it subscribed to the host's `ChildReady` event
    only AFTER a fixed post-kill wait, which the production restart-with-backoff (250 ms+relaunch, well
    under that wait) had usually already fired by - now subscribes before killing the surface.
  - `RustDesignSurfaceHost` gained a `SurfaceOutputLine` event (every line the surface process writes
    to its own stderr) and the spike now re-publishes it into its own `RustSaid`/`ClearRust` test
    helper with the ORIGINAL spike's "  rust| " prefix. Its absence was a real bug, not a forwarding
    regression: the surface's own `[embed]` trace lines were landing in the shared log file (via
    `KubunoViewsLogHost`, this class's own internal logging) but never reaching the spike's separate
    `_rustLines` list, so `RustSaid` always returned false and every keyboard check read as FAIL even
    when the log line right above it proved the key had, in fact, arrived.
  - `SelfTestAsync` acquires the foreground itself now (`TryAcquireForegroundAsync`:
    `AllowSetForegroundWindow` + an `AttachThreadInput`-brokered `SetForegroundWindow`, falling back to
    a synthetic Alt tap), and fails fast with one clear "environment: no foreground" line instead of
    cascading through every focus-dependent check when a non-interactive launch is denied it entirely.
  - The "Tab exits the surface" check now stops pressing Tab as soon as focus actually leaves the
    child instead of always pressing exactly 6 times: with only 3 WPF-level tab stops in this fixture
    (`_before`/`_after`/the host), continuing to press after tab-out already fired could cycle straight
    back into the host via `TabIntoCore` and read as a false FAIL.
  - Fixed a live crash: the spike's own log writes (UI thread, `SpikeWindow.Log`) and
    `RustDesignSurfaceHost`'s own logging of the surface's stderr (a .NET thread-pool thread, via
    `KubunoViewsLogHost`) each locked a SEPARATE `object` around their own `File.AppendAllText` call to
    the SAME log file - serialising each writer against itself but not against the other, so two
    genuinely concurrent appends could still race and throw `IOException` ("file in use"), unhandled,
    taking the whole process down mid-`--selftest`. Fixed with one shared lock (`SpikeLog`) every
    writer funnels through. `RustDesignSurfaceHost`'s own `ErrorDataReceived` handler also now catches
    around `KubunoViewsLogHost`/`SurfaceOutputLine` individually: a misbehaving logger or subscriber
    must never be able to crash the HOST process (VS, ultimately) from that thread-pool callback.
  - Not wired into the VSIX (`src/Kubuno.VisualStudio/*`, `Kubuno.VisualStudio.sln`) - see this
    library's `INTEGRATION.md` for the remaining steps.

- **`Handlers\HandlerCreationService` (work package DSG-10): double-click a control/event on the
  Events tab -> create its Rust handler.** Calls `kubuno-views-ls`'s new `kubuno/createHandler`
  (via `Handlers\IKubunoViewsLanguageServerClient`, the real implementation
  `Handlers\Infrastructure\JsonRpcKubunoViewsLanguageServerClient` wrapping the same
  `StreamJsonRpc.JsonRpc` `KubunoViewsLanguageClient.Rpc` already exposes) and applies the
  resulting edit through the existing `Editing\*` services - `BufferEditCore.TryApply`, called
  ONCE PER FILE in the response's `edit.changes` map (never `CompoundEditCoordinator`, which links
  several requests into one undo unit: the `.kbview` attribute edit and the code-behind `.rs` stub
  must stay two SEPARATE undo units), then opens the code-behind file at the new `fn` via
  `Handlers\IHandlerDocumentHost` (real implementation `Handlers\Infrastructure
  \VsHandlerDocumentHost`, bridging `IVsTextLines`/`ITextBuffer` the same way `INTEGRATION.md` §8
  point 1 already documents). When the event already names a handler, it instead just navigates to
  that handler's existing definition - no edit. `Handlers\CreateHandlerResponseParser` is the pure,
  `System.Text.Json`-based half that turns the RPC's JSON result into typed DTOs, decoupled from the
  transport (reuses DSG-2/DSG-5's own `Editing\TextEditDto`/`LspRange`/`LspPosition` for every
  file's edits - no second edit type). `Properties\EventRowViewModel`/`PropertiesPanelViewModel`
  gained the `DocumentUri`/`ElementId` a "create handler" request needs (the minimal `Properties\`
  change this package's own brief calls for); `Handlers\EventsTabHandlerCreationBridge` is the one
  line of wiring from `EventRowViewModel.CreateHandlerRequested` to the new service. Unit-tested
  with fakes (`tests\Kubuno.VisualStudio.Designer.Tests\Handlers\`, incl. a version-drift test for
  the apply-rejection path); the two real, VS/JsonRpc-dependent adapters are left for a manual
  check in the experimental instance, same as this library's other VS-SDK-bound classes. See
  `desktop` repo's own CHANGELOG for the `kubuno-views-ls` half (`kubuno/createHandler`).

### Changed

- `samples/hello-rust/src/main.rs` binds `greet(...)`'s result to a local `greeting: String` before
  printing it, instead of passing the call inline - gives the sample a concrete `String` local to
  breakpoint/inspect (used to verify natvis end to end for this task; also a generally more useful
  manual-testing fixture, matching `lib.rs`'s own stated purpose for this crate).

### Fixed

- **Test Explorer discovery/execution for a real Cargo.toml, live-verified end to end** (2 Rust
  tests discovered, both run and reported passed): `Kubuno.TestAdapter.dll`, loaded by VSTest's
  out-of-process discovery/execution host, failed with `FileNotFoundException: Could not load
  ... 'System.Text.Json'` (then, once that was shipped, `Microsoft.Bcl.AsyncInterfaces`) the
  moment it called into `Kubuno.Cargo` - unlike the in-proc VSSDK `AsyncPackage` AppDomain, that
  host does not probe an extension assembly's own directory for its dependencies, even though the
  files sit right next to it in the deployed VSIX (read the actual failure by dumping the "Tests"
  Output pane's `TextDocument` through `EnvDTE`, not by guessing). Fixed two ways: a new
  `Kubuno.TestAdapter/AssemblyResolution.cs` installs a normal `AppDomain.AssemblyResolve` handler
  (hooked from the static constructors of `KubunoTestDiscoverer`/`KubunoTestExecutor`/
  `KubunoTestContainerDiscoverer`) that resolves from the adapter's own directory - the standard
  pattern for VSTest adapters with a non-trivial dependency closure; and `Kubuno.VisualStudio.csproj`
  now also ships `Microsoft.Bcl.AsyncInterfaces.dll` and `System.ValueTuple.dll` as explicit
  `Content` items (found missing by diffing the deployed extension folder against
  `Kubuno.TestAdapter`'s own plain `dotnet build` output, which does copy its full transitive
  closure correctly).
- **`.kbview` language support now actually runs: `kubuno-views-ls.exe` starts, and diagnostics and
  completion work in Visual Studio** (verified live in the experimental instance on a scratch copy of
  `kubuno-views/examples/views/settings.kbview`: an unknown `<Bogus/>` element shows up in the Error
  List as ``unknown element `<Bogus>` `` and disappears on undo; `textDocument/completion` after `<`
  returns `Button`, `Switch`, `TextField`, `Card`...). Four separate defects stacked on top of each
  other, each confirmed with evidence rather than guessed:
  - **Root cause - `.kbview` was opened by VS's XML editor.** `.kbview` is an extension no editor
    registers, and since its content looks like XML, VS opened it in its XML editor (DTE
    `Document.Language` = `"XML"`, versus `"Plain Text"` for a `.rs` file): the buffer got the XML
    language service and content type, never `"kbview"`, so no `ILanguageClient` bound to `"kbview"`
    could ever be activated. `languages.pkgdef` now maps `.kbview` explicitly to VS's core text
    editor (`Editors\{8B382828-6202-11d1-8870-0000F87579D2}\Extensions`, `"kbview"=dword:0x64`); the
    buffer now gets the `"kbview"` content type from `Kubuno.VisualStudio.Views`' own MEF exports
    and the client activates.
  - **The runtime DLLs were not shipped.** `kubuno-views-ls.exe` imports `kubuno_ui.dll` and Rust's
    `std-<hash>.dll` (the desktop workspace links `kubuno-ui` as a dylib with `-C prefer-dynamic`),
    so the exe shipped alone in `tools\` died at load time with `STATUS_DLL_NOT_FOUND`
    (`0xC0000135`), which VS reported as a JSON-RPC `ConnectionLostException` during `initialize`.
    `Kubuno.VisualStudio.csproj` now ships both DLLs beside it: `kubuno_ui.dll` from the same cargo
    build as the exe (a Rust dylib has no stable ABI), `std-*.dll` from the toolchain
    (`KubunoRustStdDir`, default: the stable MSVC toolchain), with a build error if either is missing.
  - **VS never sent `didOpen`/`didChange`** because the server advertised the bare
    `textDocumentSync: 1` shorthand, which VS's LSP client treats as "no open/close notifications"
    (no diagnostics; `documentSymbol` answered `null` for a document the server was never told
    about). Fixed server-side in the `desktop` repo (`kubuno-views-ls` now advertises
    `{ openClose: true, change: Full }`).
  - **`kubuno-views-ls` never exited** after `exit` or when VS closed (a classic `lsp-server`
    deadlock: joining the I/O threads while still holding the connection's sender), leaving an
    orphan process that locked the extension's `tools\` folder. Also fixed in the `desktop` repo.
- **Removed the earlier `.kbview` workarounds that did not address the cause**: the duplicate
  `.kbview`/`"kbview"` content-type exports in `Kubuno.VisualStudio.dll` (`Kubuno.VisualStudio.Views.dll`'s
  own exports compose and work once the XML editor no longer claims the file) and the forwarding
  `LanguageService/KbviewLanguageClient.cs` wrapper - which had never actually been compiled (the
  old-style `Kubuno.VisualStudio.csproj` lists its `Compile` items explicitly and did not list it),
  which is why its diagnostic log line never appeared. Its absence from the stale MEF cache
  (`ComponentModelCache`, older than the deployed DLL) was consistent with that.

### Fixed (continued)

- **`Debugging/NatvisInstaller.cs` now installs into both `Documents\Visual Studio 2022\Visualizers`
  and `Documents\Visual Studio 18\Visualizers`**, idempotently, instead of gambling on a single
  folder name: which one VS 18 (2026) actually reads could not be settled live (`EnvDTE`
  automation of `Debug.Start`/`Debug.StartDebugTarget` proved too unreliable in this repo's
  scripted-testing setup - `E_FAIL`/hung `RPC_E_CALL_REJECTED` COM calls with no actionable detail
  - to drive an actual F5 session and read the Locals window), and installing into an unused
  folder is harmless while installing into only the wrong one silently breaks natvis for every
  VS 18 user. Current Microsoft Learn documentation still gives the 2022 folder name as the
  example even under the moniker range covering the latest/2026 version, so that one is kept
  installed too rather than replaced.

### Known limitations

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
