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

## Themed dialogs

Every dialog and WPF surface the extension shows must look like Visual Studio's own in the dark, light, blue and
high-contrast themes (no white window, no default WPF buttons or selection colors). The rule for contributors:

- **Derive every modal dialog from `Kubuno.VisualStudio.UI.ThemedDialog`** (`src/Shared/UI/ThemedDialog.cs`), never
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
  `Kubuno.VisualStudio.Designer`, `Kubuno.VisualStudio.RustProjectSystem`, `Kubuno.VisualStudio.TemplateWizard`):
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
- **Layers**: `Kubuno.Rust.*` (language, Cargo, `.rsproj`, debugging — product-agnostic Rust support) · `Kubuno.Desktop.*` (kubuno_ui, `.kbview`, designer) · `Kubuno.Web.*` (web modules) · shared `Kubuno.Core` (settings, output pane, MCP bridge). Keep desktop-only concepts out of the Rust layer so web modules reuse it untouched.
- **Web module = one solution, two projects**: the backend `.rsproj` (existing "Kubuno Module" template) + the frontend as VS's own JavaScript/TypeScript project (`.esproj`, Vite 8, React 19, TS 6, Tailwind v4, `@kubuno/ui`/`@kubuno/sdk`/`@kubuno/drive` from npm, `@ui` specifier mapping, `kubuno-module` cascade layer) — reuse Microsoft's JS project system rather than reinventing it.
- **F5 for a web module**: build backend + frontend, deploy into a local core (the `deploy_local.sh` equivalent on Windows, or a dev core instance), start/attach: native debugger on the module process + browser/JS debugging of the module's frontend inside the core host; `.kbpkg` packaging command (`build_kbpkg`) and "Install into core".
- **Kubuno-specific tooling**: `module.toml` editor/validation, SQLx migrations & `.sqlx` offline cache commands, `/internal/*` + events contracts, `SDK_VERSION` compatibility check, CHANGELOG `[Unreleased]` helper, release (`release.sh`) integration.
- Templates grouped in "Create a new project" under a **Kubuno** project type: Desktop application, Desktop view/user control, Web module (backend+frontend), Backend-only module, Rust console/library.

## Roadmap — Kubuno mobile apps in the same VSIX (product owner, 2026-09-29)

The mobile repo (`mobile`, ex-`android`) is a multi-app Gradle project: Kotlin + Jetpack Compose, shared modules `core-api`/`account`/`ui`/`sync`/`core-viewer`, one app per Kubuno module, releases per tag prefix. Same layering:
- **`Kubuno.Mobile.*` layer**, independent of the Desktop and Web layers, reusing `Kubuno.Core`.
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
Work order: after the extension's layer reorganisation; start with a Windows-hosted spike of the portable renderer reproducing the gallery pixel-for-pixel, then Android, then iOS.
