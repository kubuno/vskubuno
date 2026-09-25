# Integrating Kubuno.VisualStudio.Designer into the Kubuno.VisualStudio VSIX

`Kubuno.VisualStudio.Designer` is a standalone net48 class library (same shape as
`Kubuno.VisualStudio.Views` - see that project's own `INTEGRATION.md` for the precedent this one
follows): it builds and its tests run on their own (`dotnet build`/`dotnet test`, with
`COMPLUS_LoadFromRemoteSources=1` on a mapped network drive such as `Z:`), but it produces no VSIX of
its own and is never installed/loaded standalone. This document is the checklist for wiring it into
`src/Kubuno.VisualStudio` (the VSIX project) once that project's own current work is done. Nothing in
this document has been applied to the VSIX project - by design, this task must not touch
`src/Kubuno.VisualStudio/`, `Kubuno.VisualStudio.sln`, `README.md` or `CHANGELOG.md`, and this library
is deliberately **not yet** added to `Kubuno.VisualStudio.sln` either (same reason
`Kubuno.VisualStudio.Views` was not added by its own integration task - it builds standalone until
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
  `ILanguageClient` (`Kubuno.VisualStudio.Views.LanguageService.KubunoViewsLanguageClient`, which
  attaches by "kbview" content type, independent of which editor opened the file) keeps working
  unchanged.
- `DesignSurface\IDesignSurfaceHost.cs` / `IDesignSurfaceHostFactory.cs` /
  `DesignSurfaceHostFactoryHost.cs` - the seam DSG-7 plugs its real embedded render surface into;
  today `PlaceholderDesignSurfaceHost` is the only implementation.
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
`Kubuno.VisualStudio.Core`/`Kubuno.VisualStudio.Views` (see that file's existing `ItemGroup`, and
`Kubuno.VisualStudio.Views/INTEGRATION.md` §1 for the precedent):

```xml
<ProjectReference Include="..\Kubuno.VisualStudio.Designer\Kubuno.VisualStudio.Designer.csproj">
  <Project>{PUT-A-NEW-GUID-HERE}</Project>
  <Name>Kubuno.VisualStudio.Designer</Name>
  <IncludeOutputGroupsInVSIX>BuiltProjectOutputGroup%3bBuiltProjectOutputGroupDependencies%3bGetCopyToOutputDirectoryItems%3bSatelliteDllsProjectOutputGroup%3b</IncludeOutputGroupsInVSIX>
  <IncludeOutputGroupsInVSIXLocalOnly>DebugSymbolsProjectOutputGroup%3b</IncludeOutputGroupsInVSIXLocalOnly>
</ProjectReference>
```

`Kubuno.VisualStudio.Designer.csproj` already references the exact same `Microsoft.VisualStudio.SDK`
package version `Kubuno.VisualStudio.csproj` and `Kubuno.VisualStudio.Views.csproj` do (verified by
reading both before this library was written) - no version reconciliation should be needed. It also
already has a `ProjectReference` to `Kubuno.VisualStudio.Views.csproj` (for `KbviewConstants`/
`IKubunoLog`/`KubunoViewsLogHost` - see this library's own csproj comment), so once the VSIX
references this project, both are pulled in; do not add a second, separate reference to
`Kubuno.VisualStudio.Views` because of that transitive edge - the existing one already covers it,
exactly as `Kubuno.Cargo`'s dependencies flow through today.

Add the new project to `Kubuno.VisualStudio.sln` (a new `Project(...)` entry plus the matching
`Debug|AnyCPU`/`Release|AnyCPU` lines in `ProjectConfigurationPlatforms`, following the existing
entries for `Kubuno.VisualStudio.Core`), and add `tests\Kubuno.VisualStudio.Designer.Tests` likewise,
so `dotnet test`/the solution build cover it going forward - mirroring exactly what
`Kubuno.VisualStudio.Views/INTEGRATION.md` §1 asks for that library.

## 2. No MEF pickup needed for the editor factory itself

Unlike `Kubuno.VisualStudio.Views` (whose content-type/language-client classes are `[Export]`ed and
picked up by MEF automatically once the DLL is in the VSIX output directory - see that project's
`INTEGRATION.md` §2), `KbviewEditorFactory` is a **classic, package-registered** editor factory, not a
MEF component: `IVsEditorFactory`/`IVsRegisterEditors` predate MEF-based extensibility, and VS's own
"Open With"/default-editor machinery keys off `[ProvideEditorFactory]`/`[ProvideEditorExtension]`
pkgdef registration plus an explicit `Package.RegisterEditorFactory` call (§3 below) - not off
assembly presence. No `Asset Type="Microsoft.VisualStudio.MefComponent"` entry is needed for this
part; the VSIX's existing single `MefComponent` asset (pointing at `%CurrentProject%`) still covers
`Kubuno.VisualStudio.Views`' own MEF exports unchanged.

## 3. Editor factory registration (`KubunoPackage.cs`)

Add to `KubunoPackage.cs`, alongside the existing package-level attributes:

```csharp
[ProvideEditorFactory(typeof(Kubuno.VisualStudio.Designer.EditorFactory.KbviewEditorFactory), 110 /* VSPackage.resx string id - see note below */)]
[ProvideEditorLogicalView(typeof(Kubuno.VisualStudio.Designer.EditorFactory.KbviewEditorFactory), "{7651a703-06e5-11d1-8ebd-00a0c90f26ea}")] // LOGVIEWID_Designer
[ProvideEditorLogicalView(typeof(Kubuno.VisualStudio.Designer.EditorFactory.KbviewEditorFactory), "{7651a704-06e5-11d1-8ebd-00a0c90f26ea}")] // LOGVIEWID_TextView
[ProvideEditorExtension(typeof(Kubuno.VisualStudio.Designer.EditorFactory.KbviewEditorFactory), Kubuno.VisualStudio.Views.KbviewConstants.FileExtension, Kubuno.VisualStudio.Designer.DesignerConstants.EditorExtensionPriority)]
```

Notes:

- The `110` display-name resource id is a placeholder: this VSIX currently has **no `VSPackage.resx`**
  (checked - `src/Kubuno.VisualStudio/Resources/` has none), because nothing else here has needed one
  yet (the Rust `ILanguageClient` is MEF, not a `ProvideEditorFactory`-style registration).
  `ProvideEditorFactoryAttribute`'s constructor is `(Type factoryType, short nameResourceID)` - it
  needs a `#nnn`-style resource id resolving through the package's satellite resources, the standard
  VSSDK convention (see any `New Editor Extension` project template). Add a `VSPackage.resx` (or reuse
  one if this task's own work created one in parallel) with an entry whose value is
  `Kubuno.VisualStudio.Designer.DesignerConstants.EditorName` ("Kubuno View Designer"), and pick a
  free id in whatever numbering scheme that resx ends up using.
- The two `ProvideEditorLogicalView` GUIDs are `VSConstants.LOGVIEWID_Designer` and
  `VSConstants.LOGVIEWID_TextView` written as literal strings (verified against
  `Microsoft.VisualStudio.NativeMethods`'s own field initializers - both are stable, well-known VS
  GUIDs, not invented here). `KbviewEditorFactory.MapLogicalView` already accepts both (and
  `LOGVIEWID_Primary`) and maps either to the one physical "Design" view - see that method's own
  remarks for why a single physical view is correct here.
- `Kubuno.VisualStudio.Designer.DesignerConstants.EditorExtensionPriority` (`0x60`) is deliberately
  higher (lower-precedence) than the `0x32` many VSSDK samples use for a *default* editor - see
  `DesignerConstants.cs`'s own doc comment. **This priority number is the one part of "Open With
  registration" that could not be verified without a live VS instance in this task** (no `devenv` was
  run here - see the repo's own `CLAUDE.md`, "toujours tester réellement" - and per this task's
  instructions, no VS was launched except via PowerShell if truly needed, which it was not for a
  C#-only skeleton). Verify in the experimental instance after wiring this up:
  1. `.kbview` files should still **open in the plain XML/text editor on double-click** (current
     behavior - today `.kbview` has *no* explicit `ProvideEditorExtension` at all, so it falls back to
     VS's built-in Source Code Editor purely from the "kbview" content type; adding any
     `ProvideEditorExtension` for the first time is the part that needs checking).
  2. "Open With..." on a `.kbview` file should list **both** "Kubuno View Designer" and the plain text
     editor.
  3. If step 1 fails (the designer becomes the double-click default instead of staying opt-in), the
     fix is almost certainly to also add an explicit `ProvideEditorExtension` for VS's own built-in
     text editor factory (`VSConstants.CLSID.VsTextEditorFactory_guid`) at a *lower* priority number
     than `0x60`, forcing it to remain first - this cannot itself be attribute-driven the normal way
     (`ProvideEditorExtension`'s `factoryType` parameter expects a package-owned `Type`, not an
     arbitrary external CLSID), so it would need a lower-level pkgdef fragment (see how
     `kbview-languages.pkgdef` already ships a hand-written fragment merged into `languages.pkgdef` -
     `Kubuno.VisualStudio.Views/INTEGRATION.md` §3 - the same technique applies here for an
     `Editors\{8B382828-6202-11d1-8870-0000F87579D2}\Extensions\kbview` key).

In `KubunoPackage.InitializeAsync` (after the package is sited), register the factory instance itself
- `[ProvideEditorFactory]` only emits pkgdef metadata; VS still needs a live instance handed to it:

```csharp
RegisterEditorFactory(new Kubuno.VisualStudio.Designer.EditorFactory.KbviewEditorFactory());
```

(`RegisterEditorFactory` is `Microsoft.VisualStudio.Shell.Package`'s own helper - it calls
`IVsRegisterEditors.RegisterEditor` and disposes the factory on package teardown automatically, the
same pattern `AsyncPackage`-derived packages use for any classic, non-MEF editor factory.)

## 4. Options page registration

`Options\KbviewDesignerOptionsPage.cs` (in this library) is a real `DialogPage`, exactly like
`Kubuno.VisualStudio.Views.Options.KbviewOptionsPage` - see that library's own `INTEGRATION.md` §4 for
the precedent. Add to `KubunoPackage.cs`:

```csharp
[ProvideOptionPage(typeof(Kubuno.VisualStudio.Designer.Options.KbviewDesignerOptionsPage), Constants.OptionsCategoryName, Kubuno.VisualStudio.Designer.DesignerConstants.OptionsPageName, 0, 0, supportsAutomation: true)]
[ProvideProfile(typeof(Kubuno.VisualStudio.Designer.Options.KbviewDesignerOptionsPage), Constants.OptionsCategoryName, Kubuno.VisualStudio.Designer.DesignerConstants.OptionsPageName, 0, 0, isToolsOptionPage: true)]
```

(`Constants.OptionsCategoryName` is already `"Kubuno"`, shared with the Rust and Views options pages -
no change needed there.)

Then, in `KubunoPackage.InitializeAsync`, publish the page into this library's static gateway, the
same shape as `KubunoViewsOptionsHost.Current`:

```csharp
Kubuno.VisualStudio.Designer.Options.DesignerOptionsHost.Current =
    (Kubuno.VisualStudio.Designer.Options.KbviewDesignerOptionsPage)GetDialogPage(typeof(Kubuno.VisualStudio.Designer.Options.KbviewDesignerOptionsPage));
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

Nothing to integrate here yet: `DesignSurfaceHostFactoryHost.Current` defaults to
`PlaceholderDesignSurfaceHostFactory.Instance` and needs no VSIX wiring to work as today's
placeholder. When DSG-7 lands its real `kubuno-views-designer`-backed `IDesignSurfaceHost`
(`HwndHost` subclass doing the spawn/handshake/`SetParent`/focus forwarding, per docs/DESIGNER.md §7),
its integration step only needs to set
`Kubuno.VisualStudio.Designer.DesignSurface.DesignSurfaceHostFactoryHost.Current` to its own factory -
either from `KubunoPackage.InitializeAsync` (if DSG-7 stays a separate library the VSIX also
references) or from this library directly (if DSG-7 adds its files here instead). No change to
`DesignerWindowPane`, `DesignerSplitView`, or this integration document's §3/§4 is expected.

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

## What NOT to change on this library's side

Everything under `src/Kubuno.VisualStudio.Designer/` and `tests/Kubuno.VisualStudio.Designer.Tests/`
is otherwise ready to reference as-is: no source file here needs editing to complete the integration,
only the VSIX-side wiring above (§1, §3, §4, §7, §8) and, if a `VSPackage.resx` does not already exist
by the time this runs, its creation (§3). §7/§8's own new `ToolWindowPane` subclasses and MEF-imported
buffer/undo-history bridge are new files this step adds - most naturally to this library, per their own
suggestion - not edits to anything already here.
