# Kubuno.Mobile - the mobile layer

The Visual Studio side of **Kubuno mobile apps** on Android, iOS and iPadOS: the multi-app Gradle/Kotlin repository
(`mobile`, ex-`android`) today, and Kubuno views rendered by the portable Rust UI engine tomorrow (docs/ARCHITECTURE.md,
"Roadmap - Kubuno mobile apps in the same VSIX" and "Roadmap - Kubuno view rendering engine for mobile").

## Status

Skeleton. `MobileLayer` is created by `KubunoPackage.CreateLayers` and runs through the same `KubunoLayer` hooks as the
Rust and Desktop layers; it has no feature yet. The assembly ships in the VSIX and is a `MefComponent` asset, so its first
MEF part needs no manifest change.

## Rules

- References `Kubuno.Shared`, the Rust layer (Rust cores of the apps, the `aarch64-linux-android`/iOS targets, UniFFI) and
  the views layer (`Kubuno.Views*`: the `.kbview` designer, when mobile gets a renderer).
  **Never** `Kubuno.Desktop*` or `Kubuno.Web*` - `tests/Kubuno.Architecture.Tests` fails otherwise.
- The `.kbview` designer and the view registry client moved down into `Kubuno.Views` (docs/WEB-VIEWS.md WV-8): when the
  portable renderer lets mobile reuse them, this layer only adds its design surface (an `IProtocolDesignSurfaceHost`,
  plugged in through `DesignSurfaceHostFactoryHost`); Mobile never references Desktop.
- Pure logic in `Kubuno.Mobile.Logic` (no Visual Studio SDK, `tests/Kubuno.Mobile.Tests`); CPS parts in
  `Kubuno.Mobile.ProjectSystem`. Namespaces start with the assembly name (`Kubuno.Mobile.*`).

## What goes here (per the roadmaps)

| Feature | Where it plugs in |
|---|---|
| A thin `.ktproj`/`.gradleproj` CPS project type over Gradle (`settings.gradle.kts`/`build.gradle.kts` stay the source of truth, one project per Gradle module, generated for the whole multi-app repository); Build/Clean/Run through `gradlew assembleDebug`/`installDebug`, Gradle errors in the Error List | `Kubuno.Mobile.ProjectSystem` (its own pkgdef fragment, like `rsproj.pkgdef`), plus an MSBuild SDK like `Kubuno.Rust.Sdk` |
| Kotlin language support: an LSP client (kotlin-language-server / JetBrains' Kotlin LSP), TextMate grammars for Kotlin and Gradle KTS | MEF parts of this assembly; the grammars and their pkgdef fragment shipped by src/Kubuno.VisualStudio |
| Devices: emulator/device picker in the toolbar (adb), install and launch, a Logcat tool window filtered by app, UnifiedPush and multi-account test helpers | `MobileLayer.InitializeOnUIThread` (commands), tool windows declared on `KubunoPackage` with their own GUIDs |
| Debugging: a JDWP/DAP bridge to Visual Studio, or a documented hand-off to Android Studio - decided by a spike first | here, after the spike |
| Compose previews (layoutlib) in a tool window, if the spike says so | a tool window of this layer |
| The portable Rust UI engine's shells (Android `SurfaceView`/`GameActivity`, iOS UIKit + Metal): designer device frames (phone/tablet, portrait/landscape), size classes and breakpoints in `.kbview` | this layer, on top of the designer once it has moved below Desktop |
| **Pair to Mac**: a remote macOS build host driven over SSH (build, sign, run on simulator/device, stream logs, remote lldb debugging), as .NET MAUI does; CI on macOS as a complement | a connection service started in `MobileLayer.InitializeDeferredAsync`, its options page (`Kubuno.Shared.Settings.KubunoDialogPage`), its themed dialogs (`Kubuno.Shared.UI.ThemedDialog`) |
| Templates: "Kubuno mobile app (Android)" wired to the shared modules, "Shared mobile module" | `ProjectTemplates\` of this project, shipped by the packaging project |
| Signing and release per app tag prefix (keystore opt-in, same certificate); publishing stays a user action | commands of this layer |
