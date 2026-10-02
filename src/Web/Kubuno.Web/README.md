# Kubuno.Web - the web module layer

The Visual Studio side of **Kubuno web modules**: a Rust/Axum backend process plus a React/TypeScript frontend that the
Kubuno core host loads (docs/ARCHITECTURE.md, "Roadmap - Kubuno web modules in the same VSIX").

## Status

Phases 1 and 2 are built (docs/WEB.md): web solution generation (single and multi-repository), the `.esproj`\nfrontends on top of Visual Studio's JavaScript project system, Kubuno.Web.Sdk, F5 of the core and of a module (dev core,\ndevelopment database guard, deployment, attach to the module process, browser), the `.kbpkg` command, the version\ntools and the "Kubuno Core Web Module" template. Assemblies of the layer: `Kubuno.Web` (this one: commands, template),\n`Kubuno.Web.Logic`, `Kubuno.Web.MSBuild.Tasks`, `Kubuno.Web.ProjectSystem`, `Kubuno.Web.TemplateWizard`.

## Rules

- References `Kubuno.Shared`, the Rust layer (`Kubuno.Rust`, `Kubuno.Rust.Logic`, and the other `Kubuno.Rust.*`
  assemblies when needed) and the views layer (`Kubuno.Views*`: the view designer the web views designer reuses,
  docs/WEB-VIEWS.md). **Never** `Kubuno.Desktop*` or `Kubuno.Mobile*` - `tests/Kubuno.Architecture.Tests` fails
  the build's tests otherwise.
- Something the desktop layer already has and a web module needs (the SQLx migrations and `.sqlx` cache commands, the
  Data Explorer) moves **down** into the Rust layer (or Views, or Shared) first; it is never referenced sideways.
- Pure logic goes in a `Kubuno.Web.Logic` project without the Visual Studio SDK (tested by a `tests/Kubuno.Web.Tests`
  project); CPS exports for `.rsproj`/`.esproj` in a `Kubuno.Web.ProjectSystem` project, like the desktop layer's.
- Namespaces start with the assembly name (`Kubuno.Web.*`).

## What goes here (per the roadmap)

| Feature | Where it plugs in |
|---|---|
| "Web module" template: one solution with the backend `.rsproj` (today's "Kubuno Module" template, still shipped by the Rust layer - it moves here) and the frontend as Visual Studio's own JavaScript/TypeScript project (`.esproj`: Vite 8, React 19, TypeScript 6, Tailwind v4, `@kubuno/ui`/`@kubuno/sdk`/`@kubuno/drive` from npm, the `@ui` specifier mapping, the `kubuno-module` cascade layer) | `ProjectTemplates\`/`ItemTemplates\` of this project, shipped by src/Kubuno.VisualStudio at the same VSIX paths as the other layers' templates; a wizard assembly of its own if tokens are needed (today `$moduleid$` is computed by the Rust crate-name wizard) |
| F5 of a web module: build backend + frontend, deploy into a local core (the Windows equivalent of `deploy_local.sh`, or a dev core instance), start/attach the native debugger on the module process and the browser/JS debugger on its frontend inside the core host | a CPS debug launch provider in `Kubuno.Web.ProjectSystem`, scoped to the web module capability |
| `.kbpkg` packaging (`build_kbpkg`) and "Install into core" commands | `WebLayer.InitializeOnUIThread` (commands), command IDs in a `Kubuno.Web.PackageIds` class and in `KubunoCommands.vsct` |
| `module.toml` editor and validation, `SDK_VERSION` compatibility check, `/internal/*` and events contracts | MEF parts (content type, taggers, completion) of this assembly |
| SQLx migrations and `.sqlx` offline cache commands | move the desktop layer's `Migrations\` down to the Rust layer, then use it from here |
| CHANGELOG `[Unreleased]` helper, `release.sh` integration | commands of this layer |
| Options (Tools > Options > Kubuno > Web) | an options page deriving from `Kubuno.Shared.Settings.KubunoDialogPage`, declared on `KubunoPackage`, with its monikers in `UnifiedSettings\kubuno.registration.json` (tools/gen-unified-settings.ps1) |
| Dialogs | derive from `Kubuno.Shared.UI.ThemedDialog`, register samples with `Kubuno.Shared.UI.DialogGallery.Register` |
