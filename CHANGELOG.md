# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Added

- **The `.kbview` designer renders with the project's own `kubuno_ui.dll`**: once the project has been
  built, the designer's preview is compiled against the project's dependency graph and loads exactly the
  `kubuno_ui.dll` the application loads, so a change to the Kubuno controls or styles the project uses
  shows in the designer after the next build - no mismatch between what the designer draws and what the
  application draws. Until then (or when that preview cannot be prepared) the designer uses the preview
  runtime bundled with the extension and says so in a thin bar at the top of the design view ("Preview:
  bundled runtime - build the project to use its kubuno_ui.dll"), with a *Build* link. The switch is
  automatic, and after every later build of the project the preview restarts on the new runtime while
  keeping the selection. The preparation takes a couple of seconds after a build, runs in the
  background (it can be canceled from the bar) and reports its progress and errors in the *Kubuno*
  output pane. A preview whose loaded `kubuno_ui.dll` is not the one it was built against is refused
  with a clear message instead of running on mismatched code.

- **A "Dependencies" node like a .NET project's**: under a `.rsproj`, *Dependencies* (*Dépendances* in a
  French Visual Studio) now shows the resolved dependency graph in categories with the .NET project
  system's icons - *Procedural macros* (like *Analyzers*, including the ones a dependency brings in),
  *Toolchain* (like *Frameworks*: the Rust version, channel and target, with `std`/`core`/`alloc`/
  `proc_macro`/`test`), *Crates* (registry crates, like *Packages*), *Projects* (path dependencies) and
  *Git*. Every crate shows its resolved version and expands into its own dependencies; dev- and build-
  dependencies carry a `[dev]`/`[build]` badge; an unresolved dependency gets the yellow warning icon, a
  yanked version too, an outdated one an "update available" marker (checked against crates.io in the
  background); an optional dependency no feature enables is dimmed, as is one only used on another
  platform. Errors (a broken `Cargo.toml`, a failed resolution) appear as a node under *Dependencies*.
  The tree refreshes when `Cargo.toml` or `Cargo.lock` changes, never blocks Visual Studio, works
  offline and keeps expanded nodes expanded.
- **Context menus on the Dependencies tree**: *Add Project Reference...*, *Manage Crates...*, *Update
  Crates* (`cargo update`), *Remove Unused Dependencies...* (through cargo-machete, or cargo-udeps with an
  installed nightly toolchain; when neither is installed, a message says how to install them), *Scope to
  This* and *New Solution Explorer View*; on a dependency: *Open Documentation* (docs.rs, the toolchain's
  local documentation, or `cargo doc --open`), *Open Source Code*, *Update* (`cargo update -p`),
  *Remove* (`cargo remove`), *Copy Full Path*, *Open Folder in File Explorer* and *Properties*.
- **Properties window (F4) for a dependency**: name, requested and resolved version, latest version,
  source (registry, path, git URL and commit), path, activated and requested features, default
  features, optional, type (normal/dev/build), target (`cfg(...)`), license, repository, description -
  read-only, each with a description, like a .NET package reference. The toolchain node shows the Rust
  version, channel, target, commit, LLVM version and sysroot.
- **Reference Manager** (replaces the former "Project reference" checklist): Projects > Solution lists
  the other `.rsproj` projects, Browse > Recent the path dependencies outside the solution; search box,
  details pane, and *Browse...* to reference any crate folder. OK applies `cargo add --path` /
  `cargo remove`.
- **Crate manager**, the counterpart of NuGet's package manager (opened from the Dependencies node, a
  dependency, or *Add > Cargo dependency (crate)...*): *Browse* searches crates.io (download counts,
  descriptions, most downloaded crates first), *Installed* lists the project's registry crates (works
  offline), *Updates* the outdated ones (with *Update all*). The details pane offers the version,
  dependency type, default features and each feature as a checkbox, license, links, minimum Rust
  version, and *Install* / *Update* / *Uninstall* through `cargo add`/`cargo remove`, whose output goes
  to the "Kubuno" Output pane. It follows the Visual Studio theme and language.

- **Views work like Windows Forms forms**: the *Kubuno Desktop Application* project template and the
  *Kubuno View* item template now create an absolute surface - a `<Panel DesignWidth="800"
  DesignHeight="450">` root whose controls have their own `X`/`Y`/`Width`/`Height` and an `Anchor`
  (the Status field stretches with the window, anchored Top, Left, Right; the button stays top-right).
  `Anchor`/`Dock` are therefore usable on every control of a new view, as on a new WinForms form,
  instead of being greyed out under the former `Card` > `Stack` layout. The generated application opens
  its window with the view's design size as its client area and relays out the anchored controls on
  every resize. Existing views are not changed.
- **Toolbox drops into a Panel are placed like WinForms controls**: at the drop point (whole pixels),
  with a default size suited to the control (a button 100 x 36, a text field 200 x 36, ...) and
  `Anchor="Top, Left"`; a Toolbox double-click does the same.

### Fixed

- **Empty Kubuno tabs in the Toolbox for other documents**: with a code file or any other non-`.kbview`
  document active, the Toolbox showed the Kubuno tabs (*Display*, *Choice*, *Text*...) each with its
  "There are no usable controls in this group" text. The Kubuno tabs now only appear while a `.kbview`
  designer is the active document, like the Windows Forms tabs only appear for a Windows Forms
  designer; tabs shared with Windows Forms keep their own controls, and empty Kubuno tabs left by an
  earlier version are removed.
- **Cargo output could be lost**: the extension's process runner could return before the last lines
  of a command's output were read, so a large `cargo metadata` result sometimes came back empty
  ("Could not parse `cargo metadata` output").
- **Crisp Toolbox icons at high DPI**: the Kubuno components' Toolbox icons looked deformed and blurry at
  175 % (uneven circles, soft irregular lines, glyphs touching the cell edges). They are now drawn for the
  pixel grid - 1-pixel strokes on whole pixels, symmetric pixel circles, a 1-pixel margin - like the Windows
  Forms toolbox icons, which Visual Studio's scaling keeps sharp.
### Changed

- **Anchor/Dock in the Properties window show their default value like WinForms**: an absent `Anchor`
  reads `Top, Left` and an absent `Dock` `None`, in normal (non-bold) type; `Anchor` written as the
  default in any order is not bold either. A typed `Anchor` value is normalized (`right,bottom` →
  `Bottom, Right`) and an invalid one is refused with the grid's own message.
- `tools/test-templates.ps1 -Run` also checks that a new desktop application's window opens with the
  view's design size as its client area.

- **Multi-selection in the `.kbview` designer, like the Windows Forms designer**: a drag on the empty
  area of a container (or of the view, or of the canvas around it) draws a dashed selection rectangle
  that selects the children of that container it touches; Ctrl+click toggles an element, Shift+click adds
  it, Ctrl+A selects all its siblings; Esc cancels the rectangle. The primary selection (the last clicked)
  has white grab handles, the others filled ones. Dragging, arrow keys, Delete, Cut/Copy/Paste and
  Duplicate apply to the whole selection, and a resize on the primary's handles resizes every selected
  element by the same amount - each gesture is one undo unit. A right-click on a selected element keeps
  the selection.
- **Layout commands (Windows Forms' Layout toolbar and Format menu)**: Align (Lefts, Centers, Rights,
  Tops, Middles, Bottoms - relative to the primary selection), Make Same Size (width, height, both),
  Horizontal/Vertical Spacing (make equal, increase, decrease, remove), Center in View (horizontally,
  vertically), Bring to Front / Send to Back - Visual Studio's own commands, with its icons and names, in a
  new "Kubuno Layout" toolbar shown while a `.kbview` designer is active and in submenus of the surface's
  context menu, enabled only when meaningful (e.g. two movable elements in a Panel to align). Each is one
  undo unit.
- **The Properties window (F4) shows a multi-selection**: the common properties, a blank value where the
  elements differ, and an edit applies to every selected element as one undo unit. The XML view and the
  Document Outline follow the primary selection.

- **Resizable design canvas in the `.kbview` designer, like the Windows Forms designer**: the view is
  shown in a Kubuno window frame (its `Title` in the title bar) at its design size on a dark neutral
  canvas, with three resize handles (right edge, bottom edge, corner), a live relayout and a size tooltip
  while dragging, and scrollbars when it does not fit. Releasing writes the size in ONE undo unit - to
  the root's `Width`/`Height` when it has them, else to the design-time `DesignWidth`/`DesignHeight`
  (800×600 by default), together with the new place of the children its anchors moved. Clicking the
  canvas or the title bar selects the view; the Properties window then lists `DesignWidth`/`DesignHeight`
  under "Layout" ("Disposition").
- **Right-click context menus on the design surface**, native Visual Studio menus (`KubunoCommands.vsct`)
  in VS's language: on an element - View Code (F7), Create Handler › (the element's events), Cut, Copy,
  Paste, Duplicate, Delete, Select › (its containers), Bring to Front, Send to Back, Wrap In › (Stack,
  Panel, Card, GroupBox, ScrollArea - only those the registry allows), Remove Container, Properties; on
  the canvas - Paste, View Code, View Properties, Design Size... (a small dialog). Right-click selects
  first; Shift+F10 and the context-menu key open the selection's menu. Ctrl+C / Ctrl+X / Ctrl+V / Ctrl+D
  work directly on the surface. The clipboard holds the element's XML (a private format plus plain text,
  so XML copied from an editor pastes too); a paste renames colliding `x:Name`s, and a paste or wrap
  the registry forbids is refused with a status-bar message. Every action is one surgical edit through
  `kubuno-views-ls` (new `insertFragment`/`wrapElement`/`unwrapElement` operations) - one undo unit.
- **Dock and Anchor pickers in the Properties window (F4)**: the Windows Forms designer's own drop-down
  editors (the five Dock regions plus None; four Anchor bars around a centre box) edit the attributes
  (`Top, Left, Right`). Under a parent that does not lay out by Dock/Anchor (a `Stack`...), both rows
  stay listed but greyed, with a description saying they only apply inside a `Panel` (as WinForms keeps
  them for a control in a FlowLayoutPanel); the language server warns when they are set there.

- **Extended "Ajouter" project-node submenu for `.rsproj`, matching the WinForms project system's
  own shape** (right-click a `.rsproj` project node > "Ajouter"): **"Référence de projet..."** lists
  every other `.rsproj` in the solution with a checkbox (like Reference Manager), applying whatever
  changed with `cargo add --path`/`cargo remove` - never a hand-edited `Cargo.toml`; **"Vue
  Kubuno...", "Module Rust...", "Test d'intégration...", "Exemple...", "Binaire..."** each prompt for
  a name and add the corresponding item template (`.kbview`+code-behind, `src/*.rs`, `tests/*.rs`,
  `examples/*.rs`, `src/bin/*.rs`) via `ProjectItems.AddFromTemplate` against this extension's own
  installed template files; **"Dépendance Cargo (crate)..."** is a small VS-styled dialog (crate
  name, optional version/features, Normal/Dev/Build) that runs `cargo add`, with cargo's own
  output going to the "Kubuno" pane. All seven are scoped to `.rsproj` only via a
  `ProvideUIContextRule` (`ActiveProjectCapability:RustProjectSystem`) on the shared, shell-owned
  `IDG_VS_CTXT_PROJECT_ADD_REFERENCES`/`_FORMS`/`_MISC` groups the WinForms/C# project system itself
  injects into - not a competing top-level menu. The Dependencies node gained the same "Dépendance
  Cargo (crate)..." entry on its own context menu, plus "Supprimer" (`cargo remove`) on one crate
  node, both via `IVsUIShell.ShowContextMenu` (`Microsoft.Internal.VisualStudio.PlatformUI.
  IContextMenuPattern`) since neither node is a real `IVsHierarchy` item with a shell-registered
  menu. `CargoCommand` gained `Add`/`Remove` kinds (`Kubuno.Cargo`) - never hand-rewriting
  `Cargo.toml`, matching the platform `CLAUDE.md`'s own rule.
- Two more item templates ("Add New Item"): **Rust Example** (`examples/$name$.rs`) and **Rust
  Binary** (`src/bin/$name$.rs`), alongside the existing three.
- **`docs/GETTING-STARTED.md`**: a step-by-step guide for a developer installing the extension for
  the first time - prerequisites, installing the VSIX, creating a project from the four templates,
  the `.rsproj` build/F5/Test Explorer workflow, the `.kbview` designer, Open Folder mode and its
  limitations, the MCP bridge for Claude, and troubleshooting (first-run screens, the template
  cache needing a second Visual Studio start after install, a missing-DLL dialog meaning a PATH
  problem, SMB directory-cache races on a network drive, and more). Linked from `README.md`.
- **`tools/test-templates.ps1`, a pre-release check of the "Create a new project" templates**: creates
  each project template in a temporary folder the way Visual Studio does, builds it with `cargo build`
  (no warning allowed) and with MSBuild through its `.rsproj`, and with `-Run` starts the result.
- **`KUBUNO_DESKTOP_SRC`**: the environment variable naming your `github.com/kubuno/desktop` checkout;
  a new Kubuno Desktop Application points at it (default `Z:\src\desktop\windows`), and its build stops
  with a clear `KUBUNO0001` error when that folder is missing.
- **Tools > "Kubuno: Restart rust-analyzer"** ("Kubuno : Redémarrer rust-analyzer" in a French Visual
  Studio): restarts the Rust language server without closing the solution - a recovery tool when
  IntelliSense for Rust looks wrong. The open `.rs` files are sent to the new server again.

### Removed

- **The fallback "Kubuno Toolbox" and "Kubuno Properties" tool windows** and their Tools-menu
  commands. The `.kbview` designer fills Visual Studio's own Toolbox and Properties window
  (`docs/DESIGNER.md` §11), so the fallbacks were redundant - and the "Kubuno Toolbox" one could stay
  stuck on its "Open a .kbview file..." placeholder when restored from a saved window layout. Their
  tool-window GUIDs are no longer registered, so a layout saved with them open does not bring them
  back. The "Kubuno View Outline" window stays.

### Fixed

- **Rust errors no longer stay in the editor and the Error List after they are fixed.** While typing
  (for example `vm.set(` before its arguments), rust-analyzer reports errors such as `E0107 expected 2
  arguments, found 0` or `unmatched '}'`; once the code was complete again, the red squiggles and the
  Error List entries stayed until the file was closed, although `cargo check` was clean. rust-analyzer
  gives every diagnostics report the same identifier, and Visual Studio ignores an empty report whose
  identifier did not change, so the "no more errors" report was never applied. Each report now gets its
  own identifier, so fixed errors disappear as soon as rust-analyzer has re-checked the file.
- **Creating an event handler from the designer keeps the code-behind's formatting**: the new entry in the
  `handlers!` table is added on its own line with the same indentation as the existing entries, and the
  table's closing brace keeps its indentation (it used to lose it, with the new entry over-indented). A
  missing comma after the last existing entry is added, and a CRLF file stays CRLF. (Fix in
  `kubuno-views-ls`, desktop repository.)
- **A new Kubuno Desktop Application builds and runs with F5 out of the box**: it failed with
  `E0463 can't find crate for 'kubuno_ui'` when `CARGO_TARGET_DIR` was set machine-wide, because it
  then built into the same target directory as the Kubuno desktop apps, where each build overwrote the
  other's `kubuno_ui.dll`. The project now builds into a directory of its own
  (`$(CARGO_TARGET_DIR)\rsproj\<crate>`, or `<project>\target`). Existing projects: see
  `docs/GETTING-STARTED.md`, Troubleshooting.
- **A `.kbview` designer restored with the solution (on its Design tab) no longer loses its selection
  sync, Properties window and Toolbox wiring for good**: setup waited only 10 s for the XML view, which a
  pane that has not shown its XML/Split tab never creates; it now waits for it for the pane's life, and
  the context menus load the component registry on their own.
- **No custom icons in Solution Explorer after a normal VSIX install** (the `.rsproj` project node,
  `.rs`, `.kbview`, `Cargo.toml` and the `.kbview` element nodes all showed blank), while everything
  worked in the experimental instance. Root cause: the package registered one
  `[ProvideCodeBase]` per private assembly, and none of those assemblies is strong-named. When
  VSIXInstaller regenerates the hive's `devenv.exe.config`, each registration becomes a `<codeBase>`
  outside Visual Studio's application base for a `publicKeyToken=""` identity, which the .NET
  Framework refuses: every by-name `Assembly.Load` of it then fails with `FileLoadException`
  `0x80131041` ("private assembly located outside the appbase directory"), even while the same
  assembly is already loaded, and without raising `AssemblyResolve`. Visual Studio's image service
  loads the icons' XAML resources by assembly name, so every image-manifest moniker came out empty
  (the Toolbox icons, rendered directly with WPF, were unaffected); the same error also appeared in
  the MEF composition log for `Kubuno.Cargo`. The experimental instance hid it because MSBuild's
  development deployment never regenerates `devenv.exe.config`. The per-assembly code bases are
  replaced by a single `[ProvideBindingPath]` on the package, which writes nothing into
  `devenv.exe.config` and lets Visual Studio's own resolver load our assemblies from the extension
  folder.

- **The known pre-existing `NU1605` restore warning on `Kubuno.VisualStudio.csproj` is fixed**, not
  just documented: `Kubuno.Cargo.csproj`'s and `Kubuno.VisualStudio.csproj`'s own `System.Text.Json`
  `PackageReference` were pinned to `8.0.5`, below the `9.0.0` floor `Microsoft.VisualStudio.SDK`'s
  own net462 dependency closure already resolves to - a plain version-alignment fix, both pins (and
  the matching `Content` item path that ships `System.Text.Json.dll` inside the VSIX) now read
  `9.0.0`. A clean `Restore` and `Build` of the whole solution now report 0 warnings.
- **A clean build of the whole solution silently shipped a `Kubuno.VisualStudio.vsix` missing its
  entire MCP bridge** (`tools\kubuno-vs-mcp\` - no `kubuno-vs-mcp.exe`, no dependency DLLs), with 0
  build errors or warnings to say so. Root cause: `kubuno-vs-mcp.exe`'s own `Content` item was a
  plain top-level `<ItemGroup>` wildcard glob (`$(KubunoMcpOutputDir)**\*.*`) - those are evaluated
  once, at project EVALUATION time, before any target (including the `ReferenceOutputAssembly=false`
  `ProjectReference` to `Kubuno.Mcp.csproj` that builds it) has run; on a from-clean checkout nothing
  has built `kubuno-vs-mcp.exe` yet at that point, so the glob silently matched zero files - the
  pre-existing `KubunoCheckToolExes` exists-check never caught it either, since by the time THAT
  target ran (`BeforeTargets="Build"`), the exe already existed (built earlier, as a side effect of
  `ResolveProjectReferences`), so the check passed while the file glob's own timing was still wrong.
  Fixed by moving the glob into a new `KubunoAddMcpContent` target, `AfterTargets=
  "ResolveProjectReferences"` (deliberately not ALSO `BeforeTargets="Build"` - found live, comparing
  MSBuild diagnostic-verbosity target IDs, that combining the two instead pins a target right before
  the outer `Build` target's own near-empty body, which for this VSSDK project only starts after
  VSSDK's own `GetVsixSourceItems`/`CreateVsixContainer` - the steps that actually snapshot/package
  `@(Content)` - have already run, always too late no matter how the target is otherwise triggered),
  so the `Content` items are created dynamically once the exe is guaranteed to be on disk, ahead of
  `GetVsixSourceItems`. Verified live end to end: a genuinely clean `Restore` then `Build` of
  `Kubuno.VisualStudio.sln` (no manual pre-build of `Kubuno.Mcp.csproj`) now produces a VSIX whose
  `tools\kubuno-vs-mcp\` holds `kubuno-vs-mcp.exe` and its full dependency closure.

- **The `.kbview` designer now works like the Windows Forms designer, with Visual Studio's own tool
  windows** (docs/DESIGNER.md section 11):
  - **Toolbox**: while a `.kbview` designer is the active document, Visual Studio's Toolbox lists the
    Kubuno components in tabs per family ("Contrôles communs", "Affichage", "Choix", "Texte",
    "Conteneurs", "Données" - English names in an English Visual Studio), alphabetically, below the
    usual "Pointer" entry; they are hidden for every other document. Drag a component onto the design
    surface to insert it where it is dropped (on its own, indented line; one undo step; the new element
    is selected), or double-click it to add it to the selected container.
  - **New control icons**: one icon per Kubuno component, in the style of the Kubuno apps (Lucide
    glyphs, Kubuno blue accent, light/dark/high-contrast variants), in the Toolbox and on the `.kbview`
    element nodes of Solution Explorer. Rust symbols keep Visual Studio's own icons. In the Toolbox
    they are drawn with solid 1.5 px strokes on a transparent background, in the variant of the current
    theme, and follow theme changes.
  - **Properties window (F4)**: shows the selected element's properties, like a WinForms control's -
    `(Name)`, every component property and the layout attributes, grouped in categories, with
    descriptions, drop-downs for enum/boolean values, bold non-default values and "Reset"; `{Binding
    ...}` values are shown and edited as text. Editing a value rewrites just that attribute in the XML
    (one undo step) and the preview follows. The combo box at the top lists every element of the view
    (name and type) and selects the one you pick.
    Descriptions are short user-facing sentences, in French when Visual Studio runs in French (the
    component registry now exports `doc_fr`).
  - **Events tab (⚡)**: lists the element's events; double-click an event to create its handler
    (attribute + Rust stub in the code-behind `.rs`, which opens at the new function) or to go to an
    existing one; type a name to create a handler with that name.
  - **Opening like a form**: in a `.rsproj`, double-clicking a `.kbview` opens `main_view.kbview
    [Design]` (`[Conception]` in French); **F7** ("View Code") opens the XML in a code window and
    **Shift+F7** ("View Designer") returns to the designer, also from Solution Explorer's context menu
    and from the code-behind `.rs`. The Design/XML/Split tabs remain available in the designer.
  - **Ctrl+Z / Ctrl+Y** undo and redo designer changes while the design surface has the focus.
  The former "Kubuno Toolbox"/"Kubuno Properties" tool windows (Tools menu) remain available as
  fallbacks.
- `tools/generate-control-icons.ps1`: generates the control icons, their image manifest and review
  sheets (`C:\kubuno-build\icons-preview\`).

### Fixed

- **The designer restored with a solution (or opened before the Kubuno Views language server had
  started) had no selection sync, and could make kubuno-views-ls exit**: its first custom request was
  sent before the server's `initialize` answer. The designer now waits for the language server to be
  initialized.
- **Toolbox drops, Flow reorders and move/resize drags on the design surface were never applied**
  (their protocol lines were only logged as "unrecognised"): all design-surface messages are now
  dispatched by a single listener, and a move/resize is applied as one undo step.
- **Keys forwarded from the design surface never reached Visual Studio's accelerators** (the
  `IVsFilterKeys2` service was not obtained from the package's own service provider).

### Fixed

- **Double-clicking a `.kbview` element node in Solution Explorer (e.g. `hello (Button)`) put the
  design surface's selection on the wrong element** (its parent container) instead of the
  double-clicked one, even though the XML pane's caret landed correctly. Two independent timing
  races in DSG-8's selection sync, both confirmed live:
  - `VsTextViewSelectionAdapter` (the caret-move poller) seeded its "last known caret position"
    from the CURRENT caret position at construction time - but Solution Explorer's own navigation
    (`SymbolNavigator`) can move the caret before this adapter is even constructed (it is built at
    the end of an async chain: a laid-out XML view, a resolved language-server `JsonRpc`, a fetched
    component registry), so the very first poll tick saw "no change" and never raised the selection
    sync's `CaretMoved` event for that navigation at all. The poller now starts from an impossible
    sentinel position so its first tick always synchronizes to wherever the caret already is.
  - A `select` sent to the design surface (from a caret move or an Outline click) could reach it
    BEFORE the buffer edit it was computed against had been pushed as `setText`: `kubuno-views-ls`
    resolves an element id against its own always-current parse, but the design surface's own text
    only follows the same buffer through a 200ms-debounced push. `select` now flushes any pending
    debounced push first, so the two are never out of sync.
  Verified live (experimental instance, UI Automation): double-click/Enter on `hello (Button)` now
  shows the Button's own properties (Text/Variant/Size/Icon/Loading) in the Properties tool window,
  not the parent Stack's; double-click/Enter on a `.rs` symbol node still navigates to it correctly.
- **"Create a new project" crashed with "ce modèle a tenté de charger un assembly de composant"
  when instantiating any project template**: `Microsoft.VisualStudio.TemplateWizard.Wizard.
  CreateManagedInstance`'s own `Assembly.Load` does not resolve `Kubuno.VisualStudio.TemplateWizard`
  through the same `ProvideCodeBase` registry mechanism MEF/package loads use (confirmed correctly
  merged into the registry; still not found on this particular code path). `KubunoPackage.
  InitializeAsync` now preloads that assembly eagerly at package activation
  (`PreloadTemplateWizardAssembly`), so it is always already in the AppDomain's identity cache by
  the time any wizard can run.
- **"Open With... > Kubuno View Designer" silently fell back to the plain text editor**: the
  `[ProvideEditorLogicalView]` GUIDs registered for `KbviewEditorFactory` were mislabeled (the
  "Designer" and "TextView" comments pointed at the real `LOGVIEWID_TextView`/`LOGVIEWID_
  UserChooseView` values instead; the real `LOGVIEWID_Designer` was never registered at all).
  Verified live: a `.kbview` opened via the Designer logical view now renders the real split
  Design/XML/Split view instead of falling back to plain text.
- **`KubunoPackage` could fail to load with `Kubuno.Mcp.Bridge` unresolvable**: a latent gap (no
  `ProvideCodeBase` registration for that assembly, unlike its four siblings) that only surfaced
  once something forced eager resolution of it.
- **"Create a new project"/"Add New Item" never listed any Rust/Kubuno template**: every template
  declared `<ProjectType>Kubuno.Rust</ProjectType>`, but Visual Studio only shows a template whose
  `<ProjectType>` matches the `Language(VsTemplate)` of a registered project type (`Rust` for
  `.rsproj`). The templates now use `Rust` and have explicit template IDs. "Rust" now appears in the
  dialog's language filter, searching "rust" lists the three project templates, and a `.rsproj`'s
  *Add New Item* shows a "Rust" category with the three item templates (verified live). The
  `NewProjectTemplates`/`AddItemTemplates` registry keys added earlier were removed: they made
  the dialog show folder names without descriptions or tags.
- **F5 on a `.rsproj` reported "The Rust executable ... does not exist" after a successful build**
  when cargo's target directory comes from `build.target-dir` in a `.cargo/config.toml`. The launch
  now asks `cargo metadata` for the real target directory when the conventional path is missing.
- **Generated template files contained mojibake** (`Â§`, `â€”`) where the templates used non-ASCII
  characters; the template content is now ASCII-only.
- **`.rsproj` Property Pages showed three debug-related entries instead of one**: the standalone
  "Debug" page (`Sdk/Rules/debug.xaml`) is removed, and its command-arguments/working-directory/
  environment properties now live directly on the "Rust Debugger" flavor rule
  (`Sdk/Rules/rust_debugger.xaml`), so "Débogage"/"Debug" shows them under "Débogueur à lancer" /
  "Debugger to launch" like the JS and VSIX project systems do; the rule's `DisplayName` also
  drives the Start button's text, now "Local Rust Debugger".

### Changed

- **`docs/RSPROJ.md`'s lot 8 addendum corrected: Solution Explorer symbol expansion (`.rs`/
  `.kbview` element/item nodes under a file) is NOT confirmed working in Open Folder mode**, only
  in a `.rsproj`. Live-verified against `Z:\src\desktop\windows` opened directly as a folder: every
  `.rs`/`.kbview` file node reports no expand affordance at all (not "found the symbol query came
  back empty" - the tree-attachment itself never seems to run for an Open Folder file node), even
  though the plain file/folder tree and the (correctly absent, capability-gated) missing
  Dependencies node are both right. Root cause not yet isolated; tracked as a known limitation
  rather than shipped as an unverified fix - see that document's own "Open Folder, live-verified"
  note and `SymbolTreeProvider`'s updated doc comment for the detail.

### Added

- **"Kubuno Module" project template** (`docs/RSPROJ.md`'s lot 7 addendum): a backend module
  skeleton (Axum/Tokio, `/health` + `/internal/*` guarded by `X-Internal-Secret`, a
  `sqlx::migrate!`-driven Postgres schema, `module.toml`, `config.toml.example`, `build_kbpkg.sh`,
  `CHANGELOG.md`, `README.md`) following the Kubuno module conventions, mirroring a real module's
  layout trimmed to a minimal, buildable-on-this-machine skeleton - `cargo build`/`cargo clippy --
  -D warnings` verified clean. "Create a new project" now lists four Rust/Kubuno templates instead
  of three.
- **Crate-name sanitisation for every "Create a new project" Rust/Kubuno template**
  (`docs/RSPROJ.md`'s lot 7 addendum, "Crate-name casing, revisited again"): a new, separate
  `Kubuno.VisualStudio.TemplateWizard` assembly (net48, referencing only
  `Microsoft.VisualStudio.TemplateWizardInterface`/`EnvDTE`/`EnvDTE80`/`Microsoft.VisualStudio.
  Interop`) computes a Cargo-valid `$cratename$` from the project name (e.g. `My App 2` ->
  `my-app-2`, live-verified through the real dialog) and, for the Kubuno Module template, a
  matching `$moduleid$` for `module.toml`'s `id`/the Postgres schema name. Every project template's
  `Cargo.toml`/`.rsproj` now uses `$cratename$` in place of VS's raw `$safeprojectname$`.
- **Solution Explorer shows Rust projects like WinForms/WPF ones (`docs/RSPROJ.md`'s lot 8
  addendum)**:
  - a view's code-behind is nested under it: `main_view.rs` appears under `main_view.kbview` when
    both sit in the same folder with the same stem (the `Kubuno.Rust.Sdk` sets `DependentUpon`;
    opt out with `<EnableKbviewCodeBehindNesting>false</EnableKbviewCodeBehindNesting>`);
  - expanding a `.rs` file lists its items (structs, enums, traits, impl blocks, functions,
    methods, fields, constants, modules, macros), nested, with the Visual Studio symbol icons and
    the C#-style accessibility variants (`pub` = public, `pub(crate)` = internal, `pub(super)` =
    protected, private = lock); expanding a `.kbview` file lists its element tree with a
    control-like icon per element family. Double-click jumps to the item; for a view element it
    opens the Kubuno View Designer with that element selected. Symbols are computed only when a
    node is expanded and refresh when the file is saved;
  - a read-only **Dependencies** node under a `.rsproj` lists the crates declared in `Cargo.toml`,
    grouped as crates / dev-dependencies / build-dependencies;
  - new file icons: `.rs` (a source page with a Rust badge, like the C# file icon), `.kbview` (a
    form window, like the Windows Forms form icon) and `Cargo.toml` (Cargo's crate), with light,
    dark and high-contrast variants. `.rs`/`.kbview` get them in Open Folder too.
- **"Create a new project"/"Add New Item" templates (`docs/RSPROJ.md`'s lot 7 addendum)**: ships
  **Rust Console Application**, **Rust Library** and **Kubuno Desktop Application** (a
  `kubuno_ui`/`kubuno_controls`/`kubuno_views` window with a starter `main_view.kbview` view and
  same-stem `main_view.rs` code-behind) project templates, and **Kubuno View**, **Rust Module** and
  **Rust Integration Test** item templates, each instantiable and buildable (live-verified via
  `dte.Solution.AddFromTemplate` + the real Solution Build Manager - see the addendum for the
  registry registration this needed before the dialog would even discover them, not yet re-verified
  live in the dialog itself). Project/crate names use VS's own `$safeprojectname$` directly - see the
  lot 7 addendum for why an `IWizard`-based sanitiser and an automatic `cargo generate-lockfile` step
  were tried and reverted (a real,
  reproducible `KubunoPackage` load regression). New `ProjectTemplates/`, `ItemTemplates/`.
- **Open Folder / rust-analyzer coexistence pass (work package 6 of `docs/RSPROJ.md`) - verified,
  no code change needed for two of its three concerns**: a folder containing generated `.rsproj`/
  `.sln` still opens correctly in Open Folder mode (regression test added), and rust-analyzer still
  finds the right workspace root - and Go To Definition works - when a `.rsproj` solution is opened
  instead. The third concern (the `.kbview` Designer GUID bug above) was a real, fixed bug.
- **"Kubuno: Generate Visual Studio Projects" (work package 5 of `docs/RSPROJ.md`)**: from the
  Tools menu, or a right-click on a workspace-root `Cargo.toml` (Solution Explorer/Open Folder),
  creates one `.rsproj` per workspace member with a `[[bin]]` target plus a `.sln` listing them -
  idempotent (an existing `.rsproj` is never overwritten; an existing `.sln` is edited surgically,
  preserving solution folders/other projects/formatting). The `Kubuno.Rust.Sdk` package a generated
  project needs now ships inside the VSIX and self-registers as a local NuGet source on first load,
  so no manual SDK setup is required. New `Kubuno.VisualStudio.Core/ProjectGeneration/` (pure,
  unit-tested), `Commands/GenerateRustProjectsCommand.cs`, `Infrastructure/RustSdkFeedInstaller.cs`.
- **F5 / Ctrl+F5 on a `.rsproj` (work package 4 of `docs/RSPROJ.md`)**: set a Rust project as the
  startup project and F5 builds it (cargo, through the normal solution build) then starts
  `$(TargetPath)` under Visual Studio's native debugger - breakpoints bind, Rust locals show with
  the toolchain's natvis (e.g. `String` as `"Hello, Kubuno!"`); Ctrl+F5 runs it without the
  debugger. The executable's PATH gets the profile directory, its `deps` folder and the Rust
  standard library directory first, so `-C prefer-dynamic` builds (the Kubuno desktop apps with
  `kubuno_ui.dll` and `std-*.dll`) start without a missing-DLL error. The "Local Rust Debugger"
  entry on the "Debug" property page sets the command arguments, working directory (default: the
  `Cargo.toml` folder) and extra environment variables (one `NAME=value` per line), stored in the
  per-developer `.rsproj.user`.
- **`.rsproj` opens in Visual Studio as a real project (work package 3 of `docs/RSPROJ.md`)**:
  Rust/Cargo packages can sit in a `.sln` as CPS projects, registered like the JavaScript
  project system's `.esproj`. Solution Explorer shows the project (with its own Rust icon) and the
  crate folder's files (`target/`, `bin/`, `obj/` and dot-folders hidden); the Debug/Release
  dropdown maps to cargo's dev/release profiles; Build, Rebuild and Clean from the project context
  menu, Ctrl+Shift+B and solution builds run cargo, with rustc errors in the Error List
  (file/line/column/code); `.kbview` files open with their usual editor; a "Cargo" property page
  exposes the package/bin/manifest/target-dir/extra-args settings. New
  `src/Kubuno.VisualStudio.RustProjectSystem` (CPS exports) and `rsproj.pkgdef` (project type
  registration) ship in the VSIX.

### Changed

- **Kubuno.Rust.Sdk now builds on `Microsoft.Common.props`/`.targets`**, like
  `Microsoft.VisualStudio.JavaScript.SDK`: cargo is plugged into `CoreCompile`/`BeforeClean`/
  `Restore` instead of the SDK redefining Build/Rebuild/Clean, and the SDK now carries project
  configurations, capabilities, rule files (`Sdk/Rules/*.xaml`) and a default `None` item glob
  (all needed by Visual Studio; command-line builds behave as before).
- **A `.rsproj` now has the `x64` platform** instead of "Any CPU" (a Rust executable is native
  code; x64 maps to the `x86_64-pc-windows-msvc` host triple). Solutions listing a `.rsproj` should
  use `Debug|x64`/`Release|x64` (the sample solution does).

### Fixed

- **The `.rsproj` project node icon did not show in Solution Explorer**: Visual Studio could not
  load the assembly holding the image by name; the extension now registers code bases for it and
  for the assemblies the project system loads before the package itself.
- "Manage NuGet Packages..." on a `.rsproj`: verified that NuGet itself rejects the project (it
  declares none of the capabilities NuGet requires); the entry can only appear until the NuGet
  package has loaded, a Visual Studio behavior shared by every non-NuGet project type.

- **Rebuild/Clean of one configuration no longer deletes the other's build**: the SDK's clean step
  ran a bare `cargo clean` for Debug, which wipes the whole target directory (Release artifacts
  included, and other packages' with a shared `CARGO_TARGET_DIR`); it now runs
  `cargo clean --profile dev`.
- A nullable-analysis warning (CS8604) in `KubunoPackage`'s workspace-settings step.

### Added (work packages 1-2)

- **`.rsproj`: a real MSBuild project type for Cargo packages, work packages 1-2 of
  `docs/RSPROJ.md`** (`<Project Sdk="Kubuno.Rust.Sdk/1.0.0">` in a normal `.sln`, no CPS/VSIX code
  yet - see the README's new "Building Rust with MSBuild (`.rsproj`)" section):
  - **`sdk/Kubuno.Rust.Sdk/`** (work package 1): a pure MSBuild SDK (`Sdk/Sdk.props`,
    `Sdk/Sdk.targets`, no CPS dependency, packaged as a NuGet `MSBuildSdk` package like
    `Microsoft.VisualStudio.JavaScript.SDK`). Maps Build/Rebuild/Clean/Run/Test to cargo;
    `$(Configuration)` (Debug/Release, or any other name) to a cargo profile; `<CargoTargetDir>` to
    `CARGO_TARGET_DIR` (env var fallback, never overriding an explicit value); `<CargoManifestPath>`
    (honoured everywhere as `--manifest-path`) defaulting to the sibling `Cargo.toml`; and exposes
    `$(TargetPath)` as the built executable's path (authoritative once `CargoBuild` has run, a
    `Kubuno.Launch.ExecutableResolver`-matching path-convention fallback otherwise).
  - **`src/Kubuno.Cargo.MSBuild.Tasks/`** (work package 2): `CargoBuild`/`CargoTest`/`CargoFetch`
    MSBuild tasks, reusing `Kubuno.Cargo`'s existing `CargoCommand`/`CargoMessageParser`/
    `ProcessRunner` to run `cargo … --message-format=json-diagnostic-rendered-ansi` and turn each
    diagnostic into a `Log.LogError`/`LogWarning` call with file/line/column/code (a clickable
    Error List entry) plus the full rustc-rendered text as a plain message. Multi-targeted
    `net472`/`net10.0`, selected by `$(MSBuildRuntimeType)`, so the same package loads under both
    the classic, .NET Framework MSBuild.exe Visual Studio ships and `dotnet build`'s own MSBuild -
    getting this to load in process under `dotnet build` surfaced that this workspace's own mapped
    drive (`Z:`) cannot be the SDK's resolution source (confirmed live with a minimal repro:
    `MSB4061: Type must be a type provided by the runtime.`, the same class of loader-from-remote-
    source restriction already documented for MSTest), which is why the SDK is a real NuGet package
    rather than a bare path import.
  - `Kubuno.Cargo.Commands.CargoCommand`/`CargoCommandKind` gained a `Fetch` case (`cargo fetch`,
    the SDK's NuGet-free `Restore` target).
  - New `samples/hello-rust/hello-rust.rsproj` + `samples/hello-rust.sln` + `samples/NuGet.config`
    (a local feed for the SDK) verify the whole thing end to end: `msbuild
    samples\hello-rust\hello-rust.rsproj -t:Build -p:Configuration=Debug|Release` and `-t:Clean`,
    `dotnet build` on the same `.rsproj`, a no-op rebuild correctly skipping `CoreCompile`, and a
    deliberately introduced compile error surfacing as a clickable, correctly-positioned
    `src\main.rs(4,20): error E0425` Error List entry - all verified live from the command line
    (both MSBuild.exe and `dotnet build`).
  - New `tests/Kubuno.Cargo.MSBuild.Tasks.Tests` (7 cases) pins `CargoDiagnosticLogging`'s mapping
    from a parsed diagnostic to `Log.LogError`/`LogWarning`/`LogMessage`, against the same real
    captured `cargo build` fixtures `Kubuno.Cargo.Tests` already parses.
  - `Kubuno.Cargo.MSBuild.Tasks`, `Kubuno.Rust.Sdk` and their tests project are now part of
    `Kubuno.VisualStudio.sln`.
  - Not done yet (later `docs/RSPROJ.md` work packages, out of this change's scope): the CPS
    project factory/capabilities/file glob in the VSIX (work package 3), the Debug launch provider
    and Debug/Build rule pages (work package 4), the "Generate Visual Studio projects" command
    (work package 5), and the Open Folder/rust-analyzer coexistence pass (work package 6).

### Fixed

- **F5/Ctrl+F5 failed with a "std-*.dll est introuvable" dialog for every generated launch
  target.** `launch.vs.json`'s `"env"` was written as an array of `{ "name", "value" }` objects
  (the C++ Linux `environment` shape); VS's native debug engine silently ignores that shape for a
  `"type": "default"` configuration, so none of the PATH entries needed to find `kubuno_ui.dll`'s
  own `std-*.dll` dependency were ever applied. `LaunchVsJsonWriter` now emits `env` as a plain
  `{ "VAR": "value" }` object, and `RustLaunchTargetsGenerator` appends `${env.PATH}` (resolved by
  VS at launch time) instead of a literal snapshot of devenv's own PATH. Verified live: Ctrl+F5
  launches the shell with no dialog, and F5 stops at a breakpoint in `main` with a working Rust
  call stack.
- **No default "Select Startup Item" was ever pre-selected when the opened folder's own
  `Cargo.toml` is a virtual `[workspace]`-only manifest** (no `[package]` of its own - the common
  case for a multi-crate desktop app opened at its workspace root): `StartupItemSelector` only knew
  how to pick a default `[[bin]]` for a single, already-identified package. Added
  `SelectDefaultBinTargetForWorkspace` (workspace `default-members`, then a `shell/`-directory
  convention, then "the first bin") and wired it into `RustLaunchTargetsGenerator.GenerateAsync`
  for exactly this case. See "Known limitations" for the live-refresh gap this still has.
- **A vendored/reference non-Rust project tree (e.g. `tools/winforms-ref/`'s WinForms parity
  projects) made VS's own native project-file discovery take over the "Select Startup Item"
  dropdown entirely**, hiding every Cargo target. Added `NonRustProjectExclusionScanner` (pure
  logic, unit-tested) and `KubunoPackage.EnsureWorkspaceSettingsExcludeNonRustProjectsAsync`, which
  writes a `VSWorkspaceSettings.json` `ExcludedItems` list for the offending directories - only
  when that file does not already exist, never overriding a developer's own choices there.
- **The Kubuno Toolbox tool window showed a blank scroll area** (no visual sign of anything wrong)
  before a `.kbview` file was active/`kubuno-views-ls` had anything to report. It now shows a
  placeholder ("Open a .kbview file to see its components here."). Also hardened
  `DesignerToolWindowCommands.ShowToolWindow` (the one entry point in this VSIX with no try/catch
  around a tool-window's construction/`Show()`) to log a failure to the Kubuno pane instead of
  letting it escape as VS's generic "this might be caused by an extension" error info bar.
- **Stale `Z:\projects\kubuno\...` paths updated to `Z:\src\...`** in comments/docs (`CLAUDE.md`,
  `docs/MCP.md`, `docs/XML_VIEWS.md`, `src/Kubuno.VisualStudio.Views/INTEGRATION.md`,
  `tests/Kubuno.VisualStudio.Tests/App.config`, `spikes/HwndHostSpike/Program.cs`) and in
  `src/Kubuno.VisualStudio/Kubuno.VisualStudio.csproj`'s build-instruction comments/error messages,
  after the workspace moved from `~/projects/kubuno/` to `~/src/` (the csproj's actual
  `KubunoViewsLsExePath`/`KubunoViewsSurfaceExePath` defaults already pointed at the machine-local
  `C:\kubuno-build\...` and did not need changing).
- **"Open With... > Kubuno View Designer" no longer fails or crashes Visual Studio on a `.kbview`
  file that was not already open.** The designer created its text buffer and its embedded XML code
  window with `new VsTextBufferClass()`/`new VsCodeWindowClass()`, which fail inside Visual Studio
  with "class not registered" (`REGDB_E_CLASSNOTREG`); the error escaped the editor factory and, depending
  on how the open was requested, Visual Studio either fell back silently or crashed in `msenv.dll`.
  Both objects now come from the editor adapters service (`IVsEditorAdaptersFactoryService`), the
  editor factory never lets an exception escape into the shell (it is logged to the Kubuno output
  pane instead), and the Design half now waits for the document to finish loading before pushing its
  text and wiring the edit/selection pipeline (previously a newly opened document got no edit
  pipeline at all). See `src/Kubuno.VisualStudio.Designer/INTEGRATION.md` §10.
- **Clicking an element in the designer's Design half now selects it.** The surface dropped any
  click released before its next frame (see the desktop repo's `view_embed` fix); it no longer shows
  the spike's probe line and Save/Menu test buttons either (`spikes/HwndHostSpike` now passes
  `--debug-probe` to keep them for its own checks).

- **View menu commands moved to the Tools menu.** "Kubuno Toolbox"/"Kubuno Properties"/"Kubuno View
  Outline" were originally placed under View > Other Windows
  (`vsshlids.h`'s `IDG_VS_WNDO_OTRWNDWS1`); live testing found they never got a resolved canonical
  command name there and did not appear in the real menu (confirmed by direct visual check), unlike
  the pre-existing Tools > Kubuno: Debug Rust Test at Cursor command. Moved into that same,
  already-working `KubunoToolsMenuGroup` (Tools menu) instead - see
  `src/Kubuno.VisualStudio.Designer/INTEGRATION.md` §10 for the detail.

- The design surface's stdin could silently lose the FIRST protocol line ever sent to a
  freshly-launched process (typically `setText`, leaving the surface with no document loaded) -
  some `.NET` `StreamWriter` configurations emit a UTF-8 byte-order mark on a stream's very first
  write only, which the Rust side correctly (but unhelpfully) treated as an unrecognised line.
  Found and fixed during DSG-9's own visual check. `ProcessStartInfo.StandardInputEncoding` does
  not exist on .NET Framework 4.8 (only added in .NET Core 3.0+), so `RustDesignSurfaceHost
  .Protocol.cs`'s `SendLine` now writes UTF-8-without-BOM bytes directly to
  `Process.StandardInput.BaseStream` instead of going through `StreamWriter.WriteLine`;
  `RustDesignSurfaceHost.cs`'s own `ProcessStartInfo` now also sets `StandardOutputEncoding`
  explicitly (that property DOES exist on net48), symmetrically. New regression test pinning the
  encoding configuration (`RustDesignSurfaceHostDragDropTests.cs`).

- `RustDesignSurfaceHost.DragDrop.cs`'s own stdout listener was only ever wired from its
  `Notify*` (toolbox-drag) methods, so a pane whose first gesture was a mouse drag (not a toolbox
  drop) never attached it, and `EditRequestsReceived`/`DropTargetChanged` silently never fired
  (the line was logged as "unrecognised" by `RustDesignSurfaceHost.Protocol.cs`'s own, unrelated
  listener instead - easy to mistake for a parsing bug, but the parsing was never reached). Found
  during DSG-9's own visual check. Now ALSO wired via a static `EventManager.RegisterClassHandler`
  on `FrameworkElement.LoadedEvent` (subscribes to `ChildReady`, which fires once per start/restart
  with a live process), independent of any toolbox gesture ever happening.

- `kubuno_views::design::DesignController::press` now selects AND arms a Move/Resize/Reorder drag
  in the SAME press (a new `DRAG_THRESHOLD` gates when an armed session actually starts
  moving/reordering, so a plain click still just selects) - the previous two-press design (select,
  then a second press to arm) read, live, as "dragging an unselected element does nothing but
  select it".

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
  `Process.OutputDataReceived` listener (wired via a static `Loaded` class handler - see the
  "Fixed" entry above for why an instance field initializer/the `Notify*` methods alone were not
  enough) raises the new `EditRequestsReceived`/
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
