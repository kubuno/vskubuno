# Integrating Kubuno.Views.Designer into the Kubuno.VisualStudio VSIX

`Kubuno.Views.Designer` is a standalone net48 class library (same shape as
`Kubuno.Views` - see that project's own `INTEGRATION.md` for the precedent this one
follows): it builds and its tests run on their own (`dotnet build`/`dotnet test`, with
`COMPLUS_LoadFromRemoteSources=1` on a mapped network drive such as `Z:`), but it produces no VSIX of
its own and is never installed/loaded standalone. This document is the checklist for wiring it into
`src/Kubuno.VisualStudio` (the VSIX project) once that project's own current work is done. Nothing in
this document has been applied to the VSIX project - by design, this task must not touch
`src/Kubuno.VisualStudio/`, `Kubuno.VisualStudio.sln`, `README.md` or `CHANGELOG.md`, and this library
is deliberately **not yet** added to `Kubuno.VisualStudio.sln` either (same reason
`Kubuno.Views` was not added by its own integration task - it builds standalone until
this step runs).

What this library provides, at a glance (see the class doc comments for the full reasoning):

- `EditorFactory\KbviewEditorFactory.cs` - `IVsEditorFactory` for `.kbview`, producing a split
  Design | XML `WindowPane`.
- `EditorFactory\DesignerWindowPane.cs` - the `WindowPane` itself; all of its content is
  `UI\DesignerSplitView.cs`.
- `UI\DesignerSplitView.cs` / `DesignerSplitViewModel.cs` / `DesignerViewMode.cs` - the Design/XML/
  Split orientation tab strip and the two-pane layout (code-behind only, no .xaml, matching the rest
  of this repo).
- `UI\CodeWindowHost.cs` - a WPF `HwndHost` embedding a real `IVsCodeWindow` bound to the same
  `IVsTextLines` buffer the editor factory created/reused, so kubuno-views-ls's existing MEF
  `ILanguageClient` (`Kubuno.Views.LanguageService.KubunoViewsLanguageClient`, which
  attaches by "kbview" content type, independent of which editor opened the file) keeps working
  unchanged.
- `DesignSurface\IDesignSurfaceHost.cs` / `IDesignSurfaceHostFactory.cs` /
  `DesignSurfaceHostFactoryHost.cs` - the seam DSG-7 plugs its real embedded render surface into.
  `DesignSurface\RustDesignSurfaceHost.cs` / `RustDesignSurfaceHostFactory.cs` (DSG-7, production)
  are now the real implementation, turning `docs/DESIGNER.md` §7's spike
  (`spikes/HwndHostSpike`) into code: the `HwndHost` embedding, the Job Object/restart-backoff/
  `SetErrorMode`/DLL-check lifecycle, and the `unhandledKey`/`tabOut` keyboard protocol - see that
  class's own doc comment for the full design, and §6 below for what the VSIX still needs to do to
  actually use it (`DesignSurfaceHostFactoryHost.Current` still defaults to
  `PlaceholderDesignSurfaceHostFactory.Instance` until it does).
- `Options\KbviewDesignerOptionsPage.cs` - the "Use as default editor" toggle (off by default).
- `Registry\*` (DSG-4) - a C# model of the `kubuno/registry` JSON shape (`docs/DESIGNER.md` §5:
  `ComponentMeta`/`PropertyMeta`/`EventMeta`/`PropKind`/`ChildrenModel`/`LayoutKind`) and a loader
  (`ComponentRegistry.FromJson`) - not wired to any live JSON-RPC call yet, since DSG-1's
  `kubuno/registry` endpoint doesn't exist yet either; see §7 below for how the VSIX step should get
  it a real `ComponentRegistry` once it does.
- `Toolbox\ToolboxView.cs`/`ToolboxViewModel.cs` and `Properties\PropertiesPanelView.cs`/
  `PropertiesPanelViewModel.cs` (DSG-4) - the Toolbox and Properties/Events WPF content, both plain
  `UserControl`s (not yet `ToolWindowPane`s - see §7) driven by a `Registry.ComponentRegistry`.
- `Editing\*` (DSG-5) - the pure `{range, newText}` → buffer-edit pipeline
  (`LspPositionMapper`/`TextEditPlanner`/`BufferEditCore`/`CompoundEditCoordinator`) plus its two
  real, VS-dependent adapters, `Editing\Infrastructure\BufferEditApplier.cs`
  (`Microsoft.VisualStudio.Text.ITextBuffer` → one `ITextEdit` per request) and
  `Editing\Infrastructure\DesignerUndoScope.cs` (`ITextUndoHistory` → linked undo across several
  requests) - see §8 below for what still needs a real caller.

## 1. csproj reference

Add a `ProjectReference` to `Kubuno.VisualStudio.csproj`, in the same style already used there for
`Kubuno.Rust.Logic`/`Kubuno.Views` (see that file's existing `ItemGroup`, and
`Kubuno.Views/INTEGRATION.md` §1 for the precedent):

```xml
<ProjectReference Include="..\Kubuno.VisualStudio.Designer\Kubuno.VisualStudio.Designer.csproj">
  <Project>{PUT-A-NEW-GUID-HERE}</Project>
  <Name>Kubuno.Views.Designer</Name>
  <IncludeOutputGroupsInVSIX>BuiltProjectOutputGroup%3bBuiltProjectOutputGroupDependencies%3bGetCopyToOutputDirectoryItems%3bSatelliteDllsProjectOutputGroup%3b</IncludeOutputGroupsInVSIX>
  <IncludeOutputGroupsInVSIXLocalOnly>DebugSymbolsProjectOutputGroup%3b</IncludeOutputGroupsInVSIXLocalOnly>
</ProjectReference>
```

`Kubuno.Views.Designer.csproj` already references the exact same `Microsoft.VisualStudio.SDK`
package version `Kubuno.VisualStudio.csproj` and `Kubuno.Views.csproj` do (verified by
reading both before this library was written) - no version reconciliation should be needed. It also
already has a `ProjectReference` to `Kubuno.Views.csproj` (for `KbviewConstants`/
`IKubunoLog`/`KubunoViewsLogHost` - see this library's own csproj comment), so once the VSIX
references this project, both are pulled in; do not add a second, separate reference to
`Kubuno.Views` because of that transitive edge - the existing one already covers it,
exactly as `Kubuno.Rust.Cargo`'s dependencies flow through today.

Add the new project to `Kubuno.VisualStudio.sln` (a new `Project(...)` entry plus the matching
`Debug|AnyCPU`/`Release|AnyCPU` lines in `ProjectConfigurationPlatforms`, following the existing
entries for `Kubuno.Rust.Logic`), and add `tests\Kubuno.Desktop.Tests\Designer` likewise,
so `dotnet test`/the solution build cover it going forward - mirroring exactly what
`Kubuno.Views/INTEGRATION.md` §1 asks for that library.

## 2. No MEF pickup needed for the editor factory itself

Unlike `Kubuno.Views` (whose content-type/language-client classes are `[Export]`ed and
picked up by MEF automatically once the DLL is in the VSIX output directory - see that project's
`INTEGRATION.md` §2), `KbviewEditorFactory` is a **classic, package-registered** editor factory, not a
MEF component: `IVsEditorFactory`/`IVsRegisterEditors` predate MEF-based extensibility, and VS's own
"Open With"/default-editor machinery keys off `[ProvideEditorFactory]`/`[ProvideEditorExtension]`
pkgdef registration plus an explicit `Package.RegisterEditorFactory` call (§3 below) - not off
assembly presence. No `Asset Type="Microsoft.VisualStudio.MefComponent"` entry is needed for this
part; the VSIX's existing single `MefComponent` asset (pointing at `%CurrentProject%`) still covers
`Kubuno.Views`' own MEF exports unchanged.

## 3. Editor factory registration (`KubunoPackage.cs`)

Add to `KubunoPackage.cs`, alongside the existing package-level attributes:

```csharp
[ProvideEditorFactory(typeof(Kubuno.Views.Designer.EditorFactory.KbviewEditorFactory), 110 /* VSPackage.resx string id - see note below */)]
[ProvideEditorLogicalView(typeof(Kubuno.Views.Designer.EditorFactory.KbviewEditorFactory), "{7651a703-06e5-11d1-8ebd-00a0c90f26ea}")] // LOGVIEWID_Designer
[ProvideEditorLogicalView(typeof(Kubuno.Views.Designer.EditorFactory.KbviewEditorFactory), "{7651a704-06e5-11d1-8ebd-00a0c90f26ea}")] // LOGVIEWID_TextView
[ProvideEditorExtension(typeof(Kubuno.Views.Designer.EditorFactory.KbviewEditorFactory), Kubuno.Views.KbviewConstants.FileExtension, Kubuno.Views.Designer.DesignerConstants.EditorExtensionPriority)]
```

Notes:

- The `110` display-name resource id is a placeholder: this VSIX currently has **no `VSPackage.resx`**
  (checked - `src/Kubuno.VisualStudio/Resources/` has none), because nothing else here has needed one
  yet (the Rust `ILanguageClient` is MEF, not a `ProvideEditorFactory`-style registration).
  `ProvideEditorFactoryAttribute`'s constructor is `(Type factoryType, short nameResourceID)` - it
  needs a `#nnn`-style resource id resolving through the package's satellite resources, the standard
  VSSDK convention (see any `New Editor Extension` project template). Add a `VSPackage.resx` (or reuse
  one if this task's own work created one in parallel) with an entry whose value is
  `Kubuno.Views.Designer.DesignerConstants.EditorName` ("Kubuno View Designer"), and pick a
  free id in whatever numbering scheme that resx ends up using.
- The two `ProvideEditorLogicalView` GUIDs are `VSConstants.LOGVIEWID_Designer` and
  `VSConstants.LOGVIEWID_TextView` written as literal strings (verified against
  `Microsoft.VisualStudio.NativeMethods`'s own field initializers - both are stable, well-known VS
  GUIDs, not invented here). `KbviewEditorFactory.MapLogicalView` already accepts both (and
  `LOGVIEWID_Primary`) and maps either to the one physical "Design" view - see that method's own
  remarks for why a single physical view is correct here.
- `Kubuno.Views.Designer.DesignerConstants.EditorExtensionPriority` (`0x60`) is deliberately
  higher (lower-precedence) than the `0x32` many VSSDK samples use for a *default* editor - see
  `DesignerConstants.cs`'s own doc comment. **This priority number is the one part of "Open With
  registration" that could not be verified without a live VS instance in this task** (no `devenv` was
  run here - see the repo's own `CLAUDE.md`, "toujours tester réellement" - and per this task's
  instructions, no VS was launched except via PowerShell if truly needed, which it was not for a
  C#-only skeleton). Verify in the experimental instance after wiring this up:
  1. `.kbview` files should still **open in the plain text editor on double-click**. This is no
     longer implicit: `src/Rust/Kubuno.Rust/languages.pkgdef` now maps `.kbview` explicitly to VS's
     built-in Source Code (Text) Editor (`Editors\{8B382828-6202-11d1-8870-0000F87579D2}\Extensions`,
     `"kbview"=dword:00000064`), because without that key VS opened it in its XML editor (DTE
     `Document.Language` was "XML" - the content looks like XML), the buffer got the XML content type and the kbview
     language client never activated (root-caused live - see the repo CHANGELOG). The editor with the
     HIGHEST value in these `Extensions` keys is the double-click default (VSSDK
     `ProvideEditorExtension` semantics), so the designer's `0x60` stays below the text editor's
     `0x64` - keep it that way, and re-check that the language client still activates for a `.kbview`
     opened in the designer's embedded code view once the factory is registered.
  2. "Open With..." on a `.kbview` file should list **both** "Kubuno View Designer" and the plain text
     editor.

In `KubunoPackage.InitializeAsync` (after the package is sited), register the factory instance itself
- `[ProvideEditorFactory]` only emits pkgdef metadata; VS still needs a live instance handed to it:

```csharp
RegisterEditorFactory(new Kubuno.Views.Designer.EditorFactory.KbviewEditorFactory());
```

(`RegisterEditorFactory` is `Microsoft.VisualStudio.Shell.Package`'s own helper - it calls
`IVsRegisterEditors.RegisterEditor` and disposes the factory on package teardown automatically, the
same pattern `AsyncPackage`-derived packages use for any classic, non-MEF editor factory.)

## 4. Options page registration

`Options\KbviewDesignerOptionsPage.cs` (in this library) is a real `DialogPage`, exactly like
`Kubuno.Views.Options.KbviewOptionsPage` - see that library's own `INTEGRATION.md` §4 for
the precedent. Add to `KubunoPackage.cs`:

```csharp
[ProvideOptionPage(typeof(Kubuno.Views.Designer.Options.KbviewDesignerOptionsPage), Constants.OptionsCategoryName, Kubuno.Views.Designer.DesignerConstants.OptionsPageName, 0, 0, supportsAutomation: true)]
[ProvideProfile(typeof(Kubuno.Views.Designer.Options.KbviewDesignerOptionsPage), Constants.OptionsCategoryName, Kubuno.Views.Designer.DesignerConstants.OptionsPageName, 0, 0, isToolsOptionPage: true)]
```

(`Constants.OptionsCategoryName` is already `"Kubuno"`, shared with the Rust and Views options pages -
no change needed there.)

Then, in `KubunoPackage.InitializeAsync`, publish the page into this library's static gateway, the
same shape as `KubunoViewsOptionsHost.Current`:

```csharp
Kubuno.Views.Designer.Options.DesignerOptionsHost.Current =
    (Kubuno.Views.Designer.Options.KbviewDesignerOptionsPage)GetDialogPage(typeof(Kubuno.Views.Designer.Options.KbviewDesignerOptionsPage));
```

**What actually reads this option today: nothing yet.** `KbviewEditorFactory`/`CreateEditorInstance`
does not currently branch on `DesignerOptionsHost.Current?.UseDesignerAsDefaultEditor` - static
`ProvideEditorExtension` priority (§3) cannot be driven by a runtime `DialogPage` value (VS resolves
"which registered editor is the double-click default" before any of this library's code runs, from
pkgdef-time-baked priorities and the user's own persisted "Open With... > Set as Default" choice - see
§3's numbered verification steps). Two honest options for making the toggle actually take effect,
neither implemented here because both need the experimental instance to confirm which is real:

1. **Restart-required, still simple:** have `KubunoPackage.InitializeAsync` read the option and, if
   `UseDesignerAsDefaultEditor` is true, write the per-extension priority override directly into the
   same registry location `ProvideEditorExtension` would have (the user hive under
   `Editors\{DesignerConstants.EditorFactoryGuidString}\Extensions\kbview`), swapping this factory's
   effective priority below the plain editor's. Needs a VS restart to take effect (registration is
   read once at startup) - acceptable given the option's own description already says so once this
   wording is added.
2. **No registry writes:** leave registration exactly as in §3 (designer never double-click-default)
   and instead make the option only control a *convenience*: when true, `KubunoPackage.InitializeAsync`
   (or a `IVsRunningDocTableEvents3.OnBeforeDocumentWindowShow` handler) proactively reopens any
   `.kbview` document with the Designer's logical view instead of leaving VS's own choice, using
   `IVsUIShellOpenDocument.OpenSpecificEditor` right after the plain editor would otherwise open. This
   avoids touching the registry at all but only helps for windows opened after the package is loaded.

Document whichever of these (or another approach the integration step's author finds cleaner) is
chosen, and update this option's own `[Description]` in `KbviewDesignerOptionsPage.cs` to match -
right now it only promises the designer becomes "the editor a double-click opens", which is accurate
for approach 1 but not yet true for approach 2's narrower behavior.

## 5. Command UI context (reserved, not required to integrate)

`DesignerConstants.CommandUiContextGuidString` is returned as `KbviewEditorFactory.CreateEditorInstance`'s
`pguidCmdUI` out parameter today but is not bound to any `<CommandFlag>DefaultDisabled</CommandFlag>`/
`UIContext` in `KubunoCommands.vsct`. The Toolbox/Properties-Events tool windows §7 below describes will
likely want a `[ProvideAutoLoad]`/`VSCT` visibility rule keyed off this GUID so they only auto-show while
a `.kbview` designer pane has focus - left for that step, since this task's scope stops at the WPF
content itself, not its `ToolWindowPane` hosting.

## 6. `IDesignSurfaceHost` seam for DSG-7

`RustDesignSurfaceHost`/`RustDesignSurfaceHostFactory` (`DesignSurface\RustDesignSurfaceHost.cs`) are
the real implementation now, but `DesignSurfaceHostFactoryHost.Current` still defaults to
`PlaceholderDesignSurfaceHostFactory.Instance` - this task deliberately did not flip that switch (it
must not touch `KubunoPackage.cs`/the VSIX project, per this document's own top note). What the VSIX
integration step needs to do:

1. **Resolve the design surface exe's path.** There is no locator for it yet (unlike
   `KubunoViewsLanguageServerLocator` for `kubuno-views-ls.exe` -
   `Kubuno.Views/Locating/KubunoViewsLanguageServerLocator.cs`), because the exe itself
   does not exist as a shipped artifact yet: DSG-6 (`docs/DESIGNER.md` §6) is still examples-only
   (`kubuno-desktop-views/examples/view_embed.rs`, promoted out of `examples/` into a real
   `kubuno-views-designer` crate is that package's own scope). Until DSG-6 ships a real binary, either
   point `RustDesignSurfaceHostFactory` at a dev build of `view_embed.exe` (mirroring
   `KubunoViewsLsExePath`'s own MSBuild-property convention in `Kubuno.VisualStudio.csproj` - e.g. a
   new `KubunoViewsDesignerExePath` defaulting under the same `C:\kubuno-build\desktop-target`
   convention) or write a proper locator once DSG-6 lands, following
   `KubunoViewsLanguageServerLocator`'s exact order (option override → extension `tools\` folder →
   PATH → dev build folders).
2. **Set the factory**, in `KubunoPackage.InitializeAsync`, alongside the other static-gateway
   assignments (§3/§4's own pattern):
   ```csharp
   Kubuno.Views.Designer.DesignSurface.DesignSurfaceHostFactoryHost.Current =
       new Kubuno.Desktop.Designer.DesignSurface.RustDesignSurfaceHostFactory(resolvedExePath);
   ```
   No change to `DesignerWindowPane`/`DesignerSplitView` is needed - both already go through
   `DesignSurfaceHostFactoryHost.Current`, never `PlaceholderDesignSurfaceHostFactory` directly.
3. **`RustDesignSurfaceHost.VsFilterKeys`** (see its own doc comment): the real
   `IVsFilterKeys2.TranslateAcceleratorEx` path for the `unhandledKey` protocol was deliberately left
   unwired in this task - guessing that COM signature without a live `devenv.exe` to check it against
   risked a silently wrong integration (`ComponentDispatcher.RaiseThreadMessage` alone, the tested
   fallback, already routes a forwarded key into WPF's own accelerator/mnemonic processing - verified
   end to end via `spikes/HwndHostSpike --selftest`). Wiring the real VS path is: obtain
   `SVsFilterKeys`/`IVsFilterKeys2` from the package's service provider, and set
   `RustDesignSurfaceHostFactory`'s `vsFilterKeys` constructor argument to a delegate calling
   `TranslateAcceleratorEx` on it. Verify in the experimental instance that this actually improves on
   `ComponentDispatcher` alone (docs/DESIGNER.md §7 lists this among what the spike could not check)
   before relying on it over the fallback.
4. **Still to verify live** (docs/DESIGNER.md §7's own list, not exercised by the standalone spike):
   the same `HwndHost` inside a real `WindowPane`/tool window, docking/undocking (popup ownership must
   be re-checked after a re-dock - the surface's `kubuno_desktop_ui` popups are owned top-levels resolved
   through the container's ancestor chain), VS theme switches, and whether
   `ComponentDispatcher.RaiseThreadMessage` alone is enough for VS's OWN accelerator table (Ctrl+S,
   F5, Ctrl+Shift+B...) the way it is for the spike's plain WPF `KeyBinding`, or whether point 3's
   `IVsFilterKeys2` path turns out to be required after all.

## 7. Hosting the Toolbox/Properties WPF content as VS tool windows (DSG-4)

`Toolbox\ToolboxView.cs` and `Properties\PropertiesPanelView.cs` are plain `UserControl`s, each
constructed from a view-model (`ToolboxViewModel`/`PropertiesPanelViewModel`) that this library builds
from a `Registry.ComponentRegistry` - they are not `ToolWindowPane`s yet, and this task deliberately
stopped there rather than adding package-registered tool windows, for the same reason `KbviewEditorFactory`
itself is a plain class until §3's `[ProvideEditorFactory]`/`RegisterEditorFactory` step: a `ToolWindowPane`
only becomes a real VS tool window once a package class declares `[ProvideToolWindow]` and calls
`FindToolWindow`/`ShowToolWindow`, and this library must not reference `Kubuno.VisualStudio` (see the
csproj's own top comment).

What the VSIX integration step still needs to do:

1. **Two thin `ToolWindowPane` subclasses**, most naturally added to this library (mirroring
   `EditorFactory\DesignerWindowPane.cs`'s own shape) so the VSIX only adds the `[ProvideToolWindow]`
   attribute and a `Tools > Kubuno View Toolbox`/`Tools > Kubuno View Properties` command to show them:
   ```csharp
   public sealed class ToolboxToolWindow : ToolWindowPane
   {
       public ToolboxToolWindow() => Caption = "Kubuno Toolbox";
       // Content = new ToolboxView(viewModel) once a ComponentRegistry is available - see point 2.
   }
   ```
   (and the equivalent `PropertiesToolWindow` wrapping `PropertiesPanelView`). Each needs a stable GUID
   the same way `DesignerConstants.EditorFactoryGuidString` is one for the editor factory - add
   `DesignerConstants.ToolboxToolWindowGuidString`/`PropertiesToolWindowGuidString` alongside it rather
   than inventing new ones ad hoc in the VSIX project.
2. **Getting a live `ComponentRegistry`.** `ComponentRegistry.FromJson` only knows how to parse a JSON
   string - it is deliberately unaware of where that string comes from (see its own doc comment). The
   VSIX step needs to actually call `kubuno/registry` on the running `kubuno-views-ls`
   `ILanguageClient`'s `Rpc` (`StreamJsonRpc.JsonRpc`, the same object
   `KubunoViewsLanguageClient.AttachForCustomMessageAsync` already exposes per docs/DESIGNER.md §3) once
   DSG-1 adds that method server-side, cache the resulting `ComponentRegistry` for the language client's
   session (§5 of docs/DESIGNER.md: "the registry cannot change without restarting the process that
   computed it"), and hand it to both tool windows' view-models when a `.kbview` designer pane gets
   focus. Until DSG-1 lands, `ComponentRegistry.Empty` is a safe placeholder - both view-models handle
   zero components fine (empty family list, no toolbox items).
3. **Selection wiring (DSG-8's job, not this one).** `PropertiesPanelViewModel.SetSelection`/
   `ClearSelection` need a caller that knows the currently-selected element's `ComponentMeta`/attribute
   values/event handlers - that caller is docs/DESIGNER.md §6's DSG-8 (bidirectional selection sync),
   not this step; until DSG-8 exists, the Properties tool window can simply stay on
   `ClearSelection`'s empty state.
4. Bind each row's edit (`PropertyRowViewModel.ValueCommitted`, `EventRowViewModel
   .CreateHandlerRequested`, `PropertiesPanelViewModel.CreateHandlerRequested`) to §8 below's
   `Editing\*` pipeline / DSG-10's handler-insertion flow respectively - again a later step's wiring,
   not something this library can call into itself (it has no dependency on either).
5. **The `CommandUiContextGuidString` visibility rule** §5 above already flags: without it, both tool
   windows would be generically available from the `Tools`/`View` menu at all times instead of only
   while a `.kbview` designer pane has focus, which is a worse (but not broken) default if this step is
   skipped initially.

## 8. Wiring `Editing\*` into a real gesture (DSG-5)

`Editing\BufferEditCore`/`CompoundEditCoordinator` are ready to call today - they only need an
`ApplyEditRequest` (a base version + a list of `{range, newText}` edits) and, for the real adapters,
a live `Microsoft.VisualStudio.Text.ITextBuffer`/`ITextUndoHistory`. Nothing here is VSIX-specific by
itself, but three things a later package supplies are still missing before any gesture can actually
reach it:

1. **Getting the real `ITextBuffer`/`ITextUndoHistory` for a `.kbview` document.** This library's
   editor factory only deals in the legacy `IVsTextLines`/`IVsCodeWindow` COM types (see
   `UI\VsTextLinesText.cs`'s own doc comment - "there is no single 'get all text' method on the
   interface" is exactly the kind of thing the modern editor API fixes). The standard bridge is
   `IVsEditorAdaptersFactoryService.GetDataBuffer(IVsTextLines)` (MEF-imported, from
   `Microsoft.VisualStudio.Editor`) to get an `ITextBuffer`, and `ITextUndoHistoryRegistry
   .RegisterHistory(buffer)`/`.GetHistory(buffer)` (from `Microsoft.VisualStudio.Text.Operations`,
   also MEF-imported) for its `ITextUndoHistory`. Both services are ordinary MEF components the VSIX
   package (or a small MEF-exported component in this library, once it is referenced) can `[Import]`;
   neither needs `Kubuno.VisualStudio` itself, so this can live in this library if preferred, but the
   `[Import]` plumbing needs the VSIX's MEF catalog to actually compose it, which is why it's called out
   here rather than done in this task.
2. **`kubuno/applyEdit` itself (DSG-2, not built yet).** `ApplyEditRequest`'s edits are exactly
   `kubuno/applyEdit`'s `{range, newText}` result shape (docs/DESIGNER.md §2/§3) - once DSG-2 adds that
   method to `kubuno-views-ls`, the caller is: compute the semantic edit (`SetAttribute`/`InsertChild`/
   etc.) → call `kubuno/applyEdit` over the same `Rpc` object §7 point 2 uses → wrap the `{range,
   newText}` result(s) in an `ApplyEditRequest` with the buffer's current version → call
   `new BufferEditApplier(buffer).Apply(request)` for a single edit, or
   `new CompoundEditCoordinator(new BufferEditApplier(buffer), new DesignerUndoScope(history)).ApplyAll(description, requests)`
   for a gesture needing more than one round trip (see that class's own doc comment for exactly when).
3. **The actual gesture sources** (a drag's mouse-up, a Properties grid edit, a toolbox drop, double-
   click-to-create-handler) are DSG-8/DSG-9/DSG-10's own concern; this library only exposes the hooks
   they should call into (`PropertyRowViewModel.ValueCommitted`, `EventRowViewModel
   .CreateHandlerRequested`, and whatever DSG-9 adds for drag/drop) plus the buffer-apply pipeline
   itself, not a Rust-computed-edit → buffer-write path wired end to end.

## 9. Selection sync & Outline (DSG-8)

`Selection\SelectionSyncService` (docs/DESIGNER.md §6/§8/§9) is ready to construct today - it only
needs five collaborators, all behind small interfaces this library already defines, plus the
`Outline\` tool-window content. Nothing here is VSIX-specific by itself, but every real collaborator
still needs a live VS/JsonRpc object only the VSIX integration step can hand it:

1. **`ITextViewSelectionAdapter` - `Selection.Infrastructure.VsTextViewSelectionAdapter`.** Needs a
   real `IVsTextView` - exactly `UI.CodeWindowHost.PrimaryView` (that property's own doc: "Callers
   (DSG-8's selection sync) must tolerate null here and re-query later"). Construct
   `new VsTextViewSelectionAdapter(codeWindowHost.PrimaryView)` once `PrimaryView` is non-null (poll
   or retry after `DesignerSplitView`/`CodeWindowHost` construction the same way §6/§8 above already
   describe for other DSG-7/DSG-8 pieces needing a laid-out view); `Dispose()` it when the designer
   pane closes (it owns a `DispatcherTimer` - see that class's own doc comment for why it polls
   instead of subscribing to a real caret-changed event).
2. **`IDesignSurfaceSelectionTarget` - `Selection.Infrastructure.DesignSurfaceSelectionTarget`.**
   Needs the same `RustDesignSurfaceHost` instance `DesignerSplitView`/`DesignSurfaceEditingCoordinator`
   already hold (constructed by `DesignSurfaceHostFactoryHost.Current`, §6 above) - the SAME object
   also serves as the `IDesignSurfaceHost` argument below (it implements both roles; this library
   just does not let `SelectionSyncService` assume that via a single interface - see
   `IDesignSurfaceSelectionTarget`'s own doc comment for why).
3. **`IViewsSelectionLanguageServerClient` - `Selection.Infrastructure.JsonRpcViewsSelectionLanguageServerClient`.**
   Needs the same `StreamJsonRpc.JsonRpc` object §7 point 2/§8 point 2 above already get from
   `KubunoViewsLanguageClient.AttachForCustomMessageAsync` - construct one instance per open
   `.kbview` document (or share one across documents; the interface takes a `documentUri` per call,
   so either works) the same way `Handlers.Infrastructure.JsonRpcKubunoViewsLanguageServerClient` is
   already constructed for DSG-10.
4. **`Registry.ComponentRegistry`** - the same live registry §7 point 2 above already gets from
   `kubuno/registry`, cached for the language client's session.
5. **`Properties.PropertiesPanelViewModel`** - the same instance the Properties tool window (§7
   above) already shows; `SelectionSyncService` drives its `SetSelection`/`ClearSelection` directly,
   so no separate wiring is needed for the Properties panel to follow the selection once the service
   itself is constructed.
6. **`IOutlineSelectionTarget` (optional)** - a new `Outline.OutlineViewModel` instance, most
   naturally owned by a new `OutlineToolWindow : ToolWindowPane` (the same shape §7 point 1 above
   already asks for the Toolbox/Properties tool windows - add
   `DesignerConstants.OutlineToolWindowGuidString` alongside the other two GUIDs, and a
   "Tools > Other Windows > Kubuno View Outline" command). Populate it from
   `DocumentSymbolTreeBuilder.Build(await client.DocumentSymbolAsync(documentUri, ct))` once when the
   pane opens, and again on every debounced buffer change (the same `ITextBuffer.Changed`
   subscription `DesignSurfaceEditingCoordinator`'s own 200 ms debounce, §6 above, already
   demonstrates the shape of - reuse that cadence rather than inventing a second one). Subscribe
   `outlineViewModel.NodeActivated += (_, id) => _ = selectionSyncService.SelectFromOutlineAsync(id);`
   to close the loop. Construct `SelectionSyncService` with this view-model as its last
   (`outline`) constructor argument; omit it (`null`, the default) if the Outline tool window is not
   open/available for a given pane - `SelectionSyncService` tolerates that (`_outline?.Select(...)`).

Putting it together, once a `.kbview` `DesignerWindowPane` has a laid-out `CodeWindowHost.PrimaryView`
and a live `JsonRpc`:

```csharp
var textView = new VsTextViewSelectionAdapter(codeWindowHost.PrimaryView);
var surfaceTarget = new DesignSurfaceSelectionTarget(rustDesignSurfaceHost);
var client = new JsonRpcViewsSelectionLanguageServerClient(rpc);
var service = new SelectionSyncService(
    rustDesignSurfaceHost, surfaceTarget, textView, client, componentRegistry,
    propertiesPanelViewModel, documentUri, outlineViewModel);
// service.Dispose() (and textView.Dispose()) when the pane closes.
```

**Not wired by this package** (left for whoever owns the surrounding piece, same as every other
"later step" this document already calls out): continuously refreshing the Outline tree on every
buffer edit (point 6 above - `DesignSurfaceEditingCoordinator`'s existing debounce is the natural
place to also call `DocumentSymbolAsync` again and `outlineViewModel.Load(...)` the result, since it
already owns an `ITextBuffer.Changed` subscription for the SAME document); the Events tab's
"available handler names" dropdown (`PropertiesPanelViewModel.SetSelection`'s `availableHandlerNames`
parameter - `SelectionSyncService.ApplyPropertiesPanel` always passes `Array.Empty<string>()` today;
`definition.rs`'s existing handler-location lookup would need a new LS method to enumerate names,
which does not exist yet, docs/DESIGNER.md §1's own "already found via the language server (see §3)"
phrasing not withstanding). Also flagged, not fixed here (`Properties/` gets no logic changes from
this package): `Registry.EventMeta.Name`/`Properties.EventRowViewModel.AttributeName`'s `"On" + Name`
computation disagrees with the real DSG-1 registry export, where `events[].name` is ALREADY the full
attribute name (e.g. `"OnClick"`) - verified against `tests/Kubuno.Desktop.Tests/Designer/Fixtures/registry.sample.json`.
`SelectionSyncService.ApplyPropertiesPanel` itself already uses the correct (verified) convention, so
this only affects `EventRowViewModel.AttributeName`'s own (separately) displayed value, not anything
this package produces.

## What NOT to change on this library's side

Everything under `src/Views/Kubuno.Views/Designer/` and `tests/Kubuno.Desktop.Tests/Designer/`
is otherwise ready to reference as-is: no source file here needs editing to complete the integration,
only the VSIX-side wiring above (§1, §3, §4, §6, §7, §8, §9) and, if a `VSPackage.resx` does not
already exist by the time this runs, its creation (§3). §6's exe-path resolution (point 1), §7/§8's
own new `ToolWindowPane` subclasses and MEF-imported buffer/undo-history bridge, and §9's new
`OutlineToolWindow` are new files this step adds - most naturally to this library, per their own
suggestion - not edits to anything already here.

## 10. Designer integration — open issues (VSIX integration step, live testing)

The VSIX-side wiring above (§1, §3, §4, §6, §7, §8, §9) is implemented, committed, and builds with
0 warnings (Debug and Release). Live testing in the experimental instance found one severe issue
(the "Open With" crash, **now fixed** - root cause below) and fixed one registration issue; both are
recorded here rather than left silent.

### Confirmed working

- **`KubunoPackage` loads cleanly** in the experimental instance (`ActivityLog.xml`: `Begin`/`End
  package load [KubunoPackage]`, no error in between) - the pkgdef entries this package's
  `[ProvideEditorFactory]`/`[ProvideToolWindow]`/`[ProvideOptionPage]` attributes emit are present
  and well-formed (verified byte-for-byte in the deployed `Kubuno.VisualStudio.pkgdef`).
- **`KbviewEditorFactory.MapLogicalView` IS invoked** by the shell for the Designer logical view
  (`{7651a703-06e5-11d1-8ebd-00a0c90f26ea}`) - confirmed with a temporary file-write probe (since
  removed) plus a real `IVsUIShellOpenDocument.OpenSpecificEditor` call (built as a standalone net48
  probe exe referencing `envdte`/`Microsoft.VisualStudio.OLE.Interop`/`Microsoft.VisualStudio.Shell
  .Interop`/`Microsoft.VisualStudio.Interop`, since `EnvDTE.ItemOperations.OpenFile(path, viewKind)`
  turned out to be the WRONG API for this: when a document is already open, it just reactivates the
  existing window without ever calling into a non-default editor factory - `OpenSpecificEditor` is
  the API "Open With..." itself uses, and is what actually exercises the factory). This means editor
  registration/discovery itself is correct - the `.kbview` → editor-factory → logical-view wiring is
  not the problem.

### Fixed: `CreateEditorInstance` failed (and VS crashed) on a never-opened document

**Root cause (found 2026-09-28 with a try/catch probe around `CreateEditorInstance`):**
`KbviewEditorFactory.CreateTextBuffer` created the buffer with `new VsTextBufferClass()`, and
`CodeWindowHost.BuildWindowCore` the code window with `new VsCodeWindowClass()`. Both are plain
`CoCreateInstance` calls, and those coclasses are registered only in VS's private configuration hive, so
inside `devenv.exe` they fail with **`REGDB_E_CLASSNOTREG` (0x80040154)**. The resulting
`COMException` escaped `CreateEditorInstance` as a bare failure HRESULT for every never-opened
document (an already-open document never reached `CreateTextBuffer`, hence "no crash" there). How the
shell then reacted depended on the caller: with the old cross-process probe (`OpenSpecificEditor` with a
null hierarchy/`VSITEMID_NIL`), msenv.dll's error path crashed with the 0xc0000005 access violation;
with a real hierarchy (`OpenDocumentViaProjectWithSpecific`) it silently fell back. The probe's
"entry log never reached" observation was a logging artefact (the log line sat after the
`ThrowIfNotOnUIThread` call; `CreateEditorInstance` IS entered, on the UI thread, `grfCreateDoc=0x12`).
No bisection of the pane content was needed - the pane was never even constructed.

**Fix (the supported pattern):**
- Buffer and code window both come from `IVsEditorAdaptersFactoryService`
  (`CreateVsTextBufferAdapter` / `CreateVsCodeWindowAdapter`, see
  `KbviewEditorFactory.GetEditorAdapters`), which also sites them.
- A freshly created buffer is empty and unloaded until the shell calls `LoadDocData` AFTER
  `CreateEditorInstance` returns: `DesignerSplitView` now defers the initial `setText` push and
  `DesignSurfaceEditingCoordinator.TryCreate` (which needs the `ITextBuffer`) to
  `IVsTextBufferDataEvents.OnLoadCompleted` (immediately if the buffer is already loaded, i.e. reused
  doc data). Before, a never-opened document got no editing coordinator at all (`GetDataBuffer` was
  null at construction time).
- `CreateEditorInstance` now catches, logs (Kubuno output pane) and returns the HRESULT of any
  exception instead of letting it escape into the shell.

**Verified live (experimental instance, Open Folder on `C:\kubuno-build\vskubuno-live-test`):** the
genuine UI path - Solution Explorer, right-click a never-opened `.kbview`, "Ouvrir avec...", "Kubuno View
Designer", OK (driven through UI Automation + the Win32 list box of the dialog) - opens the split pane
without crashing; the design surface starts, embeds (`ready hwnd=... parent=Some(...)`) and receives the
loaded text (`setText (1614 bytes)`, `setDesignMode on=true`); kubuno-views-ls attaches to the buffer.

**Follow-up, same pass - clicks on the surface did nothing.** Mouse input DID reach the surface's
child window (traced: `WM_MOUSEACTIVATE`, `WM_LBUTTONDOWN`, `WM_SETFOCUS`, `WM_LBUTTONUP`) - no
airspace/WS_DISABLED/activation problem. `view_embed` only detected a press as a `Frame::mouse_down`
rising edge between two frames, so a click released before the next frame (a touchpad tap, a
synthetic click) was lost. It now latches the press from `WM_LBUTTONDOWN`. Verified live after that:
clicking the `TextField` in the Design half selects it and the XML selection jumps to its element;
Delete removes it from the XML; Ctrl+Z restores the text byte-for-byte. The spike's probe line and
Save/Menu demo buttons are now behind `view_embed --debug-probe` (passed only by
`spikes/HwndHostSpike`).

**Dev-build pitfall found on the way:** do NOT build `view_embed` and `kubuno-views-ls` into the SAME
`CARGO_TARGET_DIR`. Cargo feature unification differs between the two builds, so the second one
rewrites `release\kubuno_ui.dll` and the first exe then dies at startup with `0xC0000139`
(`STATUS_ENTRYPOINT_NOT_FOUND`, logged as "design surface exited unexpectedly (code -1073741511)").
Keep the surface in its own target dir (`C:\kubuno-build\agent-dsgint`, the csproj default).

The original investigation notes follow, kept for context.

**Reproduced twice, consistently.** Calling `IVsUIShellOpenDocument.OpenSpecificEditor` with this
factory's GUID against a `.kbview` file that has **never been opened in this VS session** (so
`CreateEditorInstance` must run the full `CreateTextBuffer` → `new DesignerWindowPane(...)` path, not
just reuse an existing pane) makes `devenv.exe` **crash outright**: Windows Application event log
records `Exception code: 0xc0000005` (access violation) in `msenv.dll` (VS's own core shell module,
not a managed DLL - this is a native crash, not an unhandled .NET exception with a catchable stack
trace). The same call against an **already-open** document (reusing an existing pane/doc data) does
**not** crash and returns a working `IVsWindowFrame` with the right caption.

**What is proven, precisely:**
- `MapLogicalView` is entered and returns `S_OK` (confirmed via the temporary log probe before the
  crash).
- `CreateEditorInstance`'s own entry log line (written immediately after
  `ThreadHelper.ThrowIfNotOnUIThread()`) was **never reached** in the crashing run - so either that
  thread-affinity assertion itself throws in this specific call context, or the crash happens between
  the shell deciding to call `CreateEditorInstance` and that first line executing.
- The crash is a native access violation inside VS's own shell code, not inside a `Kubuno.*`
  assembly by name (no `Kubuno` frame appears in the crash's module list) - consistent with a
  **window/HWND-parenting problem** triggered by our pane construction rather than a plain managed
  exception.

**Caveat on the test method itself:** the crash was triggered by `OpenSpecificEditor` called from a
**separate process** (the standalone probe exe) via cross-process COM, not by a genuine user click on
"Open With..." inside `devenv.exe`'s own UI thread. `EnvDTE.Commands.ExecuteCommand("View.OpenWith")`
could not be driven from outside the process ("command not available" - it needs real Win32 focus, not
just `DTE.ActiveDocument`), so the fully-authentic UI path (right-click a tab → "Open With..." →
select "Kubuno View Designer" → OK) was **not itself exercised** here. It remains possible - though
not confirmed either way - that the crash is specific to the cross-process call context (e.g. some
re-entrancy/message-pump assumption in `HwndHost`/`IVsCodeWindow` construction that a genuine
in-process, UI-thread-originated call would not violate) rather than a bug that reproduces for a real
user. **Do not assume it is safe merely because the cross-process trigger looks unusual** - the
suspects below are real, plausible bugs regardless of how the pane construction was entered.

**Suspects, in order of likelihood** (none of these were fixed here - this section is a handoff, not
a diagnosis to closure):
1. **`UI\CodeWindowHost.BuildWindowCore`** - creates `new VsCodeWindowClass()`, calls
   `SetBuffer`/`SetSite`/`CreatePaneWindow(hwndParent.Handle, ...)` against the freshly-created
   `IVsTextLines` from `KbviewEditorFactory.CreateTextBuffer`. If that buffer is not yet fully
   initialized/sited (e.g. content-type/language-service detection, driven by the
   `VsBufferDetectLangSid_guid` flag `CreateTextBuffer` sets, is asynchronous or not complete by the
   time `CreatePaneWindow` runs), a native text-view construction reading uninitialized/inconsistent
   buffer state is a classic source of an access violation this deep in `msenv.dll`. Also worth
   checking: whether `hwndParent.Handle` is valid/non-zero at the point `BuildWindowCore` runs when
   the pane is constructed via this call path (a `WindowPane`/WPF control not yet attached to a real
   top-level HWND would make this a null/garbage parent).
2. **`DesignSurface\RustDesignSurfaceHost`/`RustDesignSurfaceHostFactory`** - `DesignerSplitView`'s
   constructor creates this (via `DesignSurfaceHostFactoryHost.Current`) and it spawns the
   `kubuno-views-surface.exe` child process and creates a `WS_CHILD` native window under WPF's own
   `HwndHost` container. The DSG-7 spike (docs/DESIGNER.md §7) proved this technique works for a
   **top-level WPF window already shown**; it was never verified for a pane being constructed **during
   shell-driven `CreateEditorInstance`**, before the owning `WindowPane`'s frame necessarily has a
   realized top-level HWND yet.
3. **`DesignSurfaceEditingCoordinator`'s constructor-time fire-and-forget** (`ThreadHelper
   .JoinableTaskFactory.RunAsync(SetupSelectionSyncAsync)`) - runs synchronously-adjacent to pane
   construction; unlikely to itself cause a *synchronous* access violation during
   `CreateEditorInstance`, but worth ruling out if suspect 1/2 turn up clean.

**Next steps for whoever picks this up:**
1. First, cheaply rule the test-method caveat in or out: reproduce (or fail to reproduce) via a
   genuine UI-driven "Open With..." click inside a real, focused `devenv.exe` window (not
   cross-process automation) on a `.kbview` file never opened this session.
2. If it still crashes: attach a debugger to `devenv.exe /rootsuffix Exp` *before* triggering the
   open (Debug > Attach to Process, or launch `devenv` under the debugger directly) and get a real
   managed+native mixed-mode stack for the access violation - far more conclusive than log-probing.
3. Suspect 1 is the cheapest to test in isolation: temporarily stub `RustDesignSurfaceHostFactory`
   out (force `DesignSurfaceHostFactoryHost.Current` back to `PlaceholderDesignSurfaceHostFactory
   .Instance`) and retry the same never-opened-document open; if it still crashes, the design-surface
   `HwndHost` (suspect 2) is exonerated and `CodeWindowHost` is the remaining suspect, and vice versa.
4. Registration itself does not need further work (see "Confirmed working" above) - do not re-litigate
   `[ProvideEditorFactory]`/pkgdef priorities without new evidence.

### Fixed during this pass: View menu commands had no canonical name

The Toolbox/Properties/Outline "show" commands were originally placed in their own group parented to
`vsshlids.h`'s `IDG_VS_WNDO_OTRWNDWS1` ("View > Other Windows"). Live testing found that
`EnvDTE.Commands.Item(guid, id)` returned an object for each of them, but with an **empty**
`Name`/`LocalizedName` - unlike the pre-existing `Tools.KubunoDebugRustTestatCursor` command, which
resolves correctly - and the coordinator's own direct visual check confirmed none of the three
appeared under either "View" or "View > Other Windows" in the real menu. Root cause not fully
isolated (possibly `IDG_VS_WNDO_OTRWNDWS1` not being the right merge point in this VS build, or
needing a `<Menu>`/`<CommandPlacement>` this task did not add), but rather than keep guessing at an
unverified menu location, the three commands were moved into the same, already-proven-working
`KubunoToolsMenuGroup` (parented to `IDM_VS_MENU_TOOLS`) the Rust debug command already uses
successfully. They now live under **Tools**, not **View > Other Windows** - update
`docs/DESIGNER.md`/any user-facing docs that assume the latter location.

## 11. WinForms-like integration (Visual Studio's own Toolbox / Properties window) - wired

docs/DESIGNER.md section 11 has the design and the live findings; what the VSIX/SDK side now does:

- **`KubunoPackage`**: registers `LOGVIEWID_Code` for `KbviewEditorFactory` (its new "Code" physical
  view, a plain code window - F7), registers `EditorFactory.DesignerViewSwitchCommandTarget` as a
  priority command target (F7 / Shift+F7 on a `.kbview` document), sets
  `Toolbox.NativeToolboxInstaller.IconMoniker` to the Kubuno control icons
  (`KubunoControls.imagemanifest`, shipped at the VSIX root like `RustProject.imagemanifest`), and
  proffers `KbviewEventBindingService` as a global service (the Events tab's double-click).
- **`Kubuno.Rust.ProjectSystem`**: `KbviewDesignerEditorProvider`
  (`IProjectSpecificEditorProvider`) makes the designer the default editor of `.kbview` in a `.rsproj`
  (Designer view); the control icons live in `Resources/Icons/Controls/` (generated by
  `tools/generate-control-icons.ps1`).
- **`Kubuno.Rust.Sdk`**: `SubType=Designer` on `.kbview` items (`none.xaml` declares `SubType`), for
  CPS's own "View Designer"/"View Code" commands.
- **Designer library**: `DesignerWindowPane` implements `IVsToolboxUser`, answers `IVSMDDesigner`
  with a component-less design surface created by Visual Studio's `DesignSurfaceManager` (the Events
  tab's precondition), handles `Edit.Undo`/`Edit.Redo`, and passes its frame's `ITrackSelection` down
  to `DesignSurfaceEditingCoordinator`, whose `.Native.cs` half publishes the selection
  (`PropertyBrowser/*`), fills the Toolbox (`Toolbox/NativeToolboxInstaller`), applies DSG-9's
  `moveElement`/`insertChild`/move-resize batches (the latter as one undo unit) and creates handlers
  (DSG-10, `Handlers/HandlerCreationService`). `KubunoViewsLanguageClient.IsInitialized`/`ReadyRpc`:
  custom requests now wait for the server's `initialize` (a request sent before it made kubuno-views-ls
  exit - found live with a designer restored at startup).
- **Surface (`kubuno-desktop-views`' `view_embed`, desktop repository)**: registers its own OLE drop target
  (`mod ole_drop`) for Toolbox drags; DSG-9 lines are now dispatched by the one stdout listener
  (`TryDispatchDragDropLine`) - the lazily attached second listener missed a drop live.

The custom "Kubuno Toolbox"/"Kubuno Properties" fallback tool windows (and their `ToolboxView`/
`PropertiesPanelView` contents) were later removed: Visual Studio's own windows cover them. The
historical integration notes above that describe them are kept for context only.
