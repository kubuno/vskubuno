# vskubuno — Architecture

Kubuno for Visual Studio 2026: Rust, Cargo, Rust debugging and (later) Kubuno XML views,
inside Visual Studio, plus an MCP server so Claude sees what the developer sees.

## Goal

The developer edits what Claude generates (backend and frontend) in Visual Studio, and Claude
keeps assisting after those edits. Consequences for the design:

- **Git is the shared history.** Claude reads `git diff`/`git log` to understand the
  developer's changes.
- **Nothing the developer owns is ever regenerated.** Views (XML) are the single source of
  truth and are edited surgically; code-behind lives in Rust files nothing regenerates; any
  code derived from XML is produced at build time (`build.rs`) and never committed.
- **Canonical formatting** (rustfmt, an XML formatter) keeps diffs meaningful.
- **Tests, clippy and type-checks are the contract** between the two editors.

## Extension model

Classic VSSDK (in-process, .NET Framework 4.8, `AsyncPackage`), because phase 1 needs:

| Need | VS API |
|---|---|
| Rust language features | `ILanguageClient` (LSP client) hosting rust-analyzer |
| Coloring | TextMate grammar shipped in the VSIX |
| Cargo projects without a project file | Open Folder extensibility (`IWorkspaceProviderFactory`, file scanners, file context actions for build/debug) |
| Build output → Error List | `cargo … --message-format=json` parsed into Error List entries |
| Launch / debug a target | Open Folder launch/debug providers (native debugger, PDB + Rust natvis) |
| Embedded native preview (phase 4) | `HwndHost` in a tool window |

The out-of-process `VisualStudio.Extensibility` model has no workspace/debug providers and
cannot host a foreign native window, so it is not used.

## Layers (as built)

One VSIX serves several Kubuno targets - desktop applications today, web modules and mobile apps next (see the
roadmaps below). The code is split into **layers** whose dependencies only go down, so a target is added without
touching the layers below it and the targets never depend on each other:

```
                 Kubuno.VisualStudio   (packaging + composition root: KubunoPackage, vsct, VSIX manifest)
          ┌──────────────┼─────────────────┬───────────────────┐
     Kubuno.Desktop*   Kubuno.Web*     Kubuno.Mobile*          (targets: never reference each other)
          └──────────────┼─────────────────┘
                    Kubuno.Views*                              (.kbview views: designer, language client, .kbres editor)
                         │
                    Kubuno.Rust*                               (product-agnostic Rust support)
                         │
                    Kubuno.Shared*                             (shared Visual Studio infrastructure)
```

The lowest layer is named **Shared** (`Kubuno.Shared*`, `src/Shared`), not "Core": "Kubuno Core Web" (the server) and
"Kubuno Core Desktop" (the desktop workspace) are product names, and the extension's own infrastructure must not be
confused with them (renamed from `Kubuno.Core*` on 2026-10-02; every GUID, settings key, unified settings id, CPS
capability and template id was kept).

| Layer | Assembly | What it holds |
|---|---|---|
| Shared | `Kubuno.Shared` | Layer contracts and sequencing (`Extensibility\KubunoLayer`, `KubunoLayerHost`, `KubunoLayerContext`), host accessors for MEF parts (`KubunoHost`: package, options pages, `JoinableTaskFactory`), shared GUIDs (`KubunoGuids`: package, command set, Output pane), themed dialogs and theme helpers (`UI\ThemedDialog`, `ThemedControls`, `VsTheme`, `DialogGallery`), unified settings plumbing (`Settings\KubunoDialogPage`, `UnifiedSettingAttribute`), the "Kubuno" Output pane (`Logging\KubunoLog`), the bundled-tool locator (`KubunoExtension`), the remote Linux host options (`Remote\`), the dialog gallery command, the MCP bridge start. |
| Shared | `Kubuno.Shared.Logic` | Pure helpers (no VS SDK): LSP plumbing (`Lsp\`: handshake streams, JSON, snippets, `WorkspaceEdit` merge), the hover markdown/QuickInfo model, the UI language (`Localization\UiLanguage`), the remote host settings. |
| Shared | `Kubuno.Shared.Mcp.Bridge`, `Kubuno.Shared.Mcp` | The MCP bridge contract (net48 in-proc + net8.0) and the `kubuno-vs-mcp.exe` server (docs/MCP.md). |
| Shared | `Kubuno.Shared.DevAssistant`, `.Logic`, `.Host` | The Kubuno Dev Assistant (docs/AI-ASSISTANT.md): its tool window and `DevAssistantLayer`, its pure logic, and the `kubuno-dev-assistant.exe` provider host. |
| Rust | `Kubuno.Rust` | rust-analyzer (language client, middle layer), IntelliSense, QuickInfo, CodeLens, inlay hints, Open Folder Cargo workspaces and Error List, launch/debug, natvis/Just My Code/step filters (`Debugging\`), the `.rsproj` commands, Dependencies node, crate manager, symbol tree, Rust and Debugging options pages, grammar and `languages.pkgdef`, Rust templates, and `Extensibility\` (below). |
| Rust | `Kubuno.Rust.Logic`, `Kubuno.Rust.Cargo`, `Kubuno.Rust.Launch` | Pure logic (no VS SDK): rust-analyzer discovery/handshake, completion and hover presentation, `.rsproj` generation and properties, Dependencies tree; `cargo metadata`/build messages/TOML (netstandard2.0, also used by `Kubuno.Rust.Sdk`'s tasks); the launch/debug environment. |
| Rust | `Kubuno.Rust.ProjectSystem` | The `.rsproj` CPS project type (MEF exports, `rsproj.pkgdef`, `RustProject.imagemanifest`). |
| Rust | `Kubuno.Rust.TestAdapter`, `Kubuno.Rust.Debugger`, `Kubuno.Rust.TemplateWizard` | Test Explorer adapter; the Concord component that formats Rust panics; the crate-name wizard of the templates. |
| Rust | `Kubuno.Cargo.MSBuild.Tasks` | The MSBuild tasks inside the `Kubuno.Rust.Sdk` NuGet package (name kept: it is part of the SDK's `UsingTask`s). |
| Views | `Kubuno.Views` | What every target with `.kbview` views shares, whatever renders them: the `.kbview` language client of `kubuno-views-ls` (`LanguageService\`, `Locating\`, `Logging\`, `Options\`, content type, grammar, `kbview-languages.pkgdef`); the WinForms-like designer (`Designer\`: editor factory with Design \| XML \| Split, the design surface seam `IDesignSurfaceHost` / `IProtocolDesignSurfaceHost` and the design-surface protocol, editing pipeline, Toolbox, Properties (`ICustomTypeDescriptor`), events ⚡, collection editors, smart tags, resource/icon/binding pickers, error banner, outline, handlers); the `.kbres` resource editor (`Resources\`); `ViewsLayer`. |
| Views | `Kubuno.Views.Logic` | Pure logic (netstandard2.0): view file kinds (`ViewFiles`), the `.kbres` resource model (`Resources\`). |
| Desktop | `Kubuno.Desktop` | The Design pane's renderer: `RustDesignSurfaceHost` (`view_embed.exe` in an `HwndHost`, the `kubuno_ui` runtime handshake) and its factory (`Designer\DesignSurface\`), the per-project design runtime and surface locator (`DesignerIntegration\`); what `.kbview` adds to Rust files (`LanguageService\`: cross-language completion and navigation, override members, SQL in Rust strings); the data tooling (`DataExplorer\`, `DataSources\` with the binding drop `Designer\Bindings\BindingDropPlanner`, `Migrations\`); the Toolbox icon names of the Kubuno controls; printing (through the designer's library component tabs), paint debug, desktop templates. |
| Desktop | `Kubuno.Desktop.Logic` | Pure logic: data, data sources, migrations, SQL, `.kbview` editor helpers, override members, control icons, the design build of a project's `kubuno_ui`, paint debug. |
| Desktop | `Kubuno.Desktop.ProjectSystem`, `Kubuno.Desktop.TemplateWizard` | The desktop target's CPS exports in a `.rsproj` (the designer as default `.kbview` editor, the `.kbview` icon) and the Kubuno control icons (`KubunoControls.imagemanifest`); the control item wizard. |
| Web | `Kubuno.Web` | The Kubuno core and web modules (docs/WEB.md): Tools commands (web solution generation, multi-repository solution, version tools, `.kbpkg`), the "Kubuno Core Web Module" template (moved here from the Rust layer), the WV-9a WebView2 surface spike (`WebDesigner\Spike\`). References `Kubuno.Views` for the web views designer (docs/WEB-VIEWS.md; WV-9b's `WebDesignSurfaceHost` implements `IProtocolDesignSurfaceHost`). |
| Web | `Kubuno.Web.Logic`, `Kubuno.Web.MSBuild.Tasks` | Pure logic (netstandard2.0: development database guard, dev core, deployment, `.kbpkg`, node_modules from another OS, generation, version audit); the tasks of the `Kubuno.Web.Sdk` NuGet package (sdk/Kubuno.Web.Sdk). |
| Web | `Kubuno.Web.ProjectSystem`, `Kubuno.Web.TemplateWizard` | F5 of a core or module `.rsproj` (`KubunoWebDebugger`); the module template's wizard. |
| Mobile | `Kubuno.Mobile` | Skeleton: `MobileLayer`, registered, no feature yet (src/Mobile/Kubuno.Mobile/README.md). |

Each layer keeps its pure logic in a `*.Logic` (or netstandard) assembly without the Visual Studio SDK, tested with plain
`dotnet test`: `tests/Kubuno.Shared.Tests`, `Kubuno.Shared.Mcp.Tests`, `Kubuno.Shared.DevAssistant.Tests`,
`Kubuno.Rust.Tests`, `Kubuno.Rust.Cargo.Tests`, `Kubuno.Rust.Launch.Tests`, `Kubuno.Rust.TestAdapter.Tests`,
`Kubuno.Cargo.MSBuild.Tasks.Tests`, `Kubuno.Views.Tests` (which also covers the designer's view models, editing
pipeline and protocol through `Kubuno.Views`), `Kubuno.Desktop.Tests`, `Kubuno.Web.Tests`.

**The rules, enforced by `tests/Kubuno.Architecture.Tests`** (on the project files and, with
`System.Reflection.Metadata`, on the compiled assemblies): every project belongs to a layer by its name; project and
assembly references only go down (Shared → nothing above; Rust → Shared; Views → Shared and Rust; Desktop, Web, Mobile
→ Shared, Rust and Views, never each other); the views layer never reaches a target and the web layer never reaches
the desktop layer, even transitively through project references; the `*.Logic` and netstandard assemblies reference no
Visual Studio SDK; `Kubuno.Rust`, `Kubuno.Views`, `Kubuno.Desktop`, `Kubuno.Web` and `Kubuno.Mobile` each declare
exactly one `KubunoLayer`, all listed by `KubunoPackage.CreateLayers`; the packaging project references every shipped
assembly; every source file's namespace starts with its assembly's.

**Composition.** There is still ONE package, `KubunoPackage` (src/Kubuno.VisualStudio), with the same GUID: it owns
every registration Visual Studio reads from the pkgdef (options pages, editor factory, tool windows, key binding
table, UI context rules, menus - attributes on the class) and the command table (`KubunoCommands.vsct`), and lists the
layers. `KubunoLayerHost` (Kubuno.Shared) runs the same sequence the package ran before, per layer: background
`InitializeAsync`, UI-thread `InitializeOnUIThread` (reported as the package's UI-thread load time), then, once the
solution is loaded, `InitializeOnIdle` (UI thread at idle, after the Output pane is created) and
`InitializeDeferredAsync` (background, after the MCP bridge starts); `Dispose` in reverse order. A failing hook is
logged and does not stop the other layers. The layers' command IDs are in their own `PackageIds` classes
(`Kubuno.Rust.PackageIds`, `Kubuno.Views.ViewsCommandIds`, `Kubuno.Desktop.PackageIds`, `Kubuno.Shared.SharedCommandIds`)
with the unchanged values of the vsct. The layers are initialized in the order of `CreateLayers`: Dev Assistant (Shared),
Rust, Views, Desktop, Web, Mobile - so the designer's seams (`ViewsLayer`: the views log, the `.kbview` and `.kbres`
editor factories, the designer options, F7, the Properties window's binding commands, the Outline command, the Toolbox
clean-up) are in place before a target plugs its renderer in. Static registrations are pkgdef fragments owned by their layer (`languages.pkgdef`, `debugging.pkgdef`,
`rsproj.pkgdef`, `kbview-languages.pkgdef`) and templates, grammars and image manifests live in their layer's folder;
the packaging project ships them all at the same VSIX paths as before. Every assembly ships in the one extension
folder, so the single `[ProvideBindingPath]` resolves all of them (no `ProvideCodeBase`, see KubunoPackage.cs).

**Extension points between layers** (so a lower layer never names a higher one):
- MEF contracts of `Kubuno.Rust.Extensibility`, imported with `[ImportMany]` by the Rust MEF parts, so they work before
  (and without) the package loading: `IRustEmbeddedLanguage` (a language inside Rust strings with its own completion -
  the desktop layer's SQL), `IRustReferenceParticipant` (what a rename or Find All References implies in other files -
  the desktop layer's view handlers), `ISolutionSymbolProvider` (symbol trees, icons and navigation of other files in
  Solution Explorer - the desktop layer's `.kbview` elements).
- `Kubuno.Rust.Commands.AddProjectItemCommands` takes the "Ajouter" entries of each layer (`ProjectItemTemplate`).
- The design surface of the view designer (docs/WEB-VIEWS.md WV-8): a target implements
  `Kubuno.Views.Designer.DesignSurface.IDesignSurfaceHostFactory`, creating an `IProtocolDesignSurfaceHost` (an
  `IDesignSurfaceHost` that speaks the design-surface protocol: selection, edit intents, drops, context menus,
  resources, zoom; optionally `IDesignSurfaceStatusAware` / `IDesignSurfaceRuntimeAware` for the error banner and the
  runtime info bar), and sets it in `DesignSurfaceHostFactoryHost.Current` - the desktop layer with
  `RustDesignSurfaceHostFactory`, the web layer (WV-9b) with a WebView2 host. Without one, the Design pane shows
  `PlaceholderDesignSurfaceHost`. The Toolbox icons of a target's controls come from `NativeToolboxInstaller.IconName` and
  `IconAssembly` (the desktop layer: `ControlIcons` and `Kubuno.Desktop.ProjectSystem`'s XAML icons).
- `Kubuno.Shared.UI.DialogGallery.Register`, `KubunoHost`, `KubunoLog` for everything cross-cutting.
- CPS exports of a target go in the target's own `*.ProjectSystem` assembly, scoped to the `RustProjectSystem`
  capability next to the Rust ones.

**What stays identical for users** (checked by diffing the generated `Kubuno.VisualStudio.pkgdef`, the VSIX file list
and the template manifests with the previous build): the VSIX identity, the package GUID, command set and command IDs,
tool window GUIDs, the editor factory and its logical views, UI context rules, option page GUIDs (now explicit `[Guid]`
attributes, formerly derived from the type names) and their classic settings keys (`KubunoDialogPage`'s
`legacyTypeFullName`), the unified settings monikers, the key binding table, template IDs, CPS capabilities, image
monikers and the Test Explorer property IDs. Only the tool window `Name` values of the pkgdef (the type names) and the
assembly names changed - including the wizard assemblies the `.vstemplate` files name. The same checks were run for the
Core → Shared rename and the move of the designer into the views layer (2026-10-02): the pkgdef differs only by the
`Name` of the Outline and Dev Assistant tool windows, the VSIX file list only by the renamed assemblies and the two new
`Kubuno.Views*` ones, `kbview-languages.pkgdef` is identical; the remote host options page keeps its classic settings key
through `legacyTypeFullName: "Kubuno.Core.Remote.RemoteHostOptionsPage"`.

**Loading** is unchanged: the package still auto-loads on the one "Kubuno activation" rule (nothing loads for a
C#-only solution or the start window); each assembly with MEF parts is a `MefComponent` asset of the VSIX manifest (the
Web and Mobile ones too, so their first MEF part needs no manifest change). The "Kubuno" pane (and `KUBUNO_VS_LOG`)
still reports "package loaded in X ms (Y ms on the UI thread)" and the deferred initialization time. Measured A/B on
the same solution (DsLive, fresh `.vs`, warm, separate hives, 3-5 runs each): package load 24-42 ms on the UI thread
before the layers vs 40-44 ms after, deferred initialization 61-64 ms vs 25-28 ms at idle - the total UI-thread time
went down. Layers register their dialog gallery samples as providers (`DialogGallery.Register(Func<...>)`), built when
the gallery opens: building them eagerly (override catalog, designer and wizard assemblies) had added about 45 ms to
the package load's UI-thread time.

**Visual Studio SDK builds.** Most assemblies compile against the NuGet `Microsoft.VisualStudio.SDK` 17.14; the CPS
project systems and the wizards compile against the installed Visual Studio's 18.x assemblies. `Kubuno.Shared` compiles
against the lowest (17.x) and keeps its SDK packages private (`PrivateAssets="all"`, like the MCP bridge's), so both
kinds reference it without version conflicts; the wizards compile `ThemedDialog.cs` as source to keep their dependency
closure minimal.

**Adding a target layer (Web, Mobile)**: put its features in `src/<Layer>/Kubuno.<Layer>*` (pure logic in
`Kubuno.<Layer>.Logic`, CPS parts in `Kubuno.<Layer>.ProjectSystem`...), start them from its `KubunoLayer` hooks or as
MEF parts, declare its registrations on `KubunoPackage` and its commands in `KubunoCommands.vsct`, ship its pkgdef
fragments, templates and image manifests from the packaging project, and add its tests. Nothing in Shared, Rust, Views
or the other targets changes; if a target needs something another target has (the desktop layer's SQLx migrations for a
web module's backend), that code moves DOWN into Rust, Views or Shared first - a target never references another one.
The view designer is the first such move (docs/WEB-VIEWS.md WV-8, 2026-10-02): it left `Kubuno.Desktop` for
`Kubuno.Views` so the web views designer (and later the mobile one) reuses it; a target that has `.kbview` views only
adds its design surface (above).

Residuals kept on purpose (candidates to move when the Web layer gets features): the crate-name wizard
(`Kubuno.Rust.TemplateWizard`) also computes the `$kubunodesktopsrc$` and `$moduleid$` tokens of the desktop and module
templates (plain strings, no reference to those layers) - the module template itself moved to the Web layer on
2026-10-01, with its own wizard (docs/WEB.md); the `.kbview` file icon image stays in `RustProject.imagemanifest` so its moniker does not change (the desktop
layer applies it).

Residuals of the views layer move (WV-8, to settle with WV-9b): `DesignSurfaceHostFactoryHost.Current` is one factory
for the whole session, set by the desktop layer - the web surface will need a choice per document (the project's
target); the Kubuno control icons (`KubunoControls.imagemanifest` and the XAML icons) stay in
`Kubuno.Desktop.ProjectSystem` so their monikers do not change, and the views layer's Toolbox loads them by assembly name
(`NativeToolboxInstaller.IconAssembly`, set by the desktop layer, never referenced); `DesignSurfaceRuntime` still
describes a runtime as an executable plus the hash of its `kubuno_ui` DLL (the desktop shape; the web runtime will
describe its dev server); the `.kbview` language client still starts `kubuno-views-ls` with the desktop profile only
(docs/WEB-VIEWS.md section 5); `Kubuno.Web` references `Kubuno.Views` but uses none of it yet (its compiled assembly does
not reference it until WV-9b).

## Themed dialogs

Every dialog and WPF surface the extension shows must look like Visual Studio's own in the dark, light, blue and
high-contrast themes (no white window, no default WPF buttons or selection colors). The rule for contributors:

- **Derive every modal dialog from `Kubuno.Shared.UI.ThemedDialog`** (`src/Shared/Kubuno.Shared/UI/ThemedDialog.cs`), never
  from `Window` or a bare `DialogWindow`, and show it with `ShowModal()` (owned by the IDE). It sets the themed
  dialog panel colors (`ThemedDialogColors.WindowPanelBrushKey` / `WindowPanelTextBrushKey`), switches the title bar
  to Windows' dark mode when the theme is dark (and back when the theme changes), and registers Visual Studio's
  themed-dialog styles (`VsResourceKeys.ThemedDialog*StyleKey`: button, text box, check box, radio button, combo
  box, list box, list view and items, grid column headers, tree view, label, hyperlink, toggle button) as implicit
  styles, so controls - including ones generated later by data templates - are themed without any per-control code.
- Use `ThemedControls` for what an implicit style cannot do: `GridListView` (a `ListView` with a `GridView` needs the
  grid-row item style for readable selection), `WithPlaceholder` (a search/filter box with gray placeholder text,
  also its accessible name), `SecondaryText` (gray descriptions and statuses), `ButtonRow` (right-aligned OK/Cancel
  row with Visual Studio's sizes and spacing). For bordered lists built from panels, use
  `ThemedDialogColors.ListBoxBorderBrushKey` / `ListBoxBrushKey`.
- Tool-window and document content (e.g. the crate manager) calls `ThemedControls.AddImplicitStyles(Resources)` and
  uses `EnvironmentColors` brushes for its own surfaces.
- Only theme resource keys, never literal colors; message boxes go through `VsShellUtilities.ShowMessageBox`.
- The assemblies compiled against the NuGet SDK reference it in `Kubuno.Shared`; the wizards, which compile against
  the installed Visual Studio's 18.x assemblies, link the file as **source** (`Kubuno.Desktop.TemplateWizard`) to keep
  their dependency closure minimal.
- Re-check: in the experimental instance, Tools > "Kubuno: Dialog Gallery" (shown only when Visual Studio was started
  with `/rootsuffix`; DTE command `Kubuno.DialogGallery`) lists every dialog with sample data - switch the theme and
  open them again. A new dialog adds itself to its assembly's `*DialogGallery.Entries`, which its layer registers as a
  provider (evaluated when the gallery opens, never during the package load).

## Phases

1. **Rust + Cargo + debug** (no dependency on XML views)
   - 1a. VSIX skeleton, rust-analyzer LSP client, TextMate grammar, rustfmt on save.
   - 1b. Cargo workspace: targets from `cargo metadata`, Build/Rebuild/Clean, Error List.
   - 1c. Launch/debug any bin/example/test target with the native debugger.
2. **XML view framework in `kubuno_ui`** (Rust): component metadata registry (single source
   for loader, schema, language server, property grid), XML loader, hot reload.
3. **View editing in VS**: XML language server (Rust), embedded live preview.
4. **Reduced designer**: preview ⇄ XML selection sync, property grid, toolbox.
5. **MCP server** in the extension: open file, selection, Error List, designer selection,
   preview capture, debugger state (stack, locals at a breakpoint).

## Known limits

- VS's LSP client is less complete than VS Code's (inlay hints, code lenses: to verify).
- The debugger's expression evaluator speaks C++, not Rust: fields and natvis views work,
  Rust expressions (method calls, traits) in the Watch window do not.
- rust-analyzer uses 1–2 GB on a large workspace.

## Roadmap — Kubuno web modules in the same VSIX (product owner, 2026-09-29)

The same extension must eventually cover **Kubuno web modules** (Rust/Axum backend process + React/TypeScript frontend loaded by the core host), not only desktop apps. Organisation to keep in mind now:
- **Layers**: `Kubuno.Rust.*` (language, Cargo, `.rsproj`, debugging — product-agnostic Rust support) · `Kubuno.Desktop.*` (kubuno_ui, the Rust design surface) · `Kubuno.Views.*` (`.kbview` designer and language client, shared by the targets) · `Kubuno.Web.*` (web modules) · `Kubuno.Shared` (settings, output pane, MCP bridge). Keep desktop-only concepts out of the Rust layer so web modules reuse it untouched. **Done 2026-09-30**, see "Layers (as built)" above (`Kubuno.Web` exists as a registered skeleton).
- **Web module = one solution, two projects**: the backend `.rsproj` (existing "Kubuno Module" template) + the frontend as VS's own JavaScript/TypeScript project (`.esproj`, Vite 8, React 19, TS 6, Tailwind v4, `@kubuno/ui`/`@kubuno/sdk`/`@kubuno/drive` from npm, `@ui` specifier mapping, `kubuno-module` cascade layer) — reuse Microsoft's JS project system rather than reinventing it.
- **F5 for a web module**: build backend + frontend, deploy into a local core (the `deploy_local.sh` equivalent on Windows, or a dev core instance), start/attach: native debugger on the module process + browser/JS debugging of the module's frontend inside the core host; `.kbpkg` packaging command (`build_kbpkg`) and "Install into core".
- **Kubuno-specific tooling**: `module.toml` editor/validation, SQLx migrations & `.sqlx` offline cache commands, `/internal/*` + events contracts, `SDK_VERSION` compatibility check, CHANGELOG `[Unreleased]` helper, release (`release.sh`) integration.
- Templates grouped in "Create a new project" under a **Kubuno** project type: Desktop application, Desktop view/user control, Web module (backend+frontend), Backend-only module, Rust console/library.

## Roadmap — Kubuno mobile apps in the same VSIX (product owner, 2026-09-29)

The mobile repo (`mobile`, ex-`android`) is a multi-app Gradle project: Kotlin + Jetpack Compose, shared modules `core-api`/`account`/`ui`/`sync`/`core-viewer`, one app per Kubuno module, releases per tag prefix. Same layering:
- **`Kubuno.Mobile.*` layer**, independent of the Desktop and Web layers, reusing `Kubuno.Shared` (and `Kubuno.Rust` for the Rust cores). **Skeleton done 2026-09-30** (`Kubuno.Mobile`, registered `MobileLayer`, see "Layers (as built)").
- **Project type**: a thin `.ktproj`/`.gradleproj` CPS wrapper over Gradle (same philosophy as `.rsproj`: `settings.gradle.kts`/`build.gradle.kts` stay the source of truth; one VS project per Gradle module; generated for the whole multi-app repo like "Generate Visual Studio projects"). Build/Clean/Run → `gradlew assembleDebug`/`installDebug`, Gradle errors mapped to the Error List.
- **Language**: Kotlin via an LSP client (kotlin-language-server / JetBrains' Kotlin LSP), TextMate grammars for Kotlin/Gradle KTS.
- **Devices & run**: device/emulator picker in the toolbar (adb), install + launch, Logcat tool window filtered by app, UnifiedPush/multi-account test helpers.
- **Debugging**: VS has no JVM/ART debugger — options to evaluate: a JDWP/DAP-based debug engine (via an existing Kotlin/Java DAP adapter bridged to VS), or a documented hand-off to Android Studio for step-debugging. Decide in a spike before promising F5-debugging.
- **UI**: Compose previews are rendered by the Compose tooling (layoutlib) — evaluate hosting previews in a VS tool window; no `.kbview` designer for mobile unless Kubuno views get a mobile renderer later.
- **Templates** under the Kubuno project type: "Kubuno mobile app (Android)" wired to the shared modules, "Shared mobile module".
- **Signing/release**: keystore config (opt-in, same cert), `release` per app tag prefix — outbound publishing stays a user action.

## Roadmap — Kubuno view rendering engine for mobile (Android, iOS, iPadOS) (product owner, 2026-09-29)

Requirement: like desktop, Kubuno views (`.kbview` + Rust view models/handlers) must render on Android and iOS/iPadOS (phones, tablets, other form factors), with the same designer workflow.
Recommended direction (to confirm by a spike): **one Rust UI core, several backends** rather than mapping `.kbview` to Compose/SwiftUI (two extra renderers that would drift from kubuno_ui):
- split `kubuno_ui`/`kubuno-controls` into a portable core (layout, widgets, theming, the existing `Canvas` trait, events of EVENTS.md) and platform backends: Windows (Direct2D/DirectWrite, today), Android and iOS via a portable GPU 2D renderer (Skia via `skia-safe`, or `vello`/`wgpu`) + portable text stack (shaping/fallback, e.g. `cosmic-text`/HarfBuzz) — spike compares fidelity with the Direct2D output and binary size;
- platform shells: Android (`NativeActivity`/`GameActivity` or a `SurfaceView` hosted in a Kotlin activity so it can coexist with the existing Kotlin/Compose apps), iOS/iPadOS (UIKit view + Metal layer, Swift host); touch/gestures, IME/soft keyboard, safe areas, rotation, per-device DPI, accessibility (AccessKit → TalkBack/VoiceOver), clipboard, file pickers, notifications;
- adaptive layouts: size classes (compact/regular) and breakpoints in `.kbview` (phone/tablet/desktop variants or responsive props), designer device frames (phone/tablet, portrait/landscape) rendered by the same core;
- toolchain: `aarch64-linux-android`/`x86_64-linux-android` via the NDK (buildable on Windows); **iOS requires macOS/Xcode** for build, signing and simulators — the product owner HAS a Mac: plan a "Pair to Mac" remote build host (as .NET MAUI does) driven from VS over SSH (build, sign, run on simulator/device, stream logs, remote debugging via lldb), with CI on macOS as a complement;
- coexistence/migration with the current native Android apps (Kotlin/Compose) — decide per app; shared Rust core (API/sync) can also be exposed to Kotlin/Swift via UniFFI.
Layering consequence: the designer and the view registry client that the mobile targets will share with desktop moved DOWN into `Kubuno.Views` (2026-10-02, WV-8); the mobile layer will add its own design surface (an `IProtocolDesignSurfaceHost`), as the web layer does - Mobile never references Desktop. Pair-to-Mac, device and Gradle tooling belong to `Kubuno.Mobile` (src/Mobile/Kubuno.Mobile/README.md).
Work order: after the extension's layer reorganisation (done 2026-09-30); start with a Windows-hosted spike of the portable renderer reproducing the gallery pixel-for-pixel, then Android, then iOS.
