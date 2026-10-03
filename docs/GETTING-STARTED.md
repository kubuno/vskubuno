# Getting started with Kubuno for Visual Studio

This is a practical, step-by-step guide for a developer installing the extension for the first
time. For the full feature list and implementation detail, see `README.md`; for the `.rsproj`
project type design, `docs/RSPROJ.md`; for the `.kbview` designer, `docs/DESIGNER.md`.

## 1. Prerequisites

- **Visual Studio 2026 (18.x)** or **Visual Studio 2022 (17.x)**, any SKU. Two workloads:
  - **.NET desktop development** - the extension itself is a classic .NET Framework 4.8 VSIX and
    its generated project templates need the .NET Framework 4.8 targeting pack this workload
    installs.
  - **Desktop development with C++** - needed for the native (MSVC/PDB) debug engine the extension
    uses to debug Rust binaries, and for `link.exe`/the MSVC toolchain a Rust `-msvc` target needs
    at build time.
  - The **"Visual Studio extension development"** workload is *not* required: the VS SDK the
    extension itself is built with comes from NuGet, not from VS Setup.
- **Rust, MSVC toolchain**: install [rustup](https://rustup.rs/) if you have not already, then
  ```powershell
  rustup default stable-x86_64-pc-windows-msvc
  rustup component add rust-analyzer rust-src
  ```
  The extension looks for `rust-analyzer.exe` in this order: Tools > Options > Kubuno > Rust "Path
  override", then `rustup which rust-analyzer`, then `%USERPROFILE%\.cargo\bin\rust-analyzer.exe`,
  then PATH. If none of those resolve, an info bar tells you exactly what to run
  (`rustup component add rust-analyzer`) - it never fails silently.
- **A local, non-mapped drive for your Cargo `CARGO_TARGET_DIR`, and ideally for your checkouts
  too.** Visual Studio's toolchain (MSBuild, the .NET Framework loader, the NuGet client) has
  several documented rough edges on a network drive - see "Network drives" below - so if your
  checkout has to live on one, set `CARGO_TARGET_DIR` to a local path (e.g.
  `C:\kubuno-build\<project>-target`) rather than letting cargo default to `<checkout>\target`.
- **Network drives: use SMB, not NFS.** A Cargo/rustc build on an NFS-mounted checkout on Windows
  is unreliable (locking semantics and case handling differ enough from a native filesystem to
  cause spurious build failures); a Windows SMB share (a mapped drive letter, e.g. `Z:`) works,
  though it has its own quirks (below) - it is the supported/tested configuration, NFS is not.

## 2. Install the VSIX

Double-click the built `Kubuno.VisualStudio.vsix` (or run `VSIXInstaller.exe <path>` from a
terminal) and follow the installer. It installs into your normal Visual Studio hive - no
`/rootsuffix` needed for everyday use (that flag is only for testing the extension itself in an
*experimental* instance, see the README's "Install / debug in an experimental instance").

After installation, **start Visual Studio once and close it again before you rely on File > New
Project.** The "Create a new project" and "Add New Item" dialogs read from a template cache VS
rebuilds the first time it starts after a VSIX that ships templates is installed or updated; the
very first start after installing (or upgrading) this extension is what populates that cache; a
project template that seems to be missing right after installing is almost always this, not a
packaging problem - restart Visual Studio once and search again.

## 3. Create a Rust/Kubuno project

File > New > Project, then filter by **Language = Rust** (it appears between Python and
TypeScript in the language dropdown) or just search "rust". Four project templates are offered:

- **Rust Console Application** - a plain `cargo new --bin` equivalent, wired to build/debug through
  Visual Studio.
- **Rust Library** - a plain `cargo new --lib` equivalent.
- **Kubuno Desktop Application** - a native Kubuno window written like a Windows Forms project, in
  three short files (`docs/PROGRAMMING-MODEL.md`): `main.rs` (`kubuno::Application::run(MainView::new())`,
  the `Program.cs`), `main_view.kbview` (the window's design) and `main_view.rs` (`MainView`, the `Form1`:
  `#[kubuno::view("main_view.kbview")]` gives it a field per control - `self.status`, `self.hello` - and
  its event handlers are plain methods, `fn main_view_load(&mut self, …)`). Its generated `Cargo.toml` has
  one dependency, `kubuno`, a **path dependency pointing at a `desktop/windows` checkout on your own
  machine**. **Single prerequisite**: a checkout of `github.com/kubuno/desktop`, and - unless it
  is at the default `Z:\src\desktop\windows` - the `KUBUNO_DESKTOP_SRC` environment variable set
  to its `windows` folder (or to the repository root) **before** you create the project, e.g.
  `setx KUBUNO_DESKTOP_SRC D:\src\desktop\windows`, then restart Visual Studio. The path is written
  into the new project once, at creation (`Cargo.toml`'s `kubuno` path and the `.rsproj`'s
  `<KubunoDesktopSrc>`, to be changed together if you move the checkout later); a build with that
  folder missing stops with error `KUBUNO0001` saying exactly this. The project then builds and
  runs with F5 as created, with no other step: its `.rsproj` gives it its own cargo target
  directory, and the program links `kubuno_ui` and Rust's `std` statically (docs/DESIGNER.md
  section 16): the exe runs on its own, with no DLL beside it.
- **Kubuno Module** - an Axum/Tokio backend module skeleton (`/health` + `/internal/*` guarded by
  `X-Internal-Secret`, a `sqlx::migrate!`-driven Postgres schema, `module.toml`,
  `build_kbpkg.sh`), following the platform's own module conventions.

Whatever name you type is turned into a Cargo-valid crate name automatically (lower-cased,
invalid characters folded to `-`, forced to start with a letter - `My App 2` becomes `my-app-2`);
for a Kubuno Module, a matching `module.toml` id (`my_app_2`) is derived the same way. You do not
need to sanitize the project name yourself.

*Add New Item* on a created project adds three more templates: **Kubuno View** (another form: a new
`.kbview` plus its same-stem `.rs`, a `#[kubuno::view]` struct you open with `SettingsView::new().show()` or
`.show_dialog(self)` from a handler), **Rust Module**, and **Rust Integration Test**.

**Forms and controls in code.** A window can also be built entirely in code, the Windows Forms way, and
mixed with a designed view (controls added to `self.controls()`):

```rust
let form = Form::new().text("Hello").client_size(800.0, 450.0);
let ok = Button::new().text("OK").location(10.0, 10.0).size(75.0, 23.0).anchor(Anchor::TOP | Anchor::RIGHT);
ok.click().subscribe(|_sender, _e| { MessageBox::show("Clicked"); });
form.controls().add(&ok);
kubuno::Application::run(form)
```

`docs/PROGRAMMING-MODEL.md` describes the whole model (dialogs and `DialogResult`, `MessageBox`, several
windows, bindings, the generated members).

## 4. The `.rsproj` model

A generated project is a `.rsproj` - a real MSBuild/CPS project type, not a wrapper script. The one
thing to keep in mind throughout:

> **`Cargo.toml` stays the single source of truth.** A `.rsproj` never lists `.rs` files, never
> repeats `[dependencies]`, and never repeats target definitions. It names one Cargo package
> (`<CargoPackage>`) and defers everything else to `cargo metadata`. Solution Explorer's file tree,
> IntelliSense, and the actual compiler are Cargo/rust-analyzer's, not the `.rsproj`'s.

This is why editing `Cargo.toml` by hand (adding a dependency, adding a `[[bin]]`) is not only
safe but the *normal* way to work - the `.rsproj` picks it up on the next build (rebuild once if
Solution Explorer or the Debug target list looks stale, see "Known limitations" in `README.md`).

Every `.rsproj` has a single platform, **x64** (the `x86_64-pc-windows-msvc` host triple); a
solution containing one uses `Debug|x64`/`Release|x64` configurations, matching cargo's own
`dev`/`release` profiles (any other `$(Configuration)` name maps to a cargo profile of the same
name too).

### Adding items, references and dependencies

Right-click a `.rsproj` project node and open **"Ajouter"** ("Add"): alongside the standard "Nouvel
élément.../Élément existant.../Nouveau dossier", this extension adds the same kind of direct entries
WinForms projects have for "Formulaire (Windows Forms)...":

- **Vue Kubuno..., Module Rust..., Test d'intégration..., Exemple..., Binaire...** - each prompts for
  a name and creates the matching file(s) (`.kbview` + code-behind, `src/*.rs`, `tests/*.rs`,
  `examples/*.rs`, `src/bin/*.rs`).
- **Référence de projet...** - a checkbox list of every other `.rsproj` in the solution (like
  Reference Manager); checking/unchecking one adds or removes a path dependency with `cargo add
  --path`/`cargo remove`.
- **Dépendance Cargo (crate)...** - crate name, optional version/features, and Normal/Dev/Build,
  applied with `cargo add`. The same entry (plus "Supprimer" on one crate) is also on the
  Dependencies node's own right-click menu.

All of these run real `cargo` commands - `Cargo.toml` is never hand-edited by the extension itself,
matching the "single source of truth" rule above - and cargo's own output goes to the **"Kubuno"**
Output pane. The Dependencies node picks up the change on its own within about half a second (it
watches `Cargo.toml`), no manual refresh needed.

### For an existing Cargo workspace

If you already have a multi-crate Cargo workspace (not created from the templates above), generate
`.rsproj`/`.sln` for it instead of hand-writing one: **Tools menu > "Kubuno: Generate Visual Studio
Projects"**, or right-click the workspace-root `Cargo.toml` in Solution Explorer/Open Folder and
pick the same command. This runs `cargo metadata` and creates one `.rsproj` next to each workspace
member, plus a solution at the workspace root listing all of them: a new one is a `.slnx` with the
executables at its root and the library-only members under a `Libraries` folder.

- **Idempotent**: re-running it never overwrites an existing `.rsproj`; an existing `.sln` or `.slnx`
  (exactly one, at the workspace root - rename the generated one as you like) is edited surgically
  (only the missing project entries are inserted - your own solution folders, other projects and
  formatting are preserved).
- A library-only member is only added to a `.slnx`; a classic `.sln` keeps the executables only.
- A workspace in which a member is a Rust `dylib` gets `<CargoBuildScope>Workspace</CargoBuildScope>`
  in every project (the Kubuno desktop workspace, which has none since 2026-10-03, keeps it by choice: see
  "Working on the Kubuno desktop apps" below).
- The MSBuild SDK a generated `.rsproj` needs (`Kubuno.Rust.Sdk`) ships inside the extension and
  registers itself as a local NuGet source on first package load - no manual SDK setup, no
  internet access required for that step.

### Working on the Kubuno desktop apps

The desktop applications (`desktop/windows`: the shell, Chat, Documents, Drive and the shared crates -
`kubuno-ui`, `kubuno-controls`, `kubuno-views`, `kubuno-data`, `kubuno-print`, `kubuno`...) form one Cargo
workspace. To work on them in Visual Studio:

1. **Once per machine**: when the checkout is on a network share (`Z:`), give cargo a local target
   directory - `setx CARGO_TARGET_DIR C:\kubuno-build\desktop-target` - and start Visual Studio *after*
   that (it passes its own environment to every build and to F5). Keep `%USERPROFILE%\.cargo\bin` on
   PATH.
2. **Once per checkout**: open `desktop\windows\Cargo.toml` (File > Open > File, or open the folder),
   then run **Tools > Kubuno: Generate Visual Studio Projects**. It writes one `.rsproj` next to each
   crate's `Cargo.toml`, a `windows.slnx` at the workspace root (rename it, e.g. `Kubuno.Desktop.slnx`),
   and the solution's SDK feed (`NuGet.Config`, `.kubuno\sdk-feed`). A later run only adds what is
   missing.
3. **Open the `.slnx`**. Solution Explorer shows the six programs at the top - `kubuno-desktop` (the
   shell), `kubuno-chat`, `kubuno-documents`, `drive-app` (its executable is `drive.exe`),
   `kubuno-views-ls`, `kubuno-data-tool` - and the libraries and procedural macros under **Libraries**,
   each project showing exactly its own crate's files.
4. **Build** (Ctrl+Shift+B). Every program links `kubuno-ui` (an ordinary rlib) and Rust's `std`
   statically (docs/DESIGNER.md section 16). The programs share most of their crates, so every project of
   this workspace builds **the whole workspace** (`<CargoBuildScope>Workspace</CargoBuildScope>`, kept in
   the generated `.rsproj` files): one `cargo build --workspace --keep-going` per solution build or F5,
   whatever the number of projects, and each shared crate compiled once, with one feature set.
   Each error and warning is listed once, under the project that owns the file; a program that did
   build is still built when another crate has an error.
5. **Run and debug**: right-click a program > **Set as Startup Project**, put a breakpoint, press **F5**.
   The exe is self-contained (the framework's symbols are in its own PDB) - nothing to copy, and it can be
   started from anywhere outside Visual Studio too. Ctrl+F5 runs without the debugger.
6. **Tests**: Test Explorer lists the whole workspace's `#[test]`s after a build. The first discovery
   builds all the tests (a few minutes, and several gigabytes).

Good to know:

- **Close the running programs before building**: a program that is running keeps its own exe open, and
  the linker cannot replace it (`LNK1104`).
- **Rebuild** cleans the workspace's profile once (not once per project) and rebuilds everything.
- **Open Folder keeps working** on the same folder (the `.rsproj`/`.slnx` files are ignored there); use
  it for a quick look, the solution for F5, breakpoints and tests.
- **rust-analyzer** works on the share; the "Kubuno" Output pane may name a member's folder as its
  workspace root - it still loads the whole workspace.
- The projects use `Kubuno.Rust.Sdk/1.1.1`. A solution generated earlier (1.0.0) keeps building each
  package on its own: regenerate its `.rsproj` files (delete them and run the command again) to get the
  workspace build.

### Working on Kubuno Core Web (the web server) and its modules

The core (`core`) and the web modules (`drive`, `calendar`, `mail`...) are Cargo packages plus a React/TypeScript
frontend. `docs/WEB.md` has the details; in short:

1. **Once per machine**: a local cargo target directory when the checkouts are on the share
   (`setx CARGO_TARGET_DIR C:\kubuno-build\web-target`), and the **development database** the dev core will use -
   a separate database on the team's PostgreSQL server, never the live one (the core migrates its database at
   startup), reached through the SSH tunnel Visual Studio opens to 192.168.1.220 (a dedicated key, authorised on
   the host): `setx KUBUNO_DEV_DATABASE_URL "postgres://kubuno:<password>@localhost:55432/kubuno_dev"`. Restart
   Visual Studio afterwards. A database whose name has no `dev`/`test`/`local`... word is refused. The host, user
   and key are in **Tools > Options > Kubuno > Remote Linux host**.
2. **Once per checkout**: open a file of the repository (or its folder) and run **Tools > Kubuno: Generate Web
   Solution**. It writes the `.rsproj` files, `frontend\<name>.esproj`, `Kubuno.Core.Web.slnx` (or `Kubuno.<Module>.slnx`)
   and its launch profiles. **Tools > Kubuno Core Web: Generate Multi-Repository Solution...** puts the core and the
   modules you choose in one `Kubuno.Web.slnx` next to them.
3. **Build** (Ctrl+Shift+B): cargo for the Rust projects, `npm run build` for the frontends. A frontend whose
   `node_modules` was installed by the Linux server builds as is: the extension never reinstalls it and brings the
   Windows native packages from a local overlay.
4. **F5**: on `kubuno-core`, a development core on `http://localhost:8080/` (data under
   `%LOCALAPPDATA%\Kubuno\dev-core`); the "Core + Vite dev server" launch profile adds Vite and Edge with the script
   debugger. On a module, the module is deployed into that dev core, the core starts, the debugger attaches to the
   module process and the browser opens on the module.
5. **New module**: Create a new project > **Kubuno Core Web Module**.

### Building, F5, Ctrl+F5

Build/Rebuild/Clean work exactly like any other project type: the project context menu,
Ctrl+Shift+B, or a solution build - all of it runs cargo underneath and turns `rustc`'s diagnostics
into clickable **Error List** entries (file/line/column/code), plus the full rustc-rendered text
(source snippet, carets, notes) in the Build Output pane.

Set a Rust project as the **startup project** ("Set as Startup Project" in Solution Explorer) and
press **F5**: Visual Studio builds it (build-before-F5, for free, because a `.rsproj` genuinely
participates in the Solution Build Manager) then starts the built executable under the **native**
debugger - breakpoints bind, and Rust locals show through the toolchain's own natvis (e.g. a
`String` renders as its actual text, not a raw pointer/length pair). **Ctrl+F5** runs it without
the debugger. Command-line arguments, working directory and extra environment variables are set on
the **"Débogage"/"Debug" property page** ("Local Rust Debugger" entry), stored per-developer in
`<project>.rsproj.user` - never committed by default.

If you start Visual Studio itself from a shortcut/Start Menu tile rather than a terminal that
already has `cargo` on PATH, make sure `cargo`'s directory (normally
`%USERPROFILE%\.cargo\bin`) is on your **user or machine PATH environment variable** - Visual
Studio inherits the PATH of the process that launched it, and a build that can't find `cargo` fails
immediately and clearly (not what "missing DLL" below is about).

### Debugging like C#

Breakpoints (conditional, hit count, tracepoints, function and data breakpoints), F10/F11/Shift+F11, Locals/Watch/
DataTips and Attach to Process work as for C#, with Rust-aware additions the extension installs by itself
(`docs/DEBUGGING.md` has the full matrix and the limits):

- a **panic stops the debugger on the line that panicked**, with an exception helper reading "Rust panic:
  *message*" (Exception Settings > C++ Exceptions > ` ?? ::st_panic`, checked by default); a Kubuno application's
  crash window only opens if you continue;
- **readable values**: `Some("two")`, `Ok(42)`, `Vec`/`HashMap` contents, `Mutex` values, and the Kubuno types
  (`Sender`, `MouseEventArgs`, `Value`, `Row`, `Color`, `Rect`...);
- **F11 steps over the standard library and the Kubuno framework** but still stops in your closures and handlers;
  the Call Stack collapses them into `[External Code]` (Tools > Options > Kubuno > Debugging to keep the framework
  visible);
- in Watch/conditions, `self` is a pointer: write **`self->count`**, not `self.count`;
- `kubuno_views::debug_break()` and `result.break_on_err()?` are the `Debugger.Break()` of Rust.

### Console window

A **Kubuno Desktop Application** opens **no console window** - in Debug too, like a Windows Forms
application (`OutputType=WinExe`): its `src/main.rs` starts with `#![windows_subsystem = "windows"]`.
Its diagnostics go where a .NET developer expects them:

- **Log with `tracing`** (`tracing::info!("saved {path}")`, `tracing::debug!(...)`): under F5 the lines
  appear in Visual Studio's **Output** window (*Show output from: Debug*); started without the debugger
  (Ctrl+F5, Explorer), they go to `%LOCALAPPDATA%\Kubuno\logs\<exe>.log` (rotated at 1 MiB, the previous
  file kept as `<exe>.1.log`). The level is `debug` in Debug builds and `info` in Release; set
  `KUBUNO_LOG=trace|debug|info|warn|error` (Debug property page, environment) to change it. `log` crate
  records are routed the same way.
- **`println!`/`eprintln!`** still work and end up in the same place (when the program has no console of
  its own), but `tracing` is the better habit: levels, and one line per event.
- **A panic** of the UI thread is written there with its location and backtrace, and the application
  shows a Kubuno crash window (its name, the error, *Show details* with the backtrace, *Open log*, *Copy*,
  *Close*) before closing - never a silent exit. A panic on a background thread is only logged.

The Kubuno host installs this when its window opens (`kubuno_controls::host::diagnostics`; opt out with
`HostOptions::diagnostics = false`, or call `diagnostics::disable()` first). An application with its own
window loop calls `kubuno_ui::diagnostics::install(&kubuno_ui::diagnostics::exe_name(), true)` at the start
of `main`.

**Want a console anyway** (a tool that prints, or while porting old code)? Change the first line of
`main.rs` to `#![windows_subsystem = "console"]` (or remove it): the application then opens a console
window next to its own, and `println!` writes there. A **Rust Console Application** and a **Kubuno
Module** (a server process) keep their console, as they should. An older Kubuno desktop project gets the
new behavior by adding `#![windows_subsystem = "windows"]` as the first line of its `src/main.rs`.

## 5. Test Explorer

Cargo tests (`#[test]` functions) show up in Visual Studio's own **Test Explorer** automatically
for both Open Folder and `.rsproj`/solution mode - no extra setup, no attribute or naming
convention to follow beyond `#[test]` itself. Run/debug a test the normal way (right-click in Test
Explorer, or the CodeLens-style run/debug affordance if your Visual Studio shows one for other
languages). For a single test you want to step through, the **Tools > "Kubuno: Debug Rust Test at
Cursor"** command is faster: put the caret in or just before the `#[test]` function in the editor
and run it - it builds only that test binary and launches it under the native debugger with
`--exact --nocapture --test-threads=1`, without discovering or building every other test first.

## 6. The view designer (`.kbview` and `.kbcontrol`)

A `.kbview` file is a declarative view (see `docs/XML_VIEWS.md` for the format itself) with a
same-stem `.rs` code-behind (`settings.kbview` / `settings.rs`) - the Rust analogue of a WPF/WinForms
`.xaml`/`.xaml.cs` pair. Double-clicking one in a `.rsproj` opens the split **Design | XML**
designer (captioned `main_view.kbview [Design]`, `[Conception]` in a French Visual Studio) with
Visual Studio's own tool windows, the same way the WinForms designer works:

- **Toolbox**: while a `.kbview` designer is the active document, the Toolbox lists every Kubuno
  component in tabs per family ("Contrôles communs", "Affichage", "Choix", "Texte", "Conteneurs",
  "Données" in French; the English names in an English Visual Studio), each with its own icon.
  They are hidden for every other kind of document. **Drag** a component onto the design surface to
  insert it where you drop it; **double-click** one to add it to the currently selected container.
- **Properties window (F4)**: shows the selected element's properties - `(Name)`, every component
  property, and its layout attributes (`Dock`/`Anchor`/`X`/`Y`/`Width`/`Height`) - grouped in
  categories, with descriptions (in French when Visual Studio runs in French), dropdowns for
  enum/boolean values, non-default values in bold, and a "Reset" action. A `{Binding ...}`
  expression can be typed directly as a property value. Editing a value rewrites just that one XML
  attribute (one undo step) and the preview updates immediately. The combo box at the top of the
  Properties window lists every element in the view - pick one to select it, the same as a WinForms
  form's component tray.
- **Events tab (the ⚡ icon at the top of the Properties window)**: lists the selected element's
  events. Double-click an event with no handler yet to generate one (adds the `On*="..."` XML
  attribute and a matching Rust function stub in the code-behind, then opens the code-behind at
  that new function); double-click an already-bound event to jump straight to its existing handler;
  or type a name into an unbound row to create a handler with that specific name. In a form class
  (`#[kubuno::view]`, the templates' `main_view.rs`) the handler is a method of the struct named the
  Windows Forms way - double-clicking the `hello` button writes `OnClick="hello_click"` and
  `fn hello_click(&mut self, _sender: &Button, _e: &MouseEventArgs)`.
- **F7** ("View Code") opens the view's code, its same-stem `.rs` file (`main_view.rs`), like Windows
  Forms opening `Form1.cs` (a view without one: its raw XML in a normal code window); **Shift+F7** ("View
  Designer") switches back, from the XML or from the `.rs` file - also available from Solution Explorer's
  right-click menu. The Design/XML/Split tabs inside the designer itself stay available too,
  if you want both views open side by side.
- **Ctrl+Z / Ctrl+Y** undo and redo designer edits (drags, drops, property edits) the same way they
  undo a text edit, while the design surface has focus.
- **Drag-to-move/resize**, **Flow reordering** (dragging a child of a `<Stack>` up or down) and
  toolbox drops all apply as a single undo step each.
- Solution Explorer shows a `.kbview`'s **element tree** when you expand its node (like expanding a
  `.cs` file shows its types/members), and its code-behind `.rs` is nested underneath it, exactly
  like `Form1.Designer.cs` nests under `Form1.cs`. A `.rs` file's own node expands into its structs,
  enums, traits, functions, etc., with the same accessibility icons Visual Studio uses for C#
  (`pub` = public, `pub(crate)` = internal, `pub(super)` = protected, private = a lock overlay).
  This works inside a `.rsproj`; it does **not** currently work when the same folder is opened in
  plain Open Folder mode (see `README.md`'s "Known limitations" - the file tree itself is correct
  there, only the expand-to-symbols affordance is missing).

**Views work like Windows Forms forms.** A new Kubuno Desktop Application's `main_view.kbview`, and
every view added with *Add > New Item > Kubuno View*, has a `<Panel>` root designed at
`DesignWidth` x `DesignHeight` (800 x 450, the size of a new WinForms form): each control has its
own `X`/`Y`/`Width`/`Height`, and its **Anchor** (Top, Left by default) says which edges of the
window it stays attached to - the starter's Status field is anchored Top, Left, Right (it stretches
with the window) and its button Top, Right. In the Properties window, `Anchor` and `Dock` open the
Windows Forms designer's own pickers (the four bars around a box for Anchor), and an absent `Anchor`
shows `Top, Left` in normal (non-bold) type, like WinForms. A control dropped from the Toolbox onto
a Panel lands at the drop point with a default size and `Anchor="Top, Left"`. Resizing the design
canvas (its bottom/right handles) moves anchored controls exactly as the running application does:
the window opens with the view's design size as its client area and relays out the controls on
every resize. (`Anchor`/`Dock` only apply inside a `<Panel>`; inside a `<Stack>` or a `<Card>` the
two rows are greyed out, like a control inside a WinForms FlowLayoutPanel.)

The former **"Kubuno Toolbox"** / **"Kubuno Properties"** fallback tool windows were removed: use
Visual Studio's own Toolbox (View > Toolbox) and Properties window (F4).

### User controls (like Windows Forms `UserControl`)

A user control is a reusable piece of UI with its own designer, its own code and its own properties and events,
dropped onto forms from the Toolbox. The workflow is the Windows Forms one:

1. **Create it**: *Add > New Item > Kubuno User Control*, name it (`address_editor`). You get
   `address_editor.kbcontrol` (the designed view, root `<UserControl x:Class="AddressEditor" DesignWidth=… DesignHeight=…>`;
   a user control's view is a `.kbcontrol`, with its own Solution Explorer icon, a form's a `.kbview`)
   and `address_editor.rs` nested under it (`#[derive(UserControl)]`, its `#[event_handlers]` impl, a `Load`
   handler). The module is declared for you (`mod address_editor;`, or `#[path = "../address_editor.rs"] mod
   address_editor;` for a file added next to `Cargo.toml`). The designer opens borderless, at the design size;
   **F7** / **Shift+F7** switch between code and designer.
2. **Design it** like a form: drop controls, set their properties, double-click an event in the ⚡ tab - the
   handler is a method of the user control in its `#[event_handlers]` impl, named the Windows Forms way
   (`<x:Name>_<event>`: `validate_click`), with its typed arguments.
3. **Expose its API** with attributes on its fields, the Windows Forms ones:
   ```rust
   /// The street.                           // the Properties window's description
   #[property(bindable)]                     // {Binding} allowed
   #[category("Address")]                    // the Properties window's category
   pub street: String,
   #[property(on_change = "update_usage")]   // a method run when the property is set
   #[default_value("French")]                // not bold at this value (metadata only: Default still sets the field)
   pub format: AddressFormat,                // an enum: a dropdown
   #[property] pub accent_color: Option<ColorValue>,   // the colour picker
   #[property] pub countries: Vec<String>,   // a string list: one item per line
   #[property] #[browsable(false)] pub free_text: String,   // hidden from the Properties window
   #[event] #[category("Action")]
   pub address_validated: Event<AddressValidatedEventArgs>, // `#[derive(EventArgs)]` struct; raise_address_validated(…)
   ```
   `#[user_control(view = "…", default_event = "AddressValidated")]` sets the event a double-click on the control
   generates; `#[toolbox(bitmap = "address_editor.png")]` its Toolbox icon (an image next to the `.rs` file, the
   `[ToolboxBitmap]` analogue; `#[toolbox(icon = "map-pin")]` takes a Kubuno icon name). In `Load` (`OnLoad` on the view's root),
   `self.design_mode()` is `true` in the designer: fill sample data there, as in Windows Forms.
4. **Build** (Ctrl+Shift+B). The control appears in the Toolbox tab **«&lt;project&gt; Composants»**
   (*Components* in English), with its icon - controls of a library crate the project depends on (`uclib = { path =
   "../UcLib" }`) too.
5. **Use it**: drag it onto a form. It is drawn for real (its own code runs, `Load` included, with `design_mode()`
   true), selected and moved as one control, at its design size. Its properties show in their categories with
   their editors and update the designer live; its events are in the ⚡ tab (a double-click writes
   `fn address_address_validated(&mut self, _sender: &Control, _e: &crate::address_editor::AddressValidatedEventArgs)`
   in the form's code). A click on its surface raises its `Click`; a click on one of its labels or buttons is that
   control's, as in Windows Forms.
6. **Change it**: edit the user control then build. Until then, the forms using it show an information bar
   («out of date, build the project»); the build refreshes them without reopening.

**A user control in a `.kbview`** (a project written before the two extensions, 2026-10-01) gets a warning on its
root element; the light bulb's **Renommer en .kbcontrol** renames the file and updates its code-behind's
`#[user_control(view = "…")]` (and the `x:Inherits` of views deriving from it). The reverse fix,
**Renommer en .kbview**, is offered for a form saved as a `.kbcontrol`.

A user control can be nested in another one, in a `TabControl` page, a `DockPanel`, or be a `<Repeater>`'s item
(written inside the Repeater, or `ItemTemplate="DriveCard"`): each item has its own instance, its own context
menus, its own events (the page's handler reads the item with `current_item()`).

**Design-time data.** Attributes prefixed with `d:` only apply in the designer, like XAML's: `d:Text="Jean
Dupont"`, `d:Visible="true"` (a control bound `Visible="{Binding HasError}"` shown while designing),
`d:ItemsSource="drives.design.json"` (a JSON array of objects, inline or in a file next to the view: the
Repeater's sample items). Without `d:ItemsSource`, a Repeater shows sample items guessed from the bound field
names and types (initials, counts, booleans, colours, times, dates, images). The designer opens in the language
the application starts in (the Windows UI language, when the project has its `.kbres`); the **Langue** /
**Language** box still offers «(Par défaut)».

**Visual inheritance.** *Add > New Item > Kubuno Inherited Form* / *Kubuno Inherited User Control* opens the
**Inheritance Picker**: choose the base view. The new view's root says `x:Inherits="base_dialog.kbview"` (an inherited
user control is a `.kbcontrol` too: `x:Inherits="address_editor.kbcontrol"`); the
designer shows the base's controls with a padlock - locked, unless their **Modifiers** (`Modifiers="Protected"`, set in the base's
Properties window) is `Protected` or `Public`, in which case they can be selected, moved and their properties
overridden (the derived view repeats the element with its `x:Name` and only the changed attributes). The derived
class has a `#[base]` field (`base: BaseDialog`); the base's handlers keep running on it.

**In code.** `Custom::<AddressEditor>::new().property("City", "Lyon").location(24, 80).size(360, 200)`, or
`Custom::<AddressEditor>::init(|a| a.city = "Lyon".into())`, adds a user control to a form built in code; its
events are subscribed with `editor.on::<AddressValidatedEventArgs>("OnAddressValidated").subscribe(|sender, e| …)`
(Windows Forms' `+=`).

## 7. Open Folder mode and its limitations

You do not have to generate a `.rsproj` to start working: **File > Open > Folder** on any Cargo
project or workspace root gives you rust-analyzer, syntax coloring, rustfmt, and per-`Cargo.toml`
Build/Rebuild/Clean (right-click or the Build menu) immediately, with **no generation step at
all**. This is the fastest way to open something and start reading/editing code.

What Open Folder mode does **not** give you, which is why `.rsproj`/`.sln` exists as a second,
complementary mode (§4 above):

- **No automatic "build before F5".** Open Folder's `launch.vs.json`-based launch has no
  `preLaunchTask` equivalent - build (or rebuild) explicitly before pressing F5/Ctrl+F5.
- **Select Startup Item only lists `bin`/`example` targets whose executable already exists on
  disk**, and only after you have built at least once; it also does not list test binaries at all
  (use "Kubuno: Debug Rust Test at Cursor" for those, §5 above).
- For a workspace-only `Cargo.toml` (no `[package]` of its own - the common case for a multi-crate
  app), Visual Studio does not always keep a pre-selected default startup item visible in the
  toolbar across a session; opening the "Select Startup Item" dropdown and picking your target once
  is a one-time step after that, F5/Ctrl+F5 behave normally.
- **Solution Explorer's expand-to-symbols tree** (§6) is a `.rsproj`-only feature today.
- A `.csproj`/`.vcxproj`/`.sln` file anywhere under the opened folder (e.g. a vendored reference
  tree) can make Visual Studio's own native project discovery take over the "Select Startup Item"
  dropdown; the extension excludes such directories automatically the first time it notices them
  (a `.vs\VSWorkspaceSettings.json` it writes only once, never overriding a choice you made
  yourself there).

Both modes coexist in the same folder without conflict: opening a folder that already has a
generated `.rsproj`/`.sln` in it still works correctly in Open Folder mode, and rust-analyzer's own
workspace-root detection is unaffected by which mode brought a `.rs` file into view.

## 8. The MCP bridge, for Claude

If you use Claude Code (or another MCP-speaking client) alongside Visual Studio, this extension
exposes what you see in the IDE - the active document and selection, the Error List, open
documents, the solution/folder root and any Cargo workspaces under it, the debugger's state and
call stack, and an Output pane's text - as **seven read-only MCP tools**, so you do not have to
copy/paste any of it into a chat. Nothing here can edit, save, build, or run anything; it is
strictly a read channel (see `docs/MCP.md` "Security" for the reasoning).

The bridge (`kubuno-vs-mcp.exe`) ships inside the extension and starts automatically with no
configuration when a solution/folder is open; register it with Claude Code once:

```bash
claude mcp add kubuno-vs -- "<path to the installed extension>\tools\kubuno-vs-mcp\kubuno-vs-mcp.exe"
```

or in `.mcp.json`:

```json
{
  "mcpServers": {
    "kubuno-vs": {
      "command": "<path to the installed extension>\\tools\\kubuno-vs-mcp\\kubuno-vs-mcp.exe",
      "args": [],
      "env": { "KUBUNO_VS_PID": "" }
    }
  }
}
```

Leave `KUBUNO_VS_PID` empty (or drop it) unless you have more than one Visual Studio window open
at once - with exactly one running instance the bridge finds it automatically; with several, it
lists the candidates and asks you to set that variable to disambiguate. See `docs/MCP.md` for the
full tool list and the transport/discovery mechanism.

## 9. Troubleshooting

- **First-run screens on a freshly installed/reset Visual Studio.** A brand-new (or
  hive-reset) Visual Studio shows "Sign in" / "Skip and add accounts later", then "Customize your
  environment" / "Start Visual Studio", before anything else loads. If Visual Studio looks like it
  is hanging right after install, this is almost always what is happening - click through it once.
- **"Create a new project"/"Add New Item" does not list any Rust/Kubuno template right after
  installing.** Visual Studio's template dialogs read from a cache that is only rebuilt the first
  time Visual Studio *starts* after a template-shipping VSIX is installed or updated, not at
  install time itself. Close Visual Studio and start it once more; the templates then appear.
- **A "the program can't start because ... .dll is missing" dialog** when running a Rust binary built
  with `-C prefer-dynamic` (Kubuno programs link `kubuno_ui` and `std` statically and never need a DLL
  beside them): the executable's own directory does not have its dylibs/the matching Rust `std-*.dll` next
  to it, and something outside the extension's own PATH-prepending logic (a manual launch outside VS, a
  custom launch profile with `PATH` overridden rather than extended) short-circuited it. Inside a
  `.rsproj`/Open Folder launch through F5/Ctrl+F5, the extension already prepends the profile directory,
  its `deps` folder and the Rust standard library directory to PATH for you, as `cargo run` does - if you
  still see this dialog, check whether your own "Debug" property page / `launch.vs.json` environment
  entry *replaces* `PATH` instead of extending it (setting `PATH` directly there overrides the computed
  value rather than adding to it).
- **A Kubuno program asks for `kubuno_ui-<hash>.dll`, `kubuno_ui.dll` or `std-*.dll`**
  (`0xC0000135`, or "entry point not found", `0xC0000139`): it was built from a desktop checkout older
  than 2026-10-03, when `kubuno-ui` was a shared Rust DLL. Update the checkout and rebuild the program:
  it then links everything statically (docs/DESIGNER.md section 16).
- **`error[E0463]: can't find crate for 'kubuno_ui'`** (reported in `kubuno-views`' own sources)
  when building against a desktop checkout older than 2026-10-03 with a Kubuno Desktop Application
  created before this was fixed. `kubuno-ui` was then a Rust dylib, and cargo names a dylib without a hash
  (`deps\kubuno_ui.dll`): two cargo workspaces building into the SAME target directory overwrote each
  other's `kubuno_ui.dll`. Update the checkout (`kubuno-ui` is now a hashed rlib, which cannot collide).
  Projects created from the current template build into a directory of their own anyway
  (`$(CARGO_TARGET_DIR)\rsproj\<crate>`, or `<project>\target` when `CARGO_TARGET_DIR` is not set).
- **A one-time "would you like to create a browse database" / IntelliSense database prompt**
  the first time Visual Studio's C++ tooling touches files on a network drive (relevant here
  because the C++ workload backs the native debug engine): this is standard Visual Studio behavior
  for the C++ workload, unrelated to this extension - accept or decline as you prefer; declining
  does not affect Rust debugging, which does not use that database.
- **`dotnet test`/MSTest fails to load with a `FileLoadException` (HRESULT `0x80131515`)** if you
  build or test this extension's own source from a mapped network drive - see `README.md`'s "Tests"
  section (`COMPLUS_LoadFromRemoteSources=1`). This affects developing the *extension itself*, not
  a Rust project you open inside it.
- **A build/restore on a mapped network drive intermittently fails with a "path not found" or
  "file not found" I/O error that disappears on retry** (e.g. `MSB3501`/`MSB3024`/NuGet's own
  `project.assets.json` read failing right after being written): this is SMB client-side directory-
  entry caching lagging a moment behind a just-created file/folder, not a real missing file -
  simply re-run the same Restore/Build command; it succeeds once the cache catches up. Building on
  a local (`C:`) drive avoids it entirely.
- **rust-analyzer never starts, or starts against the wrong workspace.** Check the **"Kubuno"** pane
  in View > Output first - every discovery step (which `rust-analyzer.exe` was chosen and why,
  the workspace root it was given, start/stop, and any error) is logged there. Tools > Options >
  Kubuno > Rust has an LSP trace setting (`Off`/`Messages`/`Verbose`) if you need to see the raw
  protocol traffic.
