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
                    Kubuno.Rust*                               (product-agnostic Rust support)
                         │
                    Kubuno.Core*                               (shared Visual Studio infrastructure)
```

| Layer | Assembly | What it holds |
|---|---|---|
| Core | `Kubuno.Core` | Layer contracts and sequencing (`Extensibility\KubunoLayer`, `KubunoLayerHost`, `KubunoLayerContext`), host accessors for MEF parts (`KubunoHost`: package, options pages, `JoinableTaskFactory`), shared GUIDs (`KubunoGuids`: package, command set, Output pane), themed dialogs and theme helpers (`UI\ThemedDialog`, `ThemedControls`, `VsTheme`, `DialogGallery`), unified settings plumbing (`Settings\KubunoDialogPage`, `UnifiedSettingAttribute`), the "Kubuno" Output pane (`Logging\KubunoLog`), the bundled-tool locator (`KubunoExtension`), the dialog gallery command, the MCP bridge start. |
| Core | `Kubuno.Core.Logic` | Pure helpers (no VS SDK): LSP plumbing (`Lsp\`: handshake streams, JSON, snippets, `WorkspaceEdit` merge), the hover markdown/QuickInfo model, the UI language (`Localization\UiLanguage`). |
| Core | `Kubuno.Core.Mcp.Bridge`, `Kubuno.Core.Mcp` | The MCP bridge contract (net48 in-proc + net8.0) and the `kubuno-vs-mcp.exe` server (docs/MCP.md). |
| Rust | `Kubuno.Rust` | rust-analyzer (language client, middle layer), IntelliSense, QuickInfo, CodeLens, inlay hints, Open Folder Cargo workspaces and Error List, launch/debug, natvis/Just My Code/step filters (`Debugging\`), the `.rsproj` commands, Dependencies node, crate manager, symbol tree, Rust and Debugging options pages, grammar and `languages.pkgdef`, Rust templates, and `Extensibility\` (below). |
| Rust | `Kubuno.Rust.Logic`, `Kubuno.Rust.Cargo`, `Kubuno.Rust.Launch` | Pure logic (no VS SDK): rust-analyzer discovery/handshake, completion and hover presentation, `.rsproj` generation and properties, Dependencies tree; `cargo metadata`/build messages/TOML (netstandard2.0, also used by `Kubuno.Rust.Sdk`'s tasks); the launch/debug environment. |
| Rust | `Kubuno.Rust.ProjectSystem` | The `.rsproj` CPS project type (MEF exports, `rsproj.pkgdef`, `RustProject.imagemanifest`). |
| Rust | `Kubuno.Rust.TestAdapter`, `Kubuno.Rust.Debugger`, `Kubuno.Rust.TemplateWizard` | Test Explorer adapter; the Concord component that formats Rust panics; the crate-name wizard of the templates. |
| Rust | `Kubuno.Cargo.MSBuild.Tasks` | The MSBuild tasks inside the `Kubuno.Rust.Sdk` NuGet package (name kept: it is part of the SDK's `UsingTask`s). |
| Desktop | `Kubuno.Desktop` | The `.kbview` language client (`Views\`), the WinForms-like designer with Toolbox, Properties and events (`Designer\`), the design surface host (`DesignerIntegration\`), the data tooling (`DataExplorer\`, `DataSources\`, `Migrations\`, SQL in Rust strings), printing (through the designer's library component tabs), paint debug, desktop templates, `kbview-languages.pkgdef`. |
| Desktop | `Kubuno.Desktop.Logic` | Pure logic: data, data sources, migrations, SQL, `.kbview` editor helpers, override members, control icons, the design build of a project's `kubuno_ui`, paint debug. |
| Desktop | `Kubuno.Desktop.ProjectSystem`, `Kubuno.Desktop.TemplateWizard` | The desktop target's CPS exports in a `.rsproj` (the designer as default `.kbview` editor, the `.kbview` icon) and the Kubuno control icons (`KubunoControls.imagemanifest`); the control item wizard. |
| Web | `Kubuno.Web` | Skeleton: `WebLayer`, registered, no feature yet (src/Web/Kubuno.Web/README.md). |
| Mobile | `Kubuno.Mobile` | Skeleton: `MobileLayer`, registered, no feature yet (src/Mobile/Kubuno.Mobile/README.md). |

Each layer keeps its pure logic in a `*.Logic` (or netstandard) assembly without the Visual Studio SDK, tested with plain
`dotnet test`: `tests/Kubuno.Core.Tests`, `Kubuno.Rust.Tests`, `Kubuno.Rust.Cargo.Tests`, `Kubuno.Rust.Launch.Tests`,
`Kubuno.Rust.TestAdapter.Tests`, `Kubuno.Cargo.MSBuild.Tasks.Tests`, `Kubuno.Core.Mcp.Tests`, `Kubuno.Desktop.Tests`
(which also covers the designer's view models through `Kubuno.Desktop`).

**The rules, enforced by `tests/Kubuno.Architecture.Tests`** (on the project files and, with
`System.Reflection.Metadata`, on the compiled assemblies): every project belongs to a layer by its name; project and
assembly references only go down (Core → nothing above; Rust → Core; Desktop, Web, Mobile → Core and Rust, never each
other); the `*.Logic` and netstandard assemblies reference no Visual Studio SDK; `Kubuno.Rust`, `Kubuno.Desktop`,
`Kubuno.Web` and `Kubuno.Mobile` each declare exactly one `KubunoLayer`, all listed by `KubunoPackage.CreateLayers`;
the packaging project references every shipped assembly; every source file's namespace starts with its assembly's.

**Composition.** There is still ONE package, `KubunoPackage` (src/Kubuno.VisualStudio), with the same GUID: it owns
every registration Visual Studio reads from the pkgdef (options pages, editor factory, tool windows, key binding
table, UI context rules, menus - attributes on the class) and the command table (`KubunoCommands.vsct`), and lists the
layers. `KubunoLayerHost` (Kubuno.Core) runs the same sequence the package ran before, per layer: background
`InitializeAsync`, UI-thread `InitializeOnUIThread` (reported as the package's UI-thread load time), then, once the
solution is loaded, `InitializeOnIdle` (UI thread at idle, after the Output pane is created) and
`InitializeDeferredAsync` (background, after the MCP bridge starts); `Dispose` in reverse order. A failing hook is
logged and does not stop the other layers. The layers' command IDs are in their own `PackageIds` classes
(`Kubuno.Rust.PackageIds`, `Kubuno.Desktop.PackageIds`, `Kubuno.Core.CoreCommandIds`) with the unchanged values of the
vsct. Static registrations are pkgdef fragments owned by their layer (`languages.pkgdef`, `debugging.pkgdef`,
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
- `Kubuno.Core.UI.DialogGallery.Register`, `KubunoHost`, `KubunoLog` for everything cross-cutting.
- CPS exports of a target go in the target's own `*.ProjectSystem` assembly, scoped to the `RustProjectSystem`
  capability next to the Rust ones.

**What stays identical for users** (checked by diffing the generated `Kubuno.VisualStudio.pkgdef`, the VSIX file list
and the template manifests with the previous build): the VSIX identity, the package GUID, command set and command IDs,
tool window GUIDs, the editor factory and its logical views, UI context rules, option page GUIDs (now explicit `[Guid]`
attributes, formerly derived from the type names) and their classic settings keys (`KubunoDialogPage`'s
`legacyTypeFullName`), the unified settings monikers, the key binding table, template IDs, CPS capabilities, image
monikers and the Test Explorer property IDs. Only the tool window `Name` values of the pkgdef (the type names) and the
assembly names changed - including the wizard assemblies the `.vstemplate` files name.

**Loading** is unchanged: the package still auto-loads on the one "Kubuno activation" rule (nothing loads for a
C#-only solution or the start window); each assembly with MEF parts is a `MefComponent` asset of the VSIX manifest (the
Web and Mobile ones too, so their first MEF part needs no manifest change). The "Kubuno" pane (and `KUBUNO_VS_LOG`)
still reports "package loaded in X ms (Y ms on the UI thread)" and the deferred initialization time.

**Visual Studio SDK builds.** Most assemblies compile against the NuGet `Microsoft.VisualStudio.SDK` 17.14; the CPS
project systems and the wizards compile against the installed Visual Studio's 18.x assemblies. `Kubuno.Core` compiles
against the lowest (17.x) and keeps its SDK packages private (`PrivateAssets="all"`, like the MCP bridge's), so both
kinds reference it without version conflicts; the wizards compile `ThemedDialog.cs` as source to keep their dependency
closure minimal.

**Adding a target layer (Web, Mobile)**: put its features in `src/<Layer>/Kubuno.<Layer>*` (pure logic in
`Kubuno.<Layer>.Logic`, CPS parts in `Kubuno.<Layer>.ProjectSystem`...), start them from its `KubunoLayer` hooks or as
MEF parts, declare its registrations on `KubunoPackage` and its commands in `KubunoCommands.vsct`, ship its pkgdef
fragments, templates and image manifests from the packaging project, and add its tests. Nothing in Core, Rust or the
other targets changes; if a target needs something another target has (the desktop layer's SQLx migrations for a web
module's backend, the view designer for mobile), that code moves DOWN into Rust (or Core) first - a target never
references another one.

Residuals kept on purpose (candidates to move when the Web layer gets features): the crate-name wizard
(`Kubuno.Rust.TemplateWizard`) also computes the `$kubunodesktopsrc$` and `$moduleid$` tokens of the desktop and module
templates (plain strings, no reference to those layers); the "Kubuno Module" backend template is still shipped by the
Rust layer; the `.kbview` file icon image stays in `RustProject.imagemanifest` so its moniker does not change (the desktop
layer applies it).

## Themed dialogs

Every dialog and WPF surface the extension shows must look like Visual Studio's own in the dark, light, blue and
high-contrast themes (no white window, no default WPF buttons or selection colors). The rule for contributors:

- **Derive every modal dialog from `Kubuno.Core.UI.ThemedDialog`** (`src/Core/Kubuno.Core/UI/ThemedDialog.cs`), never
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
- The file is shared as **source**, linked by each assembly that shows a dialog (`Kubuno.VisualStudio`,
  `Kubuno.Desktop.Designer`, `Kubuno.Rust.ProjectSystem`, `Kubuno.Rust.TemplateWizard`):
  they compile against different Visual Studio SDK builds (NuGet 17.x vs the installed 18.x assemblies), so a
  shared assembly would bring binding conflicts.
- Re-check: in the experimental instance, Tools > "Kubuno: Dialog Gallery" (shown only when Visual Studio was started
  with `/rootsuffix`; DTE command `Kubuno.DialogGallery`) lists every dialog with sample data - switch the theme and
  open them again. A new dialog adds itself to its assembly's `*DialogGallery.Entries`.

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
- **Layers**: `Kubuno.Rust.*` (language, Cargo, `.rsproj`, debugging — product-agnostic Rust support) · `Kubuno.Desktop.*` (kubuno_ui, `.kbview`, designer) · `Kubuno.Web.*` (web modules) · shared `Kubuno.Core` (settings, output pane, MCP bridge). Keep desktop-only concepts out of the Rust layer so web modules reuse it untouched. **Done 2026-09-30**, see "Layers (as built)" above (`Kubuno.Web` exists as a registered skeleton).
- **Web module = one solution, two projects**: the backend `.rsproj` (existing "Kubuno Module" template) + the frontend as VS's own JavaScript/TypeScript project (`.esproj`, Vite 8, React 19, TS 6, Tailwind v4, `@kubuno/ui`/`@kubuno/sdk`/`@kubuno/drive` from npm, `@ui` specifier mapping, `kubuno-module` cascade layer) — reuse Microsoft's JS project system rather than reinventing it.
- **F5 for a web module**: build backend + frontend, deploy into a local core (the `deploy_local.sh` equivalent on Windows, or a dev core instance), start/attach: native debugger on the module process + browser/JS debugging of the module's frontend inside the core host; `.kbpkg` packaging command (`build_kbpkg`) and "Install into core".
- **Kubuno-specific tooling**: `module.toml` editor/validation, SQLx migrations & `.sqlx` offline cache commands, `/internal/*` + events contracts, `SDK_VERSION` compatibility check, CHANGELOG `[Unreleased]` helper, release (`release.sh`) integration.
- Templates grouped in "Create a new project" under a **Kubuno** project type: Desktop application, Desktop view/user control, Web module (backend+frontend), Backend-only module, Rust console/library.

## Roadmap — Kubuno mobile apps in the same VSIX (product owner, 2026-09-29)

The mobile repo (`mobile`, ex-`android`) is a multi-app Gradle project: Kotlin + Jetpack Compose, shared modules `core-api`/`account`/`ui`/`sync`/`core-viewer`, one app per Kubuno module, releases per tag prefix. Same layering:
- **`Kubuno.Mobile.*` layer**, independent of the Desktop and Web layers, reusing `Kubuno.Core` (and `Kubuno.Rust` for the Rust cores). **Skeleton done 2026-09-30** (`Kubuno.Mobile`, registered `MobileLayer`, see "Layers (as built)").
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
Layering consequence: the designer, the view registry and the design surface host that the mobile targets will share with desktop live in `Kubuno.Desktop` today; when the mobile renderer arrives they move DOWN into a shared lower assembly (Mobile never references Desktop). Pair-to-Mac, device and Gradle tooling belong to `Kubuno.Mobile` (src/Mobile/Kubuno.Mobile/README.md).
Work order: after the extension's layer reorganisation (done 2026-09-30); start with a Windows-hosted spike of the portable renderer reproducing the gallery pixel-for-pixel, then Android, then iOS.
