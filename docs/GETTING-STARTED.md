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
- **Kubuno Desktop Application** - a `kubuno_ui`/`kubuno_controls`/`kubuno_views` window with a
  starter `.kbview` view (see "The view designer" below). Its generated `Cargo.toml` depends on
  those crates as **path dependencies pointing at a `desktop/windows` checkout on your own
  machine**. **Single prerequisite**: a checkout of `github.com/kubuno/desktop`, and - unless it
  is at the default `Z:\src\desktop\windows` - the `KUBUNO_DESKTOP_SRC` environment variable set
  to its `windows` folder (or to the repository root) **before** you create the project, e.g.
  `setx KUBUNO_DESKTOP_SRC D:\src\desktop\windows`, then restart Visual Studio. The path is written
  into the new project once, at creation (`Cargo.toml`'s three `kubuno-*` paths and the `.rsproj`'s
  `<KubunoDesktopSrc>`, to be changed together if you move the checkout later); a build with that
  folder missing stops with error `KUBUNO0001` saying exactly this. The project then builds and
  runs with F5 as created, with no other step: its `.rsproj` gives it its own cargo target
  directory (see "E0463" in Troubleshooting) and F5 puts `kubuno_ui.dll` and Rust's `std-*.dll` on
  the program's PATH.
- **Kubuno Module** - an Axum/Tokio backend module skeleton (`/health` + `/internal/*` guarded by
  `X-Internal-Secret`, a `sqlx::migrate!`-driven Postgres schema, `module.toml`,
  `build_kbpkg.sh`), following the platform's own module conventions.

Whatever name you type is turned into a Cargo-valid crate name automatically (lower-cased,
invalid characters folded to `-`, forced to start with a letter - `My App 2` becomes `my-app-2`);
for a Kubuno Module, a matching `module.toml` id (`my_app_2`) is derived the same way. You do not
need to sanitize the project name yourself.

*Add New Item* on a created project adds three more templates: **Kubuno View** (a new `.kbview`
plus its same-stem `.rs` code-behind), **Rust Module**, and **Rust Integration Test**.

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
member that has a `[[bin]]` target, plus a `.sln` at the workspace root listing all of them.

- **Idempotent**: re-running it never overwrites an existing `.rsproj`; an existing `.sln` is
  edited surgically (only the missing project entries are inserted - your own solution folders,
  other projects and formatting are preserved).
- A library-only member (no `[[bin]]`) is skipped by default.
- The MSBuild SDK a generated `.rsproj` needs (`Kubuno.Rust.Sdk`) ships inside the extension and
  registers itself as a local NuGet source on first package load - no manual SDK setup, no
  internet access required for that step.

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

## 5. Test Explorer

Cargo tests (`#[test]` functions) show up in Visual Studio's own **Test Explorer** automatically
for both Open Folder and `.rsproj`/solution mode - no extra setup, no attribute or naming
convention to follow beyond `#[test]` itself. Run/debug a test the normal way (right-click in Test
Explorer, or the CodeLens-style run/debug affordance if your Visual Studio shows one for other
languages). For a single test you want to step through, the **Tools > "Kubuno: Debug Rust Test at
Cursor"** command is faster: put the caret in or just before the `#[test]` function in the editor
and run it - it builds only that test binary and launches it under the native debugger with
`--exact --nocapture --test-threads=1`, without discovering or building every other test first.

## 6. The view designer (`.kbview`)

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
  or type a name into an unbound row to create a handler with that specific name.
- **F7** ("View Code") switches to the raw XML in a normal code window; **Shift+F7** ("View
  Designer") switches back - also available from Solution Explorer's right-click menu and from the
  code-behind `.rs` file. The Design/XML/Split tabs inside the designer itself stay available too,
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
- **A "the program can't start because ... .dll is missing" dialog** when running a Kubuno Desktop
  Application (F5/Ctrl+F5), or any Rust binary built with `-C prefer-dynamic`: the executable's own
  directory does not have `kubuno_ui.dll`/the matching Rust `std-*.dll` next to it, and something
  outside the extension's own PATH-prepending logic (a manual launch outside VS, a custom launch
  profile with `PATH` overridden rather than extended) short-circuited it. Inside a `.rsproj`/Open
  Folder launch through F5/Ctrl+F5, the extension already prepends the profile directory, its
  `deps` folder and the Rust standard library directory to PATH for you - if you still see this
  dialog, check whether your own "Debug" property page / `launch.vs.json` environment entry
  *replaces* `PATH` instead of extending it (setting `PATH` directly there overrides the computed
  value rather than adding to it).
- **`error[E0463]: can't find crate for 'kubuno_ui'`** (reported in `kubuno-views`' own sources)
  when building a Kubuno Desktop Application created before this was fixed. `kubuno-ui` is a Rust
  dylib, and cargo names a dylib without a hash (`deps\kubuno_ui.dll`): when two cargo workspaces
  build into the SAME target directory - typically a machine-wide `CARGO_TARGET_DIR` that the
  `desktop` workspace also builds into - each overwrites the other's `kubuno_ui.dll`, and the next
  compile of a crate that uses it rejects the foreign DLL. Projects created from the current
  template are not affected: their `.rsproj` builds into `$(CARGO_TARGET_DIR)\rsproj\<crate>` (or
  `<project>\target` when `CARGO_TARGET_DIR` is not set). For an older project, add the two
  `<CargoTargetDir>` lines of the current template to its `.rsproj`. Running plain `cargo build`
  in such a project from a terminal where `CARGO_TARGET_DIR` points at a shared directory has the
  same problem - set `CARGO_TARGET_DIR` to a directory of its own for that shell.
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
